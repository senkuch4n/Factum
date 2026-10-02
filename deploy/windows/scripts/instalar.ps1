<#
.SYNOPSIS
    Instala Factum en esta PC (Docker Desktop + Tatana nativo). Se ejecuta DESDE EL PAQUETE
    descomprimido, con "1-Instalar Factum.bat".
.PARAMETER Carpeta
    Carpeta de instalación (por defecto C:\Factum).
.PARAMETER Reparar
    Reinstala scripts, compose e imágenes sin tocar la configuración ni los datos.
.PARAMETER SinTatana
    No instala Tatana (el agente de captura).
#>
[CmdletBinding()]
param(
    [string]$Carpeta = 'C:\Factum',
    [switch]$Reparar,
    [switch]$SinTatana
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comun.ps1')
Set-StrictMode -Version 2.0

$paquete = Get-RutaAbsoluta (Split-Path -Parent $PSScriptRoot)
$Carpeta = Get-RutaAbsoluta $Carpeta
$total = 13
$huboFalla = $false

Write-Host ''
Write-Host '=== Instalación de Factum ===' -ForegroundColor White
# El registro arranca en %TEMP% (todavía no existe la carpeta de instalación) y se pasa a
# <Carpeta>\logs en el paso 4.
$logTemporal = Start-FactumLog -Nombre 'instalar' -HomeDir ''

try {
    # 1 ── Paquete
    Write-Paso 1 $total 'Verificando el paquete de instalación'
    $version = Read-VersionTxt (Join-Path $paquete 'version.txt')
    if (-not $version) {
        Stop-Factum 'El paquete no tiene version.txt.' 'Ejecutá "1-Instalar Factum.bat" desde la carpeta del paquete descomprimido.'
    }
    Test-PaqueteIntegro $paquete
    Write-Ok ('Paquete de Factum versión ' + $version + ' completo.')

    # 2 ── Requisitos (todos, antes de tocar el disco)
    Write-Paso 2 $total 'Revisando los requisitos de la PC'
    $req = Test-Requisitos -Carpeta $Carpeta -Modo Instalar -SinPuertos:$Reparar
    foreach ($a in $req.Avisos) { Write-Aviso $a }
    if ($req.Fallas.Count -gt 0) {
        Write-Host ''
        Write-Host '  La PC todavía no está lista. No se instaló nada. Falta resolver:' -ForegroundColor Red
        $i = 0
        foreach ($f in $req.Fallas) { $i++; Write-Host ('   {0}. {1}' -f $i, $f) -ForegroundColor Red }
        Stop-Factum 'Faltan requisitos.' 'Resolvé cada punto (la guía instalacion-windows.md explica cómo) y volvé a ejecutar el instalador.'
    }
    if ($req.Avisos.Count -gt 0) {
        if (-not (Confirm-SN '¿Continuar igual con la instalación?')) {
            Stop-Factum 'Instalación cancelada.' 'Resolvé los avisos y volvé a ejecutar el instalador.'
        }
    }
    Write-Ok 'La PC cumple los requisitos.'

    # 3 ── ¿Ya instalado?
    Write-Paso 3 $total 'Revisando si Factum ya está instalado'
    $envPath = Join-Path (Join-Path $Carpeta 'config') '.env'
    $yaInstalado = Test-Path -LiteralPath $envPath -PathType Leaf
    if ($yaInstalado -and -not $Reparar) {
        Stop-Factum ('Factum ya está instalado en ' + $Carpeta + '.') 'Para cambiar de versión usá "Actualizar Factum.bat" del paquete nuevo; para reinstalar los scripts sin tocar datos, ejecutá el instalador con -Reparar.'
    }
    if ($Reparar -and -not $yaInstalado) {
        Stop-Factum ('No hay una instalación de Factum en ' + $Carpeta + ' para reparar.') 'Ejecutá el instalador sin -Reparar.'
    }
    if ($Reparar) {
        $instalada = (Read-EnvFile $envPath)['FACTUM_VERSION']
        if ($instalada -ne $version) {
            Stop-Factum ('La versión instalada es ' + $instalada + ' y este paquete es ' + $version + '.') 'Para cambiar de versión usá "Actualizar Factum.bat"; -Reparar es solo para la misma versión.'
        }
        Write-Ok 'Modo reparar: no se toca la configuración ni los datos.'
    } else {
        Write-Ok 'Instalación nueva.'
    }

    # 4 ── Carpetas
    Write-Paso 4 $total ('Creando las carpetas en ' + $Carpeta)
    foreach ($sub in @('config', 'config\branding', 'evidencia', 'backups', 'logs', 'scripts')) {
        $ruta = Join-Path $Carpeta $sub
        if (-not (Test-Path -LiteralPath $ruta)) { New-Item -ItemType Directory -Path $ruta -Force | Out-Null }
    }
    # Pasar el registro a <Carpeta>\logs
    Stop-FactumLog
    $logFinal = Join-Path (Join-Path $Carpeta 'logs') ('instalar-' + (Get-FactumStamp) + '.log')
    if ($logTemporal -and (Test-Path -LiteralPath $logTemporal)) { Copy-Item -LiteralPath $logTemporal -Destination $logFinal -Force }
    Start-Transcript -LiteralPath $logFinal -Append | Out-Null
    $script:FactumLogPath = $logFinal
    Write-Ok 'Carpetas listas.'

    # 5 ── Permisos (DT5): usuario actual + Administradores + SYSTEM
    Write-Paso 5 $total 'Restringiendo el acceso a la carpeta de Factum'
    if ($Reparar) {
        Write-Ok 'Sin cambios (modo reparar).'
    } else {
        $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $r = Invoke-Nativo -Exe 'icacls.exe' -Argumentos @($Carpeta, '/inheritance:r', '/grant:r', ('*' + $sid + ':(OI)(CI)F'), '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F') -Silencioso
        if ($r.ExitCode -eq 0) {
            Write-Ok 'Solo tu usuario, los administradores y el sistema pueden abrir la carpeta.'
        } else {
            Write-Aviso ('No se pudieron restringir los permisos (icacls: ' + ($r.Salida -join ' ') + '). La instalación sigue igual.')
        }
    }

    # 6 ── Configuración
    Write-Paso 6 $total 'Generando la configuración de esta instalación'
    if ($Reparar) {
        Write-Ok 'Se conserva la configuración existente.'
    } else {
        $valores = @{
            'FACTUM_VERSION'    = $version
            'FACTUM_HOME'       = ($Carpeta -replace '\\', '/')
            'FACTUM_JWT_SECRET' = (New-JwtSecret)
        }
        Write-EnvFile -Path $envPath -Valores $valores -Template (Join-Path $paquete '.env.example')
        $local = Join-Path (Join-Path $Carpeta 'config') 'appsettings.Local.json'
        if (-not (Test-Path -LiteralPath $local)) {
            Copy-Item -LiteralPath (Join-Path $paquete 'appsettings.Local.example.json') -Destination $local
        }
        Write-Ok 'config\.env creado con una clave secreta propia de esta PC.'
    }
    Initialize-FactumContext $Carpeta

    # 7 ── Archivos de la instalación
    Write-Paso 7 $total 'Copiando los archivos de Factum'
    Copy-Item -LiteralPath (Join-Path $paquete 'docker-compose.yml') -Destination $Carpeta -Force
    Copy-Item -LiteralPath (Join-Path $paquete 'version.txt') -Destination $Carpeta -Force
    $icono = Join-Path $paquete 'factum.ico'
    if (Test-Path -LiteralPath $icono) { Copy-Item -LiteralPath $icono -Destination $Carpeta -Force }
    $destScripts = Join-Path $Carpeta 'scripts'
    Get-ChildItem -LiteralPath (Join-Path $paquete 'scripts') -Filter '*.ps1' -File | Copy-Item -Destination $destScripts -Force
    Get-ChildItem -LiteralPath (Join-Path $paquete 'scripts') -Filter '*.psd1' -File | Copy-Item -Destination $destScripts -Force
    foreach ($bat in @('Backup de Factum.bat', 'Diagnostico de Factum.bat', 'Restaurar Factum.bat', 'abrir-factum.bat')) {
        Copy-Item -LiteralPath (Join-Path (Join-Path $paquete 'scripts') $bat) -Destination $Carpeta -Force
    }
    Write-Ok 'Archivos copiados.'

    # 8 ── Imágenes
    Write-Paso 8 $total 'Cargando las imágenes de Factum en Docker'
    Import-ImagenesFactum -Paquete $paquete -Version $version
    Write-Ok 'Imágenes cargadas.'

    # 9 ── Arranque
    Write-Paso 9 $total 'Iniciando Factum (puede tardar un par de minutos)'
    $r = Invoke-FactumCompose -Argumentos @('up', '-d')
    $fallo = $null
    if ($r.ExitCode -ne 0) { $fallo = 'docker compose up' } else { $fallo = Wait-FactumHealthy -TimeoutSec 240 }
    if ($fallo) {
        Show-BackendLogTail
        Stop-Factum ('El backend no arrancó (' + $fallo + ').') 'Revisá config\appsettings.Local.json o config\.env (el registro de arriba dice qué valor falla) y volvé a ejecutar el instalador con -Reparar.'
    }
    Write-Ok 'Factum está funcionando en http://localhost:3000'

    # 10 ── Accesos directos
    Write-Paso 10 $total 'Creando los accesos directos del Escritorio'
    try {
        New-AccesoDirecto -Nombre 'Factum' -Destino (Join-Path $Carpeta 'abrir-factum.bat') -Carpeta $Carpeta -Icono (Join-Path $Carpeta 'factum.ico') -Ventana 7
        New-AccesoDirecto -Nombre 'Backup de Factum' -Destino (Join-Path $Carpeta 'Backup de Factum.bat') -Carpeta $Carpeta
        Write-Ok 'Accesos "Factum" y "Backup de Factum" en el Escritorio.'
    } catch {
        Write-Aviso ('No se pudieron crear los accesos directos: ' + $_.Exception.Message)
    }

    # 11 ── Backup programado (opcional)
    Write-Paso 11 $total 'Backup automático'
    if (Confirm-SN '¿Programar un backup automático todos los días a las 20:00?') {
        try {
            $scriptBackup = Join-Path $destScripts 'backup.ps1'
            $accion = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $scriptBackup + '" -Desatendido')
            $disparo = New-ScheduledTaskTrigger -Daily -At '20:00'
            $usuario = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
            $principal = New-ScheduledTaskPrincipal -UserId $usuario -LogonType Interactive
            $ajustes = New-ScheduledTaskSettingsSet -StartWhenAvailable
            Register-ScheduledTask -TaskName 'Factum - Backup diario' -Action $accion -Trigger $disparo -Principal $principal -Settings $ajustes -Force | Out-Null
            Write-Ok 'Tarea "Factum - Backup diario" programada a las 20:00 (si la PC está apagada, corre al prenderla).'
        } catch {
            Write-Aviso ('No se pudo programar el backup: ' + $_.Exception.Message + '. Podés hacerlo a mano con el acceso "Backup de Factum".')
        }
    } else {
        Write-Ok 'Sin backup programado (podés usar el acceso "Backup de Factum" cuando quieras).'
    }

    # 12 ── Tatana
    Write-Paso 12 $total 'Instalando Tatana (captura de celulares por USB)'
    if ($SinTatana) {
        Write-Ok 'Omitido (-SinTatana).'
    } else {
        $zip = Get-TatanaZip $paquete
        if (-not $zip) {
            Write-Aviso 'El paquete no trae Tatana. Instalalo con los pasos de la guía, sección "Tatana y drivers".'
        } else {
            $codigo = Install-TatanaPortable $zip
            if ($codigo -ne 0) { Write-Aviso ('El instalador de Tatana terminó con código ' + $codigo + '.') }
            $salud = Wait-TatanaHealth -TimeoutSec 30
            if (-not (Test-TatanaReal $salud)) { $huboFalla = $true }
        }
    }

    # 13 ── Resumen
    Write-Paso 13 $total 'Listo'
    Write-Host ''
    Write-Host '  Factum quedó instalado.' -ForegroundColor Green
    Write-Host ('  - Abrilo con el acceso "Factum" del Escritorio o en  http://localhost:3000')
    Write-Host ('  - Evidencia (ZIP e informes):  ' + (Join-Path $Carpeta 'evidencia'))
    Write-Host ('  - Backups:                     ' + (Join-Path $Carpeta 'backups'))
    Write-Host ''
    Write-Host '  AVISO DE SEGURIDAD' -ForegroundColor Yellow
    Write-Host '  En esta versión Factum no tiene usuarios con contraseña propia: acepta cualquier DNI y' -ForegroundColor Yellow
    Write-Host '  cualquier contraseña. Solo se puede entrar desde esta PC, así que la protección real es la' -ForegroundColor Yellow
    Write-Host '  contraseña de Windows. Cada perito tiene que bloquear la sesión (Windows + L) al levantarse' -ForegroundColor Yellow
    Write-Host '  y entrar siempre con SU PROPIO DNI: la autoría de los casos y la auditoría dependen de eso.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  Próximos pasos (guía instalacion-windows.md):'
    Write-Host '   1. Cargar la identidad del estudio en config\appsettings.Local.json y el logo en config\branding\.'
    Write-Host '   2. Aplicarla con "Diagnostico de Factum" (responder S a reiniciar Factum).'
    Write-Host '   3. Hacer la prueba de humo de la guía (caso de prueba, captura e informe).'
    if ($huboFalla) {
        Write-Host ''
        Write-Falla 'La instalación terminó con un problema en Tatana (ver arriba).'
    }
    Write-Host ''
    Write-Host ('  Registro de la instalación: ' + $script:FactumLogPath)
    Stop-FactumLog
    if ($huboFalla) { exit 1 }
    exit 0
} catch {
    Write-ErrorFinal $_
    Stop-FactumLog
    exit 1
}
