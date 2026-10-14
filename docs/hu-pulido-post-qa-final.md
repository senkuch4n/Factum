# HU: Pulido después de la QA final: vista previa de grabaciones largas, header a 390 px, detalles del wizard, log de generación, consultas en el VPS y sesión larga

**Slug:** `pulido-post-qa-final` · **Issue:** #73
**Apps afectadas (preliminar, lo confirma la SDD):** `client/` (web), backend API (`server/src/Factum.Backend`) y
`docs/despliegue-nube.md` y el agente Tatana (`server/src/Factum.Agent`) por el chequeo de pantalla antes de
capturar (punto 7; y Range en `/files/{nombre}` si D6 elige B). `agent-ui/` (Electron) sin cambios.

**Como** perito que hace inspecciones largas con el wizard (y como administrador del servidor en la nube)
**quiero** que la vista previa de una grabación larga cargue rápido, que el header no se pise en el celular, que el
wizard no muestre avisos o listas que no corresponden, que el log del servidor confirme cada informe generado, saber
cómo consultar el VPS sin pelear con permisos y, sobre todo, no quedarme sin sesión en medio de una inspección
**para que** pueda trabajar una jornada completa sin cortes ni pérdida de registros de evidencia, y el operador pueda
diagnosticar en producción.

Origen: QA final integrada del 2026-10-13 (`docs/qa/2026-10-13/qa-final-post-deploy.md`): **M1**, **M2**, **B1**,
**B2**, **B3** y **B4**. Además, del issue #73: el log de éxito del flujo agent, la documentación de consultas en el
VPS con el usuario `admin` y la sesión que vence a las 8 h (vista en logs de producción). Agregado por pedido del
usuario (último comentario del issue #73): **prevenir capturas negras** chequeando en Tatana que la pantalla del
celular esté encendida y desbloqueada antes de capturar o grabar (opción C de la Duda 6 de #59,
`docs/hu-grabacion-android-robusta.md`).

> Nota del afinador: no tuve acceso a `gh` en este entorno, así que no leí los comentarios del issue #73
> directamente. El alcance sale del encargo del orquestador (que los resume) y del informe de QA. Si un comentario
> del issue agrega algo que acá no está, el orquestador lo suma a la validación.

---

## Contexto

### Qué existe hoy (arqueología)

**M1 — Vista previa de una grabación de 3 min.** `VideoCard` (`client/src/components/VideoCard.tsx:105-112`) ya
monta el `<video>` con `key={current.url}`, `src={current.url}` y `preload="metadata"`; o sea, **ya tiene preload de
metadata y se remonta cuando cambia la URL**. La URL del "Original" la pasa `CaptureStep.tsx:922` (`selected.url`):
antes de guardar en el caso apunta a la raíz de Tatana (`agentFileURL` → `GET :8765/files/{nombre}`) y después de
guardar pasa a la carpeta del caso (`agent.caseFileURL` → `CaseEvidenceController`). Diferencia relevante entre los
dos endpoints de Tatana:
- `FilesController.Download` (`server/src/Factum.Agent/Controllers/FilesController.cs:25`) responde
  `PhysicalFile(path, contentType, filename)`: **sin `enableRangeProcessing`** y con `Content-Disposition` de descarga.
  Sin Range, el navegador no puede pedir el final del archivo (donde un MKV/WebM suele tener los índices) y puede
  tener que bajar todo antes de mostrar duración y primer cuadro.
- `CaseEvidenceController` (`:83`) sí usa `enableRangeProcessing: true`.

La QA vio el `<video>` en spinner (`readyState 0`, `networkState 2`) más de 30 s con la grabación de 185 s (h264 +
opus); un `<video>` nuevo con la misma URL cargó al instante, y con la grabación de 38 s no pasó. Hipótesis de la QA:
el `<video>` se monta antes de que termine el remux/import y queda colgado con la primera respuesta. **La QA corrió
con la pestaña oculta** (`visibilityState = "hidden"`), que frena la carga de medios en algunos navegadores, así que
el hallazgo no está confirmado. Las "Pruebas físicas pendientes" de la QA ya piden grabar 10 min o más con la pestaña
visible.

**M2 — Header a 390 px.** `AppNavbar` (`client/src/components/shell/AppNavbar.tsx`): la marca es `BRAND_CAP`
(`:44`, `h-10 w-10 shrink-0`), el centro es `flex min-w-0 flex-1 justify-center` (`:139`) y adentro `SectionTabs` es
otro `nav` con `justify-center` (`:71`). Cuando las dos píldoras ("Inspecciones" + "Métricas") miden más que el
espacio del centro, `justify-center` las desborda **hacia los dos lados** y la activa queda unos 10 px sobre el
círculo del logo. Los labels están pensados para "entrar a 360 px sin colapsar a ícono" (comentario `:56-58`), pero
con el chip de Tatana y el menú de usuario a la derecha no entran.

**B1 — "la causa Causa N°".** La plantilla v6 (`Templates/plantilla_informe_v6.docx`, generada con
`ops/plantilla/build_plantilla_v4.py`) dice "…en el marco de la causa {tipoCausa} N° {numeroCausa}". `{tipoCausa}` es
texto libre (`ReportValues.cs:111`, placeholder "Ej.: disciplinaria, civil, penal…" en `CaseFormStep.tsx:126`): con
"disciplinaria" queda bien, con "Causa" se repite. El mismo `{tipoCausa}` se usa en la portada, la ficha
(`ReportService.cs:441`, "{tipoCausa} N° {numeroCausa}") y el encabezado. En la HU #62 (`informe-formato-fixes`, D5,
validación del 2026-10-13) **el usuario eligió explícitamente no cambiar "la causa Causa N°"** (R2). Por eso acá es
una duda (D5), no un arreglo.

**B2 — Dos listas de sugerencias abiertas.** Las Partes del paso 2 usan `CatalogAutoComplete`
(`client/src/components/form/CatalogAutoComplete.tsx`, sobre el `AutoComplete` de PrimeReact) con la opción
"Nuevo: «…» (se guarda con el caso)". El `onBlur` propio (`:161`) solo saca `aria-activedescendant`; el panel de la
primera queda abierto si el clic va directo al input de la segunda (se cierra con un clic afuera).

