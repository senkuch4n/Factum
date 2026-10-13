# QA de robustez — agente local Tatana (Factum.Agent)

- Fecha: 2026-10-13 · Rama: `develop` (36140d4) · Proyecto: `server/src/Factum.Agent`
- Instancia propia: copia del fuente compilada en el scratchpad (`dotnet build -o scratchpad/agentbin`, para
  no recompilar el `bin/` que usa la instancia real), corrida con
  `./Factum.Agent --mock --port 8766 --data <scratch>/tatana-qa-data/data --local-config <inexistente>` y
  `Agent__EvidenceDirectory=<scratch>/tatana-qa-data/evidencia`. No toqué la instancia real en :8765
  (pid 8699, que siguió corriendo), ni `~/Factum/Evidencia`, ni el `agent-data` real, ni ningún código de
  producción.
- Herramientas: curl, sockets crudos en Python (HTTP y WebSocket), ffmpeg (medios de prueba), 7zz y
  `zipfile` de Python (verificación de ZIP), y `hdiutil` (volumen de 12 MB para provocar disco lleno de verdad).
- Scripts para reproducir: `scratchpad/qa/tatana-scripts/` (`start.sh`, `raw.py`, `wslib.py`, `ws_*.py`, `pp.py`, `zipcase.py`, `queue.sh`).

## Resumen

Tatana aguanta bien casi todo el abuso de entrada. No encontré ningún path traversal: probé `../`, `%2e%2e`,
`%5c`, rutas absolutas, nombres reservados de Windows, unicode, nombres de 255+ caracteres y caseId que no son
Guid, en GET, DELETE, import, upload, zip y commit. La subida es atómica en todos los cortes que probé: el cliente
se corta, `Content-Length` miente, el proceso muere con `kill -9` o el disco se llena de verdad a mitad de la
escritura. En ningún caso quedó un archivo final a medias, y `.upload-tmp` se limpia al arrancar. El ZIP con
Stored/Deflate y AES-256 abre bien en 7-Zip, los hashes por entrada coinciden y la verificación interna detecta
la evidencia alterada entre el hash y el zip. El post-proceso de video cumple el contrato: exactamente un estado
terminal por variante en remove, mantenimiento, apagado, timeout, archivo inválido y duración excesiva.

Lo que importa arreglar:

1. **ALTA — un cliente WebSocket que no lee bloquea Tatana.** `BroadcastAsync` espera cada `SendAsync` en serie
   y sin timeout. Cuando se llena el buffer TCP de un cliente que no lee (unos 600 KB, unos 3.000 eventos),
   quedan colgadas todas las requests que emiten eventos (stop/start de grabación, captura de pantalla, armado
   del ZIP). El agente queda `busy` y `POST /agent/maintenance` responde 409, o sea que la actualización
   automática no puede entrar. Se recupera recién cuando ese cliente se desconecta.
2. **MEDIA — apagado de 30 s con cualquier WebSocket abierto.** El loop de lectura del hub no escucha
   `ApplicationStopping`, así que SIGTERM espera el `ShutdownTimeout` de 30 s. Sin clientes, el apagado tarda
   0,1 s. El front siempre deja un WS abierto, así que esto le pega a cada reinicio o actualización.
3. **MEDIA — DNS rebinding contra los GET.** No se valida el header `Host` (`AllowedHosts: "*"`) y los GET sin
   `Origin` pasan. Una página que use DNS rebinding puede leer `/files`, bajar grabaciones y capturas, y leer
   `/devices` (IMEI), `/devices/{serial}/explorer` (archivos del celular) y `/health` (rutas). Los POST/DELETE y
   el WS sí quedan protegidos, porque el navegador manda `Origin` en esos casos.
4. **MEDIA — un `appsettings.Local.json` con JSON válido pero tipos o valores inválidos tumba Tatana al
   arrancar.** Por ejemplo `"MaxUploadBytes":"16GB"` o un origen con path. Va en contra de D-T16, que pide
   ignorar el archivo y avisar en `/agent/state.local_config.error`; eso solo pasa con JSON sintácticamente
   roto.

