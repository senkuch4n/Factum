# Implementación frontend: ios-herramientas-windows (#7)

**Estado:** done (sin commit, como pidió el orquestador)
**Rama:** `feat/ios-herramientas-windows`
**Alcance:** checklist §11.4 (F1–F9) de `Refactorizaciones/ios-herramientas-windows.md`. Solo `client/`. No se tocaron `agent-ui/`, `server/` ni `progress/sesiones/`.

## Archivos tocados

| Archivo | Ítem | Qué |
|---|---|---|
| `client/src/lib/agent.ts` | F1 | Tipos `AgentToolStatus`, `AppleServiceState`, `AirplayUnavailableReason`, `AgentIosStatus`, `IosDeveloperModeStatus`. `AgentHealth` suma `ios_available?`, `tools?`, `ios?`. `AgentErrorCode` suma los 12 códigos de §4.1. **D7:** `takeScreenshot`, `startAirplayShot`, `markAirplayShot` y `stopAirplayShot` usan el patrón de `startRecording` (`AGENT_UNREACHABLE` si no llega el fetch, `readAgentError(res, <fallback de hoy>)` si no es 2xx). Nueva `enableIosDeveloperMode(serial)` → `POST /devices/{encodeURIComponent(serial)}/ios/developer-mode`. |
| `client/src/lib/agent-messages.ts` | F2 | `IOS_APPLE_SERVICE_MISSING_TITLE`, `IOS_APPLE_SERVICE_MISSING_MESSAGE`, `AIRPLAY_UNAVAILABLE_BADGE`, `airplayUnavailableText(reason)` e `iosDeveloperModeMessage(status, deviceName)`, con los textos exactos de §9.2. |
| `client/src/hooks/useAgentIosStatus.ts` (nuevo) | F3 | `useAgentIosStatus(enabled = true)` → `{ appleService, airplayAvailable, airplayReason, supportsDeveloperMode }`. Llama `agent.health()` al montar y cada 10 s si `enabled` y la pestaña está visible; limpia el intervalo al desmontar. Si falla o Tatana es viejo (sin `ios`): `null` / `true` / `null`. |
| `client/src/components/DeviceConnect.tsx` | F4 | Prop `appleServiceMissing`. Si `agentOnline && appleServiceMissing`: `FxBanner tone="warn" role="status"` con título y mensaje, y "Ver la guía" si hay `onOpenGuide`. Va debajo del banner de Tatana caído y se muestra aunque haya Androids. |
| `client/src/components/IOSModePicker.tsx` | F5 | Prop `airplayUnavailableReason`. Con motivo, la tarjeta `airplay` queda `disabled` + `aria-disabled`, sin `fx-card-interactive`, con `cursor-not-allowed`, `Tag secondary` "No disponible en esta PC" en vez de "30 FPS" y `desc` = `airplayUnavailableText(reason)`, enlazado con `aria-describedby` (`useId`). Las otras tres tarjetas no cambian. |
| `client/src/components/CaptureStep.tsx` | F6 | Prop `airplayUnavailableReason` → `IOSModePicker`. "Espejar para capturas" queda `disabled` con `<p class="text-xs text-fx-text-3">` del motivo debajo, enlazado con `aria-describedby`. |
| `client/src/app/dashboard/page.tsx` | F7 | `useAgentIosStatus(agentOnline)`. Pasa `appleServiceMissing={appleService === "missing"}` a `DeviceConnect` y `airplayUnavailableReason = airplayAvailable ? null : airplayReason ?? "uxplay_not_found"` a `CaptureStep`. En `onSelectIosMode`, si llega `airplay` sin AirPlay, no se llama a Tatana. `handleScreenshot` ya mostraba `e.message`; con D7 ese mensaje es el motivo real. |
| `client/src/components/usb-guide/types.ts` | F8 | `IOSStep` suma la variante `devmode` y pierde `terminal`. `TerminalBlock` sigue en `Mockups.tsx`. |
| `client/src/components/usb-guide/data.ts` | F8 | El paso de Terminal de la Mac pasa a `devmode` con el texto de §9.5. El paso siguiente cambia su `detail`. Se quitó el import de `Terminal`. |
| `client/src/components/usb-guide/IOSGuide.tsx` | F8 | Muestra `<DeveloperModeAction />` para `type === "devmode"`. |
| `client/src/components/usb-guide/DeveloperModeAction.tsx` (nuevo) | F8 | Botón "Activar Modo Desarrollador" (Prime `Button`, `loading`). Hace `listDevices()`, toma el primer `ios` y llama `enableIosDeveloperMode`. Muestra `iosDeveloperModeMessage(status, name \|\| "el iPhone")` en un `FxBanner` (`success`, o `info` si es `manual_required`); el error va con `e.message` (tono `error`, `role="alert"`). Sin iPhone muestra el texto inline de §9.5. Sin `ios_developer_mode_v1` muestra "Actualizá Tatana para activar el Modo Desarrollador desde Factum.", sin botón. |

## Verificación

```
$ cd client && npx tsc --noEmit
(sin salida) EXIT 0
```

