## Branch

Use the currently checked-out Git branch (`main`, with dig-designation now merged in via `game-rework`).

## Current Goal

Room placement priority: rooms should excavate in the order they were placed, same "player decides"
principle as dig-designation tunnels, with soft overflow to the next room once the current one has
no capacity or reachable cell left. **This goal is now complete, verified live, and ready to
commit.** No new goal has been picked yet - ask the user what's next.

## Completed Work

- **Room placement priority** (`BuildManager.TryClaimRoomDigJob`): rooms now excavate in placement
  order - `rooms` is already an insertion-ordered `List<Room>`, so the first `Excavating` room with
  any capacity wins outright; a room only lets the next one in line take a claim once it has nothing
  left to give. TDD test `RoomsExcavateInPlacementOrder` in `TerrainTests.cs` proves placement order
  beats raw distance and that overflow triggers once a room is fully manned.
- **Regression found in first live playtest and fixed**: a room placed on ordinary, ready-to-be-dug
  solid ground (the completely normal way every room starts) could still be temporarily unreachable
  (surrounded by rock, or just not yet in reach of the router's search budget). Under strict
  placement priority, that let one such room monopolise every idle worker forever - each one
  claiming its cell, failing `PlanDigRoute`, abandoning, and immediately re-claiming it. Symptoms
  live: total freeze, severe lag, and ants visibly picking up and dropping the same dirt in a loop.
  Root-caused with `superpowers:systematic-debugging`, confirmed via temporary instrumentation and a
  throwaway headless/live-ticking stress probe (since removed). Fixed by adding a reachability check
  (`GridManager.PlanDigRoute`) into the room-claim loop: a room's candidate that can't currently be
  routed to is excluded for this attempt, and the search moves on to its next candidate or the next
  room. TDD test `RoomsSkipARoomWithNoReachableCell` (walls a room's one cell in with rock on all
  eight sides - not merely far away, since `PlanDigRoute` will happily carve long corridors over
  distance - so unreachability is unambiguous, not incidental).
- **Second regression found via a live-ticking stress probe** (the static test suite never yields a
  frame, so it couldn't have caught this): the reachability check above, while correct, has no
  memory - every idle worker's claim attempt against a currently-blocked cell pays for a full
  ~6000-node `PlanDigRoute` search, every time, with no caching. Measured directly (temporary call
  counters on `PlanDigRoute`): one 10-second window burned 200,000-320,000 visited nodes *per
  second* with zero successful claims the entire time - not a livelock, but a severe, real,
  repeated performance cost that could still look exactly like the original complaint. Fixed with a
  short (3-second) per-cell cooldown cache (`BuildManager.unreachableUntilUsec`, `Time.GetTicksUsec()`
  - the same wall-clock pattern already used by `CameraController`/`FrameProbe`/`MiniMap`): a cell
  that just failed a route plan is skipped without re-asking for the cooldown window. Verified with
  the same stress probe: the sustained multi-second spike cluster is gone; only isolated, non-
  repeating single-frame spikes remain when a genuinely new candidate is checked for the first time.
  TDD test extends `RoomsSkipARoomWithNoReachableCell`: after the seal is removed (cell now
  genuinely reachable), an immediate second claim still goes to the other room, proving the negative
  result is cached rather than freshly re-evaluated.
- **User confirmed live**: no lag, no freeze, placing two ordinary disconnected rooms works
  correctly after both fixes.
- `dotnet build` and the full `TerrainTests.cs` suite (170 checks) pass clean at every step, verified
  repeatedly (one flaky, pre-existing, unrelated failure - `PilesPackIntoSolidGround`'s spoil-tipping
  check - was observed once and confirmed non-reproducing on an identical immediate rerun; not
  touched, not caused by this work).

## Files Changed

- `AntCity/Scripts/Buildings/BuildManager.cs` - `TryClaimRoomDigJob` now claims in placement order
  with reachability-checked overflow, backed by a short-lived per-cell unreachability cache
  (`unreachableUntilUsec` / `IsRecentlyUnreachable` / `TryPlanRoute`).
- `AntCity/Scripts/TerrainTests.cs` - `RoomsExcavateInPlacementOrder` and
  `RoomsSkipARoomWithNoReachableCell` (three checks total, the latter now also covering the cache).
- `AntCity/Scripts/World/GridManager.Navigation.cs`, `AntCity/Scripts/RoomPriorityStressProbe.cs`,
  `AntCity/Scenes/RoomPriorityStress.tscn` - touched/created during investigation as temporary
  diagnostics, all fully reverted/removed; no net diff.

## Important Decisions

- Room priority is **soft**, matching the dig-designation precedent: placement order wins by
  default, but a room with no capacity or no currently-reachable cell yields to the next one in
  line rather than blocking the colony. This was the user's explicit choice when the tradeoff was
  presented.
- The unreachability cache lives on `BuildManager`, not `GridManager` - it's specific to the
  claim-selection use case, not a general property of pathfinding. `GridManager.PlanDigRoute` itself
  is unchanged.
- Cooldown is 3 real seconds, chosen as long enough to matter under many simultaneous idle workers,
  short enough that a corridor finished a moment ago is picked up again quickly. Not user-configurable;
  revisit only if evidence says otherwise.

## Known Problems / Unverified

- Isolated single-frame spikes (~200-360ms) can still occur when a room's candidate is checked for
  the very first time and turns out to be unreachable - the cache only helps on repeat checks of the
  *same* cell. A colony with several simultaneously-unreachable rooms could produce a cluster of
  first-time spikes within the same second. Not treated as a bug - flagged as an accepted tradeoff,
  not silently hidden.
- **New, separate issue reported live by the user, not yet investigated**: after the game runs for a
  while, ants can end up hauling dirt "from the top" back and forth in what the user describes as an
  infinite loop. Not yet root-caused. Given this session's pattern (a claim/abandon cycle that drops
  and immediately re-picks-up carried material - see `StopCurrentTask`'s
  `DropCarriedGrains`/`TryHaulSpoil` interaction), this smells like the same *family* of bug just
  fixed for rooms - some other job-claiming path (spoil-hauling destination selection, most likely
  `GridManager.FindSpoilDropOff`, or another claim/abandon loop) may have an analogous
  claim-without-a-persistence-check problem. This is a guess, not a diagnosis - needs its own
  `superpowers:systematic-debugging` pass with real repro/instrumentation before touching code,
  the same way the two fixes above were found. Do not guess-fix this from the description alone.

## Unfinished Work

- The new hauling back-and-forth issue above - next candidate for investigation, if the user wants
  to pursue it next.
- No other new goal has been picked yet - ask the user what's next.

## Exact Recommended Next Step

1. Commit the room-placement-priority work (ask first, per Git Safety) - it's complete and verified.
2. If the user wants to pursue the hauling back-and-forth issue next, start with
   `superpowers:systematic-debugging`: get a live repro, check `AntWorker.TryHaulSpoil`,
   `GridManager.FindSpoilDropOff`, and `StopCurrentTask`'s drop/pickup interaction, and build a
   probe (live-ticking, not the static test suite - the room-priority second regression was
   invisible to static tests) before proposing any fix.
