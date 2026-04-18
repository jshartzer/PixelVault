# PixelVault — End User License Agreement (draft)

**Effective:** April 18, 2026  

**Summary:** This agreement governs your use of the **PixelVault** Windows desktop application distributed by **`{LEGAL_ENTITY}`** (“**Licensor**,” “**we**,” “**us**”). By installing or using PixelVault, you agree to these terms. If you do not agree, do not install or use the software.

**Placeholders:** Replace **`{LEGAL_ENTITY}`**, **`{PUBLISH_CONTACT}`**, **`{JURISDICTION}`**, and hosting URLs before public distribution or Microsoft Store submission (**`PV-PLN-DIST-001` §5.4**). Have qualified counsel review for your jurisdiction and business model.

**Related:** **`docs/PRIVACY_POLICY.md`** — how data is handled when you use PixelVault.

---

## 1. License grant

Subject to this Agreement, Licensor grants you a **limited, non-exclusive, non-transferable, revocable** license to install and run **one copy** of PixelVault on **Windows** devices **you own or control**, for **your personal or internal business use**, in object code form only.

Commercial redistribution of PixelVault itself (reselling the app binary, bundling it as part of a paid product where PixelVault is the primary deliverable, etc.) is **not** granted unless you have a **separate written agreement** with Licensor.

---

## 2. Restrictions

You may **not**, except where applicable law forbids these restrictions:

- Copy, modify, adapt, translate, or create derivative works of PixelVault, except as allowed by **applicable open-source licenses** for components distributed with source in this repository (if you obtain PixelVault from source).
- Reverse engineer, decompile, or disassemble PixelVault, except to the limited extent **mandatory applicable law** allows.
- Remove, obscure, or alter proprietary notices or branding.
- Use PixelVault in any way that violates law or third-party rights.
- Use PixelVault to build a competing service that reproduces its core library/metadata workflows by automated extraction from the app in bulk (this does not restrict normal personal use of your own capture library).

---

## 3. Third-party software and services

PixelVault may integrate with or invoke **third-party programs** (for example **ExifTool** for metadata; **FFmpeg** if **you** install it; APIs such as **Steam**, **SteamGridDB**, or **RetroAchievements** when you configure them).

- **Third-party executables** you install or place on disk are subject to **their** licenses and terms.
- When Licensor ships **ExifTool** alongside PixelVault, license texts may appear under **`tools\licenses\`** in distributed builds — see **`docs/BUNDLED_TOOLS_REDISTRIBUTION.md`** in the source tree.

Your use of third-party services is **between you and those providers**.

---

## 4. Updates

PixelVault may offer **updates** (bug fixes, features, security patches). Updates may be delivered through installers, in-app flows, or package managers you use. Continued use after an update is offered may constitute acceptance of updated terms if Licensor clearly notifies you and provides a reasonable way to review them.

---

## 5. Disclaimer of warranties

**TO THE MAXIMUM EXTENT PERMITTED BY APPLICABLE LAW**, PIXELVAULT IS PROVIDED **“AS IS”** AND **“AS AVAILABLE”**, WITHOUT WARRANTIES OF ANY KIND, WHETHER EXPRESS, IMPLIED, OR STATUTORY, INCLUDING IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, TITLE, AND NON-INFRINGEMENT. LICENSOR DOES NOT WARRANT THAT PIXELVAULT WILL BE ERROR-FREE, SECURE, OR UNINTERRUPTED, OR THAT DEFECTS WILL BE CORRECTED.

Some jurisdictions do not allow certain disclaimers; in those jurisdictions, disclaimers apply **to the fullest extent permitted**.

---

## 6. Limitation of liability

**TO THE MAXIMUM EXTENT PERMITTED BY APPLICABLE LAW**, IN NO EVENT WILL LICENSOR OR ITS SUPPLIERS BE LIABLE FOR ANY **INDIRECT, INCIDENTAL, SPECIAL, CONSEQUENTIAL, OR PUNITIVE DAMAGES**, OR ANY LOSS OF PROFITS, DATA, GOODWILL, OR BUSINESS OPPORTUNITY, ARISING OUT OF OR RELATED TO THIS AGREEMENT OR PIXELVAULT, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGES.

**TO THE MAXIMUM EXTENT PERMITTED BY APPLICABLE LAW**, LICENSOR’S **TOTAL CUMULATIVE LIABILITY** ARISING OUT OF OR RELATED TO THIS AGREEMENT OR PIXELVAULT WILL NOT EXCEED THE **GREATER OF** (A) THE AMOUNTS YOU PAID LICENSOR FOR PIXELVAULT IN THE **TWELVE (12) MONTHS** BEFORE THE CLAIM, OR **(B) U.S. $0** IF PIXELVAULT WAS PROVIDED **FREE OF CHARGE**.

Some jurisdictions do not allow certain limitations; in those jurisdictions, limitations apply **to the fullest extent permitted**.

---

## 7. Termination

This license **terminates automatically** if you breach this Agreement and fail to cure within a reasonable time after notice (where cure is possible). Upon termination, you must **stop using** PixelVault and **uninstall** copies. Sections that by their nature should survive (warranty disclaimers, liability limits, governing law) **survive** termination.

---

## 8. Governing law

This Agreement is governed by the laws of **`{JURISDICTION}`**, **excluding** conflict-of-law rules that would apply another jurisdiction’s laws. Courts in **`{JURISDICTION}`** have **exclusive jurisdiction**, subject to **mandatory consumer protections** in your country of residence where they cannot be waived.

---

## 9. Changes to this Agreement

Licensor may revise this Agreement by posting an updated version and updating the **Effective** date. Material changes should be summarized in **`CHANGELOG.md`** or release notes where practical.

---

## 10. Contact

**`{PUBLISH_CONTACT}`** — replace with support email or public issue URL before distribution.

---

## 11. Hosting this document (**§5.4**)

Prefer to **host HTTPS** **after** technical installer QA and signing meet your bar — roadmap **`PV-PLN-DIST-001` §10.1**. Store and broad distribution ultimately need a stable **HTTPS** URL (listing, installer, About).

1. Replace all **`{…}`** placeholders with final legal names and contacts **after counsel review**.
2. Publish Markdown or HTML to the same class of hosting you use for **`docs/PRIVACY_POLICY.md`**.
3. Keep **Effective** dates aligned when you materially change terms or product behavior (**`CHANGELOG.md`**).

---

## Draft notice

This file is an **engineering draft** for coordination with **`docs/PRIVACY_POLICY.md`** and **`PV-PLN-DIST-001`**. It is **not** tailored legal advice. **Consult qualified counsel** before relying on it for commercial distribution, employment, or regulated environments.
