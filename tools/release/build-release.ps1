#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0-preview',
    [string]$IsccPath,
    [switch]$PublishOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts\releases'))
$releaseDir = [IO.Path]::GetFullPath((Join-Path $releaseRoot $Version))
$publishDir = Join-Path $releaseDir 'publish'
$fileVersion = ($Version -split '-')[0] + '.0'
if (-not $releaseDir.StartsWith($releaseRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid release path.' }
foreach ($directory in @((Join-Path $repo 'artifacts'), $releaseRoot, $releaseDir)) {
    if ((Test-Path -LiteralPath $directory) -and ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Release output parents cannot be links.'
    }
}

if (-not $PublishOnly) {
    if (-not $IsccPath) {
        $candidates = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')
        )
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $candidates = @($command.Source) + $candidates }
        $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
    if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
        throw 'Install Inno Setup >= 6.3: winget install --id JRSoftware.InnoSetup --exact --source winget ; or supply -IsccPath. -PublishOnly prepares the client without an installer.'
    }
}

Push-Location $repo
try {
    $gitStatus = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Git status.' }
    $commit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Git revision.' }
    if ($gitStatus.Count) { Write-Warning 'Building an uncommitted working tree. Review git status before sharing this preview.' }
    $sdk = (dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'A .NET 10 SDK is required.' }

    # Only remove the exact publish directory belonging to this release.
    # Refuse to touch links or an output still used by a running client.
    if (Test-Path -LiteralPath $publishDir) {
        $item = Get-Item -LiteralPath $publishDir
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publish directory cannot be a link.' }
        $running = Get-Process Vatowiec.Desktop -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($publishDir + '\', [StringComparison]::OrdinalIgnoreCase) }
        if ($running) { throw 'Close the published client before rebuilding this release.' }
        if (@(Get-ChildItem -LiteralPath $publishDir -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Publish tree cannot contain links.' }
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    dotnet publish src/Desktop/Vatowiec.Desktop.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishTrimmed=false -p:PublishAot=false -p:PublishSingleFile=false `
        -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" `
        "-p:FileVersion=$fileVersion" "-p:AssemblyVersion=$fileVersion" `
        -p:IncludeSourceRevisionInInformationalVersion=false -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'Client publish failed.' }
    # Native NuGet packages may ship large symbols despite DebugSymbols=false.
    Get-ChildItem -LiteralPath $publishDir -Recurse -File -Filter *.pdb | Remove-Item -Force

    $required = @('Vatowiec.Desktop.exe','Vatowiec.Desktop.dll','Vatowiec.SharedContracts.dll',
        'Vatowiec.Desktop.deps.json','Vatowiec.Desktop.runtimeconfig.json',
        'coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll',
        'Avalonia.Controls.dll','Avalonia.Win32.dll','Avalonia.Themes.Fluent.dll',
        'Avalonia.Win32.Automation.dll','SkiaSharp.dll','libSkiaSharp.dll','libHarfBuzzSharp.dll','av_libglesv2.dll')
    foreach ($name in $required) { if (-not (Test-Path -LiteralPath (Join-Path $publishDir $name))) { throw "Missing runtime dependency: $name" } }
    $runtime = Get-Content -LiteralPath (Join-Path $publishDir 'Vatowiec.Desktop.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (-not $runtime.runtimeOptions.includedFrameworks) { throw 'Publish is not self-contained.' }

    $licenseDir = Join-Path $publishDir 'licenses'
    Copy-Item -LiteralPath (Join-Path $repo 'licenses\Avalonia'),(Join-Path $repo 'licenses\MicroCom'),(Join-Path $repo 'licenses\Tmds.DBus') -Destination $licenseDir -Recurse -Force
    $assets = Get-Content -LiteralPath (Join-Path $repo 'src\Desktop\obj\project.assets.json') -Raw | ConvertFrom-Json
    $roots = @($assets.packageFolders.PSObject.Properties.Name)
    $packages = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name })
    $runtimeVersion = ($runtime.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
    $packages += "Microsoft.NETCore.App.Runtime.win-x64/$runtimeVersion"
    $notices = [Collections.Generic.List[string]]::new()
    $notices.Add("# VATowiec $Version - client dependency notices`n`nPackage licenses below are taken from the restored NuGet packages. Build-only and cross-platform managed dependencies are listed conservatively. Native assets are win-x64 only.`n")
    foreach ($package in $packages | Sort-Object -Unique) {
        if ($package -like 'Avalonia.BuildServices/*') { continue } # compiler only, not shipped
        $folder = $null
        foreach ($root in $roots) {
            $candidate = Join-Path $root $package.ToLowerInvariant()
            if (Test-Path -LiteralPath $candidate) { $folder = $candidate; break }
        }
        if (-not $folder) { throw "Cannot find restored package: $package" }
        $nuspec = Get-ChildItem -LiteralPath $folder -Filter *.nuspec | Select-Object -First 1
        [xml]$metadata = Get-Content -LiteralPath $nuspec.FullName -Raw
        $m = $metadata.package.metadata
        $notices.Add("## $package`nAuthors: $($m.authors)`nLicense: $($m.license.InnerText)`nRepository: $($m.repository.url)`nCommit: $($m.repository.commit)`n")
        $legalFiles = @(Get-ChildItem -LiteralPath $folder -File | Where-Object Name -match '^(LICEN[CS]E|THIRD.PARTY.NOTICES|NOTICE|COPYING)(\..*)?$')
        if ($legalFiles.Count) {
            $destination = Join-Path $licenseDir ($package -replace '/', '-')
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            $legalFiles | Copy-Item -Destination $destination -Force
        } elseif ($package -notmatch '^(Avalonia[^/]*|MicroCom.Runtime|Tmds.DBus.Protocol)/') {
            throw "No bundled license text for $package. Review its license before releasing."
        }
    }
    $notices.Add('Avalonia license: Avalonia/LICENSE.txt; MicroCom.Runtime: MicroCom/LICENSE.txt; Tmds.DBus.Protocol: Tmds.DBus/LICENSE.txt. Upstream sources and pinned revisions: docs/windows-release.md in the source repository. IbanNet is used by the server, not this client; the pre-existing project notice is retained separately.')
    [IO.File]::WriteAllText((Join-Path $licenseDir 'CLIENT-DEPENDENCIES.md'), ($notices -join "`n"), [Text.UTF8Encoding]::new($false))
    $clientReadme = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CLIENT-README.txt')).Replace('@VERSION@', $Version)
    [IO.File]::WriteAllText((Join-Path $publishDir 'CLIENT-README.txt'), $clientReadme, [Text.UTF8Encoding]::new($false))

    $forbidden = @(Get-ChildItem -LiteralPath $publishDir -Recurse -File | Where-Object {
        $_.Name -match '(?i)(^appsettings|secrets|connectionstrings|^Vatowiec\.(Api|Core|Tests|DatabaseTools)|^Microsoft\.(Data\.SqlClient|EntityFrameworkCore))' -or
        $_.Extension -in @('.pdb','.cs','.axaml','.sql','.bak','.mdf','.ldf','.pfx','.p12','.trx')
    })
    if ($forbidden.Count) { throw ('Forbidden payload files: ' + ($forbidden.Name -join ', ')) }
    $files = @(Get-ChildItem -LiteralPath $publishDir -Recurse -File | Sort-Object FullName | ForEach-Object {
        @{ path = $_.FullName.Substring($publishDir.Length + 1).Replace('\','/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifest = @{ version = $Version; platform = 'win-x64'; configuration = 'Release'; selfContained = $true; trimmed = $false; aot = $false; singleFile = $false; sdk = $sdk; sourceCommit = $commit; uncommittedChanges = ($gitStatus.Count -ne 0); files = $files }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $releaseDir 'payload-manifest.json') -Encoding UTF8
    if ($PublishOnly) { Write-Output "Client ready: $publishDir (installer not built)"; return }

    & $IsccPath "/DAppVersion=$Version" "/DFileVersion=$fileVersion" "/DSourceDir=$publishDir" "/DReleaseDir=$releaseDir" (Join-Path $PSScriptRoot 'Vatowiec.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
    $installer = Join-Path $releaseDir "Vatowiec-$Version-win-x64-Setup.exe"
    if (-not (Test-Path -LiteralPath $installer)) { throw 'Inno Setup did not produce the installer.' }
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath (Join-Path $releaseDir 'SHA256SUMS.txt') -Encoding ASCII
    Write-Output "Installer: $installer"
    Write-Output "SHA256: $hash"
} finally { Pop-Location }
