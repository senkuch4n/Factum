# SDD: cierre de la serie de rediseño (borrar shadcn, `ui/` sin uso y tokens/clases legacy)

**Slug:** `rediseno-cierre-legacy`
**HU:** `docs/hu-rediseno-dashboard.md` (RDD paraguas). Esta SDD cubre **solo** la "Parte 4 — `rediseno-cierre-legacy`" de la "Partición propuesta" y las decisiones D2, D5 y D9 de esa HU. D1 a D11 están validadas con la opción recomendada (modo autónomo, 2026-10-01).
**Base:** el working tree de `feat/rediseno-dashboard-captura`, es decir, las partes 1 (`rediseno-dashboard-historial`) y 2 (`rediseno-dashboard-wizard`), que ya están commiteadas, más la parte 3 (`rediseno-dashboard-captura`), que está sin commitear y en revisión. También `rediseno-pagina-inicio` y `rediseno-base-primereact`, que ya están cerradas.
**Precondición de rama:** la parte 3 tiene que estar aprobada. Si todavía no entró a `develop`, la rama `feat/rediseno-cierre-legacy` sale de `feat/rediseno-dashboard-captura`, porque las ramas van encadenadas. Si cuando se implemente la parte 3 cambió algún archivo de los que lista esta SDD, **vale el grep**: el inventario de la sección 5 se vuelve a correr en T0 antes de borrar.
**Implementa:** solo `implementer-frontend` (Opus), app `client/`.

---

## 1. Resumen funcional

Esta HU cierra la serie de rediseño. Ahora que todas las páginas de `client/` (`/`, `/dashboard`, 404 y `/design-system`) usan el sistema de diseño nuevo (tokens `--fx-*` + PrimeReact unstyled), se borra lo que quedó sin consumidores:

- los componentes `ui/` de shadcn/aceternity/base-ui que ya no se usan, `ThemeToggle` y `DevModeBanner`;
- el Toaster de sonner y el `TooltipProvider` de base-ui;
- las dependencias npm `shadcn`, `class-variance-authority`, `tw-animate-css`, `@base-ui/react`, `sonner` y `motion`, y el archivo `components.json`;
- en `globals.css`, las variables y clases legacy, el bloque shadcn de `@layer base` y las keyframes sin uso;
- en `tailwind.config.ts`, los alias de compatibilidad shadcn, el color `brand` y la escala de radios legacy;
- en el showcase, las secciones "sonner" y "Convivencia legacy".

Los tres componentes de `ui/` que siguen en uso (`alert-dialog` → `ConfirmDialog`, `CopyButton` y `lens`) pasan a carpetas del sistema nuevo, así desaparece la carpeta `ui/`, que era el alias de shadcn.

Además se resuelven los pendientes que dejaron las HU anteriores:
- se agrega el token `rounded-fx-pill`. Hoy hay **7 usos** de esa clase que no generan CSS, y esas píldoras se ven con esquinas rectas;
- se corrige el orden de merge del pt de `inputtext`, para que los consumidores no necesiten `!pl-10`/`!pr-12`;
- se corrige "inspecciónes" en el contador del historial;
- la tabla del historial pasa a ordenar el "N° de causa" en orden natural (`2/26` antes que `10/26`).

No cambian datos, endpoints, contratos ni flujos. Visualmente solo cambian las píldoras del punto anterior, que pasan a ser redondeadas como pedía la SDD original, y diferencias imperceptibles de color en el fondo del `body` y en la barra de scroll (D6).

## 2. Toca

| Lado | ¿Toca? |
|---|---|
| backend (API) `server/src/Factum.Backend` | **no** |
| backend (Tatana) `server/src/Factum.Agent` | **no** |
| client `client/` | **sí** |
| agent-ui `agent-ui/` | **no** (no comparte CSS ni dependencias con `client/`) |

## 3. Modelo de datos / Endpoints / WebSocket

No aplica. Esta HU no toca colecciones, índices, endpoints, DTOs, mensajes WebSocket ni la base, ni escribe nada en `Storage`.

## 4. Contrato compartido

**Sin cambios: confirmado.** No se tocan `client/src/lib/api.ts`, `lib/agent.ts`, `lib/pericial.ts`, `types/` ni `hooks/*`. `api.getMode()` (`GET /api/auth/mode` → `{ mode: "dev" | "external" }`) lo sigue consumiendo `SystemStatusLine`; `DevModeBanner`, que era el otro consumidor, se borra (DP1).

---

## 5. Inventario real (grep de imports sobre el working tree de la parte 3)

El inventario se armó con un script que, para cada archivo `.ts/.tsx` de `client/src`, busca `from '…/<nombre>'` en el resto del árbol. Después se revisaron a mano los falsos positivos: por ejemplo, `ui/tooltip` vs `pt/tooltip`, que comparten el nombre `tooltip`. Las clases CSS se buscaron como token exacto dentro de literales de string (`"…"`, `'…'`, `` `…` ``) en todos los `.ts/.tsx`. Las variables, como `var(--x)` en `.ts/.tsx`.

### 5.1 Archivos a borrar (9 de código + `components.json`)

