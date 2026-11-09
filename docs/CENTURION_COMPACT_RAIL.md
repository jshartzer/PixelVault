# Centurion compact rail

Native 0.128.000 / Companion 0.52.0.

Replaces the game profile collection with the approved compact rail. The badge appears beside the heading and count; the five achievement icons remain in one row. Selecting an icon shows its title and requirements underneath. The selected title opens the existing full achievement detail view. On wide desktop profiles, the heading sits to the left of the rail and its detail text; narrower views stack the heading above it.

The section is visible only when the provider/game has at least one authored entry in CenturyAchievementCatalog. No entries means no heading, empty badge, placeholder row, or reserved section space. Defined games can display empty slots without an unlock or linked account. The web API exposes these definitions without reading progress for a missing account. Planned slots never count as achievements or earn credit.

Existing verified progress, badge eligibility, notification queue, first-visit baseline, account isolation and full achievement navigation are preserved. Badge caption remains absent and the empty hexagon retains equal-length sides.

Checks: Native hidden-game/selection/detail navigation and rendered layouts; web profile selection persistence, unsupported-game omission, one-row mobile layout at 320/390 pixels, desktop layout, reward sequence, accessibility/reduced motion and refresh regressions; authenticated API checks. Evidence: C:\Codex\artifacts\centurion-rail.
