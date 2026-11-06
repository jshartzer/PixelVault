#Requires -Version 7.0
#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
$updatePlan=Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
if($updatePlan.Purpose -ne 'pc-companion-startup-performance-0.40.0'){throw 'Unknown update plan.'}
foreach($entry in $updatePlan.Files){if((Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ne $entry.Sha256){throw "A reviewed update file changed: $($entry.Path)"}}
$settings='C:\Codex\Companion Production\service.json'
if((Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash -ne $updatePlan.SettingsSha256){throw 'The live settings changed; prepare a fresh update plan.'}
$previous=Get-Content -LiteralPath $settings -Raw
$configuration=$previous | ConvertFrom-Json
if($configuration.Companion.ApplicationDataRoot -ne 'C:\Codex\PixelVaultData' -or $configuration.Companion.DataRoot -ne 'C:\Codex\PixelVaultCompanionLocalData'){throw 'Unexpected production roots.'}
$service=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
$oldExe='C:\Codex\Companion Production\app-0.39.3\PixelVault.Companion.exe'
$newExe='C:\Codex\Companion Production\app-0.40.0\PixelVault.Companion.exe'
$expected='"'+$oldExe+'" --installation-config "'+$settings+'"'
if($service.PathName -ne $expected -or $service.StartName -ne 'NT SERVICE\PixelVaultCompanion' -or $service.State -ne 'Running'){throw 'The current production service differs from the reviewed update.'}
$firewall=Get-NetFirewallRule -DisplayName 'PixelVault Companion PC service' -ErrorAction Stop
if(@($firewall).Count -ne 1 -or ($firewall | Get-NetFirewallApplicationFilter).Program -ne $oldExe){throw 'The reviewed inbound rule changed.'}
$oldReader='C:\Codex\Companion Production\app-0.39.3\readers\PixelVault.exe'
$newReader='C:\Codex\Companion Production\app-0.40.0\readers\PixelVault.exe'
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
$backupTask=Get-ScheduledTask -TaskName 'PixelVault App Data Backup'
if($backupTask.State -eq 'Running'){throw 'The app backup is running. Let it finish before applying this update.'}
$backupEnabled=$backupTask.State -ne 'Disabled';$switched=$false
try{
 if($backupEnabled){Disable-ScheduledTask -TaskName 'PixelVault App Data Backup' | Out-Null}
 $backup=Get-Content -LiteralPath $updatePlan.BackupPlan -Raw | ConvertFrom-Json
 if((Get-FileHash -LiteralPath $backup.engine -Algorithm SHA256).Hash -ne $backup.engineSha256){throw 'The backup engine changed.'}
 & $backup.pythonExe $backup.engine snapshot $updatePlan.BackupPlan --label before-startup-performance-update | Out-File -LiteralPath (Join-Path $updatePlan.EvidenceRoot 'before-startup-performance-update-backup.log')
 if($LASTEXITCODE -ne 0){throw 'The pre-update app backup failed; the running service was retained.'}
 $policyQuery='import pathlib,sqlite3,json,sys; c=sqlite3.connect(pathlib.Path(sys.argv[1]).as_uri()+"?mode=ro",uri=True); r=c.execute("SELECT value FROM companion_connector_activity WHERE name=?",("settings",)).fetchone(); assert r,"Automatic settings unavailable"; print(json.dumps(bool(json.loads(r[0]).get("AutomaticEnabled",False))))'
 $automaticBefore=& $backup.pythonExe -c $policyQuery 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite'
 if($LASTEXITCODE -ne 0){throw 'Could not read the current automatic-update policy.'}
 $automaticBefore=$automaticBefore | ConvertFrom-Json
 Stop-Service -Name PixelVaultCompanion -ErrorAction Stop
 (Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
 $switched=$true
 $configuration.Companion.PhotoWorkerPath='C:\Codex\Companion Production\app-0.40.0\media-worker\PixelVault.Companion.Media.dll'
 $configuration.Companion.ReaderWorkerPath='C:\Codex\Companion Production\app-0.40.0\readers\PixelVault.exe'
 $configuration | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settings
 Invoke-UpdateSc @('config','PixelVaultCompanion','binPath=',('"'+$newExe+'" --installation-config "'+$settings+'"'))
 $firewall | Get-NetFirewallApplicationFilter | Set-NetFirewallApplicationFilter -Program $newExe
 foreach($entry in $outboundState){$destination=if($entry.Reader){$newReader}else{$newExe};$entry.Filter | Set-NetFirewallApplicationFilter -Program $destination}
 $startedTicks=[DateTime]::UtcNow.Ticks
 Start-Service PixelVaultCompanion
 $ready=$false
 for($attempt=0;$attempt -lt 60;$attempt++){
  try{$health=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 2;if($health.status -eq 'ok' -and $health.version -eq '0.40.0'){$ready=$true;break}}catch{}
  Start-Sleep -Milliseconds 500
 }
 if(-not $ready){throw 'The updated service did not pass HTTP health.'}
 $workerReady=$false
 $workerCheck='import pathlib,sqlite3,sys; c=sqlite3.connect(pathlib.Path(sys.argv[1]).as_uri()+"?mode=ro",uri=True); t=c.execute("SELECT MAX(updated) FROM companion_connector_activity WHERE name=?",("local-worker-tick",)).fetchone()[0] or 0; sys.exit(0 if t>=int(sys.argv[2]) else 1)'
 for($attempt=0;$attempt -lt 60;$attempt++){
  & $backup.pythonExe -c $workerCheck 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite' $startedTicks
  if($LASTEXITCODE -eq 0){$workerReady=$true;break};Start-Sleep -Milliseconds 500
 }
 if(-not $workerReady){throw 'The restarted local workers did not emit a new heartbeat.'}
 if($configuration.Companion.ScreenshotLibraryEnabled -or $configuration.Companion.ScreenshotPilot){
  $photosReady=$false
  $photoCheck='import pathlib,sqlite3,sys; c=sqlite3.connect(pathlib.Path(sys.argv[1]).as_uri()+"?mode=ro",uri=True); r=c.execute("SELECT value,updated FROM companion_connector_activity WHERE name=?",("local-photo-error",)).fetchone(); sys.exit(0 if r and r[0]=="" and r[1]>=int(sys.argv[2]) else 1)'
  for($attempt=0;$attempt -lt 60;$attempt++){
   & $backup.pythonExe -c $photoCheck 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite' $startedTicks
   if($LASTEXITCODE -eq 0){$photosReady=$true;break};Start-Sleep -Milliseconds 500
  }
  if(-not $photosReady){throw 'The restarted photo inventory did not complete a healthy background check.'}
 }
 $automaticAfter=& $backup.pythonExe -c $policyQuery 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite'
 if($LASTEXITCODE -ne 0){throw 'Could not verify the preserved automatic-update policy.'}
 $automaticAfter=$automaticAfter | ConvertFrom-Json
 if($automaticBefore -ne $automaticAfter){throw 'The automatic-update policy changed during the update; the prior PC service will be restored.'}
 @{Ready=$true;FreshWorkerHeartbeat=$workerReady;PhotoBackgroundReady=($photosReady -or -not $configuration.Companion.ScreenshotLibraryEnabled);AutomaticEnabled=$automaticAfter;PausePolicyPreserved=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $updatePlan.EvidenceRoot 'startup-performance-update-readiness.json')
 @{Passed=$true;Version='0.40.0';UpdatedUtc=[DateTime]::UtcNow.ToString('o');OriginalsChanged=$false;Settings=$settings;Executable=$newExe} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $updatePlan.EvidenceRoot 'startup-performance-update-result.json')
 Write-Host 'PASS: Companion 0.40.0 is running with startup performance improvements. Refresh the phone app.'
}catch{
 if($switched){
  Stop-Service PixelVaultCompanion -ErrorAction SilentlyContinue
  (Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
  [IO.File]::WriteAllText($settings,$previous)
  Invoke-UpdateSc @('config','PixelVaultCompanion','binPath=',$expected)
  $firewall | Get-NetFirewallApplicationFilter | Set-NetFirewallApplicationFilter -Program $oldExe
  foreach($entry in $outboundState){$destination=if($entry.Reader){$oldReader}else{$oldExe};$entry.Filter | Set-NetFirewallApplicationFilter -Program $destination}
  Start-Service PixelVaultCompanion
 }
 throw
}finally{if($backupEnabled){Enable-ScheduledTask -TaskName 'PixelVault App Data Backup' | Out-Null}}
