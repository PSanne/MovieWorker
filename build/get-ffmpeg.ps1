# Lädt einen aktuellen FFmpeg-Build für Windows (GPL, mit x264/x265, zimg, vid.stab) nach $Destination.
param([string]$Destination = "tools/ffmpeg")
$ErrorActionPreference = 'Stop'
if (Test-Path (Join-Path $Destination 'ffmpeg.exe')) {
    Write-Host "FFmpeg bereits vorhanden: $Destination"
    exit 0
}

$url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip'
$zip = Join-Path $env:RUNNER_TEMP 'ffmpeg.zip'
if (-not $env:RUNNER_TEMP) { $zip = Join-Path ([IO.Path]::GetTempPath()) 'ffmpeg.zip' }
Write-Host "Lade $url"
Invoke-WebRequest -Uri $url -OutFile $zip
$extract = Join-Path ([IO.Path]::GetTempPath()) ('ffmpeg-' + [guid]::NewGuid())
Expand-Archive -Path $zip -DestinationPath $extract
$root = Get-ChildItem $extract -Directory | Select-Object -First 1
New-Item -ItemType Directory -Force $Destination | Out-Null
Copy-Item (Join-Path $root.FullName 'bin/ffmpeg.exe') $Destination
Copy-Item (Join-Path $root.FullName 'bin/ffprobe.exe') $Destination
Copy-Item (Join-Path $root.FullName 'LICENSE.txt') (Join-Path $Destination 'FFMPEG-LICENSE.txt')
& (Join-Path $Destination 'ffmpeg.exe') -hide_banner -version | Select-Object -First 1
