using Godot;

// What a material fundamentally is. Behaviour in the simulation is driven off this plus the numeric
// properties below - never off "if material == Sand" - so adding a material is a data change.
public enum MaterialKind
{
    Air,
    Solid,
    Powder,
    Liquid,
    Gas,
}

// Kept as a byte so a chunk is a flat byte array. Ids are persisted in saves, so append new
// materials at the end rather than renumbering.
public enum MaterialId : byte
{
    Air = 0,
    Dirt = 1,
    Stone = 2,
    Sand = 3,
    Water = 4,
    Oil = 5,
    Fire = 6,
    Mud = 7,
    Acid = 8,
    Lava = 9,
    Steam = 10,
    Ice = 11,
}

// Everything the simulation knows about one material.
//
// Several of these fields are unused by Stage 1 on purpose. Temperature, melting and burning are
// here so the later stages slot in without touching the storage layout or the material table.
public sealed class MaterialDefinition
{
    public MaterialId Id { get; init; }
    public string Name { get; init; } = "";
    public MaterialKind Kind { get; init; }
    public Color Colour { get; init; }

    // Relative mass. Used for displacement: a heavier thing sinks through a lighter one.
    public float Density { get; init; }

    public bool GravityAffected { get; init; }

    // How far a liquid will try to spread sideways in one tick when it cannot fall. Higher reads as
    // runnier; 0 means it only ever falls.
    public int DispersionRate { get; init; }

    // Whether something heavier is allowed to push through this rather than resting on it.
    public bool Displaceable { get; init; }

    public bool Diggable { get; init; }
    public bool Destructible { get; init; }

    public bool Flammable { get; init; }

    // Ticks a cell of this material survives before expiring, or 0 for "lasts forever".
    public int Lifetime { get; init; }

    // What it leaves behind when its lifetime runs out.
    public MaterialId DecaysTo { get; init; } = MaterialId.Air;

    public bool Corrosive { get; init; }

    // Anything an ant should not walk into. Drives IsDangerousAt, so adding a hazard is a flag
    // rather than another special case at the call site.
    public bool Harmful { get; init; }

    // Degrees this pushes into each neighbour per tick. Positive for heat sources, negative for
    // things that chill what they touch. Zero means it only ever conducts what it is handed.
    public float HeatOutput { get; init; }

    public float FreezingPoint { get; init; } = float.MinValue;

    // What a crossed threshold turns this into. Air means "no such transition" rather than
    // "annihilate", so a material without one is simply unaffected.
    public MaterialId FreezesTo { get; init; } = MaterialId.Air;
    public MaterialId BoilsTo { get; init; } = MaterialId.Air;
    public MaterialId MeltsTo { get; init; } = MaterialId.Air;
    public MaterialId BurnsTo { get; init; } = MaterialId.Fire;

    public float DefaultTemperature { get; init; } = 20f;
    public float IgnitionTemperature { get; init; } = float.MaxValue;
    public float MeltingPoint { get; init; } = float.MaxValue;
    public float BoilingPoint { get; init; } = float.MaxValue;

    public bool IsAir => Kind == MaterialKind.Air;
    public bool Flows => Kind == MaterialKind.Liquid || Kind == MaterialKind.Gas;
    public bool Falls => GravityAffected;

    // Whether the renderer paints this over the tilemap. Dirt and stone are already the tilemap job -
    // it draws them with texture and proper edges - so the simulation only paints what it adds on
    // top: loose powders, liquids and gases.
    public bool RenderedOverTerrain => Kind == MaterialKind.Powder || Kind == MaterialKind.Liquid || Kind == MaterialKind.Gas;
}

public static class MaterialDatabase
{
    private static readonly MaterialDefinition[] Definitions;

