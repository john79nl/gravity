$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$argsJson = $env:GRAVITY_TOOL_ARGS
if (-not $argsJson) { throw "Missing GRAVITY_TOOL_ARGS" }
$inputArgs = $argsJson | ConvertFrom-Json
$id = $inputArgs.id
$body = $inputArgs.body
if (-not $id -or -not $body) { throw "Missing 'id' or 'body' arguments." }
if ($id -is [array]) { $id = $id[0] }
$id = [uri]::EscapeDataString([string]$id)
$is_html = $false
if ($null -ne $inputArgs.is_html) { $is_html = [System.Convert]::ToBoolean($inputArgs.is_html) }

try {
    $provider = $env:GRAVITY_EMAIL_PROVIDER
    $access_token = $env:GRAVITY_EMAIL_ACCESS_TOKEN
    if (-not $access_token) { throw "Missing access token." }

    if ($provider -eq 'MICROSOFT_GRAPH') {
        $headers = @{ Authorization = "Bearer $access_token" }
        $msg = @{ message = @{ body = @{ contentType = $(if($is_html){'HTML'}else{'Text'}); content = $body } } }
        Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/me/messages/$id/reply" -Method POST -Headers $headers -ContentType 'application/json' -Body ($msg | ConvertTo-Json -Depth 10)
        Write-Output "Replied successfully."
    } elseif ($provider -eq 'GMAIL_API') {
        $headers = @{ Authorization = "Bearer $access_token" }
        
        $origUri = "https://gmail.googleapis.com/gmail/v1/users/me/messages/$id?format=metadata&metadataHeaders=Message-ID&metadataHeaders=Subject&metadataHeaders=References&metadataHeaders=From"
        $origMsg = Invoke-RestMethod -Uri $origUri -Method GET -Headers $headers
        
        $origSubject = ($origMsg.payload.headers | Where-Object name -eq 'Subject').value
        $origFrom = ($origMsg.payload.headers | Where-Object name -eq 'From').value
        $origMessageId = ($origMsg.payload.headers | Where-Object name -eq 'Message-ID').value
        $origReferences = ($origMsg.payload.headers | Where-Object name -eq 'References').value
        
        $newSubject = $(if ($origSubject -match '^Re:') { $origSubject } else { "Re: $origSubject" })
        $fromRaw = $(if ($env:GRAVITY_EMAIL_DEFAULT_FROM) { $env:GRAVITY_EMAIL_DEFAULT_FROM } else { $env:GRAVITY_EMAIL_USER_ID })
        $from = $(if ($fromRaw -match '@') { $fromRaw } else { "$fromRaw <$($env:GRAVITY_EMAIL_USER_ID)>" })
        $dateStr = Get-Date -Format 'r'
        $newMsgId = "<$([guid]::NewGuid())@gmail.com>"
        $refs = $(if ($origReferences) { "$origReferences $origMessageId" } else { $origMessageId })
        
        $rfc822 = "From: $from`r`nTo: $origFrom`r`nSubject: $newSubject`r`nDate: $dateStr`r`nMessage-ID: $newMsgId`r`nReferences: $refs`r`nIn-Reply-To: $origMessageId`r`nMIME-Version: 1.0`r`nContent-Type: $(if($is_html){'text/html'}else{'text/plain'}); charset=utf-8`r`n`r`n" + $body
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($rfc822)
        $b64 = [System.Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
        $bodyJson = @{ raw = $b64; threadId = $origMsg.threadId } | ConvertTo-Json
        Invoke-RestMethod -Uri 'https://gmail.googleapis.com/gmail/v1/users/me/messages/send' -Method POST -Headers $headers -ContentType 'application/json' -Body $bodyJson
        Write-Output "Replied successfully."
    } else {
        throw "reply_email is not supported natively for SMTP."
    }
} catch { Write-Error $_.Exception.Message; exit 1 }
