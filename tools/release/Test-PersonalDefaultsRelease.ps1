#Requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('Fresh','Upgrade','Both')][string]$Mode='Both', [string]$ReuseVerificationDirectory)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$testId=[guid]::NewGuid()
$name='VATowiec Personal test '+$testId.ToString('N')
$output=Join-Path $env:LOCALAPPDATA ('Vatowiec\Verification\ADR-041\'+$testId.ToString('N'))
if($ReuseVerificationDirectory){
    $output=[IO.Path]::GetFullPath($ReuseVerificationDirectory)
    $parent=Join-Path $env:LOCALAPPDATA 'Vatowiec\Verification\ADR-041'
    if([IO.Path]::GetDirectoryName($output) -ne $parent -or (Split-Path $output -Leaf) -notmatch '^[0-9a-f]{32}$'){throw 'Not an owned verification directory.'}
    $previous=Get-Content (Join-Path $output 'result.json') -Raw | ConvertFrom-Json
    $testId=[guid]::Parse($previous.testAppId)
    if($testId.ToString('N') -ne (Split-Path $output -Leaf)){throw 'Verification identity mismatch.'}
    $name='VATowiec Personal test '+$testId.ToString('N')
}
$installers=Join-Path $output 'Installers'
$iscc=Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
New-Item -ItemType Directory -Path $output -Force | Out-Null
function ExistingInstallationSnapshot {
    $snapshot=[ordered]@{}
    foreach($edition in @(@('Personal','C6DB8A6A-11F0-46EA-90DF-8F73D735B0B4'),@('Network','9F727694-49DB-480A-966A-BB51688C57E5'))){
        $key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{'+$edition[1]+'}_is1'
        if(Test-Path $key){
            $entry=Get-ItemProperty $key
            $values=[ordered]@{}
            $entry.PSObject.Properties | Where-Object Name -notlike 'PS*' | Sort-Object Name | ForEach-Object {$values[$_.Name]=$_.Value}
            $snapshot[$edition[0]+'Registration']=$values
            foreach($relative in @('Vatowiec.Desktop.exe','Vatowiec.Desktop.dll','Server\Vatowiec.Api.exe','Server\Vatowiec.Core.Infrastructure.dll')){
                $file=Join-Path $entry.InstallLocation $relative
                if(Test-Path $file){$snapshot[$file]=(Get-FileHash -LiteralPath $file).Hash}
            }
        }
    }
    foreach($location in @((Join-Path $env:LOCALAPPDATA 'Vatowiec\Personal'),(Join-Path $env:LOCALAPPDATA 'Vatowiec\Desktop'))){
        if(Test-Path $location){
            # Read/hash only; no credential enumeration or access to saved passwords.
            foreach($file in Get-ChildItem $location -Recurse -File | Where-Object {$_.Extension -in @('.json','.xml','.mdf','.ldf')}){
                $snapshot[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash
            }
        }
    }
    return ($snapshot | ConvertTo-Json -Depth 6 -Compress)
}
$before=ExistingInstallationSnapshot
$before | Set-Content (Join-Path $output 'existing-before.json') -Encoding UTF8
$completed=$false
$existingVerified=$false
try {
    $versions=if($Mode -eq 'Fresh'){@('0.1.1-preview')}else{@('0.1.0-preview','0.1.1-preview')}
    foreach($version in $versions){
        $release=Join-Path $repo ('artifacts\personal-releases\'+$version)
        $manifest=Get-Content (Join-Path $release 'payload-manifest.json') -Raw | ConvertFrom-Json
        foreach($file in $manifest.files){if((Get-FileHash -LiteralPath (Join-Path $release ('publish\'+$file.path))).Hash.ToLowerInvariant() -ne $file.sha256){throw 'Published payload changed; refusing verification.'}}
        $destination=Join-Path $installers $version
        if($ReuseVerificationDirectory){
            $file=Join-Path $destination ('Vatowiec-Personal-'+$version+'-win-x64-Setup.exe')
            $expected=(Get-Content (Join-Path $destination 'SHA256SUMS.txt') -TotalCount 1).Split(' ')[0]
            if((Get-FileHash $file).Hash.ToLowerInvariant() -ne $expected){throw 'Verification installer changed.'}
            continue
        }
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        # Test-only identity/shortcuts. EXACT production payloads; stable test AppId across old/new version.
        & $iscc "/DAppVersion=$version" ("/DFileVersion="+($version -split '-')[0]+'.0') ("/DPersonalAppId="+$testId.ToString().ToUpperInvariant()) "/DPersonalAppName=$name" `
            ("/DSourceDir="+(Join-Path $release 'publish')) "/DReleaseDir=$destination" (Join-Path $PSScriptRoot 'Vatowiec-Personal.iss') *> (Join-Path $output ('compile-'+$version+'.log'))
        if($LASTEXITCODE -ne 0){throw ('Verification installer compile failed: '+$version)}
        $file=Join-Path $destination ('Vatowiec-Personal-'+$version+'-win-x64-Setup.exe')
        ((Get-FileHash $file).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($file)) | Set-Content (Join-Path $destination 'SHA256SUMS.txt') -Encoding ASCII
    }
    foreach($scenario in $(if($Mode -eq 'Both'){@('Fresh','Upgrade')}else{@($Mode)})){
        $scenarioArguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'Test-PersonalRelease.ps1'),'-Version','0.1.1-preview',
            '-VerificationAppId',$testId.ToString().ToUpperInvariant(),'-VerificationAppName',$name,'-VerificationInstallers',$installers)
        if($scenario -eq 'Upgrade'){$scenarioArguments+=@('-UpgradeFromVersion','0.1.0-preview')}
        $commandLine=($scenarioArguments | ForEach-Object {'"'+$_+'"'}) -join ' '
        $scenarioProcess=Start-Process powershell.exe -WindowStyle Hidden -ArgumentList $commandLine -RedirectStandardOutput (Join-Path $output ($scenario+'.log')) -RedirectStandardError (Join-Path $output ($scenario+'.stderr.log')) -PassThru -Wait
        if($scenarioProcess.ExitCode -ne 0){throw ('Isolated installer scenario failed: '+$scenario+'. See '+$output)}
        Write-Output ('PASS: '+$scenario)
    }
    $after=ExistingInstallationSnapshot
    $after | Set-Content (Join-Path $output 'existing-after.json') -Encoding UTF8
    if($before -ne $after){throw 'Existing installations/data snapshot changed; inspect reports. No cleanup of user installations attempted.'}
    $existingVerified=$true
    Write-Output 'PASS: existing Personal and Network registration, binaries and data hashes unchanged'
    $completed=$true
} finally {
    @{completed=$completed;mode=$Mode;testAppId=$testId.ToString();testAppName=$name;existingInstallationsUnchanged=$existingVerified;finished=(Get-Date).ToString('o')} |
        ConvertTo-Json | Set-Content (Join-Path $output 'result.json') -Encoding UTF8
    Write-Output ('Report: '+$output)
}
