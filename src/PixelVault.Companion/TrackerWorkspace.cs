using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using PixelVaultNative;

namespace PixelVault.Companion;

/// <summary>One owner, explicitly approved provider accounts. Browser parameters cannot select other accounts.</summary>
public sealed partial class TrackerWorkspace:IDisposable
{
    public TrackerRepository Repository { get; }
    public WorkspaceManifest? Manifest { get; }
    private readonly CompanionOptions options;
    readonly Lazy<LocalApplicationData>? localData;
    readonly Lazy<LocalPhotoLibrary>? localPhotos;
    public LocalApplicationData? LocalData=>localData?.Value;
    public LocalPhotoLibrary? LocalPhotos=>localPhotos?.Value;
    public void Dispose(){DisposeReads();if(localPhotos?.IsValueCreated==true)localPhotos.Value.Dispose();if(localData?.IsValueCreated==true)localData.Value.Dispose();}
    public bool ReadOnly => options.DemoEnabled || Manifest is null;
    public bool Sample => options.DemoEnabled;
    public bool DirectLocal => options.DirectLocal;
    public string Identity=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sample?"sample-v1":JsonSerializer.Serialize(Manifest))));

    public TrackerWorkspace(CompanionOptions options)
    {
        this.options=options;
        options.ValidateRoots();
        var file=Path.Combine(options.DataRoot,"workspace.json");
        if(File.Exists(file))
        {
            Manifest=JsonSerializer.Deserialize<WorkspaceManifest>(File.ReadAllText(file))
                ?? throw new InvalidDataException("Invalid Companion workspace manifest.");
            if(Manifest.Version!=1 || Manifest.Mode!=options.StorageMode || Manifest.Accounts is null || Manifest.Accounts.Count==0
                || Manifest.Accounts.Any(p=>string.IsNullOrWhiteSpace(p.Key)||string.IsNullOrWhiteSpace(p.Value)))
                throw new InvalidDataException("Unsupported Companion workspace manifest.");
            var applicationRoot=DirectLocal?options.ApplicationDataRoot:options.DataRoot;
            if(!File.Exists(Path.Combine(applicationRoot,"achievement-tracking","pixelvault-tracking.sqlite")))
                throw new InvalidDataException("The prepared tracker database is missing. Restore the workspace before starting.");
            Repository=new TrackerRepository(applicationRoot,pooling:false,initializeSchema:!DirectLocal);
            if(DirectLocal)Repository.ValidateSharedSchema();
            foreach(var definition in Repository.Definitions()) AchievementTrackingCatalog.Validate(definition);
        }
        else if(DirectLocal)throw new InvalidDataException("Direct-local mode requires an explicit approved workspace manifest.");
        else Repository=null!;
        if(DirectLocal)
        {
            localData=new(()=>new LocalApplicationData(options,this));
            if(options.ScreenshotLibraryEnabled||!string.IsNullOrEmpty(options.ScreenshotPilot))localPhotos=new(()=>new LocalPhotoLibrary(options,this));
        }
    }

    public IReadOnlyList<AchievementTrackerDefinition> Definitions() => Sample ? DemoTrackers.Create().Select(t=>t.Definition).ToList()
        : Manifest is null ? [] : Repository.Definitions().Where(d=>Manifest.Accounts.ContainsKey(d.Provider)).ToList();

    public string Account(string tracker)
    {
        if(ReadOnly)throw new InvalidDataException("This workspace is read-only. Prepare a separate tracker workspace to enable editing.");
        var provider=Repository.ProviderForTracker(tracker);
        if(provider is null||!Manifest!.Accounts.TryGetValue(provider,out var account))throw new KeyNotFoundException("Tracker not found.");
        return AchievementTrackingCatalog.AccountKey(provider,account);
    }

    public object View(string tracker,bool compact=false)
    {
        if(Sample)
        {
            var sample=DemoTrackers.Create().FirstOrDefault(t=>t.Definition.Id==tracker)??throw new KeyNotFoundException("Tracker not found.");
            return new {sample.Definition,sample.States,sample.Totals,sourceLinked=false,sample=true,readOnly=true};
        }
        if(Manifest is null)throw new KeyNotFoundException("Tracker not found.");
        var account=Account(tracker);var editor=Editor(tracker,account);
        var source=Repository.Source(tracker,account);
        object states=compact?editor.States.Select(s=>new{itemId=s.Item.Id,s.Current,s.ReportedCurrent,s.ManualCurrent,s.IsKnown,s.Complete,s.IsManual,s.HasConflict,s.Availability,s.Note}).ToArray():editor.States;
        return new {editor.Definition,states,editor.Totals,editor.Versions,editor.RulesVersions,editor.ProtectedItems,sourceLinked=source is not null,replaceManualProgress=source?.ReplaceManualProgress==true,sample=false,readOnly=false,
            connection=options.StorageMode,lastObservedUtcTicks=editor.States.Select(s=>s.ObservedUtcTicks).DefaultIfEmpty().Max()};
    }

    internal IReadOnlyList<TrackerListing> Overview()=>Sample?DemoTrackers.Create().Select(t=>new TrackerListing(t.Definition,t.Totals)).ToList()
        :Manifest is null?[]:Repository.ListForAccounts(Manifest.Accounts);

    public IReadOnlyList<object> Trackers() => (Sample?DemoTrackers.Create().Select(t=>new TrackerGameRead(t.Definition,t.States)).ToList():Manifest is null?[]:Repository.ReadForAccounts(Manifest.Accounts)).Select(row=>
    {
        var d=row.Definition;var states=row.States;
        // Ignore refresh timestamps/provenance-only changes; report actual progress, availability and note changes.
        var content=states.Where(s=>!s.Item.Archived).OrderBy(s=>s.Item.Id,StringComparer.Ordinal).Select(s=>new{
            s.Item.Id,s.Item.Target,s.Item.GroupId,s.Item.Milestone,s.Current,s.ReportedCurrent,s.ManualCurrent,s.Availability,s.Note});
        var updateVersion=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content))));
        var updateProgress=states.Where(s=>!s.Item.Archived).Select(s=>new{s.Item.Id,s.Item.GroupId,s.Current}).ToArray();
        return (object)new {d.Id,d.Provider,d.GameId,d.GameTitle,d.Title,d.Description,d.AchievementId,d.IconId,totals=TrackerTotals.Calculate(d,states),updateVersion,updateProgress,sample=Sample};
    }).ToList();

    public IReadOnlyList<object> Games() => Overview().GroupBy(t=>new{t.Definition.Provider,t.Definition.GameId,t.Definition.GameTitle}).Select(g=>
        (object)new {provider=g.Key.Provider,gameId=g.Key.GameId,title=g.Key.GameTitle,trackerCount=g.Count(),completed=g.Sum(t=>t.Totals.AggregateCompleted),total=g.Sum(t=>t.Totals.AggregateTotal),sample=Sample}).ToList();
}
