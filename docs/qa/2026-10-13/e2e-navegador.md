# QA E2E en navegador — Factum (develop, 2026-10-13, 10:50–11:55)

Entorno: web :3000 (Next dev), backend :8080, Tatana real :8765 (Debug, código de develop), Samsung SM-M127F (Android 11) por USB.
Herramienta: Claude in Chrome (pestañas propias). Sesión del usuario Jperez (no se tocó cuenta/perfil/branding; no se escribieron contraseñas).
Capturas: `qa/capturas/` (nombres `screenshot-<ts>-N.*`; se citan como `#N`). Notas crudas con hora: `qa/e2e-notas.txt`.

**Limitaciones del entorno que afectan la lectura:** la ventana de Chrome estaba oculta (`document.visibilityState = hidden` en ambas pestañas), así que las animaciones (framer-motion) quedaban congeladas hasta tomar una captura y Chrome difería la carga de `<video>`. Además el `type` sintético pierde ~1 tecla cada 60 y dispara el aviso "Maximum update depth" en cualquier input (también en el buscador del dashboard), así que **no** lo reporto como bug de la app. La máquina estaba con load average ~18–19 (otros frentes de QA) y el dev server de Next se reinició 2 veces por "approaching the used memory threshold". Eso recargó las pestañas, y lo usé como prueba de "recarga".

## Resumen

- El circuito completo **anduvo** (paso 1 a 6, ZIP AES-256 válido con 7-Zip, DOCX con hashes que coinciden), pero **hay bloqueantes**.
- **C1:** generar el ZIP falla de forma determinista con ciertos archivos ("no coincide con su hash"). Lo reproduje offline: es la lectura **async** de entradas AES+Stored en SharpZipLib 1.4.2. El caso 1 solo se pudo cerrar borrando esa captura.
- **C2:** la grabación Android pierde los **últimos ~2 min** (11m26s de reloj → 9m10s de archivo), y una grabación detenida por cierre forzado queda en **0 bytes** pero igual se registra como evidencia.
- **A1–A6:** captura que se cuela en otro caso desde otra pestaña, grabación huérfana tras recargar sin forma de detenerla desde la web, doble clic en Generar sin protección, informe con conteo de capturas desactualizado, auditoría con eventos falsos o duplicados, expediente duplicado aceptado sin aviso.
- Veredicto: **hay bloqueantes** (C1, C2) para promover a main.

## Hallazgos

