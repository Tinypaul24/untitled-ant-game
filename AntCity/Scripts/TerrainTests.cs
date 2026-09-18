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

        // The colony normally spends its first few seconds flying in and digging. Tests want the
        // world that leaves behind, not the arrival.
        GetNode<ColonyFounding>("Main/ColonyFounding").CompleteNow();

        GD.Print("--- terrain tests ---");

        StartingWorldIsWalkable();
        MatterIsConserved();
        PilesPackIntoSolidGround();
        SleepBookkeepingStaysHonest();
        RoutesGoAroundHazards();
        ChippingEatsAWholeTile();
        GrassBurnsAndStaysOnTheSurface();
        DiggingLeavesCleanTunnels();
        BoredTunnelsLeaveNothingHanging();
        SpoilIsWhatTheTileIsMadeOf();
        ReleaseHandsBackWhatItCannotPlace();
        DiggingInTheSkyLeavesSky();
        NestWallsCementThemselves();
        RoomsCanBePlacedAndFinished();
        RoomsNeedWallsAroundThem();
        RoomWallsCementThemselves();
        AnIdleAntAlwaysFindsSomethingToDo();
        PullingDownARoomDoesNotStrandItsBuilder();
        ReleasingAClaimKeepsTheJob();
        ARoomGivesUpOnGroundThatTurnedToRock();
        SmoothedRoutesStayWalkable();
        RoutesDoNotBobUpAndDown();
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
        // The open air just above the colony. The nest cell itself is turf now that the game starts
        // on the surface, and you cannot pour sand into solid ground.
        Vector2I tile = grid.NestCenterCell + new Vector2I(0, -1);

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
            Check(grid.FindTunnelPath(cursor, grid.FindNearestSurfaceStanding(cursor)) != null, $"digger at {offset} can get home");
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

    // A diggable tile with solid earth over it, including diagonally - the condition under which a
    // bore keeps its ceiling.
    private Vector2I FindBuriedDiggableNear(Vector2I wanted)
    {
        for (int radius = 0; radius < 8; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    Vector2I candidate = wanted + new Vector2I(dx, dy);

                    if (grid.CanDig(candidate) &&
                        grid.CanDig(candidate + new Vector2I(-1, -1)) &&
                        grid.CanDig(candidate + new Vector2I(0, -1)) &&
                        grid.CanDig(candidate + new Vector2I(1, -1)))
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
        Vector2I to = grid.FindNearestSurfaceStanding(nest, 8);

        // Expanded to the cells the route actually crosses. Routes are simplified down to their
        // corners now, so the returned list is no longer every cell she walks over - a mid-point
        // taken from it straight would often be an endpoint, and "does the route contain the lava
        // tile" would miss a segment that passes straight over it.
        List<Vector2I> before = ExpandRoute(grid.FindTunnelPath(from, to));

        Check(before != null && before.Count > 2, "the test route is walkable before any lava",
            $"{before?.Count ?? 0} cells crossed");

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
        List<Vector2I> after = ExpandRoute(grid.FindTunnelPath(from, to));

        Check(after == null || !after.Contains(blocked), "no route is planned through lava");

        // And the way out. Standing in it, she has to be offered somewhere better to go.
        Vector2I refuge = grid.FindNearestSafeCell(blocked);

        Check(refuge != blocked, "an ant caught in lava is offered a way out", $"refuge was {refuge}");
        Check(grid.IsSafelyStandable(refuge), "the way out leads somewhere actually safe");

        materials.ClearTile(blocked);
        materials.DeriveDirtyTiles();
    }

    // Chipping a tile away grain by grain, which is what an ant digging actually does.
    //
    // This existed only in the running game before, and a table sized for the old cell resolution
    // read off the end of itself the first time a worker swung at a wall - every test passed and the
    // game threw on the first dig. Digging is the single most common thing that happens here, so it
    // gets a test of its own.
    private void ChippingEatsAWholeTile()
    {
        Vector2I tile = FindDiggableNear(grid.NestCenterCell + new Vector2I(0, 6));

        if (!grid.CanDig(tile))
        {
            Check(false, "a diggable tile could be found to chip");
            return;
        }

        int before = CountSolidCells(tile);
        int guard = 0;

        // Grain by grain until the cell opens, exactly as OnDigTimeout drives it.
        while (grid.CanDig(tile) && guard++ < GridManager.GrainsPerCell * 4)
        {
            grid.DigGrain(tile);
        }

        materials.DeriveDirtyTiles();

        // The channel, not the whole tile. A bored tunnel deliberately keeps a lip of earth
        // overhead so the corridor is the size of the ant rather than the size of the tile - what
        // has to be empty is the part she walks through.
        int channelLeft = CountSolidCells(tile, MaterialWorld.MaxCeilingCellRows);
        int wholeTile = CountSolidCells(tile, 0);

        Check(before > 0, "the tile started out solid", $"{before} solid cells");
        Check(channelLeft == 0, "chipping clears the whole channel", $"{channelLeft} cells left");
        Check(
            wholeTile <= MaterialWorld.MaxCeilingCellRows * MaterialWorld.CellsPerTileAxis,
            "chipping leaves nothing but the ceiling",
            $"{wholeTile} cells left");
        Check(grid.IsTunnel(tile), "a fully chipped tile reads as open ground");
        Check(guard <= GridManager.GrainsPerCell * 4, "the tile opened in a sane number of grains");
    }

    private int CountSolidCells(Vector2I tile, int fromRow = 0)
    {
        Vector2I origin = MaterialWorld.TileToCellOrigin(tile);
        int solid = 0;

        for (int y = fromRow; y < MaterialWorld.CellsPerTileAxis; y++)
        {
            for (int x = 0; x < MaterialWorld.CellsPerTileAxis; x++)
            {
                if (!MaterialDatabase.Get(materials.GetCell(origin + new Vector2I(x, y))).IsAir)
                {
                    solid++;
                }
            }
        }

        return solid;
    }

    // Grass burns, and grass creeps back over bare soil - but only where daylight reaches.
    //
    // That last part is the whole reason reactions grew conditions. As a plain material pair, grass
    // spreading onto dirt spreads onto every dirt cell it touches, and every dirt cell touches
    // another one all the way down: left alone it turns the entire world to turf. This checks the
    // gate holds, because the failure is slow enough that you would not notice it until the map was
    // green to the bedrock.
    private void GrassBurnsAndStaysOnTheSurface()
    {
        Vector2I surface = new Vector2I(grid.NestCenterCell.X + 30, grid.SurfaceHeight - 1);
        Vector2I origin = MaterialWorld.TileToCellOrigin(surface);

        materials.FillTile(surface, MaterialId.Grass);
        materials.SetCell(origin, MaterialId.Fire);

        for (int tick = 0; tick < 240; tick++)
        {
            materials.Simulation.Step();
        }

        Check(CountCellsIn(surface, MaterialId.Grass) < MaterialWorld.CellsPerTile,
            "fire eats into grass", $"{CountCellsIn(surface, MaterialId.Grass)} cells left of {MaterialWorld.CellsPerTile}");

        // Deep underground, a lone patch of turf against soil must not spread.
        Vector2I deep = new Vector2I(grid.NestCenterCell.X + 34, grid.SurfaceHeight + 20);

        materials.FillTile(deep, MaterialId.Grass);

        int before = CountGrassAround(deep, 3);

        for (int tick = 0; tick < 600; tick++)
        {
            materials.Simulation.Step();
        }

        int after = CountGrassAround(deep, 3);

        Check(after <= before, "grass does not spread underground", $"{before} cells became {after}");

        materials.ClearTile(surface);
        materials.ClearTile(deep);
        materials.DeriveDirtyTiles();
    }

    private int CountCellsIn(Vector2I tile, MaterialId want, int fromRow = 0)
    {
        Vector2I origin = MaterialWorld.TileToCellOrigin(tile);
        int found = 0;

        for (int y = fromRow; y < MaterialWorld.CellsPerTileAxis; y++)
        {
            for (int x = 0; x < MaterialWorld.CellsPerTileAxis; x++)
            {
                if (materials.GetCell(origin + new Vector2I(x, y)) == want)
                {
                    found++;
                }
            }
        }

        return found;
    }

    private int CountGrassAround(Vector2I tile, int radius)
    {
        int found = 0;

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                found += CountCellsIn(tile + new Vector2I(x, y), MaterialId.Grass);
            }
        }

        return found;
    }

    // A dug corridor has to come out empty and stay walkable.
    //
    // Digging used to shake the surrounding soil loose so it slumped into the new tunnel. With spoil
    // hauling off there was nowhere for it to go and it settled on the floor as scattered blocks of
    // earth - twenty-six cells of litter in an eight-tile corridor, in every corridor, forever.
    private void DiggingLeavesCleanTunnels()
    {
        Vector2I start = grid.NestCenterCell + new Vector2I(60, 10);
        Vector2I floor = FindDiggableNear(start);

        // A corridor dug the way a worker digs it, so the same signals fire.
        for (int x = 0; x < 8; x++)
        {
            grid.Dig(floor + new Vector2I(x, 0));
        }

        materials.DeriveDirtyTiles();
        Settle();

        // A dug tunnel comes out empty. Nothing should be left lying in it - no loose grains, no
        // scattered blocks of soil that slumped in and settled on the floor.
        //
        // Measured over the channel only. The lip of earth a bore leaves overhead is the tunnel's
        // ceiling, not litter in it.
        int litter = 0;

        for (int x = 0; x < 8; x++)
        {
            litter += CountCellsIn(floor + new Vector2I(x, 0), MaterialId.Dirt, MaterialWorld.MaxCeilingCellRows)
                + CountCellsIn(floor + new Vector2I(x, 0), MaterialId.LooseDirt, MaterialWorld.MaxCeilingCellRows);
        }

        Check(litter == 0, "a dug tunnel is left empty",
            $"{litter} cells of soil still in the corridor");

        // The corridor has to still be a corridor. Walkable, not merely non-solid: loose soil piling
        // on the floor could leave a tile technically open and impossible to walk along.
        int walkable = 0;

        for (int x = 0; x < 8; x++)
        {
            if (grid.IsStandable(floor + new Vector2I(x, 0)))
            {
                walkable++;
            }
        }

        Check(walkable >= 6, "a dug corridor is still walkable once the soil settles",
            $"{walkable} of 8 tiles standable");

    }

    // What a digger scrapes out has to survive the digging.
    //
    // The spoil material used to be sampled from the cell at the middle of the tile, and chipping
    // clears cells in Bayer-dither order where that cell is the sixteenth of sixty-four visited -
    // so it was already gone by the first or second of the four chips a tile takes, and every chip
    // after that read back Air. Half of every tile's spoil evaporated before anybody carried it.
    private void SpoilIsWhatTheTileIsMadeOf()
    {
        Vector2I tile = FindDiggableNear(grid.NestCenterCell + new Vector2I(-70, 9));

        if (!grid.CanDig(tile))
        {
            Check(false, "a diggable tile could be found to scrape");
            return;
        }

        Check(materials.SpoilFor(tile) == MaterialId.LooseDirt,
            "an untouched tile yields loose soil", $"{materials.SpoilFor(tile)}");

        // Three of the four grains gone - the state the old sampling read back as empty air.
        for (int grain = 0; grain < GridManager.GrainsPerCell - 1; grain++)
        {
            grid.DigGrain(tile);
        }

        Check(materials.SpoilFor(tile) == MaterialId.LooseDirt,
            "a tile three-quarters dug still yields loose soil", $"{materials.SpoilFor(tile)}");

        // And loose soil, not packed earth. Dirt is Solid on purpose, so a heap of it would stand
        // up in mid-air as a stack of cubes instead of slumping into a cone.
        Check(MaterialDatabase.Get(MaterialId.LooseDirt).Kind == MaterialKind.Powder,
            "spoil is a powder, so a tipped load slumps");

        grid.Dig(tile);

        Check(materials.SpoilFor(tile) == MaterialId.Air,
            "an opened tile has nothing left to scrape", $"{materials.SpoilFor(tile)}");
    }

    // A load that will not fit stays on her back.
    //
    // Release used to return void and clear the list whatever happened, so every grain it could not
    // place - because the column was full, or because it walked off the top of the world - was
    // deleted with no accounting. Matter conservation here is checked to the cell elsewhere in this
    // file, and this was a hole straight through it.
    private void ReleaseHandsBackWhatItCannotPlace()
    {
        Vector2I solid = FindBuriedDiggableNear(grid.NestCenterCell + new Vector2I(-74, 14));

        var carried = new List<MaterialId>();

        for (int i = 0; i < 8; i++)
        {
            carried.Add(MaterialId.LooseDirt);
        }

        int before = Count(MaterialId.LooseDirt);
        int leftover = materials.Release(solid, carried);

        Check(leftover == 8, "a load tipped into solid ground is handed straight back", $"{leftover} of 8");
        Check(carried.Count == 8, "and is still in her jaws", $"{carried.Count} grains");
        Check(Count(MaterialId.LooseDirt) == before, "nothing was created on the way",
            $"{before} became {Count(MaterialId.LooseDirt)}");

        // The other half of the bargain: into open sky it all goes, and the list comes back empty.
        Vector2I sky = new Vector2I(grid.NestCenterCell.X - 74, 4);

        int placedBefore = Count(MaterialId.LooseDirt);
        int stillHeld = materials.Release(sky, carried);

        Check(stillHeld == 0, "a load tipped into open sky all lands", $"{stillHeld} left over");
        Check(carried.Count == 0, "and her jaws come back empty", $"{carried.Count} grains");
        Check(Count(MaterialId.LooseDirt) == placedBefore + 8, "every grain is accounted for",
            $"{placedBefore} became {Count(MaterialId.LooseDirt)}");
    }

    // Digging into a spoil heap leaves sky, not tunnel.
    //
    // Dig wrote Tunnel unconditionally, which above the surface is both wrong and self-perpetuating:
    // the next spoil to land in that tile makes the simulation report a blocked passage, which
    // queues a dig job, which opens it again, which lets more spoil in.
    private void DiggingInTheSkyLeavesSky()
    {
        Vector2I sky = new Vector2I(grid.NestCenterCell.X - 78, grid.SurfaceHeight - grid.GrassDepth - 3);

        // Pile enough soil into it to make it solid ground, the way a spoil heap does.
        materials.FillTile(sky, MaterialId.LooseDirt);
        materials.DeriveDirtyTiles();

        Check(!grid.IsTunnel(sky), "a tile full of spoil reads as solid ground",
            $"{grid.GetTileAt(sky)}");

        int obstructions = 0;
        void CountObstruction(Vector2I cell) => obstructions++;

        grid.TileObstructed += CountObstruction;

        grid.Dig(sky);

        Check(grid.GetTileAt(sky) == GridManager.TileType.Air,
            "digging out a heap above the surface leaves sky", $"{grid.GetTileAt(sky)}");

        // Refill it. Nothing should report a blocked passage, because there was never a corridor.
        materials.FillTile(sky, MaterialId.LooseDirt);
        materials.DeriveDirtyTiles();

        grid.TileObstructed -= CountObstruction;

        Check(obstructions == 0, "spoil settling back on a heap is not a blocked passage",
            $"{obstructions} obstructions reported");
    }

    // A bored tunnel keeps a lip of earth overhead so the channel is the size of an ant. This is
    // the failure that design most easily produces, and one the player has already been shown once:
    // dig the tile above and the lip has nothing left to hang from, so it reads as a block of soil
    // floating in mid-cavity.
    private void BoredTunnelsLeaveNothingHanging()
    {
        Vector2I floor = FindDiggableNear(grid.NestCenterCell + new Vector2I(-60, 10));

        // Lower row first, then the row above it - the order that turns a ceiling into a slab.
        for (int x = 0; x < 4; x++)
        {
            grid.Dig(floor + new Vector2I(x, 0));
        }

        for (int x = 0; x < 4; x++)
        {
            grid.Dig(floor + new Vector2I(x, -1));
        }

        materials.DeriveDirtyTiles();

        int hanging = 0;

        for (int x = 0; x < 4; x++)
        {
            Vector2I tile = floor + new Vector2I(x, 0);

            hanging += CountSolidCells(tile) - CountSolidCells(tile, MaterialWorld.MaxCeilingCellRows);
        }

        Check(hanging == 0, "nothing is left hanging under an opened tile", $"{hanging} cells");

        // The other half of the bargain: a corridor with solid ground over it keeps its ceiling and
        // still has to read as open ground, or the tile derives back to earth and the dig loops.
        //
        // Genuinely buried, rather than assumed to be. The surface is noise, so "fourteen tiles
        // below the nest" is open sky at some values of x - which is how this first failed.
        Vector2I lone = FindBuriedDiggableNear(grid.NestCenterCell + new Vector2I(70, 14));

        if (!grid.CanDig(lone))
        {
            Check(false, "a buried tile could be found to bore");
            return;
        }

        grid.Dig(lone);
        materials.DeriveDirtyTiles();

        int kept = CountSolidCells(lone) - CountSolidCells(lone, MaterialWorld.MaxCeilingCellRows);

        Check(grid.IsTunnel(lone), "a bored tile reads as open ground");
        Check(
            CountSolidCells(lone, MaterialWorld.MaxCeilingCellRows) == 0,
            "a bored tile's channel is empty",
            $"{CountSolidCells(lone, MaterialWorld.MaxCeilingCellRows)} cells in the way");
        Check(kept > 0, "a bored tile under solid ground keeps a ceiling", $"{kept} cells kept");
    }

    // Ants cement the walls they live behind. Slow on purpose, so this runs the clock rather than
    // expecting it to have happened already.
    private void NestWallsCementThemselves()
    {
        Vector2I nest = grid.NestCenterCell;

        // Somewhere for them to plaster: a chamber just below the landing site.
        for (int x = -3; x <= 3; x++)
        {
            for (int y = 2; y <= 3; y++)
            {
                grid.Dig(nest + new Vector2I(x, y));
            }
        }

        materials.DeriveDirtyTiles();

        int before = CountAround(nest, 8, MaterialId.HardenedDirt);

        // The hardening sweep is driven from _Process, so give it real frames rather than ticks.
        for (int pass = 0; pass < 400; pass++)
        {
            materials.CementWallsForTest(0.25);
        }

        int after = CountAround(nest, 8, MaterialId.HardenedDirt);

        Check(after > before, "ants cement the walls around the nest", $"{before} became {after}");

        // Cemented earth is the point of cementing earth: shaking it must not turn it back to soil.
        //
        // Counted over a patch that is deliberately not dug through, because digging a tile clears
        // its cells outright - measuring across the excavation would just count the earth removed by
        // the shovel and call it a failure.
        Vector2I witness = nest + new Vector2I(-6, 3);
        int hardenedBefore = CountAround(witness, 1, MaterialId.HardenedDirt);

        grid.Dig(nest + new Vector2I(-3, 4));
        grid.Dig(nest + new Vector2I(-4, 4));

        Check(CountAround(witness, 1, MaterialId.HardenedDirt) >= hardenedBefore,
            "hardened earth does not shake loose when you dig beside it",
            $"{hardenedBefore} became {CountAround(witness, 1, MaterialId.HardenedDirt)}");
    }

    // Rooms are the whole progression: they are the only thing that raises the colony's ceilings.
    // None of this was covered, and four separate faults had stacked up in it unnoticed - a footprint
    // containing any rock was rejected, oversized drags were rejected, both silently; excavation
    // never completed because the cells reported as dug through a signal that no longer fired; and
    // furnishing was starved because foraging is checked first and practically never fails.
    private void RoomsCanBePlacedAndFinished()
    {
        var build = GetNode<BuildManager>("Main/BuildManager");
        var colony = GetNode<ColonyManager>("Main/ColonyManager");

        Vector2I origin = grid.NestCenterCell + new Vector2I(80, 8);

        // Clear ground to build on, and a rock in the middle of it. Rooms have to cope with stone
        // inside the outline rather than refusing the whole placement.
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                grid.Dig(origin + new Vector2I(x, y));
            }
        }

        grid.SetTileFromSimulation(origin + new Vector2I(3, 0), GridManager.TileType.Rock);
        grid.SetTileFromSimulation(origin + new Vector2I(3, 1), GridManager.TileType.Rock);

        int capacityBefore = colony.Capacity;
        colony.AddFood(200);

        build.TryCreateRoom(new Rect2I(origin, new Vector2I(4, 2)), BuildingType.NestingChamber);

        Room placed = null;

        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is Room room && room.Footprint.Position == origin)
            {
                placed = room;
            }
        }

        Check(placed != null, "a room can be placed on ground that contains rock");

        if (placed == null)
        {
            return;
        }

        // Counted from the terrain rather than assumed. Generation puts rock wherever it likes, so
        // the two cells forced to stone above are a minimum, not the total.
        int rock = 0;

        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                if (grid.GetTileAt(origin + new Vector2I(x, y)) == GridManager.TileType.Rock)
                {
                    rock++;
                }
            }
        }

        Check(rock >= 2, "the test patch really does contain rock", $"{rock} rock cells");
        Check(placed.CellCount == 8 - rock, "rock inside the outline is not counted as part of the room",
            $"claimed {placed.CellCount} cells with {rock} of eight under stone");

        // Everything it needs was already open, so it should be past excavating immediately.
        Check(placed.State != Room.RoomState.Excavating, "a room dug out in advance needs no further excavation",
            $"state is {placed.State}");

        // Furnishing is what an ant does on arrival; drive it directly rather than waiting on one.
        build.ReportFurnishDone(placed);

        Check(placed.State == Room.RoomState.Active, "furnishing activates the room");
        Check(colony.Capacity > capacityBefore, "an activated nesting chamber raises the population cap",
            $"{capacityBefore} -> {colony.Capacity}");

        // A room only owns cells it can actually use, and only draws and charges for those. Asserted
        // against the cell set rather than the count, because the count is what used to be right
        // while the drawing was still painting the whole bounding box.
        bool ownsOnlyUsable = true;

        foreach (Vector2I cell in placed.OwnedCells)
        {
            if (grid.GetTileAt(cell) == GridManager.TileType.Rock)
            {
                ownsOnlyUsable = false;
            }
        }

        Check(ownsOnlyUsable, "a room owns no cell that is solid rock");
        Check(placed.Contains(placed.StandCell), "the cell a furnisher is sent to belongs to the room");

        RoomsCanBePulledDownAgain(build, colony, placed, capacityBefore);
    }




    // The job board must not lose work orders.
    //
    // ReleaseClaim used to drop the cell from the obstruction set as well as the claim set, which
    // destroys the work order rather than handing it back - and TileObstructed only fires on a
    // walkable-to-blocked transition, so nothing ever re-created it. Any redirect of the claiming
    // ant permanently deleted the only record that a corridor had caved in.
    private void ReleasingAClaimKeepsTheJob()
    {
        var build = GetNode<BuildManager>("Main/BuildManager");

        // A corridor cell, then filled in under her - exactly what settling spoil does.
        Vector2I corridor = FindDiggableNear(grid.NestCenterCell + new Vector2I(-158, 12));
        grid.Dig(corridor);
        materials.DeriveDirtyTiles();

        int before = build.ObstructionCount;
        int claimsBefore = build.ClaimedDigCellCount;

        grid.SetTileFromSimulation(corridor, GridManager.TileType.Dirt);

        Check(build.ObstructionCount == before + 1, "a caved-in corridor raises a job",
            $"{before} became {build.ObstructionCount}");

        if (!build.TryClaimDigJob(grid.CellToWorld(corridor), out Vector2I claimed))
        {
            Check(false, "the job can be claimed");
            return;
        }

        build.ReleaseClaim(claimed);

        Check(build.ObstructionCount == before + 1, "releasing a claim does not delete the job",
            $"{build.ObstructionCount} obstructions left");
        // Measured against what the board was already holding. Earlier tests leave live claims of
        // their own, so a global zero here would be asserting something about them instead.
        Check(build.ClaimedDigCellCount == claimsBefore, "and the claim itself is let go",
            $"{claimsBefore} became {build.ClaimedDigCellCount}");
        Check(build.TryClaimDigJob(grid.CellToWorld(corridor), out _),
            "so somebody else can pick it up");

        build.ReleaseClaim(corridor);

        // Dug back out, which is the other half of the contract: a job retires when it is genuinely
        // done. Also stops this obstruction outranking every room in the tests that follow, since
        // clearing blocked passages deliberately takes priority over starting new chambers.
        grid.Dig(corridor);

        Check(build.ObstructionCount == before, "and retires once the corridor is open again",
            $"{build.ObstructionCount} obstructions left");
    }

    // A room cannot wait forever for ground nobody can move.
    //
    // A pending cell that turns to rock - lava meeting water leaves stone - was handed out forever,
    // and the ant who walked to it dropped her claim without telling the board, so the cell stayed
    // claimed and every future ant skipped it. The room stayed Excavating for the rest of the game.
    private void ARoomGivesUpOnGroundThatTurnedToRock()
    {
        var build = GetNode<BuildManager>("Main/BuildManager");
        var colony = GetNode<ColonyManager>("Main/ColonyManager");

        colony.AddFood(300);

        Vector2I origin = FindDiggableNear(grid.NestCenterCell + new Vector2I(-170, 10));

        // Dig all but one cell, so exactly one is pending and it is the one we petrify.
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                if (x != 2 || y != 1)
                {
                    grid.Dig(origin + new Vector2I(x, y));
                }
            }
        }

        materials.DeriveDirtyTiles();

        if (!build.TryCreateRoom(new Rect2I(origin, new Vector2I(3, 2)), BuildingType.Granary))
        {
            Check(false, "a room could be placed over ground that will petrify");
            return;
        }

        Room placed = null;

        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is Room room && room.Footprint.Position == origin)
            {
                placed = room;
            }
        }

        if (placed == null || placed.State != Room.RoomState.Excavating)
        {
            Check(false, "the room starts out excavating", $"{placed?.State}");
            return;
        }

        int cellsBefore = placed.CellCount;

        // Whichever cell is actually still pending, rather than the one the layout suggests should
        // be: generation puts rock where it likes, so which of the six the room ended up owning -
        // and which of those Dig managed to open - is not something the test gets to assume.
        Vector2I pending = default;
        bool havePending = false;

        foreach (Vector2I candidate in placed.PendingDigCells)
        {
            pending = candidate;
            havePending = true;
            break;
        }

        if (!havePending)
        {
            Check(false, "the room has a cell left to dig");
            return;
        }

        grid.SetTileFromSimulation(pending, GridManager.TileType.Rock);

        // The board is asked for work, which is when staleness gets noticed.
        build.TryClaimDigJob(grid.CellToWorld(origin), out _);

        Check(placed.State != Room.RoomState.Excavating,
            "a room stops excavating ground that turned to rock", $"{placed.State}");
        Check(placed.CellCount == cellsBefore - 1, "and stops counting it as its own",
            $"{cellsBefore} became {placed.CellCount}");
    }
    // Pulling a room down out from under the worker furnishing it.
    //
    // Demolish frees the Room node, and a freed Godot node leaves a live C# wrapper behind - so the
    // ant's `pendingRoom != null` check passed and the very next member access threw, inside a timer
    // handler, where Godot prints the exception and swallows it. She was left in Building with no
    // way out. Three separate dereferences had the same hole.
    private void PullingDownARoomDoesNotStrandItsBuilder()
    {
        var build = GetNode<BuildManager>("Main/BuildManager");
        var colony = GetNode<ColonyManager>("Main/ColonyManager");

        colony.AddFood(300);

        Vector2I origin = FindDiggableNear(grid.NestCenterCell + new Vector2I(-146, 10));

        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                grid.Dig(origin + new Vector2I(x, y));
            }
        }

        materials.DeriveDirtyTiles();

        if (!build.TryCreateRoom(new Rect2I(origin, new Vector2I(3, 2)), BuildingType.Granary))
        {
            Check(false, "a room could be placed to demolish");
            return;
        }

        AntWorker builder = null;

        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is AntWorker worker)
            {
                builder = worker;
                break;
            }
        }

        Room placed = null;

        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is Room room && room.Footprint.Position == origin)
            {
                placed = room;
            }
        }

        if (builder == null || placed == null)
        {
            Check(false, "a builder and a room to take from her");
            return;
        }

        int capacityBefore = colony.Capacity;
        int foodCapBefore = colony.FoodCapacity;

        // She takes the job, then the player changes their mind.
        build.SelectRoom(placed);
        builder.CommandBuild(placed);
        build.Demolish(placed);

        Check(build.Selected == null, "demolishing clears the selection");

        // The furnish timer fires anyway - this is the moment that used to throw.
        builder.GetNode<Timer>("BuildTimer").EmitSignal(Timer.SignalName.Timeout);

        Check(!builder.IsStalled, "her builder is not left with nothing to do");
        Check(colony.FoodCapacity == foodCapBefore,
            "a demolished room grants nothing when its timer fires",
            $"{foodCapBefore} became {colony.FoodCapacity}");
        Check(colony.Capacity == capacityBefore, "and no capacity either");
    }
    // An ant always has something pending.
    //
    // She is never left non-Walking with every timer stopped: no route, no callback, nothing to wake
    // her. That state used to be reachable from four places, because GoIdle could be entered while
    // she was still Digging or Building and PickWanderTarget then refused to do anything.
    //
    // Driven through the wander timer, which is the real signal that runs GoIdle, and fired on every
    // worker in the colony whatever she happens to be doing - which is exactly the case that broke.
    private void AnIdleAntAlwaysFindsSomethingToDo()
    {
        int checked_ = 0;
        int stalled = 0;

        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is not AntWorker ant)
            {
                continue;
            }

            ant.GetNode<Timer>("WanderTimer").EmitSignal(Timer.SignalName.Timeout);

            checked_++;

            if (ant.IsStalled)
            {
                stalled++;
            }
        }

        Check(checked_ > 0, "there are workers to check", $"{checked_} ants");
        Check(stalled == 0, "no worker is left with nothing pending after going idle",
            $"{stalled} of {checked_} stalled");
    }
    // Each chamber is a chamber, not part of an open-plan cavern.

    // Ants plaster the wall of a finished chamber, the same way they plaster the burrow.
    //
    // Two properties matter more than the plastering itself, and both are asserted here.
    //
    // Cementing must never change a tile's derived type. SetCell only ever swaps one solid-or-powder
    // material for another and DeriveTile counts Solid and Powder identically, so hardening cannot
    // seal a doorway, cannot bury an ant, and cannot flip an open tile shut. That is the whole
    // reason it is safe to run a sweep over a room somebody is standing in.
    //
    // And hardened earth is still diggable - it derives to TileType.Dirt, so CanDig stays true. The
    // wall stops pathing and stops loose spoil slumping through it; it does not stop an ant. A
    // chamber whose walls could not be dug would be a chamber nobody could reach.
    private void RoomWallsCementThemselves()
    {
        var build = GetNode<BuildManager>("Main/BuildManager");
        var colony = GetNode<ColonyManager>("Main/ColonyManager");

        colony.AddFood(300);

        Vector2I origin = grid.NestCenterCell + new Vector2I(-118, 10);
        var footprint = new Rect2I(origin, new Vector2I(3, 2));

        // Dug out in advance, so the room goes straight to furnishing and starts cementing.
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                grid.Dig(origin + new Vector2I(x, y));
            }
        }

        // A doorway, so this is a chamber off a corridor rather than a sealed pocket.
        Vector2I doorway = origin + new Vector2I(-1, 1);
        grid.Dig(doorway);

        materials.DeriveDirtyTiles();

        if (!build.TryCreateRoom(footprint, BuildingType.Granary))
        {
            Check(false, "a room could be placed to cement");
            return;
        }

        Room placed = null;

        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is Room room && room.Footprint.Position == origin)
            {
                placed = room;
            }
        }

        Check(placed != null, "the room to cement exists");

        if (placed == null)
        {
            return;
        }

        // What the ring looks like before anybody plasters it, so the assertions below are about
        // the sweep rather than about the terrain.
        var typesBefore = new Dictionary<Vector2I, GridManager.TileType>();
        int hardenedBefore = 0;

        foreach (Vector2I cell in RingCellsOf(footprint))
        {
            typesBefore[cell] = grid.GetTileAt(cell);
            hardenedBefore += CountCellsIn(cell, MaterialId.HardenedDirt);
        }

        // The sweep is driven from _Process, so give it real frames rather than ticks.
        for (int pass = 0; pass < 400; pass++)
        {
            materials.CementWallsForTest(0.25);
        }

        materials.DeriveDirtyTiles();

        int hardenedAfter = 0;
        int changedType = 0;

        foreach (Vector2I cell in RingCellsOf(footprint))
        {
            hardenedAfter += CountCellsIn(cell, MaterialId.HardenedDirt);

            if (grid.GetTileAt(cell) != typesBefore[cell])
            {
                changedType++;
            }
        }

        Check(hardenedAfter > hardenedBefore, "ants cement the wall around a finished chamber",
            $"{hardenedBefore} became {hardenedAfter}");
        Check(changedType == 0, "cementing never changes what a tile is",
            $"{changedType} ring tiles changed type");
        Check(grid.IsTunnel(doorway), "the doorway is still open");
        Check(grid.CanDig(origin + new Vector2I(-1, 0)), "a cemented wall can still be dug through");

        // And a room that has been pulled down stops being plastered, or the colony keeps
        // maintaining a chamber that no longer exists - a leak nothing would ever complain about.
        build.Demolish(placed);

        int afterDemolish = 0;

        foreach (Vector2I cell in RingCellsOf(footprint))
        {
            afterDemolish += CountCellsIn(cell, MaterialId.HardenedDirt);
        }

        for (int pass = 0; pass < 200; pass++)
        {
            materials.CementWallsForTest(0.25);
        }

        int afterMore = 0;

        foreach (Vector2I cell in RingCellsOf(footprint))
        {
            afterMore += CountCellsIn(cell, MaterialId.HardenedDirt);
        }

        Check(afterMore == afterDemolish, "a demolished chamber stops being plastered",
            $"{afterDemolish} became {afterMore}");
    }

    private List<Vector2I> RingCellsOf(Rect2I footprint)
    {
        var cells = new List<Vector2I>();
        Rect2I ring = footprint.Grow(1);

        for (int x = ring.Position.X; x < ring.Position.X + ring.Size.X; x++)
        {
            for (int y = ring.Position.Y; y < ring.Position.Y + ring.Size.Y; y++)
            {
                if (!footprint.HasPoint(new Vector2I(x, y)))
                {
                    cells.Add(new Vector2I(x, y));
                }
            }
        }

        return cells;
    }
    //
    // Nothing used to look outside a footprint at all, so two rooms could share an open edge and a
    // room could be placed in mid-air. Both rules are checked through the same public entry point
    // the drag-to-place UI uses, so passing here means the UI behaves the same way.
    private void RoomsNeedWallsAroundThem()
    {
        var build = GetNode<BuildManager>("Main/BuildManager");
        var colony = GetNode<ColonyManager>("Main/ColonyManager");

        colony.AddFood(400);

        Vector2I origin = grid.NestCenterCell + new Vector2I(-90, 9);

        // A floor first, so "no floor" cannot be the reason any of these are refused.
        for (int x = -2; x < 14; x++)
        {
            materials.FillTile(origin + new Vector2I(x, 2), MaterialId.Stone);
        }

        materials.DeriveDirtyTiles();

        Check(build.TryCreateRoom(new Rect2I(origin, new Vector2I(3, 2)), BuildingType.Granary),
            "a room can be placed in virgin earth");

        // Butted straight up against it: no wall at all between the two, which is the open-plan
        // cavern this rule exists to prevent.
        Check(!build.TryCreateRoom(new Rect2I(origin + new Vector2I(3, 0), new Vector2I(3, 2)), BuildingType.Granary),
            "a room touching its neighbour is refused");

        // One tile of earth between them is exactly the rule, so this one goes up.
        Check(build.TryCreateRoom(new Rect2I(origin + new Vector2I(4, 0), new Vector2I(3, 2)), BuildingType.Granary),
            "a room one tile clear of its neighbour is accepted");

        // Diagonally touching counts too. Corners are part of the shell, and two chambers meeting
        // at a point have no wall between them however you draw it.
        Check(!build.TryCreateRoom(new Rect2I(origin + new Vector2I(-2, 2), new Vector2I(2, 2)), BuildingType.Granary),
            "a room touching a neighbour at the corner is refused");

        // And a chamber needs something underneath it.
        Vector2I sky = new Vector2I(grid.NestCenterCell.X - 90, 3);

        Check(!build.TryCreateRoom(new Rect2I(sky, new Vector2I(3, 2)), BuildingType.Granary),
            "a room in open sky is refused");

        // A hole in the floor, forced open rather than dug: generation puts rock wherever it likes,
        // and a Dig that quietly refused would leave the floor intact and this testing nothing.
        Vector2I hollow = grid.NestCenterCell + new Vector2I(-90, 14);
        int floorRow = hollow.Y + 2;
        int open = 0;

        for (int x = 0; x < 3; x++)
        {
            grid.SetTileFromSimulation(new Vector2I(hollow.X + x, floorRow), GridManager.TileType.Tunnel);

            if (grid.IsTunnel(new Vector2I(hollow.X + x, floorRow)))
            {
                open++;
            }
        }

        Check(open == 3, "the floor under the test patch really is open", $"{open} of 3 cells open");

        Check(!build.TryCreateRoom(new Rect2I(hollow, new Vector2I(3, 2)), BuildingType.Granary),
            "a room with a hole in its floor is refused");

        // A cavern is not a chamber. Dug wide and open, so more than half the ring is gone.
        Vector2I cavern = grid.NestCenterCell + new Vector2I(-104, 12);

        for (int x = -1; x < 6; x++)
        {
            for (int y = -1; y < 4; y++)
            {
                grid.Dig(cavern + new Vector2I(x, y));
                grid.SetTileFromSimulation(cavern + new Vector2I(x, y), GridManager.TileType.Tunnel);
            }
        }

        // A floor under it, so "nowhere to stand" cannot be the reason.
        for (int x = -1; x < 6; x++)
        {
            materials.FillTile(cavern + new Vector2I(x, 4), MaterialId.Stone);
            grid.SetTileFromSimulation(cavern + new Vector2I(x, 4), GridManager.TileType.Rock);
        }

        materials.DeriveDirtyTiles();

        Check(!build.TryCreateRoom(new Rect2I(cavern, new Vector2I(4, 2)), BuildingType.Granary),
            "a room carved out of an open cavern is refused");
    }

    // A misplaced room used to be permanent: no way to select it, no way to remove it, and its food
    // gone for good. Pulling one down has to give back what it is fair to give back and, crucially,
    // take its effect off again - a demolished chamber that kept raising the population cap would be
    // free capacity for the price of one room.
    private void RoomsCanBePulledDownAgain(BuildManager build, ColonyManager colony, Room room, int capacityBefore)
    {
        BuildingDef def = BuildingDefs.All[room.Type];
        int cells = room.CellCount;
        int expectedRefund = Mathf.FloorToInt(def.FoodCostPerCell * cells * 0.5f);

        // Room for the refund to land in, or AddFood clamps it at the ceiling and the assertion
        // below measures the larder rather than the refund.
        colony.RemoveFood(Mathf.Min(colony.Food, expectedRefund + 10));

        int foodBefore = colony.Food;
        Vector2I inside = room.StandCell;

        build.SelectRoom(room);
        Check(build.Selected == room, "a placed room can be selected");
        Check(build.RoomAt(inside) == room, "clicking a cell finds the room that owns it");

        build.Demolish(room);

        Check(build.Selected == null, "pulling a room down clears the selection");
        Check(build.RoomAt(inside) == null, "a demolished room no longer owns its cells");
        Check(colony.Capacity == capacityBefore, "demolishing gives back the capacity it granted",
            $"{capacityBefore} -> {colony.Capacity}");
        Check(colony.Food == foodBefore + expectedRefund, "demolishing refunds half the food",
            $"expected {foodBefore + expectedRefund}, got {colony.Food}");

        // The ground stays dug. Only the room is gone.
        Check(grid.IsTunnel(inside), "a demolished room leaves its cavity behind");
    }

    // Routes are simplified before an ant walks them: waypoints she does not have to turn at get
    // dropped, so a staircase becomes a glide. The danger is that a shortcut describes a move the
    // ant is not allowed to make.
    //
    // Ants walk, they do not climb - MoveDirections has no vertical entry, so elevation only ever
    // changes alongside horizontal movement. A simplified segment steeper than 45 degrees would be
    // a climb however open the ground between its ends happens to be, and the ant would either stall
    // against it or slide up a wall. That is the thing this guards.
    private void SmoothedRoutesStayWalkable()
    {
        // Its own corridor, cut for the purpose.
        //
        // This used to route from the nest to whatever the spoil drop-off happened to be, which
        // coupled a pathfinding test to where the colony tips its earth - and to the twenty loads of
        // sand an earlier test dumps on the surface and never clears up. It started failing with a
        // one-cell route because the surface walkway near the nest had been buried by a test that
        // has nothing to do with route smoothing.
        Vector2I start = FindDiggableNear(grid.NestCenterCell + new Vector2I(-130, 11));

        for (int step = 0; step < 14; step++)
        {
            // A descending staircase, which is the shape simplification exists to collapse.
            grid.Dig(start + new Vector2I(step, step / 3));
        }

        materials.DeriveDirtyTiles();

        Vector2I finish = start + new Vector2I(13, 13 / 3);

        if (!grid.IsStandable(start) || !grid.IsStandable(finish))
        {
            Check(false, "a corridor could be cut to route along",
                $"{grid.IsStandable(start)} .. {grid.IsStandable(finish)}");
            return;
        }

        List<Vector2I> route = grid.FindTunnelPath(start, finish);

        Check(route != null && route.Count >= 2, "there is a route to simplify", $"{route?.Count ?? 0} cells");

        if (route == null || route.Count < 2)
        {
            return;
        }

        Check(route[0] == start && route[^1] == finish, "simplifying keeps both ends of the route",
            $"{route[0]} .. {route[^1]}");

        int climbs = 0;

        for (int i = 1; i < route.Count; i++)
        {
            Vector2I step = route[i] - route[i - 1];

            if (Mathf.Abs(step.Y) > Mathf.Abs(step.X))
            {
                climbs++;
            }
        }

        Check(climbs == 0, "no simplified segment climbs faster than it walks",
            $"{climbs} of {route.Count - 1} segments are steeper than 45 degrees");

        // And every cell a segment passes over has to be somewhere she could stand, or the shortcut
        // is cutting a corner through rock.
        int unwalkable = 0;

        for (int i = 1; i < route.Count; i++)
        {
            if (!grid.IsWalkableLineForTest(route[i - 1], route[i]))
            {
                unwalkable++;
            }
        }

        Check(unwalkable == 0, "every simplified segment stays on standable ground",
            $"{unwalkable} segments cross ground she cannot walk");
    }

    // What weighting the search actually bought.
    //
    // Under the old unweighted search a zigzag was free: down-right then up-right is two moves for
    // a net two cells sideways, exactly what right-then-right costs, so the planner was genuinely
    // indifferent between a flat corridor and one that bobbed up and down the whole way. Asserted
    // on the expanded route rather than the simplified one, because simplification hides the
    // symptom - the point is that the shape is no longer *chosen*.
    private void RoutesDoNotBobUpAndDown()
    {
        // A flat floor with headroom, cut on purpose: on generated terrain a route that rises and
        // falls may be the only route there is, and this has to distinguish "chose to zigzag" from
        // "had to".
        const int Cut = 24;
        const int MinRun = 10;

        Vector2I floor = FindDiggableNear(grid.NestCenterCell + new Vector2I(-40, 12));

        for (int x = 0; x < Cut; x++)
        {
            grid.Dig(floor + new Vector2I(x, 0));
            grid.Dig(floor + new Vector2I(x, -1));
        }

        materials.DeriveDirtyTiles();

        // The longest run that is genuinely flat, rather than the run asked for.
        //
        // Generation is free to have put a cavity under part of this patch, and a cell with no
        // floor beneath it is not standable however thoroughly it has been dug. Measuring the run
        // instead of forcing it keeps the test about the planner: on terrain where leaving the row
        // is the only way through, a route that leaves the row is right to.
        int runStart = 0;
        int runLength = 0;
        int current = 0;

        for (int x = 0; x < Cut; x++)
        {
            current = grid.IsStandable(floor + new Vector2I(x, 0)) ? current + 1 : 0;

            if (current > runLength)
            {
                runLength = current;
                runStart = x - current + 1;
            }
        }

        Check(runLength >= MinRun, "a flat corridor could be cut to route along",
            $"longest flat run is {runLength} of {Cut}");

        if (runLength < MinRun)
        {
            return;
        }

        Vector2I from = floor + new Vector2I(runStart, 0);
        Vector2I to = floor + new Vector2I(runStart + runLength - 1, 0);

        List<Vector2I> cells = ExpandRoute(grid.FindTunnelPath(from, to));

        Check(cells != null, "there is a route along the flat corridor");

        if (cells == null)
        {
            return;
        }

        // Both ends are on the same row and the whole corridor is standable, so the cheapest route
        // is sixteen sideways steps and anything that leaves the row is paying 14 for a 10 it did
        // not need.
        int offRow = 0;

        foreach (Vector2I cell in cells)
        {
            if (cell.Y != from.Y)
            {
                offRow++;
            }
        }

        Check(offRow == 0, "a route across flat ground stays on the flat",
            $"{offRow} of {cells.Count} cells wander off the row");

        Check(cells.Count == runLength, "and covers it in one step per cell",
            $"{cells.Count} cells across a run of {runLength}");
    }

    // Every cell a route passes over, not just the corners it turns at.
    private List<Vector2I> ExpandRoute(List<Vector2I> route)
    {
        if (route == null)
        {
            return null;
        }

        var cells = new List<Vector2I> { route[0] };

        for (int i = 1; i < route.Count; i++)
        {
            Vector2I from = route[i - 1];
            Vector2I delta = route[i] - from;
            int steps = Mathf.Max(Mathf.Abs(delta.X), Mathf.Abs(delta.Y));

            for (int step = 1; step <= steps; step++)
            {
                cells.Add(new Vector2I(
                    from.X + Mathf.RoundToInt(delta.X * step / (float)steps),
                    from.Y + Mathf.RoundToInt(delta.Y * step / (float)steps)));
            }
        }

        return cells;
    }

    private int CountAround(Vector2I tile, int radius, MaterialId want)
    {
        int found = 0;

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                found += CountCellsIn(tile + new Vector2I(x, y), want);
            }
        }

        return found;
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
