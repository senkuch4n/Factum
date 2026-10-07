# SDD: Instalar Factum en una PC con poca RAM (4 GB): aviso propio y memoria acotada

**Slug:** `instalacion-poca-ram`
**HU:** `docs/hu-instalacion-poca-ram.md`, validada el 2026-10-02 con D1–D8 en la recomendada.
D1: piso duro < 3.5 GB. D2: S/N propia. D3: compose adicional. D4: topes B. D5: no se escribe
`.wslconfig`. D6: automático + `-PerfilMemoria`. D7: sección "Memoria" en el diagnóstico.
D8: verificación en 3 niveles. Caso de referencia: **40 capturas + videos**. La PC del estudio
tiene **SSD**.
**Depende de:** `instalacion-local-docker` (aprobada). Sus reglas R1-R9 (§6.5) y su regla dura
de datos (§9) siguen vigentes y se repiten en §9.
**Rama:** `feat/instalacion-poca-ram` (ya existe; sale de `develop` después del merge de la HU
anterior). Va contra `develop`.
**Implementador:** solo `implementer-backend`, que hace la infra (`deploy/windows/`) y la guía.
**No se lanza `implementer-frontend`.**

---

## 1. Resumen funcional

El instalador deja de bloquear las PCs de 4 GB. Con menos de 3.5 GB reportados sigue siendo una
falla. Entre 3.5 y menos de 7.5 GB muestra un aviso propio de "POCA MEMORIA", separado de los
demás avisos, y pide una confirmación S/N propia. Si se acepta, la instalación queda con
`FACTUM_PERFIL_MEMORIA=poca` en `config\.env`. Con ese valor, `Invoke-FactumCompose` suma un
segundo archivo de compose, `docker-compose.poca-ram.yml`, que acota la memoria de los
contenedores:

- Mongo: cache de WiredTiger en 0.25 GB y tope de 512 MB.
- Backend: tope de 768 MB y Workstation GC.
- Frontend: tope de 384 MB y heap de Node de 256 MB.

Con el perfil `normal` (el valor por defecto, y el de toda PC de 7.5 GB o más) los argumentos de
compose son **exactamente** los de hoy, así que la configuración efectiva no cambia. Además:

- `actualizar.ps1` conserva el perfil (o agrega `normal` a las instalaciones previas).
- `diagnostico.ps1` suma una sección "Memoria".
- La guía explica qué esperar en 4 GB, cómo cambiar de perfil y cómo crear un `.wslconfig` a
  mano si alguna vez hiciera falta. Los scripts **nunca** lo escriben.

El riesgo real, que es un OOM del backend al armar el DOCX con muchas capturas, se **mide** en la
Mac con el caso de referencia antes de dar la HU por hecha (§10, nivel 3).

## 2. Toca

| Lado | ¿Toca? | Detalle |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **no** | Ni código ni Dockerfile. Solo se **lee** para estimar el pico (§4) y se **usa** su API en la medición. |
| backend (Tatana) `server/src/Factum.Agent` | **no** | `server/src/Factum.Agent/appsettings.json` tiene un cambio local del usuario: **no se stagea**. |
| client `client/` | **no** | Solo se construye una imagen de verificación con tag `0.0.0-verif`. El código fuente no se toca. |
| agent-ui `agent-ui/` | **no** | Los `tsbuildinfo` modificados en el árbol **no son de esta HU**: no se stagean. |
| Infra `deploy/windows/` | **sí** | **Nuevo** `docker-compose.poca-ram.yml`; cambios en `.env.example`, `scripts/_comun.ps1`, `scripts/instalar.ps1`, `scripts/actualizar.ps1`, `scripts/diagnostico.ps1`, `scripts/backup.ps1`, `armar-paquete.sh` y `tests/comun.Tests.ps1`. El `docker-compose.yml` base **no se toca** (salvo un comentario de cabecera, ver B2). |
| Docs | **sí** | `docs/instalacion-windows.md`. |
| MongoDB | **no** | Sin cambios de esquema ni de documentos. |

## 3. Modelo de datos y endpoints

No hay cambios en Mongo ni en la API. La medición del nivel 3 usa los endpoints que ya existen
(§10.3) contra una Mongo **propia de la verificación** (proyecto `factum-verif`), nunca contra
la de desarrollo.

## 4. Estimación del pico de memoria al generar (base para D4 y para la medición)

Leído del código (`Services/Reports/ReportService.cs`, `EvidenceZip.cs`, `CaseService.GenerateAsync`):

| Paso de `ReportService.GenerateAsync` | Memoria | Por qué |
|---|---|---|
| 3. SHA-256 de cada archivo | constante | `File.OpenRead` por stream. |
| 4-6. ZIP (SharpZipLib) + hash + verificación | constante (~buffers de 80 KB) | Streaming. **Los videos no suben el heap**, pero sí llenan la *page cache* del cgroup: se leen tres veces (hash, zip, verify). Esa memoria es recuperable y por eso no se usa como métrica (ver DT12). |
| 7. DOCX (`WordprocessingDocument.Open(path, isEditable: true)`) | **crece con las capturas** | `System.IO.Packaging` en modo Update guarda cada parte nueva en memoria hasta el `Dispose`. `FeedData` copia por bloques, pero el destino es la parte en memoria. Las figuras del cuerpo **reutilizan** el `ImagePart` del anexo (`editor-imagenes-informe`, L697-753): una captura citada en el texto no se duplica. |

Para el caso de referencia (40 capturas de ~2 MB de promedio):

- Partes de imagen ≈ 80 MB. Con el crecimiento por duplicación de los `MemoryStream` (LOH),
  hasta ≈ 160 MB.
- Runtime de ASP.NET + driver de Mongo + plantilla ≈ 120-180 MB.
- **Pico estimado del backend: ≈ 300-450 MB**, o sea 40-60 % de 768 MB. Queda margen frente al
  umbral del 80 % (614 MiB).

Hay dos formas de quedarse sin memoria, y en ninguna se pierde evidencia:

1. **OOM gestionado.** Con `mem_limit: 768m`, .NET fija por defecto `GCHeapHardLimit` = 75 % del
   límite (576 MB). Si el heap no entra, lanza `OutOfMemoryException`. El `catch` de
   `ReportService` borra solo el ZIP/DOCX de ese intento, `CaseService` pasa el caso a `Error` y
   el backend **sigue vivo**.
2. **OOM del kernel** (memoria nativa). El contenedor muere y el caso queda en `Generating`.
   `restart: unless-stopped` lo levanta y `GenerateAsync` permite reintentar porque solo
   `Completed` bloquea.

En los dos casos los archivos sueltos de la evidencia siguen ahí, porque se borran recién en el
paso 9, con el ZIP verificado.

