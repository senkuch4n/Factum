# impl_frontend — tatana-linux (HU #91)

**Estado:** done · **Rama:** `feat/tatana-linux` (sin commit ni push, como se pidió) · **Fecha:** 2026-10-18
**SDD:** `Refactorizaciones/tatana-linux.md` §8, §10, §14.2 (F1–F18) · DU1 = a, DU2 = a.
**Apps:** `agent-ui/` (F1–F14) y `client/` (F15–F17). No se tocó `server/`, `deploy/`, `packaging/`, `ops/` ni `.github/`.

## Archivos tocados

### agent-ui
| Archivo | Qué |
|---|---|
| `package.json` / `package-lock.json` | F1: `electron-builder` `26.15.3` exacto (`npm install`), sin `postinstall`; `build.toolsets.appimage = "1.0.3"`; `build.linux` = `[{ target: AppImage, arch: [x64] }]`, `icon`, `category: Utility`, `executableName: tatana`, `synopsis`; `build.appImage.artifactName = "Tatana-${version}-x86_64.${ext}"` (sin `executableArgs`, D-T3); `extraResources` suma `resources/icon.png` → `<resources>/icon.png` (para el ícono del menú, §8.4); `test` = `tsx --test src/main/updater/*.test.ts src/main/linux/*.test.ts` |
| `scripts/parchar-electron-builder.mjs` | **Borrado** (DU1 = a) |
| `src/main/linux/paths.ts` (+ test) | F2: `canonicalAppImagePath`, `dataDir`, `desktopEntryPath`, `autostartPath`, `iconPath`, `configHome`, `dataHome`, `runtimeDir`, `pulseSocketPath`. Puro, `posix`, XDG vacío/relativo se ignora |
| `src/main/linux/requirements.ts` (+ test) | F3: `glibcTooOld(version, min='2.35')`, `requirementsMessage`, `glibcRuntimeVersion` (`process.report…glibcVersionRuntime`), `checkLinuxRequirements({dialog, app})` (texto exacto de D-T2) |
| `src/main/linux/desktop-entry.ts` (+ test) | F4: `desktopEntryText({exec, version, hidden})` puro (§8.4) con el quoting de la especificación Desktop Entry |
| `src/main/linux/desktop-integration.ts` | F4: `installOnPc`, `refreshDesktopEntries`, `applyAutostart`, `installOffer` (`install`/`replace`/`open`/`none`), `openInstalled`, `isInstalled`, `runningAppImage`, `instalacion-linux.json` (§10.F) |
| `src/main/linux/replace-file.ts` | Copia atómica `<dest>.nuevo` → `chmod 755` → `fsync` → `rename` (+ fsync de la carpeta). La usan la instalación (D-T6) y la actualización (D-T7) |
| `src/main/linux/process-sweep.ts` (+ test) | F5: `selectOrphans(entries, binDir, selfPid, uid)` puro + `listProcesses` + `sweep(binDir)` (SIGTERM, 2 s, SIGKILL) |
| `src/main/linux/prepare-pc-protocol.ts` (+ test) | F6 (parte pura): tipos de §10.B/§10.F/§10.G, `parsePrepareLine`, `resultadoFromExit`, `finalResultado`, `mergePaso`, `buildPreparacion`, `parsePreparacion`, `fallbackCommandText`, `shellQuote` |
| `src/main/linux/prepare-pc.ts` | F6: `import preparePcScript from '../../../../packaging/linux/preparar-pc.sh?raw'` (ruta exacta de §10.B), `scriptSha256`, `simulate()` (`bash -s -- --simular`), `execute(onStep)` (`pkexec /bin/bash -s -- --ejecutar --usuario <login> --version <v>`, stdin = script), `fallbackCommand()` (`~/.config/Tatana/preparar-pc.sh` 0644), `preparacion-linux.json`, `logs/preparacion-linux.log` |
| `src/main/linux/tray-support.ts` (+ test) | F7: `hasStatusNotifierWatcher()` (`gdbus … NameHasOwner org.kde.StatusNotifierWatcher`, timeout 2 s, sin gdbus = hay bandeja) + `parseNameHasOwner` puro |
| `src/main/linux/env.d.ts` | `declare module '*.sh?raw'` |
| `src/main/agent-process.ts` | F5: `agentDataDir()` en Linux empaquetado → `paths.dataDir()`; `spawn` con `detached` en Linux; `stopAndWait` Linux = D-T17 (SIGTERM + 10 s, `adb kill-server`, `kill(-pid, SIGKILL)`, barrido); `stop()` en Linux delega en `stopAndWait`; `restart()` nuevo |
| `src/main/config.ts` | F8: `applyLoginItem` en Linux → `applyAutostart`; campo `linuxInstallOffered?` |
| `src/main/index.ts` | F7: chequeo D-T2 al principio de `ready` (solo Linux empaquetado); bandeja → cerrar `hide()` con bandeja / `minimize()` sin ella; `refreshDesktopEntries` desde la canónica; diálogo de instalación la primera vez; abre "Esta PC" la primera vez sin `preparacion-linux.json` o con `setup_state = "incomplete"`; con `--hidden`, una sola `Notification`; `will-quit` en Linux espera `stopAndWait` y sale con `app.exit(0)`; "Reiniciar agente .NET" usa `restart()` |
| `src/main/updater/manifest.ts` (+ `manifest.test.ts`) | F9: `UpdateProduct`, `expectedProduct(platform)`, `manifestFileName`, `expectedFile`; `validatePayload(raw, expected)`; `verifyEnvelope(env, keys, product = expectedProduct())` |
| `src/main/updater/appimage-install.ts` (+ test) | F10: `matchesManifest` (size + sha512) e `installAppImage` (D-T7); sin Electron |
| `src/main/updater/update-manager.ts` | F10: manifiesto `url + manifestFileName(expectedProduct())`; motivos `sin_appimage` / `no_instalado`; descarga elegida por `.AppImage` en Linux; `pending` conserva archivo + payload; `installNow` Linux → `installLinux` (stopAndWait → `installAppImage` → `app.relaunch({execPath: canónica})` si corresponde → `app.exit(0)`). El bloque `win32` quedó igual |
| `src/main/updater/state.ts` + `src/renderer/src/env.d.ts` | `UpdateReason` + `no_instalado`, `sin_appimage` |
| `src/main/agent-api.ts` | F11: `getHealth(port)` → `AgentHealth { version?, os?, linux? }`, `AgentOs`, `AgentLinuxStatus`, `AgentLinuxCheck`, `AgentLinuxCheckId`, `AgentLinuxCheckStatus` (snake_case de §10.A) |
| `src/main/app-info.ts` | F11: `TatanaAppInfo.platform`, `packaged`, `linux?: { installed, appImagePath?, canonicalPath, trayAvailable }`; `setTrayAvailable()` |
| `src/main/ipc.ts` | F12: canales de §10.G (`tatana:get-linux-status`, `tatana:prepare-pc`, `tatana:prepare-pc-step`, `tatana:simulate-prepare-pc`, `tatana:install-on-pc`, `tatana:open-prepare-log`); `runInstallOnPc()` exportada; Librerías no hacen nada en Linux empaquetado (D-T19); `restart-agent` usa `restart()` |
| `src/preload/index.ts` | F12: `getLinuxStatus`, `preparePc`, `simulatePreparePc`, `installOnPc`, `openPrepareLog`, `onPrepareStep`, `onOpenView` |
| `src/renderer/src/components/LinuxPc.tsx` (nuevo) | F13: vista "Esta PC" |
| `src/renderer/src/App.tsx`, `Sidebar.tsx` | F13: vista `linux` (ítem "Esta PC" solo en Linux), "Librerías" oculta en Linux empaquetado, `onOpenView` |
| `src/renderer/src/components/UpdateStatus.tsx` | F13: línea para `no_instalado` ("Instalá Tatana en esta PC para recibir actualizaciones" + "Abrí «Esta PC» y tocá «Instalar (recomendado)».") y `sin_appimage` |
| `src/renderer/src/components/Settings.tsx` | F13: en Linux sin instalar, el interruptor de autoarranque queda deshabilitado con la explicación y "Ir a «Esta PC»"; el `Toggle` pasa a `role="switch"` + `aria-checked` + foco visible |

