# SDD: Pulido después de la QA final: vista previa de grabaciones largas, header a 390 px, detalles del wizard, log de generación, consultas en el VPS y pantalla apagada antes de capturar

**Slug:** `pulido-post-qa-final` · **Issue:** #73 · **HU:** `docs/hu-pulido-post-qa-final.md` (validada el
2026-10-13, todas las dudas con la opción recomendada) · **Rama:** `feat/pulido-post-qa-final` · **Autor:** architect ·
**Fecha:** 2026-10-13

> **Fuera de esta SDD (D1 = A):** todo lo de la sesión larga: renovación de token, re-login en el lugar, cola de
> reintentos tras 401 y las decisiones D2–D4 de la HU. Pasó a la HU #74 (`sesion-larga`). Los escenarios Gherkin de
> la sección "Sesión larga" de la HU **no** se implementan ni se revisan acá.

Cito el código por **símbolo** y uso los números de línea solo como orientación (los leí en `62618df`).

---

## 1. Resumen funcional

Esta HU junta los arreglos que dejó la QA final y una prevención nueva en Tatana:

- **M1:** una grabación larga muestra su vista previa enseguida. Si el archivo todavía se está guardando en la carpeta
  del caso, la tarjeta dice "Preparando vista previa…". Si no carga en 10 s, ofrece "Reintentar". Además, Tatana
  sirve `GET /files/{nombre}` con Range. **Todo M1 depende de la medición previa con la pestaña visible.**
- **M2:** a 360 y 390 px las pestañas "Inspecciones / Métricas" no pisan el logo.
- **B1–B4:** ayuda del campo "Tipo de causa" (sin tocar la plantilla), una sola lista de sugerencias abierta, el
  banner "Retomando" solo al retomar y sin pedidos 404 a la raíz de Tatana, y "1 de 6 resultados".
- **Log:** el backend loguea una línea en nivel Information cada vez que un informe termina bien, en los dos flujos.
- **Documentación:** `docs/despliegue-nube.md` §14 explica cómo consultar el VPS. El camino principal sigue siendo
  `deploy`, y se agrega una subsección "Solo mirar, como admin".
- **D9:** antes de una captura de pantalla o de iniciar una grabación de Android, Tatana consulta (solo lectura) si
  la pantalla del celular está apagada o bloqueada. Si lo está, no captura. La web muestra un aviso con "Reintentar"
  y la salida "Capturar igual" o "Grabar igual". Si el chequeo no es concluyente, se captura como hoy.

Hace falta una **release nueva de Tatana** (D6 y D9; ver §12).

## 2. Toca

| Parte | ¿Toca? | Qué |
|---|---|---|
| **backend (API)** `server/src/Factum.Backend` | **sí** | Solo logs: `Services/Cases/AgentGeneration.cs` y `Services/Cases/CaseService.cs` (partial class `CaseService`). Sin cambios de DTO, endpoints ni Mongo. |
| **backend (Tatana)** `server/src/Factum.Agent` | **sí** | D9: `Services/AndroidScreenProbe.cs` (nuevo), `Controllers/ScreenshotController.cs`, `Controllers/RecordingController.cs` (`StartRecordingRequest`), `Common/AgentErrorCodes.cs`, `Controllers/HealthController.cs` (`Capabilities`), `Models/AgentModels.cs` (`MockAndroidScreenState`), `Program.cs` (registro DI). M1, si se reproduce: `Controllers/FilesController.cs` (`Download`). Tests en `server/tests/Factum.Agent.Tests`. |
| **client** `client/` | **sí** | M1: `components/VideoCard.tsx`, `components/CaptureStep.tsx`. M2: `components/shell/AppNavbar.tsx`. B1: `components/CaseFormStep.tsx`. B2: `components/form/CatalogAutoComplete.tsx`. B3: `app/dashboard/page.tsx` (`go`), `hooks/useEvidenceNotices.ts`. B4: `components/CaseHistory.tsx`. D9: `lib/agent.ts`, `hooks/useRecording.ts`, `app/dashboard/page.tsx`, `components/CaptureStep.tsx`. |
| **agent-ui** `agent-ui/` | **no** | No usa capturas, `/files` ni `record/start`. Verificado con grep. |
| **docs** | **sí** | `docs/despliegue-nube.md` §14 (D8). Lo escribe `implementer-backend`. |

Por ítem:

| Ítem | client | agent-ui | Factum.Backend | Factum.Agent | docs |
|---|---|---|---|---|---|
| M1 (condicionado a la medición) | sí | no | no | sí (`FilesController`) | no |
| M2 | sí | no | no | no | no |
| B1 (D5 = A) | sí | no | no | no | no |
| B2 | sí | no | no | no | no |
| B3 | sí | no | no | no | no |
| B4 | sí | no | no | no | no |
| Log de éxito (D7 = A) | no | no | sí | no | §14 lo menciona |
| VPS (D8 = A) | no | no | no | no | sí |
| Pantalla apagada o bloqueada (D9) | sí | no | no | sí | no |

---

## 3. Arqueología verificada

- **M1.** `VideoCard` monta `<video key={current.url} src={current.url} preload="metadata">`. Para un video sin
  `storedInCase`, `CaptureStep` arma el ítem con `url: caseURL(f)`, que da `undefined`, y `VideoCard` cae en
  `agentFileURL(file.name)`: la **raíz** de Tatana. En el flujo agent, `addFile` encola enseguida el guardado
  (`useFileManager.addFile` → `enqueueSave` → `saveOne` → `agent.importEvidence`). `CaseEvidenceStore.ImportAsync`
  **mueve** el archivo de la raíz a la carpeta del caso (rename atómico) y **después** calcula el SHA-256, que con
  una grabación larga tarda varios segundos. Recién al volver, `saveOne` marca `storedInCase` y la URL pasa a
  `agent.caseFileURL`, que ya tiene `enableRangeProcessing: true`. Mientras tanto, el `<video>` quedó pidiendo a la
  raíz un archivo que ya se movió. `FilesController.Download` responde
  `PhysicalFile(path, contentType, filename)`: **sin Range** y con `Content-Disposition: attachment`. Esto coincide
  con lo que vio la QA: el `<video>` de la tarjeta quedó colgado y un `<video>` nuevo con la URL ya actualizada cargó
  al instante. Con 38 s el hash es corto y la carrera no se nota. **Es una hipótesis: hay que medirla (§8.0).**
- **B3, el 404 de `/files/screenshot_….png`.** Viene de la misma carrera. `useEvidenceNotices` encola el chequeo de
  captura negra con la URL que corresponde **en el momento de encolar** (`f.storedInCase ? caseFileURL :
  agentFileURL`). La captura recién agregada todavía está en la raíz y `isMostlyBlack` hace el `fetch` cuando el
  import ya la movió, así que recibe un 404 (`isMostlyBlack` devuelve `null` y no avisa nada).
- **B3, banner.** `isResuming` solo vuelve a `false` en el reset (`page.tsx` ~784). La navegación pasa siempre por
  `go(n)` (`goToField` lo usa), así que es el único lugar a tocar.
- **M2.** Un ancho de 390 px deja 342 px útiles en la píldora (`px-4` del header y `p-2` de la píldora). Se restan la
  marca (40), los gaps y, a la derecha, el chip compacto de Tatana (botón de ícono por debajo de `sm`), el tema y el
  usuario (unos 130 px). Al centro le quedan unos 148 px, y unos 118 px a 360. Las dos píldoras con texto miden unos
  204 px y, aun compactas, unos 166. **No entran**, y `justify-center` las desborda para los dos lados (el `<Link>`
  no tiene `min-w-0`, así que el `truncate` no actúa). Las pestañas se ven en el historial del dashboard
  (`sections` solo en `mode === "history"`) y en `MetricsScreen`.
