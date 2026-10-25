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
    $element = Until {
        $e = Element $process $id
        if ($e -and $e.Current.IsEnabled) { return $e }
        if ($process.HasExited) { return $null }
        $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        $offscreen = $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
        if ($offscreen -and $offscreen.Current.IsOffscreen) {
            $parent = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($offscreen)
            while ($parent -and $parent.Current.ControlType -ne [Windows.Automation.ControlType]::Window) {
                $scroll = $null
                if ($parent.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern,[ref]$scroll) -and $scroll.Current.VerticallyScrollable) {
                    $amount = if ($offscreen.Current.BoundingRectangle.Bottom -lt $window.Current.BoundingRectangle.Top + 60) { [Windows.Automation.ScrollAmount]::LargeDecrement } else { [Windows.Automation.ScrollAmount]::LargeIncrement }
                    $scroll.Scroll([Windows.Automation.ScrollAmount]::NoAmount,$amount)
                    break
                }
                $parent = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
            }
        }
    } ('enabled control ' + $id)
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
    $apiPath = Join-Path $install 'Server\Vatowiec.Api.exe'
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
    $password = if (Get-Variable fixturePassword -ErrorAction SilentlyContinue) { $fixturePassword } else { 'Fictional.Ui42test' }
    Text $desktop 'Password' $password
    $preferencesFile = Join-Path $root 'login-preferences.json'
    $hasDefault = $false
    if (Test-Path $preferencesFile) {
        $preferences = Get-Content $preferencesFile -Raw | ConvertFrom-Json
        $hasDefault = @($preferences.profiles | Where-Object defaultCompanyId).Count -gt 0
    }
    Click $desktop 'LoginButton'
    if ($hasDefault) {
        $null = Until { Element $desktop 'CurrentCompany' } 'persisted default company opens automatically'
        return
    }
    $null = Until { (Element $desktop 'Companies') -or (Element $desktop 'CurrentCompany') } 'company selection or configured default company'
    if (Element $desktop 'CurrentCompany') { return }
    $companies = Element $desktop 'Companies'
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $item = Until { $companies.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition) } 'company list item'
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Click $desktop 'SetDefaultCompany'
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
