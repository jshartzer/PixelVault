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
- Writable‑data contract (**§5.8**): **`docs/DISTRIBUTION_STORAGE.md`**

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

### 1.3 What would actually block Store approval? (blunt reality check)

The Microsoft Store step is **not** mostly paperwork. Treat these as **high‑risk / certification‑sensitive** until explicitly cleared:

| Area | Why it bites |
|------|----------------|
| **Bundled external tools** (**ExifTool**, **FFmpeg**) | Execution + redistribution terms + packaged‑app launch behavior. |
| **Full‑trust / Desktop Bridge** | Recommended for PixelVault, but **verification work** (data paths, tools, tray) — not “drop in a manifest.” |
| **Trademark / logo / branding assets** | Certification and **wider distribution** risk if artwork tracks first‑party marks too closely (§7.2). |
| **Privacy policy + listing requirements** | Store **requires** a privacy URL; disclosures must match real behavior. |
| **MSIX packaging + manifest** | Identity, capabilities, **`runFullTrust`**, optional startup/tray extensions — greenfield in this repo today. |
| **Install / update / uninstall** | User data preservation, upgrade identity, no corrupt half‑states. |
| **Launch‑time & writable‑data assumptions** | Repo/dist‑style layout vs installed‑app layout; **`PixelVaultData`** / cache roots must be intentional (§5.8, §7.5). |

If someone skims only the scorecard: **Store ≈ packaging + behavior + policy + certification**, not a checkbox after “the app feels done.”

---

## 2. Ground truth — product & tech today

Use this section when you reopen the doc after months away.

**Snapshot discipline:** Facts below are **authoritative only when refreshed**. After any long gap, re‑read **`src/PixelVault.Native/PixelVault.Native.csproj`**, **`docs/CURRENT_BUILD.txt`**, and **`scripts/Publish-PixelVault.ps1`** — version examples in prose **will drift**.

### 2.1 Product identity

- **PixelVault** is a **native Windows desktop** app for organizing, tagging, indexing, and browsing game screenshots and clips (`README.md`).
- Primary workflows: **intake → metadata/index → Library browse**; heavy tooling integration (**ExifTool**, **FFmpeg**, optional **SteamGridDB**, Steam APIs).
- **Persistent data:** a **`PixelVaultData`** / cache concept already exists (`README.md`) — a strength for installers and updates **once** the authoritative writable root is nailed down (**§5.8**).

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

- **Authoritative train / build path:** **`docs/CURRENT_BUILD.txt`** (and **`docs/CHANGELOG.md`**) — do not rely on example version strings embedded in this plan.
- PixelVault remains on a **pre‑1.0** polish runway until Phase 1 exit criteria are met (`PV-PLN-V1POL-001`).
- Call **1.0** only when Phase 1 exit criteria below are met (polish + distribution + blocking risks).

---

## 3. Readiness scorecard (dip‑in snapshot)

Rough **%** are directional — update this table when you ship milestones.

| Milestone | Readiness | Notes |
|-----------|-----------|--------|
| **Website / GitHub release** (signed installer, auto‑update) | **~55–65%** → raise after Phase 1 | Nothing blocks technically except execution: signing, installer, policy pages. |
| **WinGet** (community manifest) | **~40–50%** after Phase 1 | Needs **signed** installer artifact + manifest PR to `microsoft/winget-pkgs`. |
| **Microsoft Store** (Desktop Bridge **runFullTrust** MSIX) | **~25–35%** → raise after Phase 1–2 prep | Treat as **packaging + packaged‑desktop behavior verification + Partner Center + listing + policy** — not “manifest + screenshots only.” |
| **Microsoft Store** (strict sandbox without full trust) | **Low** | Conflicts with arbitrary library roots, bundled **`tools/`** exes, and current filesystem model — **not recommended** without major redesign. |
| **Apple platforms** | **~2–5%** | No iOS/macOS codebase in repo; **`ios_foundation_guide.md`** describes a **future** separate client + backend. |

### 3.1 Technical readiness vs submission readiness

Split the work so engineering is not constantly mixed with Partner Center admin.

| **Technical readiness** | **Submission / listing readiness** |
|-------------------------|-------------------------------------|
| Signing, installer, updater choice | Partner Center account, reserved name, package identity |
| Self‑contained vs framework‑dependent | Screenshots, descriptions, What’s new |
| MSIX / packaging project, manifest, **`runFullTrust`** | Privacy policy URL, support contact |
| Behavior under packaged desktop install | Age rating questionnaire |
| Data paths, writable locations, migration | Branding / trademark review of assets |
| Bundled **ExifTool** / **FFmpeg** execution + **redistribution / notices** (§5.9) | Keywords, store promotional assets |
| Crash logs / diagnostics export (near‑1.0) | Certification passes / fix/resubmit loops |

