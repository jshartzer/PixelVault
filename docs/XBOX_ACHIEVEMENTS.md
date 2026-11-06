# Xbox achievements

PixelVault's native Achievement Hub has an **Xbox achievements** button. The same
window is available from **Settings > Achievements > Xbox account and games**.
Connect opens Microsoft's sign-in page in the default browser. After sign-in,
select a game and choose **View achievements**. The existing achievement viewer
supports refresh, earned/remaining filters, artwork and unlock dates.

## Duplicate catalogue names

When achievement history gives several title IDs the same name, the bridge uses
a nonblank matching titlehub name to distinguish editions, such as Minecraft for
Windows and Minecraft for iOS. Title IDs remain authoritative. Saved explicit
profile links resolve ambiguous capture-library matches. The November 3 repair
and verification are recorded in `artifacts/minecraft-destiny-links/review.txt`.

## Setup

Run `scripts/Install-PixelVaultXbox.ps1` with Python 3.10 or newer available. This
creates a dedicated virtual environment under `%LOCALAPPDATA%\PixelVault\Xbox`.
The native build/publish copies `Xbox/bridge.py` next to the application. Install
the runtime separately for each Windows user running PixelVault.

OpenXbox is pinned to commit `672f34f256f18640d387147867950234fc2e140f`:
https://github.com/OpenXbox/xbox-webapi-python
It provides the signed HTTP session, Microsoft OAuth authentication and Xbox token
refresh. The bridge uses its session against the achievement endpoints directly
to retain optional fields (including rarity) omitted by upstream response models.
The upstream public desktop application identity and registered localhost:8080
callback are used; no client secret is needed. The account must be permitted to
access Xbox services. Sign-in availability remains subject to Microsoft accepting
this application identity.

## Storage and behavior

- OAuth tokens are encrypted with Windows DPAPI for the current Windows user.
  Passwords and tokens are never returned to the native app, placed in command
  arguments, or written to application logs. The callback binds only to loopback,
  validates OAuth state and times out after four minutes.
- Title history is fetched for both the modern and Xbox 360 APIs. Continuation
  tokens are followed; malformed/repeating pages fail the entire operation.
- Achievement snapshots use the existing snapshot store, isolated by Xbox XUID
  and title ID. Failures preserve saved progress and explicitly label cached data.
- Disconnect removes sign-in and the visible library. Account-scoped cached
  achievements remain on disk and are accessible only after reconnecting.
- Gamerscore remains provider metadata. Xbox achievements earn durable PV points
  under their Xbox XUID using the existing scoring rules. Missing global rarity
  starts at five provisional points and can finalize later; repeated syncs do not
  create duplicate awards.

Native 0.118.000 includes Xbox titles in the combined Hub collection, unlock timeline
and score. In Native 0.119.000, choose **Settings > Achievements > Sync all Xbox
achievements** to import/update every game. Cancel sync or leaving Settings cancels
remaining requests and retains completed snapshots.
For command-line maintenance use `PixelVault.exe --sync-xbox C:\Codex\PixelVaultData`
with an optional comma-separated list of Xbox title IDs as the third argument.

Snapshots now live in the canonical application's `achievement-hub` store. The
non-secret `xbox-library.json` catalogue supplies game titles and the account identity
to Companion. Older private Xbox snapshots are imported when Sync achievements runs.
Companion 0.45.0 displays Xbox games and reads updates from the same canonical
snapshots; the historical connector/export path also supports Xbox after explicitly
adding the XUID to its approved achievement accounts. Microsoft tokens stay in the
Windows user's private encrypted store. Switching to a different Xbox account
requires updating Companion's approved scope; an account mismatch fails closed.
Native game profiles now offer **Default Platform**, **Sync game**, and **Link Xbox
game…**. The preference is saved per profile in `game-profile-platforms.json`,
including the provider and provider game ID. Name matches (ignoring whitespace, trademark symbols and equivalent trailing
Roman/Arabic sequel numbers such as Alan Wake II / Alan Wake 2) appear automatically; editions with different names need an
explicit link. Multiple Xbox editions require a selection. An unavailable saved
platform never silently falls back to another platform. Switching uses that
provider/account's saved achievements; Sync game refreshes only that title. Switching
or closing cancels the old display request. Xbox completion does not mark a Steam
capture folder complete, and Steam Century/tracker state is not shown as Xbox data.
These controls are native-only; Companion is unchanged.

Background Xbox unlock notifications remain a separate feature.

## Verification

`python -m unittest discover -s tests -p test_xbox_bridge.py` covers pagination,
schema/identity rejection, unknown/revoked progress, invalid dates/counters and
Windows token encryption. Live account retrieval requires interactive sign-in;
offline tests do not establish live endpoint availability.

Verified November 3, 2026: Microsoft sign-in succeeded; 569 title-history entries
were retrieved. A Plague Tale: Innocence returned 35 achievements with 34 earned.
A World of Keflings returned 21 achievements with 1 earned after merging the
Xbox 360 earned endpoint into its catalogue. Nine Python tests and two native
snapshot isolation/preservation tests passed. Native build completed without errors.

