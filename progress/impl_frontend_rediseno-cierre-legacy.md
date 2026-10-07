# impl_frontend — rediseno-cierre-legacy

**Estado:** done
**App:** solo `client/` (no se tocó `agent-ui/` ni `server/`). Sin commit. No se tocó `backlog.json` ni `progress/current.md`.
**SDD:** `Refactorizaciones/rediseno-cierre-legacy.md`. DP1 a DP8 en la opción A. DP7 A **funcionó**: la medición dio OK, así que no hizo falta el fallback.

## T0: inventario

Los dos greps de T0 dieron solo los consumidores de la sección 5.1, sin consumidores nuevos. El único hallazgo extra fue la prop `showThemeToggle` de `AppNavbar`, que aparece por el patrón `ThemeToggle`. Es una prop, no el componente. Ver la decisión 1.
Las clases legacy se buscaron como token exacto en literales de `.ts/.tsx`. También se buscaron nombres armados dinámicamente (`badge-${…}`, `btn-${…}`, `dot-live-${…}`, etc.) y dieron 0. Los únicos matches de `input`/`badge`/`font-sans` están en comentarios o son clases válidas de Tailwind.
La escala de radios legacy tiene 0 consumidores. Los tokens `rounded*` que quedan en el código son `rounded-full`, `rounded-none`, `rounded-fx-*`, `rounded-l-fx-xl` y los arbitrarios `rounded-[…]`.

## Archivos

**Borrados (`git rm`)**: `client/components.json`, `src/components/ThemeToggle.tsx`, `src/components/DevModeBanner.tsx`, `src/components/ui/{webcam-pixel-grid,folder-tree,accordion,floating-navbar,background-paths,tooltip}.tsx` y `src/components/shell/AppToaster.tsx`. La carpeta `components/ui/` ya no existe. Ningún borrado fue denegado por permisos.

**Movidos (`git mv`, contenido sin cambios)**:
- `ui/alert-dialog.tsx` → `overlay/ConfirmDialog.tsx`. Imports actualizados en `app/dashboard/page.tsx`, `ReportStep.tsx` y `DesignSystemShowcase.tsx`.
- `ui/CopyButton.tsx` → `feedback/CopyButton.tsx`. Imports actualizados en `CaseCard.tsx`, `ResultStep.tsx` y el showcase.
- `ui/lens.tsx` → `usb-guide/Lens.tsx`. Import actualizado en `AndroidGuide.tsx`.
- Ninguno de los tres usaba imports relativos.

**Editados**:
- `shell/AppProviders.tsx` (T4): sin `TooltipProvider` ni `AppToaster`. El árbol queda ThemeProvider → PrimeReactProvider → FxToastProvider → {children, SystemStatusLine}. Comentarios actualizados.
- `design-system/DesignSystemShowcase.tsx` (T5):
  - sin imports de sonner ni de `ui/tooltip`;
  - se sacaron las entradas `sonner` y `legacy` de `SECTIONS`, junto con sus dos `<Section>`;
  - `Tip` pasa a `FxTip`;
  - `onLogout` y las tarjetas demo usan `fxToast.info`;
  - el comentario "8. Footer" pasa a "6. Footer";
  - `!pl-10` pasa a `pl-10`.
- `app/design-system/page.tsx`: comentario actualizado.
- `app/globals.css` (T6): de 823 a 233 líneas.
  - Se borraron las variables legacy de `:root`/`.dark`, todas las clases legacy, las keyframes `ping`/`scan`/`recording-pulse`/`spin`/`fade-in-up`, el bloque `prefers-reduced-motion` legacy, el `@layer base` de shadcn y la base duplicada.
  - Al final, después de utilities y de las keyframes `fx-*`, hay una sección nueva "BASE DE DOCUMENTO" con `.dark { color-scheme }`, el `body` sobre `--fx-*` (DP6), `.font-mono { font-family: var(--fx-font-mono) !important }` y el scrollbar con `--fx-border-strong`.
  - Cabecera de precedencia actualizada: el punto 2a ya no menciona shadcn y el 2e es la base de documento. Comentario de z-index sin "modales legacy".
  - El archivo no tiene `@layer base` ni `@apply`.
- `tailwind.config.ts` (T7):
  - sin `colors.brand` y sin los alias `border`/`background`/`foreground`;
  - nuevo `extend.borderColor.DEFAULT = var(--fx-border)` (DP5);
  - `borderRadius` queda solo con `fx-sm|md|lg|xl` y el nuevo `fx-pill`; se borró la escala legacy, incluidos `none`/`full`, que salen del default de TW;
  - comentario de radios actualizado.
