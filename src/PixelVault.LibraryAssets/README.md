# PixelVault.LibraryAssets

Separate **domain + planning** library (no UI, no PixelVault.Native references):

- **Canonical assets** — persistent records keyed by `AssetId`, with lifecycle (`active` → `missing` soft state → `deleted_confirmed`).
- **Scan diffs** — `Added`, `Updated`, `Missing`; `ConfirmedDeleted` is an explicit/policy outcome, not inferred from a lone scan.
- **Root health** — preflight before destructive reconciliation (offline / unreadable / topology / optional file-count sanity).
- **Reconciliation plans** — what to apply when health passes vs degrades (e.g. allow adds/updates but withhold missing/deletes when the library is inaccessible).

PixelVault.Native can reference this project and adapt its existing SQLite rows to these models over time.
