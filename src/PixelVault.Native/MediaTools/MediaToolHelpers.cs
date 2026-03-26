using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var sourceFiles = (files ?? Enumerable.Empty<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var file in sourceFiles) result[file] = new string[0];
            if (sourceFiles.Count == 0) return result;
            if (string.IsNullOrWhiteSpace(exifToolPath) || !File.Exists(exifToolPath)) return result;

            var readTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var targetToSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in sourceFiles)
            {
                var readTarget = MetadataReadPath(file);
                if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) continue;
                readTargets[file] = readTarget;
                targetToSource[NormalizeExifToolPathKey(readTarget)] = file;
            }
            if (readTargets.Count == 0) return result;
            var orderedReadTargets = readTargets
                .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var argFile = Path.Combine(cacheRoot, "exiftool-batch-read-" + Guid.NewGuid().ToString("N") + ".args");
            try
            {
                var argLines = new List<string>
                {
                    "-T",
                    "-sep",
                    "||",
                    "-Directory",
                    "-FileName",
                    "-XMP-digiKam:TagsList",
                    "-XMP-lr:HierarchicalSubject",
                    "-XMP-dc:Subject",
                    "-XMP:Subject",
                    "-XMP:TagsList",
                    "-IPTC:Keywords"
                };
                argLines.AddRange(orderedReadTargets.Select(pair => pair.Value));
                File.WriteAllLines(argFile, argLines.ToArray(), Encoding.UTF8);
                var output = RunExeCapture(exifToolPath, new[] { "-@", argFile }, Path.GetDirectoryName(exifToolPath), false);
                var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var outputLines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                for (int lineIndex = 0; lineIndex < outputLines.Length; lineIndex++)
                {
                    var line = outputLines[lineIndex];
                    var parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    var directoryPart = parts[0] == "-" ? string.Empty : parts[0];
                    var fileNamePart = parts[1] == "-" ? string.Empty : parts[1];
                    var exifPath = NormalizeExifToolPathKey(Path.Combine(directoryPart, fileNamePart));
                    string sourceFile;
                    if (!targetToSource.TryGetValue(exifPath, out sourceFile))
                    {
                        if (lineIndex >= orderedReadTargets.Count) continue;
                        sourceFile = orderedReadTargets[lineIndex].Key;
                    }
                    var tags = new List<string>();
                    for (int i = 2; i < parts.Length; i++)
                    {
                        foreach (var value in parts[i].Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var tag = CleanTag(value);
                            if (!string.IsNullOrWhiteSpace(tag) && tag != "-") tags.Add(tag);
                        }
                    }
                    result[sourceFile] = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    matchedFiles.Add(sourceFile);
                }
                foreach (var pair in readTargets)
                {
                    if (matchedFiles.Contains(pair.Key)) continue;
                    result[pair.Key] = ReadEmbeddedKeywordTagsDirect(pair.Key);
                }
            }
            finally
            {
                if (File.Exists(argFile)) File.Delete(argFile);
            }
            return result;
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
            if (!File.Exists(exifToolPath)) throw new InvalidOperationException("ExifTool not found: " + exifToolPath);
            var support = Path.Combine(Path.GetDirectoryName(exifToolPath), "exiftool_files");
            if (Path.GetFileName(exifToolPath).Equals("exiftool.exe", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(support)) throw new InvalidOperationException("ExifTool support folder missing: " + support);
            RunExe(exifToolPath, new[] { "-ver" }, Path.GetDirectoryName(exifToolPath), false);
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
