# PixelVault — distribution storage contract

**Purpose:** Describe where mutable app state lives today and how **`PV-PLN-DIST-001` §5.8** (install‑root hardening) is satisfied. Re‑check after changing **`PersistentDataMigrator`** or startup layout.

**Authoritative implementation:** `src/PixelVault.Native/Infrastructure/PersistentDataMigrator.cs` — **`ResolvePersistentDataRoot`**.

---

## Layout under the data root

Regardless of which root is chosen, **`MainWindow.ComputePersistentStorageLayout`** builds:

| Relative path | Role |
|-----------------|------|
| **`PixelVault.settings.ini`** | Settings |
| **`cache/`** | SQLite indexes, folder caches, derived data |
| **`cache/covers/`**, **`cache/thumbs/`** | Cover/thumbnail caches |
| **`logs/`** | Logs |
| **`saved-covers/`** | User‑managed custom covers + README |
| **`cache/last-import.tsv`** | Undo manifest path |

**Read‑only beside the EXE:** `CHANGELOG.md` is still loaded from **`appRoot`** (install folder), not from the data root.

---

## How the data root is resolved (probe order)

1. **`dist/PixelVault-<M.AAA.BBB>`** or **`dist/PixelVault-current`**  
   → **`<parent of dist>/PixelVaultData`** (shared across published version folders — dev/publish workflow).

2. **Dev checkout** — walk parents until a folder contains both **`PixelVaultData/`** and **`src/PixelVault.Native/`**  
   → that **`PixelVaultData`** path.

3. **Restricted install directory** — EXE path is under **`Program Files`**, **`Program Files (x86)`**, or contains **`\\WindowsApps\\`** (typical packaged desktop / Store‑style layout)  
   → **`%LocalAppData%\PixelVault`**  
   so settings and caches are **not** written next to a non‑writable install.

4. **Fallback** — **`AppDomain.CurrentDomain.BaseDirectory`** (same as today for portable installs, arbitrary folders, zip extracts outside Program Files).

---

## Migration (`MigrateFromLegacyVersions`)

When **`dataRoot`** ≠ **`appRoot`**, first‑run behavior copies **settings / cache / logs** from the app folder (and older **`dist/PixelVault-*`** siblings) into the authoritative data root **only when destination files are missing** (or settings: newer‑wins rules per migrator). Shared **`PixelVaultData`** never gets rolled back by older release folders.

---

## Operational notes

- **Portable / zip:** fallback keeps **state beside the EXE** (writable folder) — unchanged.
- **Installed under Program Files:** state moves to **`%LocalAppData%\PixelVault`** without requiring admin at runtime.
- **MSIX:** **`WindowsApps`** detection routes to the same LocalAppData tree; future work may align with package **LocalState** if you want strictly package‑scoped data — revisit when **`Package.appxmanifest`** exists.

---

## Revision

| Date | Change |
|------|--------|
| **2026‑04‑18** | Initial doc; **`ResolvePersistentDataRoot`** gains LocalAppData branch for Program Files / WindowsApps (**`PV-PLN-DIST-001` §5.8**). |
