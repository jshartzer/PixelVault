using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    internal sealed class WorkflowProgressLogBuffer
    {
        readonly List<string> _lines = new List<string>();
        readonly int _maxLines;
        bool _dirty;

        public WorkflowProgressLogBuffer(int maxLines)
        {
            _maxLines = Math.Max(1, maxLines);
        }

        public bool Append(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            _lines.Add(line);
            while (_lines.Count > _maxLines) _lines.RemoveAt(0);
            _dirty = true;
            return true;
        }

        public bool TryRender(out string text)
        {
            if (!_dirty)
            {
                text = string.Empty;
                return false;
            }

            text = string.Join(Environment.NewLine, _lines.ToArray());
            _dirty = false;
            return true;
        }

        public IReadOnlyList<string> SnapshotLines() => _lines.ToArray();
    }
}
