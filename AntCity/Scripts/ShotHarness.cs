using Godot;

// Screenshot harness. Paints a known set of materials, lets them settle, and writes a PNG.
//
// Exists because the renderer's per-cell shade maths has produced a visual bug that no test could
// have caught - a uint underflow clamped 44% of cells to white - and the only way that was found was
// by capturing a frame and looking at it.
public partial class ShotHarness : Node
{
    [Export]
    public string OutputPath { get; set; } = "user://shot_dune.png";

    private GridManager grid;
    private MaterialWorld materials;
    private int frames;

    public override void _Ready()
    {
        grid = GetNode<GridManager>("Main/GridManager");
        materials = GetNode<MaterialWorld>("Main/MaterialWorld");

        Vector2I nest = grid.NestCenterCell;

        // One band of each rendered kind, side by side, so a colour or shade regression is obvious.
        Paint(nest + new Vector2I(-6, -3), 4, 2, MaterialId.Sand);
        Paint(nest + new Vector2I(-1, -3), 4, 2, MaterialId.Water);
        Paint(nest + new Vector2I(4, -3), 4, 2, MaterialId.Oil);

        // A heap on the open surface. This is the case that makes the floor overlay awkward: a dune
        // is a stepped profile with a standable tile at nearly every level, so it is where ledge
        // lines either read as terrain or bury the pile in hatching.
        Paint(new Vector2I(nest.X - 2, grid.SurfaceHeight - 4), 5, 3, MaterialId.Sand);

        RenderingServer.FramePostDraw += Capture;
    }

    private void Paint(Vector2I origin, int wide, int tall, MaterialId material)
    {
        for (int y = 0; y < tall; y++)
        {
            for (int x = 0; x < wide; x++)
            {
                materials.FillTile(origin + new Vector2I(x, y), material);
            }
        }
    }

    private void Capture()
    {
        // A few frames in, so the material has fallen and the textures have been uploaded.
        if (++frames < 45)
        {
            return;
        }

        RenderingServer.FramePostDraw -= Capture;

        Image image = GetViewport().GetTexture().GetImage();
        image.SavePng(OutputPath);

        GD.Print($"SHOT {ProjectSettings.GlobalizePath(OutputPath)}");
        GetTree().Quit();
    }
}
