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

function Get-ProcesosBajo([string]$Raiz) {
  $prefijo = $Raiz.TrimEnd('\') + '\'
  Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $p = $null
    try { $p = $_.Path } catch { }
    $p -and $p.StartsWith($prefijo, [System.StringComparison]::OrdinalIgnoreCase)
  }
}

function Stop-ProcesosBajo([string]$Raiz) {
  foreach ($proc in @(Get-ProcesosBajo $Raiz)) {
    try {
      Write-Log ("Deteniendo {0} (PID {1})" -f $proc.Path, $proc.Id)
      Stop-Process -Id $proc.Id -Force -ErrorAction Stop
    } catch { Write-Log ("No se pudo detener PID {0}: {1}" -f $proc.Id, $_.Exception.Message) }
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

function Get-Sha256([string]$Path) {
  (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
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
