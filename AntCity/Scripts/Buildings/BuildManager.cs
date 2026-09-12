using Godot;
using System.Collections.Generic;

public partial class BuildManager : Node2D
{
    private const int MinFootprintDimension = 2;

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
        GridManager.CellDug += OnCellDug;
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
    public bool TryClaimDigJob(Vector2 fromPosition, out Vector2I cell)
    {
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
    }

    public void ReportFurnishDone(Room room)
    {
        room.Activate();

        BuildingDef def = BuildingDefs.All[room.Type];
        float effect = def.EffectPerCell * room.CellCount;

        switch (room.Type)
        {
            case BuildingType.NestingChamber:
                ColonyManager.IncreaseCapacity(Mathf.RoundToInt(effect));
                break;
            case BuildingType.Granary:
                ColonyManager.IncreaseFoodCapacity(Mathf.RoundToInt(effect));
                break;
            case BuildingType.Nursery:
                ColonyManager.AddNursery(room.CellCount);
                break;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsPlacing)
        {
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

        DrawRect(new Rect2(topLeft, size), IsFootprintValid(footprint) ? ValidPreviewColor : InvalidPreviewColor, filled: true);
    }

    private void OnCellDug(Vector2I cell)
    {
        claimedDigCells.Remove(cell);

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

    private void TryCreateRoom(Rect2I footprint)
    {
        if (!IsFootprintValid(footprint))
        {
            return;
        }

        BuildingType type = pendingType.Value;
        int cellCount = footprint.Size.X * footprint.Size.Y;
        BuildingDef def = BuildingDefs.All[type];

        if (!ColonyManager.RemoveFood(def.FoodCostPerCell * cellCount))
        {
            return;
        }

        var pendingCells = new HashSet<Vector2I>();
        ForEachCell(footprint, cell =>
        {
            if (!GridManager.IsTunnel(cell))
            {
                pendingCells.Add(cell);
            }
        });

        Room room = RoomScene.Instantiate<Room>();
        room.Type = type;
        room.Footprint = footprint;
        room.Initialize(GridManager.CellSize, pendingCells);
        GetParent().AddChild(room);
        rooms.Add(room);

        ForEachCell(footprint, cell => roomsByCell[cell] = room);
    }

    private bool IsFootprintValid(Rect2I footprint)
    {
        if (footprint.Size.X < MinFootprintDimension || footprint.Size.Y < MinFootprintDimension)
        {
            return false;
        }

        bool valid = true;

        ForEachCell(footprint, cell =>
        {
            if (!valid || roomsByCell.ContainsKey(cell) || !GridManager.IsInBounds(cell))
            {
                valid = false;
                return;
            }

            if (!GridManager.IsTunnel(cell) && !GridManager.CanDig(cell))
            {
                valid = false;
            }
        });

        return valid;
    }

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