---

## 4. Strategic sequencing — why three phases

**Nothing in Phase 2 is wasted if you do Phase 1 correctly.** Signing, privacy text, self‑contained/trusted packaging decisions, and installer/updater plumbing are **prerequisites** for Store submission.

| Phase | Name | Outcome |
|-------|------|--------|
| **Phase 1** | **Ship PixelVault 1.0** (off‑store, real users) | Signed binary, installer, updater, hosted policies, golden‑path QA. |
| **Phase 2** | **Microsoft Store** | Partner Center + **MSIX** + certification + listing assets. |
| **Phase 3** | **iOS + backend** (optional, long arc) | Separate client; **backend/service** as source of truth per **`ios_foundation_guide.md`**. |

### 4.1 Gate — do not start Phase 2 (Store packaging) until

Avoid burning time on MSIX while core desktop distribution is still unsettled.

- [ ] **Signed** release artifacts exist end‑to‑end (**`-Sign`** or equivalent ritual documented).
- [ ] **Installer or update path** chosen (not only unzip **`dist/`**).
- [ ] **Self‑contained vs .NET prerequisite** decided and documented (**§5.5**).
- [ ] **Fresh‑machine install** smoke‑tested (VM or clean profile) — not only your dev box.
- [ ] **Persistent / writable data** contract explicit — installed builds do not depend on writable install dir or repo/dist layout (**§5.8**).
- [ ] **Top Phase 1 blockers** closed **or** explicitly accepted with a note in **§12** revision log.
- [ ] **Bundled‑tools redistribution** posture clear enough to ship artifacts (**§5.9**) — don’t defer legal review until Partner Center week.

---

## 5. Phase 1 — Ship 1.0 (distribution + product bar)

**Exit criteria (all should be true before you burn a Store submission):**

- [ ] **Authenticode‑signed** release binaries (users do not fight SmartScreen “unknown publisher” on every launch).
- [ ] **Installer** (not only “unzip folder”) OR a documented **silent install** story your audience accepts.
- [ ] **Auto‑update** or an equally clear **manual update** contract (changelog + in‑app “new version” notice).
- [ ] **Self‑contained** publish **or** documented prerequisite that Store/desktop bundle will satisfy (see §5.5).
- [ ] **Persistent storage path hardening:** installed builds do not rely on writable install directories, repo‑style **`dist/`** layout discovery, or ambiguous app‑root probing for mutable state (**§5.8**).
- [ ] Hosted **`Privacy Policy`** + **`EULA`** URLs (even if traffic is tiny — Store **requires** privacy URL).
- [ ] **`PV-PLN-V1POL-001`** slices **G / H / J** brought to “good enough for 1.0” per that plan’s definition (quick‑edit depth, a11y/motion spot‑check, consistency sweep).
- [ ] **Diagnostics / supportability:** troubleshooting log path **plus** near‑1.0 **crash or failure export** story (see §5.1 optional — treat as **release work**, not late polish).
- [ ] **Risk closure:** filename‑rule **regex safety** (see §7.1) — **engineering controls landed**; treat as **closed for V1** once you accept residual edge cases (document in §12 if you waive anything).

### 5.1 Engineering checklist — blocking risks

Use **`docs/APP_REVIEW_2026-04-12.md`** as the audit trail.

- [x] **P1 — User‑authored regex execution** — **Landed:** `FilenameParserService.CreateConventionRegex` ( **`RegexOptions.NonBacktracking`** + match timeout), **`ValidateConventionPatternForSave`** (length/alternation caps + smoke + catastrophic‑backtrack probe), **`Parse`** catches **`RegexMatchTimeoutException`**; save path calls validation from **`FilenameRulesService.NormalizeRuleForSave`**. Tests: **`FilenameRulesServiceTests`** (`SaveRules_*`), **`FilenameParserServiceTests.ValidateConventionPatternForSave_*`**.
- [ ] **P2 — Hero/banner duplicate work** (`MainWindow.LibraryBrowserPhotoHero.cs`, `CoverService` — cited in review): dedupe in‑flight downloads / cancel stale selection work where practical.

**Optional but strongly recommended before wide release:**

- [ ] Local **crash breadcrumb** or dump writer (sanitize paths; write under app data; user can attach to reports). **Elevate to near‑1.0 release work** once you take paying or arms‑length users — supportability matters for metadata‑heavy tools.

