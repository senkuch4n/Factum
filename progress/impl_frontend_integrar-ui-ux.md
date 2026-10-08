# Integración frontend — rediseño UI/UX de Leo (origin/ui/ux)

Tarea de **integración** (no HU nueva). Trae el rediseño de `imleticio`
(`origin/ui/ux`) a `feat/integrar-rediseno-ui-ux` (= `origin/develop`
actualizado). Solo toca `client/` (Next.js 16). No se tocó `agent-ui/` ni
`server/`. No se tocó la base de datos.

## 1. Merge

```
git merge origin/ui/ux --no-ff --no-commit
```

- `merge-base` = `42fa825`.
- Resultado: `Auto-merging client/src/app/dashboard/page.tsx` seguido de
  "Automatic merge went well; stopped before committing as requested".
- Contrario a lo previsto en el prompt, git **no** dejó marcadores de
  conflicto en `dashboard/page.tsx`: el merge de 3 vías resolvió el archivo
  de forma automática porque las adiciones de iOS de `develop` y el rediseño
  de Leo cayeron en regiones de texto mayormente disjuntas.
- Verifiqué que no quedaran marcadores `<<<<<<< / ======= / >>>>>>>` en
  ningún `.tsx`/`.ts` de `client/src` (los únicos hits de `=======` están en
  `node_modules/` y `.next/`, no son código fuente).

### Archivos de `client/` integrados

Nuevos (entran sin conflicto):
- `client/public/logo-mark-f.png`
- `client/src/components/case-detail/CaseDetailContent.tsx`
- `client/src/components/case-detail/CaseDetailModal.tsx`
- `client/src/components/case-detail/EvidenceHostChip.tsx`
- `client/src/components/case-detail/dialog-a11y.ts`
- `client/src/components/case-detail/morph.ts`
- `client/src/components/case-detail/useCaseAgentFlow.ts`
- `client/src/components/dashboard/CurrentSessionCard.tsx`
- `client/src/components/dashboard/GreetingHeadline.tsx`
- `client/src/components/dashboard/RecentActivityCard.tsx`
- `client/src/lib/activity.ts`

Modificados por el rediseño:
- `client/src/app/dashboard/page.tsx` (el único que git automergeó)
- `client/src/app/globals.css`
- `client/src/components/CaseCard.tsx`
- `client/src/components/CaseGridCard.tsx`
- `client/src/components/CaseHistory.tsx`
- `client/src/components/UserMenu.tsx`
- `client/src/components/dashboard/AgentChip.tsx`
- `client/src/components/dashboard/DashboardStats.tsx`
- `client/src/components/design-system/DesignSystemShowcase.tsx`
- `client/src/components/shell/AppNavbar.tsx`
- `client/src/lib/theme.ts`

Docs/bitácora de Leo (se dejan tal cual, no se tocan):
- `progress/impl_frontend_case-detail-modal.md`
- `progress/impl_frontend_dashboard-sidebar.md`
- `progress/impl_frontend_navbar-pill.md`
- `progress/sesiones/imleticio.md`

## 2. Resolución de `client/src/app/dashboard/page.tsx`

Comparé las tres versiones:
- `develop` (HEAD): 1084 líneas, **con** el cableado iOS.
- `origin/ui/ux` (Leo): 1101 líneas, **sin** cableado iOS (salió de un
  `develop` viejo anterior al trabajo de iOS).
- base común (`42fa825`): 1076 líneas, sin cableado iOS.
- resultado del automerge: 1109 líneas.

Verifiqué marcador por marcador que **todo** el cableado "iOS en Windows"
que agregó `develop` sobrevivió y quedó en el componente correcto del layout
nuevo de Leo:

| Pieza iOS requerida | Línea en el merge | Estado |
|---|---|---|
| `import { useAgentIosStatus } from "@/hooks/useAgentIosStatus";` | 24 | OK |
| `const { appleService, airplayAvailable, airplayReason } = useAgentIosStatus(agentOnline);` | 280 | OK |
| `const airplayUnavailableReason = airplayAvailable ? null : airplayReason ?? "uxplay_not_found";` | 281 | OK |
| `agentStatusMessage(identity.status, identity)` (2º arg) | 196 | OK |
| `agentStatusMessage(id.status === "outdated" ? "outdated" : "offline", id)` (2º arg) | 478 | OK |
| `appleServiceMissing={appleService === "missing"}` en `DeviceConnect` (paso 1) | 933 | OK |
| guarda `if (m === "airplay" && airplayUnavailableReason) return;` antes de `api.reportAgentEvent(...)` en `onSelectIosMode` | 976 | OK |
| `airplayUnavailableReason={airplayUnavailableReason}` en el componente de captura (paso 3) | 1037 | OK |

