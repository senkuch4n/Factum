# Review — rediseno-base-primereact

**Veredicto:** APROBADA

## Resultados de verificación (corridos por el reviewer)
- `npx tsc --noEmit` en `client/`: sin errores.
- `npm run build`: corrido en una copia en el scratchpad (no en el checkout principal). Compila, TypeScript OK, rutas `/`, `/_not-found`, `/dashboard`, `/design-system`. Con `next start`: `/design-system` -> 404 y `/nada` -> 404. Copia borrada.

## Checkpoints
- C1 (arnés): [x] backlog/verify.sh los administra el orquestador; no corrí verify.sh (fuera del alcance pedido).
- C2 (documentos): [x] HU, SDD con checklist y Contrato compartido ("sin cambios"). Sin diff en `client/src/types/`, `lib/api.ts`, `lib/agent.ts`.
- C3 (arquitectura): [x] Solo `client/` (sin diff en `server/` ni `agent-ui/`). `layout.tsx` sigue siendo server component. Sin console.log/TODO en archivos nuevos.
- C4 (verificación): [x] tsc y build OK, ver arriba.
- C5 (sesión): [x] `progress/impl_frontend_rediseno-base-primereact.md` con constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` ("sin hallazgos aplicables") y `web-design-guidelines` (líneas 83-88). Sin temporales en el repo.

## Puntos revisados
- primereact `^10.9.9` en `client/package.json:18`; el lock resuelve 10.9.9. Sin v11, license key, `primeicons` ni `resources/themes`. grep de "xbox|license|107C10" en `src/` sin resultados.
- `client/src/app/dashboard/page.tsx`: el diff se limita al import (línea 27) y al bloque `<header>` con su comentario (desde ~línea 353), reemplazado por `AppNavbar`. Nada más.
- Legacy: `globals.css` solo agrega (cabecera, tokens, `@layer components`, keyframe), cambia `.font-mono` y borra el `@import` de Google Fonts, como indica la SDD. `tailwind.config.ts` es solo aditivo. `ui/tooltip.tsx` cambia solo el estilo del `Popup`.
- `client/src/lib/utils.ts` (fuera de la SDD): `extendTailwindMerge` con grupos solo para `text-fx-*`, `shadow-fx-*`, `rounded-fx-*`. Desviación justificada y documentada; aceptada.
- Tokens `--fx-*` en `globals.css` coinciden con T4. Ratios WCAG recalculados: texto/bg 17.91 oscuro y 17.76 claro; text-3/surface-3 5.20 y 4.73; on-accent/accent 10.56 y 6.17; accent-text/bg 11.71 y 6.16; border-strong/surface-3 3.01 y 3.07; success/bg 9.90 y 6.06; danger/surface-3 5.20 y 5.19; blanco/danger-fill 5.19 y 6.51. Todo cumple AA. Acento lima (`#7fd34e`/`#2f6f12`) separado del éxito menta (`#3ecf9e`/`#0b6b4f`).
- `client/src/lib/prime/pt/*`: sin hex, sin `dark:`, sin paleta Tailwind (la única coincidencia es el comentario en `shared.ts:5`).
- grep de `SystemFooter|font-inter|Geist|fonts.googleapis` en `src`: vacío.
- `client/src/app/design-system/page.tsx`: `notFound()` en producción y `robots` noindex/nofollow.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- No ejecuté el chequeo visual/interactivo (Dialog, Menu, toasts, reduced motion, 360 px); me apoyo en lo declarado por el implementador. Queda para la prueba manual del usuario (SDD sección 8).
- `sonner` instalado es 2.0.7 y no 2.0.8 como decía la SDD; sin impacto.
- `AgentChip` con `text-emerald-600` tiene bajo contraste en modo claro dentro de la navbar nueva. Preexistente, anotado para `rediseno-dashboard`.
- El Toast de Prime en `top-right` queda sobre la navbar (z 1200 vs 40); aceptable en el showcase.
- `npm audit` reporta 14 vulnerabilidades del árbol existente; fuera de alcance.
