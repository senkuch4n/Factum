# Factum - funciones comunes de los scripts de instalacion local (Windows).
# Se carga con dot-source desde cada script:  . (Join-Path $PSScriptRoot '_comun.ps1')
#
# Reglas (SDD instalacion-local-docker, R1-R9):
# - Compatible con Windows PowerShell 5.1: nada de ??, ternario, && / ||, -Parallel,
#   Join-Path con mas de 2 argumentos ni ConvertFrom-Json -AsHashtable.
# - Archivo en UTF-8 con BOM y CRLF.
# - Nunca redirigir la salida binaria de un comando nativo a un archivo.
# - Toda llamada a docker compose pasa por Invoke-FactumCompose.
# - Prohibido docker system/volume prune y docker volume rm.

Set-StrictMode -Version 2.0

$script:FactumServicios = @('mongo', 'backend', 'frontend')
$script:FactumHomeDir = $null
$script:FactumProyecto = 'factum'
$script:FactumLogPath = $null

# ── Consola (R5 / R6) ─────────────────────────────────────────────────────────

function Write-Paso {
    param([int]$Numero, [int]$Total, [string]$Texto)
    Write-Host ''
    Write-Host ('[{0}/{1}] {2}...' -f $Numero, $Total, $Texto) -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Texto = 'OK')
    Write-Host ('  OK  ' + $Texto) -ForegroundColor Green
}

function Write-Aviso {
    param([string]$Texto)
    Write-Host ('  AVISO: ' + $Texto) -ForegroundColor Yellow
}

function Write-Falla {
    param([string]$Texto)
    Write-Host ('  ERROR: ' + $Texto) -ForegroundColor Red
}

function Confirm-Si {
    # Operaciones destructivas: solo continua si la persona escribe SI en mayusculas.
    param([string]$Pregunta)
    Write-Host ''
    Write-Host $Pregunta -ForegroundColor Yellow
    $respuesta = Read-Host 'Escribí SI (en mayúsculas) para continuar, o cualquier otra cosa para cancelar'
    return ($respuesta -ceq 'SI')
}

function Confirm-SN {
    # Pregunta simple S/N (no destructiva). Devuelve $true solo con S o s.
    param([string]$Pregunta)
    $respuesta = Read-Host ($Pregunta + ' (S/N)')
    if ($null -eq $respuesta) { return $false }
    return ($respuesta.Trim() -eq 'S' -or $respuesta.Trim() -eq 's')
}

function Stop-Factum {
    # Corta el script con un mensaje humano: que paso + que hacer. El catch global del script
    # muestra la ruta del log y sale con exit 1.
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Corta el script con un mensaje (throw); no cambia el estado del sistema.')]
    param([string]$Mensaje, [string]$QueHacer = '')
    Write-Falla $Mensaje
    if ($QueHacer) { Write-Host ('  Qué hacer: ' + $QueHacer) -ForegroundColor Yellow }
    $err = New-Object System.Exception($Mensaje)
    $err.Data['FactumAbort'] = $true
    throw $err
}

function Write-ErrorFinal {
    # Para el catch global de cada script: mensaje humano + ruta del log; el detalle tecnico
    # queda en el transcript.
    param($Registro)
    $ex = $Registro.Exception
    $esAbort = $false
    if ($null -ne $ex -and $null -ne $ex.Data -and $ex.Data.Contains('FactumAbort')) { $esAbort = $true }
    if (-not $esAbort) {
        Write-Falla ('Ocurrió un error inesperado: ' + $ex.Message)
        Write-Host '  Qué hacer: abrí "Diagnostico de Factum" y mandale al proveedor el archivo que genera.' -ForegroundColor Yellow
        Write-Host '  Detalle técnico (para el proveedor):' -ForegroundColor DarkGray
        Write-Host ('  ' + ($Registro | Out-String)) -ForegroundColor DarkGray
        Write-Host ('  ' + $Registro.ScriptStackTrace) -ForegroundColor DarkGray
    }
    if ($script:FactumLogPath) {
        Write-Host ''
        Write-Host ('  Registro completo: ' + $script:FactumLogPath) -ForegroundColor Yellow
    }
}

# ── Rutas, contexto y log ─────────────────────────────────────────────────────

function Get-FactumStamp {
    return (Get-Date).ToString('yyyy-MM-dd_HHmmss')
}

function Get-RutaAbsoluta {
    param([string]$Ruta)
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Ruta)
}

function Get-FactumHome {
    # C:\Factum por defecto. Si estos scripts ya estan instalados (existe ..\config\.env), el
    # home es la carpeta padre de scripts\.
    $padre = Split-Path -Parent $PSScriptRoot
    $envInstalado = Join-Path (Join-Path $padre 'config') '.env'
    if (Test-Path -LiteralPath $envInstalado -PathType Leaf) {
        return (Get-RutaAbsoluta $padre)
    }
    return 'C:\Factum'
}

function Get-FactumEnvPath {
    param([string]$HomeDir = $script:FactumHomeDir)
    return (Join-Path (Join-Path $HomeDir 'config') '.env')
}

