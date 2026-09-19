# Untitled Ant Game — Codex Instructions

Read `CLAUDE.md` completely before modifying this repository.

The rules in `CLAUDE.md` are shared project rules and apply to Codex as well.

## Before Working

Always:

1. Read `CLAUDE.md`.
2. Read `AI_HANDOFF.md`.
3. Run `git status`.
4. Inspect the complete current `git diff`.
5. Inspect relevant existing code before designing changes.

The working tree may contain changes produced by Claude Code.

Do not revert, overwrite or independently reimplement those changes merely because they are uncommitted.

Continue from the current workspace state.

## Validation

After modifying C#:

* run `dotnet build`
* fix errors caused by your changes
* run relevant tests/probes where practical

## Handoff

Before ending substantial work, update `AI_HANDOFF.md` with:

* work completed
* files involved
* important decisions
* unresolved problems
* exact recommended next step

Claude Code may continue the task afterward.
