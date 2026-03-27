using System;

namespace PixelVaultNative
{
    sealed class LibraryFolderInfo
    {
        public string GameId;
        public string Name;
        public string FolderPath;
        public int FileCount;
        public string PreviewImagePath;
        public string PlatformLabel;
        public string[] FilePaths;
        public string SteamAppId;
        public string SteamGridDbId;
        public bool SuppressSteamAppIdAutoResolve;
        public bool SuppressSteamGridDbIdAutoResolve;
    }

    sealed class GameIndexEditorRow
    {
        public string GameId { get; set; }
        public string Name { get; set; }
        public string PlatformLabel { get; set; }
        public string SteamAppId { get; set; }
        public string SteamGridDbId { get; set; }
        public bool SuppressSteamAppIdAutoResolve { get; set; }
        public bool SuppressSteamGridDbIdAutoResolve { get; set; }
        public int FileCount { get; set; }
        public string FolderPath { get; set; }
        public string PreviewImagePath { get; set; }
        public string[] FilePaths { get; set; }
    }

    sealed class PhotoIndexEditorRow
    {
        public string FilePath { get; set; }
        public string Stamp { get; set; }
        public string GameId { get; set; }
        public string ConsoleLabel { get; set; }
        public string TagText { get; set; }
    }

    sealed class LibraryMetadataIndexEntry
    {
        public string FilePath;
        public string Stamp;
        public string GameId;
        public string ConsoleLabel;
        public string TagText;
    }

    sealed class VideoClipInfo
    {
        public double DurationSeconds;
        public int Width;
        public int Height;
        public double FrameRate;
        public bool HasAudio;
        public string VideoCodec;
    }
}
