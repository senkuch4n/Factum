<#
  migrar-portable.ps1 - lo llama el instalador NSIS de Tatana (build/installer.nsh).
  SDD tatana-instalador-autoupdate §6.3 (D10, D-T5, D-T12). PowerShell 5.1, sin modulos.

  -Accion Detectar          Mira %LOCALAPPDATA%\Programs\Tatana y devuelve:
                              0  nada que migrar (vacio, inexistente o Tatana ya instalado)
                              10 portatil de la instalacion local (CLIENT_URL vacio o http)
                              20 portatil para la nube (CLIENT_URL=https://...)
                              30 contenido desconocido
  -Accion Migrar            Solo si Detectar da 20. Detiene el portatil, copia su
                            appsettings.Local.json a %APPDATA%\Tatana\, archiva su carpeta en
                            %LOCALAPPDATA%\Tatana\portable-anterior-<fecha>, saca su acceso
                            directo de Inicio y escribe %APPDATA%\Tatana\migracion-portable.json.
                            Codigos: 0 ok; 40 no se pudo mover la carpeta; 41 fallo la copia
                            de la config local; 42 no es un portatil para la nube.
  -Accion DetenerInstalado  Mata los procesos con ejecutable bajo <Carpeta>\resources\agent\.
                            Siempre da 0.

  REGLA DURA: este script NUNCA lee, mueve ni borra %LOCALAPPDATA%\Tatana\data ni
  C:\Factum\Evidencia. La evidencia del perito no se toca. Cada paso que falla deja todo
  como estaba (la unica escritura previa al movimiento de la carpeta es una copia de la
  config local, que es inofensiva).
#>
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Detectar', 'Migrar', 'DetenerInstalado')]
  [string]$Accion,
  [ValidateSet('0', '1')]
  [string]$Silencioso = '0',
  [string]$Carpeta = ''
)

$ErrorActionPreference = 'Stop'

