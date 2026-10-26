APROBADO

# Review — informe-extraccion (#109)

**Veredicto:** APROBADA

## Checkpoints
- C2 contrato: [x] snake_case coincide backend/Tatana/client (content_base64, filename, directory, sha256, bytes, report_written_at, extraction_report_v1).
- C3 arquitectura: [x] solo Backend, Tatana, client, plantilla/script; agent-ui sin cambios; ReportWrittenAt nullable (tolera diarios viejos); backend sin cambios Mongo.
- C4 verificacion: [x] dotnet build Backend y Agent 0 errores; tsc client limpio; build_plantilla_extraccion_v6.py --check OK; dotnet test Backend 1504 ok/15 skipped (Mongo), Agent 1077 ok; flaky Rfc3161 no aparecio.
- C5 cierre: [x] progress impl_backend/impl_frontend existen; el frontend deja constancia de los 4 skills.
- C1: no verificado por mi (Project/verify.sh fuera de alcance de esta revision).

## Cadena de custodia
Tatana.WriteReport (Agent/Services/Extraction/ExtractionService.cs) escribe solo INFORME.docx e INFORME.docx.sha256 (temp .part + rename, SafeChild, cap 20 MiB, exige estado terminal), hash sobre los bytes recibidos/escritos. No toca MANIFIESTO*, CIERRE ni sellos/; hay test que compara MANIFIESTO.tsv y .sha256 antes/despues. El informe remite al hash sellado (frase fija CustodyChain).

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- WriteReport usa `_journal.SaveAsync(...).GetAwaiter().GetResult()` (sincronico, consistente con WriteSeals); aceptable.
- Backend: generacion sin caso (bloque actuacionCaso condicional) e interrumpida/cancelada (bloque interrupcion) cubiertas por tests de ExtractionReportDocxTests; regresion del informe de inspeccion pasa (suite completa verde).
- Si el guardado en carpeta falla, el cliente igual descarga el DOCX y avisa; correcto.
- Falta prueba manual end-to-end con dispositivo (QA).
