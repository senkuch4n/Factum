# impl_frontend — sellado-tiempo (#78)

**Estado:** done · **Rama:** `feat/sellado-tiempo` · **App:** solo `client/` (`agent-ui/` sin tocar) · **Fecha:** 2026-10-14
**Implementador:** implementer-frontend (Opus 5.5). Sin commits, sin mover tarjetas, sin tocar `progress/sesiones/` ni `server/`.

Checklist ejecutado: SDD §12.2 con el diseño de §9 y el Contrato compartido de §7. Decisiones del usuario aplicadas:
D-T8 (una sola etapa combinada) y D-T3 (con el sellado apagado, la web no muestra bloques vacíos ni "pendientes" eternos).

## Archivos tocados

| Archivo | Qué |
|---|---|
| `client/src/lib/api.ts` | Tipos de §7.2 con los nombres exactos: `TimestampTarget`, `TimestampStatus`, `CaseTimestamp`, `EvidenceTimestampRow`, `CaseTimestampsResponse`, `TimestampFilesResponse`, `TimestampFileItem`. `EvidenceResponse` suma `timestamping_enabled?`, `timestamp_authority?` y `timestamps?` (opcionales, §7.6). `Case.timestamp_mode?`, `PublicConfig.timestamping_enabled?`. `CaseEventType` suma `timestamp_granted`/`timestamp_failed`; `CaseEventDetail` suma `target`, `authority`, `gen_time`, `serial_number`, `tsa_url`, `attempt`, `final`. Funciones `api.getCaseTimestamps(caseId, signal?)` (`requestSafe`, `no-store`), `api.getTimestampFiles(caseId)` y `api.timestampBundleURL(caseId)` (mismo armado que `downloadURL`, con `?token=`). |
| `client/src/types/index.ts` | Reexporta los 7 tipos nuevos. |
| `client/src/lib/agent.ts` | `WriteSealsRequest`, `WriteSealsResponse`, `agent.writeCaseSeals(caseId, body)` (`PUT /cases/{id}/zip/seals`, `jsonInit`) y `agent.supportsCaseSeals()`. El caché de capabilities ahora pasa por `agent.hasCapability(name)`, que reusa `supportsRecordingState` (mismo comportamiento: se cachea por conexión, un error de red da `false` y no se cachea). Documentada la capability `case_seals_v1`. |
| `client/src/hooks/usePublicConfig.ts` | `timestampingEnabled` (`=== true`; `false` si falta). |
| `client/src/lib/timestamps.ts` (nuevo) | `TARGET_LABEL`, `timestampLine(ts, ctx)` (tabla de §9.4), `evidenceSummary`, `hasGranted`, `hasPending`, `grantedText`, `timestampPanelMode` y `syncCaseSeals(cas)`. |
| `client/src/lib/case-events.ts` | Íconos (`ClockCheck` / `ClockAlert`), tono `warning` para `timestamp_failed`, títulos de §9.5, `eventFilename` con los dos tipos y `eventTimestampDetail(ev)`. |
| `client/src/components/case-detail/CaseTimeline.tsx` | Muestra `eventTimestampDetail` debajo del título, con el estilo de `eventHashChange` (monoespaciado y `translate="no"` en el obtenido; texto secundario en el motivo del fallo). |
| `client/src/components/case-detail/TimestampPanel.tsx` (nuevo) | El bloque "Sello de tiempo" de §9.4: carga al montar, filas ZIP / Informe / Evidencia (con desplegable por archivo), "Descargar sellos (.tsr)", "Copiar sellos a la carpeta del caso", auto-sincronización y polling de 30 s mientras haya pendientes. |
| `client/src/components/ResultStep.tsx` | `AgentResult`: monta `TimestampPanel` debajo de los bloques Informe/ZIP, con `zipState` y `sameHost`. Tras el auto-commit exitoso, `void syncCaseSeals(...)`. |
| `client/src/components/case-detail/CaseDetailContent.tsx` | En "Paquete del informe": flujo agent → `TimestampPanel` dentro de `AgentZipBlock` (con su `zipState`); flujo server → `TimestampPanel` con la fila informativa. Tras el auto-commit de `AgentZipBlock`, `void syncCaseSeals(cas)`. |
| `client/src/app/dashboard/page.tsx` | Estado de §9.3: `timestampsByName`, `timestampingEnabled` (`null` = backend viejo), `timestampAuthority` y el aviso. Se actualiza desde los dos `onEvidence` (registrar y borrar). Carga con `getCaseTimestamps` al entrar al paso 3/5, lo que incluye reanudar. Polling de 30 s en el paso 3/5 mientras haya `pending`, cortado al salir o cuando no quedan. Aviso único por caso. `void syncCaseSeals(res.case)` tras `commitZip` en `handleGenerateAgent`. |
| `client/src/components/CaptureStep.tsx` | Props nuevas `timestamps?: Record<string, CaseTimestamp>`, `timestampingEnabled?: boolean`, `timestampNotice?` y `onDismissTimestampNotice?`. `sealOf(item)` solo da sello con `timestampingEnabled` y el archivo registrado. Muestra el aviso como `FxBanner` informativo, descartable, en el mismo lugar que `uploadNotice`. |
| `client/src/components/capture/EvidenceTray.tsx` | `SealBadge` (reloj con check / reloj gris / reloj con advertencia) en las miniaturas y en los chips de adjuntos, con `title` (tooltip) y el texto en el `aria-label` del ítem: `Sello de tiempo: {gen_time_display} · {authority}`, `Sello de tiempo pendiente; se reintenta solo` o `No se pudo obtener el sello de tiempo`. Sin documento: nada. |
| `client/src/components/dashboard/GenerateStep.tsx` | Con `timestampingEnabled`, la etapa `report` dice "Sellando el ZIP, generando el informe y sellándolo…" y abajo, en chico: "El sello de tiempo lo emite una autoridad externa; si no responde, el caso se genera igual." (D-T8, una sola etapa). El texto del `aria-live` también cambia. Sin sellado, igual que antes. |