| Archivo | Consumidores hoy | Qué importa (lo que se libera) |
|---|---|---|
| `client/src/components/ThemeToggle.tsx` | **ninguno** (F14 de `rediseno-pagina-inicio`) | `framer-motion`, `lib/theme`, vars legacy (`--btn-secondary-bg`, `--border-md`, `--text-secondary`), clase `.btn-icon` |
| `client/src/components/DevModeBanner.tsx` | **ninguno** (DP1) | `framer-motion`, `api.getMode` |
| `client/src/components/ui/webcam-pixel-grid.tsx` | **ninguno** (F14) | `lib/utils` |
| `client/src/components/ui/folder-tree.tsx` | **ninguno** | vars legacy (`--text-*`, `--blue-lg`, `--bg-elevated`, `--border`) |
| `client/src/components/ui/accordion.tsx` | **ninguno** | `@base-ui/react/accordion`, vars legacy |
| `client/src/components/ui/floating-navbar.tsx` | **ninguno** | `motion/react` |
| `client/src/components/ui/background-paths.tsx` | **ninguno** | `motion/react` |
| `client/src/components/ui/tooltip.tsx` | `shell/AppProviders.tsx` (`TooltipProvider`) y `DesignSystemShowcase.tsx` (`Tip`), y los dos se sacan en esta HU (T4, T5) | `@base-ui/react/tooltip` |
| `client/src/components/shell/AppToaster.tsx` | `shell/AppProviders.tsx`, que se saca en esta HU (T4) | `sonner`, `lib/theme` |
| `client/components.json` | config del CLI de shadcn | — |

En total se borran 9 archivos de código más `components.json`. **No** se borran `ThemeProvider.tsx` ni `lib/theme.ts`: los usan `AppProviders`, `ThemeSwitch` y `useTheme` en varios lados.

### 5.2 Archivos que se mueven (DP2): desaparece `components/ui/`

| De | A | Imports a actualizar |
|---|---|---|
| `components/ui/alert-dialog.tsx` (exporta `ConfirmDialog`) | `components/overlay/ConfirmDialog.tsx` | `app/dashboard/page.tsx`, `components/ReportStep.tsx`, `components/design-system/DesignSystemShowcase.tsx` |
| `components/ui/CopyButton.tsx` | `components/feedback/CopyButton.tsx` | `components/CaseCard.tsx`, `components/ResultStep.tsx`, `DesignSystemShowcase.tsx` |
| `components/ui/lens.tsx` (exporta `Lens`) | `components/usb-guide/Lens.tsx` | `components/usb-guide/AndroidGuide.tsx` |

El contenido no cambia: se hace `git mv` y se actualiza solo la ruta del import. Hay que revisar si alguno importa con ruta relativa (`./`), porque al moverlo esa ruta se rompe; `tsc` lo detecta.

### 5.3 Dependencias npm

| Paquete | Importado por (hoy) | Después de esta HU | Acción |
|---|---|---|---|
| `shadcn` | nadie (es el CLI) | — | **desinstalar** |
| `class-variance-authority` | nadie | — | **desinstalar** |
| `tw-animate-css` | nadie (no hay `@import` en `globals.css` ni plugin en `tailwind.config.ts`) | — | **desinstalar** |
| `@base-ui/react` | `ui/tooltip.tsx`, `ui/accordion.tsx` | nadie | **desinstalar** |
| `sonner` | `shell/AppToaster.tsx`, `DesignSystemShowcase.tsx` | nadie | **desinstalar** |
| `motion` | `ui/floating-navbar.tsx`, `ui/background-paths.tsx` (`motion/react`) | nadie | **desinstalar** |
| `framer-motion` | `app/dashboard/page.tsx`, `DevModeBanner.tsx`, `ThemeToggle.tsx` | `app/dashboard/page.tsx` (transición entre pasos, D9 A) | **se queda** |
| `clsx`, `tailwind-merge` | `lib/utils.ts` | igual | se quedan |
| `lucide-react`, `primereact`, `next`, `react`, `react-dom` | varios | igual | se quedan |

### 5.4 `globals.css`: qué se borra y qué queda

Se borran porque no tienen consumidores fuera de los archivos de 5.1, ni siquiera en los comentarios de las partes 1 a 3:

- **Variables legacy** (bloques `:root` y `.dark` de las líneas ~213–318): `--bg-base`, `--bg-surface`, `--bg-elevated`, `--bg-hover`, `--border`, `--border-md`, `--border-accent`, `--text-primary`, `--text-secondary`, `--text-muted`, `--blue`, `--blue-lg`, `--cyan`, `--cyan-lg`, `--amber`, `--green`, `--red`, `--glow-*`, `--bg-card`, `--bg-input`, `--btn-*`, `--input-bg`, `--input-focus-bg`, `--card-hover-shadow`, `--gradient-text-*`, `--section-label-c` y `--scrollbar-thumb`. Las únicas lecturas `var(--…)` de estas variables en `.ts/.tsx` están en `ui/accordion`, `ui/folder-tree` y `ThemeToggle`, que se borran. En CSS, las leen las clases legacy que también se borran, más `body` y el scrollbar, que se migran (D6).
- **Clases legacy** (las de `.card` en adelante, líneas ~344–740): `.card`, `.card-hover`, `.btn`, `.btn-primary`, `.btn-secondary`, `.btn-danger`, `.btn-success`, `.btn-ghost`, `.btn-xl`, `.btn-sm`, `.btn-icon`, `.input`, `.input-error`, `textarea.input`, `.badge`, `.badge-blue|green|amber|red|gray` (y sus `.dark .badge-*`), `.dot-live`, `.dot-live-red|cyan|green|amber`, `.divider`, `.divider-accent`, `.recording-btn`, `.scanline-container`, `.section-label`, `.hero-title`, `.step-title`, `.phone-mock`, `.phone-mock-bar|header|row|row-active|row-value|row-tap|section`, `.stat-card`, `.webcam-overlay`, `.shutter-btn`, `.shutter-btn-inner`, `.explorer-overlay`, `.explorer-panel` y `.explorer-row`. Según el grep, los únicos usos que quedan son `card`, `btn-primary`, `btn-secondary`, `input` y `badge*` en la sección "Convivencia legacy" del showcase, que se borra (T5), y `btn-icon` en `ThemeToggle`, que también se borra. Los matches de `input` en `CaseFormStep.tsx:62`, `pt/calendar.ts:9` y `pt/inputtext.ts:6` son comentarios o claves de pt, no clases.
- **Keyframes legacy**: `ping`, `scan`, `recording-pulse`, `spin` y `fade-in-up`. `ping` y `spin` pisan las de Tailwind con el mismo nombre. `animate-ping` tiene 0 usos. `animate-spin` tiene 25 usos, pero Tailwind emite su propia `@keyframes spin`, que es idéntica, así que el resultado es el mismo. `fade-in-up`, `scan` y `recording-pulse` tienen 0 usos.
- **Bloque `@media (prefers-reduced-motion)` legacy** (`.dot-live::before, .recording-btn, .animate-pulse, .animate-ping, .scanline-container::after`): los 3 usos de `animate-pulse` (`CaptureStep.tsx:508`, `CameraRecordModal.tsx:280`, `AgentChip.tsx:48`) ya están detrás de `motion-safe:`, y `animate-ping` no tiene usos. No se pierde nada de accesibilidad.
- **Bloque shadcn `@layer base`** (líneas ~742–823): `.theme`, `:root`/`.dark` con `--background`, `--foreground`, `--card*`, `--popover*`, `--primary*`, `--secondary*`, `--muted*`, `--accent*`, `--destructive`, `--border`, `--input`, `--ring`, `--chart-*`, `--radius`, `--sidebar*`, más `* { @apply border-border }`, `body { @apply bg-background text-foreground }` y `html { @apply font-sans }`. El grep de `var(--background|foreground|primary|secondary|muted|accent|destructive|ring|chart|radius|sidebar|popover)` y de las clases `bg-background`, `text-foreground` y `border-border` en `.ts/.tsx` da **0**.
- **Base duplicada**: `*, *::before, *::after { box-sizing }` (ya lo hace el preflight) y `html { transition: color-scheme }` (no tiene efecto: `color-scheme` no se anima).

**Se quedan**, migrados a tokens (D6 y D7):
- `.dark { color-scheme: dark; }`.
- `body`: fuente, fondo, color, suavizado, `min-height` y transición de tema, ahora sobre `--fx-*`.
- `.font-mono`: hoy tiene 26 usos y su regla con `!important` pisa cualquier `font-family` del pt. Pasa a `var(--fx-font-mono)`.
- El scrollbar WebKit, ahora con token.
- Las keyframes `fx-icon-in`, `fx-rise-in` y `fx-fade-in`, los tokens `--fx-*` y el `@layer components` `.fx-*`, sin cambios.

### 5.5 `tailwind.config.ts`

| Entrada | Consumidores | Acción |
|---|---|---|
| `colors.brand` (`#003366` / `light`) | 0 (`bg-brand`, `text-brand`, `border-brand`…) | borrar |
| `colors.border` / `background` / `foreground` (alias shadcn) | 0 fuera del bloque `@layer base` que se borra | borrar y reemplazar el color de borde por defecto (D5) |
| `borderRadius` legacy `sm`, `DEFAULT`, `md`, `lg`, `xl`, `2xl`, `3xl` | **0** (`rounded`, `rounded-sm|md|lg|xl|2xl|3xl` y las variantes por lado no aparecen en ningún archivo que queda; solo hay `rounded-none`, `rounded-full` y `rounded-fx-*`) | borrar (D4). `none` y `full` siguen saliendo del default de Tailwind con los mismos valores |
| `borderRadius["fx-pill"]` | **7 usos que hoy no generan CSS**: `shell/SystemStatusLine.tsx:25,31`, `lib/prime/pt/button.ts:82` (badge), `DesignSystemShowcase.tsx:303` y `:967–970` (las 4 de 967–970 se van con la sección legacy) | **agregar** `"fx-pill": "var(--fx-radius-pill)"` (D3) |

### 5.6 Otros pendientes recogidos de los progress y reviews

