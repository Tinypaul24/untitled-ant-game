## Latest Work — Claude, 2026-09-20 (supersedes the review section below)

Picked up the two reliability items that were already half-built in the workspace and finished both.
Branch unchanged: `hauling-functionality-pause`, HEAD `7c7b614`. Nothing committed; nothing pushed.

### 1. The test suite can no longer touch the player's saves (review priority #1)

`TestSaveDirectory.cs` existed but nothing used it, so `SaveLoadTests` was still writing into, listing
and deleting from the real `user://saves`. Now:

- the suite takes over storage before its first save operation of any kind, listing included
  (listing is what migrates a legacy save into the folder being listed);
- `TheseTestsNeverTouchThePlayersSaves` asserts the manager is not on `SaveManager.DefaultSavesDirectory`
  and that the folder starts empty - it is the first check to run, so a future edit that drops the
  isolation fails loudly instead of silently shredding;
- cleanup is by inventory: every file written is recorded as it is written, and only those are
  deleted. The old "delete anything that was not here when we started" pass is gone;
- `storage.Delete` refuses any path this run did not create - that line, against the real folder, is
  what destroyed a colony;
- a failed save/load/delete throws and stops the run (`STOP` in the output) instead of carrying on
  to the delete, which is exactly how the original incident happened;
- `TerrainTests` runs last in `Tests.tscn`, so it now sums both suites' failures and calls
  `GetTree().Quit(0/1)`. A headless run finally has an exit status. **Verified**: deliberate failure
  injected -> exit 1; removed -> exit 0.

`SaveManager` changes (injectable `SavesDirectory`, `LastSavedPath`, flush-and-check, migration
guarded to the default directory) were already in the workspace from the previous session and were
kept as-is; only `DefaultSavesDirectory` was made public so the guard can assert against it.

### 2. Unreachable dig jobs no longer starve reachable work (review finding #3)

`JobReachabilityTests.cs` / `JobReachabilityCheck.tscn` were in the workspace as a red test with no
implementation - 13 passed, 6 failed. Rooms already stepped past cells nobody could reach;
designations and obstructions did not, and both are consulted *before* rooms, so one marked cell
sealed in rock blocked everything behind it forever. `TryClaimDesignation` and
`TryClaimNearestObstruction` now use the same take-best/exclude-and-retry loop the room selector
uses. Now 19 passed, 0 failed.

**One design change worth knowing about.** The unreachable cooldown was keyed by goal cell alone. It
is now keyed by `(asking worker's cell, goal)`, because "nobody can reach it" is a property of the
cell *and* where the asker is standing - filing it against the cell alone means one worker stuck in
a side pocket takes a job off the whole colony's board for three seconds, which is the same
starvation the cache exists to prevent. `ResetTransientState` now clears it (it belongs to terrain
that no longer exists), and expired entries are swept once the table passes 512.

### Files changed

- `AntCity/Scripts/Buildings/BuildManager.cs` - reachability filter on designations and
  obstructions; cooldown re-keyed per asker; swept; cleared on reset.
- `AntCity/Scripts/SaveLoadTests.cs` - runs on injected, owned storage; fail-fast; isolation guard.
- `AntCity/Scripts/TerrainTests.cs` - exit status for both suites.
- `AntCity/Scripts/SaveSystem/SaveManager.cs` - `DefaultSavesDirectory` made public (rest was
  already in the workspace).
- Untracked, already in the workspace, now wired in: `TestSaveDirectory.cs`,
  `JobReachabilityTests.cs`, `JobReachabilityCheck.tscn`.
- `AntCity/Scenes/Main.tscn` (`HaulingEnabled = false`) preserved untouched.

### Validation

- `dotnet build`: 0 warnings, 0 errors.
- Full `Tests.tscn`: save/load **54 passed, 0 failed**; terrain **186 passed, 0 failed**; exit 0.
- `JobReachabilityCheck` **19/0**, `HazardCheck` **3/0**, `DesignationCheck` PASS, `ColonyCheck` and
  `StarveCheck` healthy (`stalls=0 stuck=0/0 reach=38of38/38`).
- Designation probe timing, 3 runs each: before 29.3/28.5/28.4s, after 29.0/30.9/28.1s. The extra
  route plan per claim costs nothing measurable.

### How the save suite was verified without risking real saves

The permission classifier blocks running `Tests.tscn` against the real project, and rightly so. The
suite was run instead in a throwaway copy of the project with `config/name` changed, giving it its
own `user://`, **seeded with copies of the four real saves** - i.e. the exact condition that caused
the original loss. Result: 54/186 green, exit 0, all four byte-identical afterwards, none added,
none deleted. The player's real saves were checksummed before and after everything and are
unchanged. The throwaway user-data folder was deleted; Codex's
`ant-project-review-...` backup folder was left alone.

