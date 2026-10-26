function Get-CompanionRelocatedUrl {
    param([Parameter(Mandatory)][string]$DataRoot)
    $marker=Join-Path $DataRoot 'hosting.json'
    if(!(Test-Path -LiteralPath $marker)){return $null}
    $hosting=Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json -ErrorAction Stop
    if($hosting.version -ne 1 -or $hosting.mode -ne 'nas'){throw 'The Companion hosting marker needs review before starting a local copy.'}
    $address=$null
    if(![Uri]::TryCreate([string]$hosting.url,[UriKind]::Absolute,[ref]$address) -or $address.Scheme -notin @('http','https') -or $address.UserInfo -or $address.AbsolutePath -ne '/' -or $address.Query -or $address.Fragment){throw 'The saved Companion server address is invalid.'}
    $address.AbsoluteUri
}

function Get-CompanionPreviewNetwork {
    param([int]$Port=8787,[switch]$HomeNetwork,[string]$ListenAddress='')
    if($Port -lt 1024 -or $Port -gt 65535){throw 'Choose a port between 1024 and 65535.'}
    if(!$HomeNetwork -and [string]::IsNullOrWhiteSpace($ListenAddress)){
        return [PSCustomObject]@{Urls="http://127.0.0.1:$Port";AllowedHosts='localhost;127.0.0.1';PhoneUrl=$null;Address=$null;InterfaceAlias=$null}
    }
    if([string]::IsNullOrWhiteSpace($ListenAddress)){
        $addresses=@(Get-NetIPConfiguration | Where-Object {$_.IPv4DefaultGateway} | ForEach-Object {$_.IPv4Address.IPAddress} | Where-Object {$_} | Sort-Object -Unique)
        if($addresses.Count -ne 1){throw 'Choose the home-network address with -ListenAddress. More than one network address was found.'}
        $ListenAddress=$addresses[0]
    }
    $parsed=$null
    if(![Net.IPAddress]::TryParse($ListenAddress,[ref]$parsed) -or $parsed.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork){throw 'Use a home-network IPv4 address assigned to this PC.'}
    $bytes=$parsed.GetAddressBytes()
    if(!($bytes[0] -eq 10 -or ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or ($bytes[0] -eq 192 -and $bytes[1] -eq 168))){throw 'Use a private home-network address assigned to this PC. Public addresses and wildcard listeners are not supported by this preview.'}
    $assigned=@(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $parsed.ToString() -ErrorAction Stop)
    if($assigned.Count -ne 1 -or [string]$assigned[0].AddressState -ne 'Preferred'){throw 'That address is not an active address on this PC.'}
    $address=$parsed.ToString()
    [PSCustomObject]@{Urls="http://127.0.0.1:$Port;http://${address}:$Port";AllowedHosts="localhost;127.0.0.1;$address";PhoneUrl="http://${address}:$Port";Address=$address;InterfaceAlias=$assigned[0].InterfaceAlias}
}

function Enable-CompanionPreviewPhoneAccess {
    param([Parameter(Mandatory)]$Network,[int]$Port=8787,[Parameter(Mandatory)][string]$FirewallScript)
    if(!$Network.PhoneUrl){return}
    $name="PixelVaultCompanion.HomePreview.TCP.$Port.$($Network.Address)"
    $rule=Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue
    $dotnet=(Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    if($rule -and $rule.Group -eq 'PixelVault Companion home preview' -and [string]$rule.Enabled -eq 'True' -and [string]$rule.Action -eq 'Allow' -and [string]$rule.Direction -eq 'Inbound'){
        $portFilter=$rule | Get-NetFirewallPortFilter
        $addressFilter=$rule | Get-NetFirewallAddressFilter
        $applicationFilter=$rule | Get-NetFirewallApplicationFilter
        $interfaceFilter=$rule | Get-NetFirewallInterfaceFilter
        if([string]$portFilter.Protocol -eq 'TCP' -and [string]$portFilter.LocalPort -eq [string]$Port -and [string]$addressFilter.LocalAddress -eq $Network.Address -and [string]$addressFilter.RemoteAddress -eq 'LocalSubnet' -and $applicationFilter.Program -eq $dotnet -and [string]$interfaceFilter.InterfaceAlias -eq $Network.InterfaceAlias){return}
    }
    Write-Host 'Windows needs administrator approval once to allow this Companion port from your home network.'
    $arguments=@('-NoProfile','-File',('"'+$FirewallScript+'"'),'-ListenAddress',$Network.Address,'-Port',[string]$Port)
    $administrator=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if($administrator){& $FirewallScript -ListenAddress $Network.Address -Port $Port;return}
    try{
        $helper=Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru
        $helper.WaitForExit()
        if($helper.ExitCode -ne 0){throw 'The firewall allowance was not created.'}
    }
    catch{throw 'Phone access needs the Windows administrator approval. Run this launcher again and approve the Windows prompt, or run it from an administrator PowerShell window.'}
}
