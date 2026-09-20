using Godot;
using System;
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

    // Read by the terrain suite, which runs last and turns the pair of them into an exit status.
    public int Failed => failed;

    private TestSaveDirectory storage;

    public override void _Ready()
    {
        main = GetNode<Node2D>("../Main");
        saveManager = main.GetNode<SaveManager>("SaveManager");
        grid = main.GetNode<GridManager>("GridManager");
        colony = main.GetNode<ColonyManager>("ColonyManager");
        camera = main.GetNode<Camera2D>("Camera2D");

        // Same reason as the terrain suite: these want the colony a few seconds in, not mid-arrival.
        main.GetNode<ColonyFounding>("ColonyFounding").CompleteNow();

        GD.Print("--- save/load tests ---");

        // Storage is taken over before the first save operation of any kind - listing included,
        // because listing is what migrates a legacy save into the folder being listed.
        using (storage = new TestSaveDirectory(saveManager))
        {
            try
            {
                TheseTestsNeverTouchThePlayersSaves();
                ColonyComesBackAsItWas();
                NothingIsHeldOverFromTheOldWorld();
                TheFoundingDoesNotStartAgainAfterALoad();
                TheQueenComesBackAsSheWas();
                OrdersAndSelectionComeBack();
                PauseMenuFreezesAndResumes();
                PauseMenuButtonsSaveAndLoad();
                SaveListHoldsEveryColony();
                SimulatedMatterComesBack();

                DiscardSavesMadeByTheseTests();
            }
            catch (InvalidOperationException stopped)
            {
                // A save that did not write, a load that did not read, a delete of something this
                // run does not own. The previous behaviour was to press on regardless, and the one
                // time it mattered the suite carried a failed write all the way to the delete and
                // destroyed a colony the player had saved. There is nothing useful past this point.
                failed++;
                GD.PrintErr($"  STOP  {stopped.Message}");
            }
        }

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

        // Save throws unless the file really landed in this run's own folder, so getting a path
        // back at all is the assertion.
        Check(!string.IsNullOrEmpty(storage.Save()), "a colony with simulated matter can be saved");

        // Scrub it, so a passing test cannot be the original state simply never having been touched.
        for (int y = 0; y < MaterialWorld.CellsPerTileAxis; y++)
        {
            for (int x = 0; x < MaterialWorld.CellsPerTileAxis; x++)
            {
                materials.SetCell(origin + new Vector2I(x, y), MaterialId.Air);
            }
        }

        Check(materials.GetCell(origin) == MaterialId.Air, "the matter really was cleared before loading");

        storage.Load();

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

    // The check that has to hold before any other save test is allowed to run.
    //
    // This suite writes saves, lists them and deletes one. Pointed at the folder the player keeps
    // their colonies in, that is not a test suite, it is a shredder - and it has already destroyed
    // a real save once, on a run whose writes were failing and which carried on to the delete
    // regardless. Storage is handed to the suite, never discovered: a directory of its own, never
    // the player's, empty when it starts.
    private void TheseTestsNeverTouchThePlayersSaves()
    {
        Check(saveManager.SavesDirectory != SaveManager.DefaultSavesDirectory,
            "the suite saves into a folder of its own, not the player's",
            $"(was writing to {saveManager.SavesDirectory})");

        int alreadyThere = saveManager.ListSaves().Count;

        Check(alreadyThere == 0, "and that folder starts out empty",
            $"({alreadyThere} saves were already in it)");
    }

    // Cleanup by inventory, not by subtraction.
    //
    // This used to list the folder and delete whatever was not in it when the run started, which is
    // only ever as safe as that opening snapshot. Now every file the suite writes is recorded as it
    // is written, and those are the only ones that can be removed.
    private void DiscardSavesMadeByTheseTests()
    {
        storage.DeleteCreatedFiles();

        int left = saveManager.ListSaves().Count;

        Check(left == 0, "the tests leave no saves of their own behind", $"({left} left over)");
    }


    // Nothing that belonged to the old world survives into the new one.
    //
    // Every one of these is transient or derived state that is neither saved nor was cleared, so it
    // leaked across a load and stayed there for the rest of the session. The forage claim is the
    // worst of them: a claimed cell is skipped by every future forager, so a deposit claimed at the
    // moment of loading became permanently invisible to the whole colony.
    private void NothingIsHeldOverFromTheOldWorld()
    {
        var build = main.GetNode<BuildManager>("BuildManager");
        var pheromones = main.GetNode<PheromoneField>("PheromoneField");

        // A claim, a trail and a placement in flight - the three shapes of leak.
        Vector2I foodCell = default;
        bool haveFood = grid.TryFindForageTarget(grid.CellToWorld(grid.NestCenterCell), 40f * grid.CellSize, out foodCell);

        if (haveFood)
        {
            grid.ClaimForageCell(foodCell);
        }

        pheromones.Deposit(grid.NestCenterCell);
        build.BeginPlacement(BuildingType.Granary);

        Check(pheromones.MarkedCells > 0, "there is a trail to lose", $"{pheromones.MarkedCells} cells");

        storage.Save();
        storage.Load();

        Check(pheromones.MarkedCells == 0, "trails from the old world are gone",
            $"{pheromones.MarkedCells} cells survived");
        Check(!build.IsPlacing, "a placement in flight does not survive a load");
        Check(build.Selected == null, "and neither does a selected room");
        Check(build.ClaimedDigCellCount == 0, "no dig cell is still claimed",
            $"{build.ClaimedDigCellCount} claimed");
        Check(AntWorker.StallRescues == 0 && AntWorker.SpoilLeftovers == 0,
            "the diagnostic counters start from zero",
            $"{AntWorker.StallRescues} stalls, {AntWorker.SpoilLeftovers} leftovers");

        if (haveFood)
        {
            Check(grid.TryFindForageTarget(grid.CellToWorld(foodCell), 3f * grid.CellSize, out _),
                "a food cell claimed when the save loaded is offered again");
        }
    }

    // The founding sequence does not restart on top of a loaded colony.
    //
    // Main creates ColonyFounding at runtime and nothing told it about loading, so a load during the
    // six-second intro left it running: it carried on cutting its shaft into the restored world,
    // teleported the Queen, spawned another set of starting workers on top of the restored
    // population, and announced the colony founded on a save that might be an hour old.
    private void TheFoundingDoesNotStartAgainAfterALoad()
    {
        var founding = main.GetNode<ColonyFounding>("ColonyFounding");

        storage.Save();

        int antsBefore = AntPositions().Count;

        storage.Load();

        Check(founding.Finished, "the founding is over once a save is loaded");
        Check(AntPositions().Count == antsBefore, "and it has not spawned another set of workers",
            $"{antsBefore} became {AntPositions().Count}");
    }

    // A queen saved mid-flight comes back mid-flight.
    private void TheQueenComesBackAsSheWas()
    {
        var queen = main.GetNode<Queen>("Queen");

        queen.Grounded = false;
        queen.LayAccumulator = 4.5;

        storage.Save();

        queen.Grounded = true;
        queen.LayAccumulator = 0;

        storage.Load();

        Check(!main.GetNode<Queen>("Queen").Grounded, "a queen saved in the air is still in the air");
        Check(Mathf.Abs(main.GetNode<Queen>("Queen").LayAccumulator - 4.5) < 0.001,
            "and her clutch timer is where she left it",
            $"{main.GetNode<Queen>("Queen").LayAccumulator}");

        main.GetNode<Queen>("Queen").Grounded = true;

    }
    private void ColonyComesBackAsItWas()
    {
        colony.AddFood(17);
        camera.Position = new Vector2(1234f, 567f);

        // A legal zoom. This was 1.75, which round-tripped exactly and was asserted to - but the
        // whole-number rule exists because a 12px ant drawn at a fraction of a pixel turns to mush,
        // and loading a save was the one route that bypassed it.
        camera.Zoom = new Vector2(3f, 3f);

        List<Vector2> antPositions = AntPositions();
        int food = colony.Food;
        int capacity = colony.Capacity;
        Vector2 cameraPosition = camera.Position;
        float cameraZoom = camera.Zoom.X;
        Vector2I digTarget = FindDiggableNear(grid.NestCenterCell + new Vector2I(4, 4));
        bool targetWasDiggable = grid.CanDig(digTarget);

        Check(antPositions.Count > 0, "there are ants to save");
        Check(targetWasDiggable, "there is solid ground to dig after saving");

        storage.Save();

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

        storage.Load();

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

        // And a save carrying an illegal one is snapped to the nearest legal zoom rather than
        // restored as written.
        var controller = camera as CameraController;
        controller?.SetZoomLevel(1.75f);

        Check(controller == null || Mathf.IsEqualApprox(camera.Zoom.X, 2f),
            "a fractional zoom is snapped to a whole one", $"{camera.Zoom.X}");

        controller?.SetZoomLevel(cameraZoom);
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

        storage.Save();
        storage.Load();

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

        // The button goes to the real manager rather than through the test's own Save, so the file
        // it wrote has to be claimed by hand or nothing would ever clean it up.
        storage.RecordSavedFile();

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

        storage.Save();
        storage.Save();

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
        // Through the owned storage, which refuses any path this run did not write. That refusal is
        // the whole point: this line, against the real folder, is what deleted a player's colony.
        storage.Delete(slots[0].Path);

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
