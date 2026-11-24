# Erzeugt künstliche Testclips im Stil der beiden Kameras (für den Selbsttest der App).
param([string]$Output = "smoke-media")
$ErrorActionPreference = 'Stop'

function Invoke-Ffmpeg([string[]]$Arguments) {
    & ffmpeg -v error -y @Arguments
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg fehlgeschlagen: $($Arguments -join ' ')" }
}

$card70 = Join-Path $Output 'Karte70D/DCIM/100CANON'
$cardR6 = Join-Path $Output 'KarteR6/DCIM/100EOSR6'
New-Item -ItemType Directory -Force $card70, $cardR6 | Out-Null

# EOS 70D: MOV, H.264 8 Bit, Full HD 25p, LPCM 16 Bit
Invoke-Ffmpeg @('-f', 'lavfi', '-i', 'testsrc2=s=1920x1080:r=25:d=6', '-f', 'lavfi', '-i', 'sine=f=440:d=6:sample_rate=48000',
    '-ac', '2', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-c:a', 'pcm_s16le', (Join-Path $card70 'MVI_0001.MOV'))
New-Item -ItemType File -Force (Join-Path $card70 'MVI_0001.THM') | Out-Null

# EOS R6 Mark II: MP4, HEVC 10 Bit 4:2:2 (Canon Log 3), 4K 23,976p, AAC
Invoke-Ffmpeg @('-f', 'lavfi', '-i', 'testsrc=s=3840x2160:r=24000/1001:d=3', '-f', 'lavfi', '-i', 'sine=f=660:d=3',
    '-ac', '2', '-c:v', 'libx265', '-pix_fmt', 'yuv422p10le', '-x265-params', 'log-level=error', '-c:a', 'aac', (Join-Path $cardR6 'MVI_0042.MP4'))

# EOS R6 Mark II: HDR PQ
Invoke-Ffmpeg @('-f', 'lavfi', '-i', 'testsrc=s=1920x1080:r=25:d=2', '-c:v', 'libx265', '-pix_fmt', 'yuv420p10le',
    '-x265-params', 'log-level=error', '-color_trc', 'smpte2084', '-color_primaries', 'bt2020', '-colorspace', 'bt2020nc',
    (Join-Path $cardR6 'MVI_0100.MP4'))

# Musik
Invoke-Ffmpeg @('-f', 'lavfi', '-i', 'sine=f=220:d=10', '-ac', '2', '-c:a', 'pcm_s16le', (Join-Path $Output 'musik.wav'))

Get-ChildItem $Output -Recurse -File | ForEach-Object { Write-Host $_.FullName $_.Length }
