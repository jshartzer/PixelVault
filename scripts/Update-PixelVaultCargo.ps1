#Requires -Version 7.0
#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
$plan=Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
if($plan.Purpose -ne 'century-cargo-0.43.0' -or $plan.FromVersion -ne '0.42.0' -or $plan.ToVersion -ne '0.43.0'){throw 'Unknown cargo update plan.'}
foreach($file in $plan.Files){if((Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash -ne $file.Sha256){throw "A reviewed update file changed: $($file.Path)"}}
$proof=Get-Content -LiteralPath $plan.RehearsalResult -Raw | ConvertFrom-Json
if(-not $proof.Passed -or $proof.Target -ne 60000 -or -not $proof.AllOtherTrackerRowsUnchanged){throw 'The real-data cargo rehearsal is required.'}
$accessProof=Get-Content -LiteralPath $plan.ReaderAccessResult -Raw | ConvertFrom-Json
if(-not $accessProof.Passed -or -not $accessProof.OuterCompanionProtocol -or -not $accessProof.NativeSqliteDependencyPresent -or $accessProof.ProcessExitCode -ne 0){throw 'The packaged Companion reader access check is required.'}
$settings='C:\Codex\Companion Production\service.json'
if((Get-FileHash -LiteralPath $settings).Hash -ne $plan.SettingsSha256){throw 'The service settings changed; prepare a fresh update plan.'}
$previous=[IO.File]::ReadAllText($settings);$configuration=$previous | ConvertFrom-Json
if($configuration.Companion.ApplicationDataRoot -ne 'C:\Codex\PixelVaultData' -or $configuration.Companion.DataRoot -ne 'C:\Codex\PixelVaultCompanionLocalData'){throw 'Unexpected production roots.'}
$oldExe='C:\Codex\Companion Production\app-0.42.0\PixelVault.Companion.exe'
$newExe='C:\Codex\Companion Production\app-0.43.0\PixelVault.Companion.exe'
$oldReader='C:\Codex\Companion Production\app-0.42.0\readers\PixelVault.exe'
$newReader='C:\Codex\Companion Production\app-0.43.0\readers\PixelVault.exe'
$oldCommand='"'+$oldExe+'" --installation-config "'+$settings+'"'
$service=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
if($service.PathName -ne $oldCommand -or $service.StartName -ne 'NT SERVICE\PixelVaultCompanion' -or $service.State -ne 'Running'){throw 'The reviewed service changed.'}
$firewall=Get-NetFirewallRule -DisplayName 'PixelVault Companion PC service' -ErrorAction Stop
if(@($firewall).Count -ne 1 -or ($firewall | Get-NetFirewallApplicationFilter).Program -ne $oldExe){throw 'The reviewed inbound rule changed.'}
$outbound=@(Get-NetFirewallRule -DisplayName 'PixelVault retired NAS Companion endpoint' | Get-NetFirewallApplicationFilter | Where-Object Program -in @($oldExe,$oldReader))
if($outbound.Count -ne 2){throw 'The retired-NAS endpoint guards changed.'}
$outboundState=@($outbound | ForEach-Object {@{Filter=$_;Reader=$_.Program -eq $oldReader}})
if(@($outboundState | Where-Object Reader).Count -ne 1){throw 'The source-reader endpoint guard changed.'}
function Invoke-CargoSc([string[]]$Arguments){
 $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $env:WINDIR 'System32\sc.exe'));$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 foreach($argument in $Arguments){$start.ArgumentList.Add($argument)}
 $process=[Diagnostics.Process]::Start($start)
 try{$output=$process.StandardOutput.ReadToEndAsync();$errors=$process.StandardError.ReadToEndAsync();$process.WaitForExit();$message=$output.GetAwaiter().GetResult()+$errors.GetAwaiter().GetResult();if($process.ExitCode -ne 0){throw $message}}finally{$process.Dispose()}
}
$sid=([Security.Principal.NTAccount]'NT SERVICE\PixelVaultCompanion').Translate([Security.Principal.SecurityIdentifier]).Value
& icacls.exe (Split-Path $newExe -Parent) /grant "*$($sid):(OI)(CI)RX" | Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not grant service access to the new application.'}
$backupTask=Get-ScheduledTask -TaskName 'PixelVault App Data Backup'
if($backupTask.State -eq 'Running'){throw 'The app backup is running. Let it finish before applying this update.'}
$backupEnabled=$backupTask.State -ne 'Disabled';$switched=$false;$stage='backup'
try{
 if($backupEnabled){Disable-ScheduledTask -TaskName 'PixelVault App Data Backup' | Out-Null}
 $backup=Get-Content -LiteralPath $plan.BackupPlan -Raw | ConvertFrom-Json
 if((Get-FileHash -LiteralPath $backup.engine).Hash -ne $backup.engineSha256){throw 'The verified backup engine changed.'}
 & $backup.pythonExe $backup.engine snapshot $plan.BackupPlan --label before-century-cargo | Out-File -LiteralPath (Join-Path $plan.EvidenceRoot 'before-update-backup.log')
 if($LASTEXITCODE -ne 0){throw 'The pre-update app backup failed; the running service was retained.'}
 $policyQuery='import pathlib,sqlite3,json,sys; c=sqlite3.connect(pathlib.Path(sys.argv[1]).as_uri()+"?mode=ro",uri=True); r=c.execute("SELECT value FROM companion_connector_activity WHERE name=?",("settings",)).fetchone(); assert r,"Automatic settings unavailable"; print(json.dumps(bool(json.loads(r[0]).get("AutomaticEnabled",False))))'
 $automaticBefore=& $backup.pythonExe -c $policyQuery 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite'
 if($LASTEXITCODE -ne 0){throw 'Could not read the automatic-update policy.'};$automaticBefore=$automaticBefore | ConvertFrom-Json
 $stage='service-stop';$switched=$true
 Stop-Service PixelVaultCompanion;(Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
 $stage='cargo-connection'
 & $plan.SetupExe 'C:\Codex\PixelVaultData' 'C:\Codex\PixelVaultCompanionLocalData\workspace.json' $newReader (Join-Path $plan.EvidenceRoot 'live-connection.json') | Out-File -LiteralPath (Join-Path $plan.EvidenceRoot 'live-connection.log')
 if($LASTEXITCODE -ne 0){throw 'The new cargo-weight source needs review. Existing tracker rows were not replaced.'}
 $stage='service-switch'
 $configuration.Companion.ReaderWorkerPath=$newReader
 $configuration.Companion.PhotoWorkerPath='C:\Codex\Companion Production\app-0.43.0\media-worker\PixelVault.Companion.Media.dll'
 $configuration | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settings
 Invoke-CargoSc @('config','PixelVaultCompanion','binPath=',('"'+$newExe+'" --installation-config "'+$settings+'"'))
 $firewall | Get-NetFirewallApplicationFilter | Set-NetFirewallApplicationFilter -Program $newExe
 foreach($entry in $outboundState){$entry.Filter | Set-NetFirewallApplicationFilter -Program $(if($entry.Reader){$newReader}else{$newExe})}
 $startedTicks=[DateTime]::UtcNow.Ticks;Start-Service PixelVaultCompanion
 $stage='http-readiness';$ready=$false
 for($attempt=0;$attempt -lt 60;$attempt++){
  try{$health=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 2;if($health.status -eq 'ok' -and $health.version -eq '0.43.0'){$ready=$true;break}}catch{}
  if((Get-Service PixelVaultCompanion).Status -eq 'Stopped'){throw 'The updated service stopped before HTTP readiness; restoring the prior service.'}
  Start-Sleep -Milliseconds 500
 }
 if(-not $ready){throw 'The updated service did not pass HTTP health.'}
 $stage='worker-readiness';$workerReady=$false
 $workerCheck='import pathlib,sqlite3,sys; c=sqlite3.connect(pathlib.Path(sys.argv[1]).as_uri()+"?mode=ro",uri=True); t=c.execute("SELECT MAX(updated) FROM companion_connector_activity WHERE name=?",("local-worker-tick",)).fetchone()[0] or 0; sys.exit(0 if t>=int(sys.argv[2]) else 1)'
 for($attempt=0;$attempt -lt 60;$attempt++){
  & $backup.pythonExe -c $workerCheck 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite' $startedTicks
  if($LASTEXITCODE -eq 0){$workerReady=$true;break};Start-Sleep -Milliseconds 500
 }
 if(-not $workerReady){throw 'The restarted local workers did not emit a new heartbeat.'}
 $connection=Get-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'live-connection.json') -Raw | ConvertFrom-Json
 $automaticVerified=$false
 if($automaticBefore -and -not $connection.Paused -and $connection.AutomaticMode -in @('SaveChanges','Periodic')){
  $stage='cargo-automatic-readiness'
  $automaticCheck='import pathlib,sqlite3,json,sys; c=sqlite3.connect(pathlib.Path(sys.argv[1]).as_uri()+"?mode=ro",uri=True); a=c.execute("SELECT state FROM tracker_automation WHERE tracker_id=?",("death-stranding-dc.the-weight-of-a-nation",)).fetchone(); s=c.execute("SELECT status FROM tracker_source WHERE tracker_id=?",("death-stranding-dc.the-weight-of-a-nation",)).fetchone(); a=json.loads(a[0]) if a else {}; s=json.loads(s[0]) if s else {}; sys.exit(0 if a.get("LastCheckUtc",0)>=int(sys.argv[2]) and a.get("Failures",0)==0 and not a.get("ReviewPending",False) and s.get("Outcome") in (3,4) else 1)'
  for($attempt=0;$attempt -lt 180;$attempt++){
   & $backup.pythonExe -c $automaticCheck 'C:\Codex\PixelVaultData\achievement-tracking\pixelvault-tracking.sqlite' $startedTicks
   if($LASTEXITCODE -eq 0){$automaticVerified=$true;break};Start-Sleep -Milliseconds 500
  }
  if(-not $automaticVerified){throw 'The new tracker did not complete a fresh automatic check. Restoring the prior service.'}
 }
 $automaticAfter=& $backup.pythonExe -c $policyQuery 'C:\Codex\PixelVaultCompanionLocalData\companion.sqlite'
 if($LASTEXITCODE -ne 0){throw 'Could not verify automatic settings.'};$automaticAfter=$automaticAfter | ConvertFrom-Json
 if($automaticBefore -ne $automaticAfter){throw 'The automatic-update policy changed; restoring the prior service.'}
 @{Passed=$true;Version='0.43.0';NativeVersion='0.116.000';Current=$connection.Current;Target=60000;UnrelatedTrackerRowsUnchanged=$connection.AllOtherTrackerRowsUnchanged;AutomaticPolicyPreserved=$true;FreshWorkerHeartbeat=$workerReady;NewTrackerAutomaticCheck=$automaticVerified;UpdatedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'update-result.json')
 Write-Host 'PASS: Century cargo and its automatic tracker are installed.'
}catch{
 $failure=$_
 @{Stage=$stage;Error=$_.Exception.Message;Details=$_.Exception.ToString();Line=$_.InvocationInfo.ScriptLineNumber} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'update-failure.json')
 if($switched){
  Stop-Service PixelVaultCompanion -ErrorAction SilentlyContinue;(Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
  [IO.File]::WriteAllText($settings,$previous);Invoke-CargoSc @('config','PixelVaultCompanion','binPath=',$oldCommand)
  $firewall | Get-NetFirewallApplicationFilter | Set-NetFirewallApplicationFilter -Program $oldExe
  foreach($entry in $outboundState){$entry.Filter | Set-NetFirewallApplicationFilter -Program $(if($entry.Reader){$oldReader}else{$oldExe})}
  Start-Service PixelVaultCompanion
 }
 throw $failure
}finally{if($backupEnabled){Enable-ScheduledTask -TaskName 'PixelVault App Data Backup' | Out-Null}}
