# SDD: Factum en la nube (VPS DonWeb + Docker Compose), con Tatana local

**HU:** `docs/hu-despliegue-nube.md` (issue #14), validada el 2026-10-06. La D9 cambió después, también el 2026-10-06
(ver §0).
**Slug / rama:** `despliegue-nube` / `feat/despliegue-nube` (desde `develop` en `645c44c`).
**Base:** #6 `usuarios-locales`, #9 `zip-local-informe-servidor`, #12 `abm-clientes`, #13 `marca-por-cliente` (todas en
`develop`), y `instalacion-local-docker` / `instalacion-poca-ram` (`deploy/windows/`, que no cambia de comportamiento).

---

## 0. Decisiones del usuario que mandan sobre esta SDD

| D | Decisión vigente | Origen |
|---|---|---|
| D1 | Un **VPS "Cloud Server" de DonWeb (Argentina)** con un `docker-compose` de producción: Caddy + frontend + backend + Mongo. Es x86_64, así que **no hacen falta imágenes ARM**. | Validación 2026-10-06 |
| D2–D4 | Todo en el mismo VPS: Mongo en un contenedor con autenticación y sin puerto publicado, el DOCX en un volumen persistente y el frontend `standalone` detrás de Caddy. | Ídem |
| D5 | **El dominio no está definido.** Arranca con uno **provisorio** (el hostname que asigne DonWeb, o DuckDNS) y queda **100 % configurable**: `.env` del host, variable de GitHub para el build del front y parámetro de empaquetado de Tatana. Nunca va hardcodeado. **Tatana acepta una lista de orígenes.** La guía documenta el cambio de dominio. | Ídem |
| D6 | Se resuelve con DonWeb: los datos quedan en Argentina. | Ídem |
| D7, D8, D8b, D10–D17 | Las opciones recomendadas por la HU. | Ídem |
| **D9** | **Cambia: deploy automático cuando se mergea `develop` a `main`** (pedido del usuario). Un workflow de GitHub Actions arma las imágenes, las publica en GHCR y despliega por SSH. Si el chequeo de salud falla, hace rollback. El script manual queda como plan B y para el primer arranque. | Pedido del usuario, 2026-10-06, posterior a la validación |
| Guía | Guía paso a paso de DonWeb en `docs/despliegue-nube.md`. Los pasos del panel que no se pudieron verificar llevan la marca "a confirmar en el panel". | Validación 2026-10-06 |

> **Nota para el orquestador:** en `docs/hu-despliegue-nube.md`, sección "Validación del usuario", la D9 tiene que
> quedar como "deploy automático al mergear a `main` (pedido del usuario)". Esta SDD ya la toma así.

---

## 1. Resumen funcional

Factum se publica como SaaS en un único VPS de DonWeb. Un `docker compose` de producción
(`deploy/cloud/docker-compose.yml`) levanta cuatro servicios:

- **Caddy**: el único que expone puertos (80 y 443). Saca el certificado de Let's Encrypt solo, redirige HTTP a HTTPS y
  agrega los headers de seguridad de D13.
- **frontend**: la imagen `standalone` de siempre.
- **backend**: `Auth:Mode=local`.
- **Mongo 7**: con usuario y contraseña, en una red interna.

Todo se sirve desde **un solo hostname**, ruteado por path:

- `/api/*`, `/health*` y `/tatana/updates/*` van al backend;
- `/descargas/*` son archivos estáticos (el Tatana para la nube);
- el resto va al frontend.

Así un dominio provisorio de un solo nombre alcanza, y cambiar de dominio toca un solo valor.

Cada merge a `main` dispara un workflow de GitHub Actions que:

1. arma las imágenes del backend y del frontend (el front, con la URL pública como build arg);
2. las publica en GHCR (privado) con el tag `sha-<commit>`;
3. entra al VPS por SSH, con una clave dedicada limitada por `command=` a un único script de deploy.

Ese script:

1. actualiza el checkout del repo al commit;
2. baja las imágenes;
3. hace un backup local previo;
4. levanta la versión nueva;
5. chequea `/health/ready` a través de Caddy;
6. si falla, vuelve solo a la versión anterior y deja el job en rojo.

Cron diario: `mongodump` y los DOCX se cifran con `age` y se suben a un bucket fuera del host (Cloudflare R2), con
retención de 7 diarios y 4 semanales. Un monitor externo (UptimeRobot) consulta `/health/ready`, un endpoint nuevo que
también chequea Mongo, y avisa por mail.

En código, los cambios son chicos:

- **backend:** `GET /health/ready`;
- **empaquetado de Tatana:** un parámetro para hornear la lista de orígenes y la `CLIENT_URL`;
- **frontend:** un build arg opcional con el link de descarga de Tatana para el mensaje `origin_not_allowed`.

Todo lo demás es infraestructura nueva en `deploy/cloud/` y `.github/workflows/`, más la guía `docs/despliegue-nube.md`.
La instalación local de Windows (`deploy/windows/`) se arma y se comporta igual que hoy.

## 2. Toca

| Parte | ¿Toca? | Detalle |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí** (chico) | `GET /health/ready` + `Infrastructure/MongoHealthProbe.cs`. Nada más: forwarded headers, CORS y Swagger ya están bien (§4). |
| tests `server/tests/Factum.Backend.Tests` | **sí** | `HealthReadyTests.cs` (mapeo de respuesta + probe contra un Mongo inalcanzable). |
| backend (Tatana) `server/src/Factum.Agent` | **no** (código) | `Agent:AllowedOrigins` ya es una lista (`OriginPolicy`). Solo un test extra opcional en `server/tests/Factum.Agent.Tests/OriginPolicyTests.cs`. |
| empaquetado de Tatana `deploy/windows/armar-tatana-portable.sh` + script nuevo | **sí** | Flags opcionales `--origenes` y `--client-url`; sin flags, el resultado es el mismo de hoy (D15). |
| infraestructura `deploy/cloud/` | **sí** (nuevo) | Compose, Caddyfile, `.env.example`, init de Mongo y scripts bash. |
| CI/CD `.github/workflows/` | **sí** (nuevo) | `desplegar-produccion.yml` (push a `main`) y `verificar.yml` (PR a `develop`/`main`). |
| docs | **sí** | `docs/despliegue-nube.md` (nuevo) y `README.md` (links y claves nuevas). |
| client `client/` | **sí** (chico) | Build arg `NEXT_PUBLIC_TATANA_DOWNLOAD_URL` (Dockerfile + `next.config.ts`) y el texto de `origin_not_allowed` en `src/lib/agent-messages.ts`. |
| agent-ui `agent-ui/` | **no** | El instalador NSIS/Electron queda fuera (P4); su `publish.url` no cambia (D8b fuera de alcance). |

Reparto:

- **`implementer-backend`**: `server/`, `deploy/`, `.github/workflows/`, `docs/despliegue-nube.md` y `README.md`.
- **`implementer-frontend`**: solo `client/`.

---

## 3. Hallazgos sobre el código y la infraestructura reales (2026-10-06)

1. **`Agent:AllowedOrigins` ya acepta una lista** (`Factum.Agent/Common/OriginPolicy.cs`, `Program.cs`). Si la lista
   está presente, **reemplaza** a los defaults (`http://localhost:3000`, `http://127.0.0.1:3000`), no se suma. Al
   hornear los orígenes de la nube hay que volver a poner los `localhost` explícitamente. Un valor inválido hace que
   **Tatana no arranque** en la PC del perito, así que el script de empaquetado valida antes de hornear.
2. **Los arrays de configuración de .NET se combinan por índice.** Una variable de entorno `Agent__AllowedOrigins__0`
   en una PC pisa solo el elemento 0 del JSON. Por eso la guía desaconseja configurar orígenes por variable de entorno
   en la PC. La vía soportada es el paquete horneado y, como escape por PC, un `appsettings.Local.json` al lado del
   `.exe`, que `Program.cs` ya carga y que `update-portable.ps1` no pisa (no viaja en el zip).
3. `armar-tatana-portable.sh` ya parte de un árbol limpio (`git archive`), ya verifica `Mock=false` y ya escribe el
   `.ini` con `CLIENT_URL` vacío para el paquete local. Es el punto natural para los flags nuevos. El NSIS de Electron
   solo se arma en `.gitlab-ci.yml`, que no corre; el `.exe` que copia es single-file y no lleva `appsettings.json` al
   lado. Queda fuera (P4).
4. **Backend detrás de un proxy:**
   - `UseForwardedHeaders` (`X-Forwarded-For`/`Proto`, `KnownProxies` vacíos) ya está. Es aceptable porque el backend
     **no publica puerto**: solo Caddy lo alcanza por la red Docker `web`.
   - No hace falta `UseHttpsRedirection` ni HSTS en .NET: los pone Caddy.
   - Swagger solo se mapea en `Development`, y el compose fija `ASPNETCORE_ENVIRONMENT=Production`.
   - `AllowedHosts: "*"` alcanza: Caddy solo atiende el hostname configurado.
   - **No hay cambios de código para el proxy.**
5. **`/health`** devuelve `{ status, version, auth_mode }` sin tocar Mongo. Lo usan:
   - el healthcheck de `deploy/windows/docker-compose.yml`;
   - `Get-BackendHealth` (`deploy/windows/scripts/_comun.ps1`);
   - `diagnostico.ps1`.

   Cambiar su semántica (por ejemplo, devolver 503 con Mongo caído) alteraría la instalación local (D15). **Decisión
   técnica DT3:** `/health` queda intacto y se agrega `/health/ready`.
6. **Mongo:** cada repositorio crea su propio `MongoClient`, sin un cliente compartido en DI. El probe nuevo crea el
   suyo, con timeouts cortos.
7. **Tope de body:**
   - `generate/finish` sube el límite por request hasta `Report:MaxGenerateUploadBytes` (256 MiB) y el resto queda en
     los 30 MB de Kestrel.
   - Caddy no limita el body por defecto. Se fija explícitamente `max_size 300MB` (SI = 300.000.000 B, más que los
     268.435.456 B) en las rutas del backend, como tope de defensa.
   - `POST /files` (hasta 4 GiB) no se usa con una base nueva (B7).
8. **Tokens en logs (B10):**
   - Caddy **no** escribe access log si el Caddyfile no tiene la directiva `log`. No se pone.
   - El backend en `appsettings.json` tiene `Microsoft.AspNetCore: Warning`, así que no hay logs de request.
   - El `server.js` standalone no loguea requests.
   - El `?token=` de las descargas no queda en ningún log. No hace falta cambiar `client/src/lib/api.ts`.
9. **El frontend usa la cámara del navegador** (`getUserMedia` en `CameraRecordModal.tsx` y `WebcamCaptureModal.tsx`)
   y `window.open` con `noopener` hacia Faro (`SoporteModal.tsx`). El `Permissions-Policy` tiene que permitir
   `camera=(self)` y `microphone=(self)`.