| Origen | Pendiente | Dónde se resuelve |
|---|---|---|
| `rediseno-pagina-inicio` F14 (`history.md`, `review_rediseno-pagina-inicio.md`) | borrar `ThemeToggle.tsx` y `ui/webcam-pixel-grid.tsx` | T2 |
| historial, wizard y login (`impl_*`, `review_rediseno-dashboard-historial.md`) | el token `rounded-fx-pill` no existe | T7 y T8 (D3) |
| `rediseno-pagina-inicio` (`impl_frontend_rediseno-pagina-inicio.md`, "Decisiones no obvias") | por el orden de merge del pt de `inputtext`, los consumidores necesitan `!pl-10`/`!pr-12` | T9 y T10 (DP7) |
| historial (hallazgo 14) | "11 inspecci**ó**nes registradas" | T11 (DP8) |
| historial (hallazgo 14) | el "N° de causa" se ordena de forma lexicográfica | T11 (DP8) |
| captura ("Para la parte 4") | clases `.webcam-overlay`, `.shutter-btn*`, `.explorer-*`, `.dot-live*`, `.section-label`, `.btn*` | T6 |
| captura (hallazgo 1) | un `…:outline` suelto que pasa por `cn()` se pierde con tailwind-merge 3.6 + TW 3.4 | Solo informativo: esta HU no agrega clases `outline`. T9 usa `cn` para el pt de `inputtext`, y esa base ya incluye `outline-none` (el grep de T13 lo cubre) |
| historial DP2 | "la parte 4 decide si mueve `ConfirmDialog`" | T3 (DP2) |
| wizard (`Refactorizaciones/rediseno-dashboard-wizard.md`, archivos fuera de alcance) | "`constants/animations.ts`: la parte 4 decide" | T12. Hoy solo se usan `EASE` y `slideDir`; `spring`, `fadeSlide`, `fadeUp` y `scaleIn` no tienen usos |
| comentarios desactualizados | `layout.tsx:10` ("body legacy y bloque .theme de shadcn"), `globals.css` (cabecera, puntos 2a y 2e, y el comentario de z-index "modales legacy"), `AppProviders.tsx` (zIndex "modales legacy (50/60)" y AppToaster), `app/design-system/page.tsx:12` ("sonner, convivencia legacy"), `lib/utils.ts:8` ("el merge de las clases legacy"), `tailwind.config.ts` (comentario de radios "páginas no migradas"), `FxTip.tsx:15` ("misma firma que el `Tip` de base-ui") | T14 |

---

## 6. Decisiones técnicas

- **D1.** Esta HU **solo borra, mueve y migra a tokens**: no se rediseña nada. Lo único visible a propósito es D3. Todo lo demás tiene que verse igual que antes en `/`, `/dashboard`, 404 y `/design-system`, en los dos temas.
- **D2.** `framer-motion` **se queda** (D9 A de la HU). Después de esta HU su único consumidor es `app/dashboard/page.tsx`, para la transición entre pasos.
- **D3.** Las dependencias se sacan con `npm uninstall` desde `client/`, en un solo comando, para que `package.json` y `package-lock.json` queden coherentes. No se edita el lock a mano.
- **D4.** Antes de borrar se corre el grep (T0). Si aparece un consumidor nuevo que esta SDD no contempla (por ejemplo, porque la parte 3 cambió en revisión), ese archivo **no se borra**: se reporta en el progress y se sigue con el resto.
- **D5.** **Permisos de borrado.** En `rediseno-pagina-inicio`, el sistema de permisos le negó al implementador el `rm` de F14. Acá se usa `git rm` / `git mv`, que se pueden revertir desde git. Si el sistema de permisos lo niega igual, el implementador **no lo rodea**: deja en `progress/impl_frontend_rediseno-cierre-legacy.md` la lista exacta de comandos pendientes, sigue con las ediciones que no dependen del borrado y devuelve `blocked`. El orquestador le pide la autorización al usuario.

### Decisiones pendientes (modo autónomo: se toma la recomendada)