El resto son hallazgos bajos o informativos: códigos de error genéricos, `.part` huérfano en la carpeta de
evidencia después de un crash, carpetas vacías que quedan tras un ZIP fallido, `ffmpeg` huérfano tras
`kill -9`, contraseñas de 100 caracteres o más que 7-Zip no abre, `password: ""` que termina en 500, y cuerpos
`x-www-form-urlencoded` que se descartan.

## Tabla de hallazgos

| ID | Sev. | Endpoint / área | Reproducción mínima | Esperado | Obtenido | Archivo:línea probable |
|---|---|---|---|---|---|---|
| T-01 | **Alta** | `/ws` + toda request que emite eventos (`POST /devices/{s}/record/start\|stop`, `/screenshot`, `POST /cases/{id}/zip`, polling de dispositivos, post-proceso) | 1) Abrir un WS con `SO_RCVBUF=1024` y no leer nunca. 2) Generar unos 3.000 eventos (500 B a 1 KB) con start/stop de grabación mock. 3) Pedir `POST /cases/{id}/zip` y `POST /devices/EMU001/screenshot`. Ver `ws_stall.py` / `ws_stall_zip3.py`. | Un cliente lento o muerto se descarta (timeout por envío o cola acotada por cliente) y no frena a los demás. | El stop de la grabación quedó colgado más de 300 s. El ZIP no respondió en 20 s, la captura tampoco en 10 s. `/agent/state` quedó en `busy:true` (`POST /devices`) y `POST /agent/maintenance` respondió **409 agent_busy**, lo que bloquea la auto-actualización. Los demás clientes WS dejaron de recibir eventos. Todo se destrabó al cerrar el cliente lento. | `WebSockets/AgentWebSocketHub.cs:72-89` (envío secuencial con `await ws.SendAsync(..., ct)` sin timeout ni cola por cliente). Los controllers pasan el `ct` de la request (`RecordingController.cs:27,39,83`), así que la request se libera solo cuando *su* cliente HTTP aborta; `ZipProgress` (`CaseZipService.cs:425-433`) y el polling (`Program.cs:319`) no tienen ningún límite. |
| T-02 | Media | Ciclo de vida / `/ws` | Abrir 1 WS y mandar `kill -TERM <pid>` (`shutdown_time.py`). | Cierre en menos de 1 s, con un close frame enviado a los clientes. | **30,1 s** de apagado con 1 cliente WS y 0,1 s sin clientes. El cliente no recibe close frame en esos 30 s, y el polling de dispositivos sigue logueando durante el apagado. | `AgentWebSocketHub.cs:55-60` (`ReceiveAsync` con `RequestAborted`, que no se cancela con `ApplicationStopping`). `Program.cs:306-312` cancela el post-proceso pero no cierra los WS. `Program.cs:319` usa `while(true)` sin token. |
| T-03 | Media | Todos los GET (`/files`, `/files/{f}`, `/devices`, `/devices/{s}/explorer`, `/health`, `/cases/{id}/files`, `/cases/{id}/zip/file`) | `curl -H "Host: rebind.attacker.example:8766" http://127.0.0.1:8766/files` → 200 con el listado. Lo mismo con `/devices` (IMEI) y `/cases/{id}/zip/file` (devolvió el ZIP completo). | Rechazar un `Host` que no sea `localhost`, `127.0.0.1` o `[::1]` (HostFiltering) y pedir un header propio en los GET sensibles. | 200 con datos. En un ataque de DNS rebinding el GET same-origin no lleva `Origin` y la guarda lo deja pasar. Lo mitiga en parte Local Network Access de Chrome reciente, pero no Safari ni Firefox. | `appsettings.json` (`"AllowedHosts": "*"`), `Program.cs:184-206` (sin `Origin` todo pasa). |
| T-04 | Media | Arranque / `--local-config` | `appsettings.Local.json` = `{"Agent":{"MaxUploadBytes":"16GB"}}` o `{"Agent":{"AllowedOrigins":["http://localhost:3000/app"]}}` | Ignorar el archivo y reportarlo en `/agent/state.local_config.error` (D-T16), como ya pasa con JSON roto. | Tatana aborta al arrancar (`Abort trap: 6`, `InvalidOperationException` del binder o "Configuración inválida, Tatana no arranca"). Con JSON sintácticamente roto sí arranca y lo reporta bien. | `Common/LocalConfigInspector.cs` (solo valida sintaxis), `Program.cs:59-80` y `:107-120` (binder y `OriginPolicy` tiran sin aislar la fuente local). |
| T-05 | Baja | `POST /cases/{id}/zip` (crash) | `kill -9` del agente con el ZIP a mitad de la escritura y después rearrancar. | Al arrancar se limpian los `.part` de `<EvidenceDirectory>/*/.factum/pendiente/`. | Queda `evidencia_X.zip.part` (117 MB en la prueba, puede ser de GB) hasta que alguien vuelva a armar el ZIP de **ese** caso. `zip/status` dice `none`, así que nadie se entera. | `Program.cs:272-283` (limpia `.upload-tmp` y `.postprocess` pero no `pendiente/`), `CaseZipService.cs:192`. |
| T-06 | Baja | `POST /cases/{id}/zip` (falla) | Pedir un ZIP con un archivo inexistente, o con `case_ref` arbitrario (`"../../../etc"`), o con 507. | No crear carpetas si el ZIP falla antes de escribir. | Quedan carpetas vacías `etc_99999999/.factum/pendiente/` y `X_11111111/...` en la carpeta de evidencia: una por cada combinación `case_ref` × intento fallido. Cualquier proceso local puede crear carpetas con el nombre que quiera (saneado). | `CaseZipService.cs:139` (`CreateDirectory` antes del chequeo de espacio y de la verificación de hashes). |
| T-07 | Baja | `POST /cases/{id}/zip` | 1) Archivo borrado entre el hash y el zip. 2) Disco lleno a mitad del zip (volumen de 12 MB con exactamente `total` bytes libres y `MinFreeBytes=0`). | 409 `evidence_changed` (missing) y 507 `insufficient_storage`. | En los dos casos 500 `zip_failed` con mensaje genérico. La evidencia queda intacta y el `.part` se borra, eso está bien. El chequeo previo de espacio no suma el overhead de ZIP/AES. | `CaseZipService.cs:149` y `:213-224` (todo lo que no es verificación se mapea a 500). |
| T-08 | Baja | `POST /cases/{id}/zip` | `"password": ""` | 400 de validación, o tratarlo como sin contraseña. | 500 `zip_failed`. SharpZipLib convierte `""` en null y después tira "The Password property must be set…". El backend siempre manda 16 caracteres, así que solo lo dispara un cliente directo. | `EvidenceZip.cs:79` / `CaseZipService.cs:116` (sin validar). |
| T-09 | Baja | `POST /cases/{id}/zip` | Contraseña de 100 caracteres ASCII o más. | Rechazar más de 99 bytes UTF-8, o avisar. | Tatana arma y **verifica OK** (con SharpZipLib), pero `7zz t -p<pw>` da *Wrong password*. 7-Zip trunca las contraseñas WinZip-AES a 99 bytes. Con 99 caracteres anda, con 100 ya no. El generador del backend usa 16, así que hoy no se dispara. | `EvidenceZip.cs:79` (sin tope). |
| T-10 | Baja | `POST /cases/{id}/evidence/upload`, `POST /agent/maintenance` | `curl -X POST --data-binary 'hello' ".../evidence/upload?filename=a.txt"` (curl pone `application/x-www-form-urlencoded`). | Leer el cuerpo crudo con cualquier `Content-Type`. | 400 `incomplete_upload` "0 B de 5 B". Con `multipart/form-data` da un 400 ProblemDetails. En maintenance, `curl -d '{"ttl_seconds":15}'` sin header ignora el TTL y usa 120 s. Con `application/octet-stream` o JSON anda. Un navegador manda `blob.type`, que nunca es form, así que el riesgo es para scripts y herramientas. | `CaseEvidenceController.cs:46-60` y `AgentStateController.cs:46-56`: el model binding de `[ApiController]` lee el form antes que la acción (faltaría `[DisableFormValueModelBinding]` o quitar los `FormValueProviderFactory`). |
| T-11 | Baja | Post-proceso (crash) | `kill -9` del agente con `ffmpeg minterpolate` corriendo y rearrancar (`pp.py kill9`). | El hijo muere con el padre, o se mata al arrancar. | `ffmpeg` sigue vivo como huérfano hasta terminar el video, unos 45 s en la prueba y hasta el `TimeoutMaxSeconds` (2 h) con grabaciones largas, gastando CPU. En macOS el rearranque borra `.postprocess` igual. En Windows el handle abierto haría fallar `CleanupWorkDirectory` (no lo verifiqué en Windows). | `Common/ProcessRunner.cs:68-112` (sin Job Object ni process group), `VideoPostprocessService.cs:337`. |
| T-12 | Baja | `POST /cases/{id}/evidence/upload` | Dos subidas simultáneas con el mismo `filename` y distinto contenido. | Una gana y la otra recibe 409, o las dos se serializan. | **Las dos reciben 200**, cada una con *su* sha256, y en disco queda solo una. El cliente que perdió cree que su archivo está guardado. Más adelante lo detecta el ZIP (`evidence_changed`). Pisar es el comportamiento especificado (SDD §6.2, `overwrite: true`); lo que falla es el reporte del perdedor. | `CaseEvidenceStore.cs:273`. |
| T-13 | Baja | Nombres de archivo | Subir `‮gpj.exe` (U+202E), nombres con U+0085, ZWJ o NBSP. | Rechazar los controles bidi y C1, como ya se hace con los `< 0x20`. | Se aceptan y se guardan. El RTL override hace que en el explorador o la UI se vea `exe.jpg` (spoofing del nombre de la evidencia). | `Common/AgentFileNames.cs:37`. |
| T-14 | Baja | `POST /devices/{s}/record/stop` sin grabación activa | `POST /devices/mock-ios-udid-001/record/stop?platform=ios` dos veces. | 409 con `code`. | 500 `{"error":"Sin grabación iOS activa"}`. En general los controllers de dispositivo devuelven `ex.Message` crudo con 500. | `Controllers/RecordingController.cs:52-56,102-105`. |
| T-15 | Info | `POST /cases/{id}/evidence/upload` | 20 subidas concurrentes de 20 MB. | Todas 200. | 5 corridas de 20: todas 200 y 20 archivos con el hash correcto en 1,25 s. **Solo en la primera corrida** (en frío) 4 de 20 terminaron con código `100` en curl y sin archivo, y sin `.part` residual. No lo pude reproducir después. Posible interacción `Expect: 100-continue` con el arranque en frío de Kestrel: conviene vigilarlo. | — |
| T-16 | Info | `GET /files/{f}` | `GET /files/device_pull_x.html` | Lo mismo que `/cases/{id}/files/{f}`: `nosniff` + `CSP sandbox`. | Va sin `nosniff` ni CSP. Lo mitiga `Content-Disposition: attachment` (no se renderiza). El pull de dispositivo conserva la extensión que trae el celular (`.html`, `.svg`). | `Controllers/FilesController.cs:16-25`. |
| T-17 | Info | Seguridad local | Cualquier proceso local (o cualquier otro usuario de la misma PC) sin `Origin` puede listar, bajar y borrar evidencia, poner Tatana en mantenimiento indefinidamente (renovando el TTL) o abrir un WS. | — | Es así por diseño (localhost sin auth). Conviene documentarlo para PCs compartidas. | `Program.cs:184`. |
| T-18 | Info | 413 en upload-check | `MaxUploadBytes` = 16 GiB y un tamaño de 16 GiB + 1. | Mensaje claro. | "pesa 16 GB y el máximo es 16 GB" (redondeo engañoso). | `CaseEvidenceStore.cs:192-195`. |

