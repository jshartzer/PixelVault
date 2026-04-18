# PixelVault — Privacy policy

**Effective:** April 18, 2026  

**Summary:** PixelVault is a **local-first** Windows desktop application. It keeps your library and configuration **on your machine** by default. This document describes what leaves the device, what stays local, and how optional online features work.

**Hosting:** Publish this file at a stable HTTPS URL before Microsoft Store submission or public distribution (**`PV-PLN-DIST-001` §5.4**). Replace `{PUBLISH_URL}` below when you host.

---

## What PixelVault does not do

- **No built-in analytics or crash telemetry** shipped to PixelVault-controlled servers as of this document date. There is no advertising SDK embedded in the open-source app line described in this repository.
- **No account system** operated by PixelVault — the app does not require a PixelVault login.

If that changes in a future release, update this file and **`CHANGELOG.md`** together.

---

## Data stored locally

PixelVault reads and writes files **you control**, including:

- **Screenshots and videos** under your configured **library** and **destination** folders.
- **SQLite indexes**, **thumbnail/cover caches**, **logs**, and **`PixelVault.settings.ini`** under the resolved **app data folder** (see **`docs/DISTRIBUTION_STORAGE.md`**). Typical locations include a repo **`PixelVaultData`** tree during development, **`%LocalAppData%\PixelVault`** when installed under protected directories, or a folder beside the executable for portable installs.
- Optional **embedded metadata** and sidecars managed through **ExifTool** workflows you configure.
- If you install **FFmpeg** yourself and set its path in **Path Settings**, PixelVault runs **`ffmpeg.exe`** locally for optional video workflows — it is **not** bundled with the application.

You are responsible for backups and for any sensitive content in your library folders.

---

## Optional network activity

When you enable or use features that touch the Internet, PixelVault may contact **third-party services**. Examples:

| Capability | Typical endpoint / service | Data involved |
|------------|-----------------------------|----------------|
| **SteamGridDB** (covers) | steamgriddb.com API | Requests you initiate; API key if you configured one locally |
| **Steam** (titles, identifiers, storefront names) | Valve Steam / CDN as implemented | IDs and filenames needed for lookups you trigger |
| **RetroAchievements** | retroachievements.org API | Credentials you configure and requests from features you use |
| **Steam Web API** | Valve endpoints | Keys and identifiers you configure |

Those services have **their own** privacy policies and terms. PixelVault does not control third-party retention or logging.

Also:

- **Windows**, **your browser**, **antivirus**, and **network intermediaries** may observe HTTPS traffic regardless of app policy.

---

## Credentials and secrets

Optional API keys or tokens (**SteamGridDB**, **Steam Web API**, **RetroAchievements**) are stored **locally** in **`PixelVault.settings.ini`** (or overridden via environment variables where documented in the app). Protect that file like any other secret on disk.

---

## Troubleshooting logs

Optional **troubleshooting logging** writes additional detail to disk (see in-app Settings). Paths may be **partially redacted** when path redaction is enabled. Logs stay on your machine unless **you** copy them elsewhere.

---

## Children

PixelVault is not directed at children. Game libraries may contain mature content depending on **your** captures.

---

## Changes

Privacy-related behavior changes belong in **`CHANGELOG.md`**. Bump the **Effective** date at the top when you materially edit this policy.

---

## Contact

Provide a **`{PUBLISH_CONTACT}`** email or issue URL before Store submission (support field in Partner Center).

---

## Hosting this policy (**§5.4**)

Public distribution and **Microsoft Store** require a stable **HTTPS** URL to this content (not only a file in the repo).

1. Replace **`{PUBLISH_CONTACT}`** in **Contact** with a real support email or public issue URL.
2. Publish the edited text (Markdown or rendered HTML) somewhere durable — for example **GitHub Pages**, a **`docs/`** branch on GitHub, a project wiki, or your own static site. A **raw** `githubusercontent.com` link to Markdown is acceptable only if you intend to keep that repo layout stable; prefer a proper page for end users.
3. Put the final URL in **Partner Center** (privacy policy field) and any store listing or download page you control.

Keep the **Effective** date in sync when behavior or disclosures change (**`CHANGELOG.md`**).

---

## Legal

This repository and build pipeline are provided **as-is** unless you attach a separate end-user license. Coordinate **`docs/PRIVACY_POLICY.md`** with your **`EULA`** or terms if you distribute commercially.