- **B1.** `tipo_causa` se precarga del último caso pericial (`PREFILL_KEYS` en `lib/pericial.ts`). Por eso "Causa"
  quedó como valor arrastrado. `FieldDef` ya admite `hint` (lo usa `nombre_tribunal`).
- **B2.** `CatalogAutoComplete`: el `onBlur` del input solo quita `aria-activedescendant`, y Prime cierra el panel
  únicamente con un clic afuera o con Escape.
- **B4.** `CaseHistory.tsx` (`countText`) pluraliza según `filtered.length`.
- **Log.** `AgentGeneration.cs` (~356) loguea "Generando informe (flujo agent)…". En el camino de éxito (~368-380)
  no hay línea. `CaseService.cs` (~764) tiene el mismo patrón en el flujo viejo. Las dos clases son `partial class
  CaseService`: comparten `_log` y pueden compartir un helper.
- **VPS.** `deploy/cloud/docker-compose.yml` tiene `name: factum`, así que los contenedores se llaman
  `factum-<servicio>-1` (`factum-mongo-1`, `factum-backend-1`, `factum-caddy-1`, `factum-frontend-1`). El contenedor de
  Mongo tiene `FACTUM_MONGO_APP_PASSWORD` en su entorno, y `scripts/verificar-informes.sh` ya se conecta así sin pasar
  la contraseña por la línea de comandos. `factum_app` tiene **readWrite**: el "solo lectura" lo garantiza la
  disciplina (consultas `find`/`countDocuments` con proyección), no el rol. En Mongo los campos van en PascalCase y
  el `Status` del caso es un **entero** (0 Draft, 1 Generating, 2 Completed, 3 Error; no hay convención de enum
  como string). `Case.ZipPassword` y `Case.PendingGeneration.Password` tienen la contraseña del ZIP. `users` tiene
  `PasswordHash`.
- **D9.** Tatana captura sin mirar la pantalla: `ScreenshotController.Take` y `RecordingController.Start` (Android →
  `adb.StartRecordingAsync`). No existe ninguna consulta `dumpsys` en el agente. El mensaje de
  `ErrorStartTimeout` ya le pide al perito "Desbloquealo, dejalo con la pantalla encendida".
  `ProcessRunner.RunArgumentListAsync` acepta un `timeout` que mata el árbol y devuelve `TimeoutExitCode` (-2). Del
  lado del client, `agent.takeScreenshot` lanza un `Error` plano (`readAgentError`) y **pierde el `code`**;
  `agent.startRecording` sí lanza `AgentError` con `code`. El JSON de Tatana es `SnakeCaseLower` (`Program.cs:101`)
  y no tiene `UnmappedMemberHandling`, así que un Tatana viejo **ignora** un campo de body desconocido y también un
  query param desconocido.
- **Medición real del chequeo (solo lectura, el Samsung SM-M127F con Android 11 conectado a esta Mac, pantalla
  apagada).** El comando de §9.1 tardó **124 ms** y devolvió lo siguiente (fixture para los tests):

  ```
    getWakefulnessLocked()=Dozing
  Display Power: state=OFF
  __FX_SEP__
      mKeyguardOccluded=false mKeyguardOccludedChanged=false mPendingKeyguardOccluded=false
        showing=false
        showingAndNotOccluded=true
        occluded=false
        dreaming=true
          mIsShowing=false
  ```

  Hay que tener en cuenta tres cosas. (a) En este Samsung la vigilia sale como `getWakefulnessLocked()=Dozing` y no
  como `mWakefulness=`. (b) Con la pantalla apagada, las señales de keyguard son incoherentes: `showing=false` y
  `showingAndNotOccluded=true` a la vez. Por eso el estado "apagada" se decide **antes** que el bloqueo. (c)
  `dumpsys trust` imprime el **nombre del titular del equipo**. Ni se usa ni se loguea ninguna salida cruda de
  `dumpsys`.

---

## 4. Modelo de datos

Sin cambios. No hay colecciones, campos ni índices nuevos, y no se escribe nada en Mongo. El estado de la pantalla
**no** se guarda en el caso ni en la cadena de custodia (`case_events`), y tampoco se reporta como `capture_failed` en
`agent_events` (ver D-T6).

## 5. Endpoints y mensajes WebSocket

**Backend API:** sin cambios.

**Tatana** (todos aditivos y compatibles con un client viejo):

| Método y ruta | Cambio |
|---|---|
| `POST /devices/{serial}/screenshot` | Query opcional nuevo, `skip_screen_check=true`. Si `platform != "ios"` y no viene `skip_screen_check=true`, se corre el chequeo antes de generar el archivo. Con `off` o `locked`, responde **409** con el cuerpo de §6.2 y **no crea archivo ni emite `screenshot_taken`**. Con `on` o `unknown`, captura como hoy. |
| `POST /devices/{serial}/record/start` | Campo opcional nuevo en el body: `skip_screen_check` (bool, default `false`). Con `platform == "android"` y sin `skip_screen_check`, se corre el chequeo antes de `adb.StartRecordingAsync`, de `storage.NewFilePath` y de `registry.SetOwner`. Con `off` o `locked`, responde 409 (§6.2), no emite `recording_started` y no deja estado. iOS no cambia. |
| `GET /health` | `capabilities` suma `"screen_check_v1"`. |
| `GET /files/{filename}` | **Solo si M1 se reproduce (§8.0):** soporta Range (`206`, `Accept-Ranges: bytes`) y se sirve *inline*, sin `Content-Disposition: attachment`, con los mismos encabezados de seguridad que `CaseEvidenceController.Get` (`X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`, `Cache-Control: no-store`). |

**WebSocket:** sin cambios. Ningún evento nuevo, y los existentes no cambian.

No cambian ni se chequean: la vista previa del paso 2 (`POST /devices/{serial}/preview`, no es evidencia), la cámara
web, AirPlay, la captura de iOS ni el stop de la grabación.

---

## 6. Contrato compartido

No cruza client ↔ backend API: el log no cambia ningún DTO. Sí cruza **client ↔ Tatana**. Tatana serializa en
`snake_case_lower` (`JsonNamingPolicy.SnakeCaseLower`, `server/src/Factum.Agent/Program.cs:101`).

### 6.1 Pedido: saltear el chequeo ("Capturar igual" / "Grabar igual")

| Dónde | Nombre exacto en el cable | Tipo | Lado Tatana | Lado client |
|---|---|---|---|---|
| Query de `POST /devices/{serial}/screenshot` | `skip_screen_check` | `"true"` (cualquier otro valor o la ausencia = `false`) | `ScreenshotController.Take`: `[FromQuery(Name = "skip_screen_check")] bool? skipScreenCheck` | `client/src/lib/agent.ts` → `agent.takeScreenshot(…, opts?: { skipScreenCheck?: boolean })` hace `q.set("skip_screen_check", "true")` |
| Body de `POST /devices/{serial}/record/start` | `skip_screen_check` | `boolean` (opcional) | `server/src/Factum.Agent/Controllers/RecordingController.cs` → `StartRecordingRequest.SkipScreenCheck` (`bool`, default `false`; snake_case por la política) | `client/src/lib/agent.ts` → `agent.startRecording(…, caseId, opts?: { skipScreenCheck?: boolean })` agrega `skip_screen_check: true` solo si viene |

### 6.2 Respuesta: pantalla apagada o bloqueada

`409 Conflict`, `Content-Type: application/json`:

```json
{
  "error": "La pantalla del celular está apagada o bloqueada. Encendela y desbloqueala para capturar.",
  "code": "screen_off",
  "screen_state": "off"
}
```