function Initialize-FactumContext {
    # Fija el home y el nombre de proyecto de compose que usan las demas funciones.
    param([string]$HomeDir)
    $script:FactumHomeDir = Get-RutaAbsoluta $HomeDir
    $script:FactumProyecto = 'factum'
    $envPath = Get-FactumEnvPath
    if (Test-Path -LiteralPath $envPath -PathType Leaf) {
        $cfg = Read-EnvFile $envPath
        if ($cfg.ContainsKey('COMPOSE_PROJECT_NAME') -and $cfg['COMPOSE_PROJECT_NAME']) {
            $script:FactumProyecto = $cfg['COMPOSE_PROJECT_NAME']
        }
    }
}

function Start-FactumLog {
    # Transcript en <home>\logs\<script>-<stamp>.log; si la carpeta todavia no existe
    # (instalacion), en %TEMP%. Devuelve la ruta.
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Solo inicia el transcript del propio script; -WhatIf no tiene sentido.')]
    param([string]$Nombre, [string]$HomeDir = $script:FactumHomeDir, [string]$Extension = 'log')
    $stamp = Get-FactumStamp
    $carpeta = $env:TEMP
    if ($HomeDir) {
        $logs = Join-Path $HomeDir 'logs'
        if (Test-Path -LiteralPath $logs -PathType Container) { $carpeta = $logs }
    }
    if (-not $carpeta) { $carpeta = [System.IO.Path]::GetTempPath() }
    $ruta = Join-Path $carpeta ('{0}-{1}.{2}' -f $Nombre, $stamp, $Extension)
    try {
        Start-Transcript -LiteralPath $ruta -Force | Out-Null
        $script:FactumLogPath = $ruta
    } catch {
        Write-Aviso ('No se pudo crear el registro en ' + $ruta + ': ' + $_.Exception.Message)
    }
    return $ruta
}

function Stop-FactumLog {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Solo cierra el transcript del propio script.')]
    param()
    if ($script:FactumLogPath) {
        try { Stop-Transcript | Out-Null } catch { Write-Verbose 'Sin transcript activo.' }
    }
}

# ── Archivo .env ──────────────────────────────────────────────────────────────

function Write-TextoUtf8SinBom {
    param([string]$Ruta, [string]$Texto)
    $abs = Get-RutaAbsoluta $Ruta
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($abs, $Texto, $enc)
}

function Read-LineasTexto {
    param([string]$Ruta)
    $abs = Get-RutaAbsoluta $Ruta
    $texto = [System.IO.File]::ReadAllText($abs, [System.Text.Encoding]::UTF8)
    if ($texto.Length -gt 0 -and $texto[0] -eq [char]0xFEFF) { $texto = $texto.Substring(1) }
    $texto = $texto -replace "`r`n", "`n"
    if ($texto.EndsWith("`n")) { $texto = $texto.Substring(0, $texto.Length - 1) }
    if ($texto.Length -eq 0) { return ,@() }
    return ,($texto -split "`n")
}

function Read-EnvFile {
    # Parser .env: ignora comentarios (#) y lineas vacias, divide en el primer '='.
    # Quita comillas que envuelvan el valor completo. Devuelve un hashtable.
    param([Parameter(Mandatory = $true)][string]$Path)
    $resultado = @{}
    foreach ($linea in (Read-LineasTexto $Path)) {
        $t = $linea.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        $i = $t.IndexOf('=')
        if ($i -lt 1) { continue }
        $clave = $t.Substring(0, $i).Trim()
        $valor = $t.Substring($i + 1).Trim()
        if ($valor.Length -ge 2) {
            $q = $valor[0]
            if (($q -eq '"' -or $q -eq "'") -and $valor[$valor.Length - 1] -eq $q) {
                $valor = $valor.Substring(1, $valor.Length - 2)
            }
        }
        $resultado[$clave] = $valor
    }
    return $resultado
}

function Write-EnvFile {
    # Escribe un .env con los valores del hashtable conservando comentarios y orden del template
    # (por defecto, el propio archivo si ya existe). Las claves que no estan en el template se
    # agregan al final, ordenadas. UTF-8 sin BOM, fin de linea LF (lo lee docker compose).
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][hashtable]$Valores,
        [string]$Template = ''
    )
    $origen = $Template
    if (-not $origen -and (Test-Path -LiteralPath $Path -PathType Leaf)) { $origen = $Path }
    $lineas = @()
    if ($origen) { $lineas = Read-LineasTexto $origen }
    $salida = New-Object System.Collections.Generic.List[string]
    $usadas = @{}
    foreach ($linea in $lineas) {
        $t = $linea.Trim()
        $i = $t.IndexOf('=')
        if ($t.Length -gt 0 -and -not $t.StartsWith('#') -and $i -ge 1) {
            $clave = $t.Substring(0, $i).Trim()
            if ($Valores.ContainsKey($clave)) {
                $salida.Add($clave + '=' + [string]$Valores[$clave])
                $usadas[$clave] = $true
                continue
            }
        }
        $salida.Add($linea)
    }
    foreach ($clave in ($Valores.Keys | Sort-Object)) {
        if (-not $usadas.ContainsKey($clave)) { $salida.Add($clave + '=' + [string]$Valores[$clave]) }
    }
    Write-TextoUtf8SinBom $Path (($salida -join "`n") + "`n")
}

function Set-EnvValue {
    # Cambia (o agrega al final) una clave puntual del .env sin tocar el resto.
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Helper interno de los scripts; la confirmación la piden los scripts (R6).')]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Key,
        [AllowEmptyString()][string]$Value
    )
    $tabla = @{}
    $tabla[$Key] = $Value
    Write-EnvFile -Path $Path -Valores $tabla
}

