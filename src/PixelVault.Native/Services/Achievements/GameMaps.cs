using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
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
            return new GameMapReader(Path.GetDirectoryName(root)).List(provider, gameId, achievement, tracker);
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
        internal static void Validate(GameMapDefinition map) => GameMapValidation.Validate(map);
        internal static (double X, double Y) WorldToImage(double x, double y, double minX, double maxX, double minY, double maxY)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(minX) || !double.IsFinite(maxX) || !double.IsFinite(minY) || !double.IsFinite(maxY) || maxX <= minX || maxY <= minY)
                throw new ArgumentOutOfRangeException(nameof(x), "Valid coordinate bounds are required.");
            return ((x - minX) / (maxX - minX), (maxY - y) / (maxY - minY));
        }
    }
}
