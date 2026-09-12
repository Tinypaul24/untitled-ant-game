using Godot;
using System.Collections.Generic;

public partial class Room : Node2D
{
    public enum RoomState
    {
        Excavating,
        Furnishing,
        Active
    }

    private const float ExcavatingAlpha = 0.15f;
    private const float FurnishingAlpha = 0.35f;
    private const float ActiveAlpha = 0.5f;

    private static readonly Dictionary<BuildingType, Color> TintColors = new()
    {
        { BuildingType.NestingChamber, new Color(0.61f, 0.34f, 0.22f) },
        { BuildingType.Granary, new Color(0.85f, 0.64f, 0.25f) },
        { BuildingType.Nursery, new Color(0.93f, 0.90f, 0.83f) },
    };

    private static readonly Dictionary<BuildingType, Texture2D> Icons = new()
    {
        { BuildingType.NestingChamber, GD.Load<Texture2D>("res://AntCity/Textures/Buildings/NestingChamber.svg") },
        { BuildingType.Granary, GD.Load<Texture2D>("res://AntCity/Textures/Buildings/Granary.svg") },
        { BuildingType.Nursery, GD.Load<Texture2D>("res://AntCity/Textures/Buildings/Nursery.svg") },
    };

    private static readonly Texture2D ScaffoldIcon = GD.Load<Texture2D>("res://AntCity/Textures/Buildings/ConstructionSite.svg");

    public BuildingType Type { get; set; }
    public Rect2I Footprint { get; set; }
    public RoomState State { get; private set; } = RoomState.Excavating;
    public bool FurnishClaimed { get; set; }

    private HashSet<Vector2I> pendingDigCells;
    private int cellSize;

    public int CellCount => Footprint.Size.X * Footprint.Size.Y;
    public bool IsFullyDug => pendingDigCells.Count == 0;
    public IReadOnlyCollection<Vector2I> PendingDigCells => pendingDigCells;

    // The cell an ant should stand on to furnish this room, regardless of its shape.
    public Vector2I StandCell => Footprint.Position + Footprint.Size / 2;

    public void Initialize(int gridCellSize, HashSet<Vector2I> initialPendingCells)
    {
        cellSize = gridCellSize;
        pendingDigCells = initialPendingCells;
        Position = new Vector2(Footprint.Position.X * cellSize, Footprint.Position.Y * cellSize);

        if (IsFullyDug)
        {
            State = RoomState.Furnishing;
        }

        QueueRedraw();
    }

    public void NotifyCellDug(Vector2I cell)
    {
        pendingDigCells.Remove(cell);
        QueueRedraw();
    }

    public void BeginFurnishing()
    {
        State = RoomState.Furnishing;
        QueueRedraw();
    }

    public void Activate()
    {
        State = RoomState.Active;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = new Vector2(Footprint.Size.X * cellSize, Footprint.Size.Y * cellSize);

        Color tint = TintColors[Type];
        tint.A = State switch
        {
            RoomState.Excavating => ExcavatingAlpha,
            RoomState.Furnishing => FurnishingAlpha,
            _ => ActiveAlpha,
        };

        DrawRect(new Rect2(Vector2.Zero, size), tint, filled: true);

        Vector2 center = size / 2f;

        if (State == RoomState.Furnishing)
        {
            Vector2 iconSize = new Vector2(cellSize, cellSize) * 0.6f;
            DrawTextureRect(ScaffoldIcon, new Rect2(center - iconSize / 2f, iconSize), false);
        }
        else if (State == RoomState.Active)
        {
            Vector2 iconSize = new Vector2(cellSize, cellSize) * 0.9f;
            DrawTextureRect(Icons[Type], new Rect2(center - iconSize / 2f, iconSize), false);
        }
    }
}
