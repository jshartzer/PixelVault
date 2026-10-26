param([string]$OutputRoot='',[switch]$IncludeDesktop,[switch]$IncludeConnector)
$ErrorActionPreference='Stop'
$companionRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$companionProject=Join-Path $companionRepo 'src\PixelVault.Companion\PixelVault.Companion.csproj'
[xml]$companionProjectXml=Get-Content -LiteralPath $companionProject -Raw
$companionVersion=[string]$companionProjectXml.Project.PropertyGroup.Version
if([string]::IsNullOrWhiteSpace($OutputRoot)){$OutputRoot=Join-Path $companionRepo 'Companion Preview'}
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
$companionPersistentData=Join-Path $companionRepo 'PixelVaultCompanionData'
$companionApp=Join-Path $OutputRoot "app-$companionVersion"
$companionLinux=Join-Path $OutputRoot "linux-x64-$companionVersion"
& dotnet publish $companionProject -c Release -r win-x64 --self-contained false -o $companionApp --nologo
if($LASTEXITCODE -ne 0){throw 'Windows Companion publish failed. Stop an existing Companion preview before rebuilding.'}
& dotnet publish $companionProject -c Release -r linux-x64 --self-contained false -o $companionLinux --nologo
if($LASTEXITCODE -ne 0){throw 'Linux Companion publish failed.'}
if($IncludeConnector){
    & dotnet publish (Join-Path $companionRepo 'src\PixelVault.Companion.Connector\PixelVault.Companion.Connector.csproj') -c Release -r win-x64 --self-contained false -o (Join-Path $OutputRoot "connector-$companionVersion") --nologo
    if($LASTEXITCODE -ne 0){throw 'PC connector publish failed.'}
}

if($IncludeDesktop){
    $companionDesktop=Join-Path $OutputRoot "desktop-$companionVersion"
    & dotnet publish (Join-Path $companionRepo 'src\PixelVault.Native\PixelVault.Native.csproj') -c Release -r win-x64 --self-contained false -o $companionDesktop --nologo
    if($LASTEXITCODE -ne 0){throw 'Companion desktop preview publish failed.'}
    @'
$ErrorActionPreference='Stop'
$companionDesktopExe=Join-Path $PSScriptRoot 'desktop-__VERSION__\PixelVault.exe'
Start-Process -FilePath $companionDesktopExe -ArgumentList '--companion'
'@.Replace('__VERSION__',$companionVersion) | Set-Content -LiteralPath (Join-Path $OutputRoot 'Start-CompanionDesktop.ps1')
}

$companionSource=Join-Path $OutputRoot ("source-$companionVersion-"+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $companionSource -Force | Out-Null
foreach($projectName in @('PixelVault.Tracking','PixelVault.Companion')){
    $projectSource=Join-Path $companionRepo "src\$projectName"
    foreach($file in Get-ChildItem -LiteralPath $projectSource -Recurse -File){
        $relative=[IO.Path]::GetRelativePath($projectSource,$file.FullName)
        if($relative -match '(^|[\\/])(bin|obj|App_Data|keys)([\\/]|$)'){continue}
        if($file.Extension -notin @('.cs','.csproj','.json','.html','.css','.js','.png','.webmanifest','.md')){continue}
        if($relative.StartsWith('..')){throw 'Unexpected source path outside the selected project.'}
        $target=Join-Path $companionSource "src\$projectName\$relative"
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
New-Item -ItemType Directory -Path (Join-Path $companionSource 'deploy\companion') -Force | Out-Null
foreach($file in Get-ChildItem -LiteralPath (Join-Path $companionRepo 'deploy\companion') -File){
    if($file.Name -eq 'Dockerfile' -or $file.Extension -eq '.yml'){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $companionSource 'deploy\companion')}
}
Copy-Item -LiteralPath (Join-Path $companionRepo '.dockerignore') -Destination $companionSource
@"
PixelVault Companion $companionVersion — source build package

This package contains application source only. No personal tracker database, account map, passwords or keys are included.
From this extracted folder on a Docker host (including the NAS Docker CLI if available):
docker build --platform linux/amd64 -f deploy/companion/Dockerfile -t pixelvault-companion:$companionVersion .
An image has not been built or pushed by this packaging script.
Import/use the resulting image in Container Station; configure the separate persistent data mount and HTTPS/proxy before using personal data over the network.
Browser and the explicit desktop connection can edit the same isolated workspace. The separately installed PC connector supports automatic observations and indexed library sync; full original native tracker cutover remains separate work.
"@ | Set-Content -LiteralPath (Join-Path $companionSource 'README.txt')
$companionArchive=Join-Path $OutputRoot "PixelVaultCompanion-$companionVersion-source.zip"
Compress-Archive -Path (Join-Path $companionSource '*') -DestinationPath $companionArchive -Force

$launcher=@'
param([int]$Port=8787,[string]$DataRoot='__DATA_ROOT__',[switch]$HomeNetwork,[string]$ListenAddress='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CompanionPreview.Network.ps1')
$companionRelocated=Get-CompanionRelocatedUrl -DataRoot $DataRoot
if($companionRelocated){Write-Host "PixelVault Companion now runs on the NAS: $companionRelocated";Write-Host 'The former PC workspace is retained as a backup and was not started.';return}
$companionNetwork=Get-CompanionPreviewNetwork -Port $Port -HomeNetwork:$HomeNetwork -ListenAddress $ListenAddress
Enable-CompanionPreviewPhoneAccess -Network $companionNetwork -Port $Port -FirewallScript (Join-Path $PSScriptRoot 'Enable-PixelVaultCompanionPhone.ps1')
$env:Companion__DataRoot=[IO.Path]::GetFullPath($DataRoot)
$env:Companion__DemoEnabled='false'
$env:Companion__AllowHttp='true'
$env:AllowedHosts=$companionNetwork.AllowedHosts
$app=Join-Path $PSScriptRoot 'app-__VERSION__'
Write-Host "PixelVault Companion: http://127.0.0.1:$Port"
Write-Host "Owner setup token, if needed: $env:Companion__DataRoot\setup-token.txt"
if($companionNetwork.PhoneUrl){Write-Host "Phone on home Wi-Fi (HTTP preview): $($companionNetwork.PhoneUrl)"}
Write-Host 'Keep this window open while using Companion. Press Ctrl+C to stop it.'
Push-Location $app
try { & dotnet (Join-Path $app 'PixelVault.Companion.dll') --urls $companionNetwork.Urls }
finally { Pop-Location }
'@
$launcher.Replace('__VERSION__',$companionVersion).Replace('__DATA_ROOT__',$companionPersistentData.Replace("'","''"))|Set-Content -LiteralPath (Join-Path $OutputRoot 'Start-Companion.ps1')
foreach($companionNetworkScript in @('CompanionPreview.Network.ps1','Enable-PixelVaultCompanionPhone.ps1')){
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $companionNetworkScript) -Destination (Join-Path $OutputRoot $companionNetworkScript)
}
(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Start-PixelVaultCompanionPhone.ps1') -Raw).Replace("'Start-PixelVaultCompanion.ps1'","'Start-Companion.ps1'") | Set-Content -LiteralPath (Join-Path $OutputRoot 'Start-Companion-Phone.ps1')
[PSCustomObject]@{Version=$companionVersion;WindowsApp=$companionApp;LinuxApp=$companionLinux;SourceArchive=$companionArchive;Launcher=(Join-Path $OutputRoot 'Start-Companion.ps1')}
