# Review — ios-herramientas-windows (#7)

**Veredicto:** APROBADA

Revisado el diff sin commitear de `feat/ios-herramientas-windows` (basada en `origin/develop`) contra la SDD (Contrato §5, §11, §12, DP1–DP3), la HU (D1–D10 validadas) y `CHECKPOINTS.md`. Fuera de revisión, como pidió el orquestador: `appsettings.json`, `*.tsbuildinfo`, HU #21.

## Verificación corrida por el reviewer
- `dotnet build` Factum.Agent: 0 warnings, 0 errores. Factum.Backend: 0 errores (solo NU1902/NU1903 de paquetes ajenos, preexistentes; la HU no toca Backend).
- `dotnet test server/tests/Factum.Agent.Tests`: 202 superados, 0 omitidos (el test del `DvtRecorder` sintético corre, no se saltea).
- `npx tsc --noEmit` en `client/`: limpio.
- `actionlint` (Docker): exit 0. `shellcheck` sobre `armar-tatana-portable.sh`: exit 0. `bash -n`: ok. `py_compile` de `ios_helper.py`: ok.
- `./ops/harness/verify.sh`: "Arnés OK", exit 0.
- Contrastado `ios_helper.py` con la API real de pymobiledevice3 10.7.4 instalada en la Mac (`UserspaceRsdTunnel(serial=).aopen()/aclose()`, `create_using_usbmux`, `get_developer_mode_status`, `MobileImageMounterService.is_image_mounted`, `auto_mount`, `AmfiService.enable_developer_mode(enable_post_restart)`, `reveal_developer_mode_option_in_ui`, `DeviceHasPasscodeSetError`, `PasswordRequiredError(PairingError)`, `Device.is_usb` como property): todas existen con esa forma.

