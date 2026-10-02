<#
.SYNOPSIS
    Abre Factum en el navegador (acceso "Factum" del Escritorio). Corre con la ventana oculta:
    si Docker Desktop no está corriendo lo arranca y espera a que Factum esté listo.
#>
[CmdletBinding()]
param([int]$TimeoutSec = 180)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comun.ps1')
Set-StrictMode -Version 2.0

Initialize-FactumContext (Get-FactumHome)
$homeDir = $script:FactumHomeDir

# Registro corto (uno por apertura); se conservan los últimos 20.
$logs = Join-Path $homeDir 'logs'
if (Test-Path -LiteralPath $logs -PathType Container) {
    @(Get-ChildItem -LiteralPath $logs -Filter 'abrir-factum-*.log' -File | Sort-Object Name -Descending | Select-Object -Skip 20) |
        Remove-Item -Force -ErrorAction SilentlyContinue
}
Start-FactumLog -Nombre 'abrir-factum' | Out-Null

function Show-Mensaje {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseCompatibleTypes', '', Justification = 'System.Windows.Forms se carga con Add-Type -AssemblyName justo antes de usarlo (viene con .NET Framework en Windows 10).')]
    param([string]$Texto)
    try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show($Texto, 'Factum', 'OK', 'Warning') | Out-Null
    } catch {
        Write-Host $Texto
    }
}

try {
    $inicio = Get-Date
    Add-DockerAlPath
    if (-not (Wait-DockerDesktop -TimeoutSec $TimeoutSec -Silencioso)) {
        Show-Mensaje 'Factum todavía no arrancó (Docker Desktop no responde). Esperá un minuto y volvé a intentar; si sigue, abrí "Diagnostico de Factum".'
        Stop-FactumLog
        exit 1
    }
    $restante = [int]($TimeoutSec - ((Get-Date) - $inicio).TotalSeconds)
    if ($restante -lt 30) { $restante = 30 }
    $pendiente = Wait-FactumHealthy -TimeoutSec $restante -Servicios @('backend', 'frontend') -Silencioso
    if ($pendiente) {
        Show-Mensaje 'Factum todavía no arrancó. Esperá un minuto y volvé a intentar; si sigue, abrí "Diagnostico de Factum".'
        Stop-FactumLog
        exit 1
    }
    Start-Process 'http://localhost:3000'
    Stop-FactumLog
    exit 0
} catch {
    Write-ErrorFinal $_
    Show-Mensaje ('No se pudo abrir Factum: ' + $_.Exception.Message + '. Abrí "Diagnostico de Factum".')
    Stop-FactumLog
    exit 1
}
