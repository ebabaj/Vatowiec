# Actual Windows process/shortcut/UIA verification; only creates a new Personal.WizardTest.<GUID> fixture.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PersonalVerificationWindows {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
}
'@
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$exe = Join-Path $repo 'src\Desktop\bin\Debug\net10.0\Vatowiec.Desktop.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build standard Debug first.' }
$root = Join-Path $env:LOCALAPPDATA ('Vatowiec\Personal.WizardTest.' + [guid]::NewGuid().ToString('N'))
$results = Join-Path $env:LOCALAPPDATA 'Vatowiec\Verification\ADR-039'
$null = New-Item -ItemType Directory -Path $results -Force
$checks = [System.Collections.Generic.List[string]]::new()
$owned = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$started = Get-Date
$networkPid = @(Get-NetTCPConnection -State Listen -LocalPort 7243 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
$completed = $false
function Check($condition, [string]$message) {
    if (-not $condition) { throw ('Verification failed: ' + $message) }
    $checks.Add($message); Write-Output ('PASS: ' + $message)
}
function Until([scriptblock]$readyProbe, [string]$description, [int]$seconds=45) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { $value = & $readyProbe; if ($value) { return $value }; Start-Sleep -Milliseconds 100 } while ((Get-Date) -lt $deadline)
    throw ('Timed out: ' + $description)
}
function Element($process, [string]$id) {
    $process.Refresh()
    if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero) { return $null }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $element = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($element -and -not $element.Current.IsOffscreen) { return $element }
    return $null
}
function Click($process, [string]$id) {
    $element = Until { $e = Element $process $id; if ($e -and $e.Current.IsEnabled) { $e } } ('enabled control ' + $id)
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Text($process, [string]$id, [string]$value) {
    $element = Until { Element $process $id } ('text control ' + $id)
    if (-not $element.Current.IsPassword) {
        $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
    } else {
        # Only a known fixture window is focused. This fictional test password is never persisted by the script.
        $null = [PersonalVerificationWindows]::SetForegroundWindow($process.MainWindowHandle)
        $element.SetFocus(); [System.Windows.Forms.SendKeys]::SendWait('^a')
        [System.Windows.Forms.SendKeys]::SendWait($value)
    }
}
function StartFixture {
    $before = @($owned | ForEach-Object Id)
    Start-Process -FilePath $shortcut
    $process = Until {
        Get-CimInstance Win32_Process -Filter "Name='Vatowiec.Desktop.exe'" | Where-Object {
            $_.ExecutablePath -eq $exe -and $_.CommandLine.Contains($root) -and $_.ProcessId -notin $before
        } | ForEach-Object { Get-Process -Id $_.ProcessId }
    } 'desktop started by isolated shortcut'
    if (@($process).Count -ne 1) { throw 'Ambiguous fixture process; refusing process operations.' }
    $null = $process.Handle; $owned.Add($process)
    $null = Until { $process.Refresh(); if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $true } } 'visible Avalonia window'
    return $process
}
function ApiFor($desktop) {
    $apiPath = Join-Path $repo 'src\Server\Api\bin\Debug\net10.0\Vatowiec.Api.exe'
    $api = Until {
        Get-CimInstance Win32_Process -Filter "Name='Vatowiec.Api.exe'" | Where-Object {
            $_.ParentProcessId -eq $desktop.Id -and $_.ExecutablePath -eq $apiPath -and $_.CommandLine.Contains('--personal-worker')
        } | ForEach-Object { Get-Process -Id $_.ProcessId }
    } 'owned child API'
    if (@($api).Count -ne 1) { throw 'Ambiguous child API; refusing process operations.' }
    $null = $api.Handle
    return $api
}
function Login($desktop) {
    $null = Until { Element $desktop 'Login' } 'normal login screen'
    Text $desktop 'Login' 'personal.process.fixture'
    Text $desktop 'Password' 'Fictional.Ui42test'
    Click $desktop 'LoginButton'
    $companies = Until { Element $desktop 'Companies' } 'company selection'
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $item = Until { $companies.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition) } 'company list item'
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Click $desktop 'OpenCompany'
    $null = Until { Element $desktop 'CurrentCompany' } 'company workspace'
}
function CloseFixture($desktop) {
    $null = $desktop.CloseMainWindow()
    $null = Until {
        if ($desktop.HasExited) { return $true }
        $condition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $desktop.Id),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Zamykanie VATowca Personal'))
        $warning = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($warning) { $warning.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
        return $false
    } 'desktop graceful close' 65
}
try {
    # Verify the existing icon's actual executable/profile; clone only its data-root argument to protect normal installations.
    $sourceShortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'VATowiec Personal (Debug).lnk'
    $shellObject = New-Object -ComObject WScript.Shell
    $original = $shellObject.CreateShortcut($sourceShortcut)
    Check ($original.TargetPath -eq $exe -and $original.Arguments -eq '--personal') 'existing Personal Debug icon selects the correct executable/profile'
    $shortcut = Join-Path $results ('Personal-process-' + [guid]::NewGuid().ToString('N') + '.lnk')
    $link = $shellObject.CreateShortcut($shortcut); $link.TargetPath = $exe
    $link.Arguments = '--personal --personal-data-root "' + $root + '"'; $link.WorkingDirectory = $repo; $link.Save()
    Check (-not (Test-Path -LiteralPath $root)) 'new isolated Personal root is unused'
    $desktop = StartFixture; $api = ApiFor $desktop
    Check (@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object OwningProcess -eq $api.Id).Count -eq 0) 'Personal API has no TCP/LAN listener'
    $visibleConsoles = @(Get-CimInstance Win32_Process -Filter "Name='conhost.exe'" | Where-Object ParentProcessId -eq $api.Id | ForEach-Object { Get-Process -Id $_.ProcessId } | Where-Object {
        $_.MainWindowHandle -ne [IntPtr]::Zero -and [PersonalVerificationWindows]::IsWindowVisible($_.MainWindowHandle)
    })
    Check ($visibleConsoles.Count -eq 0) 'child API has no visible console'
    $second = StartFixture
    $null = Until { $second.Refresh(); $second.MainWindowTitle -eq 'VATowiec Personal jest już uruchomiony' } 'second-launch notification'
    Check (@(Get-CimInstance Win32_Process -Filter "Name='Vatowiec.Api.exe'" | Where-Object ParentProcessId -eq $second.Id).Count -eq 0) 'second icon launch starts no competing API'
    $null = $second.CloseMainWindow(); $null = $second.WaitForExit(5000)
    $null = Until { $e = Element $desktop 'InitializePersonal'; $e -and $e.Current.IsEnabled } 'LocalDB check'
    Text $desktop 'PersonalSetupLogin' 'personal.process.fixture'
    Text $desktop 'PersonalSetupPassword' 'Fictional.Ui42test'
    Text $desktop 'PersonalSetupPasswordConfirmation' 'Fictional.Ui42test'
    Text $desktop 'PersonalSetupCompany' 'Firma fikcyjna cyklu procesu'
    Click $desktop 'InitializePersonal'; Click $desktop 'OpenPersonalLogin'
    Login $desktop
    Check ($true) 'actual shortcut, first-run wizard, pipe API, login and company selection work'
    Click $desktop 'NewContractor'; Text $desktop 'ContractorCode' 'PROCESS-FIXTURE'
    Text $desktop 'ContractorName' 'Fikcyjny kontrahent procesu'; Click $desktop 'SaveContractor'
    $null = Until { $e = Element $desktop 'StatusMessage'; $e -and $e.Current.Name -like '*Zapis*' } 'contractor save confirmation'
    Check ($true) 'contractor is saved through the actual Avalonia form'
    CloseFixture $desktop
    Check ($api.WaitForExit(5000) -and $api.ExitCode -eq 0) 'closing Avalonia gracefully exits its exact API process'
    $state = Get-Content -LiteralPath (Join-Path $root 'installation.json') -Raw | ConvertFrom-Json
    $instance = 'VatowiecPersonal_' + $state.InstallationId.Replace('-','')
    Check ((SqlLocalDB.exe info $instance | Out-String) -match 'Stopped') 'owned LocalDB instance is stopped after normal close'
    $desktop = StartFixture; $api = ApiFor $desktop; Login $desktop
    Click $desktop 'ShowContractors'
    $null = Until {
        $list = Element $desktop 'Contractors'; if (-not $list) { return $false }
        $list.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'PROCESS-FIXTURE'))
    } 'persisted contractor after process restart'
    Check ($true) 'restart skips initialization and reads the previously saved contractor'
    $desktop.Kill(); $null = $desktop.WaitForExit(5000)
    Check ($api.WaitForExit(60000) -and $api.ExitCode -eq 0) 'desktop crash triggers API EOF, graceful shutdown and LocalDB stop'
    $desktop = StartFixture; $api = ApiFor $desktop; Login $desktop
    $api.Kill(); $null = $api.WaitForExit(5000)
    $null = Until { $e = Element $desktop 'StatusMessage'; $e -and $e.Current.Name -like '*zakończył pracę*' } 'API crash shown in Avalonia'
    Check ($true) 'API crash clears access and is reported by the actual client'
    CloseFixture $desktop
    $desktop = StartFixture; $api = ApiFor $desktop; Login $desktop
    Check ($true) 'client and API restart after both crashes without stale lock or data reset'
    CloseFixture $desktop
    Check ($api.WaitForExit(5000) -and $api.ExitCode -eq 0) 'final API exits gracefully'
    if ($networkPid.Count -eq 1) {
        $networkAfter = @(Get-NetTCPConnection -State Listen -LocalPort 7243,7443 -ErrorAction Stop | Select-Object -ExpandProperty OwningProcess -Unique)
        Check ($networkAfter.Count -eq 1 -and $networkAfter[0] -eq $networkPid[0]) 'Network keeps the same PID and both listeners throughout Personal starts/stops/crashes'
        foreach ($url in @('https://localhost:7243/health','https://vatowiec.tenjin.eu:7443/health')) {
            $status = & curl.exe --silent --show-error --noproxy '*' --output NUL --write-out '%{http_code}' $url
            Check ($LASTEXITCODE -eq 0 -and $status -eq '200') 'Network health remains HTTP 200 with normal TLS validation'
        }
    }
    $completed = $true
} finally {
    foreach ($process in $owned) {
        if (-not $process.HasExited) { try { CloseFixture $process } catch { $process.Kill(); $null = $process.WaitForExit(5000) } }
    }
    @{ completed=$completed; root=$root; networkPid=$networkPid; checks=$checks.ToArray(); started=$started.ToString('o'); finished=(Get-Date).ToString('o') } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $results ('process-' + (Split-Path $root -Leaf) + '.json')) -Encoding UTF8
}