Comprobaciones clave de corrección semántica (no solo textual):
- `git diff HEAD:client/src/app/dashboard/page.tsx <merged>` filtrado por
  líneas iOS devuelve **vacío**: el cableado iOS quedó byte-a-byte igual que
  en `develop`; lo único que cambia respecto de `develop` es el rediseño de
  Leo.
- Las props iOS aterrizan en los componentes correctos del layout nuevo:
  `appleServiceMissing` sigue en `<DeviceConnect>` (paso 1) y
  `airplayUnavailableReason` en el componente de captura (paso 3). Leo
  reestructuró el envoltorio (fx-card, variantes de animación `slideDir`,
  sticky del paso 3) pero **no** renombró esos componentes hijos ni sus sets
  de props, así que no hubo que re-mapear nada a mano.

No hice ninguna edición manual sobre `page.tsx`: el automerge de 3 vías ya
dejó el layout de Leo con el cableado iOS de develop intacto, y la
verificación confirma que es correcto.

## 3. Limpieza de artefactos de tooling

- `.atl/.skill-registry.cache.json` y `.atl/skill-registry.md`: quitados del
  merge con `git rm --cached -r .atl` y borrados del working tree. (La
  carpeta `.atl/` queda vacía, git la ignora.)
- Agregado `.atl/` al `.gitignore` de la raíz (bloque "Tooling de skills").
- Los `progress/impl_frontend_*.md` y `progress/sesiones/imleticio.md` de
  Leo se dejaron exactamente como vienen. No se creó ni editó ninguna
  `progress/sesiones/<otra persona>.md`.

## 4. Verificación en `client/`

- `npx tsc --noEmit` → **exit 0**, sin errores.
- `npm run build` (ejecutado en un `git worktree` aparte para no pisar el
  `.next` del `npm run dev` del usuario en el checkout principal) → ver
  resultado abajo.

RESULTADO_BUILD_PLACEHOLDER

No hizo falta ningún ajuste adicional para que compilara: el rediseño de Leo
no chocó con props renombradas ni exports faltantes de los 19 commits que
`develop` avanzó.

## 5. Skills de UX/UI (autochequeo de la integración)

Invocados los cuatro obligatorios:

- **ui-ux-pro-max**: aplicado como criterio de autochequeo. El merge solo
  re-inserta props de lógica (estado iOS) dentro del layout ya aprobado de
  Leo; no se introdujo trabajo visual nuevo. Los estados de interacción del
  rediseño (vacío/hover/focus/error) vienen de la rama de Leo y se preservan
  intactos. Sin hallazgos aplicables a la integración.
- **senior-frontend**: patrón React/Next correcto — `useAgentIosStatus` es un
  hook llamado en el nivel superior del componente (no condicional), y las
  props derivadas (`airplayUnavailableReason`) se calculan de forma
  determinista a partir del estado del hook. Sin hallazgos aplicables.
- **3d-web-experience** (criterio): el rediseño de Leo usa Framer Motion para
  transiciones de pasos (variantes `slideDir`) con propósito (continuidad
  espacial entre pasos del wizard); no se detectaron efectos pseudo-3D ni
  animaciones sin propósito. No se agregó 3D. Sin hallazgos aplicables.
- **web-design-guidelines** (autochequeo final): los componentes nuevos de
  Leo incluyen un helper dedicado `case-detail/dialog-a11y.ts` y referencias
  `aria-*`/focus/role en `CurrentSessionCard`, `RecentActivityCard` y
  `GreetingHeadline`; el rediseño fue construido con accesibilidad en mente.
  La integración en sí no agrega ni quita marcado visual, así que no
  introduce regresiones de accesibilidad. Sin hallazgos aplicables a la
  integración (el diseño de Leo ya pasó su propia revisión en su rama).

## 6. Contrato compartido

Esta tarea no cambia el contrato client ↔ server ni client ↔ Tatana: los
nombres de campo del cableado iOS (`appleService`, `airplayAvailable`,
`airplayReason`, etc.) vienen tal cual de `develop` y no se modificaron. El
casing JSON del backend (snake_case_lower) no se toca.

## 7. Commit

Merge finalizado con un commit `--no-ff` que describe la integración y lleva
trailers de co-autoría (la del agente + la de `imleticio
<leonel_martinez00@hotmail.com>`, obtenida con
`git log -1 --format='%an <%ae>' origin/ui/ux`). No se hizo push ni se abrió
PR (lo hace el orquestador tras la prueba manual del usuario). No se movieron
tarjetas del Project.
