using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using PixelVaultNative;

namespace PixelVault.Companion;

/// <summary>One owner, explicitly approved provider accounts. Browser parameters cannot select other accounts.</summary>
public sealed class TrackerWorkspace
{
    public TrackerRepository Repository { get; }
    public WorkspaceManifest? Manifest { get; }
    private readonly CompanionOptions options;
    public bool ReadOnly => options.DemoEnabled || Manifest is null;
    public bool Sample => options.DemoEnabled;
    public string Identity=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sample?"sample-v1":JsonSerializer.Serialize(Manifest))));

    public TrackerWorkspace(CompanionOptions options)
    {
        this.options=options;
        var file=Path.Combine(options.DataRoot,"workspace.json");
        if(File.Exists(file))
        {
            Manifest=JsonSerializer.Deserialize<WorkspaceManifest>(File.ReadAllText(file))
                ?? throw new InvalidDataException("Invalid Companion workspace manifest.");
            if(Manifest.Version!=1 || Manifest.Mode!="isolated-copy" || Manifest.Accounts is null || Manifest.Accounts.Count==0
                || Manifest.Accounts.Any(p=>string.IsNullOrWhiteSpace(p.Key)||string.IsNullOrWhiteSpace(p.Value)))
                throw new InvalidDataException("Unsupported Companion workspace manifest.");
            if(!File.Exists(Path.Combine(options.DataRoot,"achievement-tracking","pixelvault-tracking.sqlite")))
                throw new InvalidDataException("The prepared tracker database is missing. Restore the workspace before starting.");
            Repository=new TrackerRepository(options.DataRoot,pooling:false);
            foreach(var definition in Repository.Definitions()) AchievementTrackingCatalog.Validate(definition);
        }
        else Repository=null!;
    }

    public IReadOnlyList<AchievementTrackerDefinition> Definitions() => Sample ? DemoTrackers.Create().Select(t=>t.Definition).ToList()
        : Manifest is null ? [] : Repository.Definitions().Where(d=>Manifest.Accounts.ContainsKey(d.Provider)).ToList();

    public string Account(string tracker)
    {
        if(ReadOnly)throw new InvalidDataException("This workspace is read-only. Prepare a separate tracker workspace to enable editing.");
        var definition=Definitions().FirstOrDefault(d=>d.Id==tracker)??throw new KeyNotFoundException("Tracker not found.");
        return AchievementTrackingCatalog.AccountKey(definition.Provider,Manifest!.Accounts[definition.Provider]);
    }

    public object View(string tracker)
    {
        if(Sample)
        {
            var sample=DemoTrackers.Create().FirstOrDefault(t=>t.Definition.Id==tracker)??throw new KeyNotFoundException("Tracker not found.");
            return new {sample.Definition,sample.States,sample.Totals,sourceLinked=false,sample=true,readOnly=true};
        }
        if(Manifest is null)throw new KeyNotFoundException("Tracker not found.");
        var editor=Repository.ReadEditor(tracker,Account(tracker));
        return new {editor.Definition,editor.States,editor.Totals,editor.Versions,editor.ProtectedItems,sourceLinked=Repository.Source(tracker,Account(tracker)) is not null,sample=false,readOnly=false,
            connection="isolated-copy",lastObservedUtcTicks=editor.States.Select(s=>s.ObservedUtcTicks).DefaultIfEmpty().Max()};
    }

    internal IReadOnlyList<TrackerListing> Overview()=>Sample?DemoTrackers.Create().Select(t=>new TrackerListing(t.Definition,t.Totals)).ToList()
        :Manifest is null?[]:Repository.ListForAccounts(Manifest.Accounts);

    public IReadOnlyList<object> Trackers() => Definitions().Select(d=>
    {
        var states=Sample?DemoTrackers.Create().Single(t=>t.Definition.Id==d.Id).States:Repository.Load(d.Id,Manifest!.Accounts[d.Provider]);
        // Ignore refresh timestamps/provenance-only changes; report actual progress, availability and note changes.
        var content=states.Where(s=>!s.Item.Archived).OrderBy(s=>s.Item.Id,StringComparer.Ordinal).Select(s=>new{
            s.Item.Id,s.Item.Target,s.Item.GroupId,s.Item.Milestone,s.Current,s.ReportedCurrent,s.ManualCurrent,s.Availability,s.Note});
        var updateVersion=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content))));
        var updateProgress=states.Where(s=>!s.Item.Archived).Select(s=>new{s.Item.Id,s.Item.GroupId,s.Current}).ToArray();
        return (object)new {d.Id,d.Provider,d.GameId,d.GameTitle,d.Title,d.Description,d.AchievementId,totals=TrackerTotals.Calculate(d,states),updateVersion,updateProgress,sample=Sample};
    }).ToList();

    public IReadOnlyList<object> Games() => Overview().GroupBy(t=>new{t.Definition.Provider,t.Definition.GameId,t.Definition.GameTitle}).Select(g=>
        (object)new {provider=g.Key.Provider,gameId=g.Key.GameId,title=g.Key.GameTitle,trackerCount=g.Count(),completed=g.Sum(t=>t.Totals.Completed),total=g.Sum(t=>t.Totals.Total),sample=Sample}).ToList();
}
