# impl_frontend — rediseno-dashboard-historial (Parte 1)

**Estado:** done (sin commitear, como pidió el orquestador)
**Rama:** `feat/rediseno-dashboard-historial` (base `c500aa5`, zip-cifrado-real)
**App:** solo `client/`. No se tocó `server/`, `agent-ui/`, `backlog.json` ni `progress/current.md`. No se escribió en la base.
**SDD:** `Refactorizaciones/rediseno-dashboard-historial.md` (DP1 A, DP2 A, DP3 A)

## F0. Precondiciones y textos conservados

- Existen `pt/message.ts`, `pt/password.ts`, `components/form/FxPassword.tsx` y `supportEnabled` en `usePublicConfig` → `auth-e-integraciones-sin-mpf` y `rediseno-pagina-inicio` están en la rama.
- Textos en disco antes de tocar, conservados tal cual:
  - `GuideModal`: header **"Ayuda con la conexión"** (no "Guía de conexión USB"), pie "Guía de uso de Factum", link "¿No conecta? Contactar soporte".
  - `SoporteModal`: título "Soporte", subtítulo "Reportá un problema o seguí el estado de tus reportes.", pestañas "Nuevo token"/"Mis tokens", "Ver todo en el portal de soporte", "Se va a reportar como {name} ({sigla})" con `hasRealSigla`, éxito "Token #{n} registrado".

## Archivos tocados

**Nuevos**
- `client/src/components/overlay/FxTip.tsx` (T10)
- `client/src/components/feedback/FxBanner.tsx` (T7)
- `client/src/components/shell/FxToastProvider.tsx` (T11, `useFxToast`)
- `client/src/lib/case-status.ts` (T6)
- `client/src/lib/prime/pt/{tag,selectbutton,paginator,datatable,column,calendar,tooltip,inputtextarea}.ts` (T4)

**Modificados / reescritos**
- `client/src/lib/prime/pt/inputtext.ts`: exporta `inputRootClasses` (mismas clases) (F1)
- `client/src/lib/prime/pt/shared.ts`: `FX_BUTTON_SECONDARY` (F2)
- `client/src/lib/prime/pt/dialog.ts`: variante drawer `position="right"` en root/header/content/footer/mask/transition; la centrada no cambia salvo `overscroll-contain` en content (F10)
- `client/src/lib/prime/pt/index.ts`: registra los 8 pt nuevos (F11)
- `client/src/components/ui/alert-dialog.tsx`: `ConfirmDialog` sobre `Dialog` Prime, misma API (F15)
- `client/src/components/{StatusBadge,CaseCard,CaseGridCard,CaseHistory,GuideModal,USBGuide}.tsx`
- `client/src/components/dashboard/{AgentChip,DashboardStats,ResumeDeviceModal,SoporteModal}.tsx`
- `client/src/components/usb-guide/{AndroidGuide,IOSGuide,Mockups}.tsx`, `client/src/components/ui/lens.tsx`
- `client/src/app/dashboard/page.tsx`: solo imports, raíz, modales, navbar, `pb-14`, bloque historial, `guideBtnRef`/`supportFromGuide`, sin `useTheme` (F27)
- `client/src/components/shell/AppProviders.tsx`: monta `FxToastProvider` (sonner y `TooltipProvider` siguen)
- `client/src/components/ExpertProfileDialog.tsx`: solo el import de sonner → `useFxToast`, `const toast = useFxToast();` y la línea `toast.success(…)` con el mismo texto (F28; 3 líneas)
- `client/src/components/shell/AppNavbar.tsx`: solo el JSDoc de `showThemeToggle` (ya no menciona el FloatingDock)
- `client/src/components/design-system/DesignSystemShowcase.tsx`: demos nuevas en la sección `prime` (F30)

**Borrados (`git rm`, quedan staged)** — F29, el grep previo no dio resultados:
`client/src/components/ui/{data-table,pagination,date-range-calendar,cycling-placeholder-input,floating-dock}.tsx`. No hubo denegaciones de permisos.

