using Godot;
using System;
using System.Threading.Tasks;

// No save/load calls. A real grid and job board, with two deliberately isolated pockets.
public partial class JobReachabilityTests : Node
{
    private GridManager grid;
    private BuildManager build;
    private int passed;
    private int failed;
    private readonly Vector2I origin = new(-50, 30);
    private Vector2I Left => origin + new Vector2I(2, 3);
    private Vector2I Right => origin + new Vector2I(10, 3);
    private Vector2I Older => origin + new Vector2I(3, 3);
    private Vector2I Later => origin + new Vector2I(19, 3);

    public override async void _Ready()
    {
        try
        {
            GetNode("Main").ProcessMode = ProcessModeEnum.Disabled;
            grid = GetNode<GridManager>("Main/GridManager");
            build = GetNode<BuildManager>("Main/BuildManager");
            Fixture();
            Check(grid.PlanDigRoute(Right, Older) == null, "fixture: old job is isolated from right worker");
            Check(grid.PlanDigRoute(Left, Older) != null, "fixture: left worker reaches old job");
            Check(grid.PlanDigRoute(Right, Later) != null, "fixture: right worker reaches later job");
            Mark(Older);
            Mark(Later);
            Expect(Right, Later, "skip unreachable older designation");
            Expect(Left, Older, "one origin's failure does not hide work from another");

            // Connecting the pockets must not leave a permanent failed-job entry.
            OpenBridge();
            Expect(Right, Later, "failed route is briefly cached after connection");
            await Task.Delay(3200);
            Expect(Right, Older, "retry older designation after cooldown");

            Fixture();
            Mark(Older);
            Mark(Later);
            Expect(Right, Later, "cache another failure before reset");
            OpenBridge();
            build.ResetTransientState();
            Mark(Older);
            Mark(Later);
            Expect(Right, Older, "world reset clears failed routes");

            for (int i = 0; i < 4; i++)
                Expect(Right, Older, "oldest reachable designation keeps team capacity", release: false);
            Expect(Right, Later, "full team overflows into later batch");
            for (int i = 0; i < 4; i++) build.ReleaseClaim(Older);

            Fixture();
            GetNode<ColonyManager>("Main/ColonyManager").AddFood(200);
            Set(19, 2, GridManager.TileType.Dirt);
            Check(build.TryCreateRoom(new Rect2I(origin + new Vector2I(18, 2), new Vector2I(2, 2)), BuildingType.Granary),
                "fixture: reachable room placed");
            build.NoticeObstructionForTest(Older);
            Check(!build.TryClaimObstruction(grid.CellToWorld(Right), out _), "isolated obstruction is not offered to right worker");
            bool found = build.TryClaimDigJob(grid.CellToWorld(Right), out Vector2I job);
            Check(found && job.X == Later.X, "unreachable obstruction allows reachable room work");
            if (found) build.ReleaseClaim(job);
            Check(build.TryClaimObstruction(grid.CellToWorld(Left), out job) && job == Older,
                "isolated obstruction stays available to left worker");
            build.ReleaseClaim(Older);
            build.NoticeObstructionForTest(Later);
            found = build.TryClaimDigJob(grid.CellToWorld(Right), out job);
            Check(found && job == Later, "reachable obstruction retains priority over room work");
        }
        catch (Exception exception)
        {
            failed++;
            GD.PrintErr(exception);
        }
        GD.Print($"--- job reachability: {passed} passed, {failed} failed ---");
        GetTree().Quit(failed == 0 ? 0 : 1);
    }

    private void Fixture()
    {
        for (int x = 0; x <= 22; x++)
            for (int y = 0; y <= 6; y++) Set(x, y, GridManager.TileType.Rock);
        Set(2, 2, GridManager.TileType.Tunnel);
        Set(2, 3, GridManager.TileType.Tunnel);
        for (int x = 8; x <= 18; x++)
            for (int y = 2; y <= 3; y++) Set(x, y, GridManager.TileType.Tunnel);
        Set(3, 3, GridManager.TileType.Dirt);
        Set(19, 3, GridManager.TileType.Dirt);
        build.ResetTransientState();
    }

    private void OpenBridge()
    {
        for (int x = 4; x <= 7; x++)
            for (int y = 2; y <= 3; y++) Set(x, y, GridManager.TileType.Tunnel);
    }

    private void Set(int x, int y, GridManager.TileType tile) => grid.SetTileFromSimulation(origin + new Vector2I(x, y), tile);
    private void Mark(Vector2I cell) => build.MarkForDiggingForTest(new Rect2I(cell, Vector2I.One));

    private void Expect(Vector2I worker, Vector2I expected, string message, bool release = true)
    {
        bool found = build.TryClaimRoomDigJob(grid.CellToWorld(worker), out Vector2I cell);
        Check(found && cell == expected, $"{message} (got {cell}, found={found})");
        if (found && release) build.ReleaseClaim(cell);
    }

    private void Check(bool ok, string message)
    {
        if (ok) passed++; else failed++;
        GD.Print($"  {(ok ? "PASS" : "FAIL")}: {message}");
    }
}
