using System.Collections.Generic;

public class SaveData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public long SavedAtUnixMs { get; set; }
    public WorldSave World { get; set; } = new();
    public ColonySave Colony { get; set; } = new();
    public ClockSave Clock { get; set; } = new();
    public CameraSave Camera { get; set; } = new();
    public float QueenX { get; set; }
    public float QueenY { get; set; }

    // Whether she is on the ground yet, and how far through her current clutch. Save during the
    // founding flight and she used to come back grounded, laying eggs into a burrow nobody dug.
    //
    // Defaulted true and deliberately not version-bumped: System.Text.Json leaves an absent property
    // at its C# default, so every save written before this existed reads back as a grounded queen,
    // which is what she was. Bumping the version would grey out every one of them in the load menu.
    public bool QueenGrounded { get; set; } = true;
    public double QueenLayAccumulator { get; set; }
    public List<AntSave> Ants { get; set; } = new();
    public List<BroodSave> Eggs { get; set; } = new();
    public List<BroodSave> Larvae { get; set; } = new();
    public List<RoomSave> Rooms { get; set; } = new();

    // Cell X, cell Y, batch, repeating - the same packing AddCell/ReadCellValues already use for
    // grid data. Absent in every save written before the dig tool existed, which reads back as no
    // designations, correctly.
    public List<int> Designations { get; set; } = new();
    public MaterialSave Materials { get; set; } = new();
    public float TimeScale { get; set; } = 1f;
    public bool Paused { get; set; }
}

public class SaveHeader
{
    public int Version { get; set; }
    public long SavedAtUnixMs { get; set; }
    public ColonySave Colony { get; set; } = new();
    public ClockSave Clock { get; set; } = new();
}

public class SaveSlot
{
    public string Path { get; set; } = string.Empty;
    public System.DateTime SavedAt { get; set; }
    public int Version { get; set; }
    public int Ants { get; set; }
    public int Food { get; set; }
    public int Hours { get; set; }

    public bool IsReadable => Version == SaveData.CurrentVersion;

    public string Describe()
    {
        if (!IsReadable)
        {
            return $"{SavedAt:dd MMM HH:mm:ss}  —  different version";
        }

        return $"{SavedAt:dd MMM HH:mm:ss}  —  {Ants} ants, {Food} food, {Hours}h";
    }
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
    public int FungusCellTotal { get; set; }
    public int RoyalCellTotal { get; set; }
    public int TotalFoodEarned { get; set; }
    public int StarvingIntervalStreak { get; set; }
    public bool LayingEnabled { get; set; }

    // Minus one means "this save predates crew quotas", which is not the same as a player who
    // deliberately set zero foragers. Absent properties keep their initialiser, so the sentinel
    // does the version check that the version number deliberately does not.
    public int ForagerQuota { get; set; } = -1;
    public int BuilderQuota { get; set; } = -1;
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

    // Absent from a save written before roles existed, which System.Text.Json leaves at the default
    // - Digger. That is the right answer for an old colony: the first rebalance after the load
    // moves whoever is needed onto the other trades. No version bump, because SaveSlot.IsReadable
    // compares versions exactly and raising it would grey out every save anybody already has.
    public AntRole Role { get; set; }
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

public class MaterialSave
{
    public List<MaterialChunkSave> Chunks { get; set; } = new();

    // Cell, then remaining ticks.
    public List<int> Lifetimes { get; set; } = new();

    // Cell, then whole degrees.
    public List<int> Temperatures { get; set; } = new();
}

public class MaterialChunkSave
{
    public int X { get; set; }
    public int Y { get; set; }

    // Alternating material id and run length.
    public List<int> Runs { get; set; } = new();
}