`ReportStep.tsx`: `git diff` vacío. Bloque `mode === "wizard"` de `page.tsx`: comparé byte a byte desde `<AnimatePresence mode="wait">{mode === "wizard"` hasta el final contra el original → **idéntico**. Los imports del wizard (`motion`, `AnimatePresence`, `EASE`, `slideDir`, `AlertCircle`, `X`…) se quedaron.

## Verificación

- `cd client && npx tsc --noEmit` → sin errores.
- `npm run build` → **no** lo corrí en el checkout principal (la regla dura del agente lo prohíbe porque pisa el `.next` del `npm run dev` del usuario). Lo corrí en un `git worktree` en el scratchpad con `client/src` sincronizado y una copia de `node_modules` (`cp -c`; con symlink Turbopack falla con "Symlink … points out of the filesystem root"). Resultado: `✓ Compiled successfully`, TypeScript OK, rutas `/`, `/_not-found`, `/dashboard` y `/design-system` estáticas. Después borré el worktree (`git worktree remove` + `prune`).
- Grep de aceptación 8.2 (con `rg`):
  - legacy/paleta/hex/rgba/`dark:` → solo las excepciones de T3, todas con comentario `contenido de imagen`: `dark:invert`/`dark:opacity-*` en los SVG de `USBGuide`, `IOSGuide`, `AndroidGuide` (`BrandLogo`) y `ResumeDeviceModal`, y `bg-white` en la ficha PNG de `AndroidGuide`. (Además matchea el comentario de cabecera de `pt/shared.ts`, que ya existía y solo describe la regla.)
  - `framer-motion` en los archivos de la parte → vacío.
  - pt: `dark:`/hex/paleta → vacío (salvo el mismo comentario de `shared.ts`).
- Ninguna base de datos tocada.

### Chequeo en runtime (F33)

Lo hice con Playwright (`playwright-core` instalado en el scratchpad + Chromium del caché) contra un `next dev -p 3107` levantado **en el worktree** (no en el checkout principal). Para el dashboard intercepté `http://localhost:8080/**` con datos ficticios (11 casos, 2 borradores, 1 error, `support_enabled: true`) y aborté `localhost:8765` (Tatana offline). No se llamó a ninguna API real.

Resultados (oscuro 1280 px, claro 360 px con `prefers-reduced-motion: reduce`):
- Sin overflow horizontal: `scrollWidth === innerWidth` en 1280 y 360. A 360 la navbar muestra Guía, Soporte, tema y avatar, y el "+" queda oculto (`hidden sm:inline-flex`).
- Consola sin errores ni warnings de hidratación (solo `ERR_CONNECTION_REFUSED` de la API del showcase, que no tiene mock).
- Borradores: 62 px de alto. Descargas ZIP/Word: 44 px. CTA `min-h-11`.
- Buscador: "Galaxy A1" → "3 de 11 resultados". La X limpia y devuelve el foco a `#case-search`.
- Tabla: `aria-sort` inicial `descending` en Fecha. Con foco en la cabecera "N° de causa" + Enter pasa a `ascending` y la primera fila es "1/26" (orden global, `lazy`).
- Paginador: `aria-label` "Página anterior / Página N / Página siguiente" y `aria-current="true"` en la activa (salen del locale). Al pasar a la página 2, el foco va a "Resultados de inspecciones".
- Guía: `role="dialog"`, `aria-modal`, `aria-labelledby`. Panel de 672 px pegado a la derecha en desktop y a pantalla completa (360 px) en mobile.
- Guía → "Contactar soporte" → Escape: el foco vuelve a "Guía de uso" (T13 verificado).
- Soporte a 360 px: ocupa 360×640, pantalla completa (después del fix de abajo).
- Reconexión: título "Conectá el dispositivo", "Continuar inspección" con `disabled=true` y "Cancelar" habilitado.
- `ConfirmDialog` (en el showcase): `role="alertdialog"` (el `pt.root.role` pisa el interno, así que no hizo falta el plan B), `aria-labelledby` del header, `aria-describedby` a la descripción y foco inicial en "Seguir acá". El click en la máscara no cierra, Escape sí cierra y el foco vuelve al disparador.
- `FxBanner` con `role="note"` → `note`. El `role` de props pisa el `alert` interno, así que tampoco hizo falta el plan B. `aria-live` queda `assertive` en error y `polite` en el resto.
- `FxTip`: aparece con foco de teclado (clases del pt, `absolute`, z 1101) y se oculta con Escape.
- Toast global: contenedor `position: fixed; right: 20px; bottom: 20px`.
- Calendar: el panel toma `absolute` + clases del pt. El `input` interno toma `inputRootClasses` + `pr-10`, y el botón de ícono y los botones "Hoy"/"Limpiar" toman las clases de la sección del calendar, con tailwind-merge sobre el pt global de button.

