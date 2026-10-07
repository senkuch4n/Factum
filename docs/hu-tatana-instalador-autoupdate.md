# HU: Instalador de Tatana para Windows y actualización automática desde la nube

**Slug:** `tatana-instalador-autoupdate` · **Issue:** #21
**Toca (preliminar, lo confirma la SDD):**
- `agent-ui/`: empaquetado NSIS, auto-actualización y avisos en la bandeja/ventana.
- `server/src/Factum.Agent`: versión real y config local fuera de la carpeta de instalación.
- `server/src/Factum.Backend`: el espejo `/tatana/updates/` se reemplaza o se adapta según D3.
- `packaging/portable/` y `deploy/cloud/`: armado y publicación.
- `.github/workflows/`: release de Tatana con prueba en Windows.
- `client/`: aviso de Tatana desactualizado o incompatible, solo si D6 lo incluye.
- Docs: secciones 10 y 15 de `docs/despliegue-nube.md`.

**Como** dueño de Factum (con Leo), que lo ofrece como SaaS a peritos y estudios,
**quiero** que cada perito instale Tatana en Windows con un instalador común y que después Tatana se actualice solo desde el servidor de Factum en la nube,
**para que** pueda distribuir correcciones y, sobre todo, el **cambio de dominio** (cuando se defina la marca) sin pedirle a cada perito que descargue y reinstale, y sin arriesgar nunca la evidencia ni la configuración de su PC.

---

## Contexto

### Qué existe hoy (arqueología sobre `feat/ios-herramientas-windows` ≈ `develop` `42fa825`, 2026-10-07)

**Distribución actual de Tatana para la nube**
| # | Dónde | Qué hay |
|---|---|---|
| E1 | `deploy/cloud/armar-tatana-nube.sh` → `deploy/windows/armar-tatana-portable.sh` | Se arma **en la Mac, a mano**, desde un árbol limpio de `origin/main`. Genera `Tatana-Portable-vX.Y.Z-Windows-nube.zip` + `.sha256`. Trae `Factum.Agent.exe` self-contained y `tools/` (adb, scrcpy 4.1 verificado por SHA-256, Python embebido con pymobiledevice3, ffmpeg y, si está configurado, uxplay). Hornea `Agent:AllowedOrigins` = localhost + orígenes de la nube y `CLIENT_URL` = el primer origen. **`UPDATE_URL` queda vacío a propósito** (D8b de despliegue-nube). |
| E2 | `https://<dominio>/descargas/Tatana-Portable-Windows-nube.zip` | Se publica a mano (`scp` + `sudo install`) en `/srv/factum/descargas`, servido por Caddy. La web muestra ese link en el mensaje `origin_not_allowed` (`NEXT_PUBLIC_TATANA_DOWNLOAD_URL`). La última versión armada es la **1.2.0**. |
| E3 | `packaging/portable/install-portable.bat` | Copia el zip a `%LOCALAPPDATA%\Programs\Tatana` (sin admin), crea `Tatana.lnk` en la carpeta Inicio del usuario y lo arranca. |
| E4 | `packaging/portable/launch-tatana.bat` + `tatana-portable.ini` | Lee `CLIENT_URL`, `UPDATE_URL` y `AGENT_PORT`. Si `UPDATE_URL` no está vacío, corre `update-portable.ps1` **antes** de arrancar el agente. Datos del agente en `%LOCALAPPDATA%\Tatana\data`. |
| E5 | Guía `docs/despliegue-nube.md` §10 y §15 | El cambio de dominio se hace en tres fases: (A) Tatana con los dos dominios, **redistribuido a mano**; (B) mudar el servidor y redirigir el dominio viejo con `caddy-sitios/`; (C) limpiar. Hay una salida para una sola PC: `appsettings.Local.json` al lado del `.exe`. |