`tsconfig.node.tsbuildinfo` / `tsconfig.web.tsbuildinfo` (versionados) los reescribió `tsc`; no son cambios de código. El orquestador decide si los incluye en el commit.

### client
| Archivo | Qué |
|---|---|
| `src/lib/agent.ts` | F15: `AgentOs`, `AgentHealth.os?`, `AgentHealth.linux?: AgentLinuxStatus`, `AgentLinuxCheck`, `AgentLinuxCheckId`; `AirplayUnavailableReason` + `"uxplay_without_mp4"`, `"avahi_not_running"` |
| `src/lib/agent-messages.ts` | F16: `browserOs()`, `tatanaDownloadUrls()`, `tatanaDownloadUrl(os)` (lo usan `agentMinVersionMessage` y `origin_not_allowed`); `iosAppleServiceMissingMessage(os)` + `IOS_APPLE_SERVICE_MISSING_MESSAGE_LINUX` (texto exacto de F16); `airplayUnavailableText` con los dos motivos nuevos (textos exactos de F16) |
| `src/hooks/useAgentIosStatus.ts` | F17: devuelve `os` |
| `src/hooks/useBrowserOs.ts` (nuevo) | `useSyncExternalStore` con snapshot de servidor `null` (sin desajuste de hidratación) |
| `src/components/DeviceConnect.tsx` | F17: prop `agentOs`; mensaje de servicio de Apple por SO; en "Tatana no está activo", "Descargar Tatana para <SO>" + enlace al otro SO, solo con URL configurada |
| `src/app/dashboard/page.tsx` | Pasa `agentOs` a `DeviceConnect` |
| `next.config.ts`, `Dockerfile` | F17: `NEXT_PUBLIC_TATANA_DOWNLOAD_URL_LINUX` (env + ARG/ENV) |

