using System.Collections.Generic;

// What happens when two materials end up next to each other.
//
// The point of this table is that the simulation loop contains no material names at all. Adding
// "water + lava becomes steam and stone" is a line here, not a branch in the tick.
// Extra conditions a reaction can require of the cell being changed.
//
// Flags rather than code, so a reaction that only makes sense somewhere specific stays a line in the
// table. Grass regrowth is the case that forced this: as a plain pair it spread into every dirt cell
// it touched, which is the entire world - within a minute the map is grass all the way down.
[System.Flags]
public enum ReactionNeeds
{
    Nothing = 0,

    // The changed cell must be in contact with air, so growth only reaches exposed surfaces.
    AirContact = 1,

    // The changed cell must be near the surface, so nothing grows in a deep tunnel just because an
    // ant opened it to air.
    Daylight = 2,

    // The changed cell must have no air around it at all. The opposite of AirContact, for things
    // that only happen under the weight of everything above them.
    Buried = 4,

    // The changed cell must be up against enough liquid to count as soaked rather than splashed.
    // A single wet cell should not bring a wall down; standing water should.
    Saturated = 8,
}

public readonly struct Reaction
{
    public MaterialId BecomesA { get; init; }
    public MaterialId BecomesB { get; init; }

    // Rolled per contact per tick. Below 1 this spreads raggedly rather than in a clean wavefront,
    // which is what makes fire crawling along a pool of oil look alive.
    public float Chance { get; init; }

    public ReactionNeeds Needs { get; init; }
}

public static class ReactionTable
{
    // Every material pair, flat. The obvious version keyed a Dictionary on a (MaterialId, MaterialId)
    // tuple, but the tick asks this about all four neighbours of every reactive cell, and hashing a
    // tuple through EqualityComparer for each of those was a measurable slice of a settling pool's
    // frame. This is one array read to reject, which is what the common answer deserves to cost.
    //
    // Holds an index into Entries, one-based so that zero means "these two do nothing".
    private static readonly byte[] Lookup = new byte[(byte.MaxValue + 1) * (byte.MaxValue + 1)];

    private static readonly List<Reaction> Entries = new();

    // Materials that appear in at least one reaction, so the tick can skip the neighbour scan
    // entirely for the overwhelming majority of cells.
    private static readonly bool[] Reactive = new bool[byte.MaxValue + 1];

    static ReactionTable()
    {
        // Fire sets oil alight. Both cells end up burning, which is what lets a flame travel.
        Register(MaterialId.Fire, MaterialId.Oil, MaterialId.Fire, MaterialId.Fire, 0.22f);

        // Lava does the same, and stays lava - it is the heat source, not the fuel.
        Register(MaterialId.Lava, MaterialId.Oil, MaterialId.Lava, MaterialId.Fire, 0.30f);

        // Quenching. The lava skins over into stone and the water flashes off, which is what makes
        // pouring water into a lava chamber a way to build a floor rather than just a way to die.
        Register(MaterialId.Lava, MaterialId.Water, MaterialId.Stone, MaterialId.Steam, 0.45f);

        // Soil soaks water up. The water is consumed, which is the only version of this that ends:
        // when the water survived, the dirt it touched turned to mud, the mud slumped away because
        // it is a powder, fresh dirt came into contact, and a standing pool dissolved the map around
        // it forever - and kept every cell of itself awake while it did.
        //
        // Saturation is what makes this "heavily watered" rather than "damp": a splash runs off, and
        // it takes standing water against a wall to bring it down.
        Register(MaterialId.Water, MaterialId.Dirt, MaterialId.Air, MaterialId.Mud, 0.03f,
            ReactionNeeds.Saturated);

        // Soaked loose soil turns to mud as readily as packed does - it is already falling apart.
        Register(MaterialId.Water, MaterialId.LooseDirt, MaterialId.Air, MaterialId.Mud, 0.05f,
            ReactionNeeds.Saturated);

        // Loose soil resting against packed earth settles back into it. This is what makes digging
        // destabilise a wall "for a while" rather than forever.
        //
        // Deliberately not gated on being buried, which was the first version of this. A grain lying
        // on a tunnel floor always has air above it, so it never qualified, never settled and never
        // went anywhere - every tunnel silently filled a quarter full of debris that nothing could
        // ever clear, because spoil hauling is off and there was nowhere else for it to go.
        //
        // Slow enough that you watch a wall slump and settle rather than seeing it snap back.
        Register(MaterialId.Dirt, MaterialId.LooseDirt, MaterialId.Dirt, MaterialId.Dirt, 0.006f);

        // Turf catches from a flame beside it, so a fire runs along the surface instead of sitting
        // where it started. The temperature path lights grass too; this is what makes it spread at a
        // pace you can watch.
        Register(MaterialId.Fire, MaterialId.Grass, MaterialId.Fire, MaterialId.Fire, 0.18f);

        // Turf creeps back over bare soil. Slow on purpose - this is scenery healing, not a race -
        // and gated to lit surfaces, or it would grow down every tunnel wall and fill the world.
        Register(MaterialId.Grass, MaterialId.Dirt, MaterialId.Grass, MaterialId.Grass, 0.004f,
            ReactionNeeds.AirContact | ReactionNeeds.Daylight);

        // Acid is spent as it eats, so a given blob dissolves a proportional amount and no more.
        // Stone resists it far better than soil does.
        Register(MaterialId.Acid, MaterialId.Dirt, MaterialId.Air, MaterialId.Air, 0.30f);
        Register(MaterialId.Acid, MaterialId.Sand, MaterialId.Air, MaterialId.Air, 0.30f);
        Register(MaterialId.Acid, MaterialId.Mud, MaterialId.Air, MaterialId.Air, 0.25f);
        Register(MaterialId.Acid, MaterialId.Stone, MaterialId.Air, MaterialId.Air, 0.05f);
    }

    public static bool IsReactive(MaterialId id)
    {
        return Reactive[(byte)id];
    }

    public static bool TryGet(MaterialId a, MaterialId b, out Reaction reaction)
    {
        byte index = Lookup[((byte)a << 8) | (byte)b];

        if (index == 0)
        {
            reaction = default;
            return false;
        }

        reaction = Entries[index - 1];
        return true;
    }

    // Registers the pair in both orders so the tick never has to care which cell it looked at first.
    private static void Register(MaterialId a, MaterialId b, MaterialId becomesA, MaterialId becomesB,
        float chance, ReactionNeeds needs = ReactionNeeds.Nothing)
    {
        Store(a, b, new Reaction { BecomesA = becomesA, BecomesB = becomesB, Chance = chance, Needs = needs });
        Store(b, a, new Reaction { BecomesA = becomesB, BecomesB = becomesA, Chance = chance, Needs = needs });

        Reactive[(byte)a] = true;
        Reactive[(byte)b] = true;
    }

    private static void Store(MaterialId a, MaterialId b, Reaction reaction)
    {
        Entries.Add(reaction);

        // One-based, so 255 reactions fit before the index type has to grow. Well past the point
        // where the table itself would need rethinking.
        Lookup[((byte)a << 8) | (byte)b] = (byte)Entries.Count;
    }
}