| Campo | Tipo | Valores | Tatana | client |
|---|---|---|---|---|
| `error` | string | Texto en castellano (el client arma el suyo; este queda para un client viejo, que lo muestra tal cual) | `AgentHttpException.ToBody()` | `AgentErrorBody.error` |
| `code` | string | `"screen_off"` \| `"screen_locked"` | `server/src/Factum.Agent/Common/AgentErrorCodes.cs`: `ScreenOff = "screen_off"`, `ScreenLocked = "screen_locked"` | `client/src/lib/agent.ts`: suma `"screen_off" \| "screen_locked"` a `AgentErrorCode` |
| `screen_state` | string | `"off"` \| `"locked"` | `Extra` de `AgentHttpException` (`["screen_state"] = "off" \| "locked"`) | `client/src/lib/agent.ts`: `AgentErrorBody.screen_state?: "off" \| "locked"` |

El 409 de `already_recording` sigue igual. Se distingue por `code`.

### 6.3 Capacidad

| Campo | Valor nuevo | Tatana | client |
|---|---|---|---|
| `GET /health` → `capabilities[]` | `"screen_check_v1"` | `HealthController.Capabilities` (+ comentario) | **No lo lee** (D-T5): con un Tatana viejo nunca llega un 409 de pantalla y el flag `skip_screen_check` se ignora. Queda como diagnóstico. |

### 6.4 `GET /files/{filename}` (solo si M1 se reproduce)

Cambian solo los encabezados: `Accept-Ranges: bytes`, `206 Partial Content` con `Range`, sin `Content-Disposition`,
`X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox` y `Cache-Control: no-store`.
El cuerpo y el 404 `{ error }` no cambian. Consumidores verificados: `<video>`, `<img>`, `isMostlyBlack` (`fetch`) y
`agent.ts` (XHR de descarga a blob, ~810). A ninguno le importa `Content-Disposition`.

### 6.5 Compatibilidad cruzada

| Web | Tatana | Comportamiento |
|---|---|---|
| nueva | nuevo | Completo. |
| nueva | viejo (≤ 1.4.x) | Captura como hoy: el flag se ignora y nunca llega `screen_*`. M1: la tarjeta espera `storedInCase` y usa la URL del caso, que ya tiene Range desde `case_evidence_v1`. |
| vieja | nuevo | Con la pantalla apagada, la captura falla con el texto de `error` (el client viejo lo muestra con `setGlobal` o con `agentErrorMessage`) y no hay "Capturar igual". Es aceptable porque la web y Tatana salen juntas (§12). |

---

## 7. Decisiones técnicas

| # | Decisión | ¿Usuario? |
|---|---|---|
| **D-T1** | El chequeo vive **dentro** de los endpoints de captura (`screenshot` y `record/start`), no en un endpoint de consulta aparte. Hace una sola ida y vuelta, no hay carrera entre "consulto" y "disparo", y un client viejo no necesita cambios. La salida "Capturar igual" va como `skip_screen_check`. | no |
| **D-T2** | Se chequea con un solo `adb -s <serial> shell "<script>"` que filtra con `grep` en el celular (§9.1). Así no viaja la salida completa de `dumpsys`, que puede tener datos del titular, y tarda unos 120 ms. El tope duro es de **1 s** con `RunArgumentListAsync(timeout:)`, y al vencerse el resultado es `unknown`. | no |
| **D-T3** | Orden de decisión: **apagada** antes que **bloqueada** (con la pantalla apagada, el keyguard da señales contradictorias, ver §3). Si se ve una pantalla de bloqueo *ocluida* (por ejemplo, una llamada o la cámara segura encima), se toma como `on`, porque la captura no sale negra. | no |
| **D-T4** | `unknown` (sin señales, error de adb, timeout, grep ausente) → se captura como hoy (D9.2 = A) y Tatana loguea en Information `Chequeo de pantalla no concluyente ({Reason}); se captura sin chequeo`, con `Reason ∈ {timeout, adb_error, no_signals}`. No se loguean serial, salida cruda ni datos del caso. `off`/`locked` loguean `Captura frenada: pantalla {State}`. | no |
| **D-T5** | El client **no** consulta `screen_check_v1`. El manejo del 409 es suficiente y un Tatana viejo degrada solo. | no |
| **D-T6** | Un bloqueo por pantalla **no** se reporta como `capture_failed` en `agent_events`: no hubo intento de captura y el usuario pidió que el estado no quede en la custodia. "Capturar igual" deja la captura normal (`screenshot` / `capture_start`), como siempre. | no |
| **D-T7** | El botón no muestra la etiqueta "Verificando pantalla…" que proponía la HU: el chequeo y la captura son **un mismo pedido** y no hay forma de separar las fases. Queda el estado ocupado de hoy ("Capturando…" / "Iniciando…"), y el chequeo suma unos 120 ms. | no (la HU lo dejaba a criterio de la SDD) |
| **D-T8** | Para la grabación, la salida se llama **"Grabar igual"**. "Capturar igual" queda para la captura de pantalla. Es el mismo verbo del botón que se tocó. | no |
| **D-T9** | **M1 es todo o nada según la medición de §8.0.** Si se reproduce (más de 3 s hasta ver la duración o el primer cuadro con la pestaña visible), se aplican los cambios del client (§8.1) **y** el Range en `GET /files/{nombre}` (§9.4). Si no se reproduce, no se toca nada de M1: se anota "no reproduce" con los números en `progress/impl_frontend_pulido-post-qa-final.md` y el Range tampoco se aplica. Lo de B3 (§8.5) se hace igual, porque es un 404 confirmado. | **sí: D-U1** |
| **D-T10** | M1 en el client: en el flujo agent, la tarjeta **no monta el `<video>`** mientras el archivo no está en la carpeta del caso y su guardado no falló. Muestra "Preparando vista previa…" y lo monta recién con la URL del caso, que ya tiene Range. Así se evita la carrera con el *move* y el hash, en vez de solo esquivarla con un timeout. | no |
| **D-T11** | M2: por debajo de `sm` (< 640 px), las dos pestañas pasan a **solo ícono** (36 × 36 px, `aria-label` con el nombre, `FxTip` con el nombre y `aria-current="page"` en la activa). Desde `sm` quedan como hoy. Las cuentas de §3 muestran que con texto no entran a 360-390 px ni compactando padding y tipografía. Además, el centrado pasa a ser "seguro" (`mx-auto` sobre un contenedor `justify-start`), así que si algo no entra se desborda hacia la derecha y nunca sobre el logo. El implementador lo valida con `ui-ux-pro-max` y mira 640 y 768 px: si a esos anchos las píldoras con texto se cortan, sube el corte a `md`. | no |
| **D-T12** | B2: se cierra el panel en el `blur` del input **solo si `relatedTarget` es un elemento enfocable fuera del componente y fuera de su panel**. Hacer clic en una opción (un `<li>` no enfocable, `relatedTarget = null`) no la cierra, así que no se rompe la selección. Tab hacia afuera y clic en otro input sí la cierran. No usa timers. | no |
| **D-T13** | B3: `isResuming` pasa a `false` en `go(n)` cuando se **sale** del paso 3 (`step === 3 && n !== 3`). Una retoma *legacy* que entra por el paso 2 sigue viendo el banner la primera vez que llega al 3. | no |
| **D-T14** | B3 (404): en el flujo agent, el chequeo de captura negra **espera a que la captura esté en la carpeta del caso** (`storedInCase`) y usa la URL que corresponde **al momento de correr**, no al de encolar. | no |
| **D-T15** | Log: un helper `private static string ShortHash(string? h)` en `CaseService` (partial) devuelve los primeros 8 caracteres en minúscula, o `"-"` si el hash viene vacío. El mensaje exacto es `Informe generado para caso {CaseId} (ZIP {ZipHash}, DOCX {ReportHash})`, se escribe **después** de `RecordEventAsync(ok)` y **antes** del `return`, y es igual en los dos flujos. | no |
| **D-T16** | Docs: `docs/despliegue-nube.md` lo escribe `implementer-backend` (conoce `deploy/cloud/`). No se crean scripts ni se cambian permisos. | no |
| **D-T17** | Los fixtures de prueba para "encendida y desbloqueada" y "bloqueada" se arman con los tokens que imprime Android 11 (mismo formato que el fixture real de "apagada"). El usuario o el implementador, si tiene el celular a mano, captura la salida real con el comando de §9.1 **sin tocar el equipo** (solo lectura) y la suma como fixture. Nunca se usa `input keyevent` ni nada que cambie el estado del celular. | no |

