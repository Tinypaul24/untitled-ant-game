using Godot;
using System.Collections.Generic;

// How a colony begins: a queen flies in off the nuptial flight, lands, and digs herself in.
//
// This exists because the opening did not work. The game started with a chamber already carved and
// three workers standing in it, which told a new player nothing about where burrows come from; then
// it started on bare turf with nothing at all, which told them even less. Watching the queen cut the
// first shaft teaches the shape of a burrow - diagonal, because ants cannot climb - and gives the
// colony somewhere to exist before anyone has to press a button.
//
// She digs far faster than a worker ever could. This is the founding, not gameplay: the point is to
// be watchable, not to be a fair representation of how long a metre of soil takes.
public partial class ColonyFounding : Node
{
    private enum Phase
    {
        Flying,
        Landing,
        Digging,
        Done,
    }

    // Where she comes in from, in tiles, relative to the landing site. Far enough to be off the edge
    // of a 640x360 view at the start.
    private static readonly Vector2I ArrivalOffsetTiles = new(-26, -14);

    private const float FlightSpeed = 110f;
    private const double SettleSeconds = 0.8;
    private const double SecondsPerDugCell = 0.12;

    // The shaft descends one tile across for every tile down, because that is the only slope an ant
    // can walk back up.
    private const int ShaftDepth = 6;
    private const int ChamberHalfWidth = 2;

    private static readonly PackedScene AntWorkerScene =
        GD.Load<PackedScene>("res://AntCity/Scenes/Entities/AntWorker.tscn");

    [Export] public int StartingWorkers { get; set; } = 3;

    private GridManager grid;
    private ColonyManager colony;
    private Queen queen;
    private Node2D main;

    private Phase phase = Phase.Flying;
    private Vector2 landingPoint;
    private double timer;

    private readonly Queue<Vector2I> toDig = new();

    public bool Finished => phase == Phase.Done;

    // Stop founding, without founding.
    //
    // A load during the six-second intro used to leave this running: it carried on cutting its
    // queued shaft into the restored world, teleported the Queen off her restored position, spawned
    // another set of starting workers on top of the restored population and re-announced the colony
    // as founded - on a save that might be an hour old.
    //
    // Deliberately not FinishFounding, which is what does the spawning and the announcing. This is
    // "that colony is not being founded any more", not "it finished".
    public void AbortForLoad()
    {
        toDig.Clear();
        phase = Phase.Done;
    }

    // Skips the arrival and cuts the burrow in one go.
    //
    // For tests and anything else that wants the colony as it is a few seconds in rather than the
    // staging that gets it there. Without this the suites ran against a world with no ants in it yet
    // and quietly checked nothing.
    public void CompleteNow()
    {
        if (phase == Phase.Done)
        {
            return;
        }

        queen.Position = landingPoint;

        while (toDig.Count > 0)
        {
            grid.Dig(toDig.Dequeue());
        }

        FinishFounding();
    }

    public override void _Ready()
    {
        main = GetParent<Node2D>();
        grid = main.GetNode<GridManager>("GridManager");
        colony = main.GetNode<ColonyManager>("ColonyManager");
        queen = main.GetNode<Queen>("Queen");

        landingPoint = grid.CellToWorld(grid.NestCenterCell);
        queen.Position = landingPoint + grid.CellToWorld(ArrivalOffsetTiles) - grid.CellToWorld(Vector2I.Zero);

        // She is on the wing until she lands, and laying is not her problem until she is underground.
        queen.Grounded = false;

        PlanBurrow();
    }

    public override void _Process(double delta)
    {
        switch (phase)
        {
            case Phase.Flying:
                Fly(delta);
                break;

            case Phase.Landing:
                Settle(delta);
                break;

            case Phase.Digging:
                Dig(delta);
                break;
        }
    }

    private void Fly(double delta)
    {
        Vector2 toLanding = landingPoint - queen.Position;
        float step = FlightSpeed * (float)delta;

        if (toLanding.Length() <= step)
        {
            queen.Position = landingPoint;
            phase = Phase.Landing;

            return;
        }

        queen.Position += toLanding.Normalized() * step;
    }

    private void Settle(double delta)
    {
        timer += delta;

        if (timer < SettleSeconds)
        {
            return;
        }

        timer = 0;
        phase = Phase.Digging;

        GD.Print("The Queen has landed and begun to dig.");
    }

    private void Dig(double delta)
    {
        timer += delta;

        while (timer >= SecondsPerDugCell && toDig.Count > 0)
        {
            timer -= SecondsPerDugCell;

            Vector2I cell = toDig.Dequeue();
            grid.Dig(cell);

            // She follows her own excavation down, so she ends up in the chamber rather than left
            // standing on the lawn above it.
            if (grid.IsStandable(cell))
            {
                queen.Position = grid.CellToWorld(cell);
            }
        }

        if (toDig.Count > 0)
        {
            return;
        }

        FinishFounding();
    }

    // A diagonal shaft down to a small chamber. Queued rather than dug at once so it is something to
    // watch, and so the loose soil a dig shakes free has time to slump as she goes.
    private void PlanBurrow()
    {
        Vector2I surface = grid.NestCenterCell;

        // Two tiles tall, like every corridor the workers will cut after her. Headroom before
        // floor, for the same reason the chamber below does it in that order: a shaft that opens
        // its floor first is briefly a sealed pocket.
        for (int step = 1; step <= ShaftDepth; step++)
        {
            Vector2I tread = surface + new Vector2I(step, step);

            // Same rule the workers dig by. It refuses the top treads, whose roof would be the
            // lawn beside her own front door, so the shaft runs low for its first couple of steps
            // and opens up once it is clear of the surface - which is what a real burrow entrance
            // does anyway.
            if (grid.ShouldOpenHeadroom(tread))
            {
                toDig.Enqueue(tread + new Vector2I(0, -1));
            }

            toDig.Enqueue(tread);
        }

        Vector2I floor = surface + new Vector2I(ShaftDepth, ShaftDepth);

        // Headroom first, then the floor, so the chamber never briefly looks like a sealed pocket.
        for (int y = -1; y <= 0; y++)
        {
            for (int x = -ChamberHalfWidth; x <= ChamberHalfWidth; x++)
            {
                toDig.Enqueue(floor + new Vector2I(x, y));
            }
        }
    }

    private void FinishFounding()
    {
        phase = Phase.Done;
        queen.Grounded = true;

        Vector2I floor = grid.NestCenterCell + new Vector2I(ShaftDepth, ShaftDepth);

        for (int i = 0; i < StartingWorkers; i++)
        {
            if (!colony.AddAnt())
            {
                break;
            }

            Vector2I spawn = grid.FindNearestTunnelCell(floor + new Vector2I(i - 1, 0));

            Node2D worker = AntWorkerScene.Instantiate<Node2D>();
            worker.Position = grid.CellToWorld(spawn);
            main.AddChild(worker);
        }

        colony.RaiseAlert("The colony is founded. Dig out chambers and the queen will fill them.");

        GD.Print("Colony founded.");
    }
}
