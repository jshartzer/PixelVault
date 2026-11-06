#Requires -Version 7.0
#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
if ($plan.Purpose -ne 'xbox-achievements-0.45.0') { throw 'Unknown update plan.' }
$log = Join-Path $plan.EvidenceRoot 'update.log'
Start-Transcript -LiteralPath $log -Force | Out-Null
$switched = $false
try {
    foreach ($file in $plan.Files) { if ((Get-FileHash -LiteralPath $file.Path).Hash -ne $file.Sha256) { throw "Prepared file changed: $($file.Path)" } }
    $service = Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanion'"
    if ($service.State -ne 'Running' -or $service.PathName -ne $plan.OldServicePath -or $service.StartName -ne $plan.Identity) { throw 'The running service changed.' }
    if ((Get-FileHash -LiteralPath $plan.Settings).Hash -ne $plan.SettingsSha256) { throw 'The installation settings changed.' }
    $oldScopeExists = Test-Path -LiteralPath $plan.ScopePath
    if ($oldScopeExists -ne $plan.ScopeExisted) { throw 'The achievement account scope changed.' }
    if ($oldScopeExists -and (Get-FileHash -LiteralPath $plan.ScopePath).Hash -ne $plan.ScopeSha256) { throw 'The achievement account scope changed.' }
    $currentXbox = Get-Content -LiteralPath 'C:\Codex\PixelVaultData\xbox-library.json' -Raw | ConvertFrom-Json
    $newScope = Get-Content -LiteralPath $plan.NewScope -Raw | ConvertFrom-Json
    if ($newScope.xbox -ne $currentXbox.Xuid) { throw 'The connected Xbox account changed.' }
    $filters = @(Get-NetFirewallRule -DisplayName 'PixelVault Companion PC service' | Get-NetFirewallApplicationFilter | Where-Object Program -eq $plan.OldExe)
    $blocked = @(Get-NetFirewallRule -DisplayName 'PixelVault retired NAS endpoint' -ErrorAction SilentlyContinue | Get-NetFirewallApplicationFilter | Where-Object Program -eq $plan.OldExe)
    if ($blocked.Count -eq 0) { $blocked = @(Get-NetFirewallRule -DisplayName 'PixelVault retired NAS Companion endpoint' | Get-NetFirewallApplicationFilter | Where-Object Program -eq $plan.OldExe) }
    if ($filters.Count -ne 1 -or $blocked.Count -ne 1) { throw 'The service firewall bindings changed.' }
    $sid = ([Security.Principal.NTAccount]$plan.Identity).Translate([Security.Principal.SecurityIdentifier]).Value
    & icacls.exe (Split-Path -Parent $plan.NewExe) /grant "*$($sid):(OI)(CI)RX" /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant package read access.' }
    & icacls.exe 'C:\Codex\PixelVaultData\xbox-library.json' /grant "*$($sid):R" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant Xbox catalogue read access.' }
    & icacls.exe 'C:\Codex\PixelVaultData\achievement-hub' /grant "*$($sid):(OI)(CI)R" /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant snapshot read access.' }
    function Set-Executable([string]$path) {
        & "$env:WINDIR\System32\sc.exe" config PixelVaultCompanion binPath= $path | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not update the service executable.' }
    }
    if ($oldScopeExists) { Copy-Item -LiteralPath $plan.ScopePath -Destination (Join-Path $plan.EvidenceRoot 'previous-accounts.json') }
    $switched = $true
    Stop-Service PixelVaultCompanion
    (Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
    Copy-Item -LiteralPath $plan.NewScope -Destination $plan.ScopePath -Force
    & icacls.exe $plan.ScopePath /grant "*$($sid):R" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant account scope read access.' }
    Set-Executable $plan.NewServicePath
    $filters | Set-NetFirewallApplicationFilter -Program $plan.NewExe | Out-Null
    $blocked | Set-NetFirewallApplicationFilter -Program $plan.NewExe | Out-Null
    Start-Service PixelVaultCompanion
    $healthy = $false
    for ($i=0; $i -lt 60; $i++) {
        try { $health = Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 2; if ($health.status -eq 'ok' -and $health.version -eq '0.45.0') { $healthy=$true; break } } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $healthy) { throw 'Updated service did not pass its health check.' }
    @{ Updated=$true; Version='0.45.0'; CheckedUtc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'result.json')
} catch {
    $failure = $_.Exception.Message
    if ($switched) {
        Stop-Service PixelVaultCompanion -ErrorAction SilentlyContinue
        (Get-Service PixelVaultCompanion).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
        if ($oldScopeExists) { Copy-Item -LiteralPath (Join-Path $plan.EvidenceRoot 'previous-accounts.json') -Destination $plan.ScopePath -Force }
        elseif (Test-Path -LiteralPath $plan.ScopePath) { Remove-Item -LiteralPath $plan.ScopePath }
        Set-Executable $plan.OldServicePath
        $filters | Set-NetFirewallApplicationFilter -Program $plan.OldExe | Out-Null
        $blocked | Set-NetFirewallApplicationFilter -Program $plan.OldExe | Out-Null
        Start-Service PixelVaultCompanion
    }
    @{ Updated=$false; Error=$failure } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $plan.EvidenceRoot 'result.json')
    throw
} finally { Stop-Transcript | Out-Null }