function New-JwtSecret {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Función pura: genera un valor en memoria, no cambia el sistema.')]
    param()
    # 64 bytes aleatorios criptograficos -> 128 caracteres hex (minusculas).
    $bytes = New-Object byte[] 64
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $sb = New-Object System.Text.StringBuilder
    foreach ($b in $bytes) { [void]$sb.Append($b.ToString('x2')) }
    return $sb.ToString()
}

# ── Comandos nativos y docker compose ─────────────────────────────────────────

function Invoke-Nativo {
    # Ejecuta un comando nativo de texto (docker, robocopy, icacls...). La salida pasa por
    # PowerShell (asi queda en el transcript) como TEXTO: nunca usar esto para datos binarios.
    # Devuelve un objeto { ExitCode, Salida }.
    param(
        [Parameter(Mandatory = $true)][string]$Exe,
        [string[]]$Argumentos = @(),
        [switch]$Silencioso
    )
    $lineas = New-Object System.Collections.Generic.List[string]
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Exe @Argumentos 2>&1 | ForEach-Object {
            $texto = [string]$_
            $lineas.Add($texto)
            if (-not $Silencioso) { Write-Host ('    ' + $texto) -ForegroundColor DarkGray }
        }
        $codigo = $LASTEXITCODE
    } catch {
        $lineas.Add($_.Exception.Message)
        $codigo = 9009
    } finally {
        $ErrorActionPreference = $prev
    }
    if ($null -eq $codigo) { $codigo = 0 }
    return New-Object psobject -Property @{ ExitCode = [int]$codigo; Salida = $lineas.ToArray() }
}

function Get-FactumComposeArgumento {
    $h = $script:FactumHomeDir
    return @(
        'compose',
        '--project-directory', $h,
        '-f', (Join-Path $h 'docker-compose.yml'),
        '--env-file', (Get-FactumEnvPath),
        '-p', $script:FactumProyecto
    )
}

function Invoke-FactumCompose {
    # R7: SIEMPRE con --project-directory, -f, --env-file y -p de esta instalacion.
    # Devuelve { ExitCode, Salida }; el script llamador decide que hacer si ExitCode != 0.
    param([string[]]$Argumentos, [switch]$Silencioso)
    if (-not $script:FactumHomeDir) { throw 'Initialize-FactumContext no se llamo.' }
    $todos = @(Get-FactumComposeArgumento) + $Argumentos
    return (Invoke-Nativo -Exe 'docker' -Argumentos $todos -Silencioso:$Silencioso)
}

function Get-FactumContainerId {
    param([string]$Servicio)
    $r = Invoke-FactumCompose -Argumentos @('ps', '-q', $Servicio) -Silencioso
    if ($r.ExitCode -ne 0) { return $null }
    foreach ($l in $r.Salida) {
        if ($l -match '^[0-9a-f]{12,64}$') { return $l }
    }
    return $null
}

function Get-FactumHealth {
    # Estado de salud de un servicio: healthy / starting / unhealthy / sin-contenedor.
    param([string]$Servicio)
    $id = Get-FactumContainerId $Servicio
    if (-not $id) { return 'sin-contenedor' }
    $r = Invoke-Nativo -Exe 'docker' -Argumentos @('inspect', '-f', '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}', $id) -Silencioso
    if ($r.ExitCode -ne 0 -or $r.Salida.Count -eq 0) { return 'desconocido' }
    return ([string]$r.Salida[0]).Trim()
}

function Wait-FactumHealthy {
    # Espera a que los servicios esten healthy. Devuelve $null si todo OK, o el nombre del
    # primer servicio que no llego a healthy dentro del tiempo.
    param([int]$TimeoutSec = 180, [string[]]$Servicios = $script:FactumServicios, [switch]$Silencioso)
    $limite = (Get-Date).AddSeconds($TimeoutSec)
    while ($true) {
        $pendiente = $null
        foreach ($s in $Servicios) {
            $estado = Get-FactumHealth $s
            if ($estado -ne 'healthy') { $pendiente = $s; break }
        }
        if (-not $pendiente) { return $null }
        if ((Get-Date) -gt $limite) { return $pendiente }
        if (-not $Silencioso) { Write-Host ('    esperando a ' + $pendiente + '...') -ForegroundColor DarkGray }
        Start-Sleep -Seconds 5
    }
}

function Show-BackendLogTail {
    param([int]$Lines = 40)
    Write-Host ''
    Write-Host ('  Últimas {0} líneas del registro del backend:' -f $Lines) -ForegroundColor Yellow
    $r = Invoke-FactumCompose -Argumentos @('logs', '--no-color', '--tail', [string]$Lines, 'backend') -Silencioso
    foreach ($l in $r.Salida) { Write-Host ('    ' + $l) }
}

function Get-MongoImagen {
    # Tag exacto de mongo del compose (fuente unica).
    param([string]$ComposePath)
    foreach ($l in (Read-LineasTexto $ComposePath)) {
        if ($l -match '^\s*image:\s*(mongo:[^\s#]+)') { return $Matches[1] }
    }
    return $null
}

