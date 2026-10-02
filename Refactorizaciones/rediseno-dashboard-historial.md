# SDD: Rediseño de `/dashboard`, parte 1: modo historial y piezas globales

**Slug:** `rediseno-dashboard-historial`
**HU:** `docs/hu-rediseno-dashboard.md` (RDD paraguas). Esta SDD cubre **solo** la "Parte 1 — `rediseno-dashboard-historial`" de la "Partición propuesta", con sus Gherkin (bloques "Transversal" y "Parte 1") y la UX de "Layout general", "Modo historial", "Estados de error y feedback", "Movimiento" y "Responsive". Validada en modo autónomo: D1 a D11 con la opción recomendada.
**Base:** `Refactorizaciones/rediseno-base-primereact.md` (PrimeReact 10.9.9 unstyled + pt `fxPassThrough`, tokens `--fx-*`, shell) y `Refactorizaciones/rediseno-pagina-inicio.md` (pt `password` y `message`, keyframes `fx-rise-in`/`fx-fade-in`, patrón "campo con ícono").
**Precondición de rama:** `auth-e-integraciones-sin-mpf` (commit `0333518`) y `rediseno-pagina-inicio` ya mergeadas. La rama `feat/rediseno-dashboard-historial` sale de la de `rediseno-pagina-inicio` si esta todavía no entró a `develop` (ramas encadenadas).
**Implementa:** solo `implementer-frontend` (Opus), app `client/`.

---

## 1. Resumen funcional

Se migra al sistema de diseño nuevo todo lo que el usuario ve en `/dashboard` **fuera del wizard**. Eso incluye la bienvenida con CTA, las estadísticas, los borradores pendientes, el historial (buscador, filtro de fechas, selector de vista, lista desplegable, tabla ordenable, cuadrícula y paginador), el chip de Tatana de la navbar y las piezas globales del dashboard: la guía USB (panel lateral), el diálogo de soporte, el diálogo de reconexión al retomar, la confirmación de salida (misma API), tooltips y notificaciones. El `FloatingDock` desaparece: Guía, Soporte (solo si `supportEnabled`) y Tema pasan a la navbar (D4 B). Los primitivos pasan a PrimeReact con pass-through nuevo (`DataTable`/`Column`, `Paginator`, `Calendar`, `SelectButton`, `Tag`, `Tooltip`, `InputTextarea`) y se borran cinco componentes `ui/` que quedan sin consumidores. No cambian datos, endpoints, validaciones ni flujos.

## 2. Toca

| Lado | ¿Toca? |
|---|---|
| backend (API) `server/src/Factum.Backend` | **no** |
| backend (Tatana) `server/src/Factum.Agent` | **no** |
| client `client/` | **sí** |
| agent-ui `agent-ui/` | **no** |

## 3. Modelo de datos / Endpoints / WebSocket

No aplica. No se crean ni modifican colecciones, índices, endpoints, DTOs ni mensajes WebSocket, y no se escribe en la base. `localStorage['ev-theme']` y `localStorage['factum_token']` quedan sin cambios. La vista del historial sigue siendo estado en memoria y arranca en "lista" (D10 A).

## 4. Contrato compartido

**Sin cambios: confirmado.** La serialización del backend es `JsonNamingPolicy.SnakeCaseLower` (`server/src/Factum.Backend/Program.cs` L72 y L77). Esta HU consume, **sin modificarlos**, los contratos de abajo, y no toca `client/src/lib/api.ts`, `client/src/types/`, `client/src/lib/agent.ts` ni `client/src/hooks/*`.

| Contrato | Consumidor en esta HU | Cliente | Servidor | Cambio |
|---|---|---|---|---|
| `GET /api/cases` → `{ cases: Case[] }` | `CaseHistory`, `DashboardStats`, borradores | `api.listCases` vía `hooks/useAuth.ts` | `DTOs/` de casos | ninguno |
| Descargas `GET /api/cases/{id}/download/{filename}` | `CaseCard`, `CaseGridCard` | `api.downloadURL` | — | ninguno |
| `GET /api/config/public` → `support_enabled` | dashboard (navbar, guía, soporte) | `hooks/usePublicConfig.ts` → `supportEnabled` | `DTOs/ConfigDtos.cs` | ninguno (lo agregó `auth-e-integraciones-sin-mpf`) |
| `/api/support/*` (`reportarProblema`, `listarMisReportes`, `calificarReporte`, `obtenerLinkFaro`) | `SoporteModal` | `api.ts` | `SupportController` | ninguno: mismos métodos, mismos argumentos |
| Tatana WS `devices_changed` / `useAgentConnection` | `AgentChip`, `ResumeDeviceModal` (vía `page.tsx`) | `hooks/useAgentConnection.ts` | `Factum.Agent` | ninguno |

Campos de `Case` que se **leen** (sin renombrar): `id`, `status`, `nro_referencia`, `caratula`, `nombre_denunciante`, `dni_denunciante`, `parte_denunciante`, `parte_denunciada`, `created_at`, `device.{manufacturer,model,imei,serial,platform,os_version,android_version}`, `perito.nombre`, `officer.{name,sigla}`, `observaciones`, `zip_encrypted`, `zip_encryption`, `zip_hash`, `report_hash`, `schema_version`, `zip_filename`, `pdf_filename`. `MiToken`: `id_token`, `servicio`, `descripcion`, `descripcion_resolucion`, `estado`, `fecha_creacion`, `puntuacion`.

## 5. Archivos compartidos con otras partes (coordinación)

Las partes 2 (`rediseno-dashboard-wizard`) y 3 (`rediseno-dashboard-captura`) salen **después** de esta y de esta rama. Para no pisarlas:

| Archivo | Qué toca esta parte | Qué **no** toca (zona de otra parte) |
|---|---|---|
| `client/src/app/dashboard/page.tsx` | (a) imports; (b) el `<div>` raíz (fondo `bg-fx-bg`); (c) el bloque de modales raíz (`ResumeDeviceModal`, `GuideModal`, `SoporteModal`, `FloatingDock` → se borra; `ConfirmDialog` **sin cambios de props**); (d) el bloque `<AppNavbar …/>` (center: `Tip` → `FxTip`; actions: chip, "+", Guía, Soporte; `showThemeToggle`); (e) la clase del contenedor con scroll (`pb-28` → `pb-14`); (f) el bloque `mode === "history"` completo; (g) quitar `useTheme`/`isDark`/`toggleTheme`; (h) `guideBtnRef` y el flag `supportFromGuide` (T13). | Todo el bloque `mode === "wizard"` (contador "Paso N de 6", riel, banners del wizard, "Retomando inspección", transición `slideDir`, `.card` del paso y los pasos). Estado, efectos y handlers del wizard. `STEPS`. Los imports que use el wizard (`motion`, `AnimatePresence`, `EASE`, `slideDir`, `Loader2`, `AlertCircle`, `X`, `Play`…) **se quedan** aunque el historial deje de usarlos. |
| `client/src/components/ui/alert-dialog.tsx` | Se **reescribe en el mismo path** sobre `Dialog` de Prime, con la misma API (T8). | `ReportStep.tsx` (parte 2) **no** cambia su import ni sus props. |
| `client/src/components/ExpertProfileDialog.tsx` (parte 2) | **Solo** el import de `sonner` y la línea `toast.success(…)` → `useFxToast` (T11, DP3). | Todo lo demás (formulario, `Dialog`, campos): parte 2. |
| `client/src/lib/prime/pt/index.ts`, `pt/shared.ts`, `pt/inputtext.ts` | Aditivo: pt nuevos registrados, helpers exportados (T4). `inputtext.ts`: refactor que **exporta** la función de `root` sin cambiar clases. | Las partes 2 y 3 suman `dropdown`, `checkbox`, `progressbar` y la variante "media" de `dialog`. |
| `client/src/lib/prime/pt/dialog.ts` | Aditivo: variante `position === "right"` (drawer) en `root`/`mask`/`transition` (T9). La variante centrada no cambia. | Variante "media" (parte 3). |
| `client/src/components/design-system/DesignSystemShowcase.tsx` | Aditivo: demos nuevas en la sección `prime` (T15). | Las secciones "sonner"/"legacy" las saca la parte 4. |
| `client/src/components/shell/AppProviders.tsx` | Monta `FxToastProvider` (T11). `TooltipProvider` (base-ui) y `AppToaster` (sonner) **se quedan** (los usa el showcase; los saca la parte 4). | — |
| `client/src/components/shell/AppNavbar.tsx` | **No se toca** el código. Opcional: actualizar solo el JSDoc de `showThemeToggle` ("Default false…FloatingDock") porque deja de ser cierto. | — |
| `client/src/components/UserMenu.tsx`, `DevModeBanner.tsx`, `usb-guide/data.ts`, `usb-guide/types.ts`, `hooks/usePublicConfig.ts`, `FaroIcon.tsx` | **No se tocan.** Los textos de `auth-e-integraciones-sin-mpf` (título "Soporte", subtítulo, "Ver todo en el portal de soporte", "Guía de conexión USB", `hasRealSigla`) se conservan tal como están en disco. | — |
| `client/src/app/globals.css`, `tailwind.config.ts`, `lib/prime/locale-es.ts` | **No se tocan** (las keyframes `fx-rise-in`/`fx-fade-in` ya existen; el locale ya tiene las claves de `Calendar`, `Paginator` y `aria`). | Variables y clases legacy: parte 4. |
| `client/src/constants/animations.ts` | No se toca (lo sigue usando el wizard). | — |

## 6. Decisiones técnicas

Las D1 a D11 de la HU son de producto y ya están validadas. Las técnicas van numeradas T1, T2… Las que necesitan al usuario están en la sección 9; con el modo autónomo se toma la recomendada.

### T1. Archivos nuevos, reescritos y borrados

