#Requires -Version 7.0
param([Parameter(Mandatory)][string]$PythonExe)
$ErrorActionPreference='Stop'
$receiptRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$receiptRoot=Join-Path $receiptRepo ('tmp\map-receipt-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $receiptRoot | Out-Null
$receiptDatabase=Join-Path $receiptRoot 'fixture.sqlite'
$receiptFixture=@'
import sqlite3,json,sys
c=sqlite3.connect(sys.argv[1])
c.execute('CREATE TABLE IF NOT EXISTS companion_connector_activity(name TEXT,value TEXT,updated INTEGER)')
c.execute('DELETE FROM companion_connector_activity')
case=sys.argv[2]
if case!='missing':
 result={'Identity':'wrong' if case=='identity' else r'NT SERVICE\PixelVaultCompanion','ReadableMapFiles':3 if case=='files' else 4,'CacheWritable':case!='cache'}
 c.execute('INSERT INTO companion_connector_activity VALUES(?,?,?)',('local-map-access',json.dumps(result),99 if case=='stale' else 101))
c.commit()
'@
$receiptCases=@()
foreach($case in @('valid','missing','stale','identity','files','cache')){
 & $PythonExe -c $receiptFixture $receiptDatabase $case
 if($LASTEXITCODE -ne 0){throw 'Receipt fixture creation failed.'}
 $failed=$false;$result=$null
 try{$result=& (Join-Path $receiptRepo 'scripts\Test-PixelVaultMapServiceAccess.ps1') -DatabasePath $receiptDatabase -StartedTicks 100 -PythonExe $PythonExe -MinimumFiles 4 2> (Join-Path $receiptRoot ($case+'.log'))}
 catch{$failed=$true}
 if($case -eq 'valid'){
  if($failed -or $result.Identity -ne 'NT SERVICE\PixelVaultCompanion' -or $result.ReadableMapFiles -ne 4 -or -not $result.CacheWritable){throw 'The valid receipt was not accepted.'}
 }elseif(-not $failed){throw "The invalid $case receipt was accepted."}
 $receiptCases+=@{Scenario=$case;Passed=$true}
}
$receiptCases | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $receiptRoot 'results.json')
[pscustomobject]@{Passed=$true;Scenarios=$receiptCases.Count;Evidence=$receiptRoot} | ConvertTo-Json
