using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace PixelVaultNative
{
    /// <summary>One background auto-intake run (session ring buffer).</summary>
    internal sealed class BackgroundIntakeActivityBatch
    {
        public Guid Id { get; set; }
        public DateTime CompletedUtc { get; set; }
        public List<BackgroundIntakeActivityRow> Rows { get; } = new List<BackgroundIntakeActivityRow>();
    }

    /// <summary>One moved file (or sidecar) for the activity UI and selective undo.</summary>
    internal sealed class BackgroundIntakeActivityRow
    {
        /// <summary>Snapshot matching the undo manifest row at import time.</summary>
        public UndoImportEntry UndoSnapshot { get; set; }

        public string FileLabel { get; set; } = string.Empty;
        public string SourceFolder { get; set; } = string.Empty;
        public string RuleLabel { get; set; } = string.Empty;
        /// <summary>Best-effort path after move/sort (same resolution as undo).</summary>
        public string ResolvedLibraryPath { get; set; } = string.Empty;
        public bool Undone { get; set; }
    }

    internal sealed class BackgroundIntakeActivityRowModel : INotifyPropertyChanged
    {
        bool _isSelected;

        public DateTime BatchUtc { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public BackgroundIntakeActivityRow Row { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    internal sealed class UndoImportEntryEqualityComparer : IEqualityComparer<UndoImportEntry>
    {
        public static readonly UndoImportEntryEqualityComparer Instance = new UndoImportEntryEqualityComparer();

        public bool Equals(UndoImportEntry x, UndoImportEntry y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;
            return string.Equals(x.SourceDirectory ?? string.Empty, y.SourceDirectory ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.ImportedFileName ?? string.Empty, y.ImportedFileName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.CurrentPath ?? string.Empty, y.CurrentPath ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(UndoImportEntry obj)
        {
            if (obj == null) return 0;
            unchecked
            {
                var h = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SourceDirectory ?? string.Empty);
                h = (h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ImportedFileName ?? string.Empty);
                h = (h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.CurrentPath ?? string.Empty);
                return h;
            }
        }
    }

    /// <summary>Thread-safe session store for modeless activity UI (cap batches / rows).</summary>
    internal sealed class BackgroundIntakeActivitySession
    {
        const int MaxBatches = 10;
        const int MaxRowsTotal = 200;

        readonly object _sync = new object();
        readonly List<BackgroundIntakeActivityBatch> _batches = new List<BackgroundIntakeActivityBatch>();

        public void AddBatch(BackgroundIntakeActivityBatch batch)
        {
            if (batch == null || batch.Rows.Count == 0) return;
            lock (_sync)
            {
                _batches.Insert(0, batch);
                while (_batches.Count > MaxBatches)
                    _batches.RemoveAt(_batches.Count - 1);
                TrimRowsLocked();
            }
        }

        void TrimRowsLocked()
        {
            while (true)
            {
                var total = _batches.Sum(b => b.Rows.Count);
                if (total <= MaxRowsTotal || _batches.Count == 0) break;
                _batches.RemoveAt(_batches.Count - 1);
            }
        }

        public List<BackgroundIntakeActivityBatch> GetBatchesSnapshot()
        {
            lock (_sync)
            {
                var list = new List<BackgroundIntakeActivityBatch>();
                foreach (var b in _batches)
                {
                    var nb = new BackgroundIntakeActivityBatch { Id = b.Id, CompletedUtc = b.CompletedUtc };
                    foreach (var r in b.Rows)
                    {
                        nb.Rows.Add(new BackgroundIntakeActivityRow
                        {
                            UndoSnapshot = CloneEntry(r.UndoSnapshot),
                            FileLabel = r.FileLabel,
                            SourceFolder = r.SourceFolder,
                            RuleLabel = r.RuleLabel,
                            ResolvedLibraryPath = r.ResolvedLibraryPath,
                            Undone = r.Undone
                        });
                    }
                    list.Add(nb);
                }
                return list;
            }
        }

        public void MarkUndone(IEnumerable<UndoImportEntry> undoneEntries)
        {
            if (undoneEntries == null) return;
            var set = new HashSet<UndoImportEntry>(undoneEntries.Where(e => e != null), UndoImportEntryEqualityComparer.Instance);
            if (set.Count == 0) return;
            lock (_sync)
            {
                foreach (var batch in _batches)
                {
                    foreach (var row in batch.Rows)
                    {
                        if (row.Undone) continue;
                        if (row.UndoSnapshot != null && set.Contains(row.UndoSnapshot)) row.Undone = true;
                    }
                }
            }
        }

        internal static UndoImportEntry CloneEntry(UndoImportEntry e)
        {
            if (e == null) return null;
            return new UndoImportEntry
            {
                SourceDirectory = e.SourceDirectory,
                ImportedFileName = e.ImportedFileName,
                CurrentPath = e.CurrentPath
            };
        }

        /// <summary>Remove successfully undone entries from manifest; keep skipped failures and all non-selected rows.</summary>
        internal static List<UndoImportEntry> MergeManifestAfterPartialUndo(
            List<UndoImportEntry> fullManifest,
            IReadOnlyCollection<UndoImportEntry> selectedForUndo,
            UndoImportExecutionResult undoResult)
        {
            var selected = new HashSet<UndoImportEntry>(selectedForUndo ?? Array.Empty<UndoImportEntry>(), UndoImportEntryEqualityComparer.Instance);
            var skipped = new HashSet<UndoImportEntry>(undoResult?.RemainingEntries ?? new List<UndoImportEntry>(), UndoImportEntryEqualityComparer.Instance);
            var newManifest = new List<UndoImportEntry>();
            foreach (var e in fullManifest ?? new List<UndoImportEntry>())
            {
                if (e == null) continue;
                if (!selected.Contains(e))
                {
                    newManifest.Add(CloneEntry(e));
                    continue;
                }
                if (skipped.Contains(e)) newManifest.Add(CloneEntry(e));
            }
            return newManifest;
        }
    }
}