### Decisión que necesita al usuario

**D-U1. ¿Quién mide M1 con la pestaña visible antes de implementarlo?** Solo se puede medir con el celular y una
grabación real de 3 y de 10 minutos. Los implementadores no pueden grabar con el teléfono.
- **A) (Recomendada) Lo medís vos ahora, con `develop` tal como está, antes de lanzar a los implementadores.** Abrí
  el wizard con la ventana visible y en primer plano, grabá unos 3 min y detené; después grabá 10 min o más y detené.
  Para cada uno anotá los segundos hasta ver la duración o el primer cuadro y, en DevTools › Network, la URL del
  `<video>` (raíz `:8765/files/…` o `:8765/cases/…/files/…`) y si la respuesta trae `Accept-Ranges`. El orquestador
  le pasa el resultado al implementador. Si tarda 3 s o menos en los dos casos, M1 se cierra como "no reproduce" y
  no se toca código de M1.
- B) El `implementer-frontend` hace una medición aproximada con Tatana en mock y un MKV de 10 min generado con
  ffmpeg (`Agent__MockAndroidRecordingSource`), con la pestaña visible. Es más rápido pero menos fiel: no hay scrcpy
  ni remux real, y vos lo confirmás después en la prueba manual.
- **Por qué A:** la QA dejó esta medición como "prueba física pendiente". El celular está conectado a esta Mac, y la
  medición decide si se toca Tatana o no (D-T9). B puede dar "no reproduce" por no tener la carga real del remux.

---

## 8. Diseño técnico: `client/`

### 8.0 Medición de M1 (puerta)

Es el **primer** paso del frontend. Si D-U1 = A, el resultado ya viene en el prompt del orquestador: se copia a
`progress/impl_frontend_pulido-post-qa-final.md` y se sigue. Si D-U1 = B, el implementador mide así:

1. Corre Tatana en mock (`Agent__Mock=true`) con `Agent__MockAndroidRecordingSource=<ruta a un MKV h264+opus de
   10 min generado con ffmpeg>` y la web en `npm run dev`, con la pestaña visible.
2. Graba, detiene y mide el tiempo hasta `loadedmetadata` del `<video>` de la tarjeta.
3. Anota la URL y si la respuesta trae `Accept-Ranges`.

El umbral es **> 3 s → reproduce**. Hay que anotar los números en cualquiera de los dos casos.

**Si no reproduce:** se saltean §8.1 y §9.4. El implementador lo dice en el progress y en su línea de `done`.

### 8.1 M1: `components/VideoCard.tsx` y `components/CaptureStep.tsx` (solo si reproduce)

- `VideoCard` suma la prop `previewPending?: boolean` (default `false`):
  - Si es `true`, **no** monta el `<video>`. En su lugar, la superficie (`MEDIA_SURFACE`, misma altura mínima que el
    video para que no salte el layout) muestra un ícono, el texto "Preparando vista previa…" en un `role="status"` y
    la línea "La grabación ya está a salvo en esta PC; la vista previa aparece en unos segundos." Sin spinner
    infinito: si hay animación, que respete `motion-reduce`.
  - Si es `false`, monta `<video key={`${current.url}#${attempt}`} …>` con `onLoadedMetadata` y `onError`.
  - **Timeout de 10 s:** al montar el `<video>`, arranca un timer que **solo cuenta con
    `document.visibilityState === "visible"`** (se pausa con `visibilitychange`). Si vence sin `loadedmetadata`, o
    si llega `onError`, aparece un aviso no bloqueante, "La vista previa tarda más de lo normal.", con un botón
    secundario "Reintentar" (`aria-label="Reintentar vista previa de {file.name}"`, foco visible). Reintentar hace
    `attempt++`, que remonta el `<video>` con la misma URL; el archivo de evidencia no se toca. El texto de ayuda
    aclara que la evidencia está guardada aunque la vista previa no cargue.
  - Cambiar de pestaña Original/variante resetea `attempt` y el timer.
- `CaptureStep` calcula, para el ítem `selected` de tipo video:
  `previewPending = agentMode && !selected.remoteFile?.storedInCase && uploadStates[selected.name] !== "error"`.
  Si el guardado falló, el archivo sigue en la raíz y se monta con la URL de la raíz, como hoy.
- No se cambia la URL de las variantes (siguen en la raíz).

### 8.2 M2: `components/shell/AppNavbar.tsx` (`SectionTabs`)

- `SECTIONS` suma un ícono por entrada (lucide; buscá primero si la app ya usa uno para "Inspecciones" y otro para
  "Métricas", y si no, uno de lista y uno de gráfico de barras, con el nombre real de `lucide-react` 1.x).
- Por debajo de `sm`, cada `<Link>` se ve como un botón de ícono de `h-9 w-9` con `aria-label={label}`, envuelto en
  `FxTip label={label} side="bottom"`. El texto queda `sr-only` o directamente no se renderiza, porque el
  `aria-label` ya da el nombre accesible. Desde `sm`, el `<Link>` con texto de hoy. La activa conserva
  `SECTION_ACTIVE` (fondo y peso) y `aria-current="page"`.
- Centrado seguro: el contenedor del centro, cuando hay `sections`, deja de usar `justify-center` y la `<nav>` usa
  `mx-auto` (`min-w-0`). Así, si no entra, se desborda hacia la derecha y nunca sobre el logo.
- Hay que verificarlo con el chip de Tatana y el menú de usuario visibles (dashboard historial) y en `MetricsScreen`
  (sin usuario), a 360, 390, 640, 768, 1024 y 1440 px. A 1024 y 1440 tiene que quedar idéntico a hoy.

### 8.3 B1 (D5 = A): `components/CaseFormStep.tsx`

En `CAUSA`, para `tipo_causa`:
- `placeholder: "Ej.: disciplinaria, civil, penal"`
- `hint: "Solo el tipo, sin la palabra «causa»: el informe ya dice «la causa … N° …»."`

La plantilla, el backend y `PREFILL_KEYS` no cambian.

### 8.4 B2: `components/form/CatalogAutoComplete.tsx`

En `pt.input.root.onBlur`, además de quitar `aria-activedescendant`:

```ts
const next = e.relatedTarget as Node | null;
const root = acRef.current?.getElement();
const overlay = acRef.current?.getOverlay();
if (next && !root?.contains(next) && !overlay?.contains(next)) acRef.current?.hide();
```

El botón del desplegable y el pie "Administrar sugerencias" están dentro de `root` u `overlay`, así que no cierran.
Clic en una opción: `relatedTarget` es `null` y no cierra. Lo escrito no se toca.

### 8.5 B3: `app/dashboard/page.tsx` y `hooks/useEvidenceNotices.ts`

