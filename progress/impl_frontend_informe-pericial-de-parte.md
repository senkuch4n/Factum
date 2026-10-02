# impl_frontend — informe-pericial-de-parte

**Implementador:** implementer-frontend (Claude Opus 5.5) · **Fecha:** 2026-10-01 · **Rama:** `feat/informe-pericial-de-parte`
**Alcance:** SDD `Refactorizaciones/informe-pericial-de-parte.md` §12.2 (F1–F17), solo `client/`. Sin commit. No se tocó `server/`, `ops/`, `README.md`, `backlog.json` ni `progress/current.md`. No se abrió `docs/INFORME*`. No se escribió en la base.
**Estado:** done (con la prueba manual en `npm run dev` pendiente: ver "Bloqueos / pendientes").

## Archivos tocados

Nuevos:
- `client/src/lib/pericial.ts`: claves `missing` (§6.4), mensajes "Ingresá …", `validateCaseForm`, `validateProfile`, `getMissingRequirements` → `{ key, label, step, fieldId }[]`, `describeMissing`, `prefillFromLastCase`, `isPhoneLike`, `caseToForm`, `formToCaseRequest`, `isRoleEligible`, largos máximos y formularios vacíos.
- `client/src/hooks/useExpertProfile.ts`: `profile`, `loading`, `error`, `save(req)`, `reload()` (+ `autoLoad`), `profileToForm`, `sameProfile`.
- `client/src/components/ExpertProfileCard.tsx`: `ExpertProfileFields` (los 5 campos, compartido) y `ExpertProfileCard` (plegada "Nombre · M.P. matrícula" + "Editar" / desplegada).
- `client/src/components/ExpertProfileDialog.tsx`: "Mi perfil de perito" (PrimeReact `Dialog` + `Button`, con el pt existente).
- `client/src/components/ReportStep.tsx`: paso 4 "Informe".

Modificados:
- `client/src/lib/api.ts`: `ApiError extends Error { status; missing? }` en `request()`; interfaces `Case` (campos de §6.2), `PeritoSnapshot`, `ReportTexts`/`ReportTextsInput`, `CaptureRole`, `FileSource`, `ExpertProfile`, `ExpertProfileRequest`, `CaseDataRequest`; `DeviceInput.name?`; `getCase` → `{ cas, files }`; métodos `getProfile`, `saveProfile`, `updateCase`, `getReportTextDefaults`, `saveReportTexts`, `saveCaptureRoles`; `generateCase` suma `report_hash`; `createCase` ya no manda `observaciones`.
- `client/src/types/index.ts`: re-exporta los tipos nuevos; `CaseFormData` con claves JSON; `ProfileFormData`; `CapturedFile.captureRole?`.
- `client/src/app/dashboard/page.tsx`: `STEPS` de 6 pasos (§8.1); `minJumpable = result ? 6 : currentCase ? 2 : 1`; salida con confirmación en los pasos 2 a 5; `handleSaveCase` (PUT del perfil si cambió o está incompleto → POST/PUT del caso → mapeo de `missing`); precarga al iniciar; `proceedResume` con la rama `schema_version === 0` (paso 2 en edición); `focusFieldId`; roles de captura; `result.reportHash`; `ReportStep` (4), `GenerateStep` (5), `ResultStep` (6).
- `client/src/components/CaseFormStep.tsx`: tarjeta de perfil + secciones plegables Actuación / Partes / Equipo, hints, sin "Observaciones", indicador `n/m`, modo edición (`mode`, `isLegacy`), aviso de caso viejo, foco al primer error y al campo pedido.
- `client/src/components/FormField.tsx`: prop `hint` (ayuda persistente con id `-hint`) y helper `describedBy`.
- `client/src/hooks/useFileManager.ts`: `setCaptureRole(name, role, opts)` (PUT inmediato si el archivo ya está subido); `handleUploadAndContinue` hace, después de subir todo, un `PUT capture-roles` con todas las capturas elegibles y su rol (o `null`).
- `client/src/components/CaptureStep.tsx`: "Marcar como…" (PrimeReact `Menu` popup), badge de texto en la miniatura, chips de estado + ayuda, identificación opcional con la terminología nueva, props `onPhotoPerito`/`onPhotoTitular`/`titularNombre`/`titularDni` (internos; nombres de archivo y `captureWebcam` sin cambios).
- `client/src/components/dashboard/GenerateStep.tsx`: checklist de obligatorios con enlaces, botón deshabilitado, `serverMissing`, carátula + titular, terminología.
- `client/src/components/ResultStep.tsx`: dos hashes, textos de §8.7.
- `client/src/components/CaseHistory.tsx`, `CaseCard.tsx`, `CaseGridCard.tsx`: §8.8.
- `client/src/components/UserMenu.tsx`, `client/src/components/shell/AppNavbar.tsx`: ítem "Mi perfil de perito" y montaje del diálogo.
- Terminología (§9): `IOSModePicker.tsx`, `USBGuide.tsx`, `usb-guide/AndroidGuide.tsx`, `GuideModal.tsx`, `design-system/DesignSystemShowcase.tsx`, y un comentario de `StepIndicator.tsx`.

