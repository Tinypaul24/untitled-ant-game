using System.Collections.Generic;

public enum BuildingType
{
    NestingChamber,
    Granary,
    Nursery
}

public readonly struct BuildingDef
{
    public string Name { get; init; }
    public string Description { get; init; }
    public int FoodCostPerCell { get; init; }
    public float FurnishSecondsPerCell { get; init; }
    public float EffectPerCell { get; init; }
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
                EffectPerCell = 1f
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
                EffectPerCell = 8f
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
                EffectPerCell = 0f
            }
        },
    };
}
