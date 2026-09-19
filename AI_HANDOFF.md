## Branch

Use the currently checked-out Git branch (`game-rework` as of this writing).

## Current Goal

Player-directed excavation: let the player draw ("mark") tunnels for diggers to cut, instead of
diggers only choosing targets on their own. Part of the broader "player decides what the workers
are for" direction (see recent commit history). **This goal is now complete and committed** — see
below. Next goal is not yet defined; check with the user before picking new work.

## Completed Work

The dig-designation feature, including save/load persistence, is finished, committed, and merged
into `game-rework`:

- `095fca9` — "The player draws the tunnels, and the plan survives a save": the full feature.
  - **`BuildManager.cs`** — `designations` dict (`Vector2I` cell → batch index). `BeginMarking()` /
    `CancelMarking()` toggle marking mode; `HandleMarkingInput` drives left-drag-to-mark-rectangle,
    right-click-to-cancel. `TryClaimDesignation` has diggers claim marked cells before falling back
    to organic dig-target selection, ordered by batch (draw order) then nearest. Designated cells
    are drawn with a persistent tint (`DesignationTint`) plus a live drag-preview tint
    (`MarkingTint`). Designations clear per-cell when dug and wholesale on load/reset.
    `CaptureDesignations()` / `RestoreDesignations(List<int>)` persist them through save/load,
    packing cell + batch via the existing `AddCell`/`ReadCellValues` helpers (`SaveList.cs`).
    `RestoreDesignations` re-checks `GridManager.CanDig` per cell on load and bumps `nextDigBatch`
    past the highest restored batch so newly drawn tunnels queue behind restored ones.
  - **`AntWorker.cs`** — reads `buildManager.IsDesignated(pendingDigCell)` before the dig completes
    (digging retires the designation) into `drawnByThePlayer`, which grants two-tile headroom on the
    player's own dig target cell (an organic corridor's terminal cell normally stays low ceiling).
  - **`ColonyUI.cs`** — `digButton` (toggle) wired to `BeginMarking`/`CancelMarking`, mutually
    exclusive with the build tray (each disarms the other, since both consume left-click).
  - **`Main.tscn`** — `DigButton` node next to `Build`, tooltip explains drag/right-click/draw-order.
  - **`SaveData.cs`** — `List<int> Designations`, additive/not version-bumped (same precedent as
    `QueenGrounded`).
  - **`SaveManager.cs`** — `Capture()`/`Restore()` wired to `CaptureDesignations`/`RestoreDesignations`.
  - `dotnet build` passed clean at commit time.
- `14919a3` — "Git commands get a yes first, not a blanket ban": updated `CLAUDE.md`'s Git Safety
  section from "never commit/push unless asked" to a standing yes for any git command (including
  destructive ones), conditioned on asking first and getting an explicit answer every time — no
  exception for routine low-risk commits.
- Earlier in this thread: renamed `Claude.md`/`AI_Handoff.md`/`Agents.md` to conventional casing
  (`CLAUDE.md`/`AI_HANDOFF.md`/`AGENTS.md`); now tracked and committed as part of the above.

## Files Changed (now committed on `game-rework`, nothing outstanding)

- `AntCity/Scenes/Entities/AntWorker.cs`
- `AntCity/Scenes/Main.tscn`
- `AntCity/Scripts/Buildings/BuildManager.cs`
- `AntCity/Scripts/ColonyUI.cs`
- `AntCity/Scripts/SaveSystem/SaveData.cs`
- `AntCity/Scripts/SaveSystem/SaveManager.cs`
- `project.godot` (godot_mcp editor plugin enabled)
- `.vscode/settings.json` (`dotnet.defaultSolution` added)
- `CLAUDE.md`, `AI_HANDOFF.md`, `AGENTS.md`

A stray worktree at `.claude/worktrees/finish-dig-designation` (branch
`worktree-finish-dig-designation`) had done the same save/load work independently but was left
unmerged; it was confirmed identical to `game-rework` HEAD (no diff) and removed as redundant rather
than merged.

## Important Decisions

- Designations are a **separate obstruction set** from `claimedDigCells`/`obstructions`, not merged
  into them — deliberate, to keep "what the player asked for" distinct from what the game inferred.
- Draw order *is* dig priority (lower batch index dug first) — no separate priority UI.
- The same drag gesture used for room placement is reused for marking tunnels, intentionally, to
  avoid introducing a second interaction idiom for the same kind of question.
- A designated cell gets headroom on its own target cell (unlike organic dig), because it's a
  player-authored tunnel, not a load-bearing room footprint.
- Git Safety policy changed: Claude now has standing permission to run any git command in this repo
  (including destructive ones) but must ask first, every time, and get an explicit yes — see
  `CLAUDE.md`.

## Known Problems / Unverified

- **No manual/runtime playtest has ever been done on this feature**, across any session. Still
  needs, in the real Godot editor: toggle Dig on, drag a rectangle over solid ground, confirm tint
  appears, confirm diggers path to and cut marked cells in draw order before falling back to organic
  targets, confirm right-click cancels marking, confirm opening Build tray disarms Dig and vice
  versa, confirm designations survive an actual save/quit/load cycle in-game.
- Whether `IsFullyManned` (referenced in `TryClaimDesignation`) correctly accounts for multi-ant
  cooperative digging on a designated cell has not been traced end-to-end.
- `dotnet build` passed as of `095fca9`; has not been re-verified since (no C# changes since, so
  should still hold, but wasn't re-run).

## Unfinished Work

None identified for the dig-designation feature — it is a complete vertical slice, implementation
and persistence both. No new goal has been picked yet; ask the user what's next.

## Exact Recommended Next Step

1. Launch the project in the Godot editor and manually run through the playtest checklist under
   "Known Problems" above — this feature has shipped to the branch with zero runtime verification.
2. If anything fails, fix forward on `game-rework` (no need for a worktree — everything now lives in
   the main checkout).
3. Ask the user what the next goal is; nothing is currently queued.
