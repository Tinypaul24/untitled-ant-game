using Godot;
using System.Collections.Generic;

// The tick. Walks only the dirty rectangles of awake chunks and moves what can move.
//
// Three rules keep the result from looking like a grid being scanned:
//
//  - Rows are processed bottom-up. A cell that falls lands in a row already dealt with, so it cannot
//    fall twice in one tick and a column collapses at one cell per tick rather than instantly.
//  - The horizontal direction flips every tick. A fixed direction makes piles lean and liquids
//    creep one way.
//  - Anything that moved is recorded, so a cell shoved sideways is not picked up again further along
//    the same row.
public sealed class MaterialSimulation
{
    private readonly MaterialWorld world;
    private readonly HashSet<Vector2I> movedThisTick = new();
    private readonly RandomNumberGenerator random = new();

    // Fraction of the gap to ambient a cell closes per tick. Slow enough that a heated pocket
    // lingers, fast enough that the temperature store empties out again.
    private const float HeatRelaxation = 0.06f;

    private bool scanLeftToRight;

    public int CellsProcessedLastTick { get; private set; }
    public int CellsMovedLastTick { get; private set; }

    public MaterialSimulation(MaterialWorld world)
    {
        this.world = world;
        random.Randomize();
    }

    // How many cells one tick will scan before deferring the rest to the next one.
    //
    // Without a cap, cost is set by however much happens to be moving, and a big enough flood simply
    // takes the frame rate with it. A scan stops on a row boundary, which keeps the bottom-up rule
    // intact, so the real ceiling is this plus one row. Whatever is left keeps its dirty rectangle
    // and is picked up next tick, so nothing is lost; extreme floods settle more slowly instead of
    // stuttering.
    //
    // Sized from measurement: a settling pool costs roughly 1.7us per scanned cell, so this is about
    // 8ms. Retune it against the benchmark rather than by feel.
    private const int MaxCellsPerTick = 4500;

    // Where the next tick starts in the awake list. Without this the same chunks would be serviced
    // every tick and the ones past the budget would never move at all.
    private int resumeAt;

    // Counts chunks, not cells: a deferred chunk keeps its whole dirty rectangle for next tick.
    public int ChunksDeferredLastTick { get; private set; }

    public void Step()
    {
        movedThisTick.Clear();
        scanLeftToRight = !scanLeftToRight;

        CellsProcessedLastTick = 0;
        CellsMovedLastTick = 0;
        ChunksDeferredLastTick = 0;

        // Already a snapshot: CollectAwakeChunks copies the world's awake set into a reusable list,
        // and nothing touches that list for the rest of the tick. Stepping a chunk does mutate the
        // awake set itself - waking neighbours, putting this one to sleep - which is exactly why the
        // snapshot has to exist, but there is no second copy to make here.
        List<Vector2I> coords = world.CollectAwakeChunks();

        if (coords.Count == 0)
        {
            resumeAt = 0;
            return;
        }

        int start = resumeAt % coords.Count;

        for (int i = 0; i < coords.Count; i++)
        {
            Vector2I chunkCoord = coords[(start + i) % coords.Count];

            if (CellsProcessedLastTick >= MaxCellsPerTick)
            {
                ChunksDeferredLastTick = coords.Count - i;
                resumeAt = start + i;

                return;
            }

            if (world.TryGetChunk(chunkCoord, out MaterialChunk chunk))
            {
                StepChunk(chunkCoord, chunk);
            }
        }

        resumeAt = 0;
    }

    private void StepChunk(Vector2I chunkCoord, MaterialChunk chunk)
    {
        chunk.TakeDirtyRect(out int minX, out int minY, out int maxX, out int maxY);

        // Asleep from here on unless something inside it moves and wakes it again. Done before the
        // scan rather than after, so a cell that moves during the scan is not immediately undone.
        world.MarkChunkAsleep(chunk);

        if (maxX < minX || maxY < minY)
        {
            return;
        }

        Vector2I origin = chunkCoord * MaterialChunk.Size;

        for (int localY = maxY; localY >= minY; localY--)
        {
            // Checked between rows, not between chunks. At 1px a fully dirty chunk is 4096 cells, so
            // finishing one no matter what meant the budget could be overshot by more than the
            // budget itself. Stopping on a row boundary keeps the bottom-up rule intact: the rows
            // left over go back on the list and are simply a smaller dirty rectangle next tick.
            if (CellsProcessedLastTick >= MaxCellsPerTick && localY < maxY)
            {
                world.RedirtyRows(chunk, minX, minY, maxX, localY);
                return;
            }

            for (int step = 0; step <= maxX - minX; step++)
            {
                int localX = scanLeftToRight ? minX + step : maxX - step;
                Vector2I cell = origin + new Vector2I(localX, localY);

                CellsProcessedLastTick++;
                StepCell(cell);
            }
        }
    }

