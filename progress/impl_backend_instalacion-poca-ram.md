# impl_backend — instalacion-poca-ram

**Estado:** done (sin commit, como se pidió). **Rama:** `feat/instalacion-poca-ram`.
**SDD:** `Refactorizaciones/instalacion-poca-ram.md` (DT1–DT14 recomendadas, DT8 = A).
**Topes finales del overlay: SIN CAMBIOS (512m / 768m / 384m).** La medición no pidió ajustes (DT13 no se aplicó).

## Archivos tocados

| Archivo | Cambio | Checklist |
|---|---|---|
| `deploy/windows/docker-compose.poca-ram.yml` (nuevo, LF) | Overlay exacto de §5.1 | B1 |
| `deploy/windows/docker-compose.yml` | Solo 2 líneas de comentario en la cabecera (ninguna clave YAML) | B2 |
| `deploy/windows/.env.example` (LF) | `FACTUM_PERFIL_MEMORIA=normal` + comentario §5.2 | B3 |
| `deploy/windows/scripts/_comun.ps1` | `Get-PerfilMemoria`, `Get-PerfilMemoriaConfigurado`, `$script:FactumComposePocaRam`, `$script:FactumAvisoPocaRamMostrado`, `Get-FactumComposeArgumento` con `-f` condicional + aviso único, comentario R7, `Test-Requisitos` Q3 (bloquea/poca/normal, mensaje nuevo, `RamGb`/`PerfilMemoria` inicializadas antes del `try`), `Write-AvisoPocaMemoria` | B4–B7 |
| `deploy/windows/scripts/instalar.ps1` | `-PerfilMemoria auto\|poca\|normal` + ayuda; rechazo `-Reparar`+`-PerfilMemoria` antes del paso 1; paso 2 con bloque y S/N propios; perfil final (reparar lo lee del `.env` sin escribir, línea informativa si la PC ya tiene RAM normal); `Write-Ok 'Perfil de memoria: …'`; paso 6 (clave), paso 7 (copia del overlay siempre), paso 13 (línea del perfil) | B8 |
| `deploy/windows/scripts/actualizar.ps1` | `Copy-ArchivosInstalacion` y paso 4 copian el overlay si existe | B9 |
| `deploy/windows/scripts/diagnostico.ps1` | Sección "Memoria" (RAM, perfil crudo/efectivo/overlay, coherencia, `docker stats --no-stream` por id, reinicios + "sin memoria: SÍ"); reinicio DT8 A = `up -d` + `restart backend`, pregunta y `.PARAMETER` nuevos | B10 |
| `deploy/windows/scripts/backup.ps1` | Overlay en la lista del paso 5 (`restaurar.ps1` sin cambios) | B11 |
| `deploy/windows/armar-paquete.sh` | Overlay en la lista obligatoria (paso 2) y en el `cp` (paso 8) | B12 |
| `deploy/windows/tests/comun.Tests.ps1` | Describes `Get-PerfilMemoria`, `Get-PerfilMemoriaConfigurado`, `Get-FactumComposeArgumento` (a–d + "sin caché"), guardas del repo | B13–B16 |
| `docs/instalacion-windows.md` | §2 (fila RAM, 3 filas Q3), §2.1 nueva completa (tabla de topes con valores finales, qué esperar, consejos, si falla, cambiar/revertir perfil, `.wslconfig` a mano, checklist §10.4), §5 (pasos 2, 6, 13 y opciones), §12 (fila nueva), §14 (`-PerfilMemoria poca` + comando de comparación nivel 2) | B17 |

No se tocó: `client/`, `agent-ui/`, la API, Tatana, `restaurar.ps1`, `backlog.json`, `progress/current.md`, `server/src/Factum.Agent/appsettings.json`, `agent-ui/*.tsbuildinfo`. No hubo cambios en .NET, así que no corresponde `dotnet build`/`dotnet test` de esta HU. (`verify.sh` igual compiló Tatana por el cambio local del usuario en su `appsettings.json`: limpio.)

## Decisiones no obvias

