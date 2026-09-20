using Godot;
using System.Collections.Generic;

// The trail a colony leaves behind itself.
//
// Foraging used to be telepathic: TryFindForageTarget walked the entire food dictionary, so every
// idle worker knew about every deposit within twenty-four tiles regardless of walls, depth or
// whether anyone had ever been there. The colony never discovered anything; it simply knew.
//
// Real ants solve that with chemistry. A worker who has found food lays a trail on her way home,
// and a worker with nothing to go on follows somebody else's trail outward and looks again from the
// end of it. Nothing here decides that a column should form - it forms because a good source gets
// walked more often, and a source that runs dry stops being refreshed and fades.
//
// Sparse on purpose: this is a dictionary of the few hundred tiles that have been walked recently,
// not a field over the world. Untouched ground costs nothing at all.
public partial class PheromoneField : Node2D
{
    [Export] public GridManager Grid { get; set; }

    // Laid per tile a laden forager walks through.
    private const float DepositPerTile = 1.4f;
    // A ceiling, so a trail that has been walked a thousand times is not a thousand times louder
    // than one walked twice. What matters is which ways are used, not by how much.
    private const float MaxStrength = 6f;

    // Fraction lost per second. Around forty seconds to fade from full to nothing, which is long
    // enough for a column to establish itself and short enough that a dry source is forgotten.
    private const float EvaporationPerSecond = 0.09f;
    private const double EvaporateEverySeconds = 0.5;
    // Below this a trail is gone rather than merely faint - it stops being drawn and stops costing
    // anything, which is what keeps the dictionary the size of the colony's actual traffic.
    private const float Faintest = 0.1f;

    private static readonly Color TrailColor = new Color(0.55f, 0.75f, 0.35f);

    private static readonly Vector2I[] Neighbours =
    {
        new Vector2I(-1, -1), new Vector2I(0, -1), new Vector2I(1, -1),
        new Vector2I(-1, 0), new Vector2I(1, 0),
        new Vector2I(-1, 1), new Vector2I(0, 1), new Vector2I(1, 1)
    };

    private readonly Dictionary<Vector2I, float> trail = new();
    private readonly List<Vector2I> faded = new();
    private double sinceEvaporation;

    public int MarkedCells => trail.Count;


    // Trails belong to the world that was walked, not to the colony. Left alone across a load, idle
    // workers follow routes laid down in a world that no longer exists - out into terrain generated
    // from different noise.
    public void ResetTransientState()
    {
        trail.Clear();
        QueueRedraw();
    }
    public void Deposit(Vector2I cell)
    {
        trail.TryGetValue(cell, out float strength);
        trail[cell] = Mathf.Min(MaxStrength, strength + DepositPerTile);
    }

    public float Strength(Vector2I cell)
    {
        return trail.TryGetValue(cell, out float strength) ? strength : 0f;
    }

    // Where a trail leads.
    //
    // A searching worker walks it *outward*: from where she stands, step repeatedly onto the marked
    // neighbour that lies further from home. Strength on its own cannot tell her which way that is,
    // because every trail is strongest near the nest where they all overlap - so the gradient she
    // climbs is distance from home, and the scent only decides which directions are a way at all.
    //
    // She is not promised food at the end. She is promised somewhere another ant thought was worth
    // coming back from, which is the entire bargain.
    public bool TryFollowOutward(Vector2I from, out Vector2I destination)
    {
        const int MaxSteps = 64;

        destination = from;

        if (Grid == null || trail.Count == 0)
        {
            return false;
        }

        Vector2I nest = Grid.NestCenterCell;
        Vector2I current = from;

        // Visited cells are refused rather than merely deprioritised: a trail that doubles back on
        // itself would otherwise walk this loop until the step cap, and hand back a destination
        // somewhere in the middle of it.
        var walked = new HashSet<Vector2I> { from };

        for (int step = 0; step < MaxSteps; step++)
        {
            Vector2I best = current;
            float bestStrength = 0f;
            int reach = DistanceFromNest(current, nest);

            foreach (Vector2I direction in Neighbours)
            {
                Vector2I next = current + direction;

                if (walked.Contains(next) || DistanceFromNest(next, nest) <= reach)
                {
                    continue;
                }

                float strength = Strength(next);

                if (strength > bestStrength)
                {
                    bestStrength = strength;
                    best = next;
                }
            }

            if (best == current)
            {
                break;
            }

            walked.Add(best);
            current = best;
        }

        destination = current;

        return current != from;
    }

    private static int DistanceFromNest(Vector2I cell, Vector2I nest)
    {
        return Mathf.Max(Mathf.Abs(cell.X - nest.X), Mathf.Abs(cell.Y - nest.Y));
    }

    public override void _Process(double delta)
    {
        sinceEvaporation += delta;

        if (sinceEvaporation < EvaporateEverySeconds)
        {
            return;
        }

        Evaporate((float)sinceEvaporation);
        sinceEvaporation = 0;
    }

    // On a timer rather than per frame. Evaporation is a sweep of every marked tile, and a scent
    // that decays twice a second decays indistinguishably from one that decays sixty times.
    private void Evaporate(float elapsed)
    {
        float kept = Mathf.Max(0f, 1f - EvaporationPerSecond * elapsed);

        faded.Clear();

        foreach (KeyValuePair<Vector2I, float> entry in trail)
        {
            float strength = entry.Value * kept;

            if (strength < Faintest)
            {
                faded.Add(entry.Key);
                continue;
            }

            trail[entry.Key] = strength;
        }

        foreach (Vector2I cell in faded)
        {
            trail.Remove(cell);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Grid == null)
        {
            return;
        }

        foreach (KeyValuePair<Vector2I, float> entry in trail)
        {
            float weight = entry.Value / MaxStrength;

            // Faint on purpose. A trail is a hint about where the colony's traffic runs, not a
            // painted road - if it competes with the ants walking it, it is drawn too strongly.
            var colour = new Color(TrailColor, 0.14f + 0.34f * weight);

            DrawCircle(Grid.CellToWorld(entry.Key), 3f + 4f * weight, colour);
        }
    }
}
