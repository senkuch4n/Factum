# SDD — Instalador de Tatana para Windows y actualización automática desde la nube

**Slug:** `tatana-instalador-autoupdate` · **Issue:** #21 · **HU:** `docs/hu-tatana-instalador-autoupdate.md`
(validada el 2026-10-07: D2 = A sin firma de código, D4 = A publicación a mano, D1/D3/D5–D12 = las recomendadas,
D11 = por ahora solo CI y la prueba con USB queda pendiente).
**Base del análisis:** `feat/ios-herramientas-windows` ≈ `develop` `42fa825`. SDD **adelantada** mientras se hace
la #7 (ver §14, "Concurrencia con #7").

---

## 1. Resumen funcional

Tatana para Factum en la nube deja de ser un zip portátil que se arma en la Mac y se redistribuye a mano. Pasa a ser
un **instalador NSIS de Electron (`agent-ui`)**, per-user y sin admin, que trae lo mismo que el portátil: agente
self-contained, `tools/` y orígenes horneados. Lo arma y lo prueba **GitHub Actions** en un runner Windows y lo
publica **a mano** (tag `tatana-vX.Y.Z` o "Run workflow") como archivos estáticos en el VPS: `/tatana/updates/`
para el canal y `/descargas/tatana/` para la primera instalación. Para subirlos usa una clave SSH separada de la de
deploy, de un usuario sin Docker y con `command=` forzado. Cada versión publicada lleva un **manifiesto firmado con
Ed25519**: la clave privada vive solo en un secreto de GitHub y la pública está horneada en Tatana. Así, un VPS
comprometido no puede instalar software en las PCs de los peritos. Tatana busca actualizaciones al arrancar y
cada 6 h, probando en orden una **lista de URLs horneada** (sobrevive al cambio de dominio). Descarga en silencio,
verifica la firma y el hash, avisa una vez por versión y **solo instala** al reiniciar Tatana, al iniciar sesión o
cuando el perito toca "Reiniciar y actualizar", **nunca con una operación en curso**. El agente expone su
**versión real** (que llega a la auditoría y al informe) y una señal de "operaciones en curso" con modo
mantenimiento atómico. El backend declara una **versión mínima de Tatana** y, si la PC está por debajo, la web no
inicia capturas. El instalador **migra el portátil para la nube** que encuentre: lo detiene, saca su arranque
automático, pasa su `appsettings.Local.json` a `%APPDATA%\Tatana\` y archiva su carpeta. **La evidencia no se toca
nunca:** el instalado usa la misma carpeta de datos que el portátil. El espejo de GitLab del backend se retira.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| **backend (API)** `server/src/Factum.Backend` | **sí** | Retira `TatanaUpdatesController`/`TatanaUpdatesService` (espejo GitLab, U3). Agrega `Tatana:MinVersion` y `tatana_min_version` en `GET /api/config/public` (D6). |
| **backend (Tatana)** `server/src/Factum.Agent` | **sí** | Versión real (D12), `--mode`, `--local-config` (D9), registro de operaciones + modo mantenimiento (D5), `GET /agent/state`, `POST/DELETE /agent/maintenance`, 503 `agent_updating`. |
| **deploy / CI / packaging** (implementer-backend) | **sí** | `.github/workflows/tatana-release.yml` (nuevo), `verificar.yml`, `deploy/cloud/` (Caddyfile, compose, `factum.env.example`, `scripts/preparar-servidor.sh`, `scripts/publicar-tatana-forzado.sh` nuevo), `deploy/windows/armar-tatana-portable.sh`, `packaging/portable/`, `ops/tatana/` (nuevo), `.gitlab-ci.yml` (se borra), docs. |
| **agent-ui** | **sí** | Config NSIS + `build/installer.nsh` + `build/migrar-portable.ps1`, módulo de actualización (`src/main/updater/`), single-instance, bandeja, notificación, línea de estado y avisos en la ventana, tests de `tsx`. |
| **client** | **sí** (chico) | D6: `tatana_min_version`, estado `outdated` con motivo `min_version`, mensaje. Código de error `agent_updating`. |
| **Mongo** | no | Sin cambios de modelo. `agent_events.agent_version` y los datos del host del informe pasan a recibir la versión real; los documentos viejos no se tocan. |

---

## 3. Vista general

```
 tag tatana-vX.Y.Z / Run workflow (D4)
        │
        ▼  .github/workflows/tatana-release.yml
 ┌─────────────┐   ┌──────────────────┐   ┌───────────┐   ┌──────────────────┐   ┌──────────────┐
 │ preparar    │──▶│ payload (ubuntu) │──▶│ instalador│──▶│ firmar (ubuntu,  │──▶│ prueba       │
 │ (ubuntu)    │   │ armar-tatana-    │   │ (windows) │   │ env tatana-      │   │ (windows,D11)│
 │ versión,    │   │ portable.sh      │   │ NSIS vX   │   │ release) Ed25519 │   │ instala 0.0.1│
 │ rama, keys  │   │ + agente 0.0.1   │   │ + 0.0.1   │   │ tatana-update.json│  │ → actualiza  │
 └─────────────┘   └──────────────────┘   └───────────┘   └──────────────────┘   │ a vX, datos  │
                                                                                  └──────┬───────┘
                                                        solo si la prueba pasa           ▼
                                   ┌───────────────────────────────────────────────────────────┐
                                   │ publicar (ubuntu, env tatana-release)                      │
                                   │ ssh tatana-pub@VPS "publicar X.Y.Z" < bundle.tar           │
                                   │ (command= forzado → /srv/factum/bin/publicar-tatana-…sh)   │
                                   │ + GitHub Release (archivo histórico, repo privado)         │
                                   └───────────────────────────────────────────────────────────┘
 VPS (Caddy, estático):
   https://<dominio>/tatana/updates/   latest.yml, tatana-update.json, Tatana-Setup-X.Y.Z.exe(.blockmap)
   https://<dominio>/descargas/tatana/ Tatana-Setup-Windows.exe (+ .sha256)

 PC del perito (Tatana.exe = agent-ui):
   al arrancar (+10 s) y cada 6 h → para cada URL de tatana-dist.json.update_urls (D8):
     GET tatana-update.json → verificar firma Ed25519 (clave horneada) → ¿versión > instalada?
       → electron-updater (generic, misma URL) checkForUpdates → updateInfo == manifiesto firmado
       → downloadUpdate (sha512 lo verifica electron-updater) → re-verificar sha512+tamaño → "lista"
   instalar (Reiniciar y actualizar / Salir / próximo arranque):
     POST /agent/maintenance → 409 si hay operaciones (no instala) | 200 → detener agente + adb + tools
     → quitAndInstall(silencioso)
```

---

## 4. Decisiones técnicas

Las marcadas con **(usuario)** están en §17, "Decisiones pendientes". El resto son decisiones técnicas que
implementan lo validado.

- **D-T1. Una sola fuente de versión.** La versión sale del tag `tatana-vX.Y.Z` o del input del workflow.
  Recorre esta cadena: `npm version X.Y.Z` en `agent-ui/package.json` (solo en CI) → `__TATANA_VERSION__` y
  `app.getVersion()` → `latest.yml` (electron-builder) → `dotnet publish -p:Version=X.Y.Z` del agente →
  `AssemblyInformationalVersion` → `/health.version` y `/info.version`. El `.csproj` lleva
  `<Version>0.0.0-dev</Version>` (lo que ve un build local) e
  `<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>` (si no,
  .NET agrega `+<sha>`). Formato aceptado en una release: **`^\d+\.\d+\.\d+$`** (sin prerelease).
- **D-T2. Capability `real_version_v1`.** Los Tatana anteriores a esta HU dicen siempre `"2.0.0"`, que es
  **mayor** que cualquier versión real 1.x. La web solo confía en `version` si `capabilities` incluye
  `real_version_v1`. Sin esa capability, la versión se trata como "desconocida, anterior al mínimo". En la guía
  queda dicho que `agent_version = "2.0.0"` en `agent_events` significa "Tatana anterior a esta HU".
- **D-T3. Datos del agente instalado en `%LOCALAPPDATA%\Tatana\data`**, la **misma** carpeta que el portátil (y no
  `%APPDATA%\Tatana\agent-data`, que hoy usa `agent-process.ts`). Así se cumple D10 ("usa esa carpeta si ya tiene
  casos") sin migrar ni copiar evidencia. Además, `%APPDATA%` es *roaming*: en una PC de dominio, la evidencia se
  sincronizaría con el perfil. En macOS y Linux (dev) sigue siendo `<userData>/agent-data`. Hay un override
  avanzado sin UI: `agentDataDir` en `tatana.json`. `C:\Factum\Evidencia` no cambia.
- **D-T4. Configuración en `%APPDATA%\Tatana\`** (D9): `tatana.json` (UI), `appsettings.Local.json` (agente),
  `update-state.json`, `logs\tatana-updater.log` y `migracion-portable.json`. Electron fija
  `app.setPath('userData', join(app.getPath('appData'), 'Tatana'))` **antes de `ready`**, así la ruta no depende de
  `name`/`productName` del `package.json`. El agente recibe `--local-config <ruta>`.
- **D-T5. Carpeta de instalación:** la de electron-builder per-user, `%LOCALAPPDATA%\Programs\Tatana`, que es la
  **misma** que la del portátil. Por eso, antes de copiar nada, el instalador (`customInit`) detecta qué hay ahí:
  - **portátil para la nube** (`tatana-portable.ini` con `CLIENT_URL=https://…`): lo migra (§6.3);
  - **portátil de la instalación local** (`CLIENT_URL` vacío o `http://localhost…`): **aborta** con un mensaje.
    La instalación local no se toca (D10) y dos agentes pelearían por el 8765;
  - **contenido desconocido**: aborta sin tocar nada;
  - **Tatana ya instalado** (existe `Uninstall Tatana.exe`): sigue, es una actualización.
- **D-T6. Integridad (D7):** sobre electron-updater (que ya verifica el sha512 de `latest.yml`) se agrega un
  **sobre firmado** `tatana-update.json` (§7). Electron solo descarga si el `updateInfo` que obtuvo electron-updater
  coincide **exactamente** con el manifiesto firmado: `version`, nombre de archivo y `sha512`. Después de la
  descarga **recalcula** el sha512 y el tamaño. Si algo no coincide, descarta, sigue con la versión actual y lo deja
  en el log. Las claves se aceptan por `key_id`, contra una lista horneada en código, así se pueden rotar.
- **D-T7. Sin downgrade y sin "repetir":** solo se acepta una versión **mayor** que la instalada (semver).
  `autoUpdater.allowDowngrade = false`. El VPS también rechaza publicar una versión ≤ a la vigente. Para revertir,
  se publica una X.Y.Z+1 desde el commit anterior (va en la guía).
- **D-T8. Canal (D3/D8):** cada `update_url` es `https://<origen>/tatana/updates/`, uno por cada origen `https` de
  `TATANA_ORIGENES` y **en ese orden**. El primero es también el `client_url` ("Abrir Factum"). Se hornean en
  `resources/tatana-dist.json`. Hay un override **solo para la prueba de CI** con la variable de entorno
  `TATANA_UPDATE_URLS` (separada por comas): acepta `https://` o `http://127.0.0.1|localhost`. No es un riesgo,
  porque una URL ajena no puede servir una versión con firma válida. Las redirecciones solo se siguen a `https`
  (el `redir` de `caddy-sitios/` en la fase B sigue sirviendo).
- **D-T9. Red:** el manifiesto se pide con `net.fetch` de Electron (usa el proxy del sistema, como
  electron-updater), con un timeout de 10 s y un tope de 64 KB. Sin red, la fase queda en `offline`, sin
  ventanas ni demoras: el chequeo nunca está en el camino del arranque, salvo en D-T11.
- **D-T10. Operaciones en curso (D5):** el agente cuenta dos cosas:
  1. **requests mutantes en vuelo**: todo `POST`/`PUT`/`DELETE` salvo `/agent/maintenance`; cubre subida, import,
     ZIP, pull, capturas;
  2. **sesiones largas** que reportan los servicios: grabación Android, grabación iOS, sesión AirPlay de capturas
     y post-proceso de video iOS (`RunInterpolationsAsync`).

  `POST /agent/maintenance` es **atómico**: si hay algo en curso, devuelve 409 y no entra; si no, entra (con un
  TTL de 120 s) y desde ahí rechaza **toda request mutante nueva** con 503 `agent_updating`. Así no hay carrera
  entre "está libre" y "lo detengo". Solo se acepta **sin** header `Origin` ni `Sec-Fetch-Site` (no se puede
  llamar desde un navegador; Electron llama desde el proceso main con `http`).