No hizo falta tocar `hooks/useFileManager.ts`, porque `onEvidence` ya recibe la `EvidenceResponse` completa.

## Verificación

```
$ cd client && npx tsc --noEmit
(sin salida, exit 0)
```

- No hay tests en `client/` y la SDD no pide ninguno. Probé la lógica pura (`timestampLine`, `timestampPanelMode`,
  `evidenceSummary`, `hasPending`, `eventTitle`, `eventTimestampDetail` y `eventTone`) con un script descartable en el
  scratchpad (jiti), fuera del repo. Los textos coinciden con §9.4 y §9.5. Por ejemplo:
  - `Sellado por Free TSA el 14/10/2026 09:46:05 (UTC-03:00) · N.º de serie 1A2B`, con la nota de `in_report=false`;
  - `Pendiente: se reintenta solo (intento 2)`;
  - `Sin sello (sellado desactivado; no se reintenta)`;
  - `Se abandonó el sello de tiempo (ZIP) tras 12 intentos`;
  - `Free TSA · fecha certificada 14/10/2026, 09:46:05 · sha256: abcdef012345…`.
- **Sin prueba visual en el navegador.** En esta corrida no estaban disponibles las herramientas de Chrome
  (`mcp__claude-in-chrome__*`), así que no hubo mock de red ni capturas. No quedó ningún mock en el repo. Para ver la
  UI hace falta la prueba manual de la SDD (§13.2) con el backend de esta rama.
- No corrí `next build`. El `next dev` de :3000 no se tocó.
- No se escribió nada en Mongo: esta HU no escribe desde el cliente.

## Contrato compartido: coincide con la SDD

Comparé con el `server/src/Factum.Backend/DTOs/TimestampDtos.cs` y el `EvidenceDtos.cs` que el implementer-backend
dejó en el working tree. Bajo `SnakeCaseLower` dan exactamente los nombres de §7.2:
- `id`, `target`, `filename`, `sha256`, `status`, `attempts`, `requested_at`, `next_attempt_at`, `last_error`,
  `authority`, `tsa_url`, `gen_time`, `gen_time_display`, `serial_number`, `policy_oid`, `granted_at`, `in_report`;
- `enabled`, `authority_hint`, `case_mode`, `evidence_storage`, `zip`, `report`, `evidence[{filename, sha256,
  timestamp}]`, `evidence_sealed`, `evidence_total`;
