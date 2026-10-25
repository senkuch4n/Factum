# Factum

Sistema de adquisición forense de evidencia digital para dispositivos móviles.
Permite a un perito u operador crear un expediente, conectar un celular
(Android o iOS) por USB, capturar evidencia (fotos, video de pantalla,
capturas) y generar un informe forense (DOCX) + un paquete ZIP cifrado (AES-256) con
hash, todo sin que la evidencia original salga del dispositivo del usuario
hacia un servidor de terceros. El informe sale con la identidad de la
organización que lo emite (ver
[Identidad de la organización (Branding)](#identidad-de-la-organización-branding)).

Como **integración opcional de soporte** (apagada por defecto) se puede
conectar con **[Faro](https://gitlab.com/joelserrudo/faro-sistema-de-tokens)**,
una mesa de ayuda (sistema de tokens): el usuario reporta un problema técnico
de Factum y le hace seguimiento sin salir de la app, con la misma identidad
(DNI) — ver [Integración de soporte (Faro)](#integración-de-soporte-faro).

## Arquitectura

El proyecto tiene 4 partes que corren por separado:

```
┌─────────────────┐      HTTP       ┌──────────────────────┐      MongoDB
│  client/         │ ───────────────▶│  server/.../Backend  │ ───────────────▶ (mongo)
│  Next.js (web)   │◀─────────────── │  ASP.NET Core API    │
└─────────────────┘   JSON + JWT     └──────────┬───────────┘
        │                                        │
        │ WebSocket + HTTP (localhost:8765)      │ HTTP + X-Service-Key
        ▼                                        ▼
┌─────────────────┐                    ┌──────────────────────┐
│  server/.../Agent │  (proceso local) │  Faro (soporte,       │
│  "Tatana"          │ ◀── ADB/USB ──  │  gestión de tokens)   │
│  habla con el      │     dispositivo │  opcional)            │
│  celular por USB   │                 └──────────────────────┘
└─────────────────┘
        ▲
        │ empaqueta y da UI de bandeja
┌─────────────────┐
│  agent-ui/       │
│  Electron        │
└─────────────────┘
```

- **`client/`** — Frontend web en Next.js 16 (App Router) + React 19 +
  Tailwind. Login, dashboard, wizard de inspección (guía USB → conectar
  dispositivo → datos del expediente → captura → generar informe → resultado),
  historial de casos, y el modal de soporte (solo si la integración
  opcional de soporte está habilitada).
- **`server/src/Factum.Backend`** — API en ASP.NET Core (.NET 10) +
  MongoDB. Autenticación JWT, gestión de casos/expedientes, generación de
  informes (DOCX + ZIP cifrado AES-256), y el cliente HTTP de la integración
  opcional de soporte (Faro).
- **`server/src/Factum.Agent`** ("Tatana") — Un segundo servicio ASP.NET
  Core que **corre en la PC del oficial**, no en el servidor. Se comunica con
  el celular conectado por USB (Android vía ADB, iOS vía `pymobiledevice3`) y
  expone una API local + WebSocket que consume el `client`. Por eso **no**
  está en `docker-compose.yml`: necesita acceso físico al puerto USB de la
  máquina donde se ejecuta.
- **`agent-ui/`** — Envoltorio de escritorio en Electron para Tatana: permite
  instalar/actualizar las librerías que necesita el agente, ver su estado de
  conexión, configurar la URL del backend, e iniciarlo en segundo plano al
  prender la PC. Ver [`AGENTE_TATANA.md`](./AGENTE_TATANA.md) para el detalle
  de casos de uso.

## Stack

| Parte | Tecnología |
|---|---|
| Frontend web | Next.js 16, React 19, Tailwind CSS, Framer Motion |
| Backend API | ASP.NET Core (.NET 10), MongoDB.Driver |
| Agente local | ASP.NET Core (.NET 10), WebSockets, ADB / pymobiledevice3 |
| UI del agente | Electron + Vite |
| Base de datos | MongoDB 7 |
| Auth | JWT (access token corto) — modos `dev` (mock) y `external` (proveedor HTTP externo genérico) |

## Requisitos previos

- [Node.js](https://nodejs.org/) 20+ y npm
- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) + Docker Compose (para el modo rápido)
- Para capturar dispositivos reales: `adb` (Android) y/o `pymobiledevice3`
  (iOS) instalados y en el `PATH` — el agente los usa para hablar con el
  celular. Sin esto, el agente puede correr en modo `Mock` (ver más abajo).

## Puesta en marcha rápida (Docker)

Levanta MongoDB + Backend + Frontend con un solo comando:

```bash
docker compose up --build
```

- Frontend: http://localhost:3000
- Backend: http://localhost:8080
- MongoDB: localhost:27017

El agente **no** se levanta con Docker (ver arriba por qué) — para probar el
flujo de captura de un dispositivo real, corré el agente por separado como se
explica en la siguiente sección.

> Este `docker-compose.yml` es de **desarrollo** (Mongo publicado, secreto JWT de
> ejemplo). Para instalar Factum en la PC Windows de un estudio se usa
> `deploy/windows/` (paquete sin internet, backups, actualización): ver
> [docs/instalacion-windows.md](docs/instalacion-windows.md).

### Despliegue en la nube

Factum como SaaS en un VPS (DonWeb, Argentina) con HTTPS: compose de producción en `deploy/cloud/` (Caddy + frontend +
backend + Mongo con autenticación), deploy automático desde GitHub Actions al mergear a `main`, backups cifrados fuera
del servidor y Tatana local en la PC de cada perito. Guía paso a paso:
**[docs/despliegue-nube.md](docs/despliegue-nube.md)**.

### Chequeos de salud del backend

| Endpoint | Qué responde | Lo usan |
|---|---|---|
| `GET /health` | `{ status, version, auth_mode }`. **No** toca Mongo (liveness): si Mongo se cae, sigue en 200 y el contenedor no se reinicia en bucle | Healthcheck de los compose, scripts de `deploy/windows/` |
| `GET /health/ready` | `200 {"status":"ok","mongo":"ok"}` o `503 {"status":"unavailable","mongo":"down"}`: hace un `ping` a Mongo con timeout de 3 s, sin detalle del error (readiness) | Monitor externo y script de deploy de la nube (`deploy/cloud/`) |

## Puesta en marcha en modo desarrollo (sin Docker)

Útil para desarrollar con hot-reload en las 4 partes.

### 1. MongoDB

```bash
docker run -d --name factum-mongo -p 27017:27017 mongo:7
```

### 2. Backend (`server/src/Factum.Backend`)

```bash
cd server/src/Factum.Backend
dotnet restore
dotnet run
```

Corre por defecto en `http://localhost:8080` (revisar `Properties/launchSettings.json`
si tu puerto difiere). Configuración en `appsettings.json` — ver
[Variables de entorno / configuración](#variables-de-entorno--configuración).

### 3. Frontend (`client`)

```bash
cd client
npm install
npm run dev
```

Corre en `http://localhost:3000`. Necesita un archivo `.env.local` (ver abajo)
apuntando al backend y al agente.

```bash
# client/.env.local
NEXT_PUBLIC_BACKEND_URL=http://localhost:8080
NEXT_PUBLIC_AGENT_URL=http://localhost:8765
```

### 4. Agente Tatana (`server/src/Factum.Agent`)

Corre en la máquina del oficial, no en el servidor:

```bash
cd server/src/Factum.Agent
dotnet restore
dotnet run
```

Por defecto escucha **solo en esta máquina** (`http://localhost:8765`, IPv4 e
IPv6 loopback). Si no tenés `adb`/`pymobiledevice3` instalados y solo querés
probar el flujo end-to-end sin un celular físico, usá el modo mock **sin tocar
`appsettings.json`** (versionado, y lo que se empaqueta):

```bash
dotnet run -- --mock
```

o, para dejarlo fijo en tu máquina, en `server/src/Factum.Agent/appsettings.Local.json`
(ignorado por git y excluido de `bin/`/`publish/`, como en el backend):

```json
{ "Agent": { "Mock": true } }
```

Argumentos de línea de comandos (ganan sobre la config): `--mock`, `--port <n>`,
`--data <carpeta>` y `--bind <dirección>`. Al arrancar loguea la URL y el mock
**efectivos** (`Factum Agent en http://localhost:8765 (mock=False)`).

### 5. (Opcional) UI de escritorio del agente (`agent-ui`)

```bash
cd agent-ui
npm install
npm run dev       # ventana Electron en modo desarrollo
npm run package   # empaqueta la app instalable
```

## Variables de entorno / configuración

**`server/src/Factum.Backend/appsettings.json`**

| Clave | Qué es |
|---|---|
| `MongoDb:ConnectionString` / `DatabaseName` | Conexión a Mongo |
| `Jwt:Secret` / `ExpiryHours` | Firma y vida del token de sesión. `ExpiryHours` (env `FACTUM_JWT_EXPIRY_HOURS` en los despliegues) default `2`, en horas, acepta decimales con punto (`0.2` = 12 min); es también el corte por inactividad. Con `Auth:Mode=local` y fuera de `Development`, `Jwt:Secret` tiene que ser propio del despliegue (≥ 32 caracteres, no el valor del repo) o el backend no arranca |
| `Jwt:SessionMaxHours` | Tope de la sesión desde el login, en horas (env `FACTUM_SESION_TOPE_HORAS`, default `12`). El refresh nunca emite un token que venza después de ese tope. Los dos valores tienen que ser > 0 y ≤ 168 o el backend no arranca; si el tope es menor que `ExpiryHours`, arranca con un warning y cada token se recorta al tope |
| `Auth:Mode` | `dev` (identidad simulada, default), `external` (login real contra un proveedor HTTP externo) o `local` (cuentas propias de Factum en la colección `users`; el modo del SaaS). Sin distinguir mayúsculas; cualquier otro valor impide arrancar |
| `Auth:AllowDevOutsideDevelopment` | Env `Auth__AllowDevOutsideDevelopment`. `true` permite `Auth:Mode=dev` fuera de `Development` (solo para una instalación local aislada); default `false`: `dev` fuera de `Development` impide arrancar. En `Development` se ignora |
| `Auth:Local:*` | Solo si `Auth:Mode=local`: política de contraseñas, bloqueo por intentos, superadmins iniciales y reset de emergencia — ver [Autenticación](#autenticación) |
| `Auth:External:BaseUrl` / `LoginPath` / `TimeoutSeconds` | Solo si `Auth:Mode=external`. Se hace `POST {BaseUrl}{LoginPath}` (default `/auth/login`); timeout entre 1 y 120 s (default 10) |
| `Auth:External:Request:DniField` / `UserField` / `PasswordField` | Nombres de los campos del body JSON que se manda al proveedor (default `dni` / `user` / `password`). Tienen que ser distintos |
| `Auth:External:Response:NameField` / `SiglaField` | Campos que se leen de la respuesta (default `name` / `sigla`). Admiten rutas con puntos (`data.user.fullName`). `SiglaField` vacío = no se lee sigla |
| `Integrations:Support:Enabled` | `true` activa la integración opcional de soporte (Faro). Default `false` |
| `Integrations:Support:BaseUrl` | URL del backend de Faro (`http://localhost:5038` en local). Obligatoria si está habilitada |
| `Integrations:Support:ServiceKey` | Clave compartida servicio-a-servicio con Faro (debe coincidir con `Integrations:ServiceKey` de Faro). Obligatoria si está habilitada. **No va en el repo**: `appsettings.Local.json` o variable de entorno |
| `Integrations:Support:TimeoutSeconds` | Timeout de las llamadas a Faro, entre 1 y 120 s (default 10) |
| `Integrations:Support:FrontendUrl` | URL del frontend de Faro, para armar el link de SSO (`http://localhost:3001` en local). Obligatoria si está habilitada |
| `Storage:DataDirectory` | Carpeta local donde se guardan los ZIP/PDF generados (dev) |
| `Storage:MaxUploadBytes` | Env `Storage__MaxUploadBytes`. Tope de tamaño de un archivo de evidencia en `POST /api/cases/{id}/files` (solo ese endpoint; el resto sigue con el tope de 30 MB de Kestrel). Default `4294967296` (4 GB). Tiene que ser mayor que 0 o el backend no arranca |
| `Storage:MinFreeBytes` | Env `Storage__MinFreeBytes`. Espacio libre que tiene que quedar en el disco de `DataDirectory` después de una subida; si no alcanza, la subida se rechaza con 507 antes de escribir. Default `1073741824` (1 GB). Tiene que ser mayor o igual que 0 o el backend no arranca |
| `Audit:AdminDnis` | DNIs habilitados a leer `GET /api/agent-events` (auditoría de uso del agente). En `Auth:Mode=local` también puede leerla cualquier superadmin |
| `Tatana:MinVersion` | Env `Tatana__MinVersion` (en la nube, `FACTUM_TATANA_VERSION_MINIMA`). Versión mínima de Tatana con la que la web inicia capturas: vacío = no bloquea; `X.Y.Z` = un Tatana menor (o anterior al instalador, sin `real_version_v1`) queda como desactualizado. Sale en `GET /api/config/public` como `tatana_min_version`. Otro formato impide arrancar |
| `Timestamping:Urls` | Env `Timestamping__Urls` (en los despliegues, `FACTUM_SELLADO_URLS`). Autoridades de sellado de tiempo RFC 3161, en orden de preferencia, separadas por coma o espacio. Vacío (default del `appsettings.json` y de la nube/Windows) = sellado desactivado; en desarrollo (`appsettings.Development.json` y el `docker-compose.yml` raíz) vale `https://freetsa.org/tsr, http://timestamp.digicert.com`. Cada una tiene que ser `http`/`https` absoluta y sin fragmento o el backend no arranca. Si la autoridad pide usuario y clave: `https://usuario:clave@host/ruta` (se mandan como Basic y nunca se loguean ni se guardan) — ver [Sellado de tiempo](#sellado-de-tiempo-rfc-3161) |
| `Timestamping:Enabled` | Env `FACTUM_SELLADO_HABILITADO`. Interruptor (default `true`): `false` desactiva el sellado aunque haya URLs |
| `Timestamping:TimeoutSeconds` / `MaxAttempts` | Env `FACTUM_SELLADO_TIMEOUT_SEGUNDOS` (2–60, default `10`) y `FACTUM_SELLADO_MAX_INTENTOS` (1–50, default `12`): timeout por autoridad y tope de intentos de la cola antes de abandonar un sello. Fuera de rango, no arranca |
| `Timestamping:InlineBudgetSeconds` / `GenerationBudgetSeconds` / `InlineCooldownSeconds` / `PollSeconds` | Solo config. Tiempo máximo que espera el registro de evidencia (1–60, default `10`), cada sello de la generación (1–120, default `15`), pausa del intento inline tras un fallo total (0–3600, default `120`) y vuelta de la cola de reintentos (5–3600, default `30`) |
| `Branding:OrganizationName` / `OrganizationLogo` / `OrganizationIsotype` / `ContactLines` / `PrimaryColor` / `AccentColor` | Identidad de la organización que emite los informes (nombre, logo, isotipo, contacto y colores del informe) — ver [Branding](#identidad-de-la-organización-branding). Vacío en el repo |
| `Report:TimeZone` / `DomicilioConstituido` / `DefaultTexts:*` | Zona horaria, domicilio constituido y textos por defecto del informe pericial — ver [Informe pericial](#informe-pericial-configuración). Domicilio vacío en el repo |
| `Report:MaxGenerateUploadBytes` | Env `Report__MaxGenerateUploadBytes`. Tope del cuerpo de `POST /api/cases/{id}/generate/finish` (los datos del ZIP más las capturas que el informe embebe). Default `268435456` (256 MiB). Tiene que ser mayor que 0 o el backend no arranca. Si se supera: 413 `request_too_large` con `max_bytes` |
| `Cors:AllowedOrigins` | Env `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, … Orígenes del front que pueden llamar a la API desde el navegador. Cada valor es un origen absoluto `http`/`https` sin path, query ni fragmento (`https://factum.ejemplo.com`; la `/` final se ignora). Si falta o está vacía vale `http://localhost:3000` y `http://127.0.0.1:3000`. `"*"` o un valor inválido impide arrancar. Se loguea la lista al arrancar. No va en el `appsettings.json` versionado: los defaults cubren desarrollo y la instalación local |
| `Security:LoginRateLimit:PerIpPermitLimit` / `PerIpWindowSeconds` | Env `Security__LoginRateLimit__PerIpPermitLimit`, `…__PerIpWindowSeconds`. Freno de `POST /api/auth/login` por IP (ventana deslizante): default 10 intentos cada 60 s. Al superarlo: 429 `account_locked` + `Retry-After`. Tienen que ser mayores que 0 o el backend no arranca |
| `Security:LoginRateLimit:PerDniPermitLimit` / `PerDniWindowSeconds` | Env `Security__LoginRateLimit__PerDniPermitLimit`, `…__PerDniWindowSeconds`. Freno del login por DNI, antes de validar credenciales (cuenta todos los intentos, también los exitosos): default 5 cada 60 s. Convive con el bloqueo por cuenta de `Auth:Mode=local`. Mayores que 0 o no arranca |
| `Security:ForwardedHeaders:KnownNetworks` | Env `Security__ForwardedHeaders__KnownNetworks__0`, … Redes (CIDR) de los proxies cuyo `X-Forwarded-For` se cree (IP real para el rate limit y la auditoría). Si falta o está vacía: `127.0.0.0/8`, `::1/128`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `fc00::/7`. Un valor que no es CIDR impide arrancar. No va en el `appsettings.json` versionado |

Las subidas en curso se escriben en `<DataDirectory>/.upload-tmp/` y recién al terminar completas se mueven a `cases/<id>/`; esa carpeta no es evidencia, no entra al ZIP y se vacía al arrancar el backend. Lo mismo `<DataDirectory>/.generate-tmp/`: guarda las capturas del informe solo mientras se arma el DOCX de un caso nuevo (ver [Evidencia en la PC del perito](#evidencia-en-la-pc-del-perito)) y se borra al terminar cada generación y al arrancar.

**`client/.env.local`**

| Variable | Qué es |
|---|---|
| `NEXT_PUBLIC_BACKEND_URL` | URL del backend |
| `NEXT_PUBLIC_AGENT_URL` | URL del agente local (Tatana) |
| `NEXT_PUBLIC_TATANA_DOWNLOAD_URL` | Opcional. Link de descarga del Tatana vigente (p. ej. `https://<dominio>/descargas/Tatana-Portable-Windows-nube.zip`). Si está, el mensaje "origen no permitido" de Tatana dice dónde descargar la versión actual; vacío (default, instalación local) deja el mensaje de siempre. Se hornea en el bundle en `next build` (build arg del `client/Dockerfile`) |

**`server/src/Factum.Agent/appsettings.json`**

| Clave | Qué es |
|---|---|
| `Agent:Port` | Puerto donde escucha el agente (8765 por defecto). CLI: `--port` |
| `Agent:BindAddress` | Dónde escucha: `localhost` (default: 127.0.0.1 y ::1, solo esta máquina) o una IP. `0.0.0.0`/`::` expone el agente a la red, que no tiene autenticación: arranca con un warning. Cualquier otro valor impide arrancar. CLI: `--bind`. Tatana solo atiende requests **dirigidas a esta PC** (filtro del header `Host`, contra DNS rebinding): `localhost`, `127.0.0.1`, `[::1]`, la IP de bind y, con `0.0.0.0`/`::`, las IP de la PC tomadas **al arrancar** (si cambian por DHCP, reiniciá Tatana o fijalas en `Agent:AllowedHosts`). Cualquier otro `Host` recibe **400** sin datos |
| `Agent:Mock` | `true` simula dispositivos sin USB real — útil para desarrollar sin celular a mano. Ponelo en `appsettings.Local.json` o usá `--mock`, nunca en el `appsettings.json` versionado |
| `Agent:DataDirectory` | Carpeta de las capturas recién hechas (raíz) y de las carpetas de trabajo de cada caso (`cases/<id>/`, flujo nuevo). CLI: `--data` |
| `Agent:EvidenceDirectory` | Env `Agent__EvidenceDirectory`. Carpeta base donde Tatana deja el ZIP de cada caso (`<causa>_<id8>/evidencia_….zip`). Default `C:\Factum\Evidencia` en Windows y `~/Factum/Evidencia` en macOS/Linux (nunca Documentos). Una ruta relativa se resuelve contra el directorio de trabajo de Tatana. Se crea al primer ZIP. Si cae dentro de OneDrive o iCloud, Tatana avisa en el log y la web lo muestra (no la cambia) |
| `Agent:AllowedOrigins` | Env `Agent__AllowedOrigins__0`, … Orígenes del front que pueden llamar a Tatana. Mismas reglas que `Cors:AllowedOrigins` del backend; default `http://localhost:3000` y `http://127.0.0.1:3000`. Tatana responde **403** `origin_not_allowed` a cualquier request (incluido el WebSocket y los preflight) con un `Origin` que no está en la lista; las requests sin `Origin` (curl, la app de escritorio) pasan |
| `Agent:AllowedHosts` | Env `Agent__AllowedHosts__0`, … Hosts **extra** que puede traer el header `Host`, además de los locales y la IP de bind (ver `Agent:BindAddress`). Un nombre (`tatana.local`) o una IP (IPv6 con o sin corchetes), sin esquema, puerto, path ni `*`. Default vacío. Inválido en `appsettings.json`, env o CLI → Tatana no arranca; inválido en `appsettings.Local.json` → se ignora el archivo entero. El `AllowedHosts` de la raíz (el de ASP.NET) no se usa |
| `Agent:MaxUploadBytes` | Env `Agent__MaxUploadBytes`. Tope de un archivo que el navegador copia a la carpeta del caso (cámara externa, adjuntos). Default `17179869184` (16 GiB) |
| `Agent:MinFreeBytes` | Env `Agent__MinFreeBytes`. Espacio libre que tiene que quedar en el disco después de copiar un archivo o de armar el ZIP. Default `1073741824` (1 GiB) |

> `Jwt:Secret` sigue commiteado con un valor de desarrollo, pensado para correr
> todo en local: rotalo antes de cualquier despliegue real. La `ServiceKey` de
> soporte **ya no está en el repo**: va en `appsettings.Local.json` (ignorado por
> git) o en la variable de entorno `Integrations__Support__ServiceKey`. La clave
> que quedó expuesta en el historial de git hay que rotarla (en Faro y en tu
> config local).

## Autenticación

El DNI identifica al usuario y es la llave de todos sus datos (casos, perfil de
perito, catálogos; si la integración de soporte está activa, es la misma
identidad que usa Faro). Hay tres modos (`Auth:Mode`):

- **Modo `local`** — el modo del SaaS: cuentas propias de Factum en la
  colección `users` de MongoDB. El login pide **DNI + contraseña**. Detalle
  abajo, en [Modo `local`](#modo-local-cuentas-propias).
- **Modo `dev`** (default): el login pide **DNI + usuario + contraseña**;
  cualquier DNI de 7-8 dígitos y cualquier contraseña no vacía autentican. El
  nombre se arma a partir del usuario con la convención `nombre.apellido` (ej:
  usuario `carlos.mendoza` → "Carlos Mendoza"). No hace falta pre-registrar a
  nadie. **Fuera de `Development`, `dev` no arranca** salvo que se ponga
  `Auth:AllowDevOutsideDevelopment=true` (env
  `Auth__AllowDevOutsideDevelopment=true`), y aun así avisa en el log que acepta
  cualquier contraseña. El `docker-compose.yml` de desarrollo y la instalación
  Windows (`FACTUM_AUTH_ALLOW_DEV`, default `true`) ya traen el flag.
- **Modo `external`**: el login pide **DNI + usuario + contraseña** y valida
  contra un proveedor HTTP externo genérico
  (`POST {Auth:External:BaseUrl}{Auth:External:LoginPath}`). Los nombres de los
  campos del request y de la respuesta se configuran en
  `Auth:External:Request:*` / `Auth:External:Response:*` (la respuesta admite
  rutas con puntos). Un 401/403 del proveedor es "Credenciales inválidas"; un
  proveedor caído, lento o con 5xx da "No se pudo conectar al servicio de
  autenticación…", y cualquier otra respuesta rara, "El servicio de
  autenticación respondió de forma inesperada." El detalle técnico va al log,
  nunca a la pantalla.

Si `Auth:Mode` (o la config del modo elegido) es inválida, **el backend no
arranca** y dice por qué, en vez de caer en `dev` sin avisar. El log de
arranque muestra el modo y, en `external`, la URL de login efectiva; en
`local`, la cantidad de superadmins activos.

**Duración de la sesión.** El JWT dura `Jwt:ExpiryHours` (2 h) y la web lo
renueva con `POST /api/auth/refresh` mientras el perito está activo, hasta un
tope de `Jwt:SessionMaxHours` (12 h) desde el login (claim `auth_time`, que el
refresh copia sin cambios). El refresh solo acepta el token por el header
`Authorization`, tiene su propio rate limit (6 por minuto por token, `429
rate_limited`) y no cuenta como intento de login. Pasado el tope, o con un
token anterior a esta versión, responde `401 session_max_age`. En `local` pasa
igual por la validación de cuenta, así que una suspensión o un cambio de
contraseña siguen cortando la sesión. Reemplaza la decisión anterior de "8 h
sin renovación" (D6 de `usuarios-locales`).

### Modo `local` (cuentas propias)

- Cada cuenta tiene DNI (7-8 dígitos, único, no editable), nombre, rol
  (`superadmin` o `cliente`) y estado (`activo` o `suspendido`). La contraseña
  se guarda con PBKDF2-HMAC-SHA256 con sal; nunca se loguea ni sale por la API.
- Toda cuenta nace con una **contraseña temporal** y en el primer ingreso hay
  que cambiarla (pantalla "Cambiá tu contraseña"); hasta entonces la API
  responde 403 `password_change_required` a todo lo demás. Desde el menú de
  usuario se puede cambiar cuando se quiera. Cambiar la contraseña cierra las
  demás sesiones abiertas de esa cuenta.
- Después de `MaxFailedAttempts` contraseñas incorrectas seguidas, la cuenta se
  bloquea `LockoutMinutes` minutos (429 `account_locked`).
- Una cuenta **suspendida** no puede entrar y su sesión abierta se corta en el
  próximo request (401 `account_suspended`).
- Un DNI que ya tiene casos, perfil o catálogos y todavía no tiene cuenta no ve
  nada hasta que se le crea; al crearla ve lo suyo sin migrar nada.

| Clave | Default | Qué es |
|---|---|---|
| `Auth:Local:PasswordMinLength` | `10` | Largo mínimo de la contraseña (entre 8 y 64). El máximo es 128. La contraseña no puede contener el DNI ni repetir la actual |
| `Auth:Local:MaxFailedAttempts` | `5` | Intentos fallidos seguidos antes de bloquear (entre 1 y 50) |
| `Auth:Local:LockoutMinutes` | `15` | Minutos de bloqueo (entre 1 y 1440) |
| `Auth:Local:BootstrapSuperadmins:<i>:Dni` / `Name` / `TemporaryPassword` | — | Superadmins iniciales. Se crean al arrancar **solo si no existen**; si ya existen no se modifican. La temporal tiene que cumplir el largo mínimo |
| `Auth:Local:ResetSuperadmin:Dni` / `TemporaryPassword` | vacío | Reset de emergencia de un superadmin (van juntos; vacío = apagado) |
| `Jwt:Secret` | — | Con `local`, propio del despliegue (≥ 32 caracteres, no el valor del repo); si no, fuera de `Development` el backend no arranca |

Las contraseñas temporales van en variables de entorno o en
`appsettings.Local.json` (ignorado por git), **nunca** en el `appsettings.json`
versionado. Ningún mensaje de error ni log las muestra.

**Superadmin inicial.** Si al arrancar en `local` no hay ningún superadmin
activo, el backend no arranca y explica qué configurar. Ejemplo con variables
de entorno (valores de ejemplo, cambialos):

```bash
Auth__Mode=local
Jwt__Secret=<secreto-propio-de-al-menos-32-caracteres>
Auth__Local__BootstrapSuperadmins__0__Dni=99000001
Auth__Local__BootstrapSuperadmins__0__Name="Superadmin de ejemplo"
Auth__Local__BootstrapSuperadmins__0__TemporaryPassword=cambiame-en-el-primer-ingreso
# más superadmins: Auth__Local__BootstrapSuperadmins__1__Dni=…, …__1__Name=…, …__1__TemporaryPassword=…
```

El log dice `Superadmin inicial creado: DNI 99000001`. Con esa temporal se
entra y se elige la contraseña definitiva. Después se puede sacar la temporal
de la configuración (dejarla no hace nada: una cuenta existente no se toca).

**Reset de emergencia** (un superadmin que se olvidó la contraseña):

```bash
Auth__Local__ResetSuperadmin__Dni=99000001
Auth__Local__ResetSuperadmin__TemporaryPassword=otra-temporal-de-emergencia
```

Al arrancar, a ese superadmin se le pone esa contraseña temporal (con cambio
obligatorio), se lo desbloquea, se lo reactiva si estaba suspendido y se cierran
sus sesiones. Se aplica una sola vez por valor, pero **borralo de la
configuración después de usarlo**: el log lo recuerda en cada arranque mientras
siga cargado. Si el DNI no es de un superadmin, se ignora con un warning.

**Suspender o reactivar a mano** (hasta que exista el panel de usuarios),
con `mongosh` sobre la base de Factum:

```js
db.users.updateOne({ Dni: "30111222" }, { $set: { Status: "suspendido" } })  // suspender
db.users.updateOne({ Dni: "30111222" }, { $set: { Status: "activo" } })      // reactivar
```

> Compatibilidad: `Auth:Mode=mpf` y `Auth:MpfBaseUrl`/`MpfLoginPath`/`MpfTimeoutSeconds`
> se aceptan como legado, con un warning al arrancar.

## Identidad de la organización (Branding)

Factum es un producto: el **emisor** del informe es la organización cliente
(un estudio, un gabinete, un perito). Hay dos niveles de marca:

- **Marca de cada cuenta** (`account_brandings` en MongoDB): cada usuario la
  edita desde la web ("Marca del informe" en el menú de usuario) y un
  superadmin la de cualquier cuenta desde `/admin/cuentas` ("Editar marca").
  Tiene nombre, logo, isotipo, hasta 6 líneas de contacto y los dos colores,
  con las mismas reglas que abajo, pero validadas estrictamente al guardar.
  Cada informe sale con la marca **del dueño del caso** en el momento de
  generarlo; si esa cuenta guardó su marca alguna vez, se usa solo la suya
  (sin mezclar con la de la instalación). Cada guardado con cambios queda en
  la auditoría (`update_branding`).
- **Default de la instalación** (sección `Branding` del backend, esta
  sección): es la marca del **login** (`GET /api/config/public`) y la de los
  informes de las cuentas que **nunca guardaron su marca**.

> En una instalación en la nube con varios clientes conviene dejar `Branding`
> vacío: así ninguna cuenta hereda la identidad de otra y cada una carga la
> suya desde la web.

| Clave | Tipo | Qué es |
|---|---|---|
| `Branding:OrganizationName` | texto | Nombre del emisor. Máx. 150 caracteres (se trunca con un warning). Vacío = no configurado. |
| `Branding:OrganizationLogo` | ruta | Logo del emisor: ruta absoluta o relativa al directorio del backend (`/app` en Docker). |
| `Branding:ContactLines` | lista de textos | Domicilio, teléfonos, correo, matrícula… Máx. 6 líneas de 150 caracteres. No se expone a la web. |
| `Branding:OrganizationIsotype` | ruta | Isotipo (versión reducida del logo, idealmente PNG transparente): va solo al cierre del informe, debajo de la firma. Mismas reglas que el logo. Vacío = sin isotipo. No se expone a la web. |
| `Branding:PrimaryColor` | `#RRGGBB` | Color primario del informe (filetes de la portada, de los títulos y del cierre, números de sección y línea bajo el encabezado de la tabla de hashes). Vacío = verde de Factum `#2F6F12`. |
| `Branding:AccentColor` | `#RRGGBB` | Color de acento: el tinte de fondo de la fila del contenedor ZIP en la tabla de hashes (lleva texto en tinta encima). Vacío = tinte de Factum `#E8F3DF`. |

**Logo:** PNG o JPEG (se valida por contenido, no por extensión; SVG no se
acepta), de hasta **1 MiB** y entre **16 y 4096 px** por lado. Para fondo
transparente, PNG. Si es inválido, el backend loguea
`Branding: logo ignorado (<motivo>)` y sigue sin logo: ni el arranque ni los
informes fallan. El logo se lee **una sola vez al arrancar**: para cambiarlo
(o cambiar el nombre o el contacto) hay que **reiniciar el backend**. Se sirve
desde memoria en `GET /api/config/branding/logo`.

**Isotipo y colores:** el isotipo sigue las mismas reglas que el logo (si es
inválido: `Branding: isotipo ignorado (<motivo>)` y se sigue sin él). Los
colores aceptan `#RRGGBB` o `RRGGBB`, sin distinguir mayúsculas; un valor
inválido loguea `Branding: PrimaryColor '<valor>' no es un color #RRGGBB; se
usa el default` y se usa el de Factum. El primario va sobre blanco (filetes y
números), así que **tiene que tener un contraste de al menos 4.5:1 con
blanco**: si no, el backend loguea un warning con el contraste calculado y usa
el default. El acento es fondo de texto, así que **tiene que tener un
contraste de al menos 4.5:1 con la tinta** (`#0E1013`): si no, se loguea
`Branding: AccentColor <hex> tiene contraste <x.x>:1 con la tinta (mínimo
4.5:1); se usa el default` y se usa el tinte de Factum. Los defaults son la
paleta de Factum (la marca del producto); los colores de un estudio son
configuración local. Al arrancar se loguea
`Branding: colores primario <P> y acento <A>`. Los informes ya generados no
cambian: los colores se aplican al generar.

**Los datos reales del cliente nunca van al repo.** En el
`appsettings.json` versionado la sección está vacía, y
`appsettings.Development.json` también está versionado, así que no sirve para
esto:

- **Desarrollo local:** `server/src/Factum.Backend/appsettings.Local.json`
  (ignorado por git, se carga después de `appsettings.{Environment}.json` y
  antes de las variables de entorno) y el logo en
  `server/src/Factum.Backend/branding/` (también ignorada). Ninguno de los dos
  se copia a `bin/`, a `publish/` ni a la imagen Docker.
- **Docker / producción:** variables de entorno, o el mismo
  `appsettings.Local.json` montado como volumen de solo lectura en
  `/app/appsettings.Local.json`. El logo, también como volumen de solo lectura.

Ejemplo de `appsettings.Local.json` (valores ficticios):

```json
{
  "Branding": {
    "OrganizationName": "Dr. Nombre Apellido · Dra. Nombre Apellido",
    "OrganizationLogo": "branding/logo.png",
    "OrganizationIsotype": "branding/isotipo.png",
    "ContactLines": [
      "Calle Ejemplo 123, Ciudad",
      "Cel. +54 9 000 000-0000 · +54 9 000 000-0000"
    ],
    "PrimaryColor": "#203040",
    "AccentColor": "#B0A080"
  }
}
```

Lo mismo con variables de entorno, en el servicio `backend` de un
`docker-compose.override.yml` (valores ficticios):

```yaml
services:
  backend:
    environment:
      Branding__OrganizationName: "Estudio Jurídico Ejemplo"
      Branding__OrganizationLogo: "/app/branding/logo.png"
      Branding__ContactLines__0: "Calle Ejemplo 123, Ciudad"
      Branding__ContactLines__1: "Cel. +54 9 000 000-0000"
      Branding__PrimaryColor: "#203040"
      Branding__AccentColor: "#B0A080"
    volumes:
      - ./branding/logo.png:/app/branding/logo.png:ro
```

**Atribución fija:** todo informe lleva en el pie de cada página el Sello de
Factum y la leyenda **"Realizado con Factum"**. La inyecta el código (no la
plantilla), así que ninguna plantilla la puede sacar.

### Placeholders de la plantilla del informe

La plantilla (`server/src/Factum.Backend/Templates/plantilla_informe_v6.docx`)
no se edita a mano: la genera `ops/plantilla/build_plantilla_v6.py` a partir de
la v4 (que a su vez sale de la plantilla del usuario con
`build_plantilla_v4.py`; ver `ops/plantilla/README.md`). La v6 (diseño
"Filete", sin formas flotantes) tiene dos secciones: la **portada** (filete
verde, título grande, subtítulo y una ficha con causa, carátula, perito y
fecha; abajo el logo, el nombre y el contacto del estudio) y el **interior**
(encabezado de texto chico con la causa y el nombre del estudio sobre una línea
fina; cada sección con su número romano arriba del título y un filete debajo;
pie con "Realizado con Factum" y "Página N de M" en la misma línea, contando la
portada). Se completa en el cuerpo y en los encabezados/pies:

| Placeholder | Reemplazo |
|---|---|
| `{nombreTribunal}`, `{organismoTribunal}`, `{tipoCausa}`, `{numeroCausa}`, `{caratula}`, `{parteDenunciante}`, `{parteDenunciada}`, `{objetoCausa}`, `{ambitoCausa}`, `{fechaIntervencion}` | Datos de la causa (los opcionales vacíos salen como "No informado") |
| `{tramiteAnte}`, `{fraseIntegracion}`, `{fraseDomicilio}`, `{datosProponente}`, `{elSuscripto}` | Frases armadas en código, para que un dato opcional vacío no deje una frase rota |
| `{nombrePerito}`, `{matriculaPerito}`, `{profesionPerito}`, `{caracterPerito}` | La copia del perfil del perito guardada en el caso |
| `{fechaInspeccion}`, `{horaInspeccion}` | Creación del caso, en la zona `Report:TimeZone` |
| `{tipoDispositivo}`, `{marcaModeloDispositivo}`, `{imeiDispositivo}`, `{lineaDispositivo}`, `{titularDispositivo}` | Datos del equipo |
| `{objetoInforme}`, `{descripcion…}` | Textos del paso Informe, un párrafo por línea (párrafo completo) |
| `{nombreArchivo}` / `{hashArchivo}` | Fila modelo de la tabla de hashes: una fila por archivo más la del ZIP |
| `{capturasImeiModelo}`, `{capturasNombreDispositivo}`, `{anexoCapturas}` | Capturas marcadas por rol y anexo con las capturas sin marca (párrafo completo) |
| `{#clave}` … `{/clave}` | Bloque condicional (párrafos propios): desaparece si el dato está vacío |
| `{ORGANIZACION}` | `Branding:OrganizationName` (vacío si no hay). |
| `{CONTACTO}` | `Branding:ContactLines`, una por línea (mismo formato del run) |
| `{CONTACTO_EN_LINEA}` | `Branding:ContactLines` unidas con " · " |
| `{LOGO_ORGANIZACION}` / `{LOGO_ORGANIZACION:4.5x2.5}` | Logo de la organización (párrafo completo), ajustado sin recortar a una caja de 5 × 1.5 cm o del tamaño indicado en cm. Sin logo, el párrafo queda vacío. En la v6 va en el pie de la portada, en 4.5 × 2.5 cm. |
| `{ISOTIPO_ORGANIZACION}` / `{ISOTIPO_ORGANIZACION:2x2}` | Isotipo (párrafo completo), ajustado sin recortar a una caja de 2 × 2 cm o del tamaño indicado. En la v6 va solo al cierre (2 × 2). |
| `{ATRIBUCION_FACTUM}` | Slot (run propio) donde el backend pone el sello y "Realizado con Factum" (B-R8). En la v6 está en el pie interior, a la izquierda de "Página N de M". Si una plantilla no lo trae, la atribución va en un párrafo centrado al final de cada pie. |
| `{#MEMBRETE}` … `{/MEMBRETE}` | Bloque de identidad del estudio (logo, nombre, contacto). En la v6 está en el pie de la portada; desaparece entero si no hay nombre, logo ni contacto. |
| `{#MEMBRETE_CON_LOGO}` … `{/MEMBRETE_CON_LOGO}` | Dentro de `MEMBRETE`: queda solo si hay logo (en la v6, tabla logo \| nombre y contacto). |
| `{#MEMBRETE_SIN_LOGO}` … `{/MEMBRETE_SIN_LOGO}` | Dentro de `MEMBRETE`: queda solo si **no** hay logo (nombre y contacto en texto, sin una celda vacía). |
| `{#ISOTIPO}` … `{/ISOTIPO}` | Bloque que solo queda si hay `Branding:OrganizationIsotype` válido. |
| `{descripcionSelloTiempo}` | Bloque explicativo del sellado de tiempo (párrafo completo, texto plano): qué autoridad selló, el sello del ZIP o la constancia de que no lo tenía al emitirse, y "N de M archivos" con sello. Ver [Sellado de tiempo](#sellado-de-tiempo-rfc-3161). |
| `{#selloTiempo}` … `{/selloTiempo}` | Bloque debajo de la tabla de hashes que solo queda si el sellado estaba activo al generar (flujo agent). Con el sellado desactivado, o en el flujo anterior, desaparece y el informe queda como antes. Las líneas "Sello de tiempo: …" de cada fila de la tabla las agrega el backend, no la plantilla. |
| `{fechaFichaTecnica}`, `{horaFichaTecnica}` | Fecha y hora (zona `Report:TimeZone`) en que el **servidor** recibió la ficha técnica del dispositivo. Ver [Ficha técnica del dispositivo y diferencia de reloj](#ficha-técnica-del-dispositivo-y-diferencia-de-reloj). |
| `{fichaSistemaOperativo}`, `{fichaCompilacion}`, `{fichaParcheSeguridad}`, `{fichaNumeroSerie}`, `{fichaIdentificadorConexion}`, `{fichaKernel}`, `{fichaOperadorSim}`, `{fichaEstadoArranque}`, `{fichaZonaHoraria}`, `{fichaWifi}` | Filas de la ficha técnica leída por Tatana (una línea cada una). Lo que el dispositivo no informa sale "No disponible (motivo)". El estado de arranque va con los valores crudos, sin calificar. El nombre de la red wifi solo sale si el perito marcó "Incluir el nombre de la red en el informe". En la v6, las 10 filas forman una segunda ficha etiqueta/valor. |
| `{descripcionRelojDispositivo}` | Párrafo "Reloj del dispositivo" (texto plano, un párrafo por línea): la diferencia celular/estación de trabajo al inicio y, si se midió al generar, al cierre; la diferencia estación de trabajo/servidor solo si supera el umbral; la frase de constancia si alguna diferencia supera `Report:ClockSkewWarningSeconds`; y la zona horaria configurada en el dispositivo. |
| `{#fichaTecnica}` … `{/fichaTecnica}` | Bloque de la sección del dispositivo, después de "Titular". Solo queda si el caso tiene ficha técnica; los casos anteriores (o creados con un Tatana sin `device_details_v1`) generan el informe de siempre. |

El verde de la plantilla v6 (`2F6F12`, el default del primario) funciona como
centinela: al generar, el backend lo reemplaza por `Branding:PrimaryColor` en
el cuerpo, los encabezados, los pies, la numeración y los estilos. El tinte
(`Branding:AccentColor`) no está en la plantilla: el backend lo aplica como
fondo de la fila del contenedor ZIP al llenar la tabla de hashes.

Un placeholder que la plantilla traiga y Factum no conozca se borra y se
loguea un warning (`Plantilla: placeholder desconocido {x}`).

## Informe pericial: configuración

El informe es el **Informe Pericial Técnico Informático** del perito de parte.
Los datos del perito salen del perfil de cada usuario ("Mi perfil de perito",
colección `expert_profiles`) y se copian al caso al crearlo o editarlo. Lo que
es propio del estudio va en la sección `Report` del backend:

| Clave | Qué es |
|---|---|
| `Report:TimeZone` | Zona IANA de la fecha y hora de la inspección (default `America/Argentina/Buenos_Aires`). Si no existe en el sistema, se loguea un warning y se usa UTC-03:00 fijo. |
| `Report:DomicilioConstituido` | Domicilio constituido del perito, para la presentación ("…, con domicilio constituido en …"). Máx. 300 caracteres. Vacío = la frase se omite. |
| `Report:DefaultTexts:OperacionesRealizadas` / `AseguramientoEvidencia` / `NotasTecnicas` / `Reserva` | Textos por defecto propios del estudio para el paso Informe. Vacío = el texto neutro versionado en `Services/Reports/ReportDefaultTexts.cs`. Admiten los tokens `{fechaInspeccion}`, `{horaInspeccion}`, `{tipoDispositivo}`, `{marcaModeloDispositivo}`, `{imeiDispositivo}`, `{sistemaOperativo}`, `{zonaHoraria}`, `{elSuscripto}` y `{cantidadCapturas}`/`{cantidadGrabaciones}`/`{cantidadArchivosExtraidos}`/`{cantidadOtros}`, más los de la ficha técnica del dispositivo: `{diferenciaReloj}` ("el dispositivo adelantaba 10 s respecto de la estación de trabajo (± 1 s)"), `{numeroSerieDispositivo}`, `{compilacionDispositivo}` y `{zonaHorariaDispositivo}` (el valor, o "No disponible" si el caso no tiene ficha o el dato no se pudo leer). Con ficha, `{sistemaOperativo}` usa la versión textual leída del dispositivo ("Android 8.1.0"). El texto se interpreta como **Markdown línea por línea**: cada línea es un párrafo (una línea vacía en el medio deja un párrafo vacío); `- ` al principio es viñeta y `1. ` numerada; adentro de la línea valen `**negrita**`, `*cursiva*` y `<u>subrayado</u>`. Sin HTML (salvo `<u>`), sin imágenes y solo enlaces `http`, `https` o `mailto`: un texto configurado con algo de eso se descarta con un warning al arrancar y se usa el texto versionado. Los valores de los tokens se escapan (un `_` del dato no se vuelve cursiva). |
| `Report:EncryptZip` | Cifra el ZIP de evidencia con AES-256 (formato WinZip AE-2) y una contraseña aleatoria por caso (default `true`; variable de entorno `Report__EncryptZip`). Con `false` el ZIP sale sin cifrar y sin contraseña, el backend loguea un warning al arrancar y `GET /api/config/public` devuelve `encrypt_zip: false` para que el wizard no prometa cifrado. El texto por defecto de aseguramiento de la evidencia cambia según este valor; si se define `Report:DefaultTexts:AseguramientoEvidencia`, ese texto **gana en los dos modos** y el estudio es responsable de que hable (o no) del cifrado. |
| `Report:ClockSkewWarningSeconds` | Umbral, en segundos, de la diferencia de reloj celular/PC (y PC/servidor) a partir del cual la web muestra un aviso y el informe deja constancia (se supera estrictamente). Default `60`; fuera de 1…86400 se loguea un warning y se usa 60. Variable de entorno `Report__ClockSkewWarningSeconds`; en la nube y en Windows, `FACTUM_RELOJ_UMBRAL_SEGUNDOS`. La web lo lee de `clock_skew_warning_seconds` en `GET /api/config/public`. |

Como con `Branding`, **el domicilio real no va al repo**: va en
`appsettings.Local.json` (ignorado por git) o en `Report__DomicilioConstituido`.
La config se lee una sola vez al arrancar. Ejemplo (valores ficticios):

```json
{
  "Report": {
    "TimeZone": "America/Argentina/Buenos_Aires",
    "DomicilioConstituido": "Calle Ejemplo 123, Ciudad"
  }
}
```

**Hashes:** el ZIP de evidencia se cierra antes de generar el informe y no se
vuelve a escribir, así que el hash del ZIP que figura en la tabla del informe es
el del ZIP que se descarga: el del archivo **cifrado** final, que se puede
verificar (`shasum -a 256`) sin la contraseña. Antes de borrar los archivos
sueltos, el backend reabre el ZIP en solo lectura con la contraseña y compara el
SHA-256 de cada entrada con el del original; si algo no coincide, la generación
falla, los sueltos se conservan y el caso se puede reintentar. AES-ZIP usa sal
aleatoria por entrada, así que el mismo contenido da otro hash del ZIP en cada
generación (no es reproducible; un caso generado no se regenera). El DOCX va
aparte (no dentro del ZIP) y su SHA-256 se guarda en el caso (`report_hash`); el
informe no puede contener su propio hash.

**Contraseña del ZIP:** no figura en el informe ni en las respuestas del caso
(`zip_password` no se serializa). Se devuelve una vez en
`POST /api/cases/{id}/generate` (`password`, `null` si no se cifró) y después
solo al dueño del caso por `GET /api/cases/{id}/zip-password`
(`{ "password": "…" }`, `Cache-Control: no-store`; 403 para un caso ajeno, 404
si el caso no tiene un ZIP cifrado).

### Evidencia en la PC del perito

Desde `zip-local-informe-servidor`, en un caso nuevo **ningún byte de evidencia
llega al servidor**: las capturas, grabaciones y archivos quedan en la PC del
perito, en la carpeta de trabajo del caso de Tatana
(`<Agent:DataDirectory>/cases/<id>/`), y el backend guarda solo un
**manifiesto** (nombre, tamaño, SHA-256, ruta de origen y PC) que el navegador
registra con `PUT /api/cases/{id}/evidence`. Generar pasa por tres etapas:

1. `POST /api/cases/{id}/generate/prepare` — el backend valida el caso contra el
   manifiesto, genera la contraseña y abre un intento de generación (el caso
   **no** cambia de estado).
2. `POST http://localhost:8765/cases/{id}/zip` — Tatana verifica cada archivo
   contra los SHA-256 del manifiesto, arma el ZIP AES-256 en
   `<Agent:EvidenceDirectory>/<causa>_<id8>/.factum/pendiente/`, lo reabre para
   verificarlo y calcula su hash.
3. `POST /api/cases/{id}/generate/finish` (multipart) — el navegador manda el
   hash y la ubicación del ZIP y **solo las capturas que el informe embebe**. El
   backend las escribe en `.generate-tmp/`, arma el DOCX, lo guarda en
   `cases/<id>/` (es lo único que queda en el servidor), borra el temporal y
   marca el caso `completed`.

Recién entonces Tatana pasa el ZIP a su nombre final (`/zip/commit`) y borra los
archivos sueltos de la carpeta de trabajo. Si el navegador se cierra entre el
paso 3 y el commit, el ZIP pendiente tiene el hash registrado y se confirma la
próxima vez que se abre el caso en esa PC. Los casos y borradores con evidencia
ya subida al servidor siguen con el flujo anterior (`POST /files` +
`POST /generate`) hasta cerrarse; el JSON del caso trae `evidence_storage`
(`"agent"` o `"server"`) para que la web sepa cuál usar.

**Regenerar las plantillas v4 y v6** (por ejemplo, si cambia la plantilla de
origen o el diseño): ver `ops/plantilla/README.md`.

### Sellado de tiempo (RFC 3161)

Opcional. Con `Timestamping:Urls` configurado, el backend pide a una
**autoridad de sellado de tiempo** externa (TSA) un sello RFC 3161 sobre el
**hash SHA-256** de cada objeto — nunca manda el contenido. El sello es una
constancia firmada por un tercero de que ese hash existía en esa fecha y hora,
según el reloj de la autoridad (no el de la PC del perito ni el del servidor), y
se verifica con herramientas estándar (`openssl ts -verify`) sin Factum.

- **Qué se sella** (solo en el flujo de evidencia en la PC del perito): cada
  archivo de evidencia al registrarlo en el caso (`PUT …/evidence`), el ZIP al
  generar (antes de armar el DOCX, así el informe lo cita) y el DOCX después de
  generarlo (el informe no puede contener su propio sello). Cada sello se guarda
  en la colección `case_timestamps` con la autoridad, la fecha certificada, el
  número de serie y el `.tsr` completo.
- **Si la autoridad no responde, nada se bloquea:** el sello queda pendiente y
  una cola en segundo plano lo reintenta (1, 2, 5, 10, 30 min, … hasta 24 h
  entre intentos; 12 intentos, unas 52 h). El guardado espera como máximo
  `InlineBudgetSeconds` y, tras un fallo total, los siguientes no esperan
  durante `InlineCooldownSeconds`. Si el ZIP no tenía sello al emitirse el
  informe, el informe lo dice; un sello que llega después lleva su fecha real.
- **Dónde se ve:** el informe (una línea por archivo en la tabla de hashes y un
  bloque debajo), la web (la bandeja, el resultado, el detalle y la
  trazabilidad, con los eventos `timestamp_granted`/`timestamp_failed`) y la
  carpeta `sellos/` al lado del ZIP en la PC del perito (los `.tsr` y un
  `LEEME-verificacion.txt` con los comandos de verificación; la copia la hace
  Tatana con `case_seals_v1`). También se descargan desde la web
  (`GET /api/cases/{id}/timestamps/bundle`).
- **Autoridades por defecto en desarrollo:** FreeTSA y DigiCert, las dos
  verificadas con `openssl ts -verify`. En la nube y en Windows arranca
  **desactivado** (`FACTUM_SELLADO_URLS` vacío) hasta que el estudio elija la
  autoridad. **Factum no afirma la validez legal de ninguna autoridad**: se elige
  con asesoramiento jurídico.
- **Desactivarlo:** vaciar `FACTUM_SELLADO_URLS` o poner
  `FACTUM_SELLADO_HABILITADO=false`. Los sellos ya obtenidos siguen visibles y
  descargables; los pendientes no se reintentan. El log de arranque dice
  `Sellado de tiempo: habilitado (<hosts>)…` o `Sellado de tiempo: deshabilitado`.
- **Casos anteriores y flujo viejo:** no se re-sella nada. Un caso generado
  antes de activar el sellado, o del flujo anterior (evidencia en el servidor),
  no tiene sellos y la web lo indica.

### Ficha técnica del dispositivo y diferencia de reloj

Al pasar al paso 2 del wizard, la web le pide a Tatana (`GET /devices/{serial}/details`, capability
`device_details_v1`) una lectura **de solo lectura** del celular conectado: número de serie, versión y
compilación, parche, kernel, operador y SIM, estado de arranque (valores crudos), zona horaria, hora automática y
wifi. En Android son `getprop`, `uname`, `settings get`, `cmd wifi status` (o `dumpsys wifi` filtrado en el
celular, para que BSSID, MAC e IP nunca salgan del dispositivo) y `date`; en iPhone, una lista blanca de valores
de lockdown (nunca ICCID, IMSI, número de línea ni direcciones MAC). Nada escribe en el dispositivo. Al crear el
caso, la ficha se guarda en `Case.DeviceDetails` (`PUT /api/cases/{id}/device-details`) con la hora de recepción
del **servidor**; la API vuelve a validar cada valor (un dato con formato inesperado queda "No disponible (el
valor devuelto no tiene un formato válido y se descartó)") y deja el evento `device_details_recorded` en la
trazabilidad, sin valores ni nombre de red. Un Tatana sin `device_details_v1` no impide crear el caso: queda sin
ficha y el informe sale como siempre.

**Diferencia de reloj.** Tatana toma 3 muestras: hora de la PC antes (`b`), hora del celular (`d`, `date +%s.%N`
o `TimeIntervalSince1970` de lockdown) y hora de la PC después (`a`). Se elige la muestra válida de menor ida y
vuelta (RTT = `a − b`, como mucho 5 s) y se calcula:

- `device_minus_pc_ms = round(d − (b + RTT/2))` (positivo = el celular adelanta);
- `margin_ms = ceil(RTT/2 + resolución)`, con resolución 1 ms (o 1000 ms si el `date` del celular no informa
  fracciones).

La hora del celular se leyó en algún instante entre `b` y `a`, así que el error del punto medio está acotado por
RTT/2 más la resolución: es una **cota estricta**, no estadística. La API **recalcula** las cifras desde las horas
crudas (no confía en las que manda el navegador). Además, el navegador hace un sondeo de una vuelta contra
`GET /api/time` y la API calcula de la misma forma la diferencia **PC/servidor** (`pc_minus_server_ms`, margen
`ceil(RTT/2) + 1`), solo si Tatana corre en la misma PC (`AGENT_URL` en loopback); si no, queda `not_measured`.
Al generar (flujo agent), si el mismo celular sigue conectado, se hace una **medición de cierre**; una medición
de un intento anterior no entra al informe.

Si alguna diferencia supera `Report:ClockSkewWarningSeconds`, la web muestra un aviso no bloqueante y el informe
agrega "Se deja constancia de que el reloj del dispositivo presentaba una diferencia de … respecto de la estación
de trabajo; las horas visibles en las capturas corresponden al reloj del dispositivo." Nunca se sugiere corregir
la hora del celular.

**Riesgo aceptado:** la hora del servidor del sondeo PC/servidor vuelve desde el navegador y el propio perito
podría manipularla. No obtiene nada que no pueda lograr cambiando la hora de su PC; la API acota el valor a los
últimos 300 s del reloj del servidor.

**El nombre de la red wifi** se guarda en el caso (lo ve el perito en la web) pero va al informe solo si marca
"Incluir el nombre de la red en el informe" (`include_ssid_in_report`, por defecto desmarcado); nunca va a los
logs ni a la trazabilidad.

### Grabación de la recolección (video de la sesión completa)

En el paso 3 del asistente, Factum puede grabar en video la pantalla del celular **durante toda la recolección**
(Android; el iPhone y la cámara del procedimiento llegan en una versión siguiente). La grabación arranca al entrar
al paso y termina al salir; Tatana la guarda en **tramos** (`sesion_pantalla_NNN_<fecha>_<hora>.mkv`) y cada
tramo, apenas se cierra, se mueve a la carpeta del caso, se le calcula el SHA-256, se registra en el expediente y
se sella (si el sellado está activo). Los cortes quedan registrados como **huecos** con hora y motivo (salir del
paso, retomar otro día, desconexión del USB con reanudación automática al reconectar el mismo celular, cierre de
Tatana con recuperación del tramo al reabrir, falta de espacio y detención por el perito). "Grabar" pasa a ser
"Marcar momento", y cada captura de pantalla deja una marca automática con el tramo y el segundo del video.

- **Configuración de la instalación (backend):** `Recording:SessionMode` (`FACTUM_GRABACION_SESION_MODO`):
  `disabled` (no se ofrece), `optional_off` (default: el perito la activa), `optional_on` (viene activada) o
  `required` (obligatoria; con un Tatana viejo se avisa pero no bloquea: para exigirla de verdad, fijar
  `FACTUM_TATANA_VERSION_MINIMA` a la versión con la capability `recording_session_v1`).
  `Recording:SegmentMinutes` (`FACTUM_GRABACION_SESION_TRAMO_MINUTOS`), de 5 a 120 (default 30). Un valor inválido
  no impide arrancar: queda el default y un aviso en el log. Las dos salen en `GET /api/config/public`
  (`recording_session_mode`, `recording_session_segment_minutes`).
- **Parámetros por PC (Tatana):** `Agent:Session:*` en `appsettings.json` / `appsettings.Local.json` (tasas para
  estimar el espacio, umbrales de aviso y de corte, reintentos). `Agent:Session:MockSegmentSeconds` acorta los
  tramos a segundos para probar (en mock o en desarrollo).
- **Espacio en disco:** para empezar hacen falta `Agent:MinFreeBytes` + dos tramos libres (a 30 MiB/min por
  defecto, o a la tasa real del último tramo). La web muestra las horas estimadas (contando la copia del ZIP si va
  al mismo disco). Por debajo de una hora aparece un aviso y, cuando quedan dos minutos, la grabación se corta sola,
  limpia, con el hueco "sin espacio".
- **Entrega:** al generar, los tramos van **dentro del ZIP cifrado**, en la carpeta `sesion/` (ZIP64 + AES-256,
  verificado con 5 y 10 GiB). Armar un ZIP de 10 GiB tarda unos minutos (más en un disco mecánico). Sus sellos van
  planos en `sellos/` y el `LEEME-verificacion.txt` indica `Dentro del ZIP: "sesion/<archivo>"`. El informe suma
  la sección condicional **"Registro audiovisual de la recolección"** (tabla de tramos con hash y sello, huecos,
  marcas y la correlación con el reloj del celular); los tramos no se repiten en la tabla general ni cuentan como
  grabaciones.
- **Los tramos no se pueden quitar del caso** (Tatana responde `session_file_protected` y el backend
  `recording_session_file`): son el registro de la recolección.
- **Audio:** la pista de pantalla graba el audio interno del celular en Android 11+ (como los clips) y nunca el
  micrófono de la PC. Mientras graba, el celular puede no reproducir sonido (Android 11–12).
- **"Pantalla siempre encendida":** para no perder imagen entre tramos, el tramo nuevo arranca antes de cerrar el
  anterior (~0,7 s de solapamiento). Como cada scrcpy restaura el ajuste al cerrarse, Tatana vuelve a poner el mismo
  valor que pone scrcpy (`stay_on_while_plugged_in = 7`) después de cada corte y restaura el original al terminar.
  Si Tatana se cae y el celular no vuelve a esa PC, el ajuste queda en 7 hasta reconectarlo (se restaura solo) o
  cambiarlo a mano.
- **Riesgo ante un corte de luz o un cuelgue de la PC:** scrcpy escribe el video a disco por bloques de 256 KiB. Si
  **scrcpy** muere de golpe, se pierde lo que no llegó a disco: menos de un segundo con movimiento, pero pueden ser
  minutos con la pantalla quieta (o el tramo entero si empezó quieto). Lo legible se recupera al reabrir Tatana; si
  no hay nada recuperable, el hueco dice "el tramo en curso no se pudo recuperar". Si se cierra solo **Tatana**
  (lo más común), scrcpy sigue vivo y la recuperación lo cierra limpio: el tramo queda completo. Los tramos ya
  cerrados no se ven afectados.
- **Diario en la PC:** `<Agent:DataDirectory>/sessions/<caseId>/sesion.json` (y el tramo en curso, que nunca
  aparece como evidencia hasta cerrarse). Se borra solo al confirmar el ZIP del caso y si no quedó nada abierto.

## Integración de soporte (Faro)

Es **opcional y está apagada por defecto**. Apagada, el backend no llama a Faro,
`GET /api/config/public` devuelve `support_enabled: false`, el dashboard no
muestra el soporte y `/api/support/*` responde `404`.

Encendida, Factum y Faro comparten la misma identidad de usuario (DNI). Desde el
dashboard de Factum, un usuario puede:

1. **Reportar un problema** (botón del ícono del faro) — se crea un token de
   soporte real en Faro, con el DNI, nombre, número interno y teléfono del
   oficial.
2. **Ver el estado de sus reportes y calificarlos** sin salir de Factum.
3. **Abrir Faro ya logueado** ("Ver todo en el portal de soporte") — usa un código de
   intercambio de un solo uso (SSO) generado por Faro, así el oficial no
   vuelve a poner su contraseña ahí.

Para activarla en local, con el backend de Faro corriendo (ver su README),
agregá en `server/src/Factum.Backend/appsettings.Local.json` (ignorado por git):

```json
{
  "Integrations": {
    "Support": {
      "Enabled": true,
      "BaseUrl": "http://localhost:5038",
      "ServiceKey": "<clave-compartida-con-faro>",
      "FrontendUrl": "http://localhost:3001"
    }
  }
}
```

`ServiceKey` tiene que ser idéntica al `Integrations:ServiceKey` configurado en
Faro. En Docker/producción se usan las variables de entorno
`Integrations__Support__Enabled`, `Integrations__Support__BaseUrl`,
`Integrations__Support__ServiceKey` y `Integrations__Support__FrontendUrl`. Con
`Enabled=true`, si falta `BaseUrl`, `ServiceKey` o `FrontendUrl` el backend no
arranca y dice qué clave falta.

Una sección vieja `FaroIntegration` **no activa** el soporte: el backend arranca
con el soporte apagado y avisa en el log. Con `Enabled=true`, las claves que
falten en `Integrations:Support` se toman de `FaroIntegration` (también con un
warning); conviene renombrarlas.

## Estructura del proyecto

```
factum/
├── client/                        # Frontend Next.js
│   └── src/
│       ├── app/                   # Rutas (login, dashboard)
│       ├── components/            # Wizard, dashboard, soporte, UI
│       └── lib/                   # Cliente de API, tipos, agente
├── server/src/
│   ├── Factum.Backend/         # API principal (.NET)
│   │   ├── Controllers/
│   │   ├── Services/
│   │   │   ├── Auth/              # Proveedores de identidad (Dev/External)
│   │   │   ├── Branding/          # Identidad de la organización (nombre, logo, contacto)
│   │   │   ├── Reports/           # Informe DOCX + ZIP
│   │   │   └── Support/           # Integración de soporte opcional (Faro)
│   │   └── Models/
│   └── Factum.Agent/           # Agente local "Tatana" (.NET)
├── agent-ui/                      # UI de escritorio del agente (Electron)
├── deploy/
│   ├── windows/                   # Instalación local en la PC de un estudio
│   └── cloud/                     # Producción en la nube (VPS): compose, Caddy, scripts
├── .github/workflows/             # Verificación de PRs, deploy a producción y release de Tatana
├── ops/tatana/                    # Firma del canal de Tatana y prueba del instalador en Windows
├── docker-compose.yml             # mongo + backend + frontend (desarrollo)
└── AGENTE_TATANA.md               # Casos de uso del agente
```

## Despliegue del agente en la PC del fiscal

Hay dos formas de correr Tatana; las dos hablan con el mismo `client` vía
`localhost:8765`, traen adb, scrcpy, ffmpeg y un Python embebido con
`pymobiledevice3` (en `tools/`, que `ToolResolver` prefiere al `PATH`) y no piden
permisos de administrador:

- **Instalado, para Factum en la nube** (`agent-ui`, Electron + NSIS per-user,
  `Tatana-Setup-Windows.exe`): lo arma, lo prueba en un runner Windows, lo firma
  y lo publica `.github/workflows/tatana-release.yml` (tag `tatana-vX.Y.Z` o
  "Run workflow", solo desde `main`). Se actualiza solo desde
  `https://<dominio>/tatana/updates/` (archivos estáticos de Caddy en el VPS):
  cada versión lleva un manifiesto `tatana-update.json` **firmado con Ed25519**
  cuya clave pública está horneada en Tatana (`agent-ui/src/main/updater/trusted-keys.ts`),
  así que un servidor comprometido no puede instalar nada en las PCs. Instala
  solo al reiniciar Tatana o al iniciar sesión, nunca con una operación en
  curso (`GET /agent/state`, `POST /agent/maintenance`). Al instalarse migra el
  portátil para la nube que encuentre sin tocar la evidencia. Guía:
  [docs/despliegue-nube.md](docs/despliegue-nube.md), secciones 10 y 19.
- **Portátil** (`packaging/portable/`, armado con
  `deploy/windows/armar-tatana-portable.sh`): el de la instalación local
  (`deploy/windows/`). `install-portable.bat` copia todo a
  `%LOCALAPPDATA%\Programs\Tatana` y arma el autostart con un acceso directo en
  el `Startup` del usuario. Se actualiza con `Actualizar Factum.bat` (ya no hay
  auto-actualización del portátil: el viejo `update-portable.ps1`, sin
  verificación, se retiró).

Tatana reporta su versión real (`/health.version`, `/info.version`, capability
`real_version_v1`), que llega a la auditoría. Los Tatana anteriores al
instalador decían siempre `"2.0.0"`. El backend puede exigir una versión mínima
(`Tatana:MinVersion`).

**Auditoría de uso**: cada acción de captura (screenshot, grabación, webcam) y
el arranque del wizard reportan al backend quién (DNI), desde qué PC
(hostname, IP, modo instalado/portátil) y para qué caso —
`POST /api/agent-events`, colección Mongo `agent_events`. La lectura
(`GET /api/agent-events`) está limitada a los DNIs en `Audit:AdminDnis`
(`appsettings.json`) y, en `Auth:Mode=local`, a los superadmins.

## Notas

- El agente (`Factum.Agent`) y su UI (`agent-ui`) están pensados para
  correr en la PC del oficial, con acceso físico al USB — por diseño no
  forman parte de `docker-compose.yml`.
- La evidencia capturada (fotos/video) no se sube a ningún servidor externo
  fuera de este sistema; el ZIP final queda cifrado con AES-256 y su
  contraseña se entrega por separado.

## Licencia

Factum es software libre, publicado bajo la **Licencia Pública General de GNU,
versión 3 (GPL-3.0)** — ver [`LICENSE`](./LICENSE). Copyright © 2026 Joel
Serrudo.

Esto significa que cualquiera que reciba Factum puede usarlo, estudiarlo,
modificarlo y redistribuirlo, y tiene derecho a recibir su código fuente. Las
obras derivadas deben distribuirse también bajo GPL-3.0. La licencia **no**
impide desplegar Factum ni ofrecerlo como servicio; sí obliga a entregar el
código fuente a quien reciba el software.

Factum integra o deriva de componentes de terceros bajo licencias compatibles
(p. ej. herramientas forenses GPL). Los binarios de aplicaciones de terceros
(como las APK que se usan en una extracción) **no** se redistribuyen con
Factum: los aporta la institución que lo opera, bajo su propio marco legal.
