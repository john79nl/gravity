$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$argsJson = $env:GRAVITY_TOOL_ARGS
if (-not $argsJson) { throw "Missing GRAVITY_TOOL_ARGS" }
$inputArgs = $argsJson | ConvertFrom-Json
$id = $inputArgs.id
if (-not $id) { throw "Missing email 'id' argument." }
if ($id -is [array]) { $id = $id[0] }
# Trim whitespace only — Gmail/Graph IDs are opaque strings; never regex-strip or URL-encode them.
$id = ([string]$id).Trim()

# Refresh the OAuth token if possible before making the API call
. "$PSScriptRoot\_get_token.ps1"
if (-not $script:access_token) { throw "Missing access token." }

try {
    if ($script:provider -eq 'MICROSOFT_GRAPH') {
        $headers  = @{ Authorization = "Bearer $script:access_token" }
        $uri      = "https://graph.microsoft.com/v1.0/me/messages/$id"
        $response = Invoke-RestMethod -Uri $uri -Method GET -Headers $headers
        $response | ConvertTo-Json -Depth 5
    } elseif ($script:provider -eq 'GMAIL_API') {
        $headers  = @{ Authorization = "Bearer $script:access_token" }
        $uri      = "https://gmail.googleapis.com/gmail/v1/users/me/messages/$id" + "?format=full"
        $response = Invoke-RestMethod -Uri $uri -Method GET -Headers $headers
        $response | ConvertTo-Json -Depth 10
    } else {
        throw "read_email is not supported natively for SMTP."
    }
} catch {
    $ex  = $_.Exception
    $msg = $ex.Message
    if ($ex.Response) {
        $reader = New-Object System.IO.StreamReader($ex.Response.GetResponseStream())
        $msg   += "`n" + $reader.ReadToEnd()
    }
    Write-Error $msg
    exit 1
}