Native release: `C:\Codex\dist\PixelVault-0.118.000\PixelVault.exe`; the normal
PixelVault shortcut points here. Companion PC service 0.45.0 is live at
http://192.168.1.69:8787/. Its existing save-reader and photo-worker paths are retained.
Verified the two initial games in the canonical stores: 56 achievements, 35 earned,
175 provisional PV points. Remaining historical games require Sync achievements.
77 native scoring/Hub tests and 17 Companion achievement/direct-local tests pass.
Deployment receipt and read-only verification: `artifacts/xbox-integration-20261103`.

Verified November 3, 2026 for Native 0.119.000: Batman: AA GOTY (1396901900)
now syncs successfully with 47 achievements, 30 earned. Its unspecified sequence
4294967295 now falls back to response order within signed Int32. Eleven bridge
checks and 127 native profile, Xbox, Hub and scoring checks pass. Desktop visual
verification was unavailable because both computer-use runtimes failed to initialize.

Native 0.119.005 fixes large modern Xbox artwork: Diablo IV's 1920x1080 PNGs
exceed the previous 768 KiB image cap. Only the Xbox HTTPS image host gets an 8 MiB
allowance; other hosts retain the standard cap. The memory cache has a 64 MiB byte
threshold in addition to its entry limit. Existing URLs need no resync. Twenty-six
artwork/detail tests pass and the live Diablo image downloads and decodes via WPF.
Xbox 360 artwork extraction remains a separate known gap (legacy payloads do not
use the modern mediaAssets field); this fix addresses modern Xbox image downloads.

Native 0.119.010 fixes Xbox title selection in the link dialog, adds Show all games
for recovering from a search with no matches, and styles the native platform panel
and picker. A real WPF modal selection/link submission test verifies the returned
title and persisted default. Unsupported profiles show No linked platform until
an Xbox title is linked or a supported provider ID is added in Edit Metadata.

Native 0.120.000 makes the library achievement window honor the profile's saved
platform and use the same account-scoped cache/loading path. A Platform selector
switches between linked providers, saves the preference, and updates the open
profile when the viewer closes. Hub/timeline entries retain the explicitly opened
provider initially. Switching cancels the previous request, clears old progress,
scoping and pins, and rejects late responses. Failed refreshes preserve loaded
status. Xbox progress is never copied into Steam. Sort and Missable share the
profile selector theme and wrap at narrow widths. Kabuto Park regression: Xbox
2026288209 has 16 of 21 earned; the previous viewer incorrectly opened Steam.

Native 0.120.005 crops Xbox landscape artwork to a centered square in the shared
badge loader, before downsampling and grayscale. Kabuto Park (2026288209) has a
verified 512px square badge centered inside 1920x1080 artwork; its proportional
crop applies only when the image retains that 16:9 layout. Other landscape Xbox
art uses the full-height center square. Original bytes/URLs remain untouched;
square/portrait art and other providers are unaffected. The change applies across
native achievement views and requires no resync. Thirty-six artwork/detail tests
pass; actual Kabuto Park, Diablo IV and Alan Wake II crops were inspected.

November 3 review — Native 0.120.006 / Companion 0.45.1:

- Fixed the Hub pin validator rejecting Xbox game keys.
- Bulk sync now exposes request failures instead of converting them to cached
  successes, so HTTP 429/401/403 stops further requests. Interactive views still
  retain saved progress on a failed refresh.
- Companion's icon proxy previously rejected every Xbox CDN URL and capped images
  at 768 KiB. It now accepts only HTTPS images-eds-ssl.xboxlive.com/image requests
  on the default port, with redirects disabled and an 8 MiB streamed limit.
  Other providers retain their previous limits.
- Companion cards and achievement details use native's centered landscape crop,
  including Kabuto Park's verified inner badge. Portrait and square artwork is
  preserved; cache files retain original bytes. Browser assets have new revisions.
- Verified canonical Xbox snapshots populate the direct-local Companion projection;
  changes refresh without a service restart, unknown progress stays unknown, and
  Steam/Xbox accounts, progress and completion remain separate. Browser tests cover
  cards/details at 390px and 1280px under the production content security policy.

Remaining review limitations: Xbox 360 payloads contain image IDs rather than the
modern mediaAssets array, so their artwork still needs a legacy mapping. Companion
groups editions by normalized title, without native's explicit Xbox links or Roman
numeral alias matching; differently named editions can appear as separate cards.
Default Platform and per-game Xbox sync controls remain native-only as requested.
The title catalogue currently does not import provider last-played timestamps.
Catalogue entries without an achievement snapshot show unknown totals until synced.

