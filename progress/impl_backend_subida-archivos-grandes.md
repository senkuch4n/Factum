# impl_backend — subida-archivos-grandes

**Estado:** `done`, sin commit (lo pidió el orquestador). La partición en dos commits que pide la SDD está preparada y
verificada: ver "Commits" más abajo.
**SDD:** `Refactorizaciones/subida-archivos-grandes.md`. **HU:** `docs/hu-subida-archivos-grandes.md`.
**Rama:** `feat/subida-archivos-grandes` (HEAD `d15970a`).
**Fecha:** 2026-10-02.

No se tocó `client/`, `agent-ui/`, `backlog.json`, `progress/current.md` ni `server/src/Factum.Agent/appsettings.json`.
Tampoco la MongoDB del usuario, `dev-data/` ni ningún `Storage:DataDirectory` real: los tests usan
`Path.GetTempPath()/factum-upload-<guid>` y la medición usó su propio proyecto `factum-verif` con carpeta de scratch,
ya borrados.

## Archivos

**Nuevos**

| Archivo | Qué |
|---|---|
| `server/src/Factum.Backend/Services/Cases/EvidenceUpload.cs` | `UploadErrorCodes`, `IsValidUploadName`, `Evaluate` + `UploadRejection(Kind)`, `FormatBytes` y los mensajes `error` de §5.2 |
| `server/src/Factum.Backend/Infrastructure/UploadStaging.cs` | `IDiskSpaceProbe`, `DriveInfoDiskSpaceProbe` (con `statvfs` en Linux, ver D-1), `UploadIncompleteException`, `InsufficientStorageException` y `StagedUpload` (`Commit` / `Dispose`) |
| `server/src/Factum.Backend/Controllers/DisableFormValueModelBindingAttribute.cs` | Filtro que saca los value providers de formulario del POST de subida (ver D-2) |
| `server/tests/Factum.Backend.Tests/UploadFilenameTests.cs` | T1 |
| `server/tests/Factum.Backend.Tests/EvidenceUploadTests.cs` | T2 + T4 |
| `server/tests/Factum.Backend.Tests/UploadStorageTests.cs` | T3 (1-11) |

**Modificados**

| Archivo | Qué |
|---|---|
| `Infrastructure/StorageService.cs` | `StorageOptions.MaxUploadBytes`/`MinFreeBytes` con defaults en código. Constructor público (DI) e interno (tests). `_uploadTmp`, `GetAvailableFreeBytes`, `StageUploadAsync` (buffer de 1 MiB de `ArrayPool`, SHA-256 incremental, lectura y escritura en `try` separados, clasificación de disco lleno, verificación de longitud, `fsync`, `finally` que borra) y `CleanupOrphanUploads`. **`SaveFileAsync` eliminado** (grep de usos: 0; `File.Create` ya no aparece en el backend) |
| `Common/Result.cs` | `ErrorKind` + `PayloadTooLarge`, `InsufficientStorage`, `ServerError` **al final**; `Details`; sobrecarga `Fail(kind, error, details)` (también en el `Result` estático); `Cast` propaga `Missing` y `Details` |
| `Controllers/ResultHttpExtensions.cs` | Sin `Details`: los 5 brazos de antes quedan **idénticos** (solo se suman 3 brazos para los kinds nuevos, que antes no existían). Con `Details`: diccionario `{ error[, missing], ...Details }` + 404/409/413/507/500/400; `Forbidden` sigue con `Forbid()` |
| `DTOs/CaseDtos.cs` | `UploadCheckResponse(long MaxUploadBytes)` |
| `Services/Cases/CaseService.cs` | Inyecta `IOptions<StorageOptions>`. `CheckUploadAsync` (interfaz + impl). Helper `PrecheckUploadAsync<T>` con los pasos 1-6 de §4.1 en orden. `UploadFileAsync` con la firma nueva (`long? contentLength`) y el flujo de §5.5: catch de todo, revalidación de editable y `Commit` |
| `Controllers/CasesController.cs` | Inyecta `IOptions<StorageOptions>`. `UploadFile`: fija `IHttpMaxRequestBodySizeFeature` antes de tocar el cuerpo, pasa `Request.ContentLength` y suma `[DisableFormValueModelBinding]` y `ProducesResponseType` 400/409/413/500/507. Nuevo `UploadCheck` (`size` parseado a mano con `NumberStyles.None`, `Cache-Control: no-store`) |
| `Program.cs` | Valida los 2 valores y los suma a `configErrors` (DT11). Registra `IDiskSpaceProbe` como singleton. Al arrancar llama a `CleanupOrphanUploads()` y loguea la línea "Subidas: …" |
| `README.md` | 2 filas de config + nota de `.upload-tmp/` |

