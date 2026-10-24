#Requires -Version 5.1
# Interactive Windows desktop required. This never changes existing SQL users.
[CmdletBinding()]
param([switch]$RunInstaller)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$release = Join-Path $repo 'artifacts\releases\0.1.0-preview'
$results = Join-Path $repo 'TestResults'
New-Item -ItemType Directory -Path $results -Force | Out-Null
$preferences = Join-Path $env:LOCALAPPDATA 'Vatowiec\Desktop\login-preferences.json'
function PreferenceHash {
    if (Test-Path -LiteralPath $preferences) { return (Get-FileHash -LiteralPath $preferences -Algorithm SHA256).Hash }
    return 'absent'
}
$originalHash = PreferenceHash
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$condition, [string]$description) {
    if (-not $condition) { throw "FAILED: $description" }
    $checks.Add("PASS: $description")
    Write-Output "PASS: $description"
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
public sealed class ReleaseFixtureTls : IDisposable {
    readonly TcpListener listener;
    readonly RSA rsa;
    readonly X509Certificate2 certificate;
    readonly Task work;
    public readonly int Port;
    public ReleaseFixtureTls() {
        rsa=RSA.Create(2048);
        var request=new CertificateRequest("CN=untrusted-release-fixture.invalid",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddMinutes(10));
        listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
        Port=((IPEndPoint)listener.LocalEndpoint).Port;
        work=Task.Run(async () => {
            try {
                using(var client=await listener.AcceptTcpClientAsync())
                using(var tls=new SslStream(client.GetStream(),false)) {
                    await tls.AuthenticateAsServerAsync(certificate,false,System.Security.Authentication.SslProtocols.Tls12,false);
                }
            } catch { /* Client must reject the untrusted certificate. No body is read. */ }
        });
    }
    public void Dispose() { listener.Stop(); work.Wait(3000); certificate.Dispose(); rsa.Dispose(); }
}
public static class ReleaseFixtureCredential {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct Credential {
        public uint Flags, Type; public string TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize; public IntPtr CredentialBlob;
        public uint Persist, AttributeCount; public IntPtr Attributes;
        public string TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool CredWriteW(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool CredReadW(string target, uint type, uint flags, out IntPtr value);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr value);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode)]
    static extern bool CredDeleteW(string target, uint type, uint flags);
    public static void Create(string target) {
        var data=Marshal.StringToCoTaskMemUni("fictional-release-fixture");
        try {
            var c=new Credential {Type=1, TargetName=target, UserName="release-fixture",
                CredentialBlob=data, CredentialBlobSize=50, Persist=2};
            if(!CredWriteW(ref c,0)) throw new InvalidOperationException("Fixture CredWrite failed");
        } finally { Marshal.ZeroFreeCoTaskMemUnicode(data); }
    }
    public static bool Exists(string target) {
        IntPtr value; if(!CredReadW(target,1,0,out value)) return false;
        CredFree(value); return true;
    }
    public static void Delete(string target) { CredDeleteW(target,1,0); }
}
'@
function FindElement($window, [string]$id) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $item = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $item) { throw "Missing UI element $id" }
    return $item
}
function SetValue($window, [string]$id, [string]$value) {
    $item = FindElement $window $id
    $pattern = $item.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($value)
}
function WaitStatus($window, [string]$expected) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        $status = (FindElement $window 'StatusMessage').Current.Name
        if ($status -match $expected) { return }
        Start-Sleep -Milliseconds 100
    } while ($watch.Elapsed.TotalSeconds -lt 25)
    throw "Expected UI status ($expected) not reached. No credentials or response body logged."
}
function StartClient([string]$exe) {
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $env:TEMP
    # A distinct origin prevents startup login with an existing user's profile.
    $start.EnvironmentVariables['VATOWIEC_API_URL'] = 'https://localhost:17243'
    $start.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $env:TEMP 'vatowiec-no-runtime-here'
    $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    return [Diagnostics.Process]::Start($start)
}
function TestClient([string]$exe, [string]$label) {
    $process = StartClient $exe
    try {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        do { Start-Sleep -Milliseconds 100; $process.Refresh() }
        while ($process.MainWindowHandle -eq [IntPtr]::Zero -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 15)
        Check (-not $process.HasExited -and $process.MainWindowHandle -ne [IntPtr]::Zero) "$label opens outside repo without dotnet run"
        $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        $watch.Restart()
        $addressInput = $null
        do {
            try { $addressInput = FindElement $window 'ApiAddress'; break } catch { Start-Sleep -Milliseconds 100 }
        } while ($watch.Elapsed.TotalSeconds -lt 15)
        Check ($null -ne $addressInput) "$label login screen available"
        SetValue $window 'ApiAddress' 'https://localhost:7243'
        SetValue $window 'Login' ('release-fixture-' + [Guid]::NewGuid().ToString('N'))
        SetValue $window 'Password' 'fictional-release-password'
        (FindElement $window 'LoginButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        WaitStatus $window 'Nieprawid'
        Check $true "$label uses trusted local HTTPS API and receives real authentication rejection for nonexistent fixture login"
        # Changing the origin clears the password, as ADR-032 requires.
        SetValue $window 'ApiAddress' 'http://localhost:7243'
        SetValue $window 'Password' 'fictional-release-password'
        (FindElement $window 'LoginButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        WaitStatus $window 'Podaj adres HTTPS'
        Check $true "$label API address editable and plain HTTP rejected"
        $tlsFixture = [ReleaseFixtureTls]::new()
        try {
            SetValue $window 'ApiAddress' ('https://localhost:' + $tlsFixture.Port)
            SetValue $window 'Password' 'fictional-release-password'
            (FindElement $window 'LoginButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            WaitStatus $window 'certyfikat'
            Check $true "$label rejects untrusted TLS certificate with certificate explanation"
        } finally { $tlsFixture.Dispose() }
        SetValue $window 'ApiAddress' 'https://localhost:17243'
        SetValue $window 'Password' 'fictional-release-password'
        (FindElement $window 'LoginButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        WaitStatus $window 'localhost oznacza'
        Check $true "$label explains unreachable API"
    } finally {
        if (-not $process.HasExited) {
            $process.CloseMainWindow() | Out-Null
            if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
        }
        $process.Dispose()
    }
    Check ((PreferenceHash) -eq $originalHash) "$label leaves existing preferences unchanged"
}

$credential = 'Vatowiec:ReleaseTest:' + [Guid]::NewGuid().ToString('N')
$installedByTest = $false
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\VATowiec'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{9F727694-49DB-480A-966A-BB51688C57E5}_is1'
$installer = Join-Path $release 'Vatowiec-0.1.0-preview-win-x64-Setup.exe'
function Install([string]$log) {
    $setup = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=desktopicon',('/LOG="'+(Join-Path $results $log)+'"')) -WindowStyle Hidden -PassThru -Wait
    Check ($setup.ExitCode -eq 0) "installer succeeds ($log)"
}
try {
    $manifest = Get-Content -LiteralPath (Join-Path $release 'payload-manifest.json') -Raw | ConvertFrom-Json
    $payload = [IO.Path]::GetFullPath((Join-Path $release 'publish'))
    foreach ($file in $manifest.files) {
        $full = [IO.Path]::GetFullPath((Join-Path $payload $file.path))
        if (-not $full.StartsWith($payload + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path.' }
        if ((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ne $file.sha256) { throw "Payload hash mismatch: $($file.path)" }
    }
    Check (@(Get-ChildItem -LiteralPath $payload -Recurse -File).Count -eq $manifest.files.Count) 'payload matches all manifest file hashes and count'
    Check ((Invoke-WebRequest -Uri 'https://localhost:7243/health' -UseBasicParsing).StatusCode -eq 200) 'existing Debug API responds with normal TLS validation'
    TestClient (Join-Path $release 'publish\Vatowiec.Desktop.exe') 'published EXE'
    if ($RunInstaller) {
        # Never upgrade or uninstall a pre-existing user's installation.
        if ((Test-Path -LiteralPath $installDir) -or (Test-Path $uninstallKey)) { throw 'Existing installation found. Use a separate Windows account for installer lifecycle tests.' }
        if (-not (Test-Path -LiteralPath $installer)) { throw 'Build installer first.' }
        $shell = New-Object -ComObject WScript.Shell
        $startLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'VATowiec.lnk'
        $desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'VATowiec.lnk'
        if ((Test-Path -LiteralPath $startLink) -or (Test-Path -LiteralPath $desktopLink)) { throw 'Existing shortcut found. Use a separate Windows account.' }
        [ReleaseFixtureCredential]::Create($credential)
        $installedByTest = $true
        Install 'release-install.txt'
        Check (Test-Path $uninstallKey) 'per-user Installed Apps entry exists'
        Check ((Get-ItemProperty $uninstallKey).DisplayVersion -eq '0.1.0-preview') 'installed version matches preview'
        foreach ($link in @($startLink,$desktopLink)) {
            Check (Test-Path -LiteralPath $link) 'requested shortcut exists'
            Check ($shell.CreateShortcut($link).TargetPath -eq (Join-Path $installDir 'Vatowiec.Desktop.exe')) 'shortcut points to installed client'
        }
        TestClient (Join-Path $installDir 'Vatowiec.Desktop.exe') 'installed EXE'
        $runningClient = StartClient (Join-Path $installDir 'Vatowiec.Desktop.exe')
        try {
            $watch = [Diagnostics.Stopwatch]::StartNew()
            do { Start-Sleep -Milliseconds 100; $runningClient.Refresh() }
            while ($runningClient.MainWindowHandle -eq [IntPtr]::Zero -and -not $runningClient.HasExited -and $watch.Elapsed.TotalSeconds -lt 15)
            Check (-not $runningClient.HasExited -and $runningClient.MainWindowHandle -ne [IntPtr]::Zero) 'client is running before upgrade test'
            Install 'release-reinstall.txt'
            Check ($runningClient.WaitForExit(5000)) 'installer closes running client through Restart Manager'
        } finally {
            if (-not $runningClient.HasExited) {
                $runningClient.CloseMainWindow() | Out-Null
                if (-not $runningClient.WaitForExit(5000)) { $runningClient.Kill(); $runningClient.WaitForExit() }
            }
            $runningClient.Dispose()
        }
        Check ((PreferenceHash) -eq $originalHash) 'reinstallation preserves existing preferences byte-for-byte'
        Check ([ReleaseFixtureCredential]::Exists($credential)) 'reinstallation preserves fixture Credential Manager entry'
        TestClient (Join-Path $installDir 'Vatowiec.Desktop.exe') 'reinstalled EXE'
        $uninstaller = Join-Path $installDir 'unins000.exe'
        $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $results 'release-uninstall.txt')+'"')) -WindowStyle Hidden -Wait -PassThru
        Check ($uninstall.ExitCode -eq 0) 'uninstaller succeeds'
        # Inno uninstaller may hand off to a temporary copy before it exits.
        $watch=[Diagnostics.Stopwatch]::StartNew()
        while ((Test-Path -LiteralPath (Join-Path $installDir 'Vatowiec.Desktop.exe')) -and $watch.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 100 }
        Check (-not (Test-Path -LiteralPath (Join-Path $installDir 'Vatowiec.Desktop.exe'))) 'installed client removed'
        Check (-not (Test-Path $uninstallKey)) 'Installed Apps entry removed'
        Check (-not (Test-Path -LiteralPath $startLink) -and -not (Test-Path -LiteralPath $desktopLink)) 'installer shortcuts removed'
        Check ((PreferenceHash) -eq $originalHash) 'uninstall preserves existing preferences byte-for-byte'
        Check ([ReleaseFixtureCredential]::Exists($credential)) 'uninstall preserves fixture Credential Manager entry'
        $installedByTest = $false
    }
} finally {
    [ReleaseFixtureCredential]::Delete($credential)
    $checks | Set-Content -LiteralPath (Join-Path $results 'release-preview-lifecycle.txt') -Encoding UTF8
    if ($installedByTest) { Write-Warning 'Test installed the client but did not finish uninstall. Inspect TestResults before manual cleanup.' }
}
