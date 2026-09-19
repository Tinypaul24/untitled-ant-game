using Godot;
using System;
using System.Collections.Generic;

public partial class AntWorker : Area2D
{
    private enum State
    {
        Idle,
        Walking,
        Digging,
        Foraging,
        Building
    }

    // Left alone deliberately, despite the ant now being 12px rather than 7. The whole food economy
    // is tuned against this - dig time, forage round trips, upkeep per hour - and the hazard escape
    // check has a fixed frame deadline she has to clear. Changing walking speed to fix a sprite
    // scale would be a balance change smuggled in behind an art change.
    private const float MoveSpeed = 24f;
    // Gravity does not negotiate. A worker dropping into a shaft she has just undermined covers it
    // far faster than she walks - at walking pace a three-tile drop is a four-second glide.
    private const float FallSpeed = 96f;
    private const float ArrivalDistance = 2f;

    // Steering. An ant reaches full speed in a fifth of a second, which is quick enough that
    // setting off still feels immediate and slow enough that you can see her lean into it.
    private const float Acceleration = 120f;
    // Radians per second. About 1.4 turns a second: a right-angle corner takes ~0.17s, which at
    // walking speed is an arc roughly four pixels across - visible as a curve, not as a swerve.
    private const float TurnRate = 9f;
    // Fleeing lava is not the moment to be graceful.
    private const float PanicTurnRate = 20f;
    // Past this much of a turn she nearly stops and pivots instead of arcing. Real ants turn on
    // the spot, and it is also what stops a bounded turn rate becoming an orbit she never escapes.
    private const float PivotSpeedFraction = 0.2f;
    private static readonly float SharpTurnRadians = Mathf.DegToRad(70f);
    // Seconds to excavate one whole cell, split evenly across the grains it is made of.
    private const float DigSeconds = 2f;
    private const float DigSecondsPerGrain = DigSeconds / GridManager.GrainsPerCell;
    private const int WanderCellRadius = 3;
    private const double MinWanderPause = 1.0;
    private const double MaxWanderPause = 3.0;
    // Clear of a 12px body rather than cutting through it.
    private const float SelectionRingRadius = 8f;
    private const float ForageSeconds = 1f;
    private const int ForageCarryCapacity = 10;
    private const int HarvestPerTick = 2;
    // How far an idle worker can spot food for herself, in tiles.
    //
    // It used to be twenty-four, which was not sight but telepathy: every worker knew every deposit
    // for four hundred pixels in all directions, through rock, whether or not anyone had ever been
    // there. Eight tiles is roughly what she could plausibly find by casting about, and everything
    // beyond it she has to be led to by somebody else's trail.
    private const int ForageSightTiles = 8;
    // Each dig tick scrapes out a share of the cell; a full load is three cells worth, matching the old haul cadence.
    private const int GrainsPerDigTick = MaterialWorld.CellsPerTile / GridManager.GrainsPerCell;
    private const int HaulCapacityGrains = MaterialWorld.CellsPerTile * 3;
    private const int MaxCarriedSpecksDrawn = 6;
    private const int CarriedSpecksPerRow = 3;
    // How often a worker checks the ground under her for danger.
    private const double HazardCheckSeconds = 0.25;
    // How close something harmful has to get before she drops everything and moves.
    private const int HazardReactionCells = 1;
    // Out at the mandibles of a 12px body, so a carried load sits in front of her rather than on
    // top of her.
    private const float MouthOffset = 8f;
    private const float CarriedSpeckSize = 2f;

    // How far she wanders off her own line, and how often. Wavelength is in world pixels travelled
    // rather than in seconds, so a worker slowed by a load weaves the same shape more slowly instead
    // of weaving a different shape. A real ant does not walk a ruled line and neither should she.
    private const float WeaveAmplitude = 1.25f;
    private const float WeaveWavelength = 14f;

    // Two ants meeting stop and touch antennae. It is the single most recognisable thing ants do,
    // it costs a third of a second, and it is what turns a corridor of traffic into a colony.
    private const float AntennationRange = 7f;
    private const double AntennationSeconds = 0.35;
    // Long enough that a crowded nest does not become a standing ovation.
    private const double AntennationCooldownSeconds = 4.0;

    // Every part of her is drawn this far below the point the game thinks she occupies.
    //
    // Navigation works in tiles and puts her at the centre of one, so her feet hung two pixels clear
    // of the floor she was supposed to be standing on. Now that a bored tunnel keeps a ragged
    // ceiling, that same two pixels is also the difference between her antennae brushing the roof
    // and her head being buried in it.
    //
    // A render offset, never a change to Position: WorldToCell(Position) is the "which tile am I in"
    // oracle at fifteen call sites and in the tests, and shifting her by eight pixels of floor could
    // push that answer across a tile boundary - at which point TryDigOut sees solid ground and she
    // digs the floor out from under herself.
    private static readonly Vector2 BodyOffset = new Vector2(0f, 2f);

    private static readonly Color SelectionRingColor = new Color(1f, 1f, 0.4f);

    private static readonly Texture2D UpTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Up.svg");
    private static readonly Texture2D DownTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Down.svg");
    private static readonly Texture2D LeftTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Left.svg");
    private static readonly Texture2D RightTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Right.svg");
    // Down-right. Mirrored at draw time to cover the other three diagonals.
    private static readonly Texture2D DiagonalTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant DownRight.svg");

    private const int Octants = 8;
    private static readonly float SectorRadians = Mathf.Tau / Octants;
    // About ten degrees of stickiness past the halfway line between two sprites.
    private static readonly float FacingHysteresisRadians = Mathf.DegToRad(10f);

    // Indexed by octant, starting at Right and going clockwise on screen. Normalised, because this
    // is also where the mandibles are and a carried load hung off an unnormalised diagonal would
    // sit forty percent further out than one carried sideways.
    private static readonly Vector2[] OctantDirections =
    {
        new Vector2(1f, 0f),
        new Vector2(1f, 1f).Normalized(),
        new Vector2(0f, 1f),
        new Vector2(-1f, 1f).Normalized(),
        new Vector2(-1f, 0f),
        new Vector2(-1f, -1f).Normalized(),
        new Vector2(0f, -1f),
        new Vector2(1f, -1f).Normalized(),
    };

    private Sprite2D sprite;
    private Timer digTimer;
    private Timer wanderTimer;
    private Timer forageTimer;
    private Timer buildTimer;
    private GridManager gridManager;
    private SelectionManager selectionManager;
    private BuildManager buildManager;
    private ColonyManager colonyManager;
    private MaterialWorld materialWorld;
    private PheromoneField pheromones;

    private State state = State.Idle;
    private bool isSelected;
    private Vector2 wanderHome;
    private Vector2 moveTarget;
    private Vector2 facing = Vector2.Down;
    // Down, matching the texture the scene file starts her on.
    private int facingOctant = 2;

    // Where she is actually going and how fast, as opposed to where the path says she should be.
    private Vector2 velocity;
    private Vector2 heading = Vector2.Right;
    // The straight line from where this leg began to its waypoint. Kept so she can tell "reached
    // it" from "went past it", which distance alone cannot answer once she travels in arcs.
    private Vector2 legDirection;
    private double legTimer;
    private double legDeadline;
    private bool falling;

    // What makes one worker not the same worker as the next.
    //
    // Ants on the same errand used to occupy exactly the same pixels: same speed, same line, same
    // moment of arrival, so a column of them read as one ant drawn several times. None of this
    // changes where she goes - it is all in how she covers the ground.
    private float paceScale = 1f;
    private float laneOffset;
    private double weavePhase;
    private double antennationTimer;
    private double antennationCooldown;
    private Vector2 renderOffset = BodyOffset;

