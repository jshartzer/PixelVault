# PV-PLN-DIST-001 — Windows distribution, Microsoft Store & long‑term platform arc

| Field | Value |
|-------|-------|
| **Plan ID** | `PV-PLN-DIST-001` |
| **Status** | **Active roadmap** — strategic sequencing: ship **PixelVault 1.0** (signed, installable, updatable), then **Microsoft Store** (Desktop Bridge MSIX), then defer **iOS/backend** until desktop distribution is stable. |
| **Owner** | PixelVault / Codex |
| **Audience** | Occasional dip‑in reference for “what’s next” on shipping, signing, packaging, certification, and the longer platform story. |

**Companion docs (keep these in sync when milestones ship):**

- Product polish before calling it **1.0**: **`docs/plans/PV-PLN-V1POL-001-pre-v1-polish-program.md`**
- Engineering quality baseline: **`docs/APP_REVIEW_2026-04-12.md`**, **`docs/CODE_QUALITY_IMPROVEMENT_PLAN.md`**
- Current build pointer: **`docs/CURRENT_BUILD.txt`**, **`docs/HANDOFF.md`**, **`docs/CHANGELOG.md`**
- iOS / backend direction (Phase 3 only): **`docs/ios_foundation_guide.md`**
- MainWindow / service seams (ongoing): **`docs/plans/PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md`**

---

## 1. Purpose

### 1.1 What this plan is

A **single place** to answer:

- Where is PixelVault today on **real‑world distribution** (not just “it builds”)?
- What **ordered steps** move you toward **1.0 off‑store**, then **Microsoft Store**, without throwaway work?
- What **legal, packaging, and architectural** risks apply so you can revisit them months later without re‑deriving context?

### 1.2 What this plan is not

- Not a replacement for **`CHANGELOG.md`** or **`HANDOFF.md`** (those stay the day‑to‑day truth).
- Not a commitment to ship every optional channel (WinGet, Store, iOS); treat optional rows as forks you can skip or postpone.

---

## 2. Ground truth — product & tech today

Use this section when you reopen the doc after months away.

### 2.1 Product identity

- **PixelVault** is a **native Windows desktop** app for organizing, tagging, indexing, and browsing game screenshots and clips (`README.md`).
- Primary workflows: **intake → metadata/index → Library browse**; heavy tooling integration (**ExifTool**, **FFmpeg**, optional **SteamGridDB**, Steam APIs).

### 2.2 Platform & packaging facts (authoritative in repo)

From **`src/PixelVault.Native/PixelVault.Native.csproj`**:

| Setting | Typical value | Implication |
|--------|----------------|---------------|
| **TargetFramework** | `net8.0-windows` | Windows‑only. |
| **UseWPF / UseWindowsForms** | `true` | Desktop Bridge / MSIX path is WPF + WinForms eligible; **not** portable to macOS/iOS from this codebase. |
| **RuntimeIdentifier** | `win-x64` | **x64 only** unless you add another RID. |
| **PublishSingleFile** | `true` | Single EXE publish layout (good for bundling). |
| **SelfContained** | `false` (today) | End users may need **.NET 8 runtime** unless you flip self‑contained for distribution. |

Published output pattern: **`dist/PixelVault-<version>/`** via **`scripts/Publish-PixelVault.ps1`** (`README.md`, **`docs/CURRENT_BUILD.txt`**).

### 2.3 Versioning posture

- Train today is **`0.076.xxx`** / similar (`docs/CURRENT_BUILD.txt`) — explicitly **pre‑1.0** polish runway (`PV-PLN-V1POL-001`).
- Call **1.0** only when Phase 1 exit criteria below are met (polish + distribution + blocking risks).

---

## 3. Readiness scorecard (dip‑in snapshot)

Rough **%** are directional — update this table when you ship milestones.