**Piezas de actualización que ya existen (ninguna funciona hoy en la nube)**
| # | Dónde | Qué hace hoy | Problemas detectados |
|---|---|---|---|
| U1 | `packaging/portable/update-portable.ps1` | Lee `version.txt` local, pide `<UPDATE_URL>/latest-portable.json` (timeout de 5 s) y, si la versión es **distinta**, baja el zip de `manifest.url`. Mata `Factum.Agent` por nombre, descomprime en `%TEMP%`, **sobreescribe** la carpeta con `Copy-Item -Force`, marca `.pending-restart` y `launch-tatana.bat` se relanza. Ante cualquier error, sigue con la versión actual. | **No verifica integridad** (ni hash ni firma). Compara por "distinto", así que **puede hacer downgrade**. No borra archivos viejos. Mata el agente **sin mirar si hay una captura o grabación en curso**, y no cierra adb, scrcpy ni ffmpeg (DP9 de grabacion-android-windows sí lo hace en `Install-TatanaPortable`). Solo chequea al iniciar sesión. En el zip para la nube nunca se activa (`UPDATE_URL` vacío). |
| U2 | `Caddyfile`: `/tatana/updates/*` → backend | El enrutamiento existe. | — |
| U3 | Backend `Controllers/TatanaUpdatesController.cs` + `Services/Updates/TatanaUpdatesService.cs` (anónimo) | Sirve `latest.yml` y cualquier `{filename}` (feed de electron-updater), además de `latest-portable.json` (`{version, url}`) y `portable.zip`. Todo es un **proxy de releases de GitLab**: resuelve el último tag por la API de GitLab (con caché de 5 min) y baja los "raw job artifacts" de los jobs `build-installer-win`/`build-portable-win`. Config `TatanaUpdates:*` (vacía en el repo). | **El repo vive en GitHub**: con la config vacía, todo devuelve 404. El nombre del zip que espera (`Tatana-Portable-{tag}-Windows.zip`) no es el del zip para la nube. Lee el archivo **entero a memoria** (`ReadAsByteArrayAsync`): un zip de unos 300 MB por pedido en el VPS. |
| U4 | `.gitlab-ci.yml` | Por tag `v*`: publica el agente win-x64 **single-file**, arma el NSIS con `electronuserland/builder:wine` (inyecta `publish.url` desde `TATANA_UPDATES_URL`), arma el portátil y crea la Release en GitLab. | **No corre**: no hay GitLab. El NSIS solo copia `Factum.Agent.exe` a `resources/agent`: **no lleva `tools/`** (ni adb, ni scrcpy, ni pymobiledevice3) **ni `appsettings.json` con orígenes**, así que un Tatana instalado así no grabaría Android con scrcpy y no aceptaría la web en la nube. |
| U5 | `agent-ui/package.json` | `electron-builder` con `win.target: nsis`, `appId: com.factum.tatana`, `productName: Tatana`, `version: 1.0.0`, `publish: generic → http://localhost:8080/tatana/updates/`. `electron-updater` ^6.8.9 ya es dependencia. | `publish.url` apunta a localhost. Nunca se armó desde GitHub. |
| U6 | `agent-ui/src/main/index.ts` | `autoUpdater.autoDownload = true`, `autoInstallOnAppQuit = true`, `checkForUpdatesAndNotify()` **una sola vez al arrancar** (solo empaquetado). Los eventos solo van al log de la ventana. | La app se minimiza a la bandeja y casi nunca "sale", así que la actualización puede quedar pendiente días. No hay chequeo periódico ni aviso visible, y no sabe si hay una captura en curso. |
| U7 | `agent-ui/src/main/agent-process.ts` | Lanza `resources/agent/Factum.Agent.exe` con `--data <userData>/agent-data` (≈ `%APPDATA%\Tatana\agent-data`) y `cwd` = carpeta del binario. Si se cae, lo reinicia a los 3 s. | Un `appsettings.Local.json` queda **dentro de la carpeta de instalación**, y una actualización NSIS desinstala la versión anterior antes de instalar la nueva. |

**Datos de la PC que no se pueden perder**
| Qué | Dónde (hoy) |
|---|---|
| Evidencia en curso por caso (`<DataDirectory>/cases/<caseId>/`) | Portátil: `%LOCALAPPDATA%\Tatana\data`. Electron: `%APPDATA%\Tatana\agent-data`. Por defecto `./agent-data`, relativo al `.exe`. |
| ZIP de evidencia | `EvidenceDirectory`, por defecto `C:\Factum\Evidencia` (`AgentOptions.ResolveEvidenceDirectory`). |
| Config local | `appsettings.Local.json` **al lado de `Factum.Agent.exe`** (lo carga `Factum.Agent/Program.cs`). La guía lo usa como salida para agregar un origen en una sola PC. |
| Config de la UI de Electron | `%APPDATA%\Tatana\tatana.json` (`serverUrl`, `autostart`, `agentPort`). |