- `lib/utils.ts` (T8): `fx-pill` agregado al grupo `rounded` del merge. Comentario actualizado.
- `lib/prime/pt/inputtext.ts` y `inputtextarea.ts` (T9): el `root` del pt reaplica `props.className` al final: `cn(inputRootClasses(…), props.className)`. En textarea va `…, "resize-none min-h-[5rem]", props.className`. `inputRootClasses` no se tocó (la sigue usando `calendar.ts`). TS infiere `props.className` sin `any`. Se documentó el porqué en el JSDoc.
- `login/LoginForm.tsx`, `form/FxPassword.tsx`, `CaseHistory.tsx` y el showcase (T10): sin `!`, y con los comentarios del `!` reescritos.
- `CaseHistory.tsx` (T11):
  - el contador dice "1 inspección registrada" / "N inspecciones registradas"; el caso 0 no cambia;
  - `TEXT_COLLATOR = new Intl.Collator("es", { numeric: true, sensitivity: "base" })` a nivel de módulo y se usa cuando `va`/`vb` son string;
  - se sacaron los `.toLowerCase()` de los accessors `causa`/`caratula`.
- `constants/animations.ts` (T12): quedan solo `EASE` y `slideDir`. Se comprobó por grep que `spring`, `fadeSlide`, `fadeUp` y `scaleIn` tenían 0 usos.
- `package.json` / `package-lock.json` (T13): `npm uninstall shadcn class-variance-authority tw-animate-css @base-ui/react sonner motion`.
- Comentarios (T14): `layout.tsx` (`--font-sans` lo leen `body` vía `--fx-font-sans` y Tailwind `font-sans`), `FxTip.tsx` (sin la mención a base-ui), `AppProviders.tsx`, `globals.css`, `tailwind.config.ts`, `utils.ts` y `design-system/page.tsx`.
- `shell/AppNavbar.tsx`, `app/dashboard/page.tsx` y el showcase: prop `showThemeToggle` → `showThemeSwitch`. Ver la decisión 1.

## Decisiones no obvias

1. **Renombre de la prop `showThemeToggle` → `showThemeSwitch`.** El primer grep de la sección 8 exige 0 matches de `ThemeToggle` y la prop de `AppNavbar` lo hacía fallar. La prop ya renderiza `<ThemeSwitch />`, así que el nombre nuevo es más fiel. El cambio son 6 líneas en 3 archivos, no tiene impacto visual y `tsc` lo verifica. Es lo único que se hizo fuera del checklist literal.
2. El collator también se aplica a `estado`, porque T11 dice "si `va` y `vb` son string". Los valores de `status` son identificadores ASCII en minúscula, así que el orden no cambia en la práctica.
3. Para el build y la medición **no** se usó el checkout principal: se copió `client/` al scratchpad (con `node_modules` clonado por APFS) y el build corrió ahí. Para "antes" se armó otra copia de `HEAD` con `git archive` y `npm ci`. Las dos se levantaron con `next dev` en los puertos 3461/3462 y después se apagaron.

## Verificación

### tsc
`cd client && npx tsc --noEmit` → sin errores. Se volvió a correr después del último cambio.

### build
Se corrió en una copia del scratchpad con las fuentes finales: `npm run build` → `✓ Compiled successfully`, rutas `○ /`, `○ /_not-found`, `○ /dashboard` y `○ /design-system`.
En el CSS compilado se ve:
- `.rounded-fx-pill{border-radius:var(--fx-radius-pill)}`;
- el preflight como `*,:before,:after{box-sizing:border-box;border-style:solid;border-width:0;border-color:var(--fx-border)}`;
- `body{font-family:var(--fx-font-sans);background-color:var(--fx-bg);…}`, `.font-mono{font-family:var(--fx-font-mono)!important}` y el thumb con `var(--fx-border-strong)`;
- 0 apariciones de `--bg-base`/`--background`.

### Greps de la sección 8 (todos con 0 resultados)
- `sonner|@base-ui|…|background-paths` sobre src, package.json, tailwind.config.ts, postcss.config.js y next.config.ts → 0 (después del renombre de la decisión 1).
- `OK sin components.json ni ui/` → impreso.
- Variables legacy/shadcn declaradas en globals.css → 0.
- `var(--legacy)` en globals.css → 0.
- `@layer base|@apply|.card|.btn|…|@keyframes (ping|scan|…)` en globals.css → 0.
- `var(--legacy)` en `.ts/.tsx` → 0.
- `!pl-`/`!pr-` en `.ts/.tsx` → 0.
- `inspección${` → 0.
- `brand|compatibility|"border|background|foreground":` en tailwind.config.ts → 0.

### npm ls --depth=0
Sin missing, invalid ni extraneous. Quedan: @types/node, @types/react-dom, @types/react, autoprefixer, clsx, framer-motion@12.42.2, lucide-react, next@16.2.10, postcss, primereact, react, react-dom, tailwind-merge, tailwindcss@3.4.19 y typescript.

### Medición T10 (Playwright, `getComputedStyle`, antes = HEAD / después = working tree, oscuro y claro)
| Campo | Antes (con `!`) | Después (sin `!`) |
|---|---|---|
| Login DNI | 40px/12px | 40px/12px |
| Login Usuario | 40px/12px | 40px/12px |
| Login Contraseña | 40px/48px | 40px/48px |
| Buscador del historial | 40px/40px | 40px/40px |
| Showcase `#ds-dni` | 40px/12px | 40px/12px |

