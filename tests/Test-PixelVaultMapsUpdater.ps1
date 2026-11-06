#Requires -Version 7.0
$ErrorActionPreference='Stop'
$mapTestRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$mapTestRoot=Join-Path $mapTestRepo ('tmp\map-updater-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $mapTestRoot | Out-Null
$mapTestSource=[IO.File]::ReadAllText((Join-Path $mapTestRepo 'scripts\Update-PixelVaultMaps.ps1'))
$mapTestTokens=$null;$mapTestErrors=$null
$mapTestAst=[Management.Automation.Language.Parser]::ParseInput($mapTestSource,[ref]$mapTestTokens,[ref]$mapTestErrors)
if($mapTestErrors.Count){throw 'Updater syntax is invalid.'}
$mapScFunction=$mapTestAst.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-UpdateSc'},$true)
if(-not $mapScFunction){throw 'Expected the bounded SCM wrapper.'}
# Test the actual control flow with hermetic paths and simulated Windows APIs. No SCM/native command runs.
$mapTestSource=$mapTestSource.Replace($mapScFunction.Extent.Text,@'
function Invoke-UpdateSc([string[]]$Arguments){$global:mapUpdateSim.Service.PathName=$Arguments[-1]}
'@).Replace('#Requires -RunAsAdministrator','')
function Get-CimInstance {param($ClassName,$Filter) $global:mapUpdateSim.Service}
function Get-NetFirewallRule {
 param($DisplayName,$ErrorAction)
 if($DisplayName -eq 'PixelVault Companion PC service'){$global:mapUpdateSim.Inbound}else{$global:mapUpdateSim.Outbound}
}
function Get-NetFirewallApplicationFilter {param([Parameter(ValueFromPipeline)]$InputObject) process{$InputObject}}
function Set-NetFirewallApplicationFilter {param([Parameter(ValueFromPipeline)]$InputObject,$Program) process{$InputObject.Program=$Program}}
function Get-ScheduledTask {param($TaskName) [pscustomobject]@{State=$global:mapUpdateSim.TaskState}}
function Disable-ScheduledTask {param($TaskName) $global:mapUpdateSim.TaskState='Disabled'}
function Enable-ScheduledTask {param($TaskName) $global:mapUpdateSim.TaskState='Ready'}
function icacls.exe {param([Parameter(ValueFromRemainingArguments)]$Arguments) $global:LASTEXITCODE=0}
function Stop-Service {param($Name,$ErrorAction) $global:mapUpdateSim.Service.State='Stopped';$global:mapUpdateSim.Stops++}
function Get-Service {param($Name) $global:mapUpdateSim.Service}
function Start-Service {param($Name) $global:mapUpdateSim.Service.State='Running';if($global:mapUpdateSim.Failure -eq 'policy' -and $global:mapUpdateSim.Service.PathName.Contains('0.41.0')){$global:mapUpdateSim.Automatic=$false}}
function Start-Sleep {param($Milliseconds)}
function Invoke-RestMethod {
 param($Uri,$TimeoutSec)
 $version=if($global:mapUpdateSim.Service.PathName.Contains('0.41.0')){'0.41.0'}else{'0.40.0'}
 [pscustomobject]@{version=$version;status=$(if($global:mapUpdateSim.Failure -eq 'health' -and $version -eq '0.41.0'){'failed'}else{'ok'})}
}
function Test-MapBackupPython {
 param([Parameter(ValueFromRemainingArguments)]$Arguments)
 $global:LASTEXITCODE=0
 if($Arguments[1] -eq 'snapshot'){$global:mapUpdateSim.Backups++;'fixture backup';return}
 if($Arguments[1].Contains('AutomaticEnabled')){if($global:mapUpdateSim.Automatic){'true'}else{'false'}}
}
$mapTestResults=@()
foreach($scenario in @('success','paused-task','hash','health','access','policy')){
 $caseRoot=Join-Path $mapTestRoot $scenario
 $production=Join-Path $caseRoot 'Companion Production';$evidence=Join-Path $caseRoot 'evidence'
 $application=Join-Path $caseRoot 'PixelVaultData';$state=Join-Path $caseRoot 'PixelVaultCompanionLocalData'
 New-Item -ItemType Directory -Path $production,$evidence,$state,(Join-Path $application 'game-maps\images') -Force | Out-Null
 $oldExe=Join-Path $production 'app-0.40.0\PixelVault.Companion.exe';$newExe=Join-Path $production 'app-0.41.0\PixelVault.Companion.exe'
 foreach($exe in @($oldExe,$newExe)){New-Item -ItemType Directory -Path (Split-Path $exe) -Force | Out-Null;[IO.File]::WriteAllText($exe,'fixture executable')}
 $settings=Join-Path $production 'service.json'
 @{Companion=@{ApplicationDataRoot=$application;DataRoot=$state;CacheRoot=(Join-Path $caseRoot 'cache');PhotoWorkerPath='old-photo';ReaderWorkerPath='old-reader';ScreenshotLibraryEnabled=$true}} | ConvertTo-Json | Set-Content -LiteralPath $settings
 $settingsBefore=[IO.File]::ReadAllText($settings)
 $expected='"'+$oldExe+'" --installation-config "'+$settings+'"'
 $oldReader=Join-Path $production 'app-0.40.0\readers\PixelVault.exe'
 $global:mapUpdateSim=@{Failure=$scenario;Service=[pscustomobject]@{PathName=$expected;StartName='NT SERVICE\PixelVaultCompanion';State='Running';ProcessId=$PID};Inbound=[pscustomobject]@{Program=$oldExe};Outbound=@([pscustomobject]@{Program=$oldExe},[pscustomobject]@{Program=$oldReader});TaskState=$(if($scenario -eq 'paused-task'){'Disabled'}else{'Ready'});Automatic=$true;Stops=0;Backups=0}
 $global:mapUpdateSim.Service | Add-Member -MemberType ScriptMethod -Name WaitForStatus -Value {param($status,$timeout) if($this.State -ne $status){throw 'Unexpected fixture service state'}}
 $original=Join-Path $application 'game-maps\images\fixture.png';[IO.File]::WriteAllText($original,'fixture original')
 $backupEngine=Join-Path $caseRoot 'backup.py';[IO.File]::WriteAllText($backupEngine,'fixture engine')
 $backupPlan=Join-Path $caseRoot 'backup-plan.json';@{engine=$backupEngine;engineSha256=(Get-FileHash $backupEngine).Hash;pythonExe='Test-MapBackupPython'} | ConvertTo-Json | Set-Content $backupPlan
 $sourceArchive=Join-Path $caseRoot 'source.zip';[IO.File]::WriteAllText($sourceArchive,'fixture source')
 $rehearsal=Join-Path $caseRoot 'rehearsal.json';@{passed=$true;version='0.41.0';pins=56} | ConvertTo-Json | Set-Content $rehearsal
 $access=Join-Path $caseRoot 'access.ps1'
 @'
param($DatabasePath,$StartedTicks,$PythonExe,$MinimumFiles,$ExpectedIdentity)
if($global:mapUpdateSim.Failure -eq 'access'){throw 'Simulated service access receipt failure'}
@{Identity=$ExpectedIdentity;ReadableMapFiles=$MinimumFiles;CacheWritable=$true}
'@ | Set-Content -LiteralPath $access
 $plan=Join-Path $caseRoot 'plan.json'
 @{Purpose='pc-companion-maps-0.41.0';Files=@(@{Path=$newExe;Sha256=(Get-FileHash $newExe).Hash});SourceArchive=$sourceArchive;RehearsalResult=$rehearsal;SettingsSha256=(Get-FileHash $settings).Hash;MapImages=@(@{Path=$original;Sha256=(Get-FileHash $original).Hash});BackupPlan=$backupPlan;EvidenceRoot=$evidence;MapAccessScript=$access} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $plan
 if($scenario -eq 'hash'){[IO.File]::WriteAllText($newExe,'changed package')}
 $candidate=$mapTestSource.Replace('C:\Codex',$caseRoot)
 if($candidate.Contains('C:\Codex\Companion Production') -or $candidate.Contains('C:\Codex\PixelVaultData') -or $candidate.Contains('C:\Codex\PixelVaultCompanionLocalData') -or $candidate.Contains('System32\sc.exe')){throw 'The updater simulation retained a production action.'}
 $script=Join-Path $caseRoot 'updater.ps1';[IO.File]::WriteAllText($script,$candidate)
 $failure=$null
 try{& $script -PlanPath $plan}catch{$failure=$_}
 $success=$scenario -in @('success','paused-task')
 if($success){
  if($failure){throw $failure}
  $receipt=Get-Content -Raw (Join-Path $evidence 'maps-update-result.json') | ConvertFrom-Json
  if(-not $receipt.Passed -or $receipt.Version -ne '0.41.0' -or -not $global:mapUpdateSim.Service.PathName.Contains($newExe)){throw 'Successful update did not switch to the new package.'}
 }else{
  if(-not $failure){throw 'Expected the simulated update failure.'}
  if($global:mapUpdateSim.Service.PathName -ne $expected -or $global:mapUpdateSim.Service.State -ne 'Running' -or [IO.File]::ReadAllText($settings) -ne $settingsBefore){throw 'Failure did not retain/restore the old running configuration.'}
  if($global:mapUpdateSim.Inbound.Program -ne $oldExe -or $global:mapUpdateSim.Outbound[1].Program -ne $oldReader){throw 'Failure did not restore firewall paths.'}
  if($scenario -eq 'hash' -and ($global:mapUpdateSim.Stops -ne 0 -or $global:mapUpdateSim.Backups -ne 0)){throw 'Preflight must precede backup and service changes.'}
  if($scenario -ne 'hash'){
   $rollback=Get-Content -Raw (Join-Path $evidence 'maps-update-rollback.json') | ConvertFrom-Json
   if(-not $rollback.Healthy){throw 'Rollback did not verify healthy old service.'}
  }
 }
 if($global:mapUpdateSim.TaskState -ne $(if($scenario -eq 'paused-task'){'Disabled'}else{'Ready'})){throw 'Backup schedule state changed.'}
 if($scenario -ne 'hash' -and $global:mapUpdateSim.Backups -ne 1){throw 'Expected one pre-update backup.'}
 $mapTestResults+=@{Scenario=$scenario;Passed=$true}
}
$mapTestResults | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $mapTestRoot 'results.json')
[pscustomobject]@{Passed=$true;Scenarios=$mapTestResults.Count;Evidence=$mapTestRoot} | ConvertTo-Json