## Verificación (§15.2)

```
cd agent-ui && npm test
ℹ tests 68 · pass 68 · fail 0   (36 nuevos: linux/*.test.ts + appimage-install.test.ts + 6 de Linux en manifest.test.ts)
npx tsc --noEmit -p tsconfig.web.json    → sin errores
npx tsc --noEmit -p tsconfig.node.json   → sin errores
npx electron-vite build                  → ✓ main / preload / renderer (el script de packaging/linux/preparar-pc.sh quedó embebido: 6 apariciones de TATANA_PLAN en out/main/index.js)
npx electron-builder --version           → 26.15.3
cd client && npx tsc --noEmit            → sin errores
```

- `packaging/linux/preparar-pc.sh` ya existía (B18) cuando se corrió el build: no hizo falta stub. Se confirmó contra el script que imprime `TATANA_PLAN`, `TATANA_PASO {"id","estado","detalle"}` y `TATANA_RESULTADO {"schema","resultado","requiere_reiniciar_sesion","pasos"}` como espera el parser.
- **No se corrió** `electron-builder --linux AppImage` (no hay Docker amd64 ni PC Linux en esta sesión): lo cubre el job `armar` de `tatana-linux.yml`.
- No se instaló nada del sistema, no se corrió el script (ni en `--simular`) y no quedaron procesos ni ventanas abiertas. `next build` no se corrió.

## Contrato (§10) — coincide con la SDD