- **D-T11. Cuándo se instala:** cuando la actualización está "lista" (descargada y verificada), se instala:
  - con **"Reiniciar y actualizar"** (bandeja o ventana; relanza Tatana oculto);
  - con **"Salir"** de la bandeja, si no hay operaciones (no relanza). Si hay operaciones, sale sin instalar,
    como hoy;
  - **en el próximo arranque**, si `update-state.json` quedó en `ready`: antes de lanzar el agente se repite el
    chequeo con un presupuesto de 8 s, que encuentra el archivo en la caché de electron-updater. Si llega a
    `ready`, instala y relanza. Si no, arranca normal.

  `autoInstallOnAppQuit = false`: nada se instala durante el apagado de Windows, que puede matar el proceso.
- **D-T12. Detener todo antes de instalar:** maintenance → `agentProcess.stopAndWait()` (árbol de procesos,
  `taskkill /T /F`) → `adb kill-server` con el adb incluido → matar lo que quede con ejecutable bajo
  `<instalación>\resources\agent\` (como `Install-TatanaPortable`, DP9). Sin esto, `adb.exe` bloquea la carpeta y
  NSIS falla. `customInit` y `customUnInit` repiten el último paso por las dudas.
- **D-T13. Single instance + órdenes por línea de comando.** `app.requestSingleInstanceLock()`. Una segunda
  instancia con `--quit` equivale a "Salir"; con `--install-update`, a "Reiniciar y actualizar"; sin argumentos,
  muestra la ventana. `--hidden` arranca sin mostrar la ventana: es el que usa el inicio de sesión y el relanzado
  después de actualizar. Esto también lo usa la prueba de CI.
- **D-T14. Arranque al iniciar sesión:** `autostart` pasa a `true` por defecto **solo empaquetado**, con
  `setLoginItemSettings({ openAtLogin, args: ['--hidden'] })`. En dev no se registra nada. El `Tatana.lnk` del
  portátil se saca en la migración: queda un solo Tatana al iniciar sesión.
- **D-T15. `/info.mode`:** el agente acepta `--mode installed|portable`. Electron pasa `installed`. Sin el
  argumento, se mantiene la heurística de `tools/` (portátil). No cambia el contrato (`"installed" | "portable"`).
- **D-T16. `appsettings.Local.json` externo:** con `--local-config <ruta>`, **reemplaza** al que está al lado del
  exe (el instalador nunca tiene uno ahí). Si el JSON es inválido, se **ignora** con un log de error y queda en
  `/agent/state.local_config.error`: Tatana arranca igual, no entra en el bucle de reinicios de Electron. Si define
  `Agent:AllowedOrigins`, queda `overrides_allowed_origins = true` y la ventana lo avisa (D9).
- **D-T17. Retiro del espejo GitLab (U3):** se borran `TatanaUpdatesController`, `TatanaUpdatesService` y
  `TatanaUpdatesOptions`, la sección `TatanaUpdates` de `appsettings.json`, sus registros en `Program.cs` y
  `TatanaUpdates__PublicBaseUrl` del compose. `/tatana/updates/*` pasa a ser estático en Caddy. También se borra
  `.gitlab-ci.yml`, que no corre y queda reemplazado por el workflow.
- **D-T18. Retiro de `update-portable.ps1`** y del bloque `UPDATE_URL` de `launch-tatana.bat` y de los `.ini`.
  Era un camino de actualización **sin verificación** (U1). Con D1 = B, el portátil queda solo para la instalación
  local, que se actualiza con `Actualizar Factum.bat`. Hoy todos los portátiles armados tienen `UPDATE_URL` vacío,
  así que no cambia ningún comportamiento ("La instalación local no cambia"). El portátil sí empieza a reportar su
  versión real (D-T1), que es una mejora.
- **D-T19. Versión mínima (D6) en el backend:** `Tatana:MinVersion` en `appsettings.json` (versionado: viaja con
  el código de la web que la necesita) y overridable por `Tatana__MinVersion`
  (`FACTUM_TATANA_VERSION_MINIMA` en `factum.env`). Valores: vacío, que no bloquea, o `X.Y.Z`. Cualquier otra
  cosa hace que el backend no arranque (`configErrors`). Sale en `GET /api/config/public` como
  `tatana_min_version`. Valor inicial: ver **P2**.
- **D-T20. Web por debajo del mínimo** = el mismo estado `outdated` que ya existe (`useAgentIdentity`), con un
  motivo nuevo `min_version`. Bloquea lo mismo que hoy bloquea `outdated` (capturar y generar el ZIP desde esta
  PC) y deja lo demás como está. Si está desactualizado pero es compatible: **sin indicador**.
- **D-T21. Publicación en el VPS:** usuario **`tatana-pub`** nuevo, **sin** grupo `docker` (`deploy` equivale a
  root). Su única clave lleva `command="/srv/factum/bin/publicar-tatana-forzado.sh",restrict`. El script recibe un
  `tar` por stdin, valida nombres y hashes, rechaza versiones ≤ a la vigente y publica con renombres atómicos.
  `tatana-pub` es dueño solo de `/srv/factum/tatana-updates` y `/srv/factum/descargas/tatana`.
- **D-T22. Retención:** en `/tatana/updates/` se guardan los **5** últimos `Tatana-Setup-*.exe` con su
  `.blockmap`, así la versión anterior sigue descargable para recuperarse a mano. En `/descargas/tatana/` solo
  queda `Tatana-Setup-Windows.exe` + `.sha256`, con nombre estable.
- **D-T23. Ensayo vs. release.** El mismo workflow corre en **modo ensayo** en:
  - PRs que tocan el armado o el actualizador;
  - `workflow_dispatch` con `publicar=false` desde cualquier rama.

  En ensayo se genera una **clave Ed25519 efímera** en el run y se hornea como clave extra solo en ese build
  (`TATANA_EXTRA_UPDATE_KEY` → define de Vite). Nunca se usa el secreto de producción y nunca se publica. En modo
  **release** (tag o dispatch con `publicar=true`, solo desde `main`, **P3**), la clave extra tiene que estar
  vacía (el workflow lo verifica) y se firma con el secreto del environment `tatana-release`. La prueba de
  Windows es idéntica en los dos modos.
- **D-T24. Versión base de la prueba = `0.0.1`** (agente y app). No se usa un prerelease de vX: electron-updater
  cambia de canal con versiones prerelease. Se fija `autoUpdater.channel = 'latest'` y `allowPrerelease = false`
  igual.
- **D-T25. `deleteAppDataOnUninstall` queda en `false` para siempre:** el workflow falla si no. `APP_FILENAME` es
  `Tatana` y los datos viven en `%LOCALAPPDATA%\Tatana\data`. La desinstalación borra solo
  `%LOCALAPPDATA%\Programs\Tatana` y la caché `…-updater`.
- **D-T26. `appId` fijo `com.factum.tatana`** y `productName` `Tatana`. Se agrega un `productName` de primer nivel
  en el `package.json`, así Electron y NSIS coinciden. El `name` npm `tatana-agent` no cambia: define
  `updaterCacheDirName`.

---

## 5. Agente Tatana (`server/src/Factum.Agent`)

### 5.1 Versión (D12, D-T1)
- `Factum.Agent.csproj`: `<Version>0.0.0-dev</Version>` y
  `<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>`.
- `Common/AgentVersion.cs` (nuevo):
  ```csharp
  public static class AgentVersion
  {
      public static string Current { get; }   // de AssemblyInformationalVersionAttribute del entry assembly
      internal static string Normalize(string? informational); // corta en '+', trim; null/vacío → "0.0.0-dev"
  }
  ```
- `HealthController`: `version = AgentVersion.Current` en `/health` y `/info`. `capabilities` pasa a
  `["case_evidence_v1", "real_version_v1", "agent_state_v1"]` (aditivo, ver §14 por los que sume #7).
- Log al arrancar: `"Factum Agent {Version} en {Url} (mock={Mock}, modo={Mode})"`.

### 5.2 Argumentos nuevos (`Program.cs`)
| Arg | Efecto |
|---|---|
| `--mode installed\|portable` | `AgentOptions.InstallMode`. Otro valor → no arranca (mismo estilo que `BindAddress`). |
| `--local-config <ruta>` | Carga ese JSON en lugar de `appsettings.Local.json` junto al exe (D-T16), en la misma posición de precedencia (después del último `appsettings*.json`, antes de env/CLI). `PhysicalFileProvider(Path.GetDirectoryName(ruta))`, `Optional = true`. |

Validación previa del JSON local: `Common/LocalConfigInspector.cs` (nuevo, puro y testeable):
```csharp
internal sealed record LocalConfigStatus(string Path, bool Exists, bool Loaded, string? Error, bool OverridesAllowedOrigins);
internal static class LocalConfigInspector
{
    // Lee el archivo (si existe), valida que sea JSON objeto y detecta Agent:AllowedOrigins (clave case-insensitive).
    public static LocalConfigStatus Inspect(string path);
    internal static LocalConfigStatus InspectText(string path, string? text); // para tests
}
```
Si `Error != null`, la fuente **no** se agrega y se loguea un error. El status se registra como singleton para
`/agent/state`. Sin `--local-config`, se inspecciona igual el de al lado del exe (`ContentRoot`).

### 5.3 Registro de operaciones y modo mantenimiento (D5, D-T10)
`Services/OperationTracker.cs` (nuevo, singleton, thread-safe con un solo `lock`):
```csharp
public sealed record AgentOperation(string Kind, DateTimeOffset Since, string? Detail = null);

public interface IOperationSource { IEnumerable<AgentOperation> ActiveOperations { get; } }

public sealed class OperationTracker(IEnumerable<IOperationSource> sources, TimeProvider time)
{
    // Middleware: false si está en mantenimiento (→ 503). Si true, devuelve un token a disponer al terminar.
    public bool TryEnterRequest(string method, string path, out IDisposable? token);
    public IReadOnlyList<AgentOperation> Snapshot();              // requests en vuelo + fuentes
    public bool IsBusy { get; }
    // Atómico: si hay operaciones → false + lista; si no → entra (TTL) y devuelve expiración.
    public bool TryEnterMaintenance(TimeSpan ttl, out DateTimeOffset expiresAt, out IReadOnlyList<AgentOperation> busy);
    public void ExitMaintenance();
    public bool InMaintenance { get; }                            // false si venció el TTL
}
```
- Las requests en vuelo se reportan como `Kind = "request"` con `Detail = "<METHOD> /<primer segmento>"` (por
  ejemplo, `"POST /cases"`): nunca el id del caso ni la query.
- `IOperationSource` lo implementan:
  - `AdbService`: `recording_android` mientras `_recordingProcess != null`, con `Since` = el inicio;
  - `IosService`: `recording_ios` (con `_session != null`), `airplay_session` (con `_shotSession != null`) y
    `video_postprocess` (un contador `Interlocked` que sube y baja en `RunInterpolationsAsync`, en un
    `try/finally`).

  Registro en DI: `AddSingleton<IOperationSource>(sp => (AdbService)sp.GetRequiredService<IAdbService>())`, o
  mejor, que las clases concretas se registren una sola vez y se expongan por las dos interfaces. El implementador
  elige, pero **una sola instancia** por servicio.
- Middleware en `Program.cs`, **después** de la guarda de origen y del `OPTIONS`, y antes de `next`: si el método
  es `POST`/`PUT`/`DELETE` y la ruta no es `/agent/maintenance`, llama a `TryEnterRequest`. Si da false:
  503 + `{ "error": "Tatana se está actualizando. Esperá unos segundos y reintentá.", "code": "agent_updating" }`.
  El token se dispone en un `finally`. Los `GET`, `/ws` y `/health` nunca se bloquean.
- `Common/AgentErrorCodes.cs`: `AgentUpdating = "agent_updating"`, `AgentBusy = "agent_busy"`,
  `MaintenanceLocalOnly = "maintenance_local_only"`.

### 5.4 Endpoints nuevos (`Controllers/AgentStateController.cs`)
| Método y ruta | Entrada | Respuesta |
|---|---|---|
| `GET /agent/state` | — | 200 `AgentStateResponse` (§13.A) |
| `POST /agent/maintenance` | `{ "ttl_seconds": 120 }` (opcional, se acota entre 10 y 600) | 200 `{ "maintenance": true, "expires_at": "<ISO-8601>" }` · 409 `{ "error": "Hay operaciones en curso", "code": "agent_busy", "operations": [AgentOperation…] }` · 403 `{ "error": "…", "code": "maintenance_local_only" }` si la request trae `Origin` o `Sec-Fetch-Site` |
| `DELETE /agent/maintenance` | — | 204 (idempotente) · 403 igual que arriba |

### 5.5 Tests (`server/tests/Factum.Agent.Tests`)
- `AgentVersionTests`: `Normalize("1.4.0+abc")` da `"1.4.0"`; `null` da `"0.0.0-dev"`.
- `LocalConfigInspectorTests`: inexistente; JSON inválido (`Error`, `Loaded = false`); sin orígenes; con
  `Agent:AllowedOrigins` en otro casing.
- `OperationTrackerTests` (con `FakeTimeProvider` o un `TimeProvider` propio):
  - con una request en vuelo, `TryEnterMaintenance` da false;
  - con una fuente activa, da false;
  - libre da true, y después `TryEnterRequest` da false;
  - el TTL vencido vuelve a aceptar requests;
  - `ExitMaintenance`;
  - disponer el token baja el contador.

---

## 6. agent-ui (Electron)

### 6.1 `package.json`
- Primer nivel: `"productName": "Tatana"`. `version` queda `1.0.0` en el repo: la pisa CI (D-T1).
- Scripts: `"test": "tsx --test src/main/updater/*.test.ts"`. DevDependency `tsx`. Dependency explícita `semver`
  (ya viene transitiva con electron-updater) y `@types/semver` en dev.
- `build`:
  ```jsonc
  {
    "appId": "com.factum.tatana",            // FIJO PARA SIEMPRE (D-T26)
    "productName": "Tatana",
    "directories": { "output": "dist", "buildResources": "build" },
    "files": ["out/**/*"],
    "extraResources": [
      { "from": "resources/agent", "to": "agent", "filter": ["**/*"] },
      { "from": "resources/tray",  "to": "tray",  "filter": ["**/*"] },
      { "from": "resources/dist-config", "to": "dist-config", "filter": ["tatana-dist.json"] }
    ],
    "publish": { "provider": "generic", "url": "https://factum.invalid/tatana/updates/", "channel": "latest" },
    "win": {
      "target": [{ "target": "nsis", "arch": ["x64"] }],
      "icon": "resources/icon.ico",
      "artifactName": "Tatana-Setup-${version}.${ext}"
    },
    "nsis": {
      "oneClick": true, "perMachine": false, "allowElevation": false,
      "deleteAppDataOnUninstall": false,       // D-T25: nunca true
      "include": "build/installer.nsh",
      "createDesktopShortcut": true, "createStartMenuShortcut": true, "shortcutName": "Tatana",
      "runAfterFinish": true, "differentialPackage": true
    },
    "mac": { … sin cambios … }, "linux": { … sin cambios … }
  }
  ```
  CI pisa `publish.url` con `-c.publish.url=<primer update_url>`, solo para que `app-update.yml` tenga algo
  coherente: el código siempre usa `setFeedURL`.
- `resources/dist-config/tatana-dist.json` **no se versiona** (`.gitignore`). En dev, si falta, el actualizador
  queda en `disabled` con el motivo "sin canal configurado". Se versiona un
  `resources/dist-config/tatana-dist.example.json` con el formato.

### 6.2 `electron.vite.config.ts`
Agregar el define `__TATANA_EXTRA_UPDATE_KEY__ = JSON.stringify(process.env.TATANA_EXTRA_UPDATE_KEY ?? '')` al
build de **main** (D-T23). El formato es `key_id:spkiDerBase64`, o vacío.

### 6.3 Instalador: `build/installer.nsh` + `build/migrar-portable.ps1` (D10, D-T5)
`installer.nsh`:
- `!macro customInit`:
  1. `InitPluginsDir`; `File /oname=$PLUGINSDIR\migrar-portable.ps1 "${BUILD_RESOURCES_DIR}\migrar-portable.ps1"`.
  2. `nsExec::ExecToLog 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\migrar-portable.ps1" -Accion Detectar'` → código:
     - `0` → nada que migrar, sigue;
     - `20` → portátil para la nube. Si **no** es silencioso: `MessageBox MB_OKCANCEL` con *"Se va a reemplazar
       Tatana portátil por esta versión instalada. Si tenés una grabación o captura en curso en Factum, terminala
       antes de continuar. Tu evidencia no se toca."* (Cancelar → `Quit`). Después,
       `-Accion Migrar -Silencioso <0|1>` → si da ≠ 0, `MessageBox` *"No se pudo reemplazar Tatana portátil
       (código N). No se borró nada. Cerrá Tatana y volvé a ejecutar el instalador."* + `SetErrorLevel N` +
       `Quit`;
     - `10` → instalación local: `MessageBox` *"Esta PC tiene la instalación local de Factum, con su propio
       Tatana. Este instalador es para Factum en la nube y no puede convivir con ella. Consultá la guía."* (si no
       es silencioso) + `SetErrorLevel 10` + `Quit`;
     - `30` → contenido desconocido en `%LOCALAPPDATA%\Programs\Tatana`: igual, con un texto propio y
       `SetErrorLevel 30`.
  3. `-Accion DetenerInstalado -Carpeta "$INSTDIR"`: mata los procesos con ejecutable bajo `$INSTDIR\resources\agent\`
     (D-T12). Siempre da 0.
- `!macro customUnInit`: lo mismo que el paso 3, antes de desinstalar.
- **No** hay `customUnInstall` que borre datos.

`migrar-portable.ps1` (PowerShell 5.1, sin módulos), parámetros
`-Accion Detectar|Migrar|DetenerInstalado [-Silencioso 0|1] [-Carpeta <ruta>]`:
- **Detectar.** `$dir = "$env:LOCALAPPDATA\Programs\Tatana"`. No existe o está vacío: 0. Existe
  `Uninstall Tatana.exe`: 0. Existen `launch-tatana.bat` y `Factum.Agent.exe`: lee `tatana-portable.ini`
  (`CLIENT_URL=`); si empieza con `https://` da 20, si no da 10. Cualquier otra cosa: 30.
