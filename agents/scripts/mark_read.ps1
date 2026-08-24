$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$argsJson = $env:GRAVITY_TOOL_ARGS
if (-not $argsJson) { throw "Missing GRAVITY_TOOL_ARGS" }
$inputArgs = $argsJson | ConvertFrom-Json
$id = $inputArgs.id
$isRead = $true
if ($null -ne $inputArgs.is_read) { $isRead = [System.Convert]::ToBoolean($inputArgs.is_read) }
if (-not $id) { throw "Missing 'id' argument." }
if ($id -is [array]) { $id = $id[0] }
# Trim whitespace only; do NOT regex-strip or URL-encode — it corrupts opaque API IDs.
$id = ([string]$id).Trim()

. "$PSScriptRoot\_get_token.ps1"
if (-not $script:access_token) { throw "Missing access token." }

try {
    if ($script:provider -eq 'MICROSOFT_GRAPH') {
        $headers = @{ Authorization = "Bearer $script:access_token" }
        $body = @{ isRead = $isRead } | ConvertTo-Json
        Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/me/messages/$id" -Method PATCH -Headers $headers -ContentType 'application/json' -Body $body
        Write-Output "Marked read status: $isRead"
    } elseif ($script:provider -eq 'GMAIL_API') {
        $headers = @{ Authorization = "Bearer $script:access_token" }
        $body = $(if ($isRead) { @{ removeLabelIds = @('UNREAD') } } else { @{ addLabelIds = @('UNREAD') } }) | ConvertTo-Json
        Invoke-RestMethod -Uri "https://gmail.googleapis.com/gmail/v1/users/me/messages/$id/modify" -Method POST -Headers $headers -ContentType 'application/json' -Body $body
        Write-Output "Marked read status: $isRead"
    } else {
        throw "mark_read is not supported natively for SMTP."
    }
} catch { Write-Error $_.Exception.Message; exit 1 }
