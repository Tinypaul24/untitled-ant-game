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
        SaveRoundTripRebuildsTheWorld();

        GD.Print($"--- {passed} passed, {failed} failed ---");
    }

    private void StartingWorldIsWalkable()
    {
        Vector2I nest = grid.NestCenterCell;

        Check(grid.IsStandable(nest), "nest floor is standable");
        // Spoil has no fixed home any more - a hauler searches the burrow she can walk and takes her
        // load to the highest point in it, which should be out on the surface.
        Vector2I dropOff = grid.FindSpoilDropOff(nest);

        Check(grid.IsStandable(dropOff), "spoil drop-off is standable");
        Check(dropOff.Y <= grid.SurfaceHeight - 1, "spoil drop-off is at or above the surface", $"landed at {dropOff}");

        List<Vector2I> out_ = grid.FindTunnelPath(nest, dropOff);
        List<Vector2I> back = grid.FindTunnelPath(dropOff, nest);

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
        // The simulation ships switched off while the colony is being balanced, so switch it on for
        // the duration - these tests are about whether it still works when it is on.
        field.Enabled = true;

        Vector2I dropOff = grid.FindSpoilDropOff(grid.NestCenterCell);

        // Tip it beside her on the side away from the nest, exactly as a hauler does.
        int awayFromNest = dropOff.X < grid.NestCenterCell.X ? -1 : 1;
        Vector2I pile = dropOff + new Vector2I(awayFromNest, 0);

        // Earlier tests leave grains of their own lying about, so measure the change rather than the
        // total - what matters is that tipping a load on the surface adds nothing to the tunnels.
        int undergroundBefore = CountUndergroundGrainsNear(pile);
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

        for (int y = 0; y < grid.SurfaceHeight; y++)
        {
            for (int x = pile.X - 8; x <= pile.X + 8; x++)
            {
                if (grid.GetTileAt(new Vector2I(x, y)) == GridManager.TileType.Dirt)
                {
                    packed++;
                }
            }
        }

        int spilledUnderground = CountUndergroundGrainsNear(pile) - undergroundBefore;

        Check(packed > 0, "dumped spoil packs back into solid ground", $"{packed} cells packed from {fed} grains");
        Check(spilledUnderground <= 0, "tipping a load adds no spoil to the tunnels", $"{spilledUnderground} grains went underground");
        Check(grid.IsStandable(grid.FindSpoilDropOff(grid.NestCenterCell)), "a drop-off is still reachable after dumping");
    }

    private int CountUndergroundGrainsNear(Vector2I pile)
    {
        int count = 0;

        for (int y = grid.SurfaceHeight; y < grid.SurfaceHeight + 16; y++)
        {
            for (int x = pile.X - 8; x <= pile.X + 8; x++)
            {
                Vector2I origin = new Vector2I(x, y) * ParticleField.SlotsPerCellAxis;

                for (int sx = 0; sx < ParticleField.SlotsPerCellAxis; sx++)
                {
                    for (int sy = 0; sy < ParticleField.SlotsPerCellAxis; sy++)
                    {
                        if (field.IsSettledSlot(origin + new Vector2I(sx, sy)))
                        {
                            count++;
                        }
                    }
                }
            }
        }

        return count;
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
            Check(grid.FindTunnelPath(cursor, grid.FindSpoilDropOff(cursor)) != null, $"digger at {offset} can get home");
        }
    }

    private void SaveRoundTripRebuildsTheWorld()
    {
        Vector2I nest = grid.NestCenterCell;

        for (int dx = -6; dx <= 6; dx++)
        {
            Vector2I target = FindDiggableNear(nest + new Vector2I(dx, 2));
            grid.Dig(target);
            grid.DigGrain(FindDiggableNear(nest + new Vector2I(dx, 3)));
        }

        foreach (Vector2I foodCell in grid.GetFoodSourceCells())
        {
            grid.Harvest(foodCell, 1);
            break;
        }

        var expectedTiles = new Dictionary<Vector2I, GridManager.TileType>();
        var expectedGrains = new Dictionary<Vector2I, int>();

        for (int x = nest.X - 40; x <= nest.X + 40; x++)
        {
            for (int y = 0; y <= nest.Y + 20; y++)
            {
                var cell = new Vector2I(x, y);
                expectedTiles[cell] = grid.GetTileAt(cell);
                expectedGrains[cell] = grid.GetGrainsRemoved(cell);
            }
        }

        var expectedFood = new Dictionary<Vector2I, int>();

        foreach (Vector2I cell in grid.GetFoodSourceCells())
        {
            expectedFood[cell] = grid.GetFoodAmount(cell);
        }

        WorldSave save = grid.CaptureState();
        grid.RestoreState(save);

        int wrongTiles = 0;
        int wrongGrains = 0;

        foreach (var entry in expectedTiles)
        {
            if (grid.GetTileAt(entry.Key) != entry.Value)
            {
                wrongTiles++;
            }

            if (grid.GetGrainsRemoved(entry.Key) != expectedGrains[entry.Key])
            {
                wrongGrains++;
            }
        }

        int wrongFood = 0;

        foreach (var entry in expectedFood)
        {
            if (grid.GetFoodAmount(entry.Key) != entry.Value)
            {
                wrongFood++;
            }
        }

        Check(wrongTiles == 0, "every tile survives a save and reload", $"({wrongTiles} of {expectedTiles.Count} differ)");
        Check(wrongGrains == 0, "part-dug cells keep their progress", $"({wrongGrains} differ)");
        Check(wrongFood == 0, "food sources keep what is left in them", $"({wrongFood} of {expectedFood.Count} differ)");
        Check(grid.GetFoodSourceCells().Count == expectedFood.Count, "no food source is invented or lost");

        WorldSave resaved = grid.CaptureState();
        Check(resaved.Seed == save.Seed, "the reloaded world keeps its seed");
        Check(resaved.ModifiedCells.Count == save.ModifiedCells.Count, "a reloaded world re-saves the same changes",
            $"({save.ModifiedCells.Count / 3} before, {resaved.ModifiedCells.Count / 3} after)");
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
