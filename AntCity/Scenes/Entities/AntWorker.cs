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
    // How far afield an idle worker will look for something to forage, in world units. Expressed in
    // tiles rather than as a pixel literal, so it means the same thing if the tile size ever moves.
    private const int ForageSearchTiles = 24;
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

    private static readonly Color SelectionRingColor = new Color(1f, 1f, 0.4f);

    private static readonly Texture2D UpTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Up.svg");
    private static readonly Texture2D DownTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Down.svg");
    private static readonly Texture2D LeftTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Left.svg");
    private static readonly Texture2D RightTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Right.svg");

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

    private State state = State.Idle;
    private bool isSelected;
    private Vector2 wanderHome;
    private Vector2 moveTarget;
    private Vector2 facing = Vector2.Down;

    // Where she is actually going and how fast, as opposed to where the path says she should be.
    private Vector2 velocity;
    private Vector2 heading = Vector2.Right;
    // The straight line from where this leg began to its waypoint. Kept so she can tell "reached
    // it" from "went past it", which distance alone cannot answer once she travels in arcs.
    private Vector2 legDirection;
    private double legTimer;
    private double legDeadline;
    private bool falling;

    private Queue<Vector2I> pendingPath = new Queue<Vector2I>();
    private Action onPathComplete;
    private Vector2I digTarget;
    private bool hasDigJob;
    private Vector2I pendingDigCell;
    private Action pendingDigCallback;
    private double hazardCheckTimer;

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
        digTimer = GetNode<Timer>("DigTimer");
        wanderTimer = GetNode<Timer>("WanderTimer");
        forageTimer = GetNode<Timer>("ForageTimer");
        buildTimer = GetNode<Timer>("BuildTimer");

        gridManager = GetNode<GridManager>("../GridManager");
        selectionManager = GetNode<SelectionManager>("../SelectionManager");
        buildManager = GetNode<BuildManager>("../BuildManager");
        colonyManager = GetNode<ColonyManager>("../ColonyManager");
        materialWorld = GetNode<MaterialWorld>("../MaterialWorld");

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
        }

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
        float cap = falling ? FallSpeed : MoveSpeed;
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

        float speed = Mathf.MoveToward(velocity.Length(), targetSpeed, Acceleration * (float)delta);
        velocity = heading * speed;

        Position += velocity * (float)delta;
        UpdateFacing(heading);

        WatchForStall(delta);
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
        escaping = false;

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

        carriedFood = save.CarriedFood;
        carriedGrains.Clear();

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
            DrawArc(BodyOffset, SelectionRingRadius, 0f, Mathf.Tau, 24, SelectionRingColor, 2f, true);
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

        Vector2 mouth = facing * MouthOffset;
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
            colonyManager.AddFood(carriedFood);
            carriedFood = 0;
        }

        // An interrupted haul tips its load out where it stands rather than deleting it - the
        // material came out of the ground, so it has to end up somewhere.
        DropCarriedGrains();

        ReleaseForageTarget();
        hasDigJob = false;
    }

    // Release whatever job-board work is in flight so a manual command doesn't leave it stuck claimed forever.
    private void AbandonCurrentJob()
    {
        if (claimedJobCell.HasValue)
        {
            buildManager.ReleaseClaim(claimedJobCell.Value);
            claimedJobCell = null;
        }

        if (pendingRoom != null)
        {
            pendingRoom.FurnishClaimed = false;
            pendingRoom = null;
        }
    }

    // Look for open job-board work before falling back to aimless wandering.
    private void GoIdle()
    {
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

        // Food is the one thing the colony always needs, and nobody else is going to fetch it. An idle
        // worker goes looking rather than milling about, which is what lets the colony feed itself.
        if (gridManager.TryFindForageTarget(Position, ForageSearchTiles * gridManager.CellSize, out Vector2I food))
        {
            CommandForage(food);
            return;
        }

        PickWanderTarget();
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
    private void CommandBuild(Room room)
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
            claimedJobCell = null;
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
        MaterialId material = materialWorld.GetTileMaterial(pendingDigCell);
        Vector2I standingCell = gridManager.WorldToCell(Position);

        bool cellOpened = gridManager.DigGrain(pendingDigCell);

        // Loose soil only exists while the dirt simulation is switched on. With it off, a dig simply
        // opens the cell and there is nothing to carry, so no hauling trips happen at all.
        if (materialWorld.HaulingEnabled)
        {
            // The scraped-out material tumbles onto the floor at her feet. Anything with nowhere to
            // land (she is walled in, or the floor is already heaped up) goes onto her back instead.
            int spilled = materialWorld.EmitInto(standingCell, GrainsPerDigTick, material);

            for (int i = spilled; i < GrainsPerDigTick && carriedGrains.Count < HaulCapacityGrains; i++)
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

        // Cell is through: scoop up the heap she has been piling at her feet, plus anything that
        // tumbled into the new opening.
        ScoopUpLooseGrains(standingCell);
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
        List<Vector2I> route = gridManager.FindTunnelPath(current, dropOff) ?? new List<Vector2I> { current };

        FollowPath(route, () =>
        {
            Vector2I arrived = gridManager.WorldToCell(Position);

            // Tip it out beside her, on the side away from the nest, so the heap builds outward
            // instead of burying the hauler or growing back across the way she came in.
            int awayFromNest = arrived.X < gridManager.NestCenterCell.X ? -1 : 1;

            materialWorld.Release(arrived + new Vector2I(awayFromNest, 0), carriedGrains);
            QueueRedraw();
            afterDump();
        });
    }

    // Re-enters whichever job was interrupted for a haul trip, by target rather than by raw callback,
    // since the ant is now standing at the dump and needs a fresh route back to the dig front.
    private void ResumeDigJob()
    {
        if (forageTarget.HasValue)
        {
            CommandForage(forageTarget.Value);
        }
        else
        {
            IssueDigCommand(digTarget, digTarget);
        }
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

    private void DepositFood()
    {
        colonyManager.AddFood(carriedFood);
        carriedFood = 0;

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


    private void StartBuilding()
    {
        state = State.Building;
        buildTimer.WaitTime = BuildingDefs.All[pendingRoom.Type].FurnishSecondsPerCell * pendingRoom.CellCount;
        buildTimer.Start();
    }

    private void OnBuildTimeout()
    {
        Room room = pendingRoom;
        pendingRoom = null;
        buildManager.ReportFurnishDone(room);

        wanderHome = Position;
        GoIdle();
    }

    private void OnMoveComplete()
    {
        wanderHome = Position;
        GoIdle();
    }

    private void PickWanderTarget()
    {
        if (state != State.Idle)
        {
            return;
        }

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

    private void UpdateFacing(Vector2 direction)
    {
        float alongX = Mathf.Abs(direction.X);
        float alongY = Mathf.Abs(direction.Y);

        // Sticky sideways, for two reasons that used to be one bug each.
        //
        // Ties go to horizontal: every ramp in the game is an exact 45 degrees, because
        // MoveDirections only holds unit diagonals - so |dx| == |dy| exactly, a strict comparison
        // fell through to the vertical branch, and the ant rendered facing Up or Down while walking
        // sideways. She visibly slid down every slope in the colony.
        //
        // And horizontal holds until clearly beaten, because heading is now a continuously turning
        // vector rather than one of eight fixed directions. Around 45 degrees a bare comparison
        // flips several times a second and the sprite strobes. Diagonal art is the real answer;
        // until then she has to commit a quarter past the diagonal before the view of her changes.
        bool wasHorizontal = facing.X != 0f;
        bool horizontal = wasHorizontal ? alongY <= alongX * 1.25f : alongX >= alongY;

        Vector2 turned = horizontal
            ? new Vector2(Mathf.Sign(direction.X), 0f)
            : new Vector2(0f, Mathf.Sign(direction.Y));

        if (turned == facing || turned == Vector2.Zero)
        {
            return;
        }

        facing = turned;

        sprite.Texture = horizontal
            ? (direction.X > 0 ? RightTexture : LeftTexture)
            : (direction.Y > 0 ? DownTexture : UpTexture);

        // Anything in her jaws has to swing round with her.
        if (carriedGrains.Count > 0)
        {
            QueueRedraw();
        }
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
