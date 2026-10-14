# HU: Sesión que no se corta en inspecciones largas: renovación con tope, re-login en el lugar y reintento de registros tras 401

**Slug:** `sesion-larga` · **Issue:** #74
**Apps afectadas:** backend API (`server/src/Factum.Backend`) y `client/` (web). También la configuración de los
dos despliegues (`deploy/cloud/` y `deploy/windows/`) y su documentación. **No toca** `agent-ui/` (Electron) ni el
agente Tatana (`server/src/Factum.Agent`). Tatana no autentica a nadie y no lleva token
(`HealthController.cs:52-54`).

**Como** perito que hace una inspección larga con el asistente
**quiero** que mi sesión siga viva mientras trabajo y que, si igual vence, pueda volver a iniciar sesión ahí mismo sin
perder lo cargado
**para que** una jornada completa de trabajo no termine con registros de evidencia fallidos ni con el asistente
reiniciado a mitad de la inspección.

**Origen:** se separó de la HU #73 (`pulido-post-qa-final`) por su D1 = A, validada el 2026-10-13. Esa HU tenía tres
decisiones de sesión que ya están validadas: D2, D3 y D4. Esta HU las hereda con la misma numeración (ver "Decisiones
validadas"). En los logs de producción aparecieron varios "Token inválido o expirado" durante la inspección de un
perito. El JWT de 8 h venció en medio del trabajo y los registros de evidencia que siguieron
(`PUT /api/cases/{id}/evidence`) fallaron con 401. Tatana igual guardó los archivos en la carpeta del caso, y el
"Retomar" del asistente registra después los que no están en el manifiesto.

---

## Contexto

### Qué existe hoy (arqueología, rama `feat/sesion-larga` = `develop` del 2026-10-13)

**Emisión y validación del token (backend).**
- `AuthService.IssueToken` (`Services/Auth/AuthService.cs:108-126`) emite un JWT HS256. Lleva el claim `user` (solo
  `Dni`, `Name` y `Sigla`), el claim `pca` si el modo es `local` y `exp = ahora + Jwt:ExpiryHours`. No pone `iat` ni
  la hora del login. `JwtOptions.ExpiryHours` vale 8 por defecto (`appsettings.json:15`).
- Configuración: `FACTUM_JWT_EXPIRY_HOURS=8` en `deploy/cloud/factum.env.example:16` y en
  `deploy/windows/.env.example:7`. Se mapea a `Jwt__ExpiryHours` en `deploy/cloud/docker-compose.yml:84` y en
  `deploy/windows/docker-compose.yml:45`, con `:-8` como valor por defecto. También aparece en `docs/despliegue-nube.md:282`
  y en `restaurar.ps1:155`. El `docker-compose.yml` de la raíz, que es el de desarrollo, fija `Jwt__ExpiryHours=8`.
- `ValidateToken` (`:76-106`) verifica la firma y el vencimiento con `ClockSkew = 0`. No mira ni el issuer ni el
  audience.
- `FactumBearerHandler` (`Infrastructure/FactumBearerHandler.cs`) lee el token de `Authorization: Bearer` o de
  `?token=`, que es lo que usan las descargas de ZIP/DOCX y la marca (`api.ts:1279`, `useMyBranding.ts`). Con un token
  vencido o inválido devuelve `401 { error, code: "invalid_token" }` y el mensaje de log es
  **"Token inválido o expirado"**, el que se vio en producción. Después, `SessionValidator.ValidateAsync` controla en
  cada pedido, **solo en modo `local`**, que la cuenta siga activa y que el `pca` coincida. Si no, responde 401
  `session_revoked` o `account_suspended`. En los modos `dev` y `external` no consulta la base
  (`SessionValidator.cs:28`).
- `AuthController` (`Controllers/AuthController.cs`):
  - `login` tiene rate limit por IP (política `login`, `FactumEdge.cs:37-44`) y por DNI (`ILoginThrottle`), los dos de
    #63 `api-hardening`. No hay `GlobalLimiter`: el límite solo se aplica a los endpoints que tienen la política.
  - `logout` es `[AllowAnonymous]` y no revoca nada (`:68-70`). El cliente tampoco lo llama: `api.logout()` solo borra
    el token (`api.ts:893-895`).
  - `change-password` (modo `local`) es el único endpoint que hoy vuelve a emitir un token.
  - **No hay refresh token ni endpoint de renovación.**
- `usuarios-locales` (D6, `docs/hu-usuarios-locales.md:447`) había elegido "8 h como hoy, sin renovación". Esta HU
  reemplaza esa decisión, que el usuario volvió a tomar el 2026-10-13.
- Ya hay tests del token en `server/tests/Factum.Backend.Tests/Auth/` (`AuthServiceTokenTests.cs`,
  `AuthTestDoubles.cs`).

**Manejo del 401 en el cliente.**
- El token se guarda en `localStorage["factum_token"]` (`api.ts:17`). Lo escriben `login` (`:889`) y
  `changePassword` (`:911`). Cada pedido lee el token en el momento (`getToken()` en `send` y en el XHR de subida),
  así que si una pestaña lo actualiza, los pedidos siguientes de **todas** las pestañas ya salen con el token nuevo.
- `notifyAuthFailure` (`api.ts:115-134`) es el único interceptor. Lo llaman `toApiError` y el XHR de `uploadFile`.
  Ante **cualquier** 401 que no venga de `/api/auth/login` ni de `/api/auth/mode`:
  1. borra el token;
  2. deja un aviso en `sessionStorage` (`lib/session-notice.ts`) con `interrupted`, si había una operación larga en
     curso según `activeLongOps`;
  3. dispara `factum:auth` → `session_ended`.

  `SessionWatcher` (`components/shell/SessionWatcher.tsx`) escucha ese evento y hace `router.replace("/")`. Al navegar
  se pierde todo el estado del asistente que está en memoria, en `app/dashboard/page.tsx`: el paso, la lista de
  evidencia, los Blobs pendientes y los textos sin guardar.
- **No hay aviso antes de que venza la sesión.** El perito se entera cuando un pedido falla.
- El cliente no escucha el evento `storage` de `factum_token`. Solo el tema lo hace (`lib/theme.ts:156`). Si una
  pestaña pierde la sesión, las otras no se enteran hasta su propio 401.

**Registro de evidencia en el flujo agent.** `useFileManager.saveOne` (`hooks/useFileManager.ts:519-597`) trabaja en
dos pasos:
1. Copia o mueve el archivo a la carpeta del caso en Tatana. Este paso **no usa el JWT**. Al terminar, el archivo
   queda marcado `storedInCase`, con `sha256` y `size` (`:555-557`).
2. Registra el archivo con `api.registerEvidence` (`:562`).

Si el paso 2 falla con 401, el archivo ya está en la carpeta, pero no en el manifiesto. Un intento posterior no lo
vuelve a importar: el camino `storedInCase` está en `:535`. Desde #61 el registro es idempotente por nombre + SHA-256:
volver a registrar el mismo hash no hace nada y no genera evento (`docs/hu-wizard-concurrencia-pestanas.md`, D4 A).
`runQueue` (`:600`) procesa la cola de a uno. Si un archivo da error, queda en `error` y la cola sigue con el resto.

**Recuperación que ya existe.** Cuando se retoma un caso, `restoreCaseEvidence` (`useFileManager.ts:736-776`) lista
la carpeta del caso en Tatana y vuelve a encolar los archivos que están en la carpeta pero no en el manifiesto
(`extras`, `:752-773`). Hoy, entonces, después del login y de "Retomar", las capturas que ya estaban en la PC se
registran solas.

Lo que se pierde hoy al redirigir al login:
- los Blobs del navegador que todavía no llegaron a Tatana (`pendingBlobs`, por ejemplo fotos de cámara web);
- el paso exacto del asistente;
- los textos que el autoguardado no llegó a mandar. `ReportStep` deja `dirty` y el estado en "error" si el PUT falla
  (`ReportStep.tsx:230-234`);
- el aviso de qué quedó pendiente.

**Otros pedidos del asistente que dependen del JWT:** el autoguardado de los textos del paso 4
(`api.saveReportTexts`), los roles de captura (`saveCaptureRoles`, que ya se reintentan al guardar), los eventos de
auditoría del agente y la generación del informe.

**Grabaciones.** Una grabación en curso vive en Tatana y no depende del JWT. Si la sesión se corta, la grabación sigue.
#61 ya permite retomarla después de recargar la página.

### Qué es nuevo

1. **Renovación deslizante con tope absoluto** (D2 = A). Hay un endpoint nuevo, `POST /api/auth/refresh`, que vuelve a
   emitir el JWT mientras el perito está activo. El token nuevo conserva la hora del login original y deja de
   renovarse al llegar al tope.
2. **Duraciones nuevas y configurables** (D3 = C con los valores de A):
   - el token dura 2 h;
   - se renueva con actividad cuando le quedan menos de 30 min;
   - se avisa 10 min antes de que venza;
   - el tope absoluto es de 12 h desde el login.
3. **Aviso antes de que venza la sesión**, y aviso antes del tope.
4. **Re-login en el lugar.** Si la sesión vence igual dentro del asistente, aparece un diálogo de login encima, sin
   perder el paso, los textos, la lista de evidencia ni los Blobs.
5. **Cola de reintentos en memoria** (D4 = A). Un registro de evidencia que falla con 401 queda "pendiente de
   registrar" y se reintenta solo cuando se recupera la sesión. Si se recarga la página, el "Retomar" de hoy cubre el
   hueco.

### Relación con otras HU

- **#73 `pulido-post-qa-final`:** de ahí sale esta HU, y de ahí vienen D2, D3 y D4.
- **`usuarios-locales`:** claim `pca`, `SessionValidator`, `session-notice`, `SessionWatcher`. Esta HU tiene que
  respetar que un cambio de contraseña o una cuenta suspendida corten la sesión. También reemplaza su D6 (8 h sin
  renovación).
- **#61 `wizard-concurrencia-pestanas`:** la cola de reintentos se apoya en que el registro sea idempotente por
  SHA-256 y en la retoma.
- **#63 `api-hardening`:** el refresh no puede contar como intento de login. Los errores siguen el formato
  `{ error, code }`.

---

## Decisiones validadas (heredadas de #73, 2026-10-13)

> Validadas por el usuario. No se vuelven a discutir en esta HU.

- **D2 = A, renovación deslizante con tope absoluto.**
  - `POST /api/auth/refresh` vuelve a emitir el JWT mientras el perito está activo y el token sigue vigente.
  - El token conserva el momento del login y no se renueva una vez pasado el tope.
  - Si la sesión vence igual, el login aparece **encima del asistente, en el lugar**, sin perder su estado.
  - **No cambia el modelo de seguridad:**
    - el token sigue en `localStorage`;
    - no hay refresh token, ni cookie, ni colección nueva en Mongo;
    - un token robado vale como mucho hasta su `exp`, igual que hoy.
  - Por modo:
    - en `local`, la renovación pasa por `SessionValidator`, así que una suspensión o un cambio de contraseña siguen
      cortando la sesión;
    - en `external` y `dev`, el tope absoluto es el límite (ver S6).
- **D3 = C con los valores de A, configurable en `factum.env`.**
  - Token de **2 h**.
  - Renovación con actividad cuando quedan **menos de 30 min**.
  - **Aviso 10 min** antes del vencimiento.
  - **Tope absoluto de 12 h** desde el login.
  - Se configura con `FACTUM_JWT_EXPIRY_HOURS`, que ya existe, y una variable nueva para el tope, por ejemplo
    `FACTUM_SESION_TOPE_HORAS`.
  - Una inactividad de más de 2 h corta la sesión.
- **D4 = A, la cola vive en la memoria de la pestaña.**
  - La cola de reintentos de registros tras un 401 está en memoria.
  - **No se guardan datos de la causa en el navegador:** ni en `localStorage`, ni en `sessionStorage`, ni en
    IndexedDB.
  - Si se recarga la página, "Retomar" vuelve a registrar lo que quedó en la carpeta del caso en Tatana.

---

## Criterios de aceptación

```gherkin
Feature: Sesión que no se corta en inspecciones largas

  Background:
    Given el backend configurado con token de 2 h y tope de 12 h (valores por defecto)
    And el perito con sesión iniciada en la web
    And Tatana corriendo en la PC con un celular conectado

  # ── Renovación ──

  Scenario: La sesión se renueva sola mientras trabajo
    Given estoy trabajando en el asistente (según la definición de actividad de S1)
    When a mi token le quedan menos de 30 minutos
    Then la web llama a POST /api/auth/refresh y guarda el token nuevo
    And no veo ningún aviso, diálogo ni cambio de pantalla
    And el paso, los textos y la lista de evidencia siguen igual

  Scenario: La renovación conserva la hora del login
    Given inicié sesión a las 08:00
    When la sesión se renueva varias veces durante el día
    Then cada token nuevo lleva la misma hora de inicio de sesión (08:00)
    And ningún token vence después de las 20:00 (tope de 12 h)

  Scenario: El refresh no cuenta como intento de login
    Given llegué al límite de intentos de login por IP o por DNI de #63
    When la web renueva la sesión
    Then la renovación funciona igual
    And no suma intentos al límite del login

  Scenario: El refresh tiene su propio límite
    When se llama a POST /api/auth/refresh muchas veces seguidas con el mismo token
    Then a partir de cierto número el backend responde 429 con Retry-After
    And el token vigente sigue sirviendo hasta su vencimiento

  Scenario: Sin actividad la sesión vence
    Given dejo la web abierta sin usarla
    When pasan los 10 minutos previos al vencimiento sin actividad
    Then veo el aviso "Tu sesión vence a las HH:MM" con el botón "Seguir conectado"
    When no toco nada hasta el vencimiento
    Then la sesión vence (2 h después de la última renovación)

  Scenario: "Seguir conectado" renueva la sesión
    Given veo el aviso de vencimiento
    When toco "Seguir conectado"
    Then la sesión se renueva sin perder el paso ni lo cargado
    And el aviso desaparece
    And el lector de pantalla anuncia "Sesión renovada" una sola vez

  # ── Tope absoluto ──

  Scenario: Aviso antes del tope
    Given me quedan menos de <aviso del tope> minutos para el tope de 12 h (según S3)
    Then veo un aviso no bloqueante "Por seguridad, vas a tener que volver a iniciar sesión a las HH:MM"
    And el aviso ofrece "Iniciar sesión ahora" (según S3)
    And el lector de pantalla lo anuncia una sola vez

  Scenario: Pasado el tope no se renueva
    Given pasaron 12 h desde mi login
    When la web intenta renovar
    Then el backend responde 401 con un code propio del tope (p. ej. "session_max_age")
    And el token vigente sigue sirviendo hasta su exp
    And al vencer aparece el re-login en el lugar (si estoy en el asistente)

  Scenario: El tope vence durante una grabación
    Given estoy grabando la pantalla del celular
    When la sesión vence por el tope
    Then la grabación sigue en Tatana y no se detiene sola
    And se aplica lo que defina S3 para el diálogo de re-login mientras graba
    And al detener, la grabación se guarda en la carpeta del caso
    And su registro queda "pendiente de registrar" hasta que vuelva a iniciar sesión

  # ── Re-login en el lugar ──

  Scenario: Volver a iniciar sesión sin perder el asistente
    Given estoy en el asistente con un caso abierto, en el paso 3, con textos cargados y capturas en la bandeja
    When un pedido al backend devuelve 401 por sesión vencida (invalid_token o session_max_age)
    Then aparece un diálogo de inicio de sesión encima del asistente, sin cambiar de página
    And el asistente queda debajo, sin perder el paso, los textos, la bandeja ni las fotos de cámara web que todavía no se guardaron
    When inicio sesión con el mismo usuario
    Then el diálogo se cierra y sigo exactamente donde estaba
    And la sesión nueva arranca su propio tope de 12 h

  Scenario: Re-login con otro usuario
    Given aparece el diálogo de re-login
    Then el DNI viene precargado y no se puede editar
    When toco "Salir"
    Then el asistente se descarta y voy al login como hoy, con el aviso de sesión terminada

  Scenario: Errores del re-login
    Given aparece el diálogo de re-login
    When pongo una contraseña incorrecta
    Then veo el mismo error que en el login de hoy y el asistente no se toca
    When el backend responde 429 por el límite de #63
    Then veo el mismo mensaje con el tiempo de espera (Retry-After) que muestra el login de hoy

  Scenario: Cuenta suspendida o contraseña cambiada (modo local)
    Given suspendieron mi cuenta o cambiaron mi contraseña desde otra sesión
    When la web intenta renovar o hace cualquier pedido
    Then el backend responde 401 session_revoked o account_suspended, como hoy
    And la sesión se corta como hoy: voy al login con el aviso que corresponde, sin diálogo en el lugar
    # El re-login en el lugar es solo para la sesión vencida, no para la revocada.

  Scenario: Fuera del asistente
    Given estoy en Métricas, en el historial o en la administración (sin caso abierto en el asistente)
    When la sesión vence
    Then el comportamiento es el de S5 (recomendado: ir al login como hoy)

  Scenario: Accesibilidad del diálogo
    Given aparece el diálogo de re-login
    Then el foco va al campo de contraseña
    And el foco queda atrapado dentro del diálogo
    And Escape no lo cierra
    And el diálogo tiene título accesible y se usa bien con teclado y con lector de pantalla

  # ── Cola de registros pendientes ──

  Scenario: Un registro que falla por sesión vencida queda pendiente
    Given guardé una grabación y Tatana la movió a la carpeta del caso
    When su registro en el expediente devuelve 401 por sesión vencida
    Then en la bandeja el archivo dice "Pendiente de registrar: iniciá sesión" (advertencia, no error)
    And no se vuelve a importar ni a copiar el archivo
    And los demás archivos de la cola quedan pendientes igual, sin pasar a "error"

  Scenario: Los pendientes se registran solos al recuperar la sesión
    Given hay registros pendientes por sesión vencida
    When vuelvo a iniciar sesión en el diálogo
    Then cada pendiente se registra solo, una vez por archivo
    And no aparece un evento "evidencia agregada" duplicado (idempotente por nombre + SHA-256, #61)
    And si un reintento falla por otro motivo, ese archivo pasa a "error" con el mensaje de hoy

  Scenario: Otros guardados pendientes se reintentan
    Given el autoguardado de los textos del paso 4 falló por sesión vencida
    When vuelvo a iniciar sesión en el diálogo
    Then se aplica lo que defina S4 (recomendado: el texto pendiente se guarda solo)

  Scenario: Si recargo, "Retomar" lo resuelve
    Given hay registros pendientes por sesión vencida
    When recargo la página o cierro la pestaña e inicio sesión de nuevo
    And retomo el caso desde "Pendientes de retomar"
    Then los archivos que están en la carpeta del caso y no en el expediente se registran solos, como hoy

  Scenario: La cola no deja datos de la causa en el navegador
    Given hay registros pendientes por sesión vencida
    Then en localStorage, sessionStorage e IndexedDB no queda id del caso, número de expediente, carátula, nombres, nombres de archivo ni rutas
    And el aviso de sesión de sessionStorage solo lleva reason e interrupted, como hoy

  # ── Varias pestañas ──

  Scenario: Varias pestañas abiertas
    Given tengo Factum abierto en dos pestañas del mismo navegador
    Then se aplica lo que defina S2 (recomendado: la actividad en cualquiera mantiene viva la sesión, una sola renueva y las dos usan el token nuevo)
    And si vuelvo a iniciar sesión en una, el diálogo de re-login de la otra se cierra solo

  # ── Descargas y compatibilidad ──

  Scenario: Una descarga en curso no se corta por la renovación
    Given estoy descargando el ZIP o el DOCX (URL con ?token=)
    When la sesión se renueva
    Then la descarga sigue
    And las descargas que inicio después usan el token nuevo

  Scenario: Tokens emitidos antes de esta versión
    Given tengo un token emitido antes del despliegue (sin la hora del login)
    Then sigue valiendo hasta su exp, como hoy
    And al intentar renovarlo el backend no lo renueva (code de tope o equivalente) y al vencer se pide login

  Scenario: Configuración
    Given el operador pone FACTUM_JWT_EXPIRY_HOURS y la variable del tope en factum.env (nube) o .env (Windows)
    When reinicia el backend
    Then el token y el tope usan esos valores
    And si el tope es menor que la duración del token, el backend no arranca o avisa al arrancar (lo decide la SDD) y nombra las dos variables

  Scenario: El refresh queda en el log según S7
    When la sesión se renueva o se rechaza una renovación
    Then el backend loguea según S7
    And la línea nunca incluye el token, la contraseña ni datos de un caso
```

---

## Datos que se registran

| Dato | Obligatorio | Uso |
|---|---|---|
| Hora del inicio de sesión dentro del JWT (claim nuevo, por ejemplo `auth_time`, en segundos Unix) | sí, en todo token nuevo | Calcular el tope absoluto. El refresh la copia sin cambios. Va solo en el token: **nada nuevo en Mongo**. |
| `iat` del JWT (estándar) | recomendado | Diagnóstico y cálculo de la ventana de renovación. Lo decide la SDD. |
| Vencimiento y tope en la respuesta de `login`, `refresh` y `change-password` (por ejemplo `expires_at` y `session_max_at`, en snake_case) | sí | Que el cliente programe la renovación y los avisos sin decodificar el JWT. Los nombres exactos van en el **Contrato compartido** de la SDD. |
| Configuración: `FACTUM_JWT_EXPIRY_HOURS` (ya existe; el valor por defecto pasa de 8 a 2) y la variable nueva del tope (por ejemplo `FACTUM_SESION_TOPE_HORAS`, por defecto 12) | no (tienen valor por defecto) | Duraciones de D3 por despliegue. Ver S8 para las instalaciones que ya tienen `8` escrito. |
| Cola de registros pendientes | — | **Solo en la memoria de la pestaña** (D4 = A). No se persiste. |
| Línea de log del refresh | según S7 | Diagnóstico. No se guarda en Mongo, no es evento de caso y no entra en la cadena de custodia. |

No se modifican documentos existentes (regla dura de datos de `AGENTS.md`). No hay colecciones ni índices nuevos.

---

## Diseño UX/UI

Todo en `client/` (web). `agent-ui/` no cambia. Usa el sistema de diseño `fx-*`, `FxBanner` y `FxTip`, los temas
claro y oscuro y `prefers-reduced-motion`. Tiene que funcionar solo con teclado y con lector de pantalla, a 360, 390,
1024 y 1440 px.

**Renovación silenciosa.** No se ve nada. Si falla por un problema de red, se reintenta en silencio mientras el token
siga vigente. El perito solo ve algo si la sesión está por vencer.

**Aviso de vencimiento por inactividad** (10 min antes):
- `FxBanner` informativo y fijo arriba del contenido. No es modal y no tapa los controles de captura.
- Texto: "Tu sesión vence a las HH:MM por inactividad." y botón primario "Seguir conectado".
- `role="status"`, anunciado una sola vez. No mueve el foco.
- Si la renovación funciona, el banner se va y un `role="status"` anuncia "Sesión renovada" (visualmente puede ser un
  toast breve).

**Aviso del tope absoluto** (según S3):
- `FxBanner` de advertencia, no modal.
- Texto: "Por seguridad, vas a tener que volver a iniciar sesión a las HH:MM. Lo que cargaste no se pierde."
- Acción secundaria "Iniciar sesión ahora", que abre el mismo diálogo de re-login de forma anticipada para que el
  perito elija el momento.
- Se puede cerrar ("Entendido"). Si se cierra, vuelve a aparecer a los 5 min del tope.

**Diálogo de re-login en el lugar** (dentro del asistente):
- Modal sobre el asistente con el título "Tu sesión venció" y el texto "Iniciá sesión para seguir. Lo que cargaste
  en esta inspección no se perdió."
- Formulario:
  - el **DNI precargado y de solo lectura**;
  - el campo de contraseña;
  - en modo `external`, el usuario si el modo lo pide: los mismos campos que `LoginForm` en ese modo;
  - en modo `dev`, el formulario de `dev`.
- Botón primario "Iniciar sesión" y secundario "Salir". "Salir" descarta el asistente y lleva al login de hoy, que es
  la única forma de cambiar de usuario.
- Errores y estados iguales a los del login actual: credenciales inválidas, cuenta bloqueada y `429` con el tiempo de
  espera de #63, servidor que no responde.
- Mientras el diálogo está abierto, debajo puede seguir corriendo lo que no depende del backend: una grabación en
  Tatana, el contador de grabación y las vistas previas.
- Si hay pendientes, el diálogo lo dice con un resumen sin datos del caso: "Hay 3 archivos pendientes de registrar: se
  van a registrar cuando inicies sesión."
- Foco inicial en la contraseña, foco atrapado y Escape no cierra. Al cerrar, el foco vuelve al elemento que lo tenía.

**Bandeja de evidencia (paso 3) y resumen del paso 5.**
- Estado nuevo "Pendiente de registrar": tono de advertencia (ámbar, no rojo), ícono de reloj o candado y texto
  "Pendiente de registrar: iniciá sesión".
- Cuando el reintento sale bien, el archivo pasa a "Guardado", como hoy.
- El paso 5 no deja generar mientras haya pendientes. Usa el mismo mecanismo que hoy bloquea con archivos sin guardar:
  la SDD confirma cuál es.

**Fuera del asistente:** según S5.

---

## Fuera de alcance

- Refresh tokens en cookie `HttpOnly`, colección de sesiones en Mongo, revocación de tokens o "cerrar sesión en todos
  los dispositivos". Se descartaron en D2.
- Mover el token fuera de `localStorage` o cambiar el `?token=` de las descargas.
- Sesión en `agent-ui/` (Electron) o en Tatana. Tatana no autentica.
- Login con proveedores externos (SSO) y "recordarme" por días.
- Persistir el estado completo del asistente entre recargas, más allá de lo que ya hace "Retomar".
- Persistir la cola de pendientes en el navegador (D4 = A).
- Volver a consultar al proveedor externo en cada renovación en modo `external`. Ver S6.
- Cambiar el rate limit del login de #63.
- Cambiar cómo y cuándo se cortan las sesiones revocadas (cambio de contraseña o cuenta suspendida en `local`). Se
  mantiene lo de `usuarios-locales`.
- Todo lo demás de #73: vista previa de grabaciones largas, header, B1–B4, log de generación, documentación del VPS y
  chequeo de pantalla.

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- **Backend.**
  - `IssueToken` suma un claim con la hora del login (por ejemplo `auth_time`). En el login es "ahora"; en el refresh
    se copia del token actual.
  - `POST /api/auth/refresh` es `[Authorize]`, así que pasa por `FactumBearerHandler` y, en `local`, por
    `SessionValidator`. Vuelve a emitir el token con el mismo `user`, `pca` y `auth_time`, y con
    `exp = min(ahora + ExpiryHours, auth_time + tope)`.
  - Si ya se llegó al tope, o el token no trae `auth_time` (tokens anteriores a esta versión), responde 401 con un code
    nuevo en `AuthErrors`, por ejemplo `session_max_age`.
  - El refresh **no** lleva la política `login` ni pasa por `ILoginThrottle`. Tiene su propia política, que la SDD
    elige. `[AllowDuringPasswordChange]` no corresponde.
  - `change-password` también vuelve a emitir el token: la SDD define si conserva o reinicia el `auth_time`. Se
    recomienda reiniciarlo, porque pedir la contraseña actual es volver a autenticarse.
  - Configuración: la opción nueva del tope va en `JwtOptions` (o en `Auth`), con valor por defecto 12 h. Hay que
    validarla al arrancar (tope ≥ duración del token).
  - Las dos variables van en: los dos `docker-compose.yml` de `deploy/`, los dos `.env.example`,
    `docs/despliegue-nube.md`, `docs/instalacion-windows.md`, `restaurar.ps1` y el `docker-compose.yml` de la raíz.
  - Tests en `server/tests/Factum.Backend.Tests/Auth/`:
    - el refresh conserva `auth_time`;
    - el `exp` se recorta al tope;
    - pasado el tope responde `session_max_age`;
    - un token viejo sin `auth_time` se rechaza;
    - en `local`, una cuenta suspendida no renueva.
- **Cliente.**
  - Un "gestor de sesión" único, montado junto a `SessionWatcher`, que:
    - programa la renovación según `expires_at` y la actividad (S1);
    - muestra los avisos;
    - se coordina entre pestañas (S2), por ejemplo con el evento `storage` sobre `factum_token` o con
      `BroadcastChannel`. Por ese canal **solo viaja el token y la hora, nunca datos de la causa**.
  - `notifyAuthFailure` distingue dos casos:
    - **sesión vencida** (`invalid_token` o `session_max_age`) **con el asistente activo**: emite un evento nuevo,
      por ejemplo `session_expired_in_place`, que abre el diálogo y **no** navega;
    - **el resto** (revocada, suspendida, fuera del asistente): el comportamiento de hoy.

    El asistente se registra como "activo" mientras hay un caso abierto en `app/dashboard/page.tsx`.
  - Si se reutiliza `LoginForm` dentro del diálogo, hay que conservar sus mensajes de error y de `429`.
- **Cola (D4 = A).**
  - En `useFileManager.saveOne`, un `ApiError` 401 de sesión vencida en `registerEvidence` deja el archivo en un
    estado nuevo, "pendiente de registrar" (no `error`), y lo guarda en una lista en memoria.
  - Cuando vuelve la sesión, se re-encola con `enqueueSave`. El camino `storedInCase` evita volver a importar.
  - `runQueue` no tiene que marcar como `error` al resto de la cola cuando el motivo es la sesión.
  - Si hay que reintentar el autoguardado del paso 4 (S4), se llama a `flush()` cuando vuelve la sesión.
- **Descargas.** Un token renovado no invalida el anterior, porque no hay revocación. La descarga en curso sigue. Las
  URLs con `?token=` que se arman en el render (marca, descargas) tienen que tomar el token vigente cada vez.
- **Contrato client ↔ server:** la SDD lista en **Contrato compartido** el endpoint nuevo, los campos de respuesta en
  snake_case (`token`, `user`, `expires_at`, `session_max_at` o los nombres que elija) y el code nuevo del 401.
- **Regla dura de datos:** esta HU no escribe en Mongo. Si alguna prueba crea cuentas `local`, limpia solo por sus
  `_id`.

---

## Dudas para validar con el usuario

> D2, D3 y D4 ya están validadas (ver arriba). Estas son las preguntas que siguen abiertas y que la SDD no puede
> decidir sola. Cada una trae una opción **recomendada** y su porqué.

**S1. ¿Qué cuenta como "actividad" para renovar sola la sesión?**
- A) **Interacción del perito con cualquier pestaña de Factum** (clic, toque, tecla o rueda, contada como mucho una vez
  por minuto) en los últimos 30 min, **o** una operación larga en curso (grabación, subida o generación del informe).
