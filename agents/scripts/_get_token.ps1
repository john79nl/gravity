# _get_token.ps1 — Call this via dot-sourcing to get a fresh access token.
# Sets $script:access_token to a valid (potentially refreshed) Gmail/Graph token.
# Usage: . "$PSScriptRoot\_get_token.ps1"

$script:access_token = $env:GRAVITY_EMAIL_ACCESS_TOKEN
$script:provider     = $env:GRAVITY_EMAIL_PROVIDER

if ($script:provider -eq 'GMAIL_API') {
    $refresh_token  = $env:GRAVITY_EMAIL_REFRESH_TOKEN
    $client_id      = $env:GRAVITY_EMAIL_CLIENT_ID
    $client_secret  = $env:GRAVITY_EMAIL_CLIENT_SECRET

    if ($refresh_token -and $client_id -and $client_secret) {
        try {
            $body = "client_id=$([uri]::EscapeDataString($client_id))" +
                    "&client_secret=$([uri]::EscapeDataString($client_secret))" +
                    "&refresh_token=$([uri]::EscapeDataString($refresh_token))" +
                    "&grant_type=refresh_token"
            $resp = Invoke-RestMethod -Uri "https://oauth2.googleapis.com/token" `
                        -Method POST `
                        -ContentType "application/x-www-form-urlencoded" `
                        -Body $body
            if ($resp.access_token) {
                $script:access_token = $resp.access_token
                Write-Host "[TOKEN] Refreshed Gmail access token successfully."
            }
        } catch {
            Write-Host "[TOKEN] Could not refresh token, using existing: $($_.Exception.Message)"
        }
    }
}