- **Migrar** (solo si Detectar da 20; sigue este orden, y cada paso que falla deja todo como estaba):
  1. Detiene `Factum.Agent` y todo proceso con `Path` bajo `$dir\`; espera 2 s.
  2. Si existe `$dir\appsettings.Local.json`:
     - destino `%APPDATA%\Tatana\appsettings.Local.json` (crea la carpeta);
     - si el destino ya existe, copia como `appsettings.Local.portable.json` y marca `conflicto = true`;
     - copia y verifica que el SHA-256 sea igual.
  3. `Move-Item $dir "$env:LOCALAPPDATA\Tatana\portable-anterior-<yyyyMMdd-HHmmss>"`: hasta 3 intentos cada 2 s.
     Si falla, `exit 40`. Lo único que hizo fue la copia del paso 2, que es inofensiva.
  4. Si `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Tatana.lnk` apunta (`TargetPath`) dentro de
     `$dir`, lo **mueve** a la carpeta archivada.
  5. Escribe `%APPDATA%\Tatana\migracion-portable.json` (§13.E). `exit 0`.
  - **Nunca** lee, mueve ni borra `%LOCALAPPDATA%\Tatana\data` ni `C:\Factum\Evidencia`. Un comentario en la
    cabecera lo dice.
- **DetenerInstalado.** `Get-Process` con `Path` que empiece con `"$Carpeta\resources\agent\"`, y
  `Stop-Process -Force`. Siempre `exit 0`.
- Sale con un log en `%APPDATA%\Tatana\logs\instalador.log` (append, con fecha).

### 6.4 Proceso del agente (`src/main/agent-process.ts`)
- Argumentos: `--port <p> --data <dataDir> --local-config <userData>\appsettings.Local.json` y, empaquetado,
  `--mode installed`.
- `dataDir` (D-T3): empaquetado en `win32`, `join(process.env.LOCALAPPDATA, 'Tatana', 'data')`. Si no,
  `join(userData, 'agent-data')`. `tatana.json.agentDataDir` lo pisa si no está vacío.
- `stopAndWait(timeoutMs = 10000): Promise<void>`: en Windows, `taskkill /PID <pid> /T /F`; si no,
  `kill('SIGTERM')`. Resuelve en `close` o al timeout. `stop()` sigue existiendo.
- `binaryDir(): string | null` (la carpeta de `Factum.Agent.exe`), para D-T12.

### 6.5 Módulo de actualización (`src/main/updater/`)
| Archivo | Contenido |
|---|---|
| `trusted-keys.ts` | `export const TRUSTED_UPDATE_KEYS: ReadonlyArray<{ keyId: string; spkiDerBase64: string }> = []` (lo completa el usuario, §18). `allTrustedKeys()` suma `__TATANA_EXTRA_UPDATE_KEY__` si no está vacío. |
| `manifest.ts` | **Puro** (solo `node:crypto` y `semver`): `parseEnvelope(text)`, `verifyEnvelope(env, keys) → { ok: true, payload } \| { ok: false, reason }`, `decide(payload, currentVersion) → 'update' \| 'up_to_date' \| 'refused_downgrade'`, `matchesUpdateInfo(payload, info) → reason \| null`, `sanitizeUpdateUrls(baked, envOverride)`. |
| `manifest.test.ts` | `node:test` con `tsx`: firma válida; payload alterado; firma de otra clave; `key_id` desconocido; JSON roto; `schema`/`product` incorrectos; versión mayor, igual y menor; `updateInfo` con otro sha512, otro archivo u otra versión; URLs no https rechazadas, override `http://127.0.0.1` aceptado y `http://otro` rechazado. Las claves de prueba se generan en el test. |
| `dist-config.ts` | Lee `<resources>/dist-config/tatana-dist.json` (§13.C) y lo valida. |
| `state.ts` | Tipo `UpdateState` (§13.F) y persistencia en `<userData>/update-state.json` (escritura atómica: tmp + rename). |
| `update-manager.ts` | Máquina de estados, programación, verificación, instalación (abajo). |
| `logger.ts` | Logger de archivo `<userData>/logs/tatana-updater.log` (rota a 1 MB, deja 1 anterior). También se usa como `autoUpdater.logger`. Cada línea también va a `pushLog` de la ventana. |

`update-manager.ts`:
- Al arrancar: `autoUpdater.autoDownload = false`, `autoInstallOnAppQuit = false`, `allowDowngrade = false`,
  `allowPrerelease = false`, `channel = 'latest'`, `logger = fileLogger`.
- `disabled` (con su `reason`) si no está empaquetado, si no hay claves confiables o si no hay URLs.
- **Programación:** primer chequeo 10 s después de `ready`, después cada 6 h (`setInterval`), y además en
  `powerMonitor.on('resume')`. No chequea si la fase es `checking`, `downloading`, `ready` o `installing`.
- **`check()`:** para cada URL de `sanitizeUpdateUrls`:
  1. `net.fetch(url + 'tatana-update.json')` (D-T9). Si hay un error de red o un HTTP ≥ 400, prueba la URL
     siguiente.
  2. `verifyEnvelope`. Si falla, `reason` = `firma_invalida` | `clave_desconocida` | `manifiesto_invalido`,
     **prueba la URL siguiente** (un canal adulterado no tapa a otro sano) y lo loguea.
  3. `decide`: `up_to_date` o `refused_downgrade` termina (fase `up_to_date`).
  4. `autoUpdater.setFeedURL({ provider: 'generic', url, channel: 'latest' })`, `checkForUpdates()` y
     `matchesUpdateInfo(payload, result.updateInfo)`. Si no coincide: `rejected` (`no_coincide_con_manifiesto`)
     y prueba la URL siguiente.
  5. `downloadUpdate()` (fase `downloading`, con el `percent` de `download-progress`).
  6. En `update-downloaded`, recalcula el sha512 (base64) y el tamaño de `event.downloadedFile`. Si no
     coinciden: `rejected` (`hash_no_coincide`), borra el archivo descargado y **no** pasa a `ready`.
  7. `ready`, con `availableVersion`, `sourceUrl` y `verifiedAt`. Notificación (abajo).

  Si todas las URLs fallaron por red, la fase es `offline`. Si alguna falló por verificación y ninguna anduvo,
  `rejected`.
