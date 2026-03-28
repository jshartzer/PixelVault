# PixelVault Documentation Sync Policy

This file keeps the repo docs and Notion aligned.

The goal is not to duplicate every sentence in both places.

The goal is:

- one clear source of truth for each kind of information
- predictable update points
- no silent drift after releases, roadmap changes, or workflow changes

## Source Of Truth

Use the repo as the source of truth for:

- code
- build outputs
- runtime paths
- scripts
- durable behavior rules
- handoff state for active technical work

Use Notion as the source of truth for:

- roadmap status
- backlog status
- bug tracking
- QA verification state
- release tracking
- project/wiki-style planning notes

## What Must Stay Matched

These facts must match in both places when they change:

- current published version
- current executable path
- current active roadmap phase
- completed milestone status
- important workflow/process changes
- known release-level bugs or regressions worth tracking

## Required Update Triggers

After every publish:

- update `C:\Codex\docs\CURRENT_BUILD.txt`
- update `C:\Codex\docs\CHANGELOG.md`
- update the current-build section in `C:\Codex\docs\HANDOFF.md`
- update the matching Notion `Releases` entry
- note smoke-test status in Notion when available

After roadmap or milestone changes:

- update `C:\Codex\docs\ROADMAP.md` if phase order, phase scope, or definitions of done changed
- update `C:\Codex\PixelVaultData\TODO.md` if current focus or rolling follow-ups changed
- update the related Notion roadmap and backlog entries

After workflow, architecture, or behavior-rule changes:

- update `C:\Codex\docs\POLICY.md` if the rule is durable
- update `C:\Codex\docs\HANDOFF.md` if future work depends on knowing it immediately
- update the matching Notion wiki/process page when the change affects planning, QA, or release operations

## Working Rule

If only one side can be updated immediately:

- update the repo first for build/runtime facts
- update Notion first for planning/status facts

Then close the gap before the task is considered finished.

## Session Close Checklist

Before wrapping meaningful work, check:

1. Did the live build version change?
2. Did the current roadmap phase or active focus change?
3. Did a workflow or operating rule change?
4. Did Notion gain or lose a status that the repo docs still describe differently?

If yes, update both sides before calling the work done.
