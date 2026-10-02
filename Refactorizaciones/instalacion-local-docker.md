# SDD: Instalación local de Factum en la PC Windows de un estudio (Docker + Tatana nativo)

**Slug:** `instalacion-local-docker`
**HU:** `docs/hu-instalacion-local-docker.md` (validada 2026-10-02: D1–D20 = A; D7 = `Auth:Mode=dev`
por ahora; D18 = licencia gratuita de Docker Desktop OK, PC Windows 10 con specs desconocidas, así que
`instalar.ps1` chequea los requisitos).
**Rama:** `feat/instalacion-local-docker` (ya existe). Va contra `develop`.
**Orden de implementación:** 1) `implementer-frontend` (imagen del `client/`), 2) `implementer-backend`
(imagen del backend, Tatana, `deploy/windows/`, empaquetado, guía y la verificación integrada, que
necesita la imagen del frontend). Se pueden lanzar en paralelo si el backend deja la verificación
integrada (§9.2, pasos B-V4 a B-V7) para el final, después de que el frontend devuelva `done`.

---

## 1. Resumen funcional

Factum se instala en una sola PC Windows 10/11 de un estudio jurídico: MongoDB, backend y frontend
corren en Docker Desktop con un compose de producción local (`deploy/windows/docker-compose.yml`). Las
imágenes salen preconstruidas en `linux/amd64` desde la Mac del desarrollador y viajan en un `.tar`
dentro de un paquete que no necesita internet. Web y API publican solo en `127.0.0.1`, Mongo no se
publica, y cada servicio tiene healthcheck y `restart: unless-stopped`. La evidencia vive en
`C:\Factum\evidencia` (bind mount) y la configuración del estudio en `C:\Factum\config` (montada de solo
lectura). Unos scripts PowerShell compatibles con Windows PowerShell 5.1 instalan (después de chequear
los requisitos y explicar en castellano qué falta), actualizan (con backup previo y vuelta atrás
automática), respaldan (con manifiesto SHA-256), restauran (verificando el manifiesto antes de tocar
nada) y diagnostican. Tatana se entrega como el **portátil** que ya existe, armado localmente desde un
`git archive` limpio con `Mock=false` verificado. Su servidor HTTP pasa a escuchar solo en `localhost`.
La guía `docs/instalacion-windows.md` explica todo paso a paso para una persona no técnica. Sin cambios
funcionales en la API, en `client/` ni en `agent-ui/`.

## 2. Toca

| Lado | ¿Toca? | Detalle | Implementador |
|---|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí (solo empaquetado)** | `Dockerfile` (build en `$BUILDPLATFORM`, sin LibreOffice, con `curl`) y un comentario en `ReportService.cs`. Sin cambios de lógica. | backend |
| backend (Tatana) `server/src/Factum.Agent` | **sí** | `Program.cs` (bind configurable, `localhost` por defecto, `appsettings.Local.json` opcional, log del mock efectivo), `Models/AgentModels.cs` (`BindAddress`), `.csproj` (no publicar `appsettings.Local.json`), **borrar** `Dockerfile`. `appsettings.json`: **no se toca** (ver D3). | backend |
| client `client/` | **sí (solo empaquetado)** | `Dockerfile` (standalone, build args, usuario no root), **nuevo** `.dockerignore`, `next.config.ts` (`output: "standalone"`). Sin cambios en `src/`. | frontend |
| agent-ui `agent-ui/` | **no** | D4 de la HU = portátil. `config.ts` (`serverUrl` 5000) queda fuera de alcance. | — |
| Infra / empaquetado | **sí** | **Nuevo** `deploy/windows/**`. `packaging/portable/launch-tatana.bat` (no abrir el navegador si `CLIENT_URL` está vacío). `docker-compose.yml` de la raíz (`NEXT_PUBLIC_*` como build args). `.gitignore` (`deploy/windows/dist/`). | backend |
| Docs | **sí** | **Nuevo** `docs/instalacion-windows.md`. `README.md` (link a la guía, `--mock`/`appsettings.Local.json` del agente, `--bind`). | backend |
| MongoDB | **no** | Sin cambios de esquema ni de documentos. Las instalaciones nuevas arrancan con la base vacía. | — |
| `.gitlab-ci.yml` | **no** | El remoto real es GitHub (`origin = github.com/senkuch4n/Factum`); el CI de GitLab no se toca. El portátil se arma localmente (D6). | — |

## 3. Hallazgos de la verificación técnica (sobre el código real)

- **H1. `Agent:Mock` en HEAD ya es `false`.** `git diff` muestra que el `true` es solo un cambio local
  sin commitear del usuario (único commit del archivo: `89912b5`). No hay nada que commitear en ese
  archivo. El riesgo real es que un build hecho **desde la copia de trabajo** se lleve el `true`
  (`dotnet publish` copia `appsettings.json` al lado del `.exe`). Ver D3 y D6.
- **H2. Proyecto `factum` ya usado en la Mac.** Existen los contenedores `factum-backend-1` y
  `factum-mongo-1` y los volúmenes `factum_mongo-data` y `factum_backend-data` del compose de la raíz.
  En la Mac, cualquier `docker compose` de esta HU usa **otro** nombre de proyecto
  (`-p factum-verif`). El compose de producción **no** fija `name:` ni `container_name:` ni nombres de
  volumen absolutos (ver D9).
- **H3. El puerto 27017 de la Mac lo ocupa `evidentia-v2-mongo-1`** (otro proyecto). El compose de
  producción no publica Mongo y la verificación no publica ningún puerto (override con `!reset`).
- **H4. `client/.env.local` existe** (hoy con `localhost`, pero sin `.dockerignore` entra al contexto de
  build). `next.config.ts` hornea `NEXT_PUBLIC_*` en `next build`. El `environment:` del compose no
  tiene efecto en el navegador.
- **H5. Ningún fetch del lado servidor de Next.** `src/lib/api.ts` y `src/lib/agent.ts` se usan desde
  client components; no hay `route.ts`, `middleware.ts` ni `proxy.ts`. Que `localhost:8080` dentro del
  contenedor del frontend no sea el backend no importa: solo el navegador (en Windows) llama a la API.
- **H6. `next/font/google`** (`layout.tsx`) descarga las fuentes **en el build** (necesita internet en
  la Mac) y las sirve desde la app. En runtime no hace falta internet.
- **H7. `next` trae `sharp` como dependencia opcional.** Con `output: "standalone"`, el trace copia
  binarios nativos de la plataforma de **build**. Por eso el build del frontend corre en la plataforma
  destino (`linux/amd64` emulada), no en `$BUILDPLATFORM` (D11).
- **H8. Backend:** ya existe `GET /health` (minimal API: `{ status, version, auth_mode }`). La imagen
  `aspnet:10.0` (Ubuntu) no trae `curl`. Kestrel escucha con `ListenAnyIP(PORT=8080)`; el `--urls` del
  `ENTRYPOINT` es redundante (inofensivo).
- **H9. `soffice`** solo aparece en `ReportService.cs` L1293-1330, marcado "Sin uso". Sin LibreOffice
  en la imagen, ese método devolvería "LibreOffice no encontrado", pero nadie lo llama. Se saca
  `libreoffice-writer` (D13 de la HU) y se corrige el comentario.
- **H10. Agente:** `Program.cs` L46 hace `UseUrls($"http://0.0.0.0:{port}")`. El log final imprime
  `mock` = solo el flag de CLI, no el valor efectivo (con `Mock: true` en config dice `mock=False`).
  `HealthController` sí devuelve el efectivo (`opts.Value.Mock`). El agente no carga
  `appsettings.Local.json` (el backend sí). `**/appsettings.Local.json` ya está en `.gitignore`.
- **H11. Portátil:** `launch-tatana.bat` hace `cd /d %~dp0`, así que el content root del agente es la
  carpeta de instalación y el `appsettings.json` publicado es el que manda. Siempre abre el navegador en
  `CLIENT_URL` a los 2 s del login de Windows, cuando Docker todavía no levantó (página de error).
  `tatana-portable.ini` del repo trae `UPDATE_URL=http://localhost:8080/tatana/updates`: en el estudio
  ese endpoint no tiene releases y cada arranque espera hasta 5 s por nada.
- **H12. Windows PowerShell 5.1** (el que trae Windows 10) tiene trampas que la SDD fija como reglas:
  `>` y `|` de un comando nativo **re-codifican** binarios (un `mongodump > x.gz` sale corrupto); los
  `.ps1` con acentos sin BOM se leen como ANSI; no existen `??`, `?:` ternario, `&&`/`||` ni
  `-Parallel`.
- **H13. Branding:** `Branding:OrganizationLogo` es relativo al ContentRoot (`/app`). Montar
  `config\branding` en `/app/branding` hace que `"branding/logo.png"` del README funcione igual.

## 4. Modelo de datos y persistencia

Sin colecciones, campos ni índices nuevos. Lo que cambia es **dónde** viven los datos en la PC del
estudio:

| Dato | Dónde | Cómo |
|---|---|---|
| Base `factum` (casos, `zip_password`, `report_hash`, perfiles, catálogos, `agent_events`) | Volumen Docker `<proyecto>_mongo-data` (en el estudio: `factum_mongo-data`) | Volumen con nombre (D8 de la HU). Backup con `mongodump`. |
| Evidencia (`/data/cases/<caseId>/…`, ZIP + DOCX) | `C:\Factum\evidencia` | Bind mount `type: bind` con `create_host_path: false`. |
| Config del estudio | `C:\Factum\config\` (`.env`, `appsettings.Local.json`, `branding\`) | `.env` = solo interpolación del compose; los otros dos montados `:ro`. |
| Backups | `C:\Factum\backups\AAAA-MM-DD_HHMMSS\` o el destino elegido | §6.4. |
| Logs de scripts | `C:\Factum\logs\<script>-AAAA-MM-DD_HHMMSS.log` | `Start-Transcript`. |
| Datos de Tatana | `%LOCALAPPDATA%\Tatana\data` (lo decide `launch-tatana.bat`, sin cambios) | No entran en el backup (son temporales de captura; lo que vale queda en el ZIP del caso). |

**Compatibilidad:** la primera instalación arranca con Mongo vacío. Los repositorios crean índices y
siembran catálogos al arrancar (sin cambios). Restaurar un backup con `mongorestore --drop` pisa las
colecciones sembradas. Es lo esperado.

## 5. Endpoints / mensajes WebSocket

**No hay endpoints nuevos ni cambios de forma.** Los scripts **consumen** dos endpoints que ya existen
(ver §7, contrato con los scripts):

| Servicio | Método y ruta | Respuesta (snake_case) | Uso |
|---|---|---|---|
| Backend | `GET http://127.0.0.1:8080/health` | `{ "status": "ok", "version": "2.0.0", "auth_mode": "dev" }` | Healthcheck del contenedor (con `curl`), espera de `instalar`/`actualizar`/`abrir-factum`, `diagnostico`. |
| Tatana | `GET http://127.0.0.1:8765/health` | `{ "status": "ok", "version": "2.0.0", "mock": false, "ios_available": true }` | `diagnostico.ps1` y el paso final de `instalar.ps1`: si `mock` es `true`, error en rojo. |

