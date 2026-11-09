#Requires -Version 7.0
#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
$plan=Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
if($plan.Purpose -ne 'PixelVault-central-refresh-0.50.0' -or $plan.FromVersion -ne '0.49.0' -or $plan.ToVersion -ne '0.50.0'){throw 'Unexpected central refresh update plan.'}
if($plan.NewExe -ne 'C:\Codex\Companion Production\app-0.50.0\PixelVault.Companion.exe'){throw 'Unexpected release destination.'}
$resultPath=Join-Path $plan.EvidenceRoot 'deployment-verification.json'
$switched=$false
$filters=@()
function Set-CompanionServicePath([string]$path){
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $env:WINDIR 'System32\sc.exe'))
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in @('config','PixelVaultCompanion','binPath=',$path)){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($start)
    try{$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$process.WaitForExit();$message=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult();if($process.ExitCode -ne 0){throw $message}}finally{$process.Dispose()}
}
function Wait-CompanionHealth([string]$version){
    for($attempt=0;$attempt -lt 50;$attempt++){
        try{$health=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 2;if($health.status -eq 'ok' -and $health.version -eq $version){return}}catch{}
        Start-Sleep -Milliseconds 500
    }
    throw "Companion did not report version $version."
}
try{
    foreach($file in $plan.Files){if((Get-FileHash -LiteralPath $file.Path).Hash -ne $file.Sha256){throw "Prepared file changed: $($file.Path)"}}
    $service=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
    if($service.State -ne 'Running' -or $service.PathName -ne $plan.OldServicePath -or $service.StartName -ne $plan.Identity -or $service.StartMode -ne $plan.StartMode){throw 'The installed Companion service changed after preparation.'}
    if((Get-FileHash -LiteralPath $plan.Settings).Hash -ne $plan.SettingsSha256){throw 'The service settings changed after preparation.'}
    if((Get-ScheduledTask -TaskName 'PixelVault App Data Backup' -ErrorAction Stop).State -eq 'Running'){throw 'The app data backup is running. Retry after it completes.'}
    $inbound=@(Get-NetFirewallRule -DisplayName 'PixelVault Companion PC service')
    if($inbound.Count -ne 1 -or $inbound[0].Enabled -ne 'True' -or $inbound[0].Action -ne 'Allow' -or $inbound[0].Direction -ne 'Inbound'){throw 'The Companion inbound firewall rule changed.'}
    $inboundFilter=$inbound[0] | Get-NetFirewallApplicationFilter
    if($inboundFilter.Program -ne $plan.OldExe){throw 'The inbound firewall application differs from the service.'}
    $filters+=@($inboundFilter)
    $blocks=@(Get-NetFirewallRule -DisplayName 'PixelVault retired NAS Companion endpoint')
    if($blocks.Count -ne 3 -or @($blocks | Where-Object {$_.Enabled -ne 'True' -or $_.Direction -ne 'Outbound' -or $_.Action -ne 'Block'}).Count){throw 'The retired NAS firewall protections changed.'}
    $appBlocks=@($blocks | Get-NetFirewallApplicationFilter | Where-Object Program -EQ $plan.OldExe)
    if($appBlocks.Count -ne 1){throw 'The retired NAS block for the current web executable is missing.'}
    $filters+=@($appBlocks)
    foreach($name in @('PixelVault.Tracking.dll','PixelVault.Maps.dll')){
        if((Get-FileHash -LiteralPath (Join-Path (Split-Path $plan.OldExe) $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path (Split-Path $plan.NewExe) $name)).Hash){throw 'Shared tracker/map assemblies must remain unchanged.'}
    }
    $sid=([Security.Principal.NTAccount]$plan.Identity).Translate([Security.Principal.SecurityIdentifier]).Value
    & icacls.exe (Split-Path $plan.NewExe) /grant "*$($sid):(OI)(CI)RX" /T /C | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Could not grant the service read access to the prepared release.'}
    Wait-CompanionHealth $plan.FromVersion
    Stop-Service PixelVaultCompanion
    $switched=$true
    (Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
    Set-CompanionServicePath $plan.NewServicePath
    foreach($filter in $filters){$filter | Set-NetFirewallApplicationFilter -Program $plan.NewExe}
    Start-Service PixelVaultCompanion
    Wait-CompanionHealth $plan.ToVersion
    $running=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
    if($running.PathName -ne $plan.NewServicePath -or $running.StartName -ne $plan.Identity -or $running.StartMode -ne $plan.StartMode){throw 'The updated service identity/startup differs from the prepared plan.'}
    if((Get-FileHash -LiteralPath $plan.Settings).Hash -ne $plan.SettingsSha256){throw 'Service settings changed during the update.'}
    foreach($asset in @('app.js','updates.js','profile.css','refresh-stream.js')){
        $response=Invoke-WebRequest ("http://127.0.0.1:8787/"+$asset) -TimeoutSec 10
        $expected=[IO.File]::ReadAllText((Join-Path (Split-Path $plan.NewExe) "wwwroot\$asset"))
        if($response.StatusCode -ne 200 -or $response.Content -cne $expected){throw "Served asset differs from the prepared file: $asset"}
    }
    $auth=Invoke-WebRequest 'http://127.0.0.1:8787/api/v1/achievement-updates' -SkipHttpErrorCheck -TimeoutSec 10
    if($auth.StatusCode -ne 401){throw 'Achievement update metadata must require authentication.'}
    $streamAuth=Invoke-WebRequest 'http://127.0.0.1:8787/api/v1/refresh/events?workspace=unauthorized' -SkipHttpErrorCheck -TimeoutSec 10
    if($streamAuth.StatusCode -ne 401){throw 'The update stream must require authentication.'}
    $notice=Join-Path 'C:\Codex\PixelVaultData' 'refresh-updates.json'
    for($i=0;$i -lt 40;$i++){if((Test-Path $notice) -and ((Get-Item $notice).LastWriteTimeUtc -gt [DateTime]::UtcNow.AddSeconds(-30))){break};Start-Sleep -Milliseconds 500}
    if(!(Test-Path $notice) -or ((Get-Item $notice).LastWriteTimeUtc -lt [DateTime]::UtcNow.AddSeconds(-30))){throw 'The desktop update snapshot is not fresh.'}
    $lan=Invoke-RestMethod 'http://192.168.1.69:8787/health' -TimeoutSec 10
    if($lan.version -ne $plan.ToVersion){throw 'LAN health does not match the updated version.'}
    @{Succeeded=$true;Version=$plan.ToVersion;PreviousVersion=$plan.FromVersion;SettingsUnchanged=$true;SharedAssembliesUnchanged=$true;LocalHealth=$true;LanHealth=$true;AchievementEndpointRequiresAuthentication=$true;UpdatedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $resultPath
}catch{
    $failure=$_.Exception.Message;$rollback=$false;$rollbackError=$null
    if($switched){
        try{
            Stop-Service PixelVaultCompanion -ErrorAction SilentlyContinue
            (Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
            Set-CompanionServicePath $plan.OldServicePath
            foreach($filter in $filters){$filter | Set-NetFirewallApplicationFilter -Program $plan.OldExe}
            Start-Service PixelVaultCompanion
            Wait-CompanionHealth $plan.FromVersion
            $rollback=$true
        }catch{$rollbackError=$_.Exception.Message}
    }
    @{Succeeded=$false;Error=$failure;RolledBack=$rollback;RollbackError=$rollbackError;UpdatedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $resultPath
    throw
}
