# Factum en la fiscalía — PLAN A: prepara una PC WINDOWS para usar el servidor de la fiscalía (o del GIF).
# Equivalente de preparar-pc-perito.sh (Linux). Se corre DESDE EL PAQUETE, como administrador, con el
# ca-factum-fiscalia.crt que exportó instalar-servidor.sh al lado:
#
#   perito\preparar-pc-windows.cmd <IP-del-servidor> [-Ca <ruta .crt>] [-Simular]
#   (o: powershell -NoProfile -ExecutionPolicy Bypass -File perito\preparar-pc-windows.ps1 -IP <IP> ...)
#
# Hace (todo idempotente; se puede volver a correr, p. ej. si cambia la IP del servidor):
#   1. hosts: "<IP> factum.internal" (reemplaza una línea anterior de factum.internal)
#   2. Instala la CA de Factum en "Entidades de certificación raíz de confianza" del equipo (Chrome y Edge la usan)
#      y activa en Firefox la política ImportEnterpriseRoots (que confíe en esas mismas raíces)
#   3. Tatana: %APPDATA%\Tatana\appsettings.Local.json del usuario que tiene la sesión abierta, con
#      Agent.AllowedOrigins = localhost + https://factum.internal (sin pisar otras claves)
#   4. Prueba https://factum.internal/health
# Tatana se instala aparte (Tatana-Setup-X.Y.Z.exe); este script solo lo configura.
# Probado para Windows PowerShell 5.1 (viene con Windows 10/11). Guardar en UTF-8 con BOM (si no, 5.1 rompe los acentos).
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$IP = '',
    [string]$Ca = '',
    [switch]$Simular
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Nombre = 'factum.internal'          # FACTUM_NOMBRE_RED de comun/_comun.sh
$CaArchivo = 'ca-factum-fiscalia.crt' # FACTUM_CA_ARCHIVO de comun/_comun.sh
$Paq = Split-Path -Parent $PSScriptRoot
$HostsF = if ($env:FACTUM_HOSTS_PRUEBA) { $env:FACTUM_HOSTS_PRUEBA } else { Join-Path $env:SystemRoot 'System32\drivers\etc\hosts' }

function Paso([int]$n, [string]$t) { Write-Host ''; Write-Host "[$n/4] $t" -ForegroundColor Cyan }
function Ok([string]$t) { Write-Host "  OK  $t" -ForegroundColor Green }
function Info([string]$t) { Write-Host "      $t" }
function Aviso([string]$t) { Write-Host "  AVISO  $t" -ForegroundColor Yellow }
function Morir([string]$t, [string]$ayuda = '') {
    Write-Host "  ERROR  $t" -ForegroundColor Red
    if ($ayuda) { Write-Host "         $ayuda" }
    exit 1
}
function Hacer([string]$desc, [scriptblock]$accion) {
    if ($Simular) { Write-Host "  [simular] $desc" } else { & $accion }
}
function Escribir-Utf8([string]$ruta, [string]$texto) {
    $tmp = "$ruta.nuevo"
    [IO.File]::WriteAllText($tmp, $texto, (New-Object Text.UTF8Encoding $false))
    Move-Item -LiteralPath $tmp -Destination $ruta -Force
}

# Con doble clic / "Ejecutar como administrador" no hay argumentos: se pide la IP.
if (-not $IP) { $IP = (Read-Host 'IP del servidor de Factum (la que mostró instalar-servidor.sh)').Trim() }
$ipObj = $null
if ($IP -notmatch '^\d{1,3}(\.\d{1,3}){3}$' -or -not [Net.IPAddress]::TryParse($IP, [ref]$ipObj)) {
    Morir "'$IP' no es una IP (tiene que ser como 192.168.0.10)." 'La muestra instalar-servidor.sh al terminar, o diagnostico.sh en el servidor.'
}