- **Notificación** (`new Notification({ title: 'Tatana', body: 'Hay una versión nueva de Tatana (vX.Y.Z). Se instala al reiniciar Tatana o al iniciar sesión.' })`):
  una sola vez por versión, controlada por `notifiedVersion` en `update-state.json`.
- **Ocupado:** en `ready`, cada 15 s hace `GET /agent/state` para refrescar `busyOperations` (tooltip, ítem de la
  bandeja y ventana).
- **`installNow({ relaunch })`** (D-T11, D-T12):
  1. Solo si la fase es `ready`.
  2. `POST /agent/maintenance` con `{ ttl_seconds: 120 }`:
     - 409: `busyOperations` = lista, no instala y devuelve `{ ok: false, reason: 'busy' }`;
     - agente caído o sin respuesta en 3 s: sigue (no puede haber operaciones).
  3. Fase `installing` (persistida).
  4. `agentProcess.stopAndWait()`; `adb kill-server` con `<binaryDir>\tools\platform-tools\adb.exe` (timeout de
     5 s); mata los procesos que quedaron bajo `binaryDir` (PowerShell `Get-Process | Where Path -like …`, timeout
     de 10 s).
  5. `autoUpdater.quitAndInstall(true, relaunch)`. Si tira una excepción: `agentProcess.start()`, fase `error`
     con `reason`.
- **Arranque con una actualización pendiente** (D-T11): si el `update-state.json` previo está en `ready`, corre
  `check()` **antes** de `agentProcess.start()`, con un presupuesto de 8 s. Si llega a `ready`, sigue con
  `installNow({ relaunch: true })` (sin agente, no hay maintenance). Si no, arranca el agente. Si el estado previo
  era `installing`, la app arrancó después de actualizarse: muestra la notificación *"Tatana se actualizó a
  vX.Y.Z"* (una vez) y la fase pasa a `up_to_date`.

### 6.6 `src/main/index.ts`
- Antes de todo: `app.setPath('userData', join(app.getPath('appData'), 'Tatana'))` y
  `requestSingleInstanceLock()`. Si no consigue el lock, `app.quit()`. Maneja `second-instance` según D-T13.
- `--hidden` (o el arranque de login): no hace `show()` en `ready-to-show`.
- Se borra el bloque actual de `autoUpdater` (L134–L145 y `checkForUpdatesAndNotify`) y se reemplaza por
  `updateManager.init()`.
- Bandeja: el tooltip es `Tatana vX.Y.Z` o `Tatana vX.Y.Z — Actualización lista: vA.B.C`. Menú:
  - **Abrir Factum**: `shell.openExternal(dist.client_url)`. Está oculto si no hay `client_url`.
  - Abrir Tatana.
  - Buscar actualizaciones.
  - **Reiniciar y actualizar**: visible solo en `ready`; `enabled = busyOperations.length === 0`. Deshabilitado,
    el label pasa a *"Reiniciar y actualizar (esperando: grabación en curso)"*, con el texto de §13.F.
  - Reiniciar agente .NET.
  - Salir. En `ready` y libre, llama a `installNow({ relaunch: false })`. Si no, sale como hoy.
- `createTray` envuelto en `try`: si falla en el runner, la app sigue.
- `setLoginItemSettings` solo si `app.isPackaged` (D-T14). `config.ts` `DEFAULTS.autostart = app.isPackaged`.
  `DEFAULTS.serverUrl = dist.client_url ?? 'http://localhost:5000'`.

### 6.7 IPC y preload (contrato interno main ↔ renderer)
Canales nuevos en `ipc.ts` y `preload/index.ts`, con tipos en `renderer/src/env.d.ts`:
| Canal | Dirección | Payload |
|---|---|---|
| `tatana:get-app-info` | invoke | `TatanaAppInfo` (§13.F) |
| `tatana:get-update-state` | invoke | `UpdateState` |
| `tatana:check-updates` | invoke | `UpdateState` (después de pedir el chequeo) |
| `tatana:install-update` | invoke | `{ ok: boolean; reason?: 'busy' \| 'not_ready' \| 'error' }` |
| `tatana:dismiss-notice` | invoke | `(id: 'migration' \| 'local_config')` → `void` |
| `tatana:update-state-change` | main → renderer | `UpdateState` |
| `tatana:app-info-change` | main → renderer | `TatanaAppInfo` |

### 6.8 Renderer
- **Versión siempre visible:** se reemplaza el "Versión del agente" actual de `Sidebar.tsx` por la versión de la
  app y, si difiere, también la del agente (`/agent/state.version`). Es útil para soporte.
- **Línea de estado de la actualización** (en `Dashboard.tsx`, debajo del estado del agente), un texto por fase:
  - `checking`: "Buscando actualizaciones…";
  - `downloading`: "Descargando vX (NN %)";
  - `ready`: "Actualización lista: vX" + botón **"Reiniciar y actualizar"** (deshabilitado con el motivo si está
    ocupado);
  - `up_to_date`: "Tatana está al día";
  - `offline`: "No se pudo buscar actualizaciones (sin conexión)";
  - `rejected`: "Se descartó una actualización: no pasó la verificación";
  - `error`: "No se pudo instalar la actualización";
  - `disabled`: sin línea en producción; en dev, "Actualizaciones desactivadas".
- **Avisos** (cerrables, persisten con `tatana:dismiss-notice`):
  - migración: *"Se reemplazó Tatana portátil (v{portable_version}). Tu evidencia sigue en %LOCALAPPDATA%\Tatana\data y C:\Factum\Evidencia. La carpeta del portátil anterior quedó en {carpeta_anterior}; la podés borrar cuando confirmes que todo anda."*
    Si hubo `conflicto`, se agrega *"Tu configuración local anterior quedó en appsettings.Local.portable.json."*;
  - D9: *"La configuración local ({path}) fija los orígenes permitidos: las actualizaciones de Tatana no los van a cambiar."*;
  - config local inválida: *"La configuración local ({path}) tiene un error y se ignoró: {error}."*
- Nada de diálogos modales.
- Skills obligatorias del implementador frontend: `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience`,
  `web-design-guidelines` (y `ui-styling`/`mblode-agent-skills-ui-animation` si corresponde).

---

## 7. Manifiesto firmado y claves (D7)

- **Algoritmo:** Ed25519 (`crypto.sign(null, …)` / `crypto.verify(null, …)` de Node).
  - Privada: PEM PKCS#8 en el secreto `TATANA_FIRMA_CLAVE_PRIVADA` del environment `tatana-release`.
  - Pública: SPKI DER en base64, en `trusted-keys.ts`.
- **Qué se firma:** los **bytes exactos** del payload (UTF-8), que viajan en base64 dentro del sobre. No se
  re-serializa nada, así no hay ambigüedad de canonicalización. Formatos en §13.C.
- **Herramientas** (`ops/tatana/`, Node ≥ 22, sin dependencias):
  - `generar-clave-firma.mjs --key-id <id> --salida <archivo.pem>`:
    - se niega si `--salida` cae dentro del repo (`git rev-parse --show-toplevel`);
    - escribe con modo 0600;
    - imprime la línea para `trusted-keys.ts` y el comando
      `gh secret set TATANA_FIRMA_CLAVE_PRIVADA --env tatana-release < <archivo>`.
  - `firmar-manifiesto.mjs --payload <payload.json> --key-id <id> --salida <tatana-update.json>`:
    - lee la clave **solo** de la env `TATANA_FIRMA_CLAVE_PRIVADA`;
    - verifica que la pública derivada esté en `trusted-keys.ts`, salvo que se pase `--clave-extra`, que se
      usa en el ensayo;
    - nunca imprime la clave.
  - `verificar-manifiesto.mjs --manifiesto <archivo> [--clave-extra id:spki]`: verifica contra
    `trusted-keys.ts`. Lo usan CI (antes de publicar) y la guía (soporte).
- **Rotación** (en la guía):
  1. Publicar una versión que confíe en la clave vieja **y** en la nueva, firmada con la vieja.
  2. Esperar a que las PCs actualicen (`agent_events`).
  3. Firmar con la nueva.
  4. Sacar la vieja de `trusted-keys.ts`.
- **Pérdida de la clave:** las PCs necesitan una reinstalación manual. La guía pide **dos copias** fuera de
  GitHub (un gestor de contraseñas y un medio offline).

---

## 8. Workflow de release (`.github/workflows/tatana-release.yml`, nuevo)

Convenciones del repo: actions pineadas por SHA (las mismas que `desplegar-produccion.yml`/`verificar.yml`), vars y
secrets que llegan a los `run:` **por `env:`**, `persist-credentials: false`, `permissions: contents: read` por
defecto.

**Disparadores**
- `push.tags: ['tatana-v*']` → modo **release**, versión = el tag sin el prefijo.
- `workflow_dispatch.inputs`: `version` (string, requerido) y `publicar` (boolean, default `false`). `true` →
  release; `false` → ensayo.
- `pull_request` a `develop`/`main` con `paths`: `.github/workflows/tatana-release.yml`, `ops/tatana/**`,
  `agent-ui/build/**`, `agent-ui/src/main/updater/**`, `agent-ui/package.json`,
  `deploy/windows/armar-tatana-portable.sh` → **ensayo** con versión `0.9.0`.
- `concurrency: { group: tatana-release, cancel-in-progress: false }`.

**Jobs**
1. **`preparar`** (`ubuntu-24.04`). Salidas: `version`, `modo` (`release`|`ensayo`), `origenes`, `client_url`,
   `update_urls`.
   - Valida la versión `^\d+\.\d+\.\d+$` y que sea ≠ `0.0.1`.
   - En release: el commit tiene que ser ancestro de `origin/main` (**P3**), `vars.TATANA_FIRMA_KEY_ID` tiene que
     estar en `trusted-keys.ts`, y la versión tiene que ser mayor que la publicada. La publicada se lee con `curl`
     a `<primer update_url>tatana-update.json`, decodificando `payload.version` con `node`; un 404 equivale a
     "primera publicación".
   - `origenes` = `vars.TATANA_ORIGENES || vars.FACTUM_URL_PUBLICA`, validado igual que en
     `armar-tatana-nube.sh`: el primero `https://`, minúsculas y sin barra final.
   - Chequeos de `agent-ui/package.json` con `node -e`: `build.nsis.deleteAppDataOnUninstall === false`
     (D-T25) y `build.appId === 'com.factum.tatana'` (D-T26).
2. **`payload`** (`ubuntu-24.04`; setup-dotnet 10 y python 3.12).
   - `git archive HEAD | tar -x -C src` y
     `deploy/windows/armar-tatana-portable.sh --version V --src src --salida out --origenes "$ORIGENES" --client-url "$CLIENT_URL"`.
   - `dotnet publish … -r win-x64 --self-contained -p:Version=0.0.1 -o base` y se guarda solo
     `Factum.Agent.exe`.
   - Artifacts (`retention-days: 7`): `payload` (el zip) y `agente-base`.
3. **`instalador`** (`windows-latest`; setup-node 22, cache npm). Con `shell: pwsh`:
   - Expande el zip en `agent-ui/resources/agent/` y **borra** lo que es solo del portátil: `launch-tatana.bat`,
     `install-portable.bat`, `tatana-portable.ini` y `version.txt`.
   - Verifica que existan `Factum.Agent.exe`, `appsettings.json` (con `Agent.Mock == false` y los
     `AllowedOrigins` esperados), `tools/platform-tools/adb.exe`, `tools/scrcpy/scrcpy.exe`,
     `tools/ffmpeg/ffmpeg.exe` y `tools/python-embed/python.exe`.
   - Escribe `agent-ui/resources/dist-config/tatana-dist.json` (§13.C).
   - En ensayo, genera la clave efímera (`node ops/tatana/generar-clave-firma.mjs`, en `$RUNNER_TEMP`) y exporta
     `TATANA_EXTRA_UPDATE_KEY`. En release, falla si esa variable no está vacía.
   - `npm ci`, `npm version V --no-git-tag-version --allow-same-version`, `npx electron-vite build` y
     `npx electron-builder --win nsis --x64 --publish never -c.publish.url=<primer update_url>`. Sale
     `dist/Tatana-Setup-V.exe`, `.blockmap` y `latest.yml`.
   - **Base:** se reemplaza `resources/agent/Factum.Agent.exe` por el de `agente-base`, se hace `npm version 0.0.1`
     y se rearma igual en `dist-base/`, con `-c.directories.output=dist-base`.
   - Artifacts: `instalador` (exe, blockmap, latest.yml), `instalador-base` (exe) y, en ensayo, `clave-ensayo`
     (la PEM efímera; `retention-days: 1`).
