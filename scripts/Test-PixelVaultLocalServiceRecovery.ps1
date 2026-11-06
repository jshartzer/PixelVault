#Requires -Version 7.0
#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
$recoveryPlanPath=[IO.Path]::GetFullPath($PlanPath)
$recoveryDirectory=Split-Path $recoveryPlanPath -Parent
$recoveryResult=Join-Path $recoveryDirectory 'recovery-proof.json'
try {
    $recoveryPlan=Get-Content -LiteralPath $recoveryPlanPath -Raw | ConvertFrom-Json
    if($recoveryPlan.Purpose -ne 'pc-companion-rehearsal' -or $recoveryPlan.ServiceName -ne 'PixelVaultCompanionPhase4'){throw 'Only the isolated Phase 4 rehearsal can be tested.'}
    $recoveryMarker=Get-Content -LiteralPath (Join-Path $recoveryPlan.ApplicationRoot 'local-store-preparation.json') -Raw | ConvertFrom-Json
    if($recoveryMarker.purpose -ne 'pc-companion-rehearsal'){throw 'The application root is not an isolated rehearsal.'}
    if((Get-FileHash -LiteralPath $recoveryPlan.Settings -Algorithm SHA256).Hash -ne $recoveryPlan.SettingsSha256){throw 'The reviewed service configuration changed.'}
    $recoveryExpectedCommand='"'+$recoveryPlan.Executable+'" --installation-config "'+$recoveryPlan.Settings+'"'
    $recoveryService=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanionPhase4'"
    if($recoveryService.State -ne 'Running' -or $recoveryService.StartName -ne 'NT SERVICE\PixelVaultCompanionPhase4' -or $recoveryService.PathName -ne $recoveryExpectedCommand){throw 'The running rehearsal service does not match the reviewed plan.'}
    $recoveryBase='http://127.0.0.1:8788'
    $recoverySession=[Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $recoveryCsrf=Invoke-RestMethod -Uri ($recoveryBase+'/api/v1/antiforgery') -WebSession $recoverySession -TimeoutSec 10
    $recoveryAuth=Get-Content -LiteralPath (Join-Path $recoveryDirectory 'proof-auth.private.json') -Raw
    $null=Invoke-RestMethod -Method Post -Uri ($recoveryBase+'/api/v1/login') -WebSession $recoverySession -ContentType 'application/json' -Headers @{'X-CSRF-TOKEN'=$recoveryCsrf.token} -Body $recoveryAuth -TimeoutSec 10
    $recoveryBefore=Invoke-RestMethod -Uri ($recoveryBase+'/api/v1/refresh/connection') -WebSession $recoverySession -TimeoutSec 10
    if(-not $recoveryBefore.local -or -not $recoveryBefore.online -or $recoveryBefore.automaticEnabled){throw 'The rehearsal must be healthy and automatic updates paused before testing.'}
    $recoveryOldId=[int]$recoveryService.ProcessId
    $recoveryProcess=Get-CimInstance Win32_Process -Filter "ProcessId=$recoveryOldId"
    if($recoveryProcess.ExecutablePath -ne $recoveryPlan.Executable -or (Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanionPhase4'").ProcessId -ne $recoveryOldId){throw 'The rehearsal process changed before the test; nothing was stopped.'}
    Write-Host 'Testing Windows crash recovery for the isolated rehearsal service...'
    $recoveryTimer=[Diagnostics.Stopwatch]::StartNew()
    Stop-Process -Id $recoveryOldId -Force
    $recoveryComplete=$false
    while($recoveryTimer.Elapsed.TotalSeconds -lt 90){
        Start-Sleep -Seconds 1
        try {
            $recoveryCurrent=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanionPhase4'"
            if($recoveryCurrent.State -ne 'Running' -or $recoveryCurrent.ProcessId -eq $recoveryOldId -or $recoveryCurrent.ProcessId -eq 0){continue}
            $recoveryHealth=Invoke-RestMethod -Uri ($recoveryBase+'/health') -TimeoutSec 3
            $recoveryOwner=Invoke-RestMethod -Uri ($recoveryBase+'/api/v1/session') -WebSession $recoverySession -TimeoutSec 3
            $recoveryAfter=Invoke-RestMethod -Uri ($recoveryBase+'/api/v1/refresh/connection') -WebSession $recoverySession -TimeoutSec 3
            if($recoveryHealth.status -eq 'ok' -and $recoveryOwner.authenticated -and $recoveryAfter.online){$recoveryComplete=$true;break}
        } catch { }
    }
    if(-not $recoveryComplete){throw 'The service did not recover with its authenticated session and readers healthy within 90 seconds. No manual restart was attempted.'}
    if($recoveryAfter.automaticEnabled -or $recoveryAfter.workspaceId -ne $recoveryBefore.workspaceId){throw 'Paused automation or the rehearsal workspace did not survive recovery.'}
    $recoveryGames=Invoke-RestMethod -Uri ($recoveryBase+'/api/v1/games') -WebSession $recoverySession -TimeoutSec 10
    if($recoveryGames.Count -eq 0){throw 'The recovered service could not read the staged library.'}
    [pscustomobject]@{Passed=$true;Utc=[DateTime]::UtcNow.ToString('o');Service=$recoveryPlan.ServiceName;PreviousProcessId=$recoveryOldId;NewProcessId=$recoveryCurrent.ProcessId;RecoveryMilliseconds=$recoveryTimer.ElapsedMilliseconds;OwnerSessionPreserved=$true;ReadersOnline=$true;AutomaticPaused=$true;WorkspacePreserved=$true;Games=$recoveryGames.Count;NasChanged=$false;RebootVerified=$false} | ConvertTo-Json | Set-Content -LiteralPath $recoveryResult
    Write-Host 'PASS: Windows restarted the rehearsal service; login, paused automation and library access survived. The NAS remains live.'
} catch {
    [pscustomobject]@{Passed=$false;Utc=[DateTime]::UtcNow.ToString('o');Error=$_.Exception.Message;NasChanged=$false} | ConvertTo-Json | Set-Content -LiteralPath $recoveryResult
    throw
}
