# PixelVault Real App Implementation Map

This document turns the goals in `docs/REAL_APP_GOALS.md` into concrete product and engineering lanes for the current codebase.

Use it when deciding:

- where a "real app" polish change should land
- which existing seam should own the work
- what to do next without waiting for the full `MainWindow` breakup to finish

This is a companion to:

- `docs/REAL_APP_GOALS.md` for the north-star experience goals
- `docs/PERFORMANCE_TODO.md` for the active performance backlog
- `docs/MAINWINDOW_EXTRACTION_ROADMAP.md` for responsibility moves out of `MainWindow`

## Working Rule

Prefer landing polish work in the seam that already exists, even if that seam is still thin.

For PixelVault today, that usually means:

- Library shell and flow: `src/PixelVault.Native/UI/Library/`
- metadata and ExifTool work: `src/PixelVault.Native/Services/Metadata/MetadataService.cs`
- cover/provider work: `src/PixelVault.Native/Services/Covers/CoverService.cs`
- library scans and cached folder models: `src/PixelVault.Native/Services/Library/LibraryScanner.cs`
- workflow progress and long-running orchestration: `src/PixelVault.Native/Import/ImportWorkflow.cs` and `src/PixelVault.Native/UI/Progress/WorkflowProgressWindow.cs`

If a change only fits by adding more deep delegate glue to `PixelVault.Native.cs`, that is usually a sign the next slice should be a seam improvement first.

## Goal Map

### 1. Library startup and selection feel instant

Primary ownership:

- `src/PixelVault.Native/UI/Library/LibraryBrowserHost.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserShowOrchestration.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserChrome.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserLayout.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserRender.FolderList.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserRender.DetailPane.cs`
- `src/PixelVault.Native/UI/Library/LibrarySession.cs`
- `src/PixelVault.Native/UI/Library/LibraryWorkspaceContext.cs`
- `src/PixelVault.Native/Services/Library/LibraryScanner.cs`
- `src/PixelVault.Native/UI/LibraryVirtualization.cs`

What "real" looks like:

- the Library window opens immediately
- cached folder state appears before expensive refresh completes
- selection changes do not feel blocked by metadata or cover work
- offscreen tile work stops mattering once the user moves somewhere else

Concrete moves:

- keep the Library shell window-first and stream folder/detail data in after first paint
- treat cached folder models as the default first render path, then quietly revalidate
- make selected-folder work higher priority than background refresh or offscreen thumb work
- coalesce duplicate reload requests into a single refresh cycle
- keep extending cancellation around folder-detail and cover-refresh churn

Good follow-on slices:

- persist and restore the last selected folder, search text, sort mode, and scroll anchor
- add explicit timing for first folder list render and first detail render
- move more Library orchestration from `LibraryBrowserShowOrchestration` into session/facade methods instead of nested closures

### 2. Cover and metadata workflows become fully backgrounded and cancellable

Primary ownership:

- `src/PixelVault.Native/Services/Covers/CoverService.cs`
- `src/PixelVault.Native/Services/Metadata/MetadataService.cs`
- `src/PixelVault.Native/Infrastructure/TimeoutWebClient.cs`
- `src/PixelVault.Native/Services/Library/LibraryScanner.cs`
- `src/PixelVault.Native/PixelVault.Native.cs`
- `src/PixelVault.Native/Import/ImportWorkflow.cs`

What "real" looks like:

- the UI stays interactive while covers or metadata are resolving
- cancellation feels immediate, not "after the current batch eventually ends"
- stale async results do not overwrite newer user intent
- provider/network failures degrade cleanly and leave the app stable

Concrete moves:

- continue converting UI call sites to async-first service methods instead of sync wrappers
- isolate every "load, then apply to UI" flow behind stale-result guards or version tokens
- tighten the remaining sync-shaped metadata paths so callers cannot accidentally block the UI thread
- expand cancellation from batch boundaries toward per-provider and per-file responsiveness
- standardize a small helper for background work + UI marshaling + fault logging to reduce race-prone custom patterns

Good follow-on slices:

- audit `GetAwaiter().GetResult()` usage in UI-adjacent flows and remove it where it can leak back onto the UI path
- separate "network/provider success" from "cover file committed to cache" so failures are easier to message and retry
- add regression tests around stale completion, cancel-while-loading, and repeated open/refresh actions

### 3. State restoration gets much stronger

Primary ownership:

- `src/PixelVault.Native/Services/Config/SettingsService.cs`
- `src/PixelVault.Native/UI/Library/LibraryBrowserHost.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserShowOrchestration.cs`
- `src/PixelVault.Native/UI/Settings/MainWindow.SettingsShell.cs`
- `src/PixelVault.Native/PixelVault.Native.cs`
- editor hosts under `src/PixelVault.Native/UI/Editors/`

What "real" looks like:

- the app reopens where the user left it
- Library context survives refreshes and normal restarts
- editors reopen without making users rebuild their mental state

Concrete moves:

- persist last Library view state: folder, search text, sort, selected item, and layout splitters
- restore in-progress context when reopening Settings, Game Index, and Photo Index surfaces
- treat restart and refresh as "resume" operations whenever the data model still supports it
- keep saved state separate from transient per-window state so restoration is predictable

Good follow-on slices:

- add a small `LibraryViewState` model owned by settings/session code instead of many scattered fields
- preserve manual editor filters and search text across reloads
- restore pending workflow context where safe, especially long library maintenance operations

### 4. Keyboard shortcuts and inline status polish land

Primary ownership:

- `src/PixelVault.Native/UI/Library/LibraryBrowserHost.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserShowOrchestration.cs`
- `src/PixelVault.Native/UI/Settings/MainWindow.SettingsShell.cs`
- `src/PixelVault.Native/UI/Editors/GameIndexEditorHost.cs`
- `src/PixelVault.Native/UI/Editors/PhotoIndexEditorHost.cs`
- `src/PixelVault.Native/UI/Intake/IntakePreviewWindow.cs`
- `src/PixelVault.Native/UI/Progress/WorkflowProgressWindow.cs`

What "real" looks like:

- common actions are faster from the keyboard than from hunting buttons
- status messaging is specific, calm, and local to the action the user just took
- fewer workflows need blocking modal detours

Concrete moves:

- define a first-class shortcut set for search, refresh, next/previous item, edit metadata, open index editors, and retry cover actions
- add inline, short-lived feedback for save, refresh, skip, cancel, and retry states
- replace generic "working" or silent waits with progress that names the current operation
- keep maintenance progress visible without trapping the user in modal walls when that is not required

Good follow-on slices:

- unify transient status copy and success/error wording across Library, intake, and editors
- add keyboard affordances to hosts as part of host ownership rather than from `MainWindow` delegates
- use progress windows for true long-running work and inline badges/banners for quick operations

### 5. Undo and recovery get broadened

Primary ownership:

- `src/PixelVault.Native/Services/Intake/ImportService.cs`
- `src/PixelVault.Native/Import/ImportWorkflow.cs`
- `src/PixelVault.Native/Services/Covers/CoverService.cs`
- `src/PixelVault.Native/Services/Metadata/MetadataService.cs`
- `src/PixelVault.Native/UI/Editors/GameIndexEditorHost.cs`
- `src/PixelVault.Native/UI/Editors/PhotoIndexEditorHost.cs`

What "real" looks like:

- risky actions feel reversible
- partial failures do not leave the app in a mysterious state
- users can recover from mistakes without digging into the filesystem

Concrete moves:

- expand undo beyond import moves into cover replacement/clear and metadata edit flows where practical
- make long-running workflows resumable or at least restartable from a known checkpoint
- expose recovery state clearly after partial failures instead of burying it in logs
- treat game/photo index edits as workflows with explicit save, revert, and conflict language

Good follow-on slices:

- formalize a lightweight operation journal for user-facing reversible actions
- separate "preview changes" from "commit changes" in more editor flows
- broaden tests around undo manifests, recovery messaging, and partial-failure outcomes

## Sequencing

### Quick wins

These can start now without waiting for a major architecture pass.