1. **DP1. `DevModeBanner`.** **(Recomendada, tomada) A: borrarlo.** `SystemStatusLine` ya muestra el chip "Dev" cuando `api.getMode()` devuelve `"dev"`, en todas las páginas. Además, el banner usa hex/rgba legacy y `framer-motion`. B: montarlo en el shell restilizado con tokens. Duplicaría el aviso y sumaría altura a la navbar.
2. **DP2. Los `ui/` que siguen en uso.** **(Recomendada, tomada) A: moverlos** (`ConfirmDialog` → `overlay/`, `CopyButton` → `feedback/`, `Lens` → `usb-guide/`) y borrar la carpeta `components/ui/`. `ui/` era la convención del CLI de shadcn (`components.json` → `"ui": "@/components/ui"`): si se dejan ahí, parece que shadcn sigue en uso. Son 7 líneas de import y `tsc` las verifica. B: dejarlos en `ui/`. Es cero churn, pero la carpeta queda como resto confuso.
3. **DP3. `rounded-fx-pill`.** **(Recomendada, tomada) A: agregar el token** `borderRadius["fx-pill"] = "var(--fx-radius-pill)"`, sumar `"fx-pill"` al grupo `rounded` de `extendTailwindMerge` en `lib/utils.ts` y **no** reemplazar los `rounded-full` que usaron las partes 1 a 3, que dan el mismo valor (9999px). Con esto, los 3 usos que quedan (`SystemStatusLine` ×2 y el badge de `pt/button`) se ven como píldora, que era el diseño de la SDD base. B: reemplazar `rounded-fx-pill` por `rounded-full` en esos 3 usos y no agregar el token. También es válido, pero `--fx-radius-pill` quedaría huérfana. C: además, migrar todos los `rounded-full` a `rounded-fx-pill`. Mucho churn sin cambio visible, y `rounded-full` también se usa para círculos (avatares, puntos), donde es el nombre correcto.
4. **DP4. Escala de radios legacy de `tailwind.config.ts`.** **(Recomendada, tomada) A: borrarla** (`sm`, `DEFAULT`, `md`, `lg`, `xl`, `2xl`, `3xl`) y dejar solo `fx-sm|md|lg|xl|pill`. `none` y `full` siguen saliendo del default de Tailwind. El grep da 0 consumidores, y si en el futuro alguien escribe `rounded-lg`, recibe el valor estándar de Tailwind en lugar de uno legacy. B: dejarla, porque es inofensiva. Pero es exactamente lo que este cierre tiene que limpiar.
5. **DP5. Color de borde por defecto.** Hoy `* { @apply border-border }` (bloque shadcn) le da a todo `border` sin color la variable legacy `--border`, que es casi igual a `--fx-border`. Hay 20 strings con `border`/`border-2`/`divide-y` sin color en el mismo literal: `CaseCard:54`, `CaptureStep:307,751`, `DeviceFileExplorer:268`, `IdentityCard` (`divide-y`), `EvidenceTray:35,97`, `CaptureRoleMenu:52`, `Mockups:58`, `AgentChip:13`, `ResumeDeviceModal:78`, `AgentStatusChip:47` y los pt `checkbox`, `tag`, `dropdown`, `button`, `shared` ×2, `inputtext` y `message`. Casi todos reciben el color por un `cn()` condicional, pero no se puede garantizar en todos los estados. **(Recomendada, tomada) A:** `theme.extend.borderColor = { DEFAULT: "var(--fx-border)" }` en `tailwind.config.ts`. En TW 3.4 el preflight usa `theme('borderColor.DEFAULT')` y `divideColor` hereda de `borderColor`, así que reemplaza al `* { border-border }` sin CSS extra y con un valor del sistema nuevo. B: `* { border-color: var(--fx-border) }` en `globals.css`. Es equivalente, pero queda fuera del config y no cubre `divide-*` sin color. C: no hacer nada. El default de Tailwind pasaría a ser `gray-200` (`#e5e7eb`), que en oscuro se vería como un borde claro: regresión visible.
6. **DP6. `body` y scrollbar.** **(Recomendada, tomada) A:**
   ```css
   body {
     font-family: var(--fx-font-sans);
     background-color: var(--fx-bg);
     color: var(--fx-text);
     -webkit-font-smoothing: antialiased;
     -moz-osx-font-smoothing: grayscale;
     min-height: 100vh;
     transition: background-color 0.25s, color 0.25s;
   }
   ::-webkit-scrollbar-thumb { background: var(--fx-border-strong); border-radius: 9999px; }
   ```
   (`::-webkit-scrollbar` y `-track` quedan igual). El fondo del `body` casi no se ve, porque las páginas pintan `bg-fx-bg` en su contenedor: `/`, 404, `/dashboard` y el showcase ya lo hacen. Para el scrollbar se usa `--fx-border-strong`, el token de "límite de control" (≥ 3:1), y no `--fx-border`, porque en oscuro `--fx-border` (`#2c3036`) es más tenue que el legacy (`#3f3f46`) y el thumb casi no se vería. B: `--fx-border`, más fiel al gris claro legacy en modo claro, pero queda tenue en oscuro.
