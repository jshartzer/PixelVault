using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PixelVaultNative
{
    // Reliefs come from Companion's approved renderer; the surround uses actual dimensions.
    internal static class CompletionCaseArtwork
    {
        internal const double TopPadding = 20, Header = 62, Footer = 21, ShelfSpace = 57;
        sealed record Alloy(string Dark, string Shade, string Mid, string Light, string Shine, string Wall, string Glow);
        static readonly Alloy Gold = new("#3b230a", "#80501e", "#b68b41", "#f5d88e", "#fff1ce", "#2d2416", "#ffc14b");
        static readonly Alloy Emerald = new("#032b27", "#086758", "#179c81", "#68d8bd", "#d4fff0", "#0a3027", "#26e6b3");
        static readonly Alloy Diamond = new("#23364b", "#456481", "#6d8da9", "#c3e5fb", "#f5fcff", "#102738", "#5cb3ff");
        static readonly Dictionary<string, DrawingGroup> Reliefs = LoadReliefs();
        static readonly Brush Grain = MakeGrain();
        static Color Color(string value)
        {
            var h = value.TrimStart('#');
            if (h.Length == 3 || h.Length == 4) { var expanded = ""; foreach (var c in h) expanded += new string(c, 2); h = expanded; }
            var alpha = h.Length == 8 ? byte.Parse(h.Substring(6, 2), NumberStyles.HexNumber) : (byte)255;
            return System.Windows.Media.Color.FromArgb(alpha, byte.Parse(h.Substring(0, 2), NumberStyles.HexNumber), byte.Parse(h.Substring(2, 2), NumberStyles.HexNumber), byte.Parse(h.Substring(4, 2), NumberStyles.HexNumber));
        }
        static Brush Solid(string color) { var b = new SolidColorBrush(Color(color)); b.Freeze(); return b; }
        static LinearGradientBrush Gradient(double x1, double y1, double x2, double y2, params (double At, string Color)[] stops)
        {
            var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2) };
            foreach (var s in stops) b.GradientStops.Add(new GradientStop(Color(s.Color), s.At)); b.Freeze(); return b;
        }
        static Geometry Path(params Point[] points)
        {
            var g = new StreamGeometry(); using (var c = g.Open()) { c.BeginFigure(points[0], true, true); for (var i = 1; i < points.Length; i++) c.LineTo(points[i], true, false); }
            g.Freeze(); return g;
        }
        static void Polygon(DrawingContext c, Brush fill, params Point[] points) => c.DrawGeometry(fill, null, Path(points));
        static void Rect(DrawingContext c, double x, double y, double w, double h, Brush fill) => c.DrawRectangle(fill, null, new Rect(x, y, Math.Max(0, w), Math.Max(0, h)));
        static void Line(DrawingContext c, double x1, double y1, double x2, double y2, Brush brush, double width = 1) => c.DrawLine(new Pen(brush, width), new Point(x1, y1), new Point(x2, y2));
        static Brush JsonPaint(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String) return Solid(value.GetString());
            var stops = new List<(double, string)>(); foreach (var s in value.GetProperty("stops").EnumerateArray()) stops.Add((s[0].GetDouble(), s[1].GetString()));
            return Gradient(value.GetProperty("x1").GetDouble(), value.GetProperty("y1").GetDouble(), value.GetProperty("x2").GetDouble(), value.GetProperty("y2").GetDouble(), stops.ToArray());
        }
        static Dictionary<string, DrawingGroup> LoadReliefs()
        {
            using var stream = typeof(CompletionCaseArtwork).Assembly.GetManifestResourceStream("PixelVault.CompletionCaseReliefs.json");
            using var data = JsonDocument.Parse(stream); var groups = new Dictionary<string, DrawingGroup>();
            foreach (var part in data.RootElement.EnumerateObject())
            {
                var group = new DrawingGroup(); using (var c = group.Open()) foreach (var op in part.Value.EnumerateArray())
                {
                    var m = op.GetProperty("matrix"); c.PushTransform(new MatrixTransform(m[0].GetDouble(), m[1].GetDouble(), m[2].GetDouble(), m[3].GetDouble(), m[4].GetDouble(), m[5].GetDouble()));
                    var geometry = Geometry.Parse(op.GetProperty("d").GetString()); Brush fill = null; Pen pen = null;
                    if (op.TryGetProperty("fill", out var f)) fill = JsonPaint(f);
                    if (op.TryGetProperty("stroke", out var s)) pen = new Pen(JsonPaint(s), op.GetProperty("sw").GetDouble()) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    c.DrawGeometry(fill, pen, geometry); c.Pop();
                }
                group.Freeze(); groups[part.Name] = group;
            }
            return groups;
        }
        static Brush MakeGrain()
        {
            var g = new DrawingGroup(); using (var c = g.Open())
            {
                uint seed = 913; for (var i = 0; i < 480; i++)
                {
                    seed = unchecked(seed * 1664525 + 1013904223); var x = seed % 160; seed = unchecked(seed * 1664525 + 1013904223); var y = seed % 160;
                    c.DrawRectangle(Solid(i % 2 == 0 ? "#ffffff0b" : "#00000015"), null, new Rect(x, y, 1, 1));
                }
            }
            g.Freeze(); var b = new DrawingBrush(g) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 160, 160), ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 160, 160) }; b.Freeze(); return b;
        }
        static void Relief(DrawingContext c, string name, double x, double y, double scale = 1, bool mirror = false)
        {
            c.PushTransform(new TranslateTransform(x, y)); c.PushTransform(new ScaleTransform(mirror ? -scale : scale, scale)); c.DrawDrawing(Reliefs[name]); c.Pop(); c.Pop();
        }
        internal static DrawingGroup DiamondCover(double w, double h, double dpi)
        {
            var group = new DrawingGroup(); using (var c = group.Open())
            {
                if (SystemParameters.HighContrast) c.DrawRoundedRectangle(null, new Pen(SystemColors.WindowTextBrush, 2), new Rect(0, 0, w, h), 12, 12);
                else
                {
                    c.PushTransform(new ScaleTransform(w / 226, h / 341)); c.PushTransform(new TranslateTransform(-13, -22));
                    c.DrawDrawing(Reliefs["diamond-cover"]);
                    Text(c, "100%", 207, 355, 21, .5, Solid("#e3f6ff"), dpi); c.Pop(); c.Pop();
                }
            }
            group.Freeze(); return group;
        }
        static DrawingGroup DrawDiamondFront(double w, double height, int rows, double pitch, double dpi)
        {
            var group = new DrawingGroup(); using (var c = group.Open())
            {
                c.PushTransform(new TranslateTransform(0, 33)); Perimeter(c, w, height - 33, Diamond); c.Pop();
                // A continuous cut-crystal inlay follows the platinum rails.
                for (var side = 0; side < 2; side++) for (var y = 124d; y < height - 67; y += 62)
                {
                    var x = side == 0 ? 16d : w - 22; var end = Math.Min(y + 62, height - 58);
                    Polygon(c, Gradient(x, y, x + 6, end, (0, "#ffffffbb"), (.3, "#92bde14d"), (.54, "#12253c55"), (.8, "#c9eaffbb"), (1, "#64b7ff80")), new(x, y), new(x + 6, y + 17), new(x + 6, end), new(x, end - 14));
                }
                void Floor(double y)
                {
                    Polygon(c, Gradient(0, y - 29, 0, y, (0, "#d0eeff05"), (.5, "#5eb4f022"), (1, "#caedff55")), new(29, y - 29), new(w - 29, y - 29), new(w - 20, y), new(20, y));
                    Rect(c, 20, y, w - 40, 8, Gradient(0, y, 0, y + 10, (0, "#23364b"), (.16, "#d8e7f2"), (.29, "#ffffff"), (.4, "#6d8da9"), (.52, "#1c3148"), (.72, "#c3e5fb"), (.84, "#f5fcff"), (1, "#456481")));
                    Line(c, 20, y, w - 20, y, Solid("#c9f0ff")); Line(c, 20, y + 5, w - 20, y + 5, Solid("#499ee7"));
                }
                for (var r = 1; r < rows; r++) Floor(112 + r * pitch - 11); Floor(height - 22);
                Relief(c, "diamond-upper", 23, 86); Relief(c, "diamond-upper", w - 23, 86, 1, true);
                Relief(c, "diamond-lower", 22, height - 31); Relief(c, "diamond-lower", w - 22, height - 31, 1, true);
                if (rows > 1) { var y = 112 + Math.Ceiling((rows - 1) / 2d) * pitch - 7; Relief(c, "diamond-side", 20, y); Relief(c, "diamond-side", w - 20, y); }
                var cap = Math.Min(1, (w - 12) / 422); Relief(c, "diamond-header", (w - 422 * cap) / 2, 0, cap);
                var sheen = Gradient(0, 45 * cap, 0, 72 * cap, (0, "#f6fdff"), (.43, "#91b8dc"), (.53, "#edfaff"), (1, "#4f83b8"));
                Text(c, "100% COMPLETE", w / 2, 57 * cap, 24 * cap, 2.6 * cap, sheen, dpi);
                Text(c, "BASE GAME + DLC + PV", w / 2, 79 * cap, 10 * cap, 1.8 * cap, Solid("#c4e4fa"), dpi);
                Line(c, w / 2 - 145 * cap, 79 * cap, w / 2 - 115 * cap, 79 * cap, Solid("#91b8dc"), .7);
                Line(c, w / 2 + 115 * cap, 79 * cap, w / 2 + 145 * cap, 79 * cap, Solid("#91b8dc"), .7);
            }
            group.Freeze(); return group;
        }
        static void Ledge(DrawingContext c, double w, double y, Alloy p, bool bottom)
        {
            var edge = bottom ? 5d : 16d;
            Polygon(c, Gradient(0, y - 33, 0, y, (0, "#a2c8c70a"), (.35, p.Light + "10"), (.65, "#c5d8d826"), (1, p.Shine + "49")), new(26, y - 33), new(w - 26, y - 33), new(w - edge, y), new(edge, y));
            Polygon(c, Gradient(0, y, 0, y + 11, (0, p.Shine), (.12, p.Mid), (.31, p.Dark), (.54, p.Shade), (.76, p.Mid), (1, p.Dark)), new(edge, y), new(w - edge, y), new(w - edge + 1, y + 9), new(edge - 1, y + 9));
            Line(c, edge, y + .6, w - edge, y + .6, Gradient(edge, 0, w - edge, 0, (0, p.Mid), (.12, p.Shine), (.28, p.Mid), (.5, p.Light), (.7, p.Mid), (.89, p.Shine), (1, p.Mid)), 1.2);
            Rect(c, edge + 8, y + 9, w - edge * 2 - 16, 6, Solid("#00000077"));
        }
        static void Perimeter(DrawingContext c, double w, double h, Alloy p)
        {
            Polygon(c, Gradient(0, 43, 0, 55, (0, p.Shine), (.14, p.Mid), (.28, p.Dark), (.47, p.Light), (.55, p.Shine), (.7, p.Shade), (1, p.Dark)), new(14, 43), new(w - 14, 43), new(w - 25, 54), new(25, 54));
            Polygon(c, Gradient(w - 25, 0, w - 14, 0, (0, p.Dark), (.25, p.Shine), (.36, p.Mid), (.56, p.Dark), (.84, p.Light), (1, p.Dark)), new(w - 14, 43), new(w - 14, h - 21), new(w - 25, h - 32), new(w - 25, 54));
            Polygon(c, Gradient(0, h - 32, 0, h - 21, (0, p.Dark), (.23, p.Light), (.33, p.Shine), (.5, p.Shade), (.75, p.Dark), (1, p.Mid)), new(w - 14, h - 21), new(14, h - 21), new(25, h - 32), new(w - 25, h - 32));
            Polygon(c, Gradient(14, 0, 25, 0, (0, p.Dark), (.2, p.Light), (.3, p.Shine), (.48, p.Shade), (.7, p.Dark), (.9, p.Light), (1, p.Dark)), new(14, h - 21), new(14, 43), new(25, 54), new(25, h - 32));
            c.DrawRectangle(null, new Pen(Solid(p.Light), .8), new Rect(14, 43, w - 28, h - 64)); c.DrawRectangle(null, new Pen(Solid(p.Mid + "77"), 1), new Rect(29, 58, w - 58, h - 94));
            var specular = Gradient(24, 0, w - 24, 0, (0, p.Light + "00"), (.14, p.Shine + "ee"), (.25, p.Shine + "22"), (.58, p.Shine + "44"), (.84, p.Shine + "dd"), (1, p.Shine + "00"));
            Rect(c, 24, 46, w - 48, 1.1, specular); Rect(c, 26, h - 25, w - 52, .8, specular);
        }
        static void Text(DrawingContext c, string value, double cx, double cy, double size, double tracking, Brush brush, double dpi)
        {
            var glyphs = new List<FormattedText>(); var width = -tracking;
            foreach (var letter in value) { var f = new FormattedText(letter.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Georgia"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), size, brush, dpi); glyphs.Add(f); width += f.WidthIncludingTrailingWhitespace + tracking; }
            var x = cx - width / 2; foreach (var f in glyphs) { c.DrawText(f, new Point(x, cy - f.Height / 2)); x += f.WidthIncludingTrailingWhitespace + tracking; }
        }
        internal static DrawingGroup Draw(bool emerald, double w, double height, int rows, int columns, double pitch, double coverWidth, double gap, double inset, double dpi, out DrawingGroup front, bool diamond = false)
        {
            var p = diamond ? Diamond : emerald ? Emerald : Gold; var padding = diamond ? 50d : TopPadding; var h = height - padding; var background = new DrawingGroup();
            using (var c = background.Open())
            {
                c.PushTransform(new TranslateTransform(0, padding)); Rect(c, 15, 41, w - 30, h - 65, Solid("#050b0d"));
                Rect(c, 23, 54, w - 46, h - 86, Gradient(0, 55, w, 0, (0, "#020605"), (.08, p.Wall), (.5, "#0a100d"), (.92, p.Wall), (1, "#020605"))); Rect(c, 25, 54, w - 50, h - 86, Grain);
                for (var row = 0; row < rows; row++)
                {
                    var top = Header + row * pitch; var bottom = Header + (row + 1) * pitch;
                    Rect(c, 25, top, w - 50, pitch, Gradient(0, top, 0, bottom, (0, "#000000dd"), (.22, "#00000000"), (.75, "#00000033"), (1, "#000000cc"))); c.PushClip(new RectangleGeometry(new Rect(25, top, w - 50, pitch)));
                    for (var col = 0; col < columns; col++)
                    {
                        var x = inset + col * (coverWidth + gap) + coverWidth / 2; var glow = new RadialGradientBrush { Center = new Point(.5, .22), GradientOrigin = new Point(.5, .12), RadiusX = .5, RadiusY = .5 };
                        glow.GradientStops.Add(new GradientStop(Color(p.Glow + "a6"), 0)); glow.GradientStops.Add(new GradientStop(Color(p.Glow + "42"), .22)); glow.GradientStops.Add(new GradientStop(Color(p.Glow + "0e"), .68)); glow.GradientStops.Add(new GradientStop(Color(p.Glow + "00"), 1)); c.DrawEllipse(glow, null, new Point(x, top + 23), coverWidth * .85, coverWidth * .85);
                    }
                    c.Pop(); Rect(c, 26, bottom - 42, w - 52, 29, Gradient(0, bottom - 42, 0, bottom - 13, (0, p.Light + "00"), (.55, p.Mid + "20"), (1, p.Light + "44")));
                }
                Rect(c, 25, 55, w - 50, h - 87, Gradient(25, 0, w - 25, 0, (0, "#00000099"), (.05, "#00000000"), (.95, "#00000000"), (1, "#00000099"))); c.Pop();
            }
            if (diamond) { front = DrawDiamondFront(w, height, rows, pitch, dpi); background.Freeze(); return background; }
            front = new DrawingGroup(); using (var c = front.Open())
            {
                c.PushTransform(new TranslateTransform(0, TopPadding)); Perimeter(c, w, h, p);
                Polygon(c, Gradient(0, 35, 0, 43, (0, "#15231d"), (.3, p.Mid), (.55, "#17241c"), (.85, p.Dark), (1, p.Light)), new(5, 36), new(w - 5, 36), new(w - 14, 43), new(14, 43)); Rect(c, 6, 43, w - 12, 4, Gradient(0, 43, 0, 47, (0, p.Shine), (.3, p.Mid), (.6, p.Dark), (1, p.Shade)));
                for (var row = 1; row < rows; row++) Ledge(c, w, Header + row * pitch - 11, p, false); Ledge(c, w, h - 22, p, true);
                var tone = emerald ? "emerald" : "gold"; Relief(c, tone + "-upper", 24, 54); Relief(c, tone + "-upper", w - 24, 54, 1, true); Relief(c, tone + "-lower", 25, h - 34); Relief(c, tone + "-lower", w - 25, h - 34, 1, true);
                if (emerald) { Relief(c, "emerald-side", 20, 54 + (h - 88) * .5); Relief(c, "emerald-side", w - 20, 54 + (h - 88) * .5); }
                var cap = Math.Min(1, (w - 12) / 512); Relief(c, tone + "-header", (w - 512 * cap) / 2, 0, cap);
                var letters = Gradient(0, 12 * cap, 0, 38 * cap, (0, emerald ? "#e2fff0" : "#fff5d0"), (.43, emerald ? "#7acdb2" : "#e5bc70"), (.53, emerald ? "#d6ffed" : "#fff3c5"), (1, emerald ? "#56a98f" : "#b88a43"));
                Text(c, "100% COMPLETE", w / 2, 25 * cap, 24 * cap, 2.6 * cap, letters, dpi); Text(c, emerald ? "BASE GAME + DLC" : "BASE GAME", w / 2, 47 * cap, Math.Max(9, 11 * cap), 2.1 * cap, Solid(emerald ? "#d2eddb" : "#e7d1a4"), dpi); c.Pop();
            }
            background.Freeze(); front.Freeze(); return background;
        }
    }
}
