$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$argsJson = $env:GRAVITY_TOOL_ARGS
$inputArgs = @{}
if ($argsJson) {
    $inputArgs = $argsJson | ConvertFrom-Json
}

$top = 10
if ($null -ne $inputArgs.top) { $top = [int]$inputArgs.top }
$folder = 'inbox'
if ($null -ne $inputArgs.folder) { $folder = $inputArgs.folder }

. "$PSScriptRoot\_get_token.ps1"
if (-not $script:access_token) { throw "Missing access token. Please connect your account." }

try {
    if ($script:provider -eq 'MICROSOFT_GRAPH') {
        $headers = @{ Authorization = "Bearer $script:access_token" }
        $uri = "https://graph.microsoft.com/v1.0/me/mailFolders/$folder/messages?`$top=$top&`$select=id,subject,from,receivedDateTime,isRead,bodyPreview"
        $response = Invoke-RestMethod -Uri $uri -Method GET -Headers $headers
        $emails = @()
        if ($response.value) {
            foreach ($msg in $response.value) {
                $emails += @{
                    Id = $msg.id
                    Subject = $msg.subject
                    From = $msg.from.emailAddress.address
                    Date = $msg.receivedDateTime
                    IsRead = $msg.isRead
                    Preview = $msg.bodyPreview
                }
            }
        }
        $emails | ConvertTo-Json -Depth 5
    } elseif ($script:provider -eq 'GMAIL_API') {
        $headers = @{ Authorization = "Bearer $script:access_token" }
        $q = "in:$folder"
        $uri = "https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=$top&q=$q"
        $response = Invoke-RestMethod -Uri $uri -Method GET -Headers $headers
        $messages = $response.messages
        if (-not $messages) {
            Write-Output "[]"
            exit 0
        }
        $emails = @()
        foreach ($msg in $messages) {
            $msgUri = "https://gmail.googleapis.com/gmail/v1/users/me/messages/$($msg.id)?format=metadata&metadataHeaders=Subject&metadataHeaders=From&metadataHeaders=Date"
            $msgDetail = Invoke-RestMethod -Uri $msgUri -Method GET -Headers $headers
            $subject = ($msgDetail.payload.headers | Where-Object name -eq 'Subject').value
            $from = ($msgDetail.payload.headers | Where-Object name -eq 'From').value
            $date = ($msgDetail.payload.headers | Where-Object name -eq 'Date').value
            $isRead = -not ($msgDetail.labelIds -contains 'UNREAD')
            $emails += @{
                Id = $msg.id
                Subject = $subject
                From = $from
                Date = $date
                IsRead = $isRead
                Preview = $msgDetail.snippet
            }
        }
        # Wrap in @() to force JSON array output even when only 1 email is returned.
        # Without this, PowerShell serializes a single hashtable as an object {}, not [{},]
        # which breaks the model's ability to extract the Id.
        @($emails) | ConvertTo-Json -Depth 5
    } else {
        throw "list_emails is not supported for SMTP. IMAP is required, which is not currently configured."
    }
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
