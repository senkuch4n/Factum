# impl_frontend — zip-cifrado-real

**Estado:** done (sin commit, como pidió el orquestador)
**Rama / base:** `feat/zip-cifrado-real` sobre `c1656ef`
**App tocada:** solo `client/`. `agent-ui/` y `server/` no se tocaron.
**SDD:** `Refactorizaciones/zip-cifrado-real.md` §8 (implementer-frontend, F1 a F9), §6 (contrato) y resolución P1 A / P2 A (las dos son de backend o de prueba manual; no cambian nada en el cliente).

## Archivos tocados

| Archivo | Ítem | Qué |
|---|---|---|
| `client/src/lib/api.ts` | F1 | `Case`: se borra `zip_password` y se agregan `zip_encrypted?: boolean` y `zip_encryption?: string \| null` (con JSDoc). `generateCase()` → `password: string \| null`. Nuevo `getZipPassword(caseId)` → `GET /api/cases/${caseId}/zip-password`, `Promise<{ password: string }>`. `PublicConfig.encrypt_zip: boolean` (con JSDoc). |
| `client/src/hooks/usePublicConfig.ts` | F2 | `PublicClientConfig.encryptZip`, `EMPTY.encryptZip = false` y el mapeo `cfg.encrypt_zip === true`. |
| `client/src/components/ui/CopyButton.tsx` (nuevo) | F3 | Botón "Copiar" (`btn-secondary btn-sm`) con `aria-label` que da contexto. Usa `navigator.clipboard.writeText`. La región `role="status"` + `aria-live="polite"` queda siempre montada y muestra "Copiada" o "No se pudo copiar" durante 2 s. Si no hay portapapeles o la llamada falla, sale el aviso y el texto de al lado sigue con `select-all`. El timer se limpia al desmontar. No hay `console.*`. |
| `client/src/components/dashboard/GenerateStep.tsx` | F4 | `usePublicConfig()`. Si `encryptZip`, muestra "ZIP cifrado con AES-256 y contraseña única" (ícono `Lock`). Si no, "ZIP de evidencia con hash SHA-256 verificable" (ícono `Archive`). |
| `client/src/components/ResultStep.tsx` | F5 | Props `password: string \| null` y `encrypted: boolean`. Si `encrypted && password`, aparece el bloque "Contraseña del ZIP" (mono, `select-all`, `translate="no"`), con `CopyButton` ("Copiar contraseña del ZIP") y el texto del canal separado, que reemplaza a "Guardala por seguridad". Debajo de los botones de descarga va la nota de compatibilidad, con el texto exacto de la SDD, enlazada al link "Descargar ZIP" con `aria-describedby`. El check "ZIP cifrado con AES-256" se suma a la lista. Si `!encrypted`: no hay bloque, ni nota, ni check. |
| `client/src/app/dashboard/page.tsx` | F6 | `result` suma `password: string \| null` y `encrypted: res.case.zip_encrypted === true`. Los dos se pasan a `ResultStep`. |
| `client/src/components/CaseCard.tsx` | F7 | Ya no lee `zip_password`. `isEncrypted = cas.zip_encrypted === true`. Si está cifrado: el botón dice "ZIP cifrado" y aparece la fila nueva `ZipPasswordRow`. Si no: el botón dice "ZIP de evidencia" y aparece la nota tenue "Este ZIP se generó sin cifrar" (ícono `Info`). Los hashes se muestran igual que antes. |
| `client/src/components/design-system/DesignSystemShowcase.tsx` | F8 | La demo de card dice "Informe Word + ZIP de evidencia" y el toast dice "El informe y el ZIP de evidencia quedaron guardados.". |

`client/src/types/index.ts` no se tocó. La SDD dice que no hace falta, y reexporta desde `api.ts`.

### `ZipPasswordRow` (dentro de `CaseCard.tsx`)

- Estados posibles: `idle`, `loading`, `error` y `shown` (con la contraseña).
- En `idle`, el botón dice **Mostrar contraseña** (ícono `Eye`). En `loading`, aparece un spinner con "Obteniendo contraseña…", más `aria-busy` y `aria-disabled`, y se ignoran los clics. En `error`, se ve "No se pudo obtener la contraseña. Probá de nuevo." en línea, con `role="alert"` y enlazado al botón con `aria-describedby`; el botón vuelve a estar disponible. En `shown`, la contraseña aparece en mono (`select-all`, `translate="no"`) junto con `CopyButton`.
- La contraseña vive solo en el `useState` de esta fila. La fila está dentro del panel expandible del card, así que **se descarta al cerrar el card** (AnimatePresence la desmonta) o al desmontar el historial. No se escribe en `localStorage` ni en `sessionStorage`, y no se loguea. Hay un guard `active` que evita hacer `setState` después de desmontar.

## Adaptaciones al código real (respecto de la SDD)

- La SDD se escribió sobre `f3471e7` + auth. El código real ya trae `rediseno-pagina-inicio`, que agregó `ApiError.serverMessage` y el login nuevo. `api.ts` se editó sobre esa versión, sin tocar `request`/`ApiError`, así que el login sigue igual.
- **F8:** la SDD hablaba de "L125" y "L428". En el código real esas líneas son la 127 (`DEMO_CARDS`) y la 485 (toast). Además, los textos decían "Informe **PDF** + ZIP cifrado" y "El **PDF** y el ZIP cifrado…", no "Word". Se reemplazaron por los textos exactos que pide la SDD.
- `GenerateStep` ya tenía la garantía con `Lock`. Solo se volvió condicional y se agregó el ícono `Archive` al import.

## Decisiones no obvias