```
NUEVOS
client/src/components/overlay/FxTip.tsx              ← tooltip Prime con la firma de Tip (T10)
client/src/components/feedback/FxBanner.tsx          ← Message de Prime + botón cerrar opcional (T7)
client/src/components/shell/FxToastProvider.tsx      ← Toast global de Prime + hook useFxToast (T11)
client/src/lib/case-status.ts                        ← estado de caso → etiqueta, severidad, ícono (T6)
client/src/lib/prime/pt/{datatable,column,paginator,calendar,selectbutton,tag,tooltip,inputtextarea}.ts

REESCRITOS (mismo path, misma firma salvo lo indicado)
client/src/components/ui/alert-dialog.tsx            (T8, misma API)
client/src/components/dashboard/{AgentChip,DashboardStats,ResumeDeviceModal,SoporteModal}.tsx
client/src/components/{CaseHistory,CaseCard,CaseGridCard,StatusBadge,GuideModal,USBGuide}.tsx
client/src/components/usb-guide/{AndroidGuide,IOSGuide,Mockups}.tsx
client/src/components/ui/lens.tsx

BORRADOS (git rm, después de un grep sin consumidores)
client/src/components/ui/{data-table,pagination,date-range-calendar,cycling-placeholder-input,floating-dock}.tsx
```

Cambios de firma, todos con un solo consumidor (`dashboard/page.tsx`):
- `ResumeDeviceModal({ cas: Case | null, deviceConnected, onConfirm, onCancel })`: `cas` pasa a admitir `null` (= cerrado) para que `Dialog` pueda animar la salida. Internamente guarda el último `cas` no nulo en un `useState` para renderizar durante la salida.
- `GuideModal({ visible, onClose, onSupport? })`: suma `visible`.
- `CaseCard`/`CaseGridCard`: se quita la prop `index` (solo servía para el stagger de framer).

### T2. Criterio de reemplazo (D3 A) aplicado a esta parte

| Pieza | Queda como |
|---|---|
| Diálogos (reconexión, soporte, confirmación) | `Dialog` Prime |
| Panel de la guía | `Dialog` Prime con `position="right"` (variante drawer del pt, T9, DP1). **No** `Sidebar` |
| Tabla | `DataTable` + `Column` Prime |
| Paginador | `Paginator` Prime |
| Rango de fechas | `Calendar selectionMode="range"` Prime |
| Selector de vista, pestañas de la guía, pestañas y categorías de soporte | `SelectButton` Prime (`role="group"` + `aria-pressed`, flechas) |
| Estado de caso y estado de token | `Tag` Prime |
| Inputs / textarea de soporte, buscador | `InputText` / `InputTextarea` Prime |
| Botones | `Button` Prime. Los links de descarga (`<a>`) usan las clases `FX_BUTTON_*` de `pt/shared.ts`, porque un link no es un `Button` |
| Tooltips | `Tooltip` Prime vía `FxTip` |
| Toasts | `Toast` Prime global vía `useFxToast` |
| Avisos en línea | `Message` Prime vía `FxBanner` |
| Tarjetas de caso, stats, borradores, chip del agente, pasos de la guía, bloque de terminal, lupa | Propios, con tokens |

Buscador sin `IconField`/`InputIcon`: `PrimeReactPTOptions` 10.9.9 no los declara (lo mismo que verificó `rediseno-pagina-inicio`). Se usa el patrón "campo con ícono" del login: wrapper `group relative` + ícono lucide absoluto + `InputText` con `pl-10`.

### T3. Reglas transversales (las chequea el reviewer)

- En los archivos de esta parte no quedan variables legacy (`var(--bg-*|--text-*|--blue*|--btn-*|--border*|--green|--red|--amber|--glow*|--section-label-c…)`), clases legacy (`card`, `card-hover`, `btn`, `btn-*`, `input`, `badge*`, `section-label`, `hero-title`, `step-title`, `stat-card`, `phone-mock*`), paleta Tailwind (`emerald-`, `amber-`, `red-`, `teal-`, `gray-`, `indigo-`, `green-`, `white/`…), hex ni `rgba` sueltos.
- **Excepciones permitidas** (contenido de imagen, como el logo del login): `dark:invert` / `dark:opacity-*` sobre los SVG monocromos `/android.svg`, `/apple.svg` y los logos de marca de la guía, y `bg-white` detrás de las fichas PNG de la guía (`/usb-guide/*.png`, son imágenes con fondo blanco). Cada excepción lleva un comentario `/* contenido de imagen */`.
- Los pt nuevos: solo clases `fx-*`, sin `dark:`, sin hex y sin paleta (regla T12 de la base).
- Acento ≠ éxito: `--fx-accent*` solo para CTA, foco, selección activa (página actual, opción de `SelectButton`, día elegido) y marca. Completado/conectado usan `--fx-success*`, siempre con ícono + texto.
- Foco: `fx-focus-ring` o `FOCUS_RING` de `pt/shared.ts` en todo lo interactivo propio.
- Movimiento (D9 A): **sin `framer-motion`** en ningún archivo de esta parte, salvo `page.tsx`, donde sigue el del wizard (zona de la parte 2). Las entradas usan `motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]` o `fx-rise-in`. La elevación de tarjetas usa `--fx-lift` (0 con reduced motion). Sin loops, salvo los spinners `Loader2` (con `motion-safe:animate-spin` donde son decorativos de espera y `animate-spin` en los `loading` de `Button`) y el punto de REC (`motion-safe:animate-pulse`).
- Textos visibles y `aria-label` en español rioplatense. Objetivos táctiles de las acciones principales ≥ 44 px en `< sm` (`min-h-11` en CTA, borradores y descargas).

### T4. Pass-through nuevos (`client/src/lib/prime/pt/`)

Se registran todos en `pt/index.ts`. Referencia de estructura: el preset oficial `node_modules/primereact/passthrough/tailwind/index.esm.js` (no se importa). Secciones verificadas en los `.d.ts` de 10.9.9.

| Archivo | Secciones y estilo |
|---|---|
| `tag.ts` | `root: ({ props })` → `inline-flex items-center gap-1 rounded-fx-pill border px-2 py-0.5 text-xs font-semibold whitespace-nowrap` + tono por `props.severity`: `success` → `bg-fx-success-soft border-fx-success text-fx-success`; `info` → `bg-fx-info-soft border-fx-info text-fx-info`; `warning` → `bg-fx-warning-soft border-fx-warning text-fx-warning`; `danger` → `bg-fx-danger-soft border-fx-danger text-fx-danger`; `secondary`/sin severidad → `bg-fx-surface-3 border-fx-border-strong text-fx-text-2`. `icon`: `w-3 h-3 shrink-0`. `value`: `leading-none`. |
| `selectbutton.ts` | `root`: `inline-flex items-stretch gap-0.5 rounded-fx-md border border-fx-border-strong bg-fx-surface-2 p-0.5`. `button: ({ context })` → `inline-flex items-center justify-center gap-1.5 min-h-8 px-2.5 rounded-[6px] text-fx-body-sm font-semibold cursor-pointer select-none transition-colors duration-fx-fast ease-fx` + `FOCUS_RING` + (`context.selected` ? `bg-fx-accent text-fx-on-accent` : `text-fx-text-2 hover:bg-fx-surface-3 hover:text-fx-text`) + (`context.disabled` && `opacity-50 cursor-not-allowed`). `label`: `leading-none`. |
| `paginator.ts` | `root`: `flex flex-wrap items-center justify-center gap-1`. `prevPageButton`/`nextPageButton`/`firstPageButton`/`lastPageButton: ({ context })` y `pageButton: ({ context })` → `inline-flex items-center justify-center min-w-9 h-9 px-2 rounded-fx-md text-fx-body-sm font-semibold tabular-nums cursor-pointer transition-colors duration-fx-fast ease-fx` + `FOCUS_RING`; activa (`context.active`) `bg-fx-accent text-fx-on-accent`; resto `text-fx-text-2 hover:bg-fx-surface-3 hover:text-fx-text`; `context.disabled` → `opacity-40 cursor-not-allowed`. `pages`: `flex items-center gap-1`. `current`: `px-2 text-fx-body-sm text-fx-text-3 tabular-nums`. Íconos: `w-4 h-4`. |
| `datatable.ts` | `root`: `relative`. `wrapper`: `overflow-x-auto`. `table`: `w-full border-collapse text-left`. `thead`: `bg-fx-surface-2`. `tbody`: `""`. `bodyRow: ({ context })` → `transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3` (con `rowHover`). `emptyMessage`: `px-4 py-6 text-center text-fx-text-3`. `loadingOverlay`: `absolute inset-0 flex items-center justify-center bg-fx-overlay`. |
| `column.ts` | `headerCell: ({ context, props })` → `px-4 py-3 border-b border-fx-border text-fx-label uppercase text-fx-text-3 whitespace-nowrap` + si es ordenable (`props.sortable`): `cursor-pointer select-none hover:text-fx-text` + `FOCUS_RING` (outline `-outline-offset-2` para que no lo recorte el wrapper) + `context.sorted && "text-fx-text"`. `headerContent`: `inline-flex items-center gap-1.5`. `sort`: `inline-flex`. `sortIcon`: `w-3.5 h-3.5`. `bodyCell`: `px-4 py-3 border-b border-fx-border text-fx-body-sm text-fx-text align-middle`. |
| `calendar.ts` | `root`: `relative inline-flex w-full sm:w-auto`. `input`: mismo look que `inputtext` (reusar la función exportada, T4 nota) + `pr-10`. `dropdownButton`: `absolute right-0.5 top-1/2 -translate-y-1/2 w-9 h-9 p-0 border-0 bg-transparent text-fx-text-3 hover:text-fx-text rounded-fx-md` + `FOCUS_RING`. `panel`: `bg-fx-surface-2 text-fx-text border border-fx-border rounded-fx-lg shadow-fx-2 p-3 w-[min(20rem,calc(100vw-2rem))]`. `header`: `flex items-center justify-between gap-2 pb-2 mb-2 border-b border-fx-border`. `previousButton`/`nextButton`: `CLOSE_BUTTON`. `title`: `flex items-center gap-1 text-fx-body-sm font-semibold`. `monthTitle`/`yearTitle`: `px-1.5 py-1 rounded-fx-sm hover:bg-fx-surface-3 capitalize` + `FOCUS_RING`. `table`: `w-full border-collapse text-fx-body-sm`. `weekDay`/`tableHeaderCell`: `p-1 text-center text-fx-label text-fx-text-3`. `day`: `p-0.5 text-center`. `dayLabel: ({ context })` → `inline-flex items-center justify-center w-9 h-9 rounded-fx-md cursor-pointer transition-colors duration-fx-fast ease-fx` + `FOCUS_RING` + (`context.selected` ? `bg-fx-accent-soft text-fx-accent-text font-bold ring-1 ring-inset ring-fx-accent` : `context.disabled` ? `text-fx-text-disabled cursor-not-allowed` : `text-fx-text hover:bg-fx-surface-3`) + (`context.today && !context.selected` && `font-bold underline underline-offset-4`) + (`context.otherMonth` && `text-fx-text-3`). `monthPicker`/`yearPicker`: `grid grid-cols-3 gap-1`. `month`/`year: ({ context })` → como `dayLabel`, con `w-auto px-2`. `buttonbar`: `flex justify-between pt-2 mt-2 border-t border-fx-border`. `todayButton`/`clearButton`: `root` como `Button` `text` chico (`px-3 py-1.5 text-xs font-bold rounded-fx-md text-fx-accent-text hover:bg-fx-surface-3` + `FOCUS_RING`). `transition`: fade (`opacity-0` → `!opacity-100` en `duration-fx-fast`, sin escala). **Verificar en el navegador** que el `InputText` y los `Button` internos de `Calendar` tomen estas clases y no las del pt global. Si alguno las pisa, ajustar con `pt={{ … }}` en la sección del calendar. |
| `tooltip.ts` | `root`: `absolute z-[1100] max-w-[220px] pointer-events-none px-0 py-0`. `text`: `px-2 py-1 text-xs font-medium bg-fx-surface-3 text-fx-text border border-fx-border-strong rounded-fx-sm shadow-fx-2 break-words` (el mismo look del `Tip` restilizado: 13.21:1 en oscuro y 15.21:1 en claro). `arrow`: `hidden`. **Verificar** en runtime el posicionamiento: si Prime no aplica `position:absolute` inline en unstyled, se deja `absolute` en `root`. |
| `inputtextarea.ts` | `root: ({ props, context })` → **la misma función** que `inputtext.root` + `resize-none min-h-[5rem]`. Para eso, `inputtext.ts` exporta `export function inputRootClasses({ props, context })` y su `root` la usa. Es un refactor sin cambio de clases. |

