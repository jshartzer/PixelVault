using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    sealed class GameMapMarker
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Label { get; set; }
        public string Detail { get; set; }
        // Image-relative coordinates keep pins aligned independently of DPI, aspect ratio and zoom.
        public double X { get; set; }
        public double Y { get; set; }
        public double? WorldX { get; set; }
        public double? WorldY { get; set; }
        public string TrackerId { get; set; }
        public string ItemId { get; set; }
    }

    sealed class GameMapDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Provider { get; set; }
        public string GameId { get; set; }
        public string GameTitle { get; set; }
        public string Title { get; set; }
        public string Region { get; set; }
        public string ImageFile { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public string Source { get; set; }
        public string Calibration { get; set; }
        public List<string> AchievementIds { get; set; } = new();
        public List<string> TrackerIds { get; set; } = new();
        public List<GameMapMarker> Markers { get; set; } = new();
        public long Revision { get; set; }
        public override string ToString() => Title;
    }

    /// <summary>Local, game-scoped map media and links. No accounts or progress are stored here.</summary>
    sealed class GameMapStore
    {
        readonly string root;
        internal string Root => root;
        internal GameMapStore(string dataRoot) => root = Path.Combine(Path.GetFullPath(dataRoot), "game-maps");
        SqliteConnection Open()
        {
            Directory.CreateDirectory(root);
            var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "maps.sqlite") }.ToString());
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS game_map(id TEXT PRIMARY KEY,provider TEXT NOT NULL,game_id TEXT NOT NULL,revision INTEGER NOT NULL,definition TEXT NOT NULL); CREATE INDEX IF NOT EXISTS game_map_game ON game_map(provider,game_id);";
            cmd.ExecuteNonQuery();return c;
        }
        internal List<GameMapDefinition> List(string provider, string gameId, string achievement = null, string tracker = null)
        {
            using var c = Open();using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT definition,revision FROM game_map WHERE provider=$p AND game_id=$g ORDER BY id";
            cmd.Parameters.AddWithValue("$p", provider ?? "");cmd.Parameters.AddWithValue("$g", gameId ?? "");
            using var reader = cmd.ExecuteReader();var result = new List<GameMapDefinition>();
            while (reader.Read())
            {
                var map = JsonSerializer.Deserialize<GameMapDefinition>(reader.GetString(0));map.Revision = reader.GetInt64(1);
                if ((!string.IsNullOrEmpty(achievement) && !map.AchievementIds.Contains(achievement)) ||
                    (!string.IsNullOrEmpty(tracker) && !map.TrackerIds.Contains(tracker))) continue;
                result.Add(map);
            }
            return result.OrderBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        internal string ImagePath(GameMapDefinition map)
        {
            var name = map.ImageFile;
            if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.Contains(':') || name.Contains('/') || name.Contains('\\'))
                throw new InvalidDataException("The map image must be stored in the map library.");
            return Path.Combine(root, "images", name);
        }
        internal GameMapDefinition Import(GameMapDefinition map, string imagePath)
        {
            Validate(map);var info = new FileInfo(imagePath);
            if (!info.Exists || info.Length > 160L * 1024 * 1024) throw new InvalidDataException("Choose a map image smaller than 160 MB.");
            Directory.CreateDirectory(Path.Combine(root, "images"));
            // Read and validate a private copy, so changing/removing the original cannot corrupt this import.
            var temp = Path.Combine(root, "images", Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(imagePath, temp);
                using var stream = File.OpenRead(temp);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder is not PngBitmapDecoder && decoder is not JpegBitmapDecoder) throw new InvalidDataException("Choose a PNG or JPEG map.");
                var frame = decoder.Frames[0];
                if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > 100_000_000)
                    throw new InvalidDataException("Choose a map with at most 100 million pixels.");
                map.PixelWidth = frame.PixelWidth;map.PixelHeight = frame.PixelHeight;
                stream.Position = 0;var hash = Convert.ToHexString(SHA256.HashData(stream));
                map.ImageFile = hash + (decoder is PngBitmapDecoder ? ".png" : ".jpg");
                stream.Dispose();var dest = ImagePath(map);
                if (!File.Exists(dest)) File.Move(temp, dest);
                Save(map, insert: true);return map;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal void Save(GameMapDefinition map, bool insert = false)
        {
            Validate(map);_ = ImagePath(map);
            if (map.PixelWidth <= 0 || map.PixelHeight <= 0 || (long)map.PixelWidth * map.PixelHeight > 100_000_000)
                throw new InvalidDataException("Invalid map image dimensions.");
            using var c = Open();using var cmd = c.CreateCommand();
            var revision = map.Revision + 1;
            cmd.CommandText = insert
                ? "INSERT INTO game_map(id,provider,game_id,revision,definition) VALUES($id,$p,$g,$next,$json)"
                : "UPDATE game_map SET definition=$json,revision=$next WHERE id=$id AND provider=$p AND game_id=$g AND revision=$old";
            cmd.Parameters.AddWithValue("$id", map.Id);cmd.Parameters.AddWithValue("$p", map.Provider);cmd.Parameters.AddWithValue("$g", map.GameId);
            cmd.Parameters.AddWithValue("$next", revision);cmd.Parameters.AddWithValue("$old", map.Revision);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(map));
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("This map changed in another window. Reopen it before editing.");
            map.Revision = revision;
        }
        internal static void Validate(GameMapDefinition map)
        {
            if (map == null || string.IsNullOrWhiteSpace(map.Id) || string.IsNullOrWhiteSpace(map.Provider) || string.IsNullOrWhiteSpace(map.GameId) ||
                string.IsNullOrWhiteSpace(map.Title) || map.Title.Length > 300) throw new InvalidDataException("A game and map title are required.");
            if (map.AchievementIds == null || map.TrackerIds == null || map.Markers == null || map.Markers.Count > 10000) throw new InvalidDataException("Invalid map links or markers.");
            if (map.Markers.Select(m => m?.Id).Distinct().Count() != map.Markers.Count) throw new InvalidDataException("Map marker IDs must be unique.");
            foreach (var marker in map.Markers)
                if (marker == null || string.IsNullOrWhiteSpace(marker.Id) || string.IsNullOrWhiteSpace(marker.Label) || marker.Label.Length > 300 ||
                    !double.IsFinite(marker.X) || !double.IsFinite(marker.Y) || marker.X < 0 || marker.X > 1 || marker.Y < 0 || marker.Y > 1 ||
                    (marker.WorldX.HasValue && !double.IsFinite(marker.WorldX.Value)) || (marker.WorldY.HasValue && !double.IsFinite(marker.WorldY.Value)) ||
                    (!string.IsNullOrEmpty(marker.ItemId) && string.IsNullOrEmpty(marker.TrackerId)))
                    throw new InvalidDataException("Each marker needs a name and a position inside the map.");
        }
        internal static (double X, double Y) WorldToImage(double x, double y, double minX, double maxX, double minY, double maxY)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(minX) || !double.IsFinite(maxX) || !double.IsFinite(minY) || !double.IsFinite(maxY) || maxX <= minX || maxY <= minY)
                throw new ArgumentOutOfRangeException(nameof(x), "Valid coordinate bounds are required.");
            return ((x - minX) / (maxX - minX), (maxY - y) / (maxY - minY));
        }
    }
}