Mongo con `--wiredTigerCacheSizeGB 0.25` usa ≈ 150-300 MB. El `healthcheck` (`mongosh` cada
10 s, ~60-100 MB por exec), el `mongodump` del backup y el `mongosh` de `Measure-FactumCaso`
corren **dentro del mismo cgroup** y suman a esos 512 MB. Por eso la medición los incluye.

## 5. Diseño técnico

### 5.1 `deploy/windows/docker-compose.poca-ram.yml` (nuevo)

```yaml
# Factum — perfil de memoria "poca RAM" (FACTUM_PERFIL_MEMORIA=poca en config/.env).
# Se SUMA a docker-compose.yml solo en ese perfil: lo agrega Invoke-FactumCompose (scripts\_comun.ps1)
# con un segundo -f. En el perfil normal no se usa y la configuración es la de siempre.
# SDD instalacion-poca-ram §5.1. Reglas de este archivo:
# - Solo límites de memoria. Nada de image:, ports:, volumes: ni healthcheck: (el tag de mongo
#   se lee del compose base: Get-MongoImagen y armar-paquete.sh).
# - Los topes son máximos, no reservas. Sin memswap_limit: Docker permite swap igual al límite
#   dentro de la VM de WSL2 (preferible lento a que el contenedor muera).

services:
  mongo:
    command: ["mongod", "--wiredTigerCacheSizeGB", "0.25"]
    mem_limit: 512m
  backend:
    mem_limit: 768m
    environment:
      DOTNET_gcServer: "0"
  frontend:
    mem_limit: 384m
    environment:
      NODE_OPTIONS: "--max-old-space-size=256"
```

- `command` con `mongod` como primer elemento: el entrypoint de la imagen oficial lo acepta
  igual que el `CMD` por defecto.
- `environment` del backend se **fusiona** con el del base (mapping + mapping), y el frontend no
  tiene `environment` en el base. Se verifica en el nivel 2 (§10.2).
- Si la medición (§10.3) obliga a subir algún valor, se cambia **solo acá** (DT13).

### 5.2 `.env.example`

Al final se agrega:

```dotenv
# Perfil de memoria: normal (sin límites; PCs de 8 GB o más) o poca (PCs de 4 GB: suma
# docker-compose.poca-ram.yml). Lo fija instalar.ps1 según la RAM; para cambiarlo, ver la guía, sección 2.1.
FACTUM_PERFIL_MEMORIA=normal
```

El default `normal` es lo que `actualizar.ps1` agrega a las instalaciones previas (L117-129,
sin cambios en esa lógica).

### 5.3 `scripts/_comun.ps1`

**Funciones nuevas (puras, con Pester):**

```powershell
function Get-PerfilMemoria {
    # Decisión de Q3 a partir de la RAM que reporta Windows (GB, ya redondeada a 1 decimal,
    # la misma cifra que muestra el mensaje). Devuelve 'bloquea' | 'poca' | 'normal'.
    param([Parameter(Mandatory = $true)][double]$Gb)
    # < 3.5 -> 'bloquea'; < 7.5 -> 'poca'; si no -> 'normal'
}

function Get-PerfilMemoriaConfigurado {
    # Perfil efectivo de un config\.env: 'poca' solo si FACTUM_PERFIL_MEMORIA vale "poca"
    # (sin importar mayúsculas ni espacios). Si falta el archivo o la clave, o el valor es otro,
    # devuelve 'normal'.
    param([string]$EnvPath)
}
```

**Constante:** `$script:FactumComposePocaRam = 'docker-compose.poca-ram.yml'`.

**`Get-FactumComposeArgumento`:** sin caché, lee el `.env` en **cada** llamada (DT5).

- Orden de los argumentos: `compose`, `--project-directory <h>`, `-f <h>\docker-compose.yml`,
  **[`-f <h>\docker-compose.poca-ram.yml`]**, `--env-file <env>`, `-p <proyecto>`.
- Con perfil `normal`, el arreglo es **idéntico** al actual.
- Con `poca`, el `-f` extra se agrega **solo si el archivo existe**. Si falta, se muestra **una
  sola vez por proceso** (con una bandera `$script:FactumAvisoPocaRamMostrado`) el aviso:
  `Write-Aviso 'El perfil de memoria es "poca" pero falta docker-compose.poca-ram.yml en <home>: Factum corre sin límites de memoria. Ejecutá el instalador con -Reparar.'`.
  Después sigue con el base. No se usa `Stop-Factum`: no tiene que trabar backup, restaurar ni
  diagnóstico.
- No usar `$args` como nombre de variable: es automática en PowerShell.
- Actualizar el comentario de R7: "…siempre `-f <home>\docker-compose.yml` (+ el de poca RAM si
  el perfil lo pide)…".

**`Test-Requisitos`, Q3:**

- Se calcula `$perfil = Get-PerfilMemoria $gb`.
- `bloquea` → **falla**:
  `'La PC tiene {0} GB de memoria RAM. Factum necesita al menos 4 GB instalados (se recomiendan 8 GB).' -f $gb`.
- `poca` → **no** agrega nada a `Avisos`. El aviso lo muestra `instalar.ps1` aparte.
- `normal` con `$gb -lt 15` → el aviso de "recomendable 16 GB" de hoy, **sin cambios**.
- El objeto de retorno suma dos propiedades:
  - `RamGb`: `[double]`, o `$null` si WMI falló o en modo `Actualizar`.
  - `PerfilMemoria`: `'bloquea'`, `'poca'`, `'normal'` o `$null` en esos mismos casos.

  Hay que inicializar las dos en `$null` **antes** del `try` de WMI, por `Set-StrictMode`.
- Si WMI falla, queda el aviso genérico de hoy y `PerfilMemoria = $null`. `instalar.ps1` lo
  trata como `normal` (DT2).

**Helper de consola nuevo**, `Write-AvisoPocaMemoria -Gb <double>`. Imprime en amarillo, con el
texto de la HU (tono de los demás mensajes; `{0}` = GB con la cultura de la consola, igual que
Q3):

```
  POCA MEMORIA: la PC tiene {0} GB de RAM (se recomiendan 8 GB).
  Factum va a funcionar, pero LENTO: abrir pantallas y generar informes puede tardar
  bastante más, sobre todo con muchas capturas. Para que ande mejor:
   - cerrá otros programas y pestañas mientras uses Factum,
   - no dejes abierta la ventana de Docker Desktop (alcanza con la ballena junto al reloj).
  Factum se va a configurar para usar la menor memoria posible.
```

Va precedido de una línea en blanco.

### 5.4 `scripts/instalar.ps1`

**Parámetro nuevo:**

```powershell
.PARAMETER PerfilMemoria
    auto (por defecto) = según la RAM de la PC; poca o normal = forzarlo (por ejemplo para probar
    el perfil de poca memoria en una PC con más RAM). No saltea el mínimo de RAM.
[ValidateSet('auto', 'poca', 'normal')][string]$PerfilMemoria = 'auto'
```