`pt/shared.ts` suma `FX_BUTTON_SECONDARY`, el look del `Button` `severity="secondary"` para `<a>`: `inline-flex items-center justify-center gap-2 select-none whitespace-nowrap no-underline border rounded-fx-md font-bold px-5 py-2.5 text-fx-body-sm bg-fx-surface-2 border-fx-border-strong text-fx-text hover:bg-fx-surface-3 transition-colors duration-fx-fast ease-fx` + `FOCUS_RING`.

### T5. `dashboard/page.tsx`: navbar sin dock (D4 B) y modo historial

**Navbar.** `<AppNavbar user onLogout showThemeToggle center={…} actions={…} />`:
- `center`: igual que hoy. El `Tip` del botón volver pasa a `FxTip` con la misma `label`.
- `actions`, en este orden:
  1. `<AgentChip …/>` (sin cambios de props).
  2. "Nueva inspección" (solo `mode === "history"`): `<button>` nativo con las mismas clases fx de hoy + `hidden sm:inline-flex` (en `< sm` está el CTA grande de la bienvenida) + `motion-safe:animate-[fx-fade-in…]`. Sin `motion.button` ni `title`: `aria-label="Nueva inspección"` + `FxTip label="Nueva inspección" side="bottom"`.
  3. Guía: `<button ref={guideBtnRef}>` de ícono, `w-8 h-8 rounded-fx-md text-fx-text-2 hover:text-fx-text hover:bg-fx-surface-3 fx-focus-ring` (el mismo look que `ThemeSwitch`), `HelpCircle`, `aria-label="Guía de uso"`, `aria-haspopup="dialog"`, `aria-expanded={guideOpen}`, `FxTip label="Guía de uso" side="bottom"`, `onClick={() => setGuideOpen(true)}`.
  4. Soporte, **solo si `supportEnabled`**: el mismo look, ícono `LifeBuoy`, `aria-label="Soporte"`, `aria-haspopup="dialog"`, `aria-expanded={reportModal}`, `FxTip label="Soporte" side="bottom"`, `onClick={() => { setSupportFromGuide(false); setReportModal(true); }}`. No mueve el foco cuando aparece tarde (sin `autoFocus`).
- `showThemeToggle`: `ThemeSwitch` de la base (cubre "cambio de tema").
- Ancho a 360 px: brand 24 + help 32 + soporte 32 + separador + tema 32 + avatar 32 + gaps ≈ 175 px a la derecha, y quedan ≈ 115 px para el breadcrumb, que trunca. El chip ya es `hidden sm:flex` y el "+" pasa a `hidden sm:inline-flex`. Verificar sin overflow horizontal.

**Contenedor.** El `<div>` raíz cambia `style={{ background: "var(--bg-base)" }}` por `bg-fx-bg text-fx-text`. El scroll `flex-1 overflow-y-auto pb-28` pasa a `pb-14` (reserva la píldora de `SystemStatusLine`). Se borra el comentario del dock.

**Bloque historial** (reemplaza `AnimatePresence`+`motion.div key="history"`): `<div className="p-4 sm:p-6 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">` con `max-w-5xl mx-auto space-y-8`:
1. **Bienvenida** (si `user`), `motion-safe:animate-[fx-rise-in_var(--fx-dur-slow)_var(--fx-ease-out)_both]`, `flex flex-col sm:flex-row sm:items-end sm:justify-between gap-4`:
   - `<h1 className="text-fx-display text-fx-text break-words">{greeting}, <span className="text-fx-accent-text">{user.name}</span></h1>`. El acento como texto de marca da 11.71 / 6.16.
   - `<p className="mt-2 text-fx-body text-fx-text-2 first-letter:uppercase">{formattedDate}</p>`. Se cambia `capitalize`, que capitalizaba todas las palabras, por `first-letter:uppercase`.
   - `<Button size="large" icon={<Plus className="h-5 w-5" aria-hidden />} label="Nueva inspección" onClick={startWizard} className="w-full sm:w-auto min-h-11" />`.
2. **Error global**: `<FxBanner tone="error" onClose={() => setGlobal("")}>{globalError}</FxBanner>` si `globalError`.
3. **Stats**: `{historyCases.length > 0 && <DashboardStats cases={historyCases} />}` (T6).
4. **Pendientes de retomar** (si `draftCases.length`): `<section aria-labelledby="drafts-title">`, título `<h2 id="drafts-title" className="flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2"><Play …aria-hidden/> Pendientes de retomar</h2>`, `<ul className="flex gap-3 overflow-x-auto px-1 py-2 -mx-1">` (el `py-2` evita que la elevación se recorte). Cada `<li className="shrink-0">` con un `<button type="button" onClick={() => handleResume(cas)} aria-label={`Retomar inspección ${cas.nro_referencia} — ${manufacturer} ${model}`} className="fx-card fx-card-interactive flex items-center gap-3 min-h-11 w-60 px-3.5 py-3 text-left">`: ícono `Play` en `w-9 h-9 rounded-fx-md bg-fx-accent-soft text-fx-accent-text` (9.50 / 5.76), N° en `text-fx-body-sm font-semibold text-fx-text truncate` y equipo con `Smartphone` en `text-xs text-fx-text-3 truncate`.
5. **Estado**: `<FxBanner tone="info" role="status" icon={<Loader2 className="h-4 w-4 motion-safe:animate-spin" aria-hidden />}>{statusMsg}</FxBanner>` si `statusMsg`.
6. `<CaseHistory …/>` con las mismas props.

### T6. Componentes del historial

**`lib/case-status.ts`** (puro, sin JSX): `CASE_STATUS: Record<"draft"|"generating"|"completed"|"error", { label: string; severity: "secondary"|"info"|"success"|"danger"; stripe: string }>` con label `Borrador` / `Generando…` / `Completado` / `Error` (los mismos de hoy) y `stripe` = `bg-fx-border-strong` / `bg-fx-info` / `bg-fx-success` / `bg-fx-danger`. Y `caseStatusOf(status: string)`, que cae en `draft` si el valor es desconocido, aunque la etiqueta muestra el valor crudo como hoy. Reemplaza `STATUS_CLASS`/`STATUS_LABEL` de `StatusBadge` y el `STATUS_STYLE` exportado de `CaseGridCard`, que no tiene otros consumidores (verificado con grep).

**`StatusBadge`** (misma firma `{ status }`): `<Tag severity={…} value={label} icon={…} />`. Íconos lucide `w-3 h-3`, con `aria-hidden`: `CircleDashed` (borrador), `Loader2` con `motion-safe:animate-spin` (generando), `CheckCircle2` (completado) y `AlertCircle` (error). Siempre ícono + texto.

**`AgentChip`** (misma firma). Sin framer. Contenedor `hidden sm:flex items-center gap-1.5`, `role="status" aria-live="polite"`:
- Píldora del agente, `inline-flex items-center gap-1.5 rounded-fx-pill border px-3 py-1 text-xs font-medium`:
  - online: `bg-fx-success-soft border-fx-success text-fx-success` + ícono `Wifi` + texto (`deviceLabel` o "Tatana activo · esperando dispositivo"), `truncate max-w-[280px]`.
  - offline: `bg-fx-warning-soft border-fx-warning text-fx-warning` + `WifiOff` + "Tatana no disponible".
- Si `recording`, una segunda píldora `bg-fx-danger-soft border-fx-danger text-fx-danger font-bold` con un punto `w-1.5 h-1.5 rounded-full bg-fx-danger motion-safe:animate-pulse` (`aria-hidden`) + "REC".
- Contraste: success/success-soft 8.14 / 5.71, warning/warning-soft 8.48 / 5.24, danger/danger-soft 6.21 / 5.44. Cierra H5 (el `emerald-600` en claro). Ícono + texto en los tres estados.

