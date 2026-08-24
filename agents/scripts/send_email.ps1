$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$argsJson = $env:GRAVITY_TOOL_ARGS
if (-not $argsJson) {
    Write-Error "Missing GRAVITY_TOOL_ARGS environment variable."
    exit 1
}

$inputArgs = $argsJson | ConvertFrom-Json
$to = $inputArgs.to
$subject = $inputArgs.subject
$body = $inputArgs.body
$is_html = $false
if ($null -ne $inputArgs.is_html) { $is_html = [System.Convert]::ToBoolean($inputArgs.is_html) }

$attachments = @()
if ($null -ne $inputArgs.attachments) {
    $attachments = @($inputArgs.attachments)
}

foreach ($filePath in $attachments) {
    if (-not (Test-Path -LiteralPath $filePath)) {
        Write-Error "Attachment file not found: $filePath"
        exit 1
    }
}

try {
    $provider = $env:GRAVITY_EMAIL_PROVIDER
    $access_token = $env:GRAVITY_EMAIL_ACCESS_TOKEN
    if (-not $access_token) { throw "Missing access token. Please connect your account." }

    if ($provider -eq 'MICROSOFT_GRAPH') {
        $headers = @{ Authorization = "Bearer $access_token" }
        $msgObj = @{
            message = @{
                subject = $subject
                body = @{ contentType = $(if($is_html){'HTML'}else{'Text'}); content = $body }
                toRecipients = @(@{ emailAddress = @{ address = $to } })
            }
            saveToSentItems = $true
        }
        if ($attachments.Count -gt 0) {
            $fileAttachments = @()
            foreach ($filePath in $attachments) {
                $fileName = [System.IO.Path]::GetFileName($filePath)
                $contentBytes = [System.Convert]::ToBase64String([System.IO.File]::ReadAllBytes($filePath))
                $fileAttachments += @{
                    "@odata.type" = "#microsoft.graph.fileAttachment"
                    name = $fileName
                    contentBytes = $contentBytes
                }
            }
            $msgObj.message.attachments = $fileAttachments
        }
        Invoke-RestMethod -Uri 'https://graph.microsoft.com/v1.0/me/sendMail' -Method POST -Headers $headers -ContentType 'application/json' -Body ($msgObj | ConvertTo-Json -Depth 20)

    } elseif ($provider -eq 'GMAIL_API') {
        $headers = @{ Authorization = "Bearer $access_token" }
        $fromRaw = $(if ($env:GRAVITY_EMAIL_DEFAULT_FROM) { $env:GRAVITY_EMAIL_DEFAULT_FROM } else { $env:GRAVITY_EMAIL_USER_ID })
        $from = $(if ($fromRaw -match '@') { $fromRaw } else { "$fromRaw <$($env:GRAVITY_EMAIL_USER_ID)>" })
        $dateStr = Get-Date -Format 'r'
        $msgId   = "<$([guid]::NewGuid())@gmail.com>"
        $boundary = "Boundary_$([guid]::NewGuid())"

        if ($attachments.Count -gt 0) {
            $contentType = if($is_html){'text/html'}else{'text/plain'}
            $rfc822 = "From: $from`r`nTo: $to`r`nSubject: $subject`r`nDate: $dateStr`r`nMessage-ID: $msgId`r`nMIME-Version: 1.0`r`nContent-Type: multipart/mixed; boundary=`"$boundary`"`r`n`r`n"
            $rfc822 += "--$boundary`r`nContent-Type: $contentType; charset=utf-8`r`n`r`n" + $body + "`r`n"
            foreach ($filePath in $attachments) {
                $fileName = [System.IO.Path]::GetFileName($filePath)
                $fileBytes = [System.IO.File]::ReadAllBytes($filePath)
                $fileB64 = [System.Convert]::ToBase64String($fileBytes)
                $rfc822 += "--$boundary`r`nContent-Type: application/octet-stream; name=`"$fileName`"`r`nContent-Disposition: attachment; filename=`"$fileName`"`r`nContent-Transfer-Encoding: base64`r`n`r`n" + $fileB64 + "`r`n"
            }
            $rfc822 += "--$boundary--`r`n"
        } else {
            $rfc822 = "From: $from`r`nTo: $to`r`nSubject: $subject`r`nDate: $dateStr`r`nMessage-ID: $msgId`r`nMIME-Version: 1.0`r`nContent-Type: $(if($is_html){'text/html'}else{'text/plain'}); charset=utf-8`r`n`r`n" + $body
        }

        $bytes = [System.Text.Encoding]::UTF8.GetBytes($rfc822)
        $b64 = [System.Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
        $bodyJson = @{ raw = $b64 } | ConvertTo-Json
        Invoke-RestMethod -Uri 'https://gmail.googleapis.com/gmail/v1/users/me/messages/send' -Method POST -Headers $headers -ContentType 'application/json' -Body $bodyJson

    } else {
        # Fallback to SMTP
        $fromRaw = $(if ($env:GRAVITY_EMAIL_DEFAULT_FROM) { $env:GRAVITY_EMAIL_DEFAULT_FROM } else { $env:GRAVITY_EMAIL_USER_ID })
        $from = $(if ($fromRaw -match '@') { $fromRaw } else { "$fromRaw <$($env:GRAVITY_EMAIL_USER_ID)>" })
        $smtpHost = $env:GRAVITY_EMAIL_SMTP_HOST
        $smtpPort = $env:GRAVITY_EMAIL_SMTP_PORT
        if (-not $smtpHost) { throw "Missing SMTP Host." }
        $mail = New-Object System.Net.Mail.MailMessage
        $mail.From = New-Object System.Net.Mail.MailAddress($from)
        $mail.To.Add($to)
        $mail.Subject = $subject
        $mail.Body = $body
        $mail.IsBodyHtml = $is_html
        $senderDomain = if ($from -match '@(.*)') { $matches[1] } else { 'gmail.com' }
        $mail.Headers.Add("Message-Id", "<$([guid]::NewGuid())@$senderDomain>")
        foreach ($filePath in $attachments) {
            $mail.Attachments.Add($filePath)
        }
        $smtp = New-Object System.Net.Mail.SmtpClient($smtpHost, $smtpPort)
        $smtp.EnableSsl = $true
        $smtp.Credentials = New-Object System.Net.NetworkCredential($from, $access_token)
        $smtp.Send($mail)
        $mail.Dispose(); $smtp.Dispose()
    }
    Write-Output 'Email sent successfully.'
} catch { 
    Write-Error $_.Exception.Message
    exit 1 
}