    private void StepCell(Vector2I cell)
    {
        if (movedThisTick.Contains(cell))
        {
            return;
        }

        MaterialId id = world.GetCell(cell);
        MaterialDefinition definition = MaterialDatabase.Get(id);

        if (definition.IsAir)
        {
            return;
        }

        if (ReactionTable.IsReactive(id) && TryReact(cell, id))
        {
            return;
        }

        if ((definition.HeatOutput != 0f || world.HasTemperature(cell)) && StepHeat(cell, definition))
        {
            return;
        }

        if (definition.Lifetime > 0 && TickLifetime(cell, definition))
        {
            return;
        }

        if (!definition.Falls)
        {
            return;
        }

        if (TryFall(cell, definition))
        {
            return;
        }

        if (definition.Flows)
        {
            TrySpread(cell, definition);
        }
    }

    // Anything with a finite life burns down and leaves whatever it decays into.
    private bool TickLifetime(Vector2I cell, MaterialDefinition definition)
    {
        int remaining = world.GetLifetime(cell) - 1;

        if (remaining > 0)
        {
            world.SetLifetime(cell, remaining);

            // Still alive, but it has to stay awake to keep counting down.
            world.Wake(cell);
            return false;
        }

        world.SetCell(cell, definition.DecaysTo);
        return true;
    }

    private bool TryReact(Vector2I cell, MaterialId id)
    {
        bool pending = false;

        for (int i = 0; i < Neighbours.Length; i++)
        {
            Vector2I neighbour = cell + Neighbours[i];
            MaterialId other = world.GetCell(neighbour);

            if (!ReactionTable.TryGet(id, other, out Reaction reaction))
            {
                continue;
            }

            // Conditions are checked against whichever cells actually change identity, not against
            // the pair as a whole. Grass regrowing onto soil only changes the soil, and it is the
            // soil that has to be somewhere grass could plausibly take hold.
            if (!MeetsNeeds(cell, id, reaction.BecomesA, reaction.Needs)
                || !MeetsNeeds(neighbour, other, reaction.BecomesB, reaction.Needs))
            {
                continue;
            }

            if (random.Randf() > reaction.Chance)
            {
                // In contact, but the roll failed this tick. The cell has to stay awake or the
                // chunk settles and a reaction that should have happened never gets another go.
                pending = true;
                continue;
            }

            world.SetCell(cell, reaction.BecomesA);
            world.SetCell(neighbour, reaction.BecomesB);

            return true;
        }

        if (pending)
        {
            world.Wake(cell);
        }

        return false;
    }

    // A cell that is not changing has nothing to satisfy; only the ones being rewritten are tested.
    private bool MeetsNeeds(Vector2I cell, MaterialId before, MaterialId after, ReactionNeeds needs)
    {
        if (needs == ReactionNeeds.Nothing || before == after)
        {
            return true;
        }

        if (needs.HasFlag(ReactionNeeds.Daylight) && !world.IsNearSurface(cell))
        {
            return false;
        }

        if (needs.HasFlag(ReactionNeeds.AirContact) && CountNeighbours(cell, MaterialKind.Air) == 0)
        {
            return false;
        }

        if (needs.HasFlag(ReactionNeeds.Buried) && CountNeighbours(cell, MaterialKind.Air) > 0)
        {
            return false;
        }

        return !needs.HasFlag(ReactionNeeds.Saturated)
            || CountNeighbours(cell, MaterialKind.Liquid) >= SaturatingNeighbours;
    }

    // Out of the four orthogonal neighbours, so this is "more than half surrounded by liquid".
    private const int SaturatingNeighbours = 3;

    private int CountNeighbours(Vector2I cell, MaterialKind kind)
    {
        int found = 0;

        for (int i = 0; i < Neighbours.Length; i++)
        {
            if (MaterialDatabase.Get(world.GetCell(cell + Neighbours[i])).Kind == kind)
            {
                found++;
            }
        }

        return found;
    }


