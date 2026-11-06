#Requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$DatabasePath,
    [Parameter(Mandatory)][long]$StartedTicks,
    [Parameter(Mandatory)][string]$PythonExe,
    [Parameter(Mandatory)][int]$MinimumFiles,
    [string]$ExpectedIdentity='NT SERVICE\PixelVaultCompanion'
)
$ErrorActionPreference='Stop'
# The running service performs the check under its own identity and saves a fresh private receipt.
# The installer reads that receipt without process-token inspection or impersonation.
$mapAccessQuery="import pathlib,sqlite3,json,sys; c=sqlite3.connect(pathlib.Path(sys.argv[1]).as_uri()+'?mode=ro',uri=True); row=c.execute('SELECT value,updated FROM companion_connector_activity WHERE name=?',('local-map-access',)).fetchone(); assert row and row[1]>=int(sys.argv[2]),'A fresh map service access receipt is missing.'; result=json.loads(row[0]); assert result.get('Identity','').lower()==sys.argv[3].lower() and result.get('ReadableMapFiles',0)>=int(sys.argv[4]) and result.get('CacheWritable'),'The service could not verify access to saved maps and cache.'; print(json.dumps(result))"
$mapAccessResult=& $PythonExe -c $mapAccessQuery $DatabasePath $StartedTicks $ExpectedIdentity $MinimumFiles
if($LASTEXITCODE -ne 0){throw 'The running service did not report fresh access to the saved maps and cache.'}
$mapAccessResult | ConvertFrom-Json