- B) Cualquier pedido exitoso al backend, incluso los automáticos.
- C) Solo la interacción del perito, sin contar las operaciones en curso.
- **Recomendada: A.** Con B, una pestaña olvidada que consulta algo en segundo plano mantendría la sesión viva hasta
  el tope: es justo lo que la inactividad de 2 h quiere evitar. Con C, una grabación larga sin tocar la pantalla (el
  perito mira el celular, no la PC) dispararía el aviso o el vencimiento en medio de la grabación. Mover el mouse sin
  hacer clic no cuenta, para que una PC olvidada no se mantenga viva sola.

**S2. ¿Qué pasa con varias pestañas abiertas?**
- A) **Una sola sesión compartida.**
  - La actividad en cualquier pestaña cuenta.
  - Renueva una sola pestaña, coordinada por `storage` o `BroadcastChannel`, y las demás toman el token nuevo (ya
    lo leen de `localStorage` en cada pedido).
  - Si una vuelve a iniciar sesión, las demás cierran su diálogo.
  - "Salir" en una lleva a las demás al login la próxima vez que hagan un pedido.
- B) Cada pestaña renueva por su cuenta, sin coordinación. Es más simple, pero puede haber renovaciones duplicadas y
  avisos o diálogos repetidos.
- **Recomendada: A.** El token ya está compartido en `localStorage`, así que la sesión de hecho es una sola. Coordinar
  evita pedirle al perito que inicie sesión dos veces. Por el canal entre pestañas no viajan datos de la causa.

