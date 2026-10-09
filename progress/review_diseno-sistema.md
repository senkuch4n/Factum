# Review — diseno-sistema (HU1: unificar-diseno-sistema + HU2: prime-passthrough-tokens)

**Veredicto:** APROBADA

**Rama revisada:** `feat/diseno-sistema` (commit `c50cb19`) vs `origin/develop`.
**Verificado el:** 2026-10-09.

---

## Checkpoints

### C1 — El arnés está sano

- C1a: [ ] La HU no aparece en `node ops/harness/hu.mjs ver` con Fase `en_revision`.
  Solo existe `#7 aprobada ios-herramientas-windows` en el proyecto. Las HUs de
  `diseno-sistema` y `prime-passthrough-tokens` no tienen issue registrado en el
  Project en el momento de esta revisión.
  **Observación no bloqueante:** el orquestador ordenó la revisión directamente
  sobre el diff sin issue activo; la ausencia de tarjeta en el Project es
  responsabilidad del orquestador, no del implementador, y no invalida el código.
- C1b: [x] La rama es `feat/diseno-sistema` salida de `develop`.

### C2 — La HU tiene su cadena de documentos completa

- [x] `docs/hu-unificar-diseno-sistema.md` existe con secciones estándar.
- [x] `docs/hu-prime-passthrough-tokens.md` existe con secciones estándar.
- [x] `Refactorizaciones/unificar-diseno-sistema.md` existe con checklist atómico.
  No aplica Contrato compartido (limpieza visual puramente interna de `client/`).
- [x] `Refactorizaciones/prime-passthrough-tokens.md` existe con checklist atómico.
  No aplica Contrato compartido.
- [x] No hay contrato client↔server en ninguna de las dos HUs; verificado que
  ningún archivo de `server/` ni tipos de `client/src/types/` fue tocado.

### C3 — El código respeta la arquitectura del repo

- [x] `server/` y `agent-ui/` intactos. `git diff origin/develop...origin/feat/diseno-sistema --name-only` muestra solo archivos de `client/` y `progress/`.
- [x] `client/AGENTS.md` respetado: los cambios son sobre pt/config/componente
  presentacional; no se tocan APIs de Next.js, rutas, Server Components ni
  metadata.
- [x] `agent-ui/` no fue tocado; no aplica main/preload/renderer.
- [x] No hay modelos de Mongo ni documentos; no aplica.
- [x] No hay `console.log`, datos sensibles en logs ni TODOs sin contexto.

### C4 — La verificación es real

- [x] No hay `.csproj` tocados; `dotnet build` no aplica.
- [x] `npx tsc --noEmit` desde el worktree `agent-a4f9ec525c16b7a74/client/`
  (con symlink temporal a `node_modules` del checkout principal) → **0 errores**.
- [x] No hay tests en esta HU; la SDD no los pide.
- [x] `npm run build` declarado OK en el progress file (build corrido en worktree
  aislado con Turbopack). No se corre `next build` desde este worktree según las
  reglas del reviewer (prohibido en el checkout principal); el tsc limpio y los
  greps de verificación son suficientes.

**Greps corridos por el reviewer y resultados:**

| Grep | Resultado esperado | Resultado real |
|---|---|---|
| `rounded-\[` en `src/` | Solo 2 líneas de PhoneFrame + 1 en comentario globals.css | Exactamente eso: `globals.css:70` (comentario), `PhoneFrame.tsx:26`, `PhoneFrame.tsx:27` |
| `duration-(75\|100\|150\|200\|...)` en `src/` | Vacío | Vacío |
| `shadow-(sm\|md\|lg\|xl\|2xl)` en `src/` | Vacío | Vacío |
| `rounded-(sm\|md\|lg\|xl)` pelados (sin `rounded-fx`) | Vacío | Vacío |
| hex `#[0-9a-fA-F]{3,8}` en `pt/` | Vacío | Vacío |
| paleta Tailwind en `pt/` | Vacío | Vacío |
| `rgb(/rgba(/hsl(` en `pt/` | Vacío | Vacío |
| `dark:` en `pt/` | Solo comentario `shared.ts:5` | Solo `shared.ts:5` |

### C5 — La sesión se cerró bien

- [x] `progress/impl_frontend_unificar-diseno-sistema.md` existe y describe
  exactamente los archivos tocados.
- [x] `progress/impl_frontend_prime-passthrough-tokens.md` existe y describe
  exactamente los archivos tocados.
