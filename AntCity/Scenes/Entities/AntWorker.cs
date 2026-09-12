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
        Building
    }

    private const float MoveSpeed = 24f;
    private const float ArrivalDistance = 2f;
    private const float DigSeconds = 2f;
    private const int WanderCellRadius = 3;
    private const double MinWanderPause = 1.0;
    private const double MaxWanderPause = 3.0;
    private const float SelectionRingRadius = 6f;

    private static readonly Color SelectionRingColor = new Color(1f, 1f, 0.4f);

    private static readonly Texture2D UpTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Up.svg");
    private static readonly Texture2D DownTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Down.svg");
    private static readonly Texture2D LeftTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Left.svg");
    private static readonly Texture2D RightTexture = GD.Load<Texture2D>("res://AntCity/Textures/Red Ant Right.svg");

    private Sprite2D sprite;
    private Timer digTimer;
    private Timer wanderTimer;
    private Timer buildTimer;
    private GridManager gridManager;
    private SelectionManager selectionManager;
    private BuildManager buildManager;

    private State state = State.Idle;
    private bool isSelected;
    private Vector2 wanderHome;
    private Vector2 moveTarget;
    private Queue<Vector2I> pendingPath = new Queue<Vector2I>();
    private Action onPathComplete;
    private Vector2I digTarget;
    private Vector2I pendingDigCell;
    private Room pendingRoom;
    private Vector2I? claimedJobCell;

    public override void _Ready()
    {
        AddToGroup("ants");

        sprite = GetNode<Sprite2D>("Sprite2D");
        digTimer = GetNode<Timer>("DigTimer");
        wanderTimer = GetNode<Timer>("WanderTimer");
        buildTimer = GetNode<Timer>("BuildTimer");

        gridManager = GetNode<GridManager>("/root/Main/GridManager");
        selectionManager = GetNode<SelectionManager>("/root/Main/SelectionManager");
        buildManager = GetNode<BuildManager>("/root/Main/BuildManager");

        digTimer.OneShot = true;
        digTimer.WaitTime = DigSeconds;
        digTimer.Timeout += OnDigTimeout;

        wanderTimer.OneShot = true;
        wanderTimer.Timeout += GoIdle;

        buildTimer.OneShot = true;
        buildTimer.Timeout += OnBuildTimeout;

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
        StopActiveTimers();

        Vector2I startCell = gridManager.WorldToCell(Position);
        List<Vector2I> route = gridManager.FindTunnelPath(startCell, targetCell) ?? new List<Vector2I> { startCell };

        FollowPath(route, OnMoveComplete);
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
        // Cancel whatever timed action was in progress so it doesn't fire against the old target later.
        StopActiveTimers();

        digTarget = targetCell;

        Vector2I startCell = gridManager.WorldToCell(Position);
        Vector2I wallCell = gridManager.FindNearestTunnelCell(wallReferenceCell);
        List<Vector2I> route = gridManager.FindTunnelPath(startCell, wallCell) ?? new List<Vector2I> { startCell };

        FollowPath(route, DigTowardTarget);
    }

    private void StopActiveTimers()
    {
        if (state == State.Digging)
        {
            digTimer.Stop();
        }
        else if (state == State.Building)
        {
            buildTimer.Stop();
        }
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

    // Walk to a room's stand cell (already fully dug) and work its furnish timer.
    private void CommandBuild(Room room)
    {
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
            wanderHome = Position;
            GoIdle();
            return;
        }

        pendingDigCell = gridManager.GetStepToward(current, digTarget);
        state = State.Digging;
        digTimer.Start();
    }

    private void OnDigTimeout()
    {
        gridManager.Dig(pendingDigCell);
        FollowPath(new List<Vector2I> { pendingDigCell }, DigTowardTarget);
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
