# Small Feature To-Do (organized backlog)

**Live checkboxes:** [PixelVault HQ — Small Feature To-Do](https://www.notion.so/cf63eb7c2524470d9b9a601ab46f4cc6) (Project Wiki). Update Notion when items ship or when this grouping changes.

**Code system (reference):** `LIB-*` library UI, `COV-*` covers, `META-*` manual/metadata UX, `IMP-*` import, `PHT-*` photography, `UX-*` cross-app polish.

---

## Done (ship notes)

| Id | Summary |
|----|---------|
| **LIB-001** | Library search, folder, scroll — flush live session before main-window rebuild; full flush on close. |
| **META-001** | Library manual metadata — apply batch, keep dialog, next selection. |
| **META-002** | “Same as previous” in manual metadata. |
| **META-003** | Recent game title labels in settings + ComboBox. |
| **IMP-001** | Import destination preview before progress workflow. |
| **UX-002** | Copy path / open folder — manual metadata list, folder tile; capture tiles already had it. |
| **LIB-004** | `Refresh this folder only` | Scoped cover/ID refresh without full folder-list reload; detail + folder context menu + “Reload library folder list” escape hatch. |
| **LIB-007** | Larger / smaller folder tiles | Library footer **Tiles − / +**; persists `library_folder_tile_size`. |
| **COV-002** | Use current image as folder cover | Capture context menu **Use as folder cover**; detail **Use as cover** when one image selected. |
| **COV-003** | Cover source badge | Folder subtitle + detail title: **Custom / Downloaded / Preview**. |
| **UX-003** | Short toasts | Library toast host: cover/metadata/saved/tile size messages. |
| **UX-004** | Keyboard shortcut cheat sheet | **F1** or **?** — library shortcuts dialog. |
| **PHT-001** | Photography gallery refresh after delete | Library delete triggers force gallery reload when window is open. |

---

## Tier A — Smaller increments (typical single PR / short milestone)

Suitable when the change is localized UI, wiring to **existing** data, or a narrow bugfix.

The previous Tier A batch is recorded in **Done** above; add new rows here when you define the next small batch.

---

## Tier B — Medium (multi-area or new behavior, still one themed milestone)

Expect touch points across **library model**, **filters/sort**, **metadata index**, or **drag‑drop**; plan a short spec or acceptance list before coding.

| Id | Item | Notes |
|----|------|--------|
| **LIB-003** | Filter chips: missing cover, missing game ID, needs metadata | Needs **definitions** per folder/file + index/cover APIs + chip UI + interaction with search/sort. |
| **LIB-006** | Compact details side panel (counts, dates, cover source, Steam ID, path) | Layout + data plumbing; avoid duplicating full detail pane. |
| **LIB-008** | Sort by recently **edited** metadata (not capture date) | Persist or derive “last metadata touch” per file/folder; sort path in browser. |
| **LIB-009** | Jump to today / this month / oldest in large folders | Scroll-to / group navigation in virtualized detail list; define behavior with current sort modes. |
| **COV-001** | Drag-and-drop custom cover onto folder tile | WPF DnD + validation + same persistence as picker flow. |
| **META-004** | Lightweight “what changed” before saving metadata | **Diff model** (which fields differ from disk/index); copy for confirm UI. |
| **UX-001** | Undo last delete (photo / library) when recoverable | OS recycle + path rules + scope; easy to under-ship. |

---

## Tier C — Larger / product-shaped (chunk deliberately)

These imply **new concepts**, **persistence**, or **multi-session UX** — not “small” even if the first slice looks small.

| Id | Item | Notes |
|----|------|--------|
| **LIB-002** | Favorite / star folder or capture + **Favorites** filter | Star state storage, scope (per library?), filter composability with search. |
| **LIB-005** | Random capture from folder / filter | Randomness, empty state, respect multi-select?, keyboard shortcut? |
| **LIB-010** | Library tabs: Collections / favorites | Navigation model + state; overlaps **LIB-002**. |
| **PHT-002** | Publish-ready flag / tag for Immich / social | Taxonomy + persistence + maybe export. |
| **PHT-003** | Move selected photos to “publish set” workflow | New container or convention + UI. |
| **PHT-004** | Duplicate detection (filename + time proximity) | Heuristics + UI surfacing + performance on large sets. |
| **PHT-005** | Before/after crop-orientation note (editorial) | New fields + optional UI surface. |
| **PHT-006** | Move photos to SSD, Immich publish set | Ops workflow + external tool coupling. |

---

## Suggested next batches (after Tier A/B/C)

1. **Quick wins:** **LIB-004** + **COV-002** + **COV-003** (or swap in **UX-003**).
2. **Library depth:** **LIB-003** + **LIB-004** (accept Tier B cost for chips).
3. **Avoid mixing** Tier C favorites/collections (**LIB-002**, **LIB-010**) with Tier A polish in the same milestone unless scope is explicitly capped.

---

## Doc hygiene

- Prefer updating **Notion** checkboxes when items ship; keep this file’s **tiers** stable unless effort class clearly changes.
- Optional: bump **`NEXT_TRIM_PLAN.md`** or `CHANGELOG.md` for releases; no need to duplicate every checkbox here forever.
