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

    private const float MoveSpeed = 24f;
    private const float ArrivalDistance = 2f;
    // Seconds to excavate one whole cell, split evenly across the grains it is made of.
    private const float DigSeconds = 2f;
    private const float DigSecondsPerGrain = DigSeconds / GridManager.GrainsPerCell;
    private const int WanderCellRadius = 3;
    private const double MinWanderPause = 1.0;
    private const double MaxWanderPause = 3.0;
    private const float SelectionRingRadius = 6f;
    private const float ForageSeconds = 1f;
    private const int ForageCarryCapacity = 5;
    private const int HarvestPerTick = 1;
    // Each dig tick scrapes out a share of the cell; a full load is three cells worth, matching the old haul cadence.
    private const int GrainsPerDigTick = ParticleField.SlotsPerCell / GridManager.GrainsPerCell;
    private const int HaulCapacityGrains = ParticleField.SlotsPerCell * 3;
    private const int MaxCarriedSpecksDrawn = 10;
    private const int CarriedSpecksPerRow = 4;
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
    private ParticleField particleField;

    private State state = State.Idle;
    private bool isSelected;
    private Vector2 wanderHome;
    private Vector2 moveTarget;
    private Queue<Vector2I> pendingPath = new Queue<Vector2I>();
    private Action onPathComplete;
    private Vector2I digTarget;
    private Vector2I pendingDigCell;
    private Action pendingDigCallback;
    private Vector2I? forageTarget;
    private int carriedFood;
    private readonly List<GridManager.TileType> carriedGrains = new();
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

        gridManager = GetNode<GridManager>("/root/Main/GridManager");
        selectionManager = GetNode<SelectionManager>("/root/Main/SelectionManager");
        buildManager = GetNode<BuildManager>("/root/Main/BuildManager");
        colonyManager = GetNode<ColonyManager>("/root/Main/ColonyManager");
        particleField = GetNode<ParticleField>("/root/Main/ParticleField");

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
        if (state != State.Walking)
        {
            return;
        }

        Vector2 direction = moveTarget - Position;
        float distance = direction.Length();

        if (distance <= ArrivalDistance)
        {
            Position = moveTarget;
            AdvancePath();
            return;
        }

        Vector2 step = direction.Normalized() * MoveSpeed * (float)delta;
        Position += step.Length() > distance ? direction : step;
        UpdateFacing(direction);
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
        List<Vector2I> route = gridManager.FindTunnelPath(startCell, targetCell) ?? new List<Vector2I> { startCell };

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

        forageTarget = targetCell;

        Vector2I startCell = gridManager.WorldToCell(Position);
        Vector2I nearestTunnel = gridManager.FindNearestTunnelCell(targetCell);
        List<Vector2I> route = gridManager.FindTunnelPath(startCell, nearestTunnel) ?? new List<Vector2I> { startCell };

        FollowPath(route, ApproachForageTarget);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawCarriedGrains();

        if (isSelected)
        {
            DrawArc(Vector2.Zero, SelectionRingRadius, 0f, Mathf.Tau, 24, SelectionRingColor, 2f, true);
        }
    }

    // The load she is actually carrying, heaped on her back. Without this a haul reads as the dirt
    // vanishing at the dig face and reappearing on the pile.
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

        for (int i = 0; i < specks; i++)
        {
            int row = i / CarriedSpecksPerRow;
            int column = i % CarriedSpecksPerRow;

            Vector2 offset = new Vector2(
                (column - (CarriedSpecksPerRow - 1) / 2f) * CarriedSpeckSize + row * CarriedSpeckSize / 2f,
                -SelectionRingRadius - CarriedSpeckSize - row * CarriedSpeckSize
            );

            // Sample across the load so a mixed haul shows the materials it is actually made of.
            Color color = ParticleField.ColorFor(carriedGrains[i * carriedGrains.Count / specks]);

            DrawRect(new Rect2(offset, new Vector2(CarriedSpeckSize, CarriedSpeckSize)), color, filled: true);
        }
    }


    private void IssueDigCommand(Vector2I targetCell, Vector2I wallReferenceCell)
    {
        // Cancel whatever timed action was in progress so it doesn't fire against the old target later.
        StopCurrentTask();

        digTarget = targetCell;
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

        forageTarget = null;
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

        if (buildManager.TryClaimFurnishJob(Position, out Room room))
        {
            CommandBuild(room);
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
        pendingPath = new Queue<Vector2I>(cells);
        onPathComplete = onComplete;
        AdvancePath();
    }

    private void AdvancePath()
    {
        if (pendingPath.Count == 0)
        {
            state = State.Idle;

            Action callback = onPathComplete;
            onPathComplete = null;
            callback?.Invoke();

            return;
        }

        moveTarget = gridManager.CellToWorld(pendingPath.Dequeue());
        state = State.Walking;
    }

    private void DigTowardTarget()
    {
        Vector2I current = gridManager.WorldToCell(Position);

        if (current == digTarget)
        {
            claimedJobCell = null;

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
        GridManager.TileType material = gridManager.GetTileAt(pendingDigCell);
        Vector2I standingCell = gridManager.WorldToCell(Position);

        bool cellOpened = gridManager.DigGrain(pendingDigCell);

        // The scraped-out material tumbles onto the floor at her feet. Anything with nowhere to land
        // (she is walled in, or the floor is already heaped up) goes straight onto her back instead.
        int spilled = particleField.Emit(standingCell, GrainsPerDigTick, material);

        for (int i = spilled; i < GrainsPerDigTick && carriedGrains.Count < HaulCapacityGrains; i++)
        {
            carriedGrains.Add(material);
        }

        QueueRedraw();

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
        if (carriedGrains.Count >= HaulCapacityGrains)
        {
            FollowPath(new List<Vector2I> { dugCell }, () => HaulGrainsThen(ResumeDigJob));
            return;
        }

        FollowPath(new List<Vector2I> { dugCell }, callback);
    }

    private void ScoopUpLooseGrains(Vector2I cell)
    {
        int room = HaulCapacityGrains - carriedGrains.Count;

        if (room > 0 && particleField.Collect(cell, room, carriedGrains) > 0)
        {
            QueueRedraw();
        }
    }

    private void DropCarriedGrains()
    {
        if (carriedGrains.Count > 0)
        {
            particleField.Release(gridManager.WorldToCell(Position), carriedGrains);
            QueueRedraw();
        }
    }

    // Walks to the spoil dump, tips the load onto the pile beside it, then continues whatever
    // dig/forage job was interrupted to do it.
    private void HaulGrainsThen(Action afterDump)
    {
        Vector2I current = gridManager.WorldToCell(Position);
        List<Vector2I> route = gridManager.FindTunnelPath(current, gridManager.SpoilDumpCell) ?? new List<Vector2I> { current };

        FollowPath(route, () =>
        {
            Vector2I arrived = gridManager.WorldToCell(Position);

            // Only tip onto the pile if she actually reached the dump. If the route failed she never
            // went anywhere, and the load has to go down at her feet rather than across the map.
            bool atDump = GridManager.IsWithinReach(arrived, gridManager.SpoilDumpCell);

            particleField.Release(atDump ? gridManager.SpoilPileCell : arrived, carriedGrains);
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

        if (IsAdjacent(current, target))
        {
            if (carriedGrains.Count > 0)
            {
                HaulGrainsThen(ResumeDigJob);
                return;
            }

            state = State.Foraging;
            forageTimer.Start();
            return;
        }

        StepOrDig(target, ApproachForageTarget);
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
        Vector2I storageCell = gridManager.GetNearestStorageCell(current);
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

        forageTarget = null;
        wanderHome = Position;
        GoIdle();
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
        sprite.Texture = Mathf.Abs(direction.X) > Mathf.Abs(direction.Y)
            ? (direction.X > 0 ? RightTexture : LeftTexture)
            : (direction.Y > 0 ? DownTexture : UpTexture);
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
