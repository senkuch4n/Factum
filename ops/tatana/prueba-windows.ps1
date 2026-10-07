# Prueba automática del instalador de Tatana en Windows (SDD tatana-instalador-autoupdate §10, D11).
# La corre .github/workflows/tatana-release.yml (job "prueba") en windows-latest, con pwsh 7.
#
#   pwsh -File ops/tatana/prueba-windows.ps1 -Instalador <Tatana-Setup-X.Y.Z.exe> `
#       -InstaladorBase <Tatana-Setup-0.0.1.exe> -Canal <carpeta con exe, blockmap, latest.yml y
#       tatana-update.json> -PayloadZip <Tatana-Portable-vX.Y.Z-Windows.zip> -Version X.Y.Z -OrigenWeb https://…
#
# Recorre: portátil para la nube → instalar la base 0.0.1 (migra el portátil) → firma inválida
# (descarta) → actualización válida bloqueada por una operación en curso → actualización a X.Y.Z →
# sin repetición → sin red → desinstalar. En cada paso comprueba que la evidencia, los datos y la
# config local siguen intactos (D9/D10).
#
# ES DESTRUCTIVA PARA EL USUARIO QUE LA CORRE (instala, migra y desinstala Tatana en su perfil): solo
# corre en GitHub Actions o con -ForzarFueraDeCI, y se niega si ya hay datos o evidencia de Tatana.
# La prueba con USB y un celular real queda pendiente (§16.3).
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Instalador,
    [Parameter(Mandatory = $true)][string]$InstaladorBase,
    [Parameter(Mandatory = $true)][string]$Canal,
    [Parameter(Mandatory = $true)][string]$PayloadZip,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$OrigenWeb,
    [int]$PuertoCanal = 8099,
    [switch]$ForzarFueraDeCI
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ── Rutas ────────────────────────────────────────────────────────────────────
$Programas = Join-Path $env:LOCALAPPDATA 'Programs\Tatana'
$AppExe = Join-Path $Programas 'Tatana.exe'
$Desinstalador = Join-Path $Programas 'Uninstall Tatana.exe'
$AgentDir = Join-Path $Programas 'resources\agent'
$UserData = Join-Path $env:APPDATA 'Tatana'
$EstadoArchivo = Join-Path $UserData 'update-state.json'
$LocalConfigInstalado = Join-Path $UserData 'appsettings.Local.json'
$TatanaLocal = Join-Path $env:LOCALAPPDATA 'Tatana'
$DataDir = Join-Path $TatanaLocal 'data'
$Unidad = if ($env:SystemDrive) { $env:SystemDrive } else { 'C:' }
$EvidDir = Join-Path $Unidad 'Factum\Evidencia'
$Startup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\Tatana.lnk'
$Pending = Join-Path $env:LOCALAPPDATA 'tatana-agent-updater\pending'
$Base = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$Trabajo = Join-Path $Base 'tatana-prueba'
$CanalServido = Join-Path $Trabajo 'canal'
$AgenteUrl = 'http://localhost:8765'
$CaseId = '0123456789abcdef01234567'
$resumen = [System.Collections.Generic.List[string]]::new()
$servidor = $null

function Ok([string]$texto) { Write-Host "OK   $texto"; $resumen.Add("- OK $texto") }
function Falla([string]$texto) { throw "FALLA  $texto" }
function Paso([string]$texto) { Write-Host "`n== $texto"; $resumen.Add("`n**$texto**`n") }
function Hash([string]$ruta) { (Get-FileHash -Algorithm SHA256 -LiteralPath $ruta).Hash }

