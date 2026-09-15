using Godot;
using System.Collections.Generic;

// Real, conserved terrain grains.
//
// Undisturbed ground stays cheap 16px tiles - only material that has actually been dug out exists as
// individual grains, so the simulated count stays in the tens rather than the millions. Grains snap to
// an 8x8 lattice inside each cell, which makes collision an integer hashset lookup instead of physics,
// and once a cell packs full again it re-freezes into a solid tile and its grains are dropped. That
// keeps a big spoil pile free to own, and makes it real terrain the colony can dig back out later.
public partial class ParticleField : Node2D
{
    public const int SlotsPerCellAxis = 8;
    public const int SlotsPerCell = SlotsPerCellAxis * SlotsPerCellAxis;

    private const double TickSeconds = 1.0 / 30.0;
    private const int MaxStepsPerFrame = 4;
    private const int MaxCellsSearchedForRelease = 6;

    private static readonly Dictionary<GridManager.TileType, Color> MaterialColors = new()
    {
        { GridManager.TileType.Dirt, new Color("6b4a32") },
        { GridManager.TileType.FoodDeposit, new Color("d9a441") },
        { GridManager.TileType.SeedCache, new Color("f0c868") },
        { GridManager.TileType.MushroomPatch, new Color("e8dcc0") },
    };

    private static readonly Color DefaultMaterialColor = new Color("6b4a32");

    private struct Grain
    {
        public Vector2I Slot;
        public GridManager.TileType Material;
    }

    [Export]
    public GridManager Grid { get; set; }

    // Master switch for the whole soil simulation - excavated grains, carrying, hauling, spoil heaps.
    // Off keeps the colony quick to play: digging just opens the cell. On restores conserved dirt.
    [Export]
    public bool Enabled { get; set; } = false;

    private readonly Dictionary<Vector2I, GridManager.TileType> settled = new();
    private readonly Dictionary<Vector2I, int> settledPerCell = new();
    private readonly List<Grain> falling = new();

    public int SettledGrainCount => settled.Count;
    public int FallingGrainCount => falling.Count;

    // Whether one lattice slot currently holds a grain at rest.
    public bool IsSettledSlot(Vector2I slot) => settled.ContainsKey(slot);

    // Every slot held by a grain, settled or in flight. Keeps two falling grains from claiming the
    // same slot and quietly annihilating one another when they land.
    private readonly HashSet<Vector2I> occupied = new();

    private double tickAccumulator;
    private bool dirty;

    public override void _Ready()
    {
        Grid.CellDug += OnCellDug;
    }

    public override void _Process(double delta)
    {
        if (!Enabled)
        {
            return;
        }

        tickAccumulator += delta;

        for (int step = 0; step < MaxStepsPerFrame && tickAccumulator >= TickSeconds; step++)
        {
            tickAccumulator -= TickSeconds;
            Advance();
        }

        // Settled grains are static, so the canvas only gets rebuilt when something actually moved.
        if (dirty)
        {
            dirty = false;
            QueueRedraw();
        }
    }

    // Scrapes loose material out into an open cell. Returns how many grains found room; any beyond
    // that had nowhere to fall, and the caller keeps them.
    public int Emit(Vector2I cell, int count, GridManager.TileType material)
    {
        int placed = 0;

        while (placed < count && TryPlaceGrain(cell, material))
        {
            placed++;
        }

        return placed;
    }

    // An ant scooping loose grains up off the floor. Returns how many it managed to take.
    public int Collect(Vector2I cell, int max, List<GridManager.TileType> into)
    {
        int taken = 0;

        for (int i = falling.Count - 1; i >= 0 && taken < max; i--)
        {
            if (SlotToCell(falling[i].Slot) != cell)
            {
                continue;
            }

            into.Add(falling[i].Material);
            occupied.Remove(falling[i].Slot);
            falling.RemoveAt(i);
            taken++;
        }

        if (settledPerCell.ContainsKey(cell))
        {
            Vector2I origin = cell * SlotsPerCellAxis;

            // Top down, so a scooped pile keeps a sensible surface instead of being hollowed out.
            for (int y = 0; y < SlotsPerCellAxis && taken < max; y++)
            {
                for (int x = 0; x < SlotsPerCellAxis && taken < max; x++)
                {
                    Vector2I slot = origin + new Vector2I(x, y);

                    if (!settled.TryGetValue(slot, out GridManager.TileType material))
                    {
                        continue;
                    }

                    into.Add(material);
                    RemoveSettled(slot, cell);
                    taken++;
                }
            }
        }

        if (taken > 0)
        {
            dirty = true;
        }

        return taken;
    }

