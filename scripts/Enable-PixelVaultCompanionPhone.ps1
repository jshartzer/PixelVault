#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$ListenAddress,[int]$Port=8787)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CompanionPreview.Network.ps1')
$network=Get-CompanionPreviewNetwork -Port $Port -ListenAddress $ListenAddress
$dotnet=(Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
$name="PixelVaultCompanion.HomePreview.TCP.$Port.$($network.Address)"
$group='PixelVault Companion home preview'
$existing=Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue
if($existing -and $existing.Group -ne $group){throw 'A different firewall rule already uses this name. It was not changed.'}
$parameters=@{
    Direction='Inbound';Action='Allow';Enabled='True';Profile='Any';Protocol='TCP';LocalPort=$Port
    LocalAddress=$network.Address;RemoteAddress='LocalSubnet';InterfaceAlias=$network.InterfaceAlias;Program=$dotnet
    Description='Companion phone preview: this application, one local IPv4 address/port, and clients on the attached home network only.'
}
if($existing){Set-NetFirewallRule -Name $name @parameters | Out-Null}
else{New-NetFirewallRule -Name $name -DisplayName "PixelVault Companion home preview ($Port)" -Group $group @parameters | Out-Null}