- En `go(n)`: `if (step === 3 && n !== 3) setIsResuming(false);`. No hay otros lugares.
- `useEvidenceNotices(caseId, files, opts?: { agentStorage?: boolean })` (`page.tsx` le pasa
  `currentStorage === "agent"`):
  - En el bucle de encolado, con `agentStorage` y `!f.storedInCase` → `continue` **sin** marcar `seenRef`. Se va a
    encolar cuando el archivo pase a `storedInCase`, porque `files` cambia y el efecto vuelve a correr.
  - La cola guarda solo `name`. La URL se resuelve **en `pump`** con un `filesRef` actualizado:
    `storedInCase ? agent.caseFileURL(caseId, name) : agentFileURL(name)`.
  - Sin `agentStorage` (flujo servidor) sigue como hoy.
- **Verificación:** en DevTools › Network no aparece ningún 404 de `:8765/files/screenshot_…` después de capturar en
  un caso del flujo agent. Si queda alguno, sale de un `<img>` (escenario o miniatura) que pidió la raíz justo
  antes del *move*. En ese caso, se agrega un `onError` que reintenta **una vez** con `agent.caseFileURL` cuando el
  archivo ya está `storedInCase`, y se documenta en el progress.

### 8.6 B4: `components/CaseHistory.tsx`

`countText`: `` `${filtered.length} de ${cases.length} resultado${cases.length !== 1 ? "s" : ""}` ``. Cubre los
cuatro ejemplos del Gherkin.

### 8.7 D9: pantalla apagada o bloqueada

**`lib/agent.ts`**
- `AgentErrorCode` suma `"screen_off" | "screen_locked"`. `AgentErrorBody` suma `screen_state?: "off" | "locked"`.
- `takeScreenshot(serial, platform, attribution, opts?: { skipScreenCheck?: boolean })`: si
  `opts?.skipScreenCheck`, hace `q.set("skip_screen_check", "true")`. Si la respuesta no es ok, lee el cuerpo
  **una sola vez**. Si `code` es `screen_off` o `screen_locked`, lanza `new AgentError("http", res.status, body)`; si
  no, lanza el `Error` de hoy con el mismo texto (los llamadores existentes no cambian).
- `startRecording(…, caseId, opts?: { skipScreenCheck?: boolean })`: agrega `skip_screen_check: true` al body solo si
  viene.
- Helper exportado: `screenBlockOf(e: unknown): "off" | "locked" | null`, que lee
  `e instanceof AgentError && (e.code === "screen_off" || e.code === "screen_locked")` y devuelve
  `body?.screen_state ?? (code === "screen_off" ? "off" : "locked")`.
- `agentErrorMessage` suma los dos códigos con el texto del aviso (por si algún llamador genérico lo muestra).

**`hooks/useRecording.ts`**
- `RecordOutcome` suma `{ kind: "screen_blocked"; state: "off" | "locked" }`.
- `startRecording(device, onError, iosMode?, opts?: { skipScreenCheck?: boolean })` le pasa `opts` a
  `agent.startRecording`. En el `catch`, si `screenBlockOf(e)` da un valor, **no** llama a `onError` y devuelve
  `{ kind: "screen_blocked", state }`.
- `handleToggleRecord(device, onError, opts?)` reenvía `opts` en la rama Android. La guarda `opInFlightRef` sigue
  igual: un doble toque produce un solo pedido.

**`app/dashboard/page.tsx`**
- Estado nuevo: `screenBlock: { action: "screenshot" | "record"; state: "off" | "locked" } | null`.
- `handleScreenshot(opts?: { skipScreenCheck?: boolean })`: le pasa `opts` a `agent.takeScreenshot`. En el `catch`,
  si `screenBlockOf(e)` da valor, hace `setScreenBlock({ action: "screenshot", state })`, sin `setGlobal` y sin
  `reportAgentEvent("capture_failed")` (D-T6). Cuando la captura sale bien, `setScreenBlock(null)`.
- La grabación hace lo mismo: el outcome `screen_blocked` pone `screenBlock` con `action: "record"`.
  `reportRecordOutcome` lo ignora (cae en `default`).
- `screenBlock` se limpia cuando hay cualquier captura o inicio de grabación exitoso, al cambiar o desconectarse el
  equipo, al salir del paso 3 (en `go`) y con el cierre del banner.
- Handlers para `CaptureStep`: `onScreenRetry` vuelve a correr la acción **sin** skip y `onScreenForce` la corre
  **con** `skipScreenCheck: true`. Los dos respetan los in-flight de hoy (`screenshotInFlightRef`,
  `opInFlightRef`).

**`components/CaptureStep.tsx`**
- Props nuevas: `screenBlock`, `onScreenRetry`, `onScreenForce` y `onDismissScreenBlock`.
- `FxBanner` de advertencia **junto a los controles de captura** (arriba de la botonera Pantalla/Grabar). Lleva un
  ícono de candado o de pantalla (`aria-hidden`), y el texto es:
  - captura: "La pantalla del celular está apagada o bloqueada. Encendela y desbloqueala para capturar."
  - grabación: "La pantalla del celular está apagada o bloqueada. Encendela y desbloqueala para grabar."
  - Botones: **"Reintentar"** (primario) y **"Capturar igual"** o **"Grabar igual"** (secundario). Los dos se
    deshabilitan mientras la acción está en vuelo.
  - `role="alert"` y anuncio **una sola vez** por aparición. Al aparecer, el foco va a "Reintentar".
- El tono y los textos siguen el aviso posterior de captura negra de #59 (`useEvidenceNotices`), que **no cambia**.
- iOS: nunca llega el 409, así que no hay cambios visibles.

### 8.8 Lo que NO cambia en el client