**Al inicio del `try`, antes del paso 1:** si `$Reparar -and $PerfilMemoria -ne 'auto'`, se corta
con `Stop-Factum 'Con -Reparar no se cambia el perfil de memoria.' 'Para cambiarlo seguí la guía, sección 2.1.'`.
En ese punto no se tocó nada (DT4).

**Paso 2**, en este orden:

1. Igual que hoy: avisos genéricos, fallas (Q3 `bloquea` cae acá) y `Confirm-SN '¿Continuar igual con la instalación?'`
   **solo si hay avisos genéricos**.
2. `$perfilDetectado = $req.PerfilMemoria`.
3. Si `$perfilDetectado -eq 'poca'`, se llama a `Write-AvisoPocaMemoria -Gb $req.RamGb`. Si
   además `$PerfilMemoria -eq 'normal'`, se agrega
   `Write-Aviso 'Elegiste el perfil normal con -PerfilMemoria: los contenedores no van a tener límite de memoria y la PC puede quedarse sin memoria.'`.
   Después, `if (-not (Confirm-SN '¿Instalar igual con poca memoria?')) { Stop-Factum 'Instalación cancelada. No se tocó nada.' 'Podés volver a ejecutar el instalador cuando quieras.' }`.
   Corre igual con `-Reparar` (escenario de la HU).
4. `Write-Ok 'La PC cumple los requisitos.'`, como hoy.

**Perfil final** (variable `$perfilFinal`):

- Sin `-Reparar`:
  - `-PerfilMemoria poca` o `normal` → ese valor.
  - `auto` → `'poca'` si se detectó poca memoria; si no, `'normal'` (también cuando WMI falló).
- Con `-Reparar`: en el paso 3, después de validar la versión,
  `$perfilFinal = Get-PerfilMemoriaConfigurado $envPath`, y **no se escribe**. Si
  `$perfilFinal -eq 'poca'` y la PC tiene `normal` detectado, se muestra la línea informativa
  `Write-Host '  La PC tiene {0} GB: podés pasar al perfil normal (guía, sección 2.1).'`.
  No es un aviso ni hace pregunta.
- Al final del paso 2 (sin reparar) o del 3 (reparar):
  `Write-Ok ('Perfil de memoria: ' + <'poca RAM (límites de memoria activos)' | 'normal'>)`,
  más `' (elegido con -PerfilMemoria)'` si se forzó.

**Pasos 6, 7 y 13:**

- **Paso 6:** `$valores['FACTUM_PERFIL_MEMORIA'] = $perfilFinal`.
- **Paso 7:** se copia `docker-compose.poca-ram.yml` del paquete junto al `docker-compose.yml`.
  Se copia **siempre**, sea cual sea el perfil (DT6).
- **Paso 13:** después de "Backups:" va la línea
  `'  - Perfil de memoria:           poca RAM (para cambiarlo: guía, sección 2.1)'` o `normal`,
  leída con `Get-PerfilMemoriaConfigurado`.

**Ayuda del script (.SYNOPSIS/.PARAMETER):** se documenta `-PerfilMemoria`. Los pasos siguen
siendo 13.

### 5.5 `scripts/actualizar.ps1`

- **`Copy-ArchivosInstalacion`:** copia también `docker-compose.poca-ram.yml` **si existe en
  `$Origen`**. En el paquete siempre existe; en una copia previa a esta HU, no.
- **Paso 4 (copia para volver atrás):** copia `docker-compose.poca-ram.yml` de la instalación a
  `$previo` si existe.
- Lo demás no cambia:
  - La clave `FACTUM_PERFIL_MEMORIA` llega por el merge de claves nuevas de `.env.example`.
  - No se pregunta por la RAM (`-Modo Actualizar` no corre Q3).
- **Vuelta atrás a una versión anterior a esta HU:** el overlay nuevo queda en la carpeta, pero
  el `.env` restaurado no tiene la clave, así que el perfil es `normal` y no se usa. No hace
  falta borrarlo.

### 5.6 `scripts/diagnostico.ps1`: sección "Memoria" (solo lectura)

Va después de "Servicios (estado y salud)":

```
── Memoria ──
  RAM de la PC: 3,8 GB
  Perfil de memoria: poca RAM (FACTUM_PERFIL_MEMORIA=poca; docker-compose.poca-ram.yml presente)
  <docker stats --no-stream: SERVICIO / USO / % / CPU>
  Reinicios: mongo 0, backend 1 (sin memoria: SÍ), frontend 0
```

- **RAM:** `Get-CimInstance Win32_ComputerSystem`, redondeada a 1 decimal. Si falla:
  `Write-Aviso 'No se pudo leer la RAM (WMI).'`.
- **Perfil:** valor crudo del `.env` + `Get-PerfilMemoriaConfigurado` + si existe el overlay.
  - Valor que no es `poca`/`normal`/vacío → `Write-Aviso ('FACTUM_PERFIL_MEMORIA="' + $v + '" no es válido: se usa el perfil normal.')`.
  - Coherencia: RAM < 7.5 con perfil `normal` → `Write-Aviso 'La PC tiene poca RAM y el perfil es normal (sin límites). Ver guía, sección 2.1.'`.
    RAM ≥ 7.5 con perfil `poca` → `Write-Host '  La PC tiene RAM suficiente para el perfil normal (guía, sección 2.1).'`.
- **Uso por contenedor** (solo si `$dockerOk`): ids con `Get-FactumContainerId` para los tres
  servicios (los que existan) y
  `Invoke-Nativo docker @('stats','--no-stream','--format','table {{.Name}}\t{{.MemUsage}}\t{{.MemPerc}}\t{{.CPUPerc}}') + ids`.
  Es `docker` directo, no compose: R7 aplica a compose, y `docker compose stats` no está
  garantizado en compose 2.20. `MemUsage` muestra "uso / límite", así se ve si el perfil está
  aplicado.
- **Reinicios:** por id,
  `docker inspect -f '{{.RestartCount}} {{.State.OOMKilled}}'`. Muestra "sin memoria: SÍ" si
  `OOMKilled` es true.
- **Reinicio al final (DT8):** si se responde S o se pasa `-ReiniciarBackend`, primero corre
  `Invoke-FactumCompose @('up','-d')`, que recrea los contenedores cuya configuración cambió (por
  ejemplo, un cambio de perfil), y después `restart backend`, como hoy, para tomar
  `appsettings.Local.json` y el logo. Después espera healthy como hoy. Texto nuevo de la
  pregunta: `'¿Reiniciar Factum para aplicar cambios de configuración (identidad del estudio, logo o perfil de memoria)?'`.
  `.PARAMETER ReiniciarBackend` se actualiza en la misma línea.

### 5.7 `scripts/backup.ps1` y `scripts/restaurar.ps1`

- **`backup.ps1`, paso 5:** la lista `@('docker-compose.yml', 'version.txt')` suma
  `'docker-compose.poca-ram.yml'` (ya se copia solo si existe). Queda como referencia de cómo
  estaba la PC.
