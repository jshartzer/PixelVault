#Requires -Version 7.0
#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
$cutoverPlanFile=[IO.Path]::GetFullPath($PlanPath)
$cutoverPlan=Get-Content -LiteralPath $cutoverPlanFile -Raw | ConvertFrom-Json
if($cutoverPlan.Purpose -ne 'pc-companion-cutover' -or $cutoverPlan.ApplicationRoot -ne 'C:\Codex\PixelVaultData' -or $cutoverPlan.ServiceName -ne 'PixelVaultCompanion'){throw 'This command accepts only the reviewed PC cutover plan.'}
$cutoverEvidence=$cutoverPlan.EvidenceRoot
$cutoverJournalFile=Join-Path $cutoverEvidence 'cutover-journal.json'
if(Test-Path -LiteralPath $cutoverJournalFile){throw 'A cutover journal already exists. Review its checkpoint before retrying; no writer was stopped.'}
foreach($cutoverEntry in $cutoverPlan.Files){
 if((Get-FileHash -LiteralPath $cutoverEntry.Path -Algorithm SHA256).Hash -ne $cutoverEntry.Sha256){throw "A reviewed deployment input changed: $($cutoverEntry.Path)"}
}
if(Get-Service -Name PixelVaultCompanion -ErrorAction SilentlyContinue){throw 'A production PC service already exists; its state was retained.'}
if(Test-Path -LiteralPath $cutoverPlan.StateRoot){throw 'The new production state root already exists. The historical PC workspace will not be reused.'}
if(-not (Get-NetIPAddress -InterfaceAlias 'Ethernet 4' -AddressFamily IPv4 -ErrorAction Stop | Where-Object IPAddress -eq '192.168.1.69')){throw 'The reviewed PC address/interface changed.'}
if(Get-NetTCPConnection -LocalPort 8787 -State Listen -ErrorAction SilentlyContinue){throw 'PC port 8787 is already in use.'}
$cutoverCurrentLink=Get-Item -LiteralPath 'C:\Codex\dist\PixelVault-current' -Force -ErrorAction Stop
if($cutoverCurrentLink.LinkType -ne 'Junction' -or [string]$cutoverCurrentLink.Target -ne $cutoverPlan.PreviousNativeRoot){throw 'The installed Native build changed since preparation.'}
foreach($scope in @('Process','User','Machine')){
 $override=[Environment]::GetEnvironmentVariable('PIXELVAULT_DATA_ROOT',$scope)
 if($override -and [IO.Path]::GetFullPath($override) -ne $cutoverPlan.ApplicationRoot){throw 'A Native data-root override differs from the canonical store.'}
}
function Invoke-CutoverPython {
 param([string]$Script,[string[]]$Arguments,[string]$Log)
 $loader='import runpy,sys; sys.path.insert(0,sys.argv[1]); sys.argv.pop(1); script=sys.argv.pop(1); sys.argv[0]=script; runpy.run_path(script,run_name="__main__")'
 if($Log){& $cutoverPlan.PythonExe -c $loader 'C:\Codex\scripts\CompanionMigrationAudit' $Script @Arguments | Set-Content -LiteralPath $Log}
 else{& $cutoverPlan.PythonExe -c $loader 'C:\Codex\scripts\CompanionMigrationAudit' $Script @Arguments}
 if($LASTEXITCODE -ne 0){throw "Cutover helper failed: $([IO.Path]::GetFileName($Script)). Existing evidence was retained."}
}
function Invoke-CutoverNas([string]$Command){
 $output=& ssh.exe -o BatchMode=yes -o ConnectTimeout=10 -o StrictHostKeyChecking=yes admin1@192.168.1.99 $Command
 if($LASTEXITCODE -ne 0){throw 'A NAS cutover command failed.'}
 return $output
}
function Invoke-CutoverSc([string[]]$Arguments){
 $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $env:WINDIR 'System32\sc.exe'))
 $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
 foreach($argument in $Arguments){$start.ArgumentList.Add($argument)}
 $process=[Diagnostics.Process]::Start($start)
 try{$process.WaitForExit();if($process.ExitCode -ne 0){throw "Windows service configuration failed, exit $($process.ExitCode)."}}finally{$process.Dispose()}
}
function Save-CutoverJournal([string]$Phase){
 $script:cutoverJournal.Phase=$Phase
 $script:cutoverJournal.UpdatedUtc=[DateTime]::UtcNow.ToString('o')
 $script:cutoverJournal | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $cutoverJournalFile
}
$cutoverJournal=[ordered]@{Purpose='pc-companion-cutover';Phase='prepared';UpdatedUtc=[DateTime]::UtcNow.ToString('o');FrozenEvidence='';NasStopped=$false;CanonicalModified=$false;PcAuthority=$false;BackupTaskWasEnabled=$false;ConnectorTaskWasEnabled=$false;StartupRetired=$false}
$cutoverBackupTask=Get-ScheduledTask -TaskName 'PixelVault App Data Backup' -ErrorAction Stop
$cutoverConnectorTask=Get-ScheduledTask -TaskName 'PixelVault Companion Connector Recovery' -ErrorAction Stop
$cutoverJournal.BackupTaskWasEnabled=$cutoverBackupTask.State -ne 'Disabled'
$cutoverJournal.ConnectorTaskWasEnabled=$cutoverConnectorTask.State -ne 'Disabled'
$cutoverStartup=Join-Path ([Environment]::GetFolderPath('Startup')) 'PixelVault Companion Connector.lnk'
$cutoverStartupSaved=Join-Path $cutoverEvidence 'PixelVault Companion Connector.startup.lnk'
$cutoverNasImage=(Invoke-CutoverNas '/share/CACHEDEV1_DATA/.qpkg/container-station/bin/docker inspect --format "{{.Image}}" pixelvault-companion' | Out-String).Trim()
if($cutoverNasImage -ne $cutoverPlan.NasImageId){throw 'The NAS installation changed since the reviewed capture.'}
$cutoverNasRunning=(Invoke-CutoverNas '/share/CACHEDEV1_DATA/.qpkg/container-station/bin/docker inspect --format "{{.State.Running}}" pixelvault-companion' | Out-String).Trim()
if($cutoverNasRunning -ne 'true'){throw 'The NAS authority is not running as expected; review its state before cutover.'}
$cutoverNasPolicy=(Invoke-CutoverNas '/share/CACHEDEV1_DATA/.qpkg/container-station/bin/docker inspect --format "{{.HostConfig.RestartPolicy.Name}}" pixelvault-companion' | Out-String).Trim()
if($cutoverNasPolicy -notin @('always','unless-stopped','no','on-failure')){throw 'Unexpected NAS restart policy.'}
$cutoverJournal.NasRestartPolicy=$cutoverNasPolicy
Save-CutoverJournal 'prepared'
try {
 Write-Host 'Closing the rehearsal and retiring the old connector startup paths...'
 if($cutoverConnectorTask.State -eq 'Running'){Stop-ScheduledTask -TaskName 'PixelVault Companion Connector Recovery' -ErrorAction Stop}
 if($cutoverBackupTask.State -eq 'Running'){Stop-ScheduledTask -TaskName 'PixelVault App Data Backup' -ErrorAction Stop}
 Disable-ScheduledTask -TaskName 'PixelVault Companion Connector Recovery' -ErrorAction Stop | Out-Null
 Disable-ScheduledTask -TaskName 'PixelVault App Data Backup' -ErrorAction Stop | Out-Null
 if(Test-Path -LiteralPath $cutoverStartup){
  if((Get-FileHash -LiteralPath $cutoverStartup -Algorithm SHA256).Hash -ne $cutoverPlan.StartupSha256){throw 'The reviewed connector Startup shortcut changed.'}
  Move-Item -LiteralPath $cutoverStartup -Destination $cutoverStartupSaved
  $cutoverJournal.StartupRetired=$true
 }
 $rehearsal=Get-Service PixelVaultCompanionPhase4 -ErrorAction SilentlyContinue
 if($rehearsal -and $rehearsal.Status -ne 'Stopped'){Stop-Service PixelVaultCompanionPhase4 -ErrorAction Stop; (Get-Service PixelVaultCompanionPhase4).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
 foreach($native in Get-Process -Name PixelVault -ErrorAction SilentlyContinue){
  if(-not $native.CloseMainWindow()){throw 'Close Native before running cutover; an unexpected headless process is still running.'}
  if(-not $native.WaitForExit(30000)){throw 'Native is still open; finish its save/close prompt before cutover.'}
 }
 foreach($connector in Get-CimInstance Win32_Process -Filter "Name='PixelVault.Companion.Connector.exe'"){
  if($connector.ExecutablePath -ne $cutoverPlan.ConnectorExecutable){throw 'An unreviewed connector process is running.'}
  Stop-Process -Id $connector.ProcessId -Force -ErrorAction Stop
 }
 Save-CutoverJournal 'local-writers-stopped'
 Write-Host 'Stopping only the NAS Companion container and capturing final app-only recovery data...'
 $null=Invoke-CutoverNas '/share/CACHEDEV1_DATA/.qpkg/container-station/bin/docker stop --time 30 pixelvault-companion'
 $cutoverJournal.NasStopped=$true
 $null=Invoke-CutoverNas '/share/CACHEDEV1_DATA/.qpkg/container-station/bin/docker update --restart=no pixelvault-companion'
 $cutoverFrozen=Join-Path $cutoverEvidence 'frozen-capture'
 New-Item -ItemType Directory -Path $cutoverFrozen | Out-Null
 $cutoverJournal.FrozenEvidence=$cutoverFrozen
 Save-CutoverJournal 'writers-stopped'
 & scp.exe -B -o StrictHostKeyChecking=yes $cutoverPlan.SnapshotHelperTar 'admin1@192.168.1.99:/share/CACHEDEV1_DATA/Container/pixelvault-companion/build/frozen-capture-helper.tar'
 if($LASTEXITCODE -ne 0){throw 'Final snapshot helper upload failed.'}
 Invoke-CutoverPython -Script 'C:\Codex\scripts\CompanionMigrationAudit\app_backup.py' -Arguments @('snapshot',$cutoverPlan.BackupPlan,'--label','before-live-cutover','--no-remote') -Log (Join-Path $cutoverEvidence 'before-cutover-backup.log')
 Invoke-CutoverPython -Script 'C:\Codex\scripts\CompanionMigrationAudit\capture.py' -Arguments @($cutoverFrozen,'--include-auth-keys') -Log (Join-Path $cutoverEvidence 'frozen-capture.log')
 Invoke-CutoverPython -Script 'C:\Codex\scripts\CompanionMigrationAudit\compare.py' -Arguments @($cutoverFrozen,'--output',(Join-Path $cutoverFrozen 'comparison.json')) -Log (Join-Path $cutoverEvidence 'frozen-comparison.private.json')
 Invoke-CutoverPython -Script 'C:\Codex\scripts\CompanionMigrationAudit\cutover.py' -Arguments @('prepare',$cutoverFrozen,'--companion-dll',$cutoverPlan.CompanionDll) -Log (Join-Path $cutoverEvidence 'final-candidate.log')
 Save-CutoverJournal 'frozen-and-captured'
 Write-Host 'Reconciliation passed; updating only the canonical records and new production state...'
 $cutoverJournal.CanonicalModified=$true
 Save-CutoverJournal 'frozen-and-captured'
 Invoke-CutoverPython -Script 'C:\Codex\scripts\CompanionMigrationAudit\cutover.py' -Arguments @('apply-frozen',$cutoverFrozen,'--plan',$cutoverPlanFile) -Log (Join-Path $cutoverEvidence 'canonical-apply.log')
 $cutoverJournal=Get-Content -LiteralPath $cutoverJournalFile -Raw | ConvertFrom-Json -AsHashtable
 $command='"'+$cutoverPlan.Executable+'" --installation-config "'+$cutoverPlan.Settings+'"'
 Invoke-CutoverSc @('create','PixelVaultCompanion','binPath=',$command,'start=','delayed-auto','obj=','NT SERVICE\PixelVaultCompanion')
 Invoke-CutoverSc @('sidtype','PixelVaultCompanion','unrestricted')
 $sid=([Security.Principal.NTAccount]'NT SERVICE\PixelVaultCompanion').Translate([Security.Principal.SecurityIdentifier]).Value
 foreach($directory in @($cutoverPlan.PackageRoot)){
  & icacls.exe $directory /grant "*$($sid):(OI)(CI)RX" | Out-Null
  if($LASTEXITCODE -ne 0){throw 'Could not grant production package access.'}
 }
 & icacls.exe $cutoverPlan.Settings /grant "*$($sid):R" | Out-Null
 if($LASTEXITCODE -ne 0){throw 'Could not grant explicit production configuration access.'}
 New-Item -ItemType Directory -Path $cutoverPlan.CacheRoot -Force | Out-Null
 foreach($directory in @($cutoverPlan.StateRoot,$cutoverPlan.CacheRoot)){
  $ownerSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
  & icacls.exe $directory /inheritance:r /grant:r "*$($ownerSid):(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' "*$($sid):(OI)(CI)M" | Out-Null
  if($LASTEXITCODE -ne 0){throw 'Could not protect production state/cache permissions.'}
 }
 & icacls.exe $cutoverPlan.ApplicationRoot /grant "*$($sid):(OI)(CI)M" | Out-Null
 if($LASTEXITCODE -ne 0){throw 'Could not grant canonical application access.'}
 foreach($directory in $cutoverPlan.ReadOnlySaveRoots){
  & icacls.exe $directory /grant "*$($sid):(OI)(CI)RX" | Out-Null
  if($LASTEXITCODE -ne 0){throw 'Could not grant approved save-source read access.'}
 }
 Invoke-CutoverSc @('failure','PixelVaultCompanion','reset=','86400','actions=','restart/5000/restart/15000/restart/60000')
 Invoke-CutoverSc @('failureflag','PixelVaultCompanion','1')
 New-NetFirewallRule -DisplayName 'PixelVault Companion PC service' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8787 -LocalAddress '192.168.1.69' -RemoteAddress '192.168.1.0/24' -InterfaceAlias 'Ethernet 4' -Profile Any -Program $cutoverPlan.Executable -Service PixelVaultCompanion -EdgeTraversalPolicy Block -ErrorAction Stop | Out-Null
 foreach($program in @($cutoverPlan.Executable,$cutoverPlan.ReaderExecutable,$cutoverPlan.NativeExecutable)){
  New-NetFirewallRule -DisplayName 'PixelVault retired NAS Companion endpoint' -Direction Outbound -Action Block -Protocol TCP -RemotePort 8787 -RemoteAddress '192.168.1.99' -Program $program -Profile Any -ErrorAction Stop | Out-Null
 }
 # Expand the already tested daily plan to the actual production owner/state/configuration.
 $backup=Get-Content -LiteralPath $cutoverPlan.BackupPlan -Raw | ConvertFrom-Json -AsHashtable
 $backup | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $cutoverEvidence 'backup-plan-before.json')
 $backup.roots.state=$cutoverPlan.StateRoot;$backup.roots.machine=(Split-Path $cutoverPlan.Settings -Parent)
 $backup.include+=@(@{root='state';path='companion.sqlite';kind='sqlite';required=$true;role='authentication-preferences-queue'},@{root='state';path='workspace.json';kind='file';required=$true;role='account-scope'},@{root='state';path='achievement-accounts.json';kind='file';required=$true;role='account-scope'},@{root='state';path='keys/*.xml';kind='glob';required=$true;role='protected-authentication-keys'},@{root='machine';path='service.json';kind='file';required=$true;role='machine-configuration'})
 $backup.scope='Canonical PC application data and actual production Companion owner/state/configuration.'
 $backup | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $cutoverPlan.BackupPlan
 Invoke-CutoverPython -Script 'C:\Codex\scripts\CompanionMigrationAudit\app_backup.py' -Arguments @('snapshot',$cutoverPlan.BackupPlan,'--label','before-pc-activation') -Log (Join-Path $cutoverEvidence 'production-owner-backup.log')
 # Publish only the reviewed Native destination and exact shared-data root.
 $shell=New-Object -ComObject WScript.Shell
 foreach($linkName in @('PixelVault.lnk','PixelVault Companion.lnk')){
  $linkPath=Join-Path 'C:\Codex' $linkName
  if(Test-Path -LiteralPath $linkPath){Copy-Item -LiteralPath $linkPath -Destination (Join-Path $cutoverEvidence ($linkName+'.before'))}
  $link=$shell.CreateShortcut($linkPath);$link.TargetPath=$cutoverPlan.NativeExecutable;$link.WorkingDirectory=(Split-Path $cutoverPlan.NativeExecutable -Parent);$link.Arguments=if($linkName -like '*Companion*'){'--companion'}else{''};$link.Save()
 }
 $current='C:\Codex\dist\PixelVault-current'
 if(Test-Path -LiteralPath $current){
  $old=Get-Item -LiteralPath $current -Force
  if($old.LinkType -ne 'Junction' -or -not ([IO.Path]::GetFullPath([string]$old.Target)).StartsWith('C:\Codex\dist\PixelVault-',[StringComparison]::OrdinalIgnoreCase)){throw 'The current-build junction needs review.'}
  Remove-Item -LiteralPath $current -Force
 }
 New-Item -ItemType Junction -Path $current -Target (Split-Path $cutoverPlan.NativeExecutable -Parent) | Out-Null
 $desktop=Join-Path $env:LOCALAPPDATA 'PixelVault\Companion'
 New-Item -ItemType Directory -Path $desktop -Force | Out-Null
 $saved=Join-Path $desktop 'companion-connection.json'
 if(Test-Path -LiteralPath $saved){Copy-Item -LiteralPath $saved -Destination (Join-Path $cutoverEvidence 'desktop-connection-before.json')}
 @{Server='http://127.0.0.1:8787/';AllowHttp=$true} | ConvertTo-Json | Set-Content -LiteralPath $saved
 $marker='C:\Codex\PixelVaultCompanionData\hosting.json'
 Copy-Item -LiteralPath $marker -Destination (Join-Path $cutoverEvidence 'hosting-before.json')
 @{version=1;mode='pc-service';url='http://127.0.0.1:8787/';migratedUtc=[DateTime]::UtcNow.ToString('o');stateRoot=$cutoverPlan.StateRoot;backup=$cutoverEvidence} | ConvertTo-Json | Set-Content -LiteralPath $marker
 $cutoverApplied=Get-Content -LiteralPath (Join-Path $cutoverFrozen 'canonical-apply-proof.json') -Raw | ConvertFrom-Json
 if($cutoverApplied.AutomaticEnabled){
  & $cutoverPlan.Executable --enable-local-automatic $cutoverPlan.Settings
  if($LASTEXITCODE -ne 0){throw 'Could not restore approved automatic work in production.'}
 }
 $cutoverJournal.PcAuthority=$true
 Save-CutoverJournal 'pc-authority-starting'
 Start-Service PixelVaultCompanion -ErrorAction Stop
 $ready=$false;$timer=[Diagnostics.Stopwatch]::StartNew()
 while($timer.Elapsed.TotalSeconds -lt 90){
  try{
   $health=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 3
   if($health.status -eq 'ok' -and $health.version -eq '0.39.0'){
    Invoke-CutoverPython -Script 'C:\Codex\scripts\CompanionMigrationAudit\cutover.py' -Arguments @('check-running',$cutoverFrozen,'--plan',$cutoverPlanFile) -Log (Join-Path $cutoverEvidence 'production-readiness.json')
    $ready=$true;break
   }
  }catch{}
  Start-Sleep -Seconds 1
 }
 if(-not $ready){throw 'Production PC health did not become ready. NAS remains stopped to prevent two writers.'}
 Set-Service -Name PixelVaultCompanionPhase4 -StartupType Disabled -ErrorAction Stop
 Get-NetFirewallRule -DisplayName 'PixelVault Companion Phase 4 rehearsal' -ErrorAction SilentlyContinue | Disable-NetFirewallRule -ErrorAction Stop
 Save-CutoverJournal 'pc-running-awaiting-phone-acceptance'
 Enable-ScheduledTask -TaskName 'PixelVault App Data Backup' -ErrorAction Stop | Out-Null
 Start-ScheduledTask -TaskName 'PixelVault App Data Backup' -ErrorAction Stop
 Write-Host 'PC Companion 0.39.0 is running at http://192.168.1.69:8787/. Use your existing owner login. Native 0.114.000 shortcuts are ready. NAS Companion and connector startup remain stopped; photos were not copied.'
} catch {
 $failure=$_.Exception.Message
 if(-not $cutoverJournal.CanonicalModified){
  if($cutoverJournal.NasStopped){$null=Invoke-CutoverNas (('/share/CACHEDEV1_DATA/.qpkg/container-station/bin/docker update --restart='+$cutoverNasPolicy+' pixelvault-companion')+' && /share/CACHEDEV1_DATA/.qpkg/container-station/bin/docker start pixelvault-companion')}
  if($cutoverJournal.StartupRetired -and -not (Test-Path -LiteralPath $cutoverStartup)){Move-Item -LiteralPath $cutoverStartupSaved -Destination $cutoverStartup}
  if($cutoverJournal.ConnectorTaskWasEnabled){Enable-ScheduledTask -TaskName 'PixelVault Companion Connector Recovery' -ErrorAction Stop | Out-Null;Start-ScheduledTask -TaskName 'PixelVault Companion Connector Recovery' -ErrorAction Stop}
  Save-CutoverJournal 'pre-write-abort-nas-restored'
 }else{Save-CutoverJournal 'stopped-for-review-do-not-start-nas'}
 if($cutoverJournal.BackupTaskWasEnabled){Enable-ScheduledTask -TaskName 'PixelVault App Data Backup' -ErrorAction Stop | Out-Null}
 throw ($failure+' Cutover checkpoint: '+$cutoverJournalFile)
}
