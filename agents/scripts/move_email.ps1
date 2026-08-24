$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$argsJson = $env:GRAVITY_TOOL_ARGS
if (-not $argsJson) { throw "Missing GRAVITY_TOOL_ARGS" }
$inputArgs = $argsJson | ConvertFrom-Json
$id = $inputArgs.id
$folder_id = $inputArgs.folder_id
if (-not $id -or -not $folder_id) { throw "Missing 'id' or 'folder_id' arguments." }
if ($id -is [array]) { $id = $id[0] }
$id = [uri]::EscapeDataString([string]$id)

try {
    $provider = $env:GRAVITY_EMAIL_PROVIDER
    $access_token = $env:GRAVITY_EMAIL_ACCESS_TOKEN
    if (-not $access_token) { throw "Missing access token." }

    if ($provider -eq 'MICROSOFT_GRAPH') {
        $headers = @{ Authorization = "Bearer $access_token" }
        $body = @{ destinationId = $folder_id } | ConvertTo-Json
        Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/me/messages/$id/move" -Method POST -Headers $headers -ContentType 'application/json' -Body $body
        Write-Output "Moved successfully to $folder_id."
    } elseif ($provider -eq 'GMAIL_API') {
        $headers = @{ Authorization = "Bearer $access_token" }
        $body = @{ addLabelIds = @($folder_id); removeLabelIds = @('INBOX') } | ConvertTo-Json
        Invoke-RestMethod -Uri "https://gmail.googleapis.com/gmail/v1/users/me/messages/$id/modify" -Method POST -Headers $headers -ContentType 'application/json' -Body $body
        Write-Output "Applied label $folder_id and removed from INBOX successfully."
    } else {
        throw "move_email is not supported natively for SMTP."
    }
} catch { Write-Error $_.Exception.Message; exit 1 }
