$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$argsJson = $env:GRAVITY_TOOL_ARGS
if (-not $argsJson) { throw "Missing GRAVITY_TOOL_ARGS" }
$inputArgs = $argsJson | ConvertFrom-Json
$query = $inputArgs.query
if (-not $query) { throw "Missing 'query' argument." }

. "$PSScriptRoot\_get_token.ps1"
if (-not $script:access_token) { throw "Missing access token." }

try {
    if ($script:provider -eq 'MICROSOFT_GRAPH') {
        $headers = @{ Authorization = "Bearer $script:access_token" }
        $uri = "https://graph.microsoft.com/v1.0/me/messages?`$search=`"$query`""
        $response = Invoke-RestMethod -Uri $uri -Method GET -Headers $headers
        $emails = @()
        if ($response.value) {
            foreach ($msg in $response.value) {
                $emails += @{ Id = $msg.id; Subject = $msg.subject; From = $msg.from.emailAddress.address; Date = $msg.receivedDateTime; Preview = $msg.bodyPreview }
            }
        }
        @($emails) | ConvertTo-Json -Depth 5
    } elseif ($script:provider -eq 'GMAIL_API') {
        $headers = @{ Authorization = "Bearer $script:access_token" }
        $uri = "https://gmail.googleapis.com/gmail/v1/users/me/messages?q=$query"
        $response = Invoke-RestMethod -Uri $uri -Method GET -Headers $headers
        $messages = $response.messages
        if (-not $messages) { Write-Output "[]"; exit 0 }
        $emails = @()
        foreach ($msg in $messages) {
            $msgUri = "https://gmail.googleapis.com/gmail/v1/users/me/messages/$($msg.id)?format=metadata&metadataHeaders=Subject&metadataHeaders=From&metadataHeaders=Date"
            $msgDetail = Invoke-RestMethod -Uri $msgUri -Method GET -Headers $headers
            $subject = ($msgDetail.payload.headers | Where-Object name -eq 'Subject').value
            $from = ($msgDetail.payload.headers | Where-Object name -eq 'From').value
            $date = ($msgDetail.payload.headers | Where-Object name -eq 'Date').value
            $emails += @{ Id = $msg.id; Subject = $subject; From = $from; Date = $date; Preview = $msgDetail.snippet }
        }
        @($emails) | ConvertTo-Json -Depth 5
    } else {
        throw "search_emails is not supported natively for SMTP."
    }
} catch { Write-Error $_.Exception.Message; exit 1 }
