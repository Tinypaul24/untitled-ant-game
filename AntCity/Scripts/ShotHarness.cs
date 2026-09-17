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
            return;
        }

        // A plain corridor, cut on purpose and photographed close up.
        //
        // The queen's burrow is a shaft and a chamber in terrain that already has rock and slopes in
        // it, which is a poor subject for the one question this answers: what shape does a bore
        // leave? Six times zoom because at three a four-pixel ceiling is two screen pixels and you
        // cannot honestly say either way.
        if (frames == 500)
        {
            CutCorridor();

            camera.Zoom = new Vector2(6f, 6f);
            camera.Position = grid.CellToWorld(CorridorStart + new Vector2I(4, 0));
            return;
        }

        if (frames == 520)
        {
            Save("bore.png");
            return;
        }

        // Long after founding, zoomed out, because a trail is a colony-scale pattern: the question
        // is whether the traffic has a shape, and you cannot see a shape one corridor at a time.
        if (frames == 5400)
        {
            camera.Zoom = new Vector2(1f, 1f);
            camera.Position = grid.CellToWorld(grid.NestCenterCell + new Vector2I(0, 6));
            return;
        }

        if (frames == 5420)
        {
            Save("trails.png");

            GetTree().Quit();
        }
    }

    private Vector2I CorridorStart => grid.NestCenterCell + new Vector2I(14, 6);

    // A straight run, a two-tile chamber off it and a diagonal step down, which between them cover
    // every case the bore has to get right: a ceiling kept, a ceiling removed because the tile above
    // was opened too, and two cavities meeting at a corner.
    private void CutCorridor()
    {
        for (int x = 0; x < 9; x++)
        {
            grid.Dig(CorridorStart + new Vector2I(x, 0));
        }

        for (int x = 2; x < 5; x++)
        {
            grid.Dig(CorridorStart + new Vector2I(x, -1));
        }

        for (int step = 1; step <= 3; step++)
        {
            grid.Dig(CorridorStart + new Vector2I(8 + step, step));
        }
    }

    private void Save(string name)
    {
        Image image = GetViewport().GetTexture().GetImage();
        image.SavePng($"user://{Tag}{name}");

        GD.Print($"SHOT {ProjectSettings.GlobalizePath($"user://{Tag}{name}")}");
    }
}