## Lo que pasó bien

**Seguridad local y CORS**
- Un `Origin` ajeno, `null`, `http://localhost:3000.evil.com` o una lista separada por comas dan 403 en GET, OPTIONS y WS.
- El preflight con `Access-Control-Request-Private-Network: true` desde un origen permitido devuelve `Access-Control-Allow-Private-Network: true` y, desde uno ajeno, 403 sin headers CORS.
- `Origin` con `/` final o en mayúsculas se acepta, y está bien que así sea.

**Path traversal (todo en 400/404, nada afuera de la carpeta)**
- GET y DELETE de `/files/{f}` y de `/cases/{id}/files/{f}` con `..%2F`, `%2e%2e%2f`, `..%5c`, `%252e`, `/etc/passwd`, `.`, `..`, `%00`, nombres de 256+ y de 100 KB (414).
- `evidence/import` con `../`, `..\\`, absolutos, `cases`, `.upload-tmp`, vacío y null.
- `upload?filename=` con todo lo anterior más `CON`, `aux.mp4`, `LPT1.mp4`, `a:b`, `evidencia_*.zip` e `informe_*`.
- `zip_filename` con traversal, `case_ref` con `../../../etc` (se sanea a `etc_<id>`) y `delete_files` del commit con `../../SENTINEL.txt` o `/etc/hosts`, que se ignoran.
- Un caseId no-Guid, sin guiones, con llaves o con `%2F..` da 400 `invalid_case_id`; uno en mayúscula se canoniza.
- El archivo centinela de afuera nunca se tocó.