    // Heat, and what crossing a threshold turns a cell into.
    //
    // Only runs for cells that emit heat or have drifted off their material default, so ordinary
    // terrain never enters this path at all. Returns true if the cell became something else, in
    // which case the caller stops working on it this tick.
    private bool StepHeat(Vector2I cell, MaterialDefinition definition)
    {
        float temperature = world.GetTemperature(cell, definition);

        if (definition.HeatOutput != 0f)
        {
            // A source holds its own temperature and pushes into its surroundings. Neighbours are
            // driven toward the source rather than past it, so heat cannot run away upward.
            for (int i = 0; i < Neighbours.Length; i++)
            {
                Vector2I neighbour = cell + Neighbours[i];
                MaterialDefinition other = MaterialDatabase.Get(world.GetCell(neighbour));
                float otherTemperature = world.GetTemperature(neighbour, other);

                float pushed = definition.HeatOutput > 0f
                    ? Mathf.Min(otherTemperature + definition.HeatOutput, temperature)
                    : Mathf.Max(otherTemperature + definition.HeatOutput, temperature);

                if (!Mathf.IsEqualApprox(pushed, otherTemperature))
                {
                    world.SetTemperature(neighbour, pushed, other);
                    world.Wake(neighbour);
                }
            }
        }
        else
        {
            // Everything else bleeds back toward its default. Without this a cell heated once stays
            // hot forever and the temperature store never shrinks.
            temperature = Mathf.Lerp(temperature, definition.DefaultTemperature, HeatRelaxation);
            world.SetTemperature(cell, temperature, definition);
        }

        if (TryPhaseChange(cell, definition, temperature))
        {
            return true;
        }

        // Still off-ambient, so it has to keep ticking to finish cooling or to cross a threshold.
        if (world.HasTemperature(cell) || definition.HeatOutput != 0f)
        {
            world.Wake(cell);
        }

        return false;
    }

    // Thresholds are checked hottest-first so a material with several cannot pick the wrong one.
    private bool TryPhaseChange(Vector2I cell, MaterialDefinition definition, float temperature)
    {
        if (definition.Flammable && temperature >= definition.IgnitionTemperature)
        {
            return Transform(cell, definition.BurnsTo);
        }

        if (temperature >= definition.MeltingPoint && Transform(cell, definition.MeltsTo))
        {
            return true;
        }

        if (temperature >= definition.BoilingPoint && Transform(cell, definition.BoilsTo))
        {
            return true;
        }

        return temperature <= definition.FreezingPoint && Transform(cell, definition.FreezesTo);
    }

    // Air as a target means the material simply has no such transition, rather than that it should
    // be annihilated when it gets hot.
    private bool Transform(Vector2I cell, MaterialId into)
    {
        if (MaterialDatabase.Get(into).IsAir)
        {
            return false;
        }

        world.SetCell(cell, into);
        return true;
    }

    // Straight down for anything heavier than air, straight up for anything lighter, then the two
    // diagonals so powders form slopes instead of towers.
    //
    // Each of those is a move of up to CellsPerStep cells rather than exactly one. A cell is a world
    // pixel now, and one pixel per tick is a quarter of the speed material used to fall at - sand
    // would drift rather than drop. Walking out cell by cell and stopping at the first thing in the
    // way keeps it from tunnelling through a floor on the way.
    private bool TryFall(Vector2I cell, MaterialDefinition definition)
    {
        int vertical = definition.Density < 0f ? -1 : 1;

        if (TryStep(cell, new Vector2I(0, vertical), definition, MaterialWorld.CellsPerStep))
        {
            return true;
        }

        int first = random.Randf() < 0.5f ? -1 : 1;

        if (TryStep(cell, new Vector2I(first, vertical), definition, MaterialWorld.CellsPerStep))
        {
            return true;
        }

        return TryStep(cell, new Vector2I(-first, vertical), definition, MaterialWorld.CellsPerStep);
    }

    // Moves as far along `direction` as the way is clear, up to maxSteps.
    private bool TryStep(Vector2I from, Vector2I direction, MaterialDefinition mover, int maxSteps)
    {
        int furthest = FurthestReachable(from, direction, mover, maxSteps);

        return furthest > 0 && TryMove(from, from + direction * furthest, mover);
    }

