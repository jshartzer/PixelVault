namespace PixelVaultNative
{
    /// <summary>
    /// High-level library UI workspace (plan <c>PV-PLN-LIBWS-001</c>).
    /// Distinct from folder grouping presets (all/console/folders) except that Timeline grouping maps to <see cref="Timeline"/>.
    /// </summary>
    internal enum LibraryWorkspaceMode
    {
        /// <summary>Folder grid is the primary surface.</summary>
        Folder = 0,

        /// <summary>Captures/detail workspace for the active game (future shell takeover).</summary>
        Photo = 1,

        /// <summary>Timeline/calendar grouping — own layout contract, not Photo.</summary>
        Timeline = 2
    }
}