4. **`firmar`** (`ubuntu-24.04`). En release usa `environment: tatana-release`; en ensayo, ninguno.
   - Calcula el sha512 (base64) y el sha256 (hex) del exe y verifica que el sha512 coincida con `latest.yml`.
   - Arma `payload.json` (§13.C) con `released_at` UTC, `commit = github.sha` y `notes` = el input `notas`
     (opcional) o el mensaje del tag anotado.
   - Firma con `ops/tatana/firmar-manifiesto.mjs`. En release, la clave sale de
     `secrets.TATANA_FIRMA_CLAVE_PRIVADA` por `env:`; en ensayo, de la PEM efímera.
   - Corre `verificar-manifiesto.mjs`.
   - Genera `SHA256SUMS` (exe, blockmap, latest.yml, tatana-update.json).
   - Artifact `canal`.
5. **`prueba`** (`windows-latest`, `needs: [payload, instalador, firmar]`, `timeout-minutes: 40`). Corre
   `pwsh ops/tatana/prueba-windows.ps1 -Instalador … -InstaladorBase … -Canal … -PayloadZip … -Version V -OrigenWeb <client_url>`
   (§10). Si falla, sube `%APPDATA%\Tatana\logs`, `update-state.json` y los logs del agente.
6. **`publicar`** (`ubuntu-24.04`, `needs: prueba`, solo si `modo == 'release'` y
   `vars.TATANA_PUBLICAR_HABILITADO == 'true'`, `environment: tatana-release`, `permissions: contents: write`):
   - Prepara SSH igual que `desplegar-produccion.yml`, con `secrets.TATANA_PUB_SSH_KEY`,
     `secrets.DEPLOY_KNOWN_HOSTS`, `secrets.DEPLOY_HOST`, `vars.DEPLOY_PORT` y `vars.TATANA_PUB_USER`
     (default `tatana-pub`).
   - `tar -cf bundle.tar Tatana-Setup-V.exe Tatana-Setup-V.exe.blockmap latest.yml tatana-update.json SHA256SUMS`
     y `ssh … "publicar V" < bundle.tar`.
   - **Chequeo externo:** para cada `update_url`, `curl` de `tatana-update.json` (los bytes tienen que coincidir
     con los firmados) y `curl -I` del exe (`Content-Length` = el tamaño).
   - `gh release create tatana-vV --target <sha> --title "Tatana vV" --notes …` con los 5 archivos (si ya
     existe el tag, solo sube los assets).
   - Limpia la clave en `always()`.

**Secrets y variables** (todo en Settings del repo; **ninguno en el repo**)
| Nombre | Tipo | Dónde | Uso |
|---|---|---|---|
| `TATANA_FIRMA_CLAVE_PRIVADA` | secret | environment `tatana-release` | PEM Ed25519 (job `firmar`) |
| `TATANA_PUB_SSH_KEY` | secret | environment `tatana-release` | clave SSH de `tatana-pub` |
| `DEPLOY_KNOWN_HOSTS`, `DEPLOY_HOST` | secret | repo (ya existen) | host del VPS |
| `TATANA_FIRMA_KEY_ID` | var | repo | `key_id` vigente |
| `TATANA_ORIGENES` | var | repo | orígenes horneados, en orden (fallback `FACTUM_URL_PUBLICA`) |
| `TATANA_PUB_USER` | var | repo | default `tatana-pub` |
| `TATANA_PUBLICAR_HABILITADO` | var | repo | `'true'` para subir al VPS (igual que `FACTUM_DEPLOY_HABILITADO`) |
| `DEPLOY_PORT` | var | repo (ya existe) | puerto SSH |

Environment `tatana-release`: "Deployment branches and tags" = `main` + tags `tatana-v*`. Así, un PR o una rama
no pueden leer la clave de firma.

`verificar.yml`: en el job `node`, paso agent-ui, se agrega `npm test` después de los `tsc`.

---

## 9. VPS y Caddy (`deploy/cloud/`)

- **`Caddyfile`:** se saca `/tatana/updates/*` de `@backend` y se agrega, antes de `handle_path /descargas/*`:
  ```caddyfile
  handle_path /tatana/updates/* {
  	root * /srv/tatana-updates
  	@manifiestos path /latest.yml /tatana-update.json
  	header @manifiestos Cache-Control "no-cache"
  	file_server
  }
  ```
  También se actualiza el comentario de la cabecera.
- **`docker-compose.yml` (caddy):** volumen `${FACTUM_HOME:-/srv/factum}/tatana-updates:/srv/tatana-updates:ro`.
  En el backend se saca `TatanaUpdates__PublicBaseUrl` y se agrega `Tatana__MinVersion: ${FACTUM_TATANA_VERSION_MINIMA:-}`.
- **`factum.env.example`:**
  - `FACTUM_TATANA_VERSION_MINIMA=`, con un comentario: vacío = no bloquea; `X.Y.Z` = la web no captura con un
    Tatana menor;
  - el comentario de `FACTUM_TATANA_DESCARGA_URL` pasa a apuntar a
    `https://<dominio>/descargas/tatana/Tatana-Setup-Windows.exe`.
- **`scripts/publicar-tatana-forzado.sh`** (nuevo; copia estable en `/srv/factum/bin/`, root:root 755, igual que
  `deploy-forzado.sh`). `set -euo pipefail`. `SSH_ORIGINAL_COMMAND`:
  - **`estado`:** imprime el contenido de `/srv/factum/tatana-updates/VERSION`, o `ninguna`.
  - **`publicar X.Y.Z`** (regex estricta):
    1. Arma el staging en `/srv/factum/tatana-staging/<X.Y.Z>-<epoch>` (lo borra con `trap`).
    2. Copia stdin con `head -c 1073741825` a `bundle.tar`; si pasa 1 GiB, `exit 3`.
    3. `tar -tf`: tienen que ser **exactamente** los 5 nombres de §13.D (sin `/`, sin `..`, sin
       directorios); si no, `exit 4`.
    4. `tar -xf --no-same-owner --no-same-permissions`.
    5. `sha256sum -c SHA256SUMS` (si falla, `exit 5`) y que `latest.yml` tenga la línea `version: X.Y.Z`.
    6. Monotonía: si `VERSION` existe y `sort -V` dice que X.Y.Z ≤ la vigente, `exit 6`.
    7. Publica:
       - `install -m 644` del exe y del `.blockmap` a `tatana-updates/`;
       - copia del exe a `descargas/tatana/Tatana-Setup-Windows.exe.tmp`, `mv` y su `.sha256`;
       - `latest.yml` (tmp + `mv`), después `tatana-update.json` (tmp + `mv`), después `VERSION`.
    8. Retención (D-T22).
    9. `logger -t factum-tatana` en cada paso.

    Cualquier otra cosa: `exit 2`, igual que `deploy-forzado.sh`.
- **`scripts/preparar-servidor.sh`:** nuevo bloque **10** (con `confirmar`, idempotente):
  - crea el usuario `tatana-pub` (`--disabled-password`, **sin** grupo docker);
  - `~tatana-pub/.ssh/authorized_keys` vacío (0600);
  - `install -d -o tatana-pub -g tatana-pub /srv/factum/tatana-updates /srv/factum/descargas/tatana /srv/factum/tatana-staging`;
  - instala `publicar-tatana-forzado.sh` en `$RAIZ/bin` (root:root);
  - imprime la línea `command="/srv/factum/bin/publicar-tatana-forzado.sh",restrict ssh-ed25519 AAAA… factum-tatana-release`
    que se pega a mano.

  El bloque 1 de copia (`scp …`) del encabezado suma el script nuevo.
- `caddy-sitios/LEEME.md` aclara que el `redir` del dominio anterior también cubre `/tatana/updates/` y que
  Tatana sigue los redirects a `https` (D8).

---

## 10. Prueba automática en Windows (D11): `ops/tatana/prueba-windows.ps1`

Corre en `windows-latest` con `pwsh`. Cada paso hace `throw` con un mensaje claro. Helpers:
- `Esperar-Health -Version`: polling de `http://localhost:8765/health` cada 1 s, hasta 60 s;
- `Esperar-Fase -Fase`: lee `%APPDATA%\Tatana\update-state.json` cada 2 s, hasta 180 s;
- `Hash`: `Get-FileHash -Algorithm SHA256`.

Para lanzar la app: `Start-Process "$env:LOCALAPPDATA\Programs\Tatana\Tatana.exe" -ArgumentList '--hidden'`,
con `$env:TATANA_UPDATE_URLS = 'http://127.0.0.1:8099/'`. Para servir el canal:
`Start-Process python -ArgumentList '-m','http.server','8099','--bind','127.0.0.1','--directory',<canalDir>`.

1. **Estado previo: portátil para la nube.**
   - Expandir el `PayloadZip` en `%TEMP%\portable`. El `.ini` ya trae `CLIENT_URL=https://…`, que es lo que
     hace que Detectar dé 20. Que `launch-tatana.bat` abra el navegador en el runner no bloquea (`start ""`).
   - Correr `install-portable.bat`.
   - `Esperar-Health`: version = `V` (el portátil del payload ya trae la versión real).
   - Centinelas (aleatorios, 1 MB cada uno), guardando sus hashes:
     - `%LOCALAPPDATA%\Tatana\data\cases\ci-centinela\evidencia.bin`;
     - `C:\Factum\Evidencia\ci-centinela\caso.zip`;
     - `%LOCALAPPDATA%\Programs\Tatana\appsettings.Local.json` =
       `{"Agent":{"AllowedOrigins":["https://ci.invalid"]}}`.
2. **Instalar la base** con `Tatana-Setup-0.0.1.exe /S` y esperar a que termine. Comprobar:
   - exit 0;
   - `Programs\Tatana\Uninstall Tatana.exe` existe;
   - `%LOCALAPPDATA%\Tatana\portable-anterior-*` existe;
   - `Startup\Tatana.lnk` no existe;
   - `%APPDATA%\Tatana\appsettings.Local.json` tiene el mismo hash que el centinela;
   - `migracion-portable.json` existe;
   - el centinela de datos y el de evidencia siguen con el mismo hash.
3. **Arrancar la base** (lanzar la app con el canal **vacío**). `Esperar-Health 0.0.1`. Comprobar:
   - `/info.mode == 'installed'`;
   - `/health.tools.{adb,scrcpy,ffmpeg,python}.found == true`, con `path` bajo
     `Programs\Tatana\resources\agent\tools\`;
   - `/health.capabilities` contiene `real_version_v1`;
   - `/agent/state.local_config.loaded == true` y `overrides_allowed_origins == true` (D9);
   - `Get-NetTCPConnection -LocalPort 8765 -State Listen` tiene **un solo** dueño y su `Path` está bajo
     `Programs\Tatana\resources\agent\`;
   - `HKCU:\…\CurrentVersion\Run` tiene un valor cuyo dato contiene `Tatana.exe`;
   - **Origen:** `curl.exe -s -o NUL -w "%{http_code}" -H "Origin: <OrigenWeb>" http://localhost:8765/health` da
     `200`, y con `Origin: https://evil.invalid` da `403`.
4. **Firma inválida.**
   - Canal = exe vX + blockmap + latest.yml + `tatana-update.json` **re-firmado** con una clave aleatoria
     generada en el paso (mismo `key_id` y mismo payload), con un `node -e` inline (`generateKeyPairSync('ed25519')`
     + `sign`), no con `firmar-manifiesto.mjs`, que se niega a firmar con una clave que no es de confianza.
   - `Tatana.exe --quit`, esperar a que salga y relanzar. `Esperar-Fase rejected`.
   - `reason` es `firma_invalida`, la carpeta `%LOCALAPPDATA%\tatana-agent-updater\pending` está vacía o no
     existe, y `/health.version == 0.0.1`.
