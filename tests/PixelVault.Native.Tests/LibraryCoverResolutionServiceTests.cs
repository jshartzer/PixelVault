#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryCoverResolutionServiceTests
{
    [Fact]
    public void GetSafeGameFolderName_RemovesRegisteredTrademarkSymbols()
    {
        var service = CreateService();

        Assert.Equal("Diablo IV", service.GetSafeGameFolderName("Diablo\u00AE IV"));
        Assert.Equal("Alan Wake 2", service.GetSafeGameFolderName("Alan Wake\u2122 2"));
        Assert.Equal("Forza Horizon 5", service.GetSafeGameFolderName("Forza&#174; Horizon 5"));
    }

    static LibraryCoverResolutionService CreateService()
    {
        var parser = new FilenameParserService(new FilenameParserServiceDependencies
        {
            LoadCustomConventions = _ => new List<FilenameConventionRule>(),
            LoadSavedGameIndexRows = _ => new List<GameIndexEditorRow>(),
            NormalizeGameIndexName = value => Regex.Replace((value ?? string.Empty).Trim(), "\\s+", " "),
            ParseTagText = value => (value ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => Regex.Replace(tag, "\\s+", " ").Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag)),
            IsVideo = file =>
            {
                var extension = Path.GetExtension(file ?? string.Empty).ToLowerInvariant();
                return extension is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm";
            },
            NormalizeConsoleLabel = value => string.IsNullOrWhiteSpace(value) ? "Other" : value.Trim()
        });

        return new LibraryCoverResolutionService(new LibraryCoverResolutionDependencies
        {
            CoverService = new StubCoverService(),
            FilenameParser = parser,
            FileSystem = new FileSystemService(),
            GetLibraryRoot = () => string.Empty,
            HasSteamGridDbApiToken = () => false,
            NormalizeTitle = TextAndPathHelpers.NormalizeTitle,
            NormalizeConsoleLabel = value => string.IsNullOrWhiteSpace(value) ? "Other" : value.Trim(),
            NormalizeGameId = value => (value ?? string.Empty).Trim(),
            BuildLibraryFolderMasterKey = folder => folder == null ? string.Empty : (folder.Name ?? string.Empty),
            BuildLibraryFolderInventoryStamp = _ => "test",
            LoadLibraryFolderCache = (_, _) => new List<LibraryFolderInfo>(),
            SaveLibraryFolderCache = (_, _, _) => { },
            RefreshCachedLibraryFoldersFromGameIndex = _ => { },
            GetSavedGameIndexRowsForRoot = _ => new List<GameIndexEditorRow>(),
            FindSavedGameIndexRow = (_, _) => null,
            UpsertSavedGameIndexRow = (_, _) => { },
            ResolveLibraryFolderSteamAppId = (_, _) => string.Empty,
            ParseFilename = (file, root) => parser.Parse(file, root),
            Log = _ => { },
            RemoveCachedImageEntries = _ => { }
        });
    }
}