**S3. El tope de 12 h: ¿cómo se avisa y qué pasa si llega en medio de una grabación?**
- A) **Avisar 30 min antes del tope** con el banner "vas a tener que volver a iniciar sesión a las HH:MM" y el botón
  "Iniciar sesión ahora" (re-login anticipado, que reinicia el tope).
  - Si el tope llega durante una grabación, la grabación **sigue** en Tatana.
  - El diálogo de re-login aparece igual, pero no tapa el botón "Detener": se muestra como panel lateral o deja visible
    la barra de grabación.
  - El registro de la grabación queda pendiente hasta que el perito inicie sesión.
- B) Igual que A, pero el diálogo **no aparece** mientras hay una grabación en curso. Se muestra recién al detenerla.
- C) Sin aviso previo del tope: el perito se entera con el diálogo.
- **Recomendada: A.** El aviso anticipado con "Iniciar sesión ahora" deja que el perito elija un momento tranquilo y
  evita que el corte caiga en medio de una toma. Si igual coincide, la grabación no depende del JWT y su registro
  queda en la cola, así que no se pierde nada. Esconder el diálogo (B) haría que otros guardados del asistente fallen
  en silencio durante toda la grabación.

**S4. Además del registro de evidencia, ¿qué otros guardados se reintentan solos al recuperar la sesión?**
- A) **El registro de evidencia (D4) y el autoguardado de textos del paso 4** (que ya queda marcado como "sin
  guardar").
  - Los roles de captura ya se reintentan al guardar.
  - Los eventos de auditoría del agente que fallen se descartan (no tienen datos que se pierdan).
  - Una generación de informe que falló con 401 la vuelve a disparar el perito con el botón de hoy.
- B) Solo el registro de evidencia, como dice D4. El resto queda en "error" y el perito lo reintenta a mano.
- C) Reintentar automáticamente cualquier pedido que falló con 401 (cola genérica).
- **Recomendada: A.** El texto del paso 4 es lo que más cuesta volver a escribir y ya está en memoria: guardarlo solo
  cuesta una llamada a `flush()`. C reintentaría a ciegas operaciones con efectos, como la generación, que conviene
  que el perito vuelva a disparar a sabiendas.