**B3 — Banner "Retomando" al volver del paso 5 al 3.** `isResuming` (`app/dashboard/page.tsx:150`) se pone en
`true` al retomar (`:847`) y solo vuelve a `false` al reiniciar el wizard (`:784`). El banner se muestra con
`isResuming && step === 3 && currentCase` (`:1157`), así que reaparece cada vez que se vuelve al paso 3 en un caso
retomado (en la QA, QA-FINAL-001 se había retomado en el punto 2). Aparte, después de importar, el cliente pide
`GET :8765/files/screenshot_….png` (la ruta temporal de la raíz, que ya no existe) y recibe 404 sin efecto visible.

**B4 — "1 de 6 resultado".** `CaseHistory.tsx:185` pluraliza según `filtered.length` y no según el total:
"`${filtered.length} de ${cases.length} resultado${filtered.length !== 1 ? "s" : ""}`".

**Log de éxito de la generación (flujo agent).** `AgentGeneration.cs:356` loguea "Generando informe (flujo agent)
para caso {CaseId}" y el `catch` (`:384`) loguea el error; **el camino de éxito (`:368-380`) no deja ninguna línea**.
En producción no hay forma de ver en el log que un informe terminó bien (Caddy no tiene access log y el backend no
loguea pedidos, a propósito: `docs/despliegue-nube.md` §14). El flujo viejo (`CaseService.cs:764`) tiene el mismo
patrón.

**Consultas en el VPS con `admin`.** `docs/despliegue-nube.md` §14 indica `compose.sh logs …` / `compose.sh ps`, que
se corren como `deploy` (`sudo -iu deploy`, §5). Entrando como `admin`, `compose.sh` falla porque no puede leer
`/srv/factum/config/factum.env` (`chmod 600`, dueño `deploy`, §6). No está documentado cómo consultar logs, estado o
Mongo en solo lectura desde `admin` (con `sudo`, o `sudo docker exec`/`sudo docker logs`).

**Sesión de 8 h.** Hoy:
- `AuthService.IssueToken` (`Services/Auth/AuthService.cs:108-126`) emite un JWT HS256 con `exp = ahora +
  Jwt:ExpiryHours` (8 por defecto; `FACTUM_JWT_EXPIRY_HOURS` en `deploy/cloud/factum.env.example` y
  `deploy/windows/.env.example`). No hay refresh token ni endpoint de renovación; `POST /api/auth/logout` no revoca nada
  (`AuthController.cs:68-70`). El único "re-emisor" es `change-password` (modo `local`).
- `FactumBearerHandler` (`Infrastructure/FactumBearerHandler.cs:29-33`) devuelve 401 `{ error, code: "invalid_token" }`
  con el mensaje de log "Token inválido o expirado" (lo que se vio en producción). Acepta el token por `Authorization:
  Bearer` o por `?token=` (descargas de ZIP/DOCX, `client/src/lib/api.ts:1279`). En modo `local`, `SessionValidator`
  revisa en cada pedido que la cuenta siga activa y que la contraseña no haya cambiado (claim `pca`); en `dev` y
  `external` no consulta la base.
- Cliente: el token vive en `localStorage["factum_token"]` (`api.ts:17`, `:889`, `:911`). Ante **cualquier 401** fuera
  de login/mode, `notifyAuthFailure` (`api.ts:115-134`) borra el token, deja un aviso para el login en
  `sessionStorage` (`lib/session-notice.ts`, con `interrupted` si había una operación larga en curso) y
  `SessionWatcher` (`components/shell/SessionWatcher.tsx`) navega a `/`. **No hay aviso antes de vencer**: el perito se
  entera cuando un pedido falla, y la navegación descarta el estado en memoria del wizard.
- Registro de evidencia (flujo agent): `useFileManager.saveOne` (`hooks/useFileManager.ts:519-597`) primero
  copia/mueve el archivo a la carpeta del caso en Tatana (**no necesita el JWT**) y después lo registra con
  `PUT /api/cases/{id}/evidence` (`api.registerEvidence`, `api.ts:1201`). Si el registro falla con 401, el archivo **ya
  quedó en la carpeta del caso** pero no en el manifiesto. El registro es idempotente por nombre + SHA-256 desde #61
  (`docs/hu-wizard-concurrencia-pestanas.md`, D4 A: re-registro con el mismo hash = no-op sin evento).
- **Ya existe una recuperación parcial:** al retomar un caso, `restoreCaseEvidence` (`useFileManager.ts:736-776`) lista
  la carpeta del caso en Tatana y **encola para registrar los archivos que están en la carpeta y no en el manifiesto**
  (`extras`, `:758` y `:773`). O sea, después de volver a iniciar sesión y tocar "Retomar", las capturas y grabaciones
  que ya estaban en la PC se registran solas. Lo que se pierde hoy al redirigir al login: los Blobs del navegador que
  todavía no se copiaron a Tatana (`pendingBlobs`, p. ej. fotos de cámara web), el punto exacto del wizard (paso,
  textos sin autoguardar) y el aviso de qué quedó pendiente.
- Una grabación en curso sigue en Tatana aunque se corte la sesión (no depende del JWT), y #61 ya permite retomarla
  después de recargar.
- #63 (`docs/hu-api-hardening.md`) agregó rate-limit del login por IP y por DNI con `429` + `Retry-After`. Cualquier
  re-login "en el lugar" o endpoint de renovación tiene que convivir con ese límite (la renovación no debería contar
  como intento de login).

**Capturas negras (pantalla apagada o bloqueada).** Hoy se detectan **después** y solo en el navegador:
- `client/src/lib/black-image.ts` (`isMostlyBlack`, canvas sobre la miniatura) y `hooks/useEvidenceNotices.ts` marcan
  la captura con un aviso no bloqueante ("Conservar" / quitar) y el paso 5 lo resume en "Revisá antes de generar"
  (`GenerateStep.tsx:530`). Es lo que eligió #59 (`grabacion-android-robusta`, Duda 6 **A**); la opción **C**
  (prevenir consultando el estado de la pantalla antes de disparar) quedó sin hacer.
- El resultado está solo en memoria (se recalcula al recargar) y no viaja a Tatana ni al backend.
- Tatana dispara la captura sin mirar la pantalla: `ScreenshotController` (`devices/{serial}/screenshot`), el inicio de
  grabación `RecordingController.Start` (`:21`), la cámara (`WebcamController`) y las capturas por AirPlay
  (`AirplayShotController`). No hay ninguna consulta `dumpsys power`/`window` en el agente.