# ── Guardas: nunca en la PC de alguien con evidencia ─────────────────────────
if ($env:GITHUB_ACTIONS -ne 'true' -and -not $ForzarFueraDeCI) {
    Falla 'esta prueba instala, migra y desinstala Tatana en tu perfil: solo corre en GitHub Actions (o con -ForzarFueraDeCI en una VM descartable)'
}
foreach ($d in @($DataDir, $EvidDir, $Programas)) {
    if ((Test-Path $d) -and (Get-ChildItem -LiteralPath $d -Force -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1)) {
        Falla "ya existe $d con archivos: esta prueba necesita un perfil limpio (no toca datos ni evidencia ajenos)"
    }
}
foreach ($f in @($Instalador, $InstaladorBase, $PayloadZip, (Join-Path $Canal 'tatana-update.json'))) {
    if (-not (Test-Path -LiteralPath $f)) { Falla "no existe $f" }
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { Falla "versión inválida: $Version" }
New-Item -ItemType Directory -Force -Path $Trabajo, $CanalServido | Out-Null

# ── Helpers ──────────────────────────────────────────────────────────────────
function Obtener([string]$ruta) {
    try { return Invoke-RestMethod -Uri "$AgenteUrl$ruta" -TimeoutSec 5 } catch { return $null }
}

function Esperar-Health([string]$Version, [int]$Segundos = 60) {
    $limite = (Get-Date).AddSeconds($Segundos)
    $ultima = $null
    while ((Get-Date) -lt $limite) {
        $h = Obtener '/health'
        if ($null -ne $h) {
            $ultima = $h.version
            if ($h.version -eq $Version) { return $h }
        }
        Start-Sleep -Seconds 1
    }
    Falla "/health no respondió con version $Version en $Segundos s (última: $ultima)"
}

function Leer-Estado {
    if (-not (Test-Path -LiteralPath $EstadoArchivo)) { return $null }
    try { return Get-Content -LiteralPath $EstadoArchivo -Raw | ConvertFrom-Json } catch { return $null }
}

function Tiene([object]$obj, [string]$prop) {
    return $null -ne $obj -and ($obj.PSObject.Properties.Name -contains $prop) -and $null -ne $obj.$prop
}

# Espera la fase en update-state.json. -Desde: el chequeo tiene que ser posterior (checkedAt), así no
# se toma por buena la fase que dejó la corrida anterior.
function Esperar-Fase([string]$Fase, [datetime]$Desde = [datetime]::MinValue, [int]$Segundos = 180) {
    $limite = (Get-Date).AddSeconds($Segundos)
    $ultima = $null
    while ((Get-Date) -lt $limite) {
        $e = Leer-Estado
        if ($null -ne $e) {
            $ultima = $e.phase
            $reciente = $true
            if ($Desde -gt [datetime]::MinValue -and (Tiene $e 'checkedAt')) {
                # pwsh 7 ya convierte las fechas ISO de ConvertFrom-Json a [datetime].
                $c = $e.checkedAt
                $utc = if ($c -is [datetime]) { $c.ToUniversalTime() } else { [DateTimeOffset]::Parse([string]$c).UtcDateTime }
                $reciente = ($utc -ge $Desde.ToUniversalTime().AddSeconds(-2))
            }
            if ($e.phase -eq $Fase -and $reciente) { return $e }
        }
        Start-Sleep -Seconds 2
    }
    Falla "update-state.json no llegó a '$Fase' en $Segundos s (última fase: $ultima)"
}

# Ojo: una función que devuelve @(...) igual se desenrolla al salir. Con 1 elemento llega el objeto
# suelto y con 0 llega $null; con Set-StrictMode, `$null.Count` falla ("The property 'Count' cannot
# be found"). Siempre envolver la llamada: @(Procesos-Tatana), @(Duenos-8765).
function Procesos-Tatana { @(Get-Process -Name 'Tatana' -ErrorAction SilentlyContinue) }

# PIDs que escuchan en 8765. Si Get-NetTCPConnection (CIM) no devuelve nada, se usa netstat como
# respaldo. Se toman las filas TCP con dirección local :8765 y remota :0, que son las de escucha;
# así no depende del idioma de la columna de estado.
function Duenos-8765 {
    $ids = @(Get-NetTCPConnection -LocalPort 8765 -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
    if ($ids.Count -eq 0) {
        $ids = @(netstat -ano -p TCP; netstat -ano -p TCPv6) |
            ForEach-Object { if ($_ -match '^\s*TCP\s+\S+:8765\s+\S+:0\s+\S+\s+(\d+)\s*$') { [int]$Matches[1] } } |
            Select-Object -Unique
        $ids = @($ids)
    }
    $ids
}

# Para los mensajes de falla: quién escucha en 8765 según netstat.
function Netstat-8765 { (@(netstat -ano -p TCP; netstat -ano -p TCPv6) | Where-Object { $_ -match ':8765\s' }) -join '; ' }

# Espera SOLO a ese proceso, con tope. No usar `Start-Process -Wait`: en pwsh 7 espera también a los
# descendientes, y install-portable.bat deja corriendo Factum.Agent y el navegador (el job se colgó
# hasta el timeout). Si se pasa del tope, mata solo ese proceso (no el árbol) y falla con un mensaje claro.
function Iniciar-Proceso([string]$Archivo, [string[]]$Argumentos, [string]$Directorio = $null) {
    $opc = @{ FilePath = $Archivo; ArgumentList = $Argumentos; PassThru = $true; WindowStyle = 'Hidden' }
    if ($Directorio) { $opc.WorkingDirectory = $Directorio }
    $p = Start-Process @opc
    $null = $p.Handle   # retiene el handle: sin esto ExitCode puede quedar vacío al terminar
    return $p
}

function Esperar-Salida([System.Diagnostics.Process]$Proceso, [string]$Que, [int]$Segundos) {
    if (-not $Proceso.WaitForExit($Segundos * 1000)) {
        Stop-Process -Id $Proceso.Id -Force -ErrorAction SilentlyContinue
        Falla "$Que no terminó en $Segundos s (PID $($Proceso.Id), se lo detuvo)"
    }
    return $Proceso.ExitCode
}

# Navegadores que ya estaban antes de la prueba: al final se cierran solo los que abrió ella
# (launch-tatana.bat abre CLIENT_URL; el portátil de los peritos no tiene un modo sin navegador
# para la nube y no se cambia).
$NombresNavegador = @('msedge', 'chrome', 'firefox')
$navegadoresPrevios = @(Get-Process -Name $NombresNavegador -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
function Cerrar-Navegadores-De-La-Prueba {
    Get-Process -Name $NombresNavegador -ErrorAction SilentlyContinue |
        Where-Object { $navegadoresPrevios -notcontains $_.Id } |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Lanzar-App {
    $script:lanzado = [datetime]::UtcNow
    Start-Process -FilePath $AppExe -ArgumentList '--hidden' | Out-Null
    return $script:lanzado
}

function Cerrar-App([int]$Segundos = 60) {
    if (@(Procesos-Tatana).Count -eq 0) { return }
    Start-Process -FilePath $AppExe -ArgumentList '--quit' | Out-Null
    $limite = (Get-Date).AddSeconds($Segundos)
    while ((Get-Date) -lt $limite) {
        if (@(Procesos-Tatana).Count -eq 0 -and @(Duenos-8765).Count -eq 0) { return }
        Start-Sleep -Seconds 1
    }
    Falla "Tatana no se cerró con --quit en $Segundos s"
}

# Copy-Item conserva la mtime del original. Si se restaura un manifiesto con la misma mtime (o con
# otra dentro del mismo segundo), http.server contesta 304 a un pedido condicional y un cliente con
# caché reusaría el cuerpo anterior (run 37637630657). Cada publicación lleva una mtime nueva y
# estrictamente creciente: al menos 2 s más que la anterior, porque Last-Modified tiene resolución
# de 1 s.
$script:ultimaMtimeCanal = [datetime]::MinValue
function Servir-Canal([string[]]$Archivos) {
    Get-ChildItem -LiteralPath $CanalServido -Force | Remove-Item -Force -Recurse
    $mtime = Get-Date
    if ($mtime -lt $script:ultimaMtimeCanal.AddSeconds(2)) { $mtime = $script:ultimaMtimeCanal.AddSeconds(2) }
    $script:ultimaMtimeCanal = $mtime
    foreach ($a in $Archivos) {
        Copy-Item -LiteralPath $a -Destination $CanalServido
        (Get-Item -LiteralPath (Join-Path $CanalServido (Split-Path -Leaf $a))).LastWriteTime = $mtime
    }
}

$centinelas = @{}
function Verificar-Centinelas([string]$momento) {
    foreach ($ruta in $centinelas.Keys) {
        if (-not (Test-Path -LiteralPath $ruta)) { Falla "$momento`: desapareció $ruta" }
        if ((Hash $ruta) -ne $centinelas[$ruta]) { Falla "$momento`: cambió $ruta" }
    }
    Ok "$momento`: datos, evidencia y config local intactos ($($centinelas.Count) centinelas)"
}

function Nuevo-Aleatorio([string]$ruta, [int]$bytes) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ruta) | Out-Null
    $buf = New-Object byte[] $bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($buf)
    [IO.File]::WriteAllBytes($ruta, $buf)
}

try {
    $env:TATANA_UPDATE_URLS = "http://127.0.0.1:$PuertoCanal/"
    $servidor = Start-Process -FilePath python -ArgumentList @('-m', 'http.server', "$PuertoCanal", '--bind', '127.0.0.1', '--directory', $CanalServido) `
        -PassThru -RedirectStandardError (Join-Path $Trabajo 'canal-http.log')

    # ── 1. Estado previo: portátil para la nube ──────────────────────────────
    Paso '1. Portátil para la nube con evidencia'
    $portable = Join-Path $Trabajo 'portable'
    Expand-Archive -LiteralPath $PayloadZip -DestinationPath $portable -Force
    $ini = Get-Content -LiteralPath (Join-Path $portable 'tatana-portable.ini') -Raw
    if ($ini -notmatch 'CLIENT_URL=https://') { Falla "el .ini del payload no es para la nube:`n$ini" }
    # Se espera solo al cmd del .bat: Factum.Agent (lanzado por launch-tatana.bat) queda corriendo a
    # propósito, porque el paso 2 prueba que el instalador detenga un portátil EN USO antes de migrarlo.
    $p = Iniciar-Proceso 'cmd.exe' @('/c', 'install-portable.bat') $portable
    $codigo = Esperar-Salida $p 'install-portable.bat' 180
    if ($codigo -ne 0) { Falla "install-portable.bat salió con '$codigo'" }
    $h = Esperar-Health $Version
    Ok "portátil $($h.version) corriendo desde $Programas"
    $agentePortable = @(Get-Process -Name 'Factum.Agent' -ErrorAction SilentlyContinue | Where-Object {
            $r = $null; try { $r = $_.Path } catch { }
            $r -and $r.StartsWith($Programas + '\', [StringComparison]::OrdinalIgnoreCase) })
    if ($agentePortable.Count -eq 0) { Falla "no hay un Factum.Agent corriendo desde $Programas" }
    Ok "Factum.Agent del portátil en uso (PID $($agentePortable[0].Id)): lo tiene que detener el instalador"
    if (-not (Test-Path -LiteralPath $Startup)) { Falla "el portátil no creó $Startup" }

    $centinelaDatos = Join-Path $DataDir 'cases\ci-centinela\evidencia.bin'
    $centinelaEvid = Join-Path $EvidDir 'ci-centinela\caso.zip'
    $centinelaConfig = Join-Path $Programas 'appsettings.Local.json'
    Nuevo-Aleatorio $centinelaDatos 1MB
    Nuevo-Aleatorio $centinelaEvid 1MB
    # Fija orígenes (D9). Incluye el origen web: la fusión de arrays de .NET pisa por índice.
    $configJson = @{ Agent = @{ AllowedOrigins = @('https://ci.invalid', $OrigenWeb) } } | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath $centinelaConfig -Value $configJson -Encoding utf8
    $hashConfig = Hash $centinelaConfig
    $centinelas[$centinelaDatos] = Hash $centinelaDatos
    $centinelas[$centinelaEvid] = Hash $centinelaEvid
    Ok 'centinelas creados (datos, evidencia, appsettings.Local.json del portátil)'

    # ── 2. Instalar la base 0.0.1 (migra el portátil) ────────────────────────
    Paso '2. Instalar la base 0.0.1 en silencio'
    # Solo el proceso del instalador (si NSIS arrancara la app al terminar, no se la espera).
    $p = Iniciar-Proceso $InstaladorBase @('/S')
    $codigo = Esperar-Salida $p 'el instalador base' 300
    if ($codigo -ne 0) { Falla "el instalador base salió con '$codigo' (ver %APPDATA%\Tatana\logs\instalador.log)" }
    Ok 'instalador base: exit 0'
    $limite = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $limite -and @($agentePortable | Where-Object { -not $_.HasExited }).Count -gt 0) { Start-Sleep -Seconds 1 }
    if (@($agentePortable | Where-Object { -not $_.HasExited }).Count -gt 0) { Falla 'el instalador no detuvo el Factum.Agent del portátil' }
    Ok 'el instalador detuvo el Factum.Agent del portátil'
    if (-not (Test-Path -LiteralPath $Desinstalador)) { Falla "no existe $Desinstalador" }
    $archivado = @(Get-ChildItem -LiteralPath $TatanaLocal -Directory -Filter 'portable-anterior-*' -ErrorAction SilentlyContinue)
    if ($archivado.Count -ne 1) { Falla "se esperaba una carpeta portable-anterior-* en $TatanaLocal (hay $($archivado.Count))" }
    Ok "portátil archivado en $($archivado[0].FullName)"
    if (Test-Path -LiteralPath $Startup) { Falla "sigue $Startup (dos Tatana al iniciar sesión)" }
    Ok 'sin el Tatana.lnk del portátil en Inicio'
    if (-not (Test-Path -LiteralPath $LocalConfigInstalado)) { Falla "no se migró $LocalConfigInstalado" }
    if ((Hash $LocalConfigInstalado) -ne $hashConfig) { Falla 'el appsettings.Local.json migrado no es idéntico al del portátil' }
    $centinelas[$LocalConfigInstalado] = $hashConfig
    Ok 'appsettings.Local.json migrado a %APPDATA%\Tatana sin cambios'
    $migracion = Join-Path $UserData 'migracion-portable.json'
    if (-not (Test-Path -LiteralPath $migracion)) { Falla "falta $migracion" }
    $m = Get-Content -LiteralPath $migracion -Raw | ConvertFrom-Json
    if ($m.portable_version -ne $Version) { Falla "migracion-portable.json.portable_version = $($m.portable_version)" }
    Ok "migracion-portable.json (portable_version $($m.portable_version))"
    Verificar-Centinelas 'después de instalar'

    # ── 3. Arrancar la base con el canal vacío ───────────────────────────────
    Paso '3. Arrancar la base 0.0.1'
    Servir-Canal @()
    $null = Lanzar-App
    $h = Esperar-Health '0.0.1'
    $info = Obtener '/info'
    if ($info.mode -ne 'installed') { Falla "/info.mode = $($info.mode)" }
    foreach ($cap in @('real_version_v1', 'agent_state_v1', 'case_evidence_v1')) {
        if (-not ($h.capabilities -contains $cap)) { Falla "/health.capabilities sin $cap ($($h.capabilities -join ', '))" }
    }
    $toolsBase = Join-Path $AgentDir 'tools'
    foreach ($t in @('adb', 'scrcpy', 'ffmpeg', 'python')) {
        $tool = $h.tools.$t
        if (-not $tool.found) { Falla "/health.tools.$t no encontrado" }
        if (-not ([string]$tool.path).StartsWith($toolsBase, [StringComparison]::OrdinalIgnoreCase)) {
            Falla "/health.tools.$t.path = $($tool.path), se esperaba bajo $toolsBase"
        }
    }
    Ok '/health: 0.0.1, modo installed, capabilities y herramientas del instalador'
    $estado = Obtener '/agent/state'
    if ($null -eq $estado) { Falla '/agent/state no respondió' }
    if (-not $estado.local_config.loaded -or -not $estado.local_config.overrides_allowed_origins) {
        Falla "/agent/state.local_config = $($estado.local_config | ConvertTo-Json -Compress)"
    }
    if ($estado.local_config.path -ne $LocalConfigInstalado) { Falla "local_config.path = $($estado.local_config.path)" }
    Ok '/agent/state: config local cargada y fija los orígenes (D9)'
    $duenos = @(Duenos-8765)
    if ($duenos.Count -ne 1) { Falla "8765 tiene $($duenos.Count) dueños (PIDs: $($duenos -join ', '); netstat: $(Netstat-8765))" }
    $ruta = (Get-Process -Id $duenos[0] -ErrorAction SilentlyContinue).Path
    if (-not $ruta -or -not $ruta.StartsWith($AgentDir, [StringComparison]::OrdinalIgnoreCase)) { Falla "8765 lo escucha $ruta" }
    Ok "un solo agente en 8765 ($ruta)"
    $run = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
    $enRun = $false
    if ($null -ne $run) {
        foreach ($prop in $run.PSObject.Properties) { if ([string]$prop.Value -like '*Tatana.exe*') { $enRun = $true } }
    }
    if (-not $enRun) { Falla 'Tatana no quedó en HKCU\...\Run (arranque al iniciar sesión)' }
    Ok 'arranque al iniciar sesión registrado'
    $codigo = & curl.exe -s -o NUL -w '%{http_code}' -H "Origin: $OrigenWeb" "$AgenteUrl/health"
    if ($codigo -ne '200') { Falla "Origin $OrigenWeb → $codigo" }
    $codigo = & curl.exe -s -o NUL -w '%{http_code}' -H 'Origin: https://ci.invalid' "$AgenteUrl/health"
    if ($codigo -ne '200') { Falla "Origin https://ci.invalid (config local) → $codigo" }
    $codigo = & curl.exe -s -o NUL -w '%{http_code}' -H 'Origin: https://evil.invalid' "$AgenteUrl/health"
    if ($codigo -ne '403') { Falla "Origin https://evil.invalid → $codigo" }
    $codigo = & curl.exe -s -o NUL -w '%{http_code}' -X POST -H "Origin: $OrigenWeb" "$AgenteUrl/agent/maintenance"
    if ($codigo -ne '403') { Falla "POST /agent/maintenance desde el navegador → $codigo" }
    Ok 'orígenes: web y config local 200, ajeno 403; mantenimiento solo local'

    # ── 4. Firma inválida ────────────────────────────────────────────────────
    Paso '4. Manifiesto con firma inválida'
    $refirmado = Join-Path $Trabajo 'refirmado\tatana-update.json'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $refirmado) | Out-Null
    $js = @'
const fs = require('fs'), c = require('crypto');
const [src, dst] = process.argv.slice(1);
const env = JSON.parse(fs.readFileSync(src, 'utf8'));
const { privateKey } = c.generateKeyPairSync('ed25519');
const sig = c.sign(null, Buffer.from(env.payload, 'base64'), privateKey);
fs.writeFileSync(dst, JSON.stringify({ ...env, signature: sig.toString('base64') }));
'@
    & node -e $js (Join-Path $Canal 'tatana-update.json') $refirmado
    if ($LASTEXITCODE -ne 0) { Falla 'no se pudo re-firmar el manifiesto' }
    $exeV = Join-Path $Canal "Tatana-Setup-$Version.exe"
    $archivosCanal = @($exeV, "$exeV.blockmap", (Join-Path $Canal 'latest.yml'))
    Servir-Canal ($archivosCanal + $refirmado)
    Cerrar-App
    $desde = Lanzar-App
    $null = Esperar-Health '0.0.1'
    $e = Esperar-Fase 'rejected' $desde
    if ($e.reason -ne 'firma_invalida') { Falla "reason = $($e.reason)" }
    if ((Test-Path -LiteralPath $Pending) -and (Get-ChildItem -LiteralPath $Pending -Force | Select-Object -First 1)) {
        Falla "se descargó algo con una firma inválida: $Pending"
    }
    if ((Obtener '/health').version -ne '0.0.1') { Falla 'cambió la versión con una firma inválida' }
    Ok 'firma inválida: rejected/firma_invalida, nada descargado, sigue 0.0.1'

    # ── 5. Actualización válida con una operación en curso ───────────────────
    Paso "5. Actualización a $Version con una subida en curso"
    Servir-Canal ($archivosCanal + (Join-Path $Canal 'tatana-update.json'))
    Cerrar-App
    $desde = Lanzar-App
    $null = Esperar-Health '0.0.1'
    $e = Esperar-Fase 'ready' $desde -Segundos 300
    if ($e.availableVersion -ne $Version) { Falla "availableVersion = $($e.availableVersion)" }
    Ok "descargada y verificada: ready ($($e.availableVersion))"

    $subida = Join-Path $Trabajo 'ci-subida.bin'
    Nuevo-Aleatorio $subida 4MB
    $hashSubida = Hash $subida
    $codigoSubida = Join-Path $Trabajo 'subida.code'
    $curl = Start-Process -FilePath 'curl.exe' -PassThru -RedirectStandardOutput $codigoSubida -ArgumentList @(
        '-s', '-o', 'NUL', '-w', '%{http_code}', '--limit-rate', '100k', '-X', 'POST',
        '--data-binary', "@$subida", '-H', 'Content-Type: application/octet-stream',
        "$AgenteUrl/cases/$CaseId/evidence/upload?filename=ci-subida.bin")
    $limite = (Get-Date).AddSeconds(15)
    $ocupado = $false
    while ((Get-Date) -lt $limite -and -not $ocupado) {
        $s = Obtener '/agent/state'
        if ($null -ne $s -and $s.busy) { $ocupado = $true } else { Start-Sleep -Milliseconds 300 }
    }
    if (-not $ocupado) { Falla '/agent/state.busy no se puso en true durante la subida' }
    Ok '/agent/state.busy = true durante la subida'
    Start-Process -FilePath $AppExe -ArgumentList '--install-update' | Out-Null
    Start-Sleep -Seconds 10
    if ($curl.HasExited) { Falla 'la subida terminó antes de comprobar el bloqueo (subí el tamaño o bajá --limit-rate)' }
    if ((Obtener '/health').version -ne '0.0.1') { Falla 'se instaló con una operación en curso' }
    $e = Leer-Estado
    if ($e.phase -ne 'ready' -or -not (Tiene $e 'busyOperations') -or @($e.busyOperations).Count -eq 0) {
        Falla "con la subida en curso: phase=$($e.phase), busyOperations=$(@($e.busyOperations) -join ',')"
    }
    Ok "no instala con operaciones en curso (busyOperations: $(@($e.busyOperations) -join ', '))"
    $null = $curl.Handle
    if (-not $curl.WaitForExit(180000)) {
        Stop-Process -Id $curl.Id -Force -ErrorAction SilentlyContinue
        Falla 'la subida no terminó en 180 s'
    }
    $curl.WaitForExit()   # vacía la salida redirigida
    $codigo = (Get-Content -LiteralPath $codigoSubida -Raw).Trim()
    if ($codigo -ne '200') { Falla "la subida terminó con HTTP $codigo" }
    $subidaDestino = Join-Path $DataDir "cases\$CaseId\ci-subida.bin"
    if ((Hash $subidaDestino) -ne $hashSubida) { Falla 'la subida no quedó idéntica' }
    $centinelas[$subidaDestino] = $hashSubida
    Ok 'subida terminada (200) e idéntica'

    Start-Process -FilePath $AppExe -ArgumentList '--install-update' | Out-Null
    $h = Esperar-Health $Version -Segundos 180
    $s = Obtener '/agent/state'
    if ($s.version -ne $Version) { Falla "/agent/state.version = $($s.version)" }
    if (@(Procesos-Tatana).Count -eq 0) { Falla 'la app no se relanzó después de actualizar' }
    $e = Esperar-Fase 'up_to_date' -Segundos 120
    Ok "actualizado a $Version, la app siguió corriendo y quedó up_to_date"
    Verificar-Centinelas 'después de actualizar'
    if ((Hash $LocalConfigInstalado) -ne $hashConfig) { Falla 'la actualización cambió appsettings.Local.json' }

    # ── 6. Sin repetición ────────────────────────────────────────────────────
    Paso '6. Mismo canal: no repite'
    Cerrar-App
    $desde = Lanzar-App
    $null = Esperar-Health $Version
    $null = Esperar-Fase 'up_to_date' $desde
    if ((Obtener '/health').version -ne $Version) { Falla 'cambió la versión' }
    Ok "sigue en $Version (up_to_date)"

    # ── 7. Sin red ───────────────────────────────────────────────────────────
    Paso '7. Sin red'
    Cerrar-App
    $env:TATANA_UPDATE_URLS = 'http://127.0.0.1:9/'
    $desde = Lanzar-App
    $null = Esperar-Health $Version -Segundos 15
    Ok 'arranca en ≤ 15 s sin canal'
    $null = Esperar-Fase 'offline' $desde
    Ok 'fase offline'
    $env:TATANA_UPDATE_URLS = "http://127.0.0.1:$PuertoCanal/"

    # ── 8. Desinstalar ───────────────────────────────────────────────────────
    Paso '8. Desinstalar'
    Cerrar-App
    $p = Iniciar-Proceso $Desinstalador @('/S')
    $codigo = Esperar-Salida $p 'el desinstalador' 120
    if ($codigo -ne 0) { Falla "el desinstalador salió con '$codigo'" }
    # El desinstalador de NSIS se copia a %TEMP% y sigue: se espera a que termine de borrar.
    $limite = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $limite -and (Test-Path -LiteralPath $Programas) -and
           (Get-ChildItem -LiteralPath $Programas -Recurse -File -Force -ErrorAction SilentlyContinue | Select-Object -First 1)) {
        Start-Sleep -Seconds 2
    }
    if ((Test-Path -LiteralPath $Programas) -and
        (Get-ChildItem -LiteralPath $Programas -Recurse -File -Force -ErrorAction SilentlyContinue | Select-Object -First 1)) {
        Falla "quedaron archivos en $Programas"
    }
    Ok "se borró $Programas"
    Verificar-Centinelas 'después de desinstalar'
    if (@(Duenos-8765).Count -ne 0) { Falla 'algo sigue escuchando en 8765' }
    Ok 'nada escucha en 8765'

    $resumen.Insert(0, "### Tatana ${Version}: prueba del instalador OK`n")
}
catch {
    $resumen.Insert(0, "### Tatana ${Version}: prueba del instalador FALLÓ`n`n$($_.Exception.Message)`n")
    throw
}
finally {
    Cerrar-Navegadores-De-La-Prueba
    if ($null -ne $servidor -and -not $servidor.HasExited) { Stop-Process -Id $servidor.Id -Force -ErrorAction SilentlyContinue }
    if ($env:GITHUB_STEP_SUMMARY) { $resumen -join "`n" | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 }
}
Write-Host "`nRESULTADO: OK"