`isMostlyBlack` y el aviso posterior de #59, el paso 2 (vista previa), la cámara web, AirPlay, `agent-ui/` y todo
lo de la sesión (#74).

---

## 9. Diseño técnico: `server/`

### 9.1 Tatana: `Services/AndroidScreenProbe.cs` (nuevo)

```csharp
public enum ScreenState { On, Off, Locked, Unknown }
public sealed record ScreenProbeResult(ScreenState State, string? Reason); // Reason: timeout | adb_error | no_signals

public interface IAndroidScreenProbe
{
    Task<ScreenProbeResult> GetStateAsync(string serial, CancellationToken ct);
}
```

- **Comando** (con `ProcessRunner.RunArgumentListAsync(AdbPath, ["-s", serial, "shell", Script],
  timeout: TimeSpan.FromSeconds(1))`):

  ```
  dumpsys power | grep -E 'mWakefulness=|getWakefulnessLocked|Display Power: state='; echo __FX_SEP__; dumpsys window policy | grep -E 'showing=|showingAndNotOccluded=|occluded=|mIsShowing=|mShowingLockscreen=|isStatusBarKeyguard=|isKeyguardShowing=|mKeyguardOccluded='
  ```

  La ruta de adb se resuelve igual que en `AdbService` (`ToolResolver.Find(AgentTools.Adb)?.Path ?? "adb"`). Hay que
  extraerla a un helper compartido o duplicar el `Lazy`, sin cambiar el comportamiento de `AdbService`.
  - `ExitCode == TimeoutExitCode` → `Unknown("timeout")`. `ExitCode == -1` o `!= 0` sin salida →
    `Unknown("adb_error")`. Si no, `Parse(stdout)`.
  - Un `OperationCanceledException` del `ct` se propaga. El controller usa `HttpContext.RequestAborted`, pero el
    chequeo es corto.
- **`internal static ScreenState Parse(string output)`** es puro y testeable. Busca las claves como token, es decir,
  precedidas por un carácter que no sea letra (regex `(?<![A-Za-z])clave=(valor)`), para que `showing=` no matchee
  `mAodShowing=`.
  1. **Apagada:** `Display Power: state=(OFF|DOZE|DOZE_SUSPEND)`, o vigilia (`mWakefulness=` o
     `getWakefulnessLocked()=`) en `Asleep|Dozing` → `Off`.
  2. **Encendida:** vigilia `Awake` o `Display Power: state=ON`, y ninguna señal de apagada. Si no hay ninguna señal
     de energía → `Unknown`.
  3. **Bloqueo** (solo si está encendida). El orden de preferencia es:
     1. `showing=` (bloque `KeyguardServiceDelegate`) junto con `occluded=`. Si `showing=true` y `occluded=false`, es
        `Locked`. Si `showing=false`, o `occluded=true`, es `On`.
     2. Si no, `isKeyguardShowing=` o `mShowingLockscreen=` o `isStatusBarKeyguard=` o `mIsShowing=`. Cualquiera en
        `true` con `mKeyguardOccluded=false` (o sin ese dato) da `Locked`. Todas las presentes en `false` dan `On`.
     3. Si no hay ninguna señal de bloqueo → `Unknown`, y se captura igual (D9.2).
- **Mock** (`AgentOptions.Mock`): no llama a adb. Devuelve el valor de `AgentOptions.MockAndroidScreenState`
  (`"on"|"off"|"locked"|"unknown"`, default `"on"`), con una espera de 100 ms para que se vea el estado ocupado.
- Logs según D-T4. **Nunca** la salida cruda, el serial ni datos del caso.
- Se registra como singleton en `Program.cs`.

### 9.2 Tatana: controllers y errores

- `Common/AgentErrorCodes.cs`: `ScreenOff = "screen_off"` y `ScreenLocked = "screen_locked"`, con un `<summary>` que
  diga "409 de screenshot y record/start (pulido-post-qa-final D9)".
- Un helper compartido (por ejemplo, `internal static AgentHttpException? ScreenBlock(ScreenState s, string verb)`,
  en `AndroidScreenProbe` o en `Common`) devuelve la excepción 409 con `Extra = { ["screen_state"] = "off"|"locked" }`
  y el texto: "La pantalla del celular está apagada o bloqueada. Encendela y desbloqueala para {capturar|grabar}."
- `ScreenshotController.Take`: inyecta `IAndroidScreenProbe` y suma
  `[FromQuery(Name = "skip_screen_check")] bool? skipScreenCheck`. Si `platform != "ios" && skipScreenCheck != true`,
  sondea **antes** de `storage.NewFilePath`. Con `off`/`locked`, devuelve `StatusCode(409, ex.ToBody())`.
- `RecordingController.Start`: `StartRecordingRequest` suma `bool SkipScreenCheck = false` (al final del record,
  para no romper los usos posicionales; revisá los tests existentes que lo construyen). En la rama Android, si
  `!request.SkipScreenCheck`, sondea antes de `storage.NewFilePath` y de `adb.StartRecordingAsync`. Con
  `off`/`locked`, `throw` del `AgentHttpException`, que ya lo atrapa el `catch (AgentHttpException)` existente y lo
  devuelve con su status.
- `HealthController.Capabilities` suma `"screen_check_v1"`, con una línea de comentario como las demás.
- `Models/AgentModels.cs`: `public string? MockAndroidScreenState { get; set; }`, con un `<summary>` en el estilo de
  `MockAndroidRecordingOutcome` (solo en mock; env, CLI o `appsettings.Local.json`, nunca en `appsettings.json`).

### 9.3 Tatana: tests (`server/tests/Factum.Agent.Tests/AndroidScreenProbeTests.cs`, nuevo)

`Parse` con:
1. **Real, SM-M127F con Android 11 y la pantalla apagada:** el fixture de §3, tal cual → `Off`.
2. `mWakefulness=Asleep` → `Off`. `Display Power: state=DOZE` → `Off`.
3. Encendida y desbloqueada: `mWakefulness=Awake`, `Display Power: state=ON`, `showing=false`, `occluded=false` → `On`.
4. Encendida y bloqueada: `getWakefulnessLocked()=Awake`, `Display Power: state=ON`, `showing=true`,
   `occluded=false` → `Locked`.
5. Bloqueada pero ocluida (`showing=true`, `occluded=true`) → `On`.
6. Solo `isKeyguardShowing=true` con energía `Awake` → `Locked`.
7. `mAodShowing=true` con energía `Awake` y sin otras señales de keyguard → `Unknown` (no tiene que confundirse con
   `showing=`).
8. Salida vacía o basura → `Unknown`.
9. Energía `Awake` sin ninguna señal de keyguard → `Unknown`.

Más el **mock**: con `Mock = true` y `MockAndroidScreenState = "locked"`, `GetStateAsync` devuelve `Locked` sin
llamar a adb. Y un test del helper de 409, que verifique que `ToBody()` tiene `error`, `code` y `screen_state`. Si
hay un fixture real de pantalla encendida (D-T17), se suma como caso 10.

### 9.4 Tatana: `Controllers/FilesController.Download` (solo si M1 reproduce)

```csharp
Response.Headers.XContentTypeOptions = "nosniff";
Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
Response.Headers.CacheControl = "no-store";
return PhysicalFile(path, contentType, enableRangeProcessing: true);
```

Sin `fileDownloadName`, la respuesta pasa a ser inline. Los encabezados de seguridad hacen falta porque la raíz
también recibe archivos traídos del celular (`DeviceFilesController`), que pueden ser HTML. `List` y `Delete` no
cambian.

### 9.5 Backend API: log de éxito (D7 = A)

- `CaseService` (partial): `private static string ShortHash(string? h) => string.IsNullOrEmpty(h) ? "-" :
  h[..Math.Min(8, h.Length)].ToLowerInvariant();`
- `AgentGeneration.cs`: después de `await RecordEventAsync(ok);` y antes del `return Result.Ok(...)`:
  `_log.LogInformation("Informe generado para caso {CaseId} (ZIP {ZipHash}, DOCX {ReportHash})", id,
  ShortHash(meta.ZipHash), ShortHash(report.ReportHash));`
- `CaseService.cs` (flujo viejo): la misma línea después de `await RecordEventAsync(ok);`, con `result.ZipHash` y
  `result.ReportHash`.
- **Prohibido en la línea:** `pending.Password`, `result.Password`, `NroReferencia`, carátula, nombres, DNI,
  `ZipFilename`/`PdfFilename` (llevan datos de la causa), `zipLocation` o rutas, y el token.
- El `catch` de error no cambia.

### 9.6 Docs: `docs/despliegue-nube.md` §14 (D8 = A)

Se reorganiza el bloque **Logs** de §14 así, en el tono "seguila en orden" del documento:

1. **"Como `deploy` (camino principal)"**: `ssh admin@<IP>` → `sudo -iu deploy`, y después los comandos de hoy
   (`compose.sh logs --tail 200 backend`, `compose.sh logs --tail 100 caddy`, `compose.sh ps`, `df -h /srv`). Agrega
   cómo buscar los informes generados: `compose.sh logs --tail 2000 backend | grep "Informe generado"`.
2. **Subsección nueva "Solo mirar, como admin"**. Arranca explicando que `compose.sh` no anda como `admin` porque
   `factum.env` es `600` de `deploy` (§6), y que **no hay que cambiar ese permiso**. Comandos:
   - `sudo docker ps --format 'table {{.Names}}\t{{.Status}}'`. Los nombres esperados son `factum-backend-1`,
     `factum-mongo-1`, `factum-caddy-1` y `factum-frontend-1`. Si difieren, se usan los que muestra este comando.
   - `sudo docker logs --tail 200 factum-backend-1` y `sudo docker logs --tail 100 factum-caddy-1`.
   - `sudo docker logs --tail 2000 factum-backend-1 2>&1 | grep "Informe generado"`.
   - `df -h /srv` y `sudo docker system df`.
   - **Mongo en solo lectura**: una consola interactiva en la que la contraseña de `factum_app` sale de la variable
     de entorno **dentro** del contenedor, así que no pasa por la línea de comandos del host ni por el historial:

     ```bash
     sudo docker exec -it factum-mongo-1 mongosh --quiet --nodb --shell --eval '
       db = connect("mongodb://factum_app:" + encodeURIComponent(process.env.FACTUM_MONGO_APP_PASSWORD) +
         "@127.0.0.1:27017/factum?authSource=factum")'
     ```

     Consultas de ejemplo, **siempre con proyección explícita** (los campos van en PascalCase; `Status`: 0 borrador,
     1 generando, 2 completo, 3 error):

     ```js
     db.cases.countDocuments()
     db.cases.find({}, { _id: 1, Status: 1, CreatedAt: 1, GeneratedAt: 1, ZipHash: 1, ReportHash: 1 })
       .sort({ CreatedAt: -1 }).limit(10)
     db.cases.countDocuments({ Status: 3 })
     db.case_events.find({ CaseId: "<id>" }, { _id: 0, Type: 1, Timestamp: 1, Filename: 1 }).sort({ Timestamp: 1 })
     ```

   - **Advertencias** (lista corta):
     - El usuario `factum_app` puede escribir: en esta consola solo se usan `find` y `countDocuments`. Nada de
       `update*`, `delete*`, `insert*`, `drop` ni `createIndex`.
     - Nunca `db.cases.find()` sin proyección: muestra `ZipPassword`, `PendingGeneration.Password` y datos personales
       de la causa. Tampoco `db.users.find()` sin proyección (`PasswordHash`).
     - No correr `sudo docker inspect`, `sudo docker exec … env` ni `printenv`, que imprimen los secretos.
     - **Qué no se hace como `admin`:** editar `factum.env`, `up`, `down`, `restart`, `pull` ni deploys. Eso se
       hace como `deploy` con `compose.sh` o con los scripts.
3. Agrega una línea en "Qué no queda en los logs" que diga que la línea "Informe generado" lleva solo el id del caso
   y los primeros 8 caracteres de los hashes.

No hay credenciales, IPs reales ni dominios reales en el texto: se usan los marcadores `<IP>`, `<DOMINIO>` e `<id>`.

---

## 10. Checklist atómico

### 10.1 `implementer-backend` (`server/src/Factum.Agent`, `server/src/Factum.Backend`, `server/tests/Factum.Agent.Tests`, `docs/despliegue-nube.md`)

**Tatana: chequeo de pantalla (D9)**
- [ ] `Common/AgentErrorCodes.cs`: `ScreenOff`, `ScreenLocked` con su `<summary>`.
- [ ] `Models/AgentModels.cs`: `MockAndroidScreenState` con `<summary>`.
- [ ] `Services/AndroidScreenProbe.cs`: `ScreenState`, `ScreenProbeResult`, `IAndroidScreenProbe`, implementación con
  el comando de §9.1, timeout de 1 s, `Parse` puro (`internal static`), mock y logs según D-T4 (sin salida cruda ni
  serial).
- [ ] Helper del 409 (`screen_state` en `Extra`, verbo capturar/grabar).
- [ ] `Program.cs`: `AddSingleton<IAndroidScreenProbe, AndroidScreenProbe>()`.
- [ ] `ScreenshotController.Take`: query `skip_screen_check` y chequeo antes de `NewFilePath` (solo si no es iOS).
- [ ] `RecordingController`: `StartRecordingRequest.SkipScreenCheck` (último parámetro, default `false`) y chequeo
  en la rama Android antes de `NewFilePath`, `StartRecordingAsync` y `SetOwner`.
- [ ] `HealthController.Capabilities`: `"screen_check_v1"` + comentario.
- [ ] `server/tests/Factum.Agent.Tests/AndroidScreenProbeTests.cs`: casos 1–9 de §9.3, mock y cuerpo del 409.
- [ ] Ajustar los tests existentes que construyen `StartRecordingRequest` o que comparan `Capabilities` (si los hay).
- [ ] Prueba con Tatana en mock: `Agent__MockAndroidScreenState=off` → `curl -i -X POST
  'localhost:8765/devices/<serial>/screenshot'` da 409 con `code: "screen_off"` y **no crea archivo** en la raíz;
  con `?skip_screen_check=true` da 200. Lo mismo con `record/start` y `{"android_version":11,"skip_screen_check":true}`.
  Con `=unknown` captura (200) y loguea "no concluyente". Anotá los comandos y resultados en el progress.
- [ ] Si el Samsung está conectado: correr el comando de §9.1 **solo lectura** y anotar la salida (sin datos
  personales) en el progress. Prohibido `input keyevent` o cualquier cosa que cambie el equipo.

**Tatana: Range (M1, solo si la medición de §8.0 dio "reproduce"; el orquestador lo indica en el prompt)**
- [ ] `FilesController.Download` según §9.4.
- [ ] `curl -sI -H 'Range: bytes=0-99' localhost:8765/files/<archivo>` → `206`, `Accept-Ranges: bytes` y sin
  `Content-Disposition`.

**Backend API: log (D7 = A)**
- [ ] `ShortHash` en `CaseService` (partial).
- [ ] Línea `Informe generado para caso {CaseId} (ZIP {ZipHash}, DOCX {ReportHash})` en `AgentGeneration.cs`
  (después de `RecordEventAsync(ok)`).
- [ ] La misma línea en `CaseService.cs`, flujo viejo.
- [ ] Revisar que la línea no tenga nada de la lista prohibida de §9.5.

**Docs (D8 = A)**
- [ ] `docs/despliegue-nube.md` §14 según §9.6. Confirmar los nombres de contenedor contra
  `deploy/cloud/docker-compose.yml` (`name: factum`).

**Cierre**
- [ ] Comandos de §11.1 en verde.
- [ ] `progress/impl_backend_pulido-post-qa-final.md` con los archivos tocados, la verificación, si se aplicó o no el
  Range y por qué, y la nota de release de Tatana (§12).

### 10.2 `implementer-frontend` (solo `client/`; `agent-ui/` no se toca)

Skills obligatorias: `ui-ux-pro-max` (antes del JSX final de M1, M2 y el banner de D9), `senior-frontend`,
`3d-web-experience` (como criterio, sin 3D) y `web-design-guidelines` (autochequeo al final). Suma `ui-styling` y
`mblode-agent-skills-ui-animation` si aplica.

**M1 (puerta primero)**
- [ ] §8.0: registrar la medición (de D-U1) en el progress, con los segundos, la URL y `Accept-Ranges`.
- [ ] Si **no reproduce**: anotar "M1 no reproduce" y saltear los dos ítems siguientes.
- [ ] `VideoCard`: `previewPending`, el estado "Preparando vista previa…", el timeout de 10 s (solo con la pestaña
  visible), "Reintentar" con `attempt` en la `key` y los textos de §8.1.
- [ ] `CaptureStep`: calcular y pasar `previewPending`.

**M2**
- [ ] `AppNavbar` (`SectionTabs`): ícono, `aria-label` y `FxTip` por debajo de `sm`, centrado seguro (`mx-auto`) y
  verificación a 360, 390, 640, 768, 1024 y 1440 px en el dashboard (historial) y en Métricas.

**B1–B4**
- [ ] B1: `placeholder` y `hint` de `tipo_causa` (§8.3).
- [ ] B2: `onBlur` con `relatedTarget` (§8.4). Probar con clic y con Tab de "Parte denunciante" a "Parte denunciada",
  y que elegir una opción con el mouse siga funcionando.
- [ ] B3: `setIsResuming(false)` al salir del paso 3 en `go`.
- [ ] B3: `useEvidenceNotices` con `agentStorage` y la URL resuelta en `pump` (§8.5). Verificar en Network que no hay
  404 de `/files/screenshot_…`.
- [ ] B4: plural por `cases.length`.

**D9**
- [ ] `lib/agent.ts`: códigos, `screen_state`, `skipScreenCheck` en `takeScreenshot` y `startRecording`, el
  `AgentError` para `screen_*` y `screenBlockOf`.
- [ ] `lib/agent-messages.ts` (`agentErrorMessage`): los dos códigos.
- [ ] `hooks/useRecording.ts`: outcome `screen_blocked` y `opts` en `startRecording`/`handleToggleRecord`.
- [ ] `app/dashboard/page.tsx`: estado `screenBlock`, `handleScreenshot(opts)`, los handlers de reintento y de forzar,
  y la limpieza de §8.7. Sin `capture_failed` por pantalla.
- [ ] `CaptureStep`: banner con "Reintentar" y "Capturar igual"/"Grabar igual", `role="alert"`, foco en "Reintentar"
  y botones deshabilitados en vuelo.
- [ ] Probar con Tatana en mock (`Agent__MockAndroidScreenState=off|locked|unknown|on`) los escenarios D9 del
  Gherkin: aviso, Reintentar (con el doble toque, una sola captura), Capturar igual o Grabar igual, `unknown` que
  captura sin aviso, y `on` sin aviso.
- [ ] Con un Tatana viejo (sin `screen_check_v1`, por ejemplo con el build de `develop`): captura como hoy, sin errores.

**Cierre**
- [ ] Comandos de §11.1 en verde.
- [ ] `progress/impl_frontend_pulido-post-qa-final.md` con los archivos, la medición de M1, las capturas o anchos
  verificados, las skills invocadas y sus hallazgos.

**Orden sugerido:** primero el backend, que es chico y fija el contrato de Tatana, y después el frontend, para probar
D9 contra el mock real. Si se lanzan en paralelo, el frontend deja la prueba de D9 para el final.

---

## 11. Verificación

### 11.1 Comandos antes de declararse `done`

**`implementer-backend`**
```bash
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test server/tests/Factum.Agent.Tests
dotnet test server/tests/Factum.Backend.Tests   # no se tocan tests acá, pero tiene que seguir en verde
```

**`implementer-frontend`**
```bash
cd client && npx tsc --noEmit
cd client && npm run lint   # si el script existe
```

El orquestador corre además `./ops/harness/verify.sh` antes de `arquitectura_lista` y de `aprobada`.

### 11.2 Prueba manual del usuario

1. **M1** (si se implementó): con la pestaña visible, grabá 10 min o más y detené. La tarjeta muestra "Preparando
   vista previa…" y, en pocos segundos, la duración y el primer cuadro. Adelantá a la mitad sin esperar.
2. **M2:** con DevTools a 390 y 360 px, en el dashboard (historial) y en Métricas, las pestañas no pisan el logo y se
   pueden usar con toque y con Tab. A 1440 px se ve igual que antes.
3. **B1:** en el paso 2, el campo "Tipo de causa" muestra la ayuda nueva.
4. **B2:** escribí en "Parte denunciante" y hacé clic en "Parte denunciada": queda una sola lista abierta.
5. **B3:** retomá un caso, andá al paso 5 y volvé al 3 con el link de faltantes: no aparece el banner "Retomando".
   En Network no hay 404 de `/files/screenshot_…`.
6. **B4:** filtrá el historial hasta que quede 1 caso: dice "1 de N resultados".
7. **Log:** generá un informe (flujo agent) y buscá `Informe generado para caso` en el log del backend. No aparecen
   la contraseña, el expediente ni nombres.
8. **D9, con el Samsung real y el Tatana nuevo:**
   - Con la pantalla apagada, tocá "Pantalla": aparece el aviso y no se crea ningún archivo. Tocá "Reintentar" con
     la pantalla todavía apagada: el aviso sigue.
   - Encendé la pantalla sin desbloquear y tocá "Reintentar": aviso de bloqueada.
   - Desbloqueá y tocá "Reintentar": se captura una sola vez.
   - Repetí con "Grabar". Probá "Capturar igual" con la pantalla de bloqueo: se captura la pantalla de bloqueo.
   - Si podés, corré el comando de §9.1 con la pantalla encendida y con la de bloqueo (solo lectura) y pasale la
     salida al orquestador para sumarla como fixture.
9. **Docs:** seguí la subsección "Solo mirar, como admin" en el VPS: `docker ps`, `logs` y la consola de Mongo con
   una consulta con proyección.

### 11.3 Qué mira el reviewer además de `CHECKPOINTS.md`

- Que M1 respete la puerta (D-T9): o están los cambios con la medición que los justifica, o no hay ningún cambio de
  M1 y está la nota "no reproduce".
- Que el contrato de §6 coincida exactamente de los dos lados (`skip_screen_check`, `screen_off`, `screen_locked`,
  `screen_state`, `screen_check_v1`).
- Que ni Tatana ni el backend logueen salida cruda de `dumpsys`, serial, contraseña, expediente, nombres o rutas.
- Que el chequeo no corra en iOS, en la vista previa del paso 2, en la cámara web ni en AirPlay.
- Que la doc no tenga credenciales y que las consultas a Mongo tengan proyección sin `ZipPassword`.
- Que no se haya tocado nada de sesión (#74) ni `agent-ui/`.

---

## 12. Release de Tatana

Esta HU necesita una **versión nueva de Tatana** (la última publicada es la 1.4.0; se sugiere la **1.5.0**, que es
un cambio aditivo). Incluye el chequeo de pantalla (D9) y, si M1 se reprodujo, el Range de `/files`. Se publica con
`docs/despliegue-nube.md` §19.5 **después** de mergear a `develop` y en coordinación con la promoción de la web
(regla: develop → main solo a pedido del usuario). **No** hace falta subir `FACTUM_TATANA_VERSION_MINIMA`: la web
nueva funciona con Tatana 1.4.x (§6.5). Los implementadores no publican. Lo dejan anotado en su progress y el
orquestador se lo recuerda al usuario al cerrar.

## 13. Regla dura de datos (AGENTS.md)

Esta HU no escribe en Mongo. Las pruebas manuales de los implementadores con Tatana en mock crean archivos en la
`DataDirectory` de Tatana de desarrollo (capturas o grabaciones de prueba). Cada uno borra **solo los archivos que
creó**, por nombre (`DELETE /files/{nombre}`), y nunca la carpeta ni otros archivos. Si una prueba crea un caso para
probar el wizard, se usa uno nuevo creado por la prueba y no se modifican casos existentes. Nada de evidencia de
`Storage:DataDirectory` del backend se borra.

## 14. Riesgos

- **Variación por fabricante del `dumpsys`:** si un equipo no expone las señales, el resultado es `unknown` y se
  captura como hoy (D9.2 = A). El riesgo es un falso **positivo**, es decir, frenar con la pantalla encendida y
  desbloqueada. Lo mitigan dos cosas: "Capturar igual" está siempre disponible, y el parser exige tokens exactos.
- **M1 sin reproducir:** si la medición da "no reproduce", el síntoma de la QA queda como artefacto de la pestaña
  oculta y no se toca código. Si reaparece, la observación de §3 (la carrera *move* → hash) sirve como punto de
  partida.
- **Web vieja con Tatana nuevo:** con la pantalla apagada falla con el texto de Tatana y sin "Capturar igual" (§6.5).
  Se evita publicando Tatana y la web juntas.