## 6. Diseño técnico

### 6.1 Agente Tatana (`server/src/Factum.Agent`)

**Bind (D11 de la HU, ver D1):**

- `AgentOptions` suma `public string BindAddress { get; set; } = "localhost";`.
- Nuevo argumento de CLI `--bind <addr>`, con la misma precedencia que `--port`: CLI > config > default.
- Resolución en `Program.cs`, reemplazando L44-46:
  - `"localhost"` → `http://localhost:{port}` (Kestrel escucha en `127.0.0.1` **y** `::1`; así
    `fetch("http://localhost:8765")` funciona aunque el navegador resuelva a IPv6).
  - Si no, `IPAddress.TryParse`: IPv4 → `http://{ip}:{port}`; IPv6 → `http://[{ip}]:{port}`.
  - Cualquier otro valor → `throw new InvalidOperationException("Agent:BindAddress inválido: '<v>'. Usá localhost o una IP.")`
    (fail-fast, igual que el backend con su config).
  - Si la IP es `0.0.0.0`/`::` → `LogWarning("Agente expuesto a la red en {Bind}: cualquiera en la red local puede listar dispositivos y disparar capturas.")`.
- `opts.BindAddress` también se aplica en `Configure<AgentOptions>` (bind de config + override de CLI),
  para que quede un solo valor efectivo.
- El log final pasa a imprimir los valores **efectivos**:
  `"Factum Agent en {Url} (mock={Mock})"`, con `Mock` = `IOptions<AgentOptions>.Value.Mock` resuelto
  después de `Build()` (H10). Hoy usa la variable `mock` del flag de CLI, que miente.

**Config local del agente (D3):** se copia el bloque de `Factum.Backend/Program.cs` L21-43 (insertar
`appsettings.Local.json`, opcional, `ReloadOnChange=false`, después del último `appsettings*.json`). En
`Factum.Agent.csproj` se suma el mismo `ItemGroup` que el backend (`Content Remove` + `None Remove` de
`appsettings.Local.json`), para que no se copie a `bin/` ni a `publish/`.

**`server/src/Factum.Agent/appsettings.json`: el implementador NO lo edita, NO lo agrega al commit, NO
hace `git checkout`/`stash`/`restore` sobre él.** HEAD ya tiene `"Mock": false` (H1). La copia de
trabajo del usuario queda como está. Al cerrar la HU, el orquestador le avisa al usuario que puede
pasar su `Mock: true` a `server/src/Factum.Agent/appsettings.Local.json` (ignorado por git) o usar
`dotnet run -- --mock`, y descartar su cambio local cuando quiera. El paquete nunca se lleva ese `true`
porque se arma desde `git archive` y además se verifica (D6).

**Borrar `server/src/Factum.Agent/Dockerfile`** (D17 de la HU). Antes, `grep -rn "Factum.Agent/Dockerfile"`
en el repo para confirmar que no lo referencia nada (esperado: solo la HU y esta SDD).

### 6.2 Imagen del backend (`server/src/Factum.Backend/Dockerfile`)

```dockerfile
# Build en la arquitectura de la máquina que compila (rápido en Apple Silicon) y publish cruzado
# a la arquitectura destino (linux/amd64 para la PC del estudio).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY Factum.Backend.csproj .
RUN dotnet restore -a $TARGETARCH
COPY . .
RUN dotnet publish -c Release -a $TARGETARCH --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# tzdata: <comentario actual, se conserva>
# curl: lo usa el healthcheck del compose (GET /health).
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Factum.Backend.dll"]
```

- Sale `libreoffice-writer` y su comentario. Se reemplaza por el de `curl`.
- Se saca `--urls http://0.0.0.0:8080` del `ENTRYPOINT`: es redundante con `ListenAnyIP(PORT)` (H8) y
  hoy genera el warning "Overriding address(es)". **Si el implementador encuentra que algo depende de
  ese flag, lo deja y lo anota en el progress.**
- Si `-a $TARGETARCH` no acepta `amd64` en el SDK 10 (debería: es el patrón oficial de
  `dotnet-docker`), se mapea con `RUN arch=$([ "$TARGETARCH" = "amd64" ] && echo x64 || echo $TARGETARCH)`
  y se anota.
- La imagen corre como root (sin `USER`), igual que hoy. Pasar a `USER $APP_UID` complica el bind mount
  de evidencia en Windows y queda fuera de alcance (D10).
- `.dockerignore` del backend: sin cambios (ya excluye `appsettings.Local.json`, `branding/`, `bin/`,
  `obj/`, `dev-data/`).
- `ReportService.cs` L1293-1295: solo el comentario pasa a decir que `libreoffice-writer` **ya no está
  en la imagen** (HU `instalacion-local-docker`) y que borrar el código muerto queda para otra HU.
  Ningún otro cambio en ese archivo.

### 6.3 Imagen del frontend (`client/`) — implementer-frontend

**`client/.dockerignore` (nuevo):**

```
node_modules
.next
.env
.env.*
*.tsbuildinfo
.DS_Store
Dockerfile
.dockerignore
npm-debug.log*
```

**`client/next.config.ts`:** se suma `output: "standalone"`. `env:` y `allowedDevOrigins` quedan como
están (`allowedDevOrigins` solo aplica a `next dev`; limpiarlo es otra cosa). Leer antes
`client/node_modules/next/dist/docs/01-app/03-api-reference/05-config/01-next-config-js/output.md`
(Next 16, `client/AGENTS.md`). Confirmado ahí: `.next/standalone/server.js`, sin `public/` ni
`.next/static` (se copian a mano), y escucha en `PORT`/`HOSTNAME`.

**`client/Dockerfile`:**

```dockerfile
FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

FROM node:22-alpine AS builder
WORKDIR /app
# URLs que usa el NAVEGADOR (no el contenedor): se hornean en el bundle en `next build`.
# Para la instalación de una sola PC los valores correctos son los defaults.
ARG NEXT_PUBLIC_BACKEND_URL=http://localhost:8080
ARG NEXT_PUBLIC_AGENT_URL=http://localhost:8765
ENV NEXT_PUBLIC_BACKEND_URL=$NEXT_PUBLIC_BACKEND_URL \
    NEXT_PUBLIC_AGENT_URL=$NEXT_PUBLIC_AGENT_URL \
    NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN npm run build

FROM node:22-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 PORT=3000 HOSTNAME=0.0.0.0
COPY --from=builder --chown=node:node /app/.next/standalone ./
COPY --from=builder --chown=node:node /app/.next/static ./.next/static
COPY --from=builder --chown=node:node /app/public ./public
USER node
EXPOSE 3000
CMD ["node", "server.js"]
```

- **Sin `--platform=$BUILDPLATFORM`** en ningún stage (H7): todo el build corre en `linux/amd64` emulado.
  Es más lento en la Mac, pero los binarios nativos que traza `standalone` salen de la arquitectura
  correcta.
- `HOSTNAME=0.0.0.0` es obligatorio: sin eso `server.js` escucha en el hostname del contenedor y el
  healthcheck a `127.0.0.1` falla.
- **No correr `npm run build` en la copia de trabajo** (pisaría `client/.next` del dev del usuario): el
  build se prueba solo dentro de Docker.

### 6.4 Compose de producción (`deploy/windows/docker-compose.yml`)

Especificación (el implementador puede ajustar el formato, pero no la semántica):

```yaml
# Factum — instalación local en una PC Windows (Docker Desktop + WSL2).
# Se usa SIEMPRE a través de los scripts (scripts\_comun.ps1), que pasan
# --env-file C:\Factum\config\.env y -p $COMPOSE_PROJECT_NAME.
# Tatana NO va acá: corre nativo en Windows (USB). Ver docs/instalacion-windows.md.

x-logging: &logging
  driver: local
  options:
    max-size: "10m"
    max-file: "5"

services:
  mongo:
    image: mongo:7.0.<PATCH>          # tag exacto, nunca "7" ni "latest" (D8)
    pull_policy: never
    volumes:
      - mongo-data:/data/db
    networks: [datos]
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "mongosh", "--quiet", "--eval", "db.adminCommand('ping').ok"]
      interval: 10s
      timeout: 5s
      retries: 12
      start_period: 20s
    logging: *logging

  backend:
    image: factum-backend:${FACTUM_VERSION:?Falta FACTUM_VERSION en config/.env}
    pull_policy: never
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      MongoDb__ConnectionString: mongodb://mongo:27017
      MongoDb__DatabaseName: factum
      Jwt__Secret: ${FACTUM_JWT_SECRET:?Falta FACTUM_JWT_SECRET en config/.env}
      Jwt__ExpiryHours: ${FACTUM_JWT_EXPIRY_HOURS:-8}
      Auth__Mode: ${FACTUM_AUTH_MODE:-dev}
      Storage__DataDirectory: /data
      Integrations__Support__Enabled: "false"
    volumes:
      - type: bind
        source: ${FACTUM_HOME:?Falta FACTUM_HOME en config/.env}/evidencia
        target: /data
        bind: { create_host_path: false }
      - type: bind
        source: ${FACTUM_HOME}/config/appsettings.Local.json
        target: /app/appsettings.Local.json
        read_only: true
        bind: { create_host_path: false }
      - type: bind
        source: ${FACTUM_HOME}/config/branding
        target: /app/branding
        read_only: true
        bind: { create_host_path: false }
    ports:
      - "127.0.0.1:8080:8080"
    networks: [web, datos]
    depends_on:
      mongo: { condition: service_healthy }
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://127.0.0.1:8080/health"]
      interval: 10s
      timeout: 5s
      retries: 6
      start_period: 40s
    logging: *logging

  frontend:
    image: factum-frontend:${FACTUM_VERSION}
    pull_policy: never
    ports:
      - "127.0.0.1:3000:3000"
    networks: [web]
    depends_on:
      backend: { condition: service_healthy }
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "wget", "-q", "--spider", "http://127.0.0.1:3000/"]
      interval: 10s
      timeout: 5s
      retries: 6
      start_period: 20s
    logging: *logging

networks:
  web: {}
  datos:
    internal: true     # Mongo sin salida ni entrada fuera de Docker; solo el backend lo ve

volumes:
  mongo-data: {}
```

