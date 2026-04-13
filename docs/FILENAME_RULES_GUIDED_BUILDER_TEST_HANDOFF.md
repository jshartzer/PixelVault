# Filename Rules Guided Builder Test Handoff

## Purpose
This document describes what the new guided filename-rule experience is supposed to do so it can be tested end to end.

This is the intended behavior for the new guided builder work introduced on the Renaming Rules form. The goal is to validate whether the feature is wired correctly, whether the UI actually responds, and whether the saved rules still behave like normal `FilenameConventionRule` entries.

## Related documentation
- **Execution order, gates, and product strategy (codified plan):** [`docs/plans/PV-PLN-FNRU-001-guided-builder-verification.md`](./plans/PV-PLN-FNRU-001-guided-builder-verification.md) (`PV-PLN-FNRU-001` — includes **Strategy** for remembering filename shape + platform, and **Stage 6** follow-through)
- Form behavior and fields: [`FILENAME_RULES_FORM_SPEC.md`](./FILENAME_RULES_FORM_SPEC.md)
- Parsing architecture and rule engine context: [`FILENAME_PARSING_ARCHITECTURE.txt`](./FILENAME_PARSING_ARCHITECTURE.txt)

## Regression anchor (fill in when testing)
Record the build under test so results stay comparable across machines:
- **App version / build:** PixelVault **0.075.011** — git **`f1a0c3e`** — published path `C:\Codex\dist\PixelVault-0.075.011\PixelVault.exe` (per [`CURRENT_BUILD.txt`](./CURRENT_BUILD.txt))
- **Date initiated / anchor set:** **2026-04-12** (docs + planning alignment); prior anchor **2026-04-09** / **0.075.008** / `6309851` superseded for new runs
- **Date tested (manual A–G):** _fill when manual checklist completes_

## Actual Form / Asset Names
- **Window UI host (static):** `FilenameConventionEditorWindow` — `UI/Editors/FilenameConventionEditorWindow.cs` (builds the WPF window and wires controls).
- **Launch / wiring:** `FilenameConventionEditor` is not a separate public class; it is the **`MainWindow` partial** in `UI/FilenameConventionEditor.cs` (opens the editor, paths, services). When searching the codebase, look for `FilenameConventionEditor.cs` on `MainWindow`.
- **Window title:** `PixelVault <version> Renaming Rules`

These are the visible or code-level assets Cursor should reference while testing:
- Card title: `Filename Staging`
- Primary builder section title: `Guided Builder`
- Escape-hatch button: `Open Advanced`
- Built-ins list card: `Built-In Rules`
- Staging actions:
  - `Create Rule From Sample`
  - `Add From File...`
  - `Clear Staged`

## What The Feature Is Supposed To Do
The top of the Renaming Rules window should now be builder-first instead of raw-rule-first.

The intended flow is:
1. The user selects a recent unmatched filename or stages one from disk.
2. PixelVault analyzes the filename shape locally.
3. PixelVault proposes likely parts such as title, timestamp, Steam App ID, Non-Steam ID, counter, extension, and literal separators.
4. The user confirms or remaps what each part means.
5. PixelVault builds a normal saved filename rule from that guided draft.
6. Advanced editing is still available, but it should be the fallback path, not the main one.

## Core Behaviors To Verify

### 1. The `Filename Staging` card should be active and useful
Expected behavior:
- The top card should be `Filename Staging`.
- It should show persisted recent unmatched samples if any exist.
- It should also support session-only staged filenames from `Add From File...`.
- Selecting a staged or recent sample should drive the builder.
- `Clear Staged` should clear only session-added staged filenames, not the persisted recent unmatched sample store.

Important expectation:
- Staged filenames are session-only.
- They should not be written into SQLite as recent unmatched samples.

### 2. `Add From File...` should only use the filename
Expected behavior:
- Clicking `Add From File...` should let the user pick a real file from disk.
- PixelVault should only use `Path.GetFileName(...)` from that file path.
- It should not read file contents.
- It should not inspect image/video metadata.
- It should not require the asset to be imported into the library first.

Practical test:
- Pick any image or video from disk.
- The builder should react based only on the basename.
- A file path like `C:\captures\Halo Infinite_20260326221306.png` should stage as `Halo Infinite_20260326221306.png`.

### 3. The `Guided Builder` should populate when a sample is selected
Expected behavior:
- Selecting a sample in `Filename Staging` should populate the `Guided Builder`.
- The builder should show an ordered sequence of segments.
- Each segment should have a suggested meaning.
- Editable roles should include:
  - `Literal`
  - `Title`
  - `Timestamp`
  - `Steam App ID`
  - `Non-Steam ID`
  - `Counter`
  - `Extension`
