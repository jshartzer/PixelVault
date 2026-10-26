# PixelVault planning documents (codified)

Long-lived execution plans that use a **stable plan ID** for cross-linking (repo, Notion, commits).

| Plan ID | Document | Summary |
|---------|----------|---------|
| **PV-PLN-WEB-002** | *Active — Steps 1–5 complete; Step 6 next* [Companion Screenshot Library](PV-PLN-WEB-002-companion-screenshot-library.md) | NAS media foundation, game gallery/viewer, variable square layouts, existing groups, home strip and full timeline in six increments. |
| **PV-PLN-WEB-001** | *In progress â€” editable isolated workspace / shared storage implemented* [PV-PLN-WEB-001-nas-web-companion.md](PV-PLN-WEB-001-nas-web-companion.md) | **PixelVault Companion:** broad NAS web app, starting with trackers. Real-copy editing/history, conflict guards, backup rehearsal and API client implemented; native-window cutover, trusted connector and NAS runtime remain pending. |
| **PV-PLN-V1POL-001** | [PV-PLN-V1POL-001-pre-v1-polish-program.md](PV-PLN-V1POL-001-pre-v1-polish-program.md) | Pre-V1 product polish: tokens, loading/empty states, inline feedback, command palette, smart views, health surface, staged drawers. **Notion:** [page](https://www.notion.so/33a73adc59b6819d8ddcc20b9f03b2d6). |
| **PV-PLN-LIBWS-001** | *Archived â€”* [PV-PLN-LIBWS-001-library-workspace-modes.md](../archive/PV-PLN-LIBWS-001-library-workspace-modes.md) (**Done**) | Library **workspace modes** (shipped): Folder (default), Photo (focused captures), **Timeline** as distinct mode; shell, density, rail, hero. |
| **PV-PLN-LIBST-001** | *Completed â€”* [PV-PLN-LIBST-001-single-folder-storage-model.md](../completed-projects/PV-PLN-LIBST-001-single-folder-storage-model.md) (**Done**) | Single-folder **storage model** per game while preserving **one Game Index row per console**; remove folder-structure inference and introduce explicit storage-group / placement ownership. |
| **PV-PLN-AINT-001** | *Complete â€”* [PV-PLN-AINT-001-background-intake-agent.md](PV-PLN-AINT-001-background-intake-agent.md) (**Done**) | **Background Intake Agent** â€” slices **1â€“9** shipped; long-form architecture + sequencing retained in-plan; manual dogfood as needed. |
| **PV-PLN-UI-001** | [PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md](PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md) | **Postâ€“Aâ€“F** MainWindow / `PixelVault.Native.cs` thin-out; extraction stages aligned with **`docs/ios_foundation_guide.md`** (services, plain models, mobile-safe writes). Companion **complete** track: **PV-PLN-EXT-002** (service extraction + `Services/` organization). |
| **PV-PLN-EXT-002** | *Complete â€”* [PV-PLN-EXT-002-service-extraction-and-organization.md](PV-PLN-EXT-002-service-extraction-and-organization.md) (**Done**) | Postâ€“MainWindow-split **Phase A** (extraction) + **Phase B** (organization): composition graph, intake consolidation, `IFileSystemService` / `ILogService`, **`IntakePipeline`**; index row in [`completed-projects/README.md`](../completed-projects/README.md). |
| **PV-PLN-FNRU-001** | [PV-PLN-FNRU-001-guided-builder-verification.md](PV-PLN-FNRU-001-guided-builder-verification.md) | **Guided Builder**: verification (Tests Aâ€“G) + **strategy** (save rule = format memory; optional sampleâ†’rule; parser ordering). Handoff: **`docs/FILENAME_RULES_GUIDED_BUILDER_TEST_HANDOFF.md`**. |
| **PV-PLN-RVW-001** | *Completed â€”* [PV-PLN-RVW-001-post-app-review-hardening.md](../completed-projects/PV-PLN-RVW-001-post-app-review-hardening.md) (**Done**) | **Postâ€“app-review (2026-04-12):** P1 **regex** + P2 **hero/banner** dedupe/cancel + P3 **Steam/NonSteamId numeric prefix** hardening (`SteamImportRename`); **`NEXT_TRIM_PLAN.md`** refreshed same day. Short redirect: [`PV-PLN-RVW-001-post-app-review-hardening.md`](PV-PLN-RVW-001-post-app-review-hardening.md) (retired duplicate `open/` stub: [`../archive/PV-PLN-RVW-001-open-folder-stub-superseded.md`](../archive/PV-PLN-RVW-001-open-folder-stub-superseded.md)). |
| **PV-PLN-LIBPV-001** | [PV-PLN-LIBPV-001-in-app-library-photo-viewer.md](PV-PLN-LIBPV-001-in-app-library-photo-viewer.md) | **In-app library photo viewer:** non-modal full-frame window (match main size), timeline chrome parity, **Ctrl+click** = selection only (**PV-POL-LIBPV-SEL-001**), translucent **prev/next** overlays (**PV-POL-LIBPV-NAV-001**); staged phases Aâ€“E + risks. |
| **PV-PLN-DIST-001** | [PV-PLN-DIST-001-windows-store-and-distribution-roadmap.md](PV-PLN-DIST-001-windows-store-and-distribution-roadmap.md) | **Distribution roadmap:** Phase 1 **1.0** (signing, **storage-root hardening**, tool **redistribution**, installer/updater), Phase 2 **Store** (manifest + **packaged spike**), Phase 3 **iOS/backend**; Store blockers box, tech vs submission split, Phase 2 gate. |
| **PV-PLN-PERF-001** | [PV-PLN-PERF-001-app-speed-and-efficiency.md](PV-PLN-PERF-001-app-speed-and-efficiency.md) | **App speed and efficiency:** single-pass import prep, incremental folder-cache updates, background metadata repair, progress/log coalescing, cancelable detail-pane work, scroll/render polish. |
| **PV-PLN-PERF-002** | [PV-PLN-PERF-002-library-open-responsiveness.md](PV-PLN-PERF-002-library-open-responsiveness.md) | **Library open responsiveness:** cached/stale folder first paint, folder projection metadata-index reuse, linear saved game-index merge, and async refresh close/session guards. |
| **PV-PLN-GPRO-001** | [PV-PLN-GPRO-001-game-profile-dashboard.md](PV-PLN-GPRO-001-game-profile-dashboard.md) | **Game Profile dashboard:** turn the new "Open Game Profile" window into a per-game dashboard â€” hero action cluster (Favorite/Showcase/100% + Edit Game / Open Folders / Change Art), summary line, Game Notes, **Sessions** section reusing `librarySessionThresholdMinutes`, achievements completion %, external-ID pills, Recent Captures with chevron scroll. |
| **PV-PLN-ACHG-001** | *Complete â€”* [PV-PLN-ACHG-001-achievement-guide-library.md](PV-PLN-ACHG-001-achievement-guide-library.md) (**Done**) | **Achievement Guide Library:** durable provider achievement identities, locally authored per-achievement guides with source attribution, a compact Guide editor, and validated JSON bundle imports for sourced summaries. |
| **PV-PLN-ACHG-002** | *Active â€” Aâ€“E implemented; F verification* [PV-PLN-ACHG-002-achievement-collection-experience.md](PV-PLN-ACHG-002-achievement-collection-experience.md) | **Achievement Collection Experience:** Access, read-first guides, Next Up pins, statistics/history, and sorting implemented; compact pins and collection styling/unlock feedback implemented; broader manual QA remains. |
| **PV-PLN-ACHG-003** | *Active â€” Steps 1â€“4 implemented; awaiting user review* [PV-PLN-ACHG-003-achievement-hub.md](PV-PLN-ACHG-003-achievement-hub.md) | **Achievement Hub:** main-toolbar entry and hub shell; then recent games/progress, earned timeline, and a library-wide searchable/sortable achievement collection, one step at a time. |

### Plan families (merge by theme, not by file)

These are **documentation umbrellas**â€”each plan file stays canonical for its ID; cross-link when work overlaps.

| Theme | Plans |
|-------|--------|
| **MainWindow / services / iOS-aligned seams** | **PV-PLN-UI-001** (active), **PV-PLN-EXT-002** (complete), **`docs/NEXT_TRIM_PLAN.md`**, **`pixelvault_service_split_plan.txt`** |
| **Pre-1.0 polish + shipping** | **PV-PLN-V1POL-001**, **PV-PLN-DIST-001** |
| **Intake, filenames, trust, placement** | **PV-PLN-FNRU-001**, **PV-PLN-AINT-001** (complete), **PV-PLN-LIBST-001** (complete) |
| **Performance / responsiveness** | **PV-PLN-PERF-001**, **PV-PLN-PERF-002**, **`docs/LIBRARY_PERFORMANCE_PLAN.md`**, **`docs/PERFORMANCE_TODO.md`** |
| **Library UX surfaces** | **PV-PLN-LIBPV-001** (capture viewer), **PV-PLN-GPRO-001** (game profile dashboard), **PV-PLN-ACHG-001** (achievement guide library, complete), **PV-PLN-ACHG-002** (achievement collection experience, active), **PV-PLN-ACHG-003** (achievement hub, active), **PV-PLN-RVW-001** (post-app-review hardening, complete) |

**ID format:** `PV-PLN-<TOPIC>-<NNN>`  
- **PV** â€” PixelVault  
- **PLN** â€” plan (initiative / program)  
- **TOPIC** â€” short mnemonic (here `V1POL` = V1 polish)  
- **NNN** â€” zero-padded serial per topic  

Lightweight **in-flight** stubs may appear under [`open/README.md`](open/README.md) when link stability needs a dedicated `open/` path; prefer a single redirect under `docs/plans/` once work is closed.

Add new rows here when creating additional codified plans.
