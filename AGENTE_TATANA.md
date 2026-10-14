
# Tatana agente
## Listado de casos de uso
- Instalar Librerias (pymobiledevice3, adb, etc.)
- Actualizar Librerias
- Mostrar Estado de conexion del agente con el servidor
- Configurar Link del servidor
- Configurar que se inicie agente tatana al prender la maquina del usuario factum
- Visualizar dispositivos conectados disponibles
- Permitir Minimizar la aplicacion y dejarla abierta en segundo plano
- Copiar los sellos de tiempo a la carpeta del caso
- Leer la ficha técnica y el reloj del dispositivo
- Grabar la sesión completa de la recolección (Android)
- Marcar un momento de la sesión
- Recuperar la grabación de la sesión al reabrir Tatana
- Cerrar Tatana con una grabación de la sesión en curso

### Caso de uso: Instalar Librerias
> #### Actor principal: Usuario Factum
> #### Actor secundario: -
 #### Pre-condiciones:
  - Tatana agente instalado en la computadora.
  - computadora del usuario con acceso a internet
  - Tatana agente con permisos para poder descargar e instalar cosas en la computadora

#### Post-condiciones:
  - Libreria/s descargadas en la computadora del usuario 

| Actor principal  | Sistema |
| ------------- |:-------------:|
| 1 - Inicia instalacion de librerias      |      |
|      | 2 - Muestra listado de librerias disponibles sin descargar     |
| 3 - selecciona una o varias librerias para descargar||
||4 - Muestra barra de progreso de descarga
||5 - Muestra barra de progreso completa|
||6 - Muestra icono de que la libreria se descargo|
||7 - Fin de caso de uso.|

### Caso de uso: Actualizar  librerias
> #### Actor principal: Usuario Factum
> #### Actor secundario: -
#### Pre-condiciones:
 - Al menos una libreria disponible para actualizar
 - Tatana agente instalado en la computadora del usuario
 - Computadora del usuario con acceso a internet

#### Post-condiciones:
 - Libreria/s actualizada/s

|Actor principal|Sistema|
|--|:--:|
|1 - Inicia Actualizacion||
||2 - Muestra listado de librerias disponibles para actualizar|
|3 - Selecciona libreria/s para actualizar||
||4 - Muestra barra de progreso de actualizacion|
||5 - Muestra barra de progreso completa|
||6 - Muestra icono de que la libreria se actualizo|
||7 - Fin de caso de uso|



### Caso de uso: Mostrar estado de conexion del agente con el servidor

> #### Actor principal: Usuario factum
> #### Actor secundario: -
#### Pre-condiciones:
 - Computadora del usuario con acceso a internet
 - Link del servidor configurado

#### Post-condiciones:
 - Usuario informado de la conexion del agente tatana con el servidor

|Actor principal|Sistema|
|--|:--:|
|1 - Inicia caso de uso||
||2 - Muestra estado de conexion del agente tatana con el servidor|
||3 - Habilita boton para reiniciar/reintentar conexion|
||4 - Fin de caso de uso|


### Caso de uso: Configurar link del servidor
> #### Actor principal: Usuario factum
> #### Actor secundario: -
#### Pre-condiciones:
 - Agente tatana instalado en la computadora del usuario
 - Computadora del usuario con conexion a internet
#### Post-condiciones:
 - Agente tatana conectado al servidor

|Actor principal|Sistema|
|--|:--:|
|1 - Inicia configuracion del link del servidor||
||2 - Solicita ingresar link del servidor|
|3 - Escribe link del servidor||
||4 - Valida link del servidor|
||5 - Muestra mensaje de que el link es correcto y esta conectado al servidor|
|6 - Acepta mensaje||
||7 - Guarda configuracion|
||8 - Fin de caso de uso|

### Caso de uso: Configurar que se inicie agente tatana al prender la maquina del usuario factum

> #### Actor principal: Usuario factum
> #### Actor secundario: -

#### Pre-condiciones:
 - Agente tatana instalado en la computadora del usuario

#### Post-condiciones:
 - Agente tatana se iniciara cada vez que se prenda la computadora del usuario en segundo plano

|Actor principal|Sistema|
|--|:--:|
|1 - Inicia caso de uso||
||2 - Pregunta si se quiere iniciar el agente cada vez que se prende la conputadora del usuario|
|3 - Selecciona que si quiere iniciar el agente cada vez que se prende la computadora del usuario||
||4 - Guarda configuracion|
||5 - Fin de caso de uso|

