using Godot;
using System.Collections.Generic;

public partial class GridManager : Node2D
{
    public enum TileType
    {
        Dirt,
        Tunnel,
        Rock,
        FoodDeposit,
        Grass,
        Water,
        Tree,
        SeedCache,
        MushroomPatch,
        BerryBush,
        Air
    }

    private const int ChunkSize = 16;

    [ExportGroup("Grid")]
    [Export]
    public int CellSize { get; set; } = 16;

    [Export]
    public TileMapLayer Ground { get; set; }

    [Export]
    public ColonyManager ColonyManager { get; set; }

    [ExportGroup("World Generation")]
    // 0 picks a random seed every run. Any other value always reproduces the same world.
    [Export]
    public int Seed { get; set; } = 0;

    [Export]
    public int SurfaceHeight { get; set; } = 12;

    // How many rows of actual topsoil sit at the surface. Everything above it is open sky, which is
    // what gives spoil mounds somewhere to grow and trees somewhere to grow into.
    [Export]
    public int GrassDepth { get; set; } = 1;

    // How many cells below the surface the starting nest is carved.
    [Export]
    public int NestDepth { get; set; } = 6;

    // Where the colony starts: on the surface, standing on the turf, with nothing dug yet. A colony
    // begins as a queen who has landed and has to make her own way in, so where the first tunnel
    // goes is a decision rather than something the world hands over.
    //
    // This is the topmost turf row, not the air above it: grass counts as walkable, so an ant stands
    // on the turf with soil beneath her rather than hovering over it.
    //
    // One definition on purpose. It was written out twice - once for a fresh world and once for a
    // restored one - and the two drifted the moment the colony moved up here, which quietly put
    // every loaded save back underground.
    private Vector2I SurfaceNestCell => new Vector2I(0, SurfaceHeight - GrassDepth);

    // How many chunks around the nest are generated immediately, so there's ground to see at the start.
    [Export]
    public int InitialRadiusChunks { get; set; } = 4;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float RockNoiseFrequency { get; set; } = 0.15f;

    [Export(PropertyHint.Range, "-1,1,0.01")]
    public float RockThreshold { get; set; } = 0.35f;

    [Export]
    public int RockFreeDepth { get; set; } = 2;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float FoodNoiseFrequency { get; set; } = 0.3f;

    [Export(PropertyHint.Range, "-1,1,0.01")]
    public float FoodThreshold { get; set; } = 0.62f;

    [Export]
    public int FoodPerDeposit { get; set; } = 15;

    [Export]
    public int FoodPerTree { get; set; } = 3;

    // Rare, big payoff - roughly 1 in 60 underground tiles once past the rock-free layer.
    [Export(PropertyHint.Range, "0,1,0.001")]
    public float SeedCacheChance { get; set; } = 0.016f;

    [Export]
    public int FoodPerSeedCache { get; set; } = 40;

    // Common, small payoff - the frequent little pickups.
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MushroomPatchChance { get; set; } = 0.06f;

    [Export]
    public int FoodPerMushroomPatch { get; set; } = 6;

    // Surface alternative to trees.
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float BerryBushChance { get; set; } = 0.05f;

    [Export]
    public int FoodPerBerryBush { get; set; } = 20;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float WaterNoiseFrequency { get; set; } = 0.1f;

    [Export(PropertyHint.Range, "-1,1,0.01")]
    public float WaterThreshold { get; set; } = 0.55f;

    // Trees are switched off for now. They block the one-tile-thick surface walkway outright, and an
    // ant can stand on a treetop, which made spoil haulers pile their loads up in the canopy. Set
    // this above zero to bring them back once they are proper multi-tile plants.
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float TreeChance { get; set; } = 0f;

    // These are the coordinates from your tileset.
    private static readonly Dictionary<TileType, Vector2I> TileAtlasCoords = new()
    {
        { TileType.Dirt, new Vector2I(0, 0) },
        { TileType.Tunnel, new Vector2I(1, 0) },
        { TileType.Rock, new Vector2I(2, 0) },
        { TileType.FoodDeposit, new Vector2I(3, 0) },
        { TileType.Grass, new Vector2I(0, 1) },
        { TileType.Water, new Vector2I(1, 1) },
        { TileType.Tree, new Vector2I(2, 1) },
        { TileType.SeedCache, new Vector2I(1, 2) },
        { TileType.MushroomPatch, new Vector2I(2, 2) },
        { TileType.BerryBush, new Vector2I(3, 2) },
    };

