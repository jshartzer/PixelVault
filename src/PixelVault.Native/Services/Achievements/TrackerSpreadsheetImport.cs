using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PixelVaultNative
{
    sealed class TrackerImportSheet
    {
        internal string Name;
        internal List<string[]> Rows = new();
        internal HashSet<(int Row, int Column)> Formulas = new();
        public override string ToString() => Name;
        internal string[] Headers => Rows.FirstOrDefault() ?? Array.Empty<string>();
    }

    /// <summary>Bounded, values-only XLSX/CSV reader. Never evaluates formulas or follows external links.</summary>
    static class TrackerSpreadsheetImport
    {
        const int MaxRows = 10001, MaxColumns = 128, MaxBytes = 16 * 1024 * 1024;
        static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        internal static List<TrackerImportSheet> Read(string path)
        {
            if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("Choose a spreadsheet smaller than 16 MiB.");
            if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                return new() { new TrackerImportSheet { Name = Path.GetFileNameWithoutExtension(path), Rows = Csv(File.ReadAllText(path)) } };
            if (!Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Save the workbook as .xlsx or CSV first.");
            using var zip = ZipFile.OpenRead(path);
            if (zip.Entries.Count > 2048 || zip.Entries.Sum(e => e.Length) > 64L * 1024 * 1024)
                throw new InvalidDataException("The expanded workbook is too large.");
            XDocument ReadXml(string name)
            {
                var entry = zip.GetEntry(name) ?? throw new InvalidDataException("Missing workbook part: " + name);
                if (entry.Length > MaxBytes) throw new InvalidDataException("A workbook part is too large.");
                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes });
                return XDocument.Load(reader);
            }
            var strings = zip.GetEntry("xl/sharedStrings.xml") == null ? new List<string>()
                : ReadXml("xl/sharedStrings.xml").Descendants(S + "si").Select(e => string.Concat(e.Descendants(S + "t").Select(t => t.Value))).ToList();
            var relations = ReadXml("xl/_rels/workbook.xml.rels").Root.Elements()
                .Where(e => (string)e.Attribute("TargetMode") != "External")
                .ToDictionary(e => (string)e.Attribute("Id"), e => (string)e.Attribute("Target"));
            var result = new List<TrackerImportSheet>();
            foreach (var info in ReadXml("xl/workbook.xml").Descendants(S + "sheet"))
            {
                if (!relations.TryGetValue((string)info.Attribute(R + "id") ?? "", out var target)) continue;
                var part = new Uri(new Uri("https://workbook.local/xl/workbook.xml"), target).AbsolutePath.TrimStart('/');
                var document = ReadXml(part);
                // Ignore chartsheets and other non-tabular sheet types.
                if (document.Root?.Name != S + "worksheet") continue;
                var sheet = new TrackerImportSheet { Name = (string)info.Attribute("name") ?? "Sheet" };
                foreach (var row in document.Descendants(S + "sheetData").Elements(S + "row"))
                {
                    if (sheet.Rows.Count >= MaxRows) throw new InvalidDataException("A worksheet may contain at most 10,000 items plus its header.");
                    var cells = new Dictionary<int, string>();
                    foreach (var cell in row.Elements(S + "c"))
                    {
                        var reference = (string)cell.Attribute("r") ?? throw new InvalidDataException("A cell is missing its address.");
                        int col = 0;
                        foreach (var ch in reference.TakeWhile(char.IsLetter)) col = checked(col * 26 + char.ToUpperInvariant(ch) - 'A' + 1);
                        if (col < 1 || col > MaxColumns) throw new InvalidDataException("Use the first 128 worksheet columns.");
                        var type = (string)cell.Attribute("t");
                        var value = cell.Element(S + "v")?.Value ?? "";
                        if (type == "s")
                        {
                            if (!int.TryParse(value, out var n) || n < 0 || n >= strings.Count) throw new InvalidDataException("Invalid shared text reference.");
                            value = strings[n];
                        }
                        else if (type == "inlineStr") value = string.Concat(cell.Descendants(S + "t").Select(t => t.Value));
                        if (type == "e") value = "#SPREADSHEET_ERROR";
                        if (cell.Element(S + "f") != null || type == "e") sheet.Formulas.Add((sheet.Rows.Count, col - 1));
                        cells.Add(col - 1, value);
                    }
                    var values = new string[cells.Keys.DefaultIfEmpty(-1).Max() + 1];
                    foreach (var cell in cells) values[cell.Key] = cell.Value;
                    sheet.Rows.Add(values);
                }
                if (sheet.Rows.Count > 0) result.Add(sheet);
            }
            if (result.Count == 0) throw new InvalidDataException("The workbook has no data worksheets.");
            return result;
        }

        internal static List<string[]> Csv(string text)
        {
            var rows = new List<string[]>(); var row = new List<string>(); var value = new StringBuilder(); bool quoted = false, endedQuote = false;
            void Cell() { row.Add(value.ToString()); value.Clear(); endedQuote = false; if (row.Count > MaxColumns) throw new InvalidDataException("Use at most 128 columns."); }
            void Row() { Cell(); rows.Add(row.ToArray()); row.Clear(); if (rows.Count > MaxRows) throw new InvalidDataException("Use at most 10,000 items plus the header."); }
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; }
                    else if (ch == '"') { quoted = false; endedQuote = true; }
                    else value.Append(ch);
                }
                else if (ch == ',' ) Cell();
                else if (ch == '\n' || ch == '\r') { if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; Row(); }
                else if (ch == '"' && value.Length == 0 && !endedQuote) quoted = true;
                else if (endedQuote || ch == '"') throw new InvalidDataException("Malformed CSV quoting.");
                else value.Append(ch);
            }
            if (quoted) throw new InvalidDataException("A CSV quoted field is unfinished.");
            if (row.Count > 0 || value.Length > 0 || endedQuote) Row();
            if (rows.Count == 0) throw new InvalidDataException("The spreadsheet is empty.");
            return rows;
        }

        internal static List<AchievementTrackerItem> Items(TrackerImportSheet sheet, int nameColumn, int categoryColumn = -1, int targetColumn = -1, int detailColumn = -1, int idColumn = -1)
        {
            if (nameColumn < 0 || nameColumn >= sheet.Headers.Length) throw new InvalidDataException("Choose the item name column.");
            var mapped = new[] { nameColumn, categoryColumn, targetColumn, detailColumn, idColumn }.Where(n => n >= 0).ToList();
            if (mapped.Any(n => n >= sheet.Headers.Length) || mapped.Distinct().Count() != mapped.Count) throw new InvalidDataException("Map each column to only one field.");
            var result = new List<AchievementTrackerItem>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int r = 1; r < sheet.Rows.Count; r++)
            {
                var row = sheet.Rows[r];
                string Get(int col) => col < 0 || col >= row.Length ? "" : (row[col] ?? "").Trim();
                if (row.All(string.IsNullOrWhiteSpace) && !sheet.Formulas.Any(f => f.Row == r)) continue;
                if (mapped.Any(col => sheet.Formulas.Contains((r, col)))) throw new InvalidDataException($"Row {r + 1}: replace formulas and errors in mapped columns with values before importing.");
                var name = Get(nameColumn);
                if (name.Length == 0 || name.Length > 1000) throw new InvalidDataException($"Row {r + 1}: enter an item name (up to 1,000 characters).");
                var targetText = Get(targetColumn); int target = 1;
                if (targetText.Length > 0 && (!int.TryParse(targetText, NumberStyles.None, CultureInfo.InvariantCulture, out target) || target < 1 || target > 1000000))
                    throw new InvalidDataException($"Row {r + 1}: target must be a whole number from 1 to 1,000,000.");
                var id = Get(idColumn); if (id.Length == 0) id = "row." + r;
                if (id.Length > 200 || !ids.Add(id)) throw new InvalidDataException($"Row {r + 1}: item IDs must be unique and at most 200 characters.");
                if (Get(detailColumn).Length > 20000 || Get(categoryColumn).Length > 200) throw new InvalidDataException($"Row {r + 1}: detail or category is too long.");
                result.Add(new AchievementTrackerItem { Id = id, Name = name, Category = Get(categoryColumn).Length == 0 ? "Checklist" : Get(categoryColumn), Target = target, Detail = Get(detailColumn), Order = result.Count });
            }
            if (result.Count == 0 || result.Count > 10000) throw new InvalidDataException("Choose a sheet containing between 1 and 10,000 items below the header.");
            return result;
        }
    }
}