**Subidas**
- Tamaño 0: OK, con el hash de vacío.
- Chunked o sin `Content-Length`: 400 `length_required`.
- `Content-Length` mayor que lo enviado: corta por min data rate y responde `incomplete_upload` sin dejar archivo.
- `Content-Length` menor: guarda exactamente CL bytes (framing HTTP).
- `Content-Length` negativo o enorme: Kestrel lo rechaza.
- Mayor que `MaxUploadBytes`: 413 antes de leer, tanto en el check como en la subida real, también con `MaxUploadBytes=1000`.
- `MinFreeBytes` enorme: 507 en upload-check, upload y zip.
- Disco lleno real a mitad de la escritura: 507 `insufficient_storage` y `.part` borrado.
- Corte del cliente a mitad: `.part` borrado al instante y `busy` vuelve a false.
- `kill -9` a mitad: el `.part` queda, pero al arrancar se borra ("temporales huérfanos borrados: 1").
- 20 subidas concurrentes de 20 MB: correctas, sin `.part` residual y con 110 MB de RSS.
- Un nombre de 200 `ñ` (400 bytes UTF-8) o con emoji, CJK o combinantes se guarda y se lista bien.

**Configuración**
- `MaxUploadBytes=0`, `MinFreeBytes=-1` y `AllowedOrigins=*` impiden arrancar con mensaje claro, que es lo correcto para la config principal.
- Valores inválidos de `VideoPostprocess` se normalizan con un warning.

