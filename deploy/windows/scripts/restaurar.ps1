<#
.SYNOPSIS
    Restaura un backup de Factum (base de datos, evidencia y configuración) en esta instalación.
    Antes de tocar nada verifica el manifiesto SHA-256 del backup.
.PARAMETER Backup
    Carpeta del backup (la que tiene manifiesto-sha256.txt). Si no se pasa, se pide.
#>
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseCompatibleTypes', '', Justification = 'System.Windows.Forms se carga con Add-Type -AssemblyName justo antes de usarlo (viene con .NET Framework en Windows 10).')]
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Backup = '',
    [string]$Carpeta = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comun.ps1')
Set-StrictMode -Version 2.0

if (-not $Carpeta) { $Carpeta = Get-FactumHome }
Initialize-FactumContext $Carpeta
$homeDir = $script:FactumHomeDir
Start-FactumLog -Nombre 'restaurar' | Out-Null
$total = 8
$serviciosDetenidos = $false

function Select-CarpetaBackup {
    # Doble clic en el .bat: se elige la carpeta con un diálogo (o se escribe la ruta).
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = 'Elegí la carpeta del backup de Factum (la que tiene manifiesto-sha256.txt)'
        $dlg.SelectedPath = Join-Path $homeDir 'backups'
        $dlg.ShowNewFolderButton = $false
        if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $dlg.SelectedPath }
        return ''
    } catch {
        return (Read-Host 'Escribí la ruta de la carpeta del backup')
    }
}