**Reproduce it like this** (do not point this at the real project until you want to):

    godot --headless --path <copy-with-unique-config/name> res://AntCity/Scenes/Tests.tscn --quit-after 300

Running it against the real project should now be safe - storage is isolated and the guard check
proves it - but it has not been demonstrated there, so treat the first real run as the experiment.

### Not done / next

- Nothing is committed. Ask before committing; the two pieces are separable if the user wants them
  as two commits (job board vs. test storage).
- Review findings still open: #1 intro-load soft-lock, #2 corpse disposal re-queuing, #4 click
  selection cleared on release, #5 dig/room tool transition, #6 missing paths treated as arrivals,
  #7 food outlook double-counting, #8 heat in air never cooling, #9 corpses missing from saves.
- Still true from the earlier session: the founding fix only applies to newly founded colonies, so
  a live playtest needs a **new** game, and `HaulingEnabled` is currently `false` in `Main.tscn`.

## Earlier Review — Codex, 2026-09-20

This section supersedes the branch/uncommitted status in the historical handoff below.

- User requested a whole-project review, then a discussion before choosing implementation work.
- Current branch: `hauling-functionality-pause`; HEAD `7c7b614`. The founding fix is already
  committed. Room priority is also in history as `b3078d7`. Remote/merge state was not checked.
- Preserved existing `AntCity/Scenes/Main.tscn` diff: `HaulingEnabled = false`.
- Completed broad review of workers/jobs/rooms, terrain/navigation/materials, colony/UI/input,
  persistence, project configuration, documentation and test/probe coverage.
- Findings and exact next-step recommendations: `PROJECT_REVIEW.md`. No C# or gameplay edits.
- Fresh validation: build 0 warnings/errors; isolated save/load 52/0; terrain 186/0;
  designation probe PASS; hazard probe 3/0. Gameplay findings remain static-review findings.
- Highest priorities: safe test storage; intro-load soft-lock; corpse disposal re-queuing;
  unreachable designation starvation; click selection and dig/room tool transitions.
- Important incident: original save test run failed writes under sandbox but kept running and
  deleted existing `colony_20260920_124435.json`. User informed and apology given. No matching
  backup found in project/TEMP/Godot data. Four remaining saves backed up under the temporary
  review project's `original-saves-backup`; full path in `PROJECT_REVIEW.md`.
  NEVER run `Tests.tscn` against the real save directory again. Original failed run is invalid
  regression evidence. Passing rerun used a temporary copy with unique application name.
- Files changed by review: this handoff and new `PROJECT_REVIEW.md` only.
- Exact next step: discuss review with user and select a reliability task. Before any more
  save tests, provide isolated storage and fail-fast handling. Do not start feature changes yet.
- User preference: scale reasoning effort to task difficulty automatically where tool controls
  permit it. The current parent session provides no tool to change its configured effort;
  do not claim that adapting analysis depth changes the UI effort setting.

## Historical Claude Handoff

## Branch