    private readonly Dictionary<Vector2I, TileType> grid = new();
    private readonly HashSet<Vector2I> generatedChunks = new();

    private readonly HashSet<Vector2I> modifiedCells = new();

    private bool isGenerating;

    public int ActiveSeed { get; private set; }

    private readonly Dictionary<Vector2I, int> foodRemaining = new();

    // Food sources a worker is already on her way to, so foragers spread out instead of stacking up.
    private readonly HashSet<Vector2I> claimedForageCells = new();

    // Solid cells are made of GrainsPerCell small pieces that ants chip out one at a time,
    // so a wall visibly crumbles instead of flipping to open tunnel in one step.
    public const int GrainsPerCell = 4;

    private readonly Dictionary<Vector2I, int> grainsRemoved = new();


    private FastNoiseLite rockNoise;
    private FastNoiseLite foodNoise;
    private FastNoiseLite waterNoise;
    private int treeSalt;
    private int seedCacheSalt;
    private int mushroomPatchSalt;
    private int berryBushSalt;

    // World cell the starting nest is centered on. Useful for e.g. pointing the camera at the colony.
    public Vector2I NestCenterCell { get; private set; }

    public override void _Ready()
    {
        InitializeNoise();

        NestCenterCell = SurfaceNestCell;

        // So the colony can pick who starves without having to know the grid exists.
        if (ColonyManager != null)
        {
            ColonyManager.NestPosition = CellToWorld(NestCenterCell);
        }

        // Pre-generate enough terrain around the nest to fill the initial view; everything further
        // out is generated on demand as ants path or dig toward it, so the map keeps expanding.
        Vector2I nestChunk = CellToChunk(NestCenterCell);
        for (int cx = -InitialRadiusChunks; cx <= InitialRadiusChunks; cx++)
        {
            for (int cy = -InitialRadiusChunks; cy <= InitialRadiusChunks; cy++)
            {
                EnsureChunkGenerated(nestChunk + new Vector2I(cx, cy));
            }
        }

        CreateStartingNest();

        GD.Print("Grid created!");
    }

    public Vector2I WorldToCell(Vector2 worldPosition)
    {
        return new Vector2I(
            Mathf.FloorToInt(worldPosition.X / CellSize),
            Mathf.FloorToInt(worldPosition.Y / CellSize)
        );
    }

    public Vector2 CellToWorld(Vector2I cell)
    {
        return new Vector2(
            cell.X * CellSize + CellSize / 2f,
            cell.Y * CellSize + CellSize / 2f
        );
    }

    // The world has no floor or side walls; the only edge is the surface at y = 0.
    public bool IsInBounds(Vector2I cell)
    {
        return cell.Y >= 0;
    }

    // True for cells an ant is allowed to dig through (dirt and food deposits; rock is a hard obstacle).
    public bool CanDig(Vector2I cell)
    {
        return IsInBounds(cell) && IsDiggable(GetTile(cell));
    }

    // True for an already-dug cell an ant can be sent to walk to without digging.
    public bool IsTunnel(Vector2I cell)
    {
        return IsInBounds(cell) && IsWalkable(GetTile(cell));
    }

    // Somewhere a load may be tipped.
    //
    // Spoil belongs on the surface, and this is the invariant that makes it so. Every load goes
    // through it, so soil cannot end up in a corridor however the hauling code is rearranged - which
    // is the failure that had this whole feature switched off, and it deserves to be impossible
    // rather than merely avoided.
    //
    // Open sky, and not a tile somebody dug: a heap tipped into a dug-out sky tile would be an ant
    // filling in the hole she had just climbed out of.
    public bool IsSpoilTile(Vector2I tile)
    {
        return tile.Y >= 0
            && tile.Y < SurfaceHeight - GrassDepth
            && GetTile(tile) != TileType.Tunnel
            && !IsEntranceApron(tile);
    }

