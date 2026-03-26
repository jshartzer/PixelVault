# PixelVault To Do

## Current Priority
1. Expand FFmpeg-backed video handling.
- Move beyond poster generation and make clips a more first-class library workflow.

2. Stress-test the Library virtualization and lazy-loading paths.
- Exercise large folder lists, large mixed-media folders, slider resize behavior, and scroll-triggered lazy rendering so the new Library UI changes settle in cleanly.

## Refactor Order
- Pure models and helpers first.
- Storage and indexing second.
- Media-tool wrappers third.
- Import orchestration fourth.
- UI wiring last.