**ZIP de evidencia**
- Sin archivos: 400. Nombres duplicados: 400. Hash, tamaño o archivo distinto: 409 `evidence_changed`. Archivo alterado entre el hash y el zip: 500 con "no coincide con su hash" y sin ZIP.
- Corte del cliente a mitad: se limpia el `.part`.
- ZIP de 800 MB (600 MB `.mp4` y 200 MB `.txt`) con AES: `7zz t` da "Everything is Ok". El `.mp4` va **AES-256 Store** y el `.txt` **AES-256 Deflate**. El SHA-256 de lo extraído coincide con el manifiesto. Con una contraseña incorrecta falla.
- 16 nombres raros (RTL, emoji, `#%&`, `;,`, 250 caracteres, sin extensión, `.hidden`, `.tar.gz`, `UPPER.MP4`):
  - con contraseña, sin contraseña (`zipfile.testzip()` OK, flag UTF-8 en todas las entradas y hashes iguales), con `" "`, y con unicode más comillas y barra invertida: todos se verifican con 7zz;
  - los métodos por extensión son correctos.
- Commit:
  - un hash incorrecto da 409 y no mueve nada;
  - un hash en mayúsculas se acepta;
  - repetir el commit es idempotente;
  - volver a armar el ZIP después del commit da 409 `zip_already_committed`;
  - `estado.json` se escribe;
  - la descarga del ZIP final da 200.

