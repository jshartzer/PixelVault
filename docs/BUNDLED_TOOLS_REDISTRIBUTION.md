# Bundled external tools — redistribution worksheet (**PV-PLN-DIST-001** §5.9)

PixelVault may **launch** third-party binaries (**ExifTool**, **FFmpeg**) from **`Path Settings`** or bundled **`tools/`**. Shipping them in installers or Store packages requires matching each vendor’s **license**, **attribution**, and **redistribution** rules.

This page is an **engineering worksheet** — not legal advice. Fill it before calling **§5.9** complete.

| Tool | Binary / version shipped | Source download URL | License name / URL | Redistribution allowed? (Y/N / notes) | Notice / **`LICENSE` file** required in artifact? | Shipped in channel (zip / Velopack / Store) |
|------|--------------------------|---------------------|-------------------|--------------------------------------|---------------------------------------------------|--------------------------------------------|
| **ExifTool** | *(fill — e.g. Windows exe)* | | | | | |
| **FFmpeg** | *(fill — build flavor)* | | | | | |

**Release packaging checklist**

- [ ] Required **`LICENSE` / COPYING / NOTICE`** files copied **next to** or **under** **`tools/`** in **`Publish-PixelVault`** / **`Publish-Velopack`** outputs as licenses demand.
- [ ] **About** / **Setup & health** / **`tools/`** readme mentions third-party tools if attribution is required.
- [ ] **Partner Center / Desktop Bridge:** bundled **`runFullTrust`** executables disclosed per submission guidance.

**Fallback (product)**

- **Path Settings–only** external installs if a channel forbids bundling — aligns with roadmap **§5.9** fallback row.
