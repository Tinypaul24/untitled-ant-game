using Godot;

// Draws the surfaces ants can actually walk on.
//
// Since ants cannot climb, whether a cell has ground under it is the single most important thing the
// player needs to read, and flat 16px tiles say nothing about it. This traces a lit edge along every
// standable surface. Where a step up is unambiguous the edge slopes into it; where a tight zigzag
// branches both up and down on the same side it stays a step, bridged by a riser, so the staircase
// still reads as one connected route rather than a stack of disconnected squares.
public partial class FloorOverlay : Node2D
{
    private const float SurfaceThickness = 2f;

    private static readonly Color SurfaceColor = new Color("a8845c");

    // Dimmer, so the vertical face of a step never competes with the floors themselves.
    private static readonly Color RiserColor = new Color("6b543a");

    [Export]
    public GridManager Grid { get; set; }

    private Camera2D camera;
    private Vector2 lastCameraPosition;
    private Vector2 lastCameraZoom;
    private bool terrainDirty = true;

    public override void _Ready()
    {
        camera = GetNode<Camera2D>("../Camera2D");
        Grid.TerrainChanged += () => terrainDirty = true;
    }

    public override void _Process(double delta)
    {
        // The surface only moves when the view does or the ground itself changes, so an idle screen
        // costs nothing.
        if (!terrainDirty && camera.GlobalPosition == lastCameraPosition && camera.Zoom == lastCameraZoom)
        {
            return;
        }

        terrainDirty = false;
        lastCameraPosition = camera.GlobalPosition;
        lastCameraZoom = camera.Zoom;

        QueueRedraw();
    }

    public override void _Draw()
    {
        int cellSize = Grid.CellSize;
        float half = cellSize / 2f;

        Vector2 viewSize = GetViewportRect().Size / camera.Zoom;
        Vector2 topLeft = camera.GlobalPosition - viewSize / 2f;

        Vector2I first = Grid.WorldToCell(topLeft) - Vector2I.One;
        Vector2I last = Grid.WorldToCell(topLeft + viewSize) + Vector2I.One;

        for (int y = Mathf.Max(first.Y, 0); y <= last.Y; y++)
        {
            for (int x = first.X; x <= last.X; x++)
            {
                if (!Grid.IsKnownStandable(new Vector2I(x, y)))
                {
                    continue;
                }

                float left = x * cellSize;
                float surface = (y + 1) * cellSize;

                // A neighbour one cell higher means the floor ramps that way, so the edge lifts to
                // meet it and the two surfaces join into one slope. Only when that is the sole
                // connection on the side, though - a tight zigzag has both an upward and a downward
                // neighbour on the same side, and there the honest read is a step, not a slope.
                float leftY = surface - (RampsUp(x, y, -1) ? cellSize : 0);
                float rightY = surface - (RampsUp(x, y, 1) ? cellSize : 0);

                Vector2 middle = new Vector2(left + half, surface);

                DrawLine(new Vector2(left, leftY), middle, SurfaceColor, SurfaceThickness);
                DrawLine(middle, new Vector2(left + cellSize, rightY), SurfaceColor, SurfaceThickness);

                DrawStepRiser(x, y, left + cellSize, rightY, cellSize);
            }
        }
    }

    // Links a ledge to the one diagonally beside it, so a zigzag staircase reads as one connected
    // route rather than a ladder of floating shelves.
    private void DrawStepRiser(int x, int y, float edgeX, float edgeY, int cellSize)
    {
        for (int dy = -1; dy <= 1; dy += 2)
        {
            Vector2I other = new Vector2I(x + 1, y + dy);

            if (!Grid.IsKnownStandable(other))
            {
                continue;
            }

            float otherY = (other.Y + 1) * cellSize - (RampsUp(other.X, other.Y, -1) ? cellSize : 0);

            if (otherY != edgeY)
            {
                DrawLine(new Vector2(edgeX, edgeY), new Vector2(edgeX, otherY), RiserColor, SurfaceThickness);
            }
        }
    }

    // True when the only standable neighbour on this side sits one cell higher.
    private bool RampsUp(int x, int y, int side)
    {
        return Grid.IsKnownStandable(new Vector2I(x + side, y - 1))
            && !Grid.IsKnownStandable(new Vector2I(x + side, y))
            && !Grid.IsKnownStandable(new Vector2I(x + side, y + 1));
    }
}