- **`Write-PerfilElegido`** es una función local de `instalar.ps1` (no de `_comun.ps1`): arma el `Write-Ok 'Perfil de memoria: …'` con el sufijo `(elegido con -PerfilMemoria)`. Sin reparar se muestra al final del paso 2; con reparar, al final del paso 3, y nunca con sufijo, porque con `-Reparar` no se acepta `-PerfilMemoria`.
- **Q3 cuando WMI falla en Q4 pero no en Q3.** `RamGb`/`PerfilMemoria` quedan con el valor leído en Q3. Q3 y Q4 comparten el `try`, así que si la RAM se leyó bien no se descarta. Si falla la propia lectura de RAM, quedan en `$null`, como dice la SDD.
- **Diagnóstico, reinicio:** si falla `up -d`, no se corre `restart`. El mensaje final pasó a "Factum reiniciado/no volvió a arrancar…" y menciona también `config\.env`.
- **Diagnóstico, sección Memoria:** se ve después de "Servicios" y antes de "Puertos". Los avisos de coherencia salen solo si se pudo leer la RAM.
- **Test (c):** `Write-Aviso` se mockea y se verifica `-Times 1 -Exactly` después de dos llamadas, para confirmar que el aviso sale una sola vez por proceso. Agregué un sexto caso, "lee el .env en cada llamada (sin caché)" (DT5).
- **Guía, "~40 capturas":** el número sale de la medición (ver abajo). Se aclara que con 40 capturas el backend quedó en ≈ 260 MB de 768 MB.

## Contrato (coincide con la SDD §6)

`FACTUM_PERFIL_MEMORIA` (`normal`|`poca`; se lee sin importar mayúsculas ni espacios y cualquier otro valor cuenta como `normal`), `docker-compose.poca-ram.yml`, `DOTNET_gcServer: "0"`, `NODE_OPTIONS: "--max-old-space-size=256"`, `--wiredTigerCacheSizeGB 0.25`, `Test-Requisitos` → `RamGb`/`PerfilMemoria`, `-PerfilMemoria auto|poca|normal`. Ningún compose interpola la clave.

---

## Nivel 1 — scripts (contenedor `mcr.microsoft.com/powershell:lts-ubuntu-22.04`, comando V1.1)

```
PARSE OK
ANALYZER DONE            <- Invoke-ScriptAnalyzer /w/scripts con el settings: 0 hallazgos
ANALYZER TESTS DONE      <- Invoke-ScriptAnalyzer /w/tests: 0 hallazgos
Pester v5.9.1 — Discovery found 35 tests
Describing Get-PerfilMemoria: 0/3.4 -> bloquea; 3.5/3.8/7.4 -> poca; 7.5/7.6/16 -> normal   (8 OK)
Describing Get-PerfilMemoriaConfigurado: sin archivo / sin clave / normal / poca / ' POCA ' / xyz / vacío  (7 OK)
Describing Get-FactumComposeArgumento: (a) (b) (c) (d) + sin caché  (5 OK)
Describing Guardas del repo: .env.example=normal; overlay sin image:; base = mongo:7.0.43  (3 OK)
Tests Passed: 35, Failed: 0, Skipped: 0   (12 anteriores + 23 nuevos)
```

V1.2:
- `head -c3` da `efbbbf` en `_comun`, `instalar`, `actualizar`, `diagnostico`, `backup` y `comun.Tests`.
- `file` da "with CRLF line terminators" en todos los `.ps1`.
- `docker-compose.poca-ram.yml` y `.env.example` quedan en LF ("UTF-8 text", sin CRLF).

V1.3:
- `bash -n armar-paquete.sh` OK.
- `shellcheck` no está instalado en la Mac.

Guardas:
- `grep wslconfig|wsl --shutdown` en `scripts/` da vacío.
- Toda llamada a compose sigue pasando por `Invoke-FactumCompose`. Las únicas apariciones de "docker compose" fuera de `_comun.ps1` son textos de error.

## Nivel 2 — `docker compose config` (proyecto `factum-verif`, `.env` de scratch)

**V2.1** (`A` = `git show cfb97c8:deploy/windows/docker-compose.yml` + `.env` sin la clave; `B` = compose actual + `.env` con `normal`): `diff A B` → **vacío**.

**V2.2** `diff B P` (P = base + overlay + `.env` con `poca`). Solo aparecen las 6 diferencias esperadas:

```
10a11
>       DOTNET_gcServer: "0"
32a34
>     mem_limit: "805306368"
66a69,70
>     environment:
>       NODE_OPTIONS: --max-old-space-size=256
83a88
>     mem_limit: "402653184"
94a100,103
>     command:
>       - mongod
>       - --wiredTigerCacheSizeGB
>       - "0.25"
111a121
>     mem_limit: "536870912"
```