- **`restaurar.ps1`:** **sin cambios**. No toma `FACTUM_PERFIL_MEMORIA` del backup, porque el
  perfil es de la PC, como `FACTUM_HOME`/`FACTUM_VERSION` (DT9). Restaurar en una PC de 4 GB un
  backup de una de 16 GB conserva el perfil `poca` de la PC destino.

### 5.8 `armar-paquete.sh`

- **Paso 2:** `deploy/windows/docker-compose.poca-ram.yml` entra en la lista de archivos
  obligatorios.
- **Paso 8:** se agrega al `cp` de la raíz del paquete. `SHA256SUMS.txt` lo incluye solo, por el
  `find`.
- La extracción del tag de mongo sigue leyendo **solo** el compose base. El overlay no tiene
  `image:`.

### 5.9 `docs/instalacion-windows.md` (sin renumerar secciones, DT11)

- **§2, tabla de requisitos, fila RAM:**
  "**8 GB** recomendado. Con **4 GB** funciona, más lento, con el perfil de poca memoria
  (sección 2.1). Si Windows reporta menos de 3,5 GB, no se puede."
- **§2, fila Q3:** tres mensajes.
  - Falla "Factum necesita al menos 4 GB": no se puede instalar en esa PC.
  - Bloque "POCA MEMORIA" + pregunta S/N: con S sigue y queda el perfil de poca memoria.
  - Aviso "recomendable 16 GB": igual que hoy.
- **§2.1 nueva, "PC con poca memoria (4 GB)":**
  - **Qué hace el perfil:** tabla con los tres topes. Aclarar que son máximos, no reservas.
  - **Qué esperar:** pantallas y generación de informes más lentas, y Windows usando el disco
    como memoria. La PC del estudio tiene SSD, así que es tolerable; con un disco mecánico sería
    mucho más lento y un SSD es la mejora más barata.
  - **Consejos:** cerrar programas y pestañas, cerrar la ventana de Docker Desktop, reiniciar la
    PC al empezar el día y no tener otras distros de WSL abiertas.
  - **Si un informe falla o Factum se reinicia al generar:**
    - Volver a generar: la evidencia no se pierde y el caso se puede reintentar.
    - Mirar la sección "Memoria" del diagnóstico ("sin memoria: SÍ").
    - Si se repite, avisar al proveedor con el archivo del diagnóstico.
    - Los casos de más de ~40 capturas son los más exigentes (el número sale de la medición).
  - **Cambiar de perfil** (por ejemplo, si le agregan RAM): abrir `C:\Factum\config\.env` con el
    Bloc de notas, cambiar `FACTUM_PERFIL_MEMORIA=poca` por `normal` (o al revés), guardar, abrir
    "Diagnostico de Factum" y responder **S** a reiniciar. Los datos no se tocan: solo se
    recrean los contenedores.
  - **Cómo revertir esta HU:** `FACTUM_PERFIL_MEMORIA=normal` + reiniciar desde el diagnóstico.
  - **`.wslconfig` opcional, a mano** (los scripts no lo escriben):
    - Sin él, WSL2 ya usa como máximo la mitad de la RAM y ~25 % de swap.
    - Si el diagnóstico muestra la VM corta, se puede crear `%UserProfile%\.wslconfig` con
      `[wsl2]` / `memory=2GB` / `swap=2GB` y ejecutar `wsl --shutdown`. Esto apaga Docker
      Desktop: después hay que abrirlo de nuevo.
    - Afecta a todas las distros de WSL.
    - Para revertir: borrar el archivo y repetir `wsl --shutdown`.
  - **Checklist después de instalar en la PC de 4 GB:** ver §10.4. Es la prueba final en la PC
    real.
- **§5:**
  - El paso 2 de la lista menciona el aviso de poca memoria con su propia S/N.
  - El paso 6 menciona el perfil de memoria en `config\.env`.
  - El paso 13 menciona la línea del perfil en el resumen.
  - En "Opciones": `-PerfilMemoria poca|normal` (forzar el perfil; no saltea el mínimo de RAM;
    no se combina con `-Reparar`).
- **§12, filas nuevas:**
  - "**El informe falla o Factum se reinicia al generar** (PC con poca memoria)" → §2.1.
  - En la fila de "El backend no arrancó": "…o un valor inválido en `config\.env`" ya cubre el
    perfil. No hace falta más.
- **§14:** para probar el perfil en una VM o PC con más RAM:
  `scripts\instalar.ps1 -PerfilMemoria poca`. Agregar también el comando de comparación
  `docker compose config` del nivel 2 (§10.2), que sirve de referencia.

## 6. Contrato compartido

No cruza `client/` ↔ `server/` y no hay JSON nuevo. El contrato es entre el `.env`, los scripts
y los compose:

| Elemento | Valor exacto | Dónde vive / quién lo lee |
|---|---|---|
| Clave del `.env` | `FACTUM_PERFIL_MEMORIA` | `deploy/windows/.env.example` (default `normal`). La escribe `instalar.ps1` (paso 6). La leen `_comun.ps1` (`Get-PerfilMemoriaConfigurado`, `Get-FactumComposeArgumento`), `instalar.ps1` (resumen y reparar) y `diagnostico.ps1`. **No** se interpola en ningún compose. |
| Valores | `normal` \| `poca` (minúsculas al escribir; al leer no importan mayúsculas ni espacios; cualquier otro valor = `normal`) | ídem |
| Archivo de compose | `docker-compose.poca-ram.yml` (raíz del paquete y de `C:\Factum`) | `deploy/windows/docker-compose.poca-ram.yml`. Lo copian `instalar.ps1` (paso 7), `actualizar.ps1` (`Copy-ArchivosInstalacion` y paso 4), `backup.ps1` (paso 5) y `armar-paquete.sh` (pasos 2 y 8). |
| Variables de entorno de los contenedores | `DOTNET_gcServer: "0"` (backend), `NODE_OPTIONS: "--max-old-space-size=256"` (frontend) | overlay §5.1 |
| Flag de mongod | `--wiredTigerCacheSizeGB 0.25` | overlay §5.1 |
| Retorno de `Test-Requisitos` | propiedades nuevas `RamGb` (`[double]`/`$null`) y `PerfilMemoria` (`'bloquea'`\|`'poca'`\|`'normal'`/`$null`), además de `Fallas`/`Avisos` | `_comun.ps1` → `instalar.ps1` |
| Parámetro del instalador | `-PerfilMemoria auto\|poca\|normal` (default `auto`) | `instalar.ps1`, guía §5 y §14 |

## 7. Decisiones técnicas

Todas van en la opción recomendada. **Solo DT8 necesita al usuario**, porque cambia un texto y
un comportamiento visibles del diagnóstico que van más allá de la sección "Memoria" de la HU.

