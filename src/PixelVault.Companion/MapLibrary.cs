using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using PixelVaultNative;

namespace PixelVault.Companion;

public sealed record MapSummary(string Id, string Provider, string GameId, string Title, string? Region,
    int PixelWidth, int PixelHeight, long Revision, int PinCount,
    IReadOnlyList<string> AchievementIds, IReadOnlyList<string> TrackerIds);
public sealed record MapPin(string Id, string Label, string? Detail, double X, double Y, string? TrackerId, string? ItemId);
public sealed record MapDetail(MapSummary Map, IReadOnlyList<MapPin> Pins);
public sealed record MapListing(bool Enabled, IReadOnlyList<MapSummary> Maps);
public sealed record MapPinProgress(string Id, string Status, int? Current, int? Target, string? TrackerUrl, string? AchievementUrl);
public sealed record MapProgress(string WorkspaceId, long MapRevision, string Version, IReadOnlyList<MapPinProgress> Pins);

/// <summary>Owner-facing projection of canonical game maps; no images, source readers or progress writes.</summary>
public sealed class MapLibrary(CompanionOptions options, GameLibrary games, TrackerWorkspace workspace)
{
    int observedStore;

    public MapListing List(string provider, string game, string? achievement = null, string? tracker = null)
    {
        _ = games.MediaTitle(provider, game);
        if (!options.DirectLocal) return new(false, []);
        return Read(reader => new MapListing(true, reader?.List(provider, game, achievement, tracker).Select(Summary).ToArray() ?? []));
    }

    public MapDetail Detail(string provider, string game, string id)
    {
        var map = Definition(provider, game, id);
        return new MapDetail(Summary(map), map.Markers.Select(pin =>
            new MapPin(pin.Id, pin.Label, pin.Detail, pin.X, pin.Y, pin.TrackerId, pin.ItemId)).ToArray());
    }

    public MapProgress Progress(string provider, string game, string id)
    {
        try
        {
            var map = Definition(provider, game, id);
            var approved = workspace.Manifest!.Accounts;
            // Validate the configured account on every read, even while library metadata is cached.
            // Additional connected accounts never expand the approved tracker scope.
            _ = Local.LocalAchievementReader.ConnectedAccounts(options.ApplicationDataRoot, approved);
            var requested = map.Markers.Select(m => m.TrackerId).Where(t => !string.IsNullOrWhiteSpace(t))
                .Concat(map.TrackerIds).Distinct(StringComparer.Ordinal).ToArray();
            var trackers = new Dictionary<string, TrackerGameRead>(StringComparer.Ordinal);
            if (approved.TryGetValue(provider, out var account) && requested.Length > 0)
            {
                LocalApplicationData.RequireFile(workspace.Repository.DatabasePath, options.ApplicationDataRoot);
                trackers = workspace.Repository.ReadGameTrackers(provider, game, account, requested).ToDictionary(t => t.Definition.Id, StringComparer.Ordinal);
            }
            var items = trackers.ToDictionary(t => t.Key,
                t => t.Value.States.Where(s => !s.Item.Archived).ToDictionary(s => s.Item.Id, StringComparer.Ordinal), StringComparer.Ordinal);
            var pins = map.Markers.Select(pin =>
            {
                var linked = !string.IsNullOrEmpty(pin.TrackerId) && !string.IsNullOrEmpty(pin.ItemId);
                if (string.IsNullOrEmpty(pin.TrackerId) || !trackers.TryGetValue(pin.TrackerId, out var tracker))
                    return new MapPinProgress(pin.Id, linked ? "unavailable" : "unlinked", null, null, null, null);
                var trackerUrl = "#tracker/" + Uri.EscapeDataString(tracker.Definition.Id);
                var achievementUrl = string.IsNullOrEmpty(tracker.Definition.AchievementId) ? null
                    : $"#achievement/{Uri.EscapeDataString(provider)}/{Uri.EscapeDataString(game)}/{Uri.EscapeDataString(tracker.Definition.AchievementId)}";
                if (!linked) return new MapPinProgress(pin.Id, "unlinked", null, null, trackerUrl, achievementUrl);
                if (!items[pin.TrackerId].TryGetValue(pin.ItemId, out var state))
                    return new MapPinProgress(pin.Id, "unavailable", null, null, null, null);
                var status = !state.IsKnown ? "unknown" : state.Item.Target > 0 && state.Complete ? "completed" : "remaining";
                return new MapPinProgress(pin.Id, status, state.Current, state.Item.Target, trackerUrl, achievementUrl);
            }).ToArray();
            var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { workspace = workspace.Identity, map.Revision, pins }))));
            return new(workspace.Identity, map.Revision, version, pins);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException or JsonException)
        {
            throw new MapLibraryUnavailableException(e);
        }
    }

    internal GameMapDefinition Definition(string provider, string game, string id)
    {
        _ = games.MediaTitle(provider, game);
        if (!options.DirectLocal) throw new KeyNotFoundException();
        return Read(reader =>
        {
            return reader?.Find(provider, game, id) ?? throw new KeyNotFoundException();
        });
    }

    static MapSummary Summary(GameMapDefinition map) => new(map.Id, map.Provider, map.GameId, map.Title, map.Region,
        map.PixelWidth, map.PixelHeight, map.Revision, map.Markers.Count, map.AchievementIds.ToArray(), map.TrackerIds.ToArray());

    T Read<T>(Func<GameMapReader?, T> query)
    {
        try
        {
            var root = Path.Combine(options.ApplicationDataRoot, "game-maps");
            try { _ = File.GetAttributes(root); }
            catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException)
            {
                // No maps have ever been imported here. Do not create a replacement store.
                if (Volatile.Read(ref observedStore) == 0) return query(null);
                throw;
            }
            Interlocked.Exchange(ref observedStore, 1);
            // An existing directory with a missing/unreadable database is unavailable, not an empty library.
            return query(new GameMapReader(options.ApplicationDataRoot, allowMissing: false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException or JsonException or InvalidDataException)
        {
            throw new MapLibraryUnavailableException(e);
        }
    }
}