10. **No hay recursos externos** en `client/src` (fuentes autoalojadas, sin CDN). La CSP Report-Only puede ser
    `'self'` más Tatana.
11. **`report_hash`** es el SHA-256 en hex minúscula del DOCX (`AgentGeneration.cs`, `Convert.ToHexStringLower`). El
    DOCX vive en `<DataDirectory>/cases/<id>/<pdf_filename>`. Eso permite el script de verificación de §6.8.
12. **`.dockerignore` del backend** ya excluye `appsettings.Local.json`, `branding/` y `dev-data/`. Las imágenes que
    arma CI no llevan identidad ni datos de nadie.
13. **`.gitignore`** ignora `.env` pero no `*.env`. Se agrega una regla para `deploy/cloud/` (§6.10).
14. `server/src/Factum.Agent/appsettings.json` tiene en la copia de trabajo `Mock: true` **sin commitear**. Es ajeno a
    esta HU; el empaquetado parte de `git archive` y lo verifica. El implementador **no** lo commitea.
15. La regla dura de datos de `AGENTS.md` vale para la Mongo de desarrollo: ninguna prueba de esta HU toca la base
    `factum`/`factum_dev` local ni `dev-data/`. La prueba local del compose de producción (§9.2) usa un directorio de
    datos propio en un temporal.

---

## 4. Decisiones técnicas