## Verificación

**Build:** `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` → **0 errores, 4 advertencias**. Son las mismas
4 `NU190x` de paquetes que había antes del cambio; no hay advertencias nuevas.

**Tests:** `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` → **406/406 en verde**: 335
existentes + 71 nuevos.

- Las 3 advertencias del proyecto de tests (`EvidenceZipTests` CS8714/CS8619 y `ReportBodyImagesDocxTests` xUnit1031)
  ya estaban; los archivos nuevos no generan ninguna.
- T3 cubre los 11 casos de §11. Extras:
  - el disco lleno se prueba en las 3 variantes;
  - el nombre que escapa se prueba con `../x`, `../../x`, `sub/x` y `""`, y en todos se verifica que no quede **ningún**
    archivo en el root;
  - la limpieza de huérfanos no entra en una subcarpeta de `.upload-tmp/` (sin recursión).

## Medición §12.1 (Mac Apple Silicon, Docker Desktop 10 CPU / 7,75 GiB)

**Montaje**

- Proyecto `factum-verif`:
  - `deploy/windows/docker-compose.yml` + `docker-compose.poca-ram.yml` + override de scratch;
  - backend con `mem_limit` 805306368 y `DOTNET_gcServer=0`;
  - puerto `!override ["127.0.0.1:18080:8080"]`.
- `.env` de scratch, con `FACTUM_HOME` en el scratchpad.
- Se levantaron **solo `mongo` y `backend`**: el frontend no hacía falta.
- **Mongo:** el override usa `image: mongo:7` (ya local, arm64) para no tocar el tag `mongo:7.0.43` (ver lo que pasó en
  `impl_backend_instalacion-poca-ram`). No se hizo ningún `pull`.
- No se tocaron `evidentia-v2-mongo-1` ni el 27017.
- Antes de empezar se sacó una foto de `docker ps -a`, `volume ls`, `images` y `network ls`.
- Monitoreo: `memory.stat` (`anon`/`file`) + `memory.current` del cgroup del backend cada ~1 s.