- **`aria-disabled` en vez de `disabled`** en "Mostrar contraseña" durante la carga: con `disabled`, el foco del teclado se va a `<body>` mientras se espera la respuesta (y `.btn:disabled` también pone `pointer-events: none`). Con `aria-disabled` + `aria-busy` y el clic ignorado, el botón se comporta como deshabilitado para el lector de pantalla y para el mouse, pero conserva el foco. Visualmente queda `opacity-60 cursor-wait`. (ui-ux-pro-max: "Loading Indicators", que pide preservar el foco y un estado busy accesible.)
- **Foco al mostrar la contraseña:** el botón desaparece cuando llega la contraseña, así que el foco pasa al valor (`tabIndex={-1}`, con anillo `focus-visible`) para no perderlo y para que el lector lo lea.
- **Región `aria-live` de `CopyButton` siempre montada:** si se montara junto con el mensaje, varios lectores de pantalla no la anuncian.
- **`translate="no"`** en las dos contraseñas visibles (web-design-guidelines: identificadores y códigos), para que un traductor automático no las altere.
- La nota de compatibilidad va como un `<p>` a dos columnas debajo de la grilla de descargas, alineada a la izquierda. "Junto al botón" se resolvió así para no cambiar el layout de dos botones, y la relación queda explícita con `aria-describedby`. El wizard no se rediseñó.
- En `ResultStep`, `zipPassword = encrypted && password ? password : null` funciona como única fuente para renderizar el bloque y además estrecha el tipo para `CopyButton`.

## Skills invocadas (con `Skill`)

- **ui-ux-pro-max** (antes del JSX): búsquedas `ux` sobre "copy to clipboard feedback", "loading button disabled" e "inline error near field". De ahí salen: estado busy accesible que preserva el foco, error en línea al lado del control y enlazado con `aria-describedby`, y estado deshabilitado visible (opacidad + cursor).
- **senior-frontend:** componente reutilizable chico (`CopyButton`) con el timer limpiado al desmontar, estado local tipado como unión discriminada en `ZipPasswordRow`, guard contra `setState` después de desmontar y ningún dato sensible fuera del estado del componente.
- **3d-web-experience** (como criterio): no se agregó 3D ni efectos pseudo-3D ni animaciones nuevas. Lo único que se mueve es el spinner `Loader2` mientras carga, que tiene propósito. Sin hallazgos aplicables.
- **web-design-guidelines** (autochequeo final, reglas bajadas de vercel-labs/web-interface-guidelines) sobre `CopyButton`, `CaseCard`, `ResultStep` y `GenerateStep`. Hallazgos aplicados: `translate="no"` en las contraseñas y el texto de carga terminado en "…". Ya cumplían: íconos decorativos con `aria-hidden`, botones con nombre accesible, `aria-live` en el feedback asíncrono, foco visible (`.btn:focus-visible`, y anillo `focus-visible` en el valor con `outline-none` reemplazado), `break-all`/`min-w-0` para contenido largo. No aplica: Title Case (la UI está en castellano).

## Verificación

- `cd client && npx tsc --noEmit` → **exit 0**, sin salida.
- `npm run build` en una **copia en el scratchpad** (`rsync` de `client/` sin `.next`, con `node_modules` copiado). No se tocó el `.next` del `next dev` del usuario en el 3002. Resultado: `✓ Compiled successfully`, `Finished TypeScript`, 5/5 páginas estáticas (`/`, `/_not-found`, `/dashboard`, `/design-system`). (Nota: el primer intento con `node_modules` como symlink falla en Turbopack con module-not-found; por eso se usó una copia real.)
- **F9:** `grep -rn "zip_password\|ZIP cifrado\|cifrad" client/src` → solo quedan las apariciones condicionadas: `CaseCard` (`isEncrypted ? "ZIP cifrado" : …`), `ResultStep` (`encrypted ? ["ZIP cifrado con AES-256"] : []`), `GenerateStep` (`encryptZip ? … : …`) y comentarios/JSDoc. `zip_password` no aparece más en `client/src`.
- **Seguridad:** no hay `console.*`, `localStorage` ni `sessionStorage` en `CopyButton`, `CaseCard` ni `ResultStep`. La única mención es un comentario. La contraseña del wizard vive en el `useState` `result` de `dashboard/page.tsx` (como antes) y la del historial en el estado de `ZipPasswordRow`.
- **Pasada visual en `npm run dev`:** no se hizo. El único dev server es el del usuario en el 3002, y el backend con `encrypt_zip`/`zip-password` lo está implementando `implementer-backend` en paralelo. Queda cubierta por las pruebas manuales M1, M4, M6 y M8 de la SDD: GenerateStep, ResultStep, CaseCard en sus estados cifrado / sin cifrar / error de `zip-password`, y `/design-system`.

## Contrato (coincide con la SDD §6)

| JSON | TS en el cliente |
|---|---|
| `zip_encrypted` | `Case.zip_encrypted?: boolean`, se decide solo con `=== true` |
| `zip_encryption` | `Case.zip_encryption?: string \| null` (no se usa para decidir) |
| `zip_password` | borrado de `Case` |
| `password` (generate) | `generateCase(): { …; password: string \| null }` |
| `password` (zip-password) | `getZipPassword(caseId): Promise<{ password: string }>`, `GET /api/cases/${caseId}/zip-password` |
| `encrypt_zip` | `PublicConfig.encrypt_zip: boolean` → `PublicClientConfig.encryptZip` (`=== true`, `EMPTY` = `false`) |

## Bloqueos

Ninguno. No hubo acciones rechazadas por permisos.
