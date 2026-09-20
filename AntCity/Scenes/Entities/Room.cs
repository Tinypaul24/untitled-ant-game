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

    private const float FurnishingAlpha = 0.4f;
    private const float ActiveAlpha = 0.5f;
    // Ground that has been marked out but not yet dug. Drawn as an outline rather than a wash: a
    // fifteen-percent tint over solid earth was so nearly invisible that an excavating room looked
    // like nothing had happened, which is how "I placed a room and it did not work" happens.
    private const float PendingAlpha = 0.22f;
    private const float PendingOutlineAlpha = 0.75f;

    private static readonly Color SelectionColor = new Color(1f, 1f, 0.55f);

    private static readonly Dictionary<BuildingType, Color> TintColors = new()
    {
        { BuildingType.NestingChamber, new Color(0.61f, 0.34f, 0.22f) },
        { BuildingType.Granary, new Color(0.85f, 0.64f, 0.25f) },
        { BuildingType.Nursery, new Color(0.93f, 0.90f, 0.83f) },
        { BuildingType.FungusFarm, new Color(0.55f, 0.72f, 0.45f) },
        { BuildingType.RoyalChamber, new Color(0.72f, 0.45f, 0.72f) },
    };

    private static readonly Dictionary<BuildingType, Texture2D> Icons = new()
    {
        { BuildingType.NestingChamber, GD.Load<Texture2D>("res://AntCity/Textures/Buildings/NestingChamber.svg") },
        { BuildingType.Granary, GD.Load<Texture2D>("res://AntCity/Textures/Buildings/Granary.svg") },
        { BuildingType.Nursery, GD.Load<Texture2D>("res://AntCity/Textures/Buildings/Nursery.svg") },
        // No art of their own yet, so they borrow the nearest thing and are told apart by tint.
        { BuildingType.FungusFarm, GD.Load<Texture2D>("res://AntCity/Textures/Buildings/Granary.svg") },
        { BuildingType.RoyalChamber, GD.Load<Texture2D>("res://AntCity/Textures/Buildings/NestingChamber.svg") },
    };

    private static readonly Texture2D ScaffoldIcon = GD.Load<Texture2D>("res://AntCity/Textures/Buildings/ConstructionSite.svg");

    public BuildingType Type { get; set; }
    public Rect2I Footprint { get; set; }
    public RoomState State { get; private set; } = RoomState.Excavating;
    public bool FurnishClaimed { get; set; }

    private bool isSelected;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            isSelected = value;
            QueueRedraw();
        }
    }

    private HashSet<Vector2I> pendingDigCells;
    private int cellSize;

    // The cells the room actually occupies.
    //
    // Not the same thing as its footprint, and the distinction is now visible: rock inside the
    // outline is never dug and never becomes part of the room, so it must not be paid for, must not
    // count towards what the room does, and must not be painted over as though it belonged. Rooms
    // build around stone, and now they look like it.
    private readonly HashSet<Vector2I> ownedCells = new();

    public int CellCount => ownedCells.Count;
    public IReadOnlyCollection<Vector2I> OwnedCells => ownedCells;
    public bool IsFullyDug => pendingDigCells.Count == 0;
    public IReadOnlyCollection<Vector2I> PendingDigCells => pendingDigCells;

    public void SetOwnedCells(IEnumerable<Vector2I> cells)
    {
        ownedCells.Clear();

        foreach (Vector2I cell in cells)
        {
            ownedCells.Add(cell);
        }

        QueueRedraw();
    }

    // The cell an ant should stand on to furnish this room.
    //
    // The middle of the footprint used to do, which is a cell the room may well not own - solid rock
    // in the centre of a chamber sent the furnisher to stand inside a wall. This picks the owned cell
    // closest to that middle: the same answer for a rectangular room, and a place that actually
    // exists for one built around an outcrop.
    public Vector2I StandCell
    {
        get
        {
            Vector2I middle = Footprint.Position + Footprint.Size / 2;

            if (ownedCells.Count == 0 || ownedCells.Contains(middle))
            {
                return middle;
            }

            Vector2I best = middle;
            int bestDistance = int.MaxValue;

            foreach (Vector2I cell in ownedCells)
            {
                int distance = (cell - middle).LengthSquared();

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = cell;
                }
            }

            return best;
        }
    }

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

    public void RestoreState(int gridCellSize, HashSet<Vector2I> remainingDigCells, RoomState restoredState, bool furnishClaimed)
    {
        cellSize = gridCellSize;
        pendingDigCells = remainingDigCells;
        State = restoredState;
        FurnishClaimed = furnishClaimed;
        Position = new Vector2(Footprint.Position.X * cellSize, Footprint.Position.Y * cellSize);

        QueueRedraw();
    }


    // Ground the room is never going to get - it turned to rock after the room was placed. Dropped
    // from both sets, so the room's size, its effect and its refund all shrink honestly.
    public void AbandonCell(Vector2I cell)
    {
        pendingDigCells.Remove(cell);
        ownedCells.Remove(cell);

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

    // How far through excavation this room is, 0 to 1. For the inspector.
    public float DugFraction
    {
        get
        {
            if (ownedCells.Count == 0)
            {
                return 1f;
            }

            return (ownedCells.Count - pendingDigCells.Count) / (float)ownedCells.Count;
        }
    }

    public bool Contains(Vector2I cell)
    {
        return ownedCells.Contains(cell);
    }

    public override void _Draw()
    {
        Color tint = TintColors[Type];
        var cell = new Vector2(cellSize, cellSize);

        // Cell by cell rather than one rectangle over the bounding box. The box painted over rock
        // the room neither owns nor can use, so a room built around an outcrop took visible credit
        // for ground it had never touched.
        foreach (Vector2I owned in ownedCells)
        {
            var topLeft = new Vector2(owned.X - Footprint.Position.X, owned.Y - Footprint.Position.Y) * cellSize;

            if (!pendingDigCells.Contains(owned))
            {
                tint.A = State == RoomState.Active ? ActiveAlpha : FurnishingAlpha;
                DrawRect(new Rect2(topLeft, cell), tint, filled: true);

                continue;
            }

            // Still earth. Outlined as well as tinted so it reads as "marked out, waiting to be
            // dug" rather than as a faint stain on the ground.
            tint.A = PendingAlpha;
            DrawRect(new Rect2(topLeft, cell), tint, filled: true);

            tint.A = PendingOutlineAlpha;
            DrawRect(new Rect2(topLeft + Vector2.One, cell - Vector2.One * 2f), tint, filled: false, width: 1f);
        }

        DrawIcon();

        if (isSelected)
        {
            var size = new Vector2(Footprint.Size.X, Footprint.Size.Y) * cellSize;
            DrawRect(new Rect2(Vector2.Zero, size), SelectionColor, filled: false, width: 1f);
        }
    }

    private void DrawIcon()
    {
        Vector2 middle = new Vector2(StandCell.X - Footprint.Position.X, StandCell.Y - Footprint.Position.Y) * cellSize
            + new Vector2(cellSize, cellSize) / 2f;

        // An excavating room shows the scaffold too. It used to show nothing at all until the last
        // cell was dug, so the longest phase of the life of a room was the one with no sign of it.
        Texture2D icon = State == RoomState.Active ? Icons[Type] : ScaffoldIcon;
        float scale = State == RoomState.Active ? 0.9f : 0.6f;
        Vector2 iconSize = new Vector2(cellSize, cellSize) * scale;

        DrawTextureRect(icon, new Rect2(middle - iconSize / 2f, iconSize), false);
    }
}