5. **Actualización válida, con una operación en curso.**
   - Canal = el `tatana-update.json` real del job `firmar`. Relanzar. `Esperar-Fase ready`
     (`availableVersion == V`).
   - Iniciar en segundo plano
     `curl.exe --limit-rate 100k -X POST --data-binary @<4 MB> -H "Content-Type: application/octet-stream" "http://localhost:8765/cases/0123456789abcdef01234567/evidence/upload?filename=ci-subida.bin"`
     (el `caseId` tiene que pasar `CaseEvidenceStore.RequireCaseId`; si exige otro formato, se ajusta).
   - Mientras sube: `/agent/state.busy == true`. `Tatana.exe --install-update`. A los 10 s:
     `/health.version` sigue en `0.0.1` y `update-state.json` sigue en `ready` con `busyOperations` no vacío.
   - Esperar a que termine la subida (HTTP 200). `Tatana.exe --install-update`. `Esperar-Health V` (hasta 120 s).
   - Después: `/agent/state.version == V`; la app sigue corriendo; los 3 centinelas y la subida tienen los hashes
     esperados; `update-state.json.phase` es `up_to_date` (después de `installing`).
6. **Sin downgrade ni repetición:** con el mismo canal, `--quit` y relanzar. `Esperar-Fase up_to_date`; la versión
   sigue en V. El caso "versión menor" lo cubren los tests unitarios de `manifest.ts`, porque no se firma un
   downgrade con la clave real.
7. **Sin red:** `TATANA_UPDATE_URLS=http://127.0.0.1:9/` (nadie escucha), relanzar. `Esperar-Health V` en ≤ 15 s y
   `Esperar-Fase offline`.
8. **Desinstalar:** `Tatana.exe --quit` y `"Programs\Tatana\Uninstall Tatana.exe" /S`. Comprobar:
   - `Programs\Tatana` no existe o solo quedan carpetas vacías;
   - `%LOCALAPPDATA%\Tatana\data` y `C:\Factum\Evidencia` siguen, con los mismos hashes;
   - `%APPDATA%\Tatana\appsettings.Local.json` sigue;
   - nada escucha en 8765.
9. Resumen en `$env:GITHUB_STEP_SUMMARY`.

La prueba con USB y un celular real queda **pendiente** (D11 validada): §16.3.

---

## 11. Backend (`server/src/Factum.Backend`)

- **Retiro de U3 (D-T17):** borrar `Controllers/TatanaUpdatesController.cs` y
  `Services/Updates/TatanaUpdatesService.cs` (y la carpeta, si queda vacía); en `Program.cs`, L76 (`Configure`) y
  L185–186 (`AddHttpClient`); en `appsettings.json`, la sección `TatanaUpdates`; y las filas de `README.md`.
- **Versión mínima (D6, D-T19):**
  - `TatanaOptions { public string? MinVersion { get; set; } }`, junto a las demás `*Options` (misma convención
    que `ReportOptions`). Sección `Tatana` en `appsettings.json`: `"Tatana": { "MinVersion": "" }`.
  - `Program.cs`: si `MinVersion` no está vacío y no cumple `^\d+\.\d+\.\d+$`, va a
    `configErrors.Add("Tatana:MinVersion tiene que ser X.Y.Z o vacío")`. Los blancos se normalizan a `null`.
  - `DTOs/ConfigDtos.cs`: `PublicConfigResponse(…, bool EncryptZip, string? TatanaMinVersion)` y el XML doc
    actualizado.
  - `ConfigController`: inyecta `IOptions<TatanaOptions>` y pasa el valor normalizado.
  - Test en `server/tests/Factum.Backend.Tests`: `GET /api/config/public` incluye `tatana_min_version` (`null` con
    la config vacía, `"1.4.0"` configurado). Si no hay infraestructura de test de controladores, alcanza con un
    test del DTO o de la normalización.

## 12. Client (`client/`)

- `src/lib/api.ts` → `PublicConfig.tatana_min_version?: string | null` (con doc).
- `src/hooks/usePublicConfig.ts`:
  - `PublicClientConfig.tatanaMinVersion: string | null` (un valor no string o vacío pasa a `null`);
  - **exportar** `loadPublicConfig()`.
- `src/lib/agent-version.ts` (nuevo, puro): `meetsMinVersion(health: AgentHealth, min: string): boolean`. Sin
  `real_version_v1` da false (D-T2); compara semver `X.Y.Z[-pre]` a mano, sin dependencia; si no parsea, da
  false.
- `src/lib/agent.ts`:
  - el doc de `AgentHealth.capabilities` menciona `real_version_v1` y `agent_state_v1`;
  - `AgentErrorCode` suma `"agent_updating"`.
- `src/hooks/useAgentIdentity.ts`:
  - `AgentIdentity` suma `outdatedReason?: "capability" | "min_version"` y `minVersion?: string`;
  - `fetchIdentity` espera `loadPublicConfig()` junto con `health`. Sin `case_evidence_v1`, da `outdated` +
    `capability`. Con `min` configurado y `!meetsMinVersion`, da `outdated` + `min_version`.
- `src/lib/agent-messages.ts`:
  - `agentStatusMessage` pasa a recibir `(status, identity?: Pick<AgentIdentity, "info" | "outdatedReason">)`.
    Con `min_version`: *"Tu Tatana{v} necesita actualizarse para esta versión de Factum. Se actualiza solo al
    reiniciarlo; si no, descargalo de {url}."*
    - `{v}` = ` (v${info.version})` solo si la versión es real (`real_version_v1`); si no, ` es de una versión
      anterior y`, con la redacción ajustada.
    - `{url}` = `NEXT_PUBLIC_TATANA_DOWNLOAD_URL`; sin URL: "pedile a tu administrador el instalador nuevo".
  - `agentErrorMessage` suma `case "agent_updating": return "Tatana se está actualizando. Esperá unos segundos y reintentá.";`
  - Para que `agentStatusMessage` sepa si la versión es real, `AgentInfo` no alcanza: se guarda
    `versionIsReal: boolean` en `AgentIdentity`.
- Call sites de `agentStatusMessage`: `app/dashboard/page.tsx` (L160, L440), `components/ReportStep.tsx` (L132) y
  `hooks/useFileManager.ts` (L586). Pasan la identidad.
- Sin cambios en `reportAgentEvent` ni en `useFileManager` L461: `info.version` ya es la real (D12).

## 13. Contrato compartido

Política JSON: el **agente** y el **backend** serializan en `snake_case_lower` (`JsonNamingPolicy.SnakeCaseLower`
en sus `Program.cs`). El agente omite los `null` (`WhenWritingNull`); el backend **no** los omite.

### 13.A Agente ↔ agent-ui (main) y client
Archivos:
- server: `server/src/Factum.Agent/Controllers/AgentStateController.cs`,
  `server/src/Factum.Agent/Controllers/HealthController.cs`, `server/src/Factum.Agent/Common/AgentErrorCodes.cs`,
  `server/src/Factum.Agent/Services/OperationTracker.cs`;
- agent-ui: `agent-ui/src/main/agent-api.ts` (nuevo, tipos + llamadas con `http`);
- client: `client/src/lib/agent.ts`, `client/src/lib/agent-messages.ts`.

`GET /health` (cambia el valor de `version`; `capabilities` es aditivo):
```json
{ "status": "ok", "version": "1.4.0", "mock": false, "ios_available": true,
  "tools": { "...": "sin cambios (#7 suma campos)" },
  "capabilities": ["case_evidence_v1", "real_version_v1", "agent_state_v1"] }
```
`GET /info`: `version` = la real; `mode` = `"installed"` con `--mode installed`.

`GET /agent/state`:
```json
{
  "version": "1.4.0",
  "mode": "installed",
  "busy": true,
  "maintenance": false,
  "operations": [ { "kind": "recording_android", "since": "2026-10-07T15:04:05+00:00" },
                  { "kind": "request", "since": "…", "detail": "POST /cases" } ],
  "local_config": { "path": "C:\\Users\\x\\AppData\\Roaming\\Tatana\\appsettings.Local.json",
                    "exists": true, "loaded": true, "overrides_allowed_origins": false }
}
```
- `kind` ∈ `recording_android` | `recording_ios` | `airplay_session` | `video_postprocess` | `request`.
- `local_config.error` (string) solo aparece si el archivo es inválido.
- En TS: `interface AgentOperation { kind: string; since: string; detail?: string }` e
  `interface AgentStateResponse { version: string; mode: 'installed' | 'portable'; busy: boolean; maintenance: boolean; operations: AgentOperation[]; local_config: { path: string; exists: boolean; loaded: boolean; overrides_allowed_origins: boolean; error?: string } }`.

`POST /agent/maintenance` → body `{ "ttl_seconds": 120 }`. Respuestas:
- 200 `{ "maintenance": true, "expires_at": "…" }`;
- 409 `{ "error": "Hay operaciones en curso", "code": "agent_busy", "operations": [ … ] }`;
- 403 `{ "error": "…", "code": "maintenance_local_only" }`.

`DELETE /agent/maintenance` → 204.

Request mutante en mantenimiento → **503** `{ "error": "Tatana se está actualizando. Esperá unos segundos y reintentá.", "code": "agent_updating" }`.

Argumentos de proceso (agent-ui → agente): `--port <n> --data <dir> --local-config <ruta> --mode installed`.

### 13.B Backend ↔ client
- `GET /api/config/public` suma `tatana_min_version: string | null` (siempre presente; `null` = sin mínimo).
- Archivos: `server/src/Factum.Backend/DTOs/ConfigDtos.cs` (`PublicConfigResponse.TatanaMinVersion`) ↔
  `client/src/lib/api.ts` (`PublicConfig.tatana_min_version`), leído en `client/src/hooks/usePublicConfig.ts`.
- Config: `Tatana:MinVersion` / env `Tatana__MinVersion` / `factum.env` `FACTUM_TATANA_VERSION_MINIMA`.

### 13.C Canal de actualización (CI ↔ agent-ui) y config horneada
`<update_url>tatana-update.json` (sobre). Lo escribe `ops/tatana/firmar-manifiesto.mjs` y lo lee
`agent-ui/src/main/updater/manifest.ts`:
```json
{ "schema": 1, "key_id": "tatana-2026-10", "payload": "<base64 de los bytes UTF-8 del payload>", "signature": "<base64, 64 bytes>" }
```
Payload (JSON, `snake_case`):
```json
{ "schema": 1, "product": "tatana-windows", "version": "1.4.0",
  "released_at": "2026-10-07T15:00:00Z", "commit": "<40 hex>",
  "file": "Tatana-Setup-1.4.0.exe", "size": 312345678,
  "sha512": "<base64, idéntico al de latest.yml>", "sha256": "<hex>", "notes": "texto opcional" }
```
Reglas de `verifyEnvelope`: `schema === 1`; `key_id` en las claves confiables; firma válida sobre los bytes
decodificados; payload `schema === 1`; `product === "tatana-windows"`; `version` semver `X.Y.Z`;
`file === "Tatana-Setup-" + version + ".exe"`; `size > 0`.

`latest.yml`: el que genera electron-builder, sin tocar.

`agent-ui/resources/dist-config/tatana-dist.json`. Lo escribe el job `instalador` y lo lee
`agent-ui/src/main/updater/dist-config.ts`:
```json
{ "schema": 1, "client_url": "https://factum.example.com",
  "update_urls": ["https://factum.example.com/tatana/updates/", "https://anterior.example.com/tatana/updates/"] }
```
Las `update_urls` son `https://` y terminan en `/`.

### 13.D Publicación en el VPS (CI ↔ `publicar-tatana-forzado.sh`)
- `ssh tatana-pub@<host> "publicar X.Y.Z" < bundle.tar` | `ssh … estado`.
- El tar trae exactamente: `Tatana-Setup-X.Y.Z.exe`, `Tatana-Setup-X.Y.Z.exe.blockmap`, `latest.yml`,
  `tatana-update.json` y `SHA256SUMS` (formato `sha256sum`).
- Códigos de salida:
  - 0 ok
  - 2 comando no permitido
  - 3 demasiado grande
  - 4 contenido del tar
  - 5 hashes o versión de latest.yml
  - 6 versión ≤ la vigente
- URLs resultantes: `https://<dominio>/tatana/updates/{latest.yml,tatana-update.json,Tatana-Setup-X.Y.Z.exe[.blockmap]}`
  y `https://<dominio>/descargas/tatana/Tatana-Setup-Windows.exe[.sha256]`.

### 13.E Migración del portátil (`migrar-portable.ps1` → agent-ui)
`%APPDATA%\Tatana\migracion-portable.json`:
```json
{ "schema": 1, "fecha": "2026-10-07T15:00:00-03:00", "portable_version": "1.3.0",
  "carpeta_anterior": "C:\\Users\\x\\AppData\\Local\\Tatana\\portable-anterior-20261007-150000",
  "config_local_migrada": true, "config_local_destino": "C:\\Users\\x\\AppData\\Roaming\\Tatana\\appsettings.Local.json",
  "conflicto": false, "mostrado": false }
```
`portable_version` sale de `version.txt`; si no está, `null`. agent-ui pone `mostrado: true` al cerrar el aviso.