# NSIS hereda el entorno de quien lo lanzo. Si es pwsh 7 (como en CI), PSModulePath trae las
# rutas de modulos de pwsh 7 y Windows PowerShell 5.1 no puede autocargar modulos de script
# (Get-FileHash, Expand-Archive, CimCmdlets...): "The term 'Get-FileHash' is not recognized".
# Para este proceso se fija PSModulePath a las rutas propias de 5.1. De todos modos este script no
# usa cmdlets de modulos con autocarga: solo los del nucleo y .NET (ver Get-Sha256 y Get-RutasProcesos).
if ($PSVersionTable.PSEdition -ne 'Core') {
  $env:PSModulePath = @(
    (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'WindowsPowerShell\Modules'),
    (Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules'),
    (Join-Path $PSHOME 'Modules')
  ) -join ';'
}

$PortableDir = Join-Path $env:LOCALAPPDATA 'Programs\Tatana'
$ConfigDir   = Join-Path $env:APPDATA 'Tatana'
$LogDir      = Join-Path $ConfigDir 'logs'
$Utf8SinBom  = New-Object System.Text.UTF8Encoding($false)

function Write-Log([string]$Mensaje) {
  try {
    if (-not (Test-Path -LiteralPath $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
    $linea = '{0} [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz'), $Accion, $Mensaje
    [System.IO.File]::AppendAllText((Join-Path $LogDir 'instalador.log'), $linea + "`r`n", $Utf8SinBom)
  } catch { }
}

# PID -> ruta completa del ejecutable de cada proceso visible.
# Fuente principal: WMI (Win32_Process.ExecutablePath) via System.Management de .NET, sin
# CimCmdlets. Funciona sin importar la arquitectura: Get-Process.Path lee MainModule, y desde un
# PowerShell de 32 bits (el que lanza NSIS si no se usa Sysnative) queda vacio para todo proceso
# de 64 bits, como Factum.Agent.exe. Respaldo: MainModule, para los PID sin ruta en WMI.
# Un proceso sin ruta conocida no se incluye: nunca se detiene algo cuya carpeta no se conoce.
function Get-RutasProcesos {
  $rutas = @{}
  try {
    Add-Type -AssemblyName System.Management -ErrorAction Stop
    $buscador = New-Object System.Management.ManagementObjectSearcher('SELECT ProcessId, ExecutablePath FROM Win32_Process')
    try {
      $resultados = $buscador.Get()
      try {
        foreach ($o in $resultados) {
          try {
            $ruta = [string]$o['ExecutablePath']
            if ($ruta) { $rutas[[int]$o['ProcessId']] = $ruta }
          } finally { $o.Dispose() }
        }
      } finally { $resultados.Dispose() }
    } finally { $buscador.Dispose() }
  } catch { Write-Log ("WMI no disponible para leer rutas de procesos: {0}" -f $_.Exception.Message) }
  foreach ($proc in @(Get-Process -ErrorAction SilentlyContinue)) {
    if (-not $rutas.ContainsKey([int]$proc.Id)) {
      try { if ($proc.MainModule -and $proc.MainModule.FileName) { $rutas[[int]$proc.Id] = $proc.MainModule.FileName } } catch { }
    }
  }
  return $rutas
}

# Procesos cuyo ejecutable esta bajo $Raiz, como objetos @{ Id; Path }.
function Get-ProcesosBajo([string]$Raiz) {
  $prefijo = $Raiz.TrimEnd('\') + '\'
  $rutas = Get-RutasProcesos
  foreach ($procId in @($rutas.Keys)) {
    $ruta = $rutas[$procId]
    if ($procId -ne $PID -and $ruta.StartsWith($prefijo, [System.StringComparison]::OrdinalIgnoreCase)) {
      New-Object PSObject -Property @{ Id = $procId; Path = $ruta }
    }
  }
}

function Stop-ProcesosBajo([string]$Raiz) {
  $procesos = @(Get-ProcesosBajo $Raiz)
  if ($procesos.Count -eq 0) { Write-Log "No hay procesos corriendo bajo $Raiz." }
  foreach ($proc in $procesos) {
    try {
      Write-Log ("Deteniendo {0} (PID {1})" -f $proc.Path, $proc.Id)
      Stop-Process -Id $proc.Id -Force -ErrorAction Stop
    } catch { Write-Log ("No se pudo detener PID {0}: {1}" -f $proc.Id, $_.Exception.Message) }
  }
  # Espera (hasta 10 s) a que terminen, para que la carpeta quede libre.
  $limite = (Get-Date).AddSeconds(10)
  foreach ($proc in $procesos) {
    try {
      $p = [System.Diagnostics.Process]::GetProcessById([int]$proc.Id)
      $resta = [int][Math]::Max(0, ($limite - (Get-Date)).TotalMilliseconds)
      if (-not $p.WaitForExit($resta)) { Write-Log ("PID {0} sigue corriendo despues de detenerlo." -f $proc.Id) }
      $p.Dispose()
    } catch { }   # ya termino
  }
}

function Get-ClientUrl([string]$Dir) {
  $ini = Join-Path $Dir 'tatana-portable.ini'
  if (-not (Test-Path -LiteralPath $ini)) { return '' }
  foreach ($l in (Get-Content -LiteralPath $ini -ErrorAction SilentlyContinue)) {
    if ($l -match '^\s*CLIENT_URL\s*=\s*(.*)$') { return $Matches[1].Trim() }
  }
  return ''
}

function Invoke-Detectar {
  if (-not (Test-Path -LiteralPath $PortableDir)) { return 0 }
  $items = @(Get-ChildItem -LiteralPath $PortableDir -Force -ErrorAction SilentlyContinue)
  if ($items.Count -eq 0) { return 0 }
  if (Test-Path -LiteralPath (Join-Path $PortableDir 'Uninstall Tatana.exe')) { return 0 }
  if ((Test-Path -LiteralPath (Join-Path $PortableDir 'launch-tatana.bat')) -and
      (Test-Path -LiteralPath (Join-Path $PortableDir 'Factum.Agent.exe'))) {
    $url = Get-ClientUrl $PortableDir
    if ($url.StartsWith('https://', [System.StringComparison]::OrdinalIgnoreCase)) { return 20 }
    return 10
  }
  return 30
}

# SHA-256 con .NET: Get-FileHash vive en un modulo de script que puede no cargarse (ver arriba).
function Get-Sha256([string]$Path) {
  $sha = [System.Security.Cryptography.SHA256]::Create()
  try {
    $fs = [System.IO.File]::OpenRead($Path)
    try { return [System.BitConverter]::ToString($sha.ComputeHash($fs)).Replace('-', '') }
    finally { $fs.Dispose() }
  } finally { $sha.Dispose() }
}

function Invoke-Migrar {
  $det = Invoke-Detectar
  if ($det -ne 20) { Write-Log "Detectar dio ${det}; no hay portatil para la nube que migrar."; return 42 }

  $portableVersion = $null
  $versionTxt = Join-Path $PortableDir 'version.txt'
  if (Test-Path -LiteralPath $versionTxt) {
    $v = (Get-Content -LiteralPath $versionTxt -TotalCount 1 -ErrorAction SilentlyContinue)
    if ($v) { $portableVersion = ([string]$v).Trim() }
  }

  # 1. Detener el portatil.
  # Solo procesos con ejecutable bajo la carpeta del portatil (incluido su Factum.Agent):
  # un Factum.Agent de otra carpeta (otra instalacion) no se corta.
  Stop-ProcesosBajo $PortableDir
  Start-Sleep -Seconds 2

  # 2. Config local -> %APPDATA%\Tatana\ (copia verificada por hash).
  $configMigrada = $false
  $conflicto = $false
  $destino = $null
  $creado = $false
  $origenConfig = Join-Path $PortableDir 'appsettings.Local.json'
  if (Test-Path -LiteralPath $origenConfig) {
    try {
      if (-not (Test-Path -LiteralPath $ConfigDir)) { New-Item -ItemType Directory -Path $ConfigDir -Force | Out-Null }
      $destino = Join-Path $ConfigDir 'appsettings.Local.json'
      if (Test-Path -LiteralPath $destino) {
        $destino = Join-Path $ConfigDir 'appsettings.Local.portable.json'
        $conflicto = $true
      }
      $creado = -not (Test-Path -LiteralPath $destino)
      Copy-Item -LiteralPath $origenConfig -Destination $destino -Force
      if ((Get-Sha256 $origenConfig) -ne (Get-Sha256 $destino)) { throw 'El hash de la copia no coincide.' }
      $configMigrada = $true
      Write-Log "Config local copiada a $destino (conflicto=$conflicto)."
    } catch {
      Write-Log ("Fallo la copia de la config local: {0}" -f $_.Exception.Message)
      # Deja todo como estaba: borra solo la copia que hizo este script.
      if ($creado -and $destino -and (Test-Path -LiteralPath $destino)) {
        Remove-Item -LiteralPath $destino -Force -ErrorAction SilentlyContinue
      }
      return 41
    }
  }

  # 3. Archivar la carpeta del portatil (hasta 3 intentos cada 2 s).
  $base = Join-Path $env:LOCALAPPDATA 'Tatana'
  if (-not (Test-Path -LiteralPath $base)) { New-Item -ItemType Directory -Path $base -Force | Out-Null }
  $archivo = Join-Path $base ('portable-anterior-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
  $movido = $false
  for ($i = 1; $i -le 3 -and -not $movido; $i++) {
    try {
      Move-Item -LiteralPath $PortableDir -Destination $archivo -ErrorAction Stop
      $movido = $true
    } catch {
      Write-Log ("Intento {0} de mover la carpeta fallo: {1}" -f $i, $_.Exception.Message)
      if ($i -lt 3) { Start-Sleep -Seconds 2 }
    }
  }
  if (-not $movido) { return 40 }
  Write-Log "Carpeta del portatil archivada en $archivo."

  # 4. Acceso directo de inicio de sesion del portatil -> a la carpeta archivada.
  $lnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\Tatana.lnk'
  if (Test-Path -LiteralPath $lnk) {
    try {
      $target = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk).TargetPath
      $prefijo = $PortableDir.TrimEnd('\') + '\'
      if ($target -and $target.StartsWith($prefijo, [System.StringComparison]::OrdinalIgnoreCase)) {
        Move-Item -LiteralPath $lnk -Destination (Join-Path $archivo 'Tatana.lnk') -Force
        Write-Log 'Acceso directo de inicio del portatil movido a la carpeta archivada.'
      }
    } catch { Write-Log ("No se pudo revisar Tatana.lnk: {0}" -f $_.Exception.Message) }
  }

  # 5. Registro para el aviso de la ventana de Tatana (SDD §13.E).
  if (-not (Test-Path -LiteralPath $ConfigDir)) { New-Item -ItemType Directory -Path $ConfigDir -Force | Out-Null }
  $registro = [ordered]@{
    schema               = 1
    fecha                = (Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz')
    portable_version     = $portableVersion
    carpeta_anterior     = $archivo
    config_local_migrada = $configMigrada
    config_local_destino = $destino
    conflicto            = $conflicto
    mostrado             = $false
  }
  [System.IO.File]::WriteAllText((Join-Path $ConfigDir 'migracion-portable.json'),
    ($registro | ConvertTo-Json -Depth 3), $Utf8SinBom)
  Write-Log 'Migracion terminada.'
  return 0
}

Write-Log ("Arranca: PowerShell {0}, proceso de {1} bits." -f $PSVersionTable.PSVersion, $(if ([Environment]::Is64BitProcess) { 64 } else { 32 }))
$codigo = 0
try {
  switch ($Accion) {
    'Detectar' { $codigo = Invoke-Detectar }
    'Migrar'   { $codigo = Invoke-Migrar }
    'DetenerInstalado' {
      if ($Carpeta) { Stop-ProcesosBajo (Join-Path $Carpeta 'resources\agent') }
      $codigo = 0
    }
  }
} catch {
  Write-Log ("Error inesperado: {0}" -f $_.Exception.Message)
  if ($Accion -eq 'DetenerInstalado') { $codigo = 0 }
  elseif ($Accion -eq 'Detectar') { $codigo = 30 }
  else { $codigo = 40 }
}
Write-Log "Sale con $codigo (silencioso=$Silencioso)."
exit $codigo