## Contrato (§6): coincide con la SDD

- Todos los campos en `snake_case_lower`, tal como §6.1–6.3: `schema_version`, `perito{nombre,matricula,profesion,caracter,tratamiento}`, `nombre_tribunal`, `organismo_tribunal`, `sala_tribunal`, `integrantes_tribunal`, `tipo_causa`, `caratula`, `parte_denunciante`, `parte_denunciada`, `objeto_causa`, `ambito_causa`, `fecha_intervencion`, `nombre_proponente`, `profesion_proponente`, `matricula_proponente`, `tipo_dispositivo`, `linea_dispositivo`, `report_texts{objeto_informe,…,reserva,updated_at}`, `capture_roles[{filename,role}]`, `report_hash`, `device.name`, `file_sources`; perfil `dni`, `exists`, `is_complete`, `updated_at`; `PUT capture-roles` con `{ capture_roles: [{ filename, role|null }] }`; `UpdateCaseRequest` con `imei`; `GenerateResponse.report_hash`.
- Claves `missing` de `lib/pericial.ts` comparadas contra `server/src/Factum.Backend/Services/Cases/CaseValidation.cs` (ya escrito por el backend): idénticas (`perfil`, `perito`, los 10 de causa/equipo, `imei`, `report_texts.*` ×5, `capture_roles.imei_modelo`). El `PUT /api/profile` del backend devuelve `missing` con `nombre`/`matricula`/`profesion`/`caracter`; el cliente lo mapea a los campos del perfil.

## Verificación

- `cd client && npx tsc --noEmit` → **sin errores** (exit 0).
- `next build` en una **copia** de `client/` en el scratchpad (no en el checkout: el `next dev` del usuario en :3002 no se tocó). Resultado: `✓ Compiled successfully`, `Finished TypeScript`, rutas `/`, `/_not-found`, `/dashboard`, `/design-system` estáticas. La copia se borró al terminar.
- El `next dev` del usuario sirve `/dashboard` con 200 después de los cambios.
- Grep de aceptación de §9: lo que queda son identificadores y contrato (`fileType` "funcionario"/"denunciante", `captureWebcam`, claves `nombre_denunciante`/`parte_denunciante`, `foto_funcionario`) y las etiquetas que la propia SDD fija ("Parte denunciante", "Número de causa / expediente"). No queda ninguna etiqueta visible de rol fiscal, denunciante o expediente.

## Bloqueos / pendientes

- **No se pudo hacer el recorrido en `npm run dev` de §13** (perfil, caso de punta a punta, roles, autoguardado, checklist, historial, diálogo, claro y oscuro). El backend que corre en :8080 es el viejo: `GET /api/profile` responde 404, así que todavía no tiene los endpoints de `implementer-backend`. Queda para el reviewer o el usuario, con el backend nuevo levantado. Hasta entonces, la verificación es por tipos y build, como pidió el orquestador.

## Skills (constancia)

