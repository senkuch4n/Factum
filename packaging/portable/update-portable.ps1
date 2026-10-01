# Auto-actualizacion de Tatana Portable — se llama desde launch-tatana.bat
# ANTES de arrancar el agente, así que la carpeta nunca está bloqueada por un
# proceso corriendo. Si el agente quedó corriendo de una sesión anterior
# (autostart), se lo detiene por nombre antes de sobreescribir los archivos.
#
# Nunca debe impedir que el fiscal use el agente: cualquier falla de red o de
# descarga simplemente deja la versión actual como está.
param(
    [Parameter(Mandatory = $true)][string]$UpdateUrl,
    [Parameter(Mandatory = $true)][string]$InstallDir
)

$ErrorActionPreference = "Stop"

function Get-LocalVersion {
    $versionFile = Join-Path $InstallDir "version.txt"
    if (Test-Path $versionFile) { return (Get-Content $versionFile -Raw).Trim() }
    return "0.0.0"
}

try {
    $localVersion = Get-LocalVersion
    $manifest = Invoke-RestMethod -Uri "$UpdateUrl/latest-portable.json" -TimeoutSec 5

    if (-not $manifest.version -or $manifest.version -eq $localVersion) {
        exit 0
    }

    Write-Host "[Tatana] Nueva version portable disponible: $($manifest.version) (actual: $localVersion)"

    $tempZip = Join-Path $env:TEMP "tatana-portable-update.zip"
    Invoke-WebRequest -Uri $manifest.url -OutFile $tempZip -TimeoutSec 180

    # Si el agente quedó corriendo (autostart de una sesión anterior), frenarlo
    # para poder sobreescribir sus archivos.
    Get-Process -Name "Factum.Agent" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500

    $extractDir = Join-Path $env:TEMP "tatana-portable-new"
    if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
    Expand-Archive -Path $tempZip -DestinationPath $extractDir -Force

    Copy-Item -Path (Join-Path $extractDir "*") -Destination $InstallDir -Recurse -Force

    Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
    Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue

    New-Item -Path (Join-Path $InstallDir ".pending-restart") -ItemType File -Force | Out-Null
    Write-Host "[Tatana] Actualizado a $($manifest.version)."
}
catch {
    # Sin servidor de updates disponible, sin internet, etc. — seguir con lo que hay.
    Write-Host "[Tatana] No se pudo chequear actualizaciones: $($_.Exception.Message)"
    exit 0
}
