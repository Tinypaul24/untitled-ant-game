using Godot;
using System.Collections.Generic;

// How ants get around, and what the ground has to look like for them to manage it.
//
// The one rule everything here follows: ants walk, they do not climb. A cell is only occupiable if it
// has solid ground directly beneath it, and elevation only ever changes on a diagonal. That single
// constraint is what turns every descent into a ramp that has to be deliberately dug.
public partial class GridManager : Node2D
{
    private static readonly Vector2I[] Directions =
    {
        new Vector2I(0, -1), // Up
        new Vector2I(0, 1),  // Down
        new Vector2I(-1, 0), // Left
        new Vector2I(1, 0)   // Right
    };

    private static readonly Vector2I[] MoveDirections =
    {
        new Vector2I(-1, 0),  // Left
        new Vector2I(1, 0),   // Right
        new Vector2I(-1, -1), // Ramp up-left
        new Vector2I(1, -1),  // Ramp up-right
        new Vector2I(-1, 1),  // Ramp down-left
        new Vector2I(1, 1)    // Ramp down-right
    };

    // What makes a cell lethal to stand in. Injected by MaterialWorld rather than called directly,
    // so navigation does not have to know the material system exists and still works - treating the
    // world as entirely safe - in a scene that has no simulation in it, which is what the pathfinding
    // tests rely on.
    private System.Func<Vector2I, bool> hazardTest;

    public void SetHazardTest(System.Func<Vector2I, bool> test)
    {
        hazardTest = test;
    }

    public bool IsHazardous(Vector2I cell)
    {
        return hazardTest != null && hazardTest(cell);
    }

