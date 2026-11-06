namespace PixelVaultNative;

public static class GameMapValidation
{
    public static void Validate(GameMapDefinition map)
    {
        if (map == null || string.IsNullOrWhiteSpace(map.Id) || string.IsNullOrWhiteSpace(map.Provider) || string.IsNullOrWhiteSpace(map.GameId) ||
            string.IsNullOrWhiteSpace(map.Title) || map.Title.Length > 300) throw new InvalidDataException("A game and map title are required.");
        if (map.AchievementIds == null || map.TrackerIds == null || map.Markers == null || map.Markers.Count > 10000) throw new InvalidDataException("Invalid map links or markers.");
        if (map.Markers.Select(m => m?.Id).Distinct().Count() != map.Markers.Count) throw new InvalidDataException("Map marker IDs must be unique.");
        foreach (var marker in map.Markers)
            if (marker == null || string.IsNullOrWhiteSpace(marker.Id) || string.IsNullOrWhiteSpace(marker.Label) || marker.Label.Length > 300 ||
                !double.IsFinite(marker.X) || !double.IsFinite(marker.Y) || marker.X < 0 || marker.X > 1 || marker.Y < 0 || marker.Y > 1 ||
                (marker.WorldX.HasValue && !double.IsFinite(marker.WorldX.Value)) || (marker.WorldY.HasValue && !double.IsFinite(marker.WorldY.Value)) ||
                (!string.IsNullOrEmpty(marker.ItemId) && string.IsNullOrEmpty(marker.TrackerId)))
                throw new InvalidDataException("Each marker needs a name and a position inside the map.");
    }
}
