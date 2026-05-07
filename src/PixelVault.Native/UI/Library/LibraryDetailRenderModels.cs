using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    internal sealed class LibraryDetailMediaLayoutInfo
    {
        internal long LastWriteUtcTicks;
        internal long FileLength;
        internal int PixelWidth;
        internal int PixelHeight;
        internal bool IsVideo;
    }

    /// <summary>Mutable snapshot for one library detail-pane virtualized render pass (day groups + visible file list).</summary>
    internal sealed class LibraryDetailRenderSnapshot
    {
        public List<LibraryDetailRenderGroup> Groups = new List<LibraryDetailRenderGroup>();
        public List<string> VisibleFiles = new List<string>();
        public Dictionary<string, LibraryTimelineCaptureContext> TimelineContextByFile = new Dictionary<string, LibraryTimelineCaptureContext>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, LibraryDetailMediaLayoutInfo> MediaLayoutByFile = new Dictionary<string, LibraryDetailMediaLayoutInfo>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class LibraryDetailRenderGroup
    {
        public DateTime CaptureDate;
        public List<string> Files = new List<string>();
    }

    internal sealed class LibraryTimelineCaptureContext
    {
        internal string GameTitle;
        internal string PlatformLabel;
        internal DateTime CaptureDate;
        internal string Comment;
    }

    internal sealed class LibraryDetailViewportFileOrder
    {
        internal readonly List<string> PrimaryFiles;
        internal readonly List<string> DeferredFiles;

        internal LibraryDetailViewportFileOrder(List<string> primaryFiles, List<string> deferredFiles)
        {
            PrimaryFiles = primaryFiles ?? new List<string>();
            DeferredFiles = deferredFiles ?? new List<string>();
        }

        internal List<string> AllFiles()
        {
            var all = new List<string>(PrimaryFiles.Count + DeferredFiles.Count);
            all.AddRange(PrimaryFiles);
            all.AddRange(DeferredFiles);
            return all;
        }
    }
}
