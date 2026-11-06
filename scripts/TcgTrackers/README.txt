TCG Card Shop Simulator trackers — installed November 5, 2026 (America/Chicago)

Installed in PixelVault
12 custom trackers for Steam game 3070070 in the existing canonical tracker database:
- Tetramon, Destiny, Ghost and Ascension collections (4,460 distinct variants).
  One Collected column follows historical discovery (originally obtained).
  Current binder ownership does not affect completion. Category goals count
  each historically collected card once.
- Stock reserves for 113 orderable retail products across all shop catalogue tabs.
  Initial targets are one smallest order carton per product, adjustable in PixelVault.
- Six financial health checks, including current/last-day/30-day profitability,
  outstanding bills, cash coverage and a 10% recent net-margin planning target.
- Six linked, previously unearned achievements: Card Connossieur, Card Master,
  Legendary Shop, Grading Master, TCG Arcade and Obsessed With Grades.

No Steam achievement or provider unlock state is changed. Limitless Pull and
Graded Gold are event-based and have no fabricated running counter.

Latest baseline
Autosave written November 1, 2026, 8:34 AM America/Chicago, Ben & Frank's.
Displayed day 261, displayed level 71, cash $212,514.21.
Distinct cards currently in ungraded binder: 2,347.
Card Connossieur 2347/2500; Card Master 2347/4000; Legendary Shop 71/100;
Grading Master 195/500; TCG Arcade 0/50; Obsessed With Grades 5/10.
The leading grade index is 0 with grades 6,7,8,9,10; grades 1–5 remain.
Discovery totals: Tetramon 1318/1452, Destiny 1170/1452, Ghost 60/104,
Ascension 287/1452. Currently held variants are tracked separately.

Data and refresh
Private catalogue, runtime, generated observations and extracted data:
C:\Codex\PixelVaultData\achievement-tracking\tcg

Open ledger-latest.txt for a readable finance/stock/collection report.
cards-latest.json, stock-latest.json and finances-latest.json contain full
normalized data with source hash and save time. summary-latest.json is compact.
history stores one summary/financial ledger per distinct autosave content hash.
extracted-monsters.json, extracted-stock.json and extracted-english.json retain
the private installed-game reference data. No extracted game data is bundled
into the PixelVault application or uploaded elsewhere.

The Python standard-library reader checks every ten seconds while this Windows
user is signed in. Startup shortcut: PixelVault TCG Save Reader.lnk.
The existing PixelVault Companion worker consumes its JSON snapshots using the
pixelvault.json-file adapter in SaveChanges mode. The worker's serial queue can
take a minute or two. Native and Companion share the same tracker database.
The reader is currently running; no application binary or service was replaced.

The source is pinned to savedGames_Release0.json (autosave). It never selects a
different manual slot merely because that slot is newer. Steam autocloud account
and shop name must match. Save writes must be stable for two seconds. Missing
fields, corrupt JSON, unexpected catalogue slots, game updates and account/shop
changes preserve the last valid observation and produce an error in status.json.
Changed assembly SHA256 or catalogue size/timestamp requires revalidation.
An older source timestamp cannot replace observations. PixelVault also retains
its usual manual-progress protection, notes, stale-source review and history.
Neither the reader nor extraction tool writes to the game or save directory.

To pause a tracker, use PixelVault's automatic refresh settings. To stop all TCG
file generation, stop only the process identified in tcg/status.json and remove
the PixelVault TCG Save Reader startup shortcut. This leaves progress intact.
To restart, open that shortcut. An OS lock prevents duplicate watchers.

Mapping evidence and limits
Installed Assembly-CSharp.dll was decompiled for local read-only analysis.
InventoryBase in level2 binds stock asset 102251 and monster asset 102233 in
sharedassets2.assets. English terms come from resources.assets object 2943.
The card save index is shown-monster position * variants-per-monster + border
+ foil offset. Tetramon/Destiny/Ascension have 121 monsters * 12 variants each.
Ghost has 26 monsters * two foil states * two color sets. Unused array slots are
excluded. Megabot, Fantasy RPG and Cat Job reference catalogues are extracted
but not installed as collection goals because their packs are absent from the
active restock catalogue. Their valid ranges are still included in the exact
game album-count calculation if a save contains any.

CPlayerData.GetTotalCardCollectedAmount and GetCardCollectedAmount count distinct
positive ungraded binder quantities across supported expansions. Copies, ever-
collected flags, graded cards and cards elsewhere do not inflate those counters.
Day and shop level are zero-based in the save; displayed values add one.
Gem Mint count uses m_GameReportDataCollectPermanent.gemMintCardObtained.
PlayCardGameManager.ReportWinner passes m_GameReportDataCollect.duelWinCount to
OnDuelWin; this build's GameReportDataCollect.ResetData does not reset that field.
Obsessed With Grades mirrors OnCheckCollectedGradedCardSet: held cards plus
graded inventory, distinct grades grouped by cardSaveIndex. In graded inventory,
CompactCardDataAmount.amount stores a GRADE, not a copy quantity. The installed
achievement groups by save index without separating expansion or ghost color;
the tracker deliberately follows that implementation. This is a progress aid,
not a promise that Steam has awarded the achievement.

