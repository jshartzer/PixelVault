# Death Stranding Director's Cut fabrication source

October 24 chip-reader addition: all 56 restoration observations now use a fully validated 190-entry installed collector catalogue. Full prefix, keys, sentinel, sequential index, flags, bounds and ambiguity checks precede any progress. Only restoration mask 0x02 counts; pickup and intermediate states do not. Native source setup and automatic capability use the existing shared forms/worker/reconciliation/scheduler. See [Chip reader evidence](DEATH_STRANDING_MEMORY_CHIP_RESEARCH.md).

ACHG-004 native save source, implemented October 21, 2026. The legacy adapter ID `death-stranding-dc.fabrication`, version `1`, now supports the bundled Steam / game 1850570 / Homo Faber, Master Builder, Trail-Blazer, Growth of a Legend and Fount of Knowledge trackers. Automatic/background refresh is separately opt-in. Facility connections remain research.

## Setup and use

Open the Death Stranding tracker → **Source…** → **Death Stranding Director's Cut save**. Choose the account's save folder and installation folder containing `ds.exe` and `oo2core_7_win64.dll`. Steam library manifests provide an installation suggestion; the normal Local AppData save location is suggested from the connected Steam ID. Paths remain editable for moved libraries, saves and backup copies. No drive letter or Codex runtime is required by the adapter.

The save folder may be the account directory, its parent containing that account's numeric directory, or a specific slot directory. The reader does not scan other account directories. The decrypted Steam ID must exactly match the connected account. A profile label is descriptive; this version has one active source per tracker/account, rather than parallel independent profile progress.

Leave **Specific checkpoint** blank to select the newest valid checkpoint by UTC modification time, with a deterministic path tie-breaker. Select a `checkpoint.dat` inside the configured account folder to pin a save instead. The chosen slot and observation time appear in the reconciliation preview. Known corrupt saves can be skipped in newest-first order, with an explicit skipped-newer-checkpoints notice. Account mismatches and unsupported layouts stop the refresh; they do not silently fall back.

**Refresh** reads current disk data and opens the existing review. It preserves manual overrides and notes by default. Use the preview's explicit choices to accept source counts or an older observation. **History / undo** can undo an applied batch. Selecting a source does not itself apply observations.

## Read pipeline and checks

1. Enumerate up to 128 recognized autosave/manualsave/quicksave slots without recursively scanning unrelated directories.
2. Copy one checkpoint using read-only access with sharing that excludes an active writer. Verify size and timestamp around the read; retry temporary locks or incomplete files twice. Bound input to 16 MiB.
3. Save the encrypted copy, request and SHA-256 manifest under the durable, private tracking directory. Launch the same packaged PixelVault executable with its internal read-worker argument. The worker exits before Velopack, SQLite, WPF Application, MainWindow or network services initialize.
4. Validate checkpoint section lengths and magic; decrypt a memory copy; verify the embedded Steam identity. The worker includes no save-writing, re-signing or game-launching operation.
5. Load the compression DLL from the configured game installation in the child process only. Bound decompression to a 256 KiB prefix and at most 64 MiB of save data plus alignment/header space. Verify the declared data's MD5.
6. Locate exactly one complete sequence of 408 unique known catalogue keys with sequential packed indices. Match all 108 trophy targets by stable catalogue UUID/name-code metadata. Unknown/ambiguous layouts require a reader update.
7. Emit validated observations with source UTC time, encrypted input fingerprint, parser version, selected slot and game-DLL hash. Fabrication uses mask `0x80000000`; blueprint availability uses the previously inferred `0x20000000` flag. Availability does not guarantee materials or facility support.
8. Use the common reconciliation and transactional history layer. A byte-identical checkpoint whose filesystem timestamp changes remains the same observation, so touching/copying it does not create a spurious progress batch. Semantic rollback or manual conflicts still require explicit review.

No assertions depend on a particular item already being fabricated. The initial positive/negative gameplay examples may change freely. Metadata alone is never used to invent completion of structures or collectibles.

For construction/maximum-upgrade trackers, the worker instead parses the complete counted persistent boolean map with bounded counts, unique GUIDs, binary values, known-identity coverage and trailing empty-map markers. Extracted persistent/default-false metadata permits absent targets to resolve false only after full map validation. Controlled Chiral Bridge and Postbox transitions, 24 copied checkpoints and matching latest provider counters corroborate interpretation. See [World reader details](DEATH_STRANDING_WORLD_TRACKERS.md). Worker requests include the selected tracker ID, while older requests default to fabrication. All selected catalogue IDs and returned item counts must match; unsupported trackers/formats fail closed.