function Measure-FactumCaso {
    # Cantidad de casos en la base (o -1 si no se pudo consultar).
    $r = Invoke-FactumCompose -Argumentos @('exec', '-T', 'mongo', 'mongosh', '--quiet', '--eval', "db.getSiblingDB('factum').cases.countDocuments()") -Silencioso
    if ($r.ExitCode -ne 0) { return -1 }
    for ($i = $r.Salida.Count - 1; $i -ge 0; $i--) {
        $t = ([string]$r.Salida[$i]).Trim()
        if ($t -match '^\d+$') { return [int]$t }
    }
    return -1
}

function Test-DockerResponde {
    $r = Invoke-Nativo -Exe 'docker' -Argumentos @('info', '--format', '{{.OSType}}') -Silencioso
    return ($r.ExitCode -eq 0)
}

function Get-DockerDesktopExe {
    $candidato = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (Test-Path -LiteralPath $candidato -PathType Leaf) { return $candidato }
    return $null
}

function Add-DockerAlPath {
    # Recien instalado Docker Desktop, la sesion puede no tener docker.exe en el PATH.
    $bin = Join-Path $env:ProgramFiles 'Docker\Docker\resources\bin'
    if ((Test-Path -LiteralPath $bin -PathType Container) -and ($env:Path -notlike ('*' + $bin + '*'))) {
        $env:Path = $env:Path + ';' + $bin
    }
}

function Wait-DockerDesktop {
    # Si Docker no responde, arranca Docker Desktop y espera. Devuelve $true si responde.
    param([int]$TimeoutSec = 120, [switch]$Silencioso)
    if (Test-DockerResponde) { return $true }
    $exe = Get-DockerDesktopExe
    if ($exe) {
        if (-not $Silencioso) { Write-Host '    Esperando a Docker Desktop (puede tardar un par de minutos)...' -ForegroundColor DarkGray }
        Start-Process -FilePath $exe | Out-Null
    }
    $limite = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $limite) {
        Start-Sleep -Seconds 5
        if (Test-DockerResponde) { return $true }
    }
    return $false
}

function Test-ImagenesFactum {
    # Devuelve la lista de imagenes que faltan en Docker.
    param([string[]]$Imagenes)
    $faltan = @()
    foreach ($img in $Imagenes) {
        $r = Invoke-Nativo -Exe 'docker' -Argumentos @('image', 'inspect', '--format', '{{.Id}}', $img) -Silencioso
        if ($r.ExitCode -ne 0) { $faltan += $img }
    }
    return ,$faltan
}

function Import-ImagenesFactum {
    # docker load del .tar del paquete + verificacion de las tres imagenes.
    param([string]$Paquete, [string]$Version)
    $tar = Join-Path (Join-Path $Paquete 'imagenes') ('factum-v' + $Version + '.tar')
    if (-not (Test-Path -LiteralPath $tar -PathType Leaf)) {
        Stop-Factum ('No se encontró ' + $tar + '.') 'Volvé a copiar el paquete de instalación completo.'
    }
    Write-Host '    Cargando las imágenes (puede tardar unos minutos)...' -ForegroundColor DarkGray
    $r = Invoke-Nativo -Exe 'docker' -Argumentos @('load', '-i', $tar)
    if ($r.ExitCode -ne 0) {
        Stop-Factum 'Docker no pudo cargar las imágenes del paquete.' 'Revisá que haya espacio en disco y volvé a intentar.'
    }
    $mongo = Get-MongoImagen (Join-Path $Paquete 'docker-compose.yml')
    $faltan = Test-ImagenesFactum @(('factum-backend:' + $Version), ('factum-frontend:' + $Version), $mongo)
    if ($faltan.Count -gt 0) {
        Stop-Factum ('Después de cargar el paquete faltan imágenes: ' + ($faltan -join ', ')) 'El paquete está incompleto: pedile al proveedor uno nuevo.'
    }
}

# ── Manifiestos SHA-256 (formato: "<sha256>  <ruta/relativa>", UTF-8 sin BOM, LF, ordenado) ──

