using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace PixelVaultNative
{
    sealed class DeathStrandingReadRequest
    {
        public int Version { get; set; } = 1;
        public string TrackerId { get; set; } = AchievementTrackingCatalog.DeathStrandingId;
        public string GameDirectory { get; set; }
        public string Account { get; set; }
        public long ObservedUtc { get; set; }
        public string SourceName { get; set; }
        public int SkippedNewer { get; set; }
    }
    sealed class DeathStrandingReadResult
    {
        public int Version { get; set; } = 1;
        public string ErrorCode { get; set; }
        public string Error { get; set; }
        public string Fingerprint { get; set; }
        public string GameDllSha256 { get; set; }
        public AchievementTrackerSnapshot Snapshot { get; set; }
    }
    static class DeathStrandingSaveWorker
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate long OodleDecode([In] byte[] source, long sourceSize, [Out] byte[] target, long targetSize,
            int fuzzSafe, int checkCrc, int verbosity, IntPtr decodeBuffer, long decodeBufferSize, IntPtr callback, IntPtr callbackUser,
            IntPtr scratch, long scratchSize, int threadPhase);
        [DllImport("kernel32.dll")]
        static extern uint SetErrorMode(uint mode);

        // A worker exits before Velopack, SQLite, Application, MainWindow or any network service starts.
        internal static int Run(string requestPath)
        {
            string root = null;
            try
            {
                if (OperatingSystem.IsWindows()) SetErrorMode(0x0001 | 0x0002 | 0x8000);
                if (!Path.IsPathFullyQualified(requestPath) || Path.GetFileName(requestPath) != "request.json") return 21;
                root = Path.GetDirectoryName(Path.GetFullPath(requestPath));
                if (new FileInfo(requestPath).Length > 16384) throw new InvalidDataException("The save read request is too large.");
                var request = JsonSerializer.Deserialize<DeathStrandingReadRequest>(File.ReadAllText(requestPath), AchievementTrackingCatalog.Json);
                if (request?.Version != 1 || !Path.IsPathFullyQualified(request.GameDirectory ?? "") || !DeathStrandingTrackerSource.IsSteamAccount(request.Account)
                    || request.ObservedUtc <= 0 || request.ObservedUtc > DateTime.UtcNow.AddMinutes(5).Ticks || string.IsNullOrWhiteSpace(request.SourceName)
                    || request.SourceName.Length > 240 || request.SkippedNewer < 0 || request.SkippedNewer > 128
                    || request.TrackerId != AchievementTrackingCatalog.DeathStrandingId && !DeathStrandingWorldReader.Supports(request.TrackerId) && !DeathStrandingOrderReader.Supports(request.TrackerId) && !DeathStrandingChipReader.Supports(request.TrackerId) && !DeathStrandingInterviewReader.Supports(request.TrackerId))
                    throw new InvalidDataException("The save read request is invalid.");
                if (!File.Exists(Path.Combine(request.GameDirectory, "ds.exe"))) throw new FileNotFoundException("Choose the Director's Cut installation containing ds.exe.");
                var dllPath = Path.Combine(request.GameDirectory, "oo2core_7_win64.dll");
                if (!File.Exists(dllPath)) throw new FileNotFoundException("The game's oo2core_7_win64.dll is missing. Check the installation path.");
                if (!Environment.Is64BitProcess || !BitConverter.IsLittleEndian) throw new NotSupportedException("The save reader needs the Windows x64 build.");
                var inputPath = Path.Combine(root, "input.dat");
                if (new FileInfo(inputPath).Length > DeathStrandingSaveReader.MaxFile) throw new InvalidDataException("The copied checkpoint is too large.");
                var input = File.ReadAllBytes(inputPath); var fingerprint = Convert.ToHexString(SHA256.HashData(input));
                var compressed = DeathStrandingSaveReader.DecryptContainer(input, request.Account);
                using var dllFile = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (dllFile.Length > 64 * 1024 * 1024) throw new InvalidDataException("The game compression library is too large.");
                var dllHash = Convert.ToHexString(SHA256.HashData(dllFile));
                var library = NativeLibrary.Load(dllPath);
                try
                {
                    var decompress = Marshal.GetDelegateForFunctionPointer<OodleDecode>(NativeLibrary.GetExport(library, "OodleLZ_Decompress"));
                    byte[] Decode(byte[] source, int size)
                    {
                        var output = new byte[size];
                        long actual = decompress(source, source.Length, output, size, 1, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 3);
                        if (actual != size) throw new InvalidDataException("The game's decompressor could not read this checkpoint.");
                        return output;
                    }
                    var data = DeathStrandingSaveReader.DecodePayload(compressed, Decode);
                    var sourceLabel = "Death Stranding DC · " + request.SourceName + (request.SkippedNewer > 0 ? " · skipped " + request.SkippedNewer + " newer invalid checkpoint(s)" : "");
                    var evidence = "Read-only copied save; matching Steam account; decompressed MD5 verified; one complete indexed 408-entry catalogue; 108 trophy targets. "
                        + "Fabrication flag 0x80000000; availability flag 0x20000000 remains inferred and does not guarantee facility/material support. Reader v1; game DLL SHA-256 " + dllHash + ".";
                    var snapshot = request.TrackerId == AchievementTrackingCatalog.DeathStrandingId
                        ? DeathStrandingSaveReader.ReadCatalogue(data, request.Account, request.ObservedUtc, sourceLabel, evidence)
                        : DeathStrandingOrderReader.Supports(request.TrackerId)
                        ? DeathStrandingOrderReader.Read(data, request.Account, request.ObservedUtc, sourceLabel,
                            "Read-only copied save; matching Steam account; decompressed MD5 verified. Game DLL SHA-256 " + dllHash + ".")
                        : DeathStrandingInterviewReader.Supports(request.TrackerId)
                        ? DeathStrandingInterviewReader.Read(data, request.Account, request.ObservedUtc, sourceLabel,
                            "Read-only copied save; matching Steam account; decompressed MD5 verified. Game DLL SHA-256 " + dllHash + ".")
                        : DeathStrandingChipReader.Supports(request.TrackerId)
                        ? DeathStrandingChipReader.Read(data, request.Account, request.ObservedUtc, sourceLabel,
                            "Read-only copied save; matching Steam account; decompressed MD5 verified. Game DLL SHA-256 " + dllHash + ".")
                        : DeathStrandingWorldReader.Read(data, request.TrackerId, request.Account, request.ObservedUtc, sourceLabel,
                            "Read-only copied save; matching Steam account; decompressed MD5 verified. World milestone reader v1; game DLL SHA-256 " + dllHash + ".");
                    WriteResult(root, new DeathStrandingReadResult { Fingerprint = fingerprint, GameDllSha256 = dllHash, Snapshot = snapshot });
                }
                finally { NativeLibrary.Free(library); }
                return 0;
            }
            catch (Exception ex)
            {
                if (root != null)
                {
                    var code = ex is DeathStrandingReaderException reader ? reader.Code : ex is NotSupportedException ? "UnsupportedFormat"
                        : ex is DllNotFoundException || ex is BadImageFormatException || ex is EntryPointNotFoundException || ex is FileNotFoundException ? "MissingDependency"
                        : ex is InvalidDataException ? "InvalidSave" : "ReaderFailure";
                    try { WriteResult(root, new DeathStrandingReadResult { ErrorCode = code, Error = ex.Message }); } catch { }
                }
                return 20;
            }
        }
        static void WriteResult(string root, DeathStrandingReadResult result)
        {
            using var stream = new FileStream(Path.Combine(root, "result.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, result, AchievementTrackingCatalog.Json);
        }
    }
}
