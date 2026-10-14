
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
