#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Version='0.1.1-preview',
    [string]$UpgradeFromVersion,
    [ValidatePattern('^[0-9a-fA-F-]{36}$')][string]$VerificationAppId,
    [ValidatePattern('^VATowiec Personal test [0-9a-f]{32}$')][string]$VerificationAppName,
    [string]$VerificationInstallers
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PersonalVerificationWindows {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
}
'@
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$release=Join-Path $repo ('artifacts\personal-releases\'+$Version)
$installer=Join-Path $release ('Vatowiec-Personal-'+$Version+'-win-x64-Setup.exe')
$initialVersion=if($UpgradeFromVersion){$UpgradeFromVersion}else{$Version}
$initialInstaller=Join-Path $repo ('artifacts\personal-releases\'+$initialVersion+'\Vatowiec-Personal-'+$initialVersion+'-win-x64-Setup.exe')
if($VerificationInstallers){
    if(-not $VerificationAppId -or -not $VerificationAppName){throw 'Verification installers require their own AppId and shortcut name.'}
    $installer=Join-Path $VerificationInstallers ($Version+'\Vatowiec-Personal-'+$Version+'-win-x64-Setup.exe')
    $initialInstaller=Join-Path $VerificationInstallers ($initialVersion+'\Vatowiec-Personal-'+$initialVersion+'-win-x64-Setup.exe')
}
$fixturePassword=if($initialVersion -eq '0.1.0-preview'){'Fictional.Ui42test'}else{'a'}
$id=[guid]::NewGuid().ToString('N')
$results=Join-Path $env:LOCALAPPDATA ('Vatowiec\Verification\ADR-040\'+$id)
$install=Join-Path $results 'Program'
$exe=Join-Path $install 'Vatowiec.Desktop.exe'
$root=Join-Path $env:LOCALAPPDATA ('Vatowiec\Personal.WizardTest.'+$id)
$key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{C6DB8A6A-11F0-46EA-90DF-8F73D735B0B4}_is1'
$menu=Join-Path ([Environment]::GetFolderPath('Programs')) 'VATowiec Personal.lnk'
$icon=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'VATowiec Personal.lnk'
if($VerificationAppId){
    $key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{'+$VerificationAppId+'}_is1'
    $menu=Join-Path ([Environment]::GetFolderPath('Programs')) ($VerificationAppName+'.lnk')
    $icon=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) ($VerificationAppName+'.lnk')
}
if ((Test-Path $key) -or (Test-Path $menu) -or (Test-Path $icon) -or (Test-Path $install) -or (Test-Path $root)) {
    throw 'Existing Personal installation/shortcut/fixture detected. Use a separate Windows test account. Nothing changed.'
}
$networkKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{9F727694-49DB-480A-966A-BB51688C57E5}_is1'
function NetworkRegistration {
    if(-not(Test-Path $networkKey)){return ''}
    $snapshot=[ordered]@{}
    (Get-ItemProperty $networkKey).PSObject.Properties | Where-Object Name -notlike 'PS*' | Sort-Object Name | ForEach-Object {$snapshot[$_.Name]=$_.Value}
    return ($snapshot | ConvertTo-Json -Depth 4 -Compress)
}
$networkBefore=NetworkRegistration
$networkPid=@(Get-NetTCPConnection -State Listen -LocalPort 7243 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
$checks=[Collections.Generic.List[string]]::new()
$owned=[Collections.Generic.List[Diagnostics.Process]]::new()
$completed=$false
New-Item -ItemType Directory -Path $results -Force | Out-Null
$networkBefore | Set-Content (Join-Path $results 'network-registration-before.json') -Encoding UTF8
. (Join-Path $PSScriptRoot 'Personal-UiTestHelpers.ps1')
. (Join-Path $PSScriptRoot 'Personal-FixtureSnapshot.ps1')
function InstallFixture([string]$name, [switch]$Initial) {
    $setup=if($Initial){$initialInstaller}else{$installer}
    $expectedVersion=if($Initial){$initialVersion}else{$Version}
    $p=Start-Process -FilePath $setup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$install+'"'),'/TASKS=desktopicon',('/LOG="'+(Join-Path $results ($name+'.log'))+'"') -Wait -PassThru
    Check ($p.ExitCode -eq 0) ('installer exit 0: '+$name)
    $registration=Get-ItemProperty $key
    Check ($registration.InstallLocation.TrimEnd('\') -eq $install) 'Personal registration points only to the isolated installation'
    Check ($registration.DisplayVersion -eq $expectedVersion) ('Windows registration version is '+$expectedVersion)
}
function UninstallFixture {
    $registration=Get-ItemProperty $key
    if ($registration.InstallLocation.TrimEnd('\') -ne $install) { throw 'Uninstall target changed; refusing.' }
    $uninstaller=Join-Path $install 'unins000.exe'
    if (-not $uninstaller.StartsWith($results+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe uninstall path.' }
    $p=Start-Process $uninstaller -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $results 'uninstall.log')+'"') -Wait -PassThru
    Check ($p.ExitCode -eq 0 -and -not (Test-Path $key)) 'uninstaller removes only its own registration'
}
function PreservedFiles {
    $values=@{}
    foreach($file in @(Get-Item (Join-Path $root 'installation.json'),(Join-Path $root 'login-preferences.json'))+@(Get-ChildItem (Join-Path $root 'Keys') -Recurse -File)) {
        $values[$file.FullName]=(Get-FileHash $file.FullName).Hash
    }
    return $values
}
function ComparePreserved($expected) {
    foreach($file in $expected.Keys){ Check ((Test-Path $file) -and (Get-FileHash $file).Hash -eq $expected[$file]) 'manifest, preferences and DP key file is preserved byte for byte' }
    Check (@(Get-ChildItem (Join-Path $root 'SqlData') -Filter *.mdf).Count -eq 2) 'both LocalDB database files are retained'
}
try {
    Check (Test-Path $installer) 'Personal EXE exists'
    $checksumDirectory=if($VerificationInstallers){Split-Path $installer}else{$release}
    $expected=(Get-Content (Join-Path $checksumDirectory 'SHA256SUMS.txt') -TotalCount 1).Split(' ')[0]
    Check ((Get-FileHash $installer).Hash.ToLowerInvariant() -eq $expected) 'installer SHA256 matches release manifest'
    InstallFixture 'install' -Initial
    Check ((Test-Path $menu) -and (Test-Path $icon)) 'Start menu and desktop shortcuts are created'
    $shellObject=New-Object -ComObject WScript.Shell
    foreach($path in @($menu,$icon)){
        $link=$shellObject.CreateShortcut($path)
        Check ($link.TargetPath -eq $exe -and $link.Arguments -eq '--personal') 'installed shortcut selects Personal, not Network'
    }
    $initialRelease=Join-Path $repo ('artifacts\personal-releases\'+$initialVersion)
    $manifest=Get-Content (Join-Path $initialRelease 'payload-manifest.json') -Raw | ConvertFrom-Json
    foreach($file in $manifest.files){
        $path=Join-Path $install $file.path
        if (-not (Test-Path $path) -or (Get-FileHash $path).Hash.ToLowerInvariant() -ne $file.sha256){throw ('Installed payload mismatch: '+$file.path)}
    }
    Check ($true) 'every installed payload file matches the publish manifest'
    $shortcut=Join-Path $results 'Personal-isolated.lnk'
    $link=$shellObject.CreateShortcut($shortcut); $link.TargetPath=$exe
    $link.Arguments='--personal --personal-data-root "'+$root+'"'; $link.WorkingDirectory=$install; $link.Save()
    $desktop=StartFixture; $api=ApiFor $desktop
    Check ($api.Path -eq (Join-Path $install 'Server\Vatowiec.Api.exe')) 'published API runs outside repository from installed Server directory'
    Check (($desktop.Modules | Where-Object ModuleName -eq 'coreclr.dll').FileName -eq (Join-Path $install 'coreclr.dll')) 'Desktop uses its bundled .NET runtime'
    Check (($api.Modules | Where-Object ModuleName -eq 'coreclr.dll').FileName -eq (Join-Path $install 'Server\coreclr.dll')) 'API uses its bundled .NET runtime'
    Check (@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object OwningProcess -eq $api.Id).Count -eq 0) 'installed Personal API has no TCP listener'
    $null=Until { $e=Element $desktop 'InitializePersonal'; $e -and $e.Current.IsEnabled } 'LocalDB readiness'
    Text $desktop 'PersonalSetupLogin' 'personal.process.fixture'
    Text $desktop 'PersonalSetupPassword' $fixturePassword
    Text $desktop 'PersonalSetupPasswordConfirmation' $fixturePassword
    Text $desktop 'PersonalSetupCompany' 'Fikcyjna firma instalatora Personal'
    Click $desktop 'InitializePersonal'; Click $desktop 'OpenPersonalLogin'
    $null=Until { Element $desktop 'Login' } 'login'
    Text $desktop 'Login' 'personal.process.fixture'
    (Element $desktop 'RememberCredentials').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
    Login $desktop
    if($UpgradeFromVersion){
        Click $desktop 'InstallationSettings'; Click $desktop 'OpenPasswordPolicy'
        $special=Until { $e=Element $desktop 'PasswordRequireSpecial'; if($e -and $e.Current.IsEnabled){$e} } 'old policy checkbox'
        $special.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
        Click $desktop 'SavePasswordPolicy'
        $null=Until { $e=Element $desktop 'StatusMessage'; $e -and $e.Current.Name -like '*zapis*' } 'old API confirms individual policy edit'
        Click $desktop 'ClosePasswordPolicy'; Click $desktop 'CloseGusSettings'
    }
    Click $desktop 'NewContractor'; Text $desktop 'ContractorCode' 'PROCESS-FIXTURE'
    Text $desktop 'ContractorName' 'Fikcyjny kontrahent instalatora'; Click $desktop 'SaveContractor'
    $null=Until { $e=Element $desktop 'StatusMessage'; $e -and $e.Current.Name -like '*Zapis*' } 'confirmed API write'
    Check ($true) 'installed Avalonia wizard creates administrator/company, logs in and saves contractor through secured pipe API'
    $configuration=ReadFixtureConfiguration
    if(-not $UpgradeFromVersion){
        $p=$configuration.Policy[0]
        Check (-not($p.IsEnabled -or $p.RequireUppercase -or $p.RequireLowercase -or $p.RequireDigit -or $p.RequireSpecialCharacter -or $p.PreventPasswordReuse -or $p.EnforcePasswordExpiration) -and $p.MinimumLength -eq 1) 'fresh installed Personal accepts a one-character password and stores all password restrictions disabled'
        Check (@($configuration.Currencies).Count -eq 149 -and @($configuration.Currencies | Where-Object IsActive).Count -eq 1 -and ($configuration.Currencies | Where-Object IsActive).Code -eq 'PLN') 'fresh installed Personal retains all 149 currencies with only PLN active'
        Check (@($configuration.Currencies | Where-Object {$_.DownloadA -or $_.DownloadB -or $_.DownloadC}).Count -eq 0 -and $configuration.Rates[0].Count -eq 0) 'fresh company has no download flags or automatically fetched rates'
        Check (($configuration.Registries | Where-Object Source -eq 'MF').Environment -eq 'Production' -and ($configuration.Registries | Where-Object Source -eq 'VIES').Environment -eq 'Production' -and ($configuration.Registries | Where-Object Source -eq 'GUS').Environment -eq 'Test') 'fresh installed Personal has MF/VIES Production and GUS Test'
    }else{
        Check ($configuration.Policy[0].IsEnabled -and -not $configuration.Policy[0].RequireSpecialCharacter -and $configuration.PolicyAudit[0].Count -eq 1) 'old installation stores an individually edited policy through its API with audit'
    }
    CloseFixture $desktop
    Check ($api.WaitForExit(5000) -and $api.ExitCode -eq 0) 'installed desktop drains and stops its own API'
    $state=Get-Content (Join-Path $root 'installation.json') -Raw | ConvertFrom-Json
    $snapshot=PreservedFiles
    $prefs=Get-Content (Join-Path $root 'login-preferences.json') -Raw | ConvertFrom-Json
    Check ($prefs.profiles[0].rememberCredentials -and $prefs.profiles[0].userId -and $prefs.profiles[0].defaultCompanyId) 'preferences retain immutable user identity, default company and remember choice'
    $desktop=StartFixture; $api=ApiFor $desktop
    $null=Until { Element $desktop 'Login' } 'login after restart'
    $null=Until { $e=Element $desktop 'SavedPasswordStatus'; $e -and $e.Current.Name -like '*zapisane*' } 'Credential Manager saved credential indicator'
    Check ($true) 'Windows Credential Manager credential survives installed application restart'
    # Real reinstall while the old application is open must request WM_CLOSE and wait for its exact child API.
    InstallFixture 'reinstall-while-open'
    Check ($desktop.WaitForExit(5000) -and $api.WaitForExit(5000) -and $api.ExitCode -eq 0) 'reinstall gracefully closes installed client and its API without killing Network'
    ComparePreserved $snapshot
    $after=ReadFixtureConfiguration
    Check (($after | ConvertTo-Json -Depth 6 -Compress) -eq ($configuration | ConvertTo-Json -Depth 6 -Compress)) 'upgrade/reinstall preserves policy, registries, every currency version and audit counts'
    $desktop=StartFixture; $api=ApiFor $desktop; Login $desktop
    Click $desktop 'ShowContractors'
    $null=Until {
        $list=Element $desktop 'Contractors'; if(-not $list){return $false}
        $list.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'PROCESS-FIXTURE'))
    } 'contractor persisted after reinstall'
    Check ($true) 'reinstalled application skips wizard and reads the same company contractor'
    CloseFixture $desktop
    $snapshot=PreservedFiles
    UninstallFixture
    ComparePreserved $snapshot
    Check (-not (Test-Path $menu) -and -not (Test-Path $icon)) 'uninstall removes Personal shortcuts'
    InstallFixture 'install-after-uninstall'
    $desktop=StartFixture; $api=ApiFor $desktop
    $null=Until { Element $desktop 'Login' } 'login after uninstall/reinstall'
    $null=Until { $e=Element $desktop 'SavedPasswordStatus'; $e -and $e.Current.Name -like '*zapisane*' } 'saved credential after uninstall/reinstall'
    Check ($true) 'uninstall retains Credential Manager entry and usable original installation'
    UninstallFixture
    Check ($desktop.WaitForExit(5000) -and $api.WaitForExit(5000) -and $api.ExitCode -eq 0) 'uninstall gracefully closes open installed client and its own API'
    ComparePreserved $snapshot
    $networkAfter=NetworkRegistration
    $networkAfter | Set-Content (Join-Path $results 'network-registration-after.json') -Encoding UTF8
    Check ($networkAfter -eq $networkBefore) 'Network installer registration is unchanged'
    if($networkPid.Count -eq 1){
        $after=@(Get-NetTCPConnection -State Listen -LocalPort 7243,7443 | Select-Object -ExpandProperty OwningProcess -Unique)
        Check ($after.Count -eq 1 -and $after[0] -eq $networkPid[0]) 'Network keeps same PID and both listeners'
        foreach($url in @('https://localhost:7243/health','https://vatowiec.tenjin.eu:7443/health')){
            $status=& curl.exe --silent --show-error --noproxy '*' --output NUL --write-out '%{http_code}' $url
            Check ($LASTEXITCODE -eq 0 -and $status -eq '200') 'Network health HTTP 200 with normal TLS validation'
        }
    }
    $completed=$true
} finally {
    foreach($process in $owned){if(-not $process.HasExited){try{CloseFixture $process}catch{Write-Warning 'Fixture remains open; not killing unconfirmed processes.'}}}
    if(-not $completed -and (Test-Path $key)) {
        # Only the installation created by this exact run. Never clean another installation.
        $registration=Get-ItemProperty $key
        if($registration.InstallLocation.TrimEnd('\') -eq $install -and @($owned | Where-Object {-not $_.HasExited}).Count -eq 0){UninstallFixture}
    }
    @{completed=$completed;root=$root;install=$install;checks=$checks.ToArray();networkPid=$networkPid;finished=(Get-Date).ToString('o')} |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $results 'result.json') -Encoding UTF8
    Write-Output ('Report: '+(Join-Path $results 'result.json'))
}