    // Tips a carried load out onto the pile, working upward as the lower cells fill in.
    public void Release(Vector2I cell, List<GridManager.TileType> materials)
    {
        foreach (GridManager.TileType material in materials)
        {
            for (int up = 0; up < MaxCellsSearchedForRelease; up++)
            {
                Vector2I target = cell - new Vector2I(0, up);

                if (!Grid.IsInBounds(target))
                {
                    break;
                }

                if (TryPlaceGrain(target, material))
                {
                    break;
                }
            }
        }

        materials.Clear();
    }

    public override void _Draw()
    {
        float slotSize = Grid.CellSize / (float)SlotsPerCellAxis;
        Vector2 size = new Vector2(slotSize, slotSize);

        foreach (KeyValuePair<Vector2I, GridManager.TileType> entry in settled)
        {
            DrawRect(new Rect2(SlotToWorld(entry.Key, slotSize), size), ColorFor(entry.Value), filled: true);
        }

        foreach (Grain grain in falling)
        {
            DrawRect(new Rect2(SlotToWorld(grain.Slot, slotSize), size), ColorFor(grain.Material), filled: true);
        }
    }

    // One tick of the sand simulation. Driven by _Process in play, and directly by the terrain tests.
    public void Advance()
    {
        if (falling.Count == 0)
        {
            return;
        }

        // Lowest grains move first, so a falling column collapses cleanly instead of each grain
        // landing on one that was about to get out of the way.
        falling.Sort((a, b) => a.Slot.Y.CompareTo(b.Slot.Y));

        for (int i = falling.Count - 1; i >= 0; i--)
        {
            Grain grain = falling[i];

            if (TryFall(ref grain))
            {
                falling[i] = grain;
                continue;
            }

            falling.RemoveAt(i);
            Settle(grain);
        }

        dirty = true;
    }

    public ParticleSave CaptureState()
    {
        var save = new ParticleSave();

        foreach (var entry in settled)
        {
            save.Settled.AddCell(entry.Key, (int)entry.Value);
        }

        foreach (Grain grain in falling)
        {
            save.Falling.AddCell(grain.Slot, (int)grain.Material);
        }

        return save;
    }

    public void RestoreState(ParticleSave save)
    {
        settled.Clear();
        settledPerCell.Clear();
        falling.Clear();
        occupied.Clear();

        foreach ((Vector2I slot, int material) in save.Settled.ReadCellValues())
        {
            settled[slot] = (GridManager.TileType)material;
            occupied.Add(slot);

            Vector2I cell = SlotToCell(slot);
            settledPerCell[cell] = settledPerCell.TryGetValue(cell, out int existing) ? existing + 1 : 1;
        }

        foreach ((Vector2I slot, int material) in save.Falling.ReadCellValues())
        {
            falling.Add(new Grain { Slot = slot, Material = (GridManager.TileType)material });
            occupied.Add(slot);
        }

        QueueRedraw();
    }

    private bool TryFall(ref Grain grain)
    {
        Vector2I below = grain.Slot + new Vector2I(0, 1);

        if (IsSlotFree(below))
        {
            Move(ref grain, below);
            return true;
        }

        // Slide off a slope, alternating which way each grain prefers so piles spread into a mound
        // instead of stacking into a spike.
        int bias = ((grain.Slot.X + grain.Slot.Y) & 1) == 0 ? 1 : -1;

        Vector2I nearSide = grain.Slot + new Vector2I(bias, 1);

        if (IsSlotFree(nearSide))
        {
            Move(ref grain, nearSide);
            return true;
        }

        Vector2I farSide = grain.Slot + new Vector2I(-bias, 1);

        if (IsSlotFree(farSide))
        {
            Move(ref grain, farSide);
            return true;
        }

        return false;
    }

