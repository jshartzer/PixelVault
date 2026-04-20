namespace PixelVaultNative
{
    /// <summary>
    /// PV-PLN-EXT-002 A.1: application service graph for <see cref="MainWindow"/> — one place to read construction order
    /// (explicit constructors only; no DI container).
    /// </summary>
    public sealed partial class MainWindow
    {
        /// <summary>
        /// Immutable result of <see cref="BuildApplicationServiceGraph"/>; assigns to <see cref="MainWindow"/> readonly service fields.
        /// </summary>
        readonly record struct MainWindowServiceGraph(
            ILogService LogService,
            ISettingsService SettingsService,
            IFileSystemService FileSystemService,
            ICoverService CoverService,
            ILibraryCoverResolution LibraryCoverResolutionService,
            IIndexPersistenceService IndexPersistenceService,
            IFilenameParserService FilenameParserService,
            IFilenameRulesService FilenameRulesService,
            IGameIndexEditorAssignmentService GameIndexEditorAssignmentService,
            IMetadataService MetadataService,
            ILibraryScanner LibraryScanner,
            IImportService ImportService,
            IntakePipeline IntakePipeline,
            LibraryWorkspaceContext LibraryWorkspace,
            ILibrarySession LibrarySession,
            IGameIndexService GameIndexService);

        /// <summary>
        /// Builds the application service graph in dependency order (behavior matches the previous inline <see cref="MainWindow"/> constructor).
        /// <list type="number">
        /// <item><description><see cref="TroubleshootingLogService"/> from <paramref name="troubleshootingLog"/></description></item>
        /// <item><description><see cref="CreateSettingsAndFileServices"/></description></item>
        /// <item><description><see cref="CreateCoverService"/></description></item>
        /// <item><description><see cref="CreateIndexFilenameRulesServices"/></description></item>
        /// <item><description><see cref="CreateLibraryCoverResolutionService"/></description></item>
        /// <item><description><see cref="CreateMetadataService"/></description></item>
        /// <item><description><see cref="CreateLibraryScanner"/></description></item>
        /// <item><description><see cref="ImportService"/> + <see cref="BuildImportServiceDependencies"/></description></item>
        /// <item><description><see cref="IntakeAnalysisService"/> then <see cref="IntakePipeline"/></description></item>
        /// <item><description><see cref="LibraryWorkspaceContext"/></description></item>
        /// <item><description><see cref="CreateLibrarySessionForStartup"/> then <see cref="IImportService.AttachLibrarySessionAccessor"/></description></item>
        /// <item><description><see cref="CreateGameIndexServiceForStartup"/></description></item>
        /// </list>
        /// </summary>
        static MainWindowServiceGraph BuildApplicationServiceGraph(MainWindow host, string cacheRoot, string coversRoot, TroubleshootingLog troubleshootingLog)
        {
            var logService = new TroubleshootingLogService(troubleshootingLog);
            var (settingsService, fileSystemService) = CreateSettingsAndFileServices();
            var coverService = CreateCoverService(host, fileSystemService, coversRoot);
            var (indexPersistenceService, filenameParserService, gameIndexEditorAssignmentService, filenameRulesService) =
                CreateIndexFilenameRulesServices(cacheRoot, host);
            var libraryCoverResolutionService = CreateLibraryCoverResolutionService(
                host,
                coverService,
                filenameParserService,
                fileSystemService);
            var metadataService = CreateMetadataService(host, cacheRoot);
            var libraryScanner = CreateLibraryScanner(host, metadataService, fileSystemService);
            var importService = new ImportService(BuildImportServiceDependencies(
                host,
                libraryScanner,
                fileSystemService,
                metadataService,
                coverService,
                gameIndexEditorAssignmentService,
                logService));
            var intakeAnalysisService = new IntakeAnalysisService(host.ParseFilename, IsVideo, host.GetLibraryDate);
            var intakePipeline = new IntakePipeline(importService, fileSystemService, intakeAnalysisService);
            var libraryWorkspace = new LibraryWorkspaceContext(host);
            var librarySession = CreateLibrarySessionForStartup(
                host,
                libraryWorkspace,
                libraryScanner,
                fileSystemService,
                gameIndexEditorAssignmentService,
                indexPersistenceService);
            importService.AttachLibrarySessionAccessor(() => librarySession);
            var gameIndexService = CreateGameIndexServiceForStartup(
                host,
                libraryScanner,
                librarySession,
                indexPersistenceService,
                gameIndexEditorAssignmentService);

            return new MainWindowServiceGraph(
                logService,
                settingsService,
                fileSystemService,
                coverService,
                libraryCoverResolutionService,
                indexPersistenceService,
                filenameParserService,
                filenameRulesService,
                gameIndexEditorAssignmentService,
                metadataService,
                libraryScanner,
                importService,
                intakePipeline,
                libraryWorkspace,
                librarySession,
                gameIndexService);
        }
    }
}
