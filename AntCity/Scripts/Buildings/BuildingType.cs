using System.Collections.Generic;

public enum BuildingType
{
    NestingChamber,
    Granary,
    Nursery,
    FungusFarm,
    RoyalChamber
}

public readonly struct BuildingDef
{
    public string Name { get; init; }
    public string Description { get; init; }
    public int FoodCostPerCell { get; init; }
    public float FurnishSecondsPerCell { get; init; }
    public float EffectPerCell { get; init; }

    // What one cell of it does, in the player's own units.
    //
    // Tooltips used to state the cost and nothing else, so the only way to find out what a room was
    // worth was to build one and watch a number move. Choosing between rooms is the whole of the
    // build decision and it was being made blind.
    public string EffectSummary { get; init; }
}

public static class BuildingDefs
{
    public static readonly Dictionary<BuildingType, BuildingDef> All = new()
    {
        {
            BuildingType.NestingChamber,
            new BuildingDef
            {
                Name = "Nesting Chamber",
                Description = "Raises the colony's population cap, so it can support more ants, eggs, and larvae at once.",
                FoodCostPerCell = 3,
                FurnishSecondsPerCell = 0.5f,
                EffectPerCell = 1f,
                EffectSummary = "+1 population capacity per cell"
            }
        },
        {
            BuildingType.Granary,
            new BuildingDef
            {
                Name = "Granary",
                Description = "Raises how much food the colony can stockpile, so surplus foraging isn't wasted.",
                FoodCostPerCell = 2,
                FurnishSecondsPerCell = 0.4f,
                EffectPerCell = 8f,
                EffectSummary = "+8 food storage per cell"
            }
        },
        {
            BuildingType.Nursery,
            new BuildingDef
            {
                Name = "Nursery",
                Description = "Speeds up how fast eggs hatch and larvae mature into ants. Stacks across every nursery cell built.",
                FoodCostPerCell = 3,
                FurnishSecondsPerCell = 0.5f,
                EffectPerCell = 0.05f,
                EffectSummary = "5% faster hatching per cell, compounding"
            }
        },
        {
            // Leafcutters do not eat leaves. They compost them to grow a fungus, and the fungus is
            // the food - a colony that farms rather than forages. This is the one room that feeds
            // the colony without anybody having to walk anywhere, which is why it is the dearest.
            BuildingType.FungusFarm,
            new BuildingDef
            {
                Name = "Fungus Farm",
                Description = "A composting chamber where the colony grows its own food. Yields every hour without a forager having to leave the nest.",
                FoodCostPerCell = 5,
                FurnishSecondsPerCell = 0.8f,
                EffectPerCell = 1f,
                EffectSummary = "+1 food per hour per cell"
            }
        },
        {
            // A real nest has a chamber the queen barely leaves, kept warm and attended. Tended
            // well, she lays faster.
            BuildingType.RoyalChamber,
            new BuildingDef
            {
                Name = "Royal Chamber",
                Description = "A tended chamber for the Queen. She lays faster when the colony keeps one, and faster again the larger it is.",
                FoodCostPerCell = 4,
                FurnishSecondsPerCell = 0.7f,
                EffectPerCell = 0.08f,
                EffectSummary = "8% faster egg laying per cell, compounding"
            }
        },
    };
}