| # | Resultado |
|---|---|
| V1 | Ver **D-1**. Con `DriveInfo` puro: "espacio libre en /data: **2946,8 GB**", con el host en 11,5 GB (`df` dentro del contenedor: 12 070 064 KiB). Después del arreglo: "Subidas: tope 4 GB, margen libre 1 GB, espacio libre en /data: **11,4 GB** (temporales huérfanos borrados: 0)", coincide con el `df` del host ✔ |
| V2 | Ver tabla abajo. Las tres subidas → 200 con `hash` = `shasum` del origen = `shasum` del archivo en `cases/<id>/`, y `.upload-tmp/` vacío. `memory.events`: `oom 0`, `oom_kill 0`; `RestartCount 0`, `OOMKilled=false` ✔ |
| V3 | `truncate -s 4294967297` (sparse). `upload-check` → `413 {"error":"El archivo big.mp4 pesa 4 GB y el máximo permitido es 4 GB","code":"file_too_large","size":4294967297,"max_upload_bytes":4294967296}`, con `Access-Control-Allow-Origin: *` y `Cache-Control: no-store`. `curl -T` → 413, el mismo JSON con CORS, en **4 ms** y con `size_upload` = 0 (curl mandó `Expect: 100-continue` y no subió nada). Nada en `cases/<id>/` ni en `.upload-tmp/` ✔. `upload-check` con `size` = 4294967296 → `200 {"max_upload_bytes":4294967296}`; con `size=abc`, sin `size` o con `size=-1` → 400 `length_required` ✔ |
| V4 | Probados `../x.bin`, `..%2Fx.bin`, `..%2F..%2F..%2Fx.bin`, `a:b.bin`, `CON`, `..%5Cx.bin`, `x.bin.` y `%2Fetc%2Fpasswd` → POST y `upload-check` dan **400 `invalid_filename`**. `find <home> -newer <marca>` solo mostró `.upload-tmp/` (mtime del directorio): **nada** escrito ✔. Esta prueba destapó **D-2** |
| V5 | Caso generado → POST 409 `{"error":"El caso ya fue generado y no se puede editar","code":"case_not_editable"}`; `upload-check` → 409 igual ✔ |
| V6 | `/data` en `tmpfs` de 200m, `MinFreeBytes` default. Log: "espacio libre en /data: 200 MB". `upload-check` de 50 MB → `507 {"error":"No hay espacio en el disco del servidor para guardar v.mp4 (hace falta 1 GB, quedan 200 MB)","code":"insufficient_storage","size":52428800,"required_bytes":1126170624,"available_bytes":209715200}` con CORS. El POST también da 507 en 21 ms y con `size_upload` = 0. Nada escrito ✔ |
| V7 | `tmpfs` de 200m con `Storage__MinFreeBytes=0` (log: "margen libre 0 B"). **Desvío:** un único archivo de 300 MB lo frena el prechequeo, porque 300 MB > 200 MB aun con margen 0 (verificado: 507 por prechequeo). Para forzar el disco lleno **a mitad** se lanzaron **2 subidas concurrentes de 150 MB** (`--limit-rate 30M`): cada una pasa el prechequeo y juntas no entran. Las dos → **507 `insufficient_storage`** a mitad de escritura (`available_bytes: 0`, log `Warning` "Disco lleno subiendo al caso …" con `InsufficientStorageException`). curl recibió el JSON. `/data/cases/<id>` no existe, `.upload-tmp/` vacío y `df /data` en 0 usado ✔. Se dejó el `mem_limit` (768m): el tmpfs de 200m entró sin problema |
| V8 | `curl -T` de 1 GB con `--limit-rate 20M` y `kill -9` del **curl** a los 5 s: había ~100 MB en `.upload-tmp/`. 5 s después: `.upload-tmp/` vacío, nada en `cases/<id>/` y log `Information` "Subida incompleta al caso … (corte.mp4): 105054208 de 1073741824 bytes" (el corte llega como fallo de lectura). Reintento completo → 200, hash `e75c6238…` = origen ✔. *Nota:* en el primer intento el `kill` le pegó al `bash` envoltorio y el `curl` siguió solo hasta terminar (200 a los 51 s). Eso mostró además que dos subidas concurrentes del mismo nombre terminan bien: la última reemplaza y las dos están completas |
| V9 | (a) `docker restart` a mitad: antes había 1 `.part`. Después del arranque: `.upload-tmp/` vacío, log "**temporales huérfanos borrados: 1**", nada en `cases/<id>/` ✔. El apagado ordenado no llegó a borrarlo, por eso lo limpió el arranque. (b) Variante `docker kill -s KILL` + `docker start`: 1 `.part` de ~105 MB quedó después del kill; al arrancar, "temporales huérfanos borrados: 1" y vacío ✔ |
| V10 | `rep.bin` A (200 MB) → 200. Subida de B con `--limit-rate 20M`: a mitad (84 MB en tmp) el final es A. Se corta → sigue A y no hay tmp. B completo → final = B, sin tmp ✔ |
| V11 | Caso nuevo: `device_pull_20261002_101010.mp4` (1 GiB) subido por la API con `source_path` → 200 en 10,5 s. Más una captura PNG con rol `imei_modelo` y textos. `generate` en el perfil poca RAM → **200 en 113 s**, `status: completed`. `zip_hash` `20c4a992…` = `shasum` del ZIP. `python3 -m zipfile -l`: `captura_imei.png` y `device_pull_…mp4` (1073741824 B), **0 `.part`/`upload-tmp`**. El hash del video (`e75c6238…`) aparece en el DOCX. Memoria al generar: `anon` máx **40,9 MiB** (32,1 en reposo); `file` hasta 730 MiB (page cache) ✔ |
| V12 | Chrome 154.0.8037.97 (Playwright, `channel: "chrome"`, headless), backend con `Storage__MaxUploadBytes=104857600`, página servida desde `http://127.0.0.1:18081` (origen cruzado real). XHR POST directo de un `Blob` de 200 MB → **llegó el 413 JSON** (`onload`, status 413, body con `code: file_too_large`) a los 461 ms, con ~9,4 MB del cuerpo enviados. Control: 50 MB → 200. *Informativo:* desde `about:blank` (origen opaco) Chrome corta la petición al loopback antes de enviarla (status 0 en 3 ms, aun con 50 MB): es una restricción del navegador, no del backend |
| V13 | `down -v` del proyecto `factum-verif`: contenedores, redes `factum-verif_{web,datos}` y volumen `factum-verif_mongo-data`. `docker image rm factum-backend:0.0.0-verif`. Las imágenes intermedias de mis rebuilds (`122da890…`, `123629f5…`) ya no existen. Fotos después: `ps -a`, `volume ls` y `network ls` **idénticos**. En `images` aparecen `factum-backend:1.0.0` (`e5f50fd8`, creada 01:21 hora local) y `factum-frontend:1.0.0` (`4d08fba9`, 01:37) que **no armé yo** y no figuraban en la foto inicial: las taggeó otro proceso durante la prueba. No las toqué. Scratch propio (`scratchpad/subida/`, `upload.cs.txt`) borrado. El `http.server` de V12 y los monitores, detenidos |

