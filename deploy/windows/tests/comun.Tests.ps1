# Pester 5 — funciones puras de scripts/_comun.ps1 (DT19 de la SDD instalacion-local-docker).
# Corre en Windows PowerShell 5.1 y en pwsh (por ejemplo en un contenedor en la Mac):
#   Invoke-Pester ./deploy/windows/tests -Output Detailed

BeforeAll {
    . (Join-Path (Split-Path -Parent $PSScriptRoot) (Join-Path 'scripts' '_comun.ps1'))
    Set-StrictMode -Version 2.0

    function New-Dir {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Helper de test: carpeta temporal propia del test.')]
    param()
        $d = Join-Path ([System.IO.Path]::GetTempPath()) ('factum-test-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $d | Out-Null
        return $d
    }

    function Write-Archivo {
        param([string]$Ruta, [string]$Texto)
        $padre = Split-Path -Parent $Ruta
        if (-not (Test-Path -LiteralPath $padre)) { New-Item -ItemType Directory -Path $padre -Force | Out-Null }
        [System.IO.File]::WriteAllText($Ruta, $Texto, (New-Object System.Text.UTF8Encoding($false)))
    }
}

Describe 'Read-EnvFile' {
    BeforeEach { $script:dir = New-Dir }
    AfterEach { Remove-Item -LiteralPath $script:dir -Recurse -Force }

    It 'ignora comentarios y líneas vacías, divide en el primer =' {
        $p = Join-Path $script:dir '.env'
        Write-Archivo $p "# comentario`n`nA=1`nB = dos = tres`nC=`n  # otro`nD=""con comillas""`n"
        $e = Read-EnvFile $p
        $e.Count | Should -Be 4
        $e['A'] | Should -Be '1'
        $e['B'] | Should -Be 'dos = tres'
        $e['C'] | Should -Be ''
        $e['D'] | Should -Be 'con comillas'
    }

    It 'tolera BOM y CRLF' {
        $p = Join-Path $script:dir '.env'
        [System.IO.File]::WriteAllText($p, "X=1`r`nY=2`r`n", (New-Object System.Text.UTF8Encoding($true)))
        $e = Read-EnvFile $p
        $e['X'] | Should -Be '1'
        $e['Y'] | Should -Be '2'
    }
}

Describe 'Write-EnvFile y Set-EnvValue' {
    BeforeEach { $script:dir = New-Dir }
    AfterEach { Remove-Item -LiteralPath $script:dir -Recurse -Force }

    It 'conserva comentarios y orden del template, escribe UTF-8 sin BOM y LF' {
        $tpl = Join-Path $script:dir '.env.example'
        Write-Archivo $tpl "# encabezado`nCOMPOSE_PROJECT_NAME=factum`nFACTUM_VERSION=0.0.0`n# secreto`nFACTUM_JWT_SECRET=`n"
        $dest = Join-Path $script:dir '.env'
        Write-EnvFile -Path $dest -Valores @{ FACTUM_VERSION = '1.2.3'; FACTUM_JWT_SECRET = 'abc'; EXTRA = 'z' } -Template $tpl
        $bytes = [System.IO.File]::ReadAllBytes($dest)
        $bytes[0] | Should -Not -Be 0xEF
        $texto = [System.IO.File]::ReadAllText($dest)
        $texto | Should -Not -Match "`r"
        $texto | Should -Be "# encabezado`nCOMPOSE_PROJECT_NAME=factum`nFACTUM_VERSION=1.2.3`n# secreto`nFACTUM_JWT_SECRET=abc`nEXTRA=z`n"
    }

    It 'Set-EnvValue cambia una clave sin tocar el resto y agrega las nuevas al final' {
        $p = Join-Path $script:dir '.env'
        Write-Archivo $p "# c`nA=1`nB=2`n"
        Set-EnvValue -Path $p -Key 'B' -Value '20'
        Set-EnvValue -Path $p -Key 'C' -Value ''
        [System.IO.File]::ReadAllText($p) | Should -Be "# c`nA=1`nB=20`nC=`n"
        (Read-EnvFile $p)['B'] | Should -Be '20'
    }
}

Describe 'New-JwtSecret' {
    It 'devuelve 128 caracteres hex en minúsculas y distintos en cada llamada' {
        $a = New-JwtSecret
        $b = New-JwtSecret
        $a | Should -Match '^[0-9a-f]{128}$'
        $b | Should -Match '^[0-9a-f]{128}$'
        $a | Should -Not -Be $b
    }
}

Describe 'New-HashManifest y Test-HashManifest' {
    BeforeEach {
        $script:dir = New-Dir
        Write-Archivo (Join-Path $script:dir 'backup-info.json') '{"formato":1}'
        Write-Archivo (Join-Path (Join-Path $script:dir 'evidencia') (Join-Path 'caso1' 'evidencia.zip')) 'contenido zip'
        Write-Archivo (Join-Path (Join-Path $script:dir 'evidencia') (Join-Path 'caso1' 'informe.docx')) 'contenido docx'
        Write-Archivo (Join-Path (Join-Path $script:dir 'config') '.env') "A=1`n"
        $script:man = Join-Path $script:dir 'manifiesto-sha256.txt'
        $script:n = New-HashManifest -Raiz $script:dir -Salida $script:man
    }
    AfterEach { Remove-Item -LiteralPath $script:dir -Recurse -Force }

    It 'genera el formato hash + dos espacios + ruta relativa con /, ordenado, sin el propio manifiesto' {
        $script:n | Should -Be 4
        $texto = [System.IO.File]::ReadAllText($script:man)
        $texto | Should -Not -Match "`r"
        $lineas = $texto.TrimEnd("`n") -split "`n"
        $lineas.Count | Should -Be 4
        foreach ($l in $lineas) { $l | Should -Match '^[0-9a-f]{64}  [^\\]+$' }
        $rutas = $lineas | ForEach-Object { $_.Substring(66) }
        $rutas | Should -Be @('backup-info.json', 'config/.env', 'evidencia/caso1/evidencia.zip', 'evidencia/caso1/informe.docx')
        $texto | Should -Not -Match 'manifiesto-sha256'
        $esperado = (Get-FileHash -LiteralPath (Join-Path (Join-Path $script:dir 'config') '.env') -Algorithm SHA256).Hash.ToLowerInvariant()
        $lineas[1] | Should -Be ($esperado + '  config/.env')
    }

    It 'un backup intacto no tiene diferencias' {
        $difs = Test-HashManifest -Raiz $script:dir -Manifiesto $script:man
        $difs.Count | Should -Be 0
    }

    It 'backup corrupto: detecta un archivo alterado y uno faltante, sin modificar nada' {
        $zip = Join-Path (Join-Path $script:dir 'evidencia') (Join-Path 'caso1' 'evidencia.zip')
        [System.IO.File]::WriteAllText($zip, 'contenido zip ALTERADO')
        Remove-Item -LiteralPath (Join-Path (Join-Path $script:dir 'evidencia') (Join-Path 'caso1' 'informe.docx'))
        $antes = [System.IO.File]::ReadAllText($script:man)

        $difs = Test-HashManifest -Raiz $script:dir -Manifiesto $script:man
        $graves = Get-DiferenciaGrave $difs

        $graves.Count | Should -Be 2
        ($graves | Where-Object { $_.Ruta -eq 'evidencia/caso1/evidencia.zip' }).Motivo | Should -Be 'hash distinto'
        ($graves | Where-Object { $_.Ruta -eq 'evidencia/caso1/informe.docx' }).Motivo | Should -Be 'falta'
        [System.IO.File]::ReadAllText($script:man) | Should -Be $antes
        Test-Path -LiteralPath $zip | Should -BeTrue
    }

    It 'un archivo agregado es solo informativo (sobrante)' {
        Write-Archivo (Join-Path $script:dir 'nota.txt') 'agregado'
        $difs = Test-HashManifest -Raiz $script:dir -Manifiesto $script:man
        $difs.Count | Should -Be 1
        $difs[0].Motivo | Should -Be 'sobrante'
        (Get-DiferenciaGrave $difs).Count | Should -Be 0
    }

    It 'sin manifiesto informa el faltante' {
        Remove-Item -LiteralPath $script:man
        $difs = Test-HashManifest -Raiz $script:dir -Manifiesto $script:man
        $difs.Count | Should -Be 1
        $difs[0].Motivo | Should -Be 'falta el manifiesto'
    }

    It 'una línea mal formada invalida el manifiesto' {
        Add-Content -LiteralPath $script:man -Value 'esto no es una linea valida' -NoNewline
        $graves = Get-DiferenciaGrave (Test-HashManifest -Raiz $script:dir -Manifiesto $script:man)
        ($graves | Where-Object { $_.Motivo -eq 'linea invalida' }).Count | Should -Be 1
    }
}

Describe 'ConvertTo-VersionFactum' {
    It 'compara versiones semver ignorando el sufijo' {
        (ConvertTo-VersionFactum '1.0.10') -gt (ConvertTo-VersionFactum '1.0.9') | Should -BeTrue
        (ConvertTo-VersionFactum '0.0.0-verif') | Should -Be ([version]'0.0.0')
        ConvertTo-VersionFactum 'basura' | Should -BeNullOrEmpty
    }
}

# ── Perfil de memoria (SDD instalacion-poca-ram, B13-B16) ──────────────────────

Describe 'Get-PerfilMemoria' {
    It '<Gb> GB -> <Esperado>' -TestCases @(
        @{ Gb = 0; Esperado = 'bloquea' }
        @{ Gb = 3.4; Esperado = 'bloquea' }
        @{ Gb = 3.5; Esperado = 'poca' }
        @{ Gb = 3.8; Esperado = 'poca' }
        @{ Gb = 7.4; Esperado = 'poca' }
        @{ Gb = 7.5; Esperado = 'normal' }
        @{ Gb = 7.6; Esperado = 'normal' }
        @{ Gb = 16; Esperado = 'normal' }
    ) {
        param($Gb, $Esperado)
        Get-PerfilMemoria -Gb $Gb | Should -Be $Esperado
    }
}

Describe 'Get-PerfilMemoriaConfigurado' {
    BeforeEach { $script:dir = New-Dir; $script:env = Join-Path $script:dir '.env' }
    AfterEach { Remove-Item -LiteralPath $script:dir -Recurse -Force }

    It 'sin archivo -> normal' {
        Get-PerfilMemoriaConfigurado (Join-Path $script:dir 'no-existe.env') | Should -Be 'normal'
    }

    It 'sin la clave -> normal' {
        Write-Archivo $script:env "FACTUM_VERSION=1.0.0`n"
        Get-PerfilMemoriaConfigurado $script:env | Should -Be 'normal'
    }

    It '<Valor> -> <Esperado>' -TestCases @(
        @{ Valor = 'normal'; Esperado = 'normal' }
        @{ Valor = 'poca'; Esperado = 'poca' }
        @{ Valor = ' POCA '; Esperado = 'poca' }
        @{ Valor = 'xyz'; Esperado = 'normal' }
        @{ Valor = ''; Esperado = 'normal' }
    ) {
        param($Valor, $Esperado)
        Write-Archivo $script:env ("FACTUM_VERSION=1.0.0`nFACTUM_PERFIL_MEMORIA=" + $Valor + "`n")
        Get-PerfilMemoriaConfigurado $script:env | Should -Be $Esperado
    }
}

Describe 'Get-FactumComposeArgumento' {
    BeforeEach {
        $script:dir = New-Dir
        $script:env = Join-Path (Join-Path $script:dir 'config') '.env'
        $script:FactumAvisoPocaRamMostrado = $false
        Mock Write-Aviso { }
    }
    AfterEach { Remove-Item -LiteralPath $script:dir -Recurse -Force }

    It '(a) perfil normal: el arreglo es idéntico al de antes de la HU' {
        Write-Archivo $script:env "COMPOSE_PROJECT_NAME=factum-test`nFACTUM_PERFIL_MEMORIA=normal`n"
        Write-Archivo (Join-Path $script:dir 'docker-compose.poca-ram.yml') "services: {}`n"
        Initialize-FactumContext $script:dir
        $h = Get-RutaAbsoluta $script:dir
        $esperado = @('compose', '--project-directory', $h, '-f', (Join-Path $h 'docker-compose.yml'), '--env-file', (Join-Path (Join-Path $h 'config') '.env'), '-p', 'factum-test')
        $obtenido = @(Get-FactumComposeArgumento)
        $obtenido.Count | Should -Be $esperado.Count
        for ($i = 0; $i -lt $esperado.Count; $i++) { $obtenido[$i] | Should -Be $esperado[$i] }
    }

    It '(b) perfil poca con el overlay: segundo -f justo después del primero' {
        Write-Archivo $script:env "COMPOSE_PROJECT_NAME=factum-test`nFACTUM_PERFIL_MEMORIA=poca`n"
        Write-Archivo (Join-Path $script:dir 'docker-compose.poca-ram.yml') "services: {}`n"
        Initialize-FactumContext $script:dir
        $h = Get-RutaAbsoluta $script:dir
        $esperado = @('compose', '--project-directory', $h, '-f', (Join-Path $h 'docker-compose.yml'), '-f', (Join-Path $h 'docker-compose.poca-ram.yml'), '--env-file', (Join-Path (Join-Path $h 'config') '.env'), '-p', 'factum-test')
        $obtenido = @(Get-FactumComposeArgumento)
        $obtenido.Count | Should -Be $esperado.Count
        for ($i = 0; $i -lt $esperado.Count; $i++) { $obtenido[$i] | Should -Be $esperado[$i] }
        Should -Invoke Write-Aviso -Times 0 -Exactly
    }

    It '(c) perfil poca sin el overlay: arreglo base y un solo aviso por proceso' {
        Write-Archivo $script:env "COMPOSE_PROJECT_NAME=factum-test`nFACTUM_PERFIL_MEMORIA=poca`n"
        Initialize-FactumContext $script:dir
        $h = Get-RutaAbsoluta $script:dir
        $esperado = @('compose', '--project-directory', $h, '-f', (Join-Path $h 'docker-compose.yml'), '--env-file', (Join-Path (Join-Path $h 'config') '.env'), '-p', 'factum-test')
        $obtenido = @(Get-FactumComposeArgumento)
        $obtenido.Count | Should -Be $esperado.Count
        for ($i = 0; $i -lt $esperado.Count; $i++) { $obtenido[$i] | Should -Be $esperado[$i] }
        $null = Get-FactumComposeArgumento
        Should -Invoke Write-Aviso -Times 1 -Exactly
    }

    It '(d) sin la clave en el .env: igual que el perfil normal' {
        Write-Archivo $script:env "COMPOSE_PROJECT_NAME=factum-test`n"
        Write-Archivo (Join-Path $script:dir 'docker-compose.poca-ram.yml') "services: {}`n"
        Initialize-FactumContext $script:dir
        $h = Get-RutaAbsoluta $script:dir
        $esperado = @('compose', '--project-directory', $h, '-f', (Join-Path $h 'docker-compose.yml'), '--env-file', (Join-Path (Join-Path $h 'config') '.env'), '-p', 'factum-test')
        $obtenido = @(Get-FactumComposeArgumento)
        $obtenido.Count | Should -Be $esperado.Count
        for ($i = 0; $i -lt $esperado.Count; $i++) { $obtenido[$i] | Should -Be $esperado[$i] }
    }

    It 'lee el .env en cada llamada (sin caché)' {
        Write-Archivo $script:env "COMPOSE_PROJECT_NAME=factum-test`nFACTUM_PERFIL_MEMORIA=normal`n"
        Write-Archivo (Join-Path $script:dir 'docker-compose.poca-ram.yml') "services: {}`n"
        Initialize-FactumContext $script:dir
        @(Get-FactumComposeArgumento).Count | Should -Be 9
        Write-Archivo $script:env "COMPOSE_PROJECT_NAME=factum-test`nFACTUM_PERFIL_MEMORIA=poca`n"
        @(Get-FactumComposeArgumento).Count | Should -Be 11
    }
}

Describe 'Guardas del repo (perfil de memoria)' {
    BeforeAll { $script:raizW = Split-Path -Parent $PSScriptRoot }

    It '.env.example trae FACTUM_PERFIL_MEMORIA=normal' {
        (Read-EnvFile (Join-Path $script:raizW '.env.example'))['FACTUM_PERFIL_MEMORIA'] | Should -Be 'normal'
    }

    It 'el compose de poca RAM no tiene image: (Get-MongoImagen devuelve $null)' {
        Get-MongoImagen (Join-Path $script:raizW 'docker-compose.poca-ram.yml') | Should -BeNullOrEmpty
    }

    It 'el compose base sigue con mongo:7.0.43' {
        Get-MongoImagen (Join-Path $script:raizW 'docker-compose.yml') | Should -Be 'mongo:7.0.43'
    }
}
