using System;
using System.Collections.Generic;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    public class SavedGameIndexRowLookupTests
    {
        static SavedGameIndexRowLookup BuildLookup(IEnumerable<GameIndexEditorRow> rows)
        {
            return new SavedGameIndexRowLookup(
                rows,
                value => (value ?? string.Empty).Trim().ToLowerInvariant(),
                value => string.IsNullOrWhiteSpace(value) ? "Other" : value.Trim(),
                (name, platform) => ((name ?? string.Empty).Trim().ToLowerInvariant()) + "|" + (string.IsNullOrWhiteSpace(platform) ? "Other" : platform.Trim()));
        }

        [Fact]
        public void Find_PrefersGameIdBeforeIdentityAndFolderPath()
        {
            var byId = new GameIndexEditorRow { GameId = "wanted", Name = "Different", PlatformLabel = "Steam", FolderPath = "C:/lib/different" };
            var byIdentity = new GameIndexEditorRow { GameId = "identity", Name = "Hades", PlatformLabel = "Steam", FolderPath = "C:/lib/identity" };
            var byFolder = new GameIndexEditorRow { GameId = "folder", Name = "Other", PlatformLabel = "Steam", FolderPath = "C:/lib/hades" };
            var lookup = BuildLookup(new[] { byIdentity, byFolder, byId });
            var folder = new LibraryFolderInfo { GameId = "wanted", Name = "Hades", PlatformLabel = "Steam", FolderPath = "C:/lib/hades" };

            var result = lookup.Find(folder);

            Assert.Same(byId, result);
        }

        [Fact]
        public void Find_UsesIdentityBeforeFolderPathWhenGameIdMissing()
        {
            var byIdentity = new GameIndexEditorRow { GameId = "identity", Name = "Hades", PlatformLabel = "Steam", FolderPath = "C:/lib/identity" };
            var byFolder = new GameIndexEditorRow { GameId = "folder", Name = "Other", PlatformLabel = "Steam", FolderPath = "C:/lib/hades" };
            var lookup = BuildLookup(new[] { byFolder, byIdentity });
            var folder = new LibraryFolderInfo { Name = "Hades", PlatformLabel = "Steam", FolderPath = "C:/lib/hades" };

            var result = lookup.Find(folder);

            Assert.Same(byIdentity, result);
        }

        [Fact]
        public void Find_UsesFolderPathAndPlatformWhenIdAndIdentityMiss()
        {
            var byFolder = new GameIndexEditorRow { GameId = "folder", Name = "Other", PlatformLabel = "Steam", FolderPath = "C:/lib/hades" };
            var wrongPlatform = new GameIndexEditorRow { GameId = "wrong", Name = "Other", PlatformLabel = "PS5", FolderPath = "C:/lib/hades" };
            var lookup = BuildLookup(new[] { wrongPlatform, byFolder });
            var folder = new LibraryFolderInfo { Name = "Unknown", PlatformLabel = "Steam", FolderPath = "C:/lib/hades" };

            var result = lookup.Find(folder);

            Assert.Same(byFolder, result);
        }

        [Fact]
        public void Find_FolderPathDuplicatePrefersCandidateWithGameId()
        {
            var withoutId = new GameIndexEditorRow { GameId = "", Name = "No Id", PlatformLabel = "Steam", FolderPath = "C:/lib/hades" };
            var withId = new GameIndexEditorRow { GameId = "with-id", Name = "With Id", PlatformLabel = "Steam", FolderPath = "C:/lib/hades" };
            var lookup = BuildLookup(new[] { withoutId, withId });
            var folder = new LibraryFolderInfo { Name = "Unknown", PlatformLabel = "Steam", FolderPath = "C:/lib/hades" };

            var result = lookup.Find(folder);

            Assert.Same(withId, result);
        }
    }
}
