param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$runtime = Join-Path $env:LOCALAPPDATA 'PixelVault\Xbox\venv'
& $Python -m venv $runtime
if ($LASTEXITCODE -ne 0) { throw 'Python 3.10 or newer is required to install Xbox support.' }
& (Join-Path $runtime 'Scripts\python.exe') -m pip install --disable-pip-version-check -r (Join-Path $PSScriptRoot '..\src\PixelVault.Native\Xbox\requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Xbox dependency installation failed.' }
Write-Output 'Xbox support installed. Open Achievement Hub > Xbox achievements in PixelVault.'