- Sin `name:` de proyecto, sin `container_name:` y sin `name:` absoluto en volúmenes (H2). El proyecto
  lo da `-p` desde los scripts.
- `pull_policy: never`: si falta una imagen, el error es inmediato y claro ("no se cargó el paquete"),
  nunca un intento de descarga.
- Sintaxis larga de `volumes` con `create_host_path: false`: evita el problema del `C:` con dos puntos
  en la sintaxis corta y evita que Docker cree un **directorio** `appsettings.Local.json` si falta el
  archivo. `instalar.ps1` crea todo antes.
- `FACTUM_HOME` va con barras normales (`C:/Factum`). Compose en Windows lo acepta y evita escapes.
- `<PATCH>`: el implementador fija el último `7.0.x` disponible (`docker manifest inspect mongo:7.0.<x>`)
  y lo anota en el progress. El mismo tag va en `armar-paquete.sh`, con una sola fuente: el script lo
  lee del compose con `grep`/`sed`, no lo duplica.

**`deploy/windows/.env.example`** (lo copia y completa `instalar.ps1`; contrato en §7):

```dotenv
# Factum — configuración de esta instalación. La genera instalar.ps1; no compartir (tiene el secreto JWT).
COMPOSE_PROJECT_NAME=factum
FACTUM_VERSION=0.0.0
FACTUM_HOME=C:/Factum
# 128 caracteres hex aleatorios, propios de esta PC. instalar.ps1 lo genera; nunca el valor del repo.
FACTUM_JWT_SECRET=
FACTUM_JWT_EXPIRY_HOURS=8
# dev = cualquier DNI + cualquier contraseña (ver aviso de seguridad en la guía). external = proveedor HTTP.
FACTUM_AUTH_MODE=dev
# Carpeta de backups (vacío = C:/Factum/backups) y cuántos backups conservar ahí.
FACTUM_BACKUP_DESTINO=
FACTUM_BACKUP_CONSERVAR=10
```

**`deploy/windows/appsettings.Local.example.json`:** `Branding` con todos los campos vacíos y
`OrganizationLogo: "branding/logo.png"` documentado en la guía (no en el JSON: vacío por defecto para
que una instalación sin logo no loguee warnings); `Report` con `TimeZone`
`America/Argentina/Buenos_Aires`, `DomicilioConstituido: ""`, `EncryptZip: true` y `DefaultTexts`
vacíos; `Audit.AdminDnis: []`. Debe ser JSON válido (se verifica con `python3 -m json.tool`).

**`docker-compose.yml` de la raíz (desarrollo):** solo se cambia lo que confunde. En `frontend`, el
`environment:` con `NEXT_PUBLIC_*` pasa a `build.args` con los mismos valores, más un comentario que
explica que se hornean en el build. Puertos, volúmenes y el resto quedan igual: es el entorno del
usuario.

### 6.5 Scripts PowerShell (`deploy/windows/scripts/`)

**Reglas comunes (todas obligatorias):**

- R1. Compatibles con **Windows PowerShell 5.1** (H12): nada de `??`, ternario, `&&`/`||`,
  `ForEach-Object -Parallel`, `Join-Path` con más de 2 argumentos ni `ConvertFrom-Json -AsHashtable`.
- R2. Archivos `.ps1` en **UTF-8 con BOM** y fin de línea CRLF. Los `.bat` en ASCII puro con CRLF
  (sin acentos: `cmd` no los muestra bien).
- R3. Nunca redirigir la salida binaria de un comando nativo a archivo con `>`/`|` (H12). Dump y
  restore de Mongo van por archivo **dentro** del contenedor + `docker compose cp`.
- R4. `$ErrorActionPreference = 'Stop'`, `Set-StrictMode -Version 2.0`, y `Start-Transcript` a
  `C:\Factum\logs\<script>-<stamp>.log` en cuanto exista la carpeta. Para los comandos nativos se
  revisa `$LASTEXITCODE` después de cada `docker`.
- R5. Mensajes en castellano claro, pasos numerados `[n/N] Texto...  OK`, errores con **qué pasó + qué
  hacer + ruta del log**. Nunca un stack trace solo: un `trap`/`try` global muestra el mensaje humano y
  deja el detalle en el transcript.
- R6. Las operaciones destructivas (restaurar sobre datos, volver de versión a mano) piden escribir
  `SI` en mayúsculas. Cualquier otra respuesta cancela sin tocar nada.
- R7. Toda llamada a compose pasa por `Invoke-FactumCompose` de `_comun.ps1`, que **siempre** agrega
  `--project-directory <home> -f <home>\docker-compose.yml --env-file <home>\config\.env -p <COMPOSE_PROJECT_NAME>`.
  Prohibido `docker system prune`, `docker volume prune` o `docker volume rm` en cualquier script.
- R8. Sin admin. Si algo exige admin (agregar al grupo `docker-users`, habilitar WSL o
  virtualización), el script lo **explica** y para; no se eleva solo.
- R9. Cada `.ps1` se lanza con un `.bat` envoltorio:
  `powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\<x>.ps1" %*` seguido de `pause`.
  `abrir-factum` no hace `pause` y corre con `-WindowStyle Hidden`.

**`_comun.ps1`** (se carga con dot-source en todos los scripts; funciones puras testeables con Pester, ver §9):

| Función | Qué hace |
|---|---|
| `Get-FactumHome` | `C:\Factum` por defecto. Si existe `$PSScriptRoot\..\config\.env`, el home es `$PSScriptRoot\..` (scripts ya instalados). |
| `Read-EnvFile <path>` → hashtable | Parser `.env` (ignora `#` y líneas vacías, divide en el primer `=`). |
| `Write-EnvFile <path> <hashtable> [-Template]` | Reescribe valores conservando comentarios y orden del template. Escribe UTF-8 **sin** BOM (compose). |
| `Set-EnvValue <path> <key> <value>` | Cambia una clave puntual (lo usa `actualizar` para `FACTUM_VERSION`). |
| `New-JwtSecret` | 64 bytes de `System.Security.Cryptography.RandomNumberGenerator` → 128 caracteres hex. |
| `Invoke-FactumCompose <args>` | R7. Devuelve el exit code; si es distinto de 0, el script llamador decide. |
| `Wait-FactumHealthy [-TimeoutSec 180]` | Hace polling de `docker inspect -f '{{.State.Health.Status}}'` de los tres servicios (con `compose ps -q <svc>`) hasta `healthy`. Si vence el tiempo, devuelve qué servicio falló. |
| `Show-BackendLogTail [-Lines 40]` | `compose logs --no-color --tail 40 backend`, para el escenario "Configuración inválida". |
| `New-HashManifest <raiz> <salida>` | `Get-FileHash -Algorithm SHA256` recursivo. Formato §7.3. Excluye el propio manifiesto. |
| `Test-HashManifest <raiz> <manifiesto>` → lista de diferencias | Faltantes, sobrantes (informativos) y hash distinto. No modifica nada. |
| `Write-Paso`, `Write-Ok`, `Write-Aviso`, `Write-Falla`, `Confirm-Si` | UX de consola (R5/R6). |
| `Test-Requisitos` → lista de fallas y avisos | §6.5.1. Junta **todas** las fallas y las muestra juntas. |
| `Test-PuertoLibre <puerto>` | `Get-NetTCPConnection -State Listen -LocalPort`. Devuelve el proceso dueño si está ocupado. |
| `Get-TatanaHealth` | `Invoke-RestMethod http://127.0.0.1:8765/health -TimeoutSec 3` (o `$null`). |

#### 6.5.1 Requisitos que chequea `instalar.ps1` (D18 validada)

Se ejecutan **todos** antes de tocar el disco. Si hay alguna **falla**, el script lista cada una con su
explicación y para con exit 1. Los **avisos** se muestran y el script pregunta si continuar.

