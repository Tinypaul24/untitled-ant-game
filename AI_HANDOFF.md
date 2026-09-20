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