- The builder should show the active sample filename and a shape preview / readable pattern preview.
- The builder should also surface a lightweight cross-sample hint when multiple filenames are staged (see **Test G**).

Important expectation:
- The builder is suggestion-based, not fully automatic.
- The user must be able to reassign the segment meaning.

### 4. The builder should intelligently detect likely components
Expected behavior:
- Timestamp-like suffixes or runs should be proposed as `Timestamp`.
- Long Steam-like numeric prefixes should be proposed as `Steam App ID` when they fit that shape.
- Long shortcut-style numeric prefixes should be proposed as `Non-Steam ID` when they fit that shape.
- Trailing counters like `_1` should be proposed as `Counter`.
- File extension should be proposed as `Extension`.
- Remaining human-readable text should usually land as `Title`.
- Literal separators such as `_`, `-`, spaces, or punctuation should stay literal unless the rule format requires otherwise.

Examples that should work in the builder:
- `Halo Infinite_20260326221306.png`
- `271590_20260326221306_1.png`
- `16245548604121415680_20260407183242_1.png`
- `Game Title 4_8_2026 10_16_41 PM.png`

### 5. Builder edits should visibly change the preview
Expected behavior:
- Reassigning a segment role in the builder should immediately update the preview.
- The rule should re-materialize as a normal `FilenameConventionRule`.
- Shape preview and parse summary should change as the mapping changes.

What this should feel like:
- The form should not appear inert.
- A user changing the role from `Literal` to `Timestamp`, or from `Steam App ID` to `Non-Steam ID`, should see the rule preview react immediately.

### 6. Builder-generated rules should still save as normal rules
Expected behavior:
- Saving from the builder should produce a normal custom filename rule.
- The rule should still live in the same underlying rule system as before.
- The parser engine should continue using saved `FilenameConventionRule` rows.
- There should be no schema migration dependency for this feature.

Practical check:
- Create a rule from a sample.
- Save it.
- Re-open the Renaming Rules window.
- The rule should still exist in the custom rules list and behave like a normal rule.

### 7. Existing editable rules should prefer the builder
Expected behavior:
- Existing custom rules that are representable by the builder should open in `Guided Builder`.
- The builder should reconstruct the segments from the saved readable/token pattern when possible.
- The rule should round-trip without changing meaning.

Examples of representable rules:
- title + timestamp + extension
- Steam App ID + timestamp + counter + extension
- Non-Steam ID + timestamp + counter + extension
- Xbox PC-style readable timestamp shapes that use single-digit month/day/hour tokens

### 8. Unsupported or regex-heavy rules should fall back to Advanced
Expected behavior:
- If a rule cannot be faithfully represented by the builder, PixelVault should not fake it.
- It should mark the rule as advanced-only and direct the user to `Open Advanced`.
- This fallback should be explicit and understandable.

Expected UX:
- The builder can still show context, but the user should get a clear signal that the rule cannot round-trip safely in guided mode.
- The user should be able to continue editing that rule in the Advanced section without data loss.

### 9. Built-in rules should be inspectable and customizable without editing shipped defaults
Expected behavior:
- `Built-In Rules` should list shipped defaults.
- Selecting a built-in should let the user inspect it.
- Editing a built-in should create a custom local copy / override instead of mutating the shipped definition.
- Double-click behavior on a built-in should follow the customize/clone flow.

Expected mental model:
- Built-ins are templates.
- Customization should create a user rule.
- Shipped defaults remain intact.

### 10. Accessibility (optional but recommended)
Expected behavior:
- Tab order reaches `Filename Staging`, staged samples, `Guided Builder` segment controls, primary actions (`Create Rule From Sample`, `Add From File...`, `Clear Staged`), `Open Advanced`, and built-in list without trapping focus.
- Screen readers get a sensible name for the Renaming Rules window and for key buttons (staging vs builder vs escape hatch).
- If segment roles are combo boxes or lists, changing a role is keyboard-accessible and updates preview without requiring a mouse.

## What Cursor Should Specifically Watch For

### Signs the feature is working
- The `Filename Staging` area reacts when a sample is selected.
- `Add From File...` adds a filename to the staging list without importing anything.
- The `Guided Builder` fills in segments and suggested roles.
- Changing a role changes the preview immediately.
- `Create Rule From Sample` creates an editable rule draft rather than leaving the form unchanged.
- Built-ins can be inspected and then customized into a custom rule.
- Some rules open guided-first, while advanced-only rules clearly tell the user to use `Open Advanced`.
- With two or more staged samples, the builder still behaves per active selection; any cross-sample hint is coherent (see **Test G**).