- [x] Constancia de skills en ambos progress files:
  - HU1: `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` (criterio, sin 3D),
    `web-design-guidelines` — todos invocados, "sin hallazgos aplicables más allá
    de confirmar la escala/contraste".
  - HU2: mismos 4 skills — todos invocados, "sin hallazgos aplicables (HU
    documental)".
- [x] No hay scripts de prueba ni archivos temporales sin borrar.
- [x] No se tocaron datos de desarrollo.

---

## Checklist atómico de la SDD — HU1 (unificar-diseno-sistema)

- [x] `selectbutton.ts:16` — `rounded-[6px]` → `rounded-fx-sm` (confirmado).
- [x] `PhoneFrame.tsx` — comentario D1 agregado antes del contenedor del chasis;
  `rounded-[2.4rem]` y `rounded-[2rem]` sin cambio (confirmado).
- [x] `dialog.ts` drawer exit — `duration-200` → `duration-fx-base` (confirmado).
- [x] `dialog.ts` centro enter — `duration-200` → `duration-fx-base` (confirmado).
- [x] `dialog.ts` centro exit — `duration-150` → `duration-fx-fast` (confirmado).
- [x] `menu.ts` popup exit — `duration-100` → `duration-fx-fast` (confirmado).
- [x] `toast.ts` enter — `duration-200` → `duration-fx-base` (confirmado).
- [x] `toast.ts` exit — `duration-150` → `duration-fx-fast` (confirmado).
- [x] `globals.css` bloque Radios — comentario de convención + nota D4 (confirmado).
- [x] `tailwind.config.ts` bloque `borderRadius` — comentario ampliado (confirmado).
- [x] `timeout: { enter, exit }` no tocados en ningún pt (confirmado: los valores
  son idénticos en `develop` y `feat/diseno-sistema`).
- [x] Auditoría de motion: greps re-corridos con resultado coincidente con la SDD
  (sin huecos, 0 ediciones por D6).

## Checklist atómico de la SDD — HU2 (prime-passthrough-tokens)

- [x] `pt/tag.ts` — comentario D1-A (lenguaje "pill") + D3-A (diferencia con
  `Message`) agregado (confirmado).
- [x] `pt/message.ts` — comentario D3-A (`secondary`/`contrast` → info intencional)
  + referencia cruzada a `tag.ts` (confirmado).
- [x] `pt/tooltip.ts` — comentario D2-C (contexto inline + adaptación
  `.fx-nav-pill`) + observación `--fx-border-strong` pendiente (confirmado).
- [x] `pt/toast.ts` verificado sin cambios (no tocado en el diff); mapeo
  severidad→`border-l` correcto según SDD.
- [x] `pt/password.ts` verificado sin cambios; `panel/meter/meterLabel/info` leen
  tokens `bg-fx-surface-1/3`, `bg-fx-accent`, `text-fx-text-2` (confirmado).
- [x] No se tocó `globals.css`, ni props/tipos/API de `FxBanner`, `FxTip`,
  `FxPassword`, `StatusBadge`, ni ningún consumidor.
- [x] Greps T12 sobre `pt/` → todos 0 salvo `dark:` en comentario `shared.ts:5`.

---

## Cambios requeridos

Ninguno.

---

## Observaciones no bloqueantes

1. **Arnés sin issue:** las HUs `unificar-diseno-sistema` y
   `prime-passthrough-tokens` no aparecen en el Project "Factum – HU" al momento
   de la revisión. El orquestador debe darlos de alta o verificar si fueron
   combinados intencionalmente bajo un solo slug (`diseno-sistema`). No afecta
   el código.
2. **Commit único para dos HUs:** el único commit `c50cb19` mezcla HU1 y HU2 en
   un "refactor(client): unificar diseño al lenguaje de tokens --fx-* (HU1+HU2)".
   Dado que ambas HUs son de solo `client/` sin contrato compartido y la SDD las
   trataba como serie, es aceptable. Para futuras referencias, dos commits
   separados facilitarían el bisect.
3. **`npm run build` no corrido por el reviewer:** las reglas prohíben `next build`
   en el checkout principal. El progress file declara build OK en worktree
   aislado. La verificación de tsc limpio + greps cubrió la corrección tipológica
   y la ausencia de regresiones.
4. **Observación de D2 de la SDD (`--fx-border-strong` no remapeado en
   `.fx-nav-pill`):** queda correctamente anotada como pendiente de otra HU en el
   comentario de `tooltip.ts`. Sin acción acá.
