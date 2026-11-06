param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
$servicePlanFile=[IO.Path]::GetFullPath($PlanPath)
$servicePlan=Get-Content -LiteralPath $servicePlanFile -Raw | ConvertFrom-Json
$serviceEvidence=Split-Path $servicePlanFile -Parent
$serviceResult=Join-Path $serviceEvidence 'installation-result.json'
$serviceScSteps=[Collections.Generic.List[object]]::new()
function Get-RehearsalServiceCreateArguments {
    param([string]$ServiceName,[string]$BinaryCommand)
    # A virtual account needs a null password, not an explicitly supplied empty password.
    # https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-createservicew
    @('create',$ServiceName,'binPath=',$BinaryCommand,'start=','delayed-auto','obj=',('NT SERVICE\'+$ServiceName))
}
function Invoke-RehearsalServiceControl {
    param([string]$Action,[string[]]$ServiceArguments,[string]$Executable=(Join-Path $env:WINDIR 'System32\sc.exe'))
    $nativeStart=[Diagnostics.ProcessStartInfo]::new($Executable)
    $nativeStart.UseShellExecute=$false
    $nativeStart.CreateNoWindow=$true
    $nativeStart.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $nativeStart.RedirectStandardOutput=$true
    $nativeStart.RedirectStandardError=$true
    foreach($nativeArgument in $ServiceArguments){$nativeStart.ArgumentList.Add($nativeArgument)}
    $nativeProcess=[Diagnostics.Process]::Start($nativeStart)
    try {
        $nativeOutput=$nativeProcess.StandardOutput.ReadToEndAsync()
        $nativeError=$nativeProcess.StandardError.ReadToEndAsync()
        $nativeProcess.WaitForExit()
        $nativeText=($nativeOutput.GetAwaiter().GetResult()+[Environment]::NewLine+$nativeError.GetAwaiter().GetResult()).Trim()
        $script:serviceScSteps.Add([PSCustomObject]@{Action=$Action;ExitCode=$nativeProcess.ExitCode;Output=$nativeText})
        if($nativeProcess.ExitCode -ne 0){throw "$Action failed (sc.exe exit $($nativeProcess.ExitCode)): $nativeText"}
        Write-Host "$Action completed."
    } finally {$nativeProcess.Dispose()}
}
try {
    if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator approval is required to register a Windows service and its scoped firewall rule.'}
    if($servicePlan.Version -ne 1 -or $servicePlan.Purpose -ne 'pc-companion-rehearsal' -or $servicePlan.ServiceName -ne 'PixelVaultCompanionPhase4'){throw 'This installer accepts only the isolated Phase 4 rehearsal plan.'}
    $serviceMarker=Get-Content -LiteralPath (Join-Path $servicePlan.ApplicationRoot 'local-store-preparation.json') -Raw | ConvertFrom-Json
    if($serviceMarker.purpose -ne 'pc-companion-rehearsal'){throw 'The application root is not an isolated rehearsal.'}
    if((Get-FileHash -LiteralPath $servicePlan.Settings -Algorithm SHA256).Hash -ne $servicePlan.SettingsSha256){throw 'The reviewed configuration changed. Prepare a fresh plan.'}
    foreach($entry in $servicePlan.Files){
        $serviceFile=[IO.Path]::GetFullPath((Join-Path $servicePlan.PackageRoot $entry.Path))
        $serviceRelative=[IO.Path]::GetRelativePath($servicePlan.PackageRoot,$serviceFile)
        if([IO.Path]::IsPathRooted($serviceRelative) -or $serviceRelative -eq '..' -or $serviceRelative.StartsWith('..\')){throw 'A package file escaped its approved root.'}
        if((Get-FileHash -LiteralPath $serviceFile -Algorithm SHA256).Hash -ne $entry.Sha256){throw 'The reviewed service package changed. Prepare a fresh plan.'}
    }
    if(Get-Service -Name $servicePlan.ServiceName -ErrorAction SilentlyContinue){throw 'The rehearsal service already exists; its state was retained.'}
    $serviceAddress=Get-NetIPAddress -AddressFamily IPv4 -InterfaceAlias $servicePlan.Network.Interface | Where-Object IPAddress -eq $servicePlan.Network.Address
    if(-not $serviceAddress){throw 'The reviewed LAN address/interface changed. Prepare a fresh network plan.'}
    $serviceCommand='"'+$servicePlan.Executable+'" --installation-config "'+$servicePlan.Settings+'"'
    Invoke-RehearsalServiceControl -Action 'Service registration' -ServiceArguments (Get-RehearsalServiceCreateArguments -ServiceName $servicePlan.ServiceName -BinaryCommand $serviceCommand)
    Invoke-RehearsalServiceControl -Action 'Service SID configuration' -ServiceArguments @('sidtype',$servicePlan.ServiceName,'unrestricted')
    $serviceSid=([Security.Principal.NTAccount]('NT SERVICE\'+$servicePlan.ServiceName)).Translate([Security.Principal.SecurityIdentifier]).Value
    & icacls $servicePlan.PackageRoot /grant "*$($serviceSid):(OI)(CI)RX" | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Could not grant read/execute access to the reviewed package.'}
    & icacls $servicePlan.Settings /grant "*$($serviceSid):R" | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Could not grant read access to the explicit service configuration.'}
    foreach($serviceDirectory in @($servicePlan.ApplicationRoot,$servicePlan.StateRoot,$servicePlan.CacheRoot)){
        & icacls $serviceDirectory /grant "*$($serviceSid):(OI)(CI)M" | Out-Null
        if($LASTEXITCODE -ne 0){throw 'Could not grant the service access to its staged data.'}
    }
    # Cache validation checks ancestor attributes. These protected evidence containers
    # need directory-only attributes access, with no listing or inherited backup access.
    foreach($serviceDirectory in @($serviceEvidence,(Split-Path $serviceEvidence -Parent))){
        & icacls $serviceDirectory /grant "*$($serviceSid):(RA)" | Out-Null
        if($LASTEXITCODE -ne 0){throw 'Could not grant directory-attribute access to the rehearsal containers.'}
    }
    foreach($serviceDirectory in $servicePlan.ReadOnlySaveRoots){
        & icacls $serviceDirectory /grant "*$($serviceSid):(OI)(CI)RX" | Out-Null
        if($LASTEXITCODE -ne 0){throw 'Could not grant read access to an approved save folder.'}
    }
    # Photos and the game installation already grant Users read access; their ACLs and contents are untouched.
    Invoke-RehearsalServiceControl -Action 'Service restart recovery' -ServiceArguments @('failure',$servicePlan.ServiceName,'reset=','86400','actions=','restart/5000/restart/15000/restart/60000')
    Invoke-RehearsalServiceControl -Action 'Service failure recovery' -ServiceArguments @('failureflag',$servicePlan.ServiceName,'1')
    New-NetFirewallRule -DisplayName 'PixelVault Companion Phase 4 rehearsal' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $servicePlan.Network.Port -LocalAddress $servicePlan.Network.Address -RemoteAddress $servicePlan.Network.RemoteSubnet -InterfaceAlias $servicePlan.Network.Interface -Profile Any -Program $servicePlan.Executable -Service $servicePlan.ServiceName -EdgeTraversalPolicy Block | Out-Null
    Start-Service -Name $servicePlan.ServiceName
    [PSCustomObject]@{Installed=$true;Service=$servicePlan.ServiceName;Identity=('NT SERVICE\'+$servicePlan.ServiceName);Port=$servicePlan.Network.Port;NasChanged=$false;Startup='delayed-auto';RecoverySeconds=@(5,15,60);NativeSteps=$serviceScSteps.ToArray()} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $serviceResult
    Write-Host 'Rehearsal service installed and started on port 8788. The NAS remains live.'
} catch {
    [PSCustomObject]@{Installed=$false;Error=$_.Exception.Message;NasChanged=$false;NativeSteps=$serviceScSteps.ToArray()} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $serviceResult
    throw
}