- `/health` ya publica `capabilities` (`HealthController.cs:48`) para que un client nuevo detecte qué sabe hacer un
  Tatana viejo.
- En las grabaciones, #59 ya mencionó la pantalla apagada o bloqueada como una causa posible de cortes al final.
- Evidencia real: en producción, el caso `f8f7d568…` tiene **dos capturas idénticas de 36.518 B** inmediatamente
  después de una de 3,3 MB: muy probablemente la pantalla se apagó o se bloqueó entre una y otra. En la QA del
  2026-10-13 el teléfono 3D salió negro por `Display Power: state=OFF` y se resolvió con `adb input keyevent WAKEUP`.

### Qué es nuevo

1. M1 confirmado (o descartado) con la pestaña visible y, si se reproduce, la vista previa de una grabación larga
   carga en pocos segundos (D6).
2. A 390 px las píldoras de sección no pisan el logo.
3. B2, B3 y B4 corregidos; B1 según D5.
4. Una línea de log al terminar bien cada generación (flujo agent y, según D7, también el viejo), sin datos sensibles.
5. `docs/despliegue-nube.md` explica cómo consultar el VPS como `admin` en solo lectura.
6. La sesión no se corta en medio de una inspección activa (D2, D3) y los registros de evidencia que fallen por sesión
   vencida se reintentan solos al volver a iniciar sesión (D4). **Según D1, este punto puede salir a una HU aparte.**
7. Antes de capturar o grabar, Tatana chequea si la pantalla del celular está encendida y desbloqueada, y la web avisa
   "La pantalla del celular está apagada o bloqueada" antes de disparar (D9). La detección posterior de #59 se
   mantiene como red de seguridad.

### Relación con otras HU

- **#61 `wizard-concurrencia-pestanas`:** define la idempotencia del registro de evidencia por SHA-256 y la retoma de
  grabaciones; la cola de reintentos se apoya en eso.
- **#62 `informe-formato-fixes`:** decisión previa sobre "la causa Causa N°" (D5).
- **#63 `api-hardening`:** rate-limit del login (la renovación no debe chocar con él) y política de errores
  `{ error, code }`.
- **#64 `wizard-pulido-ux`:** dejó fuera el solape del logo a 390 px (su B4) con la condición de "no empeorarlo"; esta
  HU lo resuelve (M2).
- **`usuarios-locales`:** claim `pca`, `SessionValidator`, `session-notice` y `SessionWatcher`; cualquier cambio de
  sesión respeta que cambiar la contraseña o suspender la cuenta corte la sesión.

---

## Criterios de aceptación