### Caso de uso: Visualizar dispositivos conectados disponibles
> #### Actor principal: Usuario factum
> #### Actor secundario: -

#### Pre-condiciones:
 - Agente tatana instalado en la computadora del usuario
 - Al menos un dispositivo conectado a la computadora
#### Post-condiciones:
 - Listado de dispositivos detectados por el agente

|Actor principal|Sistema|
|--|:--:|
|1 - Inicia caso de uso||
||2 - Muestra listado de dispositivos conectados a la PC|
||3 - Fin de caso de uso|


### Caso de uso: Permitir Minimizar la aplicacion y dejarla abierta en segundo plano
> #### Actor principal: Usuario factum
 > #### Actor secundario: -

 #### Pre-condiciones:
  - Agente tatana instalado en la computadora
#### Post-condiciones:
 - Agente tatana minimizado en la computadora del usuario

|Actor principal|Sistema|
|--|:--:|
|1 - Inicia caso de uso||
||2 - Muestra ventana del agente tatana|
|3 - Minimiza el agente||
||4 - Cierra ventana|
||5 - Queda abierto en segundo plano|
||6 - Fin de caso de uso|


### Caso de uso: Copiar los sellos de tiempo a la carpeta del caso
> #### Actor principal: Usuario factum (a través de la web)
> #### Actor secundario: Backend de Factum

Sellado de tiempo RFC 3161 (`Refactorizaciones/sellado-tiempo.md` §7.3). El backend pide los sellos a una
autoridad externa; Tatana solo **escribe** los `.tsr` y el `LEEME-verificacion.txt` que le pasa el navegador en
`<carpeta del caso>/sellos/`, al lado del ZIP. Tatana no habla con la autoridad ni con el backend.

#### Pre-condiciones:
 - Agente tatana con la capability `case_seals_v1` en `GET /health` (si no la tiene, la web ofrece solo la
   descarga de los sellos y pide actualizar Tatana)
 - El ZIP final del caso ya está en esta PC (`/zip/commit` hecho)
#### Post-condiciones:
 - `<carpeta del caso>/sellos/` con un `.tsr` por sello obtenido y `LEEME-verificacion.txt`

**Endpoint:** `PUT /cases/{caseId}/zip/seals` (JSON snake_case, tope 16 MiB):

```json
{
  "case_ref": "123/2026",
  "zip_filename": "evidencia_123_2026_Perez.zip",
  "files": [ { "name": "captura_1.png.tsr", "content_base64": "MIIS…" } ],
  "readme": "VERIFICACIÓN DE LOS SELLOS…"
}
```

- Valida, en orden: el id del caso; `zip_filename` (`400 invalid_filename`); que el ZIP final exista
  (`404 zip_not_found`); los sellos: de 0 a 1000, nombre `<archivo>.tsr` seguro (sin rutas, sin caracteres
  reservados ni de control, sin nombres reservados de Windows), contenido base64 de 1 B a 64 KiB, sin repetidos
  (`400 invalid_request` con `filename`); `readme` ≤ 256 KiB o ausente (no se escribe).
- Usa el mismo lock por caso que el ZIP: si está tomado, `409 zip_in_progress`.
- Cada archivo se escribe de forma atómica (`<nombre>.part` + rename). Si ya existe con los mismos bytes, no se
  toca. El LEEME se escribe en UTF-8 sin BOM, tal cual llega.
- Respuesta `200`: `{ "directory": "<ruta absoluta de sellos/>", "written": 3, "unchanged": 1 }`. Un error de disco
  responde `500 storage_error`.

|Actor principal|Sistema|
|--|:--:|
|1 - Genera el caso (o abre el detalle de un caso con sellos nuevos)||
||2 - La web pide los sellos al backend (`GET /api/cases/{id}/timestamps/files`)|
||3 - La web se los pasa a Tatana (`PUT /cases/{id}/zip/seals`)|
||4 - Tatana los escribe en `sellos/` y responde cuántos escribió|
||5 - Fin de caso de uso|


### Caso de uso: Leer la ficha técnica y el reloj del dispositivo
> #### Actor principal: Usuario factum (a través de la web)
> #### Actor secundario: Dispositivo conectado (Android por adb, iPhone por lockdown)