| # | Decisión | ¿Usuario? |
|---|---|---|
| **DT1** | **Un solo hostname con ruteo por path** (`/api/*`, `/health`, `/health/*`, `/tatana/updates/*` van al backend; `/descargas/*` son estáticos; el resto va al frontend), en lugar de `app.` + `api.` de la D5 original. Un dominio provisorio de DonWeb o de DuckDNS es un solo nombre, y hay un solo certificado. Hay **un solo origen** para Tatana y para CORS, y el cambio de dominio toca una variable. Todas las rutas del backend ya cuelgan de `/api`, `/health` y `/tatana/updates` (verificado en los `[Route]`), así que no hay choque con las páginas del front. `NEXT_PUBLIC_BACKEND_URL` = `https://<dominio>`. | Informativa |
| **DT2** | Variables de dominio: `FACTUM_DOMINIO` (host, sin esquema) en el `.env` del VPS, y la variable de repo `FACTUM_URL_PUBLICA` (`https://<dominio>`) en GitHub para el build del front. El script de deploy **verifica que coincidan** antes de tocar nada: compara el label `factum.url_publica` de la imagen del front con `https://$FACTUM_DOMINIO`. | No |
| **DT3** | **`GET /health/ready`** nuevo (anónimo): hace `ping` a Mongo con un timeout de 3 s. Devuelve 200 `{"status":"ok","mongo":"ok"}` o 503 `{"status":"unavailable","mongo":"down"}`, sin detalle del error. `/health` queda igual (liveness, D15). El monitor externo y el script de deploy usan `/health/ready`; el healthcheck del compose usa `/health` (si Mongo se cae, no se reinicia el backend en bucle). | No |
| **DT4** | **Mongo con dos usuarios:** `root` (solo para backup/restore y administración) y `factum_app` con `readWrite` sobre `factum` (el que usa el backend). Los crea `deploy/cloud/mongo-init/01-usuario-app.js` en el primer arranque (volumen vacío). Las contraseñas en hex (`openssl rand -hex 24`) evitan escapar caracteres en la connection string. | No |
| **DT5** | **Datos en bind mounts bajo `/srv/factum/datos/`** (`mongo/`, `backend/`, `caddy-data/`, `caddy-config/`) en lugar de volúmenes con nombre. Se ven y se respaldan desde el host sin contenedores auxiliares. `caddy-data` persiste los certificados (perderlos obliga a pedir otros y puede chocar con los rate limits de Let's Encrypt). | No |
| **DT6** | **Imágenes en GHCR privado**, tag inmutable `sha-<40 hex>` más `latest` (`latest` no se usa para desplegar). Plataforma `linux/amd64` nativa en `ubuntu-24.04`, sin emulación. En el VPS, `docker login ghcr.io` con un **PAT classic con solo `read:packages`**. | No |
| **DT7** | **El VPS tiene un clon del repo** con una **deploy key de solo lectura**. El deploy hace `git checkout --detach <sha>`, así el compose, el Caddyfile y los scripts siempre corresponden a las imágenes. Antes de hacer checkout, el script exige que `<sha>` sea ancestro de `origin/main`: no se puede desplegar un commit arbitrario. | No |
| **DT8** | **SSH de CI:** usuario `deploy` (sin sudo, en el grupo `docker`). Clave ed25519 dedicada con `command="/srv/factum/bin/deploy-forzado.sh",no-port-forwarding,no-X11-forwarding,no-agent-forwarding,no-pty` en `authorized_keys`. El script forzado solo acepta `desplegar sha-<40hex>` y `estado`. La host key queda fijada en el secret `DEPLOY_KNOWN_HOSTS` (`StrictHostKeyChecking=yes`). **Aviso honesto (va en la guía):** el grupo `docker` equivale a root en el host. Lo que acota a la clave de CI es el `command=` forzado, no la falta de sudo. | Informativa |
| **DT9** | **El script se copia antes de correr:** `desplegar.sh` se re-ejecuta desde una copia en `mktemp` antes de hacer `git checkout`, porque bash lee el script a medida que lo ejecuta y el checkout lo reescribiría a mitad de camino. El `deploy-forzado.sh` del `authorized_keys` es una **copia estable** en `/srv/factum/bin/`, instalada por `preparar-servidor.sh` y actualizable a mano. | No |
| **DT10** | **Rollback:** `/srv/factum/estado/desplegado` y `anterior` guardan los SHA. Si el chequeo post-deploy falla, el script vuelve al checkout y al tag de `desplegado` previo, espera salud y sale con código ≠ 0 (job en rojo). **Solo revierte código, no datos.** Para eso está el backup local `pre-deploy` que se hace antes de cada deploy (la guía explica cuándo restaurarlo). | No |
| **DT11** | **El workflow no despliega hasta que se habilita:** el job `desplegar` corre solo si la variable de repo `FACTUM_DEPLOY_HABILITADO == 'true'`. Los merges previos a configurar el VPS solo publican imágenes (y no quedan en rojo). El primer arranque es manual, con las imágenes que ya publicó el workflow. | No |
| **DT12** | **Secrets y variables a nivel de repositorio** (funcionan en cualquier plan de GitHub). El job `desplegar` declara `environment: production` solo para el historial y, si el plan lo permite, para la aprobación manual. Ver **P3**. | Sí (P3) |
| **DT13** | **Backup:** `mongodump --archive --gzip` de la base `factum` más un `tar` de `datos/backend` (sin `.upload-tmp/` ni `.generate-tmp/`), con un `SHA256SUMS`, todo empaquetado y **cifrado con `age`** a una clave pública (`FACTUM_BACKUP_AGE_RECIPIENT`). La clave privada **nunca** está en el VPS: la guarda el usuario. Se sube con `rclone` a `diarios/` y, los domingos, también a `semanales/`. La retención la aplica el script con `rclone delete --min-age` (8 días en diarios, 29 en semanales). En el host quedan las 3 últimas copias locales. Destino concreto: **Cloudflare R2** (P2). | Sí (P2) |
| **DT14** | **Alertas:** UptimeRobot (plan gratis) contra `https://<dominio>/health/ready` cada 5 min, con mail. El backup hace ping a un check de **healthchecks.io** (gratis) al terminar bien, y a `/fail` si falla o si el disco pasa el 85 %. Si el backup no corre, también llega un mail. Los límites de los planes gratis se anotan "(a confirmar)" con fecha en la guía. | No |
| **DT15** | **Headers (D13), en Caddy:** HSTS `max-age=31536000` **sin** `includeSubDomains` ni `preload` (con un dominio provisorio de terceros como DuckDNS no corresponde). Además `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options: DENY`, una CSP **efectiva** que solo lleva `frame-ancestors 'none'` (en Report-Only se ignora `frame-ancestors`), la CSP completa en **Report-Only**, `Permissions-Policy` y sin el header `Server`. Ver §6.2. | No |
| **DT16** | **Tatana para la nube = el portátil** (`Tatana-Portable-vX.Y.Z-Windows-nube.zip`), armado en la Mac con `deploy/cloud/armar-tatana-nube.sh`. Lleva horneados `Agent:AllowedOrigins` = localhost + los orígenes de la nube, y `CLIENT_URL` = el primer origen. Se publica en `https://<dominio>/descargas/Tatana-Portable-Windows-nube.zip` (P4). | Sí (P4) |
| **DT17** | **Mensaje `origin_not_allowed`:** si el front se compiló con `NEXT_PUBLIC_TATANA_DOWNLOAD_URL`, el texto dice cómo actualizar Tatana con ese link. Si no, queda el texto actual (la instalación local no cambia). | No |
| **DT18** | **Workflow de verificación** (`verificar.yml`) en PR a `develop`/`main`: `dotnet build` y `dotnet test` de los dos proyectos .NET, y `tsc` de `client/` y de `agent-ui/`. Es barato, y si `main` no compila el deploy falla en `imagenes` antes de tocar el VPS. | No |
| **DT19** | **Caddy:** imagen `caddy:2` con tag exacto (el implementador fija la `2.x.y` estable vigente). La API admin queda en el default (localhost dentro del contenedor, no publicada). Sin access log. Publica `80/tcp`, `443/tcp` y `443/udp` (HTTP/3). | No |
| **DT20** | **Memoria:** sin `mem_limit` por defecto en la nube. Mongo con `--wiredTigerCacheSizeGB ${FACTUM_MONGO_CACHE_GB:-0.5}`. La guía agrega 2 GB de swap. Con 4 GB de RAM sobra margen (P1). | Sí (P1) |

---

## 5. Contrato compartido

No cambia ningún DTO ni campo JSON entre `client/` y el backend, ni entre `client/` y Tatana. Lo que cruza lados son
**nombres de configuración y build args**, que tienen que coincidir exactamente.

### 5.1 Build args del frontend (front ↔ workflow ↔ deploy)

| Build arg (Dockerfile / `next.config.ts`) | Valor en producción | Lo fija | Lo lee |
|---|---|---|---|
| `NEXT_PUBLIC_BACKEND_URL` | `${{ vars.FACTUM_URL_PUBLICA }}` = `https://<dominio>` (sin `/` final) | `.github/workflows/desplegar-produccion.yml` | `client/src/lib/api.ts` L9, `client/src/app/dashboard/page.tsx` L45 (sin cambios) |
| `NEXT_PUBLIC_AGENT_URL` | `${{ vars.FACTUM_AGENT_URL \|\| 'http://localhost:8765' }}` | Ídem | `client/src/lib/agent.ts` L8, `useAgentConnection.ts`, `CaseFormStep.tsx` (sin cambios) |
| `NEXT_PUBLIC_TATANA_DOWNLOAD_URL` (**nuevo**, opcional, default `""`) | `${{ vars.FACTUM_TATANA_DESCARGA_URL }}`, p. ej. `https://<dominio>/descargas/Tatana-Portable-Windows-nube.zip` | Ídem; `client/Dockerfile` (`ARG`), `client/next.config.ts` (`env`) | `client/src/lib/agent-messages.ts` (caso `origin_not_allowed`) |

Además, la imagen del front lleva el label OCI `factum.url_publica=<NEXT_PUBLIC_BACKEND_URL>`, que pone el workflow y
lee `deploy/cloud/scripts/desplegar.sh` (DT2).

`deploy/windows/armar-paquete.sh` **no** pasa `NEXT_PUBLIC_TATANA_DOWNLOAD_URL`, así que vale `""` y el mensaje sigue
siendo el actual (D15).

### 5.2 `GET /health/ready` (backend ↔ monitor / script de deploy)

Ningún archivo de `client/` lo consume. Casing snake_case_lower (`Program.cs` L81-85); las claves son palabras simples.

```
200 {"status":"ok","mongo":"ok"}
503 {"status":"unavailable","mongo":"down"}
```

Lo leen `deploy/cloud/scripts/desplegar.sh` (por código HTTP) y UptimeRobot (por código HTTP). Ver
`server/src/Factum.Backend/Program.cs`.

### 5.3 Orígenes de Tatana (empaquetado ↔ Tatana ↔ web)

- Clave: `Agent:AllowedOrigins`, un array JSON en el `appsettings.json` que viaja al lado de `Factum.Agent.exe`:
  `{"Agent": {"AllowedOrigins": ["http://localhost:3000", "http://127.0.0.1:3000", "https://<dominio>", ...]}}`.
  Lo escribe `deploy/windows/hornear-origenes-tatana.py`, llamado por `armar-tatana-portable.sh --origenes`, y lo lee
  `server/src/Factum.Agent/Program.cs` (sin cambios).
- Formato de cada origen: igual que `OriginPolicy.TryNormalize` (`http(s)://host[:puerto]`, sin path, query, fragmento
  ni usuario, sin `*`). El script valida con una regla **más estricta**, de modo que todo lo que acepta el script lo
  acepta Tatana: `^https?://[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*(:[0-9]{1,5})?$`, después de
  pasar a minúsculas y sacar la `/` final.
- El origen de la web en la nube es `https://$FACTUM_DOMINIO`. Es el mismo valor que el backend pone en
  `Cors__AllowedOrigins__0` (compose) y que el workflow usa en `FACTUM_URL_PUBLICA`.

### 5.4 Variables del `.env` del VPS ↔ compose ↔ scripts

La lista canónica está en `deploy/cloud/factum.env.example` (§6.3). Los scripts y el compose usan **exactamente** esos
nombres.

### 5.5 Secrets y variables de GitHub ↔ workflow

Ver §6.6.

---

## 6. Especificación de archivos

### 6.1 `deploy/cloud/docker-compose.yml` (nuevo)

Reglas:

- `name: factum`.
- Imágenes de GHCR con `${FACTUM_TAG:?}`. **Solo Caddy publica puertos.**
- Redes `web` y `datos` (`internal: true`).
- Logging `local` con rotación.
- Datos en `${FACTUM_DATOS:-/srv/factum/datos}`.
- Ningún secreto literal: todos vienen del `--env-file`.

Esqueleto (el implementador completa y ajusta, sin cambiar las reglas):

```yaml
# Factum — compose de PRODUCCIÓN en la nube (VPS). Ver docs/despliegue-nube.md.
# Se usa SIEMPRE a través de deploy/cloud/scripts/*.sh (pasan --env-file y FACTUM_TAG).
# Reglas: solo caddy publica puertos; mongo en red interna; ningún secreto literal acá.
name: factum

x-logging: &logging
  driver: local
  options: { max-size: "10m", max-file: "5" }

services:
  caddy:
    image: caddy:2.x.y            # tag exacto (DT19)
    ports: ["80:80", "443:443", "443:443/udp"]
    environment:
      FACTUM_DOMINIO: ${FACTUM_DOMINIO:?Falta FACTUM_DOMINIO}
      FACTUM_ACME_EMAIL: ${FACTUM_ACME_EMAIL:?Falta FACTUM_ACME_EMAIL}
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - ./caddy-sitios:/etc/caddy/sitios:ro          # redirección del dominio anterior (§6.2), opcional
      - ${FACTUM_HOME:-/srv/factum}/descargas:/srv/descargas:ro
      - ${FACTUM_DATOS:-/srv/factum/datos}/caddy-data:/data
      - ${FACTUM_DATOS:-/srv/factum/datos}/caddy-config:/config
    networks: [web]
    depends_on: { frontend: { condition: service_healthy }, backend: { condition: service_healthy } }
    restart: unless-stopped
    logging: *logging

  mongo:
    image: mongo:7.0.43           # mismo tag que deploy/windows/docker-compose.yml
    command: ["mongod", "--wiredTigerCacheSizeGB", "${FACTUM_MONGO_CACHE_GB:-0.5}"]
    environment:
      MONGO_INITDB_ROOT_USERNAME: root
      MONGO_INITDB_ROOT_PASSWORD: ${FACTUM_MONGO_ROOT_PASSWORD:?Falta FACTUM_MONGO_ROOT_PASSWORD}
      FACTUM_MONGO_APP_PASSWORD: ${FACTUM_MONGO_APP_PASSWORD:?Falta FACTUM_MONGO_APP_PASSWORD}
    volumes:
      - ${FACTUM_DATOS:-/srv/factum/datos}/mongo:/data/db
      - ./mongo-init:/docker-entrypoint-initdb.d:ro
    networks: [datos]
    healthcheck:
      test: ["CMD", "mongosh", "--quiet", "--eval", "db.adminCommand('ping').ok"]
      interval: 10s
      timeout: 5s
      retries: 12
      start_period: 20s
    restart: unless-stopped
    logging: *logging

  backend:
    image: ${FACTUM_REGISTRO:?}/factum-backend:${FACTUM_TAG:?Falta FACTUM_TAG (usá los scripts)}
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      MongoDb__ConnectionString: mongodb://factum_app:${FACTUM_MONGO_APP_PASSWORD}@mongo:27017/factum?authSource=factum
      MongoDb__DatabaseName: factum
      Jwt__Secret: ${FACTUM_JWT_SECRET:?Falta FACTUM_JWT_SECRET}
      Jwt__ExpiryHours: ${FACTUM_JWT_EXPIRY_HOURS:-8}
      Auth__Mode: local
      Auth__AllowDevOutsideDevelopment: "false"
      Auth__Local__BootstrapSuperadmins__0__Dni: ${FACTUM_SUPERADMIN_0_DNI:-}
      Auth__Local__BootstrapSuperadmins__0__Name: ${FACTUM_SUPERADMIN_0_NOMBRE:-}
      Auth__Local__BootstrapSuperadmins__0__TemporaryPassword: ${FACTUM_SUPERADMIN_0_TEMPORAL:-}
      Auth__Local__BootstrapSuperadmins__1__Dni: ${FACTUM_SUPERADMIN_1_DNI:-}
      Auth__Local__BootstrapSuperadmins__1__Name: ${FACTUM_SUPERADMIN_1_NOMBRE:-}
      Auth__Local__BootstrapSuperadmins__1__TemporaryPassword: ${FACTUM_SUPERADMIN_1_TEMPORAL:-}
      Cors__AllowedOrigins__0: https://${FACTUM_DOMINIO}
      Storage__DataDirectory: /data
      Integrations__Support__Enabled: "false"
      TatanaUpdates__PublicBaseUrl: https://${FACTUM_DOMINIO}
    volumes:
      - ${FACTUM_DATOS:-/srv/factum/datos}/backend:/data
      # Branding global del login (opcional): ver la guía, sección 6.
      - ${FACTUM_HOME:-/srv/factum}/config/branding:/app/branding:ro
    networks: [web, datos]
    depends_on: { mongo: { condition: service_healthy } }
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://127.0.0.1:8080/health"]
      interval: 10s
      timeout: 5s
      retries: 6
      start_period: 40s
    restart: unless-stopped
    logging: *logging

  frontend:
    image: ${FACTUM_REGISTRO:?}/factum-frontend:${FACTUM_TAG:?}
    networks: [web]
    depends_on: { backend: { condition: service_healthy } }
    healthcheck:
      test: ["CMD", "wget", "-q", "--spider", "http://127.0.0.1:3000/"]
      interval: 10s
      timeout: 5s
      retries: 6
      start_period: 20s
    restart: unless-stopped
    logging: *logging

networks:
  web: {}
  datos: { internal: true }
```

Puntos que el implementador tiene que verificar:

- **Superadmins vacíos (A1).** Con `FACTUM_SUPERADMIN_*` vacías, después del primer ingreso, el binder deja un elemento
  con `Dni=""`. Hay que confirmar que `AuthSettingsResolver` / `LocalUserBootstrapper` ignora un elemento con DNI vacío
  y no da error de configuración. **Si da error**, se resuelve sin tocar código: se sacan esas líneas del compose y van
  en un archivo aparte (`deploy/cloud/compose.bootstrap.yml`) que `desplegar.sh` suma con `-f` solo si
  `FACTUM_SUPERADMIN_0_DNI` no está vacía. Lo que se elija se documenta en `progress/impl_backend_despliegue-nube.md`.
- **Carpeta de branding.** Si `config/branding` no existe, el bind mount la crearía como root. `preparar-servidor.sh` la
  crea vacía y con dueño `deploy`.
- **Branding global.** Los `Branding__*` globales (login) quedan como variables opcionales en el `.env`
  (`FACTUM_BRANDING_NOMBRE` y `FACTUM_BRANDING_LOGO`, ruta relativa a `/app`, p. ej. `branding/logo.png`) mapeadas en
  el compose. Si están vacías, rige el default de la app.
- **Usuario de Mongo.** `authSource=factum` porque `factum_app` se crea en la base `factum` (DT4).

### 6.2 `deploy/cloud/Caddyfile` (nuevo) y `deploy/cloud/caddy-sitios/`

```caddyfile
# Factum — proxy HTTPS de producción. Dominio: FACTUM_DOMINIO (.env del VPS). Sin access log a propósito:
# las descargas llevan ?token= en la URL (B10). Ver docs/despliegue-nube.md, sección "HTTPS".
{
	email {$FACTUM_ACME_EMAIL}
}

(seguridad) {
	header {
		Strict-Transport-Security "max-age=31536000"
		X-Content-Type-Options "nosniff"
		Referrer-Policy "strict-origin-when-cross-origin"
		X-Frame-Options "DENY"
		Content-Security-Policy "frame-ancestors 'none'"
		Content-Security-Policy-Report-Only "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: http://localhost:8765 http://127.0.0.1:8765; media-src 'self' blob: http://localhost:8765 http://127.0.0.1:8765; font-src 'self' data:; connect-src 'self' http://localhost:8765 ws://localhost:8765 http://127.0.0.1:8765 ws://127.0.0.1:8765; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'"
		Permissions-Policy "camera=(self), microphone=(self), geolocation=(), payment=(), usb=()"
		-Server
	}
}

{$FACTUM_DOMINIO} {
	import seguridad
	encode zstd gzip

	@backend path /api/* /health /health/* /tatana/updates/*
	handle @backend {
		request_body {
			max_size 300MB
		}
		reverse_proxy backend:8080
	}

	handle_path /descargas/* {
		root * /srv/descargas
		file_server
	}

	handle {
		reverse_proxy frontend:3000
	}
}

# Redirección de un dominio anterior (cambio de dominio, guía sección 15). Vacío salvo durante una transición.
import /etc/caddy/sitios/*.caddy
```

- `deploy/cloud/caddy-sitios/LEEME.md` explica el uso y trae el bloque modelo (`<dominio-anterior> {
  redir https://{$FACTUM_DOMINIO}{uri} permanent }`). Los `*.caddy` reales quedan ignorados por git (§6.10).
- **(a confirmar por el implementador)** Que `import` con un glob sin coincidencias no corte el arranque de Caddy en el
  tag elegido. Se verifica con `caddy validate` (§9.1). Si corta, se versiona un `caddy-sitios/00-vacio.caddy` con solo
  un comentario.
- La **CSP Report-Only** no tiene `report-uri`: las violaciones se ven en la consola del navegador durante la prueba
  manual (§9.3). Pasar a CSP efectiva es otra HU (D13).
- `Referrer-Policy` evita que un `?token=` viaje en el `Referer` a otro origen.

### 6.3 `deploy/cloud/factum.env.example` (nuevo; sin valores reales)

Una clave por línea, con un comentario corto. Los placeholders dicen `CAMBIAR`, para que `desplegar.sh` los detecte.

```
# Factum en la nube — copiá este archivo a /srv/factum/config/factum.env (chmod 600, dueño deploy).
# NUNCA lo subas al repo. Cómo generar cada secreto: docs/despliegue-nube.md, sección 6.
FACTUM_HOME=/srv/factum
FACTUM_DATOS=/srv/factum/datos
# Registro de imágenes (en minúsculas): ghcr.io/<dueño-del-repo>
FACTUM_REGISTRO=ghcr.io/CAMBIAR
# Dominio público, sin https:// ni barra. Provisorio o definitivo.
FACTUM_DOMINIO=CAMBIAR.duckdns.org
# Mail para Let's Encrypt (avisos de vencimiento).
FACTUM_ACME_EMAIL=CAMBIAR@ejemplo.com
# openssl rand -hex 64
FACTUM_JWT_SECRET=CAMBIAR
FACTUM_JWT_EXPIRY_HOURS=8
# openssl rand -hex 24 (cada una distinta)
FACTUM_MONGO_ROOT_PASSWORD=CAMBIAR
FACTUM_MONGO_APP_PASSWORD=CAMBIAR
FACTUM_MONGO_CACHE_GB=0.5
# Superadmins iniciales (solo el primer arranque; vaciar después del primer ingreso, guía sección 9).
FACTUM_SUPERADMIN_0_DNI=
FACTUM_SUPERADMIN_0_NOMBRE=
FACTUM_SUPERADMIN_0_TEMPORAL=
FACTUM_SUPERADMIN_1_DNI=
FACTUM_SUPERADMIN_1_NOMBRE=
FACTUM_SUPERADMIN_1_TEMPORAL=
# Branding global del login (opcional).
FACTUM_BRANDING_NOMBRE=
FACTUM_BRANDING_LOGO=
# Backup (guía sección 12). Destino rclone "remoto:bucket/carpeta"; config en /srv/factum/config/rclone.conf.
FACTUM_BACKUP_DESTINO=r2:CAMBIAR-bucket/factum
# Clave PÚBLICA de age (age1...). La privada NO va en este servidor.
FACTUM_BACKUP_AGE_RECIPIENT=age1CAMBIAR
FACTUM_BACKUP_LOCALES=3
# Ping de healthchecks.io (opcional; vacío = sin ping).
FACTUM_BACKUP_PING_URL=
```

### 6.4 `deploy/cloud/mongo-init/01-usuario-app.js` (nuevo)

Lo corre el entrypoint de `mongo:7` **solo con el datadir vacío**, conectado como root:

```js
// Crea el usuario de la aplicación (DT4). Solo corre la primera vez (volumen vacío).
const pwd = process.env.FACTUM_MONGO_APP_PASSWORD;
if (!pwd) throw new Error("Falta FACTUM_MONGO_APP_PASSWORD");
db.getSiblingDB("factum").createUser({ user: "factum_app", pwd, roles: [{ role: "readWrite", db: "factum" }] });
```

### 6.5 Scripts bash en `deploy/cloud/scripts/` (nuevos)

Reglas para todos los scripts:

- `#!/usr/bin/env bash` y `set -euo pipefail`.
- Mensajes en castellano; errores por stderr con el prefijo `ERROR:`.
- Pasan `shellcheck` sin warnings, o con los `disable` justificados en un comentario.
- Nunca imprimen secretos: el `.env` se carga con `set -a; source` y nunca se hace `echo` de una variable `*_SECRET`,
  `*_PASSWORD` o `*_TEMPORAL`.
- Todo `docker compose` pasa por `_comun.sh`.
- Variables de ruta: `FACTUM_ENV_FILE` (default `/srv/factum/config/factum.env`), `FACTUM_HOME`, `FACTUM_DATOS`.

| Script | Corre en / como | Qué hace |
|---|---|---|
| `_comun.sh` | (librería) | `cargar_env` carga el env y valida que existan las claves obligatorias. Rechaza cualquier valor que contenga `CAMBIAR`, un `FACTUM_JWT_SECRET` de menos de 32 caracteres y un `FACTUM_DOMINIO` con `://` o `/`. `compose()` corre `docker compose --project-directory $REPO/deploy/cloud -f $REPO/deploy/cloud/docker-compose.yml --env-file $FACTUM_ENV_FILE "$@"` con `FACTUM_TAG` exportado. `tag_actual()` lee `estado/desplegado`. `esperar_salud()` hace un poll del health de docker del backend (180 s) y después `curl -fsS --resolve "$FACTUM_DOMINIO:443:127.0.0.1" "https://$FACTUM_DOMINIO/health/ready"` y `.../` (60 s). `log()` antepone la fecha. |
| `compose.sh` | VPS / deploy | Wrapper para operar a mano con el tag desplegado: `compose.sh ps`, `compose.sh logs -f backend`, `compose.sh up -d backend`. |
| `preparar-servidor.sh` | VPS / **root**, una sola vez | Idempotente; pide confirmación en cada bloque. Ver la tabla siguiente. |
| `desplegar.sh` | VPS / deploy | `--tag sha-<40hex>` (normal), `--construir-local` (plan B: build en el VPS desde el checkout actual, tag `local-<sha>`) o `--estado`. Pasos en §6.5.1. |
| `deploy-forzado.sh` | VPS / deploy (copia en `/srv/factum/bin/`) | Lee `SSH_ORIGINAL_COMMAND`. `desplegar sha-<40hex>` (regex exacta) → `exec /srv/factum/repo/deploy/cloud/scripts/desplegar.sh --tag sha-…`. `estado` → `desplegar.sh --estado`. Cualquier otra cosa → `comando no permitido`, exit 2. Loguea con `logger -t factum-deploy`. |
| `revertir.sh` | VPS / deploy | Rollback manual al SHA de `estado/anterior` (mismo mecanismo que el automático). |
| `backup.sh` | VPS / deploy (cron) | Pasos en §6.5.2. `--solo-local --motivo <texto>` sirve para el backup pre-deploy. |
| `restaurar.sh` | VPS / deploy | Pasos en §6.5.3. |
| `verificar-informes.sh` | VPS / deploy | **Solo lectura.** Con `mongosh` (usuario `factum_app`) lista `{_id, pdf_filename, report_hash}` de los casos con `report_hash`. Por cada uno corre `sha256sum datos/backend/cases/<id>/<pdf_filename>` y reporta OK / DISTINTO / FALTA. Termina ≠ 0 si hay alguno distinto o faltante. Lo usan el simulacro de restore y D17. |

`preparar-servidor.sh`, bloque por bloque:

1. `apt` update y upgrade, `unattended-upgrades`.
2. Docker Engine y el plugin compose desde el repo apt oficial de Docker.
3. `ufw`: deny incoming; allow 22/tcp, 80/tcp, 443/tcp y 443/udp; enable.
4. Swap de 2 GB si no hay.
5. Usuario `admin`: sudo, con la clave pública que se pasa con `--clave-admin <archivo.pub>`.
6. Usuario `deploy`: sin sudo, en el grupo `docker`, sin contraseña.
7. Árbol `/srv/factum/{repo,config,config/branding,datos/{mongo,backend,caddy-data,caddy-config},descargas,backups,estado,bin}`
   con dueño `deploy`; `config` en 700. `datos/mongo` queda con dueño `999:999` (uid de mongo) o lo ajusta el
   entrypoint (el implementador lo verifica).
8. Copia `deploy-forzado.sh` a `/srv/factum/bin/` (lo toma de la misma carpeta que el script).
9. **Al final, y solo si `admin` tiene una clave cargada**, `sshd`: `PasswordAuthentication no`, `PermitRootLogin no`,
   y reload. Antes imprime "abrí OTRA terminal y probá `ssh admin@IP` antes de cerrar esta".
10. Imprime el chequeo de **AVX**: `grep -m1 -o avx /proc/cpuinfo`, porque Mongo 5+ lo exige en x86_64. Si falta,
    corta con un error claro.

#### 6.5.1 `desplegar.sh` paso a paso

1. Si no corre ya desde una copia, se copia junto con `_comun.sh` a `mktemp -d` y hace `exec` (DT9).
2. `flock -n /srv/factum/estado/.lock`. Si está tomado: "hay otro deploy en curso", exit 3.
3. `cargar_env`. Chequea que queden al menos 2 GB libres en `FACTUM_DATOS`.
4. `git -C $REPO fetch --quiet origin main`. Exige `git merge-base --is-ancestor <sha> origin/main` (DT7) y después
   `git checkout --quiet --detach <sha>`. Con `--construir-local` usa el `HEAD` actual.
5. `FACTUM_TAG=sha-<sha>`. `compose pull backend frontend` (con `--construir-local`: `docker build` de las dos imágenes
   con los build args derivados de `FACTUM_DOMINIO` y el label `factum.url_publica`).
6. **Guarda de dominio (DT2):** `docker image inspect --format '{{ index .Config.Labels "factum.url_publica" }}'` de la
   imagen del front tiene que ser `https://$FACTUM_DOMINIO`. Si no coincide: error ("el front se compiló para X; revisá
   la variable FACTUM_URL_PUBLICA en GitHub"), vuelve al checkout anterior y sale **sin tocar lo que corre**.
7. Si Mongo está corriendo: `backup.sh --solo-local --motivo pre-deploy-<sha8>`.
8. `compose up -d --remove-orphans`.
9. `esperar_salud`. Si da bien: `estado/anterior` ← `estado/desplegado`, `estado/desplegado` ← `<sha>`. Borra las
   imágenes `factum-*` que no sean de `desplegado` ni de `anterior`. Imprime `OK desplegado <sha8>`. Exit 0.
10. Si da mal: imprime las últimas 80 líneas de `compose logs backend` (el fail-fast del backend no imprime secretos).
    Si hay `desplegado` previo, hace checkout de ese SHA, `FACTUM_TAG=sha-<prev>`, `compose up -d` y `esperar_salud`, e
    imprime `ROLLBACK a <prev8> OK` (exit 1) o `ROLLBACK FALLÓ` (exit 2). Si no hay previo (primer arranque), exit 1 y
    deja los contenedores como están, para diagnosticar.

#### 6.5.2 `backup.sh` paso a paso

1. `cargar_env`; `flock` propio.
2. Si el uso del disco es ≥ 85 %, avisa (ping a `$FACTUM_BACKUP_PING_URL/fail` con el mensaje) y sigue.
3. En un temporal dentro de `FACTUM_HOME/backups/.tmp`:
   - `compose exec -T mongo mongodump --username root --password "$FACTUM_MONGO_ROOT_PASSWORD" --authenticationDatabase admin --db factum --archive --gzip > mongo.archive.gz`
     (sin `-it`; la contraseña nunca aparece en ningún log del script);
   - `tar -C $FACTUM_DATOS/backend --exclude=.upload-tmp --exclude=.generate-tmp -czf docx.tar.gz .`;
   - `sha256sum * > SHA256SUMS`;
   - `version.txt` con el SHA desplegado.
4. `tar -cf - . | age -r "$FACTUM_BACKUP_AGE_RECIPIENT" > factum-<AAAAMMDD-HHMM>[-motivo].tar.age`. Requiere `age`
   instalado en el host (`apt install age`, lo hace `preparar-servidor.sh`).
5. Deja el archivo en `FACTUM_HOME/backups/` y conserva los `FACTUM_BACKUP_LOCALES` más nuevos. Los `pre-deploy`
   cuentan aparte y se conservan los 3 últimos.
6. Salvo con `--solo-local`:
   - `rclone --config /srv/factum/config/rclone.conf copy <archivo> $FACTUM_BACKUP_DESTINO/diarios/`;
   - los domingos, también a `semanales/`;
   - `rclone delete --min-age 8d .../diarios/` y `--min-age 29d .../semanales/`.
7. Si todo salió bien, ping a `$FACTUM_BACKUP_PING_URL`. Ante cualquier error (`trap ERR`), ping a `/fail` y exit ≠ 0.
8. **Cron** (lo instala la guía a mano con `crontab -e` del usuario deploy, no el script):
   `30 3 * * * /srv/factum/repo/deploy/cloud/scripts/backup.sh >> /srv/factum/backups/backup.log 2>&1`.
   El log se rota con un `logrotate` de usuario, o el script lo trunca a 1 MB.

#### 6.5.3 `restaurar.sh` paso a paso

1. Argumentos: `--archivo <local.tar.age>` o `--remoto <nombre>` (lo baja con rclone), más `--identidad <archivo de
   clave privada age>`, que el usuario sube temporalmente y el script **borra con `shred -u` al terminar** salvo con
   `--conservar-identidad`.
2. Descifra en un temporal y verifica `SHA256SUMS`.
3. **Guarda:** si la base `factum` ya tiene colecciones con documentos, se niega salvo con `--reemplazar`, que además
   pide escribir el dominio para confirmar. En producción se usa sobre un host nuevo y vacío. **Nunca** se apunta a la
   Mongo de desarrollo.
4. `compose stop backend frontend`. `mongorestore --drop --archive --gzip --nsInclude 'factum.*'` como root. Extrae
   `docx.tar.gz` en `FACTUM_DATOS/backend`.
5. `compose up -d`, `esperar_salud`, `verificar-informes.sh`. Imprime el resumen.

### 6.6 `.github/workflows/desplegar-produccion.yml` (nuevo)

```yaml
name: Desplegar producción
on:
  push:
    branches: [main]
  workflow_dispatch: {}
concurrency:
  group: despliegue-produccion
  cancel-in-progress: false
permissions:
  contents: read
env:
  REGISTRO: ghcr.io/${{ github.repository_owner }}   # el dueño ya está en minúsculas (senkuch4n)

jobs:
  imagenes:
    runs-on: ubuntu-24.04
    timeout-minutes: 30
    permissions: { contents: read, packages: write }
    steps:
      - uses: actions/checkout@<sha fijado>
      - name: Validar variables
        # FACTUM_URL_PUBLICA obligatoria, ^https://[^/]+$ ; si falta, error con instrucción.
      - uses: docker/setup-buildx-action@<sha>
      - uses: docker/login-action@<sha>   # ghcr.io, github.actor, secrets.GITHUB_TOKEN
      - uses: docker/build-push-action@<sha>   # backend
        with:
          context: server/src/Factum.Backend
          platforms: linux/amd64
          push: true
          provenance: false
          tags: |
            ${{ env.REGISTRO }}/factum-backend:sha-${{ github.sha }}
            ${{ env.REGISTRO }}/factum-backend:latest
          labels: org.opencontainers.image.source=${{ github.server_url }}/${{ github.repository }}
          cache-from: type=gha,scope=backend
          cache-to: type=gha,mode=max,scope=backend
      - uses: docker/build-push-action@<sha>   # frontend
        with:
          context: client
          platforms: linux/amd64
          push: true
          provenance: false
          build-args: |
            NEXT_PUBLIC_BACKEND_URL=${{ vars.FACTUM_URL_PUBLICA }}
            NEXT_PUBLIC_AGENT_URL=${{ vars.FACTUM_AGENT_URL || 'http://localhost:8765' }}
            NEXT_PUBLIC_TATANA_DOWNLOAD_URL=${{ vars.FACTUM_TATANA_DESCARGA_URL }}
          tags: |
            ${{ env.REGISTRO }}/factum-frontend:sha-${{ github.sha }}
            ${{ env.REGISTRO }}/factum-frontend:latest
          labels: |
            org.opencontainers.image.source=${{ github.server_url }}/${{ github.repository }}
            factum.url_publica=${{ vars.FACTUM_URL_PUBLICA }}
          cache-from: type=gha,scope=frontend
          cache-to: type=gha,mode=max,scope=frontend

  desplegar:
    needs: imagenes
    if: vars.FACTUM_DEPLOY_HABILITADO == 'true'
    runs-on: ubuntu-24.04
    timeout-minutes: 20
    environment:
      name: production          # DT12 / P3: aprobación manual solo si el plan lo permite
      url: ${{ vars.FACTUM_URL_PUBLICA }}
    steps:
      - name: Preparar SSH
        # umask 077; secrets.DEPLOY_SSH_KEY -> ~/.ssh/deploy_key ; secrets.DEPLOY_KNOWN_HOSTS -> ~/.ssh/known_hosts
      - name: Desplegar
        run: >
          ssh -i ~/.ssh/deploy_key -p "${{ vars.DEPLOY_PORT || '22' }}"
          -o StrictHostKeyChecking=yes -o UserKnownHostsFile=~/.ssh/known_hosts -o BatchMode=yes
          -o ConnectTimeout=20 -o ServerAliveInterval=30
          "${{ vars.DEPLOY_USER || 'deploy' }}@${{ secrets.DEPLOY_HOST }}" "desplegar sha-${{ github.sha }}"
      - name: Chequeo externo
        run: curl -fsS --retry 6 --retry-delay 10 --retry-all-errors "${{ vars.FACTUM_URL_PUBLICA }}/health/ready"
      - name: Limpiar clave
        if: always()
        run: rm -f ~/.ssh/deploy_key
```

- Las actions se fijan por **SHA de commit** (con la versión en un comentario).
- Los valores de `vars.*` / `secrets.*` se pasan a `run:` por `env:` y no se interpolan dentro del script, para evitar
  inyección. El esqueleto de arriba los interpola por brevedad; el implementador lo corrige.
- Ningún paso imprime los secrets.

**Secrets y variables (todo a nivel de repositorio, DT12):**

| Nombre | Tipo | Valor |
|---|---|---|
| `DEPLOY_SSH_KEY` | secret | Clave privada ed25519 dedicada (guía §11) |
| `DEPLOY_KNOWN_HOSTS` | secret | Línea `ssh-keyscan -t ed25519 <host>`, verificada contra la huella de la consola |
| `DEPLOY_HOST` | secret | IP o hostname del VPS |
| `DEPLOY_PORT` | variable | `22` (opcional) |
| `DEPLOY_USER` | variable | `deploy` (opcional) |
| `FACTUM_URL_PUBLICA` | variable | `https://<dominio>` |
| `FACTUM_AGENT_URL` | variable | opcional; default `http://localhost:8765` |
| `FACTUM_TATANA_DESCARGA_URL` | variable | opcional; `https://<dominio>/descargas/Tatana-Portable-Windows-nube.zip` |
| `FACTUM_DEPLOY_HABILITADO` | variable | `true` recién después del primer arranque manual |

### 6.7 `.github/workflows/verificar.yml` (nuevo, DT18)

- Disparo: `pull_request` a `develop` y `main`, más `workflow_dispatch`. `permissions: contents: read`.
- Job .NET (`actions/setup-dotnet` 10.x): `dotnet build` de `Factum.Backend.csproj` y `Factum.Agent.csproj`, y
  `dotnet test` de `server/tests/Factum.Backend.Tests` y `server/tests/Factum.Agent.Tests`.
- Si algún test necesita Mongo y no se saltea solo sin él, se excluye con `--filter` y se anota en el impl. **No** se
  levanta una Mongo de servicio en esta HU.
- Job Node 22: `npm ci` y `npx tsc --noEmit` en `client/`, y `npm ci` más los dos `tsc` en `agent-ui/`.

### 6.8 Tatana: `deploy/windows/armar-tatana-portable.sh` (cambio) + `deploy/windows/hornear-origenes-tatana.py` (nuevo) + `deploy/cloud/armar-tatana-nube.sh` (nuevo)

**`armar-tatana-portable.sh`**: dos flags **opcionales**. Sin ninguno de los dos, el zip resultante es byte a byte
equivalente en contenido al de hoy (D15).

- `--origenes "<o1>[,<o2>...]"`: después del paso 2 (publish) y del segundo `verificar_mock`, corre
  `python3 "$DIR_SCRIPT/hornear-origenes-tatana.py" "$OUT/appsettings.json" <o1> <o2>...`. El script se busca junto a
  `armar-tatana-portable.sh`, no en `--src`, para que funcione aunque el `--src` sea de un commit anterior.
- `--client-url <url>`: en el paso 7, el `.ini` sale con `CLIENT_URL=<url>` y un comentario de "Tatana para Factum en
  la nube", en lugar del bloque local actual. `UPDATE_URL` sigue vacío (D8b). `--client-url` sin `--origenes` es error
  (no tiene sentido abrir una web que Tatana no acepta).
- El paso 7 sigue escribiendo el `.ini` en CRLF.
- Se agrega al chequeo final: si se pasó `--origenes`, se lee el `appsettings.json` del zip
  (`unzip -p "$ZIP" appsettings.json`) y se verifica que la lista sea la esperada.

**`hornear-origenes-tatana.py <appsettings.json> <origen>...`**:

1. Normaliza cada origen (`strip`, minúsculas, sin `/` final).
2. Valida con la regex de §5.3 y rechaza `*`. Si alguno no pasa: exit 2 con el valor.
3. Arma `Agent.AllowedOrigins = ["http://localhost:3000", "http://127.0.0.1:3000"] + orígenes`, deduplicado y
   conservando el orden.
4. Reescribe el JSON en UTF-8 (sin BOM) con indentación de 2 espacios, sin tocar las otras claves.
5. Imprime la lista final.
6. Tiene `--probar`, un autotest con casos válidos e inválidos, para la verificación (§9.1).

**`deploy/cloud/armar-tatana-nube.sh --version X.Y.Z --origenes "<o1>[,...]" [--ref main] [--salida <dir>]`** (corre
en la Mac):

1. Hace `git archive` del `--ref` (default `origin/main`, después de un `git fetch`) a un temporal.
2. Llama a `deploy/windows/armar-tatana-portable.sh --version … --src <tmp> --salida <tmp-salida> --origenes … --client-url <primer origen>`.
3. Renombra la salida a `Tatana-Portable-vX.Y.Z-Windows-nube.zip` y escribe `.sha256` al lado.
4. Imprime el `scp` para subirla a `/srv/factum/descargas/Tatana-Portable-Windows-nube.zip`, con nombre estable para
   el link (vía `admin` + `sudo install -o deploy`).

**Opcional:** en `server/tests/Factum.Agent.Tests/OriginPolicyTests.cs`, un `[Fact]` que parsea
`["http://localhost:3000","http://127.0.0.1:3000","https://factum-piloto.duckdns.org"]` sin errores. Documenta que la
lista horneada es válida para Tatana.

### 6.9 Backend: `GET /health/ready`

- **`server/src/Factum.Backend/Infrastructure/MongoHealthProbe.cs`** (nuevo):
  - `public interface IMongoHealthProbe { Task<bool> PingAsync(CancellationToken ct); }`
  - `public sealed class MongoHealthProbe : IMongoHealthProbe`. El constructor recibe `IOptions<MongoOptions>` y
    `ILogger<MongoHealthProbe>`. Arma su propio `MongoClient` con
    `MongoClientSettings.FromConnectionString(cs)`, `ServerSelectionTimeout = 2s`, `ConnectTimeout = 2s` y
    `SocketTimeout = 3s`.
  - `PingAsync`: `RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1))` sobre `DatabaseName`, con un
    `CancellationTokenSource` enlazado a 3 s.
  - Si el ping falla, devuelve `false` y loguea un Warning con el **tipo** de excepción, **sin** el mensaje (el mensaje
    puede incluir el host o el usuario). Rate limit simple: como mucho un warning por minuto.
  - Es pura respecto de las respuestas HTTP: el mapeo va en una función estática testeable
    `HealthReady.ToResult(bool mongoOk)` que devuelve `(int StatusCode, object Body)`.
- **`Program.cs`**:
  - `builder.Services.AddSingleton<IMongoHealthProbe, MongoHealthProbe>();`
  - Después del `MapGet("/health", ...)` (que **no** cambia):
    ```csharp
    // despliegue-nube DT3: readiness para el monitor externo y el script de deploy. /health sigue siendo liveness.
    app.MapGet("/health/ready", async (IMongoHealthProbe probe, CancellationToken ct) =>
    {
        var (code, body) = HealthReady.ToResult(await probe.PingAsync(ct));
        return Results.Json(body, statusCode: code);
    });
    ```
  - Verificar que `Results.Json` usa las opciones de `ConfigureHttpJsonOptions`. Las claves (`status`, `mongo`) son
    iguales en cualquier casing, así que no hay riesgo de contrato.
- **`server/tests/Factum.Backend.Tests/HealthReadyTests.cs`** (nuevo):
  - `ToResult(true)` → 200, `status=ok`, `mongo=ok`; `ToResult(false)` → 503, `status=unavailable`, `mongo=down`
    (serializando con `SnakeCaseLower`).
  - `MongoHealthProbe` contra `mongodb://127.0.0.1:1` devuelve `false` en menos de 5 s. **No toca ninguna base real.**

### 6.10 Otros archivos

- **`.gitignore`**: agregar
  ```
  # Despliegue en la nube: secretos y sitios locales de Caddy nunca al repo
  deploy/cloud/*.env
  !deploy/cloud/factum.env.example
  deploy/cloud/caddy-sitios/*.caddy
  ```
  (Si se versiona `00-vacio.caddy`, agregar la excepción correspondiente.)
- **`deploy/cloud/LEEME.md`**: mapa corto de la carpeta, con un link a `docs/despliegue-nube.md`.
- **`README.md`**:
  - En "Puesta en marcha", un link a `docs/despliegue-nube.md` ("Despliegue en la nube").
  - En la tabla del backend, una fila para `GET /health/ready`.
  - En la tabla de `client/.env.local`, la fila de `NEXT_PUBLIC_TATANA_DOWNLOAD_URL`. Esa fila la agrega el
    implementer-frontend; el resto, el backend.
  - Sacar la mención a GitLab como único CI si está en el README; el CI de Tatana sigue siendo GitLab, pero el deploy
    ahora es GitHub Actions.
- **`client/Dockerfile`**: `ARG NEXT_PUBLIC_TATANA_DOWNLOAD_URL=` y sumarlo al `ENV` del stage `builder`.
- **`client/next.config.ts`**: en `env`, `NEXT_PUBLIC_TATANA_DOWNLOAD_URL: process.env.NEXT_PUBLIC_TATANA_DOWNLOAD_URL || ""`.
- **`client/src/lib/agent-messages.ts`**: el caso `origin_not_allowed` pasa a:
  ```ts
  case "origin_not_allowed": {
    const url = process.env.NEXT_PUBLIC_TATANA_DOWNLOAD_URL;
    return url
      ? `El Tatana de esta PC no está habilitado para esta dirección. Descargá e instalá la versión actual desde ${url} y reintentá.`
      : "Tatana no acepta pedidos desde esta página. Pedí que agreguen esta dirección a la configuración de Tatana (Agent:AllowedOrigins).";
  }
  ```
  El implementer-frontend revisa si el texto se muestra en un componente que pueda convertir la URL en link. Si no lo
  hay, queda como texto: no se crean componentes nuevos en esta HU. También revisa el texto del permiso de red local
  (`agent-messages.ts` L16) contra la versión estable de Chrome/Edge (T2) y lo ajusta solo si nombra mal la opción.

---

## 7. Estructura de `docs/despliegue-nube.md` (la escribe `implementer-backend`)

Convenciones de la guía:

- Cada paso lleva una etiqueta: **[A mano]** (lo hace el usuario en un panel, en su Mac o en el VPS) o **[Repo]** (lo
  hace un script o un archivo versionado; el usuario solo lo ejecuta).
- Los pasos del panel de DonWeb que no se verificaron llevan "*(a confirmar en el panel)*".
- Todo límite o precio de un tercero lleva "(a confirmar, consultado el AAAA-MM-DD)", con la fecha en que el
  implementador lo leyó en la fuente oficial. **No se inventan precios.**
- Los comandos van en bloques copiables, con los placeholders en `<MAYÚSCULAS>`.
- Ningún valor real (IP, dominio, DNI) en la guía.

| § | Sección | Contenido mínimo |
|---|---|---|
| 0 | Qué vas a tener al final | Diagrama ASCII: navegador → Caddy (443) → frontend/backend → Mongo (red interna); PC del perito con Tatana en `localhost:8765`; GitHub Actions → GHCR → SSH → VPS; backup → R2; UptimeRobot → `/health/ready`. Tabla "[A mano] vs [Repo]" (la de la HU, actualizada). Tiempo estimado. |
| 1 | Antes de empezar | Cuentas: DonWeb; GitHub (ya existe); DuckDNS (si hace falta); Cloudflare (R2); UptimeRobot; healthchecks.io. Herramientas en la Mac: `ssh`, `age` (`brew install age`), `gh`, `nmap` (opcional). Datos a decidir fuera del chat: DNI y nombre de los dos superadmins, mail(s) de alertas, mail para Let's Encrypt. Aviso: nada de esto va al repo ni al chat. |
| 2 | Contratar el Cloud Server en DonWeb | Plan recomendado (P1): mínimo 2 vCPU / 4 GB RAM / 40 GB SSD; el mínimo absoluto es 2 GB + swap, y en ese caso no se puede usar el plan B de build local. Sistema: Ubuntu 24.04 LTS x86_64. Datacenter en Argentina. Cargar la clave SSH pública al crear, si el panel lo permite *(a confirmar en el panel)*. Anotar la IP pública y el hostname asignado *(a confirmar en el panel)*. Firewall del panel, si existe: solo 22/80/443 *(a confirmar en el panel)*. Cómo leer la huella de la host key desde la consola web del panel, para fijarla después (§11). |
| 3 | Primer acceso y preparación | Generar tu clave (`ssh-keygen -t ed25519`). `scp deploy/cloud/scripts/preparar-servidor.sh deploy/cloud/scripts/deploy-forzado.sh root@<IP>:/root/`. Correr `preparar-servidor.sh --clave-admin` y explicar qué hace cada bloque. **Probar `ssh admin@<IP>` en otra terminal antes de cerrar la de root.** Chequeo de AVX y `uname -m` = `x86_64`. A partir de acá se opera como `admin` y `sudo -iu deploy`. |
| 4 | Dominio provisorio | **A)** el hostname de DonWeb: verificar con `dig +short <hostname>` que resuelve a la IP *(a confirmar en el panel si DonWeb asigna uno)*. **B)** DuckDNS: entrar, crear el subdominio, poner la IP y verificar con `dig`. Elegir uno. Por qué no conviene cambiarlo seguido (Tatana, §15). |
| 5 | Repo e imágenes en el servidor | Como `deploy`: `ssh-keygen` de la deploy key → GitHub › Settings › Deploy keys, **sin** "Allow write access" → `git clone` en `/srv/factum/repo`. PAT classic con **solo `read:packages`** → `docker login ghcr.io -u <usuario>`. Aviso: queda en `~/.docker/config.json`; cómo rotarlo. |
| 6 | Configuración (`factum.env`) | `cp deploy/cloud/factum.env.example /srv/factum/config/factum.env && chmod 600`. Tabla con cada clave: qué es y cómo se genera (`openssl rand -hex 64` para el JWT, `-hex 24` para Mongo). Superadmins: contraseña temporal de ≥ 10 caracteres. Branding opcional (subir el logo a `config/branding/`). Qué pasa si falta algo: `desplegar.sh` lo dice antes de arrancar. |
| 7 | Primer arranque | En GitHub, cargar la variable `FACTUM_URL_PUBLICA` (todavía **no** `FACTUM_DEPLOY_HABILITADO`). Correr "Desplegar producción" con *Run workflow* y anotar el SHA. En el VPS: `desplegar.sh --tag sha-<SHA>`. Qué se ve: Mongo inicializa, Caddy pide el certificado y aparece `OK desplegado`. Plan B si GHCR o Actions fallan: `desplegar.sh --construir-local`. Si el certificado no sale: DNS, puertos 80/443, `compose.sh logs caddy`. |
| 8 | Verificar HTTPS y superficie expuesta | Candado en el navegador; `curl -I http://<dominio>` (308 a https); `curl -I https://<dominio>` (los headers de D13); `nmap -Pn -p- <IP>` desde la Mac (solo 22/80/443); `curl https://<dominio>/swagger` (no la API); `curl https://<dominio>/health/ready`. |
| 9 | Primer ingreso de los superadmins y alta de clientes | Login con DNI + temporal → cambio obligatorio → `/admin/cuentas` → alta de un cliente (el panel arma el mensaje con la dirección). Después: vaciar `FACTUM_SUPERADMIN_*` en `factum.env` y `compose.sh up -d backend`. |
| 10 | Tatana para la nube | En la Mac: `deploy/cloud/armar-tatana-nube.sh --version X.Y.Z --origenes https://<dominio>[,https://<definitivo>]`. Subirlo a `/srv/factum/descargas/`. Cargar `FACTUM_TATANA_DESCARGA_URL` en GitHub (se ve en el próximo deploy). En la PC del perito: descomprimir → `install-portable.bat` → abre la web. **Permiso de red local** en Chrome/Edge: qué aceptar y cómo revertir un "Bloquear" (candado › Configuración del sitio). Navegadores soportados (D10): Chrome y Edge actualizados en Windows; Firefox debería andar; Safari no está soportado. Escape por PC: `appsettings.Local.json` al lado del `.exe` (con ejemplo) y por qué **no** usar variables de entorno (arrays por índice). |
| 11 | Deploy automático (D9) | 1) Clave dedicada en la Mac: `ssh-keygen -t ed25519 -f factum-ci -C factum-ci -N ""`. 2) En el VPS, en `~deploy/.ssh/authorized_keys`, la línea con `command="/srv/factum/bin/deploy-forzado.sh",no-port-forwarding,no-X11-forwarding,no-agent-forwarding,no-pty <clave pública>`. 3) `ssh-keyscan -t ed25519 <IP>` y **comparar la huella** con la de la consola del panel (§2). 4) GitHub › Settings › Secrets and variables › Actions: cargar la tabla de §6.6. 5) Environment `production` con aprobación, si el plan lo permite (P3). 6) Probar: `ssh -i factum-ci deploy@<IP> estado` (responde el estado) y `ssh -i factum-ci deploy@<IP> ls` (rechazado). 7) `FACTUM_DEPLOY_HABILITADO=true` → *Run workflow* → seguir el log. 8) Qué pasa al mergear `develop` → `main`. 9) Si queda en rojo: leer el paso "Desplegar"; `ROLLBACK a … OK` significa que producción sigue con la versión anterior. 10) Rollback manual: `revertir.sh`. 11) Desactivar el automático: `FACTUM_DEPLOY_HABILITADO=false`. Aviso sobre el grupo `docker` (DT8) y cómo revocar la clave de CI. Consumo de minutos de Actions en un repo privado *(a confirmar)*. |
| 12 | Backups | 1) Cloudflare: crear un bucket R2 y un token de API **limitado a ese bucket** (Object Read & Write) (P2; capa gratis y requisito de tarjeta *a confirmar*). 2) En la Mac: `age-keygen -o factum-backup.key`; guardar **dos copias offline** (p. ej. gestor de contraseñas y pendrive); la clave pública va a `FACTUM_BACKUP_AGE_RECIPIENT`. **Sin la clave privada no hay restauración.** 3) En el VPS: `rclone config` con un remoto `r2` tipo S3/Cloudflare en `/srv/factum/config/rclone.conf` (600). 4) healthchecks.io: crear el check diario y poner la URL en `FACTUM_BACKUP_PING_URL`. 5) Primera corrida a mano de `backup.sh` y verificar en R2. 6) `crontab -e` con la línea de §6.5.2. 7) Retención y los backups pre-deploy. |
| 13 | Restauración y simulacro | Cuándo restaurar (VPS perdido, error de datos después de un deploy). Simulacro en un **VPS nuevo y vacío**: secciones 2-6, `restaurar.sh --remoto … --identidad …`, `verificar-informes.sh` en OK y login con las contraseñas actuales. Restaurar un `pre-deploy` local. Borrar el VPS del simulacro. Hacerlo una vez antes de tener clientes y anotarlo. |
| 14 | Monitoreo y logs | UptimeRobot: monitor HTTP(s) contra `https://<dominio>/health/ready`, cada 5 min, alertas a los mails (límites *a confirmar*). healthchecks.io para el backup. Logs: `compose.sh logs --tail 200 backend`. Rotación: 10 MB × 5 por contenedor. Disco: `df -h /srv`. Qué **no** hay en los logs (tokens, contraseñas) y por qué no hay que activar el access log de Caddy. |
| 15 | Cambio de dominio | **Fase A** (cuando se conozca el definitivo, sin apuro): `armar-tatana-nube.sh --origenes https://<provisorio>,https://<definitivo>` → redistribuir; los peritos siguen trabajando porque el provisorio sigue en la lista. **Fase B** (cuando las PCs ya tienen el Tatana nuevo): DNS del definitivo → en el VPS, `FACTUM_DOMINIO=<definitivo>` → en GitHub, `FACTUM_URL_PUBLICA=https://<definitivo>` → *Run workflow* (la guarda DT2 exige que los dos coincidan) → crear `deploy/cloud/caddy-sitios/anterior.caddy` con la redirección → `compose.sh up -d caddy`. **Fase C** (semanas después): sacar la redirección y armar Tatana solo con el definitivo en la próxima versión. Qué pasa con una PC que no actualizó (mensaje `origin_not_allowed` con el link). El renombre del producto es otra HU. |
| 16 | Problemas frecuentes y rotación de secretos | Backend que no arranca (fail-fast: `compose.sh logs backend`, mensajes típicos); certificado que no sale; disco lleno; Mongo que no responde; deploy en rojo. Rotar: `Jwt:Secret` (cierra todas las sesiones); la contraseña de Mongo de `factum_app` y de root (`db.changeUserPassword` + env + `up -d`); el PAT de GHCR; la deploy key; la clave de CI; el token de R2; la clave age (las copias viejas necesitan la clave vieja: no borrarla). Actualizaciones del SO y reinicio del VPS (los contenedores vuelven solos por `restart: unless-stopped`). |
| 17 | Plan de salida (D14) | Señales: RAM o disco al 80 %, CPU sostenida, caídas. Cómo agrandar el plan en DonWeb *(a confirmar en el panel)* o mudarse a otro VPS: secciones 2-7 + 13. Objetivo: horas. |
| 18 | Datos personales | Los datos de producción quedan en Argentina (DonWeb, D6). Los backups cifrados quedan en R2 (fuera del país, cifrados con una clave que solo tiene el usuario): decisión P2. Sin asesoramiento legal. |

