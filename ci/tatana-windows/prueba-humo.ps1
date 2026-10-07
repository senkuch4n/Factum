# Prueba de humo del Tatana portátil de Windows sin iPhone (SDD ios-herramientas-windows §8.2).
# La corre .github/workflows/tatana-windows.yml en windows-latest. No toca Mongo ni datos de nadie:
# todo queda en $env:RUNNER_TEMP\tatana-humo (o en %TEMP% si se corre a mano).
#
#   pwsh -File ci/tatana-windows/prueba-humo.ps1 -Zip <Tatana-Portable-...-Windows.zip>
param(
    [Parameter(Mandatory = $true)][string]$Zip,
    [int]$Port = 8765
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Ok([string]$texto) { Write-Host "OK  $texto" }
function Falla([string]$texto) { throw "FALLA  $texto" }

$base = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$raiz = Join-Path $base 'tatana-humo'
$app = Join-Path $raiz 'app'
$datos = Join-Path $raiz 'data'
$autoprueba = Join-Path $raiz 'autoprueba'
New-Item -ItemType Directory -Force -Path $raiz, $datos, $autoprueba | Out-Null

# 1. Descomprimir.
if (-not (Test-Path $Zip)) { Falla "no existe el zip $Zip" }
if (Test-Path $app) { Remove-Item -Recurse -Force $app }
Expand-Archive -Path $Zip -DestinationPath $app
$exe = Join-Path $app 'Factum.Agent.exe'
$python = Join-Path $app 'tools\python-embed\python.exe'
foreach ($f in @($exe, $python, (Join-Path $app 'tools\ffmpeg\ffmpeg.exe'))) {
    if (-not (Test-Path $f)) { Falla "falta $f en el zip" }
}
Ok "zip descomprimido en $app"

# 2. pymobiledevice3 del Python embebido.
$env:PYTHONUTF8 = '1'
$env:NO_COLOR = '1'
$salidaVersion = & $python -m pymobiledevice3 version 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { Falla "python -m pymobiledevice3 version salió con $LASTEXITCODE`n$salidaVersion" }
$version = ($salidaVersion -split "`r?`n" | Where-Object { $_.Trim() -ne '' } | Select-Object -Last 1).Trim()
if ($version -ne '10.7.4') { Falla "pymobiledevice3 $version; se esperaba 10.7.4`n$salidaVersion" }
Ok "pymobiledevice3 $version"

# 3. Autoprueba de iPhone (§6.9): Python, imports de Windows, grabador DVT sintético, captura sin iPhone.
$logAutoprueba = Join-Path $raiz 'autoprueba.log'
& $exe --autoprueba-ios $autoprueba *>&1 | Tee-Object -FilePath $logAutoprueba | Write-Host
if ($LASTEXITCODE -ne 0) { Falla "la autoprueba de iPhone salió con $LASTEXITCODE (ver $logAutoprueba)" }
Ok 'autoprueba de iPhone'

# 4. Tatana real (no mock) en otro proceso.
$logOut = Join-Path $raiz 'tatana.out.log'
$logErr = Join-Path $raiz 'tatana.err.log'
$proc = Start-Process -FilePath $exe -ArgumentList @('--port', "$Port", '--data', "`"$datos`"") `
    -WorkingDirectory $app -PassThru -NoNewWindow `
    -RedirectStandardOutput $logOut -RedirectStandardError $logErr
$url = "http://localhost:$Port"
try {
    # 5. /health.
    $health = $null
    $limite = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $limite -and $null -eq $health) {
        if ($proc.HasExited) { Falla "Tatana terminó al arrancar (exit $($proc.ExitCode)); ver $logOut" }
        try { $health = Invoke-RestMethod -Uri "$url/health" -TimeoutSec 5 } catch { Start-Sleep -Seconds 1 }
    }
    if ($null -eq $health) { Falla "/health no respondió en 60 s" }
    Ok '/health responde'

    $limite = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $limite -and
           -not ($health.tools.python.PSObject.Properties.Name -contains 'pymobiledevice3_version')) {
        Start-Sleep -Seconds 1
        $health = Invoke-RestMethod -Uri "$url/health" -TimeoutSec 5
    }

    $health | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $raiz 'health.json') -Encoding utf8
    if ($health.mock -ne $false) { Falla "/health.mock = $($health.mock)" }
    foreach ($h in @('python', 'ffmpeg')) {
        $t = $health.tools.$h
        if (-not $t.found -or $t.source -ne 'portable') { Falla "tools.$h = $($t | ConvertTo-Json -Compress)" }
    }
    if (-not ($health.tools.python.PSObject.Properties.Name -contains 'pymobiledevice3_version') -or
        $health.tools.python.pymobiledevice3_version -ne '10.7.4') {
        Falla "tools.python.pymobiledevice3_version = $($health.tools.python | ConvertTo-Json -Compress)"
    }
    if ($health.tools.uxplay.found -ne $false) { Falla "tools.uxplay.found = $($health.tools.uxplay.found)" }
    if ($health.ios.airplay_available -ne $false) { Falla "ios.airplay_available = $($health.ios.airplay_available)" }
    if ($health.ios.airplay_unavailable_reason -ne 'not_supported_on_windows') {
        Falla "ios.airplay_unavailable_reason = $($health.ios.airplay_unavailable_reason)"
    }
    # El runner no tiene Apple Mobile Device Service: es la prueba de D4.
    if ($health.ios.apple_service -ne 'missing') { Falla "ios.apple_service = $($health.ios.apple_service)" }
    if ($health.ios_available -ne $true) { Falla "ios_available = $($health.ios_available)" }
    if (-not ($health.capabilities -contains 'ios_developer_mode_v1')) {
        Falla "capabilities = $($health.capabilities -join ', ')"
    }
    Ok '/health: python y ffmpeg portátiles, pymobiledevice3 10.7.4, sin AirPlay, servicio de Apple missing'

    # 6. /devices rápido y sin iPhone (sin servicio de Apple no se lanza Python).
    $reloj = [System.Diagnostics.Stopwatch]::StartNew()
    $devices = Invoke-RestMethod -Uri "$url/devices" -TimeoutSec 10
    $reloj.Stop()
    if ($reloj.Elapsed.TotalSeconds -ge 5) { Falla "/devices tardó $($reloj.Elapsed.TotalSeconds) s" }
    $ios = @($devices.devices | Where-Object { $_.platform -eq 'ios' })
    if ($ios.Count -ne 0) { Falla "/devices trae iPhone en un runner sin iPhone" }
    Ok "/devices en $([int]$reloj.Elapsed.TotalMilliseconds) ms"

    # 7. Captura de iPhone: error con código y sin "brew".
    $r = Invoke-WebRequest -Method Post -Uri "$url/devices/0000-HUMO/screenshot?platform=ios" `
        -SkipHttpErrorCheck -TimeoutSec 60
    $cuerpo = $r.Content | ConvertFrom-Json
    if ($r.StatusCode -ne 500 -or $cuerpo.code -ne 'ios_apple_service_missing') {
        Falla "screenshot: HTTP $($r.StatusCode) $($r.Content)"
    }
    if ($cuerpo.error -match 'brew') { Falla "screenshot: el mensaje menciona brew: $($cuerpo.error)" }
    Ok "screenshot: 500 ios_apple_service_missing ($($cuerpo.error))"

    # 8. Grabación AirPlay: no disponible en Windows.
    $r = Invoke-WebRequest -Method Post -Uri "$url/devices/0000-HUMO/record/start" -SkipHttpErrorCheck `
        -ContentType 'application/json' -Body '{"platform":"ios","ios_mode":"airplay","android_version":0}' `
        -TimeoutSec 30
    $cuerpo = $r.Content | ConvertFrom-Json
    if ($r.StatusCode -ne 500 -or $cuerpo.code -ne 'airplay_unavailable') {
        Falla "record/start airplay: HTTP $($r.StatusCode) $($r.Content)"
    }
    if ($cuerpo.error -match 'brew') { Falla "record/start airplay: el mensaje menciona brew: $($cuerpo.error)" }
    Ok 'record/start airplay: 500 airplay_unavailable'
}
finally {
    # 9. Detener Tatana (solo el proceso que lanzó esta prueba).
    if ($null -ne $proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}

Write-Host 'RESULTADO: OK'