### 5.2 Distribution checklist — signing

**Steps you take outside the repo:**

- [ ] Purchase an **Authenticode** code‑signing certificate (**OV** typical; **EV** improves SmartScreen reputation timelines — research current CA pricing).
- [ ] Secure the private key per CA guidance (hardware token / HSM where required).

**Steps in repo / automation:**

- [x] Add **`signtool sign`** (SHA256 + trusted timestamp server) to **`scripts/Publish-PixelVault.ps1`** post‑publish — **`-Sign`**, **`-CertificateThumbprint`**, optional **`-SignToolPath`**, **`-TimestampServer`**, or env **`PIXELVAULT_AUTHENTICODE_THUMBPRINT`**. Requires Windows Kits **signtool.exe** and cert in the store (PFX path not wired yet — add later if needed).
- [ ] Document the one‑machine publish ritual in **`docs/HANDOFF.md`** or a short **`docs/PUBLISH_SIGNING.md`** if you want a dedicated page (optional follow‑up).

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

### 5.8 Persistent data & install‑root hardening

**Why:** Runtime storage is still partly derived from **app/install location** and repo‑adjacent **`PixelVaultData`** probes — workable for dev and zip publish, **risky** for installer/MSIX until formalized. **Do this before or alongside** installer/updater decisions so upgrade/uninstall stays predictable.

**Not only a Store concern** — any installed build needs a clear contract.

**Grounded doc:** **`docs/DISTRIBUTION_STORAGE.md`** (probe order + layout). **Code:** **`PersistentDataMigrator.ResolvePersistentDataRoot`** — **`Program Files` / `WindowsApps`** → **`%LocalAppData%\PixelVault`** (**2026‑04‑18**); **dist** + **dev‑checkout** probes unchanged.

- [x] **Restricted install dirs** (**`Program Files`**, **`Program Files (x86)`**, **`…\WindowsApps\…`**): authoritative root is **`%LocalAppData%\PixelVault`** — mutable data does not sit beside the EXE.
- [ ] Optional: **explicit user‑chosen data directory** or **ProgramData** shared profile — product decision if needed later.
- [ ] Ensure **settings**, **caches**, **logs**, **SQLite**, **covers/thumbs**, and other mutable data **do not require** the install directory to be writable **for every supported distribution shape** (portable fallback still uses app root — document user expectations).
- [ ] Ensure release builds **do not depend** on repo‑style discovery (**`dist/PixelVault-*`**, **`src/`** sibling probing, etc.) to resolve the data root.
- [ ] Treat **install‑root** (`tools/`, bundled assets) as **read‑only** in the product model; mutable state lives outside Program Files (or declared Package cache areas for MSIX).
- [ ] Add **migration** from current **`PixelVaultData`** / legacy layouts into the chosen installed‑build root where needed.
- [ ] Validate **clean install**, **upgrade**, and **uninstall** with user data preserved (or intentionally removed — document which).
- [ ] Document the final **storage contract** in **`HANDOFF.md`**, **`README.md`**, **`CHANGELOG.md`**, or a short **`docs/DISTRIBUTION_STORAGE.md`** if warranted.

### 5.9 Bundled external tools — redistribution & compliance

**Why:** Full‑trust packaging can **run** **ExifTool** / **FFmpeg**; **Store / legal** still require **redistribution** and **notice** hygiene.

- [ ] Audit **license / redistribution terms** for the **exact** **ExifTool** and **FFmpeg** binaries you ship.
- [ ] Confirm those binaries may be redistributed in **each** channel you use (zip, installer, Store Desktop Bridge).
- [ ] Ship **required** **`LICENSE` / notice** files inside release artifacts when redistribution demands it.
- [ ] Meet any **user‑visible attribution** requirements (About box, **`tools/`** readme, etc.).
- [ ] Confirm **Partner Center / Desktop Bridge** submission allows this **bundled‑tool model** under **`runFullTrust`** (expect disclosure in listing/privacy text).
- [ ] Define **fallback** if a channel forbids bundling (e.g. **Path Settings**‑only external paths for that channel).

### 5.10 External tool strategy (product decision — optional table)

Track the long‑term fork explicitly so packaging work does not thrash.

