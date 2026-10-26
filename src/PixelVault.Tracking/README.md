# Shared tracker domain

This .NET 8 library owns tracker definitions/catalogues, models, SQLite persistence, manual/source separation, reconciliation, history/undo, provenance and guarded client edits. Native Windows and PixelVault Companion consume these rules. The `PixelVaultNative` type namespace is retained for source compatibility; this assembly has no WPF, game DLL, save-reader or UI dependency.

`TrackerRepository` implements `ITrackerRepository`. The native `AchievementTrackingStore` is a small derived wrapper retaining maps, achievement choices, authoring extensions and the native adapter registry. Windows reader execution/scheduling remain in the native application; the server only uses stored state.

`ReadEditor` returns a coherent snapshot of values and field versions. `Edit` and `UndoCommand` keep validation, request deduplication, state and history in one write transaction. Progress versions include source observations; notes are independent. Native confirmation comes from typed, non-undone reported-field history matching current values, not display labels or the current binding.

SQLite must be local to its host; remote clients use the API. Existing Desktop schema/serialization are retained, with an additive `tracker_client_request` table on the first guarded write. Routine reads never start a game reader.
