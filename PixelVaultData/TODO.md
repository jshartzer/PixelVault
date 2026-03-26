# PixelVault To Do

## Current Priority
1. Add verification around Library regrouping and delete flows.
- Focus on selection-aware metadata edits, permanent delete, regrouping, and thumbnail preservation.

2. Batch ExifTool metadata writes.
- Apply this to imports and library edits once the wrapper seam exists.

3. Add bounded parallel library scan workers.
- Improve throughput without overwhelming the NAS.

4. Virtualize and lazy-load the Library UI.
- Include background thumbnail and video poster generation so browsing stays responsive with large libraries.

5. Expand FFmpeg-backed video handling.
- Move beyond poster generation and make clips a more first-class library workflow.

## Refactor Order
- Pure models and helpers first.
- Storage and indexing second.
- Media-tool wrappers third.
- Import orchestration fourth.
- UI wiring last.