    private void Settle(Grain grain)
    {
        settled[grain.Slot] = grain.Material;

        Vector2I cell = SlotToCell(grain.Slot);
        int count = settledPerCell.TryGetValue(cell, out int existing) ? existing + 1 : 1;
        settledPerCell[cell] = count;

        // The dump is where ants stand to unload, so it never packs shut under them.
        if (count >= SlotsPerCell)
        {
            PackCell(cell);
        }
    }

    // A cell filled to the brim stops being loose grains and becomes ordinary diggable ground again.
    private void PackCell(Vector2I cell)
    {
        Vector2I origin = cell * SlotsPerCellAxis;

        for (int x = 0; x < SlotsPerCellAxis; x++)
        {
            for (int y = 0; y < SlotsPerCellAxis; y++)
            {
                Vector2I packedSlot = origin + new Vector2I(x, y);
                settled.Remove(packedSlot);
                occupied.Remove(packedSlot);
            }
        }

        settledPerCell.Remove(cell);
        Grid.PackCellToDirt(cell);
    }

    private void OnCellDug(Vector2I cell)
    {
        // Opening a cell pulls whatever was resting against it back into motion.
        for (int dx = -1; dx <= 1; dx++)
        {
            WakeCell(cell + new Vector2I(dx, -1));
        }

        WakeCell(cell + new Vector2I(-1, 0));
        WakeCell(cell + new Vector2I(1, 0));
    }

    private void WakeCell(Vector2I cell)
    {
        if (!settledPerCell.ContainsKey(cell))
        {
            return;
        }

        Vector2I origin = cell * SlotsPerCellAxis;

        for (int x = 0; x < SlotsPerCellAxis; x++)
        {
            for (int y = 0; y < SlotsPerCellAxis; y++)
            {
                Vector2I slot = origin + new Vector2I(x, y);

                if (!settled.TryGetValue(slot, out GridManager.TileType material))
                {
                    continue;
                }

                settled.Remove(slot);
                falling.Add(new Grain { Slot = slot, Material = material });
                occupied.Add(slot);
            }
        }

        settledPerCell.Remove(cell);
        dirty = true;
    }

    private bool TryPlaceGrain(Vector2I cell, GridManager.TileType material)
    {
        Vector2I origin = cell * SlotsPerCellAxis;

        for (int y = 0; y < SlotsPerCellAxis; y++)
        {
            for (int x = 0; x < SlotsPerCellAxis; x++)
            {
                Vector2I slot = origin + new Vector2I(x, y);

                if (!IsSlotFree(slot))
                {
                    continue;
                }

                falling.Add(new Grain { Slot = slot, Material = material });
                occupied.Add(slot);
                dirty = true;

                return true;
            }
        }

        return false;
    }

    private void Move(ref Grain grain, Vector2I destination)
    {
        occupied.Remove(grain.Slot);
        occupied.Add(destination);
        grain.Slot = destination;
    }

    private bool IsSlotFree(Vector2I slot)
    {
        Vector2I cell = SlotToCell(slot);

        if (!Grid.IsInBounds(cell) || !Grid.IsTunnel(cell))
        {
            return false;
        }

        return !occupied.Contains(slot);
    }

    private void RemoveSettled(Vector2I slot, Vector2I cell)
    {
        settled.Remove(slot);
        occupied.Remove(slot);

        int remaining = settledPerCell[cell] - 1;

        if (remaining <= 0)
        {
            settledPerCell.Remove(cell);
        }
        else
        {
            settledPerCell[cell] = remaining;
        }
    }

    private static Vector2I SlotToCell(Vector2I slot)
    {
        return new Vector2I(
            Mathf.FloorToInt(slot.X / (float)SlotsPerCellAxis),
            Mathf.FloorToInt(slot.Y / (float)SlotsPerCellAxis)
        );
    }

    private static Vector2 SlotToWorld(Vector2I slot, float slotSize)
    {
        return new Vector2(slot.X * slotSize, slot.Y * slotSize);
    }

    public static Color ColorFor(GridManager.TileType material)
    {
        return MaterialColors.TryGetValue(material, out Color color) ? color : DefaultMaterialColor;
    }
}