`config --format json` + python:
- mongo: 536870912, con `['mongod','--wiredTigerCacheSizeGB','0.25']`;
- backend: 805306368, con `DOTNET_gcServer=0`;
- frontend: 402653184, con `NODE_OPTIONS=--max-old-space-size=256`.
- Resultado: `mem_limit OK`.

**V2.3:** sin `FACTUM_JWT_SECRET` el mensaje es idéntico con y sin overlay: `required variable FACTUM_JWT_SECRET is missing a value: Falta FACTUM_JWT_SECRET en config/.env`.

## Nivel 3 — medición real (Mac Apple Silicon, Docker Desktop 10 CPU / 7.75 GiB de VM)

**Preparación:**
- Antes de levantar se confirmó que `factum-verif` no existía (0 contenedores, 0 volúmenes).
- Imágenes nativas arm64 `factum-backend:0.0.0-verif` y `factum-frontend:0.0.0-verif` (build con los comandos de §10.3).
- `verif.yml`: `backend.ports: !override ["127.0.0.1:18080:8080"]` y `frontend.ports: !reset []`.
  - **Nota:** la SDD indicaba `!reset [...]`, pero `!reset` vacía la lista e ignora el valor (el backend quedó sin puerto). Lo correcto es `!override`.
- No se usó `cpus:`.

**Mongo arm64 (anotar):** `docker pull --platform linux/arm64 mongo:7.0.43` **no** sumó la variante al índice existente.
- Docker Hub tenía republicado el tag, así que el pull movió `mongo:7.0.43` del índice `sha256:9854f713…` (con amd64 local) a un índice nuevo, `sha256:1f995ad6…`, con solo arm64 local.
- El índice viejo nunca se borró: quedó sin tag.
- **Al terminar, el tag se restauró a mano:** `docker tag sha256:9854f7139445… mongo:7.0.43`. Verificado: `mongo:7.0.43` vuelve a apuntar a `9854f713…` y `--platform linux/amd64` da `amd64 295920115`.
- El índice arm64 nuevo no se borró. `armar-paquete.sh` hace igual su propio `docker pull --platform linux/amd64`.
- `factum-mongo-1`/`evidentia-v2-mongo-1` usan `mongo:7`: no los afectó.

**Perfil aplicado (paso 4):**

| Chequeo | Valor |
|---|---|
| `HostConfig.Memory` | mongo 536870912, backend 805306368, frontend 402653184 |
| WiredTiger `maximum bytes configured` | 268435456 |
| `printenv DOTNET_gcServer` | `0` |
| `printenv NODE_OPTIONS` | `--max-old-space-size=256` |
| arquitectura | aarch64 (mongo y backend) |

**Dataset** (python stdlib + `sips`; no se commitea), 43 archivos, **824 MB** en la carpeta del caso:
- **32 PNG** 1080×2400 (`captura_01..32.png`) y **8 JPEG** q90 (`captura_33..40.jpg`):
  - 40 capturas, total 77.25 MB;
  - promedio 1.93 MB, mín 1.006 MB, máx 2.938 MB;
  - todas entre 1 y 3 MB; los JPEG pesan entre 1.85 y 2.47 MB.
- Videos de `/dev/urandom`: `grabacion_01.mp4` 300 MiB, `grabacion_02.mp4` 300 MiB y `grabacion_03.mp4` 150 MiB.
- **Desvío respecto de la SDD:** `POST /api/cases/{id}/files` no acepta los videos, porque Kestrel tiene `MaxRequestBodySize` = 30 000 000 bytes por defecto (`BadHttpRequestException: Request body too large`).
  - Las 40 capturas subieron por la API.
  - Los 3 videos se copiaron directo a `<home>/evidencia/cases/<id>/`, que es lo que enumera `StorageService.ListFilesAsync`/`GenerateAsync`. Para la generación es equivalente.
  - *Para el usuario:* un video de más de 30 MB subido por HTTP fallaría igual en cualquier perfil. Conviene revisar por qué camino llegan los videos reales (Tatana).
- Caso armado como pide §10.3:
  - perfil completo;
  - todos los obligatorios + `device.imei`;
  - roles `imei_modelo`/`nombre_dispositivo`;
  - `report-texts` en markdown, con 20 figuras en `resultados` y 10 en `conclusiones` (incluidos los `.jpg`).

**Monitoreo:**
- Cada 1 s: `memory.stat` (`anon`/`file`) por `docker exec` y `docker stats --no-stream`, con timestamps (≈ 1.2 s efectivos por vuelta).
- Además, en una corrida extra, un muestreador **dentro** del backend cada 0.2 s.