- **DT1. Umbrales en una función pura (`Get-PerfilMemoria`).** Compara contra el valor redondeado
  a 1 decimal, el mismo que se muestra: 3.5 → `poca`, 7.5 → `normal`. Así el número del mensaje
  y la decisión nunca se contradicen. *Recomendada.*
- **DT2. WMI falla → perfil `normal`.** Es el comportamiento de hoy, con el aviso genérico que
  ya existe. Se puede forzar con `-PerfilMemoria poca`. *Recomendada.*
- **DT3. El aviso de poca RAM no va en `Avisos`.** Va en las propiedades `RamGb`/`PerfilMemoria`.
  En el paso 2 primero aparecen los avisos genéricos con su pregunta, si hay. Después, el bloque
  POCA MEMORIA con su propia S/N, que es la última decisión antes de tocar el disco. Con
  `-Reparar` se usa el mismo texto, "¿Instalar igual con poca memoria?". *Recomendada.*
- **DT4. `-PerfilMemoria`:**
  - `ValidateSet auto|poca|normal`, con default `auto`.
  - No saltea el piso duro.
  - Forzar `normal` en una PC chica muestra igual el aviso y la S/N, más un aviso extra.
  - Combinado con `-Reparar` se rechaza antes del paso 1, sin tocar nada: reparar "no toca la
    configuración" y la HU dice que el perfil no cambia.

  *Recomendada.*
- **DT5. `Get-FactumComposeArgumento` lee el `.env` en cada llamada, sin caché.** Es robusto
  ante `actualizar` (agrega la clave a mitad del script) y ante la vuelta atrás (cambia el
  `.env`). Valor desconocido → `normal`. Si el perfil es `poca` y falta el overlay: un aviso y se
  sigue con el base, sin `Stop-Factum`, para no trabar backup, restaurar ni diagnóstico.
  *Recomendada.*
- **DT6. El overlay se copia siempre**, sea cual sea el perfil, en la instalación, la reparación
  y la actualización. Así cambiar de perfil después no necesita el paquete. *Recomendada.*
- **DT7. Overlay mínimo, sin `memswap_limit`.** Docker permite swap igual al límite dentro de la
  VM: con poca RAM, más lento es mejor que un OOM. No lleva `image:`, para no confundir a
  `Get-MongoImagen` ni a `armar-paquete.sh`. *Recomendada.*
- **DT8. Cómo se aplica un cambio de perfil. ⚠️ NECESITA AL USUARIO.**
  - **A (recomendada):** editar `config\.env` y abrir "Diagnostico de Factum" → **S**. Para eso,
    el reinicio del diagnóstico pasa de `restart backend` a `up -d` + `restart backend`, y la
    pregunta cambia a "¿Reiniciar Factum para aplicar cambios de configuración (identidad del
    estudio, logo o perfil de memoria)?". Hace falta porque `restart` **no** aplica cambios de
    compose (no recrea el contenedor), y reiniciar Windows tampoco.
  - **B:** no tocar el diagnóstico. La guía dice "ejecutá `scripts\instalar.ps1 -Reparar` desde
    el paquete de la misma versión". Exige tener el paquete a mano y una terminal.

  Si el usuario elige B, se saltean las partes de B10 y de la guía que tocan el reinicio del
  diagnóstico.
- **DT9. Restaurar no toma `FACTUM_PERFIL_MEMORIA` del backup** (el perfil es de la PC). El
  backup sí guarda una copia del overlay como referencia. *Recomendada.*
- **DT10. Sección "Memoria" del diagnóstico:** RAM, perfil con chequeo de coherencia,
  `docker stats --no-stream` por id de contenedor y `RestartCount`/`OOMKilled`. Es todo de solo
  lectura y muestra de entrada si el problema fue memoria. *Recomendada.*
- **DT11. Guía:** subsección nueva §2.1 y no una sección al final, para no renumerar: la guía ya
  cita "sección 13", "sección 3", etc. *Recomendada.*
- **DT12. Medición del nivel 3:**
  - Imágenes **nativas arm64** con tag `0.0.0-verif`. La emulación amd64 en la Mac distorsiona
    memoria y tiempos; los topes del cgroup se comportan igual.
  - **Métrica de decisión: pico de `anon` de `memory.stat`** (memoria no recuperable: heap de
    .NET + nativo), muestreado cada 1 s.
  - Se registran también el pico de `docker stats`, que puede inflarse con *page cache* activa
    por leer los videos tres veces, y `memory.peak`.
  - Criterio duro: **cero** eventos `oom_kill` en `memory.events`, `OOMKilled=false`,
    `RestartCount` sin cambios y ningún `OutOfMemoryException` en los logs.

  *Recomendada.*
- **DT13. Reglas de ajuste si la medición lo pide** (umbral = 80 % del tope, sobre el pico de
  `anon`). La HU ya las autoriza ("los números finales se confirman con la medición").

  | Servicio | Si el pico de `anon` supera | Ajuste |
  |---|---|---|
  | backend | 614 MiB | subir a `1024m` y volver a medir. Si con `1024m` supera ~820 MiB o hay OOM, devolver `blocked` con los números: hace falta una HU de código (DOCX sin imágenes en RAM, fuera de alcance). No bajar el caso de referencia. |
  | mongo | 410 MiB | subir a `640m` |
  | frontend | 307 MiB | subir a `512m` |

  Todo cambio de valor se aplica solo en el overlay y en la tabla de la guía §2.1, y se anota en
  el progress. **El orquestador se lo informa al usuario** al cerrar (cambia el total de topes,
  hoy ≈ 1.6 GB frente a ≈ 1.9 GB de la VM). *Informativa.*
- **DT14. Pester para las funciones puras y para el armado de argumentos de compose**, con un
  home temporal. `Test-Requisitos` sigue sin test unitario porque depende de WMI. *Recomendada.*

## 8. Fuera de alcance (recordatorio técnico)

Optimizar la memoria del generador de DOCX, escribir `.wslconfig`, limitar la memoria de
Tatana, del navegador o de Docker Desktop, el modo desatendido, cambiar Q5 u otros requisitos y
tocar `client/`, `agent-ui/`, la API o Tatana.

## 9. Checklist atómico — `implementer-backend` (infra + docs)

