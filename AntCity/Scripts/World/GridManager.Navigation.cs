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

        if (IsStandable(from))
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

                if (IsStandable(next))
                {
                    return next;
                }

                frontier.Enqueue(next);
            }
        }

        // No tunnel is reachable nearby.
        return from;
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
        const int MaxVisited = 6000;

        if (start == goal)
        {
            return new List<Vector2I> { start };
        }

        if (!IsStandable(goal))
        {
            return null;
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

                if (visited.Contains(next) || !IsStandable(next))
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
}