Metadatos del dispositivo (`Refactorizaciones/metadatos-dispositivo.md` §4, §5.1 y §7.2). Lectura **bajo demanda**
y **de solo lectura**: no corre en el sondeo de 3 s de `devices_changed` (que no cambia) ni agrega mensajes al
WebSocket. Tatana nunca escribe en el dispositivo, no cambia ajustes, no instala nada ni sincroniza la hora.

#### Pre-condiciones:
 - Agente tatana con la capability `device_details_v1` en `GET /health` (si no la tiene, la web crea el caso sin
   ficha y avisa "Actualizá Tatana para registrar los datos técnicos del dispositivo.")
 - Dispositivo conectado y autorizado (`adb get-state` = `device`; en iPhone, "Confiar" aceptado)
#### Post-condiciones:
 - La web recibe la ficha (20 datos, cada uno con `value` o con el motivo `reason` de "No disponible") y la
   medición de reloj dispositivo ↔ PC. Tatana no guarda nada: el backend la guarda en el caso.

**Endpoints** (GET, no los bloquea el modo mantenimiento; `platform=android|ios`, default `android`):

- `GET /devices/{serial}/details` → `{ serial, platform, agent_version, read_started_at, read_finished_at, fields,
  clock }`.
- `GET /devices/{serial}/clock` → `{ serial, platform, agent_version, clock }` (medición de cierre al generar).
- `clock`: `{ pc_before, pc_after, device_time, resolution_ms, device_minus_pc_ms, margin_ms, samples }` o, sin
  ninguna muestra válida, `{ samples: 0, reason: "timeout" | "error" | "invalid" }`. `device_minus_pc_ms` positivo
  = el dispositivo adelanta respecto de esta PC.
- Errores: `404 { error, code: "device_not_found" }` (Android sin dispositivo); iPhone, `{ error, code }` con el
  código del helper (`404` si es `ios_device_not_found`); `400 invalid_request` con otra plataforma.

**Comandos de solo lectura (Android)**, cada script como un solo argumento de `adb -s <serial> shell`:

1. `adb get-state` (3 s): si no es `device`, 404 sin correr nada más.
2. `date +%s.%N` × 3 (3 s cada una), con la hora de la PC antes y después de cada una. Se usa la muestra de menor
   ida y vuelta; margen = RTT/2 + resolución (1 ms, o 1 s si el `date` no da fracciones).
3. `getprop` de 15 propiedades (`ro.serialno`, `ro.build.*`, `gsm.*`, `ro.boot.*`, `persist.sys.timezone`) y
   `uname -a` (5 s).
4. `settings get global auto_time` / `auto_time_zone` (5 s).
5. `cmd wifi status` o, en Android 10 o anterior, `dumpsys wifi` filtrado **en el celular** con `sed`/`grep`
   para que BSSID, MAC, IP y RSSI nunca salgan del dispositivo (5 s).

Un script que falla marca sus datos con `timeout` o `error` sin abortar los demás. En iPhone,
`ios_helper.py details --udid U` (30 s) lee una lista blanca de lockdown (`SerialNumber`, `ProductVersion`,
`BuildVersion`, `TimeZone`, `SIMStatus`, operador y MCC/MNC de la SIM) y 3 muestras de `TimeIntervalSince1970`;
nunca reenvía ICCID, IMSI, número de línea ni direcciones MAC, y no necesita Modo Desarrollador. Cada valor se
sanea (controles, bidi) y se valida con un formato por dato; lo que no cumple sale `invalid` y no se loguea. El
log solo dice `Ficha técnica leída (<plataforma>, N no disponibles, X ms)`. Dos pedidos del mismo dispositivo se
atienden de a uno.

**Modo mock:** con `Agent:Mock=true` responde a los 300 ms la ficha real del Samsung SM-M127F (o una de iPhone
sintética con `platform=ios`), wifi conectada a "Red de prueba" y un reloj que difiere
`Agent:MockDeviceClockSkewSeconds` segundos (default 0), para probar el aviso de umbral sin celular.

|Actor principal|Sistema|
|--|:--:|
|1 - Elige el dispositivo y pasa al paso 2 del wizard||
||2 - La web pide la ficha a Tatana (`GET /devices/{serial}/details`)|
||3 - Tatana corre las consultas de solo lectura y mide el reloj|
||4 - La web muestra la diferencia de reloj y "Ver datos técnicos"|
|5 - Guarda el caso||
||6 - La web manda la ficha al backend, que la valida y la guarda con la hora del servidor|
|7 - Genera el informe con el dispositivo conectado||
||8 - La web pide una medición de cierre (`GET /devices/{serial}/clock`) y la manda al backend|
||9 - Fin de caso de uso|


