# The Weight of a Nation

The fifth Century achievement requires **60,000 kg of cargo delivered**, worth **100 PV points**. `death-stranding-dc.the-weight-of-a-nation` is a separate one-item counter linked to `century.death-stranding-dc.cargo-60000kg`. Player-facing descriptions state the unlock requirement only.

## Mapping and validation

The existing complete 47–50-entry typed achievement-statistic table contains statistic **49**, an unsigned whole-kilogram delivered-weight total. The latest approved save reports **16,503 kg**, matching the existing Steam counter for official achievement 049, Pumped Porter, and the user's screenshot showing **16.50 t** and **1,161 cargo pieces**. The lifetime delivery record stores approximately **16,503.99999935925 kg**; truncation explains the whole-kilogram counter. Never round the screenshot or floating record up to award a threshold.

Read-only research checked **176 copied, account/checksum-validated payloads**, spanning **32 distinct totals from 10,770 to 16,503 kg**. Each statistic 49 matches the floor of the unique delivery record paired with statistic 48's delivered-piece count. No fixed file offset, estimated order weight, or summed order catalogue is used. Private source copies and per-payload evidence remain under `C:/GameDataExtractor/Backup Data/Death Stranding Director's Cut/Reference/CargoSaves-20261101-204404548`; sanitized evidence is `artifacts/century-cargo/weight-research-verification.json`.

The production reader uses the existing strict typed-table validator, including every required identity/type, optional booleans, range, duplicate, truncation, ambiguity and candidate-count guards. It supports 0–1,000,000 kg, matching tracker numeric bounds, and preserves successful progress on unsupported values. Shared analysis v6 adds an independent twelfth projection. Other projections retain independent success/failure outcomes.

## Century authority and presentation

Only a known current observation from the approved bound source can record account-scoped Century progress. Manual checks/imports, unknown/failing reads and old observations cannot award it. Century display caps at 60,000; the dedicated tracker retains the observed whole-kilogram total above the threshold. First verified unlock and its 100 points persist. Existing official achievements, trackers, notes, source scope and replacement authority remain separate.

The fifth shelf spot is now defined. New weathered monochrome truck artwork matches the existing cyan-trim Century frame. Nothing Left to Deliver now uses a detailed courier-and-cargo illustration with three stars; its unlocked URI is versioned and the browser Century image cache key is refreshed. Prompts and generated asset provenance are recorded in `artifacts/century-cargo/artwork-sources.md`.

Installation evidence is recorded in `artifacts/century-cargo` after the packaged-reader rehearsal and live activation. Do not infer live activation from source or packaging alone.

The corrected installation completed at **2026-11-01 21:07:35 UTC**. Native 0.116.000 and Companion 0.43.0 are published/live. Independent persisted Century progress is **16,503/60,000**, unknown count zero, locked and zero points. SaveChanges is enabled/unpaused; a fresh automatic check reports zero failures, no pending review and an unchanged source result. Loopback/LAN health, service path/identity, browser artwork script/revision and restored Ready backup task all pass. Existing tracker rows and automatic policy remain unchanged. The pre-update app-only backup `app-20261101T210619Z-d9c2e9b5.zip` and its NAS copy share SHA-256 `1c4a62bd3f1c8f6eb826f026a92d3f3bbfe5c630ac4a44755fe353919b2d3682`. See `live-connection.json`, `update-result.json`, `live-progress-verification.json` and `live-runtime-verification.json`.

The first service attempt rolled back to healthy 0.42.0 after the outer Companion reader failed SQLite initialization: the package omitted native `e_sqlite3.dll`, although the inner save-only read succeeded. `Build-PixelVaultCompanion.ps1` now explicitly copies and verifies the native dependency. The actual outer `access` protocol and corrected packaged save rehearsal pass. The updater now requires the additional access proof and promptly restores the prior service if the new process stops before HTTP readiness. First-attempt receipts are retained under `artifacts/century-cargo/installation-attempt-1`.