> **Reglas duras** (las mismas de la SDD `instalacion-local-docker` §9, más las de esta HU):
>
> - **Docker y datos del usuario:**
>   - Ningún `docker compose` sin `-p factum-verif` y sin `--env-file` de scratch.
>   - Prohibido `docker compose down`/`rm` sobre el proyecto `factum` de la Mac
>     (`factum-backend-1`, `factum-mongo-1`, volúmenes `factum_*`).
>   - Prohibido `docker volume prune`/`system prune`/`image prune`.
>   - No tocar `evidentia-v2-*`, `nutri-bot-db-1` ni el puerto 27017.
>   - No tocar `server/src/Factum.Backend/dev-data/` ni ningún `Storage:DataDirectory` del
>     usuario.
> - **Antes de levantar `factum-verif`:** confirmar con
>   `docker ps -a --filter label=com.docker.compose.project=factum-verif` y
>   `docker volume ls --filter name=factum-verif` que no existe. Si existe, **no** se baja:
>   devolver `blocked` y avisar.
> - **Carpetas de prueba:** van en el scratchpad de la sesión y se borran al terminar.
> - **Imágenes:** solo se borran `factum-backend:0.0.0-verif` y `factum-frontend:0.0.0-verif`.
>   `mongo:7.0.43` (ni su variante arm64, si se descarga) **no** se borra.
> - **Git:**
>   - No tocar `backlog.json` ni `progress/current.md`.
>   - No editar ni stagear `server/src/Factum.Agent/appsettings.json` ni los `agent-ui/*.tsbuildinfo`.
>   - `git add` solo de los archivos de esta HU.
> - **Scripts:** R1-R9 en todos los `.ps1`: PowerShell 5.1, UTF-8 **con BOM** + CRLF y toda
>   llamada a compose por `Invoke-FactumCompose`.

**Compose y config**

- [ ] B1. Crear `deploy/windows/docker-compose.poca-ram.yml` (§5.1), en LF como el compose base.
- [ ] B2. `deploy/windows/docker-compose.yml`: **solo** el comentario de cabecera. Mencionar que
      en el perfil poca RAM se suma `docker-compose.poca-ram.yml`. Ninguna clave YAML cambia.
- [ ] B3. `deploy/windows/.env.example`: clave `FACTUM_PERFIL_MEMORIA=normal` con su comentario
      (§5.2).

**`_comun.ps1`**

- [ ] B4. `Get-PerfilMemoria` y `Get-PerfilMemoriaConfigurado` (§5.3).
- [ ] B5. Constante del overlay + `Get-FactumComposeArgumento` con el `-f` condicional, el aviso
      único y el comentario R7 actualizado (§5.3).
- [ ] B6. `Test-Requisitos`, Q3: `bloquea`/`poca`/`normal`, el mensaje nuevo de falla y las
      propiedades `RamGb`/`PerfilMemoria` inicializadas antes del `try` (§5.3).
- [ ] B7. `Write-AvisoPocaMemoria` (§5.3).

**Scripts**

- [ ] B8. `instalar.ps1`:
  - parámetro y ayuda, y rechazo de `-Reparar` + `-PerfilMemoria`;
  - paso 2 (bloque + S/N propia);
  - perfil final (también en reparar) con sus líneas informativas;
  - paso 6 (clave), paso 7 (copia del overlay) y paso 13 (línea del perfil) (§5.4).
- [ ] B9. `actualizar.ps1`: `Copy-ArchivosInstalacion` y paso 4 copian el overlay si existe
      (§5.5).
- [ ] B10. `diagnostico.ps1`: sección "Memoria" (§5.6) y, **según DT8**, reinicio con `up -d` +
      `restart backend` y texto nuevo de la pregunta y de `.PARAMETER`.
- [ ] B11. `backup.ps1`: el overlay en la lista del paso 5 (§5.7). `restaurar.ps1` sin cambios.

**Empaquetado**

- [ ] B12. `armar-paquete.sh`: el overlay en la lista obligatoria del paso 2 y en el `cp` del
      paso 8 (§5.8).

**Tests**

- [ ] B13. `tests/comun.Tests.ps1`, `Describe 'Get-PerfilMemoria'`:
  - 0 / 3.4 → `bloquea`;
  - 3.5 / 3.8 / 7.4 → `poca`;
  - 7.5 / 7.6 / 16 → `normal`.
- [ ] B14. `tests/comun.Tests.ps1`, `Describe 'Get-PerfilMemoriaConfigurado'`:
  - sin archivo / sin clave / `normal` → `normal`;
  - `poca` y `' POCA '` → `poca`;
  - `xyz` y vacío → `normal`.
- [ ] B15. `tests/comun.Tests.ps1`, `Describe 'Get-FactumComposeArgumento'`. Con un home
      temporal + `.env` + `Initialize-FactumContext`:
  - **(a)** Perfil `normal` → el arreglo es **igual elemento por elemento** a
    `@('compose','--project-directory',$h,'-f',<h/docker-compose.yml>,'--env-file',<env>,'-p',<proy>)`.
  - **(b)** `poca` + overlay presente → hay un segundo `-f` con el overlay, inmediatamente
    después del primero, y el resto igual.
  - **(c)** `poca` sin overlay → mismo arreglo que (a). Mockear `Write-Aviso` o tolerar la
    salida.
  - **(d)** Sin la clave en el `.env` → igual que (a).
- [ ] B16. `tests/comun.Tests.ps1`, guardas de repo:
  - `Read-EnvFile` de `.env.example` da `FACTUM_PERFIL_MEMORIA` = `normal`.
  - `Get-MongoImagen` sobre `docker-compose.poca-ram.yml` devuelve `$null` (sin `image:`).
  - `Get-MongoImagen` sobre `docker-compose.yml` sigue devolviendo `mongo:7.0.43`.

**Docs**

- [ ] B17. `docs/instalacion-windows.md`:
  - §2 (fila RAM y fila Q3);
  - §2.1 nueva completa, con la tabla de topes **con los valores finales de la medición** y el
    checklist de §10.4;
  - §5 (pasos 2, 6 y 13, y opciones);
  - §12 (fila nueva);
  - §14 (`-PerfilMemoria`, comando del nivel 2).

  Todo según §5.9.

**Verificación y cierre**

- [ ] B18. Nivel 1 (§10.1).
- [ ] B19. Nivel 2 (§10.2).
- [ ] B20. Nivel 3 (§10.3), con ajustes según DT13 si hacen falta.
- [ ] B21. `progress/impl_backend_instalacion-poca-ram.md` con:
  - las salidas reales de los tres niveles;
  - las tablas de reposo y pico por contenedor (`anon`, `docker stats`, `memory.peak`), tiempo
    de generación, tamaño del dataset y hash del ZIP verificado;
  - los valores finales del overlay, la confirmación de la limpieza y el `git status` final.

## 10. Verificación

### 10.1 Nivel 1: scripts (en la Mac, contenedor pwsh)

1. **V1.1. Parseo + analyzer + Pester.** Es el mismo comando que la HU anterior (B-V8). Ajustar
   el tag de la imagen si hace falta:
   ```bash
   docker run --rm -v "$PWD/deploy/windows:/w" mcr.microsoft.com/powershell:lts-ubuntu-22.04 pwsh -NoProfile -Command \
     "Install-Module PSScriptAnalyzer -Force -Scope CurrentUser; Install-Module Pester -MaximumVersion 5.99 -Force -Scope CurrentUser -SkipPublisherCheck; \
      foreach(\$f in Get-ChildItem /w/scripts/*.ps1){ \$e=\$null; [System.Management.Automation.Language.Parser]::ParseFile(\$f.FullName,[ref]\$null,[ref]\$e) | Out-Null; if(\$e){ \$e; exit 1 } }; \
      Invoke-ScriptAnalyzer -Path /w/scripts -Settings /w/scripts/PSScriptAnalyzerSettings.psd1 -Severity Warning,Error; Invoke-Pester /w/tests -Output Detailed"
   ```
   Resultado esperado: **cero errores**, todos los tests verdes (los anteriores + B13-B16) y
   ningún warning nuevo sin justificar en el progress.
