# Shared save analysis

November 1: Native 0.116.000 adds delivered cargo weight as the twelfth independent projection. It reads whole-kilogram statistic 49 from the complete strict typed statistic table; the linked Century goal requires 60,000 kg. Cache schema v6 prevents older eleven-projection results being reused. Packaged-reader rehearsal reports 16,503 kg, corroborated by the game/Steam displays and a 176-save lifetime-record audit. Other projections retain independent errors and no additional decompression is introduced. See [cargo mapping](DEATH_STRANDING_CARGO_RESEARCH.md).

November1: Native0.115.000 adds the eleventh projection, the complete540-order LLL Century tracker. The existing order table is parsed once; the new projection filters its validated LLL observations without inferring LL equivalence or changing the official tracker. Cache schema v5 rejects older ten-projection results. The new Century goal requires all540 known LLL values and preserves the same bound-source/account safeguards.

October29 evening: Native **0.109.000** / connector **0.24.0** add validated per-email opened states as the **tenth independent projection**, cache schema **v4**. All160 copied payloads and two named reading controls agree with statistic38. Desktop/NAS email catalogues are automatically connected; explicit user-approved game authority applies only to that binding. See [mapping, tests and deployment](DEATH_STRANDING_MAIL_RESEARCH.md). Earlier dated entries below are historical.

October29 mail follow-up: Native0.108.000/connector0.23.0 add mail statistic38 as the ninth independent projection, with cache schema v3. See [mail verification](DEATH_STRANDING_MAIL_RESEARCH.md). Per-email flags remain unvalidated.

October 29 follow-up: Native0.107.000 and connector0.22.0 add the itemized interview reader as the eighth independent projection and advance the analysis cache schema. Both interview trackers now refresh automatically with read-only game-file access. See [interview validation and deployment](DEATH_STRANDING_INTERVIEW_RESEARCH.md). Earlier foundation descriptions below are historical.

October28 follow-up: Native0.106.000 adds the road projection as a seventh independently validated result. Existing six readers keep their own outcomes and share the same guarded decode/cache. See [road refresh](DEATH_STRANDING_ROAD_RESEARCH.md). Earlier foundation-only descriptions below are historical.

Death Stranding DC now shares one decode across supported tracker readers. This is the foundation for future Century evaluators to consume the same validated save evidence. It does not add road-completion or individual-interview flags, award Century points, or enable automatic refresh.

## Read flow

1. Existing opt-in/manual refresh selects a checkpoint. Stable copying permits concurrent game writes/deletes, checking length and write time before/after. Container/account/checksum validation still runs in the isolated worker. A changing or invalid copy cannot become progress.
2. The source hashes actual checkpoint bytes and the game compression DLL. Cache identity includes private storage scope, worker path, game-installation path, account, checkpoint hash, DLL hash and analysis-schema version. Identical size/time alone never implies identical content.
3. On a miss, the timed/cancellable child decrypts and decompresses once, at BelowNormal process priority. It projects six existing supported readers: fabrication, construction, maximum upgrades, premium-order ranks, restored memory chips and the official interview-read counter.
4. Each table validates independently. Unsupported/invalid tables carry explicit errors, without guessed observations or blocking valid unrelated tables. Top-level dependency/account/container failures are never cached. Ordinary newest-invalid fallback and wrong-account rejection remain in force.
5. On a hit, no child or extra diagnostic save copy is created. Each consumer deserializes its own snapshot and rebases source-slot/time metadata to the checkpoint it actually read. Only the requested configured tracker enters existing preview/reconciliation/automatic-apply rules. Manual notes/counts, rollback review, settings/account checks and history remain owned by that pipeline.

The process-local cache retains serialized table results, not decrypted saves: four entries maximum, at most 4 MiB of serialized characters per entry, expiring after 30 minutes. A single cancellable gate serializes expensive analyses across adapter instances in that process; LRU eviction bounds memory. Cancellation/failure does not retain unfinished results. Cancelling a waiting consumer does not cancel another consumer's active read. The initiating consumer's cancellation terminates its worker; another waiter may retry.

Private copied saves retain the existing per-tracker optional diagnostics/three-copy retention policy. Hits do not create new diagnostic copies; KeepSnapshots=false still prunes that tracker's retained managed runs.

## Limits and extension

- Checkpoint bytes are still read/hashed on refresh; automatic change probes also hash the newest file. This catches cloud replacements retaining size/time. This is a decode cache, not continuous game-memory monitoring.
- Native and the PC connector do not share a cache across processes. Restart, expiry, eviction or changed decoder inputs requires a new analysis.
- Shared source/cache files compile into both Native and connector source. Native 0.105.000 ships this foundation; the installed connector remains 0.20.0 until its next upgrade. No NAS/server change is required.
- Future readers/evaluators must validate complete fields, add an explicit projection and update coverage/schema identity. Keep Century observations account scoped and separate from provider progress; manual tracker check-offs never award them.
- Road sections and full itemized interviews still await validation. The official interview counter is not proof of the all-interviews collection goal.
- No in-game frame-time benchmark has been performed. Lower priority and shared decoding reduce redundant work but do not guarantee zero CPU/disk contention.

## Verification

`SharedSaveAnalysisTests` covers concurrent reuse/serialized misses, cancellation ownership, retry after failure/cancellation, expiry/LRU/size bounds, identity separation, independent table outcomes, batch identity/coverage rejection, no Century inference and the actual source flow across two adapter instances. The source fixture checks content changes with preserved size/time, DLL changes, per-consumer slot/time rebasing, unchanged source bytes and disabled diagnostic retention. Existing decoder, refresh, automation and UI tests cover reconciliation/cancellation regressions.

Packaged Native 0.105.000 worker review against an existing private diagnostic copy produced six valid tables, all matching their individual-reader item outputs. The seed copy hash was unchanged; temporary raw review copies were removed. Summary: `tmp/shared-save-analysis-verification.json`. Final regression suites: 169 Native and 28 connector tests pass. The write-sharing fixture also verifies copying and change probing while a game-style write handle remains open.
