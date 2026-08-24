import os

filepath = 'Form1.cs'
with open(filepath, 'r', encoding='utf-8') as f:
    lines = f.readlines()

for i in range(len(lines)):
    idx = i + 1
    if 1688 <= idx <= 1695:
        if '"M" =>' in lines[i]: lines[i] = '                                    "M" => "📝",\n'
        elif '"A" =>' in lines[i]: lines[i] = '                                    "A" => "➕",\n'
        elif '"?" =>' in lines[i]: lines[i] = '                                    "?" => "❓",\n'
        elif '"D" =>' in lines[i]: lines[i] = '                                    "D" => "❌",\n'
        elif '_ =>' in lines[i]: lines[i] = '                                    _ => "📄"\n'
    elif idx == 1708:
        lines[i] = '                                displayText = $"📄 {part}";\n'
    elif idx == 1714:
        lines[i] = '                            displayText = $"📁 {part}";\n'

with open(filepath, 'w', encoding='utf-8') as f:
    f.writelines(lines)
print("Done")
