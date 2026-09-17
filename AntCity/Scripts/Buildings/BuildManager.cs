using Godot;
using System.Collections.Generic;

public partial class BuildManager : Node2D
{
    private const int MinFootprintDimension = 2;

    // Burrows stay ant-scaled: a chamber wider or taller than this is a cavern, not a nest room.
    private const int MaxFootprintDimension = 4;

    private static readonly PackedScene RoomScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/Room.tscn");

    private static readonly Color ValidPreviewColor = new Color(0.4f, 1f, 0.4f, 0.35f);
    private static readonly Color InvalidPreviewColor = new Color(1f, 0.3f, 0.3f, 0.35f);

    [Export]
    public GridManager GridManager { get; set; }

    [Export]
    public ColonyManager ColonyManager { get; set; }

    private readonly List<Room> rooms = new();
    private readonly Dictionary<Vector2I, Room> roomsByCell = new();
    private readonly HashSet<Vector2I> claimedDigCells = new();

    private BuildingType? pendingType;
    private bool isDragging;
    private Vector2 dragStart;
    private Vector2 dragCurrent;

    public bool IsPlacing => pendingType.HasValue;

    public override void _Ready()
    {
        // CellOpened, not CellDug: a room cell can become passable without any ant finishing a dig
        // on it, and the room still needs to know.
        GridManager.CellOpened += OnCellDug;
        GridManager.TileObstructed += OnTileObstructed;
    }

    public void BeginPlacement(BuildingType type)
    {
        pendingType = type;
        isDragging = false;
        QueueRedraw();
    }

    public void CancelPlacement()
    {
        pendingType = null;
        isDragging = false;
        QueueRedraw();
    }

    // Nearest not-yet-claimed cell that some room still needs dug, if any.
    // Tunnels that have caved in and need clearing. Kept separate from room work because a blocked
    // passage can be the only route in or out, so it takes priority over starting the next chamber.
    private readonly HashSet<Vector2I> obstructions = new();

    private void OnTileObstructed(Vector2I cell)
    {
        if (GridManager.CanDig(cell))
        {
            obstructions.Add(cell);
        }
    }

    public bool TryClaimDigJob(Vector2 fromPosition, out Vector2I cell)
    {
        if (TryClaimNearestObstruction(fromPosition, out cell))
        {
            return true;
        }

        cell = default;
        bool found = false;
        float bestDistance = float.MaxValue;

        foreach (Room room in rooms)
        {
            if (room.State != Room.RoomState.Excavating)
            {
                continue;
            }

            foreach (Vector2I candidate in room.PendingDigCells)
            {
                if (claimedDigCells.Contains(candidate))
                {
                    continue;
                }

                float distance = GridManager.CellToWorld(candidate).DistanceSquaredTo(fromPosition);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    cell = candidate;
                    found = true;
                }
            }
        }

        if (found)
        {
            claimedDigCells.Add(cell);
        }

