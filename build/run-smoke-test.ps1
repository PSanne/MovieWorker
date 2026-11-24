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
    Get-Content $logPath | ForEach-Object {
        Write-Host $_
        if ($_ -like 'FEHLER*') { Write-Host "::error::$_" }
    }
} else {
    Write-Host '::error::Der Selbsttest hat kein Protokoll geschrieben (Absturz beim Start?).'
}

if ($process.ExitCode -ne 0) {
    Write-Host "::error::Selbsttest fehlgeschlagen (Exit-Code $($process.ExitCode))."
    exit 1
}