| ID | Sev. | Pantalla | Pasos | Esperado vs obtenido | Evidencia / código probable |
|---|---|---|---|---|---|
| C1 | **Crítica** | Paso 5 Generar | Caso con `screenshot_20261013_110052_566.png` (44 214 B) → Generar (1 clic, 2 reintentos) | Esperado: ZIP OK. Obtenido: 500, toast "No se pudo armar o verificar el ZIP… Reintentá", y Tatana loguea `EvidenceZipVerificationException: … screenshot_20261013_110052_566.png no coincide con su hash`. El hash en disco es correcto (0f84cf0c…). **Reproducido offline** con copias y SharpZipLib 1.4.2: leer con `SHA256.HashDataAsync(zf.GetInputStream(e))` da un hash distinto según el tamaño y el contenido (fallan 4095, 44214 y 44216 B; pasan 4096 y 44660). Con lectura **sync** (`SHA256.HashData`): 0 fallos en 120 órdenes y en el barrido. Reintentar nunca funciona: el perito queda trabado. Es una probable regresión del PR #54 (Stored). | `server/src/Factum.Agent/Services/Evidence/EvidenceZip.cs:185-186` (VerifyAsync). Repro: `qa/ziprepro/Program.cs` |
| C2 | **Crítica** | Paso 3 Captura (grabación Android) | Grabar 11 min → Detener | Esperado: video de ~11 min. Obtenido: Tatana loguea "scrcpy no respondió a la señal; se forzó el cierre" y después "MKV remuxeado". El archivo dura **550,5 s** (último frame con el reloj del celular en 11:13; se detuvo 11:15:47), así que se pierden ~2 min finales. Además scrcpy arranca ~10 s tarde. En la grabación huérfana (detenida por API) el archivo quedó en **0 B** ("El remux del MKV falló; queda el archivo crudo") y **aun así** se importó al caso y se registró en el backend con el SHA-256 vacío `e3b0c442…`, y el informe lo lista. El paso 5 no avisa nada. | `AdbService.cs:270-323` (StopRecordingAsync solo chequea `File.Exists`, no tamaño ni duración; SIGINT a scrcpy no se respeta) |
| A1 | Alta | Paso 2/3, dos pestañas | Pestaña 1 en el paso 3 del caso A. Pestaña 2: Nueva inspección → paso 2 (hace "Actualizar captura" sola) | Esperado: la vista previa queda fuera de cualquier caso. Obtenido: la pestaña 1 **importa esa captura como evidencia del caso A** (`screenshot_20261013_110833_722.png`; pasa a "4 capturas", se registra en el backend y termina en el ZIP/informe). En la pestaña 2 la imagen queda rota (404) pero dice "Pantalla capturada". | `client/src/app/dashboard/page.tsx:279-282` (`screenshot_taken` se importa en cualquier pestaña que no esté en el paso 2, sin comparar serial ni quién la inició) |
| A2 | Alta | Paso 3 tras recarga | Grabando → recarga (reinicio del dev server) → Retomar | Esperado: muestra "Detener" o recupera el estado. Obtenido: muestra "Grabar"; al tocarlo, toast 500 "Ya hay una grabación de Android en curso." Desde la web no hay forma de detener la grabación huérfana. El banner pide "Reconectá el dispositivo" con el celular conectado. Si se detiene por fuera (lo hice con la API), la pestaña la importa igual. | `page.tsx:258` (el estado de grabación solo llega por evento WS; no se consulta `/agent/state` al montar) |
| A3 | Alta | Paso 5 | Doble clic en "Generar informe pericial" | Esperado: 1 solicitud. Obtenido: 2 POST `generate/prepare` + 2 POST `/zip` (uno 500 y otro 409 zip_in_progress). En "Crear caso" y "Pantalla" el doble clic sí está protegido. | `components/dashboard/GenerateStep.tsx` |
| A4 | Alta | DOCX | Borrar una captura en el paso 3 después de pasar por el paso 4 → Generar | Esperado: "Capturas de pantalla: N" coincide con la tabla. Obtenido: el DOCX dice "Capturas de pantalla: 3" y la tabla de hashes lista 2. El texto de Operaciones no se recalcula. También dice "sistema operativo Android Android 11" y "siendo horas 10:59". | plantilla de `operaciones_realizadas` (defaults de report-texts) |
| A5 | Alta | Cadena de custodia (detalle del caso) | Ver el detalle del caso 1 | Obtenido: hay un "Inicio de grabación" a las 11:03 que corresponde al intento rechazado (500); "Se agregó evidencia grabacion_…966.mkv" aparece **duplicado** (dos pestañas); la captura cruzada de A1 figura como agregada sin un evento de captura. | `api.reportAgentEvent` / registro de evidencia por pestaña |
| A6 | Alta | Paso 2 | Usar como expediente el número de un caso existente (QA-ROBUSTEZ-001) | Esperado: aviso de duplicado. Obtenido: el campo lo da por válido y el backend crea un segundo caso con el mismo número (5bfdf5eb…). | `CaseFormStep.tsx` / `CasesController` |
| M1 | Media | Paso 2, teclado | Click en Expediente → Tab | Esperado: el foco va a Fecha y se puede escribir. Obtenido: el calendario PrimeReact se abre al recibir foco, el foco salta a un SPAN y después a BODY, y el teclado queda perdido. El popup además tapa el propio input. | `CaseFormStep.tsx:852-863` (Calendar + `calendarPt`/`onCalendarKeyDown`) |
| M2 | Media | Paso 3 | Papelera de una captura | Esperado: confirmación o deshacer. Obtenido: borrado definitivo inmediato del disco (es evidencia forense). | `useFileManager.ts:299` |
| M3 | Media | Paso 2 / ficha viva | Carátula larga → "Ver todo" | Obtenido: el texto expandido se superpone con el bloque 3D/USB/"Actualizar captura" (#12, #13, #14, #66). | `components/case-form/DeviceStage.tsx` + ficha |
| M4 | Media | Paso 2/3 | "Siguiente" o scroll a un elemento | Obtenido: `html.scrollTop = 72` además del contenedor interno, y el header fijo queda fuera de la pantalla (#13, #19, #23). | `scrollIntoView` sobre un contenedor anidado |
| M5 | Media | Recarga | Recargar en el paso 2 antes de crear el caso | Obtenido: se pierde todo sin aviso (no hay borrador ni `beforeunload`); el wizard no usa la URL ni el historial, así que el botón Atrás del navegador sale del wizard (#16). Retomar desde el paso 4 vuelve al paso 3. | `page.tsx` (estado del wizard solo en memoria) |
| M6 | Media | Header | Dashboard o paso 1 con el celular conectado | Obtenido: el chip dice "Tatana activo · esperando dispositivo" aunque `/devices` lista el SM-M127F, y sigue igual mientras otra pestaña graba (#0, #1, #26). En 390 px no hay estado de Tatana. | header de `page.tsx` / `useDeviceConnection` |
| M7 | Media | Métricas → Desglose Carátula | Carátula larga | Obtenido: las etiquetas del eje Y se superponen (#37, #38, #40). | `DashboardBreakdown.tsx` |
| M8 | Media | Paso 3 | 2.ª captura | Obtenido: GET `/files/screenshot_…566.png` → 503 (el import ya movió el archivo al caso); la miniatura queda vacía hasta recargar. | carrera entre import y vista previa |
| B1 | Baja | Paso 3 | Grabando | No hay cronómetro de grabación (solo "REC Grabando pantalla"). | CaptureStep |
| B2 | Baja | Paso 2 | — | El DNI acepta letras; "Partes" sigue mostrando "Precargado · revisar" aunque se haya escrito todo de nuevo; la parte denunciada se trunca en 300 sin contador; "QA-ROBUSTEZ-00 / 1" se corta en medio de la palabra en la ficha; el 3D en carga muestra "turand" recortado (#18, #63). | — |
| B3 | Baja | Textos | — | "1 de 6 resultado", "Faltan 1 dato obligatorio", "Noviembre De 2025"; el contador del editor cuenta markup escapado (49 caracteres visibles → 66). | — |
| B4 | Baja | 1024 / 390 / 500 | — | A 1024 el chip "Nombre del dispositivo: sin marcar (opcional)" se sale de la tarjeta (#49). A 390 el logo pisa la pill "Inspecciones". El menú "Marcar como…" se sale por la derecha a 500 px. El rango de fechas se corta ("14/10/202"). | — |
| B5 | Baja | Tema | — | El tema no se sincroniza entre pestañas; toggle con la pestaña oculta → `InvalidStateError` de view transition sin capturar. | `lib/theme.ts` |
| B6 | Baja | Tatana | — | Las vistas previas del paso 2 quedan sueltas en la raíz de `agent-data` (no se limpian). Nombre del ZIP con tildes y truncado ("…Fernán.zip"). | — |
| B7 | Baja | a11y | `read_page` | Hay varios botones sin nombre accesible (iconos del header y de la ficha); los badges "5"/"1" de Métricas no tienen etiqueta visible. Los labels de los inputs sí están asociados (`htmlFor`). | — |

## Tiempos medidos

| Operación | Tiempo |
|---|---|
| Entrar al paso 2 (pinta formulario) | 1–5 s en blanco (dev + carga 18) |
| Crear caso (POST /api/cases) | 276 ms server; paso 3 visible ~3 s |
| Captura de pantalla | ~0,9 s (11:00:19,7 → archivo 11:00:20,5) |
| Inicio de grabación → scrcpy-server en el celular | ~10 s |
| Detener grabación de 11m26s → `busy=false` | ~10 s (timeout de SIGINT + kill + remux) |
| Paso 4: carga de los editores | 20–60 s (chunk dinámico; dev) |
| Generar ZIP (4 archivos, 440 KB) + informe | ~4 s (11:34:04 → 11:34:08) |
| Generar caso extremo (1 archivo) | ~2 s |

## Lo que anduvo bien

- Doble clic en "Crear caso y continuar": 1 solo POST. Doble clic en "Pantalla": 1 sola captura.
- HTML, emoji, comillas y "&" se muestran literales en la ficha, el historial, la tabla, Métricas y el **DOCX** (escapado XML, sin `<script>` crudo). El emoji ZWJ 👩‍⚖️ se conserva.
- Fecha futura: aviso suave "La fecha es posterior a hoy. ¿Es correcta?".
- Acordeón: Siguiente/Editar conservan los valores; el foco va al primer campo de la sección; el CTA cambia a "Crear caso y continuar".
- ZIP: el SHA-256 coincide con la UI y con el DOCX; `7zz t` da "Everything is Ok"; AES-256 Store en todas las entradas.
- Detalle del caso: recupera la contraseña, los hashes y la ruta del ZIP después de recargar en el paso 6.
- Búsqueda (sin distinguir mayúsculas), rango de fechas, Lista/Tabla/Cuadrícula, "Pendientes de retomar" y Retomar funcionan.
- Tema oscuro correcto en todos los pasos vistos; el layout a 390 px (dashboard, paso 1, paso 2 con "Ver ficha") se puede usar.
- Tatana importa el `recording_stopped` aunque la grabación se detenga por fuera; no hubo registro duplicado de evidencia en el backend.

## Pruebas físicas pendientes (usuario)

1. Desconectar el USB a mitad de una captura y a mitad de una grabación; reconectar (ver si queda una grabación huérfana como en A2).
2. Bloquear la pantalla del celular durante una grabación de 10+ min y comprobar la duración real del archivo frente al reloj (C2).
3. Grabar con audio reproduciéndose en el celular y verificar que el video dure lo mismo que la grabación (descartar que el corte de C2 dependa del audio).
4. Ver a mano la **vista previa** del MKV en el paso 3 con la ventana visible (acá no se pudo verificar porque Chrome estaba oculto).
5. Teclado real en el paso 2 (Tab desde Expediente a Fecha) y tipeo rápido real para descartar o confirmar las teclas perdidas.
6. PC sin GPU: fluidez del teléfono 3D. Windows 11: rutas con tildes en el nombre del ZIP.

## Limpieza

- Catálogo "partes": se crearon 4 entradas QA y **se borraron por id** con DELETE /api/catalogs/partes/entries/{id} (204): `b04e2c8d-39cc-4077-8b4d-50de1d8f4094`, `42db9420-66f1-45c8-96ab-ef1ad0481c1e`, `fb31a897-ec85-419e-95f0-7ac5dd51ae4d`, `cae9eebe-424e-45f1-8006-0bbd150b5c22`. Verificado: ya no queda "QA-ROBUSTEZ" en los catálogos.
- **Casos: no hay endpoint ni UI para borrar casos**, así que quedan para el orquestador:
  - `ba916e4f-0708-4799-91ff-a83ac0f9a2e8` — QA-ROBUSTEZ-001 (completed). El caseId de Tatana es el mismo.
  - `5bfdf5eb-2947-4b21-9776-5f893f46e507` — QA-ROBUSTEZ-001 duplicado / extremo (completed). El caseId de Tatana es el mismo.
- Archivos que quedan en disco por esos casos:
  - `~/Factum/Evidencia/QA_ROBUSTEZ_001_ba916e4f/` y `~/Factum/Evidencia/QA_ROBUSTEZ_001_5bfdf5eb/` (ZIP + `.factum/`).
  - `server/src/Factum.Backend/dev-data/cases/ba916e4f-…/` y `…/5bfdf5eb-…/` (DOCX).
  - Tatana `agent-data/`: las vistas previas sueltas de mis pasos 2 `screenshot_20261013_105037_467.png`, `screenshot_20261013_105615_850.png`, `screenshot_20261013_113925_976.png` y probablemente `screenshot_20261013_105818_026.png`. Las carpetas `agent-data/cases/<id>` de los dos casos ya las borró Tatana al confirmar el ZIP.
- Informes copiados antes de cualquier borrado (pedido del orquestador): `qa/informe/caso1_QA-ROBUSTEZ-001/` y `qa/informe/caso2_QA-ROBUSTEZ-001_extremo/` (DOCX + ZIP + `datos.txt` con caseId, contraseña y hashes). El **informe extremo se pudo generar**: carátula de 500 caracteres con `<b>`, `<script>`, `<img onerror>`, emoji y una palabra larga; parte denunciada de 300 (el tope, truncada); 8 integrantes con HTML y emoji ZWJ; titular largo con emoji e `<i>`.
- En el scratchpad borré las copias de evidencia de `qa/ziprepro/in*`; quedan solo `Program.cs` y el csproj del repro.
- Pestañas propias cerradas.
- Toqué el estado del celular de forma menor: lo desperté (KEYCODE_WAKEUP), volumen +1/−1 y dos swipes en Google Play.