- ESLint: no lo corrí porque `client/` no tiene `eslint.config.*` (ESLint 10 aborta sin configuración).
- No hay tests en `client/` y la SDD no pide ninguno.
- **Revisión visual pendiente (§12.1):** no abrí el navegador contra Tatana en mock ni simulé un `/health` sin AirPlay; no maté ni toqué los procesos del usuario. Para hacerla: en DevTools, sobrescribir la respuesta de `GET localhost:8765/health` con `"ios": {"apple_service":"missing","airplay_available":false,"airplay_unavailable_reason":"not_supported_on_windows"}` y `"capabilities":[…,"ios_developer_mode_v1"]`. Con eso se ven el banner en el paso 1, la tarjeta AirPlay deshabilitada y "Espejar para capturas" deshabilitado con su texto en el paso 3, y el botón en la guía de iPhone.

## Contrato compartido (§5.1): coincide

`tools`, `tools.<k>.found/source/path/version`, `tools.python.pymobiledevice3_version`, `tools.uxplay`, `ios_available`, `ios.apple_service` (`"ok"|"missing"|"unknown"`), `ios.airplay_available`, `ios.airplay_unavailable_reason` (`"not_supported_on_windows"|"uxplay_not_found"`, opcional), `capabilities` con `"ios_developer_mode_v1"`, errores `{ error, code }` con los 12 códigos y `POST /devices/{serial}/ios/developer-mode` → `{ status: "enabled"|"restarting"|"manual_required" }`. Todos los nombres están en snake_case, tal cual figuran en la SDD. No faltó ningún campo y no inventé ninguno.

Compatibilidad con un Tatana viejo: sin `ios`, AirPlay queda habilitado y no aparece el banner. Sin la capacidad, la guía muestra el texto de actualizar en lugar del botón y no muestra ningún comando.

## Decisiones no obvias

1. **Tarjeta deshabilitada** (por `ui-ux-pro-max`, "Disabled States" con opacidad y `cursor-not-allowed`): bajé la opacidad solo de la ilustración y atenué título y subtítulo a `text-fx-text-3`. El motivo queda en `text-fx-text-2`, sin opacidad, para que mantenga el contraste: es el texto que el perito tiene que leer. Poner `opacity-50` a toda la tarjeta habría dejado el motivo por debajo de 4.5:1.
2. **Accesibilidad del deshabilitado:** la SDD pide `disabled` + `aria-disabled` + `aria-describedby`. Un `<button disabled>` sale del orden de tabulación, así que el lector de pantalla lo anuncia en modo exploración y el motivo también está visible como texto. Se respetó la SDD tal cual.
3. **Hook:** además del intervalo de 10 s, revalida apenas la pestaña vuelve a estar visible (`visibilitychange`). Así el banner desaparece enseguida cuando el perito vuelve de instalar "Apple Devices". No re-renderiza si el estado no cambió y evita llamadas superpuestas (`busy`). Con `enabled = false` devuelve el estado "desconocido".
4. **`enableIosDeveloperMode`:** usa `readAgentError` con el fallback "No se pudo activar el Modo Desarrollador del iPhone". La SDD no fija ese texto, que solo se ve si Tatana no manda `error`.
5. **Mientras activa el Modo Desarrollador** (por `web-design-guidelines`, "los estados de carga terminan en …"): la etiqueta del botón pasa a "Activando…" y debajo aparece la línea `aria-live` "Puede tardar hasta un minuto; no desconectes el iPhone.". Esa línea reserva su alto (`min-h-4`) para que no salte el layout.
6. **Texto del paso `devmode`:** dice "sin Mac ni comandos", que es el texto exacto de §9.5, aunque la misma sección agrega "Ningún texto menciona Mac". Respeté el texto literal. Si el reviewer prefiere sacar la palabra, se cambia solo en `data.ts`.
7. "Espejar para capturas" quedó dentro de un `<div>` para poner la línea del motivo justo debajo del botón.

## Skills invocados

- `ui-ux-pro-max`: invocado antes del JSX. Consulté `--domain ux` por "disabled state explanation" y "async button loading feedback". De ahí salen la decisión 1 (opacidad y cursor en el deshabilitado) y el patrón del botón con carga y deshabilitado mientras corre (`DeveloperModeAction`).
- `senior-frontend`: hook con limpieza (intervalo y listener), sin re-render redundante, errores tipados con el patrón existente de `agent.ts` y props opcionales con valores por defecto.
- `3d-web-experience`, como criterio: no agregué 3D, efectos pseudo-3D ni animaciones nuevas. Solo se reutiliza el fade-in que `FxBanner` ya tiene, con `motion-safe`. Sin hallazgos aplicables.
- `web-design-guidelines`: autochequeo final sobre `IOSModePicker`, `CaptureStep`, `DeviceConnect`, `DeveloperModeAction` e `IOSGuide`. El único hallazgo, la etiqueta de carga sin "…", está corregido (decisión 5). Todo lo demás cumple: íconos con `aria-hidden`, avisos con live region (`FxBanner` / `aria-live`), botones nativos y foco de Prime/`fx-card`.
- `ui-styling` y `mblode-agent-skills-ui-animation`: no los invoqué porque no corresponden. Solo se usaron clases Tailwind y tokens `fx-*` que ya existen, y no hay transiciones ni gestos nuevos.

## Ajuste post-review

Tras la aprobación, el reviewer resolvió la decisión 6: manda la regla "ningún texto menciona Mac".
- El `detail` del paso `devmode` (`client/src/components/usb-guide/data.ts:67`) ahora termina en "…tocá el botón. Tatana habilita el Modo Desarrollador por vos.".
- Saqué "sin Mac" de los comentarios de `types.ts:28`, `DeveloperModeAction.tsx:18` y `agent.ts:390`.
- `npx tsc --noEmit`: EXIT 0.
