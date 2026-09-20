## Branch

Use the currently checked-out Git branch (`game-rework` as of this writing).

## Current Goal

Player-directed excavation: let the player draw ("mark") tunnels for diggers to cut, instead of
diggers only choosing targets on their own. Part of the broader "player decides what the workers
are for" direction (see recent commit history). The feature itself shipped in `095fca9`; this
session was its first-ever runtime playtest, which surfaced a priority gap (below) that is now
understood and either fixed or deliberately left as-is per the user's call. **No new goal has been
picked yet** - ask the user what's next.

## Completed Work

- **First runtime playtest of dig-designation**, done live with the user in the Godot editor:
  - Marking a rectangle and seeing the tint appear: confirmed working.
  - Diggers picking up and cutting marked cells: **initially looked broken** - the user watched two
    ants go idle for a moment and then resume whatever they were doing, apparently ignoring the
    marked tunnel entirely.
- **Root-caused the apparent bug**, using `superpowers:systematic-debugging`. Built a headless
  probe (`AntCity/Scripts/DesignationProbe.cs` + `AntCity/Scenes/DesignationCheck.tscn`, run via
  `godot --headless --path . res://AntCity/Scenes/DesignationCheck.tscn`, same pattern as
  `ColonyProbe`/`StarveProbe`) to reproduce it without needing a human to drag a rectangle. Added a
  test-only `BuildManager.MarkForDiggingForTest(Rect2I)` hook (mirrors the existing
  `PreviewForTest`) so the probe can mark cells directly.
  - Confirmed via temporary instrumentation (since removed) that `TryClaimDesignation` itself works
    correctly: a `Digger`-role ant does find and claim a freshly marked cell, gets a valid
    `PlanDigRoute`, and eventually digs it (probe PASS after ~75 sim-seconds for a cell 17 tiles
    from the claiming ant).
  - **Actual root cause**: `AntWorker.GoIdle()` runs `TryOwnTrade() || TryAnyTrade()`. For a
    `Forager` or `Builder`, `TryOwnTrade()` only tries their own trade (forage/build) - it never
    looks at `TryClaimRoomDigJob` (where the designation-priority check lives) unless that
    own-trade attempt fails first. So any idle ant currently holding a non-Digger role, with her
    own work available, never even glances at a marked tunnel - confirmed directly in the
    instrumented log (`TryOwnTrade=True` → `took OwnTrade/AnyTrade`, no `TryClaimDesignation` call
    at all). This is unlike cave-in clearing (`TryClaimObstruction`), which deliberately runs
    unconditionally ahead of `TryOwnTrade` for every role. The two ants the user watched almost
    certainly were not holding the Digger role at that moment.
  - Presented this to the user with three options (make designations role-agnostic like
    obstructions; elevate them earlier in `TryAnyTrade`; or leave the mechanism and fix
    expectations instead). **User chose: leave the mechanism as-is, fix expectations only.**
- **Fix applied**: updated the Dig button's tooltip in `AntCity/Scenes/Main.tscn` to say a marked
  tunnel waits for a free digger and won't pull an ant off foraging or building, so it can sit a
  while if nobody is free. No priority-logic changes.
- **Kept the probe as permanent regression infrastructure** (user's call): cleaned it up to match
  the plain `GD.Print` style of `ColonyProbe`/`StarveProbe`, widened its timeout from 30s to 120s
  sim-time after the first run showed a solo claiming ant can legitimately take that long for a
  cell far from her, and re-ran it to confirm a clean PASS. All temporary `[TEMPDBG]` instrumentation
  and the file-based `ProbeLog` workaround (needed only because the connected MCP debug-output
  capture wasn't surfacing `GD.Print` from a `run_scene` session) were removed afterward -
  `AntWorker.cs` is back to its pre-investigation state; `BuildManager.cs` only keeps the
  `MarkForDiggingForTest` hook.
- `dotnet build` passed clean after every change in this session, including the final state.

## Files Changed

- `AntCity/Scripts/Buildings/BuildManager.cs` - added `MarkForDiggingForTest(Rect2I)` (test-only
  hook, no behavior change).
- `AntCity/Scenes/Main.tscn` - `DigButton` tooltip now explains that marked tunnels wait for a free
  digger.
- `AntCity/Scripts/DesignationProbe.cs` (new) - headless regression probe for dig-designations.
- `AntCity/Scenes/DesignationCheck.tscn` (new) - runs `DesignationProbe` against `Main.tscn`, same
  pattern as `ColonyCheck.tscn`/`StarveCheck.tscn`.
- `AntCity/Scenes/Entities/AntWorker.cs` - touched during investigation, fully reverted; no net
  diff.
- `AI_HANDOFF.md` - this file; removed the stale "Current Test" handoff-confirmation section now
  that a session has picked up from it.

## Important Decisions

- Dig-designation's job-priority behavior (Forager/Builder own-trade outranks a marked tunnel)
  is **intentional, left unchanged** - the user explicitly chose "leave the mechanism, fix
  expectations instead" over making it role-agnostic like obstruction-clearing. If this comes up
  again, don't re-litigate it without the user raising it.
- `DesignationProbe`/`DesignationCheck.tscn` are kept as permanent gameplay-probe infrastructure
  (user's call), not a throwaway diagnostic - same standing as `ColonyProbe`/`StarveProbe`. Its
  `TimeoutSeconds` (120 sim-seconds) is deliberately generous because the claiming ant can be
  anywhere in the colony's tunnels, not just adjacent to the mark.
- Designations remain a separate obstruction set from `claimedDigCells`/`obstructions` (prior
  decision, unchanged) - see the feature's original commit `095fca9` for the rest of that
  reasoning.

## Known Problems / Unverified

- The connected Godot MCP plugin's `get_debug_output` did not surface any `GD.Print` output during
  this session, from either `run_project` or `run_scene` sessions - worth a look if future
  debugging wants to rely on it rather than the file-write workaround used (and then removed) here.
- Nothing else outstanding for dig-designation - see "Completed Work" above; the full original
  playtest checklist is now done, live with the user: right-click cancels marking, Build tray and
  Dig disarm each other in both directions, designations survive an actual save/quit/load cycle,
  and more than one ant can cooperatively dig a single designated cell.

## Unfinished Work

None identified for dig-designation - it has now had a full manual playtest in addition to the
automated probe. No new goal has been picked yet - ask the user what's next.

## Exact Recommended Next Step

Ask the user what the next goal is; nothing is currently queued.