```gherkin
Feature: Pulido después de la QA final

  Background:
    Given el perito con sesión iniciada en la web
    And Tatana corriendo en la PC con un Android conectado por USB

  # ── M1: vista previa de grabaciones largas ──

  Scenario: Confirmar M1 con la pestaña visible (paso previo obligatorio)
    Given la pestaña del wizard está visible y en primer plano
    When grabo unos 3 minutos y detengo
    And grabo 10 minutos o más y detengo
    Then se anota en el progress cuánto tarda cada vista previa en mostrar duración y primer cuadro
    And se anota qué URL usa el <video> en ese momento (raíz de Tatana o carpeta del caso) y si la respuesta trae "Accept-Ranges"
    # Si con la pestaña visible carga en menos de 3 s, M1 se cierra como "no reproduce" y no se cambia el código de la vista previa.

  Scenario: La vista previa de una grabación larga carga rápido
    Given M1 se reprodujo con la pestaña visible
    When detengo una grabación de 10 minutos
    Then en menos de 3 s (con la pestaña visible) la tarjeta muestra el primer cuadro o un póster, y la duración
    And puedo adelantar a cualquier punto del video sin esperar a que baje entero
    And si el archivo todavía se está procesando, la tarjeta muestra "Preparando vista previa…" en lugar de un spinner sin fin
    And cuando termina de procesarse, el <video> se carga solo, sin recargar la página

  Scenario: Una vista previa que no carga se puede reintentar
    Given el <video> no llegó a cargar metadata en 10 s
    Then la tarjeta muestra "La vista previa tarda más de lo normal" con un botón "Reintentar"
    And "Reintentar" vuelve a montar el <video> con la misma URL
    And el archivo de evidencia no se toca

  # ── M2: header a 390 px ──

  Scenario: Las secciones no pisan el logo
    Given la ventana mide 390 px de ancho (y también 360 px)
    When abro el dashboard o Métricas
    Then la píldora activa no se superpone con el círculo del logo ni con el chip de Tatana ni con el menú de usuario
    And los dos destinos ("Inspecciones" y "Métricas") siguen siendo alcanzables con un toque y con el teclado
    And cada uno tiene nombre accesible y la activa conserva aria-current="page"
    And a 1024 y 1440 px el header se ve igual que hoy

  # ── B1: "la causa Causa N°" (según D5) ──

  Scenario: Texto de la causa en el informe
    Given el tipo de causa es "Causa"
    When genero el informe
    Then el párrafo de la causa se comporta según D5
    And con el tipo "disciplinaria" el párrafo sigue diciendo "en el marco de la causa disciplinaria N° …"

  # ── B2: una sola lista de sugerencias ──

  Scenario: Pasar de una Parte a otra cierra la lista anterior
    Given escribí en "Parte denunciante" y se abrió su lista de sugerencias
    When hago clic (o paso con Tab) a "Parte denunciada"
    Then la lista de "Parte denunciante" se cierra
    And en pantalla hay como mucho una lista de sugerencias abierta
    And lo escrito en "Parte denunciante" se conserva

  # ── B3: banner "Retomando" ──

  Scenario: El banner "Retomando" aparece solo al retomar
    Given retomé el caso QA-FINAL-001 desde "Pendientes de retomar"
    Then en el paso 3 veo "Retomando inspección · QA-FINAL-001"
    When avanzo al paso 4 o 5 y vuelvo al paso 3 (con "Atrás" o con el link "Paso 3" de los faltantes)
    Then el banner "Retomando" ya no aparece

  Scenario: Sin pedidos a archivos temporales que ya no existen
    Given guardé una captura en la carpeta del caso
    Then el wizard no vuelve a pedir esa captura a la raíz de Tatana (no hay 404 de "/files/screenshot_…")

  # ── B4: contador del historial ──

  Scenario Outline: Plural del contador del historial
    Given el historial tiene <total> casos
    When un filtro deja <filtrados>
    Then el contador dice "<texto>"

    Examples:
      | total | filtrados | texto                 |
      | 6     | 1         | 1 de 6 resultados     |
      | 6     | 0         | 0 de 6 resultados     |
      | 1     | 1         | 1 de 1 resultado      |
      | 6     | 3         | 3 de 6 resultados     |

  # ── Log de generación ──

  Scenario: Log de éxito de la generación (flujo agent)
    When un informe del flujo agent termina bien
    Then el backend loguea en nivel Information "Informe generado para caso <id> (ZIP <prefijo del hash>, DOCX <prefijo del hash>)"
    And la línea no incluye contraseña del ZIP, número de expediente, carátula, nombres, DNI, rutas de la PC ni el token

  Scenario: Fallo de generación sin cambios
    When la generación falla
    Then se sigue logueando el error como hoy

  # ── Documentación del VPS ──

  Scenario: Consultar el VPS como admin
    Given entré al servidor como "admin"
    When sigo docs/despliegue-nube.md sección 14
    Then encuentro cómo ver logs, estado de contenedores y espacio en disco sin cambiar de usuario (con sudo)
    And cómo abrir una consulta de solo lectura a Mongo sin imprimir secretos en pantalla ni en el historial
    And qué no hacer como admin (editar factum.env, "up", "down", reiniciar) y que eso se hace como deploy

  # ── Prevenir capturas negras (pantalla apagada o bloqueada) ──

  Scenario: Pantalla apagada antes de capturar (Android)
    Given la pantalla del Android está apagada
    When toco "Pantalla" (captura) o "Grabar"
    Then Tatana consulta el estado de la pantalla antes de disparar
    And la web muestra "La pantalla del celular está apagada o bloqueada. Encendela y desbloqueala para capturar."
    And según D9 la captura no se dispara (bloqueo) o se ofrece "Capturar igual"
    And no se crea ningún archivo de evidencia hasta que el perito confirme o reintente

  Scenario: Pantalla encendida pero bloqueada (Android)
    Given la pantalla del Android está encendida y en la pantalla de bloqueo
    When toco "Pantalla" o "Grabar"
    Then veo el mismo aviso de pantalla apagada o bloqueada

  Scenario: Pantalla encendida y desbloqueada
    Given la pantalla del Android está encendida y desbloqueada
    When toco "Pantalla"
    Then la captura sale como hoy, sin aviso
    And el chequeo no agrega más de 1 s a la captura

  Scenario: Reintentar después de desbloquear
    Given vi el aviso de pantalla apagada o bloqueada
    When enciendo y desbloqueo el celular y toco "Reintentar"
    Then Tatana vuelve a chequear y, si está bien, captura
    And un doble toque en "Reintentar" saca una sola captura (como #61)

  Scenario: El estado de la pantalla no se puede saber
    Given el fabricante no expone el estado en "dumpsys", el comando falla o tarda más que el límite
    When toco "Pantalla" o "Grabar"
    Then el comportamiento es el de D9 (recomendado: capturar como hoy, sin aviso, y dejar que actúe la detección posterior)
    And Tatana registra en su log que el chequeo no fue concluyente (sin datos del caso)

  Scenario: iPhone (según D9)
    Given un iPhone conectado
    When toco "Pantalla" o "Grabar"
    Then se aplica lo que defina D9 para iOS
    And si no hay chequeo para iOS, la captura sale como hoy y sigue la detección posterior

  Scenario: Tatana viejo sin el chequeo
    Given Tatana no anuncia la capacidad del chequeo de pantalla en /health
    Then la web captura como hoy, sin aviso previo ni errores

  Scenario: Pantalla que se apaga durante una grabación
    Given estoy grabando y la pantalla del celular se apaga o se bloquea
    Then (si D9 lo incluye) la web avisa "La pantalla del celular se apagó: la grabación sigue pero puede salir negra"
    And la grabación no se detiene sola

  Scenario: La detección posterior sigue funcionando
    Given una captura salió negra igual (chequeo no concluyente, iPhone o "Capturar igual")
    Then la tarjeta muestra el aviso de captura negra de #59 y el paso 5 la lista en "Revisá antes de generar"

  # ── Sesión larga (según D1, puede salir a otra HU) ──

  Scenario: Aviso antes de que venza la sesión
    Given mi sesión vence en menos de <aviso> minutos (según D3)
    Then veo un aviso no bloqueante "Tu sesión vence a las HH:MM" con la acción "Seguir conectado"
    And el aviso lo anuncia el lector de pantalla una sola vez
    When toco "Seguir conectado"
    Then la sesión se extiende según D2 sin perder el paso ni lo cargado en el wizard

  Scenario: Renovación con actividad (si D2 elige renovación)
    Given estoy trabajando en el wizard (capturo, escribo, navego)
    When la sesión está por vencer
    Then se renueva sola sin interrumpirme
    And nunca supera el tope absoluto de D3 desde el inicio de sesión

  Scenario: Sin actividad la sesión vence igual
    Given dejo la web abierta sin usarla más que el tiempo de inactividad de D3
    Then la sesión vence
    And la próxima acción me lleva a iniciar sesión con el aviso de sesión vencida, como hoy

  Scenario: Volver a iniciar sesión sin perder el wizard
    Given la sesión venció en medio de una inspección (según D2/D4)
    When un pedido devuelve 401
    Then veo un diálogo para volver a iniciar sesión sobre el wizard, sin perder el paso, los textos ni la lista de evidencia
    When inicio sesión con el mismo usuario
    Then sigo exactamente donde estaba
    And si inicio sesión con otro usuario, el wizard se descarta y voy al dashboard

  Scenario: Registros de evidencia que fallaron por sesión vencida
    Given guardé una grabación en la carpeta del caso en esta PC
    And su registro en el expediente falló con 401
    Then la evidencia queda como "pendiente de registrar" (no como "error")
    When vuelvo a iniciar sesión
    Then el registro se reintenta solo, una vez por archivo
    And no aparece un evento "evidencia agregada" duplicado (idempotente por nombre + SHA-256, #61)
    And si el reintento falla por otro motivo, el archivo pasa a "error" con el mensaje de hoy

  Scenario: La cola no deja datos de la causa en el navegador
    Given hay registros pendientes por sesión vencida
    Then en almacenamiento persistente del navegador no queda número de expediente, carátula, nombres ni rutas (según D4)

  Scenario: Cambio de contraseña o cuenta suspendida siguen cortando la sesión (modo local)
    Given la cuenta se suspendió o se cambió su contraseña desde otra sesión
    Then la renovación falla
    And la sesión se corta como hoy, con el aviso que corresponde

  Scenario: Una descarga en curso no se corta por la renovación
    Given estoy descargando el ZIP o el DOCX
    When la sesión se renueva
    Then la descarga sigue
```

