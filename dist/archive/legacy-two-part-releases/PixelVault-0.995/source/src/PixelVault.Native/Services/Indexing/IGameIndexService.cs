using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    /// <summary>
    /// Game index editor load/save orchestration (folder context, merge, alignment, aliases) — Phase 1a seam off <see cref="MainWindow"/>.
    /// </summary>
    internal interface IGameIndexService
    {
        /// <summary>Load merged rows for the game index editor (thread-safe when <paramref name="setUiStatus"/> is null).</summary>
        List<GameIndexEditorRow> LoadEditorRowsCore(string root, Action<string> setUiStatus);

        /// <summary>Persist editor rows, align folders, apply ID aliases, refresh caches.</summary>
        void SaveEditorRows(string root, IEnumerable<GameIndexEditorRow> rows);

        /// <summary>
        /// Saved game-index rows for <paramref name="root"/>: uses <see cref="ILibrarySession.LoadSavedGameIndexRows"/> when
        /// <paramref name="root"/> matches the host library root, otherwise persistence for that root.
        /// </summary>
        List<GameIndexEditorRow> GetSavedRowsForRoot(string root);
    }
}
