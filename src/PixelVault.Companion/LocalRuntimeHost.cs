using PixelVaultNative;
using System.Text.Json;
namespace PixelVault.Companion;

public sealed class LocalRuntimeHost(CompanionOptions options,LocalRefreshService refresh,TrackerWorkspace workspace,ConnectorSyncState state,ILocalReaderClient reader):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.DirectLocal||!options.LocalWorkersEnabled)return;
        // The lease is held by the OS, spans Windows sessions, and is released after process death.
        try
        {
            await using var owner=new FileStream(Path.Combine(options.DataRoot,"local-runtime.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            await using var canonicalOwner=TrackerSourceLease.OwnBackground(workspace.Repository.DatabasePath);
            await reader.Run(new(){Operation="access",ApplicationRoot=options.ApplicationDataRoot,CacheRoot=options.CacheRoot,Accounts=AchievementAccountScope.Read(options,workspace),RequireProviderCredentials=options.LocalProviderRefreshEnabled},stoppingToken);
            state.Put("local-worker-ready");
            try { state.Put("local-map-access",JsonSerializer.Serialize(LocalMapReadiness.Verify(options))); }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException)
            { state.Put("local-map-access",JsonSerializer.Serialize(new {Error="Saved map access could not be verified."})); }
            using var scope=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var loops=new List<Task>{Saves(scope.Token),Providers(scope.Token),Heartbeat(scope.Token)};
            if(options.ScreenshotLibraryEnabled||options.ScreenshotPilot.Length>0){loops.Add(Task.Run(()=>Photos(scope.Token),scope.Token));loops.Add(Task.Run(()=>Thumbnails(scope.Token),scope.Token));}
            await Task.WhenAny(loops);scope.Cancel();await Task.WhenAll(loops);
        }
        catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){}
        catch{Environment.ExitCode=1;throw;}
    }
    async Task Heartbeat(CancellationToken token)
    {
        while(!token.IsCancellationRequested){state.Put("local-worker-tick");try{await Task.Delay(5000,token);}catch(OperationCanceledException){break;}}
    }
    async Task Photos(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try{_=workspace.LocalPhotos!.Catalogues();state.Put("local-photo-error","");}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException){state.Put("local-photo-error","The photo index is temporarily unavailable. Saved photo metadata is preserved.");}
            try{await Task.Delay(2000,token);}catch(OperationCanceledException){break;}
        }
    }
    async Task Thumbnails(CancellationToken token)
    {
        await Task.Delay(5000,token);
        while(!token.IsCancellationRequested)
        {
            try{await workspace.LocalPhotos!.WarmRecent(token);}
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch{/* Optional warming never changes availability or the photo worker's health. */}
            await Task.Delay(TimeSpan.FromSeconds(30),token);
        }
    }
    async Task Saves(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try{await refresh.Tick(token);}
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch{state.Put("source-error","A local save check is temporarily unavailable. Saved progress was preserved.");}
            try{await Task.Delay(2000,token);}catch(OperationCanceledException){break;}
        }
    }
    async Task Providers(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try
            {
                var minutes=15;var allow=true;var preferences=Path.Combine(options.ApplicationDataRoot,"achievement-notifications.json");
                if(File.Exists(preferences)){using var document=JsonDocument.Parse(File.ReadAllText(preferences));var p=document.RootElement;if(p.TryGetProperty("Enabled",out var enabled))allow=enabled.GetBoolean();if(p.TryGetProperty("IntervalMinutes",out var interval))minutes=interval.GetInt32();}
                if(options.LocalProviderRefreshEnabled&&state.Settings().AutomaticEnabled&&allow&&minutes!=0)
                {
                    var accounts=AchievementAccountScope.Read(options,workspace);
                    foreach(var game in workspace.LocalData!.Games().Values.Where(g=>accounts.ContainsKey(g.Provider)))
                    {
                        if(!state.Settings().AutomaticEnabled)break;
                        var key="provider-"+game.Provider+"-"+game.GameId;var last=state.Get(key).Updated;
                        if(last>DateTime.UtcNow.AddMinutes(-Math.Clamp(minutes,5,1440)).Ticks)continue;
                        var failed=state.Get(key+"-failed");if(failed.Value=="yes"&&failed.Updated>DateTime.UtcNow.AddMinutes(-30).Ticks)continue;
                        try
                        {
                            // Native's writer also takes this account/file lease before merging saved provider results.
                            await reader.Run(new(){Operation="provider",ApplicationRoot=options.ApplicationDataRoot,CacheRoot=options.CacheRoot,Provider=game.Provider,GameId=game.GameId,Accounts=accounts},token);
                            state.Put(key);state.Put(key+"-failed","");state.Put("local-provider-check");state.Put("local-provider-error","");
                        }
                        catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                        catch{state.Put(key+"-failed","yes");state.Put("local-provider-error","Saved provider data is retained; a provider refresh could not finish.");}
                        await Task.Delay(1000,token);
                    }
                }
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch{state.Put("local-provider-error","Provider refresh is temporarily unavailable. Saved data was preserved.");}
            // Due checks remain rate limited per game; revisit the schedule promptly so
            // a completed interval does not wait up to another five minutes.
            try{await Task.Delay(TimeSpan.FromSeconds(15),token);}catch(OperationCanceledException){break;}
        }
    }
}

public sealed class BoundedRuntimeLog(string root):ILoggerProvider
{
    readonly object gate=new();
    readonly string logRoot=root;
    public ILogger CreateLogger(string categoryName)=>new Writer(this);
    sealed class Writer(BoundedRuntimeLog owner):ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>level>=LogLevel.Warning;
        public void Log<TState>(LogLevel level,EventId eventId,TState state,Exception? error,Func<TState,Exception?,string> formatter)
        {
            if(!IsEnabled(level))return;
            lock(owner.gate)
            {
                Directory.CreateDirectory(owner.logRoot);var file=Path.Combine(owner.logRoot,"runtime.log");
                if(File.Exists(file)&&new FileInfo(file).Length>4*1024*1024)
                {for(var i=2;i>=1;i--){var prior=file+"."+i;if(File.Exists(prior))File.Move(prior,file+"."+(i+1),true);}File.Move(file,file+".1",true);}
                // Method names and numeric error codes make failures diagnosable without
                // recording exception messages, paths, request data or credentials.
                var failure=error?.GetBaseException();
                var calls=failure is null?"":string.Join(" > ",new System.Diagnostics.StackTrace(failure,false).GetFrames()
                    .Select(f=>f.GetMethod()).Where(m=>m?.DeclaringType?.FullName?.StartsWith("PixelVault.",StringComparison.Ordinal)==true)
                    .Take(5).Select(m=>m!.DeclaringType!.FullName+"."+m.Name));
                File.AppendAllText(file,$"{DateTime.UtcNow:o} {level} {eventId.Id} {error?.GetType().Name??"runtime-event"} Code={failure?.HResult.ToString("X8")??"none"} At={calls}{Environment.NewLine}");
            }
        }
    }
    public void Dispose(){}
}