- **ui-ux-pro-max** (antes del JSX). Consultas `"error summary validation" --domain ux` y `"autosave status feedback" --domain ux`. Qué se aplicó:
  - un error inline por campo, con `aria-describedby` y `role="alert"`;
  - foco al primer error al hacer submit, que despliega la sección si está plegada;
  - el checklist de Generar como resumen de errores con enlaces a cada campo;
  - el autoguardado con los estados Guardando… / Guardado / Error + Reintentar en `aria-live`;
  - los chips de roles con texto e ícono, no solo con color.
- **senior-frontend**. Qué se aplicó:
  - un `ApiError` tipado con `status` y `missing`;
  - el autoguardado con debounce, `flush` en blur, Atrás y Continuar, y guardado al desmontar;
  - un contador de secuencia para que una respuesta vieja no pise el estado visible;
  - el hook `useExpertProfile` reutilizado por la tarjeta y el diálogo;
  - el foco al primer error disparado por un contador de submit (`focusErrorsTick`) y no por cada cambio de `errors`. El efecto anterior movía el foco al siguiente campo con error mientras se tipeaba.
- **3d-web-experience** (solo como criterio). No se agregó 3D ni efectos pseudo-3D. Las secciones plegables del paso 2 abren sin animación. El indicador `n/m` usa `transform: scaleX`, no anima `width`. Los efectos existentes de `ResultStep` (sparkles y pulso, que ya respeta reduced motion) quedaron como estaban. Sin hallazgos aplicables.
- **web-design-guidelines** (autochequeo con las guías descargadas de vercel-labs). Se corrigió:
  - los placeholders ahora terminan en "…";
  - se agregó `fx-focus-ring` a los botones nuevos con estilo propio: cabeceras de sección, "Restaurar", "Reintentar", "Marcar como…" y enlaces del checklist;
  - `break-words` en la carátula del resumen;
  - `tabular-nums` en `n/m`.
- **ui-styling / mblode-agent-skills-ui-animation**: no se invocaron. Sin shadcn nuevo ni animaciones nuevas (D14 A: sin rediseño).

## Decisiones no obvias

1. **"Marcar como…"** está en la barra del ítem seleccionado, debajo del teléfono, y no dentro de cada miniatura de 76 px. Cada captura es seleccionable desde la bandeja con su botón, y la miniatura muestra un badge de texto ("IMEI" / "Nombre", con el rol completo en el `aria-label`). Así se evita meter un tercer control diminuto por miniatura. Solo aparece en las capturas elegibles: nombre con `screenshot`/`captura` y extensión png/jpg/jpeg, el mismo criterio que `EvidenceClassifier.Screenshot`.
2. **Los chips de rol cuentan la unión** de `currentCase.capture_roles` y las marcas locales. La marca local manda sobre la del servidor para los archivos presentes en esta sesión.
3. **Errores del perfil** con claves `perfil.<campo>` en el mismo mapa de errores del paso 2. Un `missing: ["perfil"]` del servidor se resuelve con la validación local del perfil, o con un mensaje genérico en "nombre".
4. **Volver al paso 2 con caso creado** (riel o enlace del checklist) recarga el formulario desde `currentCase` y entra en modo edición (PUT). "Cancelar" vuelve al paso 3. En un caso `schema_version === 0` no hay "Cancelar".
5. **IMEI manual:** después de guardar, si el caso ya tiene IMEI, `selDevice.imei` se actualiza para que el paso 2 en edición no lo vuelva a pedir.
6. **Indicador `n/m`:** pasó de puntos a una barra fina más el texto. Con 10 a 15 obligatorios, una fila de puntos no entraba.
7. **`GenerateStep`:**
   - la garantía "Informe oficial en Word con la cadena de custodia" pasa a "Informe pericial en Word con la tabla de valores hash";
   - las fotos de identidad faltantes muestran "Sin foto · opcional" en gris (antes, ámbar).
8. **`ResultStep`:** se corrigió la tilde de "Datos que te van a ser útiles".
9. **`ExpertProfileDialog`** solo pide `GET /api/profile` al abrirse (`autoLoad: false`), así el navbar no hace un request extra en cada página. El dashboard recarga su perfil al iniciar una inspección, para tomar lo guardado desde el diálogo.
10. **Placeholders y ejemplos** sin datos reales: "Ej.: 1234/2026", "Ej.: Dres. Nombre Apellido y Nombre Apellido", "Tribunal de ejemplo".