    // The doorstep. Kept clear of spoil, because a hill grown over the nest mouth is a colony that
    // has buried its own way in.
    public bool IsEntranceApron(Vector2I tile)
    {
        return Mathf.Abs(tile.X - NestCenterCell.X) <= EntranceClearTiles;
    }

    // The doorstep proper: the apron at or above the turf line, which is the ground a forager has
    // to cross to get in or out. Below that is the shaft, and a tile going solid down there is
    // ordinary cave-in business rather than the colony being shut out of its own nest.
    public bool IsDoorstep(Vector2I tile)
    {
        return IsEntranceApron(tile) && tile.Y <= SurfaceHeight - GrassDepth;
    }

    // How wide the clear apron around the entrance is, in tiles.
    private const int EntranceClearTiles = 3;

    // Fired when terrain changes at runtime, so overlays know to redraw.
    [Signal]
    public delegate void TerrainChangedEventHandler();

    // Fired whenever a cell actually transitions to Tunnel, regardless of what caused the dig.
    [Signal]
    public delegate void CellDugEventHandler(Vector2I cell);

    // A cell that has become walkable, however it happened.
    //
    // Distinct from CellDug, which means specifically "an ant dug this" and drives the material grid.
    // Job bookkeeping needs the broader question, because a tile can open without anyone finishing a
    // dig on it: the material simulation derives a tile passable once half its cells are gone, which
    // chipping reaches a grain before the last one. Rooms waiting on those cells were never told they
    // had been excavated and sat unfinished forever.
    [Signal]
    public delegate void CellOpenedEventHandler(Vector2I cell);

    // Fired for each chip short of breaking through, so the material simulation can erode the tile
    // gradually instead of it staying whole until the final blow.
    [Signal]
    public delegate void TileChippedEventHandler(Vector2I cell, int removed, int total);

    // Fired when a tunnel the colony dug gets filled in, so the job board can send someone to
    // clear it rather than the colony quietly walling itself in.
    [Signal]
    public delegate void TileObstructedEventHandler(Vector2I cell);

    public void Dig(Vector2I cell)
    {
        if (!IsInBounds(cell))
        {
            return;
        }

        TileType previous = GetTile(cell);

        if (!IsDiggable(previous))
        {
            return;
        }

        grainsRemoved.Remove(cell);

        // Digging into a spoil heap leaves sky, not tunnel.
        //
        // This was Tunnel unconditionally, which is wrong above the surface and self-perpetuating:
        // an ant buried by a tipped load digs herself out, that sky tile is now marked Tunnel, and
        // the next spoil to land in it makes SetTileFromSimulation see `previous == Tunnel` and
        // report a blocked passage. That queues a dig job, which opens it again, which lets more
        // spoil in. The whole loop comes from one tile being labelled as a corridor because
        // somebody dug it.
        //
        // Matches what the simulation already does for its own writes - see PassableTileFor.
        SetTile(cell, cell.Y < SurfaceHeight - GrassDepth ? TileType.Air : TileType.Tunnel);

        // Tunnelling into a food source salvages it rather than throwing it away.
        //
        // Food tiles are diggable, so a worker routing a corridor could drive straight through a
        // seed cache and destroy forty food on her way past - a forager wrecking the very thing she
        // was sent to collect. Whatever the storehouse cannot take is genuinely spilled, which is
        // worth saying out loud, because that is a real loss the player can prevent by building.
        if (IsFoodTileType(previous))
        {
            int salvaged = GetFoodAmount(cell);
            foodRemaining.Remove(cell);

            if (salvaged > 0 && ColonyManager != null)
            {
                int stored = ColonyManager.AddFood(salvaged);

                if (stored < salvaged)
                {
                    ColonyManager.RaiseAlert($"Dug through a food source; {salvaged - stored} spilled with nowhere to store it.");
                }
            }
        }

        EmitSignal("CellDug", cell);
        EmitSignal(SignalName.CellOpened, cell);
        EmitSignal(SignalName.TerrainChanged);
    }

    // Chips a single grain out of a solid cell. Returns true once there's nothing left to dig here -
    // either the cell gave up its last grain and became open tunnel, or it was never diggable.