**Versión y compatibilidad**
- `Factum.Agent` responde **`version = "2.0.0"` fijo** en `GET /health` y `GET /info` (`HealthController.cs`), sin importar la versión real (`version.txt` del portátil: 1.2.0 en la nube; `package.json`: 1.0.0). La web manda ese valor como `agent_version` a la auditoría (`agent_events`) y a los datos del host del informe (`client/src/lib/api.ts`, `useFileManager.ts`). Hoy **el informe registra una versión de Tatana que no es la real**.
- La web ya detecta un Tatana viejo por `capabilities` (`case_evidence_v1`, `client/src/lib/agent.ts`).
- `/info.mode` dice `portable` si existe `tools/` al lado del exe; un instalador que traiga `tools/` se reportaría como `portable`.

**Instalación local (`deploy/windows/`)**: usa el mismo portátil, con `UPDATE_URL` vacío. Se actualiza con `Actualizar Factum.bat` (`Install-TatanaPortable` en `_comun.ps1`, que compara `version.txt` y pregunta S/N). No depende de la nube.

**CI en GitHub**: `verificar.yml` (build/test .NET y `tsc`, en `ubuntu-24.04`) y `desplegar-produccion.yml` (imágenes en GHCR + SSH restringido al VPS). No hay runner Windows ni release de Tatana. **El repo es privado**: los assets de una GitHub Release no son públicos.

### Por qué hace falta
- **El dominio va a cambiar** con la marca. Hoy eso obliga a redistribuir Tatana a mano en cada PC (fase A de §15) y, a cada perito que se atrase, la web le muestra "origen no permitido".
- Cada corrección de Tatana (por ejemplo, la grabación de Android en Windows) también se distribuye a mano.
- El usuario **no tiene PC con Windows**: todo lo que se pueda verificar tiene que verificarse en CI.

### Qué es nuevo
1. Un **instalador de Windows** armado y publicado por CI en GitHub Actions, que trae lo mismo que el portátil (agente, `tools/`, orígenes horneados) (D1).
2. Un **canal de actualización** servido por el dominio de Factum, con **integridad verificada** (D3, D7) y que **sobrevive al cambio de dominio** (D8).
3. Reglas de aplicación de la actualización: **nunca durante una captura**, sin downgrade, sin bloquear el uso si no hay red, y aviso al perito (D5).
4. **Versión real de Tatana** en `/health` y `/info`, así la web, la auditoría y el informe dicen la verdad. Habilita el bloqueo por incompatibilidad (D6, D12).
5. **Migración de las instalaciones existentes** (portátil para la nube) sin perder evidencia ni configuración (D9, D10).

---

## Criterios de aceptación