    private Queue<Vector2I> pendingPath = new Queue<Vector2I>();
    private Action onPathComplete;
    private Vector2I digTarget;
    private bool hasDigJob;
    private Vector2I pendingDigCell;
    private Action pendingDigCallback;
    private double hazardCheckTimer;
    private Vector2I lastTrailCell = new Vector2I(int.MinValue, int.MinValue);
    private bool followedTrail;

    // A dead nestmate over her shoulder, on her way to the refuse heap.
    private bool carryingBody;

    // True while she is walking a route out of danger, which is the one time she is allowed to head
    // into a hazardous cell on purpose.
    private bool escaping;
    private Vector2I? forageTarget;
    private int carriedFood;
    private readonly List<MaterialId> carriedGrains = new();
    private Room pendingRoom;
    private Vector2I? claimedJobCell;
    private Queue<Vector2I> plannedDigRoute = new Queue<Vector2I>();

    public override void _Ready()
    {
        AddToGroup("ants");

        sprite = GetNode<Sprite2D>("Sprite2D");

        // Drawn slightly low so she stands on the floor rather than hovering over it, and each ant
        // a little differently so a file of them does not read as one sprite repeated.
        paceScale = (float)GD.RandRange(0.9, 1.1);
        laneOffset = (float)GD.RandRange(-1.5, 1.5);
        weavePhase = GD.RandRange(0.0, Mathf.Tau);

        renderOffset = BodyOffset + new Vector2(0f, laneOffset);
        sprite.Position = renderOffset;
        GetNode<CollisionShape2D>("CollisionShape2D").Position = BodyOffset;
        digTimer = GetNode<Timer>("DigTimer");
        wanderTimer = GetNode<Timer>("WanderTimer");
        forageTimer = GetNode<Timer>("ForageTimer");
        buildTimer = GetNode<Timer>("BuildTimer");

        gridManager = GetNode<GridManager>("../GridManager");
        selectionManager = GetNode<SelectionManager>("../SelectionManager");
        buildManager = GetNode<BuildManager>("../BuildManager");
        colonyManager = GetNode<ColonyManager>("../ColonyManager");
        materialWorld = GetNode<MaterialWorld>("../MaterialWorld");
        pheromones = GetNode<PheromoneField>("../PheromoneField");

        digTimer.OneShot = true;
        digTimer.WaitTime = DigSecondsPerGrain;
        digTimer.Timeout += OnDigTimeout;

        forageTimer.OneShot = true;
        forageTimer.WaitTime = ForageSeconds;
        forageTimer.Timeout += OnForageTimeout;

        buildTimer.OneShot = true;
        buildTimer.Timeout += OnBuildTimeout;

        wanderTimer.OneShot = true;
        wanderTimer.Timeout += GoIdle;

        InputEvent += OnInputEvent;

        wanderHome = Position;
        GoIdle();
    }

    public override void _Process(double delta)
    {
        // Danger travels on its own. A worker who is mid-dig, foraging or simply standing idle can
        // have lava reach her without ever taking a step, so checking only as she moves would miss
        // every case where the flow does the moving. On a timer rather than per frame: it costs a
        // material lookup, and nothing in the simulation spreads fast enough to need more than this.
        hazardCheckTimer += delta;

        if (hazardCheckTimer >= HazardCheckSeconds)
        {
            hazardCheckTimer = 0;

            // Reacts to danger arriving beside her, not just under her. Waiting to be engulfed is
            // too late when the flow moves faster than she walks. Skipped while already escaping, or
            // the run for safety restarts from scratch every quarter second and never gets anywhere.
            if (!escaping && gridManager.IsHazardNear(gridManager.WorldToCell(Position), HazardReactionCells))
            {
                FleeHazard();
            }

            CheckForPassingAnt();

            WatchForIdleStall();
        }

        antennationCooldown -= delta;

        if (state != State.Walking)
        {
            return;
        }

        Steer(delta);
    }

    // Walking, with momentum.
    //
    // This used to be: point straight at the next cell centre, translate at a constant speed, and
    // teleport the last two pixels on arrival. Direction was recomputed from nothing every frame,
    // so a corner was a corner - an instantaneous ninety-degree change of heading, every sixteen
    // pixels, for the whole length of a corridor.
    private void Steer(double delta)
    {
        Vector2 toTarget = moveTarget - Position;
        float distance = toTarget.Length();

        if (distance <= ArrivalDistance || HasPassed(toTarget))
        {
            AdvancePath();
            return;
        }

        Vector2 wanted = toTarget / distance;

        // Fast enough to still stop on the waypoint, rather than a flat speed plus an arrival
        // snap. This is what makes her settle onto a target instead of overshooting it. The cap is
        // only reached on long legs; a single-tile hop never gets there, which is why a corridor
        // now reads as one accelerating run rather than sixteen identical steps.
        float cap = (falling ? FallSpeed : MoveSpeed) * paceScale;
        float targetSpeed = Mathf.Min(cap, Mathf.Sqrt(2f * Acceleration * distance));

        // Turn at a bounded rate, so corners are arcs.
        float maxTurn = (escaping ? PanicTurnRate : TurnRate) * (float)delta;
        float turn = Mathf.Clamp(heading.AngleTo(wanted), -maxTurn, maxTurn);

        heading = heading.Rotated(turn).Normalized();

        // Anti-orbit, and the reason arrival is reliable at a finite turn rate: an ant who cannot
        // turn tightly enough to hit her target slows to nearly a pivot rather than circling it.
        // Real ants turn on the spot, so this reads correctly as well as behaving correctly - you
        // cannot orbit a point you are barely translating toward.
        if (Mathf.Abs(heading.AngleTo(wanted)) > SharpTurnRadians)
        {
            targetSpeed *= PivotSpeedFraction;
        }

        // Stopped nose to nose with somebody. She still turns while she does it, so the pause reads
        // as two ants attending to each other rather than as two ants glitching.
        if (antennationTimer > 0)
        {
            antennationTimer -= delta;
            targetSpeed = 0f;
        }

        float speed = Mathf.MoveToward(velocity.Length(), targetSpeed, Acceleration * (float)delta);
        velocity = heading * speed;

        Position += velocity * (float)delta;
        UpdateFacing(heading);
        Weave(speed * (float)delta);
        LayTrail();

        WatchForStall(delta);
    }

    // The wander in her walk.
    //
    // Driven by distance covered rather than by time, so it survives her being slowed down: a
    // laden worker weaves the same shape at the same scale, just more slowly. Purely a render
    // offset - she is exactly where the game thinks she is, she simply is not drawn on the rail.
    private void Weave(float travelled)
    {
        weavePhase += travelled / WeaveWavelength * Mathf.Tau;

        Vector2 across = new Vector2(-heading.Y, heading.X);

        renderOffset = BodyOffset
            + new Vector2(0f, laneOffset)
            + across * (WeaveAmplitude * Mathf.Sin((float)weavePhase));

        sprite.Position = renderOffset;

        // Anything in her jaws is drawn by _Draw, which does not follow the sprite node on its own.
        if (carriedGrains.Count > 0)
        {
            QueueRedraw();
        }
    }

    // Two ants meeting stop and touch antennae.
    //
    // It is the most recognisable thing ants do, it is how they actually exchange information, and
    // it costs a third of a second. Without it a corridor is a conveyor belt; with it the same
    // corridor reads as traffic between individuals.
    //
    // On the quarter-second tick rather than per frame, and only while walking: this is an
    // all-pairs scan over the colony and the cheapest honest way to keep it that way is to do it
    // rarely. Never while fleeing - there is a frame deadline on getting out of lava, and stopping
    // to say hello on the way is how an ant dies politely.
    private void CheckForPassingAnt()
    {
        if (escaping || state != State.Walking || antennationTimer > 0 || antennationCooldown > 0)
        {
            return;
        }

        foreach (Node node in GetTree().GetNodesInGroup("ants"))
        {
            if (node == this || node is not AntWorker other)
            {
                continue;
            }

            if (Position.DistanceSquaredTo(other.Position) > AntennationRange * AntennationRange)
            {
                continue;
            }

            Greet();
            other.Greet();

            return;
        }
    }

