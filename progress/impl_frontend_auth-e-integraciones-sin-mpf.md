# impl_frontend — auth-e-integraciones-sin-mpf

**Estado:** done
**Implementador:** implementer-frontend (Claude Opus 5.5)
**Fecha:** 2026-10-01
**Rama:** `feat/auth-e-integraciones-sin-mpf` (incluye `informe-pericial-de-parte`, f3471e7). Sin commit, como se pidió.
**Alcance:** solo `client/`. No se tocaron `agent-ui/`, `server/`, `client/src/app/page.tsx` (D-T11), `backlog.json` ni `progress/current.md`.

## F0. Convivencia con `informe-pericial-de-parte`

- `api.ts`: esa HU sumó tipos y métodos de perfil del perito y de `Case`. Acá solo se tocó `PublicConfig`, se agregó `AuthMode` (justo debajo) y se cambió `getMode()`.
- `types/index.ts`: se agregó `AuthMode` al final del re-export existente, sin reordenar.
- `UserMenu.tsx`: esa HU sumó el ítem "Mi perfil de perito". Acá solo se tocó el `<p>` de "DNI · sigla". Los ítems de perfil y de organización quedaron intactos.
- `dashboard/page.tsx`: esa HU rehizo el wizard. Acá solo se tocaron un import, una línea `usePublicConfig()` y el bloque `GuideModal`/`SoporteModal`/`FloatingDock`.
- **F13 y F15: adaptados al código real, sin cambios.** La HU anterior ya había neutralizado los dos textos, a propósito y con mapeo explícito en `Refactorizaciones/informe-pericial-de-parte.md` L699-700:
  - `GuideModal.tsx`: el pie dice "Guía de uso de Factum", no "Guía del Gabinete de Informática Forense".
  - `DesignSystemShowcase.tsx`: dice "Tribunal (deshabilitado)" / "Tribunal de ejemplo", no "Fiscalía" / "Unidad Fiscal 3".

  Ninguno de los dos tiene ya un resabio institucional y el grep de D12 da vacío. No los reescribí ("Guía de conexión USB" / "Organización (deshabilitado)") para no pisar la terminología pericial de la HU anterior. **Consecuencia para la verificación visual de 11.2:** el pie de la guía va a decir "Guía de uso de Factum" y el showcase "Tribunal (deshabilitado)". Si el orquestador o el usuario prefieren los textos de esta SDD, el cambio es de una línea en cada archivo.

## Archivos tocados (client/)

| Archivo | Checklist | Cambio |
|---|---|---|
| `client/src/lib/api.ts` | F1 | `PublicConfig.support_enabled: boolean`, con JSDoc. Nuevo `export type AuthMode = "dev" \| "external"`. `getMode(): Promise<AuthMode>` lee `request<{ mode: AuthMode }>`. Los métodos de soporte no se renombraron (D9 A). |
| `client/src/types/index.ts` | F2 | Re-export de `AuthMode`. |
| `client/src/hooks/usePublicConfig.ts` | F3 | `PublicBranding` → `PublicClientConfig` con `supportEnabled: boolean`; `EMPTY.supportEnabled = false`; el mapeo es `supportEnabled: cfg.support_enabled === true`; JSDoc actualizado ("nombre, logo y flags públicos"). La función interna pasó de `loadPublicBranding` a `loadPublicConfig` (no se exportaba). El hook conserva su nombre y su caché de módulo, y los consumidores (`UserMenu`, `DesignSystemShowcase`, etc.) compilan sin cambios. |
| `client/src/components/DevModeBanner.tsx` | F5 | `useState<AuthMode \| null>` (tipo importado de `@/lib/api`). El texto pasa a "MODO DESARROLLO — Autenticación simulada · No apto para producción". |
| `client/src/components/shell/SystemStatusLine.tsx` | F6 | **Sin cambios.** Compila e infiere `AuthMode`. |
| `client/src/app/dashboard/page.tsx` | F7-F11 | `const { supportEnabled } = usePublicConfig();`. El ítem "¿Bug o idea? Contanos" del dock se agrega con un spread condicional (mismo `title`, mismo ícono, orden Guía → Soporte → Tema). `GuideModal` recibe `onSupport={supportEnabled ? … : undefined}`. `<SoporteModal>` se monta solo si `supportEnabled`. `reportModal` no cambió. |
| `client/src/components/dashboard/SoporteModal.tsx` | F12, F16 | Título "Soporte"; subtítulo "Reportá un problema o seguí el estado de tus reportes."; botón "Ver todo en el portal de soporte". `FaroIcon` se queda (D9 A). El criterio de sigla de "Se va a reportar como…" usa ahora el helper compartido. Los únicos "Faro" que quedan son comentarios e identificadores (`openingFaro`, `handleAbrirFaro`, `obtenerLinkFaro`, `FaroIcon`); no hay texto visible con "Faro". |
| `client/src/components/UserMenu.tsx` | F16 | Muestra `DNI {user.dni}` y agrega ` · {sigla}` solo si `hasRealSigla(user.sigla)`. |
| `client/src/lib/format.ts` | F16 | Helper nuevo `hasRealSigla(sigla: string \| null \| undefined): boolean`: trim no vacío y distinto de `"-"`. Es el mismo criterio que tenía `SoporteModal`, extraído y reutilizado en los dos componentes. |
| `client/src/components/usb-guide/data.ts` | F14 | "Abrí la Terminal en la Mac y ejecutá…" (se sacó "del gabinete"). |
| `client/src/components/GuideModal.tsx` | F13 | **Sin cambios** (ver F0). |
| `client/src/components/design-system/DesignSystemShowcase.tsx` | F15 | **Sin cambios** (ver F0). |