- **§10.A:** `os`, `linux.{distro_id, distro_version, distro_name, distro_like, glibc_version, package_manager, setup_supported, setup_state, checks[].{id, status, required, detail}}` con esos nombres exactos en `agent-ui/src/main/agent-api.ts`, `agent-ui/src/renderer/src/env.d.ts` y `client/src/lib/agent.ts`. `AirplayUnavailableReason` con los 4 valores.
- **§10.B:** import, `bash -s -- --simular`, `pkexec /bin/bash -s -- --ejecutar --usuario <os.userInfo().username> --version <app.getVersion()>`, fallback `~/.config/Tatana/preparar-pc.sh` (0644) + `sudo bash … --ejecutar --usuario <login>`, ids de pasos, estados, resultados (+ `cancelado`, `sin_polkit`, `error`) y códigos 0/1/2/3/4/5/126/127.
- **§10.C:** `tatana-update-linux.json`, `product: "tatana-linux"`, `file: Tatana-${version}-x86_64.AppImage`, cruzados rechazados (`manifiesto_invalido`).
- **§10.E:** `NEXT_PUBLIC_TATANA_DOWNLOAD_URL_LINUX` en `client/next.config.ts`, `client/Dockerfile` y `client/src/lib/agent-messages.ts`.
- **§10.F:** `preparacion-linux.json` con `schema, fecha, tatana_version, distro ("ubuntu 24.04"), gestor, resultado, requiere_reiniciar_sesion, script_sha256, pasos`; `instalacion-linux.json` con `schema, fecha, version, ruta`.
- **§10.G:** los 6 canales con los payloads de la tabla. **Aditivo** (interno de agent-ui, no cruza con otro lado): `tatana:get-linux-status` suma `offer` (`install`/`replace`/`open`/`none`, para la tarjeta de D-T6) y hay un canal main → renderer `tatana:open-view` para abrir "Esta PC" desde el main.

## Decisiones no obvias

1. **Módulos puros separados de los que usan Electron.** `tsx --test` no puede importar `electron` (se probó: falla el import con nombre). Por eso lo testeable va en archivos sin Electron (`paths`, `requirements`, `desktop-entry`, `process-sweep`, `prepare-pc-protocol`, `tray-support`, `replace-file`, `appimage-install`) y lo que usa Electron en otros (`desktop-integration`, `prepare-pc`). `parsePrepareLine` vive en `prepare-pc-protocol.ts` (la SDD lo ubicaba en `prepare-pc.ts`, que importa el `?raw` y Electron).
2. **El diálogo de D-T2 corre al principio de `ready`, no antes.** En Linux, `dialog.showErrorBox` antes de `ready` solo escribe en stderr (documentación de Electron). Sigue siendo antes de crear la ventana, como pide D-T2.
3. **Tests de manifiesto con el producto fijo.** `verifyEnvelope` toma el producto de `process.platform` por defecto; como el CI corre en Ubuntu, los tests de siempre ahora pasan `'tatana-windows'` explícito (si no, en Linux habrían fallado).
4. **"Preparar esta PC" simula antes de pedir la contraseña.** Si el plan dice `soportada: false`, devuelve `sin_soporte` con los `comandos` sin abrir `pkexec` (Gherkin "no ejecuta nada a medias" y sin pedir una contraseña inútil). Con 126/127 manda el código de pkexec; si no, el `TATANA_RESULTADO` del script; si no, el código.
5. **`preparacion-linux.json` se escribe siempre**, también con `cancelado`/`sin_polkit` (trazabilidad). La vista "Esta PC" se reabre sola igual, porque `/health.linux.setup_state` sigue en `incomplete`.
6. **Salir en Linux** (`will-quit`): `preventDefault`, `stopAndWait` (D-T17) y `app.exit(0)`, porque `stop()` es síncrono y la app saldría antes del barrido. Instalar en la PC y la actualización también salen con `app.exit(0)` después de cortar el agente (así el `close` que esconde la ventana no frena nada).
7. **`restart()` en Linux espera el corte antes de relanzar**: con el `stop()` + `setTimeout(600)` de siempre, el barrido de `/proc` podía matar a los hijos del agente nuevo (mismo `binDir`). Windows/macOS siguen igual.
8. **Instalar en la PC no se hace con operaciones en curso** (`/agent/state.operations`) y pasa por `confirmSessionShutdown()` (grabación de la sesión), igual que "Reiniciar agente".
9. **Ícono del menú:** `resources/icon.png` no viajaba empaquetado; se sumó a `extraResources` (17 KB) y se escala a 512 con `nativeImage.resize`.
10. **`StartupWMClass=Tatana`** (Electron usa `app.name` = `productName`). La SDD pide confirmarlo con `xprop WM_CLASS` en la QA con hardware (§11).
11. **Web, enlace de descarga:** en un navegador Linux sin `NEXT_PUBLIC_TATANA_DOWNLOAD_URL_LINUX` configurada, `tatanaDownloadUrl()` devuelve `null` (no se le ofrece el `.exe` a una PC Linux; el mensaje cae en "pedile a tu administrador"). En `DeviceConnect` el SO del navegador se lee con `useSyncExternalStore` (snapshot de servidor `null`) para no romper la hidratación.