- `case_ref`, `zip_filename`, `files[{name, content_base64}]`, `readme`;
- `timestamping_enabled`, `timestamp_authority`, `timestamps`.

Tatana, según §7.3: el body es `{case_ref, zip_filename, files, readme}` y la respuesta, `{directory, written,
unchanged}`. Endpoints: `GET /api/cases/{id}/timestamps`, `…/timestamps/files`, `…/timestamps/bundle?token=` y
`PUT /cases/{id}/zip/seals`.

## Decisiones no obvias

1. **D-T3, sellado apagado: `timestampPanelMode`.** La tabla de §9.4 está pensada por fila. Con el sellado apagado,
   aplicarla tal cual dejaría tres filas iguales en todos los casos de una instalación que no sella. Por eso el panel
   tiene tres modos:
   - `hidden`: el sellado está apagado (`enabled=false`) y el caso no tiene **ningún** documento de sello. Es el caso
     típico de una instalación que no sella: la web queda como hoy, sin bloque.
   - `summary`: el sellado está prendido, el caso no tiene documentos y es del flujo server, anterior a la función
     (`case_mode=null` y generado) o se generó con el sellado apagado. Una sola línea informativa con el texto de la
     tabla, por ejemplo "Sin sello de tiempo (caso anterior a esta función)".
   - `rows`: hay algún documento, o es un borrador prendido con evidencia. Ahí van las filas, con la prioridad exacta
     de §9.4.

   Los `pending` con el sellado apagado salen como "Sin sello (sellado desactivado; no se reintenta)", en tono
   neutral, y no disparan polling (`hasPending` exige `enabled`). En la bandeja, `timestampingEnabled !== true`
   → no se dibuja ningún reloj ni aparece el aviso. En la generación, sin `timestamping_enabled` → la etapa de siempre.
2. **Borrador con el sellado apagado y una evidencia sin documento.** Se muestra "Sin sello (sellado desactivado)" en
   lugar de "registrado antes de activar el sellado", que sería falso en ese caso. Si el ZIP o el informe de un caso
   generado no tienen documento con el sellado activo (no debería pasar), dice "Sin sello de tiempo" sin inventar un
   estado.
3. **"Pendiente … (intento N)".** Si `attempts = 0`, la línea es "Pendiente: se reintenta solo", para no mostrar
   "intento 0".
4. **`syncCaseSeals` deduplicado por caso.** Hay hasta dos disparadores casi simultáneos: el `syncCaseSeals` después
   del commit (page, `ResultStep` y `AgentZipBlock`, como pide el checklist) y la auto-sincronización del panel al ver
   el ZIP `final`. Tatana usa `LockFor` y respondería `409 zip_in_progress`, y el panel lo mostraría como error. Por
   eso una llamada mientras otra está en curso para el mismo caso devuelve la misma promesa. Nunca lanza.
5. **"Tatana desactualizado" contra "Tatana caído".** `supportsCaseSeals()` da `false` también sin red. Antes de
   devolver `outdated_agent`, se confirma con `agent.health()`; si falla, el resultado es `error` y se ve "No se
   pudieron copiar los sellos; reintentá o descargalos desde acá.", no "actualizá Tatana".
6. **Auto-sincronización del panel.** Corre una vez al montar, como dice la SDD, y otra cada vez que aumenta la
   cantidad de sellos `granted` (lo detecta el polling). Así un sello que llega tarde, por ejemplo el del ZIP en el
   escenario 9 de §13.2, queda en `sellos/` sin tocar el botón. La operación es idempotente en Tatana.
7. **Polling también en el panel.** La SDD lo pide en los pasos 3 y 5 (page). El panel del resultado y del detalle
   también refresca cada 30 s mientras haya pendientes y el sellado esté activo. Lo necesita el escenario 9 de §13.2
   ("cuando el sello llega, la web dice…"). Ningún polling consulta con la pestaña oculta
   (`document.visibilityState`), y todos se cortan al desmontar o cuando no quedan pendientes.
8. **Aviso único.** Se dispara si una `EvidenceResponse` trae un `timestamp` `pending` cuyo `id` no estaba en el mapa
   anterior: es un archivo recién registrado. Un re-registro idéntico devuelve el mismo `id` y no lo dispara. Se
   muestra una sola vez por caso en la sesión (`timestampNoticeFor`). Usa `FxBanner` informativo y descartable, como
   `uploadNotice`.
