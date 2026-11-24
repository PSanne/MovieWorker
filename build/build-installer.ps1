# Baut den Windows-Installer (Inno Setup 6) aus dem Ordner „publish“.
param(
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = "Stop"
$iscc = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    Write-Host "Inno Setup wird installiert …"
    choco install innosetup -y --no-progress | Out-Host
}

if (-not (Test-Path $iscc)) {
    throw "Inno Setup (ISCC.exe) wurde nicht gefunden."
}

& $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot "installer.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup meldet Fehler $LASTEXITCODE."
}

Get-ChildItem (Join-Path $PSScriptRoot "..\installer-out") | Format-Table Name, Length
