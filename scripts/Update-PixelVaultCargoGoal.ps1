#Requires -Version 7.0
#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
$plan=Get-Content -LiteralPath $PlanPath -Raw|ConvertFrom-Json
if($plan.Purpose -ne 'PixelVault-cargo-50000-0.44.1' -or $plan.FromVersion -ne '0.44.0' -or $plan.ToVersion -ne '0.44.1'){throw 'Unknown cargo update plan.'}
foreach($file in $plan.Files){if((Get-FileHash -LiteralPath $file.Path).Hash -ne $file.Sha256){throw "A reviewed package changed: $($file.Path)"}}
$service=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
if($service.State -ne 'Running' -or $service.PathName -ne $plan.OldServicePath -or $service.StartName -ne $plan.Identity -or $service.StartMode -ne $plan.StartMode){throw 'The service no longer matches the update plan.'}
if((Get-FileHash -LiteralPath $plan.Settings).Hash -ne $plan.SettingsSha256){throw 'The live service configuration changed.'}
$old=Get-Content -LiteralPath $plan.Settings -Raw|ConvertFrom-Json
$new=Get-Content -LiteralPath $plan.NewSettings -Raw|ConvertFrom-Json
foreach($field in @('ApplicationDataRoot','DataRoot','CacheRoot','PhotoWorkerPath')){if($old.Companion.$field -ne $new.Companion.$field){throw "Unexpected change to $field"}}
if($old.Companion.ApplicationDataRoot -ne 'C:\Codex\PixelVaultData' -or $old.Companion.ReaderWorkerPath -ne $plan.OldReaderPath -or $new.Companion.ReaderWorkerPath -ne $plan.NewReaderPath){throw 'Unexpected data or reader paths.'}
$inbound=@(Get-NetFirewallRule -DisplayName $plan.InboundRule -ErrorAction Stop)
if($inbound.Count -ne 1 -or -not $inbound[0].Enabled -or $inbound[0].Direction -ne 'Inbound' -or $inbound[0].Action -ne 'Allow'){throw 'The LAN inbound rule changed.'}
$inboundFilter=Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $inbound[0]
$port=Get-NetFirewallPortFilter -AssociatedNetFirewallRule $inbound[0]
if($inboundFilter.Program -ne $plan.OldExe -or $port.Protocol -ne 'TCP' -or $port.LocalPort -ne '8787'){throw 'The LAN rule does not match the running service.'}
$blocked=@(Get-NetFirewallRule -DisplayName $plan.OldExeBlockRule -ErrorAction Stop)
if($blocked.Count -ne 3){throw 'The retired NAS endpoint guards changed.'}
$blockFilters=@()
foreach($rule in $blocked){
    if(-not $rule.Enabled -or $rule.Direction -ne 'Outbound' -or $rule.Action -ne 'Block'){throw 'A retired NAS guard was weakened.'}
    $filter=Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $rule
    $blockFilters+=@([pscustomobject]@{Filter=$filter;Program=$filter.Program})
}
foreach($expected in @($plan.OldExe,$plan.OldReaderPath,$plan.LegacyNasBlockPath)){if(@($blockFilters|Where-Object Program -eq $expected).Count -ne 1){throw 'The guarded executable paths changed.'}}
if((Get-ScheduledTask -TaskName 'PixelVault App Data Backup').State -eq 'Running'){throw 'The app backup is running. Retry when it finishes.'}
$sid=([Security.Principal.NTAccount]$plan.Identity).Translate([Security.Principal.SecurityIdentifier]).Value
& icacls.exe (Split-Path -Parent $plan.NewExe) /grant "*$($sid):(OI)(CI)RX" /T /C|Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not grant service access to the reviewed package.'}
function Set-ServicePath([string]$path){
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $env:WINDIR 'System32\sc.exe'));$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in @('config','PixelVaultCompanion','binPath=',$path)){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($start)
    try{$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$process.WaitForExit();$message=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult();if($process.ExitCode -ne 0){throw $message}}finally{$process.Dispose()}
}
function Wait-Health([string]$version){
    for($i=0;$i -lt 60;$i++){try{$h=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 2;if($h.status -eq 'ok' -and $h.version -eq $version){return $true}}catch{};Start-Sleep -Milliseconds 500}
    return $false
}
$switched=$false
try{
    $switched=$true;Stop-Service PixelVaultCompanion;(Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
    & $plan.SetupExe 'C:\Codex\PixelVaultData' 'C:\Codex\PixelVaultCompanionLocalData\workspace.json' $plan.NewReaderPath (Join-Path $plan.EvidenceRoot 'live-connection.json') | Out-File -LiteralPath (Join-Path $plan.EvidenceRoot 'live-refresh.log')
    if($LASTEXITCODE -ne 0){throw 'The updated weight goal did not pass its verified save refresh.'}
    $result=Get-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'live-connection.json') -Raw|ConvertFrom-Json
    if(-not $result.Passed -or $result.Target -ne 50000 -or $result.AggregateTotal -ne 1 -or -not $result.AllOtherTrackerRowsUnchanged){throw 'Unexpected weight verification result.'}
    Copy-Item -LiteralPath $plan.NewSettings -Destination $plan.Settings -Force
    Set-ServicePath $plan.NewServicePath
    $inboundFilter|Set-NetFirewallApplicationFilter -Program $plan.NewExe
    foreach($entry in $blockFilters){if($entry.Program -eq $plan.OldExe){$entry.Filter|Set-NetFirewallApplicationFilter -Program $plan.NewExe}elseif($entry.Program -eq $plan.OldReaderPath){$entry.Filter|Set-NetFirewallApplicationFilter -Program $plan.NewReaderPath}}
    $started=[DateTime]::UtcNow.Ticks;Start-Service PixelVaultCompanion
    if(-not (Wait-Health $plan.ToVersion)){throw 'The updated service failed HTTP health.'}
    $workerReady=$false
    for($i=0;$i -lt 90;$i++){
        & $plan.PythonExe $plan.WorkerCheck 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite' $started
        if($LASTEXITCODE -eq 0){$workerReady=$true;break};Start-Sleep -Milliseconds 500
    }
    if(-not $workerReady){throw 'The updated reader did not pass service-account startup.'}
    if((Get-FileHash -LiteralPath $plan.Settings).Hash -ne $plan.NewSettingsSha256){throw 'The installed configuration changed.'}
    $running=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
    if($running.PathName -ne $plan.NewServicePath -or $running.StartName -ne $plan.Identity -or $running.StartMode -ne $plan.StartMode){throw 'The updated service identity or path changed.'}
    $index=Invoke-WebRequest 'http://127.0.0.1:8787/' -TimeoutSec 10
    $script=Invoke-WebRequest 'http://127.0.0.1:8787/library.js?v=0.44.1-cargo50k1' -TimeoutSec 10
    if($index.Content -notmatch '0.44.1-cargo50k1' -or $script.Content -notmatch 'aggregateTotal'){throw 'The browser item-count update is not ready.'}
    $lan=Invoke-RestMethod 'http://192.168.1.69:8787/health' -TimeoutSec 10
    if($lan.status -ne 'ok' -or $lan.version -ne $plan.ToVersion){throw 'LAN health failed.'}
    @{Passed=$true;Version=$plan.ToVersion;NativeVersion='0.117.006';Current=$result.Current;Target=$result.Target;AggregateTotal=$result.AggregateTotal;AggregateCompleted=$result.AggregateCompleted;LocalHealth=$true;LanHealth=$true;FreshWorkerHeartbeat=$workerReady;DataRootsChanged=$false;PhotoWorkerChanged=$false;ReaderWorkerUpdated=$true;FirewallsPreserved=$true;OtherTrackerRowsUnchanged=$true;UpdatedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'deployment-verification.json')
    Write-Host 'PASS: the 50,000 kg goal and single-item aggregate count are live.'
}catch{
    $failure=$_.Exception.Message
    if($switched){
        Stop-Service PixelVaultCompanion -ErrorAction SilentlyContinue;(Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
        Copy-Item -LiteralPath $plan.SettingsBackup -Destination $plan.Settings -Force
        Set-ServicePath $plan.OldServicePath
        $inboundFilter|Set-NetFirewallApplicationFilter -Program $plan.OldExe
        foreach($entry in $blockFilters){if($entry.Program -eq $plan.OldExe -or $entry.Program -eq $plan.OldReaderPath){$entry.Filter|Set-NetFirewallApplicationFilter -Program $entry.Program}}
        Start-Service PixelVaultCompanion
        if(-not (Wait-Health $plan.FromVersion)){throw "Update failed ($failure); rollback health also failed."}
    }
    @{Passed=$false;RolledBack=$switched;Failure=$failure}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'deployment-verification.json')
    throw $failure
}