```gherkin
Feature: Instalador de Tatana para Windows con actualización automática desde la nube

  Background:
    Given Factum en la nube respondiendo en https://<dominio>
    And una versión de Tatana publicada en el canal de actualización (D3)

  # ── Instalación ─────────────────────────────────────────────────────────
  Scenario: Instalación limpia sin permisos de administrador
    Given una PC con Windows 10 u 11 x64 sin Tatana y un usuario sin permisos de administrador
    When el perito descarga el instalador desde https://<dominio>/descargas/ y lo ejecuta
    Then Tatana queda instalado para ese usuario sin pedir elevación
    And arranca solo al iniciar sesión
    And GET http://localhost:8765/health responde con la versión instalada real
    And /health informa adb, scrcpy, ffmpeg y python resueltos desde la carpeta de Tatana
    And la web https://<dominio> habla con Tatana sin el error origin_not_allowed

  Scenario: Advertencia de Windows en la primera instalación
    Given el instalador sin firma de código (si D2 = A)
    When Windows muestra la advertencia de SmartScreen
    Then la guía y la página de descarga explican paso a paso cómo continuar
    And esa advertencia no vuelve a aparecer en las actualizaciones automáticas

  # ── Actualización ───────────────────────────────────────────────────────
  Scenario: Hay una versión nueva y Tatana está libre
    Given Tatana vN instalado y vN+1 publicada
    When Tatana chequea actualizaciones (al arrancar y cada <intervalo, D5>)
    Then descarga vN+1 en segundo plano sin interrumpir al perito
    And verifica su integridad (D7) antes de considerarla lista
    And avisa al perito que hay una actualización lista
    And la instala según la política de D5, sin una captura en curso
    And después de reiniciar, /health responde vN+1

  Scenario: Hay una captura o grabación en curso
    Given una grabación, captura o armado de ZIP en curso en Tatana
    When hay una actualización lista para instalar
    Then Tatana no se detiene ni se reinicia
    And la instalación se posterga hasta que no haya operaciones en curso

  Scenario: Paquete corrupto o adulterado
    Given un archivo descargado cuyo hash o firma no coincide con el manifiesto (D7)
    When Tatana verifica la descarga
    Then la descarta, sigue con la versión actual y deja el motivo en el log
    And no ejecuta nada de lo descargado

  Scenario: Sin red o servidor caído
    Given la PC sin internet o el canal de actualización respondiendo error
    When Tatana arranca o chequea actualizaciones
    Then arranca y funciona igual con la versión instalada, sin demoras perceptibles ni ventanas de error

  Scenario: Nunca se instala una versión anterior
    Given Tatana vN instalado
    When el manifiesto publicado anuncia una versión menor que vN
    Then Tatana no la instala

  # ── Datos que no se pueden perder ───────────────────────────────────────
  Scenario: La actualización conserva evidencia y configuración
    Given archivos en la carpeta de datos del agente (cases/), ZIP en C:\Factum\Evidencia y un appsettings.Local.json
    When Tatana se actualiza de vN a vN+1
    Then todos esos archivos siguen existiendo con el mismo SHA-256
    And la configuración local sigue aplicándose en vN+1

  Scenario: Desinstalar no borra evidencia
    When el perito desinstala Tatana
    Then la carpeta de datos del agente y C:\Factum\Evidencia quedan intactas

  # ── Cambio de dominio ───────────────────────────────────────────────────
  Scenario: El cambio de dominio llega por actualización
    Given PCs con Tatana que solo aceptan <PROVISORIO>
    When se publica una versión con orígenes [<PROVISORIO>, <DEFINITIVO>] y canales de actualización según D8
    Then cada PC la recibe sola la próxima vez que Tatana chequea
    And después de mudar el servidor a <DEFINITIVO> (fase B de la guía §15) esas PCs siguen funcionando y siguen recibiendo actualizaciones

  Scenario: Config local que pisa los orígenes
    Given una PC con appsettings.Local.json que define Agent:AllowedOrigins
    When llega una versión con orígenes nuevos
    Then Tatana lo informa de forma visible según D9 en lugar de fallar en silencio

  # ── Compatibilidad ──────────────────────────────────────────────────────
  Scenario: Tatana demasiado viejo para la web (si D6 = A)
    Given una versión mínima de Tatana declarada por el servidor
    And una PC con Tatana por debajo de ese mínimo
    When el perito intenta iniciar una captura desde la web
    Then la web no la inicia y le explica que Tatana se está actualizando o cómo actualizarlo
    And la evidencia ya capturada sigue accesible

  Scenario: La versión real queda registrada
    When la web genera un informe o registra un evento de auditoría
    Then agent_version es la versión real instalada (no "2.0.0" fijo) (D12)

  # ── Convivencia y migración ─────────────────────────────────────────────
  Scenario: PC con el portátil para la nube
    Given una PC con Tatana-Portable-…-nube en %LOCALAPPDATA%\Programs\Tatana (sin auto-actualización)
    When el perito instala el instalador nuevo
    Then queda un solo Tatana arrancando al iniciar sesión, sin dos agentes peleando por el puerto 8765
    And la evidencia y el appsettings.Local.json existentes se conservan (D10)

  Scenario: La instalación local no cambia
    Given una instalación de deploy/windows (Factum en Docker en la PC)
    Then su Tatana, su armado y "Actualizar Factum" se comportan igual que antes de esta HU

  # ── Publicación ─────────────────────────────────────────────────────────
  Scenario: Publicar una versión
    When el usuario dispara una release de Tatana (D4)
    Then GitHub Actions arma el instalador y el manifiesto con la versión, el hash y la firma (D7)
    And los prueba en un runner Windows (D11)
    And solo si la prueba pasa, los publica en el canal (D3)
    And si la prueba falla, no se publica nada y la versión anterior sigue siendo la vigente
```

