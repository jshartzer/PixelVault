#Requires -Version 7.0
#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
$backupTaskPlanPath=[IO.Path]::GetFullPath($PlanPath)
$backupTaskPlan=Get-Content -LiteralPath $backupTaskPlanPath -Raw | ConvertFrom-Json
if($backupTaskPlan.purpose -ne 'pixelvault-app-backups' -or $backupTaskPlan.roots.application -ne 'C:\Codex\PixelVaultData'){throw 'This task installer accepts only the canonical PC app-data backup plan.'}
if((Get-FileHash -LiteralPath $backupTaskPlan.engine -Algorithm SHA256).Hash -ne $backupTaskPlan.engineSha256){throw 'The reviewed backup engine changed.'}
$backupTaskName='PixelVault App Data Backup'
$backupExisting=Get-ScheduledTask -TaskName $backupTaskName -ErrorAction SilentlyContinue
$backupAction=New-ScheduledTaskAction -Execute (Get-Command pwsh.exe).Source -Argument ('-NoProfile -NonInteractive -WindowStyle Hidden -File "C:\Codex\scripts\Run-PixelVaultAppBackup.ps1" -PlanPath "'+$backupTaskPlanPath+'"') -WorkingDirectory 'C:\Codex'
$backupTrigger=New-ScheduledTaskTrigger -Daily -At '03:00'
$backupTrigger.StartBoundary=(Get-Date).Date.AddHours(3).ToString('yyyy-MM-ddTHH:mm:ss')
$backupPrincipal=New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType S4U -RunLevel Limited
$backupSettings=New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 20) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$backupTask=New-ScheduledTask -Action $backupAction -Trigger $backupTrigger -Principal $backupPrincipal -Settings $backupSettings -Description 'App-only PC backups at 03:00 local time, 14 daily and 8 weekly points plus named snapshots; verified copies to NAS with retry. No capture originals or derivative caches. Add final PC Companion state to the plan during cutover.'
if($backupExisting){
 $backupExistingSid=([Security.Principal.NTAccount]$backupExisting.Principal.UserId).Translate([Security.Principal.SecurityIdentifier]).Value
 if($backupExisting.Actions.Count -ne 1 -or $backupExisting.Actions[0].Execute -ne $backupAction.Execute -or $backupExisting.Actions[0].Arguments -ne $backupAction.Arguments -or $backupExistingSid -ne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -or $backupExisting.Principal.LogonType -ne 'S4U'){throw 'The existing task does not match this app-only backup setup; nothing was replaced.'}
 Set-ScheduledTask -TaskName $backupTaskName -Trigger $backupTrigger -ErrorAction Stop | Out-Null
}else{
 Register-ScheduledTask -TaskName $backupTaskName -InputObject $backupTask -ErrorAction Stop | Out-Null
}
$backupRegistered=Get-ScheduledTask -TaskName $backupTaskName -ErrorAction Stop
if($backupRegistered.Principal.LogonType -ne 'S4U' -or -not $backupRegistered.Settings.StartWhenAvailable){throw 'The registered backup task does not match the reviewed unattended settings.'}
if($backupRegistered.State -ne 'Running'){Start-ScheduledTask -TaskName $backupTaskName -ErrorAction Stop}
Write-Host 'App-only daily backup task registered and started. It runs without interactive sign-in; NAS-copy failures remain queued and trigger retries.'