try {
    Write-Host ''
    Write-Host '=== Restaurar un backup de Factum ===' -ForegroundColor White
    $envPath = Get-FactumEnvPath
    if (-not (Test-Path -LiteralPath $envPath -PathType Leaf)) {
        Stop-Factum ('No hay una instalación de Factum en ' + $homeDir + '.') 'Primero instalá Factum (la misma versión del backup) con "1-Instalar Factum.bat".'
    }
    if (-not $Backup) { $Backup = Select-CarpetaBackup }
    if (-not $Backup) { Stop-Factum 'No se eligió ningún backup.' 'Volvé a abrir "Restaurar Factum" y elegí la carpeta del backup.' }
    $Backup = (Get-RutaAbsoluta $Backup).TrimEnd('\')
    if (-not (Test-Path -LiteralPath $Backup -PathType Container)) {
        Stop-Factum ('No existe la carpeta ' + $Backup) 'Revisá la ruta (y que el disco externo esté conectado).'
    }

    # 1 ── Verificación previa, sin tocar nada
    Write-Paso 1 $total 'Verificando el backup (SHA-256 de cada archivo)'
    $difs = Test-HashManifest -Raiz $Backup -Manifiesto (Join-Path $Backup 'manifiesto-sha256.txt')
    $graves = Get-DiferenciaGrave $difs
    if ($graves.Count -gt 0) {
        foreach ($d in $graves) { Write-Falla ($d.Motivo + ': ' + $d.Ruta) }
        Stop-Factum 'El backup está dañado o fue modificado; no se restauró nada.' 'Usá otro backup (por ejemplo el anterior) o la copia del disco externo.'
    }
    foreach ($d in @($difs | Where-Object { $_.Motivo -eq 'sobrante' })) { Write-Aviso ('Archivo que no estaba en el backup (se ignora): ' + $d.Ruta) }
    Write-Ok 'El backup está íntegro.'

    # 2 ── Versión
    Write-Paso 2 $total 'Comparando versiones'
    $info = Get-Content -LiteralPath (Join-Path $Backup 'backup-info.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $cfg = Read-EnvFile $envPath
    $versionBackup = [string](Get-PropiedadSegura $info 'factum_version')
    $versionLocal = $cfg['FACTUM_VERSION']
    if ($versionBackup -ne $versionLocal) {
        Stop-Factum ('Este backup es de la versión ' + $versionBackup + ' y la instalada es ' + $versionLocal + '.') ('Instalá primero la versión ' + $versionBackup + ' (su paquete), restaurá, y después actualizá.')
    }
    Write-Ok ('Backup de la versión ' + $versionBackup + ' del ' + (Get-PropiedadSegura $info 'fecha_local') + ' (equipo ' + (Get-PropiedadSegura $info 'equipo') + ', ' + (Get-PropiedadSegura $info 'casos') + ' casos).')

    # 3 ── ¿La instalación tiene datos?
    Write-Paso 3 $total 'Revisando si esta instalación ya tiene datos'
    $r = Invoke-FactumCompose -Argumentos @('up', '-d', 'mongo') -Silencioso
    if ($r.ExitCode -ne 0 -or (Wait-FactumHealthy -TimeoutSec 120 -Servicios @('mongo') -Silencioso)) {
        Stop-Factum 'La base de datos no arrancó.' 'Abrí "Diagnostico de Factum" para ver qué pasa.'
    }
    $casosActuales = Measure-FactumCaso
    $evidenciaActual = Get-TamanioCarpeta (Join-Path $homeDir 'evidencia')
    if ($casosActuales -ne 0 -or $evidenciaActual.Archivos -gt 0) {
        Write-Aviso ('Esta instalación NO está vacía: tiene ' + $casosActuales + ' casos y ' + $evidenciaActual.Archivos + ' archivos de evidencia.')
        Write-Aviso 'La base de datos se va a reemplazar por la del backup. La evidencia se suma (no se borra nada).'
        if (-not (Confirm-Si '¿Seguro que querés reemplazar los datos actuales por los del backup?')) {
            Stop-Factum 'Restauración cancelada. No se tocó nada.' ''
        }
        Write-Host '  Haciendo primero un backup del estado actual (por las dudas)...' -ForegroundColor DarkGray
        $previo = Invoke-BackupPrevio
        if (-not $previo) {
            Stop-Factum 'No se pudo respaldar el estado actual, así que no se restauró nada.' 'Revisá el espacio en disco y volvé a intentar.'
        }
        Write-Ok ('Estado actual respaldado en ' + $previo)
    } else {
        Write-Ok 'Instalación vacía.'
    }

    # 4 ── Pausa
    Write-Paso 4 $total 'Pausando Factum'
    $serviciosDetenidos = $true
    $r = Invoke-FactumCompose -Argumentos @('stop', 'frontend', 'backend') -Silencioso
    if ($r.ExitCode -ne 0) { Stop-Factum 'No se pudo pausar Factum.' 'Abrí "Diagnostico de Factum" para ver qué pasa.' }
    Write-Ok 'Factum en pausa.'

    # 5 ── Base de datos (archivo + compose cp; nunca stdin/stdout binario)
    Write-Paso 5 $total 'Restaurando la base de datos'
    $archivo = Join-Path (Join-Path $Backup 'mongo') 'factum.archive.gz'
    $r = Invoke-FactumCompose -Argumentos @('cp', $archivo, 'mongo:/tmp/restore.archive.gz') -Silencioso
    if ($r.ExitCode -ne 0) { Stop-Factum 'No se pudo copiar el volcado al contenedor de la base.' 'Abrí "Diagnostico de Factum".' }
    $r = Invoke-FactumCompose -Argumentos @('exec', '-T', 'mongo', 'mongorestore', '--archive=/tmp/restore.archive.gz', '--gzip', '--drop', '--nsInclude', 'factum.*') -Silencioso
    $codigo = $r.ExitCode
    Invoke-FactumCompose -Argumentos @('exec', '-T', 'mongo', 'rm', '-f', '/tmp/restore.archive.gz') -Silencioso | Out-Null
    if ($codigo -ne 0) {
        foreach ($l in $r.Salida) { Write-Host ('    ' + $l) }
        Stop-Factum 'Falló la restauración de la base de datos.' 'Mandale este registro al proveedor.'
    }
    Write-Ok 'Base de datos restaurada.'

    # 6 ── Evidencia (sin /MIR: si había evidencia y se confirmó, queda la unión)
    Write-Paso 6 $total 'Restaurando la evidencia'
    $evidenciaLocal = Join-Path $homeDir 'evidencia'
    $r = Invoke-Nativo -Exe 'robocopy.exe' -Argumentos @((Join-Path $Backup 'evidencia'), $evidenciaLocal, '/E', '/COPY:DAT', '/DCOPY:DAT', '/R:2', '/W:2', '/NP', '/NFL', '/NDL') -Silencioso
    if ($r.ExitCode -ge 8) {
        foreach ($l in $r.Salida) { Write-Host ('    ' + $l) }
        Stop-Factum ('Falló la copia de la evidencia (robocopy ' + $r.ExitCode + ').') 'Revisá el espacio en disco y volvé a intentar.'
    }
    $esperado = Read-HashManifest (Join-Path $Backup 'manifiesto-sha256.txt')
    $malos = New-Object System.Collections.Generic.List[string]
    foreach ($rel in @($esperado.Keys | Where-Object { $_.StartsWith('evidencia/') })) {
        $local = Join-Path $evidenciaLocal ($rel.Substring('evidencia/'.Length) -replace '/', '\')
        if (-not (Test-Path -LiteralPath $local -PathType Leaf) -or (Get-Sha256 $local) -ne $esperado[$rel]) { $malos.Add($rel) }
    }
    if ($malos.Count -gt 0) {
        foreach ($m in $malos) { Write-Falla ('No coincide después de restaurar: ' + $m) }
        Stop-Factum 'La evidencia restaurada no coincide con el backup.' 'Revisá el disco (puede estar fallando) y volvé a restaurar.'
    }
    Write-Ok 'Evidencia restaurada y verificada (SHA-256).'

    # 7 ── Configuración: identidad del estudio y secretos; se conservan HOME/VERSION/proyecto locales
    Write-Paso 7 $total 'Restaurando la configuración'
    $cfgBackup = Join-Path $Backup 'config'
    $localJson = Join-Path $cfgBackup 'appsettings.Local.json'
    if (Test-Path -LiteralPath $localJson) { Copy-Item -LiteralPath $localJson -Destination (Join-Path $homeDir 'config') -Force }
    $brandingBackup = Join-Path $cfgBackup 'branding'
    if (Test-Path -LiteralPath $brandingBackup -PathType Container) {
        Get-ChildItem -LiteralPath $brandingBackup -Force | Copy-Item -Destination (Join-Path (Join-Path $homeDir 'config') 'branding') -Recurse -Force
    }
    $envBackup = Join-Path $cfgBackup '.env'
    if (Test-Path -LiteralPath $envBackup) {
        $valoresBackup = Read-EnvFile $envBackup
        $tomar = @{}
        foreach ($k in @('FACTUM_JWT_SECRET', 'FACTUM_JWT_EXPIRY_HOURS', 'FACTUM_AUTH_MODE', 'FACTUM_BACKUP_DESTINO', 'FACTUM_BACKUP_CONSERVAR')) {
            if ($valoresBackup.ContainsKey($k)) { $tomar[$k] = $valoresBackup[$k] }
        }
        Write-EnvFile -Path $envPath -Valores $tomar
    }
    Write-Ok 'Configuración restaurada (se conservan la carpeta y la versión de esta PC).'

    # 8 ── Arranque
    Write-Paso 8 $total 'Iniciando Factum'
    $r = Invoke-FactumCompose -Argumentos @('up', '-d') -Silencioso
    $serviciosDetenidos = $false
    $pendiente = $null
    if ($r.ExitCode -ne 0) { $pendiente = 'docker compose up' } else { $pendiente = Wait-FactumHealthy -TimeoutSec 180 }
    if ($pendiente) {
        Show-BackendLogTail
        Stop-Factum ('Factum no arrancó después de restaurar (' + $pendiente + ').') 'Mandale este registro al proveedor.'
    }
    $casos = Measure-FactumCaso
    Write-Host ''
    Write-Host ('  Restauración terminada: ' + $casos + ' casos.') -ForegroundColor Green
    Stop-FactumLog
    exit 0
} catch {
    Write-ErrorFinal $_
    if ($serviciosDetenidos) {
        Write-Host '  Volviendo a encender Factum...' -ForegroundColor DarkGray
        Invoke-FactumCompose -Argumentos @('up', '-d') -Silencioso | Out-Null
    }
    Stop-FactumLog
    exit 1
}