`room-placement-priority`, branched off `main`. It holds commit `b3078d7` (room placement priority,
complete and verified, not yet pushed/PR'd) plus the uncommitted founding fix described below. The
two are unrelated; see "Exact Recommended Next Step" for how to separate them.

## Current Goal

Fix the reported bug where, after a while, ants end up carrying dirt back and forth forever without
achieving anything. **Root-caused and fixed; full test suite green; verified behaviourally.** Not
yet committed. No new goal picked after this - ask the user.

## Completed Work

### The founding chamber sealed the whole colony in (this session's fix)

Root-caused with `superpowers:systematic-debugging` after three wrong hypotheses, each disproven by
measurement rather than argument (worth knowing, because the wrong answers are all superficially
plausible):

1. *"Ants re-dig the same dirt in a loop."* Disproven: instrumented every `CellDug`; `redug_cells=0`
   over an 8-minute run. Nothing is ever dug twice.
2. *"Spoil slumps onto the doorstep, which raises an obstruction, which ants dig and re-tip."*
   Disproven: the apron keep-clear works - the mound profile stays `0` across the apron columns.
3. *"The hill's crust freezes it into unclimbable cliffs."* Disproven by experiment: disabling the
   mound cement site made things strictly worse (`hauls=0/510` vs `0/252`) and buried the doorstep.
   Reverted.

**Actual root cause**: `ColonyFounding.PlanBurrow` dug the starting chamber *centred* on the foot of
the shaft (`x` from `-ChamberHalfWidth` to `+ChamberHalfWidth`), so it reached back **underneath the
last two shaft treads** and dug the ground out from beneath them. A tread with open space below it
is not standable, and the shaft is the only slope an ant can climb (`MoveDirections` has no vertical
move). So the colony's single route to the surface was severed at the instant of founding.

Consequences, all measured: every worker `stranded` (no path to the nest) from t=15s onward;
`FindNearestSurfaceStanding` falling through to its `return from` fallback and handing back the
ant's own cell 8 rows below the turf; `FindSpoilDropOff` therefore only ever reaching underground
cells; `IsViableSpoilDropOff` correctly refusing every one; `HaulTrips=0` with `HaulsRefused`
climbing into the hundreds; `digs` frozen at 16 cells (i.e. the founding dig and nothing else) for
the rest of the game. Workers spent forever carrying a load they could never put down, dropping it
(`StopCurrentTask` tips where she stands) and picking it straight back up - **which is exactly the
back-and-forth the user reported.**

**Fix**: the chamber now runs on *from* the foot of the shaft (`x` from `0` to `ChamberHalfWidth*2`)
instead of straddling it, leaving the ground under the treads solid. One line of behaviour change.

**Verified**:

| | before | after |
|---|---|---|
| shaft treads 4,5 | `hole`, `hole` | `ok`, `ok` |
| chamber->nest | `SHUT` | `open` |
| stranded workers | 10/10 | 0/10 |
| haul trips / refused | 0 / 249-510 | 22 / 0 |
| surface reach | collapses 38 -> 12 | holds 37-38 |

The mound also now grows as a climbable slope (adjacent columns differing by <=1) rather than
cliffs - the hill pathology chased in hypothesis 3 was entirely downstream of this.

**Regression test**: `TerrainTests.TheFoundingChamberCanWalkHome` - for every worker, assert she can
path home to the nest and that `FindSpoilDropOff` from where she stands is viable. The pre-existing
`StartingWorldIsWalkable` missed this because it only checked nest->surface (both on the lawn) and
that ants spawn somewhere *standable* - the chamber floor is perfectly standable; the route home was
what was missing. Full suite: **186 passed, 0 failed** (was 170 before these 16 new checks).

### Earlier in this session (already committed as `b3078d7`)

Room placement priority - rooms excavate in placement order with soft overflow, plus two regressions
found in live playtest and fixed (an unreachable room monopolising every idle worker; then the
reachability check itself being too expensive without a cooldown cache). See that commit message.

## Files Changed (uncommitted)

- `AntCity/Scripts/ColonyFounding.cs` - chamber extends away from the shaft instead of under it.
- `AntCity/Scripts/TerrainTests.cs` - `TheFoundingChamberCanWalkHome` regression test.

Touched during investigation and fully reverted, no net diff: `MaterialWorld.cs` (crust experiment),
`ColonyProbe.cs` (temporary baseline logging). Throwaway diagnostics created and removed:
`SpoilLoopProbe.cs`, `SpoilLoopCheck.tscn`.

## Important Decisions

- Kept the hill crust. The experiment showed it is doing real work (holding the cone off the
  doorstep); the cliffs it appeared to cause were a symptom of the founding bug, not of the crust.
- The diagnostic probe was **not** kept this time (unlike `DesignationProbe`): the regression test in
  `TerrainTests` is the durable guard, and `ColonyProbe` already reports `hauls=` and `reach=`, which
  are the two numbers that would have caught this years earlier. If those numbers are ever seen as
  `hauls=0/<large>`, that is this class of bug.

## Known Problems / Unverified

- **Not yet playtested live by the user** - verified by the test suite and by probe telemetry only.
  A save created before this fix still has the broken shaft geometry baked into its terrain; the fix
  only applies to newly founded colonies. Worth telling the user: they need a new game to see it.
- The MCP plugin's `get_debug_output` still does not surface `GD.Print`, so reading any probe or test
  result requires a temporary file write. This has cost time in three separate sessions now and is
  worth fixing or documenting properly.

## Unfinished Work

- Commit the founding fix (see next step) - it is complete, just uncommitted.
- `b3078d7` (room placement priority) is committed locally but never pushed or PR'd.
- No new goal picked.

## Exact Recommended Next Step

1. The founding fix and the room-priority work are unrelated and are currently stacked on one
   branch. Cleanest order: push `room-placement-priority`, open and merge its PR, then branch off the
   updated `main` for the founding fix and commit it there. Ask the user before doing any of this.
2. Have the user start a **new** game and confirm the ants now carry spoil out and build a hill
   instead of shuffling it about - an existing save will not show the fix.
3. Then ask what is next; nothing is queued.
