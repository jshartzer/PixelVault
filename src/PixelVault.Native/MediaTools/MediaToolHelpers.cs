using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        string MetadataReadPath(string file)
        {
            var sidecar = MetadataSidecarPath(file);
            return !string.IsNullOrWhiteSpace(sidecar) && File.Exists(sidecar) ? sidecar : file;
        }

        string NormalizeExifToolPathKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
            try
            {
                return Path.GetFullPath(normalized);
            }
            catch
            {
                return normalized;
            }
        }

        Dictionary<string, string[]> ReadEmbeddedKeywordTagsBatch(IEnumerable<string> files)
        {
            return metadataService.ReadEmbeddedKeywordTagsBatch(files);
        }

        Dictionary<string, EmbeddedMetadataSnapshot> ReadEmbeddedMetadataBatch(IEnumerable<string> files)
        {
            return metadataService.ReadEmbeddedMetadataBatch(files);
        }

        void MoveMetadataSidecarIfPresent(string sourceFile, string targetFile)
        {
            var sourceSidecar = MetadataSidecarPath(sourceFile);
            var targetSidecar = MetadataSidecarPath(targetFile);
            if (string.IsNullOrWhiteSpace(sourceSidecar) || string.IsNullOrWhiteSpace(targetSidecar) || !File.Exists(sourceSidecar)) return;
            if (File.Exists(targetSidecar)) File.Delete(targetSidecar);
            File.Move(sourceSidecar, targetSidecar);
            Log("Moved sidecar: " + Path.GetFileName(sourceSidecar) + " -> " + targetSidecar);
        }

        void EnsureExifTool()
        {
            metadataService.EnsureExifTool();
        }

        void RunExifToolBatch(IReadOnlyList<ExifWriteRequest> requests)
        {
            metadataService.RunExifToolBatch(requests);
        }

        void RunExe(string file, string[] args, string cwd, bool logOutput)
        {
            RunExeCapture(file, args, cwd, logOutput);
        }

        string RunExeCapture(string file, string[] args, string cwd, bool logOutput)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = string.Join(" ", args.Select(Quote).ToArray()),
                WorkingDirectory = cwd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (logOutput)
                {
                    foreach (var line in (stdout + Environment.NewLine + stderr).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) Log(line);
                }
                if (p.ExitCode != 0) throw new InvalidOperationException(Path.GetFileName(file) + " failed. " + stderr + stdout);
                return stdout;
            }
        }

        string EnsureVideoPoster(string videoPath, int decodePixelWidth)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return null;
                var info = new FileInfo(videoPath);
                var normalizedDecodePixelWidth = NormalizeThumbnailDecodeWidth(decodePixelWidth);
                var width = Math.Max(320, normalizedDecodePixelWidth > 0 ? normalizedDecodePixelWidth : 720);
                var keySource = videoPath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + width;
                string hash;
                using (var md5 = MD5.Create())
                {
                    hash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(keySource))).Replace("-", string.Empty).ToLowerInvariant();
                }
                var posterPath = Path.Combine(thumbsRoot, "video-" + hash + "-frame.png");
                var fallbackPosterPath = Path.Combine(thumbsRoot, "video-" + hash + "-fallback.png");
                if (File.Exists(posterPath))
                {
                    failedFfmpegPosterKeys.Remove(hash);
                    return posterPath;
                }
                var renderWidth = Math.Max(320, width);
                var renderHeight = Math.Max(180, (int)Math.Round(renderWidth * 9d / 16d));
                if (!failedFfmpegPosterKeys.Contains(hash))
                {
                    var ffmpegPoster = TryCreateVideoPosterWithFfmpeg(videoPath, posterPath, renderWidth);
                    if (!string.IsNullOrWhiteSpace(ffmpegPoster) && File.Exists(ffmpegPoster))
                    {
                        failedFfmpegPosterKeys.Remove(hash);
                        if (File.Exists(fallbackPosterPath))
                        {
                            try { File.Delete(fallbackPosterPath); } catch { }
                        }
                        return ffmpegPoster;
                    }
                    failedFfmpegPosterKeys.Add(hash);
                }
                if (File.Exists(fallbackPosterPath)) return fallbackPosterPath;
                return CreateFallbackVideoPoster(videoPath, fallbackPosterPath, renderWidth, renderHeight);
            }
            catch
            {
                return null;
            }
        }

        string ResolveFfmpegPath()
        {
            if (!string.IsNullOrWhiteSpace(ffmpegPath) && File.Exists(ffmpegPath)) return ffmpegPath;
            var discovered = FindExecutableOnPath("ffmpeg.exe");
            if (!string.IsNullOrWhiteSpace(discovered)) ffmpegPath = discovered;
            return ffmpegPath;
        }

        string ResolveFfprobePath()
        {
            try
            {
                var ffmpeg = ResolveFfmpegPath();
                if (!string.IsNullOrWhiteSpace(ffmpeg))
                {
                    var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? string.Empty, "ffprobe.exe");
                    if (File.Exists(sibling)) return sibling;
                }
            }
            catch
            {
            }
            return FindExecutableOnPath("ffprobe.exe");
        }

        string RunExeCaptureAllowFailure(string file, string[] args, string cwd, out int exitCode)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = string.Join(" ", args.Select(Quote).ToArray()),
                WorkingDirectory = cwd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                exitCode = p.ExitCode;
                return stdout + Environment.NewLine + stderr;
            }
        }

        string VideoClipInfoCachePath(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return null;
            try
            {
                var info = new FileInfo(videoPath);
                var keySource = "info|" + videoPath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length;
                using (var md5 = MD5.Create())
                {
                    var hash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(keySource))).Replace("-", string.Empty).ToLowerInvariant();
                    return Path.Combine(thumbsRoot, "video-" + hash + "-info.txt");
                }
            }
            catch
            {
                return null;
            }
        }

        VideoClipInfo TryLoadCachedVideoClipInfo(string videoPath)
        {
            var cachePath = VideoClipInfoCachePath(videoPath);
            if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath)) return null;
            try
            {
                var lines = File.ReadAllLines(cachePath);
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("=")) continue;
                    var split = line.IndexOf('=');
                    values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
                }

                double durationSeconds;
                int width, height;
                double frameRate;
                bool hasAudio;
                string hasAudioValue;
                values.TryGetValue("has_audio", out hasAudioValue);
                var info = new VideoClipInfo
                {
                    DurationSeconds = values.TryGetValue("duration", out var durationValue) && double.TryParse(durationValue, NumberStyles.Float, CultureInfo.InvariantCulture, out durationSeconds) ? durationSeconds : 0,
                    Width = values.TryGetValue("width", out var widthValue) && int.TryParse(widthValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out width) ? width : 0,
                    Height = values.TryGetValue("height", out var heightValue) && int.TryParse(heightValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out height) ? height : 0,
                    FrameRate = values.TryGetValue("frame_rate", out var frameRateValue) && double.TryParse(frameRateValue, NumberStyles.Float, CultureInfo.InvariantCulture, out frameRate) ? frameRate : 0,
                    HasAudio = bool.TryParse(hasAudioValue, out hasAudio) && hasAudio,
                    VideoCodec = values.TryGetValue("video_codec", out var codecValue) ? codecValue : string.Empty
                };
                return IsMeaningfulVideoClipInfo(info) ? info : null;
            }
            catch
            {
                return null;
            }
        }

        void SaveVideoClipInfoCache(string cachePath, VideoClipInfo info)
        {
            if (string.IsNullOrWhiteSpace(cachePath) || info == null) return;
            try
            {
                var directory = Path.GetDirectoryName(cachePath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllLines(cachePath, new[]
                {
                    "duration=" + info.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                    "width=" + info.Width.ToString(CultureInfo.InvariantCulture),
                    "height=" + info.Height.ToString(CultureInfo.InvariantCulture),
                    "frame_rate=" + info.FrameRate.ToString("0.###", CultureInfo.InvariantCulture),
                    "has_audio=" + info.HasAudio.ToString(),
                    "video_codec=" + (info.VideoCodec ?? string.Empty)
                }, Encoding.UTF8);
            }
            catch
            {
            }
        }

        bool IsMeaningfulVideoClipInfo(VideoClipInfo info)
        {
            return info != null
                && (info.DurationSeconds > 0
                    || (info.Width > 0 && info.Height > 0)
                    || info.FrameRate > 0
                    || info.HasAudio
                    || !string.IsNullOrWhiteSpace(info.VideoCodec));
        }

        string[] BuildFfprobeVideoInfoArgs(string videoPath)
        {
            return new[]
            {
                "-v",
                "error",
                "-show_entries",
                "format=duration:stream=codec_type,codec_name,width,height,r_frame_rate",
                "-of",
                "default=noprint_wrappers=1",
                videoPath
            };
        }

        VideoClipInfo TryProbeVideoClipInfoWithFfprobe(string videoPath)
        {
            var ffprobe = ResolveFfprobePath();
            if (string.IsNullOrWhiteSpace(ffprobe) || !File.Exists(ffprobe)) return null;
            try
            {
                var output = RunExeCapture(ffprobe, BuildFfprobeVideoInfoArgs(videoPath), Path.GetDirectoryName(ffprobe), false);
                var info = new VideoClipInfo();
                string currentCodecType = string.Empty;
                foreach (var rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || !line.Contains("=")) continue;
                    var split = line.IndexOf('=');
                    var key = line.Substring(0, split);
                    var value = line.Substring(split + 1);
                    if (string.Equals(key, "codec_type", StringComparison.OrdinalIgnoreCase))
                    {
                        currentCodecType = value;
                        if (string.Equals(value, "audio", StringComparison.OrdinalIgnoreCase)) info.HasAudio = true;
                        continue;
                    }
                    if (string.Equals(key, "duration", StringComparison.OrdinalIgnoreCase))
                    {
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSeconds)) info.DurationSeconds = durationSeconds;
                        continue;
                    }
                    if (!string.Equals(currentCodecType, "video", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(key, "codec_name", StringComparison.OrdinalIgnoreCase))
                    {
                        info.VideoCodec = value;
                    }
                    else if (string.Equals(key, "width", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)) info.Width = width;
                    }
                    else if (string.Equals(key, "height", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)) info.Height = height;
                    }
                    else if (string.Equals(key, "r_frame_rate", StringComparison.OrdinalIgnoreCase))
                    {
                        info.FrameRate = ParseFpsValue(value);
                    }
                }
                return IsMeaningfulVideoClipInfo(info) ? info : null;
            }
            catch
            {
                return null;
            }
        }

        string[] BuildFfmpegInfoArgs(string videoPath)
        {
            return new[]
            {
                "-hide_banner",
                "-i",
                videoPath
            };
        }

        VideoClipInfo TryProbeVideoClipInfoWithFfmpeg(string videoPath)
        {
            var ffmpeg = ResolveFfmpegPath();
            if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg)) return null;
            try
            {
                int exitCode;
                var output = RunExeCaptureAllowFailure(ffmpeg, BuildFfmpegInfoArgs(videoPath), Path.GetDirectoryName(ffmpeg), out exitCode);
                if (string.IsNullOrWhiteSpace(output)) return null;

                var info = new VideoClipInfo();
                var durationMatch = Regex.Match(output, @"Duration:\s*(?<hours>\d{2}):(?<minutes>\d{2}):(?<seconds>\d{2}(?:\.\d+)?)", RegexOptions.IgnoreCase);
                if (durationMatch.Success)
                {
                    var rawSeconds = double.Parse(durationMatch.Groups["seconds"].Value, CultureInfo.InvariantCulture);
                    info.DurationSeconds =
                        int.Parse(durationMatch.Groups["hours"].Value, CultureInfo.InvariantCulture) * 3600 +
                        int.Parse(durationMatch.Groups["minutes"].Value, CultureInfo.InvariantCulture) * 60 +
                        rawSeconds;
                }

                var videoLineMatch = Regex.Match(output, @"Video:\s*(?<codec>[^,\r\n]+).*?(?<width>\d{2,5})x(?<height>\d{2,5})(?:[^,\r\n]*,\s*(?<fps>\d+(?:\.\d+)?)\s*fps)?", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (videoLineMatch.Success)
                {
                    info.VideoCodec = CleanTag(videoLineMatch.Groups["codec"].Value).Replace(" ", string.Empty);
                    int.TryParse(videoLineMatch.Groups["width"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out info.Width);
                    int.TryParse(videoLineMatch.Groups["height"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out info.Height);
                    if (videoLineMatch.Groups["fps"].Success)
                    {
                        double.TryParse(videoLineMatch.Groups["fps"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out info.FrameRate);
                    }
                }

                info.HasAudio = Regex.IsMatch(output, @"Audio:\s*", RegexOptions.IgnoreCase);
                return IsMeaningfulVideoClipInfo(info) ? info : null;
            }
            catch
            {
                return null;
            }
        }

        double ParseFpsValue(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            var cleaned = raw.Trim();
            if (cleaned.Contains("/"))
            {
                var parts = cleaned.Split('/');
                if (parts.Length == 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
                    && Math.Abs(denominator) > 0.0001d)
                {
                    return numerator / denominator;
                }
            }
            return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var direct) ? direct : 0;
        }

        VideoClipInfo EnsureVideoClipInfo(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return null;
            var cached = TryLoadCachedVideoClipInfo(videoPath);
            if (cached != null) return cached;
            var cachePath = VideoClipInfoCachePath(videoPath);
            var info = TryProbeVideoClipInfoWithFfprobe(videoPath) ?? TryProbeVideoClipInfoWithFfmpeg(videoPath);
            if (IsMeaningfulVideoClipInfo(info)) SaveVideoClipInfoCache(cachePath, info);
            return info;
        }

        void WarmVideoClipInfo(string videoPath, Action<VideoClipInfo> onCompleted = null)
        {
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return;
            var cached = TryLoadCachedVideoClipInfo(videoPath);
            if (cached != null)
            {
                if (onCompleted != null) onCompleted(cached);
                return;
            }
            var cachePath = VideoClipInfoCachePath(videoPath);
            if (string.IsNullOrWhiteSpace(cachePath) || !activeVideoInfoGenerations.TryAdd(cachePath, 0)) return;
            Task.Run(delegate
            {
                VideoClipInfo loaded = null;
                try
                {
                    loaded = EnsureVideoClipInfo(videoPath);
                }
                catch
                {
                }
                finally
                {
                    byte _;
                    activeVideoInfoGenerations.TryRemove(cachePath, out _);
                }
                if (onCompleted != null && loaded != null) onCompleted(loaded);
            });
        }

        string FormatVideoDuration(double durationSeconds)
        {
            if (durationSeconds <= 0) return string.Empty;
            var wholeSeconds = Math.Max(1, (int)Math.Round(durationSeconds, MidpointRounding.AwayFromZero));
            var duration = TimeSpan.FromSeconds(wholeSeconds);
            if (duration.TotalHours >= 1) return ((int)duration.TotalHours).ToString(CultureInfo.InvariantCulture) + ":" + duration.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + duration.Seconds.ToString("00", CultureInfo.InvariantCulture);
            return duration.Minutes.ToString(CultureInfo.InvariantCulture) + ":" + duration.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        string FormatVideoFrameRate(double frameRate)
        {
            if (frameRate <= 0) return string.Empty;
            var rounded = Math.Round(frameRate);
            var label = Math.Abs(frameRate - rounded) < 0.05d
                ? rounded.ToString(CultureInfo.InvariantCulture)
                : frameRate.ToString("0.##", CultureInfo.InvariantCulture);
            return label + " fps";
        }

        string FormatVideoClipInfoSummary(VideoClipInfo info)
        {
            if (!IsMeaningfulVideoClipInfo(info)) return string.Empty;
            var parts = new List<string>();
            var duration = FormatVideoDuration(info.DurationSeconds);
            if (!string.IsNullOrWhiteSpace(duration)) parts.Add(duration);
            if (info.Width > 0 && info.Height > 0) parts.Add(info.Width.ToString(CultureInfo.InvariantCulture) + "x" + info.Height.ToString(CultureInfo.InvariantCulture));
            if (info.FrameRate > 0) parts.Add(FormatVideoFrameRate(info.FrameRate));
            if (info.HasAudio) parts.Add("Audio");
            return string.Join(" | ", parts);
        }

        string[] BuildFfmpegPosterArgs(string videoPath, string posterPath, int renderWidth, string hwaccel, string seekTime)
        {
            var args = new List<string>
            {
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-threads",
                "0"
            };
            if (!string.IsNullOrWhiteSpace(hwaccel))
            {
                args.Add("-hwaccel");
                args.Add(hwaccel);
            }
            args.Add("-i");
            args.Add(videoPath);
            args.Add("-ss");
            args.Add(seekTime);
            args.Add("-frames:v");
            args.Add("1");
            args.Add("-vf");
            args.Add("scale=" + Math.Max(320, renderWidth) + ":-2");
            args.Add(posterPath);
            return args.ToArray();
        }

        string[] BuildFfmpegPreviewArgs(string videoPath, string previewPath, int renderWidth, string hwaccel)
        {
            var args = new List<string>
            {
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-threads",
                "0"
            };
            if (!string.IsNullOrWhiteSpace(hwaccel))
            {
                args.Add("-hwaccel");
                args.Add(hwaccel);
            }
            args.Add("-ss");
            args.Add("00:00:00.250");
            args.Add("-t");
            args.Add("10");
            args.Add("-i");
            args.Add(videoPath);
            args.Add("-an");
            args.Add("-vf");
            args.Add("scale=" + Math.Max(320, renderWidth) + ":-2");
            args.Add("-c:v");
            args.Add("libx264");
            args.Add("-preset");
            args.Add("ultrafast");
            args.Add("-crf");
            args.Add("30");
            args.Add("-pix_fmt");
            args.Add("yuv420p");
            args.Add("-movflags");
            args.Add("+faststart");
            args.Add(previewPath);
            return args.ToArray();
        }

        string TryCreateVideoPosterWithFfmpeg(string videoPath, string posterPath, int renderWidth)
        {
            var ffmpeg = ResolveFfmpegPath();
            if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg)) return null;

            foreach (var hwaccel in new[] { "auto", string.Empty })
            {
                foreach (var seekTime in new[] { "00:00:00.250", "00:00:01.000", "00:00:03.000", "00:00:10.000" })
                {
                    try
                    {
                        if (File.Exists(posterPath)) File.Delete(posterPath);
                        RunExeCapture(ffmpeg, BuildFfmpegPosterArgs(videoPath, posterPath, renderWidth, hwaccel, seekTime), Path.GetDirectoryName(ffmpeg), false);
                        if (File.Exists(posterPath)) return posterPath;
                    }
                    catch
                    {
                        try
                        {
                            if (File.Exists(posterPath)) File.Delete(posterPath);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            return null;
        }

        string VideoPreviewClipPath(string videoPath, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return null;
            try
            {
                var info = new FileInfo(videoPath);
                var normalizedDecodePixelWidth = NormalizeThumbnailDecodeWidth(decodePixelWidth);
                var width = Math.Max(320, normalizedDecodePixelWidth > 0 ? normalizedDecodePixelWidth : 720);
                var keySource = "preview|" + videoPath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + width;
                using (var md5 = MD5.Create())
                {
                    var hash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(keySource))).Replace("-", string.Empty).ToLowerInvariant();
                    return Path.Combine(thumbsRoot, "video-" + hash + "-preview.mp4");
                }
            }
            catch
            {
                return null;
            }
        }

        string EnsureVideoPreviewClip(string videoPath, int decodePixelWidth)
        {
            var previewPath = VideoPreviewClipPath(videoPath, decodePixelWidth);
            if (string.IsNullOrWhiteSpace(previewPath)) return null;
            if (File.Exists(previewPath)) return previewPath;
            var ffmpeg = ResolveFfmpegPath();
            if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg)) return null;
            foreach (var hwaccel in new[] { "auto", string.Empty })
            {
                try
                {
                    if (File.Exists(previewPath)) File.Delete(previewPath);
                    RunExeCapture(ffmpeg, BuildFfmpegPreviewArgs(videoPath, previewPath, Math.Max(320, NormalizeThumbnailDecodeWidth(decodePixelWidth)), hwaccel), Path.GetDirectoryName(ffmpeg), false);
                    if (File.Exists(previewPath)) return previewPath;
                }
                catch
                {
                    try
                    {
                        if (File.Exists(previewPath)) File.Delete(previewPath);
                    }
                    catch
                    {
                    }
                }
            }
            return null;
        }

        void WarmVideoPreviewClip(string videoPath, int decodePixelWidth)
        {
            var previewPath = VideoPreviewClipPath(videoPath, decodePixelWidth);
            if (string.IsNullOrWhiteSpace(previewPath) || File.Exists(previewPath)) return;
            if (!activeVideoPreviewGenerations.TryAdd(previewPath, 0)) return;
            Task.Run(delegate
            {
                try
                {
                    EnsureVideoPreviewClip(videoPath, decodePixelWidth);
                }
                catch
                {
                }
                finally
                {
                    byte _;
                    activeVideoPreviewGenerations.TryRemove(previewPath, out _);
                }
            });
        }

        string CreateFallbackVideoPoster(string videoPath, string posterPath, int renderWidth, int renderHeight)
        {
            try
            {
                var title = Path.GetFileNameWithoutExtension(videoPath);
                var accent = Brush("#3E6D8C");
                var bg = Brush("#12191E");
                var fg = Brush("#F1E9DA");
                var sub = Brush("#A7B5BD");
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(bg, null, new Rect(0, 0, renderWidth, renderHeight));
                    dc.DrawRectangle(Brush("#1B242A"), null, new Rect(0, 0, renderWidth, 44));
                    dc.DrawRoundedRectangle(Brush("#20343A"), null, new Rect(24, 20, renderWidth - 48, renderHeight - 40), 16, 16);
                    dc.DrawEllipse(accent, null, new Point(renderWidth / 2d, renderHeight / 2d - 12), 54, 54);
                    var play = new StreamGeometry();
                    using (var ctx = play.Open())
                    {
                        ctx.BeginFigure(new Point(renderWidth / 2d - 16, renderHeight / 2d - 38), true, true);
                        ctx.LineTo(new Point(renderWidth / 2d - 16, renderHeight / 2d + 14), true, false);
                        ctx.LineTo(new Point(renderWidth / 2d + 28, renderHeight / 2d - 12), true, false);
                    }
                    play.Freeze();
                    dc.DrawGeometry(Brushes.White, null, play);
                    var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                    var titleText = new FormattedText(title, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 18, fg, pixelsPerDip);
                    titleText.MaxTextWidth = renderWidth - 64;
                    titleText.TextAlignment = TextAlignment.Center;
                    dc.DrawText(titleText, new Point((renderWidth - titleText.Width) / 2d, renderHeight - 84));
                    var subText = new FormattedText(Path.GetExtension(videoPath).TrimStart('.').ToUpperInvariant() + " video", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, sub, pixelsPerDip);
                    subText.MaxTextWidth = renderWidth - 64;
                    subText.TextAlignment = TextAlignment.Center;
                    dc.DrawText(subText, new Point((renderWidth - subText.Width) / 2d, renderHeight - 56));
                }
                var bitmap = new RenderTargetBitmap(renderWidth, renderHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(posterPath)) encoder.Save(stream);
                return posterPath;
            }
            catch
            {
                return null;
            }
        }
    }
}