---

## Datos que se registran

| Dato | Obligatorio | Uso |
|---|---|---|
| Línea de log "Informe generado …" (id del caso + prefijos de los hashes de ZIP y DOCX) | sí (al terminar bien) | Diagnóstico en producción. No se guarda en Mongo. |
| Momento de inicio de sesión dentro del token (claim nuevo, p. ej. `auth_time`), si D2 = A | sí, si D2 = A | Calcular el tope absoluto de la renovación. |
| Refresh tokens en Mongo (colección nueva, hash del token + usuario + vencimiento + revocado), si D2 = B | sí, si D2 = B | Rotación y revocación. Con D2 = A no hay datos nuevos en la base. |
| Cola de registros pendientes | — | Según D4: en memoria (recomendado) no se persiste nada. |
| Estado de la pantalla al capturar (encendida/bloqueada/desconocido) | no | Solo para el aviso en la web y el log de Tatana. No se guarda en el caso ni en la cadena de custodia (salvo que D9 diga lo contrario). |

No se modifican documentos existentes (regla dura de datos de `AGENTS.md`). M2, B2, B3, B4 y la documentación no
registran datos.

---

## Diseño UX/UI

Todo en `client/` (web). `agent-ui/` no cambia. Sistema de diseño `fx-*`, `FxBanner`, `FxTip`; temas claro/oscuro,
`prefers-reduced-motion`, solo teclado, 360/390/1024/1440 px.

**M1 (si se reproduce).** En la tarjeta del video:
- Mientras Tatana termina de procesar/importar: superficie del video con póster/miniatura (D6) o fondo neutro, ícono y
  "Preparando vista previa…" (`role="status"`), sin spinner eterno.
- A los 10 s sin metadata: "La vista previa tarda más de lo normal." + botón secundario "Reintentar" (foco visible,
  nombre accesible "Reintentar vista previa de <archivo>").
- El texto de ayuda deja claro que la evidencia está guardada aunque la vista previa no cargue.

**M2.** A menos de `sm`, las secciones no pueden desbordar hacia la izquierda: alinear al inicio cuando no entran
(o achicar padding/tipografía), y como último recurso mostrar solo ícono + `aria-label` y `FxTip` en la inactiva. La
decisión fina es de la SDD con `ui-ux-pro-max`; criterio: a 360 y 390 px nada se superpone y el objetivo táctil
sigue siendo de 36 px o más.

**B2.** Sin cambio visual: al salir de un combobox su lista se cierra.

**B3.** El banner "Retomando" se muestra al entrar al paso 3 por una retoma y se va al salir de ese paso.

**B4.** "N de M resultados" / "1 de 1 resultado" (pluraliza por el total M).

**Sesión (si queda en esta HU).**
- **Aviso previo:** `FxBanner` informativo arriba del contenido (o toast persistente), no modal: "Tu sesión vence a las
  HH:MM." + botón "Seguir conectado". Con renovación automática (D2 = A) el aviso solo aparece si no hubo actividad
  reciente o si se acerca el tope absoluto ("Por seguridad, vas a tener que volver a iniciar sesión a las HH:MM").
