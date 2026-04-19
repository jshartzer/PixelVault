# PixelVault — Architecture refactor contract

This document is the **durable contract** for structural refactoring: modular monolith, clear service boundaries, and incremental extraction. It **complements** (does not replace) execution details in:

- `C:\Codex\docs\plans\PV-PLN-EXT-002-service-extraction-and-organization.md` — **post–MainWindow split:** Phase A extraction milestones, then Phase B service **organization** (folders/facades/docs)
- `C:\Codex\docs\MAINWINDOW_EXTRACTION_ROADMAP.md` — *where* code moves (phases, hosts, partials)
- `C:\Codex\docs\PERFORMANCE_TODO.md` — responsiveness and async follow-ups
- `C:\Codex\docs\SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md` — parallel lanes and merge risk
- `C:\Codex\docs\pixelvault_service_split_plan.txt` — service split sequencing

**Not a feature spec.** User-visible behavior stays the same unless a slice explicitly calls for a behavior change. **Not a scratchpad** — update when principles or scope boundaries change.

---

## Purpose

Continue shrinking `MainWindow` as a god-object, strengthen real services, centralize workflow file I/O where practical, and keep threading/async honest—**in small verifiable slices**.

---

## North star vs near term (tiered bar)

| Tier | Meaning |
|------|--------|
| **North star** | `MainWindow` is primarily a **UI coordinator**; business rules and multi-step workflows live in **services** (or dedicated non-UI hosts); **workflow file access** goes through **`IFileSystemService`**; background hot paths avoid sync-over-async. |
| **Tier A (near term)** | Any **code touched in a slice** should **move toward** the north star: call services, use **`ILibrarySession`** / hosts where appropriate, route **central file work** through **`IFileSystemService`**, keep **background** work off the UI thread. |
| **Tier B (legacy)** | **Untouched** areas may still use older patterns. **Do not** require a repo-wide rewrite to consider a slice “done.” Migrate **when that area is already in scope**. |

This avoids the plan reading as permanent failure while legacy partials still exist.

---

## MainWindow — intended responsibility

**Should:**

- Handle UI events and read/write controls
- Call services, **`ILibrarySession`**, and host entry points (`LibraryBrowserHost`, editor hosts, progress shells)
- Show progress, results, and errors; use **`Dispatcher.BeginInvoke`** (or equivalent) to marshal UI updates from background work

**North-star “should not” (migrated over time, not overnight):**

- Own **business rules**, **index/scan orchestration**, **import pipeline logic**, or **parsing/indexing** that belongs in services
- Perform **workflow** file I/O that should use **`IFileSystemService`**

**Vehicle:** partial classes, **`UI/`** hosts, **`ILibrarySession`**, and phases in **`MAINWINDOW_EXTRACTION_ROADMAP.md`**.

---

## Service boundaries and the scan host **port**

**Target:** Services are **independent of WPF**—no `Window`, `Control`, or **`Dispatcher`** for **business** decisions.

**Accepted pattern today:** **`ILibraryScanHost`** is an explicit **application port** (callbacks for persistence, caches, stamps, logging, and metadata-index construction). **`ILibraryScanner`** depends on **`ILibraryScanHost`**, not on **`MainWindow`** as a type. That is **not** a “fake service” if the port stays **non-UI** and **shrinks or splits** over time instead of growing without bound.

**Direction:** Prefer **narrower** or **split** ports as slices land; avoid adding **UI-shaped** concerns to the host.

---

## Async and threading

**Do:**

- Use **async end-to-end** on paths that are already async (e.g. cover HTTP, metadata scan entry **`ScanLibraryMetadataIndexAsync`**)
- Use **`Task.Run` / thread-pool** for CPU-bound or blocking work, then **`BeginInvoke`** for UI
- Pass **values** into background work (e.g. mirrors, captured state) instead of **`Dispatcher.Invoke`** for **logic** on the UI thread

**Avoid on background hot paths:**

