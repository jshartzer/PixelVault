# PixelVault planning documents (codified)

Long-lived execution plans that use a **stable plan ID** for cross-linking (repo, Notion, commits).

| Plan ID | Document | Summary |
|---------|----------|---------|
| **PV-PLN-V1POL-001** | [PV-PLN-V1POL-001-pre-v1-polish-program.md](PV-PLN-V1POL-001-pre-v1-polish-program.md) | Pre-V1 product polish: tokens, loading/empty states, inline feedback, command palette, smart views, health surface, staged drawers. **Notion:** [page](https://www.notion.so/33a73adc59b6819d8ddcc20b9f03b2d6). |
| **PV-PLN-LIBWS-001** | *Archived —* [PV-PLN-LIBWS-001-library-workspace-modes.md](../archive/PV-PLN-LIBWS-001-library-workspace-modes.md) (**Done**) | Library **workspace modes** (shipped): Folder (default), Photo (focused captures), **Timeline** as distinct mode; shell, density, rail, hero. |
| **PV-PLN-LIBST-001** | *Completed —* [PV-PLN-LIBST-001-single-folder-storage-model.md](../completed-projects/PV-PLN-LIBST-001-single-folder-storage-model.md) (**Done**) | Single-folder **storage model** per game while preserving **one Game Index row per console**; remove folder-structure inference and introduce explicit storage-group / placement ownership. |
| **PV-PLN-AINT-001** | [PV-PLN-AINT-001-background-intake-agent.md](PV-PLN-AINT-001-background-intake-agent.md) | Opt-in **Background Intake Agent**: **custom** trusted rules + **built-in** only when full auto-import completes; **modeless** activity/undo; pause list refined in implementation; prerequisites vs FNRU/UI-001/LIBST in-plan. |
| **PV-PLN-UI-001** | [PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md](PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md) | **Post–A–F** MainWindow / `PixelVault.Native.cs` thin-out; extraction stages aligned with **`docs/ios_foundation_guide.md`** (services, plain models, mobile-safe writes). |
| **PV-PLN-FNRU-001** | [PV-PLN-FNRU-001-guided-builder-verification.md](PV-PLN-FNRU-001-guided-builder-verification.md) | **Guided Builder**: verification (Tests A–G) + **strategy** (save rule = format memory; optional sample→rule; parser ordering). Handoff: **`docs/FILENAME_RULES_GUIDED_BUILDER_TEST_HANDOFF.md`**. |

**ID format:** `PV-PLN-<TOPIC>-<NNN>`  
- **PV** — PixelVault  
- **PLN** — plan (initiative / program)  
- **TOPIC** — short mnemonic (here `V1POL` = V1 polish)  
- **NNN** — zero-padded serial per topic  

Add new rows here when creating additional codified plans.
