# PixelVault To Do

## Current Priority
1. Watch for any remaining Library UI extraction or polish opportunities.
- Focus on targeted cleanup only if the current browse, metadata, and virtualization passes uncover a concrete pain point.

## Recently Completed
1. Expanded FFmpeg-backed video handling.
- Added cached clip metadata probing plus richer Library clip actions so videos are more first-class than simple poster-backed tiles.

2. Stress-tested the Library virtualization and lazy-loading paths.
- Hardened resize-heavy folder/detail browsing so layout-only changes preserve scroll position more reliably, and added a repeatable mixed-media stress dataset generator for future verification.

## Refactor Order
- Pure models and helpers first.
- Storage and indexing second.
- Media-tool wrappers third.
- Import orchestration fourth.
- UI wiring last.