Pendiente de hacer a mano (no se pudo automatizar acá): mirar con equipo real el chip online y "REC", medir contraste con DevTools, hacer la regresión completa del wizard (pasos 1 a 6) y probar "Restaurar texto por defecto" de `ReportStep` con la app real. El estilo y el código del wizard no cambiaron. `ConfirmDialog` mantiene la API y el orden `onConfirm()` → `onOpenChange(false)`.

## Decisiones no obvias y hallazgos

1. **`zip_password` (aviso del orquestador):** `CaseCard` se restilizó tal como lo dejó `zip-cifrado-real`. La fila "Contraseña ZIP" usa "Mostrar contraseña" → `api.getZipPassword(id)` + `CopyButton`, y aparece solo si `zip_encrypted === true`. No se reintrodujo `zip_password`. `CopyButton` se usa con su firma actual, **sin modificarlo**, y sigue con estilo legacy (`btn-secondary`) adentro de la tarjeta nueva hasta que lo migre la parte 2. El botón "Mostrar contraseña" quedó nativo (con `aria-disabled` para no perder el foco mientras carga, como antes), estilizado con `FX_BUTTON_SECONDARY`. Las etiquetas "ZIP cifrado"/"ZIP de evidencia" y la nota "Este ZIP se generó sin cifrar" se conservaron.
2. **`rounded-fx-pill` no existe** en `tailwind.config.ts` (la SDD lo usa en `tag` y en el chip). Como no puedo tocar el config, usé `rounded-full`.
3. **Ancho de los `Dialog` por `pt`, no por `className`:** el `className` de props se fusiona **antes** que el pt global, y tailwind-merge se quedaba con el `w-[min(32rem,100%)]` del pt. Además, `maskClassName` **no se aplica en modo unstyled** (`cx('mask')` vuelve vacío). Por eso `ConfirmDialog`, `ResumeDeviceModal` y `SoporteModal` pasan ancho y pantalla completa en `< sm` por `pt={{ root, mask }}`, que se fusiona después del global. Lo detecté en runtime: el soporte no quedaba a pantalla completa en 360.
4. **`column.ts`:** DataTable resuelve cada sección de columna dos veces, una con los params de la tabla y `params.column` y otra con los de la columna. `meta()` normaliza las dos, así `props.sortable`/`context.sorted` se leen bien en ambas. Va registrado como `column` de primer nivel (F11).
5. **`calendar.ts`:** `input`, `dropdownButton`, `todayButton` y `clearButton` son InputText/Button anidados, así que su pt va en `{ root }`, como en el preset oficial. Los tipos públicos de 10.9.9 los declaran como atributos HTML planos, de ahí el cast. Sumé `enabled:active:bg-*` para neutralizar el `active` del Button de acento global.
6. **`tooltip.ts`:** la SDD pedía `px-0 py-0` en el root. Usé `py-1.5` arriba/abajo (`px-1.5` a los lados) para separar el globo del disparador, porque no hay flecha.
7. **`FxBanner.role`** acepta también `"note"`, para las demos estáticas que pide T15.
8. **`FxTip`:** `useId()` devuelve `«r1»`/`:r1:`, así que limpio los caracteres no alfanuméricos para que el selector `[data-fx-tip="…"]` sea válido.
9. **`ResumeDeviceModal`:** guarda el último `cas` no nulo con "ajuste de estado durante el render" (patrón de React) en vez de un efecto, para renderizar durante la salida.
10. **`DashboardStats`:** el `<dt>` va directo dentro del `div` de grupo (HTML válido de `dl`) y el ícono va adentro del `dt` con `aria-hidden`.
11. **`CaseHistory`:** el contenedor pasa a `<section aria-labelledby>`. El `aria-label` de la tabla va en `pt.table` (en el `<table>`, no en el wrapper). El input `type="search"` oculta la X nativa (`::-webkit-search-cancel-button`) para no duplicar la de "Limpiar búsqueda". Teclado del Calendar: el panel abre al enfocar el input y ArrowDown entra a la grilla (comportamiento de Prime).
12. **`USBGuide`:** el logo de plataforma dentro del `SelectButton` sigue el contraste de la opción (claro sobre el acento en modo claro y oscuro en modo oscuro), así no queda negro sobre verde.
13. **`ResumeDeviceModal`** conserva la X de cierre de Prime (`closable` por defecto), además de "Cancelar". La SDD no lo excluye.
14. **Hallazgo preexistente, no corregido (fuera de scope, "mismos textos"):** el conteo dice "11 inspecci**ó**nes registradas" (falta ortográfica de la lógica vieja: `inspección` + `es`). Además, el orden por "N° de causa" es lexicográfico ("10/26" antes de "2/26"), igual que antes. Los propongo como tarea chica aparte.

