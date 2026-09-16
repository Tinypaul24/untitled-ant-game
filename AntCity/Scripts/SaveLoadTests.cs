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

    private readonly HashSet<string> preExistingSaves = new();

    public override void _Ready()
    {
        main = GetNode<Node2D>("../Main");
        saveManager = main.GetNode<SaveManager>("SaveManager");
        grid = main.GetNode<GridManager>("GridManager");
        colony = main.GetNode<ColonyManager>("ColonyManager");
        camera = main.GetNode<Camera2D>("Camera2D");

        foreach (SaveSlot slot in saveManager.ListSaves())
        {
            preExistingSaves.Add(slot.Path);
        }

        // Same reason as the terrain suite: these want the colony a few seconds in, not mid-arrival.
        main.GetNode<ColonyFounding>("ColonyFounding").CompleteNow();

        GD.Print("--- save/load tests ---");

        ColonyComesBackAsItWas();
        OrdersAndSelectionComeBack();
        PauseMenuFreezesAndResumes();
        PauseMenuButtonsSaveAndLoad();
        SaveListHoldsEveryColony();
        SimulatedMatterComesBack();

        DiscardSavesMadeByTheseTests();

        GD.Print($"--- {passed} passed, {failed} failed ---");
    }


    // The material grid is now the source of truth for matter, so a reload has to bring back the
    // sand, liquids and heat as well as the tiles. Chunks are run-length encoded on the way out,
    // which is exactly the sort of thing that round-trips wrong without a test.
    private void SimulatedMatterComesBack()
    {
        MaterialWorld materials = main.GetNode<MaterialWorld>("MaterialWorld");

        Vector2I tile = grid.NestCenterCell + new Vector2I(3, 0);
        Vector2I origin = MaterialWorld.TileToCellOrigin(tile);

        // Photographed before anything is written, so the tile can be put back exactly as it was.
        // These tests share one world with the terrain tests that run after them, and this tile is
        // three steps from the nest - leaving lava sitting in it used to be merely untidy, but now
        // that routes avoid hazards it silently moves where ants can walk.
        var original = new List<MaterialId>();

        for (int i = 0; i < MaterialWorld.CellsPerTile; i++)
        {
            original.Add(materials.GetCell(origin + new Vector2I(i % MaterialWorld.CellsPerTileAxis, i / MaterialWorld.CellsPerTileAxis)));
        }

        // A deliberately mixed tile: run-length encoding is at its most fragile where runs are short.
        materials.SetCell(origin, MaterialId.Sand);
        materials.SetCell(origin + new Vector2I(1, 0), MaterialId.Water);
        materials.SetCell(origin + new Vector2I(2, 0), MaterialId.Sand);
        materials.SetCell(origin + new Vector2I(3, 0), MaterialId.Oil);
        materials.SetCell(origin + new Vector2I(0, 1), MaterialId.Lava);

        MaterialDefinition water = MaterialDatabase.Get(MaterialId.Water);
        materials.SetTemperature(origin + new Vector2I(1, 0), 64f, water);

        var expected = new List<MaterialId>();

        for (int i = 0; i < 5; i++)
        {
            expected.Add(materials.GetCell(origin + new Vector2I(i % 4, i / 4)));
        }

        Check(saveManager.Save(), "a colony with simulated matter can be saved");

        // Scrub it, so a passing test cannot be the original state simply never having been touched.
        for (int y = 0; y < MaterialWorld.CellsPerTileAxis; y++)
        {
            for (int x = 0; x < MaterialWorld.CellsPerTileAxis; x++)
            {
                materials.SetCell(origin + new Vector2I(x, y), MaterialId.Air);
            }
        }

        Check(materials.GetCell(origin) == MaterialId.Air, "the matter really was cleared before loading");

        saveManager.Load();

        bool same = true;

        for (int i = 0; i < expected.Count; i++)
        {
            if (materials.GetCell(origin + new Vector2I(i % 4, i / 4)) != expected[i])
            {
                same = false;
            }
        }

        Check(same, "every cell of a mixed tile comes back as it was");
        Check(
            Mathf.Abs(materials.GetTemperature(origin + new Vector2I(1, 0), water) - 64f) <= 1f,
            "a heated cell comes back at its temperature",
            $"got {materials.GetTemperature(origin + new Vector2I(1, 0), water):0.0}"
        );

        // Put the tile back. Everything after this shares the same world.
        for (int i = 0; i < original.Count; i++)
        {
            Vector2I cell = origin + new Vector2I(i % MaterialWorld.CellsPerTileAxis, i / MaterialWorld.CellsPerTileAxis);

            materials.SetCell(cell, original[i]);
            materials.ClearTemperature(cell);
        }

        materials.DeriveDirtyTiles();
    }

    private void DiscardSavesMadeByTheseTests()
    {
        int removed = 0;

        foreach (SaveSlot slot in saveManager.ListSaves())
        {
            if (!preExistingSaves.Contains(slot.Path) && saveManager.DeleteSave(slot.Path))
            {
                removed++;
            }
        }

        Check(saveManager.ListSaves().Count == preExistingSaves.Count,
            "the tests leave no saves of their own behind", $"(cleaned up {removed})");
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

    private void PauseMenuFreezesAndResumes()
    {
        var menu = main.GetNode<PauseMenu>("PauseMenu");
        var ui = main.GetNode<ColonyUI>("UI");

        ui.RestoreSpeed(3f, false);

        Check(!menu.IsOpen, "the menu starts closed");
        Check(Mathf.IsEqualApprox((float)Engine.TimeScale, 3f), "the game runs at the chosen speed");

        menu.Open();

        Check(menu.IsOpen, "the menu opens");
        Check(Engine.TimeScale == 0.0, "opening the menu freezes the game");

        menu.Resume();

        Check(!menu.IsOpen, "resume closes the menu");
        Check(Mathf.IsEqualApprox((float)Engine.TimeScale, 3f), "resume puts the speed back where it was");

        ui.RestoreSpeed(1f, false);
    }

    private void PauseMenuButtonsSaveAndLoad()
    {
        var menu = main.GetNode<PauseMenu>("PauseMenu");

        menu.Open();

        Button save = FindButton(menu, "Save Colony");
        Button load = FindButton(menu, "Load Colony");

        if (save == null || load == null)
        {
            Check(false, "the menu has save and load buttons");
            return;
        }

        int food = colony.Food;

        save.EmitSignal(BaseButton.SignalName.Pressed);

        Check(saveManager.LastMessage == "Colony saved.", "the save button writes a save", $"(said \"{saveManager.LastMessage}\")");
        Check(!load.Disabled, "the load button turns on once a save exists");

        colony.RemoveFood(colony.Food);

        load.EmitSignal(BaseButton.SignalName.Pressed);

        Button slot = FindSaveSlotButton(menu);

        if (slot == null)
        {
            Check(false, "the load button opens a list of saves");
            return;
        }

        Check(true, "the load button opens a list of saves");

        slot.EmitSignal(BaseButton.SignalName.Pressed);

        Check(colony.Food == food, "picking a save from the list restores it", $"(expected {food}, got {colony.Food})");
        Check(Engine.TimeScale == 0.0, "loading from the menu leaves the game frozen");
        Check(menu.IsOpen, "the menu stays open after loading");

        menu.Resume();

        Check(!menu.IsOpen, "the menu closes again afterwards");
        Check(Engine.TimeScale != 0.0, "the game is running again after resuming");
    }

    private void SaveListHoldsEveryColony()
    {
        int before = saveManager.ListSaves().Count;

        saveManager.Save();
        saveManager.Save();

        List<SaveSlot> slots = saveManager.ListSaves();

        Check(slots.Count == before + 2, "every save is kept as its own slot",
            $"(expected {before + 2}, got {slots.Count})");

        bool newestFirst = true;

        for (int i = 1; i < slots.Count; i++)
        {
            newestFirst &= slots[i - 1].SavedAt >= slots[i].SavedAt;
        }

        Check(newestFirst, "the newest save is listed first");
        Check(slots[0].Ants == colony.Ants, "a slot reports the colony it holds",
            $"(expected {colony.Ants} ants, got {slots[0].Ants})");
        Check(slots[0].IsReadable, "a freshly written slot is readable");

        int countBeforeDelete = slots.Count;
        saveManager.DeleteSave(slots[0].Path);

        Check(saveManager.ListSaves().Count == countBeforeDelete - 1, "deleting a save takes it off the list");
    }

    private static Button FindSaveSlotButton(Node node)
    {
        if (node is Button button && button.TooltipText.EndsWith(".json"))
        {
            return button;
        }

        foreach (Node child in node.GetChildren())
        {
            Button found = FindSaveSlotButton(child);

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static Button FindButton(Node node, string text)
    {
        if (node is Button button && button.Text == text)
        {
            return button;
        }

        foreach (Node child in node.GetChildren())
        {
            Button found = FindButton(child, text);

            if (found != null)
            {
                return found;
            }
        }

        return null;
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