## Checkpoints
- C1 arnés sano: [x] HU #7 en `en_revision`, rama `feat/ios-herramientas-windows` basada en `develop`; `verify.sh` exit 0.
- C2 cadena de documentos: [x] HU con validación del usuario (D1–D10, DP1–DP3), SDD con checklist y Contrato compartido.
- C2 nombres del contrato: [x] `tools.python.pymobiledevice3_version` (`[property: JsonPropertyName]` en `ToolInventory.cs`, test T5) ↔ `AgentToolStatus.pymobiledevice3_version` (`client/src/lib/agent.ts`); `ios.apple_service|airplay_available|airplay_unavailable_reason` (`HealthController.cs`) ↔ `AgentIosStatus`; `capabilities` con `ios_developer_mode_v1` ↔ `useAgentIosStatus.ts`; `{ error, code }` con los 12 códigos (`AgentErrorCodes.cs`/`IosErrors.cs`) ↔ `AgentErrorCode`; `POST /devices/{serial}/ios/developer-mode` → `{ status }` ↔ `agent.enableIosDeveloperMode`. Todo en snake_case.
- C3 lados correctos: [x] solo `server/` (Tatana), `client/`, `packaging/`, `ci/`, `deploy/windows`, `.github/workflows`, `docs/`. No se tocó `agent-ui/` ni `Factum.Backend`.
- C3 `client/AGENTS.md`: [x] componentes cliente con `"use client"`, hook con cleanup, sin APIs obsoletas.
- C3 `agent-ui/`: [x] no aplica (no tocado).
- C3 Mongo: [x] no aplica; no hay cambios de modelo ni escrituras.
- C3 sin debug/datos sensibles/TODOs: [x] los logs de iPhone llevan código y detalle recortado; el stderr completo va a Debug; no se loguean JWT ni hashes.
- C4 build limpio: [x]
- C4 tsc limpio: [x]
- C4 tests reales: [x] T1–T6 de la SDD existen y prueban comportamiento (textos exactos por SO, sonda con caché y reloj inyectado, helper embebido sin `add_signal_handler`, `CreateStartInfo` con rutas con espacios/acentos, grabación sintética real con stop < 10 s).
- C4 salida real documentada: [x] el portátil se armó en la Mac (209 MB) con los chequeos de contenido y de `Version: 10.7.4`; lo que solo prueba Windows queda al CI y al piloto (ver observaciones).
- C5 progress backend/frontend: [x] ambos existen y describen archivos y verificación.
- C5 skills del frontend: [x] `progress/impl_frontend_...` deja constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` ("sin hallazgos aplicables", sin 3D) y `web-design-guidelines` (con su hallazgo corregido); `ui-styling` y animación declarados como no aplicables.
- C5 sin temporales / datos ajenos: [x] `git status` sin archivos sueltos; no hay cambios en `agent-data/`, `Storage` ni Mongo; el implementador no ejecutó `prepare`/`devmode` contra el iPhone del usuario.

## Puntos pedidos por el orquestador
1. **Rutas multiplataforma / macOS:** OK. `IosHelper.PythonCandidates()` pone primero `ToolResolver.Find(Python)` (PATH → Homebrew, el 10.7.4 de `/opt/homebrew/bin/python3`, A1) y conserva los candidatos de antes; el helper corre en 3.14 y 3.11 (solo stdlib + pymobiledevice3). Todo lo exclusivo de Windows está detrás de `_isWindows`/`OperatingSystem.IsMacOS()`: `DYLD_LIBRARY_PATH` solo macOS, `GracefulStopAsync` conserva `kill -15/-2` fuera de Windows, la sonda usa `/var/run/usbmuxd`. La autoprueba del implementador y mi corrida de tests (incluido el `DvtRecorder` real sobre macOS) pasan.
2. **D5 (DDI y Modo Desarrollador):** OK. `prepare` (`ios_helper.py` `cmd_prepare`) solo lee el estado y monta la DDI (no destructivo; se desmonta al reiniciar) y **no** activa el Modo Desarrollador; si está apagado devuelve `ios_developer_mode_disabled`. La activación (`devmode`, que reinicia el iPhone) solo ocurre por `POST /devices/{serial}/ios/developer-mode`, que dispara un botón explícito en la guía (`DeveloperModeAction.tsx`); con código de bloqueo solo hace visible el interruptor. `EnsurePreparedAsync` se llama únicamente antes de screenshot/grabación DVT (no en listado ni `on_device`) y cachea por UDID.
3. **Inyección:** OK. Todo proceso usa `ArgumentList` (`IosHelper.CreateStartInfo`, `ProcessRunner.RunArgumentListAsync`, `DvtRecorder`); UDID y rutas nunca se interpolan en un string de comando. El UDID viaja como valor de `--udid` (observación 2).
4. **pymobiledevice3 10.7.4 / lock / DP1:** OK. `requirements-win.lock` con 97 paquetes con hash (pymobiledevice3 10.7.4, pywin32 312, av 18.1.0, lzfse 0.4.2, sslpsk-pmd3, colorama, win32_setctime, pyreadline3, pmd-pytcp), `hexdump==3.3` construido desde el sdist con `--require-hashes`, instalado en `Lib/site-packages`; el script falla si el zip no trae `Version: 10.7.4`, `win32security.pyd` y `pywin32.pth`; `TATANA-PYTHON-LOCK.txt` y `THIRD-PARTY-NOTICES.txt` (GPL-3.0-or-later, fuente de v10.7.4) van en el zip.
5. **`/health` y errores (D8):** OK. Nombres idénticos C#/TS; `airplay_unavailable_reason` no viaja si es null; Windows siempre `not_supported_on_windows`; mock devuelve `apple_service: ok`/AirPlay disponible. Los 500 de iOS suman `code` en los cuatro controladores y el nuevo; `record/stop` ya no devuelve 200 con archivo inexistente.
6. **Web (D7/D1):** OK. Las cuatro funciones de `agent.ts` usan `readAgentError`; `DeviceConnect` muestra el banner del servicio de Apple; `IOSModePicker` y "Espejar para capturas" quedan deshabilitados con el motivo, `aria-describedby` y sin llamar a Tatana; Tatana viejo conserva el comportamiento de hoy.
7. **Workflow (DP3):** OK. `workflow_dispatch` + `pull_request` (no `pull_request_target`) con `paths` acotados a Tatana/packaging/script/ci; `permissions: contents: read`; sin `secrets.*` en ningún paso; `persist-credentials: false`; acciones fijadas por SHA; timeouts; artefactos con retención de 3 días. Un PR de fork corre con token de solo lectura y sin secretos.
8. **Versión 1.3.0 (D10):** OK. Sin cambios de código (como pide §7.4); el zip 1.3.0 se arma tras el merge a `main`. `/health.version` sigue "2.0.0" (es la versión del contrato de la API, no la de distribución).
9. **Skills del frontend:** OK, ver C5.
10. **Regla dura de datos:** OK, ver C5.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
1. **Nota del frontend sobre §9.5 ("sin Mac"): resuelta.** La SDD se contradice (el texto fijado dice "sin Mac ni comandos" y la misma sección dice "ningún texto menciona Mac"). Prevalece la regla: la guía también la ve un usuario de Mac, a quien "sin Mac" le suena raro. Cambio sugerido, solo de copy (lo puede aplicar el orquestador sin otra ronda): en `client/src/components/usb-guide/data.ts:67` terminar el `detail` en `...tocá el botón. Tatana habilita el Modo Desarrollador por vos.`; opcional, ajustar los comentarios de `types.ts:28`, `DeveloperModeAction.tsx:18` y `agent.ts:390` que dicen "sin Mac".
2. `server/src/Factum.Agent/Services/IosService.cs` (llamadas a `RunHelperAsync`/`DvtRecorder.StartAsync`) pasa `"--udid", udid` como dos argumentos; un UDID que empiece con `--` lo interpretaría `argparse` como opción. No es inyección de comandos (no hay shell) y el UDID viene de `/devices`, pero `--udid=<udid>` sería más estricto.
3. **Pendiente de verificación en hardware/CI (no se pudo probar aquí):** (a) que `import win32security` funcione vía `pywin32.pth` con el embebido que tiene `import site` (A4); (b) que el túnel en modo usuario ande en Windows sin admin (D3); (c) el montaje de la DDI y `devmode` contra un iPhone real (el implementador, correctamente, no los ejecutó contra el iPhone del usuario); (d) `prueba-humo.ps1` solo está validado en sintaxis. **Según SDD §12.2, antes de dar la HU por cerrada el orquestador debe disparar `tatana-windows.yml` con `workflow_dispatch` sobre la rama y esperar que pasen los dos jobs**, y el usuario hacer la regresión de la Mac de §12.3 (la primera captura montará la DDI hoy desmontada, A7). La HU sigue abierta hasta la prueba con el perito de Windows (DP2).
4. En la Mac el primer `prepare` hace `auto_mount` y necesita internet la primera vez (A8); sin red falla con `ios_ddi_mount_failed` y el texto lo explica, sin caer al burst.
5. `ios_helper.py` clasifica `PermissionError` genérico como `ios_admin_required`; un fallo de permisos al escribir el `--output` en la Mac se vería con ese texto. Caso raro.