7. **DP7. Orden de merge del pt de `inputtext` (`!pl-10`).** Prime arma `mergeProps({ className: classNames(props.className, cx('root')) }, ptm('root'))` con `classNameMergeFunction: cn`. El pt global queda **último**, así que tailwind-merge descarta un `pl-10` del consumidor frente al `px-3` del pt. **(Recomendada, tomada) A:** en `pt/inputtext.ts`, que el `root` del pt reaplique la clase del consumidor al final: `root: ({ props, context }) => ({ className: cn(inputRootClasses({ props, context }), props.className) })`. Como `cn` deduplica, gana el consumidor. Lo mismo en `pt/inputtextarea.ts`, cuidando que `"resize-none min-h-[5rem]"` vaya antes de `props.className`. **No** se cambia `inputRootClasses` en sí, porque `calendar.ts` la llama con los props del `Calendar`, cuyo `className` es el del wrapper. Después se sacan los `!` de los 4 consumidores (`CaseHistory.tsx:208` `!pl-10 !pr-10`, `login/LoginForm.tsx:29` `!pl-10`, `form/FxPassword.tsx:12` `!pr-12` y `DesignSystemShowcase.tsx:542` `!pl-10`) y se actualizan sus comentarios. `Password` le pasa `inputClassName` a su `InputText` interno como `className`, y ese `InputText` toma el pt global de `inputtext`, así que el fix también lo cubre. **Hay que verificarlo midiendo** (T10). **Fallback**, si la medición no da: se revierten T9 y T10, se dejan los `!` y en `pt/inputtext.ts` se documenta la convención ("un override de padding del consumidor lleva `!`"). B: dejar los `!` como convención desde el principio. Es más simple, pero es un pie en falso para cada consumidor nuevo.
8. **DP8. Errores preexistentes del historial.** **(Recomendada, tomada) A: corregirlos acá**, porque son dos cambios de pocas líneas en `CaseHistory.tsx`. El texto del conteo pasa a `cases.length === 1 ? "1 inspección registrada" : \`${cases.length} inspecciones registradas\``. El orden de la tabla usa un `Intl.Collator("es", { numeric: true, sensitivity: "base" })` para las claves de texto (`causa`, `caratula`), así `2/26` va antes que `10/26` y las tildes no alteran el orden. Las claves numéricas (`fecha`) y `estado` siguen como hoy. B: dejarlos para una tarea aparte. Para dos líneas, sería otro ciclo completo.

---

## 7. Checklist atómico, solo `client/` (`implementer-frontend`)

Antes de empezar: leer `client/AGENTS.md` y `node_modules/next/dist/docs/` si hace falta (Next 16). Esta HU no usa APIs nuevas de Next. Skills obligatorios: `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` (como criterio, sin 3D) y `web-design-guidelines` como autochequeo final. En una HU sin rediseño se invocan igual y se deja constancia en el progress. Esta HU no toca la base. **No tocar `backlog.json` ni `progress/current.md`.**

> Para todos los greps se usa `bash -c '…'`, porque en zsh `--include=*.ts` falla con "no matches found".

**Precondición**
- [ ] T0. Volver a correr el inventario sobre el estado actual de la rama. Las dos líneas de abajo tienen que dar **solo** los consumidores que lista la sección 5.1. Si aparece otro, se aplica D4.
  ```bash
  cd client && bash -c 'grep -rnE "ThemeToggle|DevModeBanner|webcam-pixel-grid|folder-tree|ui/accordion|floating-navbar|background-paths|ui/tooltip|AppToaster" src --include="*.ts" --include="*.tsx"'
  cd client && bash -c 'grep -rnE "from .(sonner|@base-ui/react|class-variance-authority|motion/react|motion\W|tw-animate-css|shadcn)" src'
  ```

**Borrados y movimientos**
- [ ] T1. `git rm client/components.json`.
- [ ] T2. `git rm` de `client/src/components/ThemeToggle.tsx`, `client/src/components/DevModeBanner.tsx`, `client/src/components/ui/webcam-pixel-grid.tsx`, `client/src/components/ui/folder-tree.tsx`, `client/src/components/ui/accordion.tsx`, `client/src/components/ui/floating-navbar.tsx` y `client/src/components/ui/background-paths.tsx`.
- [ ] T3. `git mv` de `ui/alert-dialog.tsx` → `overlay/ConfirmDialog.tsx`, de `ui/CopyButton.tsx` → `feedback/CopyButton.tsx` y de `ui/lens.tsx` → `usb-guide/Lens.tsx`. Actualizar los 7 imports de la tabla 5.2. Ningún export cambia de nombre.
- [ ] T4. `shell/AppProviders.tsx`: sacar los imports y el montaje de `TooltipProvider` y `AppToaster`. El árbol queda `ThemeProvider → PrimeReactProvider → FxToastProvider → {children, SystemStatusLine}`. Actualizar el comentario (sin sonner/base-ui; zIndex "por encima de la navbar (40)", sin "modales legacy"). Después: `git rm client/src/components/ui/tooltip.tsx client/src/components/shell/AppToaster.tsx`. La carpeta `components/ui/` tiene que quedar vacía y desaparecer.
- [ ] T5. `DesignSystemShowcase.tsx`:
  - sacar `import { toast as sonner } from "sonner"` y `import { Tip } from "@/components/ui/tooltip"`;
  - en `SECTIONS`, quitar las entradas `sonner` y `legacy`;
  - borrar la `<Section id="sonner">` y la `<Section id="legacy">` completas;
  - en `AppNavbar.actions`, cambiar `Tip` → `FxTip` (misma firma);
  - cambiar `onLogout` y las dos llamadas `sonner(\`Abriste…\`)` de `DEMO_CARDS` por `fxToast.info(…)`, que ya está en scope (`useFxToast`);
  - renumerar los comentarios de sección ("8. Footer" → "6. Footer");
  - sacar `!` de `className="h-12 !pl-10"`, pero solo si T10 da OK.

  `app/design-system/page.tsx:12`: actualizar el comentario (sin "sonner, convivencia legacy").

