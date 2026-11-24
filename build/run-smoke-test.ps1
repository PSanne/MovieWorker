# Startet MovieWorker im Selbsttest-Modus und wertet das Protokoll aus.
param([string]$Exe, [string]$Media, [string]$Log, [int]$TimeoutSeconds = 900)
$ErrorActionPreference = 'Stop'
$mediaPath = (Resolve-Path $Media).Path
$logPath = [IO.Path]::GetFullPath($Log)
New-Item -ItemType Directory -Force (Split-Path $logPath) | Out-Null

$process = Start-Process -FilePath $Exe -ArgumentList @('--smoke-test', "`"$mediaPath`"", "`"$logPath`"") -PassThru
$null = $process.Handle  # nötig, damit ExitCode nach dem Warten verfügbar ist
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill()
    Write-Host "::error::Selbsttest hat nach $TimeoutSeconds s nicht geantwortet."
}

if (Test-Path $logPath) {
    $lines = Get-Content $logPath
    $lines | ForEach-Object { Write-Host $_ }
    $errorIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -clike '*FEHLER*') {
            if ($errorIndex -lt 0) { $errorIndex = $i }
            Write-Host "::error::$($lines[$i])"
        }
    }

    if ($errorIndex -ge 0) {
        # Kontext für die Fehlersuche: letzte erfolgreiche Prüfungen und die Aufrufliste.
        $from = [Math]::Max(0, $errorIndex - 3)
        $context = ($lines[$from..([Math]::Min($lines.Count - 1, $errorIndex + 8))]) -join ' | '
        Write-Host "::warning::Kontext: $context"
    }
} else {
    Write-Host '::error::Der Selbsttest hat kein Protokoll geschrieben (Absturz beim Start?).'
}

if ($process.ExitCode -ne 0) {
    Write-Host "::error::Selbsttest fehlgeschlagen (Exit-Code $($process.ExitCode))."
    exit 1
}