---

## 8. Checklist atómico

### 8.1 `implementer-backend` (`server/`, `deploy/`, `packaging/`, `.github/`, docs)

**Backend (API)**
- [ ] B1. Crear `Infrastructure/MongoHealthProbe.cs` (`IMongoHealthProbe`, `MongoHealthProbe`, `HealthReady.ToResult`) según §6.9.
- [ ] B2. Registrar el probe y mapear `GET /health/ready` en `Program.cs`, sin tocar `/health`.
- [ ] B3. Crear `server/tests/Factum.Backend.Tests/HealthReadyTests.cs` (mapeo + probe contra `127.0.0.1:1`).
- [ ] B4. Verificar A1 (superadmins con DNI vacío). Si da error, usar la variante `compose.bootstrap.yml` (§6.1) y documentarlo en el impl.
- [ ] B5. (Opcional) `[Fact]` en `server/tests/Factum.Agent.Tests/OriginPolicyTests.cs` con la lista horneada.

**Empaquetado de Tatana**
- [ ] T1. Crear `deploy/windows/hornear-origenes-tatana.py` con `--probar` (§6.8).
- [ ] T2. Agregar `--origenes` y `--client-url` a `armar-tatana-portable.sh`, más la verificación en el zip final. Sin flags, mismo resultado que hoy.
- [ ] T3. Crear `deploy/cloud/armar-tatana-nube.sh`.

