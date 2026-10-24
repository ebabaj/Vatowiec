#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$localDb=Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue
if (-not $localDb) {
    throw 'LocalDB missing. Obtain SqlLocalDB.msi from official Microsoft SQL Server 2025 Express media and approve installation separately. See docs/personal-localdb.md. This script installs nothing.'
}
$versions=& $localDb.Source versions
if ($LASTEXITCODE -ne 0 -or ($versions -join ' ') -notmatch '17\.0') { throw 'SQL Server 2025 LocalDB (17.x) is required; no existing instance has been changed.' }
Push-Location $repo
try {
    # Only this guarded command, never the historical resetting SQL runner.
    dotnet run --project tools/database/Vatowiec.DatabaseTools -c Debug --no-build --no-launch-profile -- personal-prototype
    if ($LASTEXITCODE -ne 0) { throw 'Prototype stopped. Do not delete/reset an occupied instance or data directory; inspect the report.' }
} finally { Pop-Location }