    private void Greet()
    {
        // Walkers only, matching the precondition CheckForPassingAnt applies to itself. The pause is
        // only ever ticked down inside Steer, so setting it on a digging or foraging ant leaves it
        // set until her next walk and then stalls the start of it.
        if (escaping || antennationTimer > 0 || state != State.Walking)
        {
            return;
        }

        antennationTimer = AntennationSeconds;
        antennationCooldown = AntennationCooldownSeconds;
    }

    // Whether she has gone past the waypoint rather than reaching it. Distance alone is not enough
    // with a bounded turn rate: an ant on a wide arc can sail past just outside the arrival radius
    // and then chase a point behind her forever.
    private bool HasPassed(Vector2 toTarget)
    {
        return legDirection != Vector2.Zero && toTarget.Dot(legDirection) <= 0f;
    }

    // How many times the stall watchdog below has had to rescue an ant. Counted rather than
    // silently swallowed: a safety net that catches people every day is not a safety net, it is a
    // hole in the floor. ColonyProbe prints this, and it should read zero.
    public static int StallRescues { get; private set; }

    // Both counters are static, so they carry a previous world's totals across a load and the probe
    // then reports failures belonging to a colony that no longer exists.
    public static void ResetCounters()
    {
        StallRescues = 0;
        SpoilLeftovers = 0;
        IdleStallRescues = 0;
        HaulTrips = 0;
    }

    // Last resort. Stranding is a one-way failure in this game, so an ant must never be able to
    // stall permanently on a steering bug - if a leg takes far longer than it possibly should, put
    // her on the waypoint and move on. This is the old teleport, kept as an emergency floor rather
    // than as the mechanism.
    private void WatchForStall(double delta)
    {
        legTimer += delta;

        if (legTimer < legDeadline)
        {
            return;
        }

        StallRescues++;

        Position = moveTarget;
        velocity = Vector2.Zero;

        AdvancePath();
    }

    // Route through existing tunnels to whichever tunnel cell is nearest the target, then dig a corridor the rest of the way.
    public void CommandDig(Vector2I targetCell)
    {
        AbandonCurrentJob();
        IssueDigCommand(targetCell, targetCell);
    }

    // Ignore existing tunnels entirely and start digging a corridor from wherever she already is.
    public void CommandDigDirect(Vector2I targetCell)
    {
        AbandonCurrentJob();
        IssueDigCommand(targetCell, gridManager.WorldToCell(Position));
    }

    // Walk to an already-dug tunnel cell without digging anything.
    public void CommandMove(Vector2I targetCell)
    {
        AbandonCurrentJob();
        StopCurrentTask();

        Vector2I startCell = gridManager.WorldToCell(Position);
        Vector2I goalCell = gridManager.FindNearestTunnelCell(targetCell);
        List<Vector2I> route = gridManager.FindTunnelPath(startCell, goalCell) ?? new List<Vector2I> { startCell };

        FollowPath(route, OnMoveComplete);
    }

    // Walk to a food source, harvest it in ticks up to carry capacity, then bring it back.
    public void CommandForage(Vector2I targetCell)
    {
        if (!gridManager.IsFoodSource(targetCell))
        {
            return;
        }

        AbandonCurrentJob();
        StopCurrentTask();

        plannedDigRoute.Clear();
        forageTarget = targetCell;
        gridManager.ClaimForageCell(targetCell);

        Vector2I startCell = gridManager.WorldToCell(Position);
        Vector2I nearestTunnel = gridManager.FindNearestTunnelCell(targetCell);
        List<Vector2I> route = gridManager.FindTunnelPath(startCell, nearestTunnel) ?? new List<Vector2I> { startCell };

        FollowPath(route, ApproachForageTarget);
    }

    // What she is doing, for the colony probe. Reading a private enum through a string keeps the
    // diagnostic from becoming a reason to widen the real state machine.
    public string DebugState => hasDigJob ? "digging" : forageTarget.HasValue ? "foraging" : state.ToString().ToLower();

    // Whether she has stopped having anything to do.
    //
    // An ant always has exactly one thing pending: a route in flight, or a timer running. Neither
    // means she will never act again - no path to follow, no callback queued, nothing to wake her.
    // She simply stands there for the rest of the game, still eating.
    //
    // A diagnostic read of the state machine in the same shape as DebugState, because this failure
    // is invisible in every harness: the probe counts what ants are doing, and a frozen ant reports
    // whatever she was doing when she froze.
    // Whether losing her right now would also lose something the colony needs. Used to pick who
    // starves: a forager walking food home is a worse loss than an ant wandering empty-handed.
    public bool IsCarryingSomethingUseful => carriedFood > 0 || hasDigJob || pendingRoom != null;

