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
    private const float DigSeconds = 2f;
    private const int WanderCellRadius = 3;
    private const double MinWanderPause = 1.0;
    private const double MaxWanderPause = 3.0;
    private const float SelectionRingRadius = 6f;
    private const float ForageSeconds = 1f;
    private const int ForageCarryCapacity = 5;
    private const int HarvestPerTick = 1;

    private const float BuildStorageSeconds = 3f;

    private static readonly Color SelectionRingColor = new Color(1f, 1f, 0.4f);

    private static readonly Texture2D UpTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Up.png");
    private static readonly Texture2D DownTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Down.png");
    private static readonly Texture2D LeftTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Left.png");
    private static readonly Texture2D RightTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Right.png");

    private Sprite2D sprite;
    private Timer digTimer;
    private Timer wanderTimer;
    private Timer forageTimer;
    private Timer buildTimer;
    private GridManager gridManager;
    private SelectionManager selectionManager;
    private ColonyManager colonyManager;

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
    private Vector2I buildStorageTarget;

    public override void _Ready()
    {
        AddToGroup("ants");

        sprite = GetNode<Sprite2D>("Sprite2D");
        digTimer = GetNode<Timer>("DigTimer");
        wanderTimer = GetNode<Timer>("WanderTimer");

        gridManager = GetNode<GridManager>("/root/Main/GridManager");
        selectionManager = GetNode<SelectionManager>("/root/Main/SelectionManager");
        colonyManager = GetNode<ColonyManager>("/root/Main/ColonyManager");

        forageTimer = GetNode<Timer>("ForageTimer");
        buildTimer = GetNode<Timer>("BuildTimer");

        digTimer.OneShot = true;
        digTimer.WaitTime = DigSeconds;
        digTimer.Timeout += OnDigTimeout;

        forageTimer.OneShot = true;
        forageTimer.WaitTime = ForageSeconds;
        forageTimer.Timeout += OnForageTimeout;

        buildTimer.OneShot = true;
        buildTimer.WaitTime = BuildStorageSeconds;
        buildTimer.Timeout += OnBuildTimeout;

        wanderTimer.OneShot = true;
        wanderTimer.Timeout += PickWanderTarget;

        InputEvent += OnInputEvent;

        wanderHome = Position;
        PickWanderTarget();
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
        IssueDigCommand(targetCell, targetCell);
    }

    // Ignore existing tunnels entirely and start digging a corridor from wherever she already is.
    public void CommandDigDirect(Vector2I targetCell)
    {
        IssueDigCommand(targetCell, gridManager.WorldToCell(Position));
    }

    // Walk to an already-dug tunnel cell without digging anything.
    public void CommandMove(Vector2I targetCell)
    {
        StopCurrentTask();

        Vector2I startCell = gridManager.WorldToCell(Position);
        List<Vector2I> route = gridManager.FindTunnelPath(startCell, targetCell) ?? new List<Vector2I> { startCell };

        FollowPath(route, OnMoveComplete);
    }


    public void CommandForage(Vector2I targetCell)
    {
        if (!gridManager.IsFoodSource(targetCell))
        {
            return;
        }

        StopCurrentTask();

        forageTarget = targetCell;

        Vector2I startCell = gridManager.WorldToCell(Position);
        Vector2I nearestTunnel = gridManager.FindNearestTunnelCell(targetCell);
        List<Vector2I> route = gridManager.FindTunnelPath(startCell, nearestTunnel) ?? new List<Vector2I> { startCell };

        FollowPath(route, ApproachForageTarget);
    }

    public void CommandBuildStorage(Vector2I targetCell)
    {
        if (!gridManager.IsTunnel(targetCell))
        {
            return;
        }

        StopCurrentTask();

        buildStorageTarget = targetCell;

        Vector2I startCell = gridManager.WorldToCell(Position);
        List<Vector2I> route = gridManager.FindTunnelPath(startCell, targetCell) ?? new List<Vector2I> { startCell };

        FollowPath(route, StartBuildingStorage);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!isSelected)
        {
            return;
        }

        DrawArc(Vector2.Zero, SelectionRingRadius, 0f, Mathf.Tau, 24, SelectionRingColor, 2f, true);
    }

    private void IssueDigCommand(Vector2I targetCell, Vector2I wallReferenceCell)
    {
        StopCurrentTask();

        digTarget = targetCell;

        Vector2I startCell = gridManager.WorldToCell(Position);
        Vector2I wallCell = gridManager.FindNearestTunnelCell(wallReferenceCell);
        List<Vector2I> route = gridManager.FindTunnelPath(startCell, wallCell) ?? new List<Vector2I> { startCell };

        FollowPath(route, DigTowardTarget);
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
            wanderHome = Position;
            PickWanderTarget();
            return;
        }

        StepOrDig(digTarget, DigTowardTarget);
    }

    private void OnDigTimeout()
    {
        gridManager.Dig(pendingDigCell);

        Vector2I dugCell = pendingDigCell;
        Action callback = pendingDigCallback;
        pendingDigCallback = null;

        FollowPath(new List<Vector2I> { dugCell }, callback);
    }

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
            PickWanderTarget();
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
        PickWanderTarget();
    }

    private void StartBuildingStorage()
    {
        state = State.Building;
        buildTimer.Start();
    }

    private void OnBuildTimeout()
    {
        gridManager.BuildFoodStorage(buildStorageTarget);
        wanderHome = Position;
        PickWanderTarget();
    }

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

        forageTarget = null;
    }

    private static bool IsAdjacent(Vector2I a, Vector2I b)
    {
        return Mathf.Abs(a.X - b.X) + Mathf.Abs(a.Y - b.Y) == 1;
    }

    private void OnMoveComplete()
    {
        wanderHome = Position;
        PickWanderTarget();
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
