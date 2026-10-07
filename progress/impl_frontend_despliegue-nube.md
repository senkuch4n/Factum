# impl_frontend — despliegue-nube (HU #14)

**Estado:** done
**SDD:** `Refactorizaciones/despliegue-nube.md` §8.2 (F1–F6), §5.1, §6.10 · **HU:** `docs/hu-despliegue-nube.md`
**App tocada:** solo `client/` (+ una fila de `README.md`). `agent-ui/` y `server/` sin tocar. Sin commit.

## Checklist

- [x] F1. `client/Dockerfile`: `ARG NEXT_PUBLIC_TATANA_DOWNLOAD_URL=` (default vacío) y sumado al `ENV` del stage `builder`, con comentario.
- [x] F2. `client/next.config.ts`: `env.NEXT_PUBLIC_TATANA_DOWNLOAD_URL: process.env.NEXT_PUBLIC_TATANA_DOWNLOAD_URL || ""`.
- [x] F3. `client/src/lib/agent-messages.ts`: caso `origin_not_allowed` exactamente como §6.10 (con URL → "El Tatana de esta PC no está habilitado para esta dirección. Descargá e instalá la versión actual desde ${url} y reintentá."; sin URL → texto anterior sin cambios). Texto del permiso de red local (T2) revisado: sin cambios (ver decisiones).
- [x] F4. `README.md`: fila `NEXT_PUBLIC_TATANA_DOWNLOAD_URL` en la tabla de `client/.env.local` (debajo de `NEXT_PUBLIC_AGENT_URL`).
- [x] F5. Skills invocados (ver abajo).
- [x] F6. Este archivo.

## Archivos tocados

- `client/Dockerfile`
- `client/next.config.ts`
- `client/src/lib/agent-messages.ts`
- `README.md` (una fila; el resto de cambios del README son del implementer-backend, D2)

## Contrato (§5.1) — coincide con la SDD

| Nombre | Dónde |
|---|---|
| `NEXT_PUBLIC_TATANA_DOWNLOAD_URL` (build arg, opcional, default `""`) | `client/Dockerfile` (`ARG` + `ENV` del builder), `client/next.config.ts` (`env`), leído en `client/src/lib/agent-messages.ts` (`origin_not_allowed`) |

El workflow (`.github/workflows/desplegar-produccion.yml`, del backend) tiene que pasarlo como `--build-arg NEXT_PUBLIC_TATANA_DOWNLOAD_URL=${{ vars.FACTUM_TATANA_DESCARGA_URL }}`. `deploy/windows/armar-paquete.sh` no lo pasa → vacío → mensaje de siempre (D15).

## Verificación

```
$ cd client && npx tsc --noEmit
(sin salida) exit 0
```

No hay tests en `client/` y la SDD no pide ninguno para el frontend. No se corrió `next build` (regla dura: no pisar `.next` del dev del usuario). Nota: el valor se hornea en build (`env` de `next.config.ts` reemplaza `process.env.X` en el bundle); con `next dev` hace falta reiniciar el dev server para que tome un valor nuevo en `.env.local`.

## Decisiones no obvias

1. **URL como texto, sin link clickeable.** Todos los consumidores de `agentErrorMessage` (`ZipLocalActions.tsx` → `<p role="alert">{error}</p>`, `dashboard/page.tsx` `setGlobal`, `useFileManager.ts` `onError`) renderizan un string plano; ninguno convierte URLs en links. Según §6.10 no se crean componentes nuevos: queda como texto.
2. **Texto de red local (T2) sin cambios.** `AGENT_LNA_DENIED_MESSAGE` nombra la opción como "Configuración del sitio → Acceso a la red local o a apps del dispositivo", que cubre los dos permisos actuales de Chrome/Edge (`local-network` y `loopback-network`, que es el que aplica a `localhost:8765`) y coincide con los nombres que prueba `localNetworkPermission()` en `agent.ts`. La SDD pide ajustarlo solo si nombra mal la opción, y no es el caso. Observación para la prueba manual (D17): Chrome desde la v117 muestra un ícono de "controles del sitio" en lugar del candado a la izquierda de la dirección (Edge sigue con candado); si en la prueba real confunde, cambiar "en el candado de la barra de direcciones" por "en el ícono a la izquierda de la dirección". No se pudo verificar contra un Chrome/Edge estable real desde este entorno.
3. **Observación (no aplicada, fuera de scope):** en `ZipLocalActions.tsx` el `<p>` de error es `flex` y no tiene `break-words`; una URL larga sin espacios podría desbordar en el modo `compact`. Hoy `origin_not_allowed` llega a ese componente solo en "reveal"/"copy". Si en la prueba manual se ve desbordado, sumar `break-words` (o `min-w-0` + `[overflow-wrap:anywhere]`) al `<p>`. No se tocó porque la HU no tiene cambios visuales.

## Skills invocados

- `ui-ux-pro-max`: búsqueda `"error message clarity recovery" --domain ux` → "Error Recovery: dar el próximo paso" y "errores anunciados (role=alert)". El texto nuevo da el próximo paso concreto (descargar e instalar desde el link y reintentar); los contenedores existentes ya usan `role="alert"`. Sin otros hallazgos aplicables.
- `senior-frontend`: build-time env vía `next.config.ts` `env` (confirmado en `client/node_modules/next/dist/docs/01-app/03-api-reference/05-config/01-next-config-js/env.md`: se reemplaza `process.env.X` en build, no se puede desestructurar → se lee como `process.env.NEXT_PUBLIC_TATANA_DOWNLOAD_URL` directo). La función sigue pura. Sin hallazgos adicionales.
- `3d-web-experience` (criterio): sin efectos 3D ni animaciones; nada agregado. Sin hallazgos aplicables.
- `web-design-guidelines` (guías de vercel-labs descargadas el 2026-10-06) sobre `agent-messages.ts`: "Error messages include fix/next step" se cumple; segunda persona, voz activa. Única observación: el desborde potencial de la URL (decisión 3). 
- `ui-styling` / `mblode-agent-skills-ui-animation`: no aplican (sin Tailwind ni transiciones nuevas).

Revisado con ui-ux-pro-max / senior-frontend / 3d-web-experience / web-design-guidelines: sin hallazgos aplicables más allá de las observaciones 2 y 3.
