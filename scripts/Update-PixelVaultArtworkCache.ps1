#Requires -Version 7.0
#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
$plan=Get-Content -LiteralPath $PlanPath -Raw|ConvertFrom-Json
if($plan.Purpose -ne 'PixelVault-artwork-cache-0.47.1' -or $plan.FromVersion -ne '0.47.0' -or $plan.ToVersion -ne '0.47.1'){throw 'Unknown Companion update plan.'}
foreach($file in $plan.Files){if((Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash -ne $file.Sha256){throw "A reviewed package file changed: $($file.Path)"}}
$service=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
if($service.State -ne 'Running' -or $service.StartName -ne $plan.Identity -or $service.StartMode -ne $plan.StartMode -or $service.PathName -ne $plan.OldServicePath){throw 'The running Companion service differs from the reviewed update plan.'}
if((Get-FileHash -LiteralPath $plan.Settings -Algorithm SHA256).Hash -ne $plan.SettingsSha256){throw 'The Companion service settings changed; prepare a fresh update plan.'}
$configuration=Get-Content -LiteralPath $plan.Settings -Raw|ConvertFrom-Json
if($configuration.Companion.ApplicationDataRoot -ne $plan.ApplicationDataRoot -or $configuration.Companion.DataRoot -ne $plan.DataRoot -or $configuration.Companion.CacheRoot -ne $plan.CacheRoot -or $configuration.Companion.ReaderWorkerPath -ne $plan.ReaderWorkerPath -or $configuration.Companion.PhotoWorkerPath -ne $plan.PhotoWorkerPath){throw 'The service data roots or worker paths changed; prepare a fresh update plan.'}
if($plan.ApplicationDataRoot -ne 'C:\Codex\PixelVaultData' -or $plan.DataRoot -ne 'C:\Codex\PixelVaultCompanionLocalData' -or $plan.CacheRoot -ne 'C:\Codex\PixelVaultCompanionLocalCache'){throw 'The reviewed PC-only data roots do not match.'}
$inbound=@(Get-NetFirewallRule -DisplayName $plan.InboundRule -ErrorAction Stop)
if($inbound.Count -ne 1 -or -not $inbound[0].Enabled -or $inbound[0].Direction -ne 'Inbound' -or $inbound[0].Action -ne 'Allow'){throw 'The Companion inbound firewall rule changed.'}
$inboundApp=Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $inbound[0]
$inboundPort=Get-NetFirewallPortFilter -AssociatedNetFirewallRule $inbound[0]
if($inboundApp.Program -ne $plan.OldExe -or $inboundPort.Protocol -ne 'TCP' -or $inboundPort.LocalPort -ne '8787'){throw 'The inbound LAN rule no longer matches the reviewed service.'}
$blocked=@(Get-NetFirewallRule -DisplayName $plan.OldExeBlockRule -ErrorAction Stop)
if($blocked.Count -ne 3){throw 'The retired NAS endpoint block rules changed.'}
$blockFilters=@()
foreach($rule in $blocked){
    $filter=Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $rule
    if(-not $rule.Enabled -or $rule.Direction -ne 'Outbound' -or $rule.Action -ne 'Block'){throw 'A retired NAS endpoint block rule was weakened.'}
    $blockFilters+=@([pscustomobject]@{Rule=$rule;Filter=$filter;Program=$filter.Program})
}
if(@($blockFilters|Where-Object Program -eq $plan.OldExe).Count -ne 1 -or @($blockFilters|Where-Object Program -eq $plan.OldReaderPath).Count -ne 1 -or @($blockFilters|Where-Object Program -eq $plan.LegacyNasBlockPath).Count -ne 1){throw 'The retired NAS endpoint rules do not match the reviewed executables.'}
$backupTask=Get-ScheduledTask -TaskName 'PixelVault App Data Backup' -ErrorAction Stop
if($backupTask.State -eq 'Running'){throw 'The app-only backup is running; retry after it finishes.'}
$sid=([Security.Principal.NTAccount]$plan.Identity).Translate([Security.Principal.SecurityIdentifier]).Value
& icacls.exe (Split-Path -Parent $plan.NewExe) /grant "*$($sid):(OI)(CI)RX" /T /C | Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not grant the Companion service read access to the new package.'}
function Set-ServicePath([string]$path){
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $env:WINDIR 'System32\sc.exe'))
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in @('config','PixelVaultCompanion','binPath=',$path)){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($start)
    try{$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$process.WaitForExit();$message=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult();if($process.ExitCode -ne 0){throw $message}}finally{$process.Dispose()}
}
function Wait-Health([string]$version){
    for($i=0;$i -lt 60;$i++){
        try{$health=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 2;if($health.status -eq 'ok' -and $health.version -eq $version){return $true}}catch{}
        Start-Sleep -Milliseconds 500
    }
    return $false
}
if((Get-FileHash -LiteralPath $plan.NewSettings).Hash -ne $plan.NewSettingsSha256){throw 'The prepared worker setting changed.'}
$proposed=Get-Content -LiteralPath $plan.NewSettings -Raw|ConvertFrom-Json
$expected=Get-Content -LiteralPath $plan.Settings -Raw|ConvertFrom-Json
if($plan.NewPhotoWorkerPath -ne $plan.PhotoWorkerPath){throw 'Artwork cache update must preserve photo worker.'}
if(($expected|ConvertTo-Json -Depth 50 -Compress) -ne ($proposed|ConvertTo-Json -Depth 50 -Compress)){throw 'Artwork cache update must preserve all settings.'}
Copy-Item -LiteralPath $plan.Settings -Destination (Join-Path $plan.EvidenceRoot 'settings-before-update.json') -Force
$switched=$false
try{
    Stop-Service -Name PixelVaultCompanion -ErrorAction Stop
    (Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
    $switched=$true
    Copy-Item -LiteralPath $plan.NewSettings -Destination $plan.Settings -Force
    Set-ServicePath $plan.NewServicePath
    $inbound|Get-NetFirewallApplicationFilter|Set-NetFirewallApplicationFilter -Program $plan.NewExe
    foreach($entry in $blockFilters){if($entry.Program -eq $plan.OldExe){$entry.Filter|Set-NetFirewallApplicationFilter -Program $plan.NewExe}}
    Start-Service PixelVaultCompanion
    if(-not(Wait-Health $plan.ToVersion)){throw 'Companion 0.47.1 did not pass its local HTTP health check.'}
    $running=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
    if($running.PathName -ne $plan.NewServicePath -or $running.StartName -ne $plan.Identity -or $running.StartMode -ne $plan.StartMode){throw 'The updated service path, identity or startup mode is incorrect.'}
    if((Get-FileHash -LiteralPath $plan.Settings -Algorithm SHA256).Hash -ne $plan.NewSettingsSha256){throw 'The Companion service settings changed during deployment.'}
    $root=Invoke-WebRequest 'http://127.0.0.1:8787/' -TimeoutSec 10
    $index=Invoke-WebRequest 'http://127.0.0.1:8787/index.html' -TimeoutSec 10
    $assetHash=(Get-FileHash -LiteralPath (Join-Path (Split-Path $plan.NewExe -Parent) 'wwwroot/app.js')).Hash.ToLowerInvariant().Substring(0,16)
    $script=Invoke-WebRequest ("http://127.0.0.1:8787/app.js?v="+$assetHash) -TimeoutSec 10
    if($root.StatusCode -ne 200 -or $index.Content -notmatch $assetHash -or $script.StatusCode -ne 200 -or $script.Headers['Cache-Control'] -notmatch 'immutable'){throw 'The updated browser fingerprint/cache checks failed.'}
    $after=Get-Content -LiteralPath $plan.Settings -Raw|ConvertFrom-Json
    if($after.Companion.ApplicationDataRoot -ne $plan.ApplicationDataRoot -or $after.Companion.DataRoot -ne $plan.DataRoot -or $after.Companion.CacheRoot -ne $plan.CacheRoot -or $after.Companion.ReaderWorkerPath -ne $plan.ReaderWorkerPath -or $after.Companion.PhotoWorkerPath -ne $plan.NewPhotoWorkerPath){throw 'The updated service settings do not preserve the approved roots and workers.'}
    $public=Invoke-RestMethod 'http://192.168.1.69:8787/health' -TimeoutSec 10
    if($public.status -ne 'ok' -or $public.version -ne $plan.ToVersion){throw 'LAN health did not report the updated Companion version.'}
    $result=@{Passed=$true;Version=$plan.ToVersion;PreviousVersion=$plan.FromVersion;DataRootsChanged=$false;PhotoWorkerUpdated=$false;ReaderPreserved=$true;FirewallsPreserved=$true;SettingsSha256=$plan.NewSettingsSha256;LocalHealth=$true;LanHealth=$true;BrowserCacheKey=$assetHash;ArtworkCacheUpdate=$true;UpdatedUtc=[DateTime]::UtcNow.ToString('o')}
    $result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'deployment-verification.json') -Encoding utf8
    Write-Host 'PASS: Companion 0.47.1 is live. PC data roots, reader paths and NAS block rules are preserved.'
}catch{
    $failure=$_.Exception.Message
    if($switched){
        try{
            Stop-Service PixelVaultCompanion -ErrorAction SilentlyContinue
            (Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
            Copy-Item -LiteralPath (Join-Path $plan.EvidenceRoot 'settings-before-update.json') -Destination $plan.Settings -Force
            Set-ServicePath $plan.OldServicePath
            $inbound|Get-NetFirewallApplicationFilter|Set-NetFirewallApplicationFilter -Program $plan.OldExe
            foreach($entry in $blockFilters){if($entry.Program -eq $plan.OldExe){$entry.Filter|Set-NetFirewallApplicationFilter -Program $plan.OldExe}}
            Start-Service PixelVaultCompanion
            if(-not(Wait-Health $plan.FromVersion)){throw 'Rollback service did not pass previous-version health.'}
            @{Passed=$false;RolledBack=$true;Failure=$failure;RestoredVersion=$plan.FromVersion;UpdatedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'deployment-verification.json') -Encoding utf8
        }catch{throw "Update failed ($failure). Automatic rollback also reported: $($_.Exception.Message)"}
    }
    throw
}
