param([string]$PublishedApp='C:\Codex\Companion Preview\connector-0.16.0',[string]$Version='0.16.0',
      [string]$InstallRoot='C:\Codex\Companion Connector',[string]$DataRoot='C:\Codex\PixelVaultCompanionConnectorData',
      [string]$DesktopDataRoot='C:\Codex\PixelVaultData',[string]$Manifest='C:\Codex\PixelVaultCompanionData\workspace.json')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Use an explicit connector release version.'}
$install=[IO.Path]::GetFullPath($InstallRoot);$newApp=Join-Path $install "app-$Version";$config=Join-Path $DataRoot 'connector.json'
if(!(Test-Path -LiteralPath $config)){throw 'An existing private PC pairing is required.'}
if(Test-Path -LiteralPath $newApp){throw 'The new application directory already exists.'}
$stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ');$backup=Join-Path $DataRoot "backups\pre-$Version-$stamp"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $config -Destination (Join-Path $backup 'connector.json')
Copy-Item -LiteralPath (Join-Path $install 'Start-Connector.ps1') -Destination (Join-Path $backup 'Start-Connector.ps1')
New-Item -ItemType Directory -Path $newApp | Out-Null
Copy-Item -Path (Join-Path $PublishedApp '*') -Destination $newApp -Recurse
$newExe=Join-Path $newApp 'PixelVault.Companion.Connector.exe'
& $newExe --configure-automatic $config $DesktopDataRoot $Manifest
if($LASTEXITCODE -ne 0){throw 'The approved library could not be configured; the running connector was left alone.'}
$pidFile=Join-Path $DataRoot 'connector.pid'
if(Test-Path -LiteralPath $pidFile){
    $previous=Get-Process -Id ([int](Get-Content -LiteralPath $pidFile)) -ErrorAction SilentlyContinue
    if($previous){
        $exe=[IO.Path]::GetFullPath($previous.Path)
        if(!$exe.StartsWith($install+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($exe) -ne 'PixelVault.Companion.Connector.exe'){throw 'The recorded PID is not the installed connector.'}
        Stop-Process -Id $previous.Id -ErrorAction Stop
        $previous.WaitForExit(10000) | Out-Null
    }
}
$startPath=Join-Path $install 'Start-Connector.ps1'
$start=[IO.File]::ReadAllText($startPath)
$start=[regex]::Replace($start,'app-\d+\.\d+\.\d+',"app-$Version")
[IO.File]::WriteAllText($startPath,$start,[Text.UTF8Encoding]::new($false))
& $startPath
Write-Host "PC connector $Version installed; private pairing and Windows sign-in startup retained. Backup: $backup"
