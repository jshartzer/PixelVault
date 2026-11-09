# Centurion collection and rewards

Native 0.127.001 / Companion 0.51.1, November 7, 2026.

Game profiles display a regular hexagonal badge socket with six equal sides and no caption, in a widened badge column to the left of five recessed achievement sockets. Earned achievements use existing game-specific artwork. Death Stranding Director's Cut has the approved engraved metal-rimmed badge. No per-item checkmarks or collection labels and no bottom progress explanation. The compact heading tally remains.

The badge requires all five durable, verified, account-specific Centurion unlocks. It grants no extra PV points. The interviews catalogue now participates in the same guarded bound-source transaction as roads, mails, orders and cargo. Manual edits, generic imports, stale observations, failed reads and another account cannot unlock it. Existing first-unlock dates remain durable.

Each achievement has an independent reward presentation using its own title and art. The badge follows the final achievement in a separate, larger celebration. Notifications use a sweep, particles and a hexagonal pulse; reduced-motion/high-contrast preferences are honored. Dismiss after five/seven seconds, pause on hover/focus, and allow manual dismissal. Native honors the existing notification enable and fullscreen suppression settings.

Delivery histories are separate from progress and ordinary update summaries. Native persists `centurion-rewards.json` in its data root; Companion uses workspace/account-scoped browser storage. The first observation baselines historical unlocks without a popup. Later verified unlocks enqueue once per client. Returning from missing data cannot replay an acknowledged reward; account switches clear pending presentation. Closing a client during a queued reward can suppress its remaining celebration because acknowledgement is persisted before presentation. Earned badges remain visible on profiles.

Authenticated read-only endpoints: `/api/v1/centurion`, `/api/v1/centurion/{provider}/{game}/badge`. No demo or preview controls ship in production. No artificial unlocks are written during deployment.

Verification: 55 Native Century/Centurion checks, 3 authenticated Companion update/reward checks, 23 browser/JavaScript checks including all six rewards, ordering, pausing, reload, account changes, 320/390/1280 widths and reduced motion. Rendered native and web screenshots and deployment evidence are in `C:\Codex\artifacts\centurion-live`.