**Infraestructura**
- [ ] I1. `deploy/cloud/docker-compose.yml` (§6.1), con el tag exacto de Caddy.
- [ ] I2. `deploy/cloud/Caddyfile` + `deploy/cloud/caddy-sitios/LEEME.md` (§6.2). Resolver el glob vacío.
- [ ] I3. `deploy/cloud/factum.env.example` (§6.3).
- [ ] I4. `deploy/cloud/mongo-init/01-usuario-app.js` (§6.4).
- [ ] I5. `deploy/cloud/scripts/_comun.sh` y `compose.sh`.
- [ ] I6. `deploy/cloud/scripts/preparar-servidor.sh`.
- [ ] I7. `deploy/cloud/scripts/desplegar.sh` (§6.5.1), con la copia previa, el lock, la guarda de ancestro, la guarda de dominio, el backup pre-deploy, la salud y el rollback.
- [ ] I8. `deploy/cloud/scripts/deploy-forzado.sh` y `revertir.sh`.
- [ ] I9. `deploy/cloud/scripts/backup.sh` (§6.5.2).
- [ ] I10. `deploy/cloud/scripts/restaurar.sh` (§6.5.3) y `verificar-informes.sh`.
- [ ] I11. `chmod +x` en todos los `.sh`. Dejar en `.gitattributes` (raíz o `deploy/cloud/.gitattributes`) `*.sh text eol=lf`.
- [ ] I12. `deploy/cloud/LEEME.md`.
- [ ] I13. Reglas nuevas en `.gitignore` (§6.10).

