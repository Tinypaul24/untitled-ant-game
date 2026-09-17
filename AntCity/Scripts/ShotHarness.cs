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
            return;
        }

        // A room with stone in the middle of it, selected. Two things only a picture can answer:
        // whether a room now reads as the cells it owns rather than as a rectangle over ground it
        // does not, and whether the inspector says anything useful about it.
        if (frames == 5430)
        {
            PlaceRoomWithRockInIt();

            camera.Zoom = new Vector2(4f, 4f);
            camera.Position = grid.CellToWorld(RoomOrigin + new Vector2I(2, 1));
            return;
        }

        if (frames == 5450)
        {
            Save("room.png");
            return;
        }

        // The placement preview, which is judged entirely by looking: whether the cells read as
        // what they are, and whether the ring reads as the wall the ants are going to build.
        if (frames == 5460)
        {
            ArmPlacementPreview();
            return;
        }

        if (frames == 5475)
        {
            Save("preview.png");

            GetTree().Quit();
        }
    }

    private Vector2I RoomOrigin => grid.NestCenterCell + new Vector2I(-16, 7);

    // Dug in advance so the room skips straight to furnishing, with two cells forced to stone so the
    // room has to build around them.
    private void PlaceRoomWithRockInIt()
    {
        var build = GetNode<BuildManager>("Main/BuildManager");
        var colony = GetNode<ColonyManager>("Main/ColonyManager");

        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                grid.Dig(RoomOrigin + new Vector2I(x, y));
            }
        }

        grid.SetTileFromSimulation(RoomOrigin + new Vector2I(1, 0), GridManager.TileType.Rock);
        grid.SetTileFromSimulation(RoomOrigin + new Vector2I(2, 1), GridManager.TileType.Rock);

        colony.AddFood(200);
        build.TryCreateRoom(new Rect2I(RoomOrigin, new Vector2I(4, 2)), BuildingType.Nursery);

        Room placed = null;

        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is Room room && room.Footprint.Position == RoomOrigin)
            {
                placed = room;
                build.ReportFurnishDone(room);
                build.SelectRoom(room);
            }
        }

        // Said out loud. This runs after ninety seconds of live ants, so the ring here may have been
        // chewed open by a forager on her way past - and a photo harness that silently photographs
        // nothing is worse than the rule it is hiding.
        if (placed == null)
        {
            GD.PushError($"ShotHarness: no room was placed at {RoomOrigin}; room.png shows nothing.");
        }
    }

    // Arms a placement and puts the cursor over ground with a corridor already through it, so the
    // ring has both walls and a breach in it and the preview has something to say.
    private void ArmPlacementPreview()
    {
        var build = GetNode<BuildManager>("Main/BuildManager");

        camera.Zoom = new Vector2(4f, 4f);
        camera.Position = grid.CellToWorld(CorridorStart + new Vector2I(2, 2));

        build.BeginPlacement(BuildingType.Granary);
        build.PreviewForTest(new Rect2I(CorridorStart + new Vector2I(1, 1), new Vector2I(3, 2)));
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
