using Godot;
using System.Collections.Generic;

// Checks the terrain rules that are easy to break and impossible to eyeball: that dug material is
// conserved, that piles pack back into solid ground, and above all that anywhere an ant can walk to
// it can also walk back from. The no-climbing rule makes stranding a one-way failure, so these run
// against a real generated world rather than a fixture.
//
// Run with:
//   godot --headless --path . res://AntCity/Scenes/Tests.tscn --quit-after 3
public partial class TerrainTests : Node
{
    private GridManager grid;
    private ParticleField field;

    private int passed;
    private int failed;

    public override void _Ready()
    {
        grid = GetNode<GridManager>("Main/GridManager");
        field = GetNode<ParticleField>("Main/ParticleField");

        GD.Print("--- terrain tests ---");

        StartingWorldIsWalkable();
        GrainsAreConserved();
        PilesPackIntoSolidGround();
        DugCorridorsStayWalkable();

        GD.Print($"--- {passed} passed, {failed} failed ---");
    }

    private void StartingWorldIsWalkable()
    {
        Vector2I nest = grid.NestCenterCell;

        Check(grid.IsStandable(nest), "nest floor is standable");
        Check(grid.IsStandable(grid.SpoilDumpCell), "spoil dump is standable");

        List<Vector2I> out_ = grid.FindTunnelPath(nest, grid.SpoilDumpCell);
        List<Vector2I> back = grid.FindTunnelPath(grid.SpoilDumpCell, nest);

        Check(out_ != null, "nest reaches the surface");
        Check(back != null, "surface reaches the nest");
        Check(out_ == null || IsWalkableRoute(out_), "route out never moves straight up or down");

        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is AntWorker ant)
            {
                Check(grid.IsStandable(grid.WorldToCell(ant.Position)), $"ant spawned somewhere it can stand");
            }
        }
    }

    private void GrainsAreConserved()
    {
        Vector2I open = grid.NestCenterCell;
        int before = field.SettledGrainCount;

        int emitted = field.Emit(open, 20, GridManager.TileType.Dirt);
        Settle();

        Check(emitted == 20, "all 20 grains found room", $"placed {emitted}");
        Check(
            field.SettledGrainCount - before == emitted && field.FallingGrainCount == 0,
            "every grain came to rest",
            $"settled {field.SettledGrainCount - before} of {emitted}, {field.FallingGrainCount} still falling"
        );
    }

    private void PilesPackIntoSolidGround()
    {
        Vector2I pile = grid.SpoilPileCell;
        int fed = 0;

        for (int load = 0; load < 20; load++)
        {
            List<GridManager.TileType> carried = new List<GridManager.TileType>();

            for (int i = 0; i < ParticleField.SlotsPerCell; i++)
            {
                carried.Add(GridManager.TileType.Dirt);
            }

            fed += carried.Count;
            field.Release(pile, carried);
            Settle();
        }

        int packed = 0;

        for (int y = 0; y <= grid.SurfaceHeight; y++)
        {
            for (int x = pile.X - 4; x <= pile.X + 4; x++)
            {
                if (grid.GetTileAt(new Vector2I(x, y)) == GridManager.TileType.Dirt && y < grid.SurfaceHeight)
                {
                    packed++;
                }
            }
        }

        Check(packed > 0, "dumped spoil packs back into solid ground", $"{packed} cells packed from {fed} grains");
        Check(grid.IsStandable(grid.SpoilDumpCell), "the dump never seals itself shut");
    }

    private void DugCorridorsStayWalkable()
    {
        Vector2I nest = grid.NestCenterCell;

        Vector2I[] targets =
        {
            new Vector2I(0, 15),
            new Vector2I(5, 8),
            new Vector2I(-7, 10),
            new Vector2I(2, 22),
            new Vector2I(14, 4),
            new Vector2I(-3, 28),
            new Vector2I(20, 12),
            new Vector2I(-18, 6),
            new Vector2I(1, 34),
            new Vector2I(-9, 19)
        };

        foreach (Vector2I offset in targets)
        {
            // Only diggable targets are reachable in play - both the build tray and click-to-dig gate
            // on CanDig - so nudge onto solid diggable ground rather than testing an impossible order.
            Vector2I target = FindDiggableNear(nest + offset);
            List<Vector2I> plan = grid.PlanDigRoute(nest, target);

            if (plan == null)
            {
                Check(false, $"dig to {offset} can be planned");
                continue;
            }

            // Mirror what a worker actually does: walk the planned corridor, then break into the
            // target with the same router she uses - and only move into what she opened if there is
            // a floor in there, exactly as OnDigTimeout now decides.
            Vector2I cursor = nest;

            foreach (Vector2I cell in plan)
            {
                grid.Dig(cell);

                if (grid.IsStandable(cell))
                {
                    cursor = cell;
                }
            }

            for (int step = 0; step < 16 && grid.CanDig(target); step++)
            {
                Vector2I next = grid.GetStepToward(cursor, target);
                grid.Dig(next);

                if (grid.IsStandable(next))
                {
                    cursor = next;
                }
            }

            Check(!grid.CanDig(target), $"dig to {offset} opens its target");
            Check(grid.IsStandable(cursor), $"digger at {offset} ends on solid footing");
            Check(grid.FindTunnelPath(cursor, grid.SpoilDumpCell) != null, $"digger at {offset} can get home");
        }
    }

    private Vector2I FindDiggableNear(Vector2I wanted)
    {
        for (int radius = 0; radius < 6; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    Vector2I candidate = wanted + new Vector2I(dx, dy);

                    if (grid.CanDig(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return wanted;
    }

    private bool IsWalkableRoute(List<Vector2I> route)
    {
        for (int i = 1; i < route.Count; i++)
        {
            if (route[i].X == route[i - 1].X)
            {
                return false;
            }
        }

        return true;
    }

    private void Settle()
    {
        const int MaxTicks = 400;

        for (int tick = 0; tick < MaxTicks && field.FallingGrainCount > 0; tick++)
        {
            field.Advance();
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