**CI/CD**
- [ ] C1. `.github/workflows/desplegar-produccion.yml` (§6.6): actions fijadas por SHA, valores por `env:`, `concurrency`, condición `FACTUM_DEPLOY_HABILITADO`.
- [ ] C2. `.github/workflows/verificar.yml` (§6.7).

**Documentación**
- [ ] D1. `docs/despliegue-nube.md` con las 19 secciones de §7, las etiquetas [A mano]/[Repo] y los "a confirmar" con fecha.
- [ ] D2. `README.md`: link a la guía y fila de `/health/ready` (§6.10).
- [ ] D3. Escribir `progress/impl_backend_despliegue-nube.md`: archivos tocados, resultados de §9.1, cómo se resolvieron A1 y el glob de Caddy, los límites de terceros consultados (con fecha) y lo que queda para la prueba manual.

### 8.2 `implementer-frontend` (solo `client/`)

- [ ] F1. `client/Dockerfile`: `ARG NEXT_PUBLIC_TATANA_DOWNLOAD_URL=` y sumarlo al `ENV` del `builder`.
- [ ] F2. `client/next.config.ts`: agregar `NEXT_PUBLIC_TATANA_DOWNLOAD_URL` a `env` con default `""`.
- [ ] F3. `client/src/lib/agent-messages.ts`: el caso `origin_not_allowed` según §6.10. Revisar el texto del permiso de red local (T2).
- [ ] F4. `README.md`: fila de `NEXT_PUBLIC_TATANA_DOWNLOAD_URL` en la tabla de `client/.env.local`.
- [ ] F5. Skills obligatorias (`ui-ux-pro-max`, `senior-frontend`, `3d-web-experience`, `web-design-guidelines`). Es una HU sin cambios visuales nuevos: dejar constancia en el progress.
- [ ] F6. Escribir `progress/impl_frontend_despliegue-nube.md`.