### Tabla de reposo (60 s después de healthy + 2 `wget` al frontend)

| Contenedor | `anon` máx | `docker stats` máx | `memory.peak` |
|---|---|---|---|
| mongo | 217.2 MiB (incluye los `mongosh` del healthcheck) | 196.5 MiB | 253.2 MiB |
| backend | 17.6 MiB | 33.7 MiB | 37.0 MiB |
| frontend | 31.9 MiB | 35.8 MiB | 38.6 MiB |

### Tabla de pico al generar (perfil poca RAM, caso de referencia, `mongodump` en paralelo)

**Corrida 1** (caso `bc96b3aa…`): `HTTP 200`, **70.9 s**.

| Contenedor | `anon` máx (umbral) | `file` máx | `docker stats` máx | `memory.peak` | `memory.events` |
|---|---|---|---|---|---|
| mongo | 227.5 MiB (≤ 410 ✔) | 18.0 MiB | 240.4 MiB | 257.9 MiB | oom 0, oom_kill 0 |
| backend | 240.2 MiB (≤ 614 ✔) | 712.4 MiB | 300.3 MiB | 768.0 MiB (= tope, por *page cache*) | max 9519, **oom 0, oom_kill 0** |
| frontend | 32.1 MiB (≤ 307 ✔) | 0.3 MiB | 37.1 MiB | 38.9 MiB | oom 0, oom_kill 0 |

- **Repetición a 768m** (caso 3, después del escenario OOM): `HTTP 200`, 76.2 s. `anon` máx: backend 221.6 MiB, mongo 241.9 MiB, frontend 33.8 MiB. `docker stats` máx: backend 264.6 MiB, mongo 263.1 MiB. `memory.peak` de mongo: 280.2 MiB. Sin OOM.
- **Corrida con muestreo de 0.2 s** dentro del backend (caso 5, 348 muestras): `HTTP 200`, 68.5 s, **pico de `anon` del backend = 271 925 248 B = 259.3 MiB**. Es el 34 % del tope de 768 MiB y el 42 % del umbral de 614 MiB.
- El `memory.peak` del backend llega al tope por la *page cache* de los videos, que se leen 3 veces. `memory.events max` cuenta los reclaims de esa cache. No hubo ningún `oom`/`oom_kill`: es memoria recuperable, como anticipa DT12.

**Resultado esperado (§10.3 paso 10):**
- HTTP 200 con `zip_hash` = `4e0f872a2c8fb17644fc7e18d14eff170bd93d2e0e75c49f677bfd34d28ecc99`.
- `shasum -a 256` del ZIP (861 078 592 B) da el mismo valor ✔.
- DOCX (74 443 555 B):
  - `python3 -m zipfile -t` da "Done testing" ✔;
  - **42 imágenes** embebidas: 40 capturas + 2 del documento (77 255 479 B);
  - 70 `r:embed` en `document.xml`: 40 del anexo + 30 figuras del cuerpo que reutilizan las partes.
  - Las imágenes están bajo `media/` y no `word/media/`: es la ruta que usa el OpenXML SDK, así que el criterio "≥ 40 en `word/media/`" se cumple con `media/`.
- `RestartCount` 0 → 0 y `OOMKilled=false` en los 3 contenedores.
- `oom_kill 0` en los 3 `memory.events`.
- 0 `OutOfMemoryException` en los logs del backend de la corrida a 768m.
- `mongodump --db factum --archive=/tmp/v.archive.gz --gzip` durante la generación: exit 0 (2201 B, 3 colecciones); después se hizo `rm -f`.
- **No hizo falta ningún ajuste de DT13.**

### V3.6 — "si igual se queda sin memoria"

- **Backend a 256m** (`oom.yml`, caso 2): **no falló**. `HTTP 200` en 70.8 s, `zip_hash 5753937…`, `oom_kill 0`, `RestartCount 0`, solo reclaims (`max 15676`). Con Workstation GC el heap entra en el 75 % de 256 MB. O sea: el tope de 768m tiene un margen de ~3x para este caso.
- Como 256m no reprodujo el escenario, se bajó a **128m** (caso 3), solo para la prueba. Resultado: **OOM gestionado**:
  - HTTP 400 en 72.1 s;
  - log: `fail: CaseService … Error generando informe para caso 37f0979c…` con `System.OutOfMemoryException` en `MemoryStream.set_Capacity` (el DOCX en memoria, como anticipó §4);
  - el caso quedó `status: error`;
  - backend vivo: `RestartCount 0`, `OOMKilled=false`, `oom_kill 0`;
  - **los 43 archivos sueltos siguen en la carpeta del caso**, sin ZIP ni DOCX parciales.
