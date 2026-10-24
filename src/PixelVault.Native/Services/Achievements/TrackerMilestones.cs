using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PixelVaultNative
{
    /// <summary>Named binary completion columns, shared by manual and spreadsheet trackers.</summary>
    static class TrackerMilestones
    {
        internal const int Maximum = 16;
        internal static List<string> Labels(IEnumerable<string> labels)
        {
            var result = labels?.Select(s => (s ?? "").Trim()).ToList() ?? new();
            if (result.Count > Maximum || result.Any(s => s.Length == 0 || s.Length > 80)
                || result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
                throw new InvalidDataException("Use up to 16 distinct completion names, each between 1 and 80 characters.");
            return result;
        }

        internal static string ItemId(string recordId, string label) => "box." + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(recordId + "\0" + label)));

        internal static List<AchievementTrackerItem> Expand(IEnumerable<AchievementTrackerItem> records, IEnumerable<string> labels)
        {
            var names = Labels(labels); var rows = records.ToList();
            if (names.Count == 0) return rows;
            if (rows.Count * names.Count > 10000) throw new InvalidDataException("The tracker can contain at most 10,000 completion boxes. Reduce the items or columns.");
            if (rows.Any(i => i.Target != 1)) throw new InvalidDataException("Multiple completion boxes are binary. Remove the target-count mapping or use a target of 1.");
            return rows.SelectMany(row => names.Select((name, index) => new AchievementTrackerItem
            {
                Id = ItemId(row.Id, name), GroupId = row.Id, Milestone = name, Name = row.Name,
                Category = row.Category, Detail = row.Detail, Order = row.Order * names.Count + index,
                Identifiers = new Dictionary<string, string>(row.Identifiers)
            })).ToList();
        }
    }

    sealed class TrackerMilestoneColumn
    {
        public string Label { get; set; }
        public string Header { get; set; }
    }
}
