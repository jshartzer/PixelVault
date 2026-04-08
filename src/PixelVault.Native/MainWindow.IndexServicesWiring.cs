using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        static (IIndexPersistenceService IndexPersistence, IFilenameParserService FilenameParser, IGameIndexEditorAssignmentService GameIndexEditorAssignment, IFilenameRulesService FilenameRules)
            CreateIndexFilenameRulesServices(string cacheRoot, MainWindow mw)
        {
            var indexPersistenceService = new IndexPersistenceService(new IndexPersistenceServiceDependencies
            {
                CacheRoot = cacheRoot,
                SafeCacheName = delegate(string value) { return mw.SafeCacheName(value); },
                NormalizeGameId = delegate(string value) { return mw.NormalizeGameId(value); },
                NormalizeGameIndexName = delegate(string value) { return mw.NormalizeGameIndexName(value); },
                NormalizeGameIndexNameWithFolder = delegate(string name, string folderPath) { return mw.NormalizeGameIndexName(name, folderPath); },
                FoldNormalizedGameTitleForIdentity = delegate(string normalized) { return GameIndexIdentityMatch.FoldNormalizedTitle(normalized, MainWindow.Sanitize); },
                CleanTag = delegate(string value) { return MainWindow.CleanTag(value ?? string.Empty); },
                NormalizeConsoleLabel = delegate(string value) { return MainWindow.NormalizeConsoleLabel(value); },
                DisplayExternalIdValue = delegate(string value) { return mw.DisplayExternalIdValue(value); },
                IsClearedExternalIdValue = delegate(string value) { return mw.IsClearedExternalIdValue(value); },
                SerializeExternalIdValue = delegate(string value, bool suppressAutoResolve) { return mw.SerializeExternalIdValue(value, suppressAutoResolve); },
                MergeGameIndexRows = delegate(IEnumerable<GameIndexEditorRow> rows) { return mw.MergeGameIndexRows(rows); },
                BuildGameIdAliasMap = delegate(IEnumerable<GameIndexEditorRow> sourceRows, IEnumerable<GameIndexEditorRow> normalizedRows) { return mw.BuildGameIdAliasMap(sourceRows, normalizedRows); },
                HasGameIdAliasChanges = delegate(Dictionary<string, string> aliasMap) { return mw.HasGameIdAliasChanges(aliasMap); },
                ParseInt = delegate(string value) { return MainWindow.ParseInt(value); },
                ParseTagText = delegate(string value) { return MainWindow.ParseTagText(value); },
                DetermineConsoleLabelFromTags = delegate(IEnumerable<string> tags) { return MainWindow.DetermineConsoleLabelFromTags(tags); },
                RewriteGameIdAliasesInLibraryFolderCacheFile = delegate(string root, Dictionary<string, string> aliasMap) { mw.RewriteGameIdAliasesInLibraryFolderCacheFile(root, aliasMap); },
                ApplyGameIdAliasesToCachedMetadataIndex = delegate(string root, Dictionary<string, string> aliasMap) { mw.ApplyGameIdAliasesToCachedMetadataIndex(root, aliasMap); }
            });
            var filenameParserService = new FilenameParserService(new FilenameParserServiceDependencies
            {
                LoadCustomConventions = delegate(string root) { return indexPersistenceService.LoadFilenameConventions(root); },
                LoadSavedGameIndexRows = delegate(string root) { return mw.GetSavedGameIndexRowsForRoot(root); },
                NormalizeGameIndexName = delegate(string value) { return mw.NormalizeGameIndexName(value); },
                ParseTagText = delegate(string value) { return MainWindow.ParseTagText(value); },
                IsVideo = delegate(string file) { return MainWindow.IsVideo(file); },
                NormalizeConsoleLabel = delegate(string value) { return MainWindow.NormalizeConsoleLabel(value); }
            });
            var gameIndexEditorAssignmentService = new GameIndexEditorAssignmentService(
                indexPersistenceService,
                filenameParserService,
                (name, folderPath) => mw.NormalizeGameIndexName(name, folderPath),
                value => MainWindow.NormalizeConsoleLabel(value ?? string.Empty),
                value => mw.NormalizeGameId(value ?? string.Empty),
                value => MainWindow.CleanTag(value ?? string.Empty),
                ids => mw.CreateGameId(ids),
                normalized => GameIndexIdentityMatch.FoldNormalizedTitle(normalized, MainWindow.Sanitize));
            var filenameRulesService = new FilenameRulesService(new FilenameRulesServiceDependencies
            {
                GetConventionRules = delegate(string root) { return filenameParserService.GetConventionRules(root); },
                LoadSamples = delegate(string root, int maxCount) { return indexPersistenceService.LoadFilenameConventionSamples(root, maxCount); },
                SaveConventions = delegate(string root, IEnumerable<FilenameConventionRule> rules) { indexPersistenceService.SaveFilenameConventions(root, rules); },
                InvalidateRules = delegate(string root) { filenameParserService.InvalidateRules(root); },
                DeleteSamples = delegate(string root, IEnumerable<long> sampleIds) { indexPersistenceService.DeleteFilenameConventionSamples(root, sampleIds); },
                BuildCustomRuleFromSample = delegate(FilenameConventionSample sample) { return mw.BuildCustomFilenameConventionFromSample(sample); },
                ParseTagText = delegate(string value) { return MainWindow.ParseTagText(value); },
                NormalizeConsoleLabel = delegate(string value) { return MainWindow.NormalizeConsoleLabel(value); },
                DefaultPlatformTagsTextForLabel = delegate(string value) { return mw.DefaultPlatformTagsTextForLabel(value); },
                CleanTag = delegate(string value) { return MainWindow.CleanTag(value); }
            });
            return (indexPersistenceService, filenameParserService, gameIndexEditorAssignmentService, filenameRulesService);
        }
    }
}