**WebSocket**
- 500 clientes simultáneos con 10 ciclos de eventos: todos recibieron todo, con latencia máxima de 0,3 s y 91 MB de RSS.
- Productores concurrentes (3 hilos de grabación): sin clientes perdidos, porque `ManagedWebSocket` serializa los envíos.
- Mensajes malformados no tiran el proceso ni afectan a otros clientes:
  - basura, frame sin máscara, UTF-8 inválido, opcode reservado y control > 125 B se cierran con 1002 o 1007;
  - JSON roto, binario de 1 MB, texto de 20 MB, largo declarado de 2^62 y fragmento sin fin se ignoran y el agente sigue sano;
  - un flood de 10k pings se contesta con pong.
- Reconexión después de reiniciar el agente: OK.

**Post-proceso (mock iOS con `Agent__MockIosRecordingSource`)**
- Video de 8 s: `running` → `ready` con `video_variant_ready`, la variante queda en la raíz y `.postprocess` vacío.
- Video de 90 s:
  - progreso cada 3 s por WS;
  - mover el original al caso a mitad del proceso no da `file_busy` y la variante termina igual;
  - `DELETE /files/{orig}` a mitad da `cancelled/removed` y no deja variante ni carpeta de trabajo.
- Con un trabajo en cola:
  - los pendientes figuran como `pending`;
  - borrar uno pendiente lo pasa a `cancelled/removed`;
  - `POST /agent/maintenance` no se bloquea por el post-proceso y cancela todo con `agent_update`;
  - SIGTERM cancela con `agent_stopping`, mata `ffmpeg` y limpia;
  - `kill -9` seguido de un rearranque borra `.postprocess` ("huérfanos borrados: 1"), salvo lo de T-11.
- Otros casos:
  - un timeout corto (`TimeoutMin=1`) da `timeout` con mensaje;
  - un `.mp4` inválido da `failed/ffmpeg_error`;
  - `BlendMaxSourceSeconds=10` da `skipped/too_long`;
  - `/agent/state.background_operations` aparece mientras hay trabajo y se vacía al terminar.
- `GET /video-postprocess/status` valida bien: sin filename, vacío, `..`, `../x`, `a\b` y 51 nombres dan 400; para un archivo desconocido devuelve `known:false` con las variantes que hay en disco.

**Mantenimiento**
- Desde un navegador (`Origin` o `Sec-Fetch-Site`) da 403 `maintenance_local_only`.
- Con una subida en vuelo da 409 `agent_busy` y la subida termina bien.
- Durante el mantenimiento los POST dan 503 `agent_updating` (también con `/CASES/...`, `//cases/...` o `../` en la ruta) y los GET siguen andando.
- Un TTL de 1 se acota a 10 s.

## Constancia de limpieza

- Maté mi instancia en :8766 (no quedan procesos `Factum.Agent --mock --port 8766` ni `ffmpeg` sobre los datos de prueba).
- Desmonté y borré el volumen de prueba de 12 MB (`hdiutil detach`).
- Borré `scratchpad/tatana-qa-data/` completo (datos, `cases/`, `.upload-tmp`, `.postprocess`, carpeta de evidencia de prueba y ZIPs, unos 3,5 GB), `scratchpad/qa/media/` (videos ffmpeg, binarios de dd y el `bogus.mp4`), la copia de compilación (`scratchpad/agentbin/`, `scratchpad/agentsrc/`) y mis temporales `/tmp/qa_*`.
- La instancia real en :8765 (pid 8699) siguió corriendo sin interrupción. No toqué `~/Factum/Evidencia`, `server/src/Factum.Agent/agent-data`, su `bin/` ni su `appsettings.Local.json`.
- Conservé los scripts en `scratchpad/qa/tatana-scripts/` y los logs de mi instancia en `scratchpad/qa/logs/agent*.log`.