9. **Detalle del caso: panel embebido.** "Dentro de la sección del paquete" se resolvió con la variante `embedded`:
   sin caja propia, con una línea divisoria y `h4` (la sección del paquete usa `h3`). En el flujo agent va dentro de
   `AgentZipBlock`, para reusar su `zipState` (la copia solo se ofrece con el ZIP `final` y `sameHost`).
10. **Indicador de la miniatura.** Se ubica a la derecha del estado de envío (arriba a la izquierda), porque el resto
    de las esquinas ya está ocupado (borrar, seleccionado, aviso y rol). Tiene `title` propio y no lleva
    `pointer-events-none`, para que el tooltip aparezca al pasar el mouse. Para el lector de pantalla, el texto va en
    el `aria-label` del ítem y el ícono queda `aria-hidden`.
11. **Disclosure de la evidencia.** Es un `<button aria-expanded aria-controls>` más una lista con `hidden`, en lugar
    de `<details>`, que no se usaba en la app. Así tiene el `FOCUS_RING` del sistema y funciona con teclado (Enter y
    Espacio nativos).

## Skills invocadas (constancia)

- **`ui-ux-pro-max`**: invocada antes del JSX final. Consultas `--domain ux` sobre estado sin depender solo del color
  y sobre disclosure con teclado. Lo que se aplicó:
  - cada estado lleva un ícono distinto (`ClockCheck` / `Clock` / `ClockAlert` / `Info`) + texto, y además un
    prefijo `sr-only` ("Sellado." / "Pendiente." / "Advertencia.");
  - los colores salen de los tokens `fx-*` (sin hex ni modificador `/xx`, que no anda con los tokens `var()`);
  - la confirmación de la copia va en una región `role="status"` / `aria-live="polite"`;
  - el botón de copia muestra su estado de carga ("Copiando sellos…") y los objetivos táctiles tienen ≥ 44 px
    (`min-h-11`).
- **`senior-frontend`**: invocada. Lo que se aplicó:
  - cargas con `AbortController` y cleanup en cada effect;
  - polling con `setInterval` que se corta al desmontar o sin pendientes, y no consulta con la pestaña oculta;
  - callbacks estables (`useCallback` + refs) para no re-registrar `bindCase`;
  - la deduplicación de `syncCaseSeals`.
- **`3d-web-experience`**: invocada solo como criterio. No se agregó 3D ni efectos pseudo-3D. La única animación nueva
  es la rotación del chevron del desplegable, que tiene propósito (indica abierto/cerrado), es solo `transform` y va
  con `motion-safe:`. Revisado: sin hallazgos aplicables.
- **`web-design-guidelines`**: autochequeo final sobre `TimestampPanel`, `EvidenceTray`, `CaptureStep`,
  `GenerateStep`, `CaseTimeline`, `ResultStep` y `CaseDetailContent`, con las reglas de Vercel bajadas en el momento.
  Resultado:
  - íconos decorativos con `aria-hidden`;
  - foco visible (`FOCUS_RING`) en el botón del desplegable;
  - descarga como `<a download>` con hover;
  - jerarquía de headings correcta (`h3` en el resultado, `h4` embebido);
  - "…" tipográfico;
  - textos largos con `break-words` / `break-all` y `min-w-0`;
  - fechas con `Intl` (`toLocaleString("es-AR")`) o `gen_time_display` del backend;
  - `translate="no"` en nombres de archivo y hashes;
  - sin `transition: all` y respetando `prefers-reduced-motion`.

  Sin hallazgos bloqueantes. Queda como observación menor que la línea "Sellado por {autoridad} …" no lleva
  `translate="no"` en el nombre de la autoridad ni en el N.º de serie, porque se arma como una sola frase.
- **`ui-styling`**: no se invocó. No se agregaron componentes shadcn ni cambió la configuración de Tailwind: solo se
  usan clases y tokens existentes.
- **`mblode-agent-skills-ui-animation`**: no se invocó. No hay transiciones ni gestos nuevos más allá de la rotación
  del chevron, que es un `transform` de 180° en `duration-fx-fast`, con el patrón que ya usa la app.