DP7 A funciona y no se aplicó el fallback. Para el historial se usó un backend mockeado con `page.route`: `/api/auth/me`, `/api/cases` con 4 casos ficticios y `/api/auth/mode = dev`. No se tocó la base.

### DP8
- Contador: antes "4 inspecci**ó**nes registradas", después "4 inspecciones registradas".
- Orden por "N° de causa", ascendente: antes `1/26, 10/26, 2/26, 21/26`, después `1/26, 2/26, 10/26, 21/26`. Descendente: `21/26, 10/26, 2/26, 1/26`.

### T16: comparación visual automática (diff de píxeles, umbral 12/255, 1280×900, full page, oscuro y claro)
- `/` (login) y 404: la única diferencia es la píldora de versión/Dev de `SystemStatusLine`, abajo a la izquierda. Ahora tiene `border-radius` 9999px; antes tenía 0px (D3).
- `/dashboard`, historial en lista: solo cambian el texto del contador y la píldora. Las tarjetas de caso, sus bordes y el buscador son idénticos dentro del umbral.
- `/dashboard`, historial en tabla: solo cambian el contador, la píldora y el orden de las filas (DP8).
- `/design-system`: las píldoras del índice de secciones ahora son redondeadas (D3, el uso de `:303`), ya no están las dos secciones borradas y el resto, de y=290 a y=6828, es idéntico píxel a píxel. Eso incluye todos los componentes Prime con borde sin color (checkbox, tag, dropdown, message).
- `body` bg: oscuro `rgb(10,10,10)` → `rgb(11,12,14)`, claro `rgb(250,250,250)` → `rgb(246,247,248)`. Es la diferencia imperceptible de D6 y las páginas lo tapan con `bg-fx-bg`.
- **No se cubrió automáticamente:** el wizard con Tatana real (pasos 3 a 6, cámara y explorador) y los diálogos de soporte, guía y perfil. Quedan para la prueba manual del usuario, como dice la sección 8 de la SDD. Por construcción, el riesgo está en los bordes sin color de DP5, que ahora usan `--fx-border` en vez del `--border` legacy: en oscuro pasan de `#27272a` a `#2c3036` y en claro de `#e4e4e7` a `#d9dde1`. Son tonos casi iguales y no hay ningún borde claro en oscuro.

Capturas y diffs en el scratchpad de la sesión (`shots-before/`, `shots-after/`, `diffs/`).

## Skills invocados
- **ui-ux-pro-max**: invocado. Esta HU no rediseña nada. Se aplicaron las prioridades de accesibilidad y consistencia: tokens semánticos en vez de hex (`body` y scrollbar), y un thumb del scrollbar con `--fx-border-strong` (≥ 3:1) para que no desaparezca en oscuro (DP6). No hubo otros hallazgos aplicables.
- **senior-frontend**: invocado. Collator a nivel de módulo, para no instanciarlo en cada sort. Se sacaron dependencias muertas del bundle (`motion`, `sonner`, `@base-ui/react`) y el orden de merge del pt se resolvió en un solo lugar en vez de con `!` en cada consumidor. No hubo otros hallazgos aplicables.
- **3d-web-experience** (como criterio): invocado. Se borraron las animaciones legacy sin propósito: `scan`/scanline, `recording-pulse`, `ping` y `fade-in-up`, más los componentes `background-paths`/`floating-navbar` de `motion`. Ya no quedan efectos pseudo-3D. Las animaciones que quedan (`fx-*`, y framer-motion en la transición del wizard) están detrás de `motion-safe:`. No se agregó 3D.
- **web-design-guidelines** (autochequeo final sobre los archivos tocados): invocado y lista de reglas descargada.
  - La transición del `body` lista propiedades explícitas, sin `transition: all`.
  - El input del pt conserva `outline-none` con reemplazo `focus-visible:outline-*`.
  - `FxTip` en la navbar demo, con `aria-label` en el botón ícono.
  - Los labels de los inputs no cambiaron.
  - En lo tocado no hay hallazgos aplicables. El contador del historial no tiene `aria-live`, pero ya era así y queda fuera de alcance.
- `ui-styling` y `mblode-agent-skills-ui-animation`: no se invocaron. Esta HU no agrega componentes shadcn ni transiciones nuevas.

## Contrato compartido
Sin cambios, como dice la SDD. No se tocaron `lib/api.ts`, `lib/agent.ts`, `lib/pericial.ts`, `types/` ni `hooks/*`. `api.getMode()` lo sigue consumiendo `SystemStatusLine`.

## Pendientes / bloqueos
Ninguno. No hubo denegaciones de permisos.

## Prueba manual para el usuario
Ver la sección 8 de la SDD: wizard con Tatana real, en especial los bordes de tiles, chips y bandeja en los dos temas, los diálogos y los tooltips con hover y foco.
