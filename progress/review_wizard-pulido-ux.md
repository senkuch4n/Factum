# Review — wizard-pulido-ux (#64)

**Veredicto:** APROBADA

Verificado por el reviewer (no por los progress): `dotnet build` backend 0 errores (4 warnings NU1902/NU1903 preexistentes);
`dotnet test` con OutDir temporal: 1091 pasan, 7 skipped (preexistentes), 0 fallan; `npx tsc --noEmit` en `client/` limpio.
El build temporal se borró. No se tocó ni se escribió ninguna base ni evidencia.

## Checkpoints
- C1 rama/Fase: [x] HU #64 en `en_revision`, rama `feat/wizard-pulido-ux`. `verify.sh` lo corre el orquestador antes de cerrar (no lo ejecuté).
- C2 cadena de documentos: [x] HU, SDD y progress existen. Contrato sin campos nuevos: se lee `/agent/state` -> `operations[].kind` (ya existente) y `ev-theme`; `zip_filename` solo cambia de valor.
- C3 arquitectura: [x] Solo `client/` y `ReportService`/`AgentGeneration` (lo que declara la SDD); `Factum.Agent` y `agent-ui/` sin tocar. Sin Mongo nuevo: `ResolveZipFilename` conserva `ZipFilename` registrado (D4/DT10). Sin console.log ni datos sensibles.
- C4 verificación real: [x] build, tsc y tests OK (arriba). 16 métodos de test (25 casos) cubren Sanitize (tildes NFC/NFD, corte en separador antes de 60, `_` sin colapsar, surrogates), ResolveZipFilename (registrado, intento legacy, datos cambiados), StemFromZip y la regex idéntica a la de Tatana (lee `AgentFileNames.cs`). Salida real del ZIP en Windows queda como prueba manual explícita (impl_backend).
- C5 cierre de sesión: [x] Ambos progress presentes. Constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience`, `web-design-guidelines` (+ ui-styling y animation) en impl_frontend §4. Sin archivos temporales en el repo.

## Puntos pedidos
- Chip D1/DT6/DT7: [x] `lib/agent-chip.ts` implementa la tabla (offline > equipo de la inspección conectado/desconectado > listando > 0/1/N), fabricante capitalizado solo si viene todo en minúscula. Una sola región viva `role=status`.
- REC D2: [x] `useAgentRecordingActivity` lee `/agent/state` (`recording_android|ios`), `recording = isRecording || remoteRecording`. En `handleWsEvent` la relectura es la primera sentencia y solo dispara un refresh: no altera el filtro de atribución de #61 ni `refreshRecording`/recuperación de #59.
- 360/390 px: [x] En el historial el chip va en `AgentChipBelowNav` (fila propia `sm:hidden`), ya no tapa "Métricas"; en el wizard el indicador de 32 px queda dentro de la barra (el breadcrumb trunca).
- DT9: [x] `showOnFocus={coarsePointer}` (la lectura validada; el signo de la SDD está invertido), Alt+↓/↓ abren y llevan el foco al día, `aria-keyshortcuts`. El `onChange` que no borra lo tipeado es un fix razonable.
- F13 (rangos de CaseHistory/DashboardBreakdown): [x] sin tocar y anotado como prueba manual (impl_frontend §5.2 y "Ajustes del orquestador" 2).
- "Ver todo" (DT16/#56): [x] escenario y metadatos hijos directos de la columna, carátula expandida es el único bloque que cede, con scroll propio acotado a la carátula (no hay scroll general de la ficha), piso medido para que nunca se superponga con el índice.
- Scroll: [x] `lib/scroll.ts` mueve solo el contenedor más cercano; los 4 usos de `scrollIntoView` del wizard migrados; `relative` en el contenedor corrige el estiramiento por `sr-only`.
- Tema: [x] `storage` (solo `ev-theme`), `visibilitychange`, `pageshow`; sin view transition si la pestaña está oculta, remoto o reduced-motion; todas las promesas de la transición con `catch`.
- Nombre del ZIP: [x] DT10/DT11/DT12 implementados como pide la SDD; el DOCX usa la misma base que el ZIP.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- Cambio fuera de la letra de la SDD, justificado en el progress: ficha `lg:h-[calc(100dvh-13rem)]` y mínimo del escenario de 6rem con alto < 800 px.
- Prueba manual pendiente: desenchufar el celular (chip "desconectado"), grabar de verdad en una pestaña y ver REC en otra, calendarios de rango con Tab (F13), ZIP/DOCX en Windows (Explorador y 7-Zip), ficha con carátula larga en 1024x720.
- Con puntero fino, clic en el input de la fecha ya no abre el panel (solo el ícono o Alt+↓): consecuencia aceptada de `showOnFocus={coarsePointer}`.
- B4 (pestañas "Inspecciones | Métricas" apretadas a 360/390 px) es previo a esta HU y sigue fuera de alcance.
- Corregir el signo de DT9 en la SDD (`!coarsePointer` -> `coarsePointer`).
