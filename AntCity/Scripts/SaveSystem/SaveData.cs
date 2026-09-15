using System.Collections.Generic;

public class SaveData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public WorldSave World { get; set; } = new();
    public ColonySave Colony { get; set; } = new();
    public ClockSave Clock { get; set; } = new();
    public CameraSave Camera { get; set; } = new();
    public float QueenX { get; set; }
    public float QueenY { get; set; }
    public List<AntSave> Ants { get; set; } = new();
    public List<BroodSave> Eggs { get; set; } = new();
    public List<BroodSave> Larvae { get; set; } = new();
    public List<RoomSave> Rooms { get; set; } = new();
    public ParticleSave Particles { get; set; } = new();
    public float TimeScale { get; set; } = 1f;
    public bool Paused { get; set; }
}

public class WorldSave
{
    public int Seed { get; set; }
    public List<int> Chunks { get; set; } = new();
    public List<int> ModifiedCells { get; set; } = new();
    public List<int> FoodRemaining { get; set; } = new();
    public List<int> GrainsRemoved { get; set; } = new();
}

public class ColonySave
{
    public int Ants { get; set; }
    public int Food { get; set; }
    public int FoodCapacity { get; set; }
    public int Eggs { get; set; }
    public int Larvae { get; set; }
    public int Capacity { get; set; }
    public int NurseryCellTotal { get; set; }
    public int TotalFoodEarned { get; set; }
    public int StarvingIntervalStreak { get; set; }
    public bool LayingEnabled { get; set; }
}

public class ClockSave
{
    public double ElapsedSeconds { get; set; }
    public int ElapsedHours { get; set; }
}

public class CameraSave
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Zoom { get; set; } = 1f;
}

public class AntSave
{
    public float X { get; set; }
    public float Y { get; set; }
    public bool Selected { get; set; }
    public int CarriedFood { get; set; }
    public List<int> CarriedGrains { get; set; } = new();

    public bool HasDigJob { get; set; }
    public int DigTargetX { get; set; }
    public int DigTargetY { get; set; }

    public bool HasForageTarget { get; set; }
    public int ForageTargetX { get; set; }
    public int ForageTargetY { get; set; }
}

public class BroodSave
{
    public float X { get; set; }
    public float Y { get; set; }
    public double SecondsLeft { get; set; }
}

public class RoomSave
{
    public int Type { get; set; }
    public int FootprintX { get; set; }
    public int FootprintY { get; set; }
    public int FootprintWidth { get; set; }
    public int FootprintHeight { get; set; }
    public int State { get; set; }
    public bool FurnishClaimed { get; set; }
    public List<int> PendingDigCells { get; set; } = new();
}

public class ParticleSave
{
    public List<int> Settled { get; set; } = new();
    public List<int> Falling { get; set; } = new();
}
