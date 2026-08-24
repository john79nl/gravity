$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

try {
    $provider = $env:GRAVITY_EMAIL_PROVIDER
    $access_token = $env:GRAVITY_EMAIL_ACCESS_TOKEN
    if (-not $access_token) { throw "Missing access token." }

    if ($provider -eq 'MICROSOFT_GRAPH') {
        $headers = @{ Authorization = "Bearer $access_token" }
        $uri = "https://graph.microsoft.com/v1.0/me/mailFolders?`$select=id,displayName,totalItemCount,unreadItemCount"
        $response = Invoke-RestMethod -Uri $uri -Method GET -Headers $headers
        $folders = @()
        if ($response.value) {
            foreach ($folder in $response.value) {
                $folders += @{ Id = $folder.id; Name = $folder.displayName; Total = $folder.totalItemCount; Unread = $folder.unreadItemCount }
            }
        }
        $folders | ConvertTo-Json -Depth 5
    } elseif ($provider -eq 'GMAIL_API') {
        $headers = @{ Authorization = "Bearer $access_token" }
        $uri = "https://gmail.googleapis.com/gmail/v1/users/me/labels"
        $response = Invoke-RestMethod -Uri $uri -Method GET -Headers $headers
        $labels = @()
        if ($response.labels) {
            foreach ($label in $response.labels) {
                $labels += @{ Id = $label.id; Name = $label.name }
            }
        }
        $labels | ConvertTo-Json -Depth 5
    } else {
        throw "list_folders is not supported natively for SMTP."
    }
} catch { Write-Error $_.Exception.Message; exit 1 }
