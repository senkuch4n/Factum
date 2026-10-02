# impl_frontend — instalacion-local-docker

**Estado:** done (sin commit, como se pidió)
**Rama:** `feat/instalacion-local-docker` (HEAD `d31126e`, cambios en el working tree)
**App:** solo `client/`. `agent-ui/` no se tocó. Nada en `server/`, `deploy/`, `backlog.json` ni `progress/current.md`.
**SDD:** `Refactorizaciones/instalacion-local-docker.md`, §6.3 y §9.1 (F1–F6)

## Checklist §9.1

- [x] F1. Leí `client/AGENTS.md` y `client/node_modules/next/dist/docs/01-app/03-api-reference/05-config/01-next-config-js/output.md` (Next 16.2.10). Confirmado: `output: "standalone"` genera `.next/standalone/server.js`, que **no** copia `public/` ni `.next/static` (hay que copiarlos a mano) y escucha en `PORT`/`HOSTNAME`.
- [x] F2. `client/.dockerignore` nuevo, con exactamente las entradas de §6.3 (`node_modules`, `.next`, `.env`, `.env.*`, `*.tsbuildinfo`, `.DS_Store`, `Dockerfile`, `.dockerignore`, `npm-debug.log*`). Deja fuera `client/.env.local` (H4).
- [x] F3. `client/next.config.ts`: se agregó `output: "standalone"` (con un comentario de una línea). `env:` y `allowedDevOrigins` no se tocaron.
- [x] F4. `client/Dockerfile` reescrito según §6.3: stages `deps` / `builder` / `runner`, todos `node:22-alpine` y **sin** `--platform=$BUILDPLATFORM` (H7/DT7). `ARG` + `ENV` `NEXT_PUBLIC_BACKEND_URL` (default `http://localhost:8080`) y `NEXT_PUBLIC_AGENT_URL` (default `http://localhost:8765`), `NEXT_TELEMETRY_DISABLED=1`. El runner usa `NODE_ENV=production PORT=3000 HOSTNAME=0.0.0.0`, copia `standalone`, `.next/static` y `public` con `--chown=node:node`, y corre con `USER node`, `EXPOSE 3000` y `CMD ["node", "server.js"]`. Sumé dos comentarios explicativos (por qué `HOSTNAME=0.0.0.0` y por qué se copian static/public a mano). Fuera de eso, es igual al de la SDD.
- [x] F5. Skills (ver abajo).
- [x] F6. Verificación (abajo) y este archivo.

## Contrato compartido (§7.1)

Los nombres de build arg coinciden exactamente con la SDD: `NEXT_PUBLIC_BACKEND_URL` y `NEXT_PUBLIC_AGENT_URL`, con defaults `http://localhost:8080` y `http://localhost:8765`. `next.config.ts` los sigue leyendo desde `env:`, sin cambios. Los valores horneados aparecen en el bundle (F-V4).

## Verificación §10.1 (salidas reales)

### F-V1 — `cd client && npx tsc --noEmit`
```
tsc_exit=0
```
(sin salida, limpio)

### F-V2 — `docker buildx build --platform linux/amd64 --load -t factum-frontend:0.0.0-verif client/`
Lanzado en background, con el log en el scratchpad. Resultado: `exit=0`.
- Contexto de build: `transferring context: 2.16MB` (el `.dockerignore` funciona: ni `node_modules` ni `.next` entran al contexto).
- `next build` dentro del contenedor (amd64 emulado): `Generating static pages (5/5)`, rutas `/`, `/_not-found`, `/dashboard` y `/design-system`, todas estáticas (○). El `RUN npm run build` del stage tardó 47.7 s.
- `grep -E "warn|Warn|error"` sobre el log: sin coincidencias.
- `naming to docker.io/library/factum-frontend:0.0.0-verif done`.

El build de Next se corrió **solo dentro de Docker**. No corrí `npm run build` en la copia de trabajo (regla dura: pisaría el `client/.next` del `npm run dev` del usuario). Lo que el orquestador pedía como "npm run build" queda cubierto por el build dentro de la imagen.