    // Whether anything harmful is within `radius` tiles. Used to decide when to react, which wants a
    // tighter radius than deciding where to run to: an ant should break away as soon as a flow is on
    // top of her, but she should not abandon a dig because there is lava somewhere down the corridor.
    public bool IsHazardNear(Vector2I cell, int radius)
    {
        if (hazardTest == null)
        {
            return false;
        }

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (hazardTest(cell + new Vector2I(x, y)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Standable and not lethal. Kept separate from IsStandable rather than folded into it, because
    // an ant already caught in a lava flow has to be able to walk out of a cell that is hazardous -
    // if being in danger made her position unstandable, no route out of it could be planned at all.
    public bool IsSafelyStandable(Vector2I cell)
    {
        return IsStandable(cell) && !IsHazardous(cell);
    }

    // An ant can only occupy an open cell that has solid ground directly beneath it. Open space with
    // nothing under it is a drop, not a floor - that is what forces tunnels to be dug as ramps.
    public bool IsStandable(Vector2I cell)
    {
        if (!IsInBounds(cell) || !IsWalkable(GetTile(cell)))
        {
            return false;
        }

        return !IsWalkable(GetTile(cell + new Vector2I(0, 1)));
    }

    // Standability limited to cells that already exist, so simply looking at the world cannot force
    // new chunks into being generated.
    public bool IsKnownStandable(Vector2I cell)
    {
        if (!IsInBounds(cell) || !grid.TryGetValue(cell, out TileType tile) || !IsWalkable(tile))
        {
            return false;
        }

        return grid.TryGetValue(cell + new Vector2I(0, 1), out TileType below) && !IsWalkable(below);
    }

    // First cell with a floor at or below this one, for an ant left standing over open air.
    public Vector2I FindFloorBelow(Vector2I cell)
    {
        const int MaxDrop = 64;

        for (int depth = 0; depth < MaxDrop; depth++)
        {
            Vector2I candidate = cell + new Vector2I(0, depth);

            if (IsStandable(candidate))
            {
                return candidate;
            }

            if (!IsInBounds(candidate) || !IsWalkable(GetTile(candidate)))
            {
                break;
            }
        }

        return cell;
    }

    // The next cell to carve when tunnelling from `from` toward `to`.
    //
    // Height is only ever gained or lost diagonally, so a corridor that has to descend comes out as a
    // staircase an ant can walk rather than a shaft nothing can climb. The router also avoids cutting
    // the floor out from under a cell it already opened, which would strand anything standing there.
    public Vector2I GetStepToward(Vector2I from, Vector2I to)
    {
        const float UnderminePenalty = 10000f;
        const float UnsupportedPenalty = 5000f;

        // Within reach, break straight in - unless the target is directly above or below, which would
        // undercut the cell she is standing in and leave a one-way drop. Then sidestep first so the
        // last move onto it is a diagonal she can also walk back up.
        if (Mathf.Max(Mathf.Abs(to.X - from.X), Mathf.Abs(to.Y - from.Y)) <= 1)
        {
            return to.X != from.X ? to : SidestepFor(from, to);
        }

        Vector2I best = from;
        float bestScore = float.MaxValue;

        foreach (Vector2I direction in MoveDirections)
        {
            Vector2I candidate = from + direction;

            if (!IsInBounds(candidate))
            {
                continue;
            }

            TileType tile = GetTile(candidate);

            if (!IsWalkable(tile) && !IsDiggable(tile))
            {
                continue;
            }

            int offX = candidate.X - to.X;
            int offY = candidate.Y - to.Y;
            float score = offX * offX + offY * offY;

            if (IsWalkable(GetTile(candidate + new Vector2I(0, -1))))
            {
                score += UnderminePenalty;
            }

            if (IsWalkable(GetTile(candidate + new Vector2I(0, 1))))
            {
                score += UnsupportedPenalty;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        // Boxed in on every side by rock: fall back to a plain step so the caller still makes progress.
        if (best == from)
        {
            int stepX = Mathf.Sign(to.X - from.X);
            best = from + new Vector2I(stepX == 0 ? 1 : stepX, Mathf.Sign(to.Y - from.Y));
        }

        return best;
    }

    // A step to one side, so a target sitting directly above or below can be reached on a diagonal.
    private Vector2I SidestepFor(Vector2I from, Vector2I to)
    {
        Vector2I right = from + new Vector2I(1, 0);
        Vector2I left = from + new Vector2I(-1, 0);

        bool rightOpen = IsWalkable(GetTile(right)) || IsDiggable(GetTile(right));
        bool leftOpen = IsWalkable(GetTile(left)) || IsDiggable(GetTile(left));

        if (rightOpen && !leftOpen)
        {
            return right;
        }

        if (leftOpen && !rightOpen)
        {
            return left;
        }

        if (!rightOpen && !leftOpen)
        {
            // Solid rock either side - nothing to do but break straight in and accept the drop.
            return to;
        }

        // Both usable: pick the side that keeps a floor under the cell she is leaving.
        return IsWalkable(GetTile(right + new Vector2I(0, 1))) ? left : right;
    }

    // The closest cell an ant could actually stand in, expanding outward regardless of tile type.
    // Bounded so a click far into unexplored territory can't trigger unbounded chunk generation.
    public Vector2I FindNearestTunnelCell(Vector2I from)
    {
        const int MaxVisited = 4000;

        if (IsSafelyStandable(from))
        {
            return from;
        }

        var visited = new HashSet<Vector2I> { from };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(from);

        while (frontier.Count > 0 && visited.Count < MaxVisited)
        {
            Vector2I current = frontier.Dequeue();

            foreach (Vector2I direction in Directions)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsInBounds(next))
                {
                    continue;
                }

                visited.Add(next);

                if (IsSafelyStandable(next))
                {
                    return next;
                }

                frontier.Enqueue(next);
            }
        }

        // No tunnel is reachable nearby.
        return from;
    }

    // How much daylight an ant wants between herself and the nearest harmful tile when fleeing.
    //
    // Not zero, because the thing she is running from is usually still moving. Lava spreads about a
    // cell a tick, which is several times an ant's walking speed, so stopping at the first tile that
    // happens to be clear just means being caught again a moment later - she has to break away from
    // the flow, not step off the edge of it.
    public const int HazardClearanceCells = 4;

    // The way out for an ant standing in something that is hurting her.
    //
    // Deliberately searches over IsStandable rather than IsSafelyStandable, so the route may cross
    // more hazardous ground on the way - when a flow has spread over several cells, walking through
    // the rest of it is the only way out, and refusing to plan through danger would leave her
    // standing in it. Only the destination has to be clear.
    //
    // Prefers a cell with full clearance and settles for a merely-safe one if the search runs out,
    // since being next to the flow still beats being in it. Returns the cell she is already in when
    // nothing better is in range, so callers always get somewhere valid to aim at.
    public Vector2I FindNearestSafeCell(Vector2I from)
    {
        const int MaxVisited = 1200;

        if (HasHazardClearance(from))
        {
            return from;
        }

        var visited = new HashSet<Vector2I> { from };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(from);

        Vector2I fallback = from;

        while (frontier.Count > 0 && visited.Count < MaxVisited)
        {
            Vector2I current = frontier.Dequeue();

            foreach (Vector2I direction in MoveDirections)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsStandable(next))
                {
                    continue;
                }

                visited.Add(next);

                if (HasHazardClearance(next))
                {
                    return next;
                }

                // Breadth-first, so the first merely-safe cell found is also the closest one.
                if (fallback == from && !IsHazardous(next))
                {
                    fallback = next;
                }

                frontier.Enqueue(next);
            }
        }

        return fallback;
    }

    // Safe to stand in, and with nothing harmful within HazardClearanceCells of it.
    private bool HasHazardClearance(Vector2I cell)
    {
        if (!IsSafelyStandable(cell))
        {
            return false;
        }

        for (int y = -HazardClearanceCells; y <= HazardClearanceCells; y++)
        {
            for (int x = -HazardClearanceCells; x <= HazardClearanceCells; x++)
            {
                if (IsHazardous(cell + new Vector2I(x, y)))
                {
                    return false;
                }
            }
        }

        return true;
    }


    // Plans a corridor from `start` toward `goal` that is still walkable once it has been carved.
    //
    // A greedy per-step router cannot do this: it makes locally sensible moves and then saws off its
    // own approach when a switchback doubles back underneath itself. Planning the whole run up front
    // lets three rules hold along the entire route - only horizontal and diagonal moves, every cell
    // with solid ground under it, and no cell tucked directly beneath one the route already opened.
    //
    // Ends at the first cell from which `goal` is within reach, since an ant digs a cell by standing
    // next to it, not by standing in it. Null if no walkable corridor exists.
    public List<Vector2I> PlanDigRoute(Vector2I start, Vector2I goal)
    {
        const int MaxVisited = 6000;
        const int AncestorsChecked = 4;

        if (IsWithinReach(start, goal))
        {
            return new List<Vector2I> { start };
        }

        var cameFrom = new Dictionary<Vector2I, Vector2I>();
        var visited = new HashSet<Vector2I> { start };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(start);

        while (frontier.Count > 0 && visited.Count < MaxVisited)
        {
            Vector2I current = frontier.Dequeue();

            foreach (Vector2I direction in MoveDirections)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsInBounds(next))
                {
                    continue;
                }

                TileType tile = GetTile(next);

                if (!IsWalkable(tile) && !IsDiggable(tile))
                {
                    continue;
                }

                // Never route a corridor through a lava flow or an acid pool. Worth saying twice,
                // because digging is the one case where the destination is deliberately something
                // you cannot currently stand in, and it would be easy to let the hazard slip past
                // with it - a tunnel that has to be dug through lava is not a tunnel worth having.
                if (IsHazardous(next))
                {
                    continue;
                }

                // Must not destroy a floor that something is already standing on up there.
                if (IsStandable(next + new Vector2I(0, -1)))
                {
                    continue;
                }

                // Needs something solid to stand on once it has been carved out.
                if (IsWalkable(GetTile(next + new Vector2I(0, 1))))
                {
                    continue;
                }

                if (UnderminesRoute(cameFrom, start, current, next, AncestorsChecked))
                {
                    continue;
                }

                visited.Add(next);
                cameFrom[next] = current;

                // Finish beside the goal, never directly above or below it. From alongside, breaking
                // in is a single diagonal dig the router can make without a sidestep - and a sidestep
                // would carve a cell this plan never checked, which is how the corridor behind her
                // used to end up undercut.
                if (IsWithinReach(next, goal)
                    && next.X != goal.X
                    && !UnderminesRoute(cameFrom, start, next, goal, AncestorsChecked))
                {
                    return BuildPath(cameFrom, start, next);
                }

                frontier.Enqueue(next);
            }
        }

