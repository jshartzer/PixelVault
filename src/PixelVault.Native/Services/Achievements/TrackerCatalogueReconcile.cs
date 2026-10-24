using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PixelVaultNative
{
    sealed class TrackerCatalogueOptions
    {
        internal bool AddNew,UpdateMetadata,ArchiveMissing,RestorePresent;
    }
    sealed class TrackerCatalogueRow
    {
        public string Id { get; init; }
        public string Name { get; init; }
        public string Current { get; init; }
        public string Incoming { get; init; }
        public string Action { get; init; }
        internal bool Changed { get; init; }
    }
    sealed class TrackerCataloguePlan
    {
        internal string TrackerId,Before,After,Source,Fingerprint;
        internal IReadOnlyList<TrackerCatalogueRow> Rows;
        internal int Changes=>Rows.Count(r=>r.Changed);
        internal string Summary=>Changes+" selected changes · "+Rows.Count(r=>r.Action.StartsWith("Add"))+" additions · "+Rows.Count(r=>r.Action.Contains("Archive"))+" archives · "+Rows.Count(r=>r.Action.Contains("Restore"))+" restorations";
    }
    static class TrackerCatalogueReconcile
    {
        internal static string Json(AchievementTrackerDefinition definition)=>JsonSerializer.Serialize(definition,AchievementTrackingCatalog.Json);
        internal static AchievementTrackerDefinition Clone(AchievementTrackerDefinition definition)=>JsonSerializer.Deserialize<AchievementTrackerDefinition>(Json(definition),AchievementTrackingCatalog.Json);
        internal static TrackerCataloguePlan Build(AchievementTrackerDefinition current,AchievementTrackerDefinition incoming,TrackerCatalogueOptions options,string source)
        {
            AchievementTrackingCatalog.Validate(current);
            if(incoming?.Items==null || incoming.Items.Count==0 || incoming.Items.Count>10000 || incoming.Provider!=current.Provider || incoming.GameId!=current.GameId)
                throw new InvalidDataException("Choose a nonempty catalogue for the same game and provider.");
            if(!current.Milestones.SequenceEqual(incoming.Milestones??new(),StringComparer.Ordinal))
                throw new InvalidDataException("Milestone columns must match this tracker. Create a separate definition for a different record shape.");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            if(incoming.Items.Any(i=>i==null || string.IsNullOrWhiteSpace(i.Id) || !seen.Add(i.Id)))throw new InvalidDataException("Incoming item IDs must be nonblank and unique.");
            var result=Clone(current);var known=result.Items.ToDictionary(i=>i.Id,StringComparer.Ordinal);
            var positions=result.Items.Select((item,index)=>(item.Id,index)).ToDictionary(p=>p.Id,p=>p.index,StringComparer.Ordinal);
            var original=current.Items.ToDictionary(i=>i.Id,StringComparer.Ordinal);var observations=incoming.Items.ToDictionary(i=>i.Id,StringComparer.Ordinal);
            foreach(var item in incoming.Items)
            {
                if(known.TryGetValue(item.Id,out var prior))
                {
                    var archived=prior.Archived;
                    if(options.UpdateMetadata)
                    {
                        var replacement=CloneItem(item);replacement.Archived=archived;
                        result.Items[positions[item.Id]]=replacement;known[item.Id]=replacement;prior=replacement;
                    }
                    if(options.RestorePresent && !item.Archived)prior.Archived=false;
                }
                else if(options.AddNew){var added=CloneItem(item);result.Items.Add(added);known[item.Id]=added;}
            }
            if(options.ArchiveMissing)foreach(var item in result.Items.Where(i=>!seen.Contains(i.Id)))item.Archived=true;
            AchievementTrackingCatalog.Validate(result);
            // Portable validation also bounds field lengths; no progress/source data can enter this transaction.
            _=TrackerDefinitionTransfer.Serialize(result);
            foreach(var group in result.Items.Where(i=>!string.IsNullOrEmpty(i.GroupId)).GroupBy(i=>i.GroupId))
                if(group.Select(i=>i.Archived).Distinct().Count()>1)throw new InvalidDataException("Archive or restore every milestone of a record together. Include all its item IDs in the file.");
            var rows=new List<TrackerCatalogueRow>();
            foreach(var id in current.Items.Select(i=>i.Id).Concat(incoming.Items.Select(i=>i.Id)).Distinct(StringComparer.Ordinal))
            {
                original.TryGetValue(id,out var before);observations.TryGetValue(id,out var proposed);known.TryGetValue(id,out var after);
                bool changed=JsonSerializer.Serialize(before)!=JsonSerializer.Serialize(after);
                string action=before==null?(after==null?"Keep (new item not added)":"Add item"):
                    before.Archived!=after.Archived?(after.Archived?"Archive item":"Restore item"):changed?"Update metadata":proposed==null?"Keep (missing from file)":"Keep";
                if(before!=null && changed && before.Archived!=after.Archived && Metadata(before)!=Metadata(after))action+=" + metadata";
                rows.Add(new TrackerCatalogueRow { Id=id,Name=(proposed??before).Name,Current=Describe(before),Incoming=Describe(proposed),Action=action,Changed=changed });
            }
            var input=Json(incoming);
            return new TrackerCataloguePlan { TrackerId=current.Id,Before=Json(current),After=Json(result),Source=source??"Catalogue import",
                Fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))),Rows=rows };
        }
        static string Metadata(AchievementTrackerItem item){var copy=CloneItem(item);copy.Archived=false;return JsonSerializer.Serialize(copy);}
        static string Describe(AchievementTrackerItem item)=>item==null?"Missing":item.Name+" · "+(item.Category??"No category")+" · Target "+item.Target+(item.Archived?" · Archived":"")+" · Order "+item.Order
            +(string.IsNullOrEmpty(item.GroupId)?"":" · Record "+item.GroupId+" / "+item.Milestone)+" · "+(item.Detail??"")+" · "+string.Join(", ",(item.Identifiers??new()).Select(p=>p.Key+"="+p.Value));
        static AchievementTrackerItem CloneItem(AchievementTrackerItem item)=>JsonSerializer.Deserialize<AchievementTrackerItem>(JsonSerializer.Serialize(item),AchievementTrackingCatalog.Json);
        internal static AchievementTrackerDefinition FromSpreadsheet(TrackerImportSheet sheet,TrackerSpreadsheetMapping mapping,AchievementTrackerDefinition current)
        {
            if(sheet==null || mapping==null || mapping.Worksheet!=sheet.Name)throw new InvalidDataException("Choose a worksheet first.");
            string[] names={mapping.Id,mapping.Name,mapping.Category,mapping.Target,mapping.Detail,mapping.Group,mapping.Milestone};
            if(string.IsNullOrEmpty(names[0]) || names.Where(n=>!string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).Count()!=names.Count(n=>!string.IsNullOrEmpty(n)))
                throw new InvalidDataException("Map stable item IDs and use a different column for each field.");
            int Resolve(string n){if(string.IsNullOrEmpty(n))return -1;var cols=Enumerable.Range(0,sheet.Headers.Length).Where(c=>sheet.Headers[c]==n).ToList();if(cols.Count!=1)throw new InvalidDataException("A mapped header is missing or duplicated: "+n);return cols[0];}
            var columns=names.Select(Resolve).ToArray();var original=current.Items.ToDictionary(i=>i.Id,StringComparer.Ordinal);var items=new List<AchievementTrackerItem>();var seen=new HashSet<string>(StringComparer.Ordinal);var lastOrder=current.Items.Max(i=>i.Order);
            for(int r=1;r<sheet.Rows.Count;r++)
            {
                var row=sheet.Rows[r];if(row.All(string.IsNullOrWhiteSpace) && !sheet.Formulas.Any(f=>f.Row==r))continue;
                if(columns.Any(c=>c>=0 && sheet.Formulas.Contains((r,c))))throw new InvalidDataException("Row "+(r+1)+": replace mapped formulas/errors with values.");
                string Get(int i)=>columns[i]<0 || columns[i]>=row.Length?"":(row[columns[i]]??"").Trim();
                var id=Get(0);if(id.Length==0 || !seen.Add(id))throw new InvalidDataException("Row "+(r+1)+": stable IDs must be nonblank and unique.");
                original.TryGetValue(id,out var prior);var item=prior==null?new AchievementTrackerItem { Id=id,Order=checked(lastOrder+r) }:CloneItem(prior);
                item.Archived=false; // A present spreadsheet row is a restoration candidate only when explicitly selected.
                if(columns[1]>=0)item.Name=Get(1);if(columns[2]>=0)item.Category=Get(2);if(columns[4]>=0)item.Detail=Get(4);
                if(columns[3]>=0){if(!int.TryParse(Get(3),NumberStyles.None,CultureInfo.InvariantCulture,out var target) || target<1 || target>1000000)throw new InvalidDataException("Row "+(r+1)+": target must be a whole number from 1 to 1,000,000.");item.Target=target;}
                if(columns[5]>=0)item.GroupId=Get(5);if(columns[6]>=0)item.Milestone=Get(6);
                if(string.IsNullOrWhiteSpace(item.Name))throw new InvalidDataException("Row "+(r+1)+": map a name for new items.");
                items.Add(item);
            }
            if(items.Count==0)throw new InvalidDataException("The worksheet has no catalogue items.");
            var incoming=Clone(current);incoming.Items=items;return incoming;
        }
    }
}