### F-V3 — contenido de `/app` y tamaño
`docker run --rm --platform linux/amd64 factum-frontend:0.0.0-verif ls -la /app /app/.next`:
```
/app:
drwxr-xr-x    1 node     node          4096 .next
drwxr-xr-x   13 node     node          4096 node_modules
-rw-r--r--    1 node     node          1079 package.json
drwxr-xr-x    3 node     node          4096 public
-rw-r--r--    1 node     node          6923 server.js

/app/.next:
BUILD_ID, app-path-routes-manifest.json, build-manifest.json, package.json,
prerender-manifest.json, required-server-files.json, routes-manifest.json, server/, static/
```
- `server.js`, `.next/static` y `public` están presentes.
- **No hay `.env*`** en `/app`: `ls -a /app | grep -i env` y `find /app -name ".env*"` salen vacíos.
- `node_modules` es solo el trace del standalone: 11 entradas y 34.0M, contra 487M del `client/node_modules` completo. Incluye `sharp`, `@img/sharp-linuxmusl-x64` y `@img/sharp-libvips-linuxmusl-x64`, o sea los binarios nativos de **linux/amd64 musl**. Esto confirma H7: build en la plataforma destino.
- Tamaño: `docker image ls` → `factum-frontend:0.0.0-verif 295MB` (disco, incluye la base node:22-alpine). `docker image inspect` → `Size=74286348` (~74 MB comprimidos), `Arch=amd64`.
- Comparación: en la Mac no existe una imagen `factum-frontend` anterior (solo `factum-backend:latest` 1.1GB). Estimado de la imagen anterior: base node:22-alpine + `node_modules` completo (~487M en la Mac) + `.next` ≈ 650–700 MB en disco. La nueva pesa menos de la mitad.

### F-V4 — levantar, curl, usuario, bundle
Antes de usar 127.0.0.1:13000 verifiqué que estuviera libre: `netstat -anv -p tcp | grep LISTEN | grep .13000` sin resultados y `docker ps` sin nada en 13000. Nota: `lsof -iTCP` se cuelga en esta Mac, así que maté esos procesos `lsof` míos y usé `netstat`.
```
docker run --rm -d --platform linux/amd64 --name factum-verif-front -p 127.0.0.1:13000:3000 factum-frontend:0.0.0-verif
curl → 000 (todavía arrancando), luego 200
HTML: <title>Factum</title>, "DNI", "Contraseña", "Ingresar"  (pantalla de login)
docker logs:
  ▲ Next.js 16.2.10
  - Local:         http://localhost:3000
  - Network:       http://0.0.0.0:3000
  ✓ Ready in 0ms
docker inspect: User=node Ports={"3000/tcp":[{"HostIp":"127.0.0.1","HostPort":"13000"}]}
docker exec id: uid=1000(node) gid=1000(node)
grep -rl "localhost:8080" /app/.next/static → chunks/2h-cc16im-nce.js, chunks/2-pesnjrkmd39.js
grep -rl "localhost:8765" /app/.next/static → chunks/2h-cc16im-nce.js, chunks/14e-d3zeutc2a.js, chunks/2-pesnjrkmd39.js
wget -q --spider http://127.0.0.1:3000/ (dentro del contenedor) → wget_spider_ok   (el healthcheck DT11 va a funcionar)
docker rm -f factum-verif-front → ok
```
Después, `docker ps -a | grep verif` no muestra nada: no quedó ningún contenedor.

**La imagen `factum-frontend:0.0.0-verif` queda a propósito** para que el `implementer-backend` la reutilice en la verificación integrada (B-V4…B-V7), como pide §10.1 F-V4. La tiene que borrar quien termine la verificación integrada.

### F-V5 — git status / `.next` del usuario
Cambios del frontend en `git status`: **solo** `M client/Dockerfile`, `M client/next.config.ts` y `?? client/.dockerignore`. El resto de los cambios del working tree son del backend o del orquestador, no míos. `client/next-env.d.ts` no se modificó (`git diff` vacío). `client/.next` del usuario sigue con fecha `Oct 1 23:51`, anterior a esta sesión: no se regeneró.

### Reglas duras respetadas
- No corrí ningún `docker compose`. Ningún `prune`, `down` ni `rm` sobre el proyecto `factum`. No toqué `evidentia-v2-mongo-1` ni el puerto 27017.
- Solo creé y borré el contenedor `factum-verif-front`. La única imagen creada es `factum-frontend:0.0.0-verif`, que se conserva (ver arriba).
- `.env.local` no entra a la imagen (verificado).

## Skills (F5)

Invocados con el `Skill` tool: `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` y `web-design-guidelines`.
Revisado con ui-ux-pro-max / senior-frontend / 3d-web-experience / web-design-guidelines: **sin hallazgos aplicables: HU sin cambios visuales** (solo empaquetado: Dockerfile, .dockerignore y `output` de next.config). No se tocó `src/`, no se agregó 3D ni animaciones. `web-design-guidelines` no tiene reglas aplicables a archivos que no son de UI. `ui-styling` y `mblode-agent-skills-ui-animation` no corresponden (sin Tailwind ni motion).

## Decisiones no obvias
- Puse `output: "standalone"` como primera clave, con un comentario que apunta al Dockerfile. No hay cambios funcionales para `next dev`: `output` solo afecta a `next build`.
- Para chequear el puerto usé `netstat` en vez de `lsof`, que se cuelga en esta máquina.

## Archivos tocados
- `client/.dockerignore` (nuevo)
- `client/Dockerfile` (reescrito)
- `client/next.config.ts` (+2 líneas)
- `progress/impl_frontend_instalacion-local-docker.md` (este archivo)