## Isolation, retention and compatibility

The worker uses a 25-second timeout; candidate processing has a 60-second overall deadline. Cancellation/timeouts terminate the started child process tree and wait for exit. Native decoder failure cannot execute in the UI process. Worker error dialogs are disabled in that process; structured errors are returned when possible. Failures retain last-success metadata and existing tracker state.

Private runtime copies live under `PixelVaultData/achievement-tracking/sources/death-stranding-dc/<account-scope-hash>/run-*`. **Keep the latest 3 private diagnostic snapshots** is enabled by default and can be disabled. Cleanup only removes known files in validated app-created run directories, skips active leases/reparse points and removes a directory only when empty. It never recursively deletes a computed path or touches source saves. No decrypted full-save payload is written by the production reader. Requests/manifests contain local paths/account identity and stay outside source control and release packages.

The installed game's proprietary compression DLL is not redistributed. The app uses its existing .NET Windows x64 runtime. There is no Python, Java, Decima Workshop, standalone resigner or extra executable dependency to install. The managed read-side key transform is adapted from [DeathStrandingSaveDataResigner](https://github.com/mi5hmash/DeathStrandingSaveDataResigner/blob/main/death-stranding-savedata-resigner-cli/Helpers/DeathStrandingDeencryptor.cs), under its [MIT license](https://github.com/mi5hmash/DeathStrandingSaveDataResigner/blob/main/LICENSE). Attribution/license text is in `tools-licenses/death-stranding-save-reader-MIT.txt` and is merged into packaged `tools/licenses` by the normal build scripts.

Game updates are supported only while the validated format and catalogue/milestone mapping remain compatible. Unsupported formats fail closed and preserve progress. Epic saves, the original non-Director's-Cut edition, console saves, chip/facility native progress, live in-memory progress and unsaved gameplay are not supported by this adapter.

Growth of a Legend uses a separate native order parser within the same worker. The supported compact-counted 1,140-record array has 96-byte entries, including the 16-byte prefix; mission ID is at +16 and earned ranks at +88. Complete bounded layout checks and unique coverage of all 540 standard orders precede 1,080 binary LL/LLL observations. LL bit 7 and LLL bit 8 have independent controlled-save confirmation. The adapter's legacy ID/version remains compatible; requests select the tracker as before. Unknown/ambiguous layouts return UnsupportedFormat without observations. See [Order reader evidence](DEATH_STRANDING_ORDER_RESEARCH.md).

## Verification

- 21 new tests: encrypted/plain containers, wrong account, malformed headers/lengths, checksum/bounds, exact catalogue coverage, changed fabrication flags including the initial examples, unknown/ambiguous layouts, locked/available distinction, candidate selection/containment, stable locked-file retry, cancellation, process timeout/termination, bounded retention and source UI.
- Optional local integration runs the real isolated worker against a copied encrypted checkpoint, compares decryption with the prior independent CLI output, refreshes a temporary store, checks duplicate/touched input, relocated saves, visible corrupt-save fallback and a hard account-mismatch stop. Original SHA-256 is checked unchanged. No live item state is applied by this fixture.
- The real latest checkpoint read produced 108 observations / 35 fabricated, with an unchanged source SHA-256 and matching copied fingerprint.
- 83 focused tracker tests pass; full native suite: 980 passed, only the two known `TradingCardFoilTests` rendering failures (982 total). Test report: `tests/PixelVault.Native.Tests/TestResults/tracking-death-stranding-adapter.trx`.
- Source setup was rendered and visually inspected; Steam installation discovery located the current library. Live verification of fabricating one more item during gameplay, OS keyboard/high-contrast/scaling checks and a real future game update remain manual QA. Synthetic catalogue tests already verify incremented and all-zero/all-complete states.

Automatic refresh is implemented separately as opt-in per source/account; see [Automatic tracker refresh](TRACKER_AUTOMATIC_REFRESH.md). The current copied-save integration also verifies automatic no-change refresh without duplicate history or source-save writes.

Review executable: `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`. Published version remains 0.077.000. The broader plan is [ACHG-004](plans/PV-PLN-ACHG-004-game-progress-trackers.md).
