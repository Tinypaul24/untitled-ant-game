using Godot;
using System.Collections.Generic;

public partial class SaveLoadTests : Node
{
    private Node2D main;
    private SaveManager saveManager;
    private GridManager grid;
    private ColonyManager colony;
    private Camera2D camera;

    private int passed;
    private int failed;

    public override void _Ready()
    {
        main = GetNode<Node2D>("../Main");
        saveManager = main.GetNode<SaveManager>("SaveManager");
        grid = main.GetNode<GridManager>("GridManager");
        colony = main.GetNode<ColonyManager>("ColonyManager");
        camera = main.GetNode<Camera2D>("Camera2D");

        GD.Print("--- save/load tests ---");

        ColonyComesBackAsItWas();
        OrdersAndSelectionComeBack();

        GD.Print($"--- {passed} passed, {failed} failed ---");
    }

    private void ColonyComesBackAsItWas()
    {
        colony.AddFood(17);
        camera.Position = new Vector2(1234f, 567f);
        camera.Zoom = new Vector2(1.75f, 1.75f);

        List<Vector2> antPositions = AntPositions();
        int food = colony.Food;
        int capacity = colony.Capacity;
        Vector2 cameraPosition = camera.Position;
        float cameraZoom = camera.Zoom.X;
        Vector2I digTarget = FindDiggableNear(grid.NestCenterCell + new Vector2I(4, 4));
        bool targetWasDiggable = grid.CanDig(digTarget);

        Check(antPositions.Count > 0, "there are ants to save");
        Check(targetWasDiggable, "there is solid ground to dig after saving");

        saveManager.Save();

        colony.RemoveFood(colony.Food);
        colony.IncreaseCapacity(99);
        camera.Position = Vector2.Zero;
        camera.Zoom = Vector2.One;
        grid.Dig(digTarget);

        foreach (Node child in main.GetChildren())
        {
            if (child is AntWorker ant)
            {
                ant.Position += new Vector2(400f, 0f);
            }
        }

        Check(colony.Food != food, "the colony really was changed before loading");
        Check(!grid.CanDig(digTarget), "the ground really was dug before loading");

        saveManager.Load();

        List<Vector2> restored = AntPositions();

        Check(restored.Count == antPositions.Count, "every ant comes back",
            $"({antPositions.Count} saved, {restored.Count} loaded)");

        bool samePlaces = restored.Count == antPositions.Count;

        for (int i = 0; i < restored.Count && samePlaces; i++)
        {
            samePlaces = restored[i].DistanceTo(antPositions[i]) < 0.01f;
        }

        Check(samePlaces, "every ant is standing where she was saved");
        Check(colony.Food == food, "food comes back", $"(expected {food}, got {colony.Food})");
        Check(colony.Capacity == capacity, "population capacity comes back");
        Check(grid.CanDig(digTarget), "ground dug after saving is solid again");
        Check(camera.Position.DistanceTo(cameraPosition) < 0.01f, "the view comes back to the same spot");
        Check(Mathf.Abs(camera.Zoom.X - cameraZoom) < 0.001f, "the view comes back at the same zoom");
    }

    private void OrdersAndSelectionComeBack()
    {
        var selection = main.GetNode<SelectionManager>("SelectionManager");
        AntWorker ant = FirstAnt();

        if (ant == null)
        {
            Check(false, "there is an ant to give orders to");
            return;
        }

        Vector2I target = FindDiggableNear(grid.NestCenterCell + new Vector2I(3, 1));
        ant.CommandDig(target);
        selection.Select(ant);

        AntSave before = ant.CaptureState();

        Check(before.HasDigJob, "an ordered ant records her dig job");
        Check(before.Selected, "a selected ant records that she is selected");

        saveManager.Save();
        saveManager.Load();

        AntWorker restoredAnt = FindAntNear(new Vector2(before.X, before.Y));

        if (restoredAnt == null)
        {
            Check(false, "the ordered ant comes back");
            return;
        }

        AntSave after = restoredAnt.CaptureState();

        Check(after.HasDigJob, "she is still on the same job after loading");
        Check(after.DigTargetX == before.DigTargetX && after.DigTargetY == before.DigTargetY,
            "she is still digging the same cell",
            $"(was {before.DigTargetX},{before.DigTargetY}; now {after.DigTargetX},{after.DigTargetY})");
        Check(after.Selected, "she is still selected after loading");
    }

    private AntWorker FirstAnt()
    {
        foreach (Node child in main.GetChildren())
        {
            if (child is AntWorker ant)
            {
                return ant;
            }
        }

        return null;
    }

    private AntWorker FindAntNear(Vector2 position)
    {
        foreach (Node child in main.GetChildren())
        {
            if (child is AntWorker ant && ant.Position.DistanceTo(position) < 0.01f)
            {
                return ant;
            }
        }

        return null;
    }

    private List<Vector2> AntPositions()
    {
        var positions = new List<Vector2>();

        foreach (Node child in main.GetChildren())
        {
            if (child is AntWorker ant)
            {
                positions.Add(ant.Position);
            }
        }

        positions.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

        return positions;
    }

    private Vector2I FindDiggableNear(Vector2I wanted)
    {
        for (int radius = 0; radius < 8; radius++)
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
