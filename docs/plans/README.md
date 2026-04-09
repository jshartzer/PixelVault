# PixelVault planning documents (codified)

Long-lived execution plans that use a **stable plan ID** for cross-linking (repo, Notion, commits).

| Plan ID | Document | Summary |
|---------|----------|---------|
| **PV-PLN-V1POL-001** | [PV-PLN-V1POL-001-pre-v1-polish-program.md](PV-PLN-V1POL-001-pre-v1-polish-program.md) | Pre-V1 product polish: tokens, loading/empty states, inline feedback, command palette, smart views, health surface, staged drawers. **Notion:** [page](https://www.notion.so/33a73adc59b6819d8ddcc20b9f03b2d6). |
| **PV-PLN-LIBWS-001** | *Archived —* [PV-PLN-LIBWS-001-library-workspace-modes.md](../archive/PV-PLN-LIBWS-001-library-workspace-modes.md) (**Done**) | Library **workspace modes** (shipped): Folder (default), Photo (focused captures), **Timeline** as distinct mode; shell, density, rail, hero. |
| **PV-PLN-LIBST-001** | [PV-PLN-LIBST-001-single-folder-storage-model.md](PV-PLN-LIBST-001-single-folder-storage-model.md) | Single-folder **storage model** per game while preserving **one Game Index row per console**; remove folder-structure inference and introduce explicit storage-group / placement ownership. |
| **PV-PLN-UI-001** | [PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md](PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md) | **Post–A–F** MainWindow / `PixelVault.Native.cs` thin-out; extraction stages aligned with **`docs/ios_foundation_guide.md`** (services, plain models, mobile-safe writes). |

**ID format:** `PV-PLN-<TOPIC>-<NNN>`  
- **PV** — PixelVault  
- **PLN** — plan (initiative / program)  
- **TOPIC** — short mnemonic (here `V1POL` = V1 polish)  
- **NNN** — zero-padded serial per topic  

Add new rows here when creating additional codified plans.
