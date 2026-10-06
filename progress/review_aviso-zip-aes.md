# Review — aviso-zip-aes (tarea chica, sin HU/SDD)

**Veredicto:** APROBADA

## Checkpoints
- C1, C2: n/a (fuera del backlog, sin HU ni SDD).
- C3: [x] solo `client/`; un componente nuevo y tres consumidores; sin cambios de lógica, props ni contratos. Sin console.log ni TODOs.
- C4: [x] `npx tsc --noEmit` en `client/` corrido por mí: exit 0. No hay tests. La salida visual queda como prueba manual.
- C5: [x] `progress/impl_frontend_aviso-zip-aes.md` deja constancia de ui-ux-pro-max, senior-frontend, 3d-web-experience y web-design-guidelines.
- Accesibilidad: [x] `role="note"` vía FxBanner; el icono AlertTriangle lleva `aria-hidden="true"` (FxBanner.tsx:25-27); tono `warn` con tokens de contraste AA según el implementador (no los recalculé).
- Sin duplicados: [x] el texto vive solo en `ZipCompatNotice.tsx`. Se quitaron las dos líneas antiguas de ResultStep y el import `Lock`. Un grep no encuentra otro "AES-256" fuera del componente.
- Solo si cifrado: [x] los tres montajes están condicionados por `encrypted` / `isEncrypted`.
- Sistema visual: [x] reutiliza FxBanner (tone warn) y el `FADE_IN` existente.
- `aria-describedby="result-zip-compat"` del link de descarga sigue apuntando al id (ResultStep.tsx:142 y :147).

## Observaciones no bloqueantes
- FxBanner pasa por Prime `Message`, cuyo pt puede poner `aria-live="polite"` aunque el role sea `note`. Es el comportamiento del componente compartido y queda fuera de alcance.
- El aviso es más llamativo que la línea anterior. Es lo buscado, pero conviene mirarlo en tema oscuro.
- Falta la prueba manual con un caso cifrado y uno sin cifrar, en server, agent e historial.
