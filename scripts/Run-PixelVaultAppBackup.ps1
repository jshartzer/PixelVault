#Requires -Version 7.0
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
# Task Scheduler's default priority can throttle SQLite snapshot I/O for many seconds.
# Use normal priority while holding the short database write barrier.
([Diagnostics.Process]::GetCurrentProcess()).PriorityClass=[Diagnostics.ProcessPriorityClass]::Normal
$backupRunPlan=Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
try {
 if($backupRunPlan.purpose -ne 'pixelvault-app-backups'){throw 'Unknown backup plan.'}
 if((Get-FileHash -LiteralPath $backupRunPlan.engine -Algorithm SHA256).Hash -ne $backupRunPlan.engineSha256){throw 'Backup engine changed; reseal the reviewed plan.'}
 $backupRetryOnly=$false
 $backupLatestPath=Join-Path $backupRunPlan.backupRoot 'latest-status.json'
 $backupRemotePath=Join-Path $backupRunPlan.backupRoot 'replication-status.json'
 if((Test-Path -LiteralPath $backupLatestPath) -and (Test-Path -LiteralPath $backupRemotePath)){
  $backupLatest=Get-Content -LiteralPath $backupLatestPath -Raw | ConvertFrom-Json
  $backupPreviousRemote=Get-Content -LiteralPath $backupRemotePath -Raw | ConvertFrom-Json
  $backupRetryOnly=$backupLatest.lastLocalSuccessUtc -and ([DateTime]::UtcNow-[DateTime]::Parse($backupLatest.lastLocalSuccessUtc).ToUniversalTime()).TotalMinutes -lt 60 -and @($backupPreviousRemote.archives.PSObject.Properties | Where-Object {-not $_.Value.copied}).Count -gt 0
 }
 if($backupRetryOnly){& $backupRunPlan.pythonExe $backupRunPlan.engine retry $PlanPath}else{& $backupRunPlan.pythonExe $backupRunPlan.engine snapshot $PlanPath}
 if($LASTEXITCODE -ne 0){throw 'The local app snapshot failed.'}
 if($backupRunPlan.remote){
  $backupReplication=Get-Content -LiteralPath (Join-Path $backupRunPlan.backupRoot 'replication-status.json') -Raw | ConvertFrom-Json
  if(@($backupReplication.archives.PSObject.Properties | Where-Object {-not $_.Value.copied}).Count -gt 0){throw 'The local snapshot is saved, but one or more NAS copies remain queued for retry.'}
 }
 Write-Host 'PASS: app data snapshot and configured separate-device copies verified.'
 exit 0
} catch {
 $backupFailurePath=Join-Path $backupRunPlan.backupRoot 'task-failure.json'
 if(Test-Path -LiteralPath $backupRunPlan.backupRoot){[pscustomobject]@{Utc=[DateTime]::UtcNow.ToString('o');ErrorType=$_.Exception.GetType().Name;Summary='App backup or separate-device copy failed. Existing snapshots are retained; scheduled retries are configured.'} | ConvertTo-Json | Set-Content -LiteralPath $backupFailurePath}
 Write-Error $_.Exception.Message
 exit 1
}
