#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$NetworkHost,
    [Parameter(Mandatory)][string]$LocalAddress,
    [Parameter(Mandatory)][ValidateRange(1024,65535)][int]$NetworkPort,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$ExpectedThumbprint,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$ExpectedLocalThumbprint,
    [ValidateRange(1024,65535)][int]$LocalPort = 7243
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Net.ServicePointManager]::ServerCertificateValidationCallback) { throw 'Unexpected global TLS validation override.' }
$checks = [Collections.Generic.List[object]]::new()
function Check([bool]$value, [string]$description) {
    if (-not $value) { throw "FAILED: $description" }
    Write-Output "PASS: $description"
    $checks.Add(@{ check = $description; passed = $true })
}
function InspectTls([string]$address, [string]$serverName, [int]$port) {
    $tcp = [Net.Sockets.TcpClient]::new()
    try {
        if (-not $tcp.ConnectAsync($address,$port).Wait(5000)) { throw 'TLS connection timed out.' }
        $tls = [Net.Security.SslStream]::new($tcp.GetStream(),$false)
        try {
            $tls.ReadTimeout = $tls.WriteTimeout = 10000
            # No callback: Windows validates hostname, time, chain and revocation.
            $tls.AuthenticateAsClient($serverName,[Security.Cryptography.X509Certificates.X509CertificateCollection]::new(),[Security.Authentication.SslProtocols]::Tls12,$true)
            $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($tls.RemoteCertificate)
            try { return @{ subject = $cert.Subject; thumbprint = $cert.Thumbprint; expiresUtc = $cert.NotAfter.ToUniversalTime().ToString('o'); protocol = $tls.SslProtocol.ToString() } }
            finally { $cert.Dispose() }
        } finally { $tls.Dispose() }
    } finally { $tcp.Dispose() }
}
function TcpAccepts([string]$address, [int]$port) {
    $ip = [Net.IPAddress]::Parse($address)
    $tcp = [Net.Sockets.TcpClient]::new($ip.AddressFamily)
    try {
        try { return ($tcp.ConnectAsync($address,$port).Wait(1500) -and $tcp.Connected) }
        catch [AggregateException] { return $false }
    } finally { $tcp.Dispose() }
}
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$resultPath = Join-Path $repo 'TestResults\development-https.json'
New-Item -ItemType Directory -Path (Split-Path -Parent $resultPath) -Force | Out-Null
$result = @{ completed = $false; checks = $checks }
try {
    $dns = @([Net.Dns]::GetHostAddresses($NetworkHost) | ForEach-Object IPAddressToString)
    Check ($dns -contains $LocalAddress) 'internal DNS resolves to the intended LAN address'
    $localUri = "https://localhost:$LocalPort/health"
    $networkUri = "https://${NetworkHost}:$NetworkPort/health"
    Check ((Invoke-WebRequest -Uri $localUri -UseBasicParsing -TimeoutSec 15).StatusCode -eq 200) 'localhost HTTPS health returns 200 with normal TLS validation'
    Check ((Invoke-WebRequest -Uri $networkUri -UseBasicParsing -TimeoutSec 15).StatusCode -eq 200) 'LAN HTTPS health returns 200 with normal TLS validation'
    $result.localCertificate = InspectTls 'localhost' 'localhost' $LocalPort
    $result.networkCertificate = InspectTls $LocalAddress $NetworkHost $NetworkPort
    Check ($result.localCertificate.thumbprint -eq $ExpectedLocalThumbprint) 'localhost retains the original development certificate'
    Check ($result.networkCertificate.thumbprint -eq $ExpectedThumbprint) 'LAN certificate thumbprint matches the approved certificate'
    Check ($result.networkCertificate.subject -eq "CN=$NetworkHost") 'LAN certificate subject matches the server name'
    Check $true 'both TLS handshakes validate hostname, trust chain and revocation without bypass'
    $listeners = @(Get-NetTCPConnection -State Listen | Where-Object LocalPort -in @($LocalPort,$NetworkPort))
    $network = @($listeners | Where-Object LocalPort -eq $NetworkPort)
    Check ($network.Count -eq 1 -and $network[0].LocalAddress -eq $LocalAddress) 'LAN port listens only on the intended IPv4 address'
    $local = @($listeners | Where-Object LocalPort -eq $LocalPort)
    Check ($local.Count -ge 1 -and @($local | Where-Object LocalAddress -notin @('127.0.0.1','::1')).Count -eq 0) 'development HTTPS listens only on loopback'
    $processIds = @($listeners.OwningProcess | Select-Object -Unique)
    Check ($processIds.Count -eq 1) 'one API process owns both HTTPS endpoints'
    $result.apiPid = $processIds[0]
    $result.listeners = @($listeners | Select-Object LocalAddress,LocalPort,OwningProcess)
    $otherAddresses = @('127.0.0.1','::1') + @(Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -ne $LocalAddress -and $_.IPAddress -ne '127.0.0.1' } | ForEach-Object IPAddress)
    foreach ($address in $otherAddresses | Select-Object -Unique) { Check (-not (TcpAccepts $address $NetworkPort)) "LAN port does not accept TCP on $address" }

    # Correct SNI / strict TLS, deliberately wrong HTTP Host. Never disables TLS.
    $tcp = [Net.Sockets.TcpClient]::new($LocalAddress,$NetworkPort)
    try {
        $tls = [Net.Security.SslStream]::new($tcp.GetStream(),$false)
        try {
            $tls.ReadTimeout = $tls.WriteTimeout = 10000
            $tls.AuthenticateAsClient($NetworkHost)
            $request = [Text.Encoding]::ASCII.GetBytes("GET /health HTTP/1.1`r`nHost: not-allowed.invalid`r`nConnection: close`r`n`r`n")
            $tls.Write($request,0,$request.Length)
            $reader = [IO.StreamReader]::new($tls,[Text.Encoding]::ASCII,$false,1024,$true)
            try { $statusLine = $reader.ReadLine(); Check ($statusLine -match '^HTTP/1\.[01] 400 ') 'AllowedHosts rejects an unapproved HTTP host' }
            finally { $reader.Dispose() }
        } finally { $tls.Dispose() }
    } finally { $tcp.Dispose() }
    $result.completed = $true
} finally {
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resultPath -Encoding UTF8
}
