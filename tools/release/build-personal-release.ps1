#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string]$Version = '0.1.1-preview',
    [string]$IsccPath,
    [switch]$PackageOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$releaseRoot = Join-Path $repo 'artifacts\personal-releases'
$release = [IO.Path]::GetFullPath((Join-Path $releaseRoot $Version))
$payload = Join-Path $release 'publish'
$fileVersion = ($Version -split '-')[0] + '.0'
if (-not $release.StartsWith($releaseRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid release path.' }
foreach ($directory in @((Join-Path $repo 'artifacts'),$releaseRoot,$release,$payload)) {
    if ((Test-Path -LiteralPath $directory) -and ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Release path must not contain links.' }
}
if (-not $IsccPath) { $IsccPath = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
if (-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) { throw 'Supply -IsccPath for Inno Setup >= 6.3.' }
New-Item -ItemType Directory -Path $release -Force | Out-Null
& (Join-Path $PSScriptRoot 'Get-LocalDb2025Bootstrapper.ps1') | Out-Null

function Add-DependencyLicenses([string]$project, [string]$destination, [string]$exeName) {
    $licenseDir = Join-Path $destination 'licenses'
    New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
    foreach ($fallback in @('Avalonia','MicroCom','Tmds.DBus')) {
        Copy-Item -LiteralPath (Join-Path $repo ('licenses\' + $fallback)) -Destination $licenseDir -Recurse -Force
    }
    $assets = Get-Content -LiteralPath (Join-Path $repo ($project + '\obj\project.assets.json')) -Raw | ConvertFrom-Json
    $roots = @($assets.packageFolders.PSObject.Properties.Name)
    $packages = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object Name)
    $runtime = Get-Content -LiteralPath (Join-Path $destination ($exeName + '.runtimeconfig.json')) -Raw | ConvertFrom-Json
    if (-not $runtime.runtimeOptions.includedFrameworks) { throw "$exeName is not self-contained." }
    foreach ($framework in $runtime.runtimeOptions.includedFrameworks) { $packages += "$($framework.name).Runtime.win-x64/$($framework.version)" }
    $notices = [Collections.Generic.List[string]]::new()
    $fallbacks = Get-Content -LiteralPath (Join-Path $repo 'licenses\Personal\sources.json') -Raw | ConvertFrom-Json
    $notices.Add("# $exeName $Version dependency notices`nLicenses copied unmodified from restored packages; build and cross-platform dependencies included conservatively.`n")
    foreach ($package in $packages | Sort-Object -Unique) {
        if ($package -match '^(Avalonia.BuildServices|Microsoft.EntityFrameworkCore.Design|Microsoft.NET.ILLink.Tasks|Microsoft.NET.Sdk.WebAssembly.Pack)/') { continue }
        $folder = $null
        foreach ($root in $roots) { $candidate = Join-Path $root $package.ToLowerInvariant(); if (Test-Path -LiteralPath $candidate) { $folder = $candidate; break } }
        if (-not $folder) { throw "Cannot locate dependency $package" }
        $nuspec = Get-ChildItem -LiteralPath $folder -Filter *.nuspec | Select-Object -First 1
        [xml]$metadata = Get-Content -LiteralPath $nuspec.FullName -Raw
        $license = $metadata.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="license"]')
        $notices.Add("## $package`nLicense metadata: $($license.InnerText)`n")
        $legal = @(Get-ChildItem -LiteralPath $folder -File | Where-Object Name -match '^(LICEN[CS]E|THIRD.PARTY.NOTICES|NOTICE|COPYING)(\..*)?$')
        if ($legal.Count) {
            $target = Join-Path $licenseDir ($package -replace '/', '-')
            New-Item -ItemType Directory -Path $target -Force | Out-Null
            $legal | Copy-Item -Destination $target -Force
        } elseif ($package -notmatch '^(Avalonia[^/]*|MicroCom.Runtime|Tmds.DBus.Protocol)/') {
            $entry = $fallbacks.PSObject.Properties[$package]
            if (-not $entry) { throw "No license text: $package. Pin the upstream license for this exact package version." }
            $source = Join-Path $repo ('licenses\Personal\' + $entry.Value.file)
            if ((Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant() -ne $entry.Value.sha256) { throw "License hash mismatch: $package" }
            $target = Join-Path $licenseDir ($package -replace '/', '-')
            New-Item -ItemType Directory -Path $target -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination (Join-Path $target 'LICENSE.txt') -Force
            $notices.Add("Source: $($entry.Value.source)`nSHA256: $($entry.Value.sha256)`n")
        }
    }
    [IO.File]::WriteAllText((Join-Path $licenseDir 'DEPENDENCIES.md'),($notices -join "`n"),[Text.UTF8Encoding]::new($false))
}

Push-Location $repo
try {
    if (-not $PackageOnly) {
        if (Test-Path -LiteralPath $payload) {
            # Exact release payload only; never touch data, bin/obj, other releases or running apps.
            if (@(Get-Process Vatowiec.Desktop,Vatowiec.Api -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($payload+'\',[StringComparison]::OrdinalIgnoreCase) }).Count) { throw 'Close this published application first.' }
            if (@(Get-ChildItem -LiteralPath $payload -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Payload contains a link.' }
            Remove-Item -LiteralPath $payload -Recurse -Force
        }
        foreach ($item in @(@('src/Desktop/Vatowiec.Desktop.csproj',$payload,'src\Desktop','Vatowiec.Desktop'),@('src/Server/Api/Vatowiec.Api.csproj',(Join-Path $payload 'Server'),'src\Server\Api','Vatowiec.Api'))) {
            dotnet publish $item[0] -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishAot=false -p:PublishSingleFile=false `
                -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -p:IncludeSourceRevisionInInformationalVersion=false -o $item[1]
            if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($item[0])" }
            Get-ChildItem -LiteralPath $item[1] -Recurse -File | Where-Object { $_.Extension -eq '.pdb' -or $_.Name -like 'appsettings*' -or $_.Name -eq 'web.config' } | Remove-Item -Force
            foreach ($required in @(($item[3]+'.exe'),'coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll')) {
                if (-not (Test-Path -LiteralPath (Join-Path $item[1] $required))) { throw "Missing $required" }
            }
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $payload 'Server\Vatowiec.Api.exe'))) { throw 'Publish both projects before packaging.' }
    Add-DependencyLicenses 'src\Desktop' $payload 'Vatowiec.Desktop'
    Add-DependencyLicenses 'src\Server\Api' (Join-Path $payload 'Server') 'Vatowiec.Api'
    $readme = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'PERSONAL-README.txt')).Replace('@VERSION@', $Version)
    [IO.File]::WriteAllText((Join-Path $payload 'PERSONAL-README.txt'), $readme, [Text.UTF8Encoding]::new($true))
    if (Test-Path -LiteralPath (Join-Path $repo 'LICENSE')) { Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $payload -Force }
    $forbidden = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object {
        $_.Name -match '(?i)(^appsettings|^secrets\.|connectionstrings|DatabaseTools|Tests\.)' -or $_.Extension -in @('.pdb','.cs','.sql','.bak','.mdf','.ldf','.pfx','.p12','.trx','.msi')
    })
    if ($forbidden.Count) { throw ('Forbidden payload files: '+($forbidden.Name -join ', ')) }
    $files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
        @{path=$_.FullName.Substring($payload.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
    @{version=$Version;edition='Personal';configuration='Release';platform='win-x64';selfContained=$true;trimmed=$false;aot=$false;
      sourceCommit=(git rev-parse HEAD);uncommittedChanges=(@(git status --porcelain).Count -ne 0);sdk=(dotnet --version);localDbBundled=$false;
      bootstrapperUrl='https://download.microsoft.com/download/2e652cd0-da06-4fc0-836a-b75ec45035a9/SQL2025-SSEI-Expr.exe';
      bootstrapperSha256='fa7e1fabc9a2e9c9cdab0d1512bcb30d2949133147db057ac530f510e5270680';files=$files} |
      ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $release 'payload-manifest.json') -Encoding UTF8
    & $IsccPath "/DAppVersion=$Version" "/DFileVersion=$fileVersion" "/DSourceDir=$payload" "/DReleaseDir=$release" (Join-Path $PSScriptRoot 'Vatowiec-Personal.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno compilation failed.' }
    $installer = Join-Path $release "Vatowiec-Personal-$Version-win-x64-Setup.exe"
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestHash = (Get-FileHash -LiteralPath (Join-Path $release 'payload-manifest.json')).Hash.ToLowerInvariant()
    @("$hash  $([IO.Path]::GetFileName($installer))", "$manifestHash  payload-manifest.json") | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ASCII
    Get-Item -LiteralPath $installer | Select-Object FullName,Length
    Write-Output "SHA256: $hash"
} finally { Pop-Location }
