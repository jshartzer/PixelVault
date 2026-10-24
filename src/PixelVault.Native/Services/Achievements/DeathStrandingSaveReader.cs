using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PixelVaultNative
{
    sealed class DeathStrandingReaderException : Exception
    {
        internal string Code { get; }
        internal DeathStrandingReaderException(string code, string message) : base(message) { Code = code; }
    }

    /// <summary>Read-only PC Director's Cut container and fabrication table. No save-writing or resigning operation.</summary>
    static class DeathStrandingSaveReader
    {
        internal const int MaxFile = 16 * 1024 * 1024, Block = 262144, MaxPayload = 64 * 1024 * 1024;
        internal const uint Magic = 0xC0391E1D;
        static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
        static int Size(ReadOnlySpan<byte> text)
        {
            var end = text.IndexOf((byte)0); if (end >= 0) text = text.Slice(0, end);
            if (text.IsEmpty || text.Length > 8 || text.ToArray().Any(b => b < '0' || b > '9')
                || !int.TryParse(Encoding.ASCII.GetString(text), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > MaxFile)
                throw new InvalidDataException("Invalid checkpoint length header.");
            return value;
        }
        internal static byte[] DecryptContainer(byte[] input, string expectedAccount)
        {
            if (input.Length < 24 || input.Length > MaxFile) throw new InvalidDataException("The checkpoint size is unsupported.");
            int offset = 0;
            if (U32(input, 0) != Magic)
            {
                offset = checked(24 + Size(input.AsSpan(0, 8)) + Size(input.AsSpan(8, 8)));
                if (offset > input.Length - 24 || Size(input.AsSpan(16, 8)) != input.Length - offset) throw new InvalidDataException("Checkpoint sections do not match its length.");
            }
            if (U32(input, offset) != Magic) throw new InvalidDataException("The checkpoint has no supported PC save header.");
            var data = input.AsSpan(offset).ToArray();
            if (!HasSteamHeader(data)) Transform(data.AsSpan(8), data.AsSpan(4, 4));
            if (!HasSteamHeader(data)) throw new InvalidDataException("The checkpoint could not be decrypted as a Steam PC save.");
            var account = Encoding.ASCII.GetString(data, 12, checked((int)U32(data, 8)));
            if (account != "steam_" + expectedAccount) throw new DeathStrandingReaderException("WrongAccount", "The save belongs to a different Steam account. Choose this account's save folder.");
            return data.AsSpan(12 + checked((int)U32(data, 8))).ToArray();
        }
        static bool HasSteamHeader(byte[] bytes)
        {
            var length = U32(bytes, 8);
            if (length != 23 || bytes.Length <= 12 + length) return false;
            var account = Encoding.ASCII.GetString(bytes, 12, (int)length);
            return account.StartsWith("steam_", StringComparison.Ordinal) && account.AsSpan(6).ToArray().All(c => c >= '0' && c <= '9');
        }

        // Read-side key derivation adapted from Michał Gębicki's MIT-licensed
        // DeathStrandingSaveDataResigner (see tools-licenses/death-stranding-save-reader-MIT.txt).
        internal static void Transform(Span<byte> data, ReadOnlySpan<byte> header)
        {
            Span<byte> key = stackalloc byte[] { 0x46,0x3F,0xBB,0xBC,0x7B,0x6F,0x57,0xED,0x9B,0x41,0x7D,0x36,0xE3,0x26,0x12,0xBC };
            if (header.Length != 4) throw new InvalidDataException("Invalid save key header.");
            header.CopyTo(key);
            unchecked
            {
                const ulong c1 = 0x87C37B91114253D5, c2 = 0x4CF5AD432745937F, seed = 42;
                ulong a = BinaryPrimitives.ReadUInt64LittleEndian(key), b = BinaryPrimitives.ReadUInt64LittleEndian(key.Slice(8));
                a = BitOperations.RotateLeft(a * c1, 31) * c2 ^ seed;
                a = (BitOperations.RotateLeft(a, 27) + seed) * 5 + 0x52DCE729;
                b = BitOperations.RotateLeft(b * c2, 33) * c1 ^ seed;
                b = (BitOperations.RotateLeft(b, 31) + a) * 5 + 0x38495AB5;
                a ^= 16; b ^= 16; a += b; b += a;
                static ulong Mix(ulong x) { unchecked { x ^= x >> 33; x *= 0xFF51AFD7ED558CCD; x ^= x >> 33; x *= 0xC4CEB9FE1A85EC53; return x ^ x >> 33; } }
                a = Mix(a); b = Mix(b); a += b; b += a;
                BinaryPrimitives.WriteUInt64LittleEndian(key, a); BinaryPrimitives.WriteUInt64LittleEndian(key.Slice(8), b);
                int words = data.Length / 8;
                for (int i = 0; i < words; i++) BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(i * 8, 8), BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(i * 8, 8)) ^ (i % 2 == 0 ? a : b));
                for (int i = words * 8; i < data.Length; i++) data[i] ^= key[i - words * 8];
            }
        }

        internal static byte[] DecodePayload(byte[] compressed, Func<byte[], int, byte[]> decompress)
        {
            var prefix = decompress(compressed, Block);
            if (prefix.Length != Block) throw new InvalidDataException("The save decompressor returned an incomplete prefix.");
            var length = U32(prefix, 0x1fc);
            if (length == 0 || length > MaxPayload) throw new InvalidDataException("The decompressed save length is unsupported.");
            int size = checked(((int)length + 512 + Block - 1) / Block * Block);
            var payload = decompress(compressed, size);
            if (payload.Length != size) throw new InvalidDataException("The save decompressor returned an incomplete checkpoint.");
            var data = payload.AsSpan(512, (int)length);
            if (!CryptographicOperations.FixedTimeEquals(payload.AsSpan(0x1ec, 16), MD5.HashData(data))) throw new InvalidDataException("The checkpoint checksum does not match. Saved tracker progress was preserved.");
            return data.ToArray();
        }
        internal static uint[] CatalogueKeys()
        {
            using var stream = typeof(DeathStrandingSaveReader).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStrandingKeys.json");
            var keys = JsonSerializer.Deserialize<uint[]>(stream);
            if (keys?.Length != 408 || keys.Distinct().Count() != 408) throw new InvalidDataException("The bundled catalogue reference is incomplete.");
            return keys;
        }
        internal static AchievementTrackerSnapshot ReadCatalogue(byte[] data, string account, long observedUtc, string source, string evidence,
            AchievementTrackerDefinition definition = null)
        {
            definition ??= AchievementTrackingCatalog.DeathStranding();
            var keys = CatalogueKeys().ToHashSet(); Dictionary<uint, uint> found = null;
            for (int start = 0; start <= data.Length - 408 * 8; start++)
            {
                if ((U32(data, start + 4) & 0x1fffffff) != 0 || !keys.Contains(U32(data, start))) continue;
                var records = new Dictionary<uint, uint>();
                for (int index = 0; index < 408; index++)
                {
                    var key = U32(data, start + index * 8); var flags = U32(data, start + index * 8 + 4);
                    if (!keys.Contains(key) || (flags & 0x3fff) != 0 || ((flags >> 14) & 0x7fff) != index || !records.TryAdd(key, flags)) break;
                }
                if (records.Count != 408) continue;
                if (found != null) throw new NotSupportedException("The save contains an ambiguous catalogue layout. This game version needs a reader update.");
                found = records;
            }
            if (found == null) throw new NotSupportedException("The save does not contain the supported 408-entry catalogue. This game version needs a reader update.");
            var snapshot = new AchievementTrackerSnapshot { TrackerId = definition.Id, Provider = "steam", GameId = "1850570", Account = account,
                Source = source, Evidence = evidence, ObservedUtcTicks = observedUtc };
            var targetKeys = new HashSet<uint>();
            foreach (var item in definition.Items)
            {
                if (!item.Identifiers.TryGetValue("CatalogueNameCode", out var text) || !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                    || n < int.MinValue || n > uint.MaxValue || !targetKeys.Add(unchecked((uint)n)) || !found.TryGetValue(unchecked((uint)n), out var flags))
                    throw new NotSupportedException("The trophy catalogue no longer matches the tracker definition.");
                snapshot.Items.Add(new AchievementTrackerProgress { ItemId = item.Id, Current = (flags & 0x80000000) != 0 ? 1 : 0,
                    Availability = (flags & 0x20000000) != 0 ? TrackerAvailability.Available : TrackerAvailability.Locked });
            }
            if (snapshot.Items.Count != 108) throw new NotSupportedException("The fabrication tracker must cover all 108 Director's Cut targets.");
            return snapshot;
        }
    }
}
