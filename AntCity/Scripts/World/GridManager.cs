using Godot;
using System.Collections.Generic;

public partial class GridManager : Node2D
{
    public enum TileType
    {
        Dirt,
        Tunnel
    }

    [Export]
    public int Width { get; set; } = 40;

    [Export]
    public int Height { get; set; } = 25;

    [Export]
    public int CellSize { get; set; } = 16;

    [Export]
    public TileMapLayer Ground { get; set; }

    private static readonly Vector2I[] Directions =
    {
        new Vector2I(0, -1), // Up
        new Vector2I(0, 1),  // Down
        new Vector2I(-1, 0), // Left
        new Vector2I(1, 0)   // Right
    };

    // These are the coordinates from your tileset.
    private Vector2I dirtTile = new Vector2I(0, 1);
    private Vector2I tunnelTile = new Vector2I(0, 0);

    private TileType[,] grid;

    public override void _Ready()
    {
        grid = new TileType[Width, Height];

        // Start the entire map as dirt.
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                grid[x, y] = TileType.Dirt;

                Ground.SetCell(
                    new Vector2I(x, y),
                    0,
                    dirtTile
                );
            }
        }

        CreateStartingNest();

        GD.Print("Grid created!");
    }

    public Vector2I WorldToCell(Vector2 worldPosition)
    {
        return new Vector2I(
            Mathf.FloorToInt(worldPosition.X / CellSize),
            Mathf.FloorToInt(worldPosition.Y / CellSize)
        );
    }

    public Vector2 CellToWorld(Vector2I cell)
    {
        return new Vector2(
            cell.X * CellSize + CellSize / 2f,
            cell.Y * CellSize + CellSize / 2f
        );
    }

    public bool IsInBounds(Vector2I cell)
    {
        return cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;
    }

    public bool IsDirt(Vector2I cell)
    {
        return IsInBounds(cell) && grid[cell.X, cell.Y] == TileType.Dirt;
    }

    public void Dig(Vector2I cell)
    {
        if (!IsInBounds(cell) || grid[cell.X, cell.Y] != TileType.Dirt)
        {
            return;
        }

        // Change logical tile.
        grid[cell.X, cell.Y] = TileType.Tunnel;

        // Change visual tile.
        Ground.SetCell(
            cell,
            0,
            tunnelTile
        );
    }

    // The next cell to step into when walking a straight line from `from` toward `to`.
    public Vector2I GetStepToward(Vector2I from, Vector2I to)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;

        if (Mathf.Abs(dx) >= Mathf.Abs(dy))
        {
            return from + new Vector2I(Mathf.Sign(dx), 0);
        }

        return from + new Vector2I(0, Mathf.Sign(dy));
    }

    // The closest already-dug tunnel cell to `from`, reached by expanding outward regardless of tile type.
    public Vector2I FindNearestTunnelCell(Vector2I from)
    {
        if (IsInBounds(from) && grid[from.X, from.Y] == TileType.Tunnel)
        {
            return from;
        }

        var visited = new HashSet<Vector2I> { from };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(from);

        while (frontier.Count > 0)
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

                if (grid[next.X, next.Y] == TileType.Tunnel)
                {
                    return next;
                }

                frontier.Enqueue(next);
            }
        }

        // No tunnel exists anywhere yet.
        return from;
    }

    // Shortest walkable route from `start` to `goal` through already-dug tunnel cells, or null if unreachable.
    public List<Vector2I> FindTunnelPath(Vector2I start, Vector2I goal)
    {
        if (start == goal)
        {
            return new List<Vector2I> { start };
        }

        var cameFrom = new Dictionary<Vector2I, Vector2I>();
        var visited = new HashSet<Vector2I> { start };
        var frontier = new Queue<Vector2I>();
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            Vector2I current = frontier.Dequeue();

            foreach (Vector2I direction in Directions)
            {
                Vector2I next = current + direction;

                if (visited.Contains(next) || !IsInBounds(next) || grid[next.X, next.Y] != TileType.Tunnel)
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

    private void CreateStartingNest()
    {
        // Put the nest in the middle of the map.
        int centerX = Width / 2;
        int centerY = Height / 2;

        // Create a small 5x5 room.
        for (int x = centerX - 2; x <= centerX + 2; x++)
        {
            for (int y = centerY - 2; y <= centerY + 2; y++)
            {
                Dig(new Vector2I(x, y));
            }
        }

        GD.Print("Starting nest created!");
    }
}
