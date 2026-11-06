#Requires -Version 7.0
#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
$updatePlan=Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
if($updatePlan.Purpose -ne 'pc-companion-ui-0.41.3' -or $updatePlan.FromVersion -ne '0.41.2' -or $updatePlan.ToVersion -ne '0.41.3'){throw 'Unknown update plan.'}
$fromVersion=[string]$updatePlan.FromVersion;$toVersion=[string]$updatePlan.ToVersion
foreach($entry in $updatePlan.Files){if((Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ne $entry.Sha256){throw "A reviewed update file changed: $($entry.Path)"}}
if(-not (Test-Path -LiteralPath $updatePlan.SourceArchive)){throw 'The reviewed source archive is missing.'}
$rehearsal=Get-Content -LiteralPath $updatePlan.RehearsalResult -Raw | ConvertFrom-Json
if(-not $rehearsal.passed -or $rehearsal.version -ne '0.41.0' -or $rehearsal.pins -ne 56){throw 'The verified 0.41.0 map baseline is required.'}
$settings='C:\Codex\Companion Production\service.json'
if((Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash -ne $updatePlan.SettingsSha256){throw 'The live settings changed; prepare a fresh update plan.'}
$previous=Get-Content -LiteralPath $settings -Raw
$configuration=$previous | ConvertFrom-Json
if($configuration.Companion.ApplicationDataRoot -ne 'C:\Codex\PixelVaultData' -or $configuration.Companion.DataRoot -ne 'C:\Codex\PixelVaultCompanionLocalData'){throw 'Unexpected production roots.'}
$service=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
$oldExe="C:\Codex\Companion Production\app-$fromVersion\PixelVault.Companion.exe"
$newExe="C:\Codex\Companion Production\app-$toVersion\PixelVault.Companion.exe"
$expected='"'+$oldExe+'" --installation-config "'+$settings+'"'
if($service.PathName -ne $expected -or $service.StartName -ne 'NT SERVICE\PixelVaultCompanion' -or $service.State -ne 'Running'){throw 'The current production service differs from the reviewed update.'}
$firewall=Get-NetFirewallRule -DisplayName 'PixelVault Companion PC service' -ErrorAction Stop
if(@($firewall).Count -ne 1 -or ($firewall | Get-NetFirewallApplicationFilter).Program -ne $oldExe){throw 'The reviewed inbound rule changed.'}
$oldReader="C:\Codex\Companion Production\app-$fromVersion\readers\PixelVault.exe"
$newReader="C:\Codex\Companion Production\app-$toVersion\readers\PixelVault.exe"
$outbound=@(Get-NetFirewallRule -DisplayName 'PixelVault retired NAS Companion endpoint' | Get-NetFirewallApplicationFilter | Where-Object Program -in @($oldExe,$newExe,$oldReader,$newReader))
if($outbound.Count -ne 2){throw 'The retired-NAS endpoint guards changed.'}
$outboundState=@($outbound | ForEach-Object {@{Filter=$_;Reader=$_.Program -in @($oldReader,$newReader)}})
if(@($outboundState | Where-Object Reader).Count -ne 1){throw 'The reviewed source-reader endpoint guard changed.'}
function Invoke-UpdateSc([string[]]$Arguments){
 $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $env:WINDIR 'System32\sc.exe'));$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 foreach($argument in $Arguments){$start.ArgumentList.Add($argument)}
 $process=[Diagnostics.Process]::Start($start)
 try{$output=$process.StandardOutput.ReadToEndAsync();$errorOutput=$process.StandardError.ReadToEndAsync();$process.WaitForExit();$message=$output.GetAwaiter().GetResult()+$errorOutput.GetAwaiter().GetResult();if($process.ExitCode -ne 0){throw $message}}finally{$process.Dispose()}
}
$sid=([Security.Principal.NTAccount]'NT SERVICE\PixelVaultCompanion').Translate([Security.Principal.SecurityIdentifier]).Value
& icacls.exe (Split-Path $newExe -Parent) /grant "*$($sid):(OI)(CI)RX" | Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not grant service access to the reviewed update.'}
$mapRoot='C:\Codex\PixelVaultData\game-maps'
$mapFiles=@(Join-Path $mapRoot 'maps.sqlite')+@($updatePlan.MapImages | ForEach-Object {
 $mapImagePath=[IO.Path]::GetFullPath($_.Path)
 if([IO.Path]::GetDirectoryName($mapImagePath) -ne (Join-Path $mapRoot 'images')){throw 'An image verification path escaped the map library.'}
 if((Get-FileHash -LiteralPath $mapImagePath -Algorithm SHA256).Hash -ne $_.Sha256){throw 'An approved map original changed. Rehearse again before updating.'}
 $mapImagePath
})
$backupTask=Get-ScheduledTask -TaskName 'PixelVault App Data Backup'
if($backupTask.State -eq 'Running'){throw 'The app backup is running. Let it finish before applying this update.'}
$backupEnabled=$backupTask.State -ne 'Disabled';$switched=$false
$stage='backup'
try{
 if($backupEnabled){Disable-ScheduledTask -TaskName 'PixelVault App Data Backup' | Out-Null}
 $backup=Get-Content -LiteralPath $updatePlan.BackupPlan -Raw | ConvertFrom-Json
 if((Get-FileHash -LiteralPath $backup.engine -Algorithm SHA256).Hash -ne $backup.engineSha256){throw 'The backup engine changed.'}
 & $backup.pythonExe $backup.engine snapshot $updatePlan.BackupPlan --label before-maps-update | Out-File -LiteralPath (Join-Path $updatePlan.EvidenceRoot 'before-maps-update-backup.log')
 if($LASTEXITCODE -ne 0){throw 'The pre-update app backup failed; the running service was retained.'}
 $policyQuery='import pathlib,sqlite3,json,sys; c=sqlite3.connect(pathlib.Path(sys.argv[1]).as_uri()+"?mode=ro",uri=True); r=c.execute("SELECT value FROM companion_connector_activity WHERE name=?",("settings",)).fetchone(); assert r,"Automatic settings unavailable"; print(json.dumps(bool(json.loads(r[0]).get("AutomaticEnabled",False))))'
 $automaticBefore=& $backup.pythonExe -c $policyQuery 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite'
 if($LASTEXITCODE -ne 0){throw 'Could not read the current automatic-update policy.'}
 $automaticBefore=$automaticBefore | ConvertFrom-Json
 $stage='service-switch'
 $switched=$true
 Stop-Service -Name PixelVaultCompanion -ErrorAction Stop
 (Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
 $configuration.Companion.PhotoWorkerPath="C:\Codex\Companion Production\app-$toVersion\media-worker\PixelVault.Companion.Media.dll"
 $configuration.Companion.ReaderWorkerPath=$newReader
 $configuration | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settings
 Invoke-UpdateSc @('config','PixelVaultCompanion','binPath=',('"'+$newExe+'" --installation-config "'+$settings+'"'))
 $firewall | Get-NetFirewallApplicationFilter | Set-NetFirewallApplicationFilter -Program $newExe
 foreach($entry in $outboundState){$destination=if($entry.Reader){$newReader}else{$newExe};$entry.Filter | Set-NetFirewallApplicationFilter -Program $destination}
 $startedTicks=[DateTime]::UtcNow.Ticks
 $stage='http-readiness'
 Start-Service PixelVaultCompanion
 $photosReady=$false
 $ready=$false
 for($attempt=0;$attempt -lt 60;$attempt++){
  try{$health=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 2;if($health.status -eq 'ok' -and $health.version -eq $toVersion){$ready=$true;break}}catch{}
  Start-Sleep -Milliseconds 500
 }
 if(-not $ready){throw 'The updated service did not pass HTTP health.'}
 $stage='worker-readiness'
 $workerReady=$false
 $workerCheck='import pathlib,sqlite3,sys; c=sqlite3.connect(pathlib.Path(sys.argv[1]).as_uri()+"?mode=ro",uri=True); t=c.execute("SELECT MAX(updated) FROM companion_connector_activity WHERE name=?",("local-worker-tick",)).fetchone()[0] or 0; sys.exit(0 if t>=int(sys.argv[2]) else 1)'
 for($attempt=0;$attempt -lt 60;$attempt++){
  & $backup.pythonExe -c $workerCheck 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite' $startedTicks
  if($LASTEXITCODE -eq 0){$workerReady=$true;break};Start-Sleep -Milliseconds 500
 }
 if(-not $workerReady){throw 'The restarted local workers did not emit a new heartbeat.'}
 $stage='photo-readiness'
 if($configuration.Companion.ScreenshotLibraryEnabled -or $configuration.Companion.ScreenshotPilot){
  $photosReady=$false
  $photoCheck='import pathlib,sqlite3,sys; c=sqlite3.connect(pathlib.Path(sys.argv[1]).as_uri()+"?mode=ro",uri=True); r=c.execute("SELECT value,updated FROM companion_connector_activity WHERE name=?",("local-photo-error",)).fetchone(); sys.exit(0 if r and r[0]=="" and r[1]>=int(sys.argv[2]) else 1)'
  for($attempt=0;$attempt -lt 60;$attempt++){
   & $backup.pythonExe -c $photoCheck 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite' $startedTicks
   if($LASTEXITCODE -eq 0){$photosReady=$true;break};Start-Sleep -Milliseconds 500
  }
  if(-not $photosReady){throw 'The restarted photo inventory did not complete a healthy background check.'}
 }
 $stage='automatic-policy'
 $automaticAfter=& $backup.pythonExe -c $policyQuery 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite'
 if($LASTEXITCODE -ne 0){throw 'Could not verify the preserved automatic-update policy.'}
 $automaticAfter=$automaticAfter | ConvertFrom-Json
 if($automaticBefore -ne $automaticAfter){throw 'The automatic-update policy changed during the update; the prior PC service will be restored.'}
 $running=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
 $stage='map-service-access'
 $mapAccess=& $updatePlan.MapAccessScript -DatabasePath (Join-Path $configuration.Companion.DataRoot 'companion.sqlite') -StartedTicks $startedTicks -PythonExe $backup.pythonExe -MinimumFiles $mapFiles.Count -ExpectedIdentity $running.StartName
 foreach($original in $updatePlan.MapImages){if((Get-FileHash -LiteralPath $original.Path -Algorithm SHA256).Hash -ne $original.Sha256){throw 'A private map original changed during the update.'}}
 @{Ready=$true;FreshWorkerHeartbeat=$workerReady;PhotoBackgroundReady=($photosReady -or -not $configuration.Companion.ScreenshotLibraryEnabled);AutomaticEnabled=$automaticAfter;PausePolicyPreserved=$true;MapServiceAccess=$mapAccess;AuthenticatedPhoneAcceptance=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $updatePlan.EvidenceRoot 'maps-update-readiness.json')
 @{Passed=$true;Version=$toVersion;UpdatedUtc=[DateTime]::UtcNow.ToString('o');OriginalsChanged=$false;Settings=$settings;Executable=$newExe} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $updatePlan.EvidenceRoot 'maps-update-result.json')
 Write-Host "PASS: Companion $toVersion is running. Refresh the app."
}catch{
 $updateFailure=$_
 @{Stage=$stage;Utc=[DateTime]::UtcNow.ToString('o');Error=$_.Exception.Message;Details=$_.Exception.ToString();Script=$_.InvocationInfo.ScriptName;Line=$_.InvocationInfo.ScriptLineNumber} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $updatePlan.EvidenceRoot 'maps-update-failure.json')
 if($switched){
  Stop-Service PixelVaultCompanion -ErrorAction SilentlyContinue
  (Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
  [IO.File]::WriteAllText($settings,$previous)
  Invoke-UpdateSc @('config','PixelVaultCompanion','binPath=',$expected)
  $firewall | Get-NetFirewallApplicationFilter | Set-NetFirewallApplicationFilter -Program $oldExe
  foreach($entry in $outboundState){$destination=if($entry.Reader){$oldReader}else{$oldExe};$entry.Filter | Set-NetFirewallApplicationFilter -Program $destination}
  Start-Service PixelVaultCompanion
  $rollbackReady=$false;$rollbackHealth=$null
  for($attempt=0;$attempt -lt 60;$attempt++){
   try{$rollbackHealth=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 2;if($rollbackHealth.status -eq 'ok' -and $rollbackHealth.version -eq $fromVersion){$rollbackReady=$true;break}}catch{}
   Start-Sleep -Milliseconds 500
  }
  @{RestoredVersion=$rollbackHealth.version;Healthy=$rollbackReady;Utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $updatePlan.EvidenceRoot 'maps-update-rollback.json')
  if(-not $rollbackReady){throw 'The update failed and the restored PC service needs attention. See maps-update-rollback.json.'}
 }
 throw $updateFailure
}finally{if($backupEnabled){Enable-ScheduledTask -TaskName 'PixelVault App Data Backup' | Out-Null}}