**Tabla V2.** Perfil poca RAM, `curl -T` contra 127.0.0.1:18080 (bind mount virtiofs).

| Tamaño | HTTP | Tiempo | `anon` reposo → máx | `file` máx | `memory.current` máx |
|---|---|---|---|---|---|
| 150 MiB | 200 | 1,2 s | 23,5 → 28,9 MiB | 84,7 MiB | 115,7 MiB |
| 1 GiB | 200 | 9,7 s | 28,9 → 30,9 MiB | 731,6 MiB | 768 MiB (tope, por page cache) |
| 2 GiB | 200 | 16,8 s | 30,9 → 33,9 MiB | 728,8 MiB | 768 MiB (tope, por page cache) |

- **Criterio "pico de `anon` < reposo + 64 MiB": cumplido** (+5,4 / +2,0 / +3,0 MiB). La RAM no depende del tamaño del
  archivo.
- `memory.current` llega al tope por la page cache (`memory.events max 30029` son reclaims), igual que en la medición
  de la HU anterior. **0 OOM.**
- **Desvío:** se midieron 150 MiB, 1 GiB y **2 GiB, no 3 GB**. El host tenía 11-13 GiB libres al empezar (llegó a bajar a
  4,9 GiB por algo ajeno a esta prueba: mi scratch nunca pasó de ~3 GiB) y cada prueba ocupa 2× el tamaño (origen +
  copia). El orquestador pidió 1-2 GB.

## Decisiones no obvias / desvíos respecto de la SDD

**D-1. `DriveInfo` sobreestima ×256 el espacio libre en el bind mount de Docker Desktop.** Lo detectó V1. Es el riesgo
que la SDD dejaba para M4.

- Dentro del contenedor, `stat -f /data` da `Block size: 1048576` y `Fundamental block size: 4096`.
- .NET calcula `AvailableFreeSpace = f_bavail × f_bsize` en vez de `f_frsize`.
- Sin el arreglo, el prechequeo de espacio **nunca** se dispararía en el despliegue con Docker Desktop.