2. **V1.2. BOM y fin de línea.**
   - `head -c3` = `efbbbf` en cada `.ps1` tocado.
   - `file` = CRLF en los `.ps1`.
   - El overlay y el `.env.example` en LF.
3. **V1.3. Empaquetado.** `bash -n deploy/windows/armar-paquete.sh`, y `shellcheck` si está
   instalado. Correr `armar-paquete.sh` completo **no** es obligatorio (tarda más de 10 min por
   el frontend amd64): lo cubre la prueba manual M1.

### 10.2 Nivel 2: `docker compose config` (sin levantar nada)

Preparación:

- Scratch `$S` con:
  - `$S/home/{evidencia,config/branding}`;
  - `$S/home/config/appsettings.Local.json` copiado del example;
  - un `.env` de scratch: `COMPOSE_PROJECT_NAME=factum-verif`, `FACTUM_VERSION=0.0.0-verif`,
    `FACTUM_HOME=$S/home` y `FACTUM_JWT_SECRET=$(openssl rand -hex 64)`.
- `C="docker compose -p factum-verif --project-directory $S/home --env-file"`.

1. **V2.1. Normal = hoy.**
   - `A`: `git show cfb97c8:deploy/windows/docker-compose.yml > $S/base-antes.yml`, y `config`
     con ese archivo y el `.env` **sin** la clave.
   - `B`: `config` con `deploy/windows/docker-compose.yml` (que es lo que arma
     `Get-FactumComposeArgumento` en `normal`, verificado en B15a) y el `.env` **con**
     `FACTUM_PERFIL_MEMORIA=normal`.
   - Resultado esperado: `diff A B` **vacío**.
2. **V2.2. Poca = solo lo esperado.**
   - `P`: `config` con `-f deploy/windows/docker-compose.yml -f deploy/windows/docker-compose.poca-ram.yml`
     y el `.env` con `poca`.
   - `diff B P` tiene que mostrar **únicamente**:
     - `mongo.command` (`mongod --wiredTigerCacheSizeGB 0.25`) y `mongo.mem_limit`;
     - `backend.environment.DOTNET_gcServer: "0"` y `backend.mem_limit`;
     - `frontend.environment.NODE_OPTIONS` y `frontend.mem_limit`.
   - Se pega el diff en el progress. Además, con `config --format json` + `python3`, se afirma
     que los `mem_limit` valen 536870912 / 805306368 / 402653184 (o los de DT13).
3. **V2.3. Mensaje de falta de `FACTUM_JWT_SECRET`:** sigue igual con el overlay sumado.

### 10.3 Nivel 3: medición del pico en la Mac (perfil poca RAM, caso de referencia)

**Preparación (todo en `$S`, el proyecto es `factum-verif`):**

1. Imágenes nativas:
   - `docker build -t factum-backend:0.0.0-verif server/src/Factum.Backend`
   - `docker build -t factum-frontend:0.0.0-verif --build-arg NEXT_PUBLIC_BACKEND_URL=http://localhost:8080 --build-arg NEXT_PUBLIC_AGENT_URL=http://localhost:8765 client`
   - Mongo: `docker image inspect --platform linux/arm64 mongo:7.0.43`. Si falta la variante,
     `docker pull --platform linux/arm64 mongo:7.0.43`, que agrega la variante sin borrar la
     amd64. Anotar en el progress lo que se hizo.
2. `$S/verif.yml` (override de scratch, va **después** del overlay):
   - `backend: { ports: !reset ["127.0.0.1:18080:8080"] }`
   - `frontend: { ports: !reset [] }`

   Antes, `lsof -nP -iTCP:18080 -sTCP:LISTEN` tiene que dar vacío; si no, usar otro puerto libre
   y anotarlo. Opcional: `backend: { cpus: 4 }` para imitar los 4 hilos del i3-7100U (anotar si
   se usó).
3. Levantar:
   `$C $S/.env -f deploy/windows/docker-compose.yml -f deploy/windows/docker-compose.poca-ram.yml -f $S/verif.yml up -d`
   (con `.env` `poca`) y esperar a que los tres estén `healthy`.
4. **Confirmar que el perfil está aplicado:**
   - `docker inspect -f '{{.HostConfig.Memory}}'` de cada contenedor = los topes;
   - `exec mongo mongosh --quiet --eval "db.serverStatus().wiredTiger.cache['maximum bytes configured']"` = 268435456;
   - `exec backend printenv DOTNET_gcServer` = `0`;
   - `exec frontend printenv NODE_OPTIONS`.

**Dataset sintético** (generador en `$S`, **no** se commitea):

5. Un `python3` **solo con la stdlib** (`zlib`, `struct`) escribe 32 PNG RGB de **1080×2400**
   (`captura_01.png` … `captura_32.png`).
   - Contenido: franjas de ruido mezcladas con zonas planas o degradés, ajustando la proporción
     para que **cada archivo pese entre 1 y 3 MB** (promedio ≈ 2 MB).
   - Otras 8 se convierten a JPEG con `sips -s format jpeg -s formatOptions 90 in.png --out captura_NN.jpg`
     (33-40). Hay que confirmar que pesen ≥ 1 MB; si no, subir el ruido.
   - Total: **40 capturas**.
   - Videos: `head -c` de `/dev/urandom` → `grabacion_01.mp4` (300 MB), `grabacion_02.mp4`
     (300 MB) y `grabacion_03.mp4` (150 MB). El clasificador los toma por extensión y van al ZIP,
     no al DOCX.
   - Anotar la cantidad y los tamaños en el progress.

**Caso vía API** (cliente `python3` + `urllib` o `curl` contra `http://127.0.0.1:18080`; JSON
en **snake_case**):

