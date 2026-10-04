using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace PixelVaultNative
{
    /// <summary>Read-only Steam definition cache. Never reads local player stats or game saves.</summary>
    static class SteamAchievementCounters
    {
        internal sealed record Mapping(string Stat, double Minimum, double Target);
        const int MaxBytes = 8 * 1024 * 1024;

        internal static Dictionary<string, Mapping> LoadMappings(int appId)
        {
            var roots = new List<string>();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string path) roots.Add(path);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException) { }
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var path = Path.Combine(root, "appcache", "stats", "UserGameStatsSchema_" + appId.ToString(CultureInfo.InvariantCulture) + ".bin");
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (file.Length <= 0 || file.Length > MaxBytes) continue;
                    var bytes = new byte[(int)file.Length];
                    file.ReadExactly(bytes);
                    return ParseMappings(bytes, appId);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is ArgumentException) { }
            }
            return new Dictionary<string, Mapping>(StringComparer.Ordinal);
        }

        internal static Dictionary<string, Mapping> ParseMappings(byte[] bytes, int appId)
        {
            var result = new Dictionary<string, Mapping>(StringComparer.Ordinal);
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxBytes || appId <= 0) return result;
            try
            {
                using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
                var budget = 200000;
                var root = ReadObject(reader, 0, ref budget);
                var game = Child(root, appId.ToString(CultureInfo.InvariantCulture));
                var stats = Child(game, "stats");
                if (stats == null) return result;
                var conflicts = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in stats.Values.OfType<Dictionary<string, object>>())
                {
                    var bits = Child(entry, "bits");
                    if (bits == null) continue;
                    foreach (var achievement in bits.Values.OfType<Dictionary<string, object>>())
                    {
                        var name = String(achievement, "name");
                        var progress = Child(achievement, "progress");
                        var value = Child(progress, "value");
                        var stat = String(value, "operand1");
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(stat)
                            || String(value, "operation") != "statvalue" || value.Count != 2
                            || !Number(progress, "min_val", out var minimum) || !Number(progress, "max_val", out var target)
                            || minimum < 0 || target <= minimum) continue;
                        if (!result.TryAdd(name, new Mapping(stat, minimum, target))) conflicts.Add(name);
                    }
                }
                foreach (var name in conflicts) result.Remove(name);
                return result;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is OverflowException)
            {
                // A truncated, newer, or otherwise unsupported cache is not a source of guessed progress.
                return new Dictionary<string, Mapping>(StringComparer.Ordinal);
            }
        }

        static Dictionary<string, object> ReadObject(BinaryReader reader, int depth, ref int budget)
        {
            if (depth > 32) throw new InvalidDataException();
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            while (true)
            {
                var type = reader.ReadByte();
                if (type == 8) return result;
                if (--budget < 0) throw new InvalidDataException();
                var name = ReadString(reader);
                object value = type switch
                {
                    0 => ReadObject(reader, depth + 1, ref budget),
                    1 => ReadString(reader),
                    2 => reader.ReadInt32(),
                    3 => reader.ReadSingle(),
                    4 => reader.ReadUInt32(),
                    6 => reader.ReadUInt32(),
                    7 => reader.ReadUInt64(),
                    10 => reader.ReadInt64(),
                    _ => throw new InvalidDataException()
                };
                if (!result.TryAdd(name, value)) throw new InvalidDataException();
            }
        }

        static string ReadString(BinaryReader reader)
        {
            var bytes = new List<byte>();
            while (bytes.Count < 65536)
            {
                var b = reader.ReadByte();
                if (b == 0) return new UTF8Encoding(false, true).GetString(bytes.ToArray());
                bytes.Add(b);
            }
            throw new InvalidDataException();
        }

        static Dictionary<string, object> Child(Dictionary<string, object> parent, string key) =>
            parent != null && parent.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;
        static string String(Dictionary<string, object> parent, string key) =>
            parent != null && parent.TryGetValue(key, out var value) ? value as string : null;
        static bool Number(Dictionary<string, object> parent, string key, out double number)
        {
            number = 0;
            return parent != null && parent.TryGetValue(key, out var value)
                && double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                && double.IsFinite(number);
        }

        internal static Dictionary<string, double> ParseUserStats(string json, ulong account)
        {
            using var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            if (!doc.RootElement.TryGetProperty("playerstats", out var player) || player.ValueKind != JsonValueKind.Object
                || !player.TryGetProperty("steamID", out var id) || id.ToString() != account.ToString(CultureInfo.InvariantCulture)
                || player.TryGetProperty("error", out _)
                || (player.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                || !player.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Array) return result;
            var conflicts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var stat in stats.EnumerateArray())
            {
                if (stat.ValueKind != JsonValueKind.Object || !stat.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(name.GetString()) || !stat.TryGetProperty("value", out var value)
                    || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)
                    || !double.IsFinite(number) || number < 0) continue;
                if (!result.TryAdd(name.GetString(), number)) conflicts.Add(name.GetString());
            }
            foreach (var name in conflicts) result.Remove(name);
            return result;
        }

        internal static void Apply(IEnumerable<GameAchievementsFetchService.AchievementRow> rows,
            IReadOnlyDictionary<string, Mapping> mappings, IReadOnlyDictionary<string, double> stats)
        {
            foreach (var row in rows)
            {
                row.CounterCurrent = row.CounterMinimum = row.CounterTarget = null;
                if (row.Provider != GameAchievementsFetchService.SteamProvider || !row.ProgressKnown
                    || row.SteamApiName == null || !mappings.TryGetValue(row.SteamApiName, out var mapping)
                    || !stats.TryGetValue(mapping.Stat, out var current) || !double.IsFinite(current) || current < 0) continue;
                row.CounterCurrent = current;
                row.CounterMinimum = mapping.Minimum;
                row.CounterTarget = mapping.Target;
            }
        }
    }
}