**S5. ¿Re-login en el lugar también fuera del asistente (dashboard, historial, Métricas, administración)?**
- A) **No:** fuera del asistente con un caso abierto, el 401 lleva al login como hoy. Ahí no hay nada que se pierda.
- B) Sí, en toda la web.
- **Recomendada: A.** El problema real es perder el estado del asistente. En las otras pantallas, volver al login no
  pierde nada y mantener el flujo de hoy reduce el riesgo del cambio. La renovación automática y los avisos sí
  funcionan en toda la web.

**S6. Modos `external` y `dev`: ¿se renueva la sesión?**
- A) **Sí, con el mismo tope de 12 h**, sin volver a consultar al proveedor externo. Una baja en el sistema externo se
  nota en el próximo login, a más tardar al llegar al tope.
- B) En `external` no se renueva: solo aviso y re-login en el lugar. En `dev` sí se renueva.
- C) En `external`, cada renovación vuelve a consultar al proveedor. No se puede hacer sin la contraseña, así que
  obligaría a pedirla.
- **Recomendada: A.** Hoy un token `external` ya vale 8 h sin consultar al proveedor. Con A, el peor caso pasa a 12 h,
  pero solo con actividad continua, y una PC inactiva corta a las 2 h, que es más seguro que hoy. B dejaría a los
  clientes en `external` con el mismo problema de producción. Si algún cliente necesita bajas inmediatas, puede bajar
  el tope en su configuración.

