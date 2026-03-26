# PixelVault To Do

## Current Priority
1. Continue the modular refactor with the next backend slice.
- Finish peeling remaining indexing and metadata/game-assignment helpers out of `C:\Codex\src\PixelVault.Native\PixelVault.Native.cs`.
- Keep each extraction behavior-preserving and low risk.

2. Add verification around Library regrouping and delete flows.
- Focus on selection-aware metadata edits, permanent delete, regrouping, and thumbnail preservation.

3. Batch ExifTool metadata writes.
- Apply this to imports and library edits once the wrapper seam exists.

4. Add bounded parallel library scan workers.
- Improve throughput without overwhelming the NAS.

5. Virtualize and lazy-load the Library UI.
- Include background thumbnail and video poster generation so browsing stays responsive with large libraries.

6. Expand FFmpeg-backed video handling.
- Move beyond poster generation and make clips a more first-class library workflow.

## Refactor Order
- Pure models and helpers first.
- Storage and indexing second.
- Media-tool wrappers third.
- Import orchestration fourth.
- UI wiring last.