## Contrato (sección 7): coincide con la SDD

- `GET /api/auth/mode` → `{ mode: AuthMode }` con `AuthMode = "dev" | "external"`, en `client/src/lib/api.ts` y re-exportado en `client/src/types/index.ts`.
- `GET /api/config/public` → `PublicConfig { organization_name: string | null; organization_logo_url: string | null; support_enabled: boolean }` (snake_case_lower, igual que `PublicConfigResponse(..., bool SupportEnabled)` serializado con `SnakeCaseLower`).
- Hook: `PublicClientConfig { organizationName, organizationLogoSrc, supportEnabled }`, con `supportEnabled = cfg.support_enabled === true`. Un backend viejo o un fetch fallido dan `false`.
- Rutas de soporte y métodos de `api` sin cambios (D9 A).

## Verificación

```
$ cd client && npx tsc --noEmit
tsc_exit=0   (sin salida)

$ npm run build   # en una copia de client/ en el scratchpad (rsync sin .next + node_modules copiado); copia borrada al terminar
▲ Next.js 16.2.10 (Turbopack)
✓ Compiled successfully in 3.3s
  Finished TypeScript in 4.8s ...
✓ Generating static pages using 6 workers (5/5) in 331ms
Route (app): ○ /  ○ /_not-found  ○ /dashboard  ○ /design-system

$ grep -rnIiE "\"mpf\"|'mpf'|\bmpf\b|PublicBranding" client/src                 → vacío (exit 1)
$ grep -rnIiE "gabinete|inform[aá]tica forense|uso oficial|fiscal[ií]a" client/src → vacío (exit 1)
$ grep -rnI "Faro - Sistema de tokens\|Ver todo en Faro" client/src             → vacío (exit 1)
```

- El `next dev` del usuario (puerto 3002) y su `.next` no se tocaron.
- ESLint no corre: el proyecto no tiene `eslint.config.*`, y ESLint 10 lo exige. Es preexistente y no lo agregué porque está fuera de scope.
- **Verificación visual en el navegador: no se hizo.** Mientras trabajaba, `implementer-backend` estaba modificando `server/` en paralelo, así que no había un backend estable con `support_enabled` para levantar. Además, levantar otro `next dev` sobre el checkout compartiría `.next` con el del usuario. Lo que se espera ver queda para la prueba manual del usuario o el reviewer (11.2/11.4):
  - Soporte apagado (default): el dock muestra solo "Guía de uso" y el tema; la guía no muestra "¿No conecta? Contactar soporte"; el `UserMenu` muestra "DNI 12345678" sin "· -" y conserva "Mi perfil de perito".
  - Con `Integrations__Support__Enabled=true`: el ítem aparece animado por el `AnimatePresence` existente del dock (H6) y sin mover el foco, el modal se titula "Soporte" y el botón dice "Ver todo en el portal de soporte".
  - Pie de la guía: "Guía de uso de Factum". Showcase: "Tribunal (deshabilitado)" (ver F0).

## Skills invocados

- **`ui-ux-pro-max`**, antes de los cambios de JSX. Consulté `"focus not obscured dynamic content" --domain ux`. La guía aplicable es no mover ni tapar el foco ante contenido que aparece tarde. Por eso el ítem del dock aparece sin `autoFocus` ni `focus()` (F11) y los textos nuevos son cortos, en el mismo estilo y tokens. No hubo cambios de layout ni de color.
- **`senior-frontend`**:
  - Helper puro extraído a `lib/format.ts` en vez de duplicar la condición.
  - Spread condicional en el array del dock.
  - Montaje condicional del modal en lugar de una prop de "deshabilitado".
  - Se mantienen la caché de módulo y el arranque del hook en `EMPTY`, para no generar desajustes de hidratación.
  - El helper devuelve `boolean` y no un type guard: con `sigla: string`, un guard `sigla is string` estrecha a `never` en la rama falsa.
- **`3d-web-experience`**, como criterio: sin hallazgos aplicables. No se agregó 3D, ni efectos pseudo-3D, ni animaciones nuevas. La aparición y desaparición del ítem del dock usa el `AnimatePresence` que ya existía y que respeta `reduce`.
- **`web-design-guidelines`**, como autochequeo final sobre `dashboard/page.tsx`, `UserMenu.tsx`, `SoporteModal.tsx`, `DevModeBanner.tsx` y `usb-guide/data.ts` (guía de Vercel bajada con curl):
  - Los ítems del dock siguen con `aria-label={item.title}` y el ícono de `SoporteModal` sigue con `aria-hidden`.
  - No hay `autoFocus` nuevo y no hay controles nuevos.
  - El subtítulo termina en punto.
  - La regla "Title Case" es para inglés, así que no aplica a la UI en español.
  - Sin hallazgos aplicables sobre las líneas cambiadas.
- `ui-styling` y `mblode-agent-skills-ui-animation`: no se invocaron, porque no hubo cambios de Tailwind/shadcn ni transiciones nuevas.

## Bloqueos

Ninguno. No hubo acciones rechazadas por permisos.