| Option | Summary | Effort | Risk | Blocks 1.0? | Blocks Store? |
|--------|---------|--------|------|-------------|----------------|
| **A** | Keep **ExifTool** + **FFmpeg** bundled (desktop + Store Desktop Bridge) | Low–medium | Low if licenses OK | No if legal clear | No if **`runFullTrust`** + notices |
| **B** | Replace one or both with **in‑process** libraries over time | High | Medium (parity bugs) | Product choice | Reduces proc‑spawn concerns; still need legal for any native deps |
| **C** | **Full** desktop build vs **constrained** Store flavor (fewer bundled tools later) | Medium | Split codebase / UX | Optional | Possible if policies tighten |

---

## 6. Phase 2 — Microsoft Store (Desktop Bridge)

**Goal:** Submit a **packaged desktop** MSIX that matches how PixelVault actually works: **full trust**, arbitrary library roots, bundled **`tools/`** (ExifTool, FFmpeg).

**Framing:** **Desktop Bridge + `runFullTrust`** is the recommended shape, but treat Phase 2 as a **packaging and packaged‑behavior verification project** — manifest, identity, Partner Center, **and** proving installed MSIX behaves like your Phase 1 build on **writable paths**, **tool launches**, **tray**, and **library roots on non‑system drives** (**§6.3**, **§6.6**).

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

**Packaged‑build spike (clean machine — block certification surprises):**

- [ ] Install MSIX build on **VM or clean profile**; cold launch succeeds.
- [ ] **Settings** load/save; **SQLite** / caches / logs write to intended locations (**§5.8**).
- [ ] Invoke bundled **ExifTool** / **FFmpeg** from packaged layout; failures surface clearly.
- [ ] **System tray** / minimize‑to‑tray / close prompts behave per **`MANUAL_GOLDEN_PATH_CHECKLIST.md`** touchpoints.
- [ ] **Library roots** on **non‑system drives** (e.g. second volume) still scan/import.
- [ ] **Upgrade** to next package version preserves user data; **uninstall** leaves documented residue or clean removal per policy.

### 6.4 Store listing & certification prep

- [ ] **Screenshots** (required resolutions per Partner Center current guidelines).
- [ ] **Short / long description**, keywords, **what’s new**.
- [ ] **Privacy policy URL** (Phase 1).
- [ ] **Age rating** questionnaire completed honestly.
- [ ] **Trademark / asset review** — see §7.2 (logo risk is a common certification failure).

### 6.5 Optional: WinGet

After Phase 1 produces a **signed installer URL** you control:

- [ ] Author **`winget` manifest** and submit PR to **`microsoft/winget-pkgs`** (process changes — follow current Microsoft docs).

### 6.6 Packaged desktop behavior audit (pre‑submission)

Ordinary **`MANUAL_GOLDEN_PATH_CHECKLIST.md`** is necessary but **not sufficient** for MSIX. Confirm at least:

- [ ] App **launches** when installed as MSIX (not only F5 / unpackaged).
- [ ] Logs / config / cache **write only** to allowed locations (package **LocalState** / your chosen redirected root — aligned with **§5.8**).
- [ ] **Upgrades** do not trash settings; **uninstall** behavior matches user expectations.
- [ ] **Folder pickers** / Explorer opens / shell integration still work for library workflows.
- [ ] **External tool** processes start with correct working directory and quoting.
- [ ] **Shortcuts**, **file associations**, or **protocol** handlers — if ever added — declared and tested.

---

## 7. Risk register (revisit each milestone)

### 7.1 Regex DoS / stall (V1‑blocking engineering risk)

**Source:** **`docs/APP_REVIEW_2026-04-12.md`** — user‑authored patterns must not stall imports/scans/editors.

**Status:** Mitigations in **`FilenameParserService`** (see §5.1 checklist — marked done). Re‑open only if a new execution path bypasses **`CreateConventionRegex`** / save validation.

### 7.2 Third‑party logos & trademarks (distribution‑wide + Store‑critical)

**Assets to audit before wider distribution and Store submission** (filenames under **`assets/`**):

- `Steam Library Icon.png`
- `PS5 Library Logo.png`
- `Xbox Library Logo.png`
- `Nintendo Library Icon.png`
- (and related PC/Xbox PC/emulator icons)

**Why:** nominative **text** labels (“Steam”, “PlayStation”) for categories are usually fine; **logo‑like artwork** may violate platform brand guidelines, fail Store certification, or create **general distribution** issues if marks read as official Sony/Microsoft/Nintendo branding.

**Action:**

- [ ] Visual review: generic/icon‑style vs official marks.
- [ ] Replace or license as needed; update **`CHANGELOG.md`** when assets change.

### 7.3 External tools, full trust & sandbox

Bundled **`tools/`** exes are consistent with **full‑trust** desktop bridge; **not** with strict sandbox.