### 13.F Estado del actualizador (agent-ui interno + prueba de CI)
`%APPDATA%\Tatana\update-state.json`. En camelCase, porque es interno de TS; **la prueba de CI lee `phase`,
`availableVersion`, `reason` y `busyOperations`**:
```ts
type UpdatePhase = 'disabled' | 'idle' | 'checking' | 'downloading' | 'ready' | 'installing'
                 | 'up_to_date' | 'offline' | 'rejected' | 'error'
interface UpdateState {
  phase: UpdatePhase
  currentVersion: string
  availableVersion?: string
  percent?: number
  sourceUrl?: string
  checkedAt?: string            // ISO
  verifiedAt?: string
  reason?: 'firma_invalida' | 'clave_desconocida' | 'manifiesto_invalido' | 'no_coincide_con_manifiesto'
         | 'hash_no_coincide' | 'sin_canal' | 'sin_claves' | 'no_empaquetado' | 'instalacion_fallida'
  busyOperations?: string[]     // kinds de /agent/state
  notifiedVersion?: string
}
interface TatanaAppInfo {
  appVersion: string
  agentVersion?: string
  clientUrl?: string
  notices: { migration?: MigracionPortable; localConfig?: { path: string; overridesAllowedOrigins: boolean; error?: string } }
}
```
Textos de `busyOperations` para la UI:
- `recording_android` / `recording_ios`: "grabación en curso";
- `airplay_session`: "sesión AirPlay en curso";
- `video_postprocess`: "procesando un video";
- `request`: "operación en curso".

---

## 14. Concurrencia con #7 (`ios-herramientas-windows`)

La #7 está en curso en `feat/ios-herramientas-windows` y todavía no tiene SDD (`Refactorizaciones/ios-herramientas-windows.md`
no existe al escribir esto). Según su HU validada, toca `IosService`, `ToolResolver`, `ToolInventory`,
`HealthController`, el armado del portátil, CI (un job de humo en `windows-latest`) y `client/` (`agent.ts`,
`IOSModePicker`, `usb-guide`). Su D10 publica el **portátil 1.3.0** a mano.

**Archivos que se pisan**
| Archivo | #7 | #21 (esta) | Riesgo |
|---|---|---|---|
| `server/src/Factum.Agent/Controllers/HealthController.cs` | `tools` suma `uxplay` y la versión de pymobiledevice3 (D8). Puede sumar capabilities. | `version` real, `capabilities` += `real_version_v1`, `agent_state_v1`, `mode` con `--mode`. | **Alto** (mismo método). Al rebasar se conservan **todas** las capabilities de las dos. |
| `server/src/Factum.Agent/Services/IosService.cs` | Cierre de procesos, script DVT, errores (cambios grandes). | Solo implementa `IOperationSource` (`recording_ios`, `airplay_session`, contador en `RunInterpolationsAsync`). | **Medio.** Si #7 cambia `RunInterpolationsAsync` o las sesiones, el hook se reubica. |
| `server/src/Factum.Agent/Program.cs` | Posible (encoding de Python, registro de servicios). | Args `--mode`/`--local-config`, middleware de mantenimiento, `OperationTracker`. | Medio. |
| `server/src/Factum.Agent/Services/AdbService.cs`, `Models/AgentModels.cs` | Poco probable. | `IOperationSource`, `AgentOptions.InstallMode`. | Bajo. |
| `server/src/Factum.Agent/Factum.Agent.csproj` | Posible (recursos del script, tests). | `Version`, `IncludeSourceRevisionInInformationalVersion`. | Bajo. |
| `deploy/windows/armar-tatana-portable.sh` | Fija pymobiledevice3, ffprobe (D9) y quizás uxplay. | `-p:Version=$VERSION` en `dotnet publish`; `cp` sin `*.ps1` (D-T18). | **Alto** (mismo archivo; cambios en líneas distintas). |
| `packaging/portable/*` | No previsto. | Borra `update-portable.ps1`, saca `UPDATE_URL`. | Bajo. |
| `.github/workflows/` | Job de humo Windows (nuevo, o en `verificar.yml`). | `tatana-release.yml` nuevo + `npm test` en `verificar.yml`. | Medio en `verificar.yml`. Si #7 deja un script de humo reutilizable (por ejemplo, en `ops/`), `prueba-windows.ps1` lo **llama** en lugar de duplicar la parte de `/health.tools`. |
| `server/tests/Factum.Agent.Tests/*` | Tests nuevos. | Tests nuevos. | Bajo (archivos distintos). |
| `client/src/lib/agent.ts` | `takeScreenshot`/AirPlay leen el `error`; tipos de `/health.tools`. | `AgentErrorCode` += `agent_updating`, docs de `capabilities`. | Medio (mismo archivo, distintas zonas). |
| `docs/despliegue-nube.md` §10 | Probable (publicar 1.3.0). | Reescribe §10 y §15 y suma una sección nueva. | **Alto** en §10: va después de #7. |

**Orden de merge recomendado: #7 primero**, después #21.
1. #7 se mergea a `develop` y publica el portátil **1.3.0** (su D10), como cierre del camino manual.
2. Los implementadores de #21 **arrancan desde `develop` con #7 adentro** (si #7 todavía no se mergeó al lanzar
   la implementación, la parte de Tatana y del armado espera; `client/` (D6), el backend (U3) y `deploy/cloud`
   se pueden adelantar). Antes del PR: `git rebase origin/develop`.
3. La **primera versión del instalador es la 1.4.0** (**P1**) y ya incluye los arreglos de iOS de #7. La prueba de
   Windows de #21 vuelve a validar el `/health.tools` que deja #7.
4. Si #7 se demora y se decide mergear #21 antes: #7 hereda los conflictos de `HealthController` y
   `armar-tatana-portable.sh` (`-p:Version` ya va a estar) y su 1.3.0 sale como **instalador** por este canal, no
   como portátil.

---

## 15. Checklist atómico

> Regla dura de datos (AGENTS.md): ningún implementador borra ni modifica documentos de negocio preexistentes ni
> archivos de `Storage`. Esta HU no escribe en Mongo. No se mueven tarjetas del Project ni se toca
> `progress/sesiones/`. Ningún secreto (clave privada, clave SSH, host) entra al repo.

### 15.1 Backend: `implementer-backend` (Tatana + backend + deploy + CI)

**Tatana (`server/src/Factum.Agent`)**
- [ ] B1. `Factum.Agent.csproj`: `<Version>0.0.0-dev</Version>` + `<IncludeSourceRevisionInInformationalVersion>false</…>`.
- [ ] B2. `Common/AgentVersion.cs` + `AgentVersionTests`.
- [ ] B3. `HealthController`: `version = AgentVersion.Current` (en `/health` y `/info`), `capabilities` con
      `real_version_v1` y `agent_state_v1`, `mode` = `InstallMode ?? heurística`.
- [ ] B4. `AgentOptions.InstallMode` + parseo de `--mode` (valores inválidos: no arranca).
- [ ] B5. `Common/LocalConfigInspector.cs` + tests; `--local-config` en `Program.cs` (D-T16); singleton
      `LocalConfigStatus`.
- [ ] B6. `AgentErrorCodes`: `AgentUpdating`, `AgentBusy`, `MaintenanceLocalOnly`.
- [ ] B7. `Services/OperationTracker.cs` (+ `IOperationSource`, `AgentOperation`) + `OperationTrackerTests`.
- [ ] B8. `AdbService` implementa `IOperationSource` (`recording_android`, con `Since` guardado en el start).
- [ ] B9. `IosService` implementa `IOperationSource` (`recording_ios`, `airplay_session`, `video_postprocess` con
      un contador `Interlocked` en un `try/finally` de `RunInterpolationsAsync`).
- [ ] B10. Registro DI: una instancia por servicio, expuesta como su interfaz y como `IOperationSource`;
      `OperationTracker` singleton.
- [ ] B11. Middleware de mantenimiento en `Program.cs` (después de la guarda de origen; 503 `agent_updating`).
- [ ] B12. `Controllers/AgentStateController.cs`: `GET /agent/state`, `POST`/`DELETE /agent/maintenance`
      (403 si hay `Origin`/`Sec-Fetch-Site`).
- [ ] B13. Log de arranque con versión y modo.

**Backend (`server/src/Factum.Backend`)**
- [ ] B14. Borrar `TatanaUpdatesController.cs`, `Services/Updates/TatanaUpdatesService.cs`, sus registros en
      `Program.cs` y la sección `TatanaUpdates` de `appsettings.json`.
- [ ] B15. `TatanaOptions` + sección `Tatana` + validación en `configErrors`.
- [ ] B16. `PublicConfigResponse.TatanaMinVersion` + `ConfigController` + test.

**Deploy (`deploy/cloud/`)**
- [ ] B17. `Caddyfile`: `/tatana/updates/*` estático (§9), fuera de `@backend`; comentario de cabecera.
- [ ] B18. `docker-compose.yml`: volumen `tatana-updates` en caddy; backend: sin `TatanaUpdates__PublicBaseUrl`,
      con `Tatana__MinVersion`.
- [ ] B19. `factum.env.example`: `FACTUM_TATANA_VERSION_MINIMA` y el comentario de `FACTUM_TATANA_DESCARGA_URL`.
- [ ] B20. `scripts/publicar-tatana-forzado.sh` (§9, códigos de §13.D). Probarlo localmente con un tar armado a
      mano, con `SSH_ORIGINAL_COMMAND` exportado y rutas por variables de entorno solo para test, o con un
      `--raiz` interno que el `command=` no permite pasar.
- [ ] B21. `scripts/preparar-servidor.sh`: bloque 10 (`tatana-pub`, carpetas, script forzado, línea de
      `authorized_keys`).
- [ ] B22. `caddy-sitios/LEEME.md` y `deploy/cloud/LEEME.md` (tabla de archivos y ruteo).
- [ ] B23. `armar-tatana-nube.sh`: marcarlo como **retirado para la nube** (cabecera + `echo` de aviso), con un
      puntero al workflow. No se borra: sirve de respaldo manual (ver P1).

**Packaging**
- [ ] B24. `deploy/windows/armar-tatana-portable.sh`: `-p:Version="$VERSION"` en `dotnet publish`; `cp` de
      `*.bat` y `*.ini` (sin `*.ps1`); sacar `UPDATE_URL=` de los dos heredocs del `.ini`. Que corra en
      `ubuntu-24.04`: revisar `shasum` (está en Ubuntu con perl) y `sed` portable.
- [ ] B25. `packaging/portable/`: borrar `update-portable.ps1`; sacar el bloque `UPDATE_URL` de
      `launch-tatana.bat` y la línea de `tatana-portable.ini`.
- [ ] B26. Borrar `.gitlab-ci.yml`.

**CI y herramientas (`ops/tatana/`, `.github/workflows/`)**
- [ ] B27. `ops/tatana/generar-clave-firma.mjs` (§7).
- [ ] B28. `ops/tatana/firmar-manifiesto.mjs` (lee `agent-ui/src/main/updater/trusted-keys.ts` con un regex
      simple; `--clave-extra id:spki` solo para ensayo/prueba).
- [ ] B29. `ops/tatana/verificar-manifiesto.mjs`.
- [ ] B30. Ida y vuelta local: generar una clave en `$TMPDIR`, firmar un payload de prueba, verificar ok, alterar un
      byte y verificar que falla. Dejar el comando en el progress.
- [ ] B31. `ops/tatana/prueba-windows.ps1` (§10). Si #7 dejó un script de humo, reusarlo para `/health.tools`.
- [ ] B32. `.github/workflows/tatana-release.yml` (§8): 6 jobs, modos release/ensayo, pins por SHA, `env:` para
      vars y secrets, chequeos de D-T25/D-T26, environment `tatana-release` en `firmar` y `publicar`.
- [ ] B33. `verificar.yml`: `npm test` en agent-ui.
- [ ] B34. Validar los workflows con `actionlint` si está disponible (`brew install actionlint`, o
      `docker run rhysd/actionlint`). Si no, dejarlo dicho.

