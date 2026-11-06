#Requires -Version 7.0
#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
$credentialPlanPath=[IO.Path]::GetFullPath($PlanPath)
$credentialDirectory=Split-Path $credentialPlanPath -Parent
$credentialPlan=Get-Content -LiteralPath $credentialPlanPath -Raw | ConvertFrom-Json
if($credentialPlan.Purpose -ne 'pc-companion-rehearsal' -or $credentialPlan.ServiceName -ne 'PixelVaultCompanionPhase4'){throw 'Only the isolated rehearsal is supported.'}
$credentialHelperPlan=Get-Content -LiteralPath (Join-Path $credentialDirectory 'credential-helper.json') -Raw | ConvertFrom-Json
foreach($credentialFile in $credentialHelperPlan.Files){
 $credentialPath=[IO.Path]::GetFullPath((Join-Path $credentialHelperPlan.PackageRoot $credentialFile.Path))
 $credentialRelative=[IO.Path]::GetRelativePath($credentialHelperPlan.PackageRoot,$credentialPath)
 if([IO.Path]::IsPathRooted($credentialRelative) -or $credentialRelative -eq '..' -or $credentialRelative.StartsWith('..\')){throw 'Helper file escaped the package.'}
 if((Get-FileHash -LiteralPath $credentialPath -Algorithm SHA256).Hash -ne $credentialFile.Sha256){throw 'The reviewed credential helper changed.'}
}
if((Get-FileHash -LiteralPath $credentialPlan.Settings -Algorithm SHA256).Hash -ne $credentialPlan.SettingsSha256){throw 'The reviewed service configuration changed.'}
$credentialOriginal='"'+$credentialPlan.Executable+'" --installation-config "'+$credentialPlan.Settings+'"'
$credentialTest='"'+$credentialHelperPlan.Executable+'" "'+$credentialPlanPath+'"'
$credentialService=Get-CimInstance Win32_Service -Filter "Name='PixelVaultCompanionPhase4'"
if($credentialService.StartName -ne 'NT SERVICE\PixelVaultCompanionPhase4' -or $credentialService.PathName -notin @($credentialOriginal,$credentialTest)){throw 'The service does not match the rehearsal.'}
$credentialSid=([Security.Principal.NTAccount]'NT SERVICE\PixelVaultCompanionPhase4').Translate([Security.Principal.SecurityIdentifier]).Value
& icacls.exe $credentialHelperPlan.PackageRoot /grant "*$($credentialSid):(OI)(CI)RX" | Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not grant helper read/execute access.'}
& icacls.exe $credentialPlanPath /grant "*$($credentialSid):R" | Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not grant read access to the non-secret rehearsal plan.'}
function Set-CredentialServiceCommand([string]$BinaryCommand){
 $credentialStart=[Diagnostics.ProcessStartInfo]::new((Join-Path $env:WINDIR 'System32\sc.exe'))
 $credentialStart.UseShellExecute=$false;$credentialStart.CreateNoWindow=$true;$credentialStart.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
 foreach($credentialArgument in @('config','PixelVaultCompanionPhase4','binPath=',$BinaryCommand)){$credentialStart.ArgumentList.Add($credentialArgument)}
 $credentialProcess=[Diagnostics.Process]::Start($credentialStart)
 try{$credentialProcess.WaitForExit();if($credentialProcess.ExitCode -ne 0){throw "Service command update failed with exit $($credentialProcess.ExitCode)."}}finally{$credentialProcess.Dispose()}
}
$credentialProofPath=Join-Path $credentialPlan.StateRoot 'credential-proof.json'
if(Test-Path -LiteralPath $credentialProofPath){Move-Item -LiteralPath $credentialProofPath -Destination ($credentialProofPath+'.previous-'+(Get-Date -Format yyyyMMddTHHmmssfff))}
try {
 Write-Host 'Checking Steam and RetroAchievements through the packaged reader under the rehearsal service account...'
 Stop-Service -Name PixelVaultCompanionPhase4
 (Get-Service PixelVaultCompanionPhase4).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
 Set-CredentialServiceCommand $credentialTest
 Start-Service -Name PixelVaultCompanionPhase4
 $credentialTimer=[Diagnostics.Stopwatch]::StartNew()
 while(-not (Test-Path -LiteralPath $credentialProofPath) -and $credentialTimer.Elapsed.TotalSeconds -lt 240){Start-Sleep -Seconds 1}
 if(-not (Test-Path -LiteralPath $credentialProofPath)){throw 'Credential test did not produce a result within four minutes.'}
 $credentialProof=Get-Content -LiteralPath $credentialProofPath -Raw | ConvertFrom-Json
 if(-not $credentialProof.Passed -or -not $credentialProof.ServiceIdentityVerified -or $credentialProof.Providers.Count -ne 2){throw 'A provider credential test failed. The private result contains provider status without secrets.'}
} finally {
 $credentialCurrent=Get-Service PixelVaultCompanionPhase4
 if($credentialCurrent.Status -ne 'Stopped'){Stop-Service PixelVaultCompanionPhase4; (Get-Service PixelVaultCompanionPhase4).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
 Set-CredentialServiceCommand $credentialOriginal
 Start-Service PixelVaultCompanionPhase4
}
Write-Host 'PASS: both providers accepted credentials under the service account. The rehearsal server is restored; automatic updates remain paused. The NAS remains live.'