---

## Datos que se registran

**Manifiesto de la versión publicada** (servido por el canal de D3; el formato exacto lo define la SDD, por ejemplo `latest.yml` de electron-updater + firma)
| Dato | Obligatorio | Uso |
|---|---|---|
| Versión (semver) | Sí | Comparar con la instalada; sin downgrade. |
| Archivo y tamaño | Sí | Descarga. |
| Hash (SHA-512 de electron-updater y/o SHA-256) | Sí | Integridad (D7). |
| Firma del manifiesto | Según D7 | Que un servidor comprometido no pueda instalar software en las PCs. |
| Versión mínima compatible | Según D6 | Bloquear capturas con un Tatana incompatible. |
| Fecha y notas de la versión | No | Aviso al perito y soporte. |

**En la PC**
| Dato | Obligatorio | Uso |
|---|---|---|
| Versión instalada real | Sí | `/health`, `/info`, auditoría y `agent_version` del informe (D12). |
| Último chequeo, resultado y motivo de un descarte | Sí | Log de Tatana, para diagnosticar sin acceso a la PC. |
| Lista de canales de actualización (URLs) | Sí | Horneada en el paquete; se renueva con cada versión (D8). |

**Sin cambios de modelo en Mongo.** El `agent_version` existente pasa a ser real.

---

## Diseño UX/UI

### `agent-ui/` (Electron), si D1 = B
- **Instalador:** per-user, sin preguntas técnicas, con la marca de Tatana. El único paso extra es la advertencia de SmartScreen si no hay firma (D2).
- **Bandeja:**
  - el tooltip muestra el estado ("Tatana vX.Y.Z" / "Actualización lista: vX.Y.Z");
  - en el menú se agregan "Abrir Factum" (abre el primer origen), "Buscar actualizaciones" y, cuando hay una descargada, "Reiniciar y actualizar". Este último queda **deshabilitado con el motivo** mientras haya una captura en curso.
- **Ventana:** versión instalada visible y una línea de estado de la actualización: buscando, descargando con %, lista, al día, "no se pudo chequear (sin conexión)", "descartada: no pasó la verificación".
- **Notificación de Windows,** una sola vez por versión: "Hay una versión nueva de Tatana. Se instala al reiniciar Tatana o al iniciar sesión." Nada de diálogos modales ni de cierres forzados.
- **Estados de error:** sin red, no hace nada visible salvo la línea de estado. Si la verificación falla, la línea de estado lo dice y el log da el detalle.

### `client/` (web), si D6 = A
- **Tatana bajo la versión mínima:** antes de iniciar una captura, la web muestra un aviso claro: "Tu Tatana (vX) necesita actualizarse para esta versión de Factum. Se actualiza solo al reiniciarlo; si no, descargalo de <link>". Reutiliza el estilo del mensaje `origin_not_allowed` existente.
- **Tatana desactualizado pero compatible:** sin aviso, o como mucho un indicador discreto (lo decide la SDD).

### Guía
`docs/despliegue-nube.md`:
- §10: instalar con el instalador y qué hacer con SmartScreen.
- §15: el cambio de dominio pasa a ser "publicar una versión con los dos dominios y esperar a que las PCs actualicen". Suma cómo saber qué PCs ya actualizaron (por ejemplo, la auditoría `agent_events` con `agent_version`).
- Nueva sección "Publicar una versión de Tatana".

---

## Fuera de alcance
- Instaladores o actualización automática para **macOS y Linux**.
- **Firma de código Authenticode**, salvo que D2 la incluya.
- **Rollback automático** a la versión anterior si la nueva falla después de instalarse. Queda la recuperación manual: la versión anterior sigue descargable.
- Actualización automática de la **instalación local** (`deploy/windows/`): sigue con `Actualizar Factum.bat`.
- **Renombre del producto/marca** en el instalador o la UI (HU aparte). Esta HU solo garantiza que el cambio de dominio se pueda distribuir.
- Actualizar las herramientas embebidas (adb, scrcpy, pymobiledevice3) por separado del paquete: viajan dentro de cada versión. El caso de uso "Actualizar librerías" de `AGENTE_TATANA.md` es otra HU.
- Que Tatana descargue la lista de orígenes del servidor en tiempo de ejecución (descartado por seguridad en D8 de despliegue-nube). Los orígenes solo cambian con una **versión publicada y verificada**.
- Telemetría o panel de "qué PCs tienen qué versión", más allá de lo que ya registra `agent_events`.

