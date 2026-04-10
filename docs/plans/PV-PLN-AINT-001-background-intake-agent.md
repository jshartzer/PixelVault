# PV-PLN-AINT-001 — Background Intake Agent for trusted filename matches

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-AINT-001` |
| **Status** | Implemented (slices 1–9 complete; manual dogfood as needed) |
| **Owner** | PixelVault / Codex |
| **Parent context** | Upload/source folders, intake preview, import workflow, filename rules, single-folder storage model |
| **Related** | [`docs/FILENAME_PARSING_ARCHITECTURE.txt`](../FILENAME_PARSING_ARCHITECTURE.txt), [`docs/FILENAME_RULES_GUIDED_BUILDER_TEST_HANDOFF.md`](../FILENAME_RULES_GUIDED_BUILDER_TEST_HANDOFF.md), [`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`](../MANUAL_GOLDEN_PATH_CHECKLIST.md), [`docs/completed-projects/PV-PLN-LIBST-001-single-folder-storage-model.md`](../completed-projects/PV-PLN-LIBST-001-single-folder-storage-model.md), [`docs/plans/PV-PLN-FNRU-001-guided-builder-verification.md`](PV-PLN-FNRU-001-guided-builder-verification.md), [`docs/plans/PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md`](PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md) |

**Topic mnemonic:** `AINT` — **A**uto **Int**ake.

---

## Prerequisites and sequencing vs other plans

### Hard prerequisites (should be satisfied before relying on placement behavior)

| Plan | Relationship to AINT-001 |
|------|---------------------------|
| **[PV-PLN-LIBST-001](../completed-projects/PV-PLN-LIBST-001-single-folder-storage-model.md)** (**complete**) | **Required baseline.** Auto-intake must land files through the same import/sort/placement path as manual intake. LIBST is the authoritative storage model; treat its completion as the assumed production placement path before shipping AINT. |

### Soft prerequisites / coordination (not blocking, but reduces rework)

| Plan | Relationship to AINT-001 |
|------|---------------------------|
| **[PV-PLN-FNRU-001](PV-PLN-FNRU-001-guided-builder-verification.md)** | **Not a hard gate** for the background agent core. **Do coordinate** if trust toggles or copy appear in the **Guided Builder**: FNRU Stage 6+ may touch the same surfaces. Reasonable approach: land **trust persistence + advanced rule editor** first; add Guided Builder exposure once FNRU verification stages are stable, or in the same release train with a shared test pass. |
| **[PV-PLN-UI-001](PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md)** | **Align extractions.** Slice 1 (`IntakeAnalysisService`) and Slice 4 (`HeadlessImportCoordinator`) should **prefer new service/partial homes** over growing `PixelVault.Native.cs`, consistent with UI-001’s “services + plain models at the shell edge” direction. No need to finish all UI-001 steps before AINT. |
| **[PV-PLN-V1POL-001](PV-PLN-V1POL-001-pre-v1-polish-program.md)** | **No dependency.** Toasts and modal patterns may reuse existing polish work (`TryLibraryToast`, etc.); optional cross-check when designing the post-import summary window. |

### Nothing else in `docs/plans/README.md` is listed as a **must-complete-first** for v1 AINT beyond LIBST (done).

---

## Purpose

Add an opt-in **Background Intake Agent** that watches the configured upload/source folders and automatically imports files only when the **same automatic intake/import path** the app already uses can run **without user interruption**—conservative on the first pass, but willing to **move files through** when gates say it is safe.

The agent should not invent metadata, should not bypass the existing import pipeline, and should not reintroduce folder-structure inference. It should behave like a conservative automation layer over the current intake workflow:

- **Custom rules:** only when the user has marked the rule **`Trusted exact match`** (and the same safety gates as today pass).
- **Built-in rules:** auto-intake is allowed when the file can complete the **full** automatic pipeline (game/title resolution, metadata, move, sort—every step the headless path requires). If any step would need the user (e.g. cannot identify a game name, missing Steam App ID when required, lands in manual metadata), **do not** auto-import; the file **stays in the source folder** and appears in Intake Preview / Manual Intake in **whatever section the existing analysis already assigns** (auto vs manual buckets unchanged).
- If a file is **uncertain** or **not fully auto-capable**, PixelVault leaves it alone for normal intake review—**no silent partial moves**.

This is explicitly **not** a generic “AI agent” initiative. It is a deterministic background worker that reuses the parser and import workflow the app already has.

---

## Why this fits the current app

PixelVault already has almost all of the logic needed for this feature.

Existing seams:
- Filename parsing already happens up front in [`MainWindow.IntakePreview.cs`](../../src/PixelVault.Native/UI/Intake/MainWindow.IntakePreview.cs).
- The app already separates **auto-capable** items from **manual-intake** items.
- The import pipeline already supports:
  - Steam/non-Steam rename
  - metadata write
  - move into destination
  - sort into game folders
- The post-LIBST storage model already centralizes where files belong, so the agent can rely on the current placement/sort path instead of inventing folder rules.
- **Undo today:** [`ImportService`](../../src/PixelVault.Native/Services/Import/ImportService.cs) exposes `LoadUndoManifest`, `SaveUndoManifest`, and `ExecuteUndoImportMoves` with [`UndoImportEntry`](../../src/PixelVault.Native/Models/ImportModels.cs); [`UndoLastImport`](../../src/PixelVault.Native/Import/MainWindow.ImportWorkflow.Steps.cs) moves the **entire** last manifest back. AINT should extend this story for **selective** undo and **visibility** into what the agent did.

What is missing today:
- a background watcher / scheduler
- a persisted **`AutoIntakeMode`** for **custom** rules (“safe for auto-intake” opt-in)
- a headless way to run the existing intake/import logic without opening the normal import/review windows
- reliable **built-in vs custom** branching in policy (convention metadata or flag—implementation detail in Slice 1–2)
- **merged or batch-aware undo** if multiple imports (foreground + background) must coexist without overwriting the undo manifest (see [Undo and manifest design](#undo-and-manifest-design-background-vs-foreground))

---

## Goals

1. Let PixelVault automatically process uploads while the app is running when gates pass (**custom:** explicit trust + gates; **built-in:** end-to-end automatic import possible).
2. Reuse the existing parser and import pipeline instead of duplicating business logic.
3. Keep all uncertain files visible in Intake Preview / Manual Intake.
4. Respect the new storage model and current placement/sort pipeline.
5. Make the feature observable, **reviewable**, **reversible per file**, and safe by default.

---

## Non-goals

1. Do not build a Windows service or tray daemon in v1. The agent runs only while PixelVault is open.
2. Do not auto-process ambiguous heuristic matches in v1.
3. Do not add ML / fuzzy “guess what this filename probably means” behavior to the agent.
4. Do not bypass the current import/move/sort path.
5. Do not watch the library folder or destination folder in v1. Only the configured upload/source folders are in scope.
6. Do not auto-delete historical stale exports, old uploads, or unrelated files as part of this feature.
7. Do not promise **full** reversal of every side effect (e.g. embedded metadata already written per existing undo copy). v1 undo should match **current** undo semantics: **move files back to source**; be explicit in the summary dialog if metadata/comments persist in files (same as `UndoLastImport` messaging today).
8. Do not **interrupt** the user with modal dialogs as part of the agent’s normal run (no “stop and confirm” mid-batch). Observation is **non-blocking** (toast, optional modeless summary / activity view, logs); undo is **user-initiated** after the fact.

---

## Product decisions to lock before implementation

### Decision 1: Feature shape

The user-facing feature should be called **Background Intake Agent** in docs/plans, but the UI can use plainer wording such as:
- `Background auto-intake`
- `Automatically import trusted filename matches`

### Decision 2: Safe default

The feature should be **off by default**.

Rationale:
- a rule bug should not silently move files for users who never opted in
- this is a workflow automation feature, not a baseline parser behavior change

### Decision 3: Trusted-match requirement (custom vs built-in)

Auto-intake should not mean “any matched convention.”

**Custom (user-defined) rules — conservative:**

- the parser matched the rule
- the rule’s persisted **`AutoIntakeMode`** is **`Trusted exact match`** (default remains **`Manual only`** until the user opts in)
- the parse satisfies the same metadata/update safety constraints the app already uses for automatic intake
- the file is **fully auto-capable** for the headless import path (same bar as built-in below)

**Built-in rules — capability-based (no separate “trust this built-in” toggle required for v1):**

- the parser matched the built-in rule and it is **enabled**
- intake analysis agrees the file belongs in the **automatic** path (not manual-only / not blocked by missing Steam App ID, etc.)
- the **headless import coordinator** can run **rename → metadata → move → sort** without a step that requires user input (e.g. cannot resolve a game name / title for placement → **do not** auto-import)
- if anything fails that bar, the file **stays in source** and shows in Intake Preview / Manual Intake under the **same sections** it would today—no agent-specific quarantine

**First-pass posture:** err on the side of **not** moving files when eligibility is ambiguous; tighten gates in code rather than expanding automation silently.

### Decision 4: App-open only in v1

The agent runs only while PixelVault is open.

Rationale:
- avoids service/install complexity
- keeps logs, toasts, cancel states, and **post-import review** visible
- reuses the app’s current settings, parser, import service, and session wiring

### Decision 5: Source scope in v1

The agent should watch the configured **source folders** only.

Rationale:
- that is where intake already starts
- destination and library should remain outputs of import/placement, not watched inputs

### Decision 6: Top-level behavior in v1

The agent should process the same **top-level media intake set** as the current non-recursive import path unless an explicit source-root recursion design is added later.

Rationale:
- current `BuildSourceInventory(false)` behavior is already the most stable baseline
- recursive rename scope is a separate axis and should not be silently coupled to automation

### Decision 7: Uncertain files stay manual

If a file fails any gate, it stays where it is and remains available in:
- Intake Preview
- Manual Intake

No silent discard, quarantine, or fallback move in v1.

### Decision 8: Built-in rules — trust only what can complete full auto-import

**Locked product direction:**

- Built-ins participate in background auto-intake **without** requiring a per-built-in “trust” flag in v1.
- Eligibility = **the file can go through the entire automatic import** using existing pipeline semantics. If a step cannot complete without the user (identify game name, required App ID, manual metadata, etc.), the agent **does not** move the file; it remains in the upload/source folder and appears in intake UI in the **same bucket** the current preview would use (auto-capable vs manual, etc.).
- This is **not** “trust all built-in pattern matches”—it is **strictly** “trust the pipeline when it can finish unattended.”

**Custom rules** still use persisted **`AutoIntakeMode`** (**Decision 3**) so users opt in rule-by-rule.

**Rules UI:** built-in rows can show a **read-only** explanation (e.g. “Background import when full automatic import is possible”) rather than an `Auto-intake` dropdown; only **custom** rules get **`Manual only` / `Trusted exact match`**.

### Decision 9: Respect current storage model

The agent must end in the existing import + sort + placement path so it uses the current library storage model.

It must not derive folder placement from:
- raw source folder names
- upload folder names
- old `Game - Platform` assumptions

### Decision 10: Post-import transparency and selective undo (no interruptions)

While the agent is active, the user should **not** be blocked by modal prompts; the agent is given enough **internal** trust to move files that pass gates (**Decision 3** / **8**), with **conservative** eligibility on the first pass.

**Observation (non-blocking):**

- The user can **see what moved**: original name, matched rule, **final path** after sort—via a **dedicated window** that is **modeless** by default (or opened only from toast/menu), plus structured **logs** and optional **toast** with a **Review** action.
- A **modeless** “Background imports” / “Auto-intake activity” window can stay open or be reopened from **Review** / command palette; it lists batches and rows without stealing focus from an in-progress foreground task unless the user chooses to interact.

**Correction:**

- **Per-row checkboxes** and **Undo selected** move only chosen files back (same mechanics as [Undo and manifest design](#undo-and-manifest-design-background-vs-foreground)); optional confirm only when undoing **many** files at once.
- Copy should state that **metadata embedded during import may remain** (same as `UndoLastImport` today).

Optional settings (v1 or v1.1):

- `background_auto_intake_show_summary` — default **off** or **modeless open** (product choice): never **modal** on every batch
- toast: **“Review imports”** opens the same window focused on the latest batch

### Decision 11: What pauses the agent (conservative first pass, refine in implementation)

**Direction:** Finalize the exact pause list **during implementation** when wiring real locks; start **conservative** and expand only when a concrete conflict is proven.

**Likely v1 minimum (starting point):**

- standard **import** workflow with progress UI
- **manual intake** / review flows that touch the same source files
- **import and edit**

**Defer / add later:** broad “metadata apply” or library-wide operations—only if they share files with import or lack a lock today; document each addition.

Queue stable candidates while paused; drain when the busy gate clears.

---

## Definition of “perfectly matches a rule”

For this feature, eligibility should be defined in code as (all must hold):

1. `FilenameParseResult.MatchedConvention == true`
2. the matched `ConventionId` resolves to an **enabled** rule
3. **Rule-type branch:**
   - **Custom rule:** `AutoIntakeMode == TrustedExactMatch`
   - **Built-in rule:** no persisted trust flag required; instead require the **full auto-import capability** checks in steps 4–5 (same as “would not stall waiting for user”)
4. the parse does not require manual routing:
   - if `RoutesToManualWhenMissingSteamAppId == true`, a Steam App ID must be present
5. the file passes the same automatic metadata gate the current intake preview uses **and** is classified as **auto-capable** for the standard import path (not relegated to manual-only for game/title/App ID or other reasons):
   - video, or
   - Xbox-tagged capture, or
   - parse has a capture time  
   — plus any additional conditions **`HeadlessImportCoordinator`** shares with today’s automatic move/sort (game resolution, etc.)
6. the file is stable on disk
7. the file is not already in-flight or already handled for the same observed stamp

This is intentionally stricter than “pattern matched.” **Built-in** matches that would land in manual intake stay **out** of the agent’s move set.

---

## Service boundaries (avoid duplicate gates)

These components are **intentionally separate**; responsibilities must not blur:

| Component | Owns |
|-----------|------|
| **`IntakeAnalysisService`** (extracted from preview) | Same classification as today: per-file **`IntakePreviewFileAnalysis`**, auto-capable vs manual-only, shared by **Intake Preview** and the agent. **No** rule-trust flag here unless it is purely “read from rule model.” |
| **`AutoIntakePolicy`** | **Thin** layer: given analysis + resolved rule, answers **eligible for auto-intake** using **one** function such as `IsEligibleForAutoIntake(...)` so gates **do not diverge** from preview. Encodes **Decision 3**: **custom** → enabled + `AutoIntakeMode == TrustedExactMatch` + full auto-capable; **built-in** → enabled + full auto-capable (no `TrustedExactMatch` column required). Stability + dedupe stay on the agent or a small helper. |
| **`HeadlessImportCoordinator`** | Orchestrates **the same** rename / metadata / move / sort steps as the UI workflow for an **explicit file list**; **no** eligibility parsing beyond what the coordinator already needs for safe execution. |
| **`BackgroundIntakeAgent`** | Watchers, debounce, stability probe, busy gates, calls policy + coordinator, raises UI events for toast/summary. |

---

## Post-import summary dialog and selective undo

### UX

- **Window:** **modeless** `Window` by default ([Decision 10](#decision-10-post-import-transparency-and-selective-undo-no-interruptions)) so the agent never blocks the user mid-workflow; owned by the library/main shell — product name e.g. **“Background imports”** or **“Auto-intake activity”**.
- **Content:** sortable or grouped list (by run time if multiple batches per session); columns at minimum: **original name**, **from (source root / basename)**, **to (final path)**, **rule**, **status** (success / partial if applicable).
- **Actions:**
  - **Select all / none**
  - **Undo selected** — confirm if > N files or destructive threshold
  - **Close** / **Open file location** (optional nicety for selected rows)
- **Entry points:**
  - optional: open **modeless** window after a batch when setting **show summary** is on (**never** modal-on-every-batch as default)
  - **toast**: “Imported N file(s)” → **Review** focuses the window on that batch
  - optional: command palette / Library menu **“Background imports…”** / session ring buffer

### Undo and manifest design (background vs foreground)

Today’s pipeline **replaces** the undo manifest with the move step’s entries for a **single** import (`SaveUndoManifest` after move). For AINT:

- **Design requirement (lock in Slice 4):** define how **foreground** import and **background** batches interact. **First pass — conservative:** prefer the **smallest** change that avoids silent clobber (e.g. append/merge manifest, or session-scoped undo list + strict busy gate); expand to full merge once stable.
  - **Preferred longer-term:** **append** new `UndoImportEntry` rows to a **merged manifest** (or ring buffer of batches) so **selective** undo removes only chosen entries and `SaveUndoManifest` keeps the remainder; align `UndoLastImport` copy with merged semantics.
  - **Acceptable v1 minimum:** batch-scoped undo + busy gate if merge slips the first milestone—document behavior clearly.

**Selective undo implementation:** reuse `ExecuteUndoImportMoves(IEnumerable<UndoImportEntry>)` with the **selected subset**; persist **`RemainingEntries`** via `SaveUndoManifest` (same pattern as `UndoLastImport` but partial list).

### Files likely touched

- New: e.g. `UI/Intake/BackgroundAutoIntakeSummaryWindow.xaml` + code-behind or small MVVM-lite
- [`ImportService.cs`](../../src/PixelVault.Native/Services/Import/ImportService.cs) — manifest merge helpers if needed
- [`MainWindow.ImportWorkflow.Steps.cs`](../../src/PixelVault.Native/Import/MainWindow.ImportWorkflow.Steps.cs) — align messaging with selective undo
- Background agent host — callback to `Dispatcher` to open summary with model

---

## Current code seams to reuse

### Existing intake analysis

These should be reused or extracted, not duplicated:
- [`AnalyzeIntakePreviewFiles`](../../src/PixelVault.Native/UI/Intake/MainWindow.IntakePreview.cs)
- [`BuildReviewItems`](../../src/PixelVault.Native/UI/Intake/MainWindow.IntakePreview.cs)
- [`BuildManualMetadataItems`](../../src/PixelVault.Native/UI/Intake/MainWindow.IntakePreview.cs)

These functions already encode the distinction between:
- auto-capable files
- manual-only files

**Note:** rule editor surfaces may span [`FilenameConventionEditorWindow.cs`](../../src/PixelVault.Native/UI/Editors/FilenameConventionEditorWindow.cs) and [`FilenameConventionEditor.cs`](../../src/PixelVault.Native/UI/FilenameConventionEditor.cs); plan edits in both if trust UX is split.

### Existing import execution

The agent should eventually run the same logic that the import workflow already uses:
- [`RunSteamRenameAsync`](../../src/PixelVault.Native/Services/Import/ImportService.cs)
- rename / metadata / move / sort orchestration in [`ImportWorkflow.cs`](../../src/PixelVault.Native/Import/ImportWorkflow.cs)

### Existing storage model

The agent should rely on the current import/sort/placement path so it ends in the same storage model already used by normal intake and library edits.

That keeps auto-intake aligned with:
- current canonical placement
- current game-index ownership
- current folder re-home behavior

---

## Proposed architecture

### 1. `BackgroundIntakeAgent`

New runtime service responsible for:
- starting/stopping watchers
- collecting changed source paths
- debouncing noisy file events
- probing for file stability
- asking **`AutoIntakePolicy`** whether a file is safe to auto-process
- handing eligible files to **`HeadlessImportCoordinator`**
- publishing **batch results** for the summary dialog

Recommended new file:
- `src/PixelVault.Native/Services/Intake/BackgroundIntakeAgent.cs`

### 2. `IntakeAnalysisService`

Extract the current intake-preview classification logic out of `MainWindow` so both:
- the UI preview
- the background agent

use the same code.

Recommended new files:
- `src/PixelVault.Native/Services/Intake/IntakeAnalysisService.cs`
- optional small model file if the extracted types need to be shared cleanly

### 3. `AutoIntakePolicy`

Thin policy object; see [Service boundaries](#service-boundaries-avoid-duplicate-gates).

Recommended new file:
- `src/PixelVault.Native/Services/Intake/AutoIntakePolicy.cs`

### 4. `HeadlessImportCoordinator`

A headless execution layer that can process a specific set of upload files without opening the normal import/review windows.

Recommended new file:
- `src/PixelVault.Native/Services/Intake/HeadlessImportCoordinator.cs`

This should call the same underlying import steps as the current workflow and return **structured per-file outcomes** for the summary dialog.

### 5. `SourceFileStabilityProbe`

Small utility that waits until a file is “quiet” before processing.

Recommended new file:
- `src/PixelVault.Native/Services/Intake/SourceFileStabilityProbe.cs`

---

## Data model and persistence changes

## A. Rule-level trust setting

Do **not** overload `ConfidenceLabel` for this.

`ConfidenceLabel` currently describes parse confidence/source, not user intent. Auto-intake trust is a product choice and should be represented explicitly.

Recommended addition to `FilenameConventionRule`:
- `AutoIntakeMode` — applies to **custom** rules (**Decision 3**). **Built-in** rules do not rely on this flag for v1; eligibility is **capability-based** only.

Recommended values:
- `ManualOnly`
- `TrustedExactMatch`

Possible shape:
- enum in code, persisted as text

Files likely touched:
- [`FilenameParsingModels.cs`](../../src/PixelVault.Native/Models/FilenameParsingModels.cs)
- [`IndexPersistenceService.cs`](../../src/PixelVault.Native/Services/Indexing/IndexPersistenceService.cs)
- [`FilenameRulesService.cs`](../../src/PixelVault.Native/Services/FilenameRules/FilenameRulesService.cs)
- [`FilenameParserService.cs`](../../src/PixelVault.Native/Services/FilenameParsing/FilenameParserService.cs) if parse results should surface the trusted state

### Migration impact

Non-breaking SQLite migration:

- add `auto_intake_mode TEXT NOT NULL DEFAULT 'ManualOnly'` to `filename_convention` (use the **canonical serialized enum value**, not empty string — empty/`NULL`-like sentinels are easy to mishandle in loaders).

Migration rule:
- existing rules default to manual-only
- no automatic trust upgrade for existing custom or built-in rules
- unknown legacy values should be interpreted as **`ManualOnly`**

## B. App settings

Recommended new settings in `AppSettings` / `PixelVault.settings.ini`:
- `background_auto_intake_enabled`
- `background_auto_intake_quiet_seconds`
- `background_auto_intake_toasts_enabled`
- `background_auto_intake_show_summary` (optional v1 — show summary dialog after each batch)

Optional later:
- `background_auto_intake_source_subfolders`
- `background_auto_intake_start_paused`

Files likely touched:
- [`AppSettings.cs`](../../src/PixelVault.Native/Services/Config/AppSettings.cs)
- [`SettingsService.cs`](../../src/PixelVault.Native/Services/Config/SettingsService.cs)
- [`SettingsShellHost.cs`](../../src/PixelVault.Native/UI/Settings/SettingsShellHost.cs)
- [`MainWindow.SettingsPersistence.cs`](../../src/PixelVault.Native/UI/Settings/MainWindow.SettingsPersistence.cs)

## C. Runtime state: pending queue + undo visibility

- **Pending / in-flight / last stamp:** in-memory only in v1 (as before).
- **Summary dialog:** may keep a **session-scoped ring buffer** of recent auto-intake batches (paths, rule id, outcome) so the user can reopen **Review** from a toast after dismiss; cap list size (e.g. last 10 batches or last 200 files) to avoid unbounded memory.

---

## Detailed behavior design

## 1. Watch model

Use a hybrid design:
- `FileSystemWatcher` to wake the system quickly
- debounced background scan to gather stable candidates

Rationale:
- watchers alone are too noisy with Dropbox, Game Bar, Steam, phone sync, etc.
- pure polling is simpler but less responsive
- a hybrid gives responsiveness without trusting raw events too much

**Addition — missed events and network paths:** `FileSystemWatcher` can drop events under load or on **UNC/network** roots. Mitigation for v1 or v1.1:

- **Periodic reconciliation:** optional slow pass (e.g. every 2–5 minutes) comparing top-level `BuildSourceInventory(false)` to pending/in-flight sets, **or** reconciliation when the window gains focus.

### Watched events

Listen to:
- created
- changed
- renamed

Ignore:
- deletes as a processing trigger

### Watch targets

One watcher per configured source root from `SourceRootsSerialized`.

### Event handling

Watcher events should only:
- normalize the path
- verify it is a media candidate
- enqueue it into a pending set
- schedule a debounced background pass

Watcher events should not directly run import logic on the UI thread.

## 2. File stability gate

A file should only be processed when:
- it exists
- size and last write time are unchanged across at least two probes
- it can be opened for read without sharing violations

Recommended default:
- quiet window: `3` seconds
- max retries: `10`
- retry interval: `1` second

If a file never stabilizes:
- log it
- leave it for manual intake
- do not mark it failed permanently in v1

## 3. Eligibility gate

The agent should analyze pending files through the extracted intake-analysis path and only process the subset that satisfies the strict auto gate (via **`AutoIntakePolicy`**).

Recommended gate order:
1. file exists and is media
2. file is under a configured source root
3. file is stable
4. parser matched a convention
5. matched rule is enabled
6. **trust / capability:** **custom** → `AutoIntakeMode == TrustedExactMatch`; **built-in** → passes full auto-import capability (see [Definition](#definition-of-perfectly-matches-a-rule))
7. parse does not require manual routing
8. parse satisfies current auto metadata/update capability; file is in the **auto-capable** intake bucket (not manual-only)
9. file is not already in-flight

If any step fails:
- do not auto-process
- do not delete
- do not move
- leave the file visible to Intake Preview / Manual Intake

## 4. Headless import behavior

The background agent should execute the existing standard import path, not invent a special mini-import.

Recommended v1 behavior:
- batch eligible files
- run Steam/non-Steam rename where applicable
- run metadata update
- move files into destination
- sort into canonical game folders
- refresh intake preview / library signals after completion
- populate **summary model** + **undo entries** for the dialog

**Batch failure semantics (first pass — conservative):**

- Processing is **per file** within a batch: one failure does not block unrelated files unless the implementation uses a single transactional batch (not required for v1).
- Partial success: activity window + toast list successes vs skipped; **Review** opens details.
- Failed files **remain in source** (unchanged).
- Prefer **tight** eligibility over batch optimism—if unsure, skip auto-import.

Important:
- do **not** run the `Import and Edit` path
- do **not** open the manual metadata window
- do **not** auto-populate freeform comments

## 5. Manual fallback behavior

Files that are not eligible should remain untouched in the upload/source folder.

They should still appear in:
- Intake Preview
- Manual Intake

That preserves the current workflow:
- trusted exact matches get zero-click handling
- everything else still goes through review

## 6. Concurrency rules

The agent should never run on top of another active import-like workflow that could touch the same files.

See **[Decision 11](#decision-11-what-pauses-the-agent-conservative-first-pass-refine-in-implementation)** for the evolving pause list. **Implementation approach:**

- a simple shared **intake busy** gate wired to the workflows you enumerate during Slice 8
- queue new stable candidates while busy
- process them when the current workflow finishes

## 7. Notifications and logs

Minimum user-visible behavior:
- toast when auto-intake processed files successfully (with **Review** action; **non-blocking** per [Decision 10](#decision-10-post-import-transparency-and-selective-undo-no-interruptions))
- warning toast if some background files failed
- settings toggle to silence informational toasts if needed
- **modeless activity / summary window** for audit and selective undo

Minimum log content:
- source file path
- matched convention id / name
- trusted rule decision
- stability wait result
- final workflow summary (per-file outcome in structured log lines if feasible)

Do not log as if the agent used AI judgment. Keep logs deterministic and auditable.

**Security / expectations (one line for users):** source folders remain **trusted input**; auto-intake increases the impact of mistakes — keep the feature **off by default** and trust **explicit**.

---

## UI and UX plan

## A. Settings

Add a small section in Path/Workflow settings for the global feature:
- enable background auto-intake
- quiet/stability delay
- toast preference
- optional: show summary after auto-import

Suggested label:
- `Automatically import trusted filename matches in the background`

Suggested help text:
- “Custom rules: only those you mark as trusted exact match can auto-import. Built-in rules: only when the app can run the full automatic import without your input. Everything else stays in Intake Preview. You can review and undo moves from Background imports.”

## B. Renaming Rules UI

**Custom rules:** per-rule **`Auto-intake`** control:

- `Manual only` (default)
- `Trusted exact match`

**Built-in rules:** **read-only** copy per [Decision 8](#decision-8-built-in-rules--trust-only-what-can-complete-full-auto-import) (no trust toggle); optional link to docs.

Touch **`FilenameConventionEditorWindow`** and **`FilenameConventionEditor`** as needed.

## C. Post-import summary

See [Post-import summary dialog and selective undo](#post-import-summary-dialog-and-selective-undo).

## D. Diagnostics

Add lightweight diagnostics later if needed:
- watcher running / stopped
- watched source roots
- pending file count
- last auto-intake run summary

This can live in existing health/diagnostics surfaces after v1 if needed.

---

## Implementation slices

### Slice 0 — Lock the product decisions

Before coding:
- confirm app-open-only scope
- confirm top-level-only scope for v1
- confirm **Decision 3 / 8** (custom trusted flag + built-in capability-only)
- confirm safe default = off; **conservative** first pass on merge, batching, pause list
- confirm **non-blocking** observation ([Decision 10](#decision-10-post-import-transparency-and-selective-undo-no-interruptions))
- sketch **Decision 11** pause list for Slice 8

Exit criteria:
- no ambiguity: built-ins auto-run only when **full** auto-import can complete; heuristics/ambiguous matches stay manual
- modeless review + selective undo agreed

### Slice 1 — Extract reusable intake analysis

Work:
- move intake-analysis logic out of `MainWindow.IntakePreview.cs` into a reusable service
- keep UI behavior unchanged

Target files:
- new `Services/Intake/IntakeAnalysisService.cs`
- callers in `MainWindow.IntakePreview.cs`
- any small shared model file if needed

Exit criteria:
- Intake Preview still behaves the same
- headless callers can request the same review/manual classification

### Slice 2 — Add rule trust persistence

Work:
- add `AutoIntakeMode` to `FilenameConventionRule`
- add database column + migration (`DEFAULT 'ManualOnly'`)
- load/save it through index persistence
- expose it in `FilenameRulesService`

Target files:
- `Models/FilenameParsingModels.cs`
- `Services/Indexing/IndexPersistenceService.cs`
- `Services/FilenameRules/FilenameRulesService.cs`

Exit criteria:
- custom rule can persist `ManualOnly` vs `TrustedExactMatch`
- existing rules load as manual-only

### Slice 3 — Add settings and wiring

Work:
- add global app settings (including optional `background_auto_intake_show_summary`)
- surface them in Settings UI
- wire startup load/save

Target files:
- `Services/Config/AppSettings.cs`
- `Services/Config/SettingsService.cs`
- `UI/Settings/SettingsShellHost.cs`
- `UI/Settings/MainWindow.SettingsPersistence.cs`

Exit criteria:
- feature can be enabled/disabled without code changes

### Slice 4 — Extract headless import coordinator + undo manifest strategy

Work:
- pull the reusable parts of current import workflow orchestration into a headless coordinator
- allow explicit file-list processing
- preserve current manual/UI workflows
- **implement or specify manifest merge** so selective undo and foreground import do not silently clobber each other

Target files:
- `Import/ImportWorkflow.cs`
- `Services/Import/ImportService.cs` (manifest helpers)
- possible new `Services/Intake/HeadlessImportCoordinator.cs`
- `MainWindow.StartupInitialization.cs` for dependency wiring

Exit criteria:
- a caller can process a batch of eligible source files without opening import dialogs
- current import buttons still behave the same
- **documented** behavior when multiple batches occur

### Slice 5 — Renaming Rules UI trust toggle (before full e2e automation)

Work:
- expose **`Auto-intake`** for **custom** rules in advanced (and guided if FNRU coordination allows)
- **built-ins:** read-only explanation only ([Decision 8](#decision-8-built-in-rules--trust-only-what-can-complete-full-auto-import)); do not write spurious trust flags to built-in rows

Target files:
- `UI/Editors/FilenameConventionEditorWindow.cs`
- `UI/FilenameConventionEditor.cs`
- `Services/FilenameRules/FilenameRulesService.cs`
- maybe builder draft models if the trust setting appears in guided mode

Exit criteria:
- user can mark a **custom** rule as **`Trusted exact match`** without hand-editing SQLite
- built-in UX matches capability-based auto-intake story

**Rationale:** moved **before** watcher e2e so dogfood does not depend on raw DB edits.

### Slice 6 — Implement watcher + queue + stability probe

Work:
- add `BackgroundIntakeAgent`
- add watchers for source roots
- debounce and batch paths
- probe for stability
- call extracted intake analysis + policy
- dispatch eligible files to headless coordinator

Target files:
- new `Services/Intake/BackgroundIntakeAgent.cs`
- new `Services/Intake/SourceFileStabilityProbe.cs`
- startup wiring in `PixelVault.Native.cs` / `MainWindow.StartupInitialization.cs`

Exit criteria:
- dropping a **custom trusted exact-match** file into a source root results in background import when gates pass
- dropping a file that matches a **built-in** only runs when **full** auto-import is possible; otherwise it stays in source and appears in the correct intake section
- uncertain / manual-only files remain untouched

### Slice 7 — Post-import activity window + selective undo

Work:
- new **modeless** window listing imported files (original name, from, to, rule) with checkboxes
- **Undo selected** wired to `ExecuteUndoImportMoves` + manifest update
- toast **Review** action; optional session ring buffer
- align copy with existing `UndoLastImport` expectations
- **no modal** on every batch by default

Exit criteria:
- user can see what was auto-imported and undo **individual** files without being interrupted during import
- manifest state remains consistent after partial undo (per chosen merge strategy)

### Slice 8 — Logging, toasts, busy-state coordination

Work:
- implement **Decision 11** pause list (start conservative; document each hook)
- add clear logging and user feedback
- optional periodic watcher reconciliation

Target files:
- background agent service
- workflow entry points in `ImportWorkflow.cs`
- any shared busy-state host wiring

Exit criteria:
- no overlapping import workflows for defined pause cases
- user can tell what the agent did (logs + activity window)

### Slice 9 — Verification and rollout

Work:
- automated tests
- manual golden-path verification
- dogfood with one or two trusted rules before broader use

Exit criteria:
- explicit pass on test matrix below

**Slice 9 completion (repo):**
- Automated coverage added in `PixelVault.Native.Tests`: `ForegroundIntakeBusyGate`, `SourceFileStabilityProbe`, `BackgroundIntakeActivitySession` caps, extended `AutoIntakePolicy` / settings INI round-trip for background auto-intake keys (see `ForegroundIntakeBusyGateTests`, `SourceFileStabilityProbeTests`, `BackgroundIntakeActivitySessionTests`, `AutoIntakePolicyTests`, `SettingsServiceTests`).
- **Manual / dogfood (operator):** run through [`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`](../MANUAL_GOLDEN_PATH_CHECKLIST.md) as appropriate, then spot-check: enable background auto-intake; drop a **trusted exact-match** file into a source folder; confirm move + **Background imports** activity + optional **Review** toast; run a foreground **Import** while a file is waiting and confirm background work **defers** (no overlapping moves); selective undo one row and confirm manifest consistency.

---

## Test plan

## Automated tests

### Intake analysis / policy
- **custom** rule: `TrustedExactMatch` + full auto-capable → eligible
- **custom** rule: `ManualOnly` → not eligible
- **built-in** rule: full auto-import possible → eligible without trust flag
- **built-in** rule: would need user for game/title/App ID / manual metadata → **not** eligible; file stays in source / correct intake bucket
- heuristic-only / ambiguous match → not auto-processed
- rule requiring Steam App ID routes to manual when ID is missing
- unstable file is deferred

### Persistence
- new `auto_intake_mode` column round-trips through SQLite for **custom** rules
- existing rules default safely after migration (`ManualOnly`)
- built-in eligibility does not depend on `auto_intake_mode` in v1

### Headless import
- exact-match trusted file runs through rename/metadata/move/sort
- manual-only file is not moved
- headless batch respects current storage/placement behavior
- files already moved out of source are not reprocessed
- **partial batch:** one failure does not incorrectly mark successes as failed (per agreed semantics)

### Watcher / queue
- duplicate change events for the same file collapse into one processing attempt
- in-flight file is not dispatched twice
- busy-state pause defers work instead of overlapping imports

### Activity window / undo
- modeless window lists expected rows after a mocked headless batch
- selective undo calls move-back only for selected entries
- manifest after partial undo matches remaining entries
- **no** required modal on batch completion in default configuration

## Manual tests

1. Enable background auto-intake.
2. Mark one safe **custom** rule as `Trusted exact match` (via UI), **or** use a **built-in** that can complete full auto-import for a test file.
3. Drop a matching file into the upload/source folder.
4. Confirm:
   - file waits until stable
   - file is imported automatically
   - file lands in the same final location normal import would use
   - no manual intake dialog opens **during** auto-import (**Decision 10**)
   - **Review** / activity window shows original name, rule, final path; **Undo selected** returns it to source

5. Drop a non-matching or manual-only file.
6. Confirm it stays in upload and appears in Intake Preview.

7. During a normal import, drop another trusted file.
8. Confirm the background agent waits until the foreground workflow finishes.

9. Re-open the app with feature disabled.
10. Confirm files are not processed in the background.

11. Import two trusted files in one batch; undo **one**; confirm the other stays in the library and manifest reflects one remaining undo entry (per design).

---

## Risks and mitigations

## Risk 1: cloud-sync / partial-write races

Mitigation:
- stability probe
- delayed processing
- retry loop

## Risk 2: duplicate watcher events

Mitigation:
- pending-path set
- in-flight set
- last-processed stamp cache

## Risk 3: bad rule causes unintended moves

Mitigation:
- default off
- per-rule explicit trust
- uncertain files remain manual
- visible logs, toasts, and **summary + selective undo**

## Risk 4: hidden workflow duplication

Mitigation:
- extract shared services
- one headless execution path
- do not re-implement rename/metadata/move/sort separately
- **single eligibility function** in `AutoIntakePolicy`

## Risk 5: divergence from storage model

Mitigation:
- always finish in current import/sort path
- do not compute placement inside the background agent

## Risk 6: watcher buffer overflow / network roots

Mitigation:
- debounced scan + optional periodic or focus reconciliation
- document UNC limitations in settings help

## Risk 7: undo manifest clobbering

Mitigation:
- **manifest merge or batch-scoped undo** decided in Slice 4
- tests for foreground + background interaction

## Risk 8: wrong built-in vs custom branch in policy

Mitigation:
- single helper `IsBuiltInConvention(...)` (or equivalent) used only from **`AutoIntakePolicy`**
- tests: custom `ManualOnly` must not auto-import; built-in must not require `TrustedExactMatch` flag

---

## Rollout recommendation

### Phase 1

Internal/dogfood only:
- enable the feature locally
- exercise **custom** `Trusted exact match` rules and **built-in** matches that pass **full** auto-import
- validate that files land correctly, **activity window + undo** work, and partial-capability files stay in intake

### Phase 2

Limited expansion:
- broaden **custom** trusted rules; validate **built-in** behavior on real Steam/Xbox/phone exports
- keep feature off by default in public builds

### Phase 3

General availability:
- after sustained successful dogfooding
- only once logs, busy-state coordination, manifest/undo behavior, and rollback confidence are solid

---

## Suggested first implementation order

Recommended sequence (updated for trust UI before watcher, summary after headless import path):

1. Slice 0 — lock decisions (including undo manifest + batch semantics)
2. Slice 1 — extract intake analysis
3. Slice 4 — headless import coordinator **+ undo manifest strategy**
4. Slice 2 + Slice 3 — trust persistence + settings
5. Slice 5 — Rules UI trust toggle (**enables dogfood without SQLite hacks**)
6. Slice 6 — watcher / background agent
7. Slice 7 — post-import **modeless** activity window + selective undo
8. Slice 8 — logging, toasts, busy-state polish
9. Slice 9 — verification

That sequence keeps the risky part small:
- first make intake logic reusable
- then make it callable headlessly with **safe undo bookkeeping**
- then persistence + **UI to trust rules**
- then automation + **non-blocking review/undo surface**

---

## Pre-implementation review checklist (go through before coding)

Use this as a **short meeting or solo sign-off** before Slice 1:

- [ ] **LIBST** accepted as the placement baseline (completed).
- [ ] **Decision 3 / 8 locked:** **custom** = `Trusted exact match` flag + gates; **built-in** = **full auto-import only**; partial capability → stay in source / normal intake sections.
- [ ] **Decision 10:** **Modeless** (or toast-only) observation by default; no interrupting modal per batch.
- [ ] **Undo:** Conservative first strategy chosen (merge vs batch-scoped); `UndoLastImport` behavior documented if it changes.
- [ ] **Activity window:** Session buffer size; columns (name, from, to, rule); optional auto-open vs Review-only.
- [ ] **Batch failures:** Per-file continue (**conservative** eligibility preferred).
- [ ] **Decision 11:** Initial pause list drafted for Slice 8 (import + manual intake + import-and-edit minimum); expand only with evidence.
- [ ] **FNRU:** Guided Builder shows **custom** trust only when ready; built-ins use read-only copy.
- [ ] **UI-001:** New services under `Services/Intake/` (or agreed path).

---

## Bottom line

This feature is feasible and fits PixelVault well, but it should be built as a **deterministic background intake worker** over the current parser/import/storage pipeline, not as a separate smart subsystem.

If implemented this way, the app gets:
- **Custom rules:** zero-click imports when the user opts into **`Trusted exact match`** and gates pass
- **Built-in rules:** automatic moves only when the **entire** standard auto-import path can finish without the user; otherwise files stay in intake as today
- The same storage and placement behavior as normal intake
- **Non-blocking** visibility (activity window, logs, toast) and **per-file undo** aligned with existing import undo mechanics
