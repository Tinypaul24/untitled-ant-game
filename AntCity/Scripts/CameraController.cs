using Godot;

public partial class CameraController : Camera2D
{
    private const float PanSpeed = 250f;
    private const float ZoomStep = 0.1f;
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 3f;

    private bool isPanning;
    private Vector2 panStartMouse;
    private Vector2 panStartPosition;

    public override void _Process(double delta)
    {
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
            Position += direction.Normalized() * PanSpeed * (float)delta / Zoom.X;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            HandleMouseButton(mouseButton);
        }
        else if (@event is InputEventMouseMotion mouseMotion && isPanning)
        {
            Position = panStartPosition - (mouseMotion.Position - panStartMouse) / Zoom.X;
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
                panStartPosition = Position;
            }

            return;
        }

        if (!mouseButton.Pressed)
        {
            return;
        }

        if (mouseButton.ButtonIndex == MouseButton.WheelUp)
        {
            SetZoomClamped(Zoom.X + ZoomStep);
        }
        else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
        {
            SetZoomClamped(Zoom.X - ZoomStep);
        }
    }

    private void SetZoomClamped(float zoom)
    {
        float clamped = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        Zoom = new Vector2(clamped, clamped);
    }
}
