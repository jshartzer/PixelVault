using System.Net;
using System.Net.Http;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class RobloxCaptureTests
{
    const string Capture = "1869456976_79268393072444_1788648631989.png";
    static FilenameParserService Parser() => new(new FilenameParserServiceDependencies());

    [Fact]
    public void Parser_SeparatesRobloxFromSteam_AndDoesNotExtractDate()
    {
        var parser = Parser();
        var raw = parser.Parse(Capture, "");
        Assert.Equal(RobloxCaptureNaming.RawConvention, raw.ConventionId);
        Assert.Equal("79268393072444", raw.RobloxPlaceId);
        Assert.Equal("PC", raw.PlatformLabel);
        Assert.Empty(raw.SteamAppId);
        Assert.Empty(raw.NonSteamId);
        Assert.Empty(raw.GameTitleHint);
        Assert.Null(raw.CaptureTime);
        Assert.True(raw.PreserveFileTimes);
        Assert.True(raw.RoutesToManualWhenMissingSteamAppId);
        Assert.False(RobloxCaptureNaming.IsCapture(parser.Parse("RobloxScreenShot20260525_200505425.png", "")));
        Assert.Equal("steam_screenshot_appid", parser.Parse("2561580_20260326221306_1.png", "").ConventionId);
    }

    [Fact]
    public void RenamedCapture_RoundTripsFullTitleAndCollisionSuffixWithoutDateParsing()
    {
        Assert.True(RobloxCaptureNaming.TryBuildRenameBase(Capture, "My_Game: Lemons 🍋", out var name));
        Assert.Equal("Roblox_My_Game- Lemons 🍋_1788648631989", name);
        foreach (var suffix in new[] { "", " (1)", "_2" })
        {
            var parsed = Parser().Parse(name + suffix + ".png", "");
            Assert.Equal(RobloxCaptureNaming.NamedConvention, parsed.ConventionId);
            Assert.Equal("Roblox", parsed.GameTitleHint);
            Assert.Equal("Roblox", Parser().GetGameTitleHint(name + suffix, ""));
            Assert.Empty(parsed.RobloxPlaceId);
            Assert.Null(parsed.CaptureTime);
            Assert.True(parsed.PreserveFileTimes);
            Assert.False(parsed.RoutesToManualWhenMissingSteamAppId);
        }
    }

    [Fact]
    public void Parser_CachedRawLookupUsesRobloxAsGameIdentity()
    {
        var parser = new FilenameParserService(new FilenameParserServiceDependencies
        {
            GetCachedRobloxTitle = _ => "Sell Lemons 🍋"
        });
        var parsed = parser.Parse(Capture, "");
        Assert.Equal("Roblox", parsed.GameTitleHint);
        Assert.False(parsed.RoutesToManualWhenMissingSteamAppId);
        Assert.Null(parsed.CaptureTime);
    }

    [Theory]
    [InlineData("Sell Lemons 🍋_Roblox_1788648631989.png")]
    [InlineData("Sell Lemons 🍋_Roblox_1788648631989 (2).png")]
    [InlineData("Roblox_Sell Lemons 🍋_1788648631989.png")]
    [InlineData("Roblox_My_Game_Roblox_1788648631989_2.png")]
    public void Parser_GroupsBothNamingFormatsUnderRoblox(string file)
    {
        var parser = Parser();
        var parsed = parser.Parse(file, "");
        Assert.Equal(RobloxCaptureNaming.NamedConvention, parsed.ConventionId);
        Assert.Equal("Roblox", parsed.GameTitleHint);
        Assert.Equal("Roblox", parser.GetGameTitleHint(Path.GetFileNameWithoutExtension(file), ""));
        Assert.Empty(parsed.RobloxPlaceId);
        Assert.False(parsed.RoutesToManualWhenMissingSteamAppId);
        Assert.Null(parsed.CaptureTime);
    }

    [Fact]
    public async Task Api_ResolvesPlaceViaUniverseAndCachesAcrossCaptures()
    {
        var handler = new ApiHandler();
        using var client = new HttpClient(handler);
        var service = new RobloxExperienceService(client);
        Assert.Equal("Sell Lemons 🍋", await service.ResolveTitleAsync("79268393072444", default));
        Assert.Equal("Sell Lemons 🍋", await service.ResolveTitleAsync("79268393072444", default));
        Assert.Equal(2, handler.Urls.Count);
        Assert.Equal("https://apis.roblox.com/universes/v1/places/79268393072444/universe", handler.Urls[0]);
        Assert.Equal("https://games.roblox.com/v1/games?universeIds=7395930870", handler.Urls[1]);
        Assert.DoesNotContain(handler.Urls, url => url.Contains("1869456976"));
        Assert.Equal("Sell Lemons 🍋", service.GetCachedTitle("79268393072444"));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "{}")]
    [InlineData(HttpStatusCode.NotFound, "{}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "{\"data\":[]}")]
    [InlineData(HttpStatusCode.OK, "{\"data\":[{\"id\":123,\"name\":\"Wrong game\"}]}")]
    public async Task Api_FailuresAndMismatchedResultsStayUnresolved(HttpStatusCode status, string body)
    {
        var handler = new ApiHandler { GameStatus = status, GameBody = body };
        using var client = new HttpClient(handler);
        var service = new RobloxExperienceService(client);
        Assert.Empty(await service.ResolveTitleAsync("79268393072444", default));
        Assert.Empty(await service.ResolveTitleAsync("79268393072444", default));
        Assert.Equal(2, handler.Urls.Count); // Brief negative cache prevents one request per capture.
    }

    [Fact]
    public async Task Api_RejectsInvalidIdsAndHonorsCancellation()
    {
        var handler = new ApiHandler();
        using var client = new HttpClient(handler);
        var service = new RobloxExperienceService(client);
        Assert.Empty(await service.ResolveTitleAsync("../oops", default));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ResolveTitleAsync("79268393072444", cts.Token));
        Assert.Empty(handler.Urls);
    }

    [Fact]
    public void Analysis_UsesCreationDateAndRequiresSuccessfulLookup()
    {
        using var scope = new Scope();
        var created = new DateTime(2025, 1, 2, 3, 4, 5);
        File.SetCreationTime(scope.FilePath, created);
        File.SetLastWriteTime(scope.FilePath, created.AddDays(-2));
        var analyzer = new IntakeAnalysisService(f => Parser().Parse(f, ""), _ => false, _ => DateTime.MinValue,
            getImportFolderTitleHint: _ => "RobloxPCGDK", resolveRobloxTitle: (_, _) => "Sell Lemons 🍋");
        var analysis = analyzer.AnalyzeFiles(new[] { scope.FilePath })[scope.FilePath];
        Assert.True(analysis.CanUpdateMetadata);
        Assert.True(analysis.PreserveFileTimes);
        Assert.Equal(created, analysis.CaptureTime);
        Assert.Null(analysis.Parsed.CaptureTime);
        Assert.Equal("Roblox", analysis.Parsed.GameTitleHint);
        var unresolved = new IntakeAnalysisService(f => Parser().Parse(f, ""), _ => false, _ => created,
            getImportFolderTitleHint: _ => "RobloxPCGDK", resolveRobloxTitle: (_, _) => "");
        var prep = IntakePreparationBuilder.Build(new[] { scope.FilePath }, unresolved.AnalyzeFiles, false);
        Assert.Empty(prep.ReviewItems);
        Assert.Single(prep.ManualItems);
        Assert.Empty(prep.Analysis[scope.FilePath].Parsed.GameTitleHint);
    }

    [Fact]
    public async Task ImportRename_PreservesTimesSidecarAndCollisionTarget_AndDoesNotRepeat()
    {
        using var scope = new Scope();
        var created = File.GetCreationTime(scope.FilePath);
        var modified = File.GetLastWriteTime(scope.FilePath);
        var expected = Path.Combine(scope.Root, "Roblox_Sell Lemons 🍋_1788648631989.png");
        File.WriteAllText(expected, "existing capture");
        File.WriteAllText(scope.FilePath + ".json", "sidecar");
        var service = scope.Import((_, _) => Task.FromResult("Sell Lemons 🍋"));
        var result = await service.RunSteamRenameAsync(new[] { scope.FilePath });
        Assert.Equal(1, result.Renamed);
        var target = result.OldPathToNewPath[scope.FilePath];
        Assert.EndsWith("Roblox_Sell Lemons 🍋_1788648631989 (1).png", target);
        Assert.Equal("existing capture", File.ReadAllText(expected));
        Assert.Equal("sidecar", File.ReadAllText(target + ".json"));
        Assert.Equal(created, File.GetCreationTime(target));
        Assert.Equal(modified, File.GetLastWriteTime(target));
        Assert.Equal(created, TextAndPathHelpers.GetLibraryDate(target, Parser().Parse(target, "")));
        Assert.Equal(0, (await service.RunSteamRenameAsync(new[] { target })).Renamed);
    }

    [Fact]
    public async Task ImportRename_LookupFailureLeavesOriginalFileAndDoesNotMoveIntoLibrary()
    {
        using var scope = new Scope();
        var service = scope.Import((_, _) => Task.FromResult(""));
        var result = await service.RunSteamRenameAsync(new[] { scope.FilePath });
        Assert.Equal(0, result.Renamed);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(result.OldPathToNewPath);
        var move = service.MoveFilesToLibraryDestination(new[] { scope.FilePath }, "test");
        Assert.Equal(0, move.Moved);
        Assert.Equal(1, move.Skipped);
        Assert.True(File.Exists(scope.FilePath));
    }

    sealed class ApiHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();
        public HttpStatusCode GameStatus = HttpStatusCode.OK;
        public string GameBody = "{\"data\":[{\"id\":7395930870,\"name\":\"Sell Lemons 🍋\"}]}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            var place = request.RequestUri.Host == "apis.roblox.com";
            return Task.FromResult(new HttpResponseMessage(place ? HttpStatusCode.OK : GameStatus)
            { Content = new StringContent(place ? "{\"universeId\":7395930870}" : GameBody) });
        }
    }

    sealed class Scope : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "pv-roblox-" + Guid.NewGuid().ToString("N"));
        public string FilePath => Path.Combine(Root, Capture);
        public Scope() { Directory.CreateDirectory(Root); File.WriteAllText(FilePath, "capture"); }
        public ImportService Import(Func<string, CancellationToken, Task<string>> resolve) => new(new ImportServiceDependencies
        {
            FileSystem = new FileSystemService(), LogService = NullLogService.Instance,
            MetadataService = new StubMetadataService(), CoverService = new StubCoverService(),
            GetFileCreationTime = File.GetCreationTime, GetFileLastWriteTime = File.GetLastWriteTime,
            NormalizeGameIndexName = value => value, GetLibraryRoot = () => Path.Combine(Root, "library"),
            GetDestinationRoot = () => Path.Combine(Root, "destination"),
            ParseFilenameForImport = file => Parser().Parse(file, ""), ResolveRobloxExperienceTitle = resolve,
            MoveMetadataSidecarIfPresent = (before, after) => { if (File.Exists(before + ".json")) File.Move(before + ".json", after + ".json"); }
        });
        public void Dispose() => Directory.Delete(Root, true);
    }
}