    public bool IsStalled =>
        state != State.Walking
        && digTimer.IsStopped()
        && forageTimer.IsStopped()
        && buildTimer.IsStopped()
        && wanderTimer.IsStopped();

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        QueueRedraw();
    }

    public AntSave CaptureState()
    {
        var save = new AntSave
        {
            X = Position.X,
            Y = Position.Y,
            Selected = isSelected,
            CarriedFood = carriedFood,
            HasDigJob = hasDigJob,
            DigTargetX = digTarget.X,
            DigTargetY = digTarget.Y,
            HasForageTarget = forageTarget.HasValue,
            ForageTargetX = forageTarget?.X ?? 0,
            ForageTargetY = forageTarget?.Y ?? 0,
        };

        foreach (MaterialId material in carriedGrains)
        {
            save.CarriedGrains.Add((int)material);
        }

        return save;
    }

    public void RestoreState(AntSave save)
    {
        Position = new Vector2(save.X, save.Y);
        moveTarget = Position;
        wanderHome = Position;

        // A loaded ant starts from rest. Velocity is not saved - it is a tenth of a second of
        // state, not something a player would notice restored, and carrying a stale one over would
        // have her set off in whatever direction she happened to be walking before the save.
        velocity = Vector2.Zero;
        legDirection = Vector2.Zero;
        falling = false;
        antennationTimer = 0;
        escaping = false;

        // Dropped rather than tipped out.
        //
        // StopCurrentTask below releases a carried load into the world and refunds carried food to
        // the colony, which is right when a job is interrupted and wrong here: the save already
        // says what she is carrying, so tipping it out first and then handing the saved load back
        // would leave the same soil existing twice. Whatever she happens to be holding is not part
        // of the state being restored.
        carriedGrains.Clear();
        carriedFood = 0;

        AbandonCurrentJob();
        StopCurrentTask();
        wanderTimer.Stop();
        pendingPath.Clear();
        plannedDigRoute.Clear();
        onPathComplete = null;
        state = State.Idle;

        var forageTargetCell = new Vector2I(save.ForageTargetX, save.ForageTargetY);
        var digTargetCell = new Vector2I(save.DigTargetX, save.DigTargetY);

        if (save.HasForageTarget && gridManager.IsFoodSource(forageTargetCell))
        {
            CommandForage(forageTargetCell);
        }
        else if (save.HasDigJob && gridManager.CanDig(digTargetCell))
        {
            CommandDig(digTargetCell);
        }
        else
        {
            GoIdle();
        }

        // Last, because every one of those commands runs StopCurrentTask on its way in.
        carriedFood = save.CarriedFood;

        foreach (int material in save.CarriedGrains)
        {
            carriedGrains.Add((MaterialId)material);
        }

        if (save.Selected)
        {
            selectionManager.ToggleSelect(this);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawCarriedGrains();

        if (isSelected)
        {
            DrawArc(renderOffset, SelectionRingRadius, 0f, Mathf.Tau, 24, SelectionRingColor, 2f, true);
        }
    }

    // The load she is actually carrying, heaped on her back. Without this a haul reads as the dirt
    // vanishing at the dig face and reappearing on the pile.
    // The load clamped in her mandibles, out in front of whichever way she is facing. Ants carry
    // spoil in their jaws, and putting it there rather than on her back makes a hauler readable as
    // a hauler from across the screen.
    private void DrawCarriedGrains()
    {
        if (carriedGrains.Count == 0)
        {
            return;
        }

        int specks = Mathf.Clamp(
            Mathf.RoundToInt(MaxCarriedSpecksDrawn * carriedGrains.Count / (float)HaulCapacityGrains),
            1,
            MaxCarriedSpecksDrawn
        );

        Vector2 mouth = renderOffset + facing * MouthOffset;
        Vector2 across = new Vector2(-facing.Y, facing.X);

        for (int i = 0; i < specks; i++)
        {
            int row = i / CarriedSpecksPerRow;
            int column = i % CarriedSpecksPerRow;

            // The bundle sits across her jaws and grows outward from them as the load gets heavier.
            Vector2 offset = mouth
                + across * ((column - (CarriedSpecksPerRow - 1) / 2f) * CarriedSpeckSize)
                + facing * (row * CarriedSpeckSize)
                - new Vector2(CarriedSpeckSize, CarriedSpeckSize) / 2f;

            // Sample across the load so a mixed haul shows the materials it is actually made of.
            Color color = MaterialDatabase.Get(carriedGrains[i * carriedGrains.Count / specks]).Colour;

            DrawRect(new Rect2(offset, new Vector2(CarriedSpeckSize, CarriedSpeckSize)), color, filled: true);
        }
    }


    private void IssueDigCommand(Vector2I targetCell, Vector2I wallReferenceCell)
    {
        // Cancel whatever timed action was in progress so it doesn't fire against the old target later.
        StopCurrentTask();

        digTarget = targetCell;
        hasDigJob = true;
        plannedDigRoute.Clear();

        Vector2I startCell = gridManager.WorldToCell(Position);
        Vector2I wallCell = gridManager.FindNearestTunnelCell(wallReferenceCell);
        List<Vector2I> route = gridManager.FindTunnelPath(startCell, wallCell) ?? new List<Vector2I> { startCell };

        FollowPath(route, DigTowardTarget);
    }

    // Stops whatever timed task is running, refunds any carried food, drops any carried dirt, and clears the forage target.
    private void StopCurrentTask()
    {
        if (state == State.Digging)
        {
            digTimer.Stop();
        }
        else if (state == State.Foraging)
        {
            forageTimer.Stop();
        }
        else if (state == State.Building)
        {
            buildTimer.Stop();
        }

        if (carriedFood > 0)
        {
            ReturnCarriedFood();
        }

        // An interrupted haul tips its load out where it stands rather than deleting it - the
        // material came out of the ground, so it has to end up somewhere.
        DropCarriedGrains();

        ReleaseForageTarget();
        hasDigJob = false;

        // An interrupted funeral puts the body down where she stands rather than deleting it.
        if (carryingBody)
        {
            carryingBody = false;
            GetParent()?.AddChild(new AntCorpse { Name = "AntCorpse", Position = Position });
        }

        // The wander timer too.
        //
        // It was never stopped anywhere except on load, so a stale one-to-three-second timer armed
        // before the player gave an order would fire mid-journey, run GoIdle, and silently replace
        // that order with whatever the job board offered instead. From the player side an ant
        // accepted a command and then wandered off to do something else, with no feedback.
        //
        // This is also what makes it provable that a Walking ant cannot enter GoIdle, which is the
        // property the guard deleted from PickWanderTarget was faking.
        wanderTimer.Stop();

        // One trail attempt per idle spell, and an interrupted walk should not burn it.
        followedTrail = false;
    }

    // Release whatever job-board work is in flight so a manual command doesn't leave it stuck claimed forever.
    private void AbandonCurrentJob()
    {
        if (claimedJobCell.HasValue)
        {
            buildManager.ReleaseClaim(claimedJobCell.Value);
            claimedJobCell = null;
        }

        // IsInstanceValid, not a null check: a freed Godot node leaves a live C# wrapper behind, so
        // `!= null` passes and the very next member access throws.
        if (GodotObject.IsInstanceValid(pendingRoom))
        {
            pendingRoom.FurnishClaimed = false;
        }

        pendingRoom = null;
    }

    // Look for open job-board work before falling back to aimless wandering.


    // She has starved.
    //
    // One place, because every claim she is holding has to be let go and there is no second chance
    // to do it. Nothing in this game used to free a worker at all - starvation decremented a counter
    // and left her walking around - so every reference to her that outlives the tree is a new
    // problem, and the selection list is the only one.
    //
    // RemoveChild before QueueFree, matching how rooms and the load path do it: QueueFree alone
    // defers to the end of the frame, so an ant who dies in the same frame as a quicksave would be
    // written into it.
    public void Die()
    {
        AbandonCurrentJob();
        StopCurrentTask();

        wanderTimer.Stop();

        // Whatever she was carrying falls where she lies. Release is sky-only on purpose, so using
        // it here would delete the load of anyone who died underground - and matter conservation is
        // checked to the cell.
        if (carriedGrains.Count > 0)
        {
            materialWorld.Spill(gridManager.WorldToCell(Position), carriedGrains);
        }

        selectionManager.Forget(this);
        colonyManager.RemoveAnt();

        LeaveBody();

        GetParent()?.RemoveChild(this);
        QueueFree();
    }

    private void LeaveBody()
    {
        Node parent = GetParent();

        if (parent == null)
        {
            return;
        }

        var corpse = new AntCorpse { Name = "AntCorpse", Position = Position };

        parent.AddChild(corpse);
    }
    // How many times an ant has had to be shaken out of doing nothing at all.
    //
    // Same principle as StallRescues: the normaliser in GoIdle fixes the paths we know about, and
    // this catches the one somebody adds next year. It should read zero - a safety net that catches
    // people every day is a hole in the floor, and if this number climbs there is a route into the
    // frozen state that has not been found yet.
    public static int IdleStallRescues { get; private set; }

    // Two seconds of genuinely nothing pending. Long enough that no legitimate gap between finishing
    // one thing and starting the next can trip it - every real handover is same-frame.
    private const double IdleStallSeconds = 2.0;

    private double idleStallTimer;

    private void WatchForIdleStall()
    {
        if (!IsStalled)
        {
            idleStallTimer = 0;
            return;
        }

        idleStallTimer += HazardCheckSeconds;

        if (idleStallTimer < IdleStallSeconds)
        {
            return;
        }

        idleStallTimer = 0;
        IdleStallRescues++;

        GoIdle();
    }
    // Everything she could be part-way through, put down.
    //
    // GoIdle is the colony's single "what should I do next" entry point, and it should not care how
    // she arrived at it. Several paths reached it with state still Digging or Building - and
    // PickWanderTarget used to return immediately unless the state was already Idle, so she ended up
    // with no route, no running timer and no callback: frozen for the rest of the game, still
    // eating. Guarding PickWanderTarget would have fixed the last of four exits and not the fifth
    // somebody writes next year. Normalising here fixes all of them at once.
    private void BecomeIdle()
    {
        digTimer.Stop();
        forageTimer.Stop();
        buildTimer.Stop();
        wanderTimer.Stop();

        pendingDigCallback = null;
        state = State.Idle;

        velocity = Vector2.Zero;
        legDirection = Vector2.Zero;
    }

    private void GoIdle()
    {
        BecomeIdle();

        // Spoil can pile up deep enough to set solid around her; dig back out before anything else.
        if (TryDigOut() || TryFall())
        {
            return;
        }

        if (buildManager.TryClaimDigJob(Position, out Vector2I cell))
        {
            claimedJobCell = cell;
            IssueDigCommand(cell, cell);
            return;
        }

        // Finishing a room the player asked for and paid for comes before foraging.
        //
        // It used to come after, and foraging practically never fails - the world is full of food, so
        // there was always somewhere to go. Rooms reached the furnishing stage and sat there forever
        // while every worker wandered off to fetch another seed.
        if (buildManager.TryClaimFurnishJob(Position, out Room room))
        {
            CommandBuild(room);
            return;
        }

        // Bodies first. A corpse in a corridor is in the way, and carrying it out is quick.
        if (TryRemoveCorpse())
        {
            return;
        }

        // Food is the one thing the colony always needs, and nobody else is going to fetch it. An idle
        // worker goes looking rather than milling about, which is what lets the colony feed itself.
        if (gridManager.TryFindForageTarget(Position, ForageSightTiles * gridManager.CellSize, out Vector2I food))
        {
            CommandForage(food);
            return;
        }

        // Nothing she can find herself. Follow the colony's traffic out to wherever it leads and
        // look again from there - which is how a worker finds a source she was never told about.
        if (TryFollowTrail())
        {
            return;
        }

        PickWanderTarget();
    }

    // A laden forager scent-marks the ground on her way home.
    //
    // Only on the way home, and only with food: that is what makes the trail mean something. A
    // worker wandering about would mark every dead end she looked down, and a trail that leads
    // everywhere leads nowhere.
    //
    // Once per tile entered rather than per frame, so a slow ant and a fast ant lay the same trail
    // and the strength of it reflects how many workers used the route, not how long they took.
    private void LayTrail()
    {
        Vector2I cell = gridManager.WorldToCell(Position);

        if (cell == lastTrailCell)
        {
            return;
        }

        lastTrailCell = cell;

        if (carriedFood > 0)
        {
            pheromones.Deposit(cell);
        }
    }


    // Carrying the dead out of the nest.
    //
    // This is what real ants do with a corpse in normal times - necrophoresis - and the colony only
    // eats them instead when the player says so. The body goes to the same place the spoil goes, so
    // a midden grows on the side of the hill and becomes a record of every worker the colony lost.
    private bool TryRemoveCorpse()
    {
        if (colonyManager.EatTheDeadPolicy || carryingBody)
        {
            return false;
        }

        if (!gridManager.TryFindCarrion(Position, ForageSightTiles * gridManager.CellSize, out Vector2I body))
        {
            return false;
        }

        Vector2I standAt = gridManager.FindNearestTunnelCell(body);
        List<Vector2I> route = gridManager.FindTunnelPath(gridManager.WorldToCell(Position), standAt);

        if (route == null)
        {
            return false;
        }

        forageTarget = body;
        gridManager.ClaimForageCell(body);

        FollowPath(route, PickUpBody);

        return true;
    }

    private void PickUpBody()
    {
        if (!forageTarget.HasValue)
        {
            GoIdle();
            return;
        }

        Vector2I body = forageTarget.Value;

        // Somebody else may have got there first, or it may have rotted away while she walked.
        if (!gridManager.IsCarrion(body))
        {
            ReleaseForageTarget();
            GoIdle();
            return;
        }

        gridManager.RemoveCarrion(body);
        ReleaseForageTarget();

        carryingBody = true;
        QueueRedraw();

        Vector2I here = gridManager.WorldToCell(Position);
        Vector2I midden = gridManager.FindSpoilDropOff(here);
        List<Vector2I> route = gridManager.FindTunnelPath(here, midden) ?? new List<Vector2I> { here };

        FollowPath(route, DropBody);
    }

    private void DropBody()
    {
        if (!carryingBody)
        {
            GoIdle();
            return;
        }

        carryingBody = false;
        QueueRedraw();

        // Laid down where she stands. A body already on the heap is refuse rather than a job, so it
        // is registered as carrion again and simply nobody comes for it while the policy stands.
        var corpse = new AntCorpse { Name = "AntCorpse", Position = Position };

        GetParent()?.AddChild(corpse);

        wanderHome = Position;
        GoIdle();
    }
    // Nothing in sight, so go and look where somebody else has been.
    //
    // She walks the trail outward and then simply goes idle again, which runs the short-range search
    // from wherever she has ended up. She is not told there is food there - she is told this is a
    // direction another ant came back from, and she goes and sees.
    private bool TryFollowTrail()
    {
        if (followedTrail)
        {
            // One go per idle spell. Otherwise a trail whose food has run out is a loop: walk to the
            // end, find nothing, go idle, walk to the end.
            followedTrail = false;
            return false;
        }

        Vector2I here = gridManager.WorldToCell(Position);

        if (!pheromones.TryFollowOutward(here, out Vector2I end))
        {
            return false;
        }

        Vector2I target = gridManager.FindNearestTunnelCell(end);
        List<Vector2I> route = gridManager.FindTunnelPath(here, target);

        if (route == null || target == here)
        {
            return false;
        }

        followedTrail = true;
        FollowPath(route, GoIdle);

        return true;
    }

    // Buried by settling spoil. Chip the cell she is standing in back open before taking any job.
    private bool TryDigOut()
    {
        Vector2I current = gridManager.WorldToCell(Position);

        if (gridManager.IsTunnel(current) || !gridManager.CanDig(current))
        {
            return false;
        }

        pendingDigCell = current;
        pendingDigCallback = GoIdle;
        state = State.Digging;
        digTimer.Start();

        return true;
    }

    // Standing over open air with nothing underfoot - drop to the floor before doing anything else.
    private bool TryFall()
    {
        Vector2I current = gridManager.WorldToCell(Position);

        if (!gridManager.IsTunnel(current) || gridManager.IsStandable(current))
        {
            return false;
        }

        Vector2I floor = gridManager.FindFloorBelow(current);

        if (floor == current)
        {
            return false;
        }

        falling = true;
        FollowPath(new List<Vector2I> { floor }, GoIdle);

        return true;
    }

    // Walk to a room's stand cell (already fully dug) and work its furnish timer.
    // Public so a test can hand a worker a furnish job without waiting for the job board to offer
    // her one. Same path TryClaimFurnishJob takes.
    public void CommandBuild(Room room)
    {
        StopCurrentTask();

        pendingRoom = room;

        Vector2I startCell = gridManager.WorldToCell(Position);
        List<Vector2I> route = gridManager.FindTunnelPath(startCell, room.StandCell) ?? new List<Vector2I> { startCell };

        FollowPath(route, StartBuilding);
    }

    private void FollowPath(List<Vector2I> cells, Action onComplete)
    {
        BeginRoute(cells, onComplete, fleeing: false);
    }

    // The one way a route starts.
    //
    // Fleeing used to set up its first waypoint by hand, duplicating the tail of AdvancePath, and
    // the escaping flag was raised in AdvancePath itself - for every route, not just an escape. So
    // any walking ant counted as fleeing, and neither the periodic danger check nor the
    // step-into-hazard check ran while she was on her way anywhere. Danger was only ever noticed by
    // an ant standing still.
    private void BeginRoute(List<Vector2I> cells, Action onComplete, bool fleeing)
    {
        pendingPath = new Queue<Vector2I>(cells);
        onPathComplete = onComplete;
        escaping = fleeing;

        AdvancePath();
    }

    private void AdvancePath()
    {
        if (pendingPath.Count == 0)
        {
            state = State.Idle;
            escaping = false;
            falling = false;

            // She has arrived, so she is standing still - not drifting off the far side of her
            // destination while a dig timer runs.
            velocity = Vector2.Zero;
            legDirection = Vector2.Zero;

            Action callback = onPathComplete;
            onPathComplete = null;
            callback?.Invoke();

            return;
        }

        Vector2I next = pendingPath.Peek();

        // Routes are planned around hazards, but the ground moves: lava flows and acid spreads long
        // after a path was worked out. Re-checking the one cell she is about to step into catches
        // that for the cost of a single lookup, and is the difference between a worker walking into
        // a flow and walking away from one.
        //
        // Except while escaping. A route out of a flow she is already standing in has to cross the
        // rest of it - FindNearestSafeCell plans that deliberately - so refusing the next cell here
        // aborted the escape and started it again, every quarter second, forever. She stood in lava
        // recomputing a way out she was never allowed to take.
        if (!escaping && gridManager.IsHazardous(next))
        {
            FleeHazard();
            return;
        }

        moveTarget = gridManager.CellToWorld(pendingPath.Dequeue());
        state = State.Walking;

        StartLeg();
    }

    // Book-keeping for one waypoint: which way it lies, and how long it may take.
    private void StartLeg()
    {
        Vector2 leg = moveTarget - Position;
        float length = leg.Length();

        legDirection = length > 0.001f ? leg / length : Vector2.Zero;
        legTimer = 0;

        // Three times as long as the leg could possibly need, plus a second of slack for turning
        // into it. Deliberately generous: this is a safety net, and a watchdog that fired during
        // ordinary walking would just be the old teleport wearing a hat.
        legDeadline = length / MoveSpeed * 3.0 + 1.0;

        // Starting from a standstill she is allowed to simply be facing the right way - an ant
        // pivots on the spot in a fraction of a second, and arcing out of stationary looks like a
        // car pulling away. Mid-route she keeps her heading and turns into the corner instead.
        if (velocity == Vector2.Zero && legDirection != Vector2.Zero)
        {
            heading = legDirection;
        }
    }

    // Drops whatever she was doing and walks to the nearest safe footing.
    //
    // The job is abandoned rather than resumed afterwards: the world has changed enough that the
    // plan behind it is stale, and she picks up work again from wherever she ends up.
    private void FleeHazard()
    {
        Vector2I here = gridManager.WorldToCell(Position);
        Vector2I refuge = gridManager.FindNearestSafeCell(here);

        // Nowhere to go. Worked out before anything is torn down on purpose - this runs on a timer,
        // and an ant with no way out would otherwise abandon her job and restart her wander every
        // time it fired, which is a lot of churn to express "she is stuck".
        if (refuge == here)
        {
            return;
        }

        StopCurrentTask();
        AbandonCurrentJob();

        pendingPath.Clear();
        plannedDigRoute.Clear();
        onPathComplete = null;

        // Whatever momentum she had was carrying her somewhere that no longer matters, and it is
        // the one moment she should visibly stop dead before bolting.
        velocity = Vector2.Zero;
        falling = false;
        antennationTimer = 0;

        // A path may not exist: FindTunnelPath refuses to route through danger, and she is standing
        // in it. The refuge is adjacent in that case, so step straight at it - hesitating inside a
        // flow to look for a prettier route is worse than the route.
        List<Vector2I> escape = gridManager.FindTunnelPath(here, refuge)
            ?? new List<Vector2I> { refuge };

        BeginRoute(
            escape,
            () =>
            {
                wanderHome = Position;
                GoIdle();
            },
            fleeing: true);
    }

    private void DigTowardTarget()
    {
        // The job is done once the cell is open, not once she is standing in it. Plenty of dig
        // targets - the upper cells of a chamber, anything overhead - are places no ant can stand,
        // and waiting to occupy one is how a digger used to strand herself breaking in from above.
        if (!gridManager.CanDig(digTarget))
        {
            // AbandonCurrentJob, not just dropping the reference. Letting go of the field without
            // telling the job board left the cell in claimedDigCells forever, so every future ant
            // skipped it and the room it belonged to stayed Excavating for the rest of the game.
            AbandonCurrentJob();
            hasDigJob = false;

            if (carriedGrains.Count > 0)
            {
                HaulGrainsThen(() =>
                {
                    wanderHome = Position;
                    GoIdle();
                });
            }
            else
            {
                wanderHome = Position;
                GoIdle();
            }

            return;
        }

        Vector2I current = gridManager.WorldToCell(Position);

        // Follow the planned corridor one cell at a time. Planning the whole run up front is what
        // keeps a descending tunnel walkable - stepping greedily saws off its own way back out.
        if (plannedDigRoute.Count == 0)
        {
            List<Vector2I> plan = gridManager.PlanDigRoute(current, digTarget);

            if (plan == null)
            {
                // Nothing diggable reaches it without stranding her. Drop the job and try again later.
                AbandonCurrentJob();
                state = State.Idle;
                WaitThenWander();

                return;
            }

            plannedDigRoute = new Queue<Vector2I>(plan);
        }

        while (plannedDigRoute.Count > 0 && plannedDigRoute.Peek() == current)
        {
            plannedDigRoute.Dequeue();
        }

        StepOrDig(plannedDigRoute.Count > 0 ? plannedDigRoute.Dequeue() : digTarget, DigTowardTarget);
    }

    private void OnDigTimeout()
    {
        // Read the material before the cell can flip to open tunnel on the final tick.
        MaterialId material = materialWorld.SpoilFor(pendingDigCell);
        Vector2I standingCell = gridManager.WorldToCell(Position);

        bool cellOpened = gridManager.DigGrain(pendingDigCell);

        // Loose soil only exists while hauling is switched on. With it off, a dig simply opens the
        // cell and there is nothing to carry, so no trips happen at all.
        if (materialWorld.HaulingEnabled && !MaterialDatabase.Get(material).IsAir)
        {
            // All of it onto her back.
            //
            // It used to tumble onto the floor at her feet first, and only what would not fit there
            // went into her jaws. That is where the permanent blocks of soil in every corridor came
            // from: the spoil was Dirt, which is Solid and never falls, so it sat exactly where it
            // was dropped and no amount of hauling could ever shift it. A digger carries what she
            // digs.
            for (int i = 0; i < GrainsPerDigTick && carriedGrains.Count < HaulCapacityGrains; i++)
            {
                carriedGrains.Add(material);
            }

            QueueRedraw();
        }

        // The wall is still standing - keep chipping at the same cell, unless this load is full.
        if (!cellOpened)
        {
            if (carriedGrains.Count >= HaulCapacityGrains)
            {
                pendingDigCallback = null;
                HaulGrainsThen(ResumeDigJob);
                return;
            }

            digTimer.Start();
            return;
        }

        Vector2I dugCell = pendingDigCell;
        Action callback = pendingDigCallback;
        pendingDigCallback = null;

        // Cell is through: scoop up whatever slumped into the new opening. Nothing is piled at her
        // feet any more, so there is no heap there to collect.
        ScoopUpLooseGrains(dugCell);

        // A full load gets hauled out immediately, mid-corridor, rather than waiting for the whole dig job to finish.
        Action next = carriedGrains.Count >= HaulCapacityGrains
            ? () => HaulGrainsThen(ResumeDigJob)
            : callback;

        // She only moves into what she just opened if there is a floor in there. Stepping into open
        // air is how a digger breaking through from above used to drop somewhere she could not climb
        // back out of.
        if (gridManager.IsStandable(dugCell))
        {
            FollowPath(new List<Vector2I> { dugCell }, next);
            return;
        }

        next?.Invoke();
    }

    private void ScoopUpLooseGrains(Vector2I cell)
    {
        int room = HaulCapacityGrains - carriedGrains.Count;

        if (room > 0 && materialWorld.Collect(cell, room, carriedGrains) > 0)
        {
            QueueRedraw();
        }
    }

    private void DropCarriedGrains()
    {
        if (carriedGrains.Count > 0)
        {
            materialWorld.Release(gridManager.WorldToCell(Position), carriedGrains);
            QueueRedraw();
        }
    }

    // Walks to the spoil dump, tips the load onto the pile beside it, then continues whatever
    // dig/forage job was interrupted to do it.
    private void HaulGrainsThen(Action afterDump)
    {
        Vector2I current = gridManager.WorldToCell(Position);
        Vector2I dropOff = gridManager.FindSpoilDropOff(current);

        // Nowhere to put it yet - see IsViableSpoilDropOff. Carry on working rather than walking to
        // nothing and back.
        if (!gridManager.IsViableSpoilDropOff(dropOff))
        {
            afterDump();
            return;
        }

        List<Vector2I> route = gridManager.FindTunnelPath(current, dropOff) ?? new List<Vector2I> { current };

        // Counted here rather than at the top of the method, so a load that had nowhere viable to
        // go and carried on working is not recorded as a trip that never happened.
        HaulTrips++;

        FollowPath(route, () =>
        {
            Vector2I arrived = gridManager.WorldToCell(Position);

            // Tip it out beside her, on the side away from the nest, so the heap builds outward
            // instead of burying the hauler or growing back across the way she came in.
            int awayFromNest = arrived.X < gridManager.NestCenterCell.X ? -1 : 1;
            Vector2I onto = arrived + new Vector2I(awayFromNest, 0);

            int leftover = materialWorld.Release(onto, carriedGrains);

            // One more try, a tile further out, in case the column she picked is already full to
            // the sky. Anything still in her jaws after that she simply keeps and carries on with -
            // bounded, because the load has a cap and she stops collecting when it is reached, and
            // it can never leave her standing still.
            if (leftover > 0)
            {
                leftover = materialWorld.Release(onto + new Vector2I(awayFromNest * 2, 0), carriedGrains);
            }

            SpoilLeftovers += leftover;

            QueueRedraw();
            afterDump();
        });
    }

    // Grains that found nowhere to go. Counted rather than shrugged off, on the same principle as
    // StallRescues: a colony quietly failing to dispose of its own spoil should show up as a number
    // somebody can read, not as a hill that mysteriously stops growing. It should be zero.
    public static int SpoilLeftovers { get; private set; }

    // How many times a worker has broken off to carry a load up to the surface.
    //
    // Currently close to meaningless, which is the point of measuring it: HaulCapacityGrains is
    // three whole tiles, so a digger opens an entire stretch of corridor before she ever has to
    // walk one out. Hauling nobody can see is hauling that may as well not be simulated.
    public static int HaulTrips { get; private set; }

    // Re-enters whichever job was interrupted for a haul trip, by target rather than by raw callback,
    // since the ant is now standing at the dump and needs a fresh route back to the dig front.
    // Back to whatever she was doing before the haul - if it is still there to do.
    //
    // Both branches used to dispatch blind. CommandForage silently returns when the source is no
    // longer a food source, which leaves her with no path, no timer and no callback - frozen,
    // holding the forage claim, so nobody else can take that deposit either. And the dig branch
    // used the raw digTarget field, which is (0,0) for an ant who never had a dig order: a worker
    // who filled her jaws digging herself out of settled spoil would, after dumping, set off on a
    // march to the top-left corner of the world.
    private void ResumeDigJob()
    {
        if (forageTarget.HasValue)
        {
            if (gridManager.IsFoodSource(forageTarget.Value))
            {
                CommandForage(forageTarget.Value);
                return;
            }

            ReleaseForageTarget();
        }

        if (hasDigJob && gridManager.CanDig(digTarget))
        {
            IssueDigCommand(digTarget, digTarget);
            return;
        }

        GoIdle();
    }

    // Shared by plain digging and forage-approach: walk into the next cell if it's already open, otherwise dig through it first.
    private void StepOrDig(Vector2I target, Action onStepComplete)
    {
        Vector2I current = gridManager.WorldToCell(Position);
        Vector2I next = gridManager.GetStepToward(current, target);

        if (gridManager.IsTunnel(next))
        {
            FollowPath(new List<Vector2I> { next }, onStepComplete);
            return;
        }

        pendingDigCell = next;
        pendingDigCallback = onStepComplete;
        state = State.Digging;
        digTimer.Start();
    }

    private void ApproachForageTarget()
    {
        Vector2I target = forageTarget!.Value;
        Vector2I current = gridManager.WorldToCell(Position);

        // Within reach counts diagonally, matching where the route planner leaves her - she harvests
        // from beside the source and never digs it out, since tunnelling through food destroys it.
        if (GridManager.IsWithinReach(current, target))
        {
            if (carriedGrains.Count > 0)
            {
                HaulGrainsThen(ResumeDigJob);
                return;
            }

            plannedDigRoute.Clear();
            state = State.Foraging;
            forageTimer.Start();
            return;
        }

        // Buried food needs the same planned corridor that ordinary digging uses. Stepping greedily
        // cannot descend - the router refuses to undercut open ground - so a forager sent at a
        // deposit below her would pace sideways forever instead of digging down to it.
        if (plannedDigRoute.Count == 0)
        {
            List<Vector2I> plan = gridManager.PlanDigRoute(current, target);

            if (plan == null)
            {
                GiveUpOnForageTarget();
                return;
            }

            plannedDigRoute = new Queue<Vector2I>(plan);
        }

        while (plannedDigRoute.Count > 0 && plannedDigRoute.Peek() == current)
        {
            plannedDigRoute.Dequeue();
        }

        if (plannedDigRoute.Count == 0)
        {
            // Walked the whole corridor and still not beside it - the source is not actually
            // approachable, so hand it back rather than looping on it.
            GiveUpOnForageTarget();
            return;
        }

        StepOrDig(plannedDigRoute.Dequeue(), ApproachForageTarget);
    }

    private void GiveUpOnForageTarget()
    {
        plannedDigRoute.Clear();
        ReleaseForageTarget();
        wanderHome = Position;
        GoIdle();
    }


    private void OnForageTimeout()
    {
        Vector2I target = forageTarget!.Value;
        int harvested = gridManager.Harvest(target, HarvestPerTick);
        carriedFood += harvested;

        bool full = carriedFood >= ForageCarryCapacity;
        bool depleted = harvested == 0 || !gridManager.IsFoodSource(target);

        if (full || depleted)
        {
            ReturnToStorage();
            return;
        }

        forageTimer.Start();
    }

    private void ReturnToStorage()
    {
        state = State.Idle;

        if (carriedFood <= 0)
        {
            forageTarget = null;
            wanderHome = Position;
            GoIdle();
            return;
        }

        Vector2I current = gridManager.WorldToCell(Position);
        // Food is deposited at the nest itself; there are no separate storage cells.
        Vector2I storageCell = gridManager.NestCenterCell;
        List<Vector2I> route = gridManager.FindTunnelPath(current, storageCell) ?? new List<Vector2I> { current };

        FollowPath(route, DepositFood);
    }


    // Hands her load to the colony, and says so when it does not all fit.
    //
    // AddFood clamps to the larder and returns what actually landed, and both callers used to throw
    // that away - so a forager arriving at full stores silently deleted everything she was carrying,
    // while a digger cutting through a seed cache got a warning about exactly the same loss.
    private void ReturnCarriedFood()
    {
        // Deliberately silent about the overflow.
        //
        // AddFood clamps and returns what landed, and both callers used to throw that away - but
        // announcing each shortfall turned out to be worse than saying nothing: once the larder is
        // capped every single forager arrival raises it, several times a report. The latched "Food
        // stores are full" ceiling already tells the player the one thing they can act on, which is
        // to build a Granary. The digger who cuts through a seed cache still gets her own warning,
        // because that is a single large loss she could have avoided rather than a steady trickle.
        colonyManager.AddFood(carriedFood);
        carriedFood = 0;
    }
    private void DepositFood()
    {
        ReturnCarriedFood();

        if (forageTarget.HasValue && gridManager.IsFoodSource(forageTarget.Value))
        {
            CommandForage(forageTarget.Value);
            return;
        }

        ReleaseForageTarget();
        wanderHome = Position;
        GoIdle();
    }

    // Letting go of a source hands it back to the job board, so the next idle worker can take it on.
    private void ReleaseForageTarget()
    {
        if (forageTarget.HasValue)
        {
            gridManager.ReleaseForageClaim(forageTarget.Value);
            forageTarget = null;
        }
    }

    private static bool IsAdjacent(Vector2I a, Vector2I b)
    {
        return Mathf.Abs(a.X - b.X) + Mathf.Abs(a.Y - b.Y) == 1;
    }



    // The room she was going to furnish has been pulled down.
    //
    // Pushed to her rather than checked by her. A validity check alone stops the crash but leaves
    // her holding a job that no longer exists with no story for what she does next; being told means
    // she can unwind and go find other work, which is the behaviour actually wanted. It also avoids
    // a signal subscription per ant, which would be a lifetime problem the moment ants start dying.
    public void ForgetRoom(Room room)
    {
        if (pendingRoom != room)
        {
            return;
        }

        pendingRoom = null;
        buildTimer.Stop();

        if (state == State.Building)
        {
            GoIdle();
        }
    }
    private void StartBuilding()
    {
        // The room can have been pulled down while she was walking to it.
        if (!GodotObject.IsInstanceValid(pendingRoom))
        {
            pendingRoom = null;
            GoIdle();
            return;
        }

        state = State.Building;

        // Floored, because a restored room recomputes its cells from current terrain and can come
        // back owning none. Godot refuses a non-positive wait time and silently keeps the previous
        // one, so the symptom would be a room furnished in whatever the last room took.
        buildTimer.WaitTime = Mathf.Max(
            0.1f,
            BuildingDefs.All[pendingRoom.Type].FurnishSecondsPerCell * pendingRoom.CellCount);

        buildTimer.Start();
    }

    private void OnBuildTimeout()
    {
        Room room = pendingRoom;
        pendingRoom = null;

        // Only if it is still there. Demolishing a room mid-furnish used to land here holding a
        // freed node, and ReportFurnishDone calls Activate on it, which throws inside a timer
        // handler - Godot prints and swallows that, leaving her in Building with no way out.
        if (GodotObject.IsInstanceValid(room))
        {
            buildManager.ReportFurnishDone(room);
        }

        wanderHome = Position;
        GoIdle();
    }

    private void OnMoveComplete()
    {
        wanderHome = Position;
        GoIdle();
    }

    // No guard on the state here any more. There used to be an early return unless she was already
    // Idle, which quietly turned "she had nothing else to do" into "she does nothing ever again" -
    // GoIdle now normalises on entry, so by the time this runs she is Idle by construction.
    private void PickWanderTarget()
    {
        Vector2I home = gridManager.WorldToCell(wanderHome);
        Vector2I candidate = home + new Vector2I(
            (int)GD.RandRange(-WanderCellRadius, WanderCellRadius),
            (int)GD.RandRange(-WanderCellRadius, WanderCellRadius)
        );

        Vector2I current = gridManager.WorldToCell(Position);
        Vector2I wanderTarget = gridManager.FindNearestTunnelCell(candidate);
        List<Vector2I> route = gridManager.FindTunnelPath(current, wanderTarget);

        if (route == null)
        {
            WaitThenWander();
            return;
        }

        FollowPath(route, WaitThenWander);
    }

    private void WaitThenWander()
    {
        wanderTimer.WaitTime = GD.RandRange(MinWanderPause, MaxWanderPause);
        wanderTimer.Start();
    }

    // Which of the eight ways she is drawn facing.
    //
    // Four of them used to be all there was, and on a diagonal |dx| equalled |dy| exactly, so she
    // rendered end-on while travelling at forty-five degrees and visibly slid down every ramp in
    // the colony. Steering then made it worse by turning the heading into a continuously varying
    // angle: a bare comparison around the diagonal flips several times a second and the sprite
    // strobes.
    //
    // Rotating one sprite would have been free and was the wrong answer. Twelve-pixel crisp-edge
    // art does not survive nearest-neighbour rotation at anything but multiples of ninety degrees -
    // it shimmers as the angle changes, which trades a strobe for a worse strobe.
    private void UpdateFacing(Vector2 direction)
    {
        if (direction == Vector2.Zero)
        {
            return;
        }

        int wanted = Mathf.PosMod(Mathf.RoundToInt(direction.Angle() / SectorRadians), Octants);

        if (wanted == facingOctant)
        {
            return;
        }

        // She has to be clearly into the next sector rather than merely over its border. Without
        // this a heading hovering on a boundary - which is exactly what a long shallow diagonal
        // produces - swaps the sprite back and forth every few frames.
        float fromCurrent = Mathf.Abs(Mathf.AngleDifference(facingOctant * SectorRadians, direction.Angle()));

        if (fromCurrent < SectorRadians / 2f + FacingHysteresisRadians)
        {
            return;
        }

        facingOctant = wanted;
        facing = OctantDirections[wanted];

        ApplyFacingSprite();

        // Anything in her jaws has to swing round with her.
        if (carriedGrains.Count > 0)
        {
            QueueRedraw();
        }
    }

    // One diagonal sprite serves all four diagonals.
    //
    // A top-down ant is bilaterally symmetric about her own body axis, so mirroring a down-right
    // ant across either screen axis gives a genuine up-right or down-left ant rather than an ant
    // with her legs on wrong. That is three sprites of artwork saved, and - more to the point -
    // three sprites that cannot drift out of step with each other.
    private void ApplyFacingSprite()
    {
        switch (facingOctant)
        {
            case 0:
                Show(RightTexture, flipH: false, flipV: false);
                break;
            case 1:
                Show(DiagonalTexture, flipH: false, flipV: false);
                break;
            case 2:
                Show(DownTexture, flipH: false, flipV: false);
                break;
            case 3:
                Show(DiagonalTexture, flipH: true, flipV: false);
                break;
            case 4:
                Show(LeftTexture, flipH: false, flipV: false);
                break;
            case 5:
                Show(DiagonalTexture, flipH: true, flipV: true);
                break;
            case 6:
                Show(UpTexture, flipH: false, flipV: false);
                break;
            default:
                Show(DiagonalTexture, flipH: false, flipV: true);
                break;
        }
    }

    private void Show(Texture2D texture, bool flipH, bool flipV)
    {
        sprite.Texture = texture;
        sprite.FlipH = flipH;
        sprite.FlipV = flipV;
    }

    private void OnInputEvent(Node viewport, InputEvent @event, long shapeIdx)
    {
        if (@event is InputEventMouseButton mouseButton &&
            mouseButton.ButtonIndex == MouseButton.Left &&
            mouseButton.Pressed)
        {
            if (mouseButton.ShiftPressed)
            {
                selectionManager.ToggleSelect(this);
            }
            else
            {
                selectionManager.Select(this);
            }

            GetViewport().SetInputAsHandled();
        }
    }
}
