using Godot;

public partial class CameraController : Camera2D
{
    private const float PanSpeed = 250f;

    // Whole-number zoom only.
    //
    // It used to step by 0.1 from 0.5, so almost every reachable zoom resampled the art - a 12px
    // ant drawn at 1.7x lands on fractions of a pixel and the crisp edges the whole art pipeline
    // exists to preserve turn to mush. At integer zoom every source pixel maps to a whole number
    // of screen pixels.
    private const int MinZoom = 1;
    private const int MaxZoom = 4;

    private bool isPanning;
    private Vector2 panStartMouse;
    private Vector2 panStartPosition;

    // Panning is accumulated here at full precision and only the camera's actual Position is
    // rounded. Rounding the accumulator instead would lose every sub-pixel increment and make slow
    // panning stutter or stop entirely.
    private Vector2 panPosition;

    public override void _Ready()
    {
        // Two, not one. At zoom 1 a 12px ant on a 640x360 screen is a speck and the 2px material
        // cells are invisible; at 2 the view is 20x11 tiles and you can actually see what the
        // colony is doing. Whole numbers only - see MinZoom.
        Zoom = new Vector2(2, 2);
        panPosition = Position;
    }

    public override void _Process(double delta)
    {
        // Something else may have moved the camera - Main points it at the landing site on startup,
        // the minimap jumps it on a click, a save restores it. Take their word for it rather than
        // dragging the view back to wherever the accumulator had got to.
        if (Position.DistanceSquaredTo(panPosition) > 1f)
        {
            panPosition = Position;
        }

        Vector2 direction = Vector2.Zero;

        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
        {
            direction.X -= 1f;
        }

        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
        {
            direction.X += 1f;
        }

        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
        {
            direction.Y -= 1f;
        }

        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
        {
            direction.Y += 1f;
        }

        if (direction != Vector2.Zero)
        {
            // Divide by zoom so panning covers the same screen distance per second at any zoom level.
            panPosition += direction.Normalized() * PanSpeed * (float)delta / Zoom.X;
        }

        // Snapped to whole world pixels. The project sets snap_2d_transforms_to_pixel, but that
        // snaps *nodes* - it does nothing for the camera's own transform, so a continuous pan drags
        // the entire world across sub-pixel offsets and every edge in the game crawls.
        Position = panPosition.Round();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            HandleMouseButton(mouseButton);
        }
        else if (@event is InputEventMouseMotion mouseMotion && isPanning)
        {
            // Through the accumulator, so middle-drag snaps to whole pixels like keyboard panning
            // and the two cannot fight over Position.
            panPosition = panStartPosition - (mouseMotion.Position - panStartMouse) / Zoom.X;
            Position = panPosition.Round();
        }
    }

    private void HandleMouseButton(InputEventMouseButton mouseButton)
    {
        if (mouseButton.ButtonIndex == MouseButton.Middle)
        {
            isPanning = mouseButton.Pressed;

            if (isPanning)
            {
                panStartMouse = mouseButton.Position;
                panStartPosition = panPosition;
            }

            return;
        }

        if (!mouseButton.Pressed)
        {
            return;
        }

        if (mouseButton.ButtonIndex == MouseButton.WheelUp)
        {
            SetZoomClamped(Mathf.RoundToInt(Zoom.X) + 1);
        }
        else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
        {
            SetZoomClamped(Mathf.RoundToInt(Zoom.X) - 1);
        }
    }

    private void SetZoomClamped(int zoom)
    {
        int clamped = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        Zoom = new Vector2(clamped, clamped);
    }
}