**`DashboardStats`** (misma firma). `<dl className="grid grid-cols-2 sm:grid-cols-4 gap-3">`. Se quita el `aria-live` del contenedor: el conteo de `CaseHistory` ya anuncia, y así no se repite. Cada stat es un `<div className="fx-card relative overflow-hidden p-4">` con:
- franja superior `absolute inset-x-0 top-0 h-0.5` + token (Total y Este mes: `bg-fx-border-strong`; Completadas: `bg-fx-success`; En proceso: `bg-fx-warning`);
- fila `flex items-center justify-between` con `<dt className="text-fx-label uppercase text-fx-text-2">` y el ícono lucide (`FolderOpen`, `Clock`, `CheckCircle2` en `text-fx-success`, `Activity` en `text-fx-warning`, `w-4 h-4`, `aria-hidden`);
- `<dd className="mt-2 flex items-baseline gap-1.5"><span className="text-fx-h1 tabular-nums text-fx-text">{value}</span>` + en Completadas `<span className="text-xs text-fx-text-3 tabular-nums">{pct}%</span>`.
- Mismos cálculos de hoy. Sin stagger.

**`CaseHistory`** (misma firma). Sin framer, `useTheme`, `DottedGlowBackground`, `Shrimp`, `CyclingPlaceholderInput`, `DateRangeCalendar`, `DataTable` propio ni `Pagination`. **Se conserva toda la lógica**: `filtered` (mismos campos y reglas de fecha), `SORT_ACCESSORS`, `displayRows` (orden global solo en tabla), `PAGE_SIZE = 8`, reset a página 1 al filtrar u ordenar, recorte de página fuera de rango, `toYMD`/`fromYMD`. `SortState` pasa a ser un tipo local: `{ key: string; dir: "asc" | "desc" }`.
- **Encabezado**: `<h2 className="text-fx-h2 text-fx-text">Mis inspecciones</h2>` + el conteo `<p aria-live="polite" className="mt-1 text-fx-body-sm text-fx-text-2">` con los mismos textos de hoy. "Actualizar": `<Button text severity="secondary" icon={<RefreshCw className="h-4 w-4" aria-hidden />} aria-label="Actualizar lista de inspecciones" onClick={onRefresh} />` dentro de `FxTip label="Actualizar lista"`.
- **Barra de filtros** (si `!loading && cases.length > 0`): `flex flex-col gap-2.5 sm:flex-row sm:flex-wrap sm:items-center`.
  - Buscador (patrón del login): `<label htmlFor="case-search" className="sr-only">Buscar inspecciones</label>`, wrapper `group relative w-full sm:flex-1 sm:min-w-[16rem]`, `Search` absoluto a la izquierda (`text-fx-text-3 group-focus-within:text-fx-accent-text`) y `<InputText id="case-search" type="search" value={query} onChange={e => setQuery(e.target.value)} placeholder="Buscar por causa, carátula, partes, titular, DNI, equipo o IMEI" className="pl-10 pr-10" autoComplete="off" />`. Si `query`, se agrega un botón `X` absoluto a la derecha (`w-8 h-8`, `aria-label="Limpiar búsqueda"`, `fx-focus-ring`) que vacía la búsqueda y devuelve el foco al input. Placeholder fijo (D6 A).
  - Fechas: `<label htmlFor="case-date-range" className="sr-only">Filtrar por fecha de creación</label>` + `<Calendar inputId="case-date-range" selectionMode="range" value={range} onChange={…} readOnlyInput showIcon icon={<CalendarRange className="h-4 w-4" aria-hidden />} showButtonBar hideOnRangeSelection dateFormat="dd/mm/yy" placeholder="Desde – hasta" />`. `range = dateFrom || dateTo ? [dateFrom ? fromYMD(dateFrom) : null, dateTo ? fromYMD(dateTo) : null] : null`. `onChange: e => { const [f, t] = e.value ?? []; setDateFrom(f ? toYMD(f) : ""); setDateTo(t ? toYMD(t) : ""); }`. Español, lunes primero y `dd/mm/aaaa` salen del locale `es` (`firstDayOfWeek: 1`, ya registrado). "Hoy"/"Limpiar" vienen del locale.
  - "Limpiar filtros" (si `hasAnyFilter`): `<Button text size="small" icon={<X …/>} label="Limpiar filtros" onClick={clearFilters} />`. `clearFilters` además devuelve el foco al buscador, porque el botón desaparece.
  - Vista, a la derecha (`sm:ml-auto`): `<SelectButton value={view} onChange={e => e.value && setView(e.value)} allowEmpty={false} options={VIEWS} optionLabel="label" optionValue="value" itemTemplate={o => <><o.Icon className="h-4 w-4" aria-hidden /><span className="sr-only sm:not-sr-only">{o.label}</span></>} pt={{ root: { "aria-label": "Vista del historial" } }} />` con `VIEWS = [{ value: "list", label: "Lista", Icon: List }, { value: "table", label: "Tabla", Icon: Table }, { value: "grid", label: "Cuadrícula", Icon: LayoutGrid }]`. Prime pone `aria-label` (= label) y `aria-pressed` en cada opción. Sin tooltips: el texto se ve en `sm+` y el nombre accesible está siempre.
- **Contenido**: `<div ref={resultsRef} tabIndex={-1} aria-label="Resultados de inspecciones" className="scroll-mt-6 outline-none">` (reemplaza el `listTopRef` `aria-hidden`):
  - Carga: `role="status"` + `Loader2` `w-6 h-6 animate-spin text-fx-text-3` + "Cargando historial…" (`text-fx-body-sm text-fx-text-2`), `py-14` centrado.
  - Vacío total, estático: `fx-card border-dashed border-fx-border-strong py-14 text-center`, ícono `Inbox` `w-10 h-10 text-fx-text-3` en `w-20 h-20 mx-auto rounded-fx-xl bg-fx-surface-2`, "Sin inspecciones aún" (`text-fx-h3`), "Hacé clic en "Nueva inspección" para empezar" (`text-fx-text-2`) y `<Button icon={<Plus/>} label="Primera inspección" onClick={onNewCase} />`.
  - Sin resultados: `Search` en `w-14 h-14 rounded-fx-lg bg-fx-surface-2`, "Sin resultados", "Ninguna inspección coincide con los filtros actuales" y `<Button severity="secondary" size="small" icon={<X/>} label="Limpiar filtros" onClick={clearFilters} />`.
  - Lista: `<ul className="space-y-2.5">` de `CaseCard`.
  - Tabla: `fx-card overflow-hidden` + `<DataTable value={paged} dataKey="id" lazy rowHover sortField={tableSort.key} sortOrder={tableSort.dir === "asc" ? 1 : -1} onSort={e => { setTableSort({ key: e.sortField, dir: e.sortOrder === 1 ? "asc" : "desc" }); setPage(1); }} removableSort={false} sortIcon={({ sorted, sortOrder }) => sorted ? (sortOrder === 1 ? <ChevronUp/> : <ChevronDown/>) : <ChevronsUpDown/>} tableStyle={{ minWidth: "40rem" }} aria-label="Inspecciones">`. Con `lazy`, la tabla **no** reordena por su cuenta: muestra `paged`, que ya viene ordenado globalmente (comportamiento de hoy). Columnas: `<Column columnKey="causa" sortField="causa" sortable header="N° de causa" body={…}/>`, `caratula` (ordenable; muestra la carátula y debajo, si hay carátula, el titular en `text-xs text-fx-text-3`), `equipo` (no ordenable), `fecha` (ordenable, `formatDate`), `estado` (ordenable, `StatusBadge`) y acciones (sin header visible, `header={<span className="sr-only">Acciones</span>}`, `<Button severity="secondary" size="small" label="Retomar" onClick={() => onResume(c)} />` solo en borradores). Prime pone `aria-sort` en las cabeceras ordenables y las hace operables con Tab + Enter: verificarlo.
  - Cuadrícula: `<ul className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">` de `CaseGridCard`.
  - Cambio de vista: el contenedor de cada vista lleva `key={view}` + `motion-safe:animate-[fx-fade-in…]`.
- **Paginador** (si `!loading && filtered.length > PAGE_SIZE`): `<Paginator first={(page - 1) * PAGE_SIZE} rows={PAGE_SIZE} totalRecords={filtered.length} onPageChange={e => changePage(e.page + 1)} template="PrevPageLink PageLinks NextPageLink" pageLinkSize={5} />` centrado + `<p className="text-center text-xs text-fx-text-3 tabular-nums">{desde}–{hasta} de {N}</p>`. Los `aria-label` ("Página anterior", "Página {page}"…) y el `aria-current` de la página activa salen del locale y de Prime. `changePage(n)`: `setPage(n)` y luego `resultsRef.current?.scrollIntoView({ behavior: reduce ? "auto" : "smooth", block: "start" })` + `resultsRef.current?.focus({ preventScroll: true })`, con `reduce = matchMedia("(prefers-reduced-motion: reduce)").matches`.

