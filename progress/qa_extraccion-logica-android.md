# QA de robustez — HU #100 `extraccion-logica-android`

**Fecha:** 2026-10-25 · **Rama:** `feat/extraccion-logica-android` · **Skill:** `qa-factum`
**Foco:** el **camino real con adb** (nunca ejecutado contra un dispositivo hasta ahora; toda la
cobertura previa fue por el MockExtraction). Equipo: **Samsung Galaxy M12 / SM-M127F / Android 11 /
SDK 30**, serial `R58RA3R5AGA`, por USB.

## Veredicto

**QA con hallazgos — gravedad máxima: ALTA (ningún bloqueante crítico).**

El camino feliz de la extracción real funciona de punta a punta: `content query`/`pm list` reales,
`adb pull -a`, SHA-256 en la PC, manifiesto + `.sha256` + `SHA256SUMS.txt` + ACTA.txt/json + CIERRE +
LEEME, el **ACTA identifica el equipo** (bloqueante recién corregido: confirmado OK en real),
`device_busy` (§5.7) bloquea, el connector queda deshabilitado sin instalar nada, y el sellado RFC 3161
del manifiesto se obtiene. Pero **varias piezas que la SDD especifica para el camino real están
stubeadas u omitidas** (hash en el dispositivo, disponibilidad de categorías de archivos en el
preflight, estimación `du`, totales de archivos/bytes, fase `inventory`/carpeta `registro/`, batería y
estado de pantalla en el preflight). Son exactamente los huecos que el `impl_backend` anticipó
("camino REAL con adb … conciso … no ejecutado contra un dispositivo. Queda para la QA").

---

## Qué anduvo bien (con números)

- **Extracción real completa** en ~17 s a un destino del scratchpad. Categorías de solo lectura +
  `/sdcard/Download`:
  - contactos **241**, SMS/MMS **427**, calendario **4**, apps instaladas **362** (parser de
    `content query` real OK; crudo en `datos/crudo/`, normalizado en `datos/normalizado/`).
  - descargas **17 archivos / 132.303.795 bytes** (`adb pull -a`, incluye ocultos/sistema).
- **Carpeta de salida** con todos los obligatorios: `MANIFIESTO.tsv` (43 líneas, 42 entradas),
  `MANIFIESTO.tsv.sha256`, `SHA256SUMS.txt` (42 entradas, incluye ACTA.txt/json y LEEME.txt),
  `ACTA.txt`, `ACTA.json`, `CIERRE.txt`, `LEEME.txt`; estructura `datos/crudo`, `datos/normalizado`,
  `archivos/sdcard/Download`.
- **Integridad verificada por fuera de Factum**: `sha256sum -c SHA256SUMS.txt` → **todos OK (exit 0)**;
  `sha256sum -c MANIFIESTO.tsv.sha256` → OK; el hash del manifiesto
  (`543daad5…cf5cd3bf`) coincide entre el snapshot de Tatana, el `CIERRE.txt` y lo registrado en el
  backend.
- **ACTA identifica el equipo** (bloqueante corregido): ACTA.txt §3 DISPOSITIVO con Marca `samsung`,
  Modelo `SM-M127F`, Nº de serie `R58RA3R5AGA`, Android `11`; ACTA.json con `device_identity` + `device`.
  También en el camino de recuperación según el review.
- **`device_busy` (§5.7) — garantía crítica OK**: con una grabación activa sobre el mismo serial,
  `POST /extractions` → **409 `device_busy`** (+ `serial`). La grabación se detuvo limpia (9 s) y
  `stay_on_while_plugged_in` volvió a **0**.
- **Registro de llamadas deshabilitado correctamente**: preflight `connector.available=false /
  reason=not_released`; la UI muestra "Registro de llamadas" deshabilitado con *"Esta versión de Tatana
  todavía no trae la app auxiliar"*; forzando `call_log` por API → categoría `not_extracted /
  connector_unavailable / not_released`, `device_modifications` vacío y **el connector nunca se instala**
  (`pm path app.factum.connector` vacío antes y después).
- **Sellado RFC 3161 en el camino real**: el hash del manifiesto se selló (FreeTSA, `granted`).
- **Backend + web**: la web **auto-registra** las extracciones terminales al abrir `/extracciones`
  (se registraron solas las dos que dejé en Tatana); el wizard detecta el equipo; el árbol "Elegir
  carpetas" lista `/sdcard`; sin errores de consola.

---

## Hallazgos por gravedad

### ALTA

**H1 — El SHA-256 en el dispositivo (D10) nunca corre en el camino real; el ACTA afirma algo falso.**
`DeviceSha256Available` se fija a `Mock` en el arranque y nunca se vuelve a sondear en el camino real.
Como el M12 **sí** tiene `sha256sum` (el preflight lo reporta `true`), pero la extracción lo trae
`false`, la fase `verifying_device` se saltea: en el manifiesto `sha256_dispositivo` queda vacío y
`coincide=no_calculado` para **todos** los archivos, y el ACTA §6 dice *"El dispositivo no permite
calcular el SHA-256 (sin sha256sum)"* — afirmación **falsa** para este equipo. Rompe un criterio
validado (D10, "prueba que la copia es fiel al origen").
- Causa: `server/src/Factum.Agent/Services/Extraction/ExtractionService.cs:146`
  (`DeviceSha256Available = Mock,  // en real lo fija el preflight; acá best-effort` — pero `Start`
  nunca llama a `DeviceHasSha256`).
