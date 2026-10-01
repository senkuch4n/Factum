# Review — informe-pericial-de-parte

**Veredicto:** APROBADA

## Checkpoints
- C1 backlog válido / una sola HU activa: [x] (lo valida verify.sh)
- C1 `./ops/harness/verify.sh` exit 0: [x] (corrido por mí: tsc client, tsc agent-ui y dotnet build limpios)
- C2 HU con Gherkin y validación: [x]
- C2 SDD con checklist y Contrato compartido: [x]
- C2 Nombres coinciden con el contrato (snake_case_lower): [x] `client/src/lib/api.ts` (Case, PeritoSnapshot, ReportTexts, CaptureRole, ExpertProfile, CaseDataRequest, report_hash, device.name) vs `Models/Case.cs`, `DTOs/CaseDtos.cs`, `DTOs/ProfileDtos.cs`. Las claves `missing` de `client/src/lib/pericial.ts` coinciden con `Services/Cases/CaseValidation.cs`.
- C3 Alcance (backend + client; sin Tatana ni agent-ui; ops/plantilla, .gitignore, README, Dockerfile): [x]
- C3 client/AGENTS.md respetado: [x] (sin hallazgos; tsc limpio)
- C3 agent-ui: [x] no se tocó
- C3 Mongo tolera documentos viejos y no reescribe ajenos: [x]. `[BsonIgnoreExtraElements]` en las clases, defaults `""`/null/`[]` y SchemaVersion 0. Todas las escrituras sobre `cases` son `$set` campo por campo (`MongoRepository.cs`), filtradas a casos no Generating/Completed. No hay ReplaceOne sobre `cases`. El único upsert es sobre `expert_profiles` por `_id`=DNI propio. El listado proyecta afuera `ReportTexts`.
- C3 Sin debug/TODO/datos sensibles en logs: [x]
- C4 `dotnet build` Backend: [x] 0 errores, 4 warnings NU1902/NU1903 preexistentes
- C4 `npx tsc --noEmit` client: [x]
- C4 Tests: [x] la SDD no pide tests; el implementador usó un arnés descartable ya borrado
- C4 Documento generado verificado: [x] según el progress (arnés con ReportService real, hashes y render). No re-ejecuté el smoke test de endpoints; la prueba de punta a punta con dispositivo real queda manual del usuario.
- C5 progress backend/frontend existen: [x]
- C5 Constancia de skills UX (ui-ux-pro-max, senior-frontend, 3d-web-experience, web-design-guidelines): [x] en `progress/impl_frontend_informe-pericial-de-parte.md`
- C5 Sin temporales ni datos ajenos tocados: [x]

## Verificaciones específicas del encargo
- **D10:** `ReportService.GenerateAsync` crea el ZIP solo con evidencia, calcula `zipHash` sobre el ZIP ya cerrado y no lo reabre. `AppendToZipAsync` fue eliminado. Después genera el DOCX con ese hash y calcula `reportHash` sobre el DOCX cerrado. `UpdateGeneratedAsync` persiste `ReportHash` y `GenerateResponse` lo expone.
- **Compatibilidad:** `GenerateAsync` responde 409 si el caso está Completed y 400 si `schema_version == 0`. Un informe viejo no se regenera. No hay migración ni escritura masiva.
- **Plantilla v4:** descomprimida, sin coincidencias de datos del estudio, MPF, GFD, Evidentia ni "ministerio p". `core.xml` tiene autor `Factum`. Trae los placeholders canónicos de D2 y los bloques condicionales; el header trae `{#MEMBRETE}`, `{ORGANIZACION}`, `{CONTACTO}` y `{LOGO_ORGANIZACION:…}`. Ningún dato real en el repo fuera de `docs/` (grep).
- **`docs/INFORME*`:** ningún archivo en el índice (`git ls-files` vacío). `.gitignore:42` lo cubre.
- **Terminología:** los restos de fiscal/denunciante/funcionario en `client/src` son identificadores, nombres de archivo/contrato (`foto_funcionario_*`, `captureWebcam`) y comentarios. No hay renombre de campos persistidos.
- **Alcance visual (D14 A):** sin rediseño del wizard. Cambios funcionales de pasos y textos.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- Al commitear hay que hacer `git add` de `server/src/Factum.Backend/Templates/plantilla_informe_v4.docx`, `ops/plantilla/` y los archivos nuevos (hoy sin trackear). La baja de la v3 y de `sin-foto-placeholder.png` ya está en el índice.
- `agent-ui/tsconfig.web.tsbuildinfo` (sin trackear) y `agent-ui/tsconfig.node.tsbuildinfo` (modificado) son artefactos de tsc: no deben ir al commit.
- Pendientes manuales del usuario: abrir la v4 en Word/WPS (solo se probó con LibreOffice), configurar `Report:DomicilioConstituido` en `appsettings.Local.json` y probar el recorrido completo en `npm run dev` con el backend nuevo y un dispositivo real (el implementador de frontend no pudo hacerlo porque el :8080 era el backend viejo).
- `ConvertDocxToPdfAsync`, `LibreOfficePaths` e `IsOnPath` siguen como código muerto, declarado en la SDD (T16).
- No hice smoke test de endpoints propio; confié en el reporte del implementador y en la lectura del código de servicio y repositorio.