### Signs the feature is broken or partially wired
- Clicking `Add From File...` appears to do nothing.
- Clicking a staged or recent sample does not populate `Guided Builder`.
- The builder shows no segments for obviously parseable filenames.
- Segment role changes do not update any preview.
- `Create Rule From Sample` does not create a working draft.
- Built-in rule customization edits the built-in directly instead of cloning.
- `Clear Staged` removes persisted recent unmatched samples.
- The UI only shows Advanced behavior and never meaningfully uses the builder.
- Adding a second staged filename breaks segment display, preview, or selection for the first sample.

## Suggested Manual Test Checklist

### Test A: Stage from disk
1. Open `PixelVault <version> Renaming Rules`.
2. In `Filename Staging`, click `Add From File...`.
3. Pick a file such as `Halo Infinite_20260326221306.png`.
4. Confirm the staged item appears by filename only.
5. Confirm selecting it populates `Guided Builder`.

Pass condition:
- The staged sample appears and drives the builder.

### Test B: Create a simple title + timestamp rule
1. Stage or select `Halo Infinite_20260326221306.png`.
2. Confirm the title portion is suggested as `Title`.
3. Confirm the date/time run is suggested as `Timestamp`.
4. Confirm `.png` is `Extension`.
5. Save the rule.

Pass condition:
- A custom rule is created and remains usable after re-opening the window.

### Test C: Steam-style numeric ID
1. Stage or select `271590_20260326221306_1.png`.
2. Confirm the first numeric segment is suggested as `Steam App ID`.
3. Confirm the middle segment is `Timestamp`.
4. Confirm the trailing `_1` becomes `Counter`.

Pass condition:
- Builder handles the full structure cleanly.

### Test D: Non-Steam shortcut-style numeric ID
1. Stage or select `16245548604121415680_20260407183242_1.png`.
2. Confirm the long numeric prefix is suggested as `Non-Steam ID`, not `Steam App ID`.
3. Confirm the rest of the structure still maps correctly.

Pass condition:
- The builder correctly distinguishes shortcut-style IDs.

### Test E: Advanced fallback
1. Open an existing custom rule that uses raw regex or other advanced-only behavior.
2. Confirm the rule does not silently degrade into a fake guided representation.
3. Confirm `Open Advanced` is the path forward.

Pass condition:
- The form clearly communicates that the rule is advanced-only.

### Test F: Built-in customization
1. Open `Built-In Rules`.
2. Select a built-in.
3. Attempt to customize it.
4. Confirm a custom copy/override is created.
5. Confirm the shipped built-in remains unchanged.

Pass condition:
- Built-ins behave like templates, not editable source records.

### Test G: Cross-sample hint (multiple staged filenames)
1. In `Filename Staging`, use `Add From File...` twice with two different basenames that share a structural pattern (e.g. same Steam ID + timestamp shape, different titles).
2. Confirm both appear as session-staged items.
3. Select each sample in turn and confirm `Guided Builder` updates.
4. Confirm any **cross-sample** or consistency hint the UI documents (e.g. agreement on ID/timestamp shape) appears when multiple samples are present, without contradicting the active sample’s segments.

Pass condition:
- Multi-sample staging does not break single-sample editing; any cross-sample hint is visible and helpful, or explicitly absent if not yet implemented (then file a gap—not a silent failure).

## Notes For Debugging
These implementation points may help if behavior appears dead:

**Data models**
- Builder draft: `FilenameConventionBuilderDraft`
- Per-segment row: `FilenameConventionBuilderSegment`

**Two layers (do not confuse them)**
- **`FilenameRulesService`** (`Services/FilenameRules/FilenameRulesService.cs`) — implements `IFilenameRulesService` and owns the **public** builder API the window calls:
  - `CreateBuilderDraftFromSample`
  - `CreateBuilderDraftFromFilePath`
  - `CreateBuilderDraftFromRule`
  - `ApplyBuilderDraft`
- **`FilenameConventionBuilder`** (`Services/FilenameRules/FilenameConventionBuilder.cs`) — **static** analysis/helpers used to build and interpret drafts (segment extraction, pattern text, fallbacks). Start here for “why did this filename parse this way?”

**Window wiring**
- `FilenameConventionEditorWindow` — sample selection change, `Create Rule From Sample`, `Add From File...`, built-in double-click customize flow

## Bottom Line
The new filename-rule experience is supposed to feel like:
- sample first
- builder first
- suggestion driven
- easy to correct
- advanced when necessary

If the UI feels like it does nothing, the most likely failures are:
- staged samples are not being added
- sample selection is not hydrating the builder
- builder role changes are not updating the preview
- builder drafts are not being pushed back into the editable rule state