**S7. ¿El refresh se registra en algún log o evento?**
- A) **Solo en el log del backend, sin evento de caso:**
  - cada renovación exitosa en nivel `Debug` (no aparece en producción);
  - cada renovación **rechazada** (tope alcanzado o token sin `auth_time`) en `Information`, con el code y sin DNI ni
    token.

  El login y el re-login se loguean como hoy.
- B) Cada renovación en `Information`, con el DNI del perito.
- C) Además, guardar un evento de auditoría de sesión en Mongo.
- **Recomendada: A.** Una renovación cada 1,5 h por perito no aporta en producción y llenaría el log. Lo que sirve
  para diagnosticar es ver cuándo y por qué se rechazó una, que es justo lo que faltó en el caso real. C implica una
  colección nueva y datos personales nuevos, fuera del alcance de D2.

**S8. Instalaciones existentes que tienen `FACTUM_JWT_EXPIRY_HOURS=8` escrito en su archivo de configuración.**

Los dos `.env.example` traen `8` explícito, así que las instalaciones ya hechas (la nube y los Windows de los
estudios) van a seguir con tokens de 8 h aunque el valor por defecto del código pase a 2.
- A) **No tocar la configuración de nadie automáticamente.**
  - Cambiar el ejemplo y el valor por defecto a `2`.
  - Sumar la variable del tope (por defecto 12) a los ejemplos y a los compose.
  - Documentar en la guía de cada despliegue que conviene pasar a `2`.
  - En la nube, el operador lo cambia como parte del despliegue de esta HU.
  - Con `8` sigue funcionando: token de 8 h, renovación y tope de 12 h.
- B) Que `actualizar.ps1` (Windows) y el procedimiento de la nube reemplacen `8` por `2` si encuentran el valor viejo.
- **Recomendada: A.** Los scripts de actualización hoy no reescriben valores que el operador eligió, y un `8` puesto a
  mano puede ser una decisión del cliente. Con A nada se rompe: esas instalaciones ganan la renovación y el re-login
  en el lugar, y solo les falta el corte por inactividad de 2 h hasta que el operador cambie el valor.

## Validación del usuario (2026-10-13)

El usuario validó **S1–S8 con la opción recomendada** (A en todas): actividad = interacción real o operación larga en curso; sesión compartida y coordinada entre pestañas; aviso 30 min antes del tope con "Iniciar sesión ahora" y la grabación sigue si el tope llega en medio; reintento automático del registro de evidencia y del autoguardado del paso 4; re-login en el lugar solo en el asistente con caso abierto; renovación también en `external` y `dev` con el mismo tope; log solo en backend (rechazos en `Information`, sin DNI ni token); no se reescribe la configuración existente, se cambian default/ejemplos a 2 h y se documenta.