## Notas de implementación (mínimas; el detalle es de la SDD)
- **`appId` fijo para siempre** (`com.factum.tatana`): si cambia, electron-updater deja de reconocer la instalación. El nombre visible puede cambiar con la marca, el `appId` no.
- Según D3, el espejo GitLab del backend (U3) se reemplaza o se retira. Si queda, no puede leer el archivo entero a memoria.
- El instalador tiene que llevar lo mismo que el zip de E1: `tools/`, `appsettings.json` con orígenes horneados, guarda de `Mock = false` y chequeos del contenido. Conviene reutilizar `armar-tatana-portable.sh` en lugar de duplicarlo.
- Verificar la carpeta por defecto del NSIS per-user: puede ser la misma que usa el portátil (`%LOCALAPPDATA%\Programs\Tatana`), lo que choca con D10.
- Con el instalador, `/info.mode` tiene que poder distinguir `installed` de `portable` aunque haya `tools/`.
- La versión tiene que tener una sola fuente (tag/`package.json` → assembly del agente → `/health`).
- Saber si hay una "operación en curso" necesita una señal del agente (grabación, captura, armado de ZIP). Es contrato entre `agent-ui` y `Factum.Agent`, va en la sección **Contrato compartido** de la SDD.

---

## Dudas para validar con el usuario

**D1. Formato de distribución para la nube.**
- A) Seguir solo con el **portátil** (zip + `.bat`) y arreglar `update-portable.ps1` (integridad, sin downgrade, sin matar capturas).
- B) **Instalador NSIS de Electron (`agent-ui`) per-user, sin admin**, con `electron-updater`, para la nube. El portátil queda solo para la instalación local (`deploy/windows/`), sin auto-actualización, como hoy.
- C) Los dos con auto-actualización.
- **Recomendada: B.** Es lo que pide el título. `electron-updater` ya está integrado y verifica el hash del paquete. La bandeja le da al perito un lugar visible para el estado y el aviso de actualización (el portátil no tiene UI propia). Las actualizaciones descargadas por la app no disparan SmartScreen. C duplica el trabajo de mantener y probar dos canales. A es lo más barato, pero el `.bat` + PowerShell es frágil de probar sin Windows y no tiene dónde avisar.
- **Pregunta adicional:** ¿el nombre visible sigue siendo "Tatana" aunque cambie la marca de Factum? Recomendado: sí, con `appId` fijo.

**D2. Firma de código del instalador.**
- A) **Sin firma en esta HU**: SmartScreen advierte una vez al instalar ("Windows protegió su PC" › "Más información" › "Ejecutar de todas formas") y la guía lo explica con capturas. La integridad de las actualizaciones la cubre D7.
- B) Certificado OV/EV comercial: costo anual de varios cientos de USD, a confirmar. Desde 2023 la clave tiene que estar en un token o HSM, lo que complica firmar en CI.
- C) Azure Trusted Signing: barato por mes, pero la elegibilidad por país y para entidades de Argentina está a confirmar.
- **Recomendada: A.** El certificado se emite a nombre de una persona o empresa, y **la marca todavía no está definida**: conviene comprarlo una vez definida y registrada. Se puede sumar en una HU chica sin cambiar el canal. **Pregunta:** ¿hay una entidad legal (empresa) a cuyo nombre emitirlo más adelante?

**D3. Dónde se publican las versiones y quién las arma.**
- A) **GitHub Actions las arma y las publica en el VPS** como archivos estáticos que sirve Caddy en `https://<dominio>/tatana/updates/` (y el instalador en `/descargas/`). La GitHub Release queda solo como archivo histórico (el repo es privado).
- B) **GitHub Releases como origen** y el backend hace de proxy con un token: se porta `TatanaUpdatesService` de GitLab a GitHub.
- C) Seguir armando en la Mac y subir con `scp`, como hoy.
- **Recomendada: A.** La PC del perito solo habla con el dominio de Factum, como ya diseñó U3. Con el repo privado, B obliga a guardar un token en el VPS y mantener un proxy que hoy carga cada archivo entero en memoria. C no deja probar en Windows antes de publicar. **Pregunta:** ¿está bien que el workflow de release use el acceso SSH restringido al VPS que ya usa el deploy, o preferís una clave aparte solo para subir archivos? Recomendado: una clave aparte, limitada a esa carpeta. Además, los minutos de runner Windows cuentan el doble en repos privados (a confirmar el cupo del plan).