**`CaseCard`** (firma `{ cas, onResume }`). `<li className="fx-card overflow-hidden">` (el padre es un `ul`):
- Disparador: `<button type="button" aria-expanded={open} aria-controls={`case-${cas.id}-detail`} className="w-full text-left p-4 flex items-center gap-4 hover:bg-fx-surface-2 transition-colors duration-fx-fast ease-fx fx-focus-ring">`, con el mismo `aria-label` de hoy.
- Ícono `FolderOpen` en `w-10 h-10 rounded-fx-md border`: completado → `bg-fx-success-soft border-fx-success text-fx-success`; resto → `bg-fx-surface-2 border-fx-border text-fx-text-3`.
- Textos: N° en `text-fx-body-sm font-semibold text-fx-text` + `StatusBadge`; carátula · DNI en `text-xs text-fx-text-2 truncate`; meta (equipo, fecha, hora) en `text-xs text-fx-text-3` con íconos `aria-hidden`.
- Chevron `ChevronDown` con `transition-transform duration-fx-fast motion-reduce:transition-none` y `rotate-180` si `open`.
- Detalle: solo si `open`, `<div id=… className="border-t border-fx-border bg-fx-surface-2 px-4 pt-4 pb-5 space-y-4 motion-safe:animate-[fx-fade-in…]">` (sin animar la altura):
  - borrador: `<Button icon={<Play/>} label="Retomar inspección" onClick={() => onResume(cas)} className="w-full min-h-11" />`;
  - grilla `<dl className="grid grid-cols-1 sm:grid-cols-2 gap-3">` con los mismos campos de hoy (Perito, Unidad, Titular, Sistema op., Carátula, IMEI, Observaciones): `<dt className="text-fx-label uppercase text-fx-text-3">`, `<dd className="mt-1 text-fx-body-sm text-fx-text break-words">`; los `full` usan `sm:col-span-2`;
  - completado: bloque `rounded-fx-lg border border-fx-border bg-fx-surface-1 p-4 space-y-3.5` con título `Shield` + "Paquete del informe" (`text-fx-label uppercase text-fx-text-2`); "Contraseña ZIP" (H7: mismo texto y semántica) en `font-mono text-fx-body-sm font-bold text-fx-text select-all`; hashes con el mismo rótulo condicional por `schema_version` y valor `font-mono text-xs text-fx-text-2 break-all select-all`; íconos `Key`/`Hash`/`FileText` en `text-fx-text-3`;
  - descargas `grid grid-cols-1 sm:grid-cols-2 gap-2`: `<a href={dlURL(zip)} className={cn(FX_BUTTON_PRIMARY, "min-h-11")}><Archive/> ZIP cifrado</a>` y `<a href={dlURL(pdf)} className={cn(FX_BUTTON_SECONDARY, "min-h-11")}><FileText/> Informe Word</a>` (mismos textos; el Word pasa de "success" a secundario porque descargar no es un estado de éxito).

**`CaseGridCard`** (firma `{ cas, onResume }`). `<li className="fx-card relative flex flex-col overflow-hidden transition-[transform,box-shadow,border-color] duration-fx-base ease-fx hover:[transform:translateY(var(--fx-lift))] hover:shadow-fx-3 hover:border-fx-accent focus-within:[transform:translateY(var(--fx-lift))] focus-within:shadow-fx-3 focus-within:border-fx-accent">`. No se usa `.fx-card-interactive` porque la tarjeta no es un control: se eleva al hover y cuando el foco está en sus acciones. Con reduced motion, `--fx-lift` vale 0 y solo cambian sombra y borde.
- Franja `h-1 w-full` con `CASE_STATUS[…].stripe`.
- Cuerpo `p-5 space-y-3`: N° en `text-fx-h3 text-fx-text truncate`, carátula · DNI en `text-fx-body-sm text-fx-text-2 truncate`, `StatusBadge` arriba a la derecha y meta (equipo, fecha, hora, perito con `sr-only "Perito: "`) en `text-xs text-fx-text-3`.
- Acciones `px-4 pb-4 pt-3 border-t border-fx-border flex gap-2`: borrador → `Button size="small" icon={<Play/>} label="Retomar"` con `flex-1`; completado → `<a>` `FX_BUTTON_PRIMARY` "ZIP" y `FX_BUTTON_SECONDARY` "Word", con `aria-label` "Descargar ZIP de la causa {nro}" / "Descargar Word de la causa {nro}" (hoy solo tienen `title`) y `min-h-11`.

### T7. `FxBanner` (`components/feedback/FxBanner.tsx`)

Wrapper de `Message` de Prime (pt `message` del login) para los avisos del dashboard. Lo reutiliza la parte 2.

```ts
interface FxBannerProps {
  tone: "error" | "info" | "warn" | "success";
  children: React.ReactNode;
  icon?: React.ReactNode;           // default por tono: AlertCircle / Info / AlertTriangle / CheckCircle2
  onClose?: () => void;             // si viene, botón cerrar (X) con aria-label="Cerrar aviso"
  role?: "alert" | "status";        // default: "alert" si tone === "error", "status" si no
  className?: string;
}
```

Implementación: `<Message severity={tone} role={role} className={cn("w-full", className)} content={<>{icon}<div className="min-w-0 flex-1 font-medium">{children}</div>{onClose && <button type="button" onClick={onClose} aria-label="Cerrar aviso" className={CLOSE_BUTTON + " -my-1 -mr-1 w-7 h-7"}><X className="h-3.5 w-3.5" aria-hidden/></button>}</>} />` + `motion-safe:animate-[fx-fade-in…]`. El `aria-live` lo pone el pt (`assertive` en error, `polite` en el resto). **Verificar** que `role` pasado por props pise el `role="alert"` interno, como ya se verificó con `role="note"` en el showcase del login.

### T8. `ConfirmDialog` sobre Prime (mismo path y misma API)

`client/src/components/ui/alert-dialog.tsx` se reescribe. Exports `ConfirmDialog` (nombrado y `default`) y la interfaz `Props` **idéntica**: `open`, `onOpenChange`, `onConfirm`, `title`, `description?`, `confirmLabel = "Confirmar"`, `cancelLabel = "Cancelar"`, `tone = "destructive" | "neutral"`.

```tsx
<Dialog
  visible={open}
  onHide={() => onOpenChange(false)}
  modal closable showCloseIcon={false}       // Escape cierra (requiere closable) pero sin X, como hoy
  closeOnEscape dismissableMask={false}      // click afuera NO cierra (Gherkin)
  draggable={false} resizable={false}
  onShow={() => document.getElementById(cancelId)?.focus()}   // foco inicial en "cancelar"
  className="w-[min(26rem,100%)]"
  pt={{ root: { role: "alertdialog", "aria-describedby": description ? descId : undefined } }}
  header={<span className="flex items-center gap-3"><span className={iconBox}><AlertTriangle className="h-4 w-4" aria-hidden /></span>{title}</span>}
  footer={<>
    <Button id={cancelId} severity="secondary" label={cancelLabel} onClick={() => onOpenChange(false)} />
    <Button severity={tone === "destructive" ? "danger" : undefined} label={confirmLabel}
            onClick={() => { onConfirm(); onOpenChange(false); }} />
  </>}
>
  {description && <p id={descId} className="m-0 text-fx-body-sm text-fx-text-2">{description}</p>}
</Dialog>
```

- `iconBox`: destructivo `w-9 h-9 rounded-fx-md bg-fx-danger-soft text-fx-danger`; neutro `bg-fx-info-soft text-fx-info`.
- Ids estables con `useId()`.
- El foco vuelve al disparador porque `Dialog` lo restaura en `onExited` (`focusElementOnHide`, verificado en `dialog.esm.js` 10.9.9).
- **Verificar en el DOM** que el panel quede con `role="alertdialog"`, `aria-modal="true"` y `aria-labelledby` (el id del header lo pone Prime). Si `pt.root.role` no pisa el `role` interno, pasar `role="alertdialog"` como prop (`Dialog` esparce `getOtherProps` después del `role`).
- El orden `onConfirm()` → `onOpenChange(false)` es el de hoy. `ReportStep` depende de ese orden.

### T9. Variante drawer del `Dialog` (guía USB)

En `pt/dialog.ts`, `root`, `mask` y `transition` pasan a ser funciones que miran `props.position`:
- `position === "right"`: root `h-full max-h-full w-full max-w-xl md:max-w-2xl m-0 rounded-none sm:rounded-l-fx-xl border-y-0 border-r-0 border-l border-fx-border bg-fx-surface-1`. Mask: el mismo, sin `p-4` (`p-0`). Transición: `motion-safe:translate-x-full` → `!translate-x-0` en `duration-fx-slow ease-fx`, y solo fade con reduced motion. El header lleva `border-b border-fx-border` y el content `flex-1` con scroll.
- Cualquier otra posición: exactamente las clases actuales.
- Prime ya pone el `justify-content: flex-end` de la máscara con estilos inline cuando `position="right"` (verificado en `dialog.esm.js` L312).

Así la guía queda con `role="dialog"`, `aria-modal`, foco atrapado (`FocusTrap`), Escape y retorno del foco. `Sidebar` 10.9.9 no cumple: renderiza `role="complementary"`, sin `aria-modal`, sin foco atrapado y sin devolver el foco (verificado en `sidebar.esm.js` L389). Ver DP1.

### T10. `FxTip` (tooltip Prime con la firma de `Tip`)

```tsx
interface FxTipProps { label: React.ReactNode; children: React.ReactElement; side?: "top"|"bottom"|"left"|"right"; disabled?: boolean }
export function FxTip({ label, children, side = "top", disabled }: FxTipProps) {
  const id = useId();
  if (disabled || label == null || label === "") return children;
  return (<>
    {cloneElement(children, { "data-fx-tip": id })}
    <Tooltip target={`[data-fx-tip="${id}"]`} content={label} position={side}
             event="both" showDelay={350} closeOnEscape />
  </>);
}
```

- `event="both"`: aparece con hover **y** con foco de teclado. `closeOnEscape`: se descarta con Escape (WCAG 1.4.13).
- El tooltip no reemplaza el nombre accesible: el hijo conserva su `aria-label`.
- Se ata por selector de atributo y no por ref, así no hay que fusionar refs con el hijo. El hijo y el `Tooltip` se montan juntos, de modo que el binding del `useMountEffect` encuentra el target.
- z-index: el de tooltip de Prime (1100).
- En esta parte reemplaza a `Tip` en `page.tsx` (botón volver) y en `CaseHistory`. El `Tip` de base-ui sigue en el showcase y lo borra la parte 4.

### T11. Toast global de Prime (D5 A) y fix del z-index (H5)

