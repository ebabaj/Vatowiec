#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$url='https://download.microsoft.com/download/2e652cd0-da06-4fc0-836a-b75ec45035a9/SQL2025-SSEI-Expr.exe'
$expected='FA7E1FABC9A2E9C9CDAB0D1512BCB30D2949133147DB057AC530F510E5270680'
$directory=Join-Path $env:LOCALAPPDATA 'Vatowiec\Downloads\SQL2025'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$file=Join-Path $directory 'SQL2025-SSEI-Expr.exe'
if (-not (Test-Path -LiteralPath $file)) { Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing }
if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expected) { throw 'Microsoft bootstrapper hash changed. Review the official download and pin before rebuilding. Nothing executed.' }
$signature=Get-AuthenticodeSignature -LiteralPath $file
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '^CN=Microsoft Corporation,') { throw 'Microsoft bootstrapper signature is not trusted. Nothing executed.' }
# Download/verification only. This script never launches or installs LocalDB.
Write-Output $file