---

## 9. Verificación

### 9.1 Antes de declararse `done`

**`implementer-backend`**

```bash
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj

# Scripts
bash -n deploy/cloud/scripts/*.sh deploy/cloud/armar-tatana-nube.sh deploy/windows/armar-tatana-portable.sh
shellcheck deploy/cloud/scripts/*.sh deploy/cloud/armar-tatana-nube.sh deploy/windows/armar-tatana-portable.sh   # si no está instalado: docker run --rm -v "$PWD:/m" -w /m koalaman/shellcheck:stable ...
python3 deploy/windows/hornear-origenes-tatana.py --probar
# Hornear sobre una COPIA (nunca sobre el appsettings.json del repo):
cp server/src/Factum.Agent/appsettings.json "$TMPDIR/as.json" && python3 deploy/windows/hornear-origenes-tatana.py "$TMPDIR/as.json" https://factum-piloto.duckdns.org && cat "$TMPDIR/as.json"
python3 deploy/windows/hornear-origenes-tatana.py "$TMPDIR/as.json" 'https://x.com/path'; echo "exit=$? (esperado 2)"

# Compose y Caddy (con un env de prueba en un temporal, valores ficticios sin "CAMBIAR")
docker compose -f deploy/cloud/docker-compose.yml --env-file <tmp>/prueba.env config >/dev/null
docker run --rm -e FACTUM_DOMINIO=localhost -e FACTUM_ACME_EMAIL=a@b.c \
  -v "$PWD/deploy/cloud/Caddyfile:/etc/caddy/Caddyfile:ro" -v "$PWD/deploy/cloud/caddy-sitios:/etc/caddy/sitios:ro" \
  caddy:<tag> caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile

# Workflows
docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:latest
```