- `components/shell/FxToastProvider.tsx` (`'use client'`) crea un contexto con `useFxToast(): { success(summary, detail?), info(…), warn(…), error(…) }`. Cada método llama a `toastRef.current?.show({ severity, summary, detail, life: 5000, icon: <Ícono lucide h-5 w-5 aria-hidden/> })`, con íconos `CheckCircle2` / `Info` / `AlertTriangle` / `XCircle`, los mismos de `AppToaster`. Renderiza `<Toast ref={toastRef} position="bottom-right" />` y `{children}`.
- `AppProviders`: `PrimeReactProvider` → `FxToastProvider` → `TooltipProvider` → children. `AppToaster` (sonner) se queda montado: lo usa la sección "sonner" del showcase y lo saca la parte 4.
- `position="bottom-right"`: no tapa la navbar (lo que pedía H5). Sin el dock, tampoco hay acciones inferiores fijas. La `SystemStatusLine` está abajo a la izquierda. El z 1200 de Prime queda por encima de los diálogos.
- Accesibilidad: Prime pone `role="alert"`, `aria-live="assertive"` y `aria-atomic` en cada mensaje (verificado en `toast.esm.js` L357). El botón cerrar tiene foco visible (pt `toast`) y nombre del locale ("Cerrar").
- **Migración del único `toast(...)` del dashboard** (DP3): en `ExpertProfileDialog.tsx`, `import { toast } from "sonner"` → `import { useFxToast } from "@/components/shell/FxToastProvider"`, `const toast = useFxToast();` y `toast.success("Perfil de perito guardado", "Se usa en los casos que crees o edites desde ahora.")`. Mismo texto. No se toca nada más del archivo.

### T12. Diálogos de la parte 1

**`ResumeDeviceModal`** → `<Dialog visible={cas !== null} onHide={onCancel} dismissableMask={false} draggable={false} resizable={false} className="w-[min(26rem,100%)]" header={…}>`:
- Header dinámico: `Smartphone` en `bg-fx-warning-soft text-fx-warning` + "Conectá el dispositivo" o `CheckCircle2` en `bg-fx-success-soft text-fx-success` + "Dispositivo detectado". El título cambia y se anuncia por la región de estado de abajo.
- Región `role="status" aria-live="polite"` con "Este caso requiere el dispositivo original" o "Listo para retomar la inspección" (`text-fx-text-2`).
- Ficha del equipo: `rounded-fx-lg border p-3.5 flex items-center gap-3`. Conectado → `bg-fx-success-soft border-fx-success`; esperando → `bg-fx-surface-1 border-fx-border`. Logo `/android.svg` o `/apple.svg` (`dark:invert`, excepción de T3), modelo, `UDID`/`Serial` en mono y "Expte: {nro}". Badge a la derecha: `CheckCircle2 text-fx-success` o `Loader2 motion-safe:animate-spin text-fx-warning` (con `aria-hidden`).
- Esperando: "Esperando conexión USB — se detectará automáticamente" (`text-xs text-fx-text-2`) + los dos consejos numerados de hoy (número en `bg-fx-warning-soft text-fx-warning`).
- Conectado: `CheckCircle2` + "Dispositivo conectado y reconocido — podés continuar la inspección" (`text-fx-success`).
- Footer: `<Button severity="secondary" label="Cancelar" onClick={onCancel} />` + `<Button icon={<CheckCircle2/>} label="Continuar inspección" onClick={onConfirm} disabled={!deviceConnected} />`. Hoy el botón aparece recién al conectar. Ahora está siempre y deshabilitado hasta conectar, así el layout no salta y el usuario ve qué falta.
- Sin pulsos ni springs. La detección automática no cambia (la maneja `page.tsx`).

**`SoporteModal`** (misma firma `{ open, user, onClose }`) → `<Dialog visible={open} onHide={handleClose} closable={!loading} dismissableMask draggable={false} resizable={false} className="w-[min(28rem,100%)] max-sm:w-full max-sm:h-full max-sm:max-h-full max-sm:rounded-none" maskClassName="max-sm:p-0" header={…}>`. Pantalla completa en `< sm`, como pide Responsive.
- Header: `FaroIcon` en `w-9 h-9 rounded-fx-md bg-fx-surface-3 text-fx-text-2` + título "Soporte" + subtítulo `text-fx-body-sm text-fx-text-2` con los textos de hoy.
- Pestañas "Nuevo token" / "Mis tokens": `SelectButton` de ancho completo (`pt={{ root: { className: "w-full", "aria-label": "Sección de soporte" }, button: { className: "flex-1" } }}`).
- Categoría: `<fieldset>` con `<legend className="text-fx-label uppercase text-fx-text-2">Categoría</legend>` + `SelectButton` con `itemTemplate` (ícono + label + hint `text-[11px] font-normal`), `pt.button` `flex-1 flex-col h-auto py-2.5` y `allowEmpty={false}`.
- Campos: `<label>` visible (`text-fx-label uppercase text-fx-text-2`) + `InputText` (`soporte-numero-interno`, `soporte-telefono` con `type="tel"`) + `<InputTextarea id="soporte-descripcion" rows={3} maxLength={500} aria-describedby="soporte-descripcion-hint">` + `<p id="soporte-descripcion-hint" className="text-xs text-fx-text-3">` con el contador de hoy.
- "Se va a reportar como…": `text-xs text-fx-text-3`, con `hasRealSigla` como hoy.
- Error: `<FxBanner tone="error">{error}</FxBanner>`.
- Footer de "nuevo": `<Button text severity="secondary" label="Cancelar" disabled={loading} onClick={handleClose} />` + `<Button label="Enviar" icon={<FaroIcon className="h-4 w-4" aria-hidden />} loading={loading} disabled={!isValid} onClick={handleSubmit} />`. Misma validación (`isValid`).
- Éxito: `CheckCircle2` en `w-12 h-12 rounded-fx-lg bg-fx-success-soft text-fx-success`, "Token #{n} registrado" (`text-fx-h3`), el texto de hoy y botones "Ver mis tokens" (secundario) y "Cerrar" (`text`). Contenedor `role="status"`.
- Mis tokens: link "Ver todo en el portal de soporte" como `Button link size="small"` con `ExternalLink` y `loading={openingFaro}`. Carga: `Loader2` + sr-only "Cargando tus tokens…". Vacío: `Inbox` + texto de hoy. Cada token: `fx-card p-3`, servicio `text-fx-body-sm font-bold`, `<Tag>` de estado (`EnEspera` → `warning` "En espera", `Reasignado` → `info` "En curso", `Finalizado` → `success` "Resuelto", con ícono `Clock`/`Loader2` sin girar/`CheckCircle2`), descripción `line-clamp-2 text-xs text-fx-text-2`, resolución `rounded-fx-md bg-fx-success-soft text-fx-success text-xs px-2 py-1.5`, fecha `Clock` + `text-xs text-fx-text-3`. Calificación: los 5 botones de hoy (mismos `aria-label`, `w-8 h-8 inline-flex items-center justify-center rounded-fx-sm fx-focus-ring`), `Star` en `text-fx-warning fill-current` si está marcada o `text-fx-text-3` si no, más `aria-pressed={(t.puntuacion ?? 0) >= n}`.
- **Se borra** el listener manual de Escape (lo hace `Dialog`). `handleClose` y `reset` siguen igual (con `setTimeout(reset, 250)`). Mismas llamadas a la API.

**`GuideModal`** (`{ visible, onClose, onSupport? }`) → `<Dialog visible={visible} onHide={onClose} position="right" dismissableMask draggable={false} resizable={false} header="Guía de conexión USB" footer={…}>` (variante drawer, T9). El título del header y el texto del footer son **los que dejó `auth-e-integraciones-sin-mpf` en disco**: si el header actual sigue diciendo "Ayuda con la conexión", se conserva tal cual.
- Content: `<USBGuide onDone={onClose} />`.
- Footer `justify-between text-xs text-fx-text-3`: texto de pie + `{onSupport && <Button link size="small" icon={<LifeBuoy …/>} label="¿No conecta? Contactar soporte" onClick={onSupport} />}`.
- Se borra el listener manual de Escape. La guía se cierra con Escape, con click en la máscara y con la X, y el foco vuelve al botón "Guía de uso" de la navbar o al "Ver guía" del paso 1.

### T13. Montaje de modales en `page.tsx` y foco de Guía → Soporte

- `<ResumeDeviceModal cas={resumePending} deviceConnected={resumeConnected} onConfirm={() => resumePending && proceedResume(resumePending)} onCancel={…igual…} />`, siempre montado, sin `AnimatePresence`.
- `<GuideModal visible={guideOpen} onClose={() => setGuideOpen(false)} onSupport={supportEnabled ? () => { setGuideOpen(false); setSupportFromGuide(true); setReportModal(true); } : undefined} />`, siempre montado.
- `{supportEnabled && <SoporteModal open={reportModal} user={user} onClose={() => { setReportModal(false); if (supportFromGuide) { setSupportFromGuide(false); guideBtnRef.current?.focus(); } }} />}`. Cuando el soporte se abrió desde la guía, su disparador desaparece con ella. El foco vuelve al botón "Guía de uso" de la navbar. El `DomHandler.focus` que Prime hace después en `onExited` apunta a un nodo desmontado y no tiene efecto. **Verificarlo con teclado.**
- `ConfirmDialog` de salida: sin cambios en `page.tsx`.
- Se borra `<FloatingDock …/>` y los imports `FloatingDock`, `FaroIcon`, `Sun`, `Moon`, `useTheme`. Se suman `FxTip`, `FxBanner`, `Button` (`primereact/button`) y `LifeBuoy`.

### T14. Guía USB (`USBGuide`, `usb-guide/*`, `ui/lens`)