- **Re-login en el lugar:** diálogo modal sobre el wizard ("Tu sesión venció. Iniciá sesión para seguir; lo que
  cargaste no se perdió."), con DNI precargado y bloqueado (cambiar de usuario = "Salir"), contraseña, errores del
  login de hoy (incluido `429` con `Retry-After` de #63). Foco en la contraseña; Escape no lo cierra (la alternativa es
  "Salir", que hace lo de hoy). En modo `external` y `dev` el formulario es el mismo que el login actual de ese modo.
- **Evidencia pendiente:** en la bandeja del paso 3 el archivo muestra "Pendiente de registrar: iniciá sesión" (tono
  advertencia, no error); al reintentar bien pasa a "guardado" como hoy.
- Fuera del wizard (dashboard, Métricas, admin), el 401 puede seguir navegando al login como hoy (D2).

**Documentación.** Sección nueva en `docs/despliegue-nube.md` §14, en el mismo tono "seguila en orden" del documento.

**Pantalla apagada o bloqueada (paso 3 y paso 2 si captura desde ahí).**
- Al tocar "Pantalla"/"Grabar" el botón pasa a ocupado ("Verificando pantalla…") mientras Tatana chequea (tope
  ~1 s).
- Si está apagada o bloqueada: `FxBanner` de advertencia junto a los controles de captura, con ícono de candado/
  pantalla, texto "La pantalla del celular está apagada o bloqueada. Encendela y desbloqueala para capturar." y
  acciones "Reintentar" (primaria) y, si D9 = B, "Capturar igual" (secundaria). `role="alert"` una sola vez; el foco
  va a "Reintentar".
- Si D9 incluye el aviso durante la grabación: banner informativo, no bloqueante, que desaparece si la pantalla vuelve.
- Opcional (SDD): el chip/ficha del equipo puede mostrar "pantalla apagada" junto al estado de conexión.
- Tono y textos coherentes con el aviso posterior de captura negra de #59.

---

## Fuera de alcance

- Pruebas físicas que la QA dejó pendientes y no son de esta HU: desconectar USB a mitad de captura/grabación, bloquear
  la pantalla mientras graba, iPhone, Windows 11 (salvo la grabación de 10 min con pestaña visible, que es el paso
  previo de M1).
- Cambiar el formato de grabación, el remux o el post-proceso de Tatana (más allá de D6).
- Rediseñar el header o el sistema de navegación (solo arreglar el solape a 360-390 px).
- Reescribir la plantilla del informe más allá de lo que elija D5.
- Logs de auditoría nuevos, access log de Caddy (se mantiene apagado a propósito) o loguear datos de la causa.
- Scripts nuevos en el VPS o cambios de permisos de `factum.env` (solo documentación).
- Sesión en `agent-ui/` (Electron) o en Tatana; login con proveedores externos (SSO); "recordarme" de días; cerrar
  sesión en todos los dispositivos.
- Persistir el estado completo del wizard entre recargas (más allá de lo que ya hace la retoma).
- Encender o desbloquear el celular desde Tatana (`input keyevent WAKEUP`, deslizar, PIN): el perito lo hace a mano;
  Tatana nunca ingresa credenciales del equipo.
- Mantener la pantalla encendida durante la grabación (`svc power stayon`): lo evaluó #59 y cambia la configuración
  del equipo peritado.
- Borrar o marcar en el informe las capturas negras ya tomadas (los casos existentes, como `f8f7d568…`, no se tocan).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- **M1:** primero medir (escenario "Confirmar M1"). Sospechosos, en orden: `FilesController.Download` sin
  `enableRangeProcessing` (Tatana, raíz) y con `Content-Disposition` de adjunto; el `<video>` montado mientras el
  archivo aún se escribe/remuxa (primera respuesta truncada o colgada); el cambio de URL raíz → carpeta del caso. Ver
  D6 sobre si se toca Tatana. El 404 de B3 (`/files/screenshot_…png`) es del mismo origen: una URL de la raíz que
  quedó en algún estado después de importar.
- **M2:** `AppNavbar.tsx:71` y `:139` (`justify-center` con desborde). Verificar con el chip de Tatana de #64 visible.
- **B2:** cerrar el panel del `AutoComplete` en blur (o cuando otro combobox abre el suyo) en `CatalogAutoComplete`,
  sin romper el clic en una opción de la lista (el blur ocurre antes del clic).
- **B3:** limpiar `isResuming` al salir del paso 3 (o derivarlo de "se entró al paso 3 por `proceedResume`").
- **B4:** `CaseHistory.tsx:185`.
- **Log:** en `AgentGeneration.cs`, después de `RecordEventAsync(ok)` (`:378`): `LogInformation("Informe generado
  para caso {CaseId} (ZIP {ZipHash}, DOCX {ReportHash})", …)` con los hashes recortados (p. ej. 8 caracteres). Mismo
  criterio en `CaseService` si D7 = A. Nada de `pending.Password`, `NroReferencia`, `ZipFilename` (lleva datos de la
  causa) ni `zipLocation` (rutas de la PC).
- **Docs:** comandos tipo `sudo docker compose -f … --env-file /srv/factum/config/factum.env logs --tail 200 backend`
  o `sudo docker logs --tail 200 <contenedor>`, `sudo docker ps`, y para Mongo `sudo docker exec -it <mongo> mongosh …`
  con una consulta de solo lectura y sin pasar la contraseña por la línea de comandos; o bien `sudo -iu deploy` y
  `compose.sh` (lo que el doc ya dice). La SDD confirma nombres reales de contenedores y del archivo compose en
  `deploy/cloud/`.
- **Sesión:** si D2 = A, endpoint `POST /api/auth/refresh` (`[Authorize]`, fuera de la política de rate-limit del
  login, con su propio límite razonable) que reemite el token con el mismo `auth_time` y el mismo `pca`, y en `local`
  pasa por `SessionValidator` como cualquier pedido. El cliente agenda la renovación según el `exp` del token y la
  actividad del usuario, coordinada entre pestañas (una sola renueva; `storage` event sobre `factum_token`). El
  interceptor de 401 (`notifyAuthFailure`) distingue "estoy en el wizard" → diálogo de re-login vs. resto → navegación
  de hoy. `?token=` de las descargas: un token renovado no invalida al anterior hasta su `exp` (sin revocación), así
  que una descarga en curso no se corta.
- **Cola (D4 A):** en `useFileManager`, un 401 en `registerEvidence` marca el archivo como "pendiente de registrar" y
  lo deja en una cola en memoria; al recuperar la sesión se re-encola con `enqueueSave` (el camino `storedInCase` de
  `saveOne` ya evita volver a importar). Como respaldo ya existe la reconciliación de `restoreCaseEvidence` al retomar.
- **Pantalla (Tatana):** Android, antes de `screencap` y del inicio de scrcpy: `adb shell dumpsys power`
  (`mWakefulness=Awake|Asleep|Dozing`, o `Display Power: state=ON|OFF`) y `adb shell dumpsys window` (bloqueo:
  `mDreamingLockscreen`, `isStatusBarKeyguard`/`mShowingLockscreen`, o `dumpsys trust`/`KeyguardController` según
  versión). Varía por fabricante y versión de Android: probar al menos con el Samsung SM-M127F (Android 11) y otro
  equipo; parseo tolerante con resultado `on | off | locked | unknown` y tests unitarios del parseo con salidas reales
  en `server/tests/Factum.Agent.Tests`. Timeout corto (~1 s) → `unknown`. Exponer como campo en el error de la
  captura (p. ej. `409 { code: "screen_off" | "screen_locked" }`) y/o endpoint de consulta, más capacidad nueva en
  `/health` (`capabilities`). iOS: la SDD evalúa si pymobiledevice3 da algo confiable (D9). No cambiar nada en el
  equipo (solo lectura). La SDD decide si el chequeo vive en Tatana para todas las rutas (screenshot, recording start,
  AirPlay) o solo en las de Android.
- **Pantalla (client):** manejar el código nuevo en `agentErrorMessage`/los handlers de captura del paso 3; mantener
  `isMostlyBlack` como está.
- Contrato client ↔ server: si hay endpoint nuevo, la SDD lo lista en **Contrato compartido** con nombres
  `snake_case_lower` (p. ej. `{ token, expires_at }`).
- Regla dura de datos: las pruebas no tocan casos ni archivos existentes; limpian solo por sus `_id`.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué.

**D1. ¿Partir la HU?** La parte de sesión toca auth del backend, el interceptor del cliente y la cola de evidencia;
el resto son arreglos chicos.
- A) **Partir en dos:** #73 queda con M1, M2, B1-B4, log y documentación; la sesión larga (punto 6) va a una HU nueva
  (p. ej. `sesion-larga`, toca backend + client) que se afina y valida con D2-D4.
- B) Una sola HU con todo.
- C) Tres HU: pulido visual (M2, B2-B4), M1 + log + docs, y sesión.
- D) Tres HU por el lado que tocan: #73 con lo chico (M1, M2, B1-B4, log, docs), sesión larga aparte, y
  **pantalla apagada/bloqueada** aparte (toca Tatana, necesita versión nueva del agente y pruebas con equipos reales).