**Action:** keep **`runFullTrust`** decision explicit (**§6.1**). **Also** complete **§5.9** redistribution / notice compliance — packaging alone does not satisfy license obligations.

### 7.4 Bundled third‑party redistribution & notices

**Summary:** Even when **`runFullTrust`** allows execution, shipping **FFmpeg** / **ExifTool** binaries still requires **license compliance** and often **shipping notice files** — a common late‑stage surprise.

**Action:** follow **§5.9** before calling Phase 1 “done” for public artifacts.

### 7.5 Install‑root & layout assumptions

**Summary:** Runtime storage discovery today is optimized for **dev + zip/`dist` publish**, not fully formalized for **Program Files–style installs** or **MSIX** without careful **`PixelVaultData`** / redirected state rules.

**Action:** **§5.8** hardening before Store submission; preferably **before** final installer choice so upgrades behave.

### 7.6 Network & keys

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
- [ ] Treat **hardcoded dev defaults** (paths beside repo, tool discovery) as **configuration/runtime** concerns — isolate before distribution hardening (**§5.8**).
- [ ] Keep **`docs/PERFORMANCE_TODO.md`** and **`docs/ROADMAP.md`** aligned when responsiveness items affect release confidence.

---

## 10. Suggested default order (when you don’t know where to start)

When you open this doc cold:

1. **Read §1.3** (Store blockers) and **scan §3** (scorecard) and **§7** (risks).
2. If **Phase 1** not done: work **§5** — after signing (**§5.2**), prioritize **§5.8** (data root) **alongside** **§5.3** (installer/updater), **§5.9** (tool licenses), **§5.4** (policies/support).
3. Check **§4.1** gate before touching serious MSIX work.
4. If Phase 1 is genuinely done: **§6** — manifest **plus** packaged spike (**§6.3**, **§6.6**).
5. **Ignore §8** until desktop distribution feels boring.

### 10.1 Consolidated priority order (external review)

Use when choosing the next sprint without re‑reading the whole plan:

| Priority | Focus |
|----------|--------|
| **1** | Lock **Phase 1**: signing decision, installer/updater, self‑contained vs runtime, **`MANUAL_GOLDEN_PATH`** on **clean install + upgrade** |
| **2** | **§5.8** + minimal **MSIX spike** (**§6.3**) — data paths + bundled **tool** execution under packaged desktop |
| **3** | Policy + branding: privacy/support URLs, **§7.2** logo audit, publish ritual docs |
| **4** | Keep thinning **startup/path** assumptions where it reduces distribution risk (**`PV-PLN-UI-001`**, sessions) |

**Avoid until desktop 1.0 is stable:** strict sandbox redesign, iOS/backend depth, Store‑only UI churn, chasing multiple optional channels at once.

---

## 11. What not to work on yet

Protect scope until **Phase 1** desktop distribution is **boringly stable**:

- **iOS / macOS clients** — no app repo; **`ios_foundation_guide.md`** is directional only.
- **Backend/service** beyond **future‑friendly seams** — no API to ship against.
- **Strict‑sandbox Store redesign** — not the product PixelVault is today unless consciously chosen.
- **Multi‑platform rewrite** — out of scope for this roadmap.
- **Premature Store‑specific UI** — polish general desktop UX first (**`PV-PLN-V1POL-001`**).

---

## 12. Revision history

| Date | Change |
|------|--------|
| **2026‑04‑18** | Initial roadmap: phased Windows 1.0 → Microsoft Store → optional iOS/backend; checklists, risks, scorecard (`PV-PLN-DIST-001`). |
| **2026‑04‑18** | §5.1 P1 regex marked **landed** (implementation + tests). **`Publish-PixelVault.ps1`**: optional **`-Sign`** / thumbprint / **`PIXELVAULT_AUTHENTICODE_THUMBPRINT`** + **`signtool`** (§5.2). |
| **2026‑04‑18** | Feedback integration: §1.3 Store blockers; §3.1 technical vs submission; §4.1 Phase 2 gate; §5.8–§5.10 storage/tools; §6 packaged spike + §6.6 audit; risks §7.4–§7.6; §10.1 priorities; §11 out‑of‑scope. Sources: `pixelvault_microsoft_store_plan_feedback.txt`, `docs/PV-PLN-DIST-001-suggested-updates.txt`. |
| **2026‑04‑18** | §5.8 implementation slice: **`docs/DISTRIBUTION_STORAGE.md`**; **`PersistentDataMigrator`** LocalAppData routing for Program Files / WindowsApps; tests in **`PersistentDataMigratorTests`**. |