public sealed class MapLibraryUnavailableException(Exception inner)
    : Exception("The map library is temporarily unavailable. Try again after restoring access to the saved maps.", inner);

public static class MapEndpoints
{
    public static void MapSavedMaps(this RouteGroupBuilder api)
    {
        var maps = api.MapGroup("/maps").RequireAuthorization();
        maps.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (KeyNotFoundException) { return Results.NotFound(new { error = "The game or map was not found." }); }
            catch (MapLibraryUnavailableException e)
            {
                context.HttpContext.RequestServices.GetRequiredService<ILogger<MapLibrary>>().LogWarning(e, "Saved map read failed.");
                return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        maps.MapGet("/{provider}/{game}", (string provider, string game, string? achievement, string? tracker, MapLibrary library)
            => Results.Ok(library.List(provider, game, achievement, tracker)));
        maps.MapGet("/{provider}/{game}/{id}", (string provider, string game, string id, MapLibrary library)
            => Results.Ok(library.Detail(provider, game, id)));
        maps.MapGet("/{provider}/{game}/{id}/progress", (string provider, string game, string id, MapLibrary library)
            => Results.Ok(library.Progress(provider, game, id)));
        maps.MapGet("/{provider}/{game}/{id}/manifest",async(string provider,string game,string id,MapTileCache cache,CancellationToken token)
            =>Results.Ok(await cache.Manifest(provider,game,id,token)));
        maps.MapGet("/{provider}/{game}/{id}/tiles/{z:int}/{x:int}/{y:int}",async(string provider,string game,string id,int z,int x,int y,string v,MapTileCache cache,HttpContext context)=>
        {
            var image=await cache.Tile(provider,game,id,z,x,y,v,context.RequestAborted);
            context.Response.Headers.CacheControl="private, max-age=3600";context.Response.Headers.Remove("Pragma");
            return Results.File(image.Stream,"image/jpeg",entityTag:new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\""+image.Digest+"\""));
        });
    }
}