**`globals.css`**
- [ ] T6. Borrar todo lo que lista la sección 5.4 como "se borran". Reescribir la sección "BASE" con D6 y D7: `.dark { color-scheme: dark; }`, el `body` de DP6, `.font-mono { font-family: var(--fx-font-mono) !important; }` y el scrollbar de DP6. Esa sección va **después** de `@tailwind utilities` y de las keyframes `fx-*`, en el mismo lugar relativo que hoy, para que `.font-mono` siga ganando. Actualizar la cabecera de precedencia: quitar los puntos 2a ("+ el bloque shadcn") y 2e (clases legacy); agregar "e. Base de documento (`body`, `.font-mono`, scrollbar), después de utilities". En el comentario de `--fx-z-*`, sacar "modales legacy z-50/60". El archivo tiene que quedar sin `@layer base` y sin `@apply`.

**`tailwind.config.ts` y `lib/utils.ts`**
- [ ] T7. `tailwind.config.ts`: borrar `colors.brand`, el comentario "shadcn/tailwind.css compatibility aliases" y `colors.border`, `background` y `foreground`. Agregar `borderColor: { DEFAULT: "var(--fx-border)" }` dentro de `extend` (DP5). En `borderRadius`, dejar solo `"fx-sm"`, `"fx-md"`, `"fx-lg"`, `"fx-xl"` y el nuevo `"fx-pill": "var(--fx-radius-pill)"` (DP3 y DP4), y actualizar el comentario de radios.
- [ ] T8. `lib/utils.ts`: en el grupo `rounded` de `extendTailwindMerge`, agregar `"fx-pill"`. Actualizar el comentario de la línea 8 (sin "clases legacy").

**Merge del pt de inputs (DP7)**
- [ ] T9. `lib/prime/pt/inputtext.ts`: `root` → `cn(inputRootClasses({ props, context }), props.className)`. `lib/prime/pt/inputtextarea.ts`: `cn(inputRootClasses(...), "resize-none min-h-[5rem]", props.className)`. Comprobar que el tipo de `props` en el callback de pt expone `className` (en `InputTextProps` y `InputTextareaProps` lo hace). Si TS no lo infiere, se tipa el parámetro de forma explícita. **No** se usa `any`.
- [ ] T10. Sacar los `!` de `CaseHistory.tsx:208` (`pl-10 pr-10`), `login/LoginForm.tsx:29` (`pl-10`), `form/FxPassword.tsx:12` (`pr-12`) y el showcase (`pl-10`), y actualizar los comentarios que explicaban el `!` (`LoginForm.tsx:27-28`, `FxPassword.tsx:9-11`). **Medición obligatoria** con DevTools o Playwright (`getComputedStyle(input).paddingLeft/Right`): login DNI y Usuario 40/12 px, Contraseña 40/48 px y el buscador del historial 40/40 px, igual que con `!`. Si no da, aplicar el fallback de DP7 y dejarlo anotado.

**Historial (DP8)**
- [ ] T11. `CaseHistory.tsx`: el contador queda `cases.length === 1 ? "1 inspección registrada" : \`${cases.length} inspecciones registradas\``. El caso 0 sigue mostrando "Aún no hay inspecciones". En `displayRows`, si `va` y `vb` son `string`, se compara con un `Intl.Collator("es", { numeric: true, sensitivity: "base" })` a nivel de módulo; si no, la comparación sigue como hoy. Se pueden sacar los `.toLowerCase()` de los accessors `causa` y `caratula`, porque `sensitivity: "base"` ya ignora mayúsculas y tildes.

**Limpieza menor**
- [ ] T12. `constants/animations.ts`: dejar solo `EASE` y `slideDir`, los únicos que usa `app/dashboard/page.tsx`. Antes de borrar `spring`, `fadeSlide`, `fadeUp` y `scaleIn`, confirmar con grep que tienen 0 usos.
- [ ] T13. Desinstalar las dependencias (desde `client/`):
  ```bash
  npm uninstall shadcn class-variance-authority tw-animate-css @base-ui/react sonner motion
  ```
  Verificar que `package.json` conserva `framer-motion`, `clsx`, `tailwind-merge`, `lucide-react`, `primereact`, `next`, `react` y `react-dom`, y que `npm ls --depth=0` no muestra `missing`, `invalid` ni `extraneous`.
- [ ] T14. Actualizar los comentarios desactualizados de la última fila de 5.6: `layout.tsx:10` dice que `--font-sans` "lo leen `body` (`--fx-font-sans`) y Tailwind `font-sans`"; también `FxTip.tsx:15`, `AppProviders.tsx`, `globals.css`, `tailwind.config.ts`, `utils.ts` y `design-system/page.tsx`. Solo se tocan comentarios: no cambia código.