    // Lets the material simulation write terrain back: sand that fills a corridor makes it solid
    // again, and material scoured away opens it.
    //
    // Deliberately never fires CellDug - that signal means "an ant dug this" and drives job bookkeeping.
    // Callers are expected to have checked the tile is one the simulation owns; food, grass and the
    // rest are left alone so filling a tunnel can never quietly delete a deposit.
    public void SetTileFromSimulation(Vector2I cell, TileType type)
    {
        if (!IsInBounds(cell))
        {
            return;
        }

        TileType previous = GetTile(cell);

        if (previous == type)
        {
            return;
        }

        grainsRemoved.Remove(cell);
        SetTile(cell, type);

        // A tunnel the colony dug that has just been filled in is a blockage, not scenery. Sand
        // sliding down the entrance ramp could otherwise seal the only way in or out with nothing
        // in the game able to respond to it.
        //
        // The doorstep counts whatever it was before. Spoil slumping off the hill onto the ground
        // outside the nest mouth was never a tunnel, so the test above would ignore it - and a
        // colony that has walled itself out of its own front door starves without anything
        // noticing. Real ants keep their entrance clear; now so do these.
        if (!IsWalkable(type) && (previous == TileType.Tunnel || IsDoorstep(cell)))
        {
            EmitSignal(SignalName.TileObstructed, cell);
        }

        if (!IsWalkable(previous) && IsWalkable(type))
        {
            EmitSignal(SignalName.CellOpened, cell);
        }

        EmitSignal(SignalName.TerrainChanged);
    }

    public bool DigGrain(Vector2I cell)
    {
        if (!CanDig(cell))
        {
            return true;
        }

        int removed = GetGrainsRemoved(cell) + 1;

        if (removed >= GrainsPerCell)
        {
            Dig(cell);
            return true;
        }

        grainsRemoved[cell] = removed;
        EmitSignal(SignalName.TileChipped, cell, removed, GrainsPerCell);

        return false;
    }

    public int GetGrainsRemoved(Vector2I cell)
    {
        return grainsRemoved.TryGetValue(cell, out int removed) ? removed : 0;
    }



    // ---- the dead ------------------------------------------------------------------------------
    //
    // Bodies are tracked in their own map rather than as a food tile type. Food tiles are diggable
    // and not walkable, so turning the cell an ant died in into one would wall off the corridor she
    // died in - and a corpse is meant to be something you walk up to, not something you excavate.
    //
    // Everything else about them goes through the ordinary forage pipeline: IsFoodSource,
    // GetFoodAmount, Harvest and TryFindForageTarget all consult this, so a forager treats a body
    // exactly as she treats a seed cache and none of her code had to learn what a corpse is.
    private readonly Dictionary<Vector2I, int> carrionRemaining = new();

    // Whether the colony eats its dead or carries them out to the refuse heap. The player's call -
    // real ants do both, depending on how hungry they are.
    public bool EatTheDead { get; set; }

    public void AddCarrion(Vector2I cell, int food)
    {
        carrionRemaining.TryGetValue(cell, out int already);
        carrionRemaining[cell] = already + food;

        EmitSignal(SignalName.TerrainChanged);
    }

    public void RemoveCarrion(Vector2I cell)
    {
        if (carrionRemaining.Remove(cell))
        {
            claimedForageCells.Remove(cell);
            EmitSignal(SignalName.TerrainChanged);
        }
    }

    public int CarrionAt(Vector2I cell)
    {
        return carrionRemaining.TryGetValue(cell, out int food) ? food : 0;
    }

    public bool IsCarrion(Vector2I cell) => CarrionAt(cell) > 0;

    // The nearest body nobody has gone for yet, whatever the policy is. Used by the workers who
    // carry the dead out, which is what happens when the colony is not eating them.
    public bool TryFindCarrion(Vector2 fromPosition, float maxDistance, out Vector2I cell)
    {
        cell = default;

        float bestDistance = maxDistance * maxDistance;
        bool found = false;

        foreach (System.Collections.Generic.KeyValuePair<Vector2I, int> entry in carrionRemaining)
        {
            if (claimedForageCells.Contains(entry.Key))
            {
                continue;
            }

            float distance = CellToWorld(entry.Key).DistanceSquaredTo(fromPosition);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                cell = entry.Key;
                found = true;
            }
        }

