#Requires -Version 7.0
#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
if ($plan.Purpose -ne 'PixelVault-century-legend-normal-0.44.3' -or $plan.FromVersion -ne '0.44.2' -or $plan.ToVersion -ne '0.44.3') { throw 'Unknown Legend update plan.' }
foreach ($file in $plan.Files) { if ((Get-FileHash -LiteralPath $file.Path).Hash -ne $file.Sha256) { throw "A reviewed update file changed: $($file.Path)" } }
$service = Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
if ($service.State -ne 'Running' -or $service.PathName -ne $plan.OldServicePath -or $service.StartName -ne $plan.Identity -or $service.StartMode -ne $plan.StartMode) { throw 'The Companion service no longer matches the update plan.' }
if ((Get-FileHash -LiteralPath $plan.Settings).Hash -ne $plan.SettingsSha256) { throw 'The live service configuration changed.' }
$old = Get-Content -LiteralPath $plan.Settings -Raw | ConvertFrom-Json
$new = Get-Content -LiteralPath $plan.NewSettings -Raw | ConvertFrom-Json
foreach ($field in @('ApplicationDataRoot', 'DataRoot', 'CacheRoot', 'PhotoWorkerPath')) { if ($old.Companion.$field -ne $new.Companion.$field) { throw "Unexpected change to $field" } }
if ($old.Companion.ApplicationDataRoot -ne 'C:\Codex\PixelVaultData' -or $old.Companion.DataRoot -ne 'C:\Codex\PixelVaultCompanionLocalData' -or $old.Companion.ReaderWorkerPath -ne $plan.OldReaderPath -or $new.Companion.ReaderWorkerPath -ne $plan.NewReaderPath) { throw 'Unexpected data or reader paths.' }
$inbound = @(Get-NetFirewallRule -DisplayName $plan.InboundRule -ErrorAction Stop)
if ($inbound.Count -ne 1 -or -not $inbound[0].Enabled -or $inbound[0].Direction -ne 'Inbound' -or $inbound[0].Action -ne 'Allow') { throw 'The LAN inbound rule changed.' }
$inboundFilter = Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $inbound[0]
$port = Get-NetFirewallPortFilter -AssociatedNetFirewallRule $inbound[0]
if ($inboundFilter.Program -ne $plan.OldExe -or $port.Protocol -ne 'TCP' -or $port.LocalPort -ne '8787') { throw 'The LAN rule does not match the running service.' }
$blocked = @(Get-NetFirewallRule -DisplayName $plan.OldExeBlockRule -ErrorAction Stop)
if ($blocked.Count -ne 3) { throw 'The retired NAS endpoint guards changed.' }
$blockFilters = @()
foreach ($rule in $blocked) {
    if (-not $rule.Enabled -or $rule.Direction -ne 'Outbound' -or $rule.Action -ne 'Block') { throw 'A retired NAS guard was weakened.' }
    $filter = Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $rule
    $blockFilters += [pscustomobject]@{ Filter = $filter; Program = $filter.Program }
}
foreach ($expected in @($plan.OldExe, $plan.OldReaderPath, $plan.LegacyNasBlockPath)) { if (@($blockFilters | Where-Object Program -eq $expected).Count -ne 1) { throw 'The guarded executable paths changed.' } }
if ((Get-ScheduledTask -TaskName 'PixelVault App Data Backup').State -eq 'Running') { throw 'The app backup is running. Retry after it finishes.' }
$sid = ([Security.Principal.NTAccount]$plan.Identity).Translate([Security.Principal.SecurityIdentifier]).Value
& icacls.exe (Split-Path -Parent $plan.NewExe) /grant "*$($sid):(OI)(CI)RX" /T /C | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not grant service access to the reviewed package.' }
function Set-ServicePath([string]$path) {
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $env:WINDIR 'System32\sc.exe')); $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WindowStyle = 'Hidden'; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in @('config', 'PixelVaultCompanion', 'binPath=', $path)) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try { $out = $process.StandardOutput.ReadToEndAsync(); $err = $process.StandardError.ReadToEndAsync(); $process.WaitForExit(); if ($process.ExitCode -ne 0) { throw ($out.GetAwaiter().GetResult() + $err.GetAwaiter().GetResult()) } } finally { $process.Dispose() }
}
function Wait-Health([string]$version) {
    for ($i = 0; $i -lt 60; $i++) { try { $health = Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 2; if ($health.status -eq 'ok' -and $health.version -eq $version) { return $true } } catch { }; Start-Sleep -Milliseconds 500 }
    return $false
}
$switched = $false
try {
    $switched = $true
    Stop-Service PixelVaultCompanion
    (Get-Service PixelVaultCompanion).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    & $plan.SetupExe 'C:\Codex\PixelVaultData' 'C:\Codex\PixelVaultCompanionLocalData\workspace.json' $plan.NewReaderPath (Join-Path $plan.EvidenceRoot 'live-refresh.json') | Out-File -LiteralPath (Join-Path $plan.EvidenceRoot 'live-refresh.log')
    if ($LASTEXITCODE -ne 0) { throw 'The latest full order save could not be verified.' }
    $result = Get-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'live-refresh.json') -Raw | ConvertFrom-Json
    if (-not $result.Passed -or $result.Order430 -ne 1 -or $result.Target -ne 540 -or -not $result.AllOtherTrackerRowsUnchanged) { throw 'The verified order progress did not match the rehearsal.' }
    Copy-Item -LiteralPath $plan.NewSettings -Destination $plan.Settings -Force
    Set-ServicePath $plan.NewServicePath
    $inboundFilter | Set-NetFirewallApplicationFilter -Program $plan.NewExe
    foreach ($entry in $blockFilters) { if ($entry.Program -eq $plan.OldExe) { $entry.Filter | Set-NetFirewallApplicationFilter -Program $plan.NewExe } elseif ($entry.Program -eq $plan.OldReaderPath) { $entry.Filter | Set-NetFirewallApplicationFilter -Program $plan.NewReaderPath } }
    $started = [DateTime]::UtcNow.Ticks
    Start-Service PixelVaultCompanion
    if (-not (Wait-Health $plan.ToVersion)) { throw 'The updated Companion service failed its HTTP health check.' }
    $workerReady = $false
    for ($i = 0; $i -lt 90; $i++) { & $plan.PythonExe $plan.WorkerCheck 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite' $started; if ($LASTEXITCODE -eq 0) { $workerReady = $true; break }; Start-Sleep -Milliseconds 500 }
    if (-not $workerReady) { throw 'The updated reader did not pass service-account startup.' }
    if ((Get-FileHash -LiteralPath $plan.Settings).Hash -ne $plan.NewSettingsSha256) { throw 'The installed service configuration changed.' }
    $running = Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
    if ($running.PathName -ne $plan.NewServicePath -or $running.StartName -ne $plan.Identity -or $running.StartMode -ne $plan.StartMode) { throw 'The updated service identity or path changed.' }
    $index = Invoke-WebRequest 'http://127.0.0.1:8787/' -TimeoutSec 10
    if ($index.Content -notmatch '0.44.3-century-legend1') { throw 'The new browser cache key is not live.' }
    $app = Invoke-WebRequest 'http://127.0.0.1:8787/app.js?v=0.44.3-century-legend1' -TimeoutSec 10
    if ($app.StatusCode -ne 200) { throw 'The Companion application script is unavailable.' }
    $lan = Invoke-RestMethod 'http://192.168.1.69:8787/health' -TimeoutSec 10
    if ($lan.status -ne 'ok' -or $lan.version -ne $plan.ToVersion) { throw 'LAN health failed.' }
    @{ Passed = $true; Version = $plan.ToVersion; NativeVersion = '0.117.016'; Current = $result.Current; Target = $result.Target; Order430 = $result.Order430; LocalHealth = $true; LanHealth = $true; FreshWorkerHeartbeat = $workerReady; DataRootsChanged = $false; PhotoWorkerChanged = $false; ReaderWorkerUpdated = $true; FirewallsPreserved = $true; OtherTrackerRowsUnchanged = $true; UpdatedUtc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'deployment-verification.json')
    Write-Host 'PASS: Legend rank and the saved order catalogue are live.'
}
catch {
    $failure = $_.Exception.Message
    if ($switched) {
        Stop-Service PixelVaultCompanion -ErrorAction SilentlyContinue
        (Get-Service PixelVaultCompanion).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        Copy-Item -LiteralPath $plan.SettingsBackup -Destination $plan.Settings -Force
        Set-ServicePath $plan.OldServicePath
        $inboundFilter | Set-NetFirewallApplicationFilter -Program $plan.OldExe
        foreach ($entry in $blockFilters) { if ($entry.Program -eq $plan.OldExe -or $entry.Program -eq $plan.OldReaderPath) { $entry.Filter | Set-NetFirewallApplicationFilter -Program $entry.Program } }
        Start-Service PixelVaultCompanion
        if (-not (Wait-Health $plan.FromVersion)) { throw "Update failed ($failure); rollback health also failed." }
    }
    @{ Passed = $false; RolledBack = $switched; Failure = $failure } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'deployment-verification.json')
    throw $failure
}