### Caso de uso: Grabar la sesión completa de la recolección (Android)
> #### Actor principal: Usuario factum (a través de la web)
> #### Actor secundario: Dispositivo Android conectado por USB

Grabación de la sesión (`Refactorizaciones/grabacion-sesion-completa.md` §5 y §7.2). Una sola sesión activa por
Tatana. Tatana es la fuente de verdad mientras se graba: lleva un **diario** por caso en
`<DataDirectory>/sessions/<caseId>/sesion.json` (escritura atómica), que sobrevive a recargas y reinicios.

#### Pre-condiciones:
 - Agente tatana con la capability `recording_session_v1` en `GET /health` (sin ella, la web no ofrece la función y
   pide actualizar Tatana)
 - Caso del flujo "evidencia en la PC del perito" con la grabación de la sesión activada en el backend
 - Celular Android autorizado (scrcpy y ffmpeg disponibles)
#### Post-condiciones:
 - Un tramo `sesion_pantalla_NNN_<yyyyMMdd>_<HHmmss>.mkv` por cada corte, en la carpeta del caso, con tamaño,
   SHA-256, duración y validación en el diario; los huecos con hora y motivo; la web registra cada tramo en el
   expediente (y se sella)

**Endpoints** (JSON snake_case; errores `{ error, code, … }`):

- `GET /session/capacity?camera=false` → `{ available_bytes, min_free_bytes, bytes_per_minute, zip_on_same_volume,
  estimated_minutes, required_bytes_to_start, can_start }`.
- `POST /cases/{caseId}/session/start` `{ serial, platform: "android", android_version, segment_minutes,
  skip_screen_check, origin_id }` → estado de la sesión. Errores: `400 invalid_case_id` / `invalid_request` /
  `session_platform_unsupported` (iPhone en esta versión); `409 already_recording` (clip puntual en curso en ese
  celular) / `session_active` (otra sesión, con su `case_id`) / `screen_off` / `screen_locked`;
  `504 recording_start_timeout`; `507 insufficient_storage`; `500` con el error de scrcpy. Repetirlo con el mismo
  caso y celular devuelve el estado actual (idempotente).
- `POST /cases/{caseId}/session/stop` `{ reason: "cambio_de_paso" | "detenido_por_perito", origin_id }` → responde
  después de guardar y hashear el último tramo. Idempotente.
- `GET /cases/{caseId}/session` → el diario sin datos internos (`404 session_not_found` si no hay).
- `GET /agent/state` suma `recording_session` (solo con una sesión activa) y `session_recovery`
  (`running` | `done`); `operations[]` suma `recording_session` (bloquea la actualización).
- Eventos WebSocket: `session_state`, `session_segment_closed` y `session_mark_added`.
- `POST /devices/{serial}/record/start` en un celular con la sesión activa → `409 session_active` ("Usá «Marcar
  momento»"). `DELETE /cases/{id}/files/{tramo}` → `409 session_file_protected`. Subir o importar un archivo que
  empiece con `sesion_` → `400 invalid_filename` (prefijo reservado).

**Cortes cada `segment_minutes` (corte solapado):** el tramo nuevo arranca y recién con su marcador se cierra el
anterior (~0,7 s de solapamiento, sin hueco). Si el nuevo no arranca, el actual sigue grabando y se reintenta en un
minuto (aviso `rotation_delayed`). Después de cada corte, si el scrcpy saliente restauró "Pantalla siempre
encendida", Tatana vuelve a poner el mismo valor que pone scrcpy (7) y al terminar restaura el original (si el
celular no está, queda pendiente hasta que vuelva a aparecer).

**Desconexión del USB:** el tramo se cierra, se abre un hueco `desconexion` y la sesión queda en `waiting_device`;
cuando el sondeo de 3 s ve **el mismo** serial, reanuda sola en un tramo nuevo y cierra el hueco. Si scrcpy sale
con el celular conectado, el hueco es `fallo_grabacion` y se reintenta una vez. Pantalla apagada o bloqueada
durante la sesión: solo avisos (`screen_off`/`screen_locked`), nunca corta. Espacio: aviso `disk_low` por debajo de
`Agent:Session:WarnMinutesLeft` y corte limpio `sin_espacio` cerca de `Agent:MinFreeBytes`.