- Repro: `POST /extractions` con `verify_on_device:true` sobre el M12 → columna `coincide` del
  `MANIFIESTO.tsv` todo `no_calculado`; ACTA §6.

**H2 — En la web no se puede elegir ninguna categoría de archivos con un equipo real.**
El preflight marca **todas** las categorías de archivos como `available:false` aunque las carpetas
existan (p. ej. `/sdcard/Download` con 129 MB). La UI las deshabilita con *"No existe en este celular"*.
El perito no puede tildar Descargas/Fotos/Documentos/etc. desde el asistente.
- Causa: `FileCategoriesPreflight` no sondea el dispositivo —
  `ExtractionService.cs:356-364`: `available = mockAllExist && roots.Count>0` (en real `mockAllExist=false`;
  el parámetro `serial` se ignora, no hay `ls -d` como pide §5.11). Consumido por
  `client/src/lib/extraction.ts:184-190` (`isCategoryAvailable`) y
  `client/src/components/extraction/steps/SelectionStep.tsx:46-58` (`disabled={!available}`).
- **Alta, no crítica**: hay workaround — el árbol "Elegir carpetas" (explorer) sí llega a `/sdcard`, y
  el backend acepta las categorías si se mandan directo (así corrí la extracción). Pero el mensaje
  "No existe en este celular" es además engañoso.
- Repro: `/extracciones → Nueva extracción → Qué extraer` con el M12 → todas las filas de archivos
  deshabilitadas.

**H3 — La estimación (`du -s -k`) no está implementada en el camino real.**
`Estimate` devuelve `0` bytes por categoría en real (`ExtractionService.cs:319-343`:
`catBytes = Mock ? … : 0`, con el comentario `// real: du -s -k por raíz (QA)`), así que `total_bytes`
es siempre la constante de 50 MiB de datos estructurados. La UI mostró **"Total estimado: 50 MB"** sin
importar el contenido. Consecuencia: el guardado de "si el espacio libre no alcanza… no puede iniciar"
(§5.10 + Gherkin) nunca se dispara, porque `required_bytes` queda subestimado.
- Repro: `POST /devices/{serial}/extraction/estimate` con `downloads` → `files_bytes:0`,
  `total_bytes:52428800`.

**H4 — Los totales de archivos/bytes quedan en 0 (y así llegan al backend y a la UI).**
El rollup global `Totals.FilesTotal/BytesTotal` solo se setea en la rama Mock; en la rama real de
`RunFilesAsync` no se setea nunca. El `record` que se registra usa esos campos → el backend guardó
`totals {files:0, bytes:0}` para una extracción de **17 archivos / 132 MB**, y el listado
`/extracciones` muestra **"0 B"**. Es un dato obligatorio registrado ("cantidad de archivos y bytes") y
el resumen en el detalle del caso. (El detalle **por categoría** sí es correcto: `files:17,
bytes:132303795`.)
- Causa: `ExtractionService.cs:628-635` (rama real no toca `Totals.FilesTotal/BytesTotal`; la rama Mock
  sí, 611-613); el record los lee en `:823`.
- Repro: correr una extracción real con una categoría de archivos → `GET /api/extractions` ⇒
  `totals.files=0, bytes=0`; la lista web muestra "0 B".

### MEDIA

**M1 — No hay fase `inventory`; la carpeta `registro/` queda vacía.**
`RunFilesAsync` va directo a `Copying` sin la fase `inventory` de §5.2 (find+stat →
`registro/inventario_dispositivo.tsv` + `bytes_total` real). Tampoco se escriben `registro/comandos.log`
(§5.3.1) ni `registro/hashes_dispositivo.txt` (§5.4.5). Toda la subcarpeta forense `registro/` (qué
comandos corrieron, inventario del dispositivo, hashes del dispositivo) está **ausente**. Es la causa
raíz compartida de H3/H4 (sin inventario no hay denominador para progreso/ETA ni `bytes_total`).
- Repro: `ls registro/` en la carpeta de salida → vacío.

