using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PixelVaultNative
{
    sealed class TrackerSpreadsheetMapping
    {
        public string Worksheet { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Target { get; set; }
        public string Detail { get; set; }
        public string Id { get; set; }
        public string Current { get; set; }
        public string Completed { get; set; }
        public string Availability { get; set; }
        public string Note { get; set; }
        public string Group { get; set; }
        public string Milestone { get; set; }
        public List<TrackerMilestoneColumn> CompletionColumns { get; set; } = new();
        internal string[] Headers => new[] { Name, Category, Target, Detail, Id, Current, Completed, Availability, Note,Group,Milestone }
            .Concat((CompletionColumns ?? new()).Select(c => c?.Header)).ToArray();
        internal bool HasProgress => Headers.Skip(5).Take(4).Any(h => !string.IsNullOrEmpty(h)) || (CompletionColumns?.Any(c => !string.IsNullOrEmpty(c?.Header)) ?? false);
    }
    sealed class TrackerSpreadsheetDraft
    {
        internal List<AchievementTrackerItem> Items;
        internal List<AchievementTrackerProgress> Progress = new();
        internal List<string> Milestones = new();
    }
    static class TrackerSpreadsheetProgress
    {
        internal static (List<TrackerImportSheet> Sheets, long Ticks, string Fingerprint) ReadStable(string path)
        {
            var info = new FileInfo(path); var stamp = info.LastWriteTimeUtc; var length = info.Length;
            if (length < 1 || length > 16 * 1024 * 1024) throw new InvalidDataException("Choose a spreadsheet smaller than 16 MiB.");
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var fingerprint = Convert.ToHexString(SHA256.HashData(file)); var sheets = TrackerSpreadsheetImport.Read(path);
            var after = new FileInfo(path);
            if (file.Length != length || after.Length != length || after.LastWriteTimeUtc != stamp) throw new IOException("The workbook changed during the read. Save it, then load it again.");
            return (sheets, stamp.Ticks, fingerprint);
        }
        internal static TrackerSpreadsheetDraft Build(TrackerImportSheet sheet, TrackerSpreadsheetMapping mapping, AchievementTrackerDefinition existing = null)
        {
            if (sheet == null || mapping == null || mapping.Worksheet != sheet.Name) throw new InvalidDataException("Choose the saved worksheet; it was not found in this workbook.");
            var names = mapping.Headers;
            if (names.Any(h => h?.Length > 500) || names.Where(h => !string.IsNullOrEmpty(h)).Distinct(StringComparer.Ordinal).Count() != names.Count(h => !string.IsNullOrEmpty(h)))
                throw new InvalidDataException("Map each column to only one field.");
            int Resolve(string name)
            {
                if (string.IsNullOrEmpty(name)) return -1;
                var matches = Enumerable.Range(0, sheet.Headers.Length).Where(i => sheet.Headers[i] == name).ToList();
                if (matches.Count != 1) throw new InvalidDataException("Mapped header is missing or duplicated: " + name);
                return matches[0];
            }
            var cols = names.Select(Resolve).ToArray();
            if (mapping.CompletionColumns?.Count > 0) return BuildCompletionColumns(sheet, mapping, existing, cols);
            if (existing != null && (cols[4] < 0 || cols[5] < 0 && cols[6] < 0))
                throw new InvalidDataException("Refreshing an existing tracker requires stable item IDs and a Current or Completed column.");
            var items = existing?.Items ?? TrackerSpreadsheetImport.Items(sheet, cols[0], cols[1], cols[2], cols[3], cols[4]);
            var known = items.ToDictionary(i => i.Id, StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new TrackerSpreadsheetDraft { Items = items };
            if (existing == null && (cols[9] >= 0 || cols[10] >= 0))
            {
                if (cols[9] < 0 || cols[10] < 0) throw new InvalidDataException("Map both Record ID and Milestone for a row-per-box spreadsheet.");
                int index = 0;
                for (int r = 1; r < sheet.Rows.Count; r++)
                {
                    var row = sheet.Rows[r]; if (row.All(string.IsNullOrWhiteSpace) && !sheet.Formulas.Any(f => f.Row == r)) continue;
                    string Cell(int col) => col < row.Length ? (row[col] ?? "").Trim() : "";
                    items[index].GroupId = Cell(cols[9]); items[index++].Milestone = Cell(cols[10]);
                }
                result.Milestones = TrackerMilestones.Labels(items.Select(i => i.Milestone).Distinct());
                AchievementTrackingCatalog.Validate(new AchievementTrackerDefinition { Id="preview",Provider="steam",GameId="preview",GameTitle="Preview",Title="Preview",Items=items,Milestones=result.Milestones });
            }
            for (int r = 1; r < sheet.Rows.Count; r++)
            {
                var row = sheet.Rows[r]; if (row.All(string.IsNullOrWhiteSpace) && !sheet.Formulas.Any(f => f.Row == r)) continue;
                string Get(int index) => cols[index] < 0 || cols[index] >= row.Length ? "" : (row[cols[index]] ?? "").Trim();
                if (cols.Any(c => c >= 0 && sheet.Formulas.Contains((r, c)))) throw new InvalidDataException($"Row {r + 1}: replace mapped formulas/errors with values.");
                var id = Get(4); if (id.Length == 0 && existing == null && cols[4] < 0) id = "row." + r;
                if (id.Length == 0 || !seen.Add(id) || !known.TryGetValue(id, out var item))
                    throw new InvalidDataException($"Row {r + 1}: stable item ID is blank, duplicated or not in this tracker. New/missing catalogue items are not merged by this progress import.");
                int? current = null;
                if (Get(5).Length > 0)
                {
                    if (!int.TryParse(Get(5), NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 0 || n > 1000000)
                        throw new InvalidDataException($"Row {r + 1}: current count must be a whole number from 0 to 1,000,000, or blank for unknown.");
                    current = n;
                }
                if (Get(6).Length > 0)
                {
                    bool completed = Get(6).ToLowerInvariant() switch
                    {
                        "true" or "yes" or "1" or "complete" or "completed" => true,
                        "false" or "no" or "0" or "incomplete" or "not complete" => false,
                        _ => throw new InvalidDataException($"Row {r + 1}: completed must be true/false, yes/no or 1/0.")
                    };
                    if (current.HasValue && (current >= item.Target) != completed) throw new InvalidDataException($"Row {r + 1}: Current and Completed disagree.");
                    current ??= completed ? item.Target : 0;
                }
                var availability = TrackerAvailability.Unknown;
                if (Get(7).Length > 0 && (!Enum.TryParse(Get(7), true, out availability) || !Enum.IsDefined(availability)))
                    throw new InvalidDataException($"Row {r + 1}: availability must be Unknown, Available or Locked.");
                if (Get(8).Length > 20000) throw new InvalidDataException($"Row {r + 1}: notes may contain at most 20,000 characters.");
                if (mapping.HasProgress && (existing != null || Enumerable.Range(5,4).Any(i=>Get(i).Length>0)))
                    result.Progress.Add(new AchievementTrackerProgress { ItemId = id, Current = current, Availability = availability, Note = Get(8) });
            }
            if (seen.Count == 0) throw new InvalidDataException("The worksheet has no item rows.");
            return result;
        }

        static TrackerSpreadsheetDraft BuildCompletionColumns(TrackerImportSheet sheet, TrackerSpreadsheetMapping mapping, AchievementTrackerDefinition existing, int[] cols)
        {
            var labels = TrackerMilestones.Labels(mapping.CompletionColumns.Select(c => c?.Label));
            if (cols[5] >= 0 || cols[6] >= 0 || cols[9] >= 0 || cols[10] >= 0)
                throw new InvalidDataException("For multiple boxes, map each named completion column instead of Current, Completed, Record ID or Milestone.");
            if (existing != null && (cols[4] < 0 || !labels.SequenceEqual(existing.Milestones) || !mapping.CompletionColumns.Any(c=>!string.IsNullOrEmpty(c.Header))))
                throw new InvalidDataException("Match the tracker's completion names and map stable record IDs plus at least one completion column.");
            var records = existing == null ? TrackerSpreadsheetImport.Items(sheet, cols[0], cols[1], cols[2], cols[3], cols[4]) : null;
            var items = existing?.Items ?? TrackerMilestones.Expand(records, labels);
            var groups = items.GroupBy(i=>i.GroupId).ToDictionary(g=>g.Key,g=>g.ToDictionary(i=>i.Milestone));
            var draft = new TrackerSpreadsheetDraft { Items=items,Milestones=labels }; var seen=new HashSet<string>();
            for(int r=1;r<sheet.Rows.Count;r++)
            {
                var row=sheet.Rows[r]; if(row.All(string.IsNullOrWhiteSpace)&&!sheet.Formulas.Any(f=>f.Row==r))continue;
                string Cell(int c)=>c<0||c>=row.Length?"":(row[c]??"").Trim();
                if(cols.Any(c=>c>=0&&sheet.Formulas.Contains((r,c))))throw new InvalidDataException($"Row {r+1}: replace mapped formulas/errors with values.");
                var id=cols[4]<0&&existing==null?"row."+r:Cell(cols[4]);
                if(!seen.Add(id)||!groups.TryGetValue(id,out var group))throw new InvalidDataException($"Row {r+1}: record ID is blank, duplicated or not in this tracker.");
                var note=Cell(cols[8]);if(note.Length>20000)throw new InvalidDataException($"Row {r+1}: notes may contain at most 20,000 characters.");
                var availability=TrackerAvailability.Unknown;
                if(Cell(cols[7]).Length>0&&(!Enum.TryParse(Cell(cols[7]),true,out availability)||!Enum.IsDefined(availability)))throw new InvalidDataException($"Row {r+1}: availability must be Unknown, Available or Locked.");
                for(int m=0;m<labels.Count;m++)
                {
                    if(cols[11+m]<0)continue;
                    var value=Cell(cols[11+m]); int? count=value.ToLowerInvariant() switch
                    {
                        ""=>null,"true" or "yes" or "1" or "complete" or "completed"=>1,
                        "false" or "no" or "0" or "incomplete" or "not complete"=>0,
                        _=>throw new InvalidDataException($"Row {r+1}: {labels[m]} must be true/false, yes/no or 1/0; leave blank for unknown.")
                    };
                    if(existing==null && value.Length==0 && note.Length==0 && Cell(cols[7]).Length==0)continue;
                    draft.Progress.Add(new AchievementTrackerProgress { ItemId=group[labels[m]].Id,Current=count,Availability=availability,Note=note });
                }
            }
            if(seen.Count==0)throw new InvalidDataException("The worksheet has no item rows.");
            return draft;
        }

        // Values are inline strings, including formula-like names/notes; no formulas or external links are emitted.
        internal static void ExportWorkbook(string path, AchievementTrackerDefinition definition, IReadOnlyList<AchievementTrackerState> states = null)
        {
            XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            bool grouped=definition.Milestones.Count>0;
            var headers=new[] { "ID", "Name", "Category", "Target", "Details", "Current", "Completed", "Availability", "Notes" };
            var rows = new List<string[]> { grouped?headers.Concat(new[]{"Record ID","Milestone"}).ToArray():headers };
            var byId = states?.ToDictionary(x => x.Item.Id);
            foreach (var item in definition.Items.Where(i=>!i.Archived).OrderBy(i => i.Order))
            {
                AchievementTrackerState state = null; byId?.TryGetValue(item.Id, out state);
                var values=new[] { item.Id, item.Name, item.Category, item.Target.ToString(CultureInfo.InvariantCulture), item.Detail,
                    state?.Current?.ToString(CultureInfo.InvariantCulture) ?? "", state?.IsKnown == true ? (state.Complete ? "true" : "false") : "",
                    state == null ? "" : state.Availability.ToString(), state?.Note ?? "" };
                rows.Add(grouped?values.Concat(new[]{item.GroupId,item.Milestone}).ToArray():values);
            }
            var xmlRows = rows.Select((row, n) => new XElement(s + "row", new XAttribute("r", n + 1), row.Select((value, col) =>
                new XElement(s + "c", new XAttribute("r", ((char)('A' + col)).ToString() + (n + 1)), new XAttribute("t", "inlineStr"),
                    new XElement(s + "is", new XElement(s + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? ""))))));
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None); using var zip = new ZipArchive(file, ZipArchiveMode.Create);
            void Part(string name, XDocument doc) { using var stream = zip.CreateEntry(name).Open(); doc.Save(stream); }
            XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Part("[Content_Types].xml", new XDocument(new XElement(types + "Types",
                new XElement(types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(types + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(types + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
            Part("_rels/.rels", new XDocument(new XElement(rel + "Relationships", new XElement(rel + "Relationship", new XAttribute("Id", "r1"), new XAttribute("Type", office.NamespaceName + "/officeDocument"), new XAttribute("Target", "xl/workbook.xml")))));
            Part("xl/workbook.xml", new XDocument(new XElement(s + "workbook", new XAttribute(XNamespace.Xmlns + "r", office), new XElement(s + "sheets", new XElement(s + "sheet", new XAttribute("name", "Tracker"), new XAttribute("sheetId", "1"), new XAttribute(office + "id", "r1"))))));
            Part("xl/_rels/workbook.xml.rels", new XDocument(new XElement(rel + "Relationships", new XElement(rel + "Relationship", new XAttribute("Id", "r1"), new XAttribute("Type", office.NamespaceName + "/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml")))));
            double[] widths=grouped?new double[]{44,50,24,12,60,12,14,16,60,44,24}:new double[]{44,50,24,12,60,12,14,16,60};
            Part("xl/worksheets/sheet1.xml", new XDocument(new XElement(s + "worksheet",
                new XElement(s+"sheetViews",new XElement(s+"sheetView",new XAttribute("workbookViewId",0),new XElement(s+"pane",new XAttribute("ySplit",1),new XAttribute("topLeftCell","A2"),new XAttribute("activePane","bottomLeft"),new XAttribute("state","frozen")))),
                new XElement(s+"cols",widths.Select((width,i)=>new XElement(s+"col",new XAttribute("min",i+1),new XAttribute("max",i+1),new XAttribute("width",width),new XAttribute("customWidth",1)))),
                new XElement(s + "sheetData", xmlRows),new XElement(s+"autoFilter",new XAttribute("ref","A1:"+(grouped?"K":"I")+rows.Count)))));
        }
    }

    sealed class SpreadsheetTrackerSource : ITrackerDataSource, ITrackerSourceChangeDetector
    {
        internal const string AdapterId = "pixelvault.spreadsheet";
        public string Id => AdapterId;
        public string Version => "1";
        public TrackerSourceCapabilities Capabilities => TrackerSourceCapabilities.Refresh | TrackerSourceCapabilities.PartialObservations | TrackerSourceCapabilities.ExternalObservationTime;
        public string ChangeSignature(TrackerSourceBinding binding) => TrackerSourceFiles.Signature(new[] { new FileInfo(binding.Location) }, 16 * 1024 * 1024);
        public void Validate(TrackerSourceBinding binding, AchievementTrackerDefinition definition)
        {
            var m = binding.Spreadsheet;
            if (binding.AdapterId != Id || binding.AdapterVersion != Version || !Path.IsPathFullyQualified(binding.Location ?? "")
                || !new[] { ".xlsx", ".csv" }.Contains(Path.GetExtension(binding.Location).ToLowerInvariant())
                || m == null || string.IsNullOrWhiteSpace(m.Worksheet) || m.Worksheet.Length > 200 || string.IsNullOrEmpty(m.Id)
                || string.IsNullOrEmpty(m.Current) && string.IsNullOrEmpty(m.Completed) && !(m.CompletionColumns?.Any(c=>!string.IsNullOrEmpty(c?.Header))??false) || m.Headers.Any(h => h?.Length > 500))
                throw new InvalidDataException("Choose an XLSX/CSV file, worksheet, stable item or record ID, and at least one progress column.");
            if(m.CompletionColumns?.Count>0)
            {
                var labels=TrackerMilestones.Labels(m.CompletionColumns.Select(c=>c?.Label));
                if(!labels.SequenceEqual(definition.Milestones))throw new InvalidDataException("The spreadsheet completion names must match this tracker's columns.");
            }
        }
        public Task<TrackerSourceObservation> ObserveAsync(TrackerSourceBinding binding, AchievementTrackerDefinition definition, CancellationToken token)
        {
            Validate(binding, definition); token.ThrowIfCancellationRequested();
            var read = TrackerSpreadsheetProgress.ReadStable(binding.Location); token.ThrowIfCancellationRequested();
            var matches = read.Sheets.Where(s => s.Name == binding.Spreadsheet.Worksheet).ToList();
            if (matches.Count != 1) throw new InvalidDataException("The saved worksheet is missing or ambiguous. Update the source mapping.");
            var rows = TrackerSpreadsheetProgress.Build(matches[0], binding.Spreadsheet, definition).Progress;
            var snapshot = new AchievementTrackerSnapshot { TrackerId = definition.Id, Provider = definition.Provider, GameId = definition.GameId, Account = binding.Account,
                ObservedUtcTicks = read.Ticks, Source = "Spreadsheet · " + Path.GetFileName(binding.Location) + " · " + binding.Spreadsheet.Worksheet,
                Evidence = "Stable values-only workbook read; rows matched by stable IDs. Missing rows remain unchanged. Blank counts mean unknown; zero remains zero. Existing notes/manual counts are preserved; imported notes only fill empty notes.", Items = rows };
            return Task.FromResult(new TrackerSourceObservation { SnapshotJson = JsonSerializer.Serialize(snapshot, AchievementTrackingCatalog.Json), Fingerprint = read.Fingerprint });
        }
    }
}