    // How far a mover can travel along `direction` before something stops it. A run of clear cells
    // is a prefix, so the answer is simply the last one before the first blocked cell.
    private int FurthestReachable(Vector2I from, Vector2I direction, MaterialDefinition mover, int maxSteps)
    {
        int furthest = 0;

        for (int step = 1; step <= maxSteps; step++)
        {
            if (!CanDisplace(mover, MaterialDatabase.Get(world.GetCell(from + direction * step))))
            {
                break;
            }

            furthest = step;
        }

        return furthest;
    }

    // A liquid that cannot fall runs sideways, looking further than one cell so a pool levels out in
    // a reasonable number of ticks rather than creeping one cell per tick.
    // Walks outward once per direction and goes to the furthest cell it reached.
    //
    // Every cell along the way has to be passable, otherwise liquid teleports through walls - and
    // since a run of clear cells is a prefix, the furthest reachable cell is simply the last one
    // before the first blocked one. Trying each distance from the far end inward instead re-walked
    // the same cells from scratch every time, which is where a settling pool spent its tick.
    private bool TrySpread(Vector2I cell, MaterialDefinition definition)
    {
        // Viscous things only try on some ticks. They still have to stay awake, or a pool that
        // failed its roll would settle and never flow again.
        if (definition.FlowChance < 1f && random.Randf() > definition.FlowChance)
        {
            world.Wake(cell);
            return false;
        }

        int direction = random.Randf() < 0.5f ? -1 : 1;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            // DispersionRate is written in world pixels for the same reason falling is: at 1px cells
            // an unconverted rate would make every liquid four times as sluggish as it was designed.
            if (TryStep(cell, new Vector2I(direction, 0), definition, definition.DispersionRate * MaterialWorld.CellsPerStep))
            {
                return true;
            }

            direction = -direction;
        }

        return false;
    }

    private bool TryMove(Vector2I from, Vector2I to, MaterialDefinition mover)
    {
        MaterialId targetId = world.GetCell(to);

        if (!CanDisplace(mover, MaterialDatabase.Get(targetId)))
        {
            return false;
        }

        MaterialId moverId = world.GetCell(from);

        // Swap rather than overwrite: matter is conserved, and it is what lets sand sink through
        // water while the water rises past it.
        MaterialDefinition targetDefinition = MaterialDatabase.Get(targetId);

        // Carrying the lifetimes and temperatures across is eight dictionary operations, and both
        // stores are empty unless something transient is in play - no fire, no lava, no steam. When
        // they are empty every one of those eight reduces to a no-op that still hashes a key, so
        // ordinary falling sand and water skip the lot.
        bool carriesTransientState = world.TracksAnyTransientState;

        int moverLife = 0;
        int targetLife = 0;
        float moverHeat = 0f;
        float targetHeat = 0f;

        if (carriesTransientState)
        {
            moverLife = world.GetLifetime(from);
            targetLife = world.GetLifetime(to);
            moverHeat = world.GetTemperature(from, mover);
            targetHeat = world.GetTemperature(to, targetDefinition);
        }

        world.SetCell(to, moverId);
        world.SetCell(from, targetId);

        if (carriesTransientState)
        {
            world.SetLifetime(to, moverLife);
            world.SetLifetime(from, targetLife);

            // Heat rides along with the matter, or a rising ember would cool the instant it moved.
            world.SetTemperature(to, moverHeat, mover);
            world.SetTemperature(from, targetHeat, targetDefinition);
        }

        movedThisTick.Add(to);
        CellsMovedLastTick++;

        return true;
    }

    // Air is always swallowed. Otherwise the target has to be flagged displaceable, and then which
    // way it goes depends on buoyancy: anything lighter than air rises through whatever is heavier
    // than it, everything else sinks through whatever is lighter. Without the buoyant case, steam
    // is trapped underneath the very water that produced it.
    private static bool CanDisplace(MaterialDefinition mover, MaterialDefinition target)
    {
        if (target.IsAir)
        {
            return true;
        }

        if (!target.Displaceable)
        {
            return false;
        }

        return mover.Density < 0f
            ? target.Density > mover.Density
            : mover.Density > target.Density;
    }

    private static readonly Vector2I[] Neighbours =
    {
        new Vector2I(0, -1),
        new Vector2I(0, 1),
        new Vector2I(-1, 0),
        new Vector2I(1, 0),
    };
}