- **`GetAwaiter().GetResult()`** / blocking wait patterns that stall thread-pool workers during parallel scan/import/network work

**Pragmatic allowances:**

- **Thin sync facades** at stable boundaries that delegate to async implementations (documented legacy entry points)
- UI continuations that read **`.Result`** / completed task outcomes **after** checking **`RanToCompletion`** / fault / cancel—marshaling only, not “sync over async” for heavy work

Aligns with **`PERFORMANCE_TODO.md`** (items 5, 11) and completed items such as **5a** (`Dispatcher.Invoke` removal from metadata keyword paths).

---

## File-system centralization (`IFileSystemService`)

**In scope for sweeps and new code:**

- Library scan/enumeration (already **`LibraryScanner`**)
- Import / move / copy / manifest paths (**`ImportService`** and related)
- Cover cache writes where wired (**`CoverService`** optional dependency)
- **Data migration** and similar **workflow** copies on **`MainWindow`** when those methods are touched

**Explicitly deferrable (unless the same slice already edits the call site):**

- Incidental **UI-only** I/O (e.g. icons, one-off dialog paths)—optional second pass

**Goal:** One shared seam from composition root for **business-path** I/O so tests and future batching/caching stay feasible.

---

## Orchestration

- **`MainWindow`** **triggers** workflows; **services** **execute** extracted steps (**`ILibraryScanner`**, **`IImportService`**, **`ICoverService`**, etc.).
- **`ImportWorkflow`** may remain an **orchestration shell** until more steps move under **`IImportService`**—see **`pixelvault_service_split_plan.txt`**.

---

## Incremental refactor style

- **Small slices**; **compile** after each; **behavior parity** unless the slice documents an intentional change
- **One owner** for high-conflict integration (`PixelVault.Native.cs`, **`ImportWorkflow.cs`**) per **`SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md`**

**Non-goals (unless explicitly requested):**

- Full **MVVM**
- Application-wide **DI container**

Prefer **plain classes**, **explicit constructors**, and **clear boundaries**.

---

## Recommended sequencing (aligned with current repo work)

1. **Library:** Continue **`ILibraryScanner`** ownership and **`ILibrarySession`** usage; **narrow** **`ILibraryScanHost`** over time rather than expanding it casually.
2. **Import:** **Expand `IImportService`**; move business steps out of **`ImportWorkflow`** / **`MainWindow`** in coordinated slices.
3. **File system:** **`IFileSystemService`** on **business paths** as each area is touched—not one mega-PR across the entire tree.
4. **Async:** Clean up remaining sync-over-async **when profiling or when a path is already open**—**`PERFORMANCE_TODO.md`** item 11.

---

## Definition of success (milestone-oriented)

Refactor progress is **on track** when:

- **New** library/import/index behavior lands in **services** or **hosts**, not as new **large** regions of orchestration in **`PixelVault.Native.cs`**
- Services do not take **WPF** types; **ports** (**`ILibraryScanHost`**, dependencies) stay **explicit**
- **Workflow** file operations **trend** through **`IFileSystemService`**
- **Background** hot paths stay free of **blocking sync-over-async**
- **`MAINWINDOW_EXTRACTION_ROADMAP.md`** and **`CHANGELOG.md`** stay updated for shipped slices

---

## Related docs

| Doc | Role |
|-----|------|
| `HANDOFF.md` | Current stop point and what to read first |
| `POLICY.md` | Durable behavior contract |
| `plans/PV-PLN-EXT-002-service-extraction-and-organization.md` | Post–UI-split extraction + organization sequence |
| `MAINWINDOW_EXTRACTION_ROADMAP.md` | Phase E/F and extraction milestones |
| `PERFORMANCE_TODO.md` | Responsiveness and async backlog |
| `SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md` | Safe parallel lanes |
| `PROJECT_CONTEXT.md` | Product and subsystem orientation |

---

*End of architecture refactor contract.*