|Actor principal|Sistema|
|--|:--:|
|1 - Entra al paso 3 con la grabación de la sesión activada||
||2 - La web llama a `POST …/session/start`; Tatana chequea espacio y pantalla, lanza scrcpy y espera su marcador|
||3 - Cada `segment_minutes`, Tatana corta el tramo (solapado), lo valida, lo mueve a la carpeta del caso y lo hashea|
||4 - La web recibe `session_segment_closed`, registra el tramo en el expediente y sincroniza el diario con el backend|
|5 - Sale del paso 3||
||6 - La web llama a `POST …/session/stop`; Tatana guarda el último tramo y abre el hueco `cambio_de_paso`|
||7 - Fin de caso de uso|


### Caso de uso: Marcar un momento de la sesión
> #### Actor principal: Usuario factum (a través de la web)

Con la sesión grabando, "Grabar" pasa a ser "Marcar momento".

- `POST /cases/{caseId}/session/marks` `{ text?, origin_id }` → `{ id, at, segment_index, offset_seconds, text,
  kind: "manual" }`. `text` hasta 300 caracteres, sin caracteres de control (`400 invalid_request`); sin sesión
  grabando para ese caso, `409 session_not_active`.
- Cada captura de pantalla (`POST /devices/{serial}/screenshot` con el `case_id` de la sesión) deja una marca
  automática `kind: "captura"` con la hora de antes de `screencap`; la respuesta y el evento `screenshot_taken` suman
  `session_mark`.
- El tramo y el segundo del video salen del tramo de pantalla que contiene la hora (en un solapamiento, el más
  nuevo).

|Actor principal|Sistema|
|--|:--:|
|1 - Toca "Marcar momento" y escribe qué pasó (opcional)||
||2 - Tatana guarda la marca en el diario y emite `session_mark_added`|
||3 - Fin de caso de uso|


### Caso de uso: Recuperar la grabación de la sesión al reabrir Tatana
> #### Actor principal: Tatana (al arrancar)

Si Tatana se cerró grabando (cierre de golpe, crash, la PC se reinició), al arrancar recorre los diarios en segundo
plano (`session_recovery: "running"`):

1. Un diario ilegible se renombra a `sesion.json.ilegible-<fecha>` y no se toca ningún video.
2. Si el scrcpy del tramo abierto sigue vivo (mismo PID y misma hora de inicio), lo cierra limpio: el tramo queda
   completo.
3. Cada tramo abierto se valida (con remux si quedó truncado), se mueve a la carpeta del caso y se hashea
   (`recovered: true`); uno vacío o ilegible se descarta (`descartes/`) y el hueco dice que se perdió.
4. Abre el hueco `tatana_cerrado` y deja la sesión en `interrupted`: no se reanuda sola; se reanuda cuando el perito
   vuelve al paso 3.
5. Restaura "Pantalla siempre encendida" si el celular está conectado (si no, queda pendiente).

#### Post-condiciones:
 - Los tramos recuperados aparecen en el diario y se emiten `session_segment_closed`; el diario se borra recién al
   confirmar el ZIP del caso, si no quedó nada abierto.


### Caso de uso: Cerrar Tatana con una grabación de la sesión en curso
> #### Actor principal: Usuario factum (desde la ventana de Tatana)

`POST /agent/prepare-shutdown` (solo local, sin `Origin`; desde un navegador `403 maintenance_local_only`) corta la
captura, guarda el tramo y deja el diario en `interrupted` con el hueco `tatana_cerrado`, con un presupuesto de
`Agent:Session:ShutdownBudgetSeconds` (20 s). Responde `{ had_session, segments_saved, pending }`. Lo que no entra en
el presupuesto queda abierto y lo termina la recuperación. El cierre ordenado del host (`ApplicationStopping`, por
ejemplo SIGTERM en macOS/Linux) hace lo mismo con 15 s; un cierre de golpe en Windows lo cubre la recuperación.

|Actor principal|Sistema|
|--|:--:|
|1 - Cierra o reinicia Tatana desde su ventana||
||2 - La ventana ve `recording_session` en `/agent/state` y pide confirmación|
|3 - Confirma||
||4 - La ventana llama a `POST /agent/prepare-shutdown` y recién después cierra el proceso|
||5 - Fin de caso de uso|