- **Recomendada: A.** La sesión es lo de mayor riesgo (seguridad y pérdida de evidencia) y merece su propia SDD y su
  propia revisión; mezclarla con arreglos de una línea hace que un rechazo del reviewer en auth frene arreglos ya
  listos. C fragmenta demasiado cosas que son todas chicas. Si se elige A, D2-D4 se validan igual ahora y la HU nueva
  las hereda. El chequeo de pantalla (punto 7) queda en #73 con A: es acotado (una consulta de solo lectura en
  Tatana + un aviso en el paso 3) y comparte paso y pruebas físicas con M1. Si preferís no atar #73 a una versión
  nueva de Tatana, D es la alternativa (M1 con D6 B también tocaría Tatana).

**D2. Estrategia de sesión.**
- A) **Renovación deslizante con tope absoluto:** `POST /api/auth/refresh` reemite el JWT mientras el usuario está
  activo y el token sigue vigente; el token conserva el momento del login y no se renueva pasado el tope (D3). Más
  re-login en el lugar (diálogo sobre el wizard) para cuando igual vence. Sin datos nuevos en la base; token sigue en
  `localStorage`.
- B) **Refresh token:** access token corto (15-30 min) + refresh token rotativo en cookie `HttpOnly` y colección en
  Mongo para revocar. Más seguro ante robo del access token, pero obliga a CORS con credenciales, `SameSite`/CSRF,
  cambios de Caddy y a resolver las descargas con `?token=` (hoy el token viaja en la URL).
- C) **Solo aviso + re-login en el lugar:** sin renovación; avisar antes de vencer y, al vencer, diálogo de login sin
  perder el wizard.
- D) Solo subir `FACTUM_JWT_EXPIRY_HOURS` (p. ej. a 12 h).
- **Recomendada: A.** Resuelve el caso real (perito activo que queda sin sesión) sin cambiar el modelo de seguridad
  actual: un token robado sigue valiendo como mucho hasta su `exp`, igual que hoy, y el tope absoluto evita sesiones
  eternas. B es la solución "de manual" pero es una HU grande por sí sola y rompe las descargas por URL. C sigue
  cortando al perito en medio de una grabación (solo le avisa). D alarga la ventana de un token robado para todos,
  activos o no.
- Impacto por modo: en `local` la renovación pasa por `SessionValidator` (suspensión y cambio de contraseña siguen
  cortando). En `external` y `dev` no se vuelve a consultar al proveedor/base al renovar: el tope absoluto es el límite
  (en `external`, una baja en el sistema externo recién se nota al próximo login). Si eso no es aceptable para
  `external`, la opción es no renovar en ese modo (solo aviso, como C).

**D3. Duraciones.**
- A) **Token de 2 h, renovación con actividad cuando falten menos de 30 min, aviso a los 10 min del vencimiento si no
  hubo actividad, tope absoluto de 12 h desde el login.** (La inactividad de más de 2 h corta la sesión.)
- B) Token de 8 h (como hoy), renovación en la última hora, tope de 12 h.
- C) Configurables por `factum.env` (`FACTUM_JWT_EXPIRY_HOURS` ya existe; sumar `FACTUM_SESION_TOPE_HORAS`) con los
  valores de A como default.
- **Recomendada: C con los valores de A.** 2 h de inactividad es más seguro que las 8 h de hoy (una PC olvidada con la
  sesión abierta deja de servir antes) y con actividad cubre una jornada de 12 h. Configurable porque cada cliente
  (nube vs. instalación local en Windows) puede querer otra cosa, y la variable de vencimiento ya existe en los dos
  despliegues.

**D4. Dónde vive la cola de registros pendientes.**
- A) **En memoria de la pestaña**, más re-login en el lugar (D2) para no perderla; si la pestaña se cierra o se recarga,
  la reconciliación que ya existe al "Retomar" (archivos en la carpeta del caso que no están en el manifiesto) los
  registra igual.
- B) En `localStorage`/IndexedDB, para que sobreviva a recargas.
- **Recomendada: A.** Los archivos ya están a salvo en la carpeta del caso en Tatana, y Tatana es la fuente para
  reconstruir el registro; persistir la cola en el navegador dejaría id de caso, nombres de archivo y la ruta de la
  carpeta (que incluye el número de expediente, p. ej. `QA_FINAL_001_…`) en un almacenamiento sin cifrar y
  compartido entre pestañas. B no agrega recuperación real frente a A + retoma. Lo que A no salva son los Blobs del
  navegador que todavía no llegaron a Tatana (fotos de cámara web): con re-login en el lugar se conservan; con una
  recarga se pierden, igual que hoy.

**D5. "la causa Causa N°" (B1).** En #62 elegiste no cambiarlo (D5 de `informe-formato-fixes`). La QA lo vuelve a
marcar porque "Causa" quedó como valor precargado.
- A) **Dejarlo como está** (respetar la decisión de #62) y solo aclarar en el placeholder/ayuda del campo que el tipo
  va sin la palabra "causa" ("Ej.: disciplinaria, civil, penal", "sin repetir «causa»").
- B) Cambiar la frase de la plantilla a "en el marco de {tipoCausa} N° {numeroCausa}" (el perito escribe "la causa
  disciplinaria" o "el expediente civil"); obliga a revisar portada, ficha y encabezado, que usan el mismo valor.
- C) Regla en el backend: si `{tipoCausa}` empieza con "causa" (sin distinguir mayúsculas), no repetir la palabra en
  esa frase.
- D) No hacer nada, ni siquiera la ayuda.
- **Recomendada: A.** Mantiene lo que decidiste hace días y ataca el origen (el valor que se carga), con un cambio de
  texto de una línea en `client/`. B cambia el sentido de un campo que ya tiene casos cargados; C es una regla de texto
  como la que se descartó en #62 (D5 B).

**D6. M1, si se reproduce con la pestaña visible: ¿hasta dónde se toca?**
- A) **Solo `client/`:** no montar el `<video>` hasta que el archivo esté listo (import/validación terminados), estado
  "Preparando vista previa…", timeout de 10 s con "Reintentar" y preferir la URL de la carpeta del caso (que ya tiene
  Range).
