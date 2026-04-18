# Velopack — installer & updates (**PV-PLN-DIST-001** §5.3)

PixelVault integrates **[Velopack](https://docs.velopack.io/)** for:

- **`VelopackApp`** bootstrap at startup (handles installs/updates managed by Velopack; **no-op** when you run loose **`dotnet run`** / unzip **`dist/`** builds).
- **`vpk pack`** producing a Windows setup + delta-update payloads when you are ready to host a feed.

**NuGet:** package **`Velopack`** version must stay aligned with the **`vpk`** CLI tool version ([docs](https://docs.velopack.io/getting-started/csharp)).

**Phase 1 order:** run **VM / golden-path** QA and **signing** on installer builds first; **host** privacy + EULA at stable HTTPS **last** before a public or Store push unless counsel needs it earlier — **`PV-PLN-DIST-001` §10.1**.

**Local pre-check (before copying bits to a VM):**

```powershell
pwsh -File C:\Codex\scripts\Verify-DistributionLayout.ps1 -Path C:\Codex\dist\Velopack\publish-<AppVersion>
```

Fails fast if **`PixelVault.exe`** or merged **`tools\licenses\`** are missing after publish.

---

## Prerequisites

1. [.NET SDK](https://dotnet.microsoft.com/download) (already required to build PixelVault).
2. **`vpk`** is a .NET tool that may require the **ASP.NET Core 8.x** runtime (`Microsoft.AspNetCore.App`) on the machine — install via Visual Studio / [.NET download](https://dotnet.microsoft.com/download/dotnet/8.0) if `vpk` fails to start.
3. Install the CLI (match **`Velopack`** package version in **`PixelVault.Native.csproj`**):

```powershell
dotnet tool install -g vpk --version 0.0.942
```

Upgrade later with:

```powershell
dotnet tool update -g vpk --version <same-as-nuget>
```

---

## One-shot publish + pack

From repo root:

```powershell
pwsh -File C:\Codex\scripts\Publish-Velopack.ps1
```

Optional: `-Version 0.076.000`, `-SkipVpk` (publish folder only), `-Force` overwrite output.

Outputs under **`dist\Velopack\<semver>\`** (Velopack **`packVersion`** is normalized SemVer from **`AppVersion`**, e.g. **`0.076.000`** → **`0.76.0`**) — upload the generated release assets to HTTPS static hosting (or GitHub Releases) and point users at the setup, or wire **`UpdateManager`** (below).

This script publishes **self-contained** **`win-x64`** with **`PublishSingleFile=false`** for that output only (better fit for **`vpk`** delta updates than a single mega-exe). It copies optional repo **`tools\`** next to **`PixelVault.exe`** and merges **`tools-licenses\`** → **`tools\licenses\`** (**§5.9**).

### Phase 1 decision — upgrade in place vs side‑by‑side

**Default:** **upgrade in place** — Velopack installs into a stable per‑app folder and replaces binaries on update (not a parallel **`dist/PixelVault-<version>/`** tree). User libraries and **`PixelVaultData`** remain valid because mutable state is resolved via **`PersistentDataMigrator`** / **`docs/DISTRIBUTION_STORAGE.md`** (not next to the EXE under **`Program Files`**).

**Manual QA still required:** §5.3 checklist — use **`docs/VELOPACK_VM_SPIKE_CHECKLIST.md`** for clean VM install/uninstall and **N → N+1** update with settings preserved.

---

## Feed URL & in-app updates (optional, not wired by default)

To check for updates in code (user confirmation recommended before restart):

```csharp
var mgr = new UpdateManager("https://your.host/updates");
var newVersion = await mgr.CheckForUpdatesAsync();
```

Use a **stable URL** that serves the **`releases.json`** (or equivalent) Velopack generates in the pack output. **Environment variable** for a future hook: **`PIXELVAULT_UPDATE_FEED_URL`** (reserved; not read by the shell yet).

---

## Coexistence with **`Publish-PixelVault.ps1`**

| Script | Use when |
|--------|-----------|
| **`scripts/Publish-PixelVault.ps1`** | Daily dev **`dist/PixelVault-*`** layout, single-file, optional **`-Sign`**, repo-style **`PixelVaultData`**. |
| **`scripts/Publish-Velopack.ps1`** | Shipping an **installer**, **delta updates**, or Microsoft Store prep that expects a Velopack release layout. |

**Signing:** **`docs/PUBLISH_SIGNING.md`** — **`Publish-Velopack.ps1`** **`-SignParams`** or **`$env:VPK_SIGN_PARAMS`** → **`vpk pack -n`**; **`Publish-PixelVault.ps1`** **`-Sign`** for **`dist`**.

---

## Revision

| Date | Note |
|------|------|
| **2026-04-18** | Initial Velopack bootstrap **`Main`**, **`Publish-Velopack.ps1`**, **`Velopack` 0.0.942. |
| **2026-04-18** | **`vpk -v`** uses normalized SemVer from **`AppVersion`** (see script **`ConvertTo-VpkSemVer`**). |
| **2026-04-18** | Publish copies **`tools\`** + merges **`tools-licenses\`** → **`tools\licenses\`** (**§5.9**). |