        return found;
    }

    private bool TryClaimNearestObstruction(Vector2 fromPosition, out Vector2I cell)
    {
        cell = default;

        if (obstructions.Count == 0)
        {
            return false;
        }

        bool found = false;
        float bestDistance = float.MaxValue;
        List<Vector2I> stale = null;

        foreach (Vector2I candidate in obstructions)
        {
            // Something else may have cleared it, or it may have turned into terrain nobody can dig.
            if (!GridManager.CanDig(candidate))
            {
                (stale ??= new List<Vector2I>()).Add(candidate);
                continue;
            }

            if (claimedDigCells.Contains(candidate))
            {
                continue;
            }

            float distance = GridManager.CellToWorld(candidate).DistanceSquaredTo(fromPosition);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                cell = candidate;
                found = true;
            }
        }

        if (stale != null)
        {
            foreach (Vector2I gone in stale)
            {
                obstructions.Remove(gone);
            }
        }

        if (found)
        {
            claimedDigCells.Add(cell);
        }

        return found;
    }


    // Nearest fully-dug room that still needs an ant to furnish it, if any.
    public bool TryClaimFurnishJob(Vector2 fromPosition, out Room claimedRoom)
    {
        claimedRoom = null;
        float bestDistance = float.MaxValue;

        foreach (Room room in rooms)
        {
            if (room.State != Room.RoomState.Furnishing || room.FurnishClaimed)
            {
                continue;
            }

            float distance = GridManager.CellToWorld(room.StandCell).DistanceSquaredTo(fromPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                claimedRoom = room;
            }
        }

        if (claimedRoom != null)
        {
            claimedRoom.FurnishClaimed = true;
        }

        return claimedRoom != null;
    }

    // Called when an ant working a claimed dig cell gets redirected before finishing it.
    public void ReleaseClaim(Vector2I cell)
    {
        claimedDigCells.Remove(cell);
        obstructions.Remove(cell);
    }

    public void ReportFurnishDone(Room room)
    {
        room.Activate();

        BuildingDef def = BuildingDefs.All[room.Type];
        int cellCount = room.CellCount;
        float effect = def.EffectPerCell * cellCount;

        switch (room.Type)
        {
            case BuildingType.NestingChamber:
                int capacityGain = Mathf.RoundToInt(effect);
                ColonyManager.IncreaseCapacity(capacityGain);
                ColonyManager.RaiseAlert($"{def.Name} built! +{capacityGain} population capacity.");
                break;
            case BuildingType.Granary:
                int foodCapacityGain = Mathf.RoundToInt(effect);
                ColonyManager.IncreaseFoodCapacity(foodCapacityGain);
                ColonyManager.RaiseAlert($"{def.Name} built! +{foodCapacityGain} food capacity.");
                break;
            case BuildingType.Nursery:
                ColonyManager.AddNursery(cellCount);
                ColonyManager.RaiseAlert($"{def.Name} built! Hatching sped up.");
                break;
            case BuildingType.FungusFarm:
                int yield = Mathf.RoundToInt(effect);
                ColonyManager.AddFungusFarm(cellCount);
                ColonyManager.RaiseAlert($"{def.Name} built! +{yield} food an hour, grown at home.");
                break;
            case BuildingType.RoyalChamber:
                ColonyManager.AddRoyalChamber(cellCount);
                ColonyManager.RaiseAlert($"{def.Name} built! The Queen lays {(ColonyManager.LaySpeedMultiplier - 1f) * 100f:0}% faster.");
                break;
        }
    }

    public List<RoomSave> CaptureRooms()
    {
        var saves = new List<RoomSave>();

        foreach (Room room in rooms)
        {
            var save = new RoomSave
            {
                Type = (int)room.Type,
                FootprintX = room.Footprint.Position.X,
                FootprintY = room.Footprint.Position.Y,
                FootprintWidth = room.Footprint.Size.X,
                FootprintHeight = room.Footprint.Size.Y,
                State = (int)room.State,
                FurnishClaimed = room.FurnishClaimed,
            };

            foreach (Vector2I cell in room.PendingDigCells)
            {
                save.PendingDigCells.AddCell(cell);
            }

            saves.Add(save);
        }

        return saves;
    }

    public void RestoreRooms(List<RoomSave> saves)
    {
        CancelPlacement();

        foreach (Room room in rooms)
        {
            room.GetParent()?.RemoveChild(room);
            room.QueueFree();
        }

        rooms.Clear();
        roomsByCell.Clear();
        claimedDigCells.Clear();

        foreach (RoomSave save in saves)
        {
            Room room = RoomScene.Instantiate<Room>();
            room.Type = (BuildingType)save.Type;
            room.Footprint = new Rect2I(save.FootprintX, save.FootprintY, save.FootprintWidth, save.FootprintHeight);
            room.RestoreState(
                GridManager.CellSize,
                new HashSet<Vector2I>(save.PendingDigCells.ReadCells()),
                (Room.RoomState)save.State,
                save.FurnishClaimed
            );

            GetParent().AddChild(room);
            rooms.Add(room);

            // Recomputed rather than saved. A restored room owns whatever of its outline is open or
            // diggable now, which is the same answer as when it was placed unless something blasted
            // the rock out since - and if it did, the room may as well have the space.
            var owned = new List<Vector2I>();

            ForEachCell(room.Footprint, cell =>
            {
                if (!IsUsableRoomCell(cell))
                {
                    return;
                }

                roomsByCell[cell] = room;
                owned.Add(cell);
            });

            room.SetOwnedCells(owned);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsPlacing)
        {
            // Unhandled, so an ant under the cursor has already taken the click - selecting a
            // worker standing in a chamber should select the worker, which is the thing the player
            // was aiming at.
            if (@event is InputEventMouseButton click &&
                click.Pressed &&
                click.ButtonIndex == MouseButton.Left)
            {
                SelectRoom(RoomAt(GridManager.WorldToCell(GetGlobalMousePosition())));
            }

            return;
        }

        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    isDragging = true;
                    dragStart = GetGlobalMousePosition();
                    dragCurrent = dragStart;
                }
                else if (isDragging)
                {
                    isDragging = false;
                    TryCreateRoom(ComputeFootprint(dragStart, dragCurrent));
                    QueueRedraw();
                }

                GetViewport().SetInputAsHandled();
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
            {
                CancelPlacement();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventMouseMotion && isDragging)
        {
            dragCurrent = GetGlobalMousePosition();
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (!isDragging)
        {
            return;
        }

        Rect2I footprint = ComputeFootprint(dragStart, dragCurrent);
        Vector2 topLeft = new Vector2(footprint.Position.X, footprint.Position.Y) * GridManager.CellSize;
        Vector2 size = new Vector2(footprint.Size.X, footprint.Size.Y) * GridManager.CellSize;

        DrawRect(new Rect2(topLeft, size), IsFootprintValid(footprint, out _) ? ValidPreviewColor : InvalidPreviewColor, filled: true);
    }

    private void OnCellDug(Vector2I cell)
    {
        claimedDigCells.Remove(cell);
        obstructions.Remove(cell);

        if (!roomsByCell.TryGetValue(cell, out Room room) || room.State != Room.RoomState.Excavating)
        {
            return;
        }

        room.NotifyCellDug(cell);

        if (room.IsFullyDug)
        {
            room.BeginFurnishing();
        }
    }

    // Public so tests and probes can place a room without synthesising mouse input. This is the same
    // path the drag-to-place UI takes, so exercising it exercises the real thing.
    public void TryCreateRoom(Rect2I footprint, BuildingType? forced = null)
    {
        if (!IsFootprintValid(footprint, out string reason))
        {
            // Said out loud. A placement that just quietly does nothing is indistinguishable from
            // the build system being broken, which is exactly how it read.
            ColonyManager.RaiseAlert(reason);
            return;
        }

        BuildingType type = forced ?? pendingType.Value;
        BuildingDef def = BuildingDefs.All[type];

        // Only the cells the room will actually occupy. Rock inside the footprint is neither paid
        // for nor counted towards what the room does, because the room never gets to use it.
        var roomCells = new List<Vector2I>();
        ForEachCell(footprint, cell =>
        {
            if (IsUsableRoomCell(cell))
            {
                roomCells.Add(cell);
            }
        });

        if (!ColonyManager.RemoveFood(def.FoodCostPerCell * roomCells.Count))
        {
            ColonyManager.RaiseAlert($"Not enough food - a {def.Name} that size costs {def.FoodCostPerCell * roomCells.Count}.");
            return;
        }

        var pendingCells = new HashSet<Vector2I>();

        foreach (Vector2I cell in roomCells)
        {
            if (!GridManager.IsTunnel(cell))
            {
                pendingCells.Add(cell);
            }
        }

        Room room = RoomScene.Instantiate<Room>();
        room.Type = type;
        room.Footprint = footprint;
        room.SetOwnedCells(roomCells);
        room.Initialize(GridManager.CellSize, pendingCells);
        GetParent().AddChild(room);
        rooms.Add(room);

        foreach (Vector2I cell in roomCells)
        {
            roomsByCell[cell] = room;
        }

        // Placement is finished, so stop placing.
        //
        // pendingType used to survive a successful placement, which meant the next click anywhere
        // on the map started another room of the same kind. The player had no way to tell they were
        // still armed, and the only way out was a right-click they had no reason to try.
        if (forced == null)
        {
            CancelPlacement();
        }
    }

    // ---- selecting, inspecting and pulling down --------------------------------------------------
    //
    // A room used to be permanent the instant it was placed. It could not be selected, inspected or
    // removed, and its food was not refundable - so a misplaced chamber was a scar on the colony for
    // the rest of the game, and the only response to a mistake was to live with it.

    // Fraction of the food cost handed back when a room is pulled down. Not all of it: the ants
    // really did dig that ground, and a free undo makes placement a decision with no weight.
    private const float DemolishRefundFraction = 0.5f;

    public Room Selected { get; private set; }

    [Signal]
    public delegate void RoomSelectedEventHandler(Room room);

    public Room RoomAt(Vector2I cell)
    {
        return roomsByCell.TryGetValue(cell, out Room room) ? room : null;
    }

    public void SelectRoom(Room room)
    {
        if (Selected == room)
        {
            return;
        }

        if (Selected != null)
        {
            Selected.IsSelected = false;
        }

        Selected = room;

        if (Selected != null)
        {
            Selected.IsSelected = true;
        }

        EmitSignal(SignalName.RoomSelected, room);
    }

    public void ClearSelection()
    {
        SelectRoom(null);
    }

    // Pulls a room down, gives back what it is fair to give back, and takes its effect away again.
    public void Demolish(Room room)
    {
        if (room == null || !rooms.Contains(room))
        {
            return;
        }

        BuildingDef def = BuildingDefs.All[room.Type];
        int cells = room.CellCount;

        if (room.State == Room.RoomState.Active)
        {
            UndoEffect(room.Type, cells);
        }

        int refund = Mathf.FloorToInt(def.FoodCostPerCell * cells * DemolishRefundFraction);

        if (refund > 0)
        {
            ColonyManager.AddFood(refund);
        }

        // Anything still owed to the job board goes with it, or workers keep turning up to dig a
        // chamber that no longer exists.
        foreach (Vector2I cell in room.OwnedCells)
        {
            claimedDigCells.Remove(cell);
            roomsByCell.Remove(cell);
        }

        if (Selected == room)
        {
            SelectRoom(null);
        }

        rooms.Remove(room);
        room.GetParent()?.RemoveChild(room);
        room.QueueFree();

        ColonyManager.RaiseAlert($"{def.Name} pulled down. {refund} food recovered.");
    }

    // The cavity stays dug. Only what the room *did* is taken back.
    private void UndoEffect(BuildingType type, int cells)
    {
        BuildingDef def = BuildingDefs.All[type];
        int effect = Mathf.RoundToInt(def.EffectPerCell * cells);

        switch (type)
        {
            case BuildingType.NestingChamber:
                ColonyManager.DecreaseCapacity(effect);
                break;
            case BuildingType.Granary:
                ColonyManager.DecreaseFoodCapacity(effect);
                break;
            case BuildingType.Nursery:
                ColonyManager.AddNursery(-cells);
                break;
            case BuildingType.FungusFarm:
                ColonyManager.AddFungusFarm(-cells);
                break;
            case BuildingType.RoyalChamber:
                ColonyManager.AddRoyalChamber(-cells);
                break;
        }
    }

    // Whether a room can go here, and if not, why not.
    //
    // Rock inside the footprint used to fail the whole thing. Underground rock is common enough that
    // a five-by-two area has about a nine-in-ten chance of containing some, so once the colony began
    // on bare ground - digging into virgin terrain rather than a pre-cleared chamber - almost every
    // placement was rejected, silently, with no way to tell why. Rooms now build around stone: those
    // cells simply are not part of the room.
    private bool IsFootprintValid(Rect2I footprint, out string reason)
    {
        reason = null;

        if (footprint.Size.X < MinFootprintDimension || footprint.Size.Y < MinFootprintDimension)
        {
            reason = $"Too small - a room needs to be at least {MinFootprintDimension} by {MinFootprintDimension}.";
            return false;
        }

        if (footprint.Size.X > MaxFootprintDimension || footprint.Size.Y > MaxFootprintDimension)
        {
            reason = $"Too big - a room can be at most {MaxFootprintDimension} by {MaxFootprintDimension}.";
            return false;
        }

        bool overlaps = false;
        bool outOfBounds = false;
        int usable = 0;

        ForEachCell(footprint, cell =>
        {
            if (roomsByCell.ContainsKey(cell))
            {
                overlaps = true;
                return;
            }

            if (!GridManager.IsInBounds(cell))
            {
                outOfBounds = true;
                return;
            }

            if (IsUsableRoomCell(cell))
            {
                usable++;
            }
        });

        if (overlaps)
        {
            reason = "That overlaps a room you have already placed.";
            return false;
        }

        if (outOfBounds)
        {
            reason = "That reaches outside the world.";
            return false;
        }

        if (usable < MinUsableCells)
        {
            reason = "Too much solid rock there - find somewhere softer.";
            return false;
        }

        return true;
    }

    // Open ground, or ground an ant could open. Rock is neither, so it stays where it is.
    private bool IsUsableRoomCell(Vector2I cell)
    {
        return GridManager.IsTunnel(cell) || GridManager.CanDig(cell);
    }

    // Enough of the footprint has to be diggable for the room to be worth anything at all.
    private const int MinUsableCells = MinFootprintDimension * MinFootprintDimension;

    private Rect2I ComputeFootprint(Vector2 worldA, Vector2 worldB)
    {
        Vector2I cellA = GridManager.WorldToCell(worldA);
        Vector2I cellB = GridManager.WorldToCell(worldB);

        Vector2I min = new Vector2I(Mathf.Min(cellA.X, cellB.X), Mathf.Min(cellA.Y, cellB.Y));
        Vector2I max = new Vector2I(Mathf.Max(cellA.X, cellB.X), Mathf.Max(cellA.Y, cellB.Y));

        return new Rect2I(min, max - min + Vector2I.One);
    }

    private static void ForEachCell(Rect2I footprint, System.Action<Vector2I> action)
    {
        for (int x = footprint.Position.X; x < footprint.Position.X + footprint.Size.X; x++)
        {
            for (int y = footprint.Position.Y; y < footprint.Position.Y + footprint.Size.Y; y++)
            {
                action(new Vector2I(x, y));
            }
        }
    }
}
