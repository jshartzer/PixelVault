# Desktop-to-Companion tracker names

October25,2026 locally — deployed with NAS Companion and installed PC connector0.7.1. UTC backup filenames are dated October26.

The desktop Rename action was working locally. The Companion connector previously published new custom definitions only and deliberately ignored changed existing definitions. Built-in display-name overrides in `tracker_title` were also absent from its discovery. The reported mismatch was custom.f2fec73d74664bd0b56b73561eb6f840: desktop **Road Building**, NAS **Road Bultin**,52 items.

The follow-up treats names as separate metadata instead of republishing checklist definitions. Read-only PC discovery includes custom names and bundled overrides for approved workspace accounts. The NAS uses prior publication digests, paired definitions, canonical bundled names and persisted common-name baselines to establish safe initial/update convergence. Independently changed web names remain protected as conflicts. Only display names change; IDs, checklist items, notes, counts, sources, automatic settings, pairing and save permissions are retained. Source definition comparison is title-neutral while preserving every other authorization field.

The coordinated web-app chat owns connector/server code, tests, packaged QA, backups and NAS/connector deployment. This chat verified the native rename and took read-only fingerprints of native aliases, authored/manual state and source/automatic settings. No native code or shared build output is changed by this follow-up. Final actual web convergence and preservation evidence will be added after deployment.

## Production verification

The retained real browser session now displays **Road Building**. Native and web names converge automatically through the paired running PC connector, including bundled overrides. Source/library/publication/achievement checks are healthy, no name conflicts or review jobs are pending, and pairing/startup/read-path permissions are retained.

The initial read showed4/52, but the immutable fresh pre-upgrade backup already contains **19/52**; the updated live app also contains19/52. This reflects intervening user work and must not be rolled back to the stale count. Original tracker state/history/source/automatic/link/import/catalogue-history table digests match before/after. All19 manual-value/note rows retain digest `9251D9BCF96E3F47B3A87ACCE8CEAEDF6B5D4AC6F72A62F91E8EF4D667F0273A`. The52-item title-neutral catalogue digest remains `6006C1F5525A3461EA522483376FEB086F49F2E740A2CFF4637E4F3776DBDAF9`; only title metadata changed. Token/workspace/config/source permission digest is unchanged, as are all7,413 achievement rows and676 guides. Native alias/manual/source/automatic read-only fingerprints also match; no Native code/build/data change was required for this follow-up.

Validation:64 server,7 connector and17 JS tests pass. Packaged QA verifies custom/bundled names, preservation of independently changed NAS names as conflicts, unchanged source permissions, saved progress/notes, and an unsaved note draft surviving reload. Live mobile proof: `C:/Codex/tmp/companion-tracker-titles-mobile-0.7.1.png`. QA processes stopped and viewport reset.

Fresh NAS backup: `pre-0.7.1-20261026T011200Z.tar`, SHA256 `26A08ED6E99ED386C76915BAE507EC83EBFD46FC56649B1464D2C1D5382E1DD5`, with a protected PC copy. Image ID: `0aa9d6473ed335477382d6beb4ad09539b23fce7a27fc1537485c245509492b3`. Installed connector is app-0.7.1. Common operation/handoff/changelog/deployment metadata are maintained by the coordinated web-app chat.