**Verificación y cierre**
- [ ] T15. Verificación técnica (sección 8): `tsc`, `build` y greps de cero referencias.
- [ ] T16. Recorrido visual en oscuro y claro de `/`, `/dashboard` (historial en las 3 vistas, wizard pasos 1 a 6, diálogos de soporte, guía, perfil, confirmación y cámara/explorador si hay agente), 404 y `/design-system`. Comparar con capturas de antes de T1: se sacan al empezar y se guardan en el scratchpad. Solo puede cambiar lo de D3 (`SystemStatusLine` y el badge de botón ahora redondeados) y el thumb del scrollbar. Revisar en especial los bordes sin color de DP5 (tarjetas de caso, chips del agente, bandeja y tiles de captura, checkbox, tag, dropdown y message) y que no aparezca ningún borde claro en oscuro.
- [ ] T17. Escribir `progress/impl_frontend_rediseno-cierre-legacy.md` con:
  - archivos borrados, movidos y editados;
  - la salida de los greps de la sección 8;
  - `tsc` y `build`;
  - el resultado de la medición de T10, o del fallback;
  - skills invocados ("sin hallazgos aplicables" donde corresponda);
  - comandos pendientes, si hubo bloqueo de permisos (D5).

  Devolver `done -> progress/impl_frontend_rediseno-cierre-legacy.md`.

## 8. Verificación

Comandos que corre el implementador antes de declarar `done` (desde la raíz del repo):

```bash
# 1. Tipos y build
cd client && npx tsc --noEmit
cd client && npm run build          # rutas /, /_not-found, /dashboard, /design-system OK

# 2. Cero referencias a lo borrado (cada línea tiene que dar 0 resultados)
cd client && bash -c 'grep -rnE "sonner|@base-ui|class-variance-authority|tw-animate|shadcn|motion/react|components/ui/|ThemeToggle|DevModeBanner|AppToaster|TooltipProvider|webcam-pixel-grid|folder-tree|floating-navbar|background-paths" src package.json tailwind.config.ts postcss.config.js next.config.ts'
cd client && test ! -e components.json && test ! -d src/components/ui && echo "OK sin components.json ni ui/"
cd client && bash -c 'grep -nE "^\s*--(bg|text|blue|cyan|amber|green|red|glow|btn|input|card|gradient|section|scrollbar|background|foreground|primary|secondary|muted|accent|destructive|ring|chart|radius|sidebar|popover|border)[a-z0-9-]*:" src/app/globals.css'
cd client && bash -c 'grep -nE "var\(--(bg|text|blue|cyan|amber|green|red|glow|btn|input-|card|gradient|section|scrollbar|border|background|foreground)[a-z0-9-]*\)" src/app/globals.css'
cd client && bash -c 'grep -nE "@layer base|@apply|\.(card|btn|input|badge|dot-live|divider|recording-btn|scanline|section-label|hero-title|step-title|phone-mock|stat-card|webcam-overlay|shutter-btn|explorer-)|@keyframes (ping|scan|recording-pulse|spin|fade-in-up)\b" src/app/globals.css'
cd client && bash -c 'grep -rnE "var\(--(bg|text|blue|cyan|amber|green|red|glow|btn|border|input|card|gradient|section|scrollbar|background|foreground|primary|secondary|muted|destructive|ring|chart|radius|sidebar|popover)[a-z0-9-]*\)" src --include="*.ts" --include="*.tsx"'
cd client && bash -c 'grep -rnE "(^|[^a-zA-Z0-9])!p[lr]-" src --include="*.ts" --include="*.tsx"'   # 0 si DP7 A funcionó
cd client && bash -c 'grep -rn "inspección\${" src'                                                   # 0
cd client && bash -c 'grep -nE "brand|compatibility|\"(border|background|foreground)\":" tailwind.config.ts'  # 0

# 3. Dependencias
cd client && npm ls --depth=0       # sin missing/invalid/extraneous; framer-motion presente
```

Lo que sí tiene que seguir apareciendo: `framer-motion` en `app/dashboard/page.tsx`, `rounded-fx-pill` en `SystemStatusLine.tsx` y en `pt/button.ts` (y ahora genera CSS: se puede comprobar con el build o en DevTools que `border-radius` da 9999px) y `.font-mono` en `globals.css`.

`./ops/harness/verify.sh` corre `tsc` en `client/` porque hay cambios sin commitear, y lo corre el orquestador.

**Prueba manual que queda para el usuario:**
1. `/` en oscuro y claro: el login se ve igual. Mostrar/ocultar contraseña funciona y el ícono no se superpone al texto en DNI, Usuario y Contraseña. La píldora de versión de abajo a la izquierda ahora es redondeada.
2. `/dashboard`: el historial en lista, tabla y cuadrícula se ve igual. El buscador tiene el ícono a la izquierda sin pisar el texto. El contador dice "1 inspección registrada" / "N inspecciones registradas". En la tabla, al ordenar por "N° de causa", `2/26` aparece antes que `10/26`.
3. Wizard pasos 1 a 6 con Tatana real: sin cambios visuales. Bordes de tarjetas, chips y tiles del mismo tono que antes, en los dos temas. Toasts de Prime abajo a la derecha. Tooltips (`FxTip`) con hover y foco.
4. 404 y `/design-system` (solo en dev): ya no están las secciones "Notificaciones y tooltips (sonner)" ni "Convivencia legacy". Las demás demos funcionan, y el botón "+" de la navbar demo muestra su tooltip.
5. Con el backend en modo `dev`, aparece el chip "Dev" en la línea de estado y no hay ningún banner extra.
