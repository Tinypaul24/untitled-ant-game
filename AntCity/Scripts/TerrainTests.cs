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
    private MaterialWorld materials;

    private int passed;
    private int failed;

    public override void _Ready()
    {
        grid = GetNode<GridManager>("Main/GridManager");
        materials = GetNode<MaterialWorld>("Main/MaterialWorld");

        GD.Print("--- terrain tests ---");

        StartingWorldIsWalkable();
        MatterIsConserved();
        PilesPackIntoSolidGround();
        SleepBookkeepingStaysHonest();
        RoutesGoAroundHazards();
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

    private void MatterIsConserved()
    {
        Vector2I tile = grid.NestCenterCell;

        int before = Count(MaterialId.Sand);
        int placed = materials.EmitInto(tile, MaterialWorld.CellsPerTile, MaterialId.Sand);
        Settle();

        Check(placed > 0, "sand can be placed into an open tile", $"placed {placed}");
        Check(
            Count(MaterialId.Sand) - before == placed,
            "no sand is lost or duplicated while it falls",
            $"{placed} placed, {Count(MaterialId.Sand) - before} still exist"
        );
    }

    private void PilesPackIntoSolidGround()
    {
        Vector2I dropOff = grid.FindSpoilDropOff(grid.NestCenterCell);

        // Tip it beside the drop-off on the side away from the nest, exactly as a hauler does.
        int awayFromNest = dropOff.X < grid.NestCenterCell.X ? -1 : 1;
        Vector2I pile = dropOff + new Vector2I(awayFromNest, 0);

        // Earlier tests leave material of their own lying about, so measure the change rather than
        // the total - what matters is that tipping a load on the surface adds nothing to the tunnels.
        int undergroundBefore = CountUndergroundNear(pile);
        int fed = 0;

        for (int load = 0; load < 20; load++)
        {
            var carried = new List<MaterialId>();

            for (int i = 0; i < MaterialWorld.CellsPerTile; i++)
            {
                carried.Add(MaterialId.Sand);
            }

            fed += carried.Count;
            materials.Release(pile, carried);
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

        int spilled = CountUndergroundNear(pile) - undergroundBefore;

        Check(packed > 0, "dumped spoil packs back into solid ground", $"{packed} tiles solid from {fed} cells");
        Check(spilled <= 0, "tipping a load adds nothing to the tunnels", $"{spilled} cells went underground");
        Check(grid.IsStandable(grid.FindSpoilDropOff(grid.NestCenterCell)), "a drop-off is still reachable after dumping");
    }

    private int Count(MaterialId want)
    {
        int total = 0;

        foreach (var entry in materials.Chunks)
        {
            foreach (byte cell in entry.Value.Cells)
            {
                if ((MaterialId)cell == want)
                {
                    total++;
                }
            }
        }

        return total;
    }

    private int CountUndergroundNear(Vector2I pile)
    {
        int total = 0;

        for (int y = grid.SurfaceHeight; y < grid.SurfaceHeight + 16; y++)
        {
            for (int x = pile.X - 8; x <= pile.X + 8; x++)
            {
                Vector2I origin = MaterialWorld.TileToCellOrigin(new Vector2I(x, y));

                for (int cy = 0; cy < MaterialWorld.CellsPerTileAxis; cy++)
                {
                    for (int cx = 0; cx < MaterialWorld.CellsPerTileAxis; cx++)
                    {
                        MaterialKind kind = MaterialDatabase.Get(materials.GetCell(origin + new Vector2I(cx, cy))).Kind;

                        if (kind == MaterialKind.Powder || kind == MaterialKind.Liquid)
                        {
                            total++;
                        }
                    }
                }
            }
        }

        return total;
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

    // Runs the simulation until it settles, then pushes the result into the tile grid the way a
    // frame of play would.
    // The simulation decides what to tick from a maintained set of awake chunks rather than by
    // scanning them all, which is what makes an idle world free. The failure that buys is silent and
    // horrible: a chunk that is dirty but missing from the set never gets stepped, so material hangs
    // in mid-air and nothing points back at the bookkeeping. So check the two agree, both while
    // things are moving and once everything has settled.
    private void SleepBookkeepingStaysHonest()
    {
        Vector2I tile = grid.NestCenterCell;

        materials.EmitInto(tile, MaterialWorld.CellsPerTile, MaterialId.Sand);

        bool matchedWhileMoving = true;

        for (int tick = 0; tick < 30; tick++)
        {
            materials.Simulation.Step();
            matchedWhileMoving &= materials.AwakeSetMatchesChunks();
        }

        Check(matchedWhileMoving, "the awake set tracks the chunks while material is moving");

        Settle();

        Check(materials.AwakeSetMatchesChunks(), "the awake set tracks the chunks once everything settles");

        // Not "nothing is awake": a heat source legitimately never sleeps, because it has to keep
        // pushing warmth into its neighbours, and one stray lava cell from an earlier test is enough
        // to keep its chunk ticking forever. That is by design and it is bounded - one lava body,
        // one awake chunk. What must not happen is a chunk staying awake with nothing active in it,
        // which is what a leak in the wake bookkeeping would look like.
        int idleButAwake = 0;

        foreach (KeyValuePair<Vector2I, MaterialChunk> entry in materials.Chunks)
        {
            if (entry.Value.Awake && !HoldsAHeatSource(entry.Value))
            {
                idleButAwake++;
            }
        }

        Check(idleButAwake == 0, "nothing stays awake unless something in it is still active",
            $"{idleButAwake} chunks awake with nothing happening in them");
    }

    // Ants must not path through anything harmful, and must be able to get out of it if it arrives
    // around them. Both directions matter: refusing to route into danger is useless on its own if it
    // also traps a worker who is already standing in a flow, because the rule that keeps her out is
    // the same rule that would forbid every route she could leave by.
    private void RoutesGoAroundHazards()
    {
        Vector2I nest = grid.NestCenterCell;

        // Built on the nest-to-surface run rather than a corridor dug for the occasion, because that
        // route is already proven walkable by the first test - a hand-dug one has to satisfy the
        // no-climbing rules itself, and getting that subtly wrong tests the setup, not the hazard.
        Vector2I from = nest;
        Vector2I to = grid.FindSpoilDropOff(nest);

        List<Vector2I> before = grid.FindTunnelPath(from, to);

        Check(before != null && before.Count > 2, "the test route is walkable before any lava");

        if (before == null || before.Count <= 2)
        {
            return;
        }

        Vector2I blocked = before[before.Count / 2];
        materials.FillTile(blocked, MaterialId.Lava);

        Check(grid.IsHazardous(blocked), "a lava-filled tile reads as hazardous");
        Check(!grid.IsSafelyStandable(blocked), "nothing is safely standable in lava");
        Check(grid.IsStandable(blocked), "lava does not make the floor itself vanish");

        // A null route is a pass, not a failure: if the only way through is the flooded cell, then
        // there genuinely is no way through, and saying so is better than marching her into it.
        List<Vector2I> after = grid.FindTunnelPath(from, to);

        Check(after == null || !after.Contains(blocked), "no route is planned through lava");

        // And the way out. Standing in it, she has to be offered somewhere better to go.
        Vector2I refuge = grid.FindNearestSafeCell(blocked);

        Check(refuge != blocked, "an ant caught in lava is offered a way out", $"refuge was {refuge}");
        Check(grid.IsSafelyStandable(refuge), "the way out leads somewhere actually safe");

        materials.ClearTile(blocked);
        materials.DeriveDirtyTiles();
    }

    private static bool HoldsAHeatSource(MaterialChunk chunk)
    {
        foreach (byte cell in chunk.Cells)
        {
            if (MaterialDatabase.Get((MaterialId)cell).HeatOutput != 0f)
            {
                return true;
            }
        }

        return false;
    }

    private void Settle()
    {
        const int MaxTicks = 400;

        for (int tick = 0; tick < MaxTicks; tick++)
        {
            materials.Simulation.Step();
        }

        materials.DeriveDirtyTiles();
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
