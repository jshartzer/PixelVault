# Simple guide: shipping a PixelVault build (no jargon)

This is the **human** version of **`PV-PLN-DIST-001`**. Use it when the technical roadmap feels confusing.

---

## What you’re trying to do

You want a **folder (or installer)** that contains PixelVault plus the right license files, so you can:

1. **Try it yourself** — double‑click **`PixelVault.exe`** and make sure nothing obvious broke.  
2. **Try it like a stranger** — run it on **another Windows PC** (or a **virtual machine**, which is just “a fake clean PC inside your computer”). That catches problems your dev machine hides.  
3. **Later** — put the app on a download page, Microsoft Store, etc. Legal/privacy web pages can wait until you’re happy with (1) and (2).

---

## Words that kept showing up — plain meanings

| Term | What it really means |
|------|----------------------|
| **Publish** | Run a script that **builds** the app into an output folder under **`dist\`** — a copy you could zip or install. |
| **`dist\Velopack\publish-…`** | The **self‑contained** build folder (includes .NET so users don’t install it separately). Good “like a real install” test. |
| **`Verify-DistributionLayout.ps1`** | A **sanity check**: “Is **`PixelVault.exe`** there? Are the **license text files** in **`tools\licenses\`**?” It does *not* test whether the app *works* — only that the folder isn’t obviously wrong. |
| **VM (virtual machine)** | A **second, clean Windows** for testing — so you’re not fooled by settings and tools on your dev PC. Optional but recommended before a wide release. |
| **Signing / Authenticode** | Optional step so Windows shows **your name** instead of “unknown publisher.” Needs a certificate; do when you’re ready — not required for your first “does it run?” test. |
| **Velopack / `vpk`** | Extra tooling that turns the publish folder into an **installer** (`Setup`‑style). You can skip this at first and just test the **`publish-…`** folder. |

---

## Easiest path: one command from the repo

**Important:** PowerShell resolves `.\scripts\…` from **whatever folder your terminal is in**, not from “the project.” If you open PowerShell and you see something like `PS C:\WINDOWS\system32>`, you must either **change directory first** or pass the **full path** to the script.

From **`C:\Codex`** (your repo root):

```powershell
cd C:\Codex
pwsh -File .\scripts\Run-LocalReleaseChecks.ps1
```

Or from **any** folder (no `cd` needed):

```powershell
pwsh -File C:\Codex\scripts\Run-LocalReleaseChecks.ps1
```

That script will:

1. **Publish** a Velopack-style build (by default **without** building an installer — fewer tools required).  
2. **Check** the output folder automatically.  
3. **Print** what to do next in normal language.

If **`dist\Velopack\publish-<version>`** already exists from a previous run, the script **replaces it** automatically (same version number). Direct use of **`Publish-Velopack.ps1`** still asks for **`-Force`** until you delete that folder — that is a safety rule for manual runs only.

**Full installer + update packages** (needs `vpk` and ASP.NET 8 runtime — see **`docs/VELOPACK.md`**):

```powershell
pwsh -File .\scripts\Run-LocalReleaseChecks.ps1 -MakeInstaller
```

**Only check** a folder you already built:

```powershell
pwsh -File .\scripts\Run-LocalReleaseChecks.ps1 -OnlyVerify "C:\Codex\dist\Velopack\publish-0.076.000"
```

---

## What you still do by hand (can’t be automated here)

| Step | Why |
|------|-----|
| **Double‑click `PixelVault.exe`** in the publish folder on your machine | Confirms basic “it runs.” |
| **Copy the folder to another PC or a VM** and run it there | Confirms “a new user” experience. Follow **`docs/VELOPACK_VM_SPIKE_CHECKLIST.md`** if you use the full installer later. |
| **Walk through **`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`** on a test build | Catches library/import bugs. |
| **Sign the exe / installer** | Optional; **`docs/PUBLISH_SIGNING.md`** when you want it. |
| **Privacy / EULA on a website** | **`PV-PLN-DIST-001` §10.1** — intentionally **last** for this project. |

---

## If something fails

- **`Run-LocalReleaseChecks.ps1`** prints the error in English where possible.  
- **Missing `vpk` / ASP.NET 8:** use the default command **without** `-MakeInstaller`, or install the tooling described in **`docs/VELOPACK.md`**.  
- **Deeper detail:** **`docs/VELOPACK.md`**, **`docs/HANDOFF.md`**, **`docs/plans/PV-PLN-DIST-001-windows-store-and-distribution-roadmap.md`**.
