using Godot;

// Spawning and readouts for working on the simulation.
//
// F1 toggles it. While it is on, 1-5 pick a material and the left mouse button paints it, which is
// how you check that sand piles, water levels out, oil floats and fire crawls. The readout exists to
// answer the question that matters for performance: how much of the world is actually being ticked.
public partial class MaterialDebugTool : Node2D
{
    private const float BrushRadius = 14f;
    private const float BlastRadius = 40f;

    [Export]
    public MaterialWorld World { get; set; }

    [Export]
    public MaterialRenderer Renderer { get; set; }

    private static readonly (Key Key, MaterialId Material)[] Palette =
    {
        (Key.Key1, MaterialId.Sand),
        (Key.Key2, MaterialId.Water),
        (Key.Key3, MaterialId.Oil),
        (Key.Key4, MaterialId.Fire),
        (Key.Key5, MaterialId.Lava),
        (Key.Key6, MaterialId.Acid),
        (Key.Key7, MaterialId.Steam),
        (Key.Key8, MaterialId.Ice),
        (Key.Key9, MaterialId.LooseDirt),
        (Key.Key0, MaterialId.Air),
    };

    private Label readout;
    private MaterialId selected = MaterialId.Sand;
    private bool active;
    private bool painting;

    private double sampleTimer;

    public override void _Ready()
    {
        CanvasLayer layer = new CanvasLayer { Layer = 100 };
        AddChild(layer);

        readout = new Label
        {
            Position = new Vector2(4, 46),
            Visible = false,
        };

        // Sized for the 640x360 base resolution, where the theme default would fill the screen.
        readout.AddThemeFontSizeOverride("font_size", 6);
        readout.AddThemeColorOverride("font_color", new Color("f1e6d3"));
        readout.AddThemeColorOverride("font_outline_color", new Color("17110c"));
        readout.AddThemeConstantOverride("outline_size", 2);

        layer.AddChild(readout);
    }

    public override void _Process(double delta)
    {
        if (!active)
        {
            return;
        }

        if (painting)
        {
            World.Paint(GetGlobalMousePosition(), BrushRadius, selected, onlyReplaceAir: selected != MaterialId.Air);
        }

        // Sampled rather than per-frame so the numbers are readable instead of strobing.
        sampleTimer += delta;

        if (sampleTimer < 0.2)
        {
            return;
        }

        sampleTimer = 0;
        UpdateReadout();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            HandleKey(key);
            return;
        }

        if (!active || @event is not InputEventMouseButton mouse || mouse.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        // Swallow the click so painting terrain does not also box-select ants.
        painting = mouse.Pressed;
        GetViewport().SetInputAsHandled();
    }

    private void HandleKey(InputEventKey key)
    {
        if (key.Keycode == Key.F1)
        {
            active = !active;
            painting = false;
            readout.Visible = active;

            if (active)
            {
                UpdateReadout();
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if (!active)
        {
            return;
        }


        if (key.Keycode == Key.E)
        {
            World.Explode(GetGlobalMousePosition(), BlastRadius);
            UpdateReadout();
            GetViewport().SetInputAsHandled();

            return;
        }

        foreach ((Key Key, MaterialId Material) entry in Palette)
        {
            if (key.Keycode != entry.Key)
            {
                continue;
            }

            selected = entry.Material;
            UpdateReadout();
            GetViewport().SetInputAsHandled();

            return;
        }
    }

    private void UpdateReadout()
    {
        MaterialSimulation simulation = World.Simulation;

        if (simulation == null)
        {
            return;
        }


        // Deferred is the one to watch: anything above zero means the tick hit its budget and some
        // chunks are being serviced on a later tick, which is what a flood looks like from here.
        string deferred = simulation.ChunksDeferredLastTick > 0
            ? $"   DEFERRED CHUNKS: {simulation.ChunksDeferredLastTick}"
            : "";

        readout.Text =
            $"MATERIAL SIM  [F1 to hide]\n" +
            $"brush: {MaterialDatabase.Get(selected).Name}   1 sand 2 water 3 oil 4 fire 5 lava 6 acid 7 steam 8 ice 9 soil 0 erase   E = explode\n" +
            $"chunks: {World.ChunkCount}   awake: {World.AwakeChunkCount}   sleeping: {World.ChunkCount - World.AwakeChunkCount}\n" +
            $"cells scanned/tick: {simulation.CellsProcessedLastTick}   moved: {simulation.CellsMovedLastTick}{deferred}\n" +
            $"texture uploads/frame: {(Renderer != null ? Renderer.UploadsLastFrame : 0)}\n" +
            $"heat-tracked cells: {World.TrackedTemperatureCells}\n" +
            $"cell size: {MaterialWorld.CellSize}px   chunk: {MaterialChunk.Size}x{MaterialChunk.Size} cells";
    }
}