| Milestone | Readiness | Notes |
|-----------|-----------|--------|
| **Website / GitHub release** (signed installer, auto‑update) | **~55–65%** → raise after Phase 1 | Nothing blocks technically except execution: signing, installer, policy pages. |
| **WinGet** (community manifest) | **~40–50%** after Phase 1 | Needs **signed** installer artifact + manifest PR to `microsoft/winget-pkgs`. |
| **Microsoft Store** (Desktop Bridge **runFullTrust** MSIX) | **~25–35%** → raise after Phase 1–2 prep | Packaging + Partner Center + listing + policy; **not** a small checkbox. |
| **Microsoft Store** (strict sandbox without full trust) | **Low** | Conflicts with arbitrary library roots, bundled **`tools/`** exes, and current filesystem model — **not recommended** without major redesign. |
| **Apple platforms** | **~2–5%** | No iOS/macOS codebase in repo; **`ios_foundation_guide.md`** describes a **future** separate client + backend. |

---

## 4. Strategic sequencing — why three phases

**Nothing in Phase 2 is wasted if you do Phase 1 correctly.** Signing, privacy text, self‑contained/trusted packaging decisions, and installer/updater plumbing are **prerequisites** for Store submission.

| Phase | Name | Outcome |
|-------|------|--------|
| **Phase 1** | **Ship PixelVault 1.0** (off‑store, real users) | Signed binary, installer, updater, hosted policies, golden‑path QA. |
| **Phase 2** | **Microsoft Store** | Partner Center + **MSIX** + certification + listing assets. |
| **Phase 3** | **iOS + backend** (optional, long arc) | Separate client; **backend/service** as source of truth per **`ios_foundation_guide.md`**. |

---

## 5. Phase 1 — Ship 1.0 (distribution + product bar)

**Exit criteria (all should be true before you burn a Store submission):**

- [ ] **Authenticode‑signed** release binaries (users do not fight SmartScreen “unknown publisher” on every launch).
- [ ] **Installer** (not only “unzip folder”) OR a documented **silent install** story your audience accepts.
- [ ] **Auto‑update** or an equally clear **manual update** contract (changelog + in‑app “new version” notice).
- [ ] **Self‑contained** publish **or** documented prerequisite that Store/desktop bundle will satisfy (see §5.5).
- [ ] Hosted **`Privacy Policy`** + **`EULA`** URLs (even if traffic is tiny — Store **requires** privacy URL).
- [ ] **`PV-PLN-V1POL-001`** slices **G / H / J** brought to “good enough for 1.0” per that plan’s definition (quick‑edit depth, a11y/motion spot‑check, consistency sweep).
- [ ] **Risk closure:** filename‑rule **regex safety** (see §7.1) treated as **V1‑blocking** unless explicitly waived with documented risk acceptance.

### 5.1 Engineering checklist — blocking risks

Use **`docs/APP_REVIEW_2026-04-12.md`** as the audit trail.

- [ ] **P1 — User‑authored regex execution** (`FilenameParserService`, `FilenameRulesService` — paths cited in that review): enforce save‑time limits, **`RegexOptions.NonBacktracking`** where compatible, execution timeouts, tests for pathological patterns.
- [ ] **P2 — Hero/banner duplicate work** (`MainWindow.LibraryBrowserPhotoHero.cs`, `CoverService` — cited in review): dedupe in‑flight downloads / cancel stale selection work where practical.

**Optional but strongly recommended before wide release:**

- [ ] Local **crash breadcrumb** or dump writer (sanitize paths; write under app data; user can attach to reports).

### 5.2 Distribution checklist — signing

**Steps you take outside the repo:**

- [ ] Purchase an **Authenticode** code‑signing certificate (**OV** typical; **EV** improves SmartScreen reputation timelines — research current CA pricing).
- [ ] Secure the private key per CA guidance (hardware token / HSM where required).

**Steps in repo / automation:**

- [ ] Add **`signtool sign`** (SHA256 + trusted timestamp server) to **`scripts/Publish-PixelVault.ps1`** post‑publish, parameterized by thumbprint or PFX path via environment/secret store **not** committed to git.
- [ ] Document the one‑machine publish ritual in **`docs/HANDOFF.md`** or a short **`docs/PUBLISH_SIGNING.md`** if you want a dedicated page.

### 5.3 Distribution checklist — installer & updates

