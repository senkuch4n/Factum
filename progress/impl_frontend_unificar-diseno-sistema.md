# Implementación frontend — `unificar-diseno-sistema` (HU1)

**Rama:** `feat/diseno-sistema` (desde `origin/develop`, worktree aislado).
**SDD:** `Refactorizaciones/unificar-diseno-sistema.md`
**Toca:** `client/` únicamente. No se tocó `server/` ni `agent-ui/`.
**Contrato compartido:** no aplica (limpieza visual interna de `client/`).

## Archivos tocados

| Archivo | Cambio |
|---|---|
| `client/src/lib/prime/pt/selectbutton.ts` | `rounded-[6px]` → `rounded-fx-sm` (D5 / resolución 8−2 ≈ 4px, único escalón bajo `md`). |
| `client/src/components/PhoneFrame.tsx` | Comentario de excepción D1 antes del contenedor del chasis; `rounded-[2.4rem]` y `rounded-[2rem]` quedan **sin cambio** (geometría de hardware). |
| `client/src/lib/prime/pt/dialog.ts` | drawer exit `duration-200`→`duration-fx-base`; centro enter `duration-200`→`duration-fx-base`; centro exit `duration-150`→`duration-fx-fast`. |
| `client/src/lib/prime/pt/menu.ts` | popup exit `duration-100`→`duration-fx-fast`. |
| `client/src/lib/prime/pt/toast.ts` | enter `duration-200`→`duration-fx-base`; exit `duration-150`→`duration-fx-fast`. |
| `client/src/app/globals.css` | Comentario de convención en el bloque `/* Radios */`: no usar `rounded-[…]` literal (usar `rounded-fx-*` / `rounded-full` / `rounded-none`); nota D4 de que la escala de espaciado `--fx-space-*` es una HU aparte; mención a la excepción del chasis (D1). |
| `client/tailwind.config.ts` | Comentario ampliado en `borderRadius`: recordatorio "sin `rounded-[…]` literal, usá estas utilidades" + excepción PhoneFrame. |

## Decisiones no obvias

- **`timeout: { enter, exit }` NO se tocó** en ningún pt: son los ms de
  react-transition-group para desmontar; cambiarlos alteraría el comportamiento
  (fuera de D2). Solo se cambiaron las clases Tailwind de `classNames.*Active`.
- Verifiqué que `duration-fx-fast|base|slow` existen como utilidades reales en
  `tailwind.config.ts` (`transitionDuration`, líneas 71-74) sobre `--fx-dur-*`
  (`globals.css` 84-86: 120/200/320ms). El mapeo D2-A es correcto: 200→base
  (igual), 150→fast (−30ms imperceptible), 100→fast (+20ms imperceptible).
- `menu.ts` enterActive ya usaba `duration-fx-fast`; solo faltaba el exitActive.
- **Auditoría de motion (D6-A):** re-corrí los greps. Sin huecos reales que
  cerrar (0 ediciones por D6), coincide con el veredicto de la SDD: todas las
  `animate-[fx-*]` llevan `motion-safe:`, los `transition-*` sin guard son color
  (fuera de alcance), transform neutralizado por `--fx-lift:0`, o fades de
  opacidad pura de overlays de Prime.

## Verificación (desde `client/`)

- `npx tsc --noEmit` → **OK, 0 errores**.
- `npm run build` (Next.js 16.2.10 / Turbopack) → **OK**, compila y prerenderiza
  las 6 rutas (`/`, `/_not-found`, `/admin/cuentas`, `/cambiar-contrasena`,
  `/dashboard`, `/design-system`). Build corrido en el worktree aislado (no se
  pisó el `.next` del `npm run dev` del checkout principal).
- `grep -rn "rounded-\[" src` → solo las 2 líneas reales de `PhoneFrame.tsx`
  (2.4rem y 2rem) + 1 coincidencia esperada en el comentario de convención de
  `globals.css` (es un comentario, no una utilidad).
- `grep -rnE "duration-(75|100|150|200|300|500|700|1000)\b" src` → **vacío**.
- `grep -rnE "shadow-(sm|md|lg|xl|2xl)\b" src` → **vacío**.
- `grep` de `rounded-(sm|md|lg|xl)` pelados (excluyendo `rounded-fx*`) → **vacío**.

Nota de entorno: el worktree no trae `client/node_modules`; se clonó (APFS
copy-on-write) desde el checkout principal, cuyo `package-lock.json` es idéntico
a `develop`. No se modificó ninguna dependencia. `next-env.d.ts` que el build
regenera (dev→prod path) se revirtió para dejar el commit solo con los cambios
de código.

## Skills de frontend (constancia)

- **ui-ux-pro-max:** invocado. Búsqueda UX de timing de overlays confirmó que el
  patrón correcto son tokens de movimiento compartidos y que los corrimientos de
  duración se mantienen dentro del rango de feedback responsivo. Sin hallazgos
  aplicables más allá de confirmar la escala.
- **senior-frontend:** invocado. Confirmado que son edits de string de clases en
  objetos `pt` y un componente presentacional: sin impacto en SSR, tree-shaking
  ni render de React. Sin hallazgos aplicables.
- **3d-web-experience (criterio, sin agregar 3D):** invocado. No se introduce 3D
  ni pseudo-profundidad ni animaciones sin propósito; la tokenización mantiene
  valores prácticamente idénticos. Sin hallazgos aplicables.
- **web-design-guidelines (autochequeo final):** invocado. Diff de solo tokens y
  comentarios; no cambia JSX, aria, focus rings ni semántica de interacción. Sin
  regresiones de accesibilidad/consistencia. Sin hallazgos aplicables.

## Confirmación de contrato

La SDD no tiene sección de contrato compartido (no cruza client↔server ni
client↔Tatana). Todos los nombres de token usados (`rounded-fx-sm`,
`duration-fx-base`, `duration-fx-fast`) coinciden con las utilidades ya
definidas en `tailwind.config.ts`.

**Estado: done.**