        return null;
    }

    // True if carving `next` would pull the floor out from under a cell the route just opened.
    private static bool UnderminesRoute(Dictionary<Vector2I, Vector2I> cameFrom, Vector2I start, Vector2I current, Vector2I next, int depth)
    {
        Vector2I above = next + new Vector2I(0, -1);
        Vector2I node = current;

        for (int i = 0; i < depth; i++)
        {
            if (node == above)
            {
                return true;
            }

            if (node == start || !cameFrom.TryGetValue(node, out node))
            {
                break;
            }
        }

        return false;
    }

    public static bool IsWithinReach(Vector2I a, Vector2I b)
    {
        return Mathf.Max(Mathf.Abs(a.X - b.X), Mathf.Abs(a.Y - b.Y)) <= 1;
    }

    // Shortest walkable route from `start` to `goal` through already-dug tunnel cells, or null if unreachable.
    public List<Vector2I> FindTunnelPath(Vector2I start, Vector2I goal)
    {
        if (start == goal)
        {
            return new List<Vector2I> { start };
        }

        if (!IsSafelyStandable(goal))
        {
            return null;
        }

        List<Vector2I> route = FindTunnelPathAStar(start, goal)
            ?? FindTunnelPathBreadthFirst(start, goal);

        // Simplified here rather than in BuildPath, because BuildPath also serves PlanDigRoute,
        // whose output is a carve list rather than a walking route.
        return route == null ? null : SimplifyRoute(route);
    }

    private const int MaxPathVisited = 6000;

    // Costs in tenths, so a diagonal is 14 against a sideways step's 10 and all the arithmetic
    // stays integral. Floats would work and would also make two routes of identical length compare
    // unequal depending on the order their steps happened to be summed.
    private const int StraightCost = 10;
    private const int DiagonalCost = 14;

    // A*, with a diagonal costing what a diagonal actually costs.
    //
    // This was an unweighted breadth-first search, and that is the root of the staircase rather
    // than a detail of it. Under unit costs a zigzag is free: down-right then up-right is two moves
    // for a net two cells sideways, which is exactly what right-then-right costs - so the search
    // was genuinely indifferent between a straight corridor and one that bobbed up and down the
    // whole way, and picked between them on frontier order. SimplifyRoute could tidy the result up
    // afterwards but could not stop it being chosen, and could not recover a route whose real
    // shape had already been thrown away.
    //
    // The heuristic is octile distance. It stays admissible here for a slightly subtle reason: it
    // is computed as though the ant could also step straight up and down, and she cannot. Crediting
    // her with moves she does not have can only make the estimate too small, never too large, which
    // is the direction that keeps A* honest.
    private List<Vector2I> FindTunnelPathAStar(Vector2I start, Vector2I goal)
    {
        var cameFrom = new Dictionary<Vector2I, Vector2I>();
        var costSoFar = new Dictionary<Vector2I, int> { [start] = 0 };
        var frontier = new PriorityQueue<Vector2I, int>();

        frontier.Enqueue(start, Octile(start, goal));

        while (frontier.Count > 0 && costSoFar.Count < MaxPathVisited)
        {
            Vector2I current = frontier.Dequeue();

            if (current == goal)
            {
                return BuildPath(cameFrom, start, goal);
            }

            int reached = costSoFar[current];

            foreach (Vector2I direction in MoveDirections)
            {
                Vector2I next = current + direction;

                if (!IsSafelyStandable(next))
                {
                    continue;
                }

                // Every entry in MoveDirections is either sideways or a unit diagonal, so the Y
                // component is the whole of the question.
                int candidate = reached + (direction.Y == 0 ? StraightCost : DiagonalCost);

                // A cell can be re-reached more cheaply than it was first found, and the queue keeps
                // the stale entry. Comparing against the best cost known is what makes that safe:
                // a worse arrival is dropped here, and a stale dequeue above simply re-expands a
                // cell whose neighbours are all already at least as good.
                if (costSoFar.TryGetValue(next, out int known) && known <= candidate)
                {
                    continue;
                }

                costSoFar[next] = candidate;
                cameFrom[next] = current;

                frontier.Enqueue(next, candidate + Octile(next, goal));
            }
        }

        return null;
    }

    // Diagonal-aware straight-line distance: go diagonally for as long as both axes still need
    // covering, then sideways for the rest.
    private static int Octile(Vector2I from, Vector2I to)
    {
        int dx = Mathf.Abs(to.X - from.X);
        int dy = Mathf.Abs(to.Y - from.Y);

        return StraightCost * (dx + dy) + (DiagonalCost - 2 * StraightCost) * Mathf.Min(dx, dy);
    }

    // Kept as a fallback, and not out of nostalgia.
    //
    // Under a fixed node budget a heuristic search and a breadth-first one fail in different
    // places: A* will pour its whole budget into a promising dead end, where breadth-first would
    // have plodded round the long way and arrived. A route this used to find and now does not is an
    // ant stranded somewhere she cannot walk out of, which is this game's one unrecoverable bug -
    // so when the clever search comes back empty, the plain one gets a turn.
    private List<Vector2I> FindTunnelPathBreadthFirst(Vector2I start, Vector2I goal)
    {
        var cameFrom = new Dictionary<Vector2I, Vector2I>();
        var visited = new HashSet<Vector2I> { start };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(start);

        while (frontier.Count > 0 && visited.Count < MaxPathVisited)
        {
            Vector2I current = frontier.Dequeue();

            foreach (Vector2I direction in MoveDirections)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsSafelyStandable(next))
                {
                    continue;
                }

                visited.Add(next);
                cameFrom[next] = current;

                if (next == goal)
                {
                    return BuildPath(cameFrom, start, goal);
                }

                frontier.Enqueue(next);
            }
        }

        return null;
    }

    private static List<Vector2I> BuildPath(Dictionary<Vector2I, Vector2I> cameFrom, Vector2I start, Vector2I goal)
    {
        List<Vector2I> path = new List<Vector2I> { goal };
        Vector2I current = goal;

        while (current != start)
        {
            current = cameFrom[current];
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    // Drops waypoints an ant does not actually need to walk to.
    //
    // The search is an unweighted breadth-first walk over MoveDirections, where a diagonal costs
    // exactly what a sideways step costs. It therefore *prefers* diagonals, and a run across flat
    // ground comes back as a staircase: down-right, right, down-right, right. The ant then walks
    // that staircase literally, turning ninety degrees every sixteen pixels.
    //
    // This keeps a waypoint only where the route genuinely has to turn. Everything between is on a
    // straight line she can cover in one glide, so the corner count collapses and the motion reads
    // as a path rather than as a grid being traversed.
    //
    // It never invents a move: every kept waypoint came out of the search, and the segments between
    // them are re-checked against the same walkability and no-climbing rules below.
    //
    // ONLY for routes an ant walks. PlanDigRoute returns a list of cells to *excavate*, which
    // DigTowardTarget dequeues and digs one at a time - simplifying that would silently skip
    // excavations and leave the corridor full of holes, and the undermining guards assume every
    // cell in the plan gets carved.
    public List<Vector2I> SimplifyRoute(List<Vector2I> path)
    {
        if (path.Count <= 2)
        {
            return path;
        }

        var smoothed = new List<Vector2I> { path[0] };
        int anchor = 0;

        for (int i = 1; i < path.Count - 1; i++)
        {
            // Look one further: if the ant can go straight from the anchor to path[i + 1], then
            // path[i] is a corner she does not have to turn at.
            if (IsWalkableLine(path[anchor], path[i + 1]))
            {
                continue;
            }

            smoothed.Add(path[i]);
            anchor = i;
        }

        smoothed.Add(path[^1]);

        return smoothed;
    }

    // Whether an ant can walk the straight line between two cells.
    //
    // Two conditions, and the second is the one that matters: every cell stepped through has to be
    // safely standable, AND the line has to descend or climb no faster than one row per column.
    // That second rule is the no-climbing constraint restated - MoveDirections has no vertical
    // entry, so a shortcut steeper than 45 degrees would be a move the ant is not allowed to make,
    // however open the ground between the ends happens to be.
    // Exposed so the tests can assert the property directly against a simplified route.
    public bool IsWalkableLineForTest(Vector2I from, Vector2I to) => IsWalkableLine(from, to);

    private bool IsWalkableLine(Vector2I from, Vector2I to)
    {
        Vector2I delta = to - from;
        int steps = Mathf.Max(Mathf.Abs(delta.X), Mathf.Abs(delta.Y));

        if (steps == 0)
        {
            return true;
        }

        if (Mathf.Abs(delta.Y) > Mathf.Abs(delta.X))
        {
            return false;
        }

        // Walked in whole cells so the check sees exactly the cells the ant will pass through.
        for (int step = 1; step <= steps; step++)
        {
            Vector2I cell = new Vector2I(
                from.X + Mathf.RoundToInt(delta.X * step / (float)steps),
                from.Y + Mathf.RoundToInt(delta.Y * step / (float)steps));

            if (!IsSafelyStandable(cell))
            {
                return false;
            }
        }

        return true;
    }
}
