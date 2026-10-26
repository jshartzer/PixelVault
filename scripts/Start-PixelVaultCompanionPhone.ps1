param([int]$Port=8787,[string]$DataRoot='',[string]$ListenAddress='')
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Start-PixelVaultCompanion.ps1') -HomeNetwork @PSBoundParameters
