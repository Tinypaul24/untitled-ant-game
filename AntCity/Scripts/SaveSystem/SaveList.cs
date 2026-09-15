using Godot;
using System.Collections.Generic;

public static class SaveList
{
    public static void AddCell(this List<int> list, Vector2I cell)
    {
        list.Add(cell.X);
        list.Add(cell.Y);
    }

    public static void AddCell(this List<int> list, Vector2I cell, int value)
    {
        list.Add(cell.X);
        list.Add(cell.Y);
        list.Add(value);
    }

    public static IEnumerable<Vector2I> ReadCells(this List<int> list)
    {
        for (int i = 0; i + 1 < list.Count; i += 2)
        {
            yield return new Vector2I(list[i], list[i + 1]);
        }
    }

    public static IEnumerable<(Vector2I Cell, int Value)> ReadCellValues(this List<int> list)
    {
        for (int i = 0; i + 2 < list.Count; i += 3)
        {
            yield return (new Vector2I(list[i], list[i + 1]), list[i + 2]);
        }
    }
}
