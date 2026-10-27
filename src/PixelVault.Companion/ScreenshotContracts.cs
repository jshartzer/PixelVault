namespace PixelVault.Companion;

public sealed record ScreenshotPhoto(string Id,string EditionId,string Console,long CaptureUtcTicks,string? CaptureDay,
    string Group,int Width,int Height,string Thumbnail,string Preview)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ScreenshotOriginal? Original { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ScreenshotOriginal? Metadata { get; init; }
}
public sealed record ScreenshotOriginal(string Asset,string FileName,long Bytes,string ContentType,long LastWriteUtcTicks);
public sealed record ScreenshotCatalogue(int Version,string WorkspaceId,string Provider,string GameId,string Title,
    string GameScope,string DayTimeZone,long ExportedUtcTicks,int Eligible,int SkippedVideos,IReadOnlyList<ScreenshotPhoto> Photos);
public sealed record ScreenshotUploadPlan(string Id,IReadOnlyList<string> MissingAssets,bool Current)
{
    public IReadOnlyList<string> MissingOriginals { get; init; } = [];
    public IReadOnlyList<ScreenshotPhoto> RetainedPhotos { get; init; } = [];
    public IReadOnlyList<string> SuppressedPhotoIds { get; init; } = [];
}
public sealed record ScreenshotRemovalIdentity(string Id,string EditionId,bool Removed);
public sealed record ScreenshotRemovalGame(string Provider,string GameId,string Version,IReadOnlyList<ScreenshotRemovalIdentity> Photos);
public sealed record ScreenshotRemovalRequest(string WorkspaceId,string Version,IReadOnlyList<string> RemoveIds,IReadOnlyList<string> RestoreIds);
public sealed record ScreenshotPage(string Version,string Title,int Total,int Eligible,int SkippedVideos,string DayTimeZone,
    int? NextOffset,IReadOnlyList<ScreenshotPhoto> Photos)
{
    public IReadOnlyList<ScreenshotConsole> Consoles { get; init; } = [];
    public int FilteredTotal { get; init; }
}
public sealed record ScreenshotConsole(string Name,int Count);
public sealed record ScreenshotLink(string Provider,string GameId,int Count);
public sealed record RecentScreenshot(string Provider,string GameId,string Title,ScreenshotPhoto Photo);
public sealed record RecentScreenshots(string Version,int Total,IReadOnlyList<RecentScreenshot> Photos);

public sealed record ScreenshotTimeline(string Version,int Total,int FilteredTotal,int? NextOffset,IReadOnlyList<RecentScreenshot> Photos,IReadOnlyList<ScreenshotConsole> Consoles);
