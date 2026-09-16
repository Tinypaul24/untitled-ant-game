using Godot;
using System.Collections.Generic;

// A schematic dot-map of the burrow and the food around it, centred on the camera.
//
// Two things about this are load-bearing rather than incidental, because the obvious version of both
// was costing about half the frame budget on its own:
//
//  - It redraws when what it shows changes, not every frame. The camera usually is not moving, and
//    food changes at the pace ants harvest it.
//  - Food is aggregated into buckets before it is drawn. The map is a couple of hundred pixels
//    across showing a couple of hundred tiles, so an individual deposit is a fraction of a pixel -
//    drawing one circle per deposit was hundreds of draw calls a frame to render a smudge.
public partial class MiniMap : Control
{
    // How much world the map covers, in tiles from the centre. Sized against the panel: at 66px
    // across, showing 240 tiles put every bucket within two pixels of the next and the food read as
    // one solid blob rather than as places to go.
    private const float WorldRadiusCells = 56f;
    private const float NestMarkerRadius = 2f;
    private const float FoodMarkerRadius = 1f;

    // Tiles per food bucket. About four map pixels at the scale above, which keeps neighbouring
    // patches as separate dots instead of merging them.
    private const int FoodBucketCells = 7;

    // How far the camera has to move before the map is worth rebuilding.
    private const float RedrawMoveThreshold = 8f;

    // Food is picked up slowly, so a refresh twice a second is well past imperceptible.
    private const double RefreshSeconds = 0.5;

    private static readonly Color BackgroundColor = new Color(0.1f, 0.08f, 0.06f, 0.7f);
    private static readonly Color NestColor = new Color(0.85f, 0.64f, 0.25f);
    private static readonly Color FoodColor = new Color(0.4f, 0.75f, 0.35f);

    [Export]
    public GridManager GridManager { get; set; }

    private Camera2D camera;

    private readonly HashSet<Vector2I> foodBuckets = new();
    private readonly List<Vector2I> foodScratch = new();

    private Vector2 lastOrigin = new(float.MaxValue, float.MaxValue);
    private double sinceRefresh = RefreshSeconds;

    public override void _Ready()
    {
        camera = GetNode<Camera2D>("../../../../Camera2D");
    }

    public override void _Process(double delta)
    {
        sinceRefresh += delta;

        bool moved = camera.GlobalPosition.DistanceSquaredTo(lastOrigin) >= RedrawMoveThreshold * RedrawMoveThreshold;

        if (!moved && sinceRefresh < RefreshSeconds)
        {
            return;
        }

        lastOrigin = camera.GlobalPosition;
        sinceRefresh = 0;

        RebuildFoodBuckets();
        QueueRedraw();
    }

    // Collapses every food cell in range onto a coarse grid, so what gets drawn is one marker per
    // patch rather than one per deposit.
    private void RebuildFoodBuckets()
    {
        foodBuckets.Clear();
        foodScratch.Clear();

        GridManager.CollectFoodSourceCells(foodScratch);

        Vector2I centre = GridManager.WorldToCell(camera.GlobalPosition);
        int radius = Mathf.CeilToInt(WorldRadiusCells);

        foreach (Vector2I cell in foodScratch)
        {
            // Culled in cell space before any of the floating point work, since almost everything
            // the grid knows about is off the edge of the map.
            if (Mathf.Abs(cell.X - centre.X) > radius || Mathf.Abs(cell.Y - centre.Y) > radius)
            {
                continue;
            }

            foodBuckets.Add(new Vector2I(
                Mathf.FloorToInt(cell.X / (float)FoodBucketCells),
                Mathf.FloorToInt(cell.Y / (float)FoodBucketCells)
            ));
        }
    }

    public override void _Draw()
    {
        float displayRadius = Mathf.Min(Size.X, Size.Y) / 2f;
        Vector2 center = Size / 2f;

        DrawCircle(center, displayRadius, BackgroundColor);

        Vector2 origin = camera.GlobalPosition;
        float scale = GetMapScale();

        DrawMarker(center, displayRadius, origin, GridManager.CellToWorld(GridManager.NestCenterCell), scale, NestColor, NestMarkerRadius);

        foreach (Vector2I bucket in foodBuckets)
        {
            Vector2I cell = bucket * FoodBucketCells + new Vector2I(FoodBucketCells / 2, FoodBucketCells / 2);

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