**D4. Cuándo se publica una versión de Tatana.**
- A) **A mano, con un tag `tatana-vX.Y.Z`** (o "Run workflow" con la versión).
- B) Automático en cada merge a `main` que toque `server/src/Factum.Agent`, `agent-ui/` o `packaging/`.
- **Recomendada: A.** Cada versión llega sola a todas las PCs de los peritos: conviene que sea una decisión explícita y no un efecto colateral de un merge. La versión de Tatana sigue siendo independiente de la web.

**D5. Cómo se aplica la actualización.**
- A) Silenciosa total: se descarga y se instala sola apenas Tatana está libre, aunque el perito esté trabajando con la web.
- B) **Descarga silenciosa + aviso. Se instala al reiniciar Tatana o al iniciar sesión, o cuando el perito toca "Reiniciar y actualizar".** Nunca durante una captura. Chequeo al arrancar y cada 6 h.
- C) El perito decide si actualiza o no.
- **Recomendada: B.** A puede cortar el trabajo del perito en la web (Tatana se reinicia unos segundos en medio de un caso). C deja PCs viejas para siempre y frena el cambio de dominio. Con B, una PC que se prende todos los días queda al día en uno o dos días. **Pregunta:** ¿6 h está bien como intervalo?

**D6. Actualización obligatoria por incompatibilidad.**
- A) **El servidor declara una versión mínima de Tatana. Si la PC está por debajo, la web no inicia capturas nuevas y explica cómo actualizar.** La evidencia ya capturada sigue accesible.
- B) Solo el mecanismo actual de `capabilities` (la web detecta funciones faltantes caso por caso).
- C) Nada: se confía en la actualización automática.
- **Recomendada: A.** Es lo que evita que un Tatana viejo arme un ZIP o un informe con un contrato que el servidor ya no espera. Depende de D12 (versión real). B sigue sirviendo y se mantiene.

**D7. Verificación de integridad de lo que se descarga.**
- A) Hash (SHA-512) en el manifiesto servido por HTTPS: es lo que hace `electron-updater` por defecto. Protege contra descargas corruptas, **no** contra un servidor comprometido.
- B) **A + firma Ed25519 del manifiesto.** La clave privada vive **fuera del VPS** (secreto de GitHub Actions) y la pública va horneada en Tatana. Tatana descarta cualquier versión cuya firma no valide.
- C) Solo con firma Authenticode (depende de D2).
- **Recomendada: B.** Tatana corre en la PC donde está la evidencia forense. Si alguien toma el VPS, con A podría instalar software en todas las PCs de los peritos. Con B necesita además la clave de GitHub. **Costo:** si esa clave se pierde, las PCs necesitan una reinstalación manual. La guía documenta el respaldo de la clave.

**D8. Por dónde se actualiza una PC cuando cambia el dominio.**
- A) Una sola URL de actualización (el dominio actual). Se mantiene vivo el dominio viejo con la redirección de `caddy-sitios/` hasta que todas las PCs actualicen.
- B) **Una lista de URLs de actualización horneada, que se prueban en orden.** Cada versión nueva trae la lista vigente (por ejemplo, `[<DEFINITIVO>, <PROVISORIO>]` durante la transición). Es el mismo criterio que la lista de `Agent:AllowedOrigins`.
- C) Un hostname solo para actualizaciones, independiente de la marca.
- **Recomendada: B, y además mantener la redirección de A durante la fase B de §15.** Con una lista, una PC que estuvo apagada semanas igual encuentra un canal válido aunque el provisorio ya no exista (por ejemplo, si se muda de VPS). C exige otro dominio, que hoy no se quiere comprar. Con D7 = B, la lista no es un riesgo: una URL ajena no puede servir una versión válida.