| # | Chequeo | Cómo | Falla / aviso y mensaje (resumen; texto completo en la guía) |
|---|---|---|---|
| Q1 | Windows de 64 bits | `[Environment]::Is64BitOperatingSystem` | Falla: "Factum necesita Windows de 64 bits." |
| Q2 | Windows 10 22H2 o posterior, o Windows 11 | `[Environment]::OSVersion.Version.Build` ≥ 19045 (Win 10 22H2); `ProductName`/`DisplayVersion` del registro `HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion` para el mensaje | Falla: "Tu Windows es <ProductName> <DisplayVersion> (build N). Hace falta Windows 10 versión 22H2 o posterior: Configuración > Windows Update." |
| Q3 | Memoria RAM | `Win32_ComputerSystem.TotalPhysicalMemory` | < 8 GB: falla ("Docker + Factum necesitan al menos 8 GB"). 8 a <16 GB: aviso ("va a andar, pero cerrá otros programas pesados; recomendable 16 GB"). |
| Q4 | Virtualización habilitada | `Win32_ComputerSystem.HypervisorPresent` = true → OK; si no, `Win32_Processor.VirtualizationFirmwareEnabled` | Falla: "La virtualización está apagada en la BIOS/UEFI. Hay que entrar al setup del equipo (F2/Supr al encender) y activar Intel VT-x / AMD-V (SVM). Ver guía, sección X." |
| Q5 | Espacio libre en la unidad de `FACTUM_HOME` | `Get-PSDrive` | < 15 GB: falla. < 50 GB: aviso ("la evidencia crece con cada caso"). |
| Q6 | Docker Desktop instalado | `Get-Command docker` o `"$env:ProgramFiles\Docker\Docker\Docker Desktop.exe"` | Falla: "Docker Desktop no está instalado. Ver guía, paso 2." Si falta, se chequea WSL (`wsl.exe --status`, solo el exit code) para sumar el mensaje "WSL2 no está instalado: `wsl --install` como administrador". |
| Q7 | Docker Desktop corriendo | `docker info` (exit code) | Si no responde: lo arranca (`Start-Process` del exe), espera hasta 120 s con mensaje "Esperando a Docker Desktop…". Si sigue sin responder: falla. Si el error es de permisos ("access is denied", pipe `docker_engine`): falla "Tu usuario de Windows no está en el grupo docker-users…" |
| Q8 | Contenedores Linux | `docker info --format '{{.OSType}}'` = `linux` | Falla: "Docker está en modo contenedores Windows: clic derecho en la ballena > Switch to Linux containers." |
| Q9 | Docker Compose v2 ≥ 2.20 | `docker compose version --short` | Falla: "Actualizá Docker Desktop." |
| Q10 | Puertos 3000 y 8080 libres | `Test-PuertoLibre` | Falla con el nombre del proceso que lo ocupa: "Cerralo o desinstalalo; Factum necesita ese puerto." Los puertos son fijos (D12). 8765: si lo ocupa `Factum.Agent` está OK (Tatana ya instalado); si es otro proceso, aviso. |
| Q11 | Docker Desktop arranca con la sesión | `%APPDATA%\Docker\settings-store.json` (`AutoStart`) o, en versiones viejas, `settings.json` (`autoStart`) | Solo aviso con cómo activarlo: "Settings > General > Start Docker Desktop when you sign in". No se edita el archivo. |

#### 6.5.2 `instalar.ps1`

Parámetros: `-Carpeta` (default `C:\Factum`), `-Reparar` (switch), `-SinTatana` (switch). Corre
**desde el paquete** descomprimido.

1. Lee `version.txt` del paquete. Verifica `SHA256SUMS.txt` del paquete (`Test-HashManifest`): si el
   `.tar` está corrupto (copia por USB incompleta), falla antes de nada.
2. `Test-Requisitos` (§6.5.1).
3. Si ya existe `<Carpeta>\config\.env`: sin `-Reparar`, falla con "Factum ya está instalado en
   <Carpeta>. Para cambiar de versión usá *Actualizar Factum*; para reinstalar los scripts sin tocar
   datos, `-Reparar`". Con `-Reparar` saltea los pasos 4, 5 y 6 (no regenera el JWT ni toca la config).
4. Crea `<Carpeta>\{config\branding, evidencia, backups, logs, scripts}`. Arranca el transcript.
5. Restringe permisos de `<Carpeta>` (D5): `icacls <Carpeta> /inheritance:r /grant:r "<usuario>:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F"`
   (usuario actual, Administradores, SYSTEM). Si falla, solo avisa y sigue.
6. `config\.env` desde `.env.example`: `FACTUM_VERSION` = la del paquete, `FACTUM_HOME` = `<Carpeta>`
   con `/`, `FACTUM_JWT_SECRET` = `New-JwtSecret`. `config\appsettings.Local.json` desde el example,
   solo si no existe.
7. Copia `docker-compose.yml`, `version.txt`, `scripts\*.ps1` y los `.bat` de uso diario
   (`Backup de Factum.bat`, `Diagnostico de Factum.bat`, `Restaurar Factum.bat`, `abrir-factum.bat`) a
   `<Carpeta>`.
8. `docker load -i imagenes\factum-v<ver>.tar` (avisa "puede tardar unos minutos") y verifica con
   `docker image inspect` las tres imágenes (`factum-backend:<ver>`, `factum-frontend:<ver>`,
   `mongo:7.0.<patch>`).
9. `Invoke-FactumCompose up -d` + `Wait-FactumHealthy`. Si falla: `Show-BackendLogTail`, mensaje
   "El backend no arrancó; revisá `config\appsettings.Local.json` o `config\.env`" y exit 1. No borra
   nada.
10. Accesos directos (`WScript.Shell`): Escritorio → **"Factum"** (`abrir-factum.bat`, ícono
    `logo-app.ico` si viaja en el paquete; si no, el de Windows) y **"Backup de Factum"**.
11. Pregunta "¿Programar un backup automático todos los días a las 20:00? (S/N)". Si dice S:
    `Register-ScheduledTask` del usuario actual, `-LogonType Interactive`, `StartWhenAvailable`,
    acción = `powershell -NoProfile -ExecutionPolicy Bypass -File <Carpeta>\scripts\backup.ps1 -Desatendido`.