    static MaterialDatabase()
    {
        Definitions = new MaterialDefinition[byte.MaxValue + 1];

        Add(new MaterialDefinition
        {
            Id = MaterialId.Air,
            Name = "Air",
            Kind = MaterialKind.Air,
            Colour = new Color(0, 0, 0, 0),
            Density = 0f,
            Displaceable = true,
        });

        Add(new MaterialDefinition
        {
            Id = MaterialId.Dirt,
            Name = "Dirt",
            Kind = MaterialKind.Solid,
            Colour = new Color("6b4a32"),
            Density = 1400f,
            Diggable = true,
            Destructible = true,
        });

        Add(new MaterialDefinition
        {
            Id = MaterialId.Stone,
            Name = "Stone",
            Kind = MaterialKind.Solid,
            Colour = new Color("6d675e"),
            Density = 2600f,
            // Not diggable by hand - only destruction (explosions, acid) should ever remove it.
            Destructible = true,
        });

        Add(new MaterialDefinition
        {
            Id = MaterialId.Sand,
            Name = "Sand",
            Kind = MaterialKind.Powder,
            Colour = new Color("c9a86a"),
            Density = 1600f,
            GravityAffected = true,
            Diggable = true,
            Destructible = true,
        });

        Add(new MaterialDefinition
        {
            Id = MaterialId.Water,
            Name = "Water",
            Kind = MaterialKind.Liquid,
            Colour = new Color("3d6ea8", 0.85f),
            Density = 1000f,
            GravityAffected = true,
            DispersionRate = 5,
            Displaceable = true,
            Destructible = true,
            BoilingPoint = 100f,
            BoilsTo = MaterialId.Steam,
            FreezingPoint = 0f,
            FreezesTo = MaterialId.Ice,
        });

        Add(new MaterialDefinition
        {
            Id = MaterialId.Oil,
            Name = "Oil",
            Kind = MaterialKind.Liquid,
            Colour = new Color("3b3320", 0.9f),
            // Lighter than water, so water poured onto oil sinks through it and the oil ends up on top.
            Density = 800f,
            GravityAffected = true,
            DispersionRate = 3,
            Displaceable = true,
            Destructible = true,
            Flammable = true,
            IgnitionTemperature = 150f,
            BurnsTo = MaterialId.Fire,
        });

        Add(new MaterialDefinition
        {
            Id = MaterialId.Fire,
            Name = "Fire",
            Kind = MaterialKind.Gas,
            Colour = new Color("e8632a"),
            // Fire stays put on whatever is burning. Making it buoyant sounds right, but it then
            // lifts clear of the fuel before it can set light to it and a pool of oil barely burns.
            // Rising heat belongs to Smoke as its own material in the temperature stage.
            Density = 400f,
            GravityAffected = false,
            DispersionRate = 0,
            Displaceable = true,
            Lifetime = 45,
            DecaysTo = MaterialId.Air,
            DefaultTemperature = 600f,
            HeatOutput = 9f,
        });

        Add(new MaterialDefinition
        {
            Id = MaterialId.Mud,
            Name = "Mud",
            // A powder rather than a liquid: it slumps and piles, and being a powder it also reads
            // as solid ground to the tile derivation, so a mudslide genuinely blocks a passage.
            Kind = MaterialKind.Powder,
            Colour = new Color("4a3524"),
            Density = 1800f,
            GravityAffected = true,
            Diggable = true,
            Destructible = true,
        });

        Add(new MaterialDefinition
        {
            Id = MaterialId.Acid,
            Name = "Acid",
            Kind = MaterialKind.Liquid,
            Colour = new Color("8ec63f", 0.9f),
            Density = 1200f,
            GravityAffected = true,
            DispersionRate = 4,
            Displaceable = true,
            Destructible = true,
            Corrosive = true,
            Harmful = true,
        });

        Add(new MaterialDefinition
        {
            Id = MaterialId.Lava,
            Name = "Lava",
            Kind = MaterialKind.Liquid,
            Colour = new Color("d8452a"),
            Density = 2500f,
            GravityAffected = true,
            // Sluggish. Lava that ran like water would drain out of any chamber instantly.
            DispersionRate = 1,
            Displaceable = true,
            Harmful = true,
            DefaultTemperature = 1100f,
            HeatOutput = 14f,
        });

        Add(new MaterialDefinition
        {
            Id = MaterialId.Steam,
            Name = "Steam",
            Kind = MaterialKind.Gas,
            Colour = new Color("c8d4d8", 0.55f),
            // Negative density means lighter than air, which is what makes it rise and lets it push
            // up through water rather than being trapped under it.
            Density = -2f,
            GravityAffected = true,
            DispersionRate = 3,
            Displaceable = true,
            Lifetime = 150,
            DecaysTo = MaterialId.Water,
            DefaultTemperature = 120f,
        });

        Add(new MaterialDefinition
        {
            Id = MaterialId.Ice,
            Name = "Ice",
            Kind = MaterialKind.Solid,
            Colour = new Color("a8d8e8"),
            Density = 900f,
            Diggable = true,
            Destructible = true,
            // Deliberately radiates nothing. When ice chilled its surroundings, every cell it froze
            // became another cold source, and a single block set an entire lake solid in one
            // unstoppable chain. It melts when something heats it; it does not spread itself.
            HeatOutput = 0f,
            MeltingPoint = 0f,
            MeltsTo = MaterialId.Water,
        });
    }

    public static MaterialDefinition Get(MaterialId id)
    {
        return Definitions[(byte)id] ?? Definitions[(byte)MaterialId.Air];
    }

    private static void Add(MaterialDefinition definition)
    {
        Definitions[(byte)definition.Id] = definition;
    }
}