6. Armar el caso:
   - `POST /api/auth/login` con `{"dni":"99999990","username":"verif","password":"verif"}`
     (modo `dev`) → `token`; las demás llamadas van con `Authorization: Bearer`.
   - `PUT /api/profile` con `nombre`, `matricula`, `profesion` y `caracter` completos (perito
     completo, `DTOs/ProfileDtos.cs`).
   - `POST /api/cases` con todos los obligatorios de `CaseValidation.MissingCaseFields`:
     `nombre_tribunal`, `tipo_causa`, `nro_referencia`, `caratula`, `parte_denunciante`,
     `parte_denunciada`, `fecha_intervencion` (fecha válida según `IsValidDate`),
     `nombre_proponente`, `nombre_denunciante`, `tipo_dispositivo` y `device` con `imei` válido
     (`DTOs/CaseDtos.cs`, `DeviceInfoDto`).
   - Por archivo: `POST /api/cases/{id}/files?filename=<nombre>` con el binario en el body.
   - `PUT /api/cases/{id}/capture-roles` con `{"capture_roles":[{"filename":"captura_01.png","role":"imei_modelo"},{"filename":"captura_02.png","role":"nombre_dispositivo"}]}`.
   - `PUT /api/cases/{id}/report-texts`:
     - `"formato":"markdown"` y las 5 secciones obligatorias no vacías;
     - en `resultados`, **20** figuras `![Captura N](captura:captura_NN.png)`, cada una como
       párrafo propio (máximo 20 por sección, `ReportImageRef.MaxPerSection`);
     - en `conclusiones`, 10 más (`.jpg` incluidos).

     Así el cuerpo reutiliza los `ImagePart` del anexo, como en un caso real.
7. **Monitoreo** en background desde **antes** de generar. Cada 1 s, para los 3 contenedores:
   - `docker exec <id> sh -c 'grep -E "^(anon|file) " /sys/fs/cgroup/memory.stat'`;
   - `docker stats --no-stream --format '{{.Name}},{{.MemUsage}}'`.

   Todo con timestamp a `$S/mem.csv`. Antes y después: `cat /sys/fs/cgroup/memory.peak` y
   `memory.events` de cada contenedor, y `docker inspect -f '{{.RestartCount}} {{.State.OOMKilled}}'`.
8. **Reposo:** 60 s después de `healthy`, más un par de `exec frontend wget -qO- http://127.0.0.1:3000/`.
   Se registra el uso por contenedor.
9. **Generar:** `POST /api/cases/{id}/generate`, cronometrado. Mientras corre, una vez,
   `exec -T mongo mongodump --db factum --archive=/tmp/v.archive.gz --gzip` (el mismo comando
   que `backup.ps1`) y después `rm -f` de ese archivo dentro del contenedor.
10. **Resultado esperado:**
    - HTTP 200 con `zip_hash`.
    - `shasum -a 256` del ZIP en `$S/home/evidencia/<caso>/` = `zip_hash`.
    - El DOCX abre con `python3 -m zipfile -t` y tiene ≥ 40 entradas en `word/media/`.
    - `RestartCount` sin cambios, `oom_kill 0` en los 3 `memory.events` y nada de
      `OutOfMemoryException` en `compose logs backend`.
    - **Pico de `anon` del backend ≤ 614 MiB**, mongo ≤ 410 y frontend ≤ 307. Si alguno lo
      supera → DT13: ajustar el overlay y repetir 3-10.
11. **V3.6. Escenario "si igual se queda sin memoria"** (HU):
    - Crear un **segundo** caso con el mismo dataset.
    - Sumar un `$S/oom.yml` con `backend: { mem_limit: 256m }` y hacer `up -d` (recrea solo el
      backend).
    - Generar. Se espera un error (HTTP 500 o conexión cortada).
    - Anotar si fue OOM gestionado (caso `error`, backend vivo) o del kernel (`OOMKilled=true`,
      caso `generating`, el backend reinició).
    - En los dos casos, los 43 archivos sueltos siguen en la carpeta del caso.
    - Quitar `oom.yml`, `up -d` (vuelve a 768m) y **regenerar el mismo caso** → 200 con el hash
      verificado.
12. **Limpieza:**
    - `$C $S/.env -f … -f $S/verif.yml down -v`. Solo baja `factum-verif` y su volumen
      `factum-verif_mongo-data`, creado por la prueba.
    - `docker image rm factum-backend:0.0.0-verif factum-frontend:0.0.0-verif`.
    - `rm -rf $S`.
    - Confirmar con `docker ps -a` y `docker volume ls` que `factum-*` del usuario sigue igual que
      antes.

### 10.4 Prueba manual del usuario (PC del estudio, 4 GB, Windows 10, SSD)

Es la checklist que también va en la guía §2.1.

1. **M1.** En la Mac, `deploy/windows/armar-paquete.sh --version <X>`. `SHA256SUMS.txt` lista
   `docker-compose.poca-ram.yml`.
2. **M2.** `1-Instalar Factum.bat`:
   - Aparece el bloque **POCA MEMORIA** con la RAM real (≈ 3,7-3,9 GB) y su S/N, separada de
     "¿Continuar igual…?" si hubo otros avisos.
   - **N** → "Instalación cancelada" y no existe `C:\Factum`.
   - Volver a correr y responder **S**.
3. **M3.** Al terminar:
   - `config\.env` tiene `FACTUM_PERFIL_MEMORIA=poca`;
   - el resumen dice "Perfil de memoria: poca RAM";
   - `http://localhost:3000` muestra el login.
4. **M4.** "Diagnostico de Factum":
   - la sección Memoria muestra RAM, perfil y uso "x / 512MiB", "x / 768MiB", "x / 384MiB";
   - reinicios en 0.
5. **M5.** Caso real típico (15-20 fotos + video): generar el informe. Anda (lento), el ZIP
   verifica y no hay reinicios en el diagnóstico.
6. **M6.** `instalar.ps1 -Reparar` desde el paquete: vuelve a preguntar por la poca memoria y el
   perfil sigue en `poca`.
7. **M7.** (Si hay una PC de 8 GB o más a mano.) La instalación queda en `normal` y el
   diagnóstico muestra el uso sin topes propios.
8. **M8.** Cambio de perfil: `poca` → `normal` en `config\.env` + diagnóstico → **S** (DT8). Los
   contenedores se recrean sin topes, con los casos intactos. Volver a `poca`.
9. **M9.** Actualizar con un paquete nuevo: no pregunta por la RAM y el perfil se conserva.

### 10.5 Lo que el reviewer chequea además de CHECKPOINTS

- `git diff cfb97c8 -- deploy/windows/docker-compose.yml` solo cambia comentarios.
- El diff de V2.1 está vacío y el de V2.2 tiene solo las 6 diferencias esperadas.
- Ningún script escribe `.wslconfig` ni llama a `wsl --shutdown`.
- Todo `docker compose` sigue pasando por `Invoke-FactumCompose`.
- El progress tiene números reales del nivel 3 (no estimados), la limpieza confirmada y nada
  del proyecto `factum` del usuario tocado.
- `server/src/Factum.Agent/appsettings.json` y `agent-ui/*.tsbuildinfo` no están en el diff de la
  HU.

## Resolución de decisiones (2026-10-02)

- **DT8 → A**, confirmada por el usuario: el perfil se cambia editando `config\.env` y respondiendo S en "Diagnóstico de Factum" (`up -d` + `restart backend`).
- DT13: si la medición obliga a subir un tope, se informa al usuario al cerrar la HU.
- Resto: las recomendadas.
