# Review — pulido-post-qa-final (HU #73)

**Veredicto:** APROBADA

Verificado por el reviewer (no por los progress):
- `dotnet build` Factum.Agent: 0 warnings, 0 errores. `dotnet build` Factum.Backend: 0 errores, 4 warnings NU190x preexistentes (SharpCompress/Snappier).
- `dotnet test server/tests/Factum.Agent.Tests`: 564 pasan, 0 fallan.
- `npx tsc --noEmit` en `client/`: limpio.
- Builds con `-o` a scratchpad (hay procesos `dotnet run`).

## Checkpoints
- C1 (arnés): [x] rama `feat/pulido-post-qa-final`; la Fase y `verify.sh` son del orquestador (no se tocó el Project).
- C2 documentos: [x] HU con validación del usuario, SDD con checklist y §6 Contrato compartido.
- C2 contrato: [x] coincide exacto en ambos lados.
  - `skip_screen_check`: query en `ScreenshotController.Take` y `client/src/lib/agent.ts` (`takeScreenshot`); body en `StartRecordingRequest.SkipScreenCheck` (snake_case por la política) y `startRecording`.
  - `code` `screen_off` / `screen_locked`: `AgentErrorCodes.cs` y `AgentErrorCode` del client.
  - `screen_state` `off|locked`: `Extra` de `AgentHttpException` y `AgentErrorBody.screen_state`.
  - `screen_check_v1`: `HealthController.Capabilities`; el client no lo lee (D-T5).
  - El body 409 lo arma `ScreenBlock()` con el texto de §6.2.
- C3 alcance: [x] solo `client/`, `server/` (Agent, Backend, tests) y `docs/despliegue-nube.md`. `agent-ui/` y sesión (#74) intactos. M1 sin tocar (`VideoCard`, `FilesController`), correcto por D-T9.
- C3 `client/AGENTS.md`: [x] sin APIs de Next que cambien; son componentes y hooks cliente.
- C3 agent-ui: [x] n/a.
- C3 Mongo: [x] no se escribe nada ni hay campos nuevos.
- C3 logs/sin debug: [x] ver "Seguridad".
- C4 builds y tsc: [x] ver arriba.
- C4 tests: [x] `AndroidScreenProbeTests.cs` (33 casos): `Parse` con el fixture real del SM-M127F, mock sin llamar a adb, comando de adb armado, timeout/adb_error/no_signals y cuerpo del 409.
- C4 salida real: [x] la generación de informe y la prueba con el Samsung quedan como prueba manual del usuario (SDD §11.2.7-8); el fixture de pantalla encendida/bloqueada real queda pendiente (D-T17).
- C5 progress: [x] ambos existen y describen archivos, verificación y datos.
- C5 constancia de skills: [x] `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` (sin hallazgos aplicables) y `web-design-guidelines` documentadas en `impl_frontend` §5.
- C5 temporales/datos: [x] el frontend declara haber borrado solo por `_id` lo que creó; no se ven archivos temporales en `git status`.

## Puntos que pidió el orquestador
- **Estado de pantalla fuera de la custodia:** [x] no hay `reportAgentEvent("capture_failed")` en el camino `screenBlockOf` (`page.tsx` `handleScreenshot` retorna antes; `reportRecordOutcome` ignora `screen_blocked`). El backend no recibe nada y Tatana no emite `screenshot_taken` ni `recording_started` ni crea archivo. La sonda corre antes de `NewFilePath`, `StartRecordingAsync` y `SetOwner`.
- **Sin cobertura en iOS:** [x] `platform != "ios"` en screenshot; rama solo Android en `record/start`. La vista previa, la cámara web y AirPlay no pasan por esos endpoints.
- **Seguridad de logs:** [x] `AndroidScreenProbe` loguea solo `Reason` o el estado (sin serial ni salida cruda). La línea "Informe generado" lleva id del caso y 8 caracteres de cada hash; sin contraseña, expediente, nombres ni rutas, en los dos flujos.
- **Docs del VPS:** [x] solo marcadores `<IP>`, `<id>`; sin credenciales. La contraseña de Mongo sale de `process.env` dentro del contenedor. Todas las consultas llevan proyección, que excluye `ZipPassword`, `PendingGeneration.Password` y `PasswordHash`. Se advierte expresamente contra `find()` sin proyección, `inspect`, `env` y `printenv`, y que `factum_app` puede escribir.
- **Corte de M2 en `md` (D-T11):** [x] medido por el frontend a 360-1440 px, idéntico a hoy desde 1024. `FxTip` + `aria-label` + `aria-current`; centrado seguro con `mx-auto`.
- **M1 "no reproduce":** [x] medición documentada (máx. 0,52 s, contra el umbral de 3 s), así que es correcto que falten §8.1 y §9.4. Queda anotada la confirmación física con el Samsung para la prueba manual.
- **404 de `<img>` en `/files/screenshot_…`:** no es bloqueante.
  - La causa es la carrera *move* (la SDD §3 ya la describe), preexistente; el `fetch` de `isMostlyBlack` que sí era de esta HU quedó corregido (B3).
  - El `<img>` residual se recupera solo cuando pasa a `storedInCase` (`SafeImg` repide con `caseFileURL`), así que la línea de consola es cosmética.
  - La SDD §8.5 pedía `onError` solo "si queda alguno", y el frontend justificó por qué sería redundante. Aceptable. Si molesta, va a una HU aparte.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `RecordingController.Start`: con grabación en curso y pantalla apagada responde `screen_off` en vez de `already_recording` (documentado en impl_backend; es lo que pide §9.2, y el client distingue por `code`).
- Las pestañas de ícono miden 36 px, por debajo del objetivo táctil de 44 px; lo fija la SDD (§8.2) y el frontend lo anotó.
- Antes de publicar Tatana 1.5.0, recordar sumar los fixtures reales de pantalla encendida y bloqueada (D-T17) y que web y Tatana salgan juntas (§12 de la SDD). No hace falta subir `FACTUM_TATANA_VERSION_MINIMA`.
- El tile "Pantalla" pierde el foco mientras captura (`disabled`); preexistente, fuera de alcance.
