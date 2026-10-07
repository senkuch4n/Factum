# Review — despliegue-nube (#14)

**Veredicto:** APROBADA

Verificado por el reviewer (no por los progress): `dotnet build` de Backend y Agent (0 errores; los 2 warnings NU1902/NU1903 son de paquetes ya existentes, no de esta HU), `dotnet test` Backend (794 OK, 7 omitidos de Mongo) y Agent (139 OK), `npx tsc --noEmit` en `client/`, `bash -n` sobre todos los scripts de `deploy/cloud/` y `armar-tatana-portable.sh`, `hornear-origenes-tatana.py --probar` OK, `./ops/harness/verify.sh` exit 0. `shellcheck` y `actionlint` NO están instalados: no se corrieron (queda como observación). El Project muestra #14 en `en_revision`; la rama sale de `develop`.

## Checkpoints
- C1 arnés sano: [x] (#14 en_revision, rama feat/despliegue-nube desde develop, verify.sh OK)
- C2 cadena de documentos: [x] (HU con Validación y D9 actualizada, SDD con Contrato compartido; los nombres del contrato coinciden: `NEXT_PUBLIC_TATANA_DOWNLOAD_URL` en Dockerfile, next.config.ts, agent-messages.ts y workflow; `/health/ready` snake_case `status`/`mongo`; `Agent:AllowedOrigins` horneado)
- C3 arquitectura: [x] (backend, deploy/, .github/, docs y client/ chico; agent-ui no tocado; `deploy/windows` solo cambia `armar-tatana-portable.sh` + el .py nuevo, `deploy/windows/docker-compose.yml` y `docker-compose.yml` raíz intactos, D15; sin cambios de modelos Mongo)
- C4 verificación real: [x] (builds, tests, tsc; `HealthReadyTests` prueba el mapeo y el probe contra 127.0.0.1:1; test extra de Tatana con la lista horneada)
- C5 cierre: [x] (ambos impl existen; el frontend deja constancia de los 4 skills; sin scripts temporales; no se tocó la base)

## Revisión por punto pedido
1. Secretos: [x] sin tokens, claves, DNI ni contraseñas reales (grep de patrones en deploy/cloud, .github, docs, progress y README: limpio). `factum.env.example` solo trae `CAMBIAR`/vacíos; `.gitignore` cubre `deploy/cloud/*.env` (excepto el example), `caddy-sitios/*.caddy` y `deploy/cloud/dist/`. `_comun.sh` rechaza `CAMBIAR` y valida por nombre de clave, nunca imprime valores.
2. Workflow: [x] trigger `push: main` (+ `workflow_dispatch` para el primer arranque); `permissions: contents: read` global y `packages: write` solo en `imagenes`; `concurrency` sin cancelar; `desplegar` con `if: vars.FACTUM_DEPLOY_HABILITADO == 'true'`; tag `sha-${{ github.sha }}`; `StrictHostKeyChecking=yes` + known_hosts fijado + `IdentitiesOnly`; secrets solo por `env:`, nunca interpolados en `run:` ni impresos; actions fijadas por SHA; la clave se borra con `if: always()`.
3. Scripts: [x] `set -euo pipefail` en todos; `deploy-forzado.sh:21-27` acepta solo `^desplegar (sha-[0-9a-f]{40})$` y `estado`, el resto exit 2; `desplegar.sh:29` revalida el tag y `:68-70` exige commit existente y ancestro de `origin/main` (DT7); copia previa a mktemp (DT9), flock, guarda de dominio, backup pre-deploy y rollback (`:144-161`) con códigos 0/1/2/3/4; `revertir.sh` intercambia desplegado/anterior; `backup.sh:106-107` cifra con `age -r` (solo clave pública en el host) y la contraseña de root no pasa por argv; `restaurar.sh:121-125` se niega sobre base con datos sin `--reemplazar` y pide escribir el dominio; descarta la identidad privada con `shred` salvo `--conservar-identidad`.
4. Compose y Caddy: [x] Mongo sin `ports`, red `datos` interna, `root` y `factum_app` (readWrite sobre `factum`) separados; solo Caddy publica 80/443/443udp; headers D13 (HSTS sin includeSubDomains/preload, nosniff, Referrer-Policy, XFO, CSP efectiva `frame-ancestors`, CSP Report-Only, Permissions-Policy con camera/microphone, `-Server`); `request_body max_size 300MB` en las rutas del backend (> 256 MiB de `generate/finish`); ruteo por path de DT1; sin access log. Forwarded headers sin cambios y válidos porque el backend no publica puerto. Superadmins por `compose.bootstrap*.yml` (A1 resuelto sin tocar código).
5. Backend: [x] `/health/ready` anónimo, 200/503 con cuerpo fijo sin detalle (`MongoHealthProbe.cs:81-85`), timeouts 2/2/3 s + CTS de 3 s, el warning solo loguea el tipo de excepción; `/health` intacto. Tatana: lista de orígenes validada con regla más estricta que `OriginPolicy`, sin comodines; sin flags el empaquetado es igual (D15); `Mock` re-verificado tras hornear.
6. Guía: [x] 19 secciones (0-18) más Anexo A, en el orden pedido (DonWeb, SSH, firewall, Docker, .env, primer arranque, HTTPS, superadmins, Tatana, deploy automático, backups, restore, monitor, cambio de dominio); límites de terceros marcados "a confirmar, consultado el 2026-10-06"; pasos del panel con "a confirmar en el panel"; sin precios.
7. Frontend F1-F4: [x] coinciden con la SDD; sin cambios visuales; texto de URL como texto plano (sin componente nuevo, según §6.10); constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` y `web-design-guidelines` presente.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `shellcheck` y `actionlint` no están instalados en este equipo: no se pudieron correr. Conviene correrlos (por ejemplo con `docker run koalaman/shellcheck` / `rhysd/actionlint`) antes del merge a `main`.
- `.github/workflows/desplegar-produccion.yml:18`: `workflow_dispatch` se puede lanzar sobre cualquier rama y publicaría imágenes (incluido `latest`) de una rama de HU. El VPS rechazaría el deploy por la guarda de ancestro (DT7), pero `latest` quedaría apuntando a esa rama; no se usa para desplegar. Considerar `if: github.ref == 'refs/heads/main'` en `imagenes`.
- `scripts/desplegar.sh:114` + `backup.sh:58`: si el cron de backup (03:30) está corriendo cuando llega un deploy, el backup pre-deploy falla por el lock ("ya hay un backup en curso") y el deploy aborta con exit 1. Es el lado seguro; basta reintentar. Se podría esperar el lock unos minutos.
- `desplegar-produccion.yml:144`: con `DEPLOY_PORT` distinto de 22, el `known_hosts` debe estar en formato `[host]:puerto` (conviene aclararlo en la guía, sección 11.3).
- Prueba manual pendiente del usuario (D17): HTTPS/headers reales, `nmap`, flujo del perito con Tatana "nube", reinicio, deploy automático real con y sin falla, backup a R2 y simulacro de restore, monitor. Opcional: `armar-paquete.sh` de Windows igual que antes.
- Frontend (ya señalado por el implementador): una URL larga en `ZipLocalActions.tsx` podría desbordar sin `break-words`; verificarlo en la prueba manual.