**M2 — El preflight real no trae `battery` ni `screen_state`.**
Solo la rama Mock (`ExtractionService.cs:298-306`) devuelve `battery_level/battery_charging/
screen_state`; la rama real (308-316) los omite, aunque §5.11 los pide y la UX los usa (aviso de batería
< 20 % y recordatorio de pantalla desbloqueada).
- Repro: `GET /devices/{serial}/extraction/preflight` → sin `battery*`/`screen_state`.

### BAJA

- **L1 —** ACTA.txt saltea la sección "8" (va …7 MODIFICACIONES → 9 INTEGRIDAD). Cosmético.
- **L2 —** `ACTA.json.manifest_sha256` queda `""` (el hash del manifiesto está en `CIERRE.txt` y
  `MANIFIESTO.tsv.sha256`). Probablemente a propósito —el ACTA se hashea dentro del manifiesto— pero un
  consumidor del JSON lo encuentra vacío; conviene documentarlo o poblarlo.
- **L3 —** El ACTA sigue sin la ficha técnica #79 completa ni la diferencia de reloj (solo identidad).
  Ya marcado/aceptado por el reviewer; la "Diferencia PC − servidor: No medida" aparece porque arranqué
  sin `PcServerProbe` (la web sí lo manda). Informativo.

---

## No verificable en esta QA

- **B0 / plan B del connector (§7.2).** Como no hay release firmada (`connector-lock.json.sha256=""`),
  Tatana nunca instala el connector, así que **no se pudo ejercitar** nada del proveedor: ni "responde
  en estado `stopped`", ni `install -g` concede `READ_CALL_LOG`, ni "el uid que ve el proveedor es
  2000", ni la decisión de ir al plan B (`am instrument`). **Queda pendiente** hasta que exista
  `connector-v1.0.0` firmada + actualización del lock (U1). No es un defecto; se deja anotado para el
  arquitecto/usuario: el plan B del connector sigue **indeterminado**.
- **Pruebas físicas (para el usuario):** desconectar el USB a mitad de copia y de grabación; bloquear la
  pantalla durante la copia; disco externo FAT32/exFAT/NTFS; Tatana en Docker con `/media`; iPhone;
  Windows. No las hace ningún agente.

## Notas de entorno (no son defectos del producto)

- El `:3000` estaba ocupado por **otro proyecto** (NutriBot/Numa, login con Google), no por la web de
  Factum, así que el cliente de Factum **no estaba corriendo**. Lo levanté en `:3001` y tuve que agregar
  `:3001` a la allowlist de CORS (`Cors:AllowedOrigins` del backend y `Agent:AllowedOrigins` de Tatana;
  el default es solo `:3000`) para que el navegador pudiera hablar con backend/Tatana. Todo por
  variables de entorno; **no se tocó código**.
- Backend (Development → `factum_dev`), Tatana real (`Agent__DataDirectory` al scratchpad) y el cliente
  los levanté yo (no estaban activos); los dejé **detenidos** al terminar. Mongo (contenedor compartido
  `evidentia-v2-mongo-1`) nunca se tocó.

## Constancia de limpieza

- **Mongo (`factum_dev`)** — borrado solo por `_id` exacto que yo creé:
  - `extractions`: `307d62e1-8c3d-402c-aa66-2118bc5b8c3c` y `582fa940-c982-47cc-9129-68cb3480e04e`
    (las dos auto-registradas por la web; ambas DNI QA `99000001`, serial del M12). → borradas (2).
  - `case_timestamps` (sellos): `514447d6dbc8414699a0a543892ae460` y
    `85e3359330a049c5b6e3df6d75696d89`. → borrados (2).
  - `case_events` referidos a mis extracciones: **0** (nunca asocié a un caso). Nada ajeno tocado.
- **Dispositivo:** sin instalaciones (connector nunca instalado), `stay_on_while_plugged_in=0` al
  terminar. Solo lectura (más una grabación de prueba de 9 s para `device_busy`, ya detenida).
- **Archivos:** la salida de la extracción y los datos de Tatana quedaron en el scratchpad de la sesión
  (aislado): `…/scratchpad/dest/Factum_Extraccion_samsung-SM_M127F_20261025-201848` y
  `…/scratchpad/agent-data`.

## Sugerencia de arnés

Agrupar **H1–H4 + M1–M2** como una HU de "completar el camino real de archivos/preflight de la
extracción" (todo es `server/src/Factum.Agent/Services/Extraction/`): fase `inventory` + `registro/*`,
`du` real en `Estimate`, `ls -d` real en `FileCategoriesPreflight`, sondeo de `sha256sum` en `Start`
(device hash), totales globales en la rama real, y `battery`/`screen_state` en el preflight real. B0 y
el plan B del connector dependen de la release firmada (U1) y van aparte.
