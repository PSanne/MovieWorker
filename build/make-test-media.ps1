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

# Externer Rekorder + 70D-Clip, dessen Ton 0,75 s später im Rekorder-Ton beginnt (für die Ton-Synchronisation)
$rec = Join-Path $Output 'Rekorder'
New-Item -ItemType Directory -Force $rec | Out-Null
$zoom = Join-Path $rec 'ZOOM0001.WAV'
Invoke-Ffmpeg @('-f', 'lavfi', '-i', "anoisesrc=d=12:c=pink:r=48000:seed=42,volume='0.15+0.85*abs(sin(2.3*t)*sin(0.9*t+1.3))':eval=frame",
    '-ac', '2', '-c:a', 'pcm_s24le', $zoom)
Invoke-Ffmpeg @('-f', 'lavfi', '-i', 'testsrc2=s=1920x1080:r=25:d=6', '-i', $zoom,
    '-filter_complex', '[1:a]atrim=start=0.75:duration=6,asetpts=PTS-STARTPTS,volume=0.5[a]',
    '-map', '0:v', '-map', '[a]', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-c:a', 'pcm_s16le', (Join-Path $card70 'MVI_0002.MOV'))

# Musik
Invoke-Ffmpeg @('-f', 'lavfi', '-i', 'sine=f=220:d=10', '-ac', '2', '-c:a', 'pcm_s16le', (Join-Path $Output 'musik.wav'))

Get-ChildItem $Output -Recurse -File | ForEach-Object { Write-Host $_.FullName $_.Length }
