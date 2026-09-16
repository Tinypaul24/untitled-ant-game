using Godot;

// Screenshot harness. Plays out a scripted sequence and writes a PNG at each stage.
//
// Exists because the renderer's per-cell shade maths has produced a visual bug that no test could
// have caught - a uint underflow clamped 44% of cells to white - and the only way that was found was
// by capturing a frame and looking at it. It now watches the colony's founding, which is a piece of
// staging rather than logic: whether it reads well is not something an assertion can answer.
public partial class ShotHarness : Node
{
    [Export] public string Tag { get; set; } = "";

    private GridManager grid;
    private Camera2D camera;

    private int frames;

    public override void _Ready()
    {
        grid = GetNode<GridManager>("Main/GridManager");
        camera = GetNode<Camera2D>("Main/Camera2D");

        // Left at whatever the game itself defaults to, so screenshots show what a player sees.
    }

    public override void _Process(double delta)
    {
        frames++;

        // Mid-flight, before she has crossed the view.
        if (frames == 110)
        {
            Save("fly.png");
            return;
        }

        // Burrow cut, workers out - zoomed in, because the question is what a dug cavity looks
        // like up close.
        if (frames == 470)
        {
            camera.Zoom = new Vector2(3f, 3f);
            camera.Position = grid.CellToWorld(grid.NestCenterCell + new Vector2I(7, 7));
            return;
        }

        if (frames == 490)
        {
            Save("founded.png");

            GetTree().Quit();
        }
    }

    private void Save(string name)
    {
        Image image = GetViewport().GetTexture().GetImage();
        image.SavePng($"user://{Tag}{name}");

        GD.Print($"SHOT {ProjectSettings.GlobalizePath($"user://{Tag}{name}")}");
    }
}
