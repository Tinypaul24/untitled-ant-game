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
    // Ground another room already owns - a different problem from rock, and worth saying so.
    private static readonly Color TakenPreviewColor = new Color(1f, 0.55f, 0.2f, 0.4f);
    // The HardenedDirt the ants will cement this ring into, so the preview shows the finished wall.
    private static readonly Color WallPreviewColor = new Color(0.35f, 0.29f, 0.24f, 0.55f);
    // A hole in the wall. One is a doorway; a ring full of them is an open cavern.
    private static readonly Color BreachPreviewColor = new Color(0.25f, 0.6f, 0.9f, 0.3f);

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
    private Vector2 hoverPosition;

    // Only re-announced when it changes, or the label would be rewritten sixty times a second.
    private string lastReason;
    private bool lastValid;

    // Why the placement under the cursor would be refused, for the UI to say in words. Geometry is
    // drawn here; sentences belong where the theme and the font live.
    [Signal]
    public delegate void PreviewChangedEventHandler(string reason, bool valid);

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
        hoverPosition = GetGlobalMousePosition();

        AnnouncePreview();
        QueueRedraw();
    }

    public void CancelPlacement()
    {
        pendingType = null;
        isDragging = false;

        // Said on the way out as well as on the way in. _Draw returns early when nothing is being
        // placed, so without this the label would keep whatever it last said forever.
        AnnouncePreview();
        QueueRedraw();
    }

    // Drives the preview to a chosen footprint without synthesising mouse input, so a screenshot
    // harness can photograph it. Exercises the same drag path the UI takes.
    public void PreviewForTest(Rect2I footprint)
    {
        isDragging = true;
        dragStart = GridManager.CellToWorld(footprint.Position);
        dragCurrent = GridManager.CellToWorld(footprint.Position + footprint.Size - Vector2I.One);

        AnnouncePreview();
        QueueRedraw();
    }

    private void AnnouncePreview()
    {
        string reason = null;
        bool valid = false;

        if (IsPlacing)
        {
            valid = IsFootprintValid(PreviewFootprint(), out reason);
        }

        lastReason = reason;
        lastValid = valid;

        EmitSignal(SignalName.PreviewChanged, reason ?? "", valid);
    }

    // Nearest not-yet-claimed cell that some room still needs dug, if any.
    // Tunnels that have caved in and need clearing. Kept separate from room work because a blocked
    // passage can be the only route in or out, so it takes priority over starting the next chamber.
    private readonly HashSet<Vector2I> obstructions = new();

    private void OnTileObstructed(Vector2I cell)
    {
        // Spoil settling on a heap is a heap, not a blocked passage. Above the surface there is no
        // corridor to lose, and queueing a dig job for every tile of a growing mound would have the
        // colony endlessly excavating its own spoil tip.
        if (cell.Y < GridManager.SurfaceHeight - GridManager.GrassDepth)
        {
            return;
        }

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
        else if (@event is InputEventMouseMotion motion)
        {
            // Tracked whether or not a drag is in progress. Nothing used to be drawn until the
            // player pressed the button, so the rules were only ever discoverable by breaking them.
            hoverPosition = GetGlobalMousePosition();

            if (isDragging)
            {
                dragCurrent = hoverPosition;
            }

            QueueRedraw();
        }
    }

    // What the player is about to place, before they have committed to it.
    private Rect2I PreviewFootprint()
    {
        return isDragging
            ? ComputeFootprint(dragStart, dragCurrent)
            : ComputeFootprint(hoverPosition, hoverPosition);
    }

    // The preview used to be one flat rectangle over the bounding box, tinted by a single boolean,
    // with the rejection reason thrown away (`out _`) and nothing drawn at all until a drag began.
    // So a player who broke a rule learned only that something had gone wrong, after the fact, and
    // could not see which cells were the problem.
    //
    // Now it shows the room and its wall: every cell of the footprint tinted by what it actually
    // is, and the one-tile ring drawn in the colour the finished wall will be, so the preview is a
    // picture of the chamber rather than a box.
    public override void _Draw()
    {
        if (!IsPlacing)
        {
            return;
        }

        Rect2I footprint = PreviewFootprint();
        bool valid = IsFootprintValid(footprint, out string reason);

        DrawRing(footprint);
        DrawInterior(footprint);

        // Said out loud while there is still time to move the cursor, rather than as a toast after
        // the placement has already failed.
        if (reason != lastReason || valid != lastValid)
        {
            lastReason = reason;
            lastValid = valid;

            EmitSignal(SignalName.PreviewChanged, reason ?? "", valid);
        }
    }

    private void DrawInterior(Rect2I footprint)
    {
        var size = new Vector2(GridManager.CellSize, GridManager.CellSize);

        ForEachCell(footprint, cell =>
        {
            Color tint = roomsByCell.ContainsKey(cell) ? TakenPreviewColor
                : IsUsableRoomCell(cell) ? ValidPreviewColor
                : InvalidPreviewColor;

            DrawRect(new Rect2(new Vector2(cell.X, cell.Y) * GridManager.CellSize, size), tint, filled: true);
        });
    }

    // The wall, drawn in the colour it will actually become once the ants have cemented it, so the
    // preview predicts the chamber rather than merely outlining a selection.
    private void DrawRing(Rect2I footprint)
    {
        var size = new Vector2(GridManager.CellSize, GridManager.CellSize);

        ForEachRingCell(footprint, cell =>
        {
            Color tint = roomsByCell.ContainsKey(cell) ? TakenPreviewColor
                : GridManager.IsTunnel(cell) ? BreachPreviewColor
                : WallPreviewColor;

            DrawRect(new Rect2(new Vector2(cell.X, cell.Y) * GridManager.CellSize, size), tint, filled: true);
        });
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
    //
    // Reports whether the room went up. It used to return void, so a caller had no way to tell a
    // refusal from a success except by hunting the scene tree for the room afterwards.
    public bool TryCreateRoom(Rect2I footprint, BuildingType? forced = null)
    {
        if (!IsFootprintValid(footprint, out string reason))
        {
            // Said out loud. A placement that just quietly does nothing is indistinguishable from
            // the build system being broken, which is exactly how it read.
            ColonyManager.RaiseAlert(reason);
            return false;
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
            return false;
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

        return true;
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

        // Every chamber keeps a wall between itself and the next one.
        //
        // This replaces an overlap test that asked whether any footprint cell was in roomsByCell -
        // which holds only the cells a room *owns*, so a new room could already be laid straight
        // over an existing room's rock. Growing the footprint by one and intersecting footprints
        // has no such hole, and it is what "at least one tile of earth between two rooms" means.
        Room touching = NearestRoomWithin(footprint);

        if (touching != null)
        {
            reason = $"Too close to the {BuildingDefs.All[touching.Type].Name} next door - chambers need a wall between them.";
            return false;
        }

        bool outOfBounds = false;
        int usable = 0;

        ForEachCell(footprint, cell =>
        {
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

        // Not a patch of somebody else's cavern with a label on it.
        //
        // The threshold is measured, not guessed. Watching the colony play itself, a chamber dug
        // off a corridor comes out with two or three of its sixteen ring tiles open - the doorway
        // the diggers came in through, and the odd cell their descending staircase clipped. An
        // already-excavated cavern is sixteen of sixteen. Half the ring sits in the middle of a very
        // wide gap, and it says what it means: more than half the wall is missing, so there is no
        // wall.
        int openRing = OpenRingCells(footprint);
        int ringCells = RingCellCount(footprint);

        if (openRing * 2 > ringCells)
        {
            reason = $"That is open cavern, not a chamber - {openRing} of {ringCells} edge tiles are already dug.";
            return false;
        }

        // A chamber needs somewhere to stand.
        //
        // Nothing checked this, and the failure was silent rather than loud: open sky counts as a
        // usable cell, so a room could be placed in mid-air, and CommandBuild's
        // `FindTunnelPath(...) ?? new List<Vector2I> { startCell }` fallback then had the furnisher
        // "build" it from wherever she happened to be standing.
        if (FloorCells(footprint) == 0)
        {
            reason = "Nothing to stand on - the ground under all of that is open.";
            return false;
        }

        return true;
    }

    // An existing room within one tile of this footprint, if any. Deliberately compares footprints
    // rather than owned cells, so rock a room happens not to own still counts as its territory.
    private Room NearestRoomWithin(Rect2I footprint)
    {
        Rect2I grown = footprint.Grow(1);

        foreach (Room room in rooms)
        {
            if (grown.Intersects(room.Footprint))
            {
                return room;
            }
        }

        return null;
    }

    // How many cells of this chamber will have a floor once it is dug.

    // How much of a chamber's wall is already missing.
    //
    // A ring with one or two holes is a chamber with doorways. A ring that is mostly holes is not a
    // chamber at all - it is a patch of an existing cavern with a label on it, which is the
    // open-plan building this rule exists to prevent.
    private int OpenRingCells(Rect2I footprint)
    {
        int open = 0;

        ForEachRingCell(footprint, cell =>
        {
            if (GridManager.IsTunnel(cell))
            {
                open++;
            }
        });

        return open;
    }

    private static int RingCellCount(Rect2I footprint)
    {
        Rect2I ring = footprint.Grow(1);

        return ring.Size.X * ring.Size.Y - footprint.Size.X * footprint.Size.Y;
    }
    //
    // Only the bottom row can have one: a cell is standable when the cell below is *not* walkable,
    // and every cell below a higher row is itself part of the room and will be dug out.
    //
    // This started as "the whole row must be solid", which measurement killed immediately - the
    // colony stopped building rooms entirely. Ants dig their way to a chamber along routes that
    // descend past it and come back up, so the floor row of a room usually *is* breached in a cell
    // or two by the time it is placed. Demanding an unbroken floor bans the normal case.
    //
    // One cell is the honest requirement, because it is what the rule is actually for: a chamber in
    // open sky has nowhere to stand and nobody can furnish it, and that is the failure worth
    // refusing. A chamber with a gap in its floor is just a chamber with a gap in its floor - ants
    // fall through it and TryFall walks them back.
    private int FloorCells(Rect2I footprint)
    {
        int below = footprint.Position.Y + footprint.Size.Y;
        int floor = 0;

        for (int x = footprint.Position.X; x < footprint.Position.X + footprint.Size.X; x++)
        {
            if (!GridManager.IsTunnel(new Vector2I(x, below)))
            {
                floor++;
            }
        }

        return floor;
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

    // The one-tile band of earth around a footprint - the chamber's wall.
    //
    // Corners included. They are part of the shell even though nothing can walk diagonally through
    // a corner, and leaving them out would let two chambers meet at a point with no wall between.
    private static void ForEachRingCell(Rect2I footprint, System.Action<Vector2I> action)
    {
        Rect2I ring = footprint.Grow(1);

        for (int x = ring.Position.X; x < ring.Position.X + ring.Size.X; x++)
        {
            for (int y = ring.Position.Y; y < ring.Position.Y + ring.Size.Y; y++)
            {
                bool inside = x >= footprint.Position.X && x < footprint.Position.X + footprint.Size.X
                    && y >= footprint.Position.Y && y < footprint.Position.Y + footprint.Size.Y;

                if (!inside)
                {
                    action(new Vector2I(x, y));
                }
            }
        }
    }
}
