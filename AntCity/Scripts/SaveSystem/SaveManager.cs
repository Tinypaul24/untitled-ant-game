using Godot;
using System.Collections.Generic;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

public partial class SaveManager : Node
{
    private const string SavePath = "user://savegame.json";

    private static readonly PackedScene AntWorkerScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/AntWorker.tscn");
    private static readonly PackedScene EggScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/Egg.tscn");
    private static readonly PackedScene LarvaScene = GD.Load<PackedScene>("res://AntCity/Scenes/Entities/Larva.tscn");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private Node2D main;
    private GridManager gridManager;
    private ColonyManager colonyManager;
    private GameClock gameClock;
    private BuildManager buildManager;
    private ParticleField particleField;
    private SelectionManager selectionManager;
    private ColonyUI colonyUI;
    private Camera2D camera;
    private Node2D queen;

    public override void _Ready()
    {
        main = GetParent<Node2D>();

        gridManager = main.GetNode<GridManager>("GridManager");
        colonyManager = main.GetNode<ColonyManager>("ColonyManager");
        gameClock = main.GetNode<GameClock>("GameClock");
        buildManager = main.GetNode<BuildManager>("BuildManager");
        particleField = main.GetNode<ParticleField>("ParticleField");
        selectionManager = main.GetNode<SelectionManager>("SelectionManager");
        colonyUI = main.GetNode<ColonyUI>("UI");
        camera = main.GetNode<Camera2D>("Camera2D");
        queen = main.GetNode<Node2D>("Queen");
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (key.Keycode == Key.F5)
        {
            Save();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.F9)
        {
            Load();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Save()
    {
        SaveData data = Capture();

        using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);

        if (file == null)
        {
            GD.PushError($"Could not open {SavePath} for writing: {FileAccess.GetOpenError()}");
            colonyManager.RaiseAlert("Save failed.");
            return;
        }

        file.StoreString(JsonSerializer.Serialize(data, JsonOptions));

        GD.Print($"Saved the colony to {SavePath}");
        colonyManager.RaiseAlert("Colony saved.");
    }

    public void Load()
    {
        if (!FileAccess.FileExists(SavePath))
        {
            colonyManager.RaiseAlert("No saved colony to load.");
            return;
        }

        using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);

        if (file == null)
        {
            GD.PushError($"Could not open {SavePath} for reading: {FileAccess.GetOpenError()}");
            colonyManager.RaiseAlert("Load failed.");
            return;
        }

        SaveData data;

        try
        {
            data = JsonSerializer.Deserialize<SaveData>(file.GetAsText(), JsonOptions);
        }
        catch (JsonException exception)
        {
            GD.PushError($"Save file is not readable: {exception.Message}");
            colonyManager.RaiseAlert("Save file is damaged.");
            return;
        }

        if (data == null || data.Version != SaveData.CurrentVersion)
        {
            GD.PushError($"Save file version {data?.Version} does not match {SaveData.CurrentVersion}.");
            colonyManager.RaiseAlert("Save file is from a different version.");
            return;
        }

        Restore(data);

        GD.Print("Colony loaded.");
        colonyManager.RaiseAlert("Colony loaded.");
    }

    private SaveData Capture()
    {
        var data = new SaveData
        {
            World = gridManager.CaptureState(),
            Colony = colonyManager.CaptureState(),
            Clock = gameClock.CaptureState(),
            Camera = new CameraSave { X = camera.Position.X, Y = camera.Position.Y, Zoom = camera.Zoom.X },
            QueenX = queen.Position.X,
            QueenY = queen.Position.Y,
            Rooms = buildManager.CaptureRooms(),
            Particles = particleField.CaptureState(),
            TimeScale = colonyUI.CurrentSpeed,
            Paused = colonyUI.IsPaused,
        };

        foreach (Node child in main.GetChildren())
        {
            switch (child)
            {
                case AntWorker ant:
                    data.Ants.Add(ant.CaptureState());
                    break;
                case Egg egg:
                    data.Eggs.Add(new BroodSave { X = egg.Position.X, Y = egg.Position.Y, SecondsLeft = egg.SecondsLeft });
                    break;
                case Larva larva:
                    data.Larvae.Add(new BroodSave { X = larva.Position.X, Y = larva.Position.Y, SecondsLeft = larva.SecondsLeft });
                    break;
            }
        }

        return data;
    }

    private void Restore(SaveData data)
    {
        selectionManager.ClearAll();
        FreeLivingEntities();


        gridManager.RestoreState(data.World);
        buildManager.RestoreRooms(data.Rooms);
        particleField.RestoreState(data.Particles);

        queen.Position = new Vector2(data.QueenX, data.QueenY);

        foreach (AntSave save in data.Ants)
        {
            var ant = AntWorkerScene.Instantiate<AntWorker>();
            ant.Position = new Vector2(save.X, save.Y);
            main.AddChild(ant);
            ant.RestoreState(save);
        }

        foreach (BroodSave save in data.Eggs)
        {
            var egg = EggScene.Instantiate<Egg>();
            egg.Position = new Vector2(save.X, save.Y);
            egg.RestoreSecondsLeft = save.SecondsLeft;
            main.AddChild(egg);
        }

        foreach (BroodSave save in data.Larvae)
        {
            var larva = LarvaScene.Instantiate<Larva>();
            larva.Position = new Vector2(save.X, save.Y);
            larva.RestoreSecondsLeft = save.SecondsLeft;
            main.AddChild(larva);
        }

        colonyManager.RestoreState(data.Colony);
        gameClock.RestoreState(data.Clock);

        camera.Position = new Vector2(data.Camera.X, data.Camera.Y);
        camera.Zoom = new Vector2(data.Camera.Zoom, data.Camera.Zoom);

        colonyUI.RestoreSpeed(data.TimeScale, data.Paused);
    }

    private void FreeLivingEntities()
    {
        var doomed = new List<Node>();

        foreach (Node child in main.GetChildren())
        {
            if (child is AntWorker or Egg or Larva)
            {
                doomed.Add(child);
            }
        }

        foreach (Node node in doomed)
        {
            main.RemoveChild(node);
            node.QueueFree();
        }
    }
}