Write-Host ''
Write-Host "=== Preparar esta PC Windows para Factum (servidor $IP) ==="
if ($Simular) { Write-Host '  Modo -Simular: no se cambia nada.' }
$esAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $esAdmin -and -not $Simular) {
    Morir 'hace falta correrlo como administrador (para el archivo hosts y el certificado).' `
        'Clic derecho en preparar-pc-windows.cmd > "Ejecutar como administrador", o desde una consola de administrador.'
}

# Usuario con la sesión abierta (puede no ser el administrador que corre el script): su %APPDATA% es el de Tatana.
$appData = $env:APPDATA
$usuario = $env:USERNAME
try {
    $sesion = (Get-CimInstance Win32_ComputerSystem).UserName
    if ($sesion) {
        $sid = (New-Object Security.Principal.NTAccount $sesion).Translate([Security.Principal.SecurityIdentifier]).Value
        $perfil = Get-CimInstance Win32_UserProfile -Filter "SID='$sid'"
        if ($perfil -and $perfil.LocalPath) {
            $usuario = $sesion
            $appData = Join-Path $perfil.LocalPath 'AppData\Roaming'
        }
    }
} catch {
    Aviso "No pude averiguar el usuario con la sesión abierta; uso el actual ($usuario)."
}
Info "Usuario de Tatana: $usuario ($appData)"

# ── 1. Nombre ───────────────────────────────────────────────────────────────────────────────
Paso 1 "Nombre $Nombre -> $IP"
$patron = "^\s*[^#\s]+\s+(.*\s)?$([regex]::Escape($Nombre))(\s|$)"
$lineas = @()
if (Test-Path -LiteralPath $HostsF) { $lineas = @(Get-Content -LiteralPath $HostsF) }
$viejas = @($lineas | Where-Object { $_ -match $patron })
if ($Simular) {
    Write-Host "  [simular] dejar en $HostsF una sola línea: $IP $Nombre"
    $viejas | ForEach-Object { Write-Host "  [simular]   (se reemplaza) $_" }
} else {
    if (-not (Test-Path -LiteralPath "$HostsF.antes-de-factum-fiscalia") -and (Test-Path -LiteralPath $HostsF)) {
        Copy-Item -LiteralPath $HostsF -Destination "$HostsF.antes-de-factum-fiscalia"
    }
    $nuevas = @($lineas | Where-Object { $_ -notmatch $patron }) + "$IP $Nombre"
    # hosts en ASCII y CRLF, como lo deja Windows.
    $tmp = "$HostsF.nuevo"
    [IO.File]::WriteAllText($tmp, (($nuevas -join "`r`n") + "`r`n"), [Text.Encoding]::ASCII)
    Move-Item -LiteralPath $tmp -Destination $HostsF -Force
    ipconfig /flushdns | Out-Null
    Ok "${HostsF}: $IP $Nombre"
}

# ── 2. Certificado ──────────────────────────────────────────────────────────────────────────
Paso 2 'Certificado de Factum (CA interna del servidor)'
if (-not $Ca) { $Ca = Join-Path $Paq $CaArchivo }
if (-not (Test-Path -LiteralPath $Ca)) {
    Morir "no encuentro $Ca." "Copiá al pendrive el $CaArchivo que dejó instalar-servidor.sh (también está en /srv/factum-fiscalia/ del servidor) o indicalo con -Ca <ruta>."
}
try {
    $cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2 (Resolve-Path -LiteralPath $Ca).Path
} catch {
    Morir "$Ca no es un certificado válido."
}
$sha = [Security.Cryptography.SHA256]::Create()
$huella = (($sha.ComputeHash($cert.RawData) | ForEach-Object { $_.ToString('X2') }) -join ':')
Info "subject=$($cert.Subject)"
Info "Huella SHA-256: $huella"
Info '(tiene que coincidir con la que mostró el instalador del servidor)'
$ya = @(Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Thumbprint -eq $cert.Thumbprint })
if ($ya.Count -gt 0) {
    Ok 'La CA ya estaba instalada en el equipo.'
} else {
    Hacer "importar $Ca en Cert:\LocalMachine\Root" {
        Import-Certificate -FilePath $Ca -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    }
    Ok 'Equipo: Entidades de certificación raíz de confianza (Chrome y Edge).'
}
Hacer 'política de Firefox: Certificates.ImportEnterpriseRoots = 1' {
    $clave = 'HKLM:\SOFTWARE\Policies\Mozilla\Firefox\Certificates'
    New-Item -Path $clave -Force | Out-Null
    New-ItemProperty -Path $clave -Name 'ImportEnterpriseRoots' -Value 1 -PropertyType DWord -Force | Out-Null
}
Ok 'Firefox: confía en las raíces del equipo (política ImportEnterpriseRoots).'
Info 'Cerrá y volvé a abrir el navegador (todas las ventanas) para que tome el certificado.'

# ── 3. Tatana ───────────────────────────────────────────────────────────────────────────────
Paso 3 'Tatana (agente de captura por USB)'
$confDir = Join-Path $appData 'Tatana'
$conf = Join-Path $confDir 'appsettings.Local.json'
$origen = "https://$Nombre"
if ($Simular) {
    Write-Host "  [simular] escribir/actualizar $conf con Agent.AllowedOrigins = http://localhost:3000, http://127.0.0.1:3000, $origen (+ los que ya tenga)"
} else {
    New-Item -ItemType Directory -Path $confDir -Force | Out-Null
    $datos = New-Object PSObject
    if ((Test-Path -LiteralPath $conf) -and (Get-Item -LiteralPath $conf).Length -gt 0) {
        try {
            $datos = [IO.File]::ReadAllText($conf) | ConvertFrom-Json
        } catch {
            Morir "$conf no es un JSON válido." 'Corregilo o renombralo y volvé a correr este script.'
        }
        if (-not (Test-Path -LiteralPath "$conf.antes-de-factum-fiscalia")) {
            Copy-Item -LiteralPath $conf -Destination "$conf.antes-de-factum-fiscalia"
        }
    }
    # En Tatana, Agent:AllowedOrigins REEMPLAZA a los defaults (OriginPolicy.cs): se dejan siempre los
    # localhost + el servidor + los que ya hubiera. Las otras claves no se tocan.
    if (-not ($datos.PSObject.Properties.Name -contains 'Agent')) {
        $datos | Add-Member -NotePropertyName Agent -NotePropertyValue (New-Object PSObject)
    }
    $agente = $datos.Agent
    $previos = @()
    if ($agente.PSObject.Properties.Name -contains 'AllowedOrigins' -and $agente.AllowedOrigins) { $previos = @($agente.AllowedOrigins) }
    $lista = New-Object Collections.Generic.List[string]
    foreach ($o in (@('http://localhost:3000', 'http://127.0.0.1:3000') + $previos + @($origen))) {
        $o = ([string]$o).Trim().ToLowerInvariant().TrimEnd('/')
        if ($o -and -not $lista.Contains($o)) { $lista.Add($o) }
    }
    $agente | Add-Member -NotePropertyName AllowedOrigins -NotePropertyValue ([string[]]$lista.ToArray()) -Force
    Escribir-Utf8 $conf (($datos | ConvertTo-Json -Depth 20) + "`n")
    Ok $conf
    Info "Agent.AllowedOrigins = $($lista -join ', ')"
}
Info 'Si Tatana ya estaba abierto, cerralo desde la bandeja (junto al reloj) y abrilo de nuevo: lee la config al arrancar.'

# ── 4. Prueba ───────────────────────────────────────────────────────────────────────────────
Paso 4 "Probando https://$Nombre"
if ($Simular) {
    Write-Host "  [simular] Invoke-WebRequest https://$Nombre/health"
} else {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    try {
        Invoke-WebRequest -Uri "https://$Nombre/health" -UseBasicParsing -TimeoutSec 10 | Out-Null
        Ok 'El servidor responde y el equipo confía en el certificado.'
    } catch {
        Write-Host "  FALLA  https://$Nombre no responde desde esta PC: $($_.Exception.Message)" -ForegroundColor Red
        Info "¿La IP $IP es la del servidor? ¿Llega desde esta red? Probá: Test-NetConnection $IP -Port 443"
        Info 'Si llega pero falla el certificado: ¿la hora de la PC está bien? Guía: "Certificado no confiable".'
        exit 1
    }
}
Write-Host ''
Write-Host "  Listo. Abrí el navegador en  https://$Nombre  y entrá con tu DNI."
