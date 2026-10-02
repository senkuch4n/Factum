<#
.SYNOPSIS
    Diagnóstico de Factum (solo lectura). Todo lo que muestra queda además en
    <instalación>\logs\diagnostico-<fecha>.txt para mandárselo al proveedor.
.PARAMETER ReiniciarBackend
    Reinicia Factum para aplicar cambios de configuración (config\appsettings.Local.json, logo o perfil de memoria de config\.env): up -d + restart backend.
    Si no se pasa y la consola es interactiva, se pregunta al final.
#>
[CmdletBinding()]
param(
    [switch]$ReiniciarBackend,
    [switch]$SinPreguntas,
    [string]$Carpeta = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comun.ps1')
Set-StrictMode -Version 2.0

if (-not $Carpeta) { $Carpeta = Get-FactumHome }
Initialize-FactumContext $Carpeta
$homeDir = $script:FactumHomeDir
Start-FactumLog -Nombre 'diagnostico' -Extension 'txt' | Out-Null

function Write-Titulo {
    param([string]$Texto)
    Write-Host ''
    Write-Host ('── ' + $Texto + ' ──') -ForegroundColor Cyan
}

function Write-Salida {
    param($Resultado)
    foreach ($l in $Resultado.Salida) { Write-Host ('  ' + $l) }
}

try {
    Write-Host ''
    Write-Host '=== Diagnóstico de Factum ===' -ForegroundColor White
    Write-Host ('Fecha: ' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '   Equipo: ' + $env:COMPUTERNAME + '   Carpeta: ' + $homeDir)

    Write-Titulo 'Versión instalada'
    $envPath = Get-FactumEnvPath
    if (Test-Path -LiteralPath $envPath -PathType Leaf) {
        $cfg = Read-EnvFile $envPath
        Write-Host ('  Factum ' + $cfg['FACTUM_VERSION'] + '  (proyecto ' + $script:FactumProyecto + ', modo de acceso ' + $cfg['FACTUM_AUTH_MODE'] + ')')
    } else {
        Write-Falla ('No hay config\.env en ' + $homeDir + ': Factum no está instalado acá.')
    }

    Write-Titulo 'Docker'
    Add-DockerAlPath
    $r = Invoke-Nativo -Exe 'docker' -Argumentos @('version', '--format', 'Cliente {{.Client.Version}} / Motor {{.Server.Version}} ({{.Server.Os}}/{{.Server.Arch}})') -Silencioso
    Write-Salida $r
    $dockerOk = ($r.ExitCode -eq 0)
    if (-not $dockerOk) { Write-Falla 'Docker Desktop no responde: abrilo desde el menú Inicio y esperá a que diga "Engine running".' }

    if ($dockerOk) {
        Write-Titulo 'Servicios (estado y salud)'
        Write-Salida (Invoke-FactumCompose -Argumentos @('ps', '--all', '--format', 'table {{.Service}}\t{{.Status}}\t{{.Image}}') -Silencioso)
    }

    # ── Memoria (SDD instalacion-poca-ram §5.6; solo lectura) ──
    Write-Titulo 'Memoria'
    $ramPc = $null
    try {
        $cs = Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop
        $ramPc = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
        Write-Host ('  RAM de la PC: {0} GB' -f $ramPc)
    } catch {
        Write-Aviso 'No se pudo leer la RAM (WMI).'
    }
    $perfilCrudo = ''
    if (Test-Path -LiteralPath $envPath -PathType Leaf) {
        $cfgMem = Read-EnvFile $envPath
        if ($cfgMem.ContainsKey('FACTUM_PERFIL_MEMORIA')) { $perfilCrudo = [string]$cfgMem['FACTUM_PERFIL_MEMORIA'] }
    }
    $perfilMem = Get-PerfilMemoriaConfigurado $envPath
    $overlayMem = Join-Path $homeDir $script:FactumComposePocaRam
    $overlayTexto = 'ausente'
    if (Test-Path -LiteralPath $overlayMem -PathType Leaf) { $overlayTexto = 'presente' }
    $perfilTexto = 'normal'
    if ($perfilMem -eq 'poca') { $perfilTexto = 'poca RAM' }
    Write-Host ('  Perfil de memoria: ' + $perfilTexto + ' (FACTUM_PERFIL_MEMORIA=' + $perfilCrudo + '; ' + $script:FactumComposePocaRam + ' ' + $overlayTexto + ')')
    $perfilNorm = $perfilCrudo.Trim().ToLowerInvariant()
    if ($perfilNorm -ne '' -and $perfilNorm -ne 'poca' -and $perfilNorm -ne 'normal') {
        Write-Aviso ('FACTUM_PERFIL_MEMORIA="' + $perfilCrudo + '" no es válido: se usa el perfil normal.')
    }
    if ($null -ne $ramPc) {
        if ($ramPc -lt 7.5 -and $perfilMem -eq 'normal') {
            Write-Aviso 'La PC tiene poca RAM y el perfil es normal (sin límites). Ver guía, sección 2.1.'
        } elseif ($ramPc -ge 7.5 -and $perfilMem -eq 'poca') {
            Write-Host '  La PC tiene RAM suficiente para el perfil normal (guía, sección 2.1).'
        }
    }
    if ($dockerOk) {
        $idsMem = @()
        $nombresMem = @{}
        foreach ($s in $script:FactumServicios) {
            $id = Get-FactumContainerId $s
            if ($id) { $idsMem += $id; $nombresMem[$id] = $s }
        }
        if ($idsMem.Count -gt 0) {
            Write-Salida (Invoke-Nativo -Exe 'docker' -Argumentos (@('stats', '--no-stream', '--format', 'table {{.Name}}\t{{.MemUsage}}\t{{.MemPerc}}\t{{.CPUPerc}}') + $idsMem) -Silencioso)
            $reinicios = @()
            foreach ($id in $idsMem) {
                $ri = Invoke-Nativo -Exe 'docker' -Argumentos @('inspect', '-f', '{{.RestartCount}} {{.State.OOMKilled}}', $id) -Silencioso
                $texto = $nombresMem[$id] + ' ?'
                if ($ri.ExitCode -eq 0 -and $ri.Salida.Count -gt 0) {
                    $partes = ([string]$ri.Salida[0]).Trim() -split '\s+'
                    $texto = $nombresMem[$id] + ' ' + $partes[0]
                    if ($partes.Count -gt 1 -and $partes[1] -eq 'true') { $texto = $texto + ' (sin memoria: SÍ)' }
                }
                $reinicios += $texto
            }
            Write-Host ('  Reinicios: ' + ($reinicios -join ', '))
        } else {
            Write-Host '  (no hay contenedores de Factum para medir)'
        }
    }

    Write-Titulo 'Puertos'
    foreach ($p in @(3000, 8080, 8765)) {
        $duenio = Test-PuertoLibre $p
        if (-not $duenio) { $duenio = '(nadie escucha)' }
        Write-Host ('  ' + $p + ': ' + $duenio)
    }

    Write-Titulo 'Backend (http://127.0.0.1:8080/health)'
    $hb = Get-BackendHealth
    if ($null -eq $hb) {
        Write-Falla 'El backend no responde.'
    } else {
        Write-Ok ('status=' + (Get-PropiedadSegura $hb 'status') + '  version=' + (Get-PropiedadSegura $hb 'version') + '  auth_mode=' + (Get-PropiedadSegura $hb 'auth_mode'))
    }

    Write-Titulo 'Tatana (http://127.0.0.1:8765/health)'
    $ht = Get-TatanaHealth
    if ($null -ne $ht) {
        Write-Host ('  version=' + (Get-PropiedadSegura $ht 'version') + '  ios_available=' + (Get-PropiedadSegura $ht 'ios_available'))
    }
    Test-TatanaReal $ht | Out-Null
    $lineasTools = @(Get-LineasHerramientasTatana $ht)
    if ($lineasTools.Count -gt 0) {
        Write-Host '  Herramientas de Tatana:'
        foreach ($l in $lineasTools) {
            if ($l.Ok) { Write-Ok $l.Texto } else { Write-Falla $l.Texto }
        }
    }

    Write-Titulo 'Disco'
    try {
        $letra = (Split-Path -Qualifier $homeDir).TrimEnd(':')
        $drive = Get-PSDrive -Name $letra -PSProvider FileSystem
        Write-Host ('  Libre en ' + $letra + ': ' + (Format-Tamanio ([long]$drive.Free)))
    } catch { Write-Aviso 'No se pudo leer el espacio libre.' }
    $ev = Get-TamanioCarpeta (Join-Path $homeDir 'evidencia')
    Write-Host ('  Evidencia: ' + $ev.Archivos + ' archivos, ' + (Format-Tamanio $ev.Bytes))

    Write-Titulo 'Último backup válido'
    $destinos = @(Join-Path $homeDir 'backups')
    if ((Test-Path -LiteralPath $envPath) -and (Read-EnvFile $envPath)['FACTUM_BACKUP_DESTINO']) { $destinos += (Read-EnvFile $envPath)['FACTUM_BACKUP_DESTINO'] }
    $ultimo = $null
    foreach ($d in $destinos) {
        if (-not (Test-Path -LiteralPath $d -PathType Container)) { continue }
        $c = @(Get-ChildItem -LiteralPath $d -Directory |
            Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}_\d{6}$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'manifiesto-sha256.txt')) } |
            Sort-Object Name -Descending | Select-Object -First 1)
        if ($c.Count -gt 0 -and ($null -eq $ultimo -or $c[0].Name -gt $ultimo.Name)) { $ultimo = $c[0] }
    }
    if ($null -eq $ultimo) { Write-Aviso 'No hay ningún backup todavía. Usá el acceso "Backup de Factum".' } else { Write-Host ('  ' + $ultimo.FullName) }

    if ($dockerOk) {
        Write-Titulo 'Últimas 40 líneas del registro del backend'
        Write-Salida (Invoke-FactumCompose -Argumentos @('logs', '--no-color', '--tail', '40', 'backend') -Silencioso)
    }

    $reiniciar = $ReiniciarBackend
    if (-not $reiniciar -and -not $SinPreguntas -and $dockerOk -and [Environment]::UserInteractive) {
        Write-Host ''
        $reiniciar = Confirm-SN '¿Reiniciar Factum para aplicar cambios de configuración (identidad del estudio, logo o perfil de memoria)?'
    }
    if ($reiniciar) {
        Write-Titulo 'Reiniciando Factum'
        # up -d recrea los contenedores cuya configuración de compose cambió (por ejemplo, el perfil
        # de memoria); restart backend toma appsettings.Local.json y el logo (DT8 A).
        $pendiente = $null
        $r = Invoke-FactumCompose -Argumentos @('up', '-d')
        if ($r.ExitCode -ne 0) {
            $pendiente = 'docker compose up'
        } else {
            $r = Invoke-FactumCompose -Argumentos @('restart', 'backend')
            if ($r.ExitCode -ne 0) { $pendiente = 'backend' } else { $pendiente = Wait-FactumHealthy -TimeoutSec 180 }
        }
        if ($pendiente) {
            Show-BackendLogTail
            Write-Falla 'Factum no volvió a arrancar: revisá config\appsettings.Local.json o config\.env (el registro de arriba dice qué valor falla).'
        } else {
            Write-Ok 'Factum reiniciado con la configuración nueva.'
        }
    }

    Write-Host ''
    Write-Host ('Este diagnóstico quedó guardado en: ' + $script:FactumLogPath) -ForegroundColor Yellow
    Stop-FactumLog
    exit 0
} catch {
    Write-ErrorFinal $_
    Stop-FactumLog
    exit 1
}