function Get-RutaRelativa {
    param([string]$Raiz, [string]$Completa)
    $rel = $Completa.Substring($Raiz.Length).TrimStart('\', '/')
    return ($rel -replace '\\', '/')
}

function Get-ArchivoRecursivo {
    param([string]$Raiz)
    return @(Get-ChildItem -LiteralPath $Raiz -Recurse -Force -File -ErrorAction Stop)
}

function Get-Sha256 {
    param([string]$Ruta)
    return (Get-FileHash -LiteralPath $Ruta -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-HashManifest {
    # SHA-256 de todos los archivos bajo $Raiz (recursivo), excluido el propio manifiesto.
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Helper interno; escribe el manifiesto dentro de una carpeta que crea el propio script.')]
    param([Parameter(Mandatory = $true)][string]$Raiz, [Parameter(Mandatory = $true)][string]$Salida)
    $raizAbs = (Get-RutaAbsoluta $Raiz).TrimEnd('\', '/')
    $salidaAbs = Get-RutaAbsoluta $Salida
    $entradas = New-Object System.Collections.Generic.List[string]
    $mapa = @{}
    foreach ($f in (Get-ArchivoRecursivo $raizAbs)) {
        if ([string]::Equals($f.FullName, $salidaAbs, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
        $rel = Get-RutaRelativa $raizAbs $f.FullName
        $entradas.Add($rel)
        $mapa[$rel] = Get-Sha256 $f.FullName
    }
    $ordenadas = $entradas.ToArray()
    [Array]::Sort($ordenadas, [System.StringComparer]::Ordinal)
    $sb = New-Object System.Text.StringBuilder
    foreach ($rel in $ordenadas) { [void]$sb.Append($mapa[$rel] + '  ' + $rel + "`n") }
    Write-TextoUtf8SinBom $salidaAbs $sb.ToString()
    return $ordenadas.Count
}

function Read-HashManifest {
    # Devuelve un hashtable ruta -> hash (las lineas mal formadas van con clave '#invalida:<n>').
    param([string]$Manifiesto)
    $tabla = @{}
    $n = 0
    foreach ($l in (Read-LineasTexto $Manifiesto)) {
        $n++
        if ($l.Trim().Length -eq 0) { continue }
        if ($l -match '^([0-9a-fA-F]{64})  (.+)$') {
            $tabla[$Matches[2]] = $Matches[1].ToLowerInvariant()
        } else {
            $tabla['#invalida:' + $n] = $l
        }
    }
    return $tabla
}

function Test-HashManifest {
    # Compara $Raiz contra el manifiesto. No modifica nada. Devuelve una lista de diferencias
    # { Ruta, Motivo } con Motivo = 'falta' | 'hash distinto' | 'sobrante' | 'linea invalida' |
    # 'falta el manifiesto'. 'sobrante' es informativo; el resto invalida el conjunto.
    param([Parameter(Mandatory = $true)][string]$Raiz, [Parameter(Mandatory = $true)][string]$Manifiesto)
    $difs = New-Object System.Collections.Generic.List[object]
    $raizAbs = (Get-RutaAbsoluta $Raiz).TrimEnd('\', '/')
    $manAbs = Get-RutaAbsoluta $Manifiesto
    if (-not (Test-Path -LiteralPath $manAbs -PathType Leaf)) {
        $difs.Add((New-Object psobject -Property @{ Ruta = $Manifiesto; Motivo = 'falta el manifiesto' }))
        return ,$difs.ToArray()
    }
    $esperado = Read-HashManifest $manAbs
    $presentes = @{}
    if (Test-Path -LiteralPath $raizAbs -PathType Container) {
        foreach ($f in (Get-ArchivoRecursivo $raizAbs)) {
            if ([string]::Equals($f.FullName, $manAbs, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
            $presentes[(Get-RutaRelativa $raizAbs $f.FullName)] = $f.FullName
        }
    }
    $claves = @($esperado.Keys)
    [Array]::Sort($claves, [System.StringComparer]::Ordinal)
    foreach ($rel in $claves) {
        if ($rel.StartsWith('#invalida:')) {
            $difs.Add((New-Object psobject -Property @{ Ruta = [string]$esperado[$rel]; Motivo = 'linea invalida' }))
            continue
        }
        if (-not $presentes.ContainsKey($rel)) {
            $difs.Add((New-Object psobject -Property @{ Ruta = $rel; Motivo = 'falta' }))
            continue
        }
        if ((Get-Sha256 $presentes[$rel]) -ne $esperado[$rel]) {
            $difs.Add((New-Object psobject -Property @{ Ruta = $rel; Motivo = 'hash distinto' }))
        }
    }
    $sobrantes = @($presentes.Keys | Where-Object { -not $esperado.ContainsKey($_) })
    [Array]::Sort($sobrantes, [System.StringComparer]::Ordinal)
    foreach ($rel in $sobrantes) {
        $difs.Add((New-Object psobject -Property @{ Ruta = $rel; Motivo = 'sobrante' }))
    }
    return ,$difs.ToArray()
}

function Get-DiferenciaGrave {
    param([object[]]$Diferencias)
    return ,@($Diferencias | Where-Object { $_.Motivo -ne 'sobrante' })
}

function Test-PaqueteIntegro {
    # Verifica SHA256SUMS.txt del paquete (copia por USB incompleta / archivo cambiado).
    param([string]$Paquete)
    $difs = Test-HashManifest -Raiz $Paquete -Manifiesto (Join-Path $Paquete 'SHA256SUMS.txt')
    $graves = Get-DiferenciaGrave $difs
    if ($graves.Count -gt 0) {
        foreach ($d in $graves) { Write-Falla ($d.Motivo + ': ' + $d.Ruta) }
        Stop-Factum 'El paquete de instalación está incompleto o dañado (por ejemplo, una copia por USB que no terminó).' 'Volvé a copiar el paquete completo (el .zip) y descomprimilo de nuevo.'
    }
}

# ── Version ───────────────────────────────────────────────────────────────────

function ConvertTo-VersionFactum {
    # "1.2.3" o "1.2.3-algo" -> [version] 1.2.3 (lo que sigue al '-' no cuenta).
    param([string]$Texto)
    $base = ($Texto.Trim() -split '-')[0]
    $v = $null
    if ([version]::TryParse($base, [ref]$v)) { return $v }
    return $null
}

function Read-VersionTxt {
    param([string]$Ruta)
    if (-not (Test-Path -LiteralPath $Ruta -PathType Leaf)) { return $null }
    $lineas = Read-LineasTexto $Ruta
    if ($lineas.Count -eq 0) { return $null }
    return ([string]$lineas[0]).Trim()
}

# ── Requisitos (Q1-Q11) ───────────────────────────────────────────────────────

function Test-PuertoLibre {
    # $null si el puerto esta libre; si no, el nombre del proceso que escucha.
    param([int]$Puerto)
    $conexiones = @()
    try {
        $conexiones = @(Get-NetTCPConnection -State Listen -LocalPort $Puerto -ErrorAction SilentlyContinue)
    } catch {
        return $null
    }
    if ($conexiones.Count -eq 0) { return $null }
    $proceso = Get-Process -Id $conexiones[0].OwningProcess -ErrorAction SilentlyContinue
    if ($null -eq $proceso) { return ('PID ' + $conexiones[0].OwningProcess) }
    return $proceso.ProcessName
}

function Get-PropiedadSegura {
    param($Objeto, [string]$Nombre)
    if ($null -eq $Objeto) { return $null }
    $p = $Objeto.PSObject.Properties[$Nombre]
    if ($null -eq $p) { return $null }
    return $p.Value
}

function Test-Requisitos {
    # Junta TODAS las fallas y avisos (no corta en la primera). Modo 'Actualizar' solo chequea
    # Docker (Q6-Q9) y espacio. -SinPuertos saltea Q10 (reparar una instalacion que ya corre).
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseSingularNouns', '', Justification = 'Nombre fijado por la SDD (§6.5): chequea la lista completa de requisitos Q1-Q11.')]
    param(
        [string]$Carpeta = 'C:\Factum',
        [ValidateSet('Instalar', 'Actualizar')][string]$Modo = 'Instalar',
        [switch]$SinPuertos
    )
    $fallas = New-Object System.Collections.Generic.List[string]
    $avisos = New-Object System.Collections.Generic.List[string]
    $completo = ($Modo -eq 'Instalar')

    if ($completo) {
        # Q1 - 64 bits
        if (-not [Environment]::Is64BitOperatingSystem) {
            $fallas.Add('Factum necesita Windows de 64 bits.')
        }
        # Q2 - Windows 10 22H2 (build 19045) o posterior
        $build = [Environment]::OSVersion.Version.Build
        if ($build -lt 19045) {
            $producto = 'Windows'
            $edicion = ''
            try {
                $reg = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop
                $pn = Get-PropiedadSegura $reg 'ProductName'
                if ($pn) { $producto = $pn }
                $dv = Get-PropiedadSegura $reg 'DisplayVersion'
                if (-not $dv) { $dv = Get-PropiedadSegura $reg 'ReleaseId' }
                if ($dv) { $edicion = $dv }
            } catch { Write-Verbose 'Sin datos de version en el registro.' }
            $fallas.Add(('Tu Windows es {0} {1} (build {2}). Hace falta Windows 10 versión 22H2 o posterior: Configuración > Windows Update.' -f $producto, $edicion, $build))
        }
        # Q3 - RAM (umbral algo por debajo de 8/16 GB: Windows reporta un poco menos de lo instalado)
        try {
            $cs = Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop
            $gb = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
            if ($gb -lt 7.5) {
                $fallas.Add(('La PC tiene {0} GB de memoria RAM. Docker + Factum necesitan al menos 8 GB.' -f $gb))
            } elseif ($gb -lt 15) {
                $avisos.Add(('La PC tiene {0} GB de RAM: va a andar, pero cerrá otros programas pesados mientras uses Factum (recomendable 16 GB).' -f $gb))
            }
            # Q4 - Virtualizacion
            $virt = $false
            if ($cs.HypervisorPresent) {
                $virt = $true
            } else {
                $cpu = @(Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop)
                if ($cpu.Count -gt 0 -and $cpu[0].VirtualizationFirmwareEnabled) { $virt = $true }
            }
            if (-not $virt) {
                $fallas.Add('La virtualización está apagada en la BIOS/UEFI. Hay que entrar al setup del equipo (F2/Supr al encender) y activar Intel VT-x / AMD-V (SVM). Ver la guía, sección "Habilitar la virtualización".')
            }
        } catch {
            $avisos.Add('No se pudo leer la memoria ni la virtualización de la PC (WMI). Verificalas a mano con la guía.')
        }
    }

    # Q5 - Espacio libre en la unidad de la instalacion
    try {
        $letra = (Split-Path -Qualifier (Get-RutaAbsoluta $Carpeta)).TrimEnd(':')
        $drive = Get-PSDrive -Name $letra -PSProvider FileSystem -ErrorAction Stop
        $libreGb = [math]::Round($drive.Free / 1GB, 1)
        if ($libreGb -lt 15) {
            $fallas.Add(('Quedan {0} GB libres en {1}: hacen falta al menos 15 GB (recomendable 50 GB). Liberá espacio o instalá en otra unidad.' -f $libreGb, ($letra + ':')))
        } elseif ($libreGb -lt 50) {
            $avisos.Add(('Quedan {0} GB libres en {1}: alcanza para empezar, pero la evidencia crece con cada caso (recomendable 50 GB).' -f $libreGb, ($letra + ':')))
        }
    } catch {
        $avisos.Add('No se pudo calcular el espacio libre de la unidad de instalación.')
    }

    # Q6 - Docker Desktop instalado
    Add-DockerAlPath
    $dockerCli = Get-Command docker -ErrorAction SilentlyContinue
    $dockerExe = Get-DockerDesktopExe
    if ($null -eq $dockerCli -and $null -eq $dockerExe) {
        $fallas.Add('Docker Desktop no está instalado. Ver la guía, paso "Instalar Docker Desktop".')
        $wsl = Get-Command wsl.exe -ErrorAction SilentlyContinue
        $wslOk = $false
        if ($null -ne $wsl) {
            $r = Invoke-Nativo -Exe 'wsl.exe' -Argumentos @('--status') -Silencioso
            $wslOk = ($r.ExitCode -eq 0)
        }
        if (-not $wslOk) {
            $fallas.Add('WSL2 no está instalado: abrí PowerShell como administrador, ejecutá  wsl --install  y reiniciá la PC.')
        }
    } elseif ($null -eq $dockerCli) {
        $fallas.Add('Docker Desktop está instalado pero el comando docker no está disponible. Cerrá la sesión de Windows, volvé a entrar y abrí Docker Desktop una vez.')
    } else {
        # Q7 - Docker corriendo
        $r = Invoke-Nativo -Exe 'docker' -Argumentos @('info', '--format', '{{.OSType}}') -Silencioso
        $responde = ($r.ExitCode -eq 0)
        if (-not $responde) {
            $texto = ($r.Salida -join ' ')
            if ($texto -match '(?i)access is denied|acceso denegado|permission denied') {
                $fallas.Add('Tu usuario de Windows no está en el grupo docker-users. Un administrador tiene que agregarlo (Administración de equipos > Usuarios y grupos locales > Grupos > docker-users) y después hay que cerrar la sesión y volver a entrar.')
            } else {
                $responde = Wait-DockerDesktop -TimeoutSec 120
                if (-not $responde) {
                    $fallas.Add('Docker Desktop no responde. Abrilo desde el menú Inicio, esperá a que diga "Engine running" y volvé a ejecutar el instalador.')
                }
            }
        }
        if ($responde) {
            # Q8 - Contenedores Linux
            $r = Invoke-Nativo -Exe 'docker' -Argumentos @('info', '--format', '{{.OSType}}') -Silencioso
            $os = ''
            if ($r.Salida.Count -gt 0) { $os = ([string]$r.Salida[0]).Trim() }
            if ($os -ne 'linux') {
                $fallas.Add('Docker está en modo contenedores Windows: clic derecho en la ballena (junto al reloj) > Switch to Linux containers.')
            }
            # Q9 - Docker Compose v2 >= 2.20
            $r = Invoke-Nativo -Exe 'docker' -Argumentos @('compose', 'version', '--short') -Silencioso
            $vc = ''
            if ($r.Salida.Count -gt 0) { $vc = ([string]$r.Salida[0]).Trim() }
            $okCompose = $false
            if ($r.ExitCode -eq 0 -and $vc -match '^v?(\d+)\.(\d+)') {
                $mayor = [int]$Matches[1]
                $menor = [int]$Matches[2]
                if ($mayor -gt 2 -or ($mayor -eq 2 -and $menor -ge 20)) { $okCompose = $true }
            }
            if (-not $okCompose) {
                $fallas.Add(('Docker Compose es muy viejo o no está ({0}). Actualizá Docker Desktop.' -f $vc))
            }
        }
    }

    if ($completo -and -not $SinPuertos) {
        # Q10 - Puertos fijos
        foreach ($p in @(3000, 8080)) {
            $duenio = Test-PuertoLibre $p
            if ($duenio) {
                $fallas.Add(('El puerto {0} lo está usando el programa "{1}". Cerralo o desinstalalo; Factum necesita ese puerto.' -f $p, $duenio))
            }
        }
        $duenio = Test-PuertoLibre 8765
        if ($duenio -and $duenio -ne 'Factum.Agent') {
            $avisos.Add(('El puerto 8765 (Tatana) lo usa el programa "{0}". Tatana no va a poder arrancar hasta que lo cierres.' -f $duenio))
        }
    }

    if ($completo) {
        # Q11 - Docker Desktop arranca con la sesion (solo aviso; no se edita el archivo)
        $autoStart = $null
        $store = Join-Path $env:APPDATA 'Docker\settings-store.json'
        $viejo = Join-Path $env:APPDATA 'Docker\settings.json'
        try {
            if (Test-Path -LiteralPath $store -PathType Leaf) {
                $autoStart = Get-PropiedadSegura (Get-Content -LiteralPath $store -Raw | ConvertFrom-Json) 'AutoStart'
            } elseif (Test-Path -LiteralPath $viejo -PathType Leaf) {
                $autoStart = Get-PropiedadSegura (Get-Content -LiteralPath $viejo -Raw | ConvertFrom-Json) 'autoStart'
            }
        } catch { $autoStart = $null }
        if ($autoStart -ne $true) {
            $avisos.Add('Docker Desktop no está configurado para arrancar al iniciar sesión. Activalo en Docker Desktop: Settings > General > Start Docker Desktop when you sign in.')
        }
    }

    return New-Object psobject -Property @{ Fallas = $fallas.ToArray(); Avisos = $avisos.ToArray() }
}

# ── Health de backend y Tatana ────────────────────────────────────────────────

function Get-BackendHealth {
    try {
        return (Invoke-RestMethod -Uri 'http://127.0.0.1:8080/health' -TimeoutSec 3 -UseBasicParsing)
    } catch {
        return $null
    }
}

function Get-TatanaHealth {
    try {
        return (Invoke-RestMethod -Uri 'http://127.0.0.1:8765/health' -TimeoutSec 3 -UseBasicParsing)
    } catch {
        return $null
    }
}

function Wait-TatanaHealth {
    param([int]$TimeoutSec = 30)
    $limite = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $limite) {
        $h = Get-TatanaHealth
        if ($null -ne $h) { return $h }
        Start-Sleep -Seconds 2
    }
    return $null
}

function Test-TatanaReal {
    # Muestra el estado de Tatana. Devuelve $false si esta en modo simulado (mock).
    param($Health)
    if ($null -eq $Health) {
        Write-Aviso 'Tatana no responde en http://localhost:8765. Seguí los pasos de la guía, sección "Tatana y drivers".'
        return $true
    }
    $mock = Get-PropiedadSegura $Health 'mock'
    if ($mock -eq $true) {
        Write-Falla 'Tatana está en modo simulado (mock = true). NO usar para peritajes: avisale al proveedor.'
        return $false
    }
    Write-Ok ('Tatana responde (version ' + (Get-PropiedadSegura $Health 'version') + ', modo real).')
    return $true
}

function Get-TatanaZip {
    param([string]$Paquete)
    $carpeta = Join-Path $Paquete 'tatana'
    if (-not (Test-Path -LiteralPath $carpeta -PathType Container)) { return $null }
    $zip = @(Get-ChildItem -LiteralPath $carpeta -Filter 'Tatana-Portable-*.zip' -File | Sort-Object Name | Select-Object -Last 1)
    if ($zip.Count -eq 0) { return $null }
    return $zip[0].FullName
}

function Install-TatanaPortable {
    # Descomprime el portatil en %TEMP% y corre su install-portable.bat (copia a
    # %LOCALAPPDATA%\Programs\Tatana, acceso en Inicio y lo arranca).
    param([string]$Zip)
    $agente = @(Get-Process -Name 'Factum.Agent' -ErrorAction SilentlyContinue)
    if ($agente.Count -gt 0) {
        Write-Host '    Deteniendo Tatana para reemplazarlo...' -ForegroundColor DarkGray
        $agente | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    $temp = Join-Path $env:TEMP ('factum-tatana-' + (Get-FactumStamp))
    Expand-Archive -LiteralPath $Zip -DestinationPath $temp -Force
    $bat = Join-Path $temp 'install-portable.bat'
    if (-not (Test-Path -LiteralPath $bat -PathType Leaf)) {
        Stop-Factum ('El zip de Tatana no trae install-portable.bat: ' + $Zip) 'Pedile al proveedor un paquete nuevo.'
    }
    # Sin capturar la salida y sin Start-Process -Wait: install-portable.bat arranca el agente
    # con "start", y tanto un pipe heredado como -Wait (que en PS 5.1 espera a los procesos
    # hijos) dejarían esta llamada colgada hasta que el agente termine.
    $p = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', ('"' + $bat + '"')) -WorkingDirectory $temp -NoNewWindow -PassThru
    $null = $p.Handle
    $p.WaitForExit()
    return $p.ExitCode
}

function Get-TatanaVersionInstalada {
    $ruta = Join-Path $env:LOCALAPPDATA 'Programs\Tatana\version.txt'
    return (Read-VersionTxt $ruta)
}

# ── Backup previo (lo usan actualizar y restaurar) ────────────────────────────

function Invoke-BackupPrevio {
    # Corre <home>\scripts\backup.ps1 -Desatendido en otro proceso (su propio registro) y devuelve
    # la carpeta del backup, o $null si falló.
    param([string]$HomeDir = $script:FactumHomeDir)
    $rutaBackup = Join-Path (Join-Path $HomeDir 'scripts') 'backup.ps1'
    $r = Invoke-Nativo -Exe 'powershell.exe' -Argumentos @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $rutaBackup, '-Desatendido', '-Carpeta', $HomeDir)
    if ($r.ExitCode -ne 0) { return $null }
    foreach ($l in $r.Salida) {
        if ($l -match '^BACKUP_DIR=(.+)$') { return $Matches[1].Trim() }
    }
    return $null
}

# ── Accesos directos ──────────────────────────────────────────────────────────

function New-AccesoDirecto {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Helper interno de instalar.ps1; crea accesos directos pedidos por la SDD.')]
    param([string]$Nombre, [string]$Destino, [string]$Carpeta, [string]$Icono = '', [int]$Ventana = 1)
    $escritorio = [Environment]::GetFolderPath('Desktop')
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut((Join-Path $escritorio ($Nombre + '.lnk')))
    $lnk.TargetPath = $Destino
    $lnk.WorkingDirectory = $Carpeta
    $lnk.WindowStyle = $Ventana
    if ($Icono -and (Test-Path -LiteralPath $Icono -PathType Leaf)) { $lnk.IconLocation = $Icono }
    $lnk.Save()
}

function Get-TamanioCarpeta {
    param([string]$Ruta)
    $total = [long]0
    $n = 0
    if (Test-Path -LiteralPath $Ruta -PathType Container) {
        foreach ($f in (Get-ArchivoRecursivo $Ruta)) { $total += $f.Length; $n++ }
    }
    return New-Object psobject -Property @{ Bytes = $total; Archivos = $n }
}

function Format-Tamanio {
    param([long]$Bytes)
    if ($Bytes -ge 1GB) { return ('{0:N1} GB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0:N1} MB' -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ('{0:N1} KB' -f ($Bytes / 1KB)) }
    return ('{0} bytes' -f $Bytes)
}