Stock counts use actual shelf compartments and package-box records, including
warehouse boxes. They exclude customer-held goods and machine/player reserved
items (exported separately). m_CurrentTotalItemCountList is a costing cache and
does not reconcile to these physical records; it is not used for stock totals.
Active retail products are the union of all shown shop tabs intersected with
the orderable restock catalogue. Crafted bulk boxes are not retail reserve goals.
Targets measure available units against one smallest carton, not forecast demand.
Saved price/cost fields are exported explicitly as raw values; no market-value
or sell-price estimate is inferred, and game rounding/fallbacks are not applied.

Finance uses the game's own end-of-day revenue + negative-cost calculation.
The rolling saved reports are chronological, capped at 30. Partial current-day
results are separate. The report's net figure includes upgrades and supply
purchases; it is not a stock-adjusted accounting profit. Currency is game money.
Dynamic finance details are in each source snapshot's evidence and the ledger,
not written into user-owned notes. The financial health conditions can reverse.

Maintenance and verification
build_catalogue.py requires UnityPy and TypeTreeGeneratorAPI only for extraction.
tcg_reader.py and test_reader.py use standard Python. Installed runtime is a copy
of tcg_reader.py; stop the TCG watcher before updating that runtime copy.
PixelVault.Tcg.Setup installs through TrackerRepository APIs, backs up SQLite,
refuses duplicate tracker identities, preserves manual overrides, and links the
six achievement IDs from existing provider metadata.

Eight tests passed: baseline counters, historical/current collection separation,
duplicate copies, physical stock vs costing cache, finance signs/day mapping,
graded-card field interpretation, exact duel field, malformed/new-schema rejects,
snapshot coverage, account boundary, pinned autosave, unchanged exports and
failure preservation. The installer was rehearsed against a database copy.
All 12 live source bindings completed a successful automatic check. All 9,045
item observations are known. Prior definitions and all game-save hashes were
verified unchanged. Grading Master was observed in the running PixelVault UI
with 195/500 and Automatic on.

Receipts, source evidence, pre-install backup path and verification:
C:\Codex\artifacts\tcg-trackers

November 5 presentation update: card buttons now read Collected and In Binder.
The reader keys historical/owned fields by stable .ever/.owned item IDs rather
than display text. Six achievement-linked trackers now use automatic artwork.
update_presentation.py performs the guarded, backed-up metadata migration while
preserving all item identities and progress. Live migration already completed;
receipt and verification are in artifacts/tcg-tracker-ui. Native 0.123.000 adds
PC-only custom tracker deletion with confirmation and scoped recovery JSON.
The reader only writes source snapshots; deleting a tracker does not reinstall it.

Pull-rate text (November 5, 2026)
The four collection trackers now show percentages for at least one copy of the
exact named variant per seven-card pack, rounded to six decimal places. Normal
and special packs, sequential border rolls, independent foil rolls, Ghost
replacement, Ghost color, species pool sizes and duplicate rules are included.
Rates apply after the tutorial at displayed shop level 3+. Matching Basic/Rare/
Epic/Legendary packs are named for Tetramon/Destiny; Ascension uses its full
121-species pool. Ghost shows both Tetramon and Destiny/Ascension routes.

The verified assembly is bc83eec70f3feea51a2271f3eced3589ea09a4d41061e7ff8702dee3b3a9b227.
CardOpeningSequence.GetGodPackRollIndex / GetPackContent / OpenScreen and
GetPackContentCardDataList are the source. Inclusive ranges and the 1130 overlap
are accounted for; there are 98,112 ordinary outcomes in 100,000 rolls. The
serialized CardUISetting flags confirm no duplicate monsters for Tetramon and
Destiny, and uniform draws with replacement for Ghost and Ascension. Probabilities
use exact rational arithmetic until display formatting. No Monte Carlo estimates.

pull_rates.py calculates the text; build_catalogue.py applies it to future
catalogue generation and refuses an unverified assembly. update_pull_rates.py
backs up the store and config, changes only existing card Detail fields and
records catalogue history. Deleted trackers are not recreated. Existing notes,
progress, source bindings, targets, IDs and links are preserved. Evidence and
rollback copies: artifacts/tcg-pull-rates/live. Sixteen tests passed (eight pull
rate checks plus the existing eight reader checks); all 8,920 milestone details
were verified. The running watcher consumed the new config and retains identical
normalized save data. Both apps read the shared definitions; reopen an existing
tracker or refresh the browser to load its latest details. No app rebuild.

Collected-only update (November 5, 2026)
The four collection definitions and generated snapshots now contain 4,460 .ever
items. The earlier .owned database observations remain stored for recovery but
are no longer visible fields or part of completion. cards-latest.json continues
to export current binder quantities. Achievement-linked collection counters
still follow the game's separate current-binder achievement logic.

Maintenance: collection_completion.py and update_collection_completion.py.
Both live config and build_catalogue.py generate the one-field collection.
Backups: artifacts/single-checkbox/live. The save reader executable did not
change; it consumes the updated config. Web uses a plain checkbox for any single
binary field; PC maintains its Collected button text. Eighteen Python tests and
three browser tests passed, plus live shared-repository validation.
