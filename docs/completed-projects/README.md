# Completed projects

This folder indexes **finished initiatives** in the **C:\Codex** repo: enough context and pointers to find the canonical technical write-ups, without treating them as active handoff work.

**Active** planning and sequencing stay in:

- `C:\Codex\docs\ROADMAP.md`
- `C:\Codex\docs\HANDOFF.md`
- `C:\Codex\docs\PERFORMANCE_TODO.md`
- `C:\Codex\PixelVaultData\TODO.md`

**Frozen / historical plan snapshots** (do not treat as backlog):

- `C:\Codex\docs\archive\README.md`

**Notion:** [Completed projects](https://www.notion.so/33873adc59b681c6a7ffe22e8bcab2a5) (Project Wiki). Keep repo paths here authoritative for file locations; keep Notion aligned per `C:\Codex\docs\DOC_SYNC_POLICY.md`.

---

## Index

| Initiative | Completed (repo) | Canonical detail | Summary |
|------------|------------------|------------------|---------|
| **MainWindow extraction (Phases A–F)** | Apr 2026 | `C:\Codex\docs\MAINWINDOW_EXTRACTION_ROADMAP.md` | Library host + `ILibraryBrowserShell`, settings `SettingsShellHost`, photography partial, settings persistence partial; Phases A–F bars done. |
| | | `C:\Codex\docs\completed-projects\MAINWINDOW_EXTRACTION_PHASES_A-F.md` | Short completion record + changelog pointer. |
| **Single-folder storage model (LIBST)** | Apr 2026 | `C:\Codex\docs\completed-projects\PV-PLN-LIBST-001-single-folder-storage-model.md` | PV-PLN-LIBST-001: storage-group / placement, photo index re-home, SQLite integration tests; Steps 0–9 shipped. |
| **Post–app-review hardening (RVW)** | Apr 2026 | `C:\Codex\docs\APP_REVIEW_2026-04-12.md` | PV-PLN-RVW-001: regex safety, hero coalesce/cancel, **`NEXT_TRIM`** refresh, **`SteamAppIdLooksLikeFilenamePrefix`** separator + min digit length (long NonSteamId-safe); canonical write-up: `C:\Codex\docs\completed-projects\PV-PLN-RVW-001-post-app-review-hardening.md`. |

---

## Adding a new completed project

1. Add a row to the **Index** table above and a short `YOUR_PROJECT.md` in this folder (outcomes, dates, links—avoid duplicating long specs).
2. If the canonical doc stays at repo root, add a **Status: complete** banner at the top of that doc and link back here.
3. Update `C:\Codex\docs\HANDOFF.md` so “current focus” does not imply the initiative is still in flight.
4. Sync Notion (Project Wiki or Roadmap) per `DOC_SYNC_POLICY.md`.