- **D15:** sin flags nuevos, el zip de `armar-tatana-portable.sh` tiene que salir igual que antes. Comparar la lista de
  archivos (`unzip -Z1`) y el contenido de `appsettings.json` y `tatana-portable.ini` contra un armado con `git stash`,
  o argumentarlo en el impl si el armado completo (unos 300 MB) no es viable en la sesión.
- `deploy/windows/docker-compose.yml`, `deploy/windows/scripts/*` y `armar-paquete.sh` **no se modifican**
  (`git diff --stat deploy/windows` muestra solo `armar-tatana-portable.sh` y el `.py` nuevo).

**`implementer-frontend`**

```bash
cd client && npx tsc --noEmit
```

### 9.2 Prueba local del compose de producción (D16; la puede hacer el implementador si tiene Docker, si no queda para el usuario)

- Datos en un **directorio temporal propio** (`FACTUM_DATOS=<scratch>/datos`) y `FACTUM_DOMINIO=localhost`. Con
  `localhost`, Caddy usa su CA interna: hay que aceptar el certificado o usar `curl -k`.
- Imágenes con `desplegar.sh --construir-local`, o con `docker build` a mano y tag `local-prueba`.
- **No** usar la Mongo de desarrollo, ni `dev-data/`, ni el puerto 27017 del compose de desarrollo: el compose de
  producción no publica Mongo y usa su propio datadir.
- Chequear:
  - `/health/ready` da 200, y con `docker compose stop mongo` da 503;
  - login de superadmin con cambio de contraseña;
  - headers;
  - `backup.sh --solo-local` con una clave age de prueba, `restaurar.sh` sobre otro datadir temporal y
    `verificar-informes.sh`.
- Al terminar, `docker compose down` y borrar **solo** el directorio temporal creado para la prueba.

### 9.3 Prueba manual del usuario en el entorno real (D17)

Siguiendo `docs/despliegue-nube.md`:

1. **HTTPS:** candado válido; `http://` → `https://`; `curl -I` muestra HSTS, nosniff, Referrer-Policy, XFO, CSP
   `frame-ancestors`, CSP Report-Only y Permissions-Policy.
2. **Superficie:** `nmap -Pn -p- <IP>` → solo 22, 80 y 443. `/swagger` no expone la API. No se llega a Mongo ni a 8080
   desde afuera.
3. **Configuración:** `compose.sh logs backend | head` muestra `Auth: modo local`, CORS solo con `https://<dominio>` y
   ningún secreto.
4. **Superadmins:** los dos entran con su temporal, la cambian y uno da de alta un cliente.
5. **Flujo del perito** en una PC Windows con el Tatana "nube", en Chrome o Edge:
   - acepta el permiso de red local;
   - crea un caso, captura, genera el informe;
   - el ZIP queda en la PC;
   - el DOCX se descarga desde otra PC con la misma cuenta;
   - `verificar-informes.sh` da OK (SHA-256 = `report_hash`);
   - sin errores de mixed content ni de CSP bloqueante (revisar la consola: los avisos Report-Only se anotan para la HU
     de CSP efectiva).
6. **Tatana sin el origen:** con el portátil local (sin `--origenes`) aparece el mensaje con el link de descarga.
7. **Reinicio:** `sudo reboot` del VPS; todo vuelve y los DOCX siguen dando OK.
8. **Deploy automático:** un merge trivial `develop` → `main` despliega solo, en verde. Con una versión que no arranca
   (p. ej. un `FACTUM_URL_PUBLICA` distinto para forzar la guarda, o un tag roto), el job queda en rojo y producción
   sigue con la versión anterior.
9. **Backup y restore:** a la mañana siguiente hay una copia en R2 y healthchecks.io en verde. El simulacro de restore
   en un VPS nuevo da `verificar-informes.sh` OK y los usuarios entran con sus contraseñas actuales.
10. **Monitoreo:** `compose.sh stop backend` → llega el mail de UptimeRobot; `start` → llega el de recuperación.
11. **Windows local (D15):** `deploy/windows/armar-paquete.sh` arma, instala y funciona igual que antes (localhost, sin
    HTTPS).

---

## 10. Decisiones pendientes (necesitan al usuario)

> Ninguna bloquea la implementación del repo: todo lo de abajo es configuración del despliegue o se resuelve con el
> default recomendado. Conviene confirmarlas antes de que el usuario siga la guía.

- **P1. Tamaño del plan de DonWeb.** Recomendado: **2 vCPU / 4 GB RAM / 40 GB SSD**, Ubuntu 24.04 x86_64, en
  Argentina. Con 2 GB anda con swap y `FACTUM_MONGO_CACHE_GB=0.25`, pero el plan B de build en el VPS (`next build`)
  probablemente no entre. El precio y los planes vigentes se confirman en donweb.com; la SDD no los inventa. Hay que
  confirmar en el panel que el CPU tiene **AVX**: Mongo 7 lo exige, y `preparar-servidor.sh` lo chequea.
- **P2. Destino del backup fuera del host.** Recomendado: **Cloudflare R2** (capa gratis de unos 10 GB y sin costo de
  egreso, a confirmar; puede pedir una tarjeta para habilitar R2). Va cifrado con `age` y la clave queda solo en manos
  del usuario. **Implica que una copia cifrada de los datos queda fuera de Argentina.** Alternativas: Backblaze B2
  (también fuera del país) o un segundo almacenamiento en Argentina (por ejemplo, otro producto de DonWeb, a confirmar
  si existe y cuánto cuesta). El script usa `rclone`, así que cambiar de destino es solo configuración.
- **P3. Aprobación manual del deploy.** Los environments con revisores obligatorios en repos **privados** dependen del
  plan de la cuenta de GitHub (Pro/Team), a confirmar. Recomendado: deploy **sin aprobación**, como pidió el usuario,
  con los secrets a nivel de repo. Si el plan lo permite, el environment `production` queda listo para sumar la
  aprobación más adelante. Si declarar `environment:` diera error en el plan actual, el implementador lo saca y lo
  anota.
- **P4. Cómo se distribuye el Tatana para la nube.** Recomendado: **el portátil**, publicado en
  `https://<dominio>/descargas/` (link público; Tatana no tiene secretos) y referenciado en el mensaje
  `origin_not_allowed`. Alternativa: compartirlo por un canal privado (Drive, por ejemplo) y dejar
  `FACTUM_TATANA_DESCARGA_URL` vacía. El instalador NSIS de Electron queda fuera: solo lo arma el CI de GitLab, que no
  corre. Si los clientes ya usan el instalador, hace falta una HU aparte.
- **P5. Consecuencia del dominio provisorio (informativa, para aceptar).** Como el dominio definitivo no se conoce
  todavía, cuando se defina **habrá que redistribuir Tatana una vez** con los dos orígenes (§7, sección 15, fase A).
  No hay corte: el provisorio sigue andando mientras tanto. Con la lista de orígenes, el servidor puede cambiar de
  dominio sin tocar las PCs que ya tengan ese Tatana. Tatana no acepta comodines, por seguridad.

> **Decisión del usuario (2026-10-06):** se aceptan P1 a P5 con lo recomendado (2 vCPU / 4 GB / 40 GB con AVX; backup cifrado
> en Cloudflare R2; sin aprobación manual; Tatana portátil en `/descargas/`; una redistribución de Tatana al definir el
> dominio) y DT1 (un solo hostname ruteado por path).
