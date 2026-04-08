using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PixelVaultNative
{
    /// <summary>
    /// Fills <see cref="GameIndexEditorRow.StorageGroupId"/> for rows that share the same folded title without conflicting external IDs (PV-PLN-LIBST-001 Slice B).
    /// </summary>
    internal static class GameIndexStorageGroupBackfill
    {
        internal static bool AssignDeterministicStorageGroupIds(
            IList<GameIndexEditorRow> rows,
            Func<string, string, string> normalizeGameIndexNameWithFolder,
            Func<string, string> foldNormalizedTitle,
            Func<string, string> normalizeConsoleLabel,
            Func<string, string> cleanTag,
            Func<string, string> normalizeGameId)
        {
            if (rows == null || rows.Count == 0) return false;
            var norm = normalizeGameIndexNameWithFolder ?? ((n, _) => (n ?? string.Empty).Trim());
            var fold = foldNormalizedTitle ?? (n => (n ?? string.Empty).Trim());
            var plat = normalizeConsoleLabel ?? (p => (p ?? string.Empty).Trim());
            var clean = cleanTag ?? (t => (t ?? string.Empty).Trim());
            var normId = normalizeGameId ?? (g => (g ?? string.Empty).Trim());

            var changed = false;
            var validRows = rows.Where(r => r != null).ToList();

            string TitleFoldKey(GameIndexEditorRow r)
            {
                var n = norm(r.Name ?? string.Empty, r.FolderPath ?? string.Empty);
                if (string.IsNullOrWhiteSpace(n)) return string.Empty;
                var f = fold(n);
                return string.IsNullOrWhiteSpace(f) ? string.Empty : f;
            }

            foreach (var titleGroup in validRows.GroupBy(TitleFoldKey, StringComparer.OrdinalIgnoreCase))
            {
                var bucket = titleGroup.ToList();
                if (bucket.Count == 0) continue;

                if (string.IsNullOrWhiteSpace(titleGroup.Key))
                {
                    foreach (var r in bucket)
                    {
                        var id = MakeGroupId(string.Empty, new[] { normId(r.GameId) });
                        if (string.IsNullOrWhiteSpace(r.StorageGroupId) || !string.Equals(r.StorageGroupId, id, StringComparison.OrdinalIgnoreCase))
                        {
                            r.StorageGroupId = id;
                            changed = true;
                        }
                    }
                    continue;
                }

                var n = bucket.Count;
                var uf = new UnionFind(n);
                for (var i = 0; i < n; i++)
                    for (var j = i + 1; j < n; j++)
                        if (!RowsConflictForStorageMerge(bucket[i], bucket[j], clean, plat))
                            uf.Union(i, j);

                var byRoot = new Dictionary<int, List<int>>();
                for (var i = 0; i < n; i++)
                {
                    var root = uf.Find(i);
                    if (!byRoot.TryGetValue(root, out var list))
                    {
                        list = new List<int>();
                        byRoot[root] = list;
                    }
                    list.Add(i);
                }

                foreach (var kv in byRoot)
                {
                    var members = kv.Value.Select(i => bucket[i]).ToList();
                    var distinctStored = members
                        .Select(m => (m.StorageGroupId ?? string.Empty).Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (distinctStored.Count > 1)
                    {
                        foreach (var r in members)
                        {
                            var solo = MakeGroupId(titleGroup.Key, new[] { normId(r.GameId) });
                            if (!string.Equals(r.StorageGroupId ?? string.Empty, solo, StringComparison.OrdinalIgnoreCase))
                            {
                                r.StorageGroupId = solo;
                                changed = true;
                            }
                        }
                        continue;
                    }

                    var chosen = distinctStored.Count == 1
                        ? distinctStored[0]
                        : MakeGroupId(
                            titleGroup.Key,
                            members.Select(m => normId(m.GameId)).Where(s => !string.IsNullOrWhiteSpace(s)));

                    foreach (var r in members)
                    {
                        if (string.IsNullOrWhiteSpace(r.StorageGroupId) || !string.Equals(r.StorageGroupId, chosen, StringComparison.OrdinalIgnoreCase))
                        {
                            r.StorageGroupId = chosen;
                            changed = true;
                        }
                    }
                }
            }

            return changed;
        }

        static bool RowsConflictForStorageMerge(GameIndexEditorRow a, GameIndexEditorRow b, Func<string, string> clean, Func<string, string> plat)
        {
            if (a == null || b == null) return true;
            var sa = (a.StorageGroupId ?? string.Empty).Trim();
            var sb = (b.StorageGroupId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(sa) && !string.IsNullOrWhiteSpace(sb) && !string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase))
                return true;

            var pa = plat(a.PlatformLabel ?? string.Empty);
            var pb = plat(b.PlatformLabel ?? string.Empty);

            if (string.Equals(pa, "Steam", StringComparison.OrdinalIgnoreCase) && string.Equals(pb, "Steam", StringComparison.OrdinalIgnoreCase))
            {
                var ea = clean(a.SteamAppId ?? string.Empty);
                var eb = clean(b.SteamAppId ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(ea) && !string.IsNullOrWhiteSpace(eb) && !string.Equals(ea, eb, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (string.Equals(pa, "Emulation", StringComparison.OrdinalIgnoreCase) && string.Equals(pb, "Emulation", StringComparison.OrdinalIgnoreCase))
            {
                var na = clean(a.NonSteamId ?? string.Empty);
                var nb = clean(b.NonSteamId ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(na) && !string.IsNullOrWhiteSpace(nb) && !string.Equals(na, nb, StringComparison.OrdinalIgnoreCase))
                    return true;
                var ra = clean(a.RetroAchievementsGameId ?? string.Empty);
                var rb = clean(b.RetroAchievementsGameId ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(ra) && !string.IsNullOrWhiteSpace(rb) && !string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static string MakeGroupId(string titleFoldKey, IEnumerable<string> gameIds)
        {
            var idList = (gameIds ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var idPart = string.Join("\n", idList);
            var payload = Encoding.UTF8.GetBytes((titleFoldKey ?? string.Empty) + "\n" + idPart);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(payload);
                return "SG" + Convert.ToHexString(hash.AsSpan(0, 4));
            }
        }

        sealed class UnionFind
        {
            readonly int[] _parent;

            internal UnionFind(int size)
            {
                _parent = new int[size];
                for (var i = 0; i < size; i++) _parent[i] = i;
            }

            internal int Find(int i)
            {
                while (_parent[i] != i)
                {
                    _parent[i] = _parent[_parent[i]];
                    i = _parent[i];
                }
                return i;
            }

            internal void Union(int a, int b)
            {
                var ra = Find(a);
                var rb = Find(b);
                if (ra != rb) _parent[rb] = ra;
            }
        }
    }
}
