# Run against the already-started local backend. Credentials and tokens stay local.
[CmdletBinding()]
param(
    [switch]$CopyTokenToClipboard
)

$ErrorActionPreference = 'Stop'
$apiBase = 'https://localhost:7145'

function Invoke-LocalApi {
    param(
        [string]$Method,
        [string]$Path,
        [string]$JsonBody,
        [hashtable]$Headers = @{}
    )

    $request = @{
        Uri = $apiBase + $Path
        Method = $Method
        Headers = $Headers
        UseBasicParsing = $true
        TimeoutSec = 20
        ErrorAction = 'Stop'
    }
    if ($JsonBody) {
        $request.ContentType = 'application/json; charset=utf-8'
        $request.Body = [Text.Encoding]::UTF8.GetBytes($JsonBody)
    }

    try {
        $response = Invoke-WebRequest @request
    }
    catch {
        # Do not print request arguments, passwords, token bodies or user records.
        if ($null -ne $_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            $challenge = $_.Exception.Response.Headers['WWW-Authenticate']
            throw ('{0} {1}: HTTP {2}. {3}' -f $Method, $Path, $status, $challenge)
        }
        throw ('{0} {1}: Verbindung fehlgeschlagen. Lokales Backend und HTTPS-Entwicklungszertifikat pruefen.' -f $Method, $Path)
    }

    Write-Host ('{0} {1}: HTTP {2}' -f $Method, $Path, [int]$response.StatusCode)
    return ($response.Content | ConvertFrom-Json)
}

$securePassword = $null
$loginJson = $null
$login = $null
$accessToken = $null
$authorization = $null

try {
    Write-Host ('Ziel: {0} (lokales Backend)' -f $apiBase)
    $email = Read-Host 'E-Mail deines Eventlauscher-Kontos'
    $securePassword = Read-Host 'Passwort deines Eventlauscher-Kontos' -AsSecureString
    $credential = New-Object System.Management.Automation.PSCredential($email, $securePassword)
    $loginJson = @{
        email = $email
        password = $credential.GetNetworkCredential().Password
    } | ConvertTo-Json -Compress

    $login = Invoke-LocalApi -Method POST -Path '/auth/login' -JsonBody $loginJson
    $loginJson = $null
    $credential = $null
    $accessToken = $login.accessToken
    if ([string]::IsNullOrWhiteSpace($accessToken)) {
        throw 'Die lokale Login-Antwort enthaelt keinen accessToken.'
    }

    $authorization = @{ Authorization = 'Bearer ' + $accessToken }
    $null = Invoke-LocalApi -Method GET -Path '/auth/me' -Headers $authorization
    $null = Invoke-LocalApi -Method GET -Path '/api/Events/my' -Headers $authorization
    Write-Host 'Anmeldung und geschuetzte Event-Abfrage erfolgreich. Es wurde kein Event angelegt.'

    if ($CopyTokenToClipboard) {
        Set-Clipboard -Value $accessToken
        Write-Host 'Der gepruefte accessToken ist jetzt in der Zwischenablage.'
        Write-Host 'In Swagger: Authorize -> Logout -> Token aus Zwischenablage einfuegen -> Authorize.'
        Write-Host 'Kein zusaetzliches Bearer und keine Anfuehrungszeichen eingeben.'
    }
}
finally {
    if ($null -ne $securePassword) { $securePassword.Dispose() }
    $securePassword = $null
    $credential = $null
    $loginJson = $null
    $login = $null
    $accessToken = $null
    $authorization = $null
}