- **`USBGuide`**: título `<h2 className="text-fx-h3 text-fx-text">Preparar el dispositivo</h2>` + bajada `text-fx-body-sm text-fx-text-2`. Plataforma: `SelectButton` de ancho completo (`aria-label="Tipo de dispositivo"`, opciones Android/iPhone con `itemTemplate` = logo `<img alt="">` + texto), en lugar del `tablist` incompleto de hoy, que no tenía flechas ni `tabpanel`. "El dispositivo ya está listo, continuar": `Button text` de ancho completo con `ChevronRight` como `iconPos="right"`.
- **`AndroidGuide`**: grilla de marcas `grid grid-cols-2 sm:grid-cols-3 gap-3`. Cada marca es un `<button type="button" className="fx-card fx-card-interactive flex flex-col items-center gap-2.5 p-4 text-center min-h-11">` con el logo en `w-14 h-14 rounded-fx-lg bg-fx-surface-2 border border-fx-border`, nombre `text-fx-body-sm font-semibold text-fx-text` y "{n} pasos" `text-xs text-fx-text-3`. Se borran `handleGlowMove`, el halo radial y los `whileHover`/`whileTap`. Detalle de marca: volver = `Button text severity="secondary"` de ícono con `aria-label="Volver a la lista de marcas"`; marca en `text-fx-body font-bold` + nota `text-xs text-fx-text-3`; ficha = `rounded-fx-lg border border-fx-border overflow-hidden bg-white` (excepción de T3) con `Lens`; pie "Pasá el cursor para acercar" + link "Abrir en pestaña nueva" (`text-fx-accent-text hover:underline fx-focus-ring`, es la alternativa de teclado y táctil a la lupa); bloque "Al enchufar el cable" en `rounded-fx-lg border border-fx-info bg-fx-info-soft p-4`, con título `text-fx-label uppercase text-fx-info` y textos `text-fx-text`/`text-fx-text-2`; botones "Otra marca" (`Button severity="secondary"`) y "Listo, conectar el celular" (`Button` primario, `flex-1`, ícono `Check`). Este último deja de ser "success": es la CTA del panel.
- **`IOSGuide`**: el mismo tratamiento; "Listo, conectar el iPhone" = `Button` primario de ancho completo.
- **`Mockups.tsx`**: se **borran** las exportaciones sin consumidores (`StepIcon`, `PhoneMockup`, `DialogMockup`, `NotifMockup`, `AndroidVersionCard`, verificado con grep; `AndroidVersionCard` solo aparece comentado en `AndroidGuide`, y el comentario también se borra). Así se van los usos de `.phone-mock-*`. Se reescriben con tokens las que se usan:
  - `GuideStepList`: `<ol className="space-y-2">`, cada `<li className="fx-card flex gap-3 p-3.5">`, número `w-6 h-6 rounded-fx-pill bg-fx-surface-3 text-fx-text text-xs font-bold`, título `text-fx-body-sm font-semibold text-fx-text`, detalle `text-xs text-fx-text-2`.
  - `emphasize`: las opciones entre comillas en `font-bold text-fx-text`, sin acento.
  - `StepTip` (misma firma `{ tip, color?: "indigo" | "amber" }`): `amber` → `bg-fx-warning-soft border border-fx-warning text-fx-warning`; `indigo` → `bg-fx-info-soft border border-fx-info text-fx-info`. `rounded-fx-md px-3 py-2.5 text-xs` + `Zap`.
  - `TerminalBlock`: `rounded-fx-md border border-fx-border-strong overflow-hidden`; barra `bg-fx-surface-3 px-3 py-2` con `Terminal` + "Terminal" (`text-fx-label uppercase text-fx-text-2`), **sin** los tres puntos de colores; cuerpo `bg-fx-bg px-3 py-3 flex gap-2` con `$` `text-fx-text-3` y comando `font-mono text-xs text-fx-text break-all select-all`; botón copiar `w-8 h-8 rounded-fx-sm fx-focus-ring text-fx-text-2 hover:text-fx-text`, con `Check`/`Copy`, el mismo `aria-label` dinámico y un `<span role="status" className="sr-only">` que dice "Comando copiado" durante 2 s.
- `data.ts` y `types.ts` **no se tocan**: los campos `mockup`/`dialog` quedan como datos sin renderizar, igual que hoy.
- **`ui/lens`** (misma firma): sin framer. La capa ampliada aparece con `motion-safe:animate-[fx-fade-in_var(--fx-dur-fast)_var(--fx-ease-out)_both]`. El aro pasa de `ring-white/70` a `ring-2 ring-fx-border-strong shadow-fx-2`. Se actualiza el comentario de cabecera ("fichas de la guía USB"). Sigue siendo solo con mouse; la alternativa es el link "Abrir en pestaña nueva".

### T15. Demos en `/design-system` (sección `prime`)

Con datos ficticios, sin llamar a la API. Subsecciones nuevas:
- "Tag": las 5 severidades, con ícono.
- "SelectButton": las vistas Lista/Tabla/Cuadrícula.
- "Calendar (rango)": con label `sr-only`.
- "DataTable + Paginator": 3 filas, orden por columna con `lazy` y estado local.
- "InputTextarea": normal, invalid y disabled.
- "Tooltip (FxTip)": sobre un botón de ícono.
- "ConfirmDialog": destructivo y neutro.
- "FxBanner": 4 tonos, con `role="note"` en la demo para que no se anuncie al cargar.
- "Toast global (useFxToast)": 4 botones.

Las secciones "Notificaciones y tooltips" (sonner/base-ui) y "Convivencia legacy" no se tocan.

### T16. Lo que no cambia

Estado, efectos y handlers de `page.tsx` (`handleResume`, `proceedResume`, `startWizard`, `resetWizard`, `loadHistory`…), el wizard completo, `useAuth`, `usePublicConfig`, `useAgentConnection`, `api.ts`, `agent.ts`, `types/`, `pericial.ts`, `format.ts`, `AppNavbar`, `UserMenu`, `ThemeSwitch`, `SystemStatusLine`, `AppToaster`, `ui/tooltip.tsx`, `ui/dotted-glow-background.tsx` (lo sigue usando `ResultStep`), `globals.css`, `tailwind.config.ts` y `locale-es.ts`. No se desinstala ninguna dependencia (parte 4).

## 7. Checklist atómico: `implementer-frontend` (solo `client/`)

> Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como criterio, **sin** agregar 3D) y `web-design-guidelines` (autochequeo al final). Aplican también `ui-styling` (pt nuevos) y `mblode-agent-skills-ui-animation` (entradas, elevación, reduced motion). Dejar constancia en `progress/impl_frontend_rediseno-dashboard-historial.md`. Leer `client/AGENTS.md` y las guías de `client/node_modules/next/dist/docs/` que apliquen (client components; no se usa ninguna API nueva de Next). No tocar `backlog.json`, `progress/current.md`, `server/` ni `agent-ui/`. La HU no escribe en la base.

**Preparación**
- [ ] F0. Confirmar que la rama tiene `auth-e-integraciones-sin-mpf` y `rediseno-pagina-inicio` (existen `pt/message.ts`, `pt/password.ts`, `components/form/FxPassword.tsx` y `supportEnabled` en `usePublicConfig`). Anotar en el progress los textos actuales de `GuideModal` y `SoporteModal`, para conservarlos.

**Pass-through y helpers**
- [ ] F1. `pt/inputtext.ts`: exportar `inputRootClasses` y usarla en `root`, sin cambiar clases (T4).
- [ ] F2. `pt/shared.ts`: agregar `FX_BUTTON_SECONDARY` (T4).
- [ ] F3. Crear `pt/tag.ts` (T4).
- [ ] F4. Crear `pt/selectbutton.ts` (T4).
- [ ] F5. Crear `pt/paginator.ts` (T4).
- [ ] F6. Crear `pt/datatable.ts` y `pt/column.ts` (T4).
- [ ] F7. Crear `pt/calendar.ts` (T4).
- [ ] F8. Crear `pt/tooltip.ts` (T4).
- [ ] F9. Crear `pt/inputtextarea.ts` (T4).
- [ ] F10. `pt/dialog.ts`: variante `position === "right"` en `root`/`mask`/`transition`, sin cambiar la centrada (T9).
- [ ] F11. Registrar `tag`, `selectbutton`, `paginator`, `datatable`, `column`, `calendar`, `tooltip` e `inputtextarea` en `pt/index.ts`. `npx tsc --noEmit` pasa.

**Primitivas propias**
- [ ] F12. Crear `components/overlay/FxTip.tsx` (T10).
- [ ] F13. Crear `components/feedback/FxBanner.tsx` (T7).
- [ ] F14. Crear `components/shell/FxToastProvider.tsx` con `useFxToast` y montarlo en `AppProviders.tsx` (T11).
- [ ] F15. Reescribir `components/ui/alert-dialog.tsx` sobre `Dialog`, con la misma API (T8). `git diff` de `ReportStep.tsx`: vacío.
- [ ] F16. Crear `lib/case-status.ts` (T6).

**Historial**
- [ ] F17. `StatusBadge` → `Tag` (T6).
- [ ] F18. `AgentChip` con tokens y la píldora REC (T6).
- [ ] F19. `DashboardStats` con `<dl>` y tokens (T6).
- [ ] F20. `CaseCard` (`li`, disclosure con `aria-controls`, descargas `FX_BUTTON_*`, sin `index`) (T6).
- [ ] F21. `CaseGridCard` (`li`, elevación al hover y `focus-within`, `CASE_STATUS`, sin `STATUS_STYLE` ni `index`) (T6).
- [ ] F22. `CaseHistory`: buscador, `Calendar`, `SelectButton`, `DataTable` con `lazy`, `Paginator`, estados, foco y scroll al paginar; la lógica de filtro, orden y paginado queda intacta (T6).

**Diálogos y guía**
- [ ] F23. `ResumeDeviceModal` → `Dialog` con `cas: Case | null` (T12).
- [ ] F24. `SoporteModal` → `Dialog` + `SelectButton` + `InputText`/`InputTextarea` + `Tag` + `FxBanner`, mismas llamadas a la API (T12).
- [ ] F25. `GuideModal` → `Dialog position="right"` con `visible` (T12).
- [ ] F26. `USBGuide`, `AndroidGuide`, `IOSGuide`, `Mockups` (borrar las exportaciones sin uso) y `ui/lens` (T14).

**Página**
- [ ] F27. `dashboard/page.tsx`: navbar (FxTip, "+" `hidden sm:inline-flex`, Guía, Soporte condicional, `showThemeToggle`), raíz `bg-fx-bg`, `pb-14`, bloque historial (T5), montaje de modales y foco de Guía → Soporte (T13). Borrar `FloatingDock` y `useTheme`. **`git diff` del bloque `mode === "wizard"`: vacío.**
- [ ] F28. `ExpertProfileDialog.tsx`: solo sonner → `useFxToast` (T11). `git diff` del archivo: el import y la línea del toast, nada más.