12. Tatana (salvo `-SinTatana`): descomprime `tatana\Tatana-Portable-*.zip` en
    `%TEMP%\factum-tatana-<stamp>`, ejecuta su `install-portable.bat` y espera hasta 30 s a
    `Get-TatanaHealth`. Si `mock` es `true`: **Falla en rojo** ("Tatana está en modo simulado; no usar
    para peritajes"). Si no responde: aviso con los pasos de la guía.
13. Resumen final: URL `http://localhost:3000`, carpeta de evidencia, carpeta de backups, aviso de
    seguridad del modo `dev` (D7, texto en §6.7), próximos pasos (identidad del estudio → reiniciar el
    backend con *Diagnóstico* → prueba de humo de la guía) y ruta del log.

#### 6.5.3 `backup.ps1`

Parámetros: `-Destino` (default `FACTUM_BACKUP_DESTINO` o `<home>\backups`) y `-Desatendido` (sin
preguntas; la salida queda solo en el log).

1. Valida que el destino exista y tenga espacio (≥ tamaño de `evidencia` + 1 GB). Si no, falla.
2. Crea `<destino>\AAAA-MM-DD_HHMMSS\` (en hora local).
3. **Detiene `frontend` y `backend`** (D2): `compose stop frontend backend`. Mongo sigue arriba. Así
   la base y la carpeta de evidencia quedan consistentes entre sí. En modo interactivo avisa "Factum
   va a estar pausado unos minutos" y pide Enter.
4. `compose exec -T mongo mongodump --db factum --archive=/tmp/factum-backup.archive.gz --gzip` →
   `compose cp mongo:/tmp/factum-backup.archive.gz <dir>\mongo\factum.archive.gz` →
   `compose exec -T mongo rm -f /tmp/factum-backup.archive.gz` (R3).
5. Copia la evidencia con `robocopy <home>\evidencia <dir>\evidencia /E /COPY:DAT /DCOPY:DAT /R:2 /W:2 /NP /NFL /NDL`
   (exit < 8 = OK). **Nunca `/MIR` ni `/MOV`**: el origen no se toca.
6. Copia `config\` completo (incluye `.env` con el JWT, `appsettings.Local.json`, `branding\`) y
   `docker-compose.yml` + `version.txt` a `<dir>\config\`.
7. `backup-info.json` (§7.3) y `manifiesto-sha256.txt` sobre `<dir>`. Para la evidencia, además,
   compara el hash de cada copia con el del original (`Get-FileHash` del origen). Si alguna diferencia:
   el backup se marca **FALLIDO** (se renombra a `<stamp>_FALLIDO`) y exit 1.
8. `compose start backend frontend` + `Wait-FactumHealthy` (en un `finally`: el sistema vuelve a
   levantarse **aunque el backup falle**).
9. Retención: solo en `<home>\backups` (no en destinos externos), borra los backups **más viejos** que
   superen `FACTUM_BACKUP_CONSERVAR`. Solo carpetas cuyo nombre cumpla `^\d{4}-\d{2}-\d{2}_\d{6}$` y
   que tengan `backup-info.json`. Nunca toca `_FALLIDO` ni carpetas ajenas.
10. Resumen: ruta, tamaño, cantidad de casos (`mongosh --quiet --eval "db.getSiblingDB('factum').cases.countDocuments()"`),
    y el recordatorio "este backup contiene las contraseñas de los ZIP: guardalo en un lugar seguro".

#### 6.5.4 `restaurar.ps1`

Parámetro obligatorio `-Backup <carpeta>`. Siempre interactivo (no tiene modo desatendido).

1. **Verificación previa sin tocar nada:** `Test-HashManifest`. Si hay faltantes o hashes distintos,
   lista cada archivo con su motivo y sale con exit 1 ("El backup está dañado o fue modificado; no se
   restauró nada.").
2. Lee `backup-info.json`. Si `factum_version` ≠ `FACTUM_VERSION` instalada: falla con "Este backup es
   de la versión X; instalá esa versión primero (paquete X) o actualizá después de restaurar".
3. Chequea que la instalación esté vacía: `cases.countDocuments()` = 0 y `evidencia\` sin archivos. Si
   **no** está vacía: aviso fuerte y `Confirm-Si`. Antes de pisar nada, corre `backup.ps1`
   automáticamente (respaldo del estado actual) y avisa dónde quedó.
4. `compose stop frontend backend`.
5. `compose cp <backup>\mongo\factum.archive.gz mongo:/tmp/restore.archive.gz` →
   `compose exec -T mongo mongorestore --archive=/tmp/restore.archive.gz --gzip --drop --nsInclude "factum.*"`
   → borra el temporal.
6. `robocopy <backup>\evidencia <home>\evidencia /E /COPY:DAT /DCOPY:DAT` (sin `/MIR`: si había
   evidencia y el usuario confirmó, queda la unión). Después verifica los hashes de lo restaurado
   contra el manifiesto. Si difiere: falla, con la lista.
7. Config: copia `appsettings.Local.json` y `branding\` del backup. Del `.env` del backup toma **solo**
   `FACTUM_JWT_SECRET`, `FACTUM_JWT_EXPIRY_HOURS`, `FACTUM_AUTH_MODE`, `FACTUM_BACKUP_*`. Conserva
   `FACTUM_HOME`, `COMPOSE_PROJECT_NAME` y `FACTUM_VERSION` locales (la restauración puede ser en otra
   PC u otra carpeta).
8. `compose up -d` + `Wait-FactumHealthy`. Resumen con la cantidad de casos restaurados.

#### 6.5.5 `actualizar.ps1`

Corre **desde el paquete nuevo** (`Actualizar Factum.bat` en la raíz del paquete). Parámetros:
`-Carpeta` (default: el home detectado) y `-SimularFalla` (switch solo para la prueba manual: después
del `up` de Y trata el healthcheck como fallido y ejecuta el rollback del paso 8; se documenta solo en la
Parte B de la guía).

1. Lee la versión nueva Y (`version.txt` del paquete) y la instalada X (`.env`). Si Y = X: "Ya tenés
   esa versión" y sale. Si Y < X (comparación `[version]`): aviso + `Confirm-Si`.
2. Verifica `SHA256SUMS.txt` del paquete y corre `Test-Requisitos` (solo Q6-Q9 y espacio).
3. **Backup automático**: llama a `backup.ps1 -Desatendido`. Si falla, **no actualiza** (exit 1).
4. Guarda una copia de `docker-compose.yml`, `version.txt`, `config\.env` y `scripts\` en
   `<home>\backups\pre-actualizacion-<X>-<stamp>\` (para el rollback; además ya están en el backup).
5. `docker load` del `.tar` de Y y verificación de las tres imágenes.
6. Reemplaza `docker-compose.yml`, `version.txt`, `scripts\` y los `.bat` por los del paquete;
   `Set-EnvValue FACTUM_VERSION Y`. Si el `.env.example` de Y trae claves nuevas, las agrega con su
   default sin tocar las existentes.
7. `compose up -d --remove-orphans` + `Wait-FactumHealthy -TimeoutSec 180`.
8. **Si falla:** `Show-BackendLogTail`, restaura compose/version/scripts/.env del paso 4,
   `compose up -d` con la versión X, `Wait-FactumHealthy`, y mensaje: "La versión Y no arrancó;
   Factum volvió a la versión X. Los datos que Y haya escrito en la base no se deshacen; si notás algo
   raro, restaurá el backup <ruta>". Exit 1.
9. **Si anda:** resumen. Las imágenes de X **no se borran** (permiten volver atrás); la guía explica
   cómo borrarlas a mano cuando haga falta espacio (`docker image rm factum-backend:X factum-frontend:X`).
10. Tatana: si el paquete trae un portátil de versión distinta a la instalada
    (`%LOCALAPPDATA%\Programs\Tatana\version.txt`), pregunta si actualizarlo. Si dice que sí: detiene
    `Factum.Agent` (`Stop-Process` por nombre) y corre `install-portable.bat` del zip nuevo. Después,
    `Get-TatanaHealth` con el control de `mock`.

#### 6.5.6 `diagnostico.ps1` y `abrir-factum.ps1`

- **`diagnostico.ps1`** (solo lectura; parámetro opcional `-ReiniciarBackend`): versión instalada,
  `docker version`, `compose ps` (estado + health), puertos 3000/8080/8765 (quién escucha), `GET /health`
  del backend y de Tatana (con `mock` destacado), espacio libre, tamaño de `evidencia`, fecha del último
  backup válido, últimas 40 líneas del log del backend. Todo se escribe además en
  `<home>\logs\diagnostico-<stamp>.txt` para mandárselo al desarrollador. Con `-ReiniciarBackend` hace
  `compose restart backend` + espera (es el paso para aplicar cambios de identidad del estudio).
- **`abrir-factum.ps1`** (ventana oculta): si `docker info` falla, arranca Docker Desktop. Espera hasta
  180 s a que el backend y el frontend estén `healthy`. Abre `http://localhost:3000` con
  `Start-Process`. Si vence el tiempo, muestra un cuadro (`System.Windows.Forms.MessageBox`): "Factum
  todavía no arrancó. Esperá un minuto y volvé a intentar; si sigue, abrí *Diagnóstico de Factum*".

### 6.6 Empaquetado (en la Mac) — `deploy/windows/armar-paquete.sh` y `armar-tatana-portable.sh`

Bash (en la Mac no hay `pwsh`), `set -euo pipefail`, mensajes en castellano.

**`armar-paquete.sh --version X.Y.Z [--ref <git-ref>] [--salida <dir>] [--tatana-zip <ruta>] [--sin-tatana]`**

1. `--version` obligatorio (semver). `--ref` default `HEAD`. `--salida` default `deploy/windows/dist/`
   (en `.gitignore`).
2. Si `git status --porcelain` muestra cambios, **avisa** cuáles no van a entrar al paquete (no falla:
   el usuario tiene cambios locales legítimos, H1).
3. `git archive <ref> | tar -x -C <tmp>/src`: **fuente limpia**. No entran `.env.local`,
   `appsettings.Local.json`, `branding/` ni el `Mock: true` local.
4. Guarda de Mock: en `<tmp>/src/server/src/Factum.Agent/appsettings.json`, `Agent.Mock` tiene que ser
   `false` (lo lee con `python3 -c 'import json…'`). Si no, falla.
5. Imágenes (con `docker buildx build --platform linux/amd64 --load`):
   `factum-backend:X` desde `<tmp>/src/server/src/Factum.Backend` y `factum-frontend:X` desde
   `<tmp>/src/client` con `--build-arg NEXT_PUBLIC_BACKEND_URL=http://localhost:8080 --build-arg NEXT_PUBLIC_AGENT_URL=http://localhost:8765`.
   `docker pull --platform linux/amd64 mongo:7.0.<patch>` (tag leído del compose).
6. Verifica `docker image inspect -f '{{.Os}}/{{.Architecture}}'` = `linux/amd64` en las tres. Si no,
   falla.
7. `docker save` de las tres → `imagenes/factum-vX.Y.Z.tar`. Si el Docker local usa el containerd
   image store y `docker save` admite `--platform`, se pasa `--platform linux/amd64`. El implementador
   verifica, mirando `index.json`/`manifest.json` del tar, que solo trae amd64, y lo anota.
8. Tatana: `--tatana-zip <ruta>` usa un portátil ya armado. Si no se pasa, llama a
   `armar-tatana-portable.sh`. Con `--sin-tatana` no se incluye (solo para verificación).
9. Arma `<salida>/Factum-Instalacion-vX.Y.Z/` (§6.8): copia `docker-compose.yml`, `.env.example`,
   `appsettings.Local.example.json`, `scripts/`, los `.bat`, `LEEME.txt`, `docs/instalacion-windows.md`
   (del `git archive`, no de la copia de trabajo), `client/public/logo-app.ico` como ícono del acceso
   directo, y escribe `version.txt`.
10. `SHA256SUMS.txt` (formato §7.3) de todo el paquete, y `zip -r Factum-Instalacion-vX.Y.Z.zip`.
11. Resumen: tamaño del `.tar` y del zip, tags, ruta. Borra `<tmp>`. **No borra imágenes ni toca
    contenedores** (los tags `factum-*:X` quedan en la Mac).

**`armar-tatana-portable.sh --version X.Y.Z --src <árbol limpio> --salida <dir>`** replica el job
`build-portable-win` de `.gitlab-ci.yml` (L90-133) en la Mac:

- `dotnet publish <src>/server/src/Factum.Agent/Factum.Agent.csproj -c Release -r win-x64 --self-contained true -o <out>`.
- Guarda de Mock otra vez sobre `<out>/appsettings.json`, y comprobación de que **no** existe
  `<out>/appsettings.Local.json`.
- `tools/platform-tools` (último `platform-tools_r*-win.zip` del `repository2-1.xml` de Google, igual
  que el CI), `tools/python-embed` (3.11.9 embed amd64 + `import site` +
  `python3 -m pip install --target … --platform win_amd64 --implementation cp --python-version 311 --only-binary=:all: pymobiledevice3`),
  `tools/ffmpeg` (BtbN win64 gpl), `tools/uxplay` solo si está `UXPLAY_WIN_ARTIFACT_URL` (si no, el
  mismo aviso que el CI).
- Copia `packaging/portable/*.bat *.ps1 *.ini` y **pisa en la copia** `tatana-portable.ini` con:
  `CLIENT_URL=` (vacío, D4), `UPDATE_URL=` (vacío, D15 de la HU), `AGENT_PORT=8765`. El `.ini` del repo
  no cambia (lo usa el CI).
- `version.txt` = X.Y.Z → `Tatana-Portable-vX.Y.Z-Windows.zip`.

**`packaging/portable/launch-tatana.bat`:** el `start "" "%CLIENT_URL%"` (y su `timeout`) solo corre si
`CLIENT_URL` no está vacío (`if not "%CLIENT_URL%"=="" ( … )`). Es compatible hacia atrás: con el `.ini`
del CI se comporta igual que hoy.

### 6.7 Guía `docs/instalacion-windows.md`

Para alguien **no técnico** (pedido del orquestador; la HU D3 decía "técnico"; se resuelve con dos
partes): **Parte A, "Uso diario"** (abrir Factum, backup, qué hacer si no abre), en lenguaje llano, con
capturas descriptas en texto; y **Parte B, "Instalación y mantenimiento"**, paso a paso, para quien
instala. Secciones mínimas:

1. Qué es cada pieza (Factum en Docker + Tatana para el celular), en 5 líneas.
2. Requisitos: Windows 10 22H2+/11 de 64 bits, 8 GB RAM mínimo (16 recomendado), 50 GB libres,
   virtualización, cuenta de Windows con permisos de administrador **solo para instalar Docker**.
   Cómo ver la versión de Windows (`winver`) y la RAM. Qué hacer con cada falla de §6.5.1 (Q1-Q11).
3. Habilitar la virtualización en BIOS/UEFI (genérico + fabricantes comunes) y WSL2 (`wsl --install`,
   `wsl --update`).
4. Instalar Docker Desktop: descarga, "Use WSL 2", "Start Docker Desktop when you sign in", grupo
   `docker-users` si otra cuenta lo va a usar. **Licencia**: gratis para organizaciones de menos de 250
   empleados **y** menos de USD 10 M de facturación anual (el estudio cumple; si crece, requiere
   suscripción paga).
5. Instalar Factum: descomprimir el paquete, doble clic en `1-Instalar Factum.bat`, qué va a ver en
   cada paso, SmartScreen ("Más información > Ejecutar de todas formas").
6. **Aviso de seguridad del modo de acceso (D7)**, en un recuadro destacado: "En esta versión Factum
   no tiene usuarios con contraseña propia: acepta cualquier DNI y cualquier contraseña. Solo se puede
   entrar desde esta PC, así que la protección real es la contraseña de Windows. Cada perito tiene que
   bloquear la sesión (Windows + L) al levantarse, y entrar siempre con **su propio DNI**: la autoría de
   los casos y la auditoría dependen de eso. Una versión futura va a agregar usuarios con contraseña."
7. Identidad del estudio: editar `C:\Factum\config\appsettings.Local.json` (ejemplo completo con
   valores ficticios, reglas del logo del README), copiar el logo a `config\branding\`, aplicar con
   *Diagnóstico* `-ReiniciarBackend`.
8. Tatana y drivers: lo instala `instalar.ps1`; driver USB del fabricante o Google USB Driver (Android),
   depuración USB; Apple Mobile Device Support / app "Dispositivos de Apple" (iPhone), "Confiar en esta
   computadora"; iOS 17+ y privilegios. Firewall: Tatana ya no escucha en la red (no aparece el aviso).
9. Prueba de humo: login → caso de prueba → captura Android/iPhone → generar informe → abrir DOCX en
   Word → verificar el SHA-256 del ZIP (`Get-FileHash`) contra el informe.
10. Backup (acceso del escritorio, disco externo con `-Destino`, tarea programada, retención; "el
    backup tiene las contraseñas de los ZIP, guardalo bajo llave") y restauración (misma PC u otra:
    instalar la misma versión → `Restaurar Factum.bat`).
11. Actualización (`Actualizar Factum.bat` del paquete nuevo; qué pasa si falla; Tatana).
12. Problemas comunes: Docker no arranca / WSL desactualizado, puerto ocupado, "Factum todavía no
    arrancó", el backend no arranca por config (log), celular no detectado, `mock: true` en
    diagnóstico, se llenó el disco.
13. Plan B si la PC no soporta virtualización (D2 C de la HU): solo mencionado, sin pasos ("contactar
    al proveedor").
14. Para el desarrollador: cómo se arma el paquete (`armar-paquete.sh`), requisitos en la Mac (Docker
    con buildx, `python3`, `dotnet` 10, internet para el build).

`LEEME.txt` (raíz del paquete, ASCII sin acentos, ≤ 15 líneas): qué es, "abrí `instalacion-windows.md`"
y el orden (1. Docker Desktop, 2. `1-Instalar Factum.bat`).

`README.md`: en "Puesta en marcha rápida (Docker)", una línea que aclara que ese compose es de
desarrollo y apunta a `docs/instalacion-windows.md` para instalaciones. En la sección del agente,
reemplazar la sugerencia de poner `"Mock": true` en `appsettings.json` (L155) por `--mock` o
`appsettings.Local.json`, y documentar `Agent:BindAddress` / `--bind` en la tabla de config (L203).

### 6.8 Estructura del paquete y de la PC

```
Factum-Instalacion-vX.Y.Z/                 C:\Factum\
├── LEEME.txt                              ├── docker-compose.yml
├── 1-Instalar Factum.bat                  ├── version.txt
├── Actualizar Factum.bat                  ├── abrir-factum.bat
├── instalacion-windows.md                 ├── Backup de Factum.bat
├── version.txt                            ├── Diagnostico de Factum.bat
├── SHA256SUMS.txt                         ├── Restaurar Factum.bat
├── docker-compose.yml                     ├── scripts\ (*.ps1)
├── .env.example                           ├── config\ (.env, appsettings.Local.json, branding\)
├── appsettings.Local.example.json         ├── evidencia\   ← /data del backend
├── factum.ico                             ├── backups\
├── imagenes\factum-vX.Y.Z.tar             └── logs\
├── scripts\ (_comun, instalar, actualizar, backup, restaurar, diagnostico, abrir-factum .ps1;
│            .bat de uso diario que instalar copia a C:\Factum)
└── tatana\Tatana-Portable-vX.Y.Z-Windows.zip
```

En el repo: `deploy/windows/{docker-compose.yml, .env.example, appsettings.Local.example.json, LEEME.txt, armar-paquete.sh, armar-tatana-portable.sh, *.bat, scripts/*.ps1, scripts/PSScriptAnalyzerSettings.psd1, tests/comun.Tests.ps1}`.

## 7. Contrato compartido

No cruza `client/` ↔ `server/` (ningún DTO ni tipo TS cambia). Lo que sí tiene que coincidir
exactamente entre archivos de esta HU:

### 7.1 Build args del frontend

| Nombre exacto | Valor en la instalación | Dónde se define | Dónde se consume |
|---|---|---|---|
| `NEXT_PUBLIC_BACKEND_URL` | `http://localhost:8080` | `client/Dockerfile` (`ARG`, default), `deploy/windows/armar-paquete.sh` (`--build-arg`), `docker-compose.yml` raíz (`build.args`) | `client/next.config.ts` (`env:`) → `src/lib/api.ts`, `src/app/dashboard/page.tsx` |
| `NEXT_PUBLIC_AGENT_URL` | `http://localhost:8765` | ídem | `client/next.config.ts` → `src/lib/agent.ts`, `src/hooks/useAgentConnection.ts`, `src/components/CaseFormStep.tsx` |

Los puertos publicados del compose (`127.0.0.1:8080`, `127.0.0.1:3000`) y el `AGENT_PORT=8765` del
portátil tienen que coincidir con estos valores. Por eso los puertos son fijos.

### 7.2 Variables del `.env` de la instalación

`deploy/windows/.env.example` ↔ `deploy/windows/docker-compose.yml` (interpolación) ↔
`deploy/windows/scripts/_comun.ps1` y demás scripts. Nombres exactos: `COMPOSE_PROJECT_NAME`,
`FACTUM_VERSION`, `FACTUM_HOME`, `FACTUM_JWT_SECRET`, `FACTUM_JWT_EXPIRY_HOURS`, `FACTUM_AUTH_MODE`,
`FACTUM_BACKUP_DESTINO`, `FACTUM_BACKUP_CONSERVAR`. Mapeo a config .NET (compose → backend):
`Jwt__Secret`, `Jwt__ExpiryHours`, `Auth__Mode`, `MongoDb__ConnectionString`, `MongoDb__DatabaseName`,
`Storage__DataDirectory`, `Integrations__Support__Enabled`, `ASPNETCORE_ENVIRONMENT`.

### 7.3 Formatos de archivos de backup y paquete

- `manifiesto-sha256.txt` y `SHA256SUMS.txt`: una línea por archivo, `<sha256 hex minúsculas><dos espacios><ruta relativa con />`,
  UTF-8 sin BOM, LF, ordenado por ruta. Compatible con `shasum -a 256 -c` en la Mac (así el
  implementador lo prueba sin Windows).
- `backup-info.json` (UTF-8 sin BOM):
  `{ "formato": 1, "factum_version": "X.Y.Z", "fecha_local": "AAAA-MM-DDTHH:MM:SS", "equipo": "<COMPUTERNAME>", "mongo_imagen": "mongo:7.0.<patch>", "casos": <int>, "archivos_evidencia": <int>, "bytes_evidencia": <int> }`.
- Estructura de un backup: `mongo/factum.archive.gz`, `evidencia/**`, `config/{.env, appsettings.Local.json, branding/**, docker-compose.yml, version.txt}`, `backup-info.json`, `manifiesto-sha256.txt`.

### 7.4 Health endpoints que leen los scripts

- Backend `GET /health` → campo `status` (`"ok"`) y `auth_mode` (`"dev"`/`"external"`), que
  `diagnostico.ps1` muestra. Sin cambios en `server/src/Factum.Backend/Program.cs`.
- Tatana `GET /health` → campo `mock` (bool) en `server/src/Factum.Agent/Controllers/HealthController.cs`,
  sin cambios. `instalar.ps1`, `actualizar.ps1` y `diagnostico.ps1` lo leen con `Invoke-RestMethod`
  como `$r.mock`.

### 7.5 Config del agente

`Agent:BindAddress` (string, default `"localhost"`) en `server/src/Factum.Agent/Models/AgentModels.cs`
(`AgentOptions.BindAddress`) ↔ CLI `--bind`. No se agrega a `appsettings.json` versionado (D3: no se
toca ese archivo); el default vive en la clase.

## 8. Decisiones técnicas

**Las de la HU (D1-D20) están validadas en A y no se reabren.** Estas son las de implementación:

| # | Decisión | Recomendada | ¿Usuario? |
|---|---|---|---|
| **DT1** | Bind del agente: `Agent:BindAddress` / `--bind`, default **`localhost`** (IPv4 + IPv6 loopback) para **todos** los entornos, también en desarrollo. Quien necesite el agente en la red usa `--bind 0.0.0.0` (con warning). | A) default `localhost` en todos lados. B) default `0.0.0.0` y `localhost` solo en el `.ini` del portátil. | **Sí, informativo:** cambia el comportamiento de desarrollo si alguna vez accedías al agente desde otra máquina (hoy `client/.env.local` apunta a `localhost`, así que no debería afectarte). |
| **DT2** | `backup.ps1` **detiene backend y frontend** mientras copia (base y evidencia consistentes). Factum queda pausado unos minutos (más con mucha evidencia), también en la tarea programada de las 20:00. | A) detener. B) backup en caliente (sin pausa, con riesgo de un caso a medio generar inconsistente entre base y ZIP). | **Sí:** es visible para el estudio. |
| **DT3** | Mock local del usuario: no se toca `server/src/Factum.Agent/appsettings.json` (HEAD ya está en `false`). El agente gana soporte de `appsettings.Local.json` (ignorado por git, excluido del publish) para que el usuario mueva ahí su `Mock: true`. El paquete se arma desde `git archive` y verifica `Mock=false` dos veces. | A) esto. B) sin `appsettings.Local.json` en el agente; el usuario usa `--mock`. | No (el orquestador le avisa al usuario la acción opcional de mover su `Mock: true`). |
| **DT4** | El portátil del paquete sale con `CLIENT_URL=` vacío: Tatana **no abre el navegador** al iniciar sesión (antes de que Docker esté listo, mostraba una página de error). Factum se abre con el acceso "Factum" del escritorio, que espera a que el sistema esté listo. Se cambia `launch-tatana.bat` para tolerar el valor vacío. | A) no abrir. B) dejar que abra (página de error durante 1-2 minutos). | **Sí:** cambia lo que ve el perito al prender la PC. |
| **DT5** | `instalar.ps1` restringe los permisos NTFS de `C:\Factum` al usuario que instala + Administradores + SYSTEM (evidencia, backups con contraseñas de ZIP, `.env` con el JWT). Otra cuenta de Windows de la misma PC no puede leer esas carpetas. Si `icacls` falla, solo avisa. | A) restringir. B) dejar los permisos heredados de `C:\` (cualquier usuario de la PC puede leer). | **Sí:** si en el estudio varias personas usan **cuentas de Windows distintas** en esa PC, con A solo la cuenta que instaló ve las carpetas (Factum en el navegador sigue andando para todas). Además, Docker Desktop corre por usuario. |
| DT6 | El portátil de Tatana se **arma localmente** en la Mac (`armar-tatana-portable.sh`, réplica del job de GitLab) o se pasa ya hecho con `--tatana-zip`. El remoto real es GitHub y el CI de GitLab no corre. | A) script local + opción `--tatana-zip`. | No. |
| DT7 | Imágenes: backend con build en `$BUILDPLATFORM` + publish cruzado (`-a $TARGETARCH`); frontend 100 % en `linux/amd64` emulado (H7). | A. | No. |
| DT8 | `mongo:7.0.<patch>` exacto en el compose, como fuente única que lee `armar-paquete.sh`. `backup-info.json` registra la imagen. | A. | No. |
| DT9 | Compose sin `name:`, `container_name:` ni volúmenes con nombre absoluto. Proyecto por `-p` (`factum` en el estudio, `factum-verif` en la Mac). | A. | No. |
| DT10 | Backend sigue corriendo como root. El frontend corre como `node` (no root). | A. | No. |
| DT11 | Healthchecks: backend `curl /health` (se suma `curl` a la imagen: unos MB, frente a los cientos que salen con LibreOffice); frontend `wget --spider /` (busybox de alpine); mongo `mongosh ping`. | A. | No. |
| DT12 | Red `datos` `internal: true` para Mongo, red `web` para backend/frontend con puertos en `127.0.0.1`. Logs con driver `local` rotado (10 MB × 5) para que no llenen el disco con los años. | A. | No. |
| DT13 | Dump/restore de Mongo con archivo dentro del contenedor + `docker compose cp` (nunca redirección de stdout en PS 5.1). | A. | No. |
| DT14 | `restaurar.ps1` exige la **misma versión** que el backup. Si la instalación no está vacía: backup previo automático + confirmación `SI`. Conserva `FACTUM_HOME`/`FACTUM_VERSION`/`COMPOSE_PROJECT_NAME` locales. | A. | No. |
| DT15 | `actualizar.ps1` corre desde el paquete nuevo, guarda compose/scripts/.env previos y hace rollback a X si Y no queda `healthy` en 180 s. No borra imágenes viejas. | A. | No. |
| DT16 | Retención de backups: `FACTUM_BACKUP_CONSERVAR=10`, solo en `C:\Factum\backups`. Tarea programada opcional diaria a las 20:00 con `StartWhenAvailable`. | A (10 y 20:00 se cambian en `.env`/Programador de tareas). | No. |
| DT17 | Puertos 3000/8080/8765 fijos (horneados en el bundle). Si están ocupados, `instalar.ps1` falla y explica. | A. | No. |
| DT18 | Guía en `docs/instalacion-windows.md` con Parte A (uso diario, no técnica) y Parte B (instalación). Sin PDF generado (no hay `pandoc` garantizado); el `.md` viaja en el paquete. | A. | No. |
| DT19 | Tests Pester para las funciones puras de `_comun.ps1` (`Read-EnvFile`, `Write-EnvFile`, `Set-EnvValue`, `New-JwtSecret`, `New-HashManifest`, `Test-HashManifest`), corridos en un contenedor `pwsh` en la Mac. | A. | No. |

## 9. Checklist atómico

> Regla dura de datos (AGENTS.md) para **los dos** implementadores: nada de `docker compose` sin
> `-p factum-verif` y sin `--env-file` de scratch; prohibido `docker compose down`/`rm` sobre el
> proyecto `factum` de la Mac (H2), `docker volume prune`/`system prune`/`image prune`, tocar
> `evidentia-v2-mongo-1` o el puerto 27017, y tocar `server/src/Factum.Backend/dev-data/` o
> cualquier `Storage:DataDirectory` del usuario. Las carpetas de prueba van en el scratchpad de la
> sesión y se borran al terminar; solo se borran las imágenes con tag `*-verif`/`0.0.0-verif` creadas
> por la prueba. No tocar `backlog.json` ni `progress/current.md`. No editar ni stagear
> `server/src/Factum.Agent/appsettings.json`.

### 9.1 Frontend — `implementer-frontend` (app: **`client/`** solamente; `agent-ui/` no se toca)

- [ ] F1. Leer `client/AGENTS.md` y el doc de `output` de Next 16 (§6.3).
- [ ] F2. Crear `client/.dockerignore` (§6.3).
- [ ] F3. `client/next.config.ts`: sumar `output: "standalone"`, sin tocar `env` ni `allowedDevOrigins`.
- [ ] F4. Reescribir `client/Dockerfile` (§6.3): 3 stages, `ARG`/`ENV` `NEXT_PUBLIC_*`,
      `HOSTNAME=0.0.0.0`, `USER node`, `node server.js`.
- [ ] F5. Skills obligatorias (`ui-ux-pro-max`, `senior-frontend`, `3d-web-experience`,
      `web-design-guidelines`): invocarlas y dejar constancia "sin hallazgos aplicables: HU sin cambios
      visuales" en el progress.
- [ ] F6. Verificación (§10.1) y `progress/impl_frontend_instalacion-local-docker.md`.

### 9.2 Backend / infra — `implementer-backend`

**Imagen del backend**
- [ ] B1. `server/src/Factum.Backend/Dockerfile` según §6.2 (sin LibreOffice, con `curl`,
      `$BUILDPLATFORM` + `-a $TARGETARCH`).
- [ ] B2. Comentario de `ReportService.cs` L1293-1295 (solo el comentario).

**Tatana**
- [ ] B3. `AgentOptions.BindAddress` (default `"localhost"`).
- [ ] B4. `Program.cs`: `--bind`, resolución/validación del bind, warning con `0.0.0.0`, log con mock
      efectivo (§6.1).
- [ ] B5. `Program.cs`: carga opcional de `appsettings.Local.json` (copia del bloque del backend).
- [ ] B6. `Factum.Agent.csproj`: excluir `appsettings.Local.json` de `Content`/`None`.
- [ ] B7. `grep` de referencias y borrar `server/src/Factum.Agent/Dockerfile`.
- [ ] B8. **No** tocar `server/src/Factum.Agent/appsettings.json` (confirmar con `git diff --cached`
      antes de cada commit).

**Compose y config**
- [ ] B9. `deploy/windows/docker-compose.yml` (§6.4) con el patch exacto de `mongo:7.0.x`.
- [ ] B10. `deploy/windows/.env.example` y `deploy/windows/appsettings.Local.example.json`.
- [ ] B11. `docker-compose.yml` de la raíz: `NEXT_PUBLIC_*` a `build.args` + comentario.
- [ ] B12. `.gitignore`: `deploy/windows/dist/`.

**Scripts PowerShell** (R1-R9 en todos)
- [ ] B13. `scripts/_comun.ps1` (tabla de §6.5).
- [ ] B14. `scripts/instalar.ps1` + `Test-Requisitos` Q1-Q11 (§6.5.1, §6.5.2).
- [ ] B15. `scripts/backup.ps1` (§6.5.3).
- [ ] B16. `scripts/restaurar.ps1` (§6.5.4).
- [ ] B17. `scripts/actualizar.ps1` (§6.5.5).
- [ ] B18. `scripts/diagnostico.ps1` y `scripts/abrir-factum.ps1` (§6.5.6).
- [ ] B19. `.bat` envoltorios (§6.8, R9) en ASCII + CRLF.
- [ ] B20. `scripts/PSScriptAnalyzerSettings.psd1` (reglas por defecto +
      `PSUseCompatibleSyntax`/`PSUseCompatibleCmdlets` apuntando a 5.1 en Windows, +
      `PSUseBOMForUnicodeEncodedFile`). `PSAvoidUsingWriteHost` excluida (los scripts son de consola
      interactiva).
- [ ] B21. `tests/comun.Tests.ps1` (Pester 5) para las funciones de DT19, incluido un caso de
      manifiesto con un archivo alterado y uno faltante (escenario "backup corrupto").

**Empaquetado**
- [ ] B22. `deploy/windows/armar-tatana-portable.sh` (§6.6).
- [ ] B23. `deploy/windows/armar-paquete.sh` (§6.6), con la guarda de Mock y la verificación de arquitectura.
- [ ] B24. `packaging/portable/launch-tatana.bat`: `CLIENT_URL` vacío = no abrir el navegador.
- [ ] B25. `deploy/windows/LEEME.txt`.

**Docs**
- [ ] B26. `docs/instalacion-windows.md` (§6.7, las 14 secciones, aviso D7, licencia de Docker Desktop,
      requisitos y mensajes de Q1-Q11).
- [ ] B27. `README.md`: link a la guía, `--mock`/`appsettings.Local.json` del agente, `--bind`.

**Verificación y cierre**
- [ ] B28. Verificación §10.2 completa (B-V1 a B-V9) y `progress/impl_backend_instalacion-local-docker.md`
      con resultados reales (comandos, tamaños de imagen, tag de mongo, salida del analyzer y de Pester).

## 10. Verificación

### 10.1 `implementer-frontend` (en la Mac)

1. F-V1. `cd client && npx tsc --noEmit` limpio.
2. F-V2. `docker buildx build --platform linux/amd64 --load -t factum-frontend:0.0.0-verif client/`
   (corre emulado y puede tardar más de 10 min: lanzarlo en background con timeout amplio).
3. F-V3. `docker run --rm factum-frontend:0.0.0-verif ls -la /app` → hay `server.js`, `.next/static`
   y `public`; **no** hay `.env.local` ni `node_modules` completo. Registrar el tamaño (`docker image ls`)
   frente a la imagen anterior (`factum-frontend` si existe, o estimado).
4. F-V4. `docker run --rm -d --name factum-verif-front -p 127.0.0.1:13000:3000 factum-frontend:0.0.0-verif`;
   `curl -fsS http://127.0.0.1:13000/` → 200 con el HTML del login; `docker inspect` muestra
   `User=node`; `grep -rl "localhost:8080" ` sobre `/app/.next/static` dentro del contenedor encuentra
   el bundle. Luego `docker rm -f factum-verif-front` (y `docker image rm factum-frontend:0.0.0-verif`
   **solo** si el backend no la va a reutilizar; coordinar dejándola y anotándolo en el progress).
5. F-V5. `git status` muestra solo `client/Dockerfile`, `client/.dockerignore` y `client/next.config.ts`
   como cambios del frontend. `client/.next` del usuario no se regeneró por un build local.

### 10.2 `implementer-backend` (en la Mac)

1. B-V1. `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` y
   `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` sin warnings nuevos.
2. B-V2. Agente: `dotnet run --project server/src/Factum.Agent -- --port 18765 --data <scratch>/agent --mock`
   en background → `curl http://127.0.0.1:18765/health` OK y `lsof -nP -iTCP:18765 -sTCP:LISTEN`
   muestra solo `127.0.0.1`/`[::1]`. Repetir con `--bind 0.0.0.0` (aparece el warning y `*:18765`) y
   con `--bind basura` (no arranca, mensaje claro). Matar los procesos. Con
   `<scratch>/appsettings.Local.json` no se prueba (content root = carpeta del proyecto: **no** crear
   `server/src/Factum.Agent/appsettings.Local.json`; basta con revisar el código y el `.csproj`).
3. B-V3. `docker compose -p factum-verif -f deploy/windows/docker-compose.yml --env-file <scratch>/.env config`
   OK con un `.env` de scratch (`FACTUM_HOME=<scratch>/home`, `FACTUM_VERSION=0.0.0-verif`). Con
   `FACTUM_JWT_SECRET` vacío falla con el mensaje en castellano.
4. B-V4. `docker buildx build --platform linux/amd64 --load -t factum-backend:0.0.0-verif server/src/Factum.Backend`
   (+ la del frontend de F-V2 si sigue, o rebuild); `docker image inspect` = `linux/amd64`; `dpkg -l`
   en la imagen no tiene `libreoffice*` y sí `curl`/`tzdata`. Anotar el tamaño frente a
   `factum-backend:latest` existente.
5. B-V5. Stack completo: `<scratch>/home/{evidencia,config/branding}` + `appsettings.Local.json` del
   example; `docker pull --platform linux/amd64 mongo:7.0.<patch>`; override de scratch
   `services: {backend: {ports: !reset []}, frontend: {ports: !reset []}}`;
   `docker compose -p factum-verif -f deploy/windows/docker-compose.yml -f <scratch>/sin-puertos.yml --env-file <scratch>/.env up -d`
   → los tres `healthy`. `compose exec backend curl -fsS http://127.0.0.1:8080/health` →
   `"auth_mode":"dev"`. `compose exec frontend wget -qO- http://127.0.0.1:3000/` → HTML del login.
   `compose exec backend sh -c 'touch /data/x && ls /data'` → el archivo aparece en
   `<scratch>/home/evidencia` (y se borra).
6. B-V6. Ciclo dump/restore con **los mismos comandos** que los scripts (§6.5.3 paso 4, §6.5.4 paso 5)
   desde bash: insertar un documento de prueba en `factum-verif` (Mongo de la verificación, no otra),
   dump → `compose cp` → `mongorestore --drop` → el documento sigue ahí. `shasum -a 256 -c` sobre un
   manifiesto generado por `New-HashManifest` (vía el contenedor pwsh) valida el formato §7.3.
7. B-V7. Limpieza: `docker compose -p factum-verif … down -v` (solo el volumen `factum-verif_mongo-data`,
   creado por la prueba), `docker image rm factum-backend:0.0.0-verif factum-frontend:0.0.0-verif`,
   borrar `<scratch>`. **No** borrar `mongo:7.0.<patch>`; anotarlo.
8. B-V8. Scripts PowerShell en contenedor:
   `docker run --rm -v "$PWD/deploy/windows:/w" mcr.microsoft.com/powershell:lts-ubuntu-22.04 pwsh -NoProfile -Command "Install-Module PSScriptAnalyzer,Pester -Force -Scope CurrentUser -SkipPublisherCheck; foreach($f in Get-ChildItem /w/scripts/*.ps1){ $e=$null; [System.Management.Automation.Language.Parser]::ParseFile($f.FullName,[ref]$null,[ref]$e) | Out-Null; if($e){ $e; exit 1 } }; Invoke-ScriptAnalyzer -Path /w/scripts -Settings /w/scripts/PSScriptAnalyzerSettings.psd1 -Severity Warning,Error; Invoke-Pester /w/tests -Output Detailed"`
   (ajustar el tag de la imagen si no existe). **Cero errores**; los warnings que queden se justifican
   uno por uno en el progress. Además, BOM: `head -c3 deploy/windows/scripts/*.ps1 | xxd` = `efbbbf`
   en todos, y `file` = CRLF en `.ps1` y `.bat`; `.bat` sin bytes no ASCII (`LC_ALL=C grep -nP '[^\x00-\x7F]'` vacío).
9. B-V9. Empaquetado: `bash -n` en los dos `.sh` (y `shellcheck` si está instalado).
   `deploy/windows/armar-paquete.sh --version 0.0.0-verif --sin-tatana --salida <scratch>/dist` de
   punta a punta (estructura §6.8, `SHA256SUMS.txt` válido con `shasum -a 256 -c`, `.tar` con solo
   amd64). `armar-tatana-portable.sh` se corre una vez (descarga ~300 MB; si no hay red, se anota como
   no verificado) y se confirma `appsettings.json` con `"Mock": false` dentro del zip y el `.ini` con
   `CLIENT_URL=`/`UPDATE_URL=` vacíos. Después se borran los artefactos de scratch y los tags
   `0.0.0-verif`.

### 10.3 Prueba manual del usuario (Windows 10, la PC del estudio o, mejor antes, una VM limpia)

1. Armar el paquete real en la Mac: `deploy/windows/armar-paquete.sh --version 1.0.0` y copiarlo por
   USB.
2. **Requisitos:** en una PC/VM sin Docker, correr `1-Instalar Factum.bat` → lista en castellano de lo
   que falta (sin tocar el disco). Probar también con Docker Desktop cerrado (lo arranca solo).
3. **Instalación limpia sin internet** (cable desconectado): termina con la URL; `http://localhost:3000`
   muestra el login; existe `C:\Factum\config\.env` con un JWT propio; accesos del escritorio creados.
4. **Solo local:** desde otra PC de la red, `http://<ip>:3000`, `:8080`, `:8765` y `:27017` → rechazado.
5. **Tatana:** `diagnostico` muestra `mock: false`; Android (con driver) e iPhone (con Apple Mobile
   Device Support) aparecen en el wizard y se captura pantalla.
6. **Identidad:** completar `appsettings.Local.json` + logo, *Diagnóstico* con `-ReiniciarBackend`,
   generar un informe → membrete del estudio. Abrirlo en Word.
7. **Reinicio de Windows:** al iniciar sesión, Docker arranca solo, el acceso "Factum" espera y abre;
   casos, ZIP, DOCX y contraseñas intactos. Tatana arranca sin abrir el navegador (DT4).
8. **Backup** a un disco externo con `-Destino`; revisar `manifiesto-sha256.txt` y `backup-info.json`.
9. **Restauración** en una instalación vacía (otra VM o reinstalada): casos y evidencia iguales; el
   SHA-256 de un ZIP restaurado (`Get-FileHash`) coincide con el del informe. Modificar un byte de un
   archivo del backup → `restaurar` se detiene y lo lista.
10. **Actualización** con un paquete `1.0.1` → backup automático, sube, datos intactos. Probar la vuelta
    atrás con `actualizar.ps1 -SimularFalla` (§6.5.5) → vuelve a 1.0.0, lo informa y los datos siguen.
11. **Config inválida:** poner `FACTUM_AUTH_MODE=xyz` → el script dice que el backend no arrancó y
    muestra el log.

### 10.4 Lo que el reviewer chequea además de CHECKPOINTS

- `server/src/Factum.Agent/appsettings.json` **no** está en el diff de la HU.
- Ningún `.ps1` usa sintaxis de PS 7 ni redirige binarios. Todos con BOM.
- El compose no publica Mongo, publica solo en `127.0.0.1`, y no tiene `name:`/`container_name:`.
- `progress/impl_*` muestran salidas reales de B-V1…B-V9 y F-V1…F-V5, y que no se tocó el proyecto
  `factum` de la Mac ni el puerto 27017.

## 11. Fuera de alcance (recordatorio técnico)

Usuarios locales (`Auth:Mode=local`, HU `usuarios-locales`), instalador NSIS con herramientas
embebidas (`instalador-tatana-windows`), `agent-ui/src/main/config.ts` (`serverUrl` 5000), borrar el
código muerto de PDF en `ReportService.cs`, tocar `.gitlab-ci.yml`, firmar ejecutables, HTTPS/red,
backups cifrados o remotos, puertos configurables.

## Resolución de decisiones (2026-10-02)

- DT1 y DT3: informativas, aceptadas.
- **DT2 → A, DT4 → A, DT5 → A**, confirmadas por el usuario (backup pausa web/backend; Tatana no abre el navegador y se entra por el acceso "Factum"; permisos de C:\\Factum solo para quien instala, Administradores y SYSTEM).
- Resto: las recomendadas.
