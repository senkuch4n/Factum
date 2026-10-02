# Review — rediseno-cierre-legacy

**Veredicto:** APROBADA

## Checkpoints
- C1 (verify.sh/backlog): [ ] no evaluado por el reviewer; lo corre el orquestador.
- C2 (cadena de documentos, contrato sin cambios): [x] HU/SDD existen; no se tocó api/types/hooks.
- C3 (arquitectura, solo `client/`): [x] el diff queda en `client/`; sin console.log ni TODOs nuevos.
- C4 `npx tsc --noEmit` en `client/`: [x] exit 0 (corrido por mí en el checkout).
- C4 `npm run build`: [x] corrido en una copia en el scratchpad (regla dura: no `next build` en el checkout principal); compila, rutas `/`, `/_not-found`, `/dashboard`, `/design-system` OK. Copia borrada.
- C5 progress y skills: [x] `progress/impl_frontend_rediseno-cierre-legacy.md` deja constancia de los 4 skills obligatorios.
- C5 sin temporales: [x] solo quedan `tsbuildinfo` de agent-ui (ignorados por pedido).

## Verificaciones propias
- Grep de la SDD sección 8 (sonner|@base-ui|cva|tw-animate|shadcn|motion/react|components/ui/|ThemeToggle|DevModeBanner|AppToaster|TooltipProvider|webcam-pixel-grid|folder-tree|floating-navbar|background-paths) sobre src, package.json, tailwind.config.ts, postcss.config.js, next.config.ts: 0. `components.json` y `src/components/ui/` no existen.
- globals.css: sin `@layer base`, `@apply`, clases ni keyframes legacy (grep 0). `--fx-radius-pill` definida (línea 75).
- Clases legacy como token exacto en `.ts/.tsx` (btn*, badge*, dot-live*, phone-mock*, shutter-btn*, explorer-*, section-label, animate-ping, etc.): 0 (solo `fx-card`, la prop `badge` de IOSModePicker y comentarios). `bg-brand|bg-background|text-foreground|border-border` y `rounded`/`rounded-sm..3xl` sueltos: 0. No hay `:outline` nuevo bajo `cn()`.
- `var(--…)` sin prefijo `--fx-` en src: solo `--font-sans` y `--font-mono` (definidas en layout). Ninguna variable legacy se lee.
- Dependencias: `package.json` sin las 6 sacadas y con framer-motion, clsx, tailwind-merge, lucide-react, primereact, next, react, react-dom. `package-lock.json`: 0 entradas `node_modules/<paquete sacado>`. `npm ls --depth=0` y `npm ls --all`: sin invalid/missing/extraneous.
- Moves: los 7 imports (ConfirmDialog x3 -> overlay/, CopyButton x3 -> feedback/, Lens x1 -> usb-guide/) apuntan a rutas nuevas; tsc limpio.
- `constants/animations.ts`: solo `EASE` y `slideDir` (import en `dashboard/page.tsx:39`); spring/fadeSlide/fadeUp/scaleIn: 0 usos.
- DP5: `tailwind.config.ts` agrega `borderColor.DEFAULT = var(--fx-border)`; el implementador mostró el preflight compilado con ese color.
- DP7: `lib/prime/pt/inputtext.ts` y `inputtextarea.ts` reaplican `props.className` al final de `cn`, sin `any`; `inputRootClasses` intacta (la usa calendar). `!pl-`/`!pr-` en src: 0. Medición Playwright declarada (40/12, 40/48, 40/40 iguales a antes); no la reproduje, pero el diff es consistente con la lógica de merge.
- DP8: contador y collator correctos en `CaseHistory.tsx` (líneas 26-34, 126, 174-176); caso 0 intacto.
- `fx-pill` agregado al grupo `rounded` del merge (`lib/utils.ts:15`) y a `tailwind.config.ts`.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- Fuera del checklist literal: renombre de la prop `showThemeToggle` -> `showThemeSwitch` (`AppNavbar.tsx`, `dashboard/page.tsx:482`, showcase `:261`). Declarado y consistente en los 3 archivos.
- El collator también se aplica a `estado` (strings ASCII): sin efecto práctico.
- Sin cubrir automáticamente (queda para el usuario): wizard con Tatana real, diálogos y bordes sin color de DP5 en esos pasos, en ambos temas.