**Docs**
- [ ] B35. `docs/despliegue-nube.md`:
      - §10: instalar con el instalador, SmartScreen paso a paso ("Más información" › "Ejecutar de todas
        formas"), qué pasa con el portátil anterior y con la instalación local;
      - §11: secrets y vars nuevas, environment `tatana-release`;
      - §15: el cambio de dominio = publicar una versión con `TATANA_ORIGENES=[definitivo, provisorio]`,
        mantener el `redir` en la fase B y ver qué PCs actualizaron en `agent_events.agent_version`, donde
        "2.0.0" = anterior a esta HU;
      - sección nueva **"Publicar una versión de Tatana"**: tag o Run workflow, ensayo, revertir con X.Y.Z+1,
        claves (generar, respaldar, rotar, pérdida) y `FACTUM_TATANA_VERSION_MINIMA`.
- [ ] B36. `README.md`: sacar las filas de `TatanaUpdates:*` y el texto de GitLab; describir el canal nuevo.
- [ ] B37. `progress/impl_backend_tatana-instalador-autoupdate.md` con archivos tocados, verificación y lo que
      queda manual.

### 15.2 Frontend: `implementer-frontend` (`agent-ui/` y `client/`)

Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como
criterio, sin 3D), `web-design-guidelines` (autochequeo). Suma `ui-styling`/`mblode-agent-skills-ui-animation`
si corresponde. Apps: **`agent-ui/` y `client/`**.

**agent-ui: instalador**
- [ ] F1. `package.json`: `productName` de primer nivel, `build` de §6.1, script `test`, deps `semver`,
      `@types/semver` y `tsx`.
- [ ] F2. `.gitignore` de `agent-ui`: `resources/dist-config/tatana-dist.json`, `dist-base/`. Versionar
      `resources/dist-config/tatana-dist.example.json`.
- [ ] F3. `build/installer.nsh` (`customInit`, `customUnInit`, §6.3).
- [ ] F4. `build/migrar-portable.ps1` (Detectar, Migrar, DetenerInstalado; nunca toca datos ni evidencia).
- [ ] F5. `electron.vite.config.ts`: define `__TATANA_EXTRA_UPDATE_KEY__` (main) y su `declare` en el `.d.ts` de
      main.

**agent-ui: main**
- [ ] F6. `index.ts`: `setPath('userData')`, single instance, `second-instance` (`--quit`, `--install-update`),
      `--hidden`, `createTray` en `try`, se borra el bloque viejo de `autoUpdater`.
- [ ] F7. `config.ts`: `autostart` = empaquetado; `serverUrl` = `client_url`; `agentDataDir` opcional;
      `setLoginItemSettings` solo empaquetado y con `args: ['--hidden']` (también en `ipc.ts` `save-config`).
- [ ] F8. `agent-process.ts`: args nuevos, `dataDir` (D-T3), `stopAndWait`, `binaryDir`.
- [ ] F9. `agent-api.ts`: `getAgentState`, `enterMaintenance`, `exitMaintenance`, con los tipos de §13.A, `http`
      sin `Origin` y timeout de 3 s.
- [ ] F10. `updater/trusted-keys.ts` (array vacío + comentario de cómo completarlo) y `allTrustedKeys()`.
- [ ] F11. `updater/manifest.ts` (puro) + `updater/manifest.test.ts`.
- [ ] F12. `updater/dist-config.ts`, `updater/state.ts` y `updater/logger.ts`.
- [ ] F13. `updater/update-manager.ts` (§6.5 completo, incluido el arranque con una actualización pendiente).
- [ ] F14. Bandeja: tooltip y menú (§6.6), refresco al cambiar el estado o las operaciones.
- [ ] F15. Notificaciones (una por versión; "se actualizó a vX").
- [ ] F16. `ipc.ts` + `preload/index.ts` + `renderer/src/env.d.ts`: canales de §6.7.
- [ ] F17. Lectura de `migracion-portable.json` y de `/agent/state.local_config` para `TatanaAppInfo.notices`.

**agent-ui: renderer**
- [ ] F18. `Sidebar.tsx`: versión de la app (y del agente, si difiere).
- [ ] F19. `Dashboard.tsx`: línea de estado de la actualización + botón "Reiniciar y actualizar" (deshabilitado con
      el motivo) + "Buscar actualizaciones".
- [ ] F20. Avisos cerrables: migración, orígenes fijados por la config local y config local inválida.

**client**
- [ ] F21. `lib/api.ts`: `PublicConfig.tatana_min_version`.
- [ ] F22. `hooks/usePublicConfig.ts`: `tatanaMinVersion` + export de `loadPublicConfig`.
- [ ] F23. `lib/agent-version.ts`: `meetsMinVersion` (puro).
- [ ] F24. `lib/agent.ts`: `AgentErrorCode` += `agent_updating`; docs de las capabilities.
- [ ] F25. `hooks/useAgentIdentity.ts`: `outdatedReason`, `minVersion`, `versionIsReal`.
- [ ] F26. `lib/agent-messages.ts`: `agentStatusMessage(status, identity)` con el texto de `min_version`;
      `agent_updating` en `agentErrorMessage`.
- [ ] F27. Call sites (`dashboard/page.tsx` ×2, `ReportStep.tsx`, `useFileManager.ts`).
- [ ] F28. `progress/impl_frontend_tatana-instalador-autoupdate.md` (incluye la constancia de las skills).

---

## 16. Verificación

### 16.1 Implementer-backend (antes de declararse `done`)
```bash
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test  server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
dotnet test  server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
bash -n deploy/cloud/scripts/publicar-tatana-forzado.sh deploy/cloud/scripts/preparar-servidor.sh deploy/windows/armar-tatana-portable.sh
command -v shellcheck && shellcheck deploy/cloud/scripts/publicar-tatana-forzado.sh
node --check ops/tatana/generar-clave-firma.mjs ops/tatana/firmar-manifiesto.mjs ops/tatana/verificar-manifiesto.mjs
# B30: ida y vuelta de firma en $TMPDIR (generar → firmar → verificar ok → alterar → verificar falla)
command -v pwsh && pwsh -NoProfile -Command '$e=$null;[void][System.Management.Automation.Language.Parser]::ParseFile("ops/tatana/prueba-windows.ps1",[ref]$null,[ref]$e);if($e){$e;exit 1}'
command -v actionlint && actionlint .github/workflows/tatana-release.yml .github/workflows/verificar.yml
```
Prueba manual del agente en dev (Mock), con `dotnet run --project server/src/Factum.Agent -- --mock --mode installed --local-config /tmp/x/appsettings.Local.json`:
- `curl localhost:8765/health` da `version: "0.0.0-dev"` y las 3 capabilities;
- `curl localhost:8765/agent/state`;
- `curl -X POST localhost:8765/agent/maintenance` da 200; un `POST` cualquiera da 503 `agent_updating`;
  `curl -X DELETE …` da 204;
- con `-H 'Origin: http://localhost:3000'`, `POST /agent/maintenance` da 403;
- un `appsettings.Local.json` inválido: arranca igual y `/agent/state.local_config.error` aparece.

### 16.2 Implementer-frontend
```bash
cd agent-ui && npx tsc --noEmit -p tsconfig.web.json && npx tsc --noEmit -p tsconfig.node.json && npm test && npx electron-vite build
cd client && npx tsc --noEmit
```
- `npm run dev` en agent-ui (macOS) con el agente de dev: la ventana muestra la versión, la línea "Actualizaciones
  desactivadas" (no empaquetado) y el aviso de config local si corresponde; la bandeja tiene los ítems nuevos.
- En `client`, con el backend local y `Tatana__MinVersion=9.9.9`, el paso de captura muestra el mensaje de
  `min_version`. Vacío: no bloquea.

### 16.3 Que queda para el usuario
1. **Pasos previos** (§18): clave de firma, `trusted-keys.ts`, secrets, vars, environment y bloque 10 en el VPS.
2. **Ensayo en CI:** el PR de la HU dispara `tatana-release.yml` en modo ensayo (por los `paths`). Tiene que pasar
   los 5 jobs; `publicar` no corre. Revisar el `Step Summary` de `prueba`.
3. **Primera release** (después de mergear a `develop` y promover a `main`): tag `tatana-v1.4.0` (P1) → ver que
   publica, después `curl https://<dominio>/tatana/updates/tatana-update.json` y
   `node ops/tatana/verificar-manifiesto.mjs`.
4. Actualizar `FACTUM_TATANA_DESCARGA_URL` → `https://<dominio>/descargas/tatana/Tatana-Setup-Windows.exe` (var
   de GitHub, para la imagen del frontend) y redeploy.
5. **Pendiente (D11):** prueba con una PC Windows real y un celular por USB (Leo o un perito piloto):
   - instalar desde `/descargas/` con SmartScreen;
   - captura y grabación Android e iPhone;
   - publicar 1.4.1 y ver que llega sola;
   - "Reiniciar y actualizar" bloqueado durante una grabación;
   - migración desde un portátil 1.2.0/1.3.0 real con evidencia.

---

## 17. Decisiones pendientes (necesitan al usuario)

- **P1. Numeración y primera versión del instalador.** Recomendada: **1.4.0**. #7 publica el portátil 1.3.0 a
  mano, como dice su D10, y es el último portátil para la nube. Desde 1.4.0 todo sale por el instalador.
  `armar-tatana-nube.sh` queda como respaldo manual, marcado como retirado. La alternativa es saltear el 1.3.0
  portátil y que la 1.3.0 ya sea el instalador, si #21 termina antes de que haya que distribuir los arreglos de
  #7.
- **P2. Valor inicial de `FACTUM_TATANA_VERSION_MINIMA`.** Recomendada: **vacío al mergear** y fijarlo en
  `1.4.0` recién cuando las PCs piloto migraron al instalador. Todo portátil viejo queda por debajo de cualquier
  mínimo (no tiene `real_version_v1`) y **no se auto-actualiza**: fijarlo antes bloquea las capturas de los
  peritos hasta que reinstalen a mano.
- **P3. Las releases de Tatana solo salen de commits que están en `main`.** Recomendada: **sí**, consistente con
  "a `main` solo se promueve `develop`" y con el deploy. Desde otras ramas solo hay ensayo, sin publicar. La
  alternativa es permitir desde `develop`, para sacar un arreglo de Tatana sin promover la web, pero rompe la
  regla de que lo publicado está en `main`.
- **P4. Minutos de Actions en Windows (repo privado, ×2).** Cada release o ensayo usa unos 2 jobs Windows de
  ~15–25 min, o sea **~60–100 min facturables** por corrida. Además, el ensayo corre en cada PR que toque los
  `paths` de §8. Recomendada: mantener los `paths` acotados como están y confirmar el cupo del plan. Si no
  alcanza, sacar el disparador `pull_request` y ensayar solo con `workflow_dispatch`.

## 18. Pasos manuales del usuario (no los puede hacer un agente)

1. `node ops/tatana/generar-clave-firma.mjs --key-id tatana-2026-10 --salida ~/Seguro/tatana-firma.pem`, guardar
   **dos copias** fuera de GitHub y pegar la línea pública que imprime en
   `agent-ui/src/main/updater/trusted-keys.ts` (commit en la rama de la HU o en un PR chico a `develop`).
2. GitHub → Settings → Environments → `tatana-release`, con deployment branches `main` + tags `tatana-v*`.
   Secrets: `TATANA_FIRMA_CLAVE_PRIVADA` (`gh secret set … --env tatana-release < pem`) y `TATANA_PUB_SSH_KEY`.
3. Vars del repo: `TATANA_FIRMA_KEY_ID`, `TATANA_ORIGENES` y, cuando el VPS esté listo,
   `TATANA_PUBLICAR_HABILITADO=true`.
4. En el VPS, como `admin`: copiar `preparar-servidor.sh` + `publicar-tatana-forzado.sh` y correr solo el bloque 10.
   Generar la clave SSH de publicación (`ssh-keygen -t ed25519 -C factum-tatana-release`) y pegar la pública con
   el `command=` en `~tatana-pub/.ssh/authorized_keys`. La privada va al secret `TATANA_PUB_SSH_KEY`. Después,
   redeploy (o `compose.sh up -d --force-recreate caddy`) para tomar el Caddyfile y el volumen nuevos.
5. Conseguir la PC Windows para §16.3.5.

> **Decisión del usuario (2026-10-07):** se aceptan P1 a P4 como se recomendó. **El repo pasó a ser público**, así que los minutos de GitHub Actions en runners estándar (Windows incluido) no tienen costo y P4 deja de ser una restricción. Los workflows tienen que seguir sin exponer secrets a PR de forks.
