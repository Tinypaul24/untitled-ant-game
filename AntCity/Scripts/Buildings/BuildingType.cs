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
            new BuildingDef { Name = "Nesting Chamber", FoodCostPerCell = 3, FurnishSecondsPerCell = 0.5f, EffectPerCell = 1f }
        },
        {
            BuildingType.Granary,
            new BuildingDef { Name = "Granary", FoodCostPerCell = 2, FurnishSecondsPerCell = 0.4f, EffectPerCell = 8f }
        },
        {
            BuildingType.Nursery,
            new BuildingDef { Name = "Nursery", FoodCostPerCell = 3, FurnishSecondsPerCell = 0.5f, EffectPerCell = 0f }
        },
    };
}
