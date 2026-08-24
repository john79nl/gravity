$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$argsJson = $env:GRAVITY_TOOL_ARGS
if (-not $argsJson) { throw "Missing GRAVITY_TOOL_ARGS" }
$inputArgs = $argsJson | ConvertFrom-Json
$id = $inputArgs.id
if (-not $id) { throw "Missing 'id' argument." }
if ($id -is [array]) { $id = $id[0] }
$id = [uri]::EscapeDataString([string]$id)

try {
    $provider = $env:GRAVITY_EMAIL_PROVIDER
    $access_token = $env:GRAVITY_EMAIL_ACCESS_TOKEN
    if (-not $access_token) { throw "Missing access token." }

    if ($provider -eq 'MICROSOFT_GRAPH') {
        $headers = @{ Authorization = "Bearer $access_token" }
        Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/me/messages/$id" -Method DELETE -Headers $headers
        Write-Output "Deleted successfully."
    } elseif ($provider -eq 'GMAIL_API') {
        $headers = @{ Authorization = "Bearer $access_token" }
        Invoke-RestMethod -Uri "https://gmail.googleapis.com/gmail/v1/users/me/messages/$id/trash" -Method POST -Headers $headers
        Write-Output "Moved to trash successfully."
    } else {
        throw "delete_email is not supported natively for SMTP."
    }
} catch { Write-Error $_.Exception.Message; exit 1 }
