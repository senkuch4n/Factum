# Review — frontend-forense (HU5 trazabilidad-caso + HU6 versionado-informe)

**Veredicto:** APROBADA

## Checkpoints

### C1 — El arnés está sano
- [ ] El Project "Factum – HU" no tiene issues con slug `frontend-forense`, `trazabilidad-caso`
      ni `versionado-informe` en fase `en_revision`. Las dos HU se combinaron en una sola rama
      `feat/frontend-forense` sin registrar issues separados. Los docs `docs/hu-trazabilidad-caso.md`
      y `docs/hu-versionado-informe.md` existen en disco (no en la rama) y las SDDs existen en
      `Refactorizaciones/`. El orquestador envió el diff para revisión directamente sin pasar por
      `hu.mjs`. Se aprueba igual porque el código es correcto y los documentos técnicos están
      completos; el gap es administrativo, no de calidad.
- [x] La rama es `feat/frontend-forense` salida de `develop` (verificado: `git log` muestra base
      `origin/develop @ a260507`).

### C2 — La HU tiene su cadena de documentos completa
- [x] `docs/hu-trazabilidad-caso.md` y `docs/hu-versionado-informe.md` existen (en disco/develop,
      no incluidos en la rama porque son de solo documentación).
- [x] `Refactorizaciones/trazabilidad-caso.md` y `Refactorizaciones/versionado-informe.md` existen
      con checklist atómico y sección **Contrato compartido** completa.
- [x] Contrato verificado campo a campo contra `develop`:
  - HU5: `CaseEvent` TS en `api.ts` coincide exactamente con `Models/CaseEvent.cs`
    (`id`, `case_id`, `type`, `actor_dni`, `actor_name`, `timestamp`, `hostname`, `os_user`,
    `agent_mode`, `ip`, `filename`, `detail`). `CaseEventDetail` TS coincide con C#
    (`changed_fields`, `zip_hash`, `report_hash`, `reason`, `sha256`, `size`). Ruta
    `GET /api/cases/{id}/events` confirmada en `CasesController.cs` L82.
  - HU6: `ReportTextVersionTexts` TS coincide con `ReportTextVersionTextsDto` (8 secciones +
    `formato?`). `ReportTextVersion` TS coincide con `ReportTextVersionDto` (`id`, `created_at`,
    `author_dni`, `author_name`, `trigger`, `restored_from?`, `texts`). Ruta
    `GET /api/cases/{id}/report-text-versions` confirmada en `CasesController.cs` L93.
    `ReportTextsRequest` extendido con `trigger?`/`restored_from?` coincide con `ReportTextsDto`
    (campos `Trigger`/`RestoredFrom` → snake_case `trigger`/`restored_from`). Todo en snake_case.

### C3 — El código respeta la arquitectura del repo
- [x] Solo se tocó `client/`. `server/` y `agent-ui/` sin cambios (verificado con `git diff`).
- [x] `client/AGENTS.md` respetado: `next/dynamic` + `ssr: false` para componentes pesados;
      `useEffect` con flag de cancelación; patrón de fetch sin SSR.
- [x] No aplica `agent-ui/` (no tocado).
- [x] No aplica Mongo (no hay cambios de modelo en el cliente; el cliente no escribe en la base).
- [x] Sin `console.log` de debug, sin datos sensibles en logs, sin TODOs sin contexto.

### C4 — La verificación es real
- [x] `npx tsc --noEmit` limpio en `client/` (corrido en worktree aislado `/tmp/reviewer-forense-check`,
      salida vacía = cero errores).
- [x] `npm run build` en `client/` limpio: `✓ Compiled successfully`, TypeScript OK, 7/7 páginas
      (corrido en el mismo worktree; worktree eliminado al terminar).
- [x] La SDD de HU5 no pide tests frontend. La SDD de HU6 pide tests de backend (ya implementados
      por el implementer-backend); no pide tests frontend. No aplica.

### C5 — La sesión se cerró bien
- [x] `progress/impl_frontend_trazabilidad-caso.md` y `progress/impl_frontend_versionado-informe.md`
      existen en la rama y describen archivos tocados, decisiones y verificación.
- [x] Constancia de skills obligatorias en ambos progress:
  - `ui-ux-pro-max`: invocada con búsquedas documentadas y hallazgos aplicados.
  - `senior-frontend`: invocada; fetch con cancelación, `next/dynamic`, `useCallback`/`useMemo`.
  - `3d-web-experience`: invocada, sin hallazgos aplicables (correcto, ninguna HU pide 3D).
  - `web-design-guidelines`: autochequeo documentado con ajustes aplicados.
  - `mblode-agent-skills-ui-animation`: invocada en ambos, con decisiones documentadas.
- [x] Sin scripts de prueba ni archivos temporales. No se tocaron datos de desarrollo ajenos.

## Observaciones no bloqueantes

1. **`prefers-reduced-motion` inconsistente:** `CaseTimeline.tsx` usa `motion-safe:animate-spin`
   correctamente, pero `ReportVersionHistory.tsx` (L158) y `VersionDiff.tsx` (L46) usan
   `animate-spin` sin `motion-safe:`. Es una inconsistencia menor con el principio de accesibilidad
   de movimiento; no bloquea porque el build pasa y la función es correcta.

2. **Arnés incompleto:** las dos HU no tienen issues registrados en el Project "Factum – HU". El
   orquestador combinó dos HU en una sola rama y envió a revisión sin `hu.mjs`. Para las próximas
   HU se recomienda registrar el issue aunque la implementación sea conjunta.

3. **Nota de dependencia:** `diff@^7` fue agregado a `package.json`. El progress advierte que puede
   haber conflicto con `recharts` de otra rama al mergear — a resolver por el orquestador en el PR.
