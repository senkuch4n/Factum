# `deploy/cloud/` — Factum en la nube (VPS DonWeb + Docker Compose)

La guía paso a paso es **[`docs/despliegue-nube.md`](../../docs/despliegue-nube.md)**. Esto es solo el mapa de la
carpeta. La instalación local de Windows sigue en `deploy/windows/` y no usa nada de acá.

| Archivo | Qué es |
|---|---|
| `docker-compose.yml` | Compose de producción: Caddy (único con puertos, 80/443), frontend, backend y Mongo (red interna, con usuario y contraseña). Datos en `/srv/factum/datos`. |
| `compose.bootstrap.yml`, `compose.bootstrap-2.yml` | Superadmins iniciales 0 y 1. Los suma `_comun.sh` solo si `FACTUM_SUPERADMIN_<n>_DNI` tiene valor. |
| `Caddyfile` | HTTPS automático, ruteo por path (`/api`, `/health`, `/tatana/updates` → backend; `/descargas` → estáticos; el resto → frontend), headers de seguridad, sin access log. |
| `caddy-sitios/` | Redirección del dominio anterior durante un cambio de dominio (ver su `LEEME.md`). |
| `factum.env.example` | Todas las claves del `.env` del VPS, con placeholders. El real va en `/srv/factum/config/factum.env` y **nunca** al repo. |
| `mongo-init/01-usuario-app.js` | Crea el usuario `factum_app` (solo `readWrite` sobre `factum`) en el primer arranque. |
| `armar-tatana-nube.sh` | En la Mac: arma el Tatana portátil con los orígenes de la nube horneados. |
| `scripts/_comun.sh` | Librería: carga y valida el `factum.env`, `compose()`, chequeo de salud, locks. |
| `scripts/compose.sh` | `docker compose` con el env y la versión desplegada, para operar a mano (`ps`, `logs`, `up -d`). |
| `scripts/preparar-servidor.sh` | Una vez, como root: Docker, ufw, swap, usuarios `admin`/`deploy`, árbol `/srv/factum`, sshd. |
| `scripts/desplegar.sh` | Despliega `--tag sha-<commit>` (o `--construir-local`, plan B) con backup previo, chequeo de salud y rollback automático. |
| `scripts/deploy-forzado.sh` | Comando forzado de la clave SSH de GitHub Actions (copia estable en `/srv/factum/bin/`). |
| `scripts/revertir.sh` | Rollback manual a la versión anterior. |
| `scripts/backup.sh` | Backup diario cifrado con `age` (Mongo + DOCX), subido a Cloudflare R2 con `rclone`. |
| `scripts/restaurar.sh` | Restaura un backup (pensado para un host nuevo y vacío). |
| `scripts/verificar-informes.sh` | Solo lectura: compara el SHA-256 de cada DOCX con su `ReportHash`. |

El workflow que publica las imágenes y despliega es `.github/workflows/desplegar-produccion.yml`.
