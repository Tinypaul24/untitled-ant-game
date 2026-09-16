using Godot;
using System.Collections.Generic;

// Checks that a worker actually walks away from danger, over real frames.
//
// The terrain tests prove the routing primitives refuse to cross lava, which is not the same claim:
// an ant could ignore every one of them and still pass those. This drops lava directly onto a
// working ant and watches what she does about it.
//
// Run with:
//   godot --headless --path . res://AntCity/Scenes/HazardCheck.tscn
public partial class HazardBehaviourCheck : Node
{
    private GridManager grid;
    private MaterialWorld materials;
    private AntWorker subject;

    private Vector2I engulfed;
    private int frames;
    private int passed;
    private int failed;

    public override void _Ready()
    {
        grid = GetNode<GridManager>("Main/GridManager");
        materials = GetNode<MaterialWorld>("Main/MaterialWorld");

        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is AntWorker ant)
            {
                subject = ant;
                break;
            }
        }

        GD.Print("--- hazard behaviour ---");
    }

    public override void _Process(double delta)
    {
        frames++;

        // A few frames of ordinary life first, so she is doing something real when it arrives.
        if (frames == 20)
        {
            engulfed = grid.WorldToCell(subject.Position);
            materials.FillTile(engulfed, MaterialId.Lava);
            materials.DeriveDirtyTiles();

            Check(grid.IsHazardous(engulfed), "lava landed on the worker's own tile");
        }

        // Long enough to notice (a quarter-second check) and walk one tile clear.
        if (frames == 140)
        {
            Vector2I now = grid.WorldToCell(subject.Position);

            Check(now != engulfed, "the worker left the tile the lava landed on", $"still at {now}");
            Check(!grid.IsHazardous(now), "the worker ended up somewhere not harmful", $"stopped at {now}");

            GD.Print($"--- {passed} passed, {failed} failed ---");
            GetTree().Quit();
        }
    }

    private void Check(bool condition, string name, string detail = "")
    {
        if (condition)
        {
            passed++;
            GD.Print($"  PASS  {name}");
            return;
        }

        failed++;
        GD.Print($"  FAIL  {name} {detail}");
    }
}