- Se sacó `oom.yml`, `up -d` (backend de vuelta a 805306368) y se **regeneró el mismo caso**: `HTTP 200` (76.2 s), `zip_hash f12cd23b…` = `shasum` del ZIP ✔ y el DOCX pasa `zipfile -t` ✔.
- No se observó un OOM del kernel (`OOMKilled=true`). Con .NET en contenedor, el `GCHeapHardLimit` (75 %) corta antes con una excepción gestionada.

### Limpieza (§10.3 paso 12)

- Monitores detenidos.
- `docker compose -p factum-verif … down -v`: removió `factum-verif-{mongo,backend,frontend}-1`, las redes `factum-verif_{web,datos}` y el volumen `factum-verif_mongo-data` (los creó la prueba).
- `docker image rm factum-backend:0.0.0-verif factum-frontend:0.0.0-verif`: borradas.
- Tag `mongo:7.0.43` restaurado al índice original (ver arriba).
- Se mató un `lsof` propio que había quedado colgado.
- `rm -rf` de mi carpeta de scratch (`…/scratchpad/poca`). Los demás archivos del scratchpad no eran míos y no se tocaron.
- **`docker ps -a`, `docker volume ls` y `docker images` comparados contra la foto tomada antes de empezar: idénticos.** `factum-backend-1`, `factum-mongo-1`, `factum_backend-data`, `factum_mongo-data`, `evidentia-v2-mongo-1` (27017) y `nutri-bot-db-1` siguen igual.
- No se tocó `server/src/Factum.Backend/dev-data/` ni ningún `Storage:DataDirectory` del usuario: todos los datos de la prueba vivieron en el scratch.

## Para avisar al usuario

1. **Los topes no cambian:** 512m / 768m / 384m (total ≈ 1.6 GB). El pico real del backend con el caso de referencia es ≈ 260 MiB.
2. **Tag de mongo:** el `pull` arm64 movió `mongo:7.0.43` a un índice republicado. Ya está restaurado al original; el índice arm64 nuevo quedó en el disco sin tag.
3. **Videos de más de 30 MB:** no se pueden subir por `POST /api/cases/{id}/files` (límite de Kestrel). No es de esta HU, pero conviene confirmar cómo llegan los videos reales.
4. **SDD §10.3 paso 2:** para publicar el puerto hay que usar `!override` en vez de `!reset`.
5. **Pendiente manual:** la checklist M1–M9 en la PC de 4 GB (guía §2.1).

## `git status` final

```
 M agent-ui/tsconfig.node.tsbuildinfo          (no es de esta HU)
 M backlog.json                                (orquestador)
 M deploy/windows/.env.example
 M deploy/windows/armar-paquete.sh
 M deploy/windows/docker-compose.yml
 M deploy/windows/scripts/_comun.ps1
 M deploy/windows/scripts/actualizar.ps1
 M deploy/windows/scripts/backup.ps1
 M deploy/windows/scripts/diagnostico.ps1
 M deploy/windows/scripts/instalar.ps1
 M deploy/windows/tests/comun.Tests.ps1
 M docs/instalacion-windows.md
 M progress/current.md                         (orquestador)
 M server/src/Factum.Agent/appsettings.json    (Mock local del usuario — no stagear)
?? Refactorizaciones/instalacion-poca-ram.md
?? agent-ui/tsconfig.web.tsbuildinfo           (no es de esta HU)
?? deploy/windows/docker-compose.poca-ram.yml
?? docs/hu-instalacion-poca-ram.md
?? progress/impl_backend_instalacion-poca-ram.md
```

Archivos de esta HU para el `git add`:
- `deploy/windows/{.env.example,armar-paquete.sh,docker-compose.yml,docker-compose.poca-ram.yml}`;
- `deploy/windows/scripts/{_comun,instalar,actualizar,diagnostico,backup}.ps1`;
- `deploy/windows/tests/comun.Tests.ps1`;
- `docs/instalacion-windows.md`;
- `progress/impl_backend_instalacion-poca-ram.md`;
- además de la HU y la SDD.
