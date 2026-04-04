namespace PixelVaultNative
{
    /// <summary>Values persisted in PixelVault.settings.ini (and runtime defaults before merge).</summary>
    public sealed class AppSettings
    {
        public string SourceRootsSerialized = string.Empty;
        public string DestinationRoot = string.Empty;
        public string LibraryRoot = string.Empty;
        public string ExifToolPath = string.Empty;
        public string FfmpegPath = string.Empty;
        public string SteamGridDbApiToken = string.Empty;
        public int LibraryFolderTileSize = 240;
        public string LibraryFolderSortMode = "platform";
        public string LibraryGroupingMode = "all";

        public static AppSettings Clone(AppSettings s)
        {
            if (s == null) return new AppSettings();
            return new AppSettings
            {
                SourceRootsSerialized = s.SourceRootsSerialized ?? string.Empty,
                DestinationRoot = s.DestinationRoot ?? string.Empty,
                LibraryRoot = s.LibraryRoot ?? string.Empty,
                ExifToolPath = s.ExifToolPath ?? string.Empty,
                FfmpegPath = s.FfmpegPath ?? string.Empty,
                SteamGridDbApiToken = s.SteamGridDbApiToken ?? string.Empty,
                LibraryFolderTileSize = s.LibraryFolderTileSize,
                LibraryFolderSortMode = s.LibraryFolderSortMode ?? "platform",
                LibraryGroupingMode = s.LibraryGroupingMode ?? "all"
            };
        }
    }
}