Native 0.121.000 adds a built-in Xbox 360 category and importer/review checkbox.
Library sections and profile/achievement labels display Xbox 360 while provider
keys remain `xbox:<titleId>`; pins, achievements and account scopes retain their IDs.
The canonical index migration (`--classify-xbox360 <data-root>`) backs up SQLite,
then changes only confirmed Xbox rows in the configured library. Title matching
ignores punctuation/trademark formatting and handles Roman sequel numbers; linked
title IDs take precedence. Modern/legacy same-name conflicts are left alone.
The November 3 migration updated six games: BattleBlock Theater, Dragon Age Origins,
Fable III, Mass Effect 3, Sonic & Knuckles and Sonic The Hedgehog 2. Destiny,
Minecraft and Prey remain Xbox because the API contains both generations without
an explicit matching link. All 321 game rows were compared with the backup: only
the six category fields changed. Evidence: artifacts/xbox360-category.

Native 0.122.000 / Companion 0.46.0 closes the three follow-up gaps:

- Shared edition grouping reads canonical game-profile-platforms.json, keyed by
  native game ID, with explicit Xbox title links taking precedence over name
  aliases. Native and Companion share Roman-number normalization. Named editions
  and remasters stay distinct unless explicitly linked. Original web title hashes
  remain aliases for pins; merging changes neither achievement IDs nor progress.
  The links file participates in direct-local freshness checks. Existing web
  provider routes, platform-specific completion, screenshots and tracker scope
  remain intact. These links apply to the direct-local host; historical connector
  deployments retain title-based grouping.
- Xbox 360 image URLs use HTTPS image-ssl.xboxlive.com with hexadecimal title and
  image IDs from the provider payload. The web proxy validates the exact host,
  requested title/path and unsigned image ID; redirects remain disabled.
- Modern last-played times come from titlehub titleHistory.lastTimePlayed; Xbox
  360 also uses the legacy history's lastPlayed field. Invalid/future/missing dates
  remain unknown, never replaced by lastUnlock or the refresh timestamp. Native
  recent cards immediately see newer catalogue dates.

Live verification refreshed 17 previously saved legacy games (762 artwork rows,
zero unavailable), downloaded three legacy PNGs through the Companion proxy, and
found valid play dates for 553 of 569 catalogue titles. Two duplicate web groups
merge: Alan Wake II/Alan Wake 2 and the long/short Vampire Crawlers names.
Evidence: artifacts/xbox-gaps. Default Platform and sync controls stay native-only.


Native 0.122.001 / Companion 0.46.1 corrects same-name release collisions.
Dead Space's native row is Steam 1693980 / IGDB 159119, released 2023-01-27,
with Xbox Series X|S platform metadata. Xbox history contains modern title
2008403666 and legacy title 1161889879. The old punctuation-normalized title
hash merged both before explicit-link matching even ran.

Grouping now starts with provider/title IDs. Automatic Xbox links require one
compatible candidate; canonical platform metadata excludes an incompatible
Xbox generation. Multiple candidates or conflicting known catalogue IDs stay
separate. A group cannot contain two different IDs from one provider. Explicit
native links take precedence, including saved Xbox choices, and do not pull in
additional same-name editions. Native profile choices and 360 classification
use the same matcher. The linker shows generation and title ID. No release year
is guessed from last-played dates. An incomplete provider catalogue is not a
universal cross-store identity database; explicit links remain available when
metadata is missing or a release cannot be identified confidently.

Dead Space now groups Steam with the modern Xbox title and keeps the 360 original
separate. Native category metadata was already correct; no canonical index,
account, achievements or saved preferences needed modification. Historical title
pins resolve to one retained group instead of appearing on every split sibling.
The real library has 885 editions in 735 groups after separating unsafe title
merges (22 repeated Xbox title names, including non-game applications). 18 native,
33 Companion and 18 browser/library checks pass. Read-only evidence is under
artifacts/edition-matching/live-proof.json. Deployment status is recorded in
CURRENT_BUILD.txt and HANDOFF.md; a staged package alone is not proof of rollout.


Companion 0.46.2 fixes an overbroad same-provider guard introduced in 0.46.1:
`library` entries are capture records, not one achievement provider. Their
identity scope now includes the console label, allowing PS5/Xbox/Steam editions
of the same named release to group. Same-console duplicate records, different
Steam/Xbox IDs and conflicting known IGDB identities retain the conservative
separation rules. Already-grouped capture records count as one source when
checking automatic Xbox-link ambiguity. Edition-derived historical pin aliases
preserve pins when a split card rejoins its original group.

Canonical Death Stranding Director's Cut now groups library:G00020 (PS5),
library:G00021 (Xbox) and steam:1850570, with 12 trackers, 46/63 Steam achievements
and the existing screenshot gallery. Read-only verification also asserts that
Dead Space's 360 original is still separate from the remake. The unavailable
Achievements card uses the existing padded row layout and distinguishes missing
provider data from a real platform choice. 36 server and 21 library/browser checks
pass. Evidence: artifacts/all-console-groups; deployment status is recorded in
CURRENT_BUILD.txt/HANDOFF.md, not inferred from package creation.
