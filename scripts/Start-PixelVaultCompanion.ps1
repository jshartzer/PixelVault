param(
    [int]$Port = 8787,
    [string]$DataRoot = "",
    [switch]$Demo,
    [switch]$HomeNetwork,
    [string]$ListenAddress=''
)
$ErrorActionPreference = "Stop"
$companionRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
. (Join-Path $PSScriptRoot 'CompanionPreview.Network.ps1')
if ([string]::IsNullOrWhiteSpace($DataRoot)) { $DataRoot = Join-Path $companionRepo 'PixelVaultCompanionData' }
$companionRelocated=Get-CompanionRelocatedUrl -DataRoot $DataRoot
if($companionRelocated){Write-Host "PixelVault Companion now runs on the NAS: $companionRelocated";Write-Host 'The former PC workspace is retained as a backup and was not started.';return}
$companionNetwork=Get-CompanionPreviewNetwork -Port $Port -HomeNetwork:$HomeNetwork -ListenAddress $ListenAddress
Enable-CompanionPreviewPhoneAccess -Network $companionNetwork -Port $Port -FirewallScript (Join-Path $PSScriptRoot 'Enable-PixelVaultCompanionPhone.ps1')
if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    $DataRoot = Join-Path $companionRepo 'PixelVaultCompanionData'
}
$env:Companion__DataRoot = [IO.Path]::GetFullPath($DataRoot)
$env:Companion__DemoEnabled = $Demo.IsPresent.ToString()
$env:Companion__AllowHttp = 'true'
$env:AllowedHosts=$companionNetwork.AllowedHosts
Write-Host "Local preview: http://127.0.0.1:$Port"
Write-Host "Data folder: $env:Companion__DataRoot"
if($companionNetwork.PhoneUrl){Write-Host "Phone on home Wi-Fi (HTTP preview): $($companionNetwork.PhoneUrl)"}
else{Write-Host 'This preview accepts connections only from this PC.'}
Write-Host 'Keep this window open while using Companion. First setup uses setup-token.txt in the data folder.'
& dotnet run --project (Join-Path $companionRepo 'src\PixelVault.Companion\PixelVault.Companion.csproj') -c Release --no-launch-profile --urls $companionNetwork.Urls
if ($LASTEXITCODE -ne 0) { throw "Companion exited with code $LASTEXITCODE." }