Pick **one** primary path (others become optional):

| Option | Pros | Cons |
|--------|------|------|
| **Velopack** (or similar) | Installer + delta updates; active ecosystem | New dependency to learn |
| **Squirrel.Windows** | Familiar pattern | Less momentum vs newer stacks |
| **MSIX only** | Same artifact as Store later | Heavier lift if you’re not ready for manifest/capabilities yet |

**Checklist:**

- [ ] Spike: install + launch + uninstall on a clean VM.
- [ ] Spike: update from **N → N+1** without losing settings under **`PixelVaultData`**.
- [ ] Decide default: **upgrade in place** vs side‑by‑side version folders (today’s **`dist`** pattern is dev‑oriented).

### 5.4 Distribution checklist — legal & user‑facing pages

Even with no telemetry, you still owe users clarity.

- [ ] **Privacy policy** hosted at a stable HTTPS URL — describe: local files, SQLite caches, optional network calls (Steam, SteamGridDB, etc.), **no analytics** if true.
- [ ] **EULA** or Terms — especially if you distribute beyond personal friends.
- [ ] **Support contact** — email or issue tracker linked from Store/listing later.

### 5.5 Self‑contained vs framework‑dependent

Today: **`SelfContained=false`** → users need **.NET 8**.

**For Phase 1 release:**

- [ ] Prefer **`SelfContained=true`** + trimming/R2R defaults appropriate for WPF (validate app still runs; WPF trimming can be finicky — test thoroughly).
- [ ] Document final choice in **`CHANGELOG.md`** (“requires .NET 8” vs “bundled runtime”).

**For Store:** plan on either self‑contained MSIX **or** declaring framework dependency via Store — decide explicitly; don’t leave it accidental.

### 5.6 QA — don’t skip manual golden path

Automated tests are strong; distribution changes need smoke manual QA.

- [ ] Run **`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`** on a release candidate build.
- [ ] After signing: test on **non‑dev** Windows (fresh profile, SmartScreen path).

### 5.7 Version number & release comms

- [ ] Bump to **`1.0.0`** per **`docs/VERSIONING.md`** when Phase 1 exit criteria are met (not merely calendar‑driven).
- [ ] **`CHANGELOG.md`** first‑class **1.0** section; **`docs/CURRENT_BUILD.txt`** updated by publish ritual (`POLICY.md`).

---

## 6. Phase 2 — Microsoft Store (Desktop Bridge)

**Goal:** Submit a **packaged desktop** MSIX that matches how PixelVault actually works: **full trust**, arbitrary library roots, bundled **`tools/`** (ExifTool, FFmpeg).

### 6.1 Choose the packaging model (decision)

**Recommended:** **Desktop Bridge / packaged desktop with `runFullTrust`**

- Preserves existing filesystem assumptions (`README.md` data layout under **`PixelVaultData`**, user‑chosen drives).
- Avoids rewriting the app into a sandbox‑compatible storage model.

**Not recommended without a redesign:** sandbox‑only MSIX **without** broad file access — conflicts with “your library lives where you put it.”

Document the signed decision here when made:

| Decision | Choice | Date | Notes |
|----------|--------|------|-------|
| MSIX model | e.g. **runFullTrust Desktop Bridge** | | |
| Self‑contained | yes / no | | |

### 6.2 Partner Center & identity

**Steps outside repo:**

- [ ] Enroll in **Microsoft Partner Center** (individual or company — fee applies for MS Store publishing).
- [ ] Reserve **app name**, **Package/Identity/Name**, publisher display name aligned with signing certificate.

### 6.3 Packaging artifacts

Today there is **no** `Package.appxmanifest` in repo (verify with repo search when you start).

- [ ] Add Windows Application Packaging project **or** enable MSIX packaging in SDK‑style pipeline (`GenerateAppxPackageOnBuild`, etc.) — pick one approach and document it in **`scripts/`**.
- [ ] Author **`Package.appxmanifest`**: identity, logos, capabilities, **`runFullTrust`** as required.
- [ ] Map **artifact outputs** from existing **`Publish-PixelVault.ps1`** into the MSIX layout.
- [ ] Validate **startup task** / tray behavior if applicable — may need manifest extensions.

