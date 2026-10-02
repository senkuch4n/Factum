<#
.SYNOPSIS
    Actualiza Factum a la versión de ESTE paquete ("Actualizar Factum.bat" en la raíz del
    paquete nuevo). Hace un backup antes y, si la versión nueva no arranca, vuelve sola a la
    anterior.
.PARAMETER Carpeta
    Carpeta de la instalación (por defecto C:\Factum).
.PARAMETER SimularFalla
    Solo para la prueba manual de la guía (Parte B): después de levantar la versión nueva la
    trata como fallida y ejecuta la vuelta atrás.
#>
[CmdletBinding()]
param(
    [string]$Carpeta = '',
    [switch]$SimularFalla
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comun.ps1')
Set-StrictMode -Version 2.0

$paquete = Get-RutaAbsoluta (Split-Path -Parent $PSScriptRoot)
if (-not $Carpeta) { $Carpeta = Get-FactumHome }
Initialize-FactumContext $Carpeta
$homeDir = $script:FactumHomeDir
Start-FactumLog -Nombre 'actualizar' | Out-Null
$total = 9
$bats = @('Backup de Factum.bat', 'Diagnostico de Factum.bat', 'Restaurar Factum.bat', 'abrir-factum.bat')

function Copy-ArchivosInstalacion {
    # Copia compose, version.txt, scripts\ y los .bat de $Origen (paquete o copia previa) a la
    # instalación. En el paquete los .bat de uso diario están en scripts\. El compose de poca RAM
    # se copia si existe en $Origen (una copia previa a esa HU no lo tiene).
    param([string]$Origen, [string]$CarpetaBats)
    Copy-Item -LiteralPath (Join-Path $Origen 'docker-compose.yml') -Destination $homeDir -Force
    $pocaRam = Join-Path $Origen $script:FactumComposePocaRam
    if (Test-Path -LiteralPath $pocaRam -PathType Leaf) { Copy-Item -LiteralPath $pocaRam -Destination $homeDir -Force }
    Copy-Item -LiteralPath (Join-Path $Origen 'version.txt') -Destination $homeDir -Force
    $destScripts = Join-Path $homeDir 'scripts'
    Get-ChildItem -LiteralPath (Join-Path $Origen 'scripts') -File |
        Where-Object { $_.Extension -eq '.ps1' -or $_.Extension -eq '.psd1' } |
        Copy-Item -Destination $destScripts -Force
    foreach ($b in $bats) {
        $origenBat = Join-Path $CarpetaBats $b
        if (Test-Path -LiteralPath $origenBat) { Copy-Item -LiteralPath $origenBat -Destination $homeDir -Force }
    }
}

try {
    Write-Host ''
    Write-Host '=== Actualizar Factum ===' -ForegroundColor White
    $envPath = Get-FactumEnvPath
    if (-not (Test-Path -LiteralPath $envPath -PathType Leaf)) {
        Stop-Factum ('No hay una instalación de Factum en ' + $homeDir + '.') 'Para una PC nueva usá "1-Instalar Factum.bat".'
    }

    # 1 ── Versiones
    Write-Paso 1 $total 'Comparando versiones'
    $nueva = Read-VersionTxt (Join-Path $paquete 'version.txt')
    $actual = (Read-EnvFile $envPath)['FACTUM_VERSION']
    if (-not $nueva) { Stop-Factum 'El paquete no tiene version.txt.' 'Ejecutá "Actualizar Factum.bat" desde la carpeta del paquete nuevo.' }
    if ($nueva -eq $actual) {
        Write-Ok ('Ya tenés la versión ' + $actual + '. No hay nada que actualizar.')
        Stop-FactumLog
        exit 0
    }
    $vNueva = ConvertTo-VersionFactum $nueva
    $vActual = ConvertTo-VersionFactum $actual
    if ($null -ne $vNueva -and $null -ne $vActual -and $vNueva -lt $vActual) {
        Write-Aviso ('El paquete (' + $nueva + ') es MÁS VIEJO que la versión instalada (' + $actual + ').')
        if (-not (Confirm-Si '¿Seguro que querés volver a una versión anterior?')) {
            Stop-Factum 'Actualización cancelada. No se tocó nada.' ''
        }
    }
    Write-Ok ('De ' + $actual + ' a ' + $nueva + '.')

    # 2 ── Paquete y requisitos
    Write-Paso 2 $total 'Verificando el paquete y Docker'
    Test-PaqueteIntegro $paquete
    $req = Test-Requisitos -Carpeta $homeDir -Modo Actualizar
    foreach ($a in $req.Avisos) { Write-Aviso $a }
    if ($req.Fallas.Count -gt 0) {
        foreach ($f in $req.Fallas) { Write-Falla $f }
        Stop-Factum 'Faltan requisitos; no se actualizó nada.' 'Resolvé lo de arriba y volvé a intentar.'
    }
    Write-Ok 'Paquete completo y Docker listo.'

    # 3 ── Backup automático
    Write-Paso 3 $total 'Haciendo un backup antes de actualizar'
    $backupDir = Invoke-BackupPrevio
    if (-not $backupDir) {
        Stop-Factum 'El backup previo falló, así que NO se actualizó nada.' 'Revisá el espacio en disco (o abrí "Diagnostico de Factum") y volvé a intentar.'
    }
    Write-Ok ('Backup en ' + $backupDir)

    # 4 ── Copia para volver atrás
    Write-Paso 4 $total 'Guardando la configuración actual para poder volver atrás'
    $previo = Join-Path (Join-Path $homeDir 'backups') ('pre-actualizacion-' + $actual + '-' + (Get-FactumStamp))
    New-Item -ItemType Directory -Path (Join-Path $previo 'scripts') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $previo 'config') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $homeDir 'docker-compose.yml') -Destination $previo -Force
    $pocaRamActual = Join-Path $homeDir $script:FactumComposePocaRam
    if (Test-Path -LiteralPath $pocaRamActual -PathType Leaf) { Copy-Item -LiteralPath $pocaRamActual -Destination $previo -Force }
    Copy-Item -LiteralPath (Join-Path $homeDir 'version.txt') -Destination $previo -Force
    Copy-Item -LiteralPath $envPath -Destination (Join-Path $previo 'config') -Force
    Get-ChildItem -LiteralPath (Join-Path $homeDir 'scripts') -File | Copy-Item -Destination (Join-Path $previo 'scripts') -Force
    foreach ($b in $bats) {
        $origenBat = Join-Path $homeDir $b
        if (Test-Path -LiteralPath $origenBat) { Copy-Item -LiteralPath $origenBat -Destination $previo -Force }
    }
    Write-Ok ('Copia en ' + $previo)

    # 5 ── Imágenes nuevas
    Write-Paso 5 $total ('Cargando las imágenes de la versión ' + $nueva)
    Import-ImagenesFactum -Paquete $paquete -Version $nueva
    Write-Ok 'Imágenes cargadas (las de la versión anterior quedan para poder volver atrás).'

    # 6 ── Archivos nuevos
    Write-Paso 6 $total 'Reemplazando los archivos de la instalación'
    Copy-ArchivosInstalacion -Origen $paquete -CarpetaBats (Join-Path $paquete 'scripts')
    Set-EnvValue -Path $envPath -Key 'FACTUM_VERSION' -Value $nueva
    $ejemplo = Join-Path $paquete '.env.example'
    if (Test-Path -LiteralPath $ejemplo) {
        $existentes = Read-EnvFile $envPath
        $nuevasClaves = @{}
        $defaults = Read-EnvFile $ejemplo
        foreach ($k in $defaults.Keys) {
            if (-not $existentes.ContainsKey($k)) { $nuevasClaves[$k] = $defaults[$k] }
        }
        if ($nuevasClaves.Count -gt 0) {
            Write-EnvFile -Path $envPath -Valores $nuevasClaves
            Write-Ok ('Claves nuevas en config\.env: ' + (@($nuevasClaves.Keys) -join ', '))
        }
    }
    Write-Ok 'Archivos actualizados.'

    # 7 ── Arranque de la versión nueva
    Write-Paso 7 $total ('Iniciando Factum ' + $nueva + ' (hasta 3 minutos)')
    $r = Invoke-FactumCompose -Argumentos @('up', '-d', '--remove-orphans')
    $fallo = $null
    if ($r.ExitCode -ne 0) { $fallo = 'docker compose up' } else { $fallo = Wait-FactumHealthy -TimeoutSec 180 }
    if ($SimularFalla) {
        Write-Aviso 'Prueba: -SimularFalla trata la versión nueva como fallida.'
        $fallo = 'simulado (-SimularFalla)'
    }

    if ($fallo) {
        # 8 ── Vuelta atrás
        Write-Falla ('La versión ' + $nueva + ' no arrancó (' + $fallo + ').')
        Show-BackendLogTail
        Write-Paso 8 $total ('Volviendo a la versión ' + $actual)
        Copy-ArchivosInstalacion -Origen $previo -CarpetaBats $previo
        Copy-Item -LiteralPath (Join-Path (Join-Path $previo 'config') '.env') -Destination $envPath -Force
        Initialize-FactumContext $homeDir
        $r = Invoke-FactumCompose -Argumentos @('up', '-d', '--remove-orphans')
        $falloVuelta = $null
        if ($r.ExitCode -ne 0) { $falloVuelta = 'docker compose up' } else { $falloVuelta = Wait-FactumHealthy -TimeoutSec 180 }
        if ($falloVuelta) {
            Stop-Factum ('Tampoco arrancó la versión ' + $actual + ' (' + $falloVuelta + ').') ('Abrí "Diagnostico de Factum" y mandale el resultado al proveedor. Tus datos están en el backup ' + $backupDir)
        }
        Write-Host ''
        Write-Falla ('La versión ' + $nueva + ' no arrancó; Factum volvió a la versión ' + $actual + '.')
        Write-Host ('  Los datos que ' + $nueva + ' haya escrito en la base no se deshacen; si notás algo raro, restaurá el backup ' + $backupDir) -ForegroundColor Yellow
        Write-Host ('  Registro: ' + $script:FactumLogPath) -ForegroundColor Yellow
        Stop-FactumLog
        exit 1
    }
    Write-Ok ('Factum ' + $nueva + ' funcionando.')

    # 9 ── Tatana
    Write-Paso 9 $total 'Revisando Tatana'
    $zip = Get-TatanaZip $paquete
    if ($zip) {
        $versionZip = $null
        if ((Split-Path -Leaf $zip) -match '^Tatana-Portable-v?(.+)-Windows\.zip$') { $versionZip = $Matches[1] }
        $versionTatana = Get-TatanaVersionInstalada
        if ($versionZip -and $versionZip -ne $versionTatana) {
            if (Confirm-SN ('Hay una versión nueva de Tatana (' + $versionZip + '; instalada: ' + $versionTatana + '). ¿Actualizarla ahora?')) {
                $codigo = Install-TatanaPortable $zip
                if ($codigo -ne 0) { Write-Aviso ('El instalador de Tatana terminó con código ' + $codigo + '.') }
            }
        } else {
            Write-Ok 'Tatana ya está en la versión del paquete.'
        }
    }
    $tatanaOk = Test-TatanaReal (Wait-TatanaHealth -TimeoutSec 30)

    Write-Host ''
    Write-Host ('  Actualización terminada: Factum ' + $nueva + '.') -ForegroundColor Green
    Write-Host ('  - Backup previo: ' + $backupDir)
    Write-Host ('  - Las imágenes de ' + $actual + ' siguen en Docker. Para liberar espacio (cuando ya no las necesites):')
    Write-Host ('      docker image rm factum-backend:' + $actual + ' factum-frontend:' + $actual)
    Stop-FactumLog
    if (-not $tatanaOk) { exit 1 }
    exit 0
} catch {
    Write-ErrorFinal $_
    Stop-FactumLog
    exit 1
}