- Add Library first-paint and first-selection timing around `LibraryBrowserHost.Show` / `LibraryBrowserShowOrchestration.Run` and refresh paths.
- Persist and restore last Library context through `SettingsService` plus `LibrarySession` or `LibraryWorkspaceContext`.
- Add a first keyboard shortcut pass in the Library shell and editor hosts.
- Improve inline statuses in workflow and editor surfaces so users see "what is happening" immediately.
- Add more stale-result guards to async UI completion paths using the same pattern as the recent race fixes.

### Medium lifts

These are high value, but benefit from slightly stronger seams first.

- Move more Library orchestration out of nested closures and into `LibraryBrowserHost` plus session/facade methods.
- Standardize a shared helper for background work, cancellation, UI marshaling, and exception logging.
- Push more metadata and cover call sites to async-first paths end-to-end.
- Introduce a dedicated persisted view-state model for Library and editors.
- Broaden undo and recovery coverage for covers and metadata workflows.

### Big bets

These are the moves that most strongly change how mature the app feels, but they should build on the smaller steps above.

- Make Library rendering fully stale-while-revalidate with visible-priority work queues.
- Make provider, metadata, and library maintenance workflows uniformly cancellable and resumable.
- Reduce `MainWindow` to composition plus lifecycle, with major UX flows owned by hosts/services.
- Add a lightweight diagnostics surface for dev builds: timings, active work, cache stats, and last-failure visibility.

## File And System Watchlist

When touching these areas, bias toward these goals:

- `src/PixelVault.Native/UI/Library/LibraryBrowserHost.cs`
  - host ownership, try/catch boundary, **`ILibrarySession`**
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserShowOrchestration.cs`
  - first paint wiring, selection responsiveness, keyboard flow, inline status, state restoration; less delegate sprawl over time
- `src/PixelVault.Native/UI/Library/LibrarySession.cs`
  - session facade, persisted view context, safe access to scanner/file seams
- `src/PixelVault.Native/UI/Library/LibraryWorkspaceContext.cs`
  - cached listings, file-tag cache behavior, UI-thread boundary clarity
- `src/PixelVault.Native/UI/LibraryVirtualization.cs`
  - visible-priority work, offscreen cancellation, scroll stability
- `src/PixelVault.Native/Services/Library/LibraryScanner.cs`
  - cached-first folder models, scan batching, refresh coalescing, background safety
- `src/PixelVault.Native/Services/Metadata/MetadataService.cs`
  - true non-blocking metadata paths, cancellation, batch semantics, recovery
- `src/PixelVault.Native/Services/Covers/CoverService.cs`
  - async provider work, retry/cancel behavior, cache consistency, user-facing recovery
- `src/PixelVault.Native/Infrastructure/TimeoutWebClient.cs`
  - network cancellation, timeout behavior, predictable cleanup
- `src/PixelVault.Native/Import/ImportWorkflow.cs`
  - progress clarity, resumable workflow design, recovery and undo
- `src/PixelVault.Native/Services/Intake/ImportService.cs`
  - durable move/revert behavior and operation journaling
- `src/PixelVault.Native/PixelVault.Native.cs`
  - avoid adding new orchestration there unless it is truly composition-root work

## Practical Rule Of Thumb

If a task is about:

- "faster" or "more responsive"
  - start in `UI/Library/`, `LibraryScanner`, `MetadataService`, or `CoverService`
- "feels nicer to use"
  - start in the relevant host/window under `UI/`
- "safer" or "more trustworthy"
  - start in `ImportService`, `ImportWorkflow`, editor hosts, or service-layer recovery behavior
- "hard to reason about because async/UI state is mixed together"
  - improve the seam first, then ship the polish change through that seam

## Definition Of Progress

This roadmap is working if new polish work increasingly has these traits:

- new behavior lands in hosts/services, not directly in `PixelVault.Native.cs`
- long-running work starts off the UI thread by default
- stale async completions do not win over newer user actions
- users keep context across refreshes, restarts, and editor reloads
- the app explains itself better while it is working, saving, failing, or recovering

That is the path from "fast-growing utility" to "mature desktop app" for this codebase.