        return found;
    }
    public bool IsFoodSource(Vector2I cell)
    {
        if (!IsInBounds(cell))
        {
            return false;
        }

        // A body the colony is willing to eat counts as a source. When the policy is to carry the
        // dead out instead, it is refuse rather than food and foragers ignore it.
        if (EatTheDead && IsCarrion(cell))
        {
            return true;
        }

        TileType type = GetTile(cell);
        return IsFoodTileType(type) && GetFoodAmount(cell) > 0;
    }

    public int GetFoodAmount(Vector2I cell)
    {
        if (EatTheDead && IsCarrion(cell))
        {
            return CarrionAt(cell);
        }

        return foodRemaining.TryGetValue(cell, out int amount) ? amount : 0;
    }

    // Snapshot of every cell currently tracked as a food source, for the minimap to plot.
    public List<Vector2I> GetFoodSourceCells()
    {
        return new List<Vector2I>(foodRemaining.Keys);
    }

    // Fills a caller-owned list instead of handing back a fresh one. The minimap asks for this
    // often enough that allocating a list of every food cell in the world each time showed up in
    // the frame budget.
    public void CollectFoodSourceCells(List<Vector2I> into)
    {
        foreach (Vector2I cell in foodRemaining.Keys)
        {
            into.Add(cell);
        }
    }

    // The nearest food source no other worker has already gone after.
    //
    // Reachability is deliberately not checked here. A forager tunnels toward buried food on her way
    // to it, which is how a colony ends up mining out a deposit it found - so the only limit is how
    // far afield she is willing to look.
    public bool TryFindForageTarget(Vector2 fromPosition, float maxDistance, out Vector2I cell)
    {
        cell = default;

        float bestDistance = maxDistance * maxDistance;
        bool found = false;

        foreach (KeyValuePair<Vector2I, int> entry in foodRemaining)
        {
            if (claimedForageCells.Contains(entry.Key) || !IsFoodSource(entry.Key))
            {
                continue;
            }

            float distance = CellToWorld(entry.Key).DistanceSquaredTo(fromPosition);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                cell = entry.Key;
                found = true;
            }
        }

        // And the dead, when the colony is eating them. Same loop, same claim rules - a body is just
        // another thing worth walking to.
        if (EatTheDead)
        {
            foreach (KeyValuePair<Vector2I, int> entry in carrionRemaining)
            {
                if (claimedForageCells.Contains(entry.Key))
                {
                    continue;
                }

                float distance = CellToWorld(entry.Key).DistanceSquaredTo(fromPosition);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    cell = entry.Key;
                    found = true;
                }
            }
        }

        return found;
    }

    // One worker per source: a deposit holds a load or two, so a second ant on it would just walk
    // out there and find nothing left.

    // Forage claims belong to the ants that made them, and those ants are about to be freed.
    //
    // RestoreState clears the grid, the food and the dig progress but never this, so every cell a
    // forager had claimed when the save was loaded stayed claimed for the rest of the session -
    // and TryFindForageTarget skips claimed cells, so those deposits became permanently invisible
    // to the whole colony.
    public void ResetTransientState()
    {
        claimedForageCells.Clear();
    }
    public void ClaimForageCell(Vector2I cell)
    {
        claimedForageCells.Add(cell);
    }

    public void ReleaseForageClaim(Vector2I cell)
    {
        claimedForageCells.Remove(cell);
    }

    // Where a hauler should take her load.
    //
    // Spoil goes up and out, the way a real colony works it - never to one fixed spot. A fixed dump
    // cell is unreachable from most dig faces once ants cannot climb, and a hauler who cannot route
    // there just tips her load in the tunnel she is standing in. So instead she searches the burrow
    // she can actually walk and picks the highest point in it, preferring one a few cells clear of
    // the entrance so the heap never grows across her own way out.
    public Vector2I FindSpoilDropOff(Vector2I from)
    {
        const int MaxVisited = 4000;

        Vector2I best = from;
        int bestScore = SpoilDropScore(from);

        var visited = new HashSet<Vector2I> { from };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(from);

        while (frontier.Count > 0 && visited.Count < MaxVisited)
        {
            Vector2I current = frontier.Dequeue();

            foreach (Vector2I direction in MoveDirections)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsSafelyStandable(next))
                {
                    continue;
                }

                visited.Add(next);
                frontier.Enqueue(next);

                int score = SpoilDropScore(next);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = next;
                }
            }
        }

        return best;
    }

    // Lower is better. Getting out of the burrow is what matters most, so depth dominates and every
    // cell at or above the surface ties; the tie is broken by how far the cell sits from the band
    // the hill is meant to grow in.
    //
    // It used to *maximise* distance from the nest column, capped at six tiles, which spread every
    // load as far out as a hauler could be bothered to walk. That is the right instinct for keeping
    // a heap off the doorstep and the wrong one for building a hill: spoil scattered over twelve
    // tiles is a mess, not a fortress. A band does both - every load joins one mound, and the mound
    // starts far enough out that it never grows across the way in.
    private int SpoilDropScore(Vector2I cell)
    {
        int depth = Mathf.Max(0, cell.Y - (SurfaceHeight - GrassDepth));
        int lateral = Mathf.Abs(cell.X - NestCenterCell.X);

        int offBand = lateral < MoundInnerTiles ? MoundInnerTiles - lateral
            : lateral > MoundOuterTiles ? lateral - MoundOuterTiles
            : 0;

        return depth * 100 + offBand * 10;
    }

    // Where the hill is meant to stand: an annulus around the entrance column. Inside it is the
    // apron the ants keep clear; outside it, a load would be walked further than it is worth.
    private const int MoundInnerTiles = 5;
    private const int MoundOuterTiles = 9;

    // How far either side of the entrance the hill can plausibly reach, for anything that needs to
    // work over the whole of it. Wider than the drop band, because a tipped cone slumps outward.
    public int MoundSpanTiles => MoundOuterTiles + 4;

    // Whether a load tipped from here would actually land anywhere.
    //
    // The drop-off search returns the best cell it can *reach*, and an ant sealed in a half-dug
    // chamber can reach nothing above ground - so it hands back somewhere eight tiles down. She
    // then walks there, the sky-only guard quite correctly refuses to tip soil into a corridor, and
    // she keeps the load; and because her load is full, she takes that same walk to nothing again
    // on the very next dig tick. Measured, that was thirty-five thousand grains of shuttling in
    // four minutes.
    //
    // Better to know before setting off. She keeps carrying and keeps working, and the trip becomes
    // possible again the moment somebody digs the corridor that reaches daylight.
    public bool IsViableSpoilDropOff(Vector2I cell)
    {
        if (cell.Y > SurfaceHeight - GrassDepth)
        {
            return false;
        }

        int awayFromNest = cell.X < NestCenterCell.X ? -1 : 1;

        return !IsEntranceApron(cell + new Vector2I(awayFromNest, 0))
            || !IsEntranceApron(cell + new Vector2I(awayFromNest * 2, 0));
    }

    // The nearest patch of standable surface, for anything that wants "somewhere out in the open"
    // without caring where spoil goes.
    //
    // Split out from FindSpoilDropOff because four tests were borrowing that as a general route
    // destination, two of them asserting how long the route was - so tuning where the colony tips
    // its earth was quietly breaking pathfinding tests that have nothing to do with spoil.
    // minTilesAway exists because the nest itself sits on the surface row, so "nearest standable
    // surface" from the nest is the nest - and a caller after a route to walk gets one cell.
    public Vector2I FindNearestSurfaceStanding(Vector2I from, int minTilesAway = 0)
    {
        const int MaxVisited = 4000;

        int surfaceRow = SurfaceHeight - GrassDepth;

        var visited = new HashSet<Vector2I> { from };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(from);

        while (frontier.Count > 0 && visited.Count < MaxVisited)
        {
            Vector2I current = frontier.Dequeue();

            if (current.Y <= surfaceRow &&
                Mathf.Abs(current.X - from.X) >= minTilesAway &&
                IsSafelyStandable(current))
            {
                return current;
            }

            foreach (Vector2I direction in MoveDirections)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsSafelyStandable(next))
                {
                    continue;
                }

                visited.Add(next);
                frontier.Enqueue(next);
            }
        }

        return from;
    }


    public TileType GetTileAt(Vector2I cell)
    {
        return GetTile(cell);
    }

    private static bool IsFoodTileType(TileType type)
    {
        return type == TileType.FoodDeposit
            || type == TileType.Tree
            || type == TileType.SeedCache
            || type == TileType.MushroomPatch
            || type == TileType.BerryBush;
    }

    // Underground food sources revert to open tunnel once emptied; surface ones revert to grass.
    private static bool IsUndergroundFoodTileType(TileType type)
    {
        return type == TileType.FoodDeposit || type == TileType.SeedCache || type == TileType.MushroomPatch;
    }

    public int Harvest(Vector2I cell, int amount)
    {
        if (!IsFoodSource(cell))
        {
            return 0;
        }

        int available = GetFoodAmount(cell);
        int harvested = Mathf.Min(amount, available);
        int remaining = available - harvested;

        // A body is eaten rather than mined: no tile changes hands, the corpse node simply notices
        // it has been picked clean and removes itself.
        if (EatTheDead && IsCarrion(cell))
        {
            if (remaining <= 0)
            {
                RemoveCarrion(cell);
            }
            else
            {
                carrionRemaining[cell] = remaining;
            }

            return harvested;
        }

        if (remaining <= 0)
        {
            grainsRemoved.Remove(cell);
            foodRemaining.Remove(cell);
            TileType depletedTo = IsUndergroundFoodTileType(GetTile(cell)) ? TileType.Tunnel : TileType.Grass;
            SetTile(cell, depletedTo);
            EmitSignal(SignalName.TerrainChanged);
        }
        else
        {
            foodRemaining[cell] = remaining;
        }

        return harvested;
    }

    private void InitializeNoise()
    {
        // Reseed Godot's global RNG from the current time, otherwise GD.Randi() below can repeat
        // the same sequence across separate runs and the "random" world ends up identical every time.
        GD.Randomize();

        // 0 means "no seed was set" -> roll a random one so every run gets a different world.
        InitializeNoise(Seed != 0 ? Seed : (int)GD.Randi());
    }

    private void InitializeNoise(int actualSeed)
    {
        ActiveSeed = actualSeed;
        GD.Print($"World seed: {actualSeed}");

        rockNoise = new FastNoiseLite { Seed = actualSeed, Frequency = RockNoiseFrequency };
        foodNoise = new FastNoiseLite { Seed = actualSeed + 1, Frequency = FoodNoiseFrequency };
        waterNoise = new FastNoiseLite { Seed = actualSeed + 2, Frequency = WaterNoiseFrequency };
        treeSalt = actualSeed + 3;
        seedCacheSalt = actualSeed + 4;
        mushroomPatchSalt = actualSeed + 5;
        berryBushSalt = actualSeed + 6;
    }

    // Generates and caches the chunk containing `cell` if it hasn't been generated yet.
    private void EnsureGenerated(Vector2I cell)
    {
        if (cell.Y < 0)
        {
            return;
        }

        EnsureChunkGenerated(CellToChunk(cell));
    }

    private void EnsureChunkGenerated(Vector2I chunkCoord)
    {
        // Nothing above the surface strip is part of the world.
        if (chunkCoord.Y < 0 || !generatedChunks.Add(chunkCoord))
        {
            return;
        }

        int startX = chunkCoord.X * ChunkSize;
        int startY = chunkCoord.Y * ChunkSize;

        for (int x = startX; x < startX + ChunkSize; x++)
        {
            for (int y = Mathf.Max(startY, 0); y < startY + ChunkSize; y++)
            {
                GenerateCell(new Vector2I(x, y));
            }
        }
    }

    private static Vector2I CellToChunk(Vector2I cell)
    {
        return new Vector2I(
            Mathf.FloorToInt((float)cell.X / ChunkSize),
            Mathf.FloorToInt((float)cell.Y / ChunkSize)
        );
    }

    private void GenerateCell(Vector2I cell)
    {
        int x = cell.X;
        int y = cell.Y;
        TileType type;

        if (y < SurfaceHeight - GrassDepth)
        {
            type = TileType.Air;
        }
        else if (y < SurfaceHeight)
        {
            // The topsoil band: grass dotted with water pools, trees and bushes.
            if (waterNoise.GetNoise2D(x, y) > WaterThreshold)
            {
                type = TileType.Water;
            }
            else if (RollChance(x, y, treeSalt) < TreeChance)
            {
                type = TileType.Tree;
            }
            else if (RollChance(x, y, berryBushSalt) < BerryBushChance)
            {
                type = TileType.BerryBush;
            }
            else
            {
                type = TileType.Grass;
            }
        }
        else
        {
            // Underground: mostly dirt, with rock pockets and food sources of varying rarity woven through it.
            int depthBelowSurface = y - SurfaceHeight;
            if (depthBelowSurface >= RockFreeDepth && rockNoise.GetNoise2D(x, y) > RockThreshold)
            {
                type = TileType.Rock;
            }
            else if (foodNoise.GetNoise2D(x, y) > FoodThreshold)
            {
                type = TileType.FoodDeposit;
            }
            else if (RollChance(x, y, seedCacheSalt) < SeedCacheChance)
            {
                type = TileType.SeedCache;
            }
            else if (RollChance(x, y, mushroomPatchSalt) < MushroomPatchChance)
            {
                type = TileType.MushroomPatch;
            }
            else
            {
                type = TileType.Dirt;
            }
        }

        switch (type)
        {
            case TileType.FoodDeposit:
                foodRemaining[cell] = FoodPerDeposit;
                break;
            case TileType.Tree:
                foodRemaining[cell] = FoodPerTree;
                break;
            case TileType.SeedCache:
                foodRemaining[cell] = FoodPerSeedCache;
                break;
            case TileType.MushroomPatch:
                foodRemaining[cell] = FoodPerMushroomPatch;
                break;
            case TileType.BerryBush:
                foodRemaining[cell] = FoodPerBerryBush;
                break;
        }

        isGenerating = true;
        SetTile(cell, type);
        isGenerating = false;
    }

    private static float RollChance(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h % 1_000_000u) / 1_000_000f;
        }
    }

    private TileType GetTile(Vector2I cell)
    {
        if (cell.Y < 0)
        {
            // Above the surface strip isn't part of the world; treat it as solid so nothing paths through it.
            return TileType.Rock;
        }

        EnsureGenerated(cell);
        return grid[cell];
    }

    private void SetTile(Vector2I cell, TileType type)
    {
        grid[cell] = type;

        if (!isGenerating)
        {
            modifiedCells.Add(cell);
        }

        if (type == TileType.Air)
        {
            // Open sky has no artwork - erasing leaves the background showing through, which also
            // means it costs no slot in an atlas that is already full.
            Ground.EraseCell(cell);
            return;
        }

        Ground.SetCell(cell, 0, TileAtlasCoords[type]);
    }

    private bool IsDiggable(TileType type)
    {
        return type == TileType.Dirt || IsUndergroundFoodTileType(type);
    }

    private static bool IsWalkable(TileType type)
    {
        return type == TileType.Tunnel
            || type == TileType.Grass
            || type == TileType.Air;
    }

    // Nothing is dug. The colony begins on open ground and digs its own way in.
    //
    // All this does is guarantee somewhere to stand: generation can leave the spawn tile covered by
    // a tree or a berry bush, and ants that cannot climb would have no way off it.
    private void CreateStartingNest()
    {
        for (int x = NestCenterCell.X - 2; x <= NestCenterCell.X + 2; x++)
        {
            Vector2I above = new Vector2I(x, NestCenterCell.Y);

            if (!IsWalkable(GetTile(above)))
            {
                ForceDig(above);
            }
        }

        GD.Print("Colony landed on the surface.");
    }

    // Used by world generation to guarantee the nest and its entrance shaft are always clear.
    private void ForceDig(Vector2I cell)
    {
        if (!IsInBounds(cell))
        {
            return;
        }

        SetTile(cell, TileType.Tunnel);
    }
}
