[CmdletBinding()]
param([uri]$BaseUri = 'https://localhost:7243')

$ErrorActionPreference = 'Stop'
if ($BaseUri.Scheme -ne 'https' -or $BaseUri.Host -ne 'localhost') {
    throw 'Skrypt demonstracyjny wysyła dane logowania wyłącznie przez HTTPS do localhost.'
}
Add-Type -AssemblyName System.Net.Http
$secretPath = Join-Path $env:APPDATA 'Microsoft/UserSecrets/vatowiec-api-development/secrets.json'
$secrets = Get-Content -LiteralPath $secretPath -Raw -Encoding UTF8 | ConvertFrom-Json
$companyA = 'aaaaaaaa-0000-4000-8000-000000000001'
$companyB = 'bbbbbbbb-0000-4000-8000-000000000002'

function Invoke-Api {
    param($Client, [string]$Method, [string]$Path, $Body = $null)
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $Path)
    try {
        if ($null -ne $Body) {
            $request.Content = [System.Net.Http.StringContent]::new(($Body | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, 'application/json')
        }
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $data = if ([string]::IsNullOrWhiteSpace($text)) { $null } else { $text | ConvertFrom-Json }
            return [pscustomobject]@{ Status = [int]$response.StatusCode; Data = $data }
        }
        finally { $response.Dispose() }
    }
    finally { $request.Dispose() }
}

function Assert-Status {
    param($Result, [int]$Expected, [string]$Operation)
    if ($Result.Status -ne $Expected) { throw "$Operation : HTTP $($Result.Status), oczekiwano $Expected." }
    Write-Output "$Operation : HTTP $Expected"
}

function Set-Csrf {
    param($Client)
    $result = Invoke-Api $Client GET '/api/v1/auth/csrf'
    if ($result.Status -ne 200) { throw 'Nie pobrano tokenu CSRF.' }
    $Client.DefaultRequestHeaders.Remove('X-CSRF-TOKEN') | Out-Null
    $Client.DefaultRequestHeaders.Add('X-CSRF-TOKEN', [string]$result.Data.token)
}

$clients = @()
try {
    foreach ($kind in @('Operator', 'Reader')) {
        $name = $secrets.PSObject.Properties["DemoUsers:${kind}:UserName"].Value
        $password = $secrets.PSObject.Properties["DemoUsers:${kind}:Password"].Value
        if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($password)) {
            throw "Brak lokalnej konfiguracji DemoUsers:$kind w user-secrets API."
        }
        $handler = [System.Net.Http.HttpClientHandler]::new()
        $handler.AllowAutoRedirect = $false
        $handler.CookieContainer = [Net.CookieContainer]::new()
        $client = [System.Net.Http.HttpClient]::new($handler)
        $client.BaseAddress = $BaseUri
        $clients += $client
        Set-Csrf $client
        Assert-Status (Invoke-Api $client POST '/api/v1/auth/login' @{ userName = $name; password = $password }) 200 "$name logowanie"
        $password = $null
        $list = Invoke-Api $client GET '/api/v1/companies'
        Assert-Status $list 200 "$name lista firm"
        $expectedCount = if ($kind -eq 'Operator') { 2 } else { 1 }
        if (@($list.Data).Count -ne $expectedCount) { throw "Nieprawidłowa liczba firm konta $name." }
        Assert-Status (Invoke-Api $client GET "/api/v1/companies/$companyA/profile") 200 "$name profil A"
        $expectedB = if ($kind -eq 'Operator') { 200 } else { 403 }
        Assert-Status (Invoke-Api $client GET "/api/v1/companies/$companyB/profile") $expectedB "$name profil B"
        $expectedContractors = if ($kind -eq 'Operator') { 200 } else { 403 }
        Assert-Status (Invoke-Api $client GET "/api/v1/companies/$companyA/contractors") $expectedContractors "$name kontrahenci A"
        Assert-Status (Invoke-Api $client GET "/api/v1/companies/$companyA/profile?companyId=$companyB") 400 "$name manipulacja CompanyId"
    }
    # Oba konta są zalogowane jednocześnie; wylogowanie operatora nie usuwa sesji czytelnika.
    Set-Csrf $clients[0]
    Assert-Status (Invoke-Api $clients[0] POST '/api/v1/auth/logout') 204 'Wylogowanie operatora'
    Assert-Status (Invoke-Api $clients[0] GET '/api/v1/companies') 401 'Operator po wylogowaniu'
    Assert-Status (Invoke-Api $clients[1] GET "/api/v1/companies/$companyA/profile") 200 'Czytelnik nadal w firmie A'
    Set-Csrf $clients[1]
    Assert-Status (Invoke-Api $clients[1] POST '/api/v1/auth/logout') 204 'Wylogowanie czytelnika'
    Write-Output 'Próba dwóch użytkowników zakończona poprawnie; hasła, tokeny i cookie nie zostały wypisane.'
}
finally {
    foreach ($client in $clients) { $client.Dispose() }
    $secrets = $null
}