Arreglo dentro de la misma clase (`DriveInfoDiskSpaceProbe`, mismo nombre que la SDD):

- en Linux 64 bits llama a `statvfs(3)` por P/Invoke (`libc.so.6`, con fallback a `libc`) y usa
  `f_bavail × f_frsize`;
- en el resto, o si falla, usa `DriveInfo` como antes;
- ante cualquier error devuelve `null` (fail-open, como pide la SDD).

Verificado en V1: 11,4 GB contra 11,5 GB del `df`. T4 (en la Mac) sigue pasando por `DriveInfo`.

**M4 sigue siendo necesario en Windows:** el bind de Docker Desktop en Windows es otro mecanismo y hay que confirmar
que la línea "Subidas: … espacio libre …" coincide con el espacio libre de `C:`.

**D-2. `[DisableFormValueModelBinding]` en el POST.** Lo detectó V4. Con `Content-Type: application/x-www-form-urlencoded`
(el default de `curl --data-binary`) o `multipart/form-data`, el `FormValueProviderFactory` de MVC lee el cuerpo como
formulario antes del action, y el staging recibía 0 bytes. La verificación de longitud lo atrapaba ("0 B de 5 B; no se
guardó nada"), pero la SDD dice "el `Content-Type` se ignora".

- El filtro (patrón estándar de ASP.NET para streaming) saca los value providers de formulario solo en ese action.
- Verificado: form-urlencoded, multipart y `video/mp4` dan el mismo hash.
- Además evita que un form de GB se lea a memoria.
- El front (XHR con `Blob`) no usa esos tipos, así que no lo afectaba.

**D-3. `StorageService` recibe además `ILogger<StorageService>`** en el constructor público. La SDD pide loguear
`Warning` cuando no se puede borrar un temporal, y para eso hace falta el logger. El constructor interno lo recibe
opcional (`NullLogger` en tests).

**D-4. Nombres reservados de Windows: se mira lo que hay antes del primer punto**, no solo `GetFileNameWithoutExtension`.
Así también se rechaza `aux.tar.gz`, que Windows igual reserva. Es más estricto que la SDD; los casos de T1 dan lo
mismo. `CONSOLA.txt` sigue siendo válido (está en el test).

**D-5. Si `AddFileSourceAsync` falla *después* del commit,** el archivo ya quedó publicado y completo. Se responde 500
`storage_error` con un mensaje honesto: "El servidor guardó {name} pero no pudo registrar su ruta de origen; volvé a
enviarlo". No se usa el genérico "no se guardó nada". Reenviar reemplaza con el mismo contenido y registra la ruta. La
relectura del caso y `AddFileSourceAsync` van con `CancellationToken.None`, para que un corte del cliente justo al final
no deje el archivo sin su `source_path`.

**D-6. Excepciones fuera del staging** (por ejemplo, Mongo caído en `LoadOwnedAsync`): el `try` envuelve todo
`UploadFileAsync`, así que también salen como 500 `storage_error` JSON con CORS (§4.2 "ninguna excepción sale del
action"). `CheckUploadAsync` no envuelve (no escribe nada); se comporta como el resto de los GET.

**D-7. `filename` vacío en el POST** (`?filename=`): el model binding lo convierte en `null`, así que aplica el default
`file_yyyyMMdd_HHmmss` de siempre (§4.2). En `upload-check`, vacío o ausente → 400 `invalid_filename` (§4.1).

**D-8. La línea de arranque formatea `Free` con `FormatBytes`** ("11,4 GB"). Por encima de 1024 GB sigue en GB (la SDD
no define TB): por ejemplo, "2946,8 GB".

**D-9. Mensaje del borde exacto:** con 1 byte más que el tope, el `error` dice "pesa 4 GB y el máximo permitido es 4 GB"
por el redondeo a un decimal. Sigue las reglas de §5.2 y el front arma su propio texto con los números del JSON. Es
cosmético, no se cambió.

**D-10. `StorageService` crea `cases/` en el constructor, pero no `.upload-tmp/`.** Así T3-10 ("root sin `.upload-tmp/`
→ devuelve 0 y la crea") tiene sentido. `StageUploadAsync` la crea antes de abrir el temporal y `CleanupOrphanUploads`
la crea al arrancar.

**D-11. Defensa en profundidad:** `StageUploadAsync` también valida que el temporal quede dentro de `.upload-tmp/` (el
`caseId` viene de la ruta, aunque ya pasó por `LoadOwnedAsync`). Las dos validaciones de ruta corren **antes** de crear
cualquier carpeta o archivo.

## Contrato compartido — confirmación

Todo coincide con §7 de la SDD, verificado por HTTP real en V3-V7 y V12:

- `GET /api/cases/{id}/files/upload-check?filename=&size=` → `200 {"max_upload_bytes":N}`, con `Cache-Control: no-store`.
- `POST /api/cases/{id}/files?filename=[&source_path=]` → `200 {"name","size","hash","modified_at","source_path"}`, sin
  cambios.
- Errores: `{"error", "code", …}` con claves snake_case literales:
  - 400 `invalid_filename` / `length_required` / `incomplete_upload` (+ `size`, `received_bytes`);
  - 409 `case_not_editable`;
  - 413 `file_too_large` (+ `size`, `max_upload_bytes`);
  - 507 `insufficient_storage` (+ `size`, `required_bytes`, `available_bytes` número o `null`);
  - 500 `storage_error`.
- 404/403 como hoy. CORS presente en 413/507/400/409.
- Config `Storage:MaxUploadBytes` (4294967296) y `Storage:MinFreeBytes` (1073741824).
- Formato de tamaños: se comparó con `client/src/lib/format.ts` del implementer-frontend (en el working tree). Mismas
  reglas: base 1024, `B`/`KB`/`MB`/`GB`, un decimal con coma y sin `,0`.
- Los endpoints existentes sin `Details` conservan `{ error }` / `{ error, missing }`.

## Commits (para el orquestador)

La SDD pide que el fix del `filename` vaya **solo en el primer commit**. Está preparado como parche y verificado en un
`git worktree` temporal desde `d15970a` (ya removido). Con ese estado: build 0 errores, **371/371 tests** (335 + los 36
de T1).

- **Parche:** `/private/tmp/claude-501/-Users-joelmiguelserrudo-Documents-Projects-Factum/d331c5ef-f445-4af8-98b0-4d017b56de51/scratchpad/commit1-filename.patch`.
- **Contenido:**
  - `EvidenceUpload.cs` completo (el resto de sus funciones no se usa todavía);
  - `UploadFilenameTests.cs`;
  - 4 líneas en `CaseService.UploadFileAsync`: `IsValidUploadName` → `Result.Invalid` antes de cualquier I/O, solo
    `{ error }`.

**Commit 1:** `git apply --cached <parche>` y después `git commit`. Toca solo el índice; el working tree ya tiene el
superconjunto.

**Commit 2:** `git add` de los backend restantes:

- `server/src/Factum.Backend/{Common/Result.cs, Controllers/CasesController.cs, Controllers/ResultHttpExtensions.cs, Controllers/DisableFormValueModelBindingAttribute.cs, DTOs/CaseDtos.cs, Infrastructure/StorageService.cs, Infrastructure/UploadStaging.cs, Program.cs, Services/Cases/CaseService.cs}`;
- `server/tests/Factum.Backend.Tests/{EvidenceUploadTests.cs, UploadStorageTests.cs}`;
- `README.md`.

**No stagear** `server/src/Factum.Agent/appsettings.json` ni los `*.tsbuildinfo`.

## Pendiente para el usuario (prueba manual, §12.3)

- **M4 es crítico por D-1:** en la PC Windows, comparar la línea "Subidas: … espacio libre en /data: X" de
  `docker logs` con el espacio libre de `C:`. Si no coincide, avisar.
- M1-M3 y M5 como dice la SDD.
