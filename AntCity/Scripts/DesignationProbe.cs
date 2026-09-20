using Godot;

// Verifies that a player-drawn dig designation actually gets claimed and dug by a worker,
// headless and reproducibly, instead of needing a human to drag a rectangle in the editor.
//
// Run with:
//   godot --headless --path . res://AntCity/Scenes/DesignationCheck.tscn
public partial class DesignationProbe : Node
{
    [Export] public int RunSeconds { get; set; } = 150;

    // How long to let the colony dig its own entrance organically before marking anything, so
    // there is at least one already-open cell to stand next to.
    [Export] public int WarmupSeconds { get; set; } = 8;

    // Whichever idle ant claims the mark can be anywhere in the colony's existing tunnels, so this
    // has to cover a genuinely long solo walk-and-dig, not just the marked cell's own dig time.
    [Export] public int TimeoutSeconds { get; set; } = 120;

    private GridManager grid;
    private BuildManager build;
    private ColonyFounding founding;
    private Node main;

    private double elapsed;
    private bool marked;
    private Vector2I markedCell;
    private double markedAt;

    public override void _Ready()
    {
        main = GetNode("Main");
        grid = main.GetNode<GridManager>("GridManager");
        build = main.GetNode<BuildManager>("BuildManager");
        founding = main.GetNode<ColonyFounding>("ColonyFounding");

        Engine.TimeScale = 4f;

        GD.Print("--- designation probe ---");
    }

    public override void _Process(double delta)
    {
        elapsed += delta;

        if (!founding.Finished)
        {
            return;
        }

        if (!marked)
        {
            if (elapsed < WarmupSeconds)
            {
                return;
            }

            if (!TryFindDiggableNextToTunnel(out Vector2I target))
            {
                GD.Print("  [probe] no diggable cell adjacent to a tunnel yet, waiting");
                return;
            }

            build.MarkForDiggingForTest(new Rect2I(target, Vector2I.One));
            marked = true;
            markedCell = target;
            markedAt = elapsed;

            GD.Print($"  [probe] marked {target} for digging (designations now {build.DesignationCount})");
        }
        else if (build.DesignationCount == 0)
        {
            GD.Print($"  [probe] PASS: designation at {markedCell} cleared after {elapsed - markedAt:F1}s (dug={grid.IsTunnel(markedCell)})");
            GetTree().Quit();
            return;
        }
        else if (elapsed - markedAt > TimeoutSeconds)
        {
            GD.Print($"  [probe] FAIL: designation at {markedCell} still pending after {TimeoutSeconds}s (dug={grid.IsTunnel(markedCell)})");
            GetTree().Quit();
            return;
        }

        if (elapsed >= RunSeconds)
        {
            GD.Print("  [probe] FAIL: timed out overall");
            GetTree().Quit();
        }
    }

    private bool TryFindDiggableNextToTunnel(out Vector2I found)
    {
        Vector2I nest = grid.NestCenterCell;

        // Close to the nest and reachable by an actual path, not just any open tile generation
        // happened to leave lying around - a natural cave pocket a long way off is "open" by
        // IsTunnel but no ant can ever get there to dig its neighbour.
        for (int y = -2; y <= 10; y++)
        {
            for (int x = -8; x <= 8; x++)
            {
                var cell = nest + new Vector2I(x, y);

                if (!grid.IsTunnel(cell) || grid.FindTunnelPath(cell, nest) == null)
                {
                    continue;
                }

                foreach (Vector2I offset in new[] { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right })
                {
                    Vector2I neighbor = cell + offset;

                    if (grid.CanDig(neighbor))
                    {
                        found = neighbor;
                        return true;
                    }
                }
            }
        }

        found = default;
        return false;
    }
}