- B) A + **Tatana:** `enableRangeProcessing: true` (y `Content-Disposition` inline) en `GET /files/{nombre}`. Sale en la
  próxima versión de Tatana; la web tiene que seguir funcionando con un Tatana viejo.
- C) B + miniatura/póster generado por Tatana (primer cuadro con ffmpeg) y un endpoint para pedirla.
- **Recomendada: B, condicionada a la medición.** Si la medición muestra que el `<video>` apunta a la raíz de Tatana
  cuando cuelga, el arreglo de fondo es Range en ese endpoint (una línea, sin riesgo para la evidencia) y A sola lo
  esquiva a medias. C suma un endpoint y ffmpeg extra por cada grabación para algo que con Range ya debería ser
  instantáneo; queda para otra HU si hiciera falta. Si M1 no se reproduce, no se toca nada y se cierra como "no
  reproduce" en el progress.

**D7. Log de éxito: ¿también en el flujo viejo (`server`)?**
- A) **Sí, la misma línea en los dos flujos** (`AgentGeneration` y `CaseService`).
- B) Solo en el flujo agent (lo que pidió el issue).
- **Recomendada: A.** Es una línea más, deja los dos flujos simétricos y evita que el operador piense que un informe
  del flujo viejo "no terminó" por no ver el log.

**D8. Documentación del VPS: ¿qué camino se recomienda como `admin`?**
- A) **`sudo -iu deploy` y `compose.sh` como camino principal** (el que ya usa el doc), y una subsección "Solo mirar,
  como admin" con `sudo docker ps`, `sudo docker logs --tail …` y `sudo docker exec … mongosh` de solo lectura.
- B) Documentar solo `sudo docker …` directo como `admin`.
- C) Cambiar permisos (grupo de lectura para `factum.env`) para que `compose.sh` ande como `admin`.
- **Recomendada: A.** No cambia permisos de un archivo con todos los secretos (C) y deja claro que las operaciones que
  cambian algo se hacen como `deploy`; los comandos de solo lectura con `sudo docker` cubren la consulta rápida sin
  cargar el `factum.env`.

**D9. Pantalla apagada o bloqueada antes de capturar o grabar.** Tiene cuatro partes; se recomienda una opción por
cada una.

*D9.1 ¿Bloquear o solo avisar?*
- A) **Bloquear con salida:** si Tatana dice `off`/`locked`, no se dispara; aviso con "Reintentar" y, como salida
  explícita, "Capturar igual" (por si el perito quiere documentar justamente la pantalla de bloqueo).
- B) Solo avisar: se dispara igual y se muestra el aviso al lado.
- C) Bloquear sin salida.
- **Recomendada: A.** Evita la captura inútil (el caso `f8f7d568…` tiene dos negras idénticas que ahora hay que
  explicar o borrar, y borrar evidencia deja rastro en la custodia), pero no impide documentar la pantalla de bloqueo,
  que en una pericia puede ser relevante. B igual genera el archivo negro; C le quita al perito una decisión que es
  suya.

*D9.2 ¿Qué pasa si el chequeo no es concluyente (fabricante raro, comando que falla o tarda)?*
- A) **Capturar como hoy, sin aviso**, y que actúe la detección posterior de #59; Tatana loguea "no concluyente".
- B) Avisar "No se pudo verificar la pantalla" y pedir confirmación.
- **Recomendada: A.** Un aviso que aparece siempre en ciertos equipos se aprende a ignorar y frena al perito sin
  motivo; la detección posterior ya cubre ese caso.

*D9.3 ¿iPhone?*
- A) **Solo Android en esta HU**; en iPhone la SDD investiga si pymobiledevice3 expone algo confiable y, si no, queda
  la detección posterior.
- B) Android e iPhone, aunque iPhone requiera más trabajo (o un chequeo indirecto, p. ej. capturar y analizar).
- **Recomendada: A.** En Android `dumpsys` es estándar y de solo lectura; en iOS no hay una consulta equivalente
  documentada y la grabación de iPhone va por otros caminos (AirPlay/DVT). Si la SDD encuentra una forma confiable, se
  suma sin cambiar la UX.

*D9.4 ¿Avisar también durante una grabación si la pantalla se apaga?*
- A) **No en esta HU:** chequeo solo al iniciar captura o grabación.
- B) Sí, con un sondeo periódico (p. ej. cada 5-10 s) mientras se graba, aviso no bloqueante sin detener.
- **Recomendada: A.** El pedido es prevenir al disparar; el sondeo durante la grabación agrega carga sobre ADB mientras
  scrcpy está activo y conviene evaluarlo junto con los cortes de grabación de #59. Queda como mejora siguiente.

*(Nota: el estado de la pantalla no se registra en la cadena de custodia en ninguna opción; si querés que "Capturar
igual con la pantalla bloqueada" quede como evento del caso, decilo y se suma.)*

## Validación del usuario (2026-10-13)

El usuario validó **todas las dudas con la opción recomendada**:

- **D1 = A:** se parte la HU. La sesión larga (renovación de sesión, re-login en el lugar, cola de reintentos tras 401) sale a una HU nueva `sesion-larga`. **Esta HU (#73) queda con:** M1 (vista previa de grabaciones largas), M2 (header a 390 px), B1–B4, línea de log de éxito, documentación del VPS y chequeo de pantalla apagada/bloqueada antes de capturar.
- **D2 = A, D3 = C (con los valores de A), D4 = A:** validadas, pero pasan a `sesion-larga` (no se implementan acá).
- **D5 = A:** no se toca la plantilla; se aclara placeholder/ayuda del campo de tipo de causa.
- **D6 = B:** arreglos en el client + `enableRangeProcessing` en `GET /files/{nombre}` de Tatana, condicionado a reproducir la lentitud con la pestaña visible.
- **D7 = A:** la misma línea de log de éxito en los dos flujos (AgentGeneration y CaseService).
- **D8 = A:** camino principal `sudo -iu deploy` + `compose.sh`; subsección "Solo mirar, como admin" con `sudo docker` de solo lectura. No se cambian permisos de `factum.env`.
- **D9.1 = A** (bloquear con "Reintentar" y escape "Capturar igual"), **D9.2 = A** (chequeo inconcluso → captura como hoy), **D9.3 = A** (solo Android), **D9.4 = A** (solo al iniciar, no durante la grabación).
- El estado de la pantalla **no** se registra en la cadena de custodia.

Implica una release nueva de Tatana (D6 y D9).
