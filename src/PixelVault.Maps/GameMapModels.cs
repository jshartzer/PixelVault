// Keep the existing namespace and JSON property names for Native store compatibility.
namespace PixelVaultNative;

public sealed class GameMapMarker
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; }
    public string Detail { get; set; }
    // Image-relative coordinates, with (0,0) at the top-left corner.
    public double X { get; set; }
    public double Y { get; set; }
    public double? WorldX { get; set; }
    public double? WorldY { get; set; }
    public string TrackerId { get; set; }
    public string ItemId { get; set; }
}

public sealed class GameMapDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Provider { get; set; }
    public string GameId { get; set; }
    public string GameTitle { get; set; }
    public string Title { get; set; }
    public string Region { get; set; }
    public string ImageFile { get; set; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public string Source { get; set; }
    public string Calibration { get; set; }
    public List<string> AchievementIds { get; set; } = new();
    public List<string> TrackerIds { get; set; } = new();
    public List<GameMapMarker> Markers { get; set; } = new();
    public long Revision { get; set; }
    public override string ToString() => Title;
}
