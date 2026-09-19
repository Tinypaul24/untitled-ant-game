# Untitled Ant Game — Project Instructions

## Branch Handling

Before making changes, inspect:

* `git status`
* the current branch
* relevant recent Git history when useful

Do not switch branches unless explicitly asked.

## Project

Untitled Ant Game is a simulation-heavy 2D ant colony / city-builder built with:

* Godot 4.7.2 Mono
* C#
* .NET 8

The project already contains substantial gameplay systems. Inspect existing implementations before introducing new architecture.

Important existing systems include:

* Ant worker AI and state behaviour
* terrain/grid simulation
* navigation and pathfinding
* granular excavation
* spoil hauling
* mound formation
* construction and rooms
* colony population and upkeep
* starvation and corpses
* foraging and food
* queen / colony founding
* save/load
* pheromones
* hazards
* minimap and UI
* gameplay probes/tests

## Before Changing Code

Always:

1. Read `AI_HANDOFF.md`.
2. Run `git status`.
3. Inspect the complete `git diff`.
4. Inspect relevant existing files.
5. Check recent Git history when useful.

Uncommitted changes may have been produced by Codex or another Claude session.

Never discard, revert, or rewrite them merely because you would have implemented the feature differently.

Continue from the current workspace state.

## Architecture

Preserve existing architectural boundaries unless there is a concrete reason to change them.

`GridManager` is split across multiple files, including navigation and persistence responsibilities.

Do not collapse it into one giant class.

`AntWorker.cs` already contains substantial worker behaviour.

Extend the existing worker system instead of creating a second independent worker-AI framework.

Avoid duplicate managers and duplicate sources of simulation state.

## Game Direction

Current development emphasizes:

* readable individual ants
* larger / two-tile-high tunnels
* cooperative excavation
* multiple ants contributing to jobs
* visible hauling of excavated material
* meaningful colony logistics
* stronger player decision-making
* less self-playing automation

Preserve this direction unless explicitly told otherwise.

## Work In Progress

The project may intentionally contain incomplete migrations.

Do not assume comments, constants and implementation are synchronized.

Inspect the current diff before resolving apparent inconsistencies.

## Scope

Avoid unnecessary rewrites.

Do not:

* refactor unrelated systems while fixing one feature
* create duplicate managers
* add dependencies without a concrete reason
* build abstractions for hypothetical future features
* remove working behaviour just to simplify code

Prefer the smallest coherent change that solves the requested problem.

## Validation

After changing C#:

1. Run `dotnet build`.
2. Fix compilation errors introduced by the change.
3. Run relevant existing tests/probes when practical.
4. Use Godot runtime/debugging tools when runtime behaviour needs verification.

## Git Safety

Claude Code has standing permission to run any git command in this repository, including commit,
push, `git reset --hard`, destructive checkout, force push, and mass revert — but must ask first,
every time, and get an explicit yes before running it. This applies uniformly: routine commits and
pushes are not exempted from asking just because they are low-risk, and destructive operations are
not made easier by this permission — they still need to be named explicitly in what is asked.

Never delete another agent's uncommitted work without asking first, even under this permission.

## Handoff

Claude Code and OpenAI Codex both work on this repository.

Before ending substantial work, update `AI_HANDOFF.md` with:

* current goal
* completed work
* files changed
* important decisions
* known problems
* unfinished work
* exact recommended next step