**Borrados**
- [ ] F29. `grep -rnE "ui/(data-table|pagination|date-range-calendar|cycling-placeholder-input|floating-dock)|STATUS_STYLE|PhoneMockup|DialogMockup|NotifMockup|AndroidVersionCard|StepIcon" client/src` → sin resultados. Después, `git rm` de los cinco `ui/*.tsx`.

**Demos y cierre**
- [ ] F30. Demos en `/design-system` (T15).
- [ ] F31. Grep de aceptación (sección 8.2), limpio salvo las excepciones de T3.
- [ ] F32. `npx tsc --noEmit` y `npm run build` sin errores.
- [ ] F33. Chequeo manual de la sección 8.3, en oscuro, claro, mobile y reduced motion. Anotar resultados, medidas de contraste que no estén en la tabla T4 de la base y capturas o descripción en el progress.
- [ ] F34. Escribir `progress/impl_frontend_rediseno-dashboard-historial.md`: archivos tocados, verificación, skills y hallazgos (por ejemplo, si algún pt interno de `Calendar` necesitó override, o si `role` en `FxBanner`/`ConfirmDialog` requirió el plan B).

## 8. Verificación

### 8.1 Comandos (desde `client/`, sin errores)

```bash
npx tsc --noEmit
npm run build
```

### 8.2 Grep de aceptación (archivos de esta parte)

```bash
cd client
FILES="src/components/CaseHistory.tsx src/components/CaseCard.tsx src/components/CaseGridCard.tsx \
  src/components/StatusBadge.tsx src/components/GuideModal.tsx src/components/USBGuide.tsx \
  src/components/usb-guide/AndroidGuide.tsx src/components/usb-guide/IOSGuide.tsx src/components/usb-guide/Mockups.tsx \
  src/components/ui/lens.tsx src/components/ui/alert-dialog.tsx \
  src/components/dashboard/AgentChip.tsx src/components/dashboard/DashboardStats.tsx \
  src/components/dashboard/ResumeDeviceModal.tsx src/components/dashboard/SoporteModal.tsx \
  src/components/overlay src/components/feedback src/components/shell/FxToastProvider.tsx \
  src/lib/case-status.ts src/lib/prime/pt"
# legacy, paleta, hex, rgba → solo las excepciones de T3 (dark:invert/opacity en <img>, bg-white de la ficha)
rg -n "var\(--(bg|text|blue|btn|border|green|red|amber|glow|section)|\b(card|card-hover|btn|btn-[a-z]+|badge[a-z-]*|input|section-label|hero-title|step-title|stat-card|phone-mock[a-z-]*)\b\"|(emerald|amber|red|teal|gray|slate|indigo|green|white)[-/][0-9]|#[0-9a-fA-F]{3,8}\b|rgba?\(|dark:" $FILES
# framer-motion fuera del wizard
rg -n "framer-motion" $FILES                     # → vacío
# pt sin dark: ni paleta ni hex
rg -n "dark:|#[0-9a-fA-F]{3,8}|(blue|gray|green|red|amber|emerald|teal|slate)-[0-9]" src/lib/prime/pt   # → vacío
# el wizard no se tocó
git diff develop -- src/app/dashboard/page.tsx | rg -n "StepIndicator|slideDir|CaptureStep|DeviceConnect|CaseFormStep|ReportStep|GenerateStep|ResultStep"   # → solo líneas de contexto, ninguna +/-
```

En `page.tsx`, el bloque historial y el navbar se revisan a ojo, porque el wizard conserva legacy hasta la parte 2.

### 8.3 Chequeo manual del implementador (`npm run dev`, consola abierta, sin warnings de hidratación)

1. **Oscuro y claro:** bienvenida, stats, borradores, historial en las tres vistas, chip (online, offline y REC con un grabado de prueba si hay equipo; si no, se documenta), guía, soporte (con `Integrations:Support:Enabled=true` si el entorno lo permite; si no, se verifica que el botón no aparece) y diálogo de reconexión. Contraste medido con DevTools en el chip, los `Tag`, el `SelectButton` activo, la página activa del paginador y los días elegidos del calendario.
2. **Mobile 360 × 640:** sin scroll horizontal en el historial, la navbar, la guía (pantalla completa) ni el soporte (pantalla completa). La tabla scrollea dentro de su tarjeta. CTA, borradores y descargas ≥ 44 px.
3. **Teclado:** Tab por la navbar (tooltip con foco, Escape lo oculta) → CTA → borradores → buscador (X limpia y devuelve el foco) → calendario (abre con el botón, flechas en la grilla, Enter elige, Escape cierra) → "Limpiar filtros" (el foco vuelve al buscador) → `SelectButton` (flechas, `aria-pressed`) → tarjetas (Enter despliega, `aria-expanded`) → cabeceras de la tabla (Enter ordena, `aria-sort` cambia) → paginador (`aria-current`; al cambiar de página, el foco y el scroll van al inicio de la lista).
4. **Diálogos:** guía, soporte, reconexión y "¿Salir de la inspección?" (entrar al wizard, ir al paso 2 y volver). En el DOM, `role="dialog"`/`"alertdialog"`, `aria-modal="true"`, `aria-labelledby`. Foco atrapado, Escape y retorno del foco al disparador. La confirmación arranca en "Seguir acá" y no se cierra al hacer click afuera. Guía → "Contactar soporte" → cerrar soporte: el foco va al botón "Guía de uso".
5. **Reconexión:** retomar un borrador sin el equipo. "Continuar inspección" está deshabilitado, y al conectar (o simular `devices_changed`) se habilita y el título cambia solo.
6. **Toast:** abrir "Mi perfil de perito" desde el menú de usuario y guardar. Abajo a la derecha aparece "Perfil de perito guardado", con ícono, sin tapar la navbar, y se cierra con teclado.
7. **Reduced motion** (DevTools → Rendering): sin desplazamientos ni escalados (elevación de tarjetas en 0, drawer sin deslizar, entradas sin `translate`). El único giro es el del `Loader2` de los `Button loading`.
8. **Regresión:** el wizard completo (pasos 1 a 6) funciona igual, con su estética legacy, y "Restaurar texto por defecto" de `ReportStep` sigue confirmando. `/`, la 404 y `/design-system` (con las demos nuevas) se ven bien.

### 8.4 Prueba manual que queda para el usuario

1. Entrar a `/dashboard` en oscuro y en claro: saludo grande con tu nombre, fecha, "Nueva inspección", estadísticas y "Pendientes de retomar" si hay borradores. Todo se lee bien en los dos modos.
2. Buscar por número de causa, titular, DNI e IMEI. Elegir un rango de fechas en el calendario (en español, empieza el lunes). Tocar "Limpiar filtros".
3. Cambiar entre Lista, Tabla y Cuadrícula. En la tabla, ordenar por N° de causa, Carátula, Fecha y Estado. Con más de 8 inspecciones, pasar de página.
4. Desplegar una inspección completada: ver hashes, contraseña y descargar el ZIP y el Word. En un borrador, "Retomar inspección" (con y sin el celular conectado).
5. Desde la navbar: abrir la Guía (panel a la derecha, con Android, iPhone y la lupa en las fichas), el Soporte (solo si la integración está habilitada) y cambiar el tema. Confirmar que ya no está la barra flotante inferior.
6. Mirar el chip de Tatana con el agente cerrado y abierto, y durante una grabación ("REC").
7. Empezar una inspección y salir en el paso 2: aparece "¿Salir de la inspección?".
8. Guardar "Mi perfil de perito" y ver la notificación abajo a la derecha.
9. Repetir lo básico en el celular o con el emulador a 360 px.

## 9. Decisiones pendientes

Modo autónomo: el orquestador toma la **recomendada**. Ninguna bloquea la implementación.

1. **DP1. Contenedor de la guía USB.** **(Recomendada) A:** `Dialog` de Prime con `position="right"` y una variante drawer en el pt (T9): cumple `role="dialog"`, `aria-modal`, foco atrapado y retorno del foco que exige el Gherkin. B: `Sidebar` de Prime, como decía el inventario de la HU. En 10.9.9 renderiza `role="complementary"`, sin `aria-modal`, sin foco atrapado y sin devolver el foco, y habría que completarlo a mano con `FocusTrap` y atributos por pt. Visualmente son iguales.
2. **DP2. Ubicación de `ConfirmDialog`.** **(Recomendada) A:** reescribirlo en su path actual (`components/ui/alert-dialog.tsx`) con la misma API. Así `ReportStep` y `page.tsx` no cambian de import, que es lo que pide la HU. La parte 4 decide si lo mueve. B: crearlo en `components/feedback/ConfirmDialog.tsx` y borrar el viejo. Obliga a tocar el import de `ReportStep` (archivo de la parte 2).
3. **DP3. El único `toast(...)` del dashboard está en `ExpertProfileDialog` (archivo de la parte 2).** **(Recomendada) A:** migrarlo en esta parte, porque el Gherkin "Notificaciones sin tapar la navbar" de la parte 1 usa justo ese ejemplo y el cambio son dos líneas (T11, sección 5). B: dejarlo en sonner hasta la parte 2. El Gherkin de la parte 1 quedaría sin un caso real para probar.

Informativo (consecuencias de decisiones validadas, sin respuesta):
- "Continuar inspección" del diálogo de reconexión pasa de aparecer al conectar a estar siempre visible y deshabilitado hasta conectar (T12).
- El botón "Nueva inspección" de la navbar se oculta en `< sm`, donde está el CTA grande (T5).
- El botón de soporte de la navbar usa `LifeBuoy` con la etiqueta "Soporte". `FaroIcon` queda en el header y en el botón "Enviar" del diálogo (D9 A de `auth-e-integraciones-sin-mpf`).

## Resolución de decisiones pendientes (2026-10-01, modo autónomo)

- **DP1 → A**, **DP2 → A**, **DP3 → A** (las recomendadas).


## Nota del orquestador (2026-10-01)

`zip-cifrado-real` (commit c500aa5) eliminó `zip_password` del contrato. `CaseCard` (T6) se restiliza tal como quedó: "Mostrar contraseña" → `api.getZipPassword(id)` + `CopyButton`, solo si `zip_encrypted === true`. `CopyButton` lo migra la parte 2 (wizard, DP5 A).
