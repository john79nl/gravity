$lines = Get-Content "Form1.cs" -Encoding UTF8
for ($i = 0; $i -lt $lines.Count; $i++) {
    $idx = $i + 1
    if ($idx -ge 1688 -and $idx -le 1695) {
        if ($lines[$i] -match '"M" =>') { $lines[$i] = '                                    "M" => "📝",' }
        elseif ($lines[$i] -match '"A" =>') { $lines[$i] = '                                    "A" => "➕",' }
        elseif ($lines[$i] -match '"\?" =>') { $lines[$i] = '                                    "?" => "❓",' }
        elseif ($lines[$i] -match '"D" =>') { $lines[$i] = '                                    "D" => "❌",' }
        elseif ($lines[$i] -match '_ =>') { $lines[$i] = '                                    _ => "📄"' }
    }
    elseif ($idx -eq 1708) {
        $lines[$i] = '                                displayText = $"📄 {part}";'
    }
    elseif ($idx -eq 1714) {
        $lines[$i] = '                            displayText = $"📁 {part}";'
    }
}
Set-Content -Path "Form1.cs" -Value $lines -Encoding UTF8
