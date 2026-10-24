using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelVaultNative
{
    // Portable metadata only. Account observations and executable source settings have a separate contract.
    static class TrackerDefinitionTransfer
    {
        const int Limit = 4 * 1024 * 1024;
        const string Format = "PixelVault.TrackerDefinition";
        static readonly JsonSerializerOptions Options = new(AchievementTrackingCatalog.Json)
        { MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        sealed class Document
        {
            public string Format { get; set; }
            public int Version { get; set; }
            public AchievementTrackerDefinition Definition { get; set; }
        }
        internal static AchievementTrackerDefinition Read(string path)
        {
            using var file = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            if (file.Length < 1 || file.Length > Limit) throw new InvalidDataException("Choose a tracker definition JSON smaller than 4 MiB.");
            using var reader = new StreamReader(file,new UTF8Encoding(false,true),true);
            return Parse(reader.ReadToEnd());
        }
        internal static AchievementTrackerDefinition Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > Limit) throw new InvalidDataException("Choose a tracker definition JSON smaller than 4 MiB.");
            try
            {
                using var document = JsonDocument.Parse(json,new JsonDocumentOptions { MaxDepth=32 });
                void Unique(JsonElement node)
                {
                    if (node.ValueKind == JsonValueKind.Object)
                    {
                        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var p in node.EnumerateObject()) { if (!keys.Add(p.Name)) throw new InvalidDataException("Definition JSON contains a repeated field: "+p.Name); Unique(p.Value); }
                    }
                    else if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) Unique(child);
                }
                Unique(document.RootElement);
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Choose a tracker definition, rather than a progress snapshot.");
                bool wrapped = document.RootElement.EnumerateObject().Any(p=>p.Name.Equals("Format",StringComparison.OrdinalIgnoreCase));
                AchievementTrackerDefinition definition;
                if (wrapped)
                {
                    var envelope=JsonSerializer.Deserialize<Document>(json,Options);
                    if (envelope.Format != Format || envelope.Version != 1) throw new InvalidDataException("This tracker definition format/version is not supported.");
                    definition=envelope.Definition;
                }
                else definition=JsonSerializer.Deserialize<AchievementTrackerDefinition>(json,Options); // Earlier definition-only exports.
                Validate(definition); return definition;
            }
            catch (JsonException ex) { throw new InvalidDataException("Invalid tracker definition JSON. Progress snapshots, account data and source settings are not definition fields.",ex); }
        }
        static void Validate(AchievementTrackerDefinition d)
        {
            AchievementTrackingCatalog.Validate(d);
            bool TooLong(string s,int limit)=>s?.Length>limit;
            if (d.Provider != "steam" && d.Provider != "retroachievements" || TooLong(d.GameId,200) || TooLong(d.GameTitle,500)
                || TooLong(d.AchievementId,200) || TooLong(d.Title,200) || TooLong(d.Description,20000) || string.IsNullOrWhiteSpace(d.CompletionLabel) || TooLong(d.CompletionLabel,100)
                || d.Milestones.Any(m=>TooLong(m,100)) || d.CategoryTargets.Keys.Any(k=>TooLong(k,200))
                || d.Items.Any(i=>TooLong(i.Id,200) || TooLong(i.Name,1000) || TooLong(i.Category,200) || TooLong(i.Detail,20000) || TooLong(i.GroupId,200) || TooLong(i.Milestone,100)
                    || i.Identifiers == null || i.Identifiers.Count > 32 || i.Identifiers.Any(p=>string.IsNullOrWhiteSpace(p.Key) || TooLong(p.Key,200) || TooLong(p.Value,4000))))
                throw new InvalidDataException("The definition has unsupported provider metadata or fields exceeding the supported lengths.");
        }
        internal static string Serialize(AchievementTrackerDefinition definition)
        {
            Validate(definition);
            var json=JsonSerializer.Serialize(new Document { Format=Format,Version=1,Definition=definition },Options);
            if (Encoding.UTF8.GetByteCount(json)>Limit) throw new InvalidDataException("This definition exceeds the 4 MiB portable definition limit.");
            return json;
        }
        internal static AchievementTrackerDefinition Prepare(AchievementTrackerDefinition definition, IEnumerable<string> existingIds, bool copy, string title, string achievementId)
        {
            var result=Parse(Serialize(definition));
            if (copy) result.Id="custom."+Guid.NewGuid().ToString("N");
            else if (existingIds.Contains(result.Id,StringComparer.Ordinal)) throw new InvalidDataException("This tracker ID already exists. Choose Create an independent copy to retain the original tracker and its progress.");
            result.Title=title?.Trim(); result.AchievementId=string.IsNullOrEmpty(achievementId)?null:achievementId;
            Validate(result);return result;
        }
        internal static void ValidateContext(AchievementTrackerDefinition definition,string provider,string gameId)
        {
            if (!string.IsNullOrEmpty(provider) && definition.Provider!=provider || !string.IsNullOrEmpty(gameId) && definition.GameId!=gameId)
                throw new InvalidDataException("This definition belongs to a different game. Open Trackers from the Hub to import it for that game.");
        }
    }
}
