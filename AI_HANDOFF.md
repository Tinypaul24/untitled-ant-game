## Branch

Use the currently checked-out Git branch (`game-rework` as of this writing).

This session did its work in a git worktree (`.claude/worktrees/finish-dig-designation`, on a
throwaway local branch reset onto `origin/game-rework`), because the harness enforces isolation for
background sessions. The changes below are **not yet in the main working copy or committed
anywhere** — see "Exact Recommended Next Step" for how to bring them back.

## Current Goal

Player-directed excavation: let the player draw ("mark") tunnels for diggers to cut, instead of
diggers only choosing targets on their own. Part of the broader "player decides what the workers
are for" direction (see recent commit history).

## Completed Work

A dig-designation feature appears finished in the working tree (uncommitted, not yet reviewed or
tested by this session):

- **`BuildManager.cs`** — new `designations` dict (`Vector2I` cell → batch index). `BeginMarking()` /
  `CancelMarking()` toggle a marking mode; `HandleMarkingInput` drives left-drag-to-mark-rectangle,
  right-click-to-cancel. `TryClaimDesignation` has diggers claim marked cells before falling back to
  the existing organic dig-target selection, ordered by batch (draw order) then nearest. Designated
  cells are drawn with a persistent tint (`DesignationTint`) plus a live drag-preview tint
  (`MarkingTint`). Designations are cleared per-cell when dug and wholesale on load/reset.
- **`AntWorker.cs`** — reads `buildManager.IsDesignated(pendingDigCell)` *before* the dig completes
  (digging retires the designation) into `drawnByThePlayer`. That flag now also grants two-tile
  headroom on the player's own dig target cell, whereas an organic corridor's terminal cell normally
  stays low ceiling.
- **`ColonyUI.cs`** — new `digButton` (toggle) wired to `BeginMarking`/`CancelMarking`. Mutually
  exclusive with the build tray (opening one disarms the other), since both consume left-click.
- **`Main.tscn`** — adds the `DigButton` node next to `Build`, with tooltip text explaining the drag/
  right-click/draw-order behavior.
- **Unrelated tooling changes**: `project.godot` enables a `godot_mcp` editor plugin
  (untracked `addons/` dir); `.vscode/settings.json` adds `dotnet.defaultSolution`.
- **This session also renamed** `Claude.md` → `CLAUDE.md`, `AI_Handoff.md` → `AI_HANDOFF.md`,
  `Agents.md` → `AGENTS.md` for conventional casing (two-step rename since Windows filesystem is
  case-insensitive). Contents were unchanged; they already cross-reference each other with the
  corrected casing.

This session (continuing the above) closed the save/load gap that was flagged as unverified:

- **`BuildManager.cs`** — added `CaptureDesignations()` / `RestoreDesignations(List<int>)`, packing
  cell + batch via the existing `AddCell`/`ReadCellValues` helpers (`SaveList.cs`), same scheme
  already used for grid data. `RestoreDesignations` re-checks `GridManager.CanDig` per cell (ground
  that stopped being diggable while the save sat on disk is dropped, same as the live staleness
  prune in `TryClaimDesignation`) and bumps `nextDigBatch` past the highest restored batch so newly
  drawn tunnels after a load queue *behind* restored ones rather than jumping ahead.
- **`SaveData.cs`** — added `List<int> Designations`, not version-bumped (same precedent as
  `QueenGrounded`: `System.Text.Json` leaves it at its empty-list default on older saves, which is
  the correct read — no designations existed before this feature did).
- **`SaveManager.cs`** — wired `Designations = buildManager.CaptureDesignations()` into `Capture()`,
  and `buildManager.RestoreDesignations(data.Designations)` into `Restore()` right after
  `RestoreRooms`, after `gridManager.RestoreState` so `CanDig` sees the real restored terrain.
- **`dotnet build` now passes** (0 warnings, 0 errors) against the full set of changes.

## Files Changed (uncommitted, on `game-rework`)

- `AntCity/Scenes/Entities/AntWorker.cs`
- `AntCity/Scenes/Main.tscn`
- `AntCity/Scripts/Buildings/BuildManager.cs`
- `AntCity/Scripts/ColonyUI.cs`
- `AntCity/Scripts/SaveSystem/SaveData.cs`
- `AntCity/Scripts/SaveSystem/SaveManager.cs`
- `project.godot`
- `.vscode/settings.json`
- (untracked, newly renamed) `CLAUDE.md`, `AI_HANDOFF.md`, `AGENTS.md`
- (untracked) `addons/` (godot_mcp editor plugin)

## Important Decisions

- Designations are a **separate obstruction set** from `claimedDigCells`/`obstructions`, not merged
  into them — deliberate, per the comment in `BuildManager.cs`, to keep "what the player asked for"
  distinct from what the game inferred.
- Draw order *is* dig priority (lower batch index dug first) — no separate priority UI.
- The same drag gesture used for room placement is reused for marking tunnels, intentionally, to
  avoid introducing a second interaction idiom for the same kind of question.
- A designated cell gets headroom on its own target cell (unlike organic dig), because it's a
  player-authored tunnel, not a load-bearing room footprint.

## Known Problems / Unverified

- **No manual/runtime playtest** of any of this has been done. This session's Godot MCP connection
  was bound to the main checkout's editor instance, not this worktree, so running the project
  through it would have exercised the *old* code, not these changes — running it was skipped rather
  than produce a misleading result. Still needs: toggle Dig on, drag a rectangle over solid ground,
  confirm tint appears, confirm diggers path to and cut marked cells in draw order before falling
  back to organic targets, confirm right-click cancels marking, confirm opening Build tray disarms
  Dig and vice versa, confirm designations survive an actual save/quit/load cycle.
- Whether `IsFullyManned` (referenced in `TryClaimDesignation`) already accounts correctly for
  multi-ant cooperative digging on a designated cell hasn't been traced end-to-end this session.
- The new `Designations` field is additive-only in `SaveData`/`SaveManager` and mirrors the existing
  `Rooms` pattern closely, but has had no runtime exercise (see above) — treat it as build-verified,
  not behavior-verified.

## Unfinished Work

None identified as missing from the feature itself — it reads as a complete vertical slice,
including save/load persistence now. The gap is verification, not implementation.

## Exact Recommended Next Step

1. **Bring this worktree's changes into the main checkout.** This session worked in
   `.claude/worktrees/finish-dig-designation` (branch `worktree-finish-dig-designation`, based on
   `origin/game-rework`). From the main checkout: `git diff` in that worktree against
   `origin/game-rework` reproduces the full changeset (8 tracked files); the untracked
   `CLAUDE.md`/`AI_HANDOFF.md`/`AGENTS.md`/`addons/` in the worktree are identical copies of what's
   already untracked in the main checkout except this file, which has the additions above — diff
   and merge it by hand rather than overwrite. Confirm with `git status`/`git diff` in the main
   checkout that nothing else changed there in the meantime before copying over.
2. Launch the project in the real Godot editor (main checkout) and manually test everything listed
   under Known Problems above.
3. If it checks out, this work is ready to be committed (ask the user first — nothing should be
   committed without explicit instruction; this session did not commit or push anything).