## Skills invocados

- **ui-ux-pro-max** (antes del JSX): búsquedas de UX sobre tabla en mobile (`overflow-x-auto` dentro de la tarjeta), estados vacíos con acción, foco visible en modales/drawer y paginación. Coincide con la SDD y lo apliqué: tabla con scroll propio, vacío con CTA, `fx-focus-ring`/`FOCUS_RING` en todo lo interactivo y foco + scroll al paginar.
- **senior-frontend**: sin framer en los archivos de la parte, ajuste de estado en render en lugar de `useEffect` (ResumeDeviceModal), `useMemo` estable para la API del toast, `useId` para los ids del ConfirmDialog, limpieza del timer en `TerminalBlock` y refs para devolver el foco.
- **3d-web-experience** (como criterio, sin agregar 3D): se eliminaron los efectos pseudo-3D y decorativos (halo radial que seguía al cursor en las marcas, `whileHover` scale, `DottedGlowBackground`, ícono flotante en loop, pulsos del modal de reconexión, `layoutId` de las pestañas). No se agregó 3D.
- **ui-styling**: los pt nuevos usan solo clases `fx-*`, sin `dark:`, hex ni paleta. Revisé el merge con tailwind-merge (punto 3 y 5).
- **mblode-agent-skills-ui-animation**: entradas con `motion-safe:animate-[fx-fade-in|fx-rise-in]`, drawer que entra en 320 ms y sale en 200 ms (salida más rápida, solo fade con reduced motion) y chevron con `motion-reduce:transition-none`. Hallazgo aplicado: la elevación al hover de `CaseGridCard` quedó limitada a `@media (hover:hover) and (pointer:fine)`, como `.fx-card-interactive`, para que no quede "pegada" en touch.
- **web-design-guidelines** (autochequeo final, guía de Vercel): apliqué `overscroll-contain` en el contenido de los `Dialog`, el placeholder del buscador terminado en "…", `translate="no"` en hashes y serial, `width`/`height` en el logo del modal de reconexión y `text-balance` en el h1. No apliqué la sincronización de filtros/vista/página con la URL porque está fuera de scope (D10 A: estado en memoria).

## Contrato

Coincide con la SDD: **sin cambios de contrato**. No se tocaron `lib/api.ts`, `types/`, `lib/agent.ts` ni `hooks/*`. Los campos de `Case`/`MiToken` se leen con sus nombres actuales en snake_case. `zip_password` no se usa porque ya no existe en el contrato (ver decisión 1).
