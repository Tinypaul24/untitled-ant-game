using Godot;

public partial class MiniMap : Control
{
    private const float WorldRadiusCells = 120f;
    private const float NestMarkerRadius = 5f;
    private const float FoodMarkerRadius = 3f;

    private static readonly Color BackgroundColor = new Color(0.1f, 0.08f, 0.06f, 0.7f);
    private static readonly Color NestColor = new Color(0.85f, 0.64f, 0.25f);
    private static readonly Color FoodColor = new Color(0.4f, 0.75f, 0.35f);

    [Export]
    public GridManager GridManager { get; set; }

    private Camera2D camera;

    public override void _Ready()
    {
        camera = GetNode<Camera2D>("/root/Main/Camera2D");
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        float displayRadius = Mathf.Min(Size.X, Size.Y) / 2f;
        Vector2 center = Size / 2f;

        DrawCircle(center, displayRadius, BackgroundColor);

        Vector2 origin = camera.GlobalPosition;
        float scale = GetMapScale();

        DrawMarker(center, displayRadius, origin, GridManager.CellToWorld(GridManager.NestCenterCell), scale, NestColor, NestMarkerRadius);

        foreach (Vector2I cell in GridManager.GetFoodSourceCells())
        {
            DrawMarker(center, displayRadius, origin, GridManager.CellToWorld(cell), scale, FoodColor, FoodMarkerRadius);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton &&
            mouseButton.ButtonIndex == MouseButton.Left &&
            mouseButton.Pressed)
        {
            float scale = GetMapScale();
            Vector2 clickOffset = mouseButton.Position - Size / 2f;
            camera.GlobalPosition += clickOffset / scale;

            AcceptEvent();
        }
    }

    private float GetMapScale()
    {
        float displayRadius = Mathf.Min(Size.X, Size.Y) / 2f;
        return displayRadius / (WorldRadiusCells * GridManager.CellSize);
    }

    private void DrawMarker(Vector2 center, float displayRadius, Vector2 origin, Vector2 worldPos, float scale, Color color, float radius)
    {
        Vector2 delta = (worldPos - origin) * scale;

        if (delta.Length() > displayRadius)
        {
            return;
        }

        DrawCircle(center + delta, radius, color);
    }
}
