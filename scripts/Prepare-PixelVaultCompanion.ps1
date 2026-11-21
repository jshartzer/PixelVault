param(
    [string]$DesktopDataRoot = 'C:\Codex\PixelVaultData',
    [Parameter(Mandatory)][string]$DataRoot,
    [string]$AccountsFile = ''
)
$ErrorActionPreference='Stop'

. (Join-Path $PSScriptRoot "Assert-PixelVaultMaintainedSource.ps1")
Assert-PixelVaultMaintainedSource -RepositoryRoot (Join-Path $PSScriptRoot "..")
$companionProject=Join-Path $PSScriptRoot '..\src\PixelVault.Companion\PixelVault.Companion.csproj'
$companionArguments=@('run','--project',$companionProject,'-c','Release','--no-launch-profile','--','--prepare-workspace',
    [IO.Path]::GetFullPath($DesktopDataRoot),[IO.Path]::GetFullPath($DataRoot))
if(-not [string]::IsNullOrWhiteSpace($AccountsFile)) { $companionArguments+=@('--accounts-file',[IO.Path]::GetFullPath($AccountsFile)) }
& dotnet @companionArguments
if($LASTEXITCODE -ne 0){throw "Companion workspace preparation failed (exit $LASTEXITCODE)."}