## Para el orquestador / backend

- **`.github/workflows/tatana-release.yml` L358-362** todavía corre `node scripts/parchar-electron-builder.mjs` en el job del instalador de Windows. El script se borró (DU1 = a): ese paso lo tiene que sacar `implementer-backend` (B22), si no la release de Windows falla.
- QA manual (§11): confirmar `StartupWMClass` con `xprop`, el diálogo de instalación la primera vez, el minimizado sin bandeja (GNOME sin AppIndicator) y el paso 10.1 (Windows actualizado al NSIS de electron-builder 26).

## Skills invocados

- `ui-ux-pro-max`: antes del JSX final. Búsquedas `--domain ux` ("setup wizard progress steps" → indicador "Paso N de M"; "status list icon color alone" → nunca solo color: ícono + texto en cada chequeo y paso; contraste 4.5:1 → se usó `--text-secondary` y no `--text-muted` para el texto); `--stack react` ("live region status announce" → `role="status"`/`aria-live` en el progreso y en "Copiado"). La búsqueda de "copy to clipboard feedback" no devolvió nada aplicable: se aplicó el criterio general (confirmación visible y alternativa de copiar a mano).
- `senior-frontend`: listeners de IPC con limpieza (`removeAllListeners` en el cleanup del efecto), estado derivado en vez de duplicado, sin re-renders de más en el hook de `/health` (comparación de `os`).
- `3d-web-experience`: como criterio. Sin 3D ni efectos pseudo-3D; la única animación nueva es un fundido de opacidad del resultado.
- `ui-styling`: Tailwind + variables CSS existentes (`--bg-surface`, `--btn-secondary-*`, `focus-ring`); en la web, tokens `fx-*` existentes (`text-fx-accent-text`, `outline-fx-focus`).
- `mblode-agent-skills-ui-animation`: fundido solo de `opacity`, 200 ms entrada / 120 ms salida (asimétrico), curva `cubic-bezier(0.22, 1, 0.36, 1)`, `useReducedMotion` → 0 ms; spinners con `motion-reduce:animate-none`.
- `web-design-guidelines` (autochequeo final sobre los archivos tocados): íconos decorativos con `aria-hidden`; foco visible en todos los botones nuevos y en los de la Sidebar (se sumó `focus-ring` + `aria-current`); `aria-describedby` del botón apuntando a la explicación (corregido); interruptor con `role="switch"`; textos de carga con "…"; fechas con `toLocaleString('es-AR')`; detalles largos con `break-words`; rutas y comandos con `translate="no"` y `select-text`; enlaces de descarga con texto explícito ("Descargar Tatana para Linux", no "acá"), `hover` y aviso de pestaña nueva para lectores de pantalla.
