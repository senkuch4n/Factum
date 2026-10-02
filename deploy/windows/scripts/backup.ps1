<#
.SYNOPSIS
    Backup completo de Factum: base de datos, evidencia y configuración, con manifiesto SHA-256.
.PARAMETER Destino
    Carpeta donde se crea el backup (por ejemplo un disco externo, E:\). Por defecto
    FACTUM_BACKUP_DESTINO del .env o <instalación>\backups.
.PARAMETER Desatendido
    Sin preguntas (tarea programada / llamado desde otros scripts).
.PARAMETER Carpeta
    Carpeta de la instalación (por defecto, la detectada).
#>
[CmdletBinding()]
param(
    [string]$Destino = '',
    [switch]$Desatendido,
    [string]$Carpeta = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comun.ps1')
Set-StrictMode -Version 2.0

if (-not $Carpeta) { $Carpeta = Get-FactumHome }
Initialize-FactumContext $Carpeta
$homeDir = $script:FactumHomeDir
Start-FactumLog -Nombre 'backup' | Out-Null
$total = 8
$dirBackup = $null
$backupCompleto = $false

try {
    Write-Host ''
    Write-Host '=== Backup de Factum ===' -ForegroundColor White
    $envPath = Get-FactumEnvPath
    if (-not (Test-Path -LiteralPath $envPath -PathType Leaf)) {
        Stop-Factum ('No hay una instalación de Factum en ' + $homeDir + '.') 'Ejecutá este acceso desde la carpeta de Factum (C:\Factum).'
    }
    $cfg = Read-EnvFile $envPath
    $version = $cfg['FACTUM_VERSION']
    $carpetaBackupsLocal = Join-Path $homeDir 'backups'
    if (-not $Destino) { $Destino = $cfg['FACTUM_BACKUP_DESTINO'] }
    if (-not $Destino) { $Destino = $carpetaBackupsLocal }
    $Destino = Get-RutaAbsoluta $Destino
    $evidencia = Join-Path $homeDir 'evidencia'

    # 1 ── Destino
    Write-Paso 1 $total ('Revisando el destino ' + $Destino)
    if (-not (Test-Path -LiteralPath $Destino -PathType Container)) {
        Stop-Factum ('La carpeta de destino no existe: ' + $Destino) 'Conectá el disco externo (o creá la carpeta) y volvé a intentar.'
    }
    $tamEvidencia = Get-TamanioCarpeta $evidencia
    $necesario = $tamEvidencia.Bytes + 1GB
    try {
        $info = New-Object System.IO.DriveInfo([System.IO.Path]::GetPathRoot($Destino))
        if ($info.AvailableFreeSpace -lt $necesario) {
            Stop-Factum ('No hay espacio suficiente en ' + $Destino + ': hacen falta ' + (Format-Tamanio $necesario) + ' y hay ' + (Format-Tamanio $info.AvailableFreeSpace) + '.') 'Usá otro disco o liberá espacio.'
        }
    } catch [System.ArgumentException] {
        Write-Aviso 'No se pudo calcular el espacio libre del destino (¿carpeta de red?). Se intenta igual.'
    }
    $stamp = Get-FactumStamp
    $dirBackup = Join-Path $Destino $stamp
    foreach ($sub in @('mongo', 'evidencia', 'config')) {
        New-Item -ItemType Directory -Path (Join-Path $dirBackup $sub) -Force | Out-Null
    }
    Write-Ok ('Backup en ' + $dirBackup)

    # 2 ── Pausa (DT2): base y evidencia consistentes entre sí
    Write-Paso 2 $total 'Pausando Factum mientras se copia'
    if (-not $Desatendido) {
        Write-Host '  Factum va a estar pausado unos minutos (más si hay mucha evidencia).' -ForegroundColor Yellow
        Read-Host '  Presioná Enter para empezar' | Out-Null
    }
    try {
        # El stop va dentro del try: si falla a medias (un servicio parado y el otro no),
        # el finally igual intenta volver a levantar los dos.
        $r = Invoke-FactumCompose -Argumentos @('stop', 'frontend', 'backend')
        if ($r.ExitCode -ne 0) { Stop-Factum 'No se pudo pausar Factum.' 'Abrí "Diagnostico de Factum" para ver qué pasa.' }
        Write-Ok 'Factum en pausa (la base de datos sigue encendida para el volcado).'

        # 3 ── Base de datos (archivo dentro del contenedor + compose cp; nunca stdout -> archivo)
        Write-Paso 3 $total 'Volcando la base de datos'
        $r = Invoke-FactumCompose -Argumentos @('exec', '-T', 'mongo', 'mongodump', '--db', 'factum', '--archive=/tmp/factum-backup.archive.gz', '--gzip') -Silencioso
        if ($r.ExitCode -ne 0) {
            foreach ($l in $r.Salida) { Write-Host ('    ' + $l) }
            Stop-Factum 'Falló el volcado de la base de datos.' 'Abrí "Diagnostico de Factum" y mandá el resultado al proveedor.'
        }
        $archivoMongo = Join-Path (Join-Path $dirBackup 'mongo') 'factum.archive.gz'
        $r = Invoke-FactumCompose -Argumentos @('cp', 'mongo:/tmp/factum-backup.archive.gz', $archivoMongo) -Silencioso
        $codigoCp = $r.ExitCode
        Invoke-FactumCompose -Argumentos @('exec', '-T', 'mongo', 'rm', '-f', '/tmp/factum-backup.archive.gz') -Silencioso | Out-Null
        if ($codigoCp -ne 0 -or -not (Test-Path -LiteralPath $archivoMongo -PathType Leaf)) {
            Stop-Factum 'No se pudo copiar el volcado de la base de datos.' 'Revisá el espacio del destino y volvé a intentar.'
        }
        $casos = Measure-FactumCaso
        Write-Ok ('Base de datos volcada (' + (Format-Tamanio (Get-Item -LiteralPath $archivoMongo).Length) + ').')

        # 4 ── Evidencia (nunca /MIR ni /MOV: el origen no se toca)
        Write-Paso 4 $total ('Copiando la evidencia (' + $tamEvidencia.Archivos + ' archivos, ' + (Format-Tamanio $tamEvidencia.Bytes) + ')')
        $r = Invoke-Nativo -Exe 'robocopy.exe' -Argumentos @($evidencia, (Join-Path $dirBackup 'evidencia'), '/E', '/COPY:DAT', '/DCOPY:DAT', '/R:2', '/W:2', '/NP', '/NFL', '/NDL') -Silencioso
        if ($r.ExitCode -ge 8) {
            foreach ($l in $r.Salida) { Write-Host ('    ' + $l) }
            Stop-Factum ('Falló la copia de la evidencia (robocopy ' + $r.ExitCode + ').') 'Revisá el disco de destino y volvé a intentar.'
        }
        Write-Ok 'Evidencia copiada.'

        # 5 ── Configuración
        Write-Paso 5 $total 'Copiando la configuración'
        $dirConfig = Join-Path $dirBackup 'config'
        Get-ChildItem -LiteralPath (Join-Path $homeDir 'config') -Force | Copy-Item -Destination $dirConfig -Recurse -Force
        foreach ($f in @('docker-compose.yml', 'version.txt')) {
            $origen = Join-Path $homeDir $f
            if (Test-Path -LiteralPath $origen) { Copy-Item -LiteralPath $origen -Destination $dirConfig -Force }
        }
        Write-Ok 'Configuración copiada (incluye la clave secreta y la identidad del estudio).'

        # 6 ── backup-info.json + manifiesto
        Write-Paso 6 $total 'Calculando el manifiesto SHA-256'
        $infoBackup = New-Object System.Collections.Specialized.OrderedDictionary
        $infoBackup['formato'] = 1
        $infoBackup['factum_version'] = $version
        $infoBackup['fecha_local'] = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ss')
        $infoBackup['equipo'] = $env:COMPUTERNAME
        $infoBackup['mongo_imagen'] = Get-MongoImagen (Join-Path $homeDir 'docker-compose.yml')
        $infoBackup['casos'] = [int]$casos
        $infoBackup['archivos_evidencia'] = [int]$tamEvidencia.Archivos
        $infoBackup['bytes_evidencia'] = [long]$tamEvidencia.Bytes
        Write-TextoUtf8SinBom (Join-Path $dirBackup 'backup-info.json') ((ConvertTo-Json -InputObject $infoBackup) + "`n")
        $manifiesto = Join-Path $dirBackup 'manifiesto-sha256.txt'
        $n = New-HashManifest -Raiz $dirBackup -Salida $manifiesto
        Write-Ok ($n.ToString() + ' archivos en el manifiesto.')

        # 7 ── Verificación de la evidencia contra el original
        Write-Paso 7 $total 'Verificando que la copia de la evidencia sea idéntica al original'
        $esperado = Read-HashManifest $manifiesto
        $malos = New-Object System.Collections.Generic.List[string]
        if (Test-Path -LiteralPath $evidencia -PathType Container) {
            $raizEv = (Get-RutaAbsoluta $evidencia).TrimEnd('\', '/')
            foreach ($f in (Get-ArchivoRecursivo $raizEv)) {
                $rel = 'evidencia/' + (Get-RutaRelativa $raizEv $f.FullName)
                if (-not $esperado.ContainsKey($rel) -or $esperado[$rel] -ne (Get-Sha256 $f.FullName)) { $malos.Add($rel) }
            }
        }
        if ($malos.Count -gt 0) {
            foreach ($m in $malos) { Write-Falla ('No coincide: ' + $m) }
            $fallido = $dirBackup + '_FALLIDO'
            Rename-Item -LiteralPath $dirBackup -NewName (Split-Path -Leaf $fallido)
            $dirBackup = $fallido
            Stop-Factum ('La copia de la evidencia no coincide con el original. El backup quedó marcado como FALLIDO: ' + $fallido) 'Revisá el disco de destino (puede estar fallando) y volvé a hacer el backup.'
        }
        Write-Ok 'Todos los archivos de evidencia coinciden con el original.'
        $backupCompleto = $true
    } finally {
        # 8 ── Factum vuelve a funcionar aunque el backup falle
        Write-Paso 8 $total 'Reanudando Factum'
        $r = Invoke-FactumCompose -Argumentos @('start', 'backend', 'frontend') -Silencioso
        $pendiente = Wait-FactumHealthy -TimeoutSec 180 -Silencioso
        if ($r.ExitCode -ne 0 -or $pendiente) {
            Write-Falla ('Factum no volvió a arrancar del todo (' + $pendiente + '). Abrí "Diagnostico de Factum".')
        } else {
            Write-Ok 'Factum funcionando otra vez.'
        }
    }

    # Retención: solo en <home>\backups, solo carpetas AAAA-MM-DD_HHMMSS con backup-info.json.
    if ([string]::Equals($Destino.TrimEnd('\'), (Get-RutaAbsoluta $carpetaBackupsLocal).TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
        $conservar = 10
        $valorConservar = $cfg['FACTUM_BACKUP_CONSERVAR']
        $tmp = 0
        if ($valorConservar -and [int]::TryParse($valorConservar, [ref]$tmp) -and $tmp -gt 0) { $conservar = $tmp }
        $validos = @(Get-ChildItem -LiteralPath $Destino -Directory |
            Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}_\d{6}$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'backup-info.json')) } |
            Sort-Object Name -Descending)
        if ($validos.Count -gt $conservar) {
            foreach ($viejo in ($validos | Select-Object -Skip $conservar)) {
                Write-Host ('  Borrando backup viejo (se conservan ' + $conservar + '): ' + $viejo.Name) -ForegroundColor DarkGray
                Remove-Item -LiteralPath $viejo.FullName -Recurse -Force
            }
        }
    }

    $tam = Get-TamanioCarpeta $dirBackup
    Write-Host ''
    Write-Host '  Backup terminado.' -ForegroundColor Green
    Write-Host ('  - Carpeta: ' + $dirBackup)
    Write-Host ('  - Tamaño:  ' + (Format-Tamanio $tam.Bytes))
    Write-Host ('  - Casos:   ' + $casos)
    Write-Host '  Este backup contiene las contraseñas de los ZIP: guardalo en un lugar seguro.' -ForegroundColor Yellow
    # Línea para los scripts que llaman a este (actualizar, restaurar).
    Write-Host ('BACKUP_DIR=' + $dirBackup)
    Stop-FactumLog
    exit 0
} catch {
    # Un backup a medias (falló el volcado, la copia, etc.) queda marcado _FALLIDO para
    # que no se confunda con uno bueno. La retención ya lo ignora (no es AAAA-MM-DD_HHMMSS).
    if (-not $backupCompleto -and $dirBackup -and -not $dirBackup.EndsWith('_FALLIDO') -and (Test-Path -LiteralPath $dirBackup -PathType Container)) {
        try {
            Rename-Item -LiteralPath $dirBackup -NewName ((Split-Path -Leaf $dirBackup) + '_FALLIDO')
            Write-Aviso ('El backup incompleto quedó marcado como FALLIDO: ' + $dirBackup + '_FALLIDO')
        } catch {
            Write-Aviso ('No se pudo marcar como FALLIDO la carpeta incompleta ' + $dirBackup + '. Podés borrarla a mano.')
        }
    }
    Write-ErrorFinal $_
    Stop-FactumLog
    exit 1
}