**D9. Configuración local (`appsettings.Local.json`) en el instalador.**
- A) **Moverla a la carpeta de datos del usuario** (fuera de la carpeta de instalación, por ejemplo `%APPDATA%\Tatana\`). El instalador migra la que encuentre. Si esa config pisa `Agent:AllowedOrigins`, Tatana lo muestra en la ventana como aviso ("la config local fija los orígenes; las actualizaciones no los van a cambiar").
- B) Dejarla al lado del `.exe` y que el instalador la respalde y la restaure en cada actualización.
- **Recomendada: A.** Una actualización NSIS desinstala la versión anterior antes de instalar: lo que esté en la carpeta de instalación es descartable por diseño. B depende de que el respaldo nunca falle. El aviso evita que la salida de la guía §10 bloquee en silencio un cambio de dominio. **Pregunta:** ¿hay hoy PCs con `appsettings.Local.json` cargado a mano?

**D10. Convivencia con las instalaciones existentes.**
- A) **El instalador detecta un portátil en `%LOCALAPPDATA%\Programs\Tatana`.** Lo detiene (agente, adb, scrcpy, ffmpeg), saca su `Tatana.lnk` de Inicio, migra su `appsettings.Local.json` (D9) y deja intacta su carpeta de datos (`%LOCALAPPDATA%\Tatana\data`). El Tatana instalado usa esa carpeta si ya tiene casos, o la guía explica dónde quedó. La instalación local (`deploy/windows/`) no se toca.
- B) Conviven sin tocarse: dos agentes al iniciar sesión pelean por el puerto 8765.
- C) Instrucciones manuales en la guía para desinstalar el portátil.
- **Recomendada: A.** Los portátiles para la nube ya instalados tienen `UPDATE_URL` vacío: **nunca se van a actualizar solos**, así que una reinstalación manual es inevitable una vez. A la hace segura y sin pasos técnicos. **Preguntas:**
  - ¿Cuántas PCs tienen hoy el Tatana para la nube?
  - ¿Alguna PC tiene a la vez la instalación local y la nube?
  - Si el portátil tenía evidencia de un caso en curso, ¿se prefiere que el instalado use esa misma carpeta de datos (recomendado) o una nueva?

**D11. Cómo se verifica sin una PC con Windows.**
- A) **Prueba automática en un runner Windows de GitHub Actions dentro del workflow de release:**
  - instala vN en modo silencioso y comprueba que `/health` da vN con las herramientas resueltas;
  - crea archivos centinela en datos, evidencia y config local;
  - publica vN+1 en un canal local y fuerza la actualización;
  - comprueba que `/health` da vN+1 y que los centinelas siguen con el mismo SHA-256;
  - prueba que un paquete con firma inválida se descarta;
  - desinstala y comprueba que los datos quedan.

  Además, **una prueba real con USB en una PC Windows de un tercero** (Leo o un perito piloto), con un checklist corto, antes de liberar la primera versión.
- B) Una VM con Windows en la Mac (UTM/Parallels; en Apple Silicon es Windows ARM con emulación x64 y el USB es limitado).
- C) Solo CI.
- **Recomendada: A.** CI cubre la instalación, la actualización y la preservación de datos en cada release. La captura por USB (adb, scrcpy, iPhone) solo se puede probar con un celular conectado a una PC real. **Pregunta:** ¿quién tiene una PC con Windows para esa prueba (Leo, algún perito)?

**D12. Versión real de Tatana (incluirla en esta HU).**
Hoy `/health` y `/info` dicen siempre `2.0.0`, y ese valor viaja como `agent_version` a la auditoría y al informe.
- A) **Incluirlo: el agente informa la versión del paquete** (una sola fuente, ver Notas). Los informes y eventos **nuevos** registran la real. Los ya generados no se tocan.
- B) Dejarlo para otra HU.
- **Recomendada: A.** Sin la versión real no hay comparación para D5 ni mínimo para D6. Además, en un informe pericial no corresponde declarar una versión de software que no es la que se usó.

## Validación del usuario (2026-10-07)

- **D2 = A:** sin firma de código hasta que la marca esté definida.
- **D4 = A:** las versiones de Tatana se publican a mano (tag `tatana-vX.Y.Z` o "Run workflow" con la versión). Explicadas al usuario las opciones B (cada merge), C (canales prueba/estable) y D (junto con la web); C queda como posible HU futura.
- **D1, D3, D5–D12:** las recomendadas.
- **D11:** el usuario va a conseguir una PC con Windows más adelante. Mientras tanto, la verificación es CI en un runner Windows y la prueba con USB queda pendiente.
