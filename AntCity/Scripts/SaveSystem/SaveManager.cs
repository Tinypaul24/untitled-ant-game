using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

public partial class SaveManager : Node
{
    private const string SavesDirectory = "user://saves";
    private const string LegacySavePath = "user://savegame.json";

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
    private MaterialWorld materialWorld;
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
        materialWorld = main.GetNode<MaterialWorld>("MaterialWorld");
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

    public bool HasSave => ListSaves().Count > 0;

    public string LastMessage { get; private set; } = string.Empty;

    public List<SaveSlot> ListSaves()
    {
        var slots = new List<SaveSlot>();

        MigrateLegacySave();

        using DirAccess directory = DirAccess.Open(SavesDirectory);

        if (directory == null)
        {
            return slots;
        }

        foreach (string fileName in directory.GetFiles())
        {
            if (!fileName.EndsWith(".json"))
            {
                continue;
            }

            SaveSlot slot = ReadHeader($"{SavesDirectory}/{fileName}");

            if (slot != null)
            {
                slots.Add(slot);
            }
        }

        slots.Sort((a, b) =>
        {
            int byTime = b.SavedAt.CompareTo(a.SavedAt);

            return byTime != 0 ? byTime : string.CompareOrdinal(b.Path, a.Path);
        });

        return slots;
    }

    public bool Save()
    {
        SaveData data = Capture();
        data.SavedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (DirAccess.MakeDirRecursiveAbsolute(SavesDirectory) != Error.Ok)
        {
            return Report(false, "Could not create the saves folder.");
        }

        string path = NextSavePath();

        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);

        if (file == null)
        {
            GD.PushError($"Could not open {path} for writing: {FileAccess.GetOpenError()}");
            return Report(false, "Save failed.");
        }

        file.StoreString(JsonSerializer.Serialize(data, JsonOptions));

        GD.Print($"Saved the colony to {path}");
        return Report(true, "Colony saved.");
    }

    public bool Load()
    {
        List<SaveSlot> slots = ListSaves();

        if (slots.Count == 0)
        {
            return Report(false, "No saved colony to load.");
        }

        return Load(slots[0].Path);
    }

    public bool Load(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            return Report(false, "That save is no longer there.");
        }

        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);

        if (file == null)
        {
            GD.PushError($"Could not open {path} for reading: {FileAccess.GetOpenError()}");
            return Report(false, "Load failed.");
        }

        SaveData data;

        try
        {
            data = JsonSerializer.Deserialize<SaveData>(file.GetAsText(), JsonOptions);
        }
        catch (JsonException exception)
        {
            GD.PushError($"Save file is not readable: {exception.Message}");
            return Report(false, "Save file is damaged.");
        }

        if (data == null || data.Version != SaveData.CurrentVersion)
        {
            GD.PushError($"Save file version {data?.Version} does not match {SaveData.CurrentVersion}.");
            return Report(false, "Save file is from a different version.");
        }

        Restore(data);

        GD.Print($"Colony loaded from {path}");
        return Report(true, "Colony loaded.");
    }

    public bool DeleteSave(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            return Report(false, "That save is no longer there.");
        }

        Error error = DirAccess.RemoveAbsolute(path);

        if (error != Error.Ok)
        {
            GD.PushError($"Could not delete {path}: {error}");
            return Report(false, "Could not delete that save.");
        }

        return Report(true, "Save deleted.");
    }

    private SaveSlot ReadHeader(string path)
    {
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);

        if (file == null)
        {
            return null;
        }

        try
        {
            SaveHeader header = JsonSerializer.Deserialize<SaveHeader>(file.GetAsText(), JsonOptions);

            if (header == null)
            {
                return null;
            }

            long savedAtMs = header.SavedAtUnixMs > 0
                ? header.SavedAtUnixMs
                : (long)FileAccess.GetModifiedTime(path) * 1000L;

            return new SaveSlot
            {
                Path = path,
                Version = header.Version,
                SavedAt = DateTimeOffset.FromUnixTimeMilliseconds(savedAtMs).LocalDateTime,
                Ants = header.Colony?.Ants ?? 0,
                Food = header.Colony?.Food ?? 0,
                Hours = header.Clock?.ElapsedHours ?? 0,
            };
        }
        catch (JsonException)
        {
            GD.PushWarning($"Skipping unreadable save {path}");
            return null;
        }
    }

    private static string NextSavePath()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = $"{SavesDirectory}/colony_{stamp}.json";

        for (int suffix = 2; FileAccess.FileExists(path); suffix++)
        {
            path = $"{SavesDirectory}/colony_{stamp}_{suffix}.json";
        }

        return path;
    }

    private static void MigrateLegacySave()
    {
        if (!FileAccess.FileExists(LegacySavePath))
        {
            return;
        }

        if (DirAccess.MakeDirRecursiveAbsolute(SavesDirectory) != Error.Ok)
        {
            return;
        }

        string destination = $"{SavesDirectory}/colony_previous.json";

        if (!FileAccess.FileExists(destination) && DirAccess.RenameAbsolute(LegacySavePath, destination) == Error.Ok)
        {
            GD.Print($"Moved the old single save into {destination}");
        }
    }

    private bool Report(bool succeeded, string message)
    {
        LastMessage = message;
        colonyManager.RaiseAlert(message);

        return succeeded;
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
            Materials = materialWorld.CaptureState(),
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
        materialWorld.RestoreState(data.Materials);

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
