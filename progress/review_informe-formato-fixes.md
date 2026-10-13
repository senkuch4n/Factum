# Review — informe-formato-fixes (#62)

**Veredicto:** APROBADA

## Checkpoints
- C1 (harness): [x] rama `feat/informe-formato-fixes`; Fase y verify.sh los valida el orquestador (cambios sin commitear, no re-corrí hu.mjs).
- C2: [x] HU con validación del usuario (sin script, 4 cm), SDD con checklist; el contrato es de comportamiento (sin campos JSON nuevos). Paridad `SafeText`/`ReportValues.Clean` vs `cleanText` verificada por lectura: mismo orden (quitar y luego `\r\n?`→`\n`, `\t`→espacio), mismos descartados (C0 salvo \t\n\r, U+202A-E, U+2066-9, U+FDD0-FDEF, `(cp&0xFFFE)==0xFFFE` en todos los planos, surrogates sueltos), conserva DEL/C1/LRM/ZWJ/BOM; el par no-carácter descarta los dos code units en ambos lados.
- C3: [x] solo backend + `client/src/lib/report-markdown.ts` + scripts de plantilla + tests; `SafeText` aplicado en todos los puntos de §5.2 (CaseFields x2, CaseService Create/Update/SaveReportTexts con `request with` antes de validar, ExpertProfile, CatalogText, AccountFieldRules, BrandingRules (400 C0 intacto), BrandingService, AgentAuditController). Sin migraciones ni escritura a Mongo. Sin logs con texto sensible (B-R9b loguea solo parte/tipo).
- C4: [x] `dotnet build` 0 errores (solo warnings previos NU1903); `dotnet test`: 1012 OK, 7 skipped (Mongo), 0 fallos; `npx tsc --noEmit` client OK. Plantillas: `--check` v4 y v6 en OK; regeneradas por código en scratchpad desde la plantilla de docs/ y SHA-256 idénticos a los versionados (determinista, no editadas a mano). Renders reales con soffice (tests con FACTUM_SOFFICE=1): `cover_all_max` y `c3_extremos` con la portada en 1 página (el título del escrito arranca en la p. 2), encabezado interior ≤ 2 líneas con "…" y membrete completo en portada; firma + carácter juntos en la misma página en `c2r`; "siendo las 13:30 horas"; sin "Se constató:" fijo; "Lic. en Informática" sin ".."; "Android 14" una vez. H02 (U+FFFE/FFFF/D800) generan; H04 sin bidi; OsLabel idempotente; 96 dpi; es-AR; H17 (0 errores del validador) cubiertos por los tests que pasan.
- C5: [x] progress backend y frontend presentes; el frontend deja constancia de las 4 skills ("sin hallazgos aplicables"), y del script tsx de paridad F1-F24. Temporales de mi review borrados (scratchpad); no toqué datos ni Storage.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `api-hardening.md` §4.8 debe corregirse (DEL/C1 se conservan; #62 es dueña del helper y de los puntos de entrada), como avisa la SDD §5.4.
- La apertura en Word real y la prueba manual de §11.4 quedan para el usuario.
- El nivel 2 de portada une el contacto con " · " a 8 pt: solo se activa en casos extremos; el nivel 0 queda idéntico a antes.