### 6.4 Store listing & certification prep

- [ ] **Screenshots** (required resolutions per Partner Center current guidelines).
- [ ] **Short / long description**, keywords, **what’s new**.
- [ ] **Privacy policy URL** (Phase 1).
- [ ] **Age rating** questionnaire completed honestly.
- [ ] **Trademark / asset review** — see §7.2 (logo risk is a common certification failure).

### 6.5 Optional: WinGet

After Phase 1 produces a **signed installer URL** you control:

- [ ] Author **`winget` manifest** and submit PR to **`microsoft/winget-pkgs`** (process changes — follow current Microsoft docs).

---

## 7. Risk register (revisit each milestone)

### 7.1 Regex DoS / stall (V1‑blocking engineering risk)

**Source:** **`docs/APP_REVIEW_2026-04-12.md`** — user‑authored patterns must not stall imports/scans/editors.

**Action:** implement bounded, testable regex execution (see §5.1).

### 7.2 Third‑party logos & trademarks (Store‑blocking legal/design risk)

**Assets to audit before Store submission** (filenames under **`assets/`**):

- `Steam Library Icon.png`
- `PS5 Library Logo.png`
- `Xbox Library Logo.png`
- `Nintendo Library Icon.png`
- (and related PC/Xbox PC/emulator icons)

**Why:** nominative text labels (“Steam”, “PlayStation”) for categories are usually fine; **logo‑like artwork** may violate platform brand guidelines or fail Store certification if it appears to impersonate first‑party branding.

**Action:**

- [ ] Visual review: generic/icon‑style vs official marks.
- [ ] Replace or license as needed; update **`CHANGELOG.md`** when assets change.

### 7.3 External tools & paths

Bundled **`tools/`** exes are consistent with **full‑trust** desktop bridge; **not** with strict sandbox.

**Action:** keep **`runFullTrust`** decision explicit (§6.1).

### 7.4 Network & keys

Optional **SteamGridDB** token and web APIs — disclose in privacy policy; no secret in repo.

---

## 8. Phase 3 — iOS & backend (long arc; after desktop is stable)

**Per `docs/ios_foundation_guide.md`:**

- **Desktop** remains the management hub (import, heavy metadata, covers, filesystem).
- **Future iOS** is a **separate** client; **backend/service** owns mobile‑safe data and queries.
- Refactors like **`PV-PLN-UI-001`** (plain models, host interfaces) **reduce future pain** but **do not** ship an iOS binary.

**Phase 3 checklist (only when Phase 1–2 are de‑risked):**

- [ ] Define **API contracts** (REST or similar): library query, thumbnails, detail, star/comment writes.
- [ ] Implement **self‑hosted service** that reads existing indexes / serves media safely.
- [ ] Only then: **new Apple project** (Swift/SwiftUI), provisioning, TestFlight, App Store Connect.

**Do not** block Phase 1–2 on Phase 3.

---

## 9. Ongoing work that supports all phases (low intensity)

- [ ] Continue **`PV-PLN-UI-001`** extractions where touch‑points appear — keeps logic **testable** and **backend‑shaped** without requiring a big‑bang rewrite.
- [ ] Keep **`docs/PERFORMANCE_TODO.md`** and **`docs/ROADMAP.md`** aligned when responsiveness items affect release confidence.

---

## 10. Suggested default order (when you don’t know where to start)

When you open this doc cold:

1. **Scan §3** (scorecard) and §7 (risks).
2. If **Phase 1** not done: work **§5** top‑down — especially **§5.1** (regex) and **§5.2** (signing).
3. If **Phase 1** is done: start **§6** Partner Center + MSIX spike.
4. **Ignore §8** until desktop distribution feels boring.

---

## 11. Revision history

| Date | Change |
|------|--------|
| **2026‑04‑18** | Initial roadmap: phased Windows 1.0 → Microsoft Store → optional iOS/backend; checklists, risks, scorecard (`PV-PLN-DIST-001`). |
