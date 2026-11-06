#Requires -Version 7.0
$ErrorActionPreference='Stop'
$planFile='C:\Codex\artifacts\native-tracker-completion-0114005\activation-plan.json'
$plan=Get-Content -LiteralPath $planFile -Raw | ConvertFrom-Json
if($plan.Purpose -ne 'native-tracker-completed-list-0.114.005'){throw 'Unexpected activation plan.'}
foreach($file in $plan.Files){if((Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash -ne $file.Sha256){throw "Prepared file changed: $($file.Path)"}}
$junctionPath='C:\Codex\dist\PixelVault-current';$newRoot='C:\Codex\dist\PixelVault-0.114.005';$oldRoot='C:\Codex\dist\PixelVault-0.114.000'
$junction=Get-Item -LiteralPath $junctionPath -Force
if($junction.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath([string]$junction.Target) -ne $oldRoot){throw 'The current build changed. Activation was stopped.'}
if([IO.Path]::GetFullPath($junctionPath) -ne 'C:\Codex\dist\PixelVault-current' -or [IO.Path]::GetFullPath($newRoot) -ne 'C:\Codex\dist\PixelVault-0.114.005'){throw 'Unexpected release paths.'}
$shell=New-Object -ComObject WScript.Shell
$links=@('C:\Codex\PixelVault.lnk','C:\Codex\PixelVault Companion.lnk') | Where-Object {Test-Path -LiteralPath $_}
foreach($path in $links){if($shell.CreateShortcut($path).TargetPath -ne ($oldRoot+'\PixelVault.exe')){throw 'An installed shortcut changed. Review before activation.'}}
# Remove the verified junction itself, without recursively touching either release.
Remove-Item -LiteralPath $junctionPath -Force
try {New-Item -ItemType Junction -Path $junctionPath -Target $newRoot | Out-Null}
catch {New-Item -ItemType Junction -Path $junctionPath -Target $oldRoot | Out-Null;throw}
foreach($path in $links){$link=$shell.CreateShortcut($path);$link.TargetPath=$newRoot+'\PixelVault.exe';$link.WorkingDirectory=$newRoot;$link.Save()}
$buildFile='C:\Codex\docs\CURRENT_BUILD.txt';$build=Get-Content -LiteralPath $buildFile -Raw
$build=$build.Replace('0.114.000','0.114.005');$build=($build -split '\r?\n' | Where-Object {-not $_.StartsWith('Prepared PC build:')}) -join [Environment]::NewLine
[IO.File]::WriteAllText($buildFile,$build)
$changelog='C:\Codex\docs\CHANGELOG.md';$change=(Get-Content -LiteralPath $changelog -Raw).Replace('## 0.114.005 — October 31, 2026 (prepared; activation pending)','## 0.114.005 — October 31, 2026');[IO.File]::WriteAllText($changelog,$change);Copy-Item -LiteralPath $changelog -Destination ($newRoot+'\CHANGELOG.md')
@{Activated=$true;Version='0.114.005';Executable=$newRoot+'\PixelVault.exe';ActivatedUtc=[DateTime]::UtcNow.ToString('o');ExistingWindowsClosed=$false} | ConvertTo-Json | Set-Content -LiteralPath 'C:\Codex\artifacts\native-tracker-completion-0114005\activation-result.json'
Write-Host 'PASS: PC build 0.114.005 activated. Close and reopen PixelVault through its normal shortcut.'
