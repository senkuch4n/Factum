# SDD: Más metadatos automáticos del dispositivo y diferencia de reloj celular/PC

**Slug:** `metadatos-dispositivo` · **Issue:** #79 · **HU:** `docs/hu-metadatos-dispositivo.md` (D1–D12 validadas con la
opción recomendada, incluidas las sub-dudas, el 2026-10-14) · **Autor:** architect · **Fecha:** 2026-10-14 · **Rama:**
`feat/metadatos-dispositivo`

El código se cita por **símbolo**. Los números de línea son una pista y pueden haber cambiado.

---

## 1. Resumen funcional

Tatana suma una lectura **bajo demanda** y **solo de lectura** de la "ficha técnica" del celular conectado: número de
serie, versión y compilación, parche, kernel, operador y SIM, estado de arranque (valores crudos), zona horaria, hora
automática y wifi. También mide la diferencia entre el reloj del celular y el de la PC, con un margen de error
calculado. La lectura sale por un endpoint nuevo (`GET /devices/{serial}/details`). La capacidad `device_details_v1` en
`/health` permite que la web detecte un Tatana viejo.

La web toma la ficha en el paso 2, la muestra y, al crear el caso, la manda al backend junto con una medición NTP de la
diferencia PC↔servidor. El backend valida y sanea cada valor, lo guarda en `Case.DeviceDetails` con la hora de
recepción (reloj del servidor) y deja un evento de trazabilidad sin SSID. Al generar, si el mismo dispositivo sigue
conectado, se hace una segunda medición de reloj.

El informe (plantilla v6) suma, en la sección del dispositivo, un bloque condicional `{#fichaTecnica}`. Lleva la
aclaración de origen, las filas de la ficha y un párrafo "Reloj del dispositivo". Ese párrafo incluye una frase neutra
si el desfase supera `Report:ClockSkewWarningSeconds` (default 60 s). Los casos sin ficha (anteriores a la HU, o con un
Tatana viejo) generan el mismo informe de siempre.

## 2. Toca

| Parte | ¿Toca? | Qué |
|---|---|---|
| **backend (Tatana)** `server/src/Factum.Agent` | **sí** | `Services/DeviceDetails/*` (nuevo: lector Android, parser iOS, reglas de validación, matemática del reloj, servicio), `Services/Ios/ios_helper.py` (subcomando `details`), `Services/IosService.cs` (método que corre `details`), `Models/DeviceDetailsModels.cs` (nuevo), `Models/AgentModels.cs` (`AgentOptions.MockDeviceClockSkewSeconds`), `Controllers/DeviceDetailsController.cs` (nuevo), `Controllers/HealthController.cs` (`device_details_v1`), `Common/AgentErrorCodes.cs` (`device_not_found`), `Program.cs` (DI). Tests en `server/tests/Factum.Agent.Tests` con fixtures reales. |
| **backend (API)** `server/src/Factum.Backend` | **sí** | `Models/DeviceDetails.cs` (nuevo), `Models/Case.cs` (`DeviceDetails`), `Models/CaseEvent.cs` (2 tipos + campos de detalle), `DTOs/DeviceDetailsDtos.cs` (nuevo), `DTOs/ConfigDtos.cs` (`clock_skew_warning_seconds`), `Controllers/CasesController.cs` (3 rutas), `Controllers/TimeController.cs` (nuevo, `GET /api/time`), `Controllers/ConfigController.cs`, `Services/Cases/DeviceDetailsRules.cs` (nuevo), `Services/Cases/DeviceDetailsFlow.cs` (nuevo, `partial class CaseService`), `Services/Cases/CaseService.cs` (`ICaseService`, flujo viejo), `Services/Cases/AgentGeneration.cs` (filtro de la medición de cierre), `Infrastructure/MongoRepository.cs` (3 escrituras), `Services/Reports/ReportOptions.cs`, `ReportSettings.cs`, `ReportValues.cs`, `ReportService.cs`, `ReportDeviceTexts.cs` (nuevo), `Templates/plantilla_informe_v{4,6}.docx` (regeneradas con los scripts, nunca a mano), `appsettings.json`. Tests en `server/tests/Factum.Backend.Tests`. |
| **scripts de plantilla** `ops/plantilla/` | **sí** | `build_plantilla_v4.py` (bloque `{#fichaTecnica}` después de "Titular"), `build_plantilla_v6.py` (las filas de la ficha como segunda ficha etiqueta/valor), `README.md`. |
| **client** `client/` | **sí** | `lib/agent.ts`, `lib/api.ts`, `types/index.ts`, `lib/device-details.ts` (nuevo), `hooks/useDeviceDetails.ts` (nuevo), `hooks/usePublicConfig.ts`, `components/device-details/*` (nuevos), `components/case-form/CaseSummaryCard.tsx`, `components/CaseFormStep.tsx`, `components/dashboard/GenerateStep.tsx`, `components/case-detail/CaseDetailContent.tsx`, `lib/case-events.ts`, `app/dashboard/page.tsx`. |
| **agent-ui** `agent-ui/` | **no** | Sin cambios (HU, fuera de alcance). |
| **deploy / docs** | **sí** (lo hace `implementer-backend`) | `deploy/cloud/docker-compose.yml`, `deploy/cloud/factum.env.example`, `deploy/windows/docker-compose.yml`, `deploy/windows/.env.example`, `deploy/windows/scripts/restaurar.ps1`, `deploy/windows/tests/comun.Tests.ps1`, `README.md` (placeholders y configuración), `AGENTE_TATANA.md` (caso de uso nuevo). |

**Reparto.** `implementer-backend` hace §14.1: Tatana, API, plantillas, deploy y docs. `implementer-frontend` hace §14.2:
solo `client/`.

**Orden.** Los dos lados pueden avanzar en paralelo. El cliente tolera un backend sin las rutas nuevas y un Tatana sin
`device_details_v1` (§11.2). La prueba manual (§15.2) necesita los dos y un Tatana compilado desde esta rama.

---

## 3. Arqueología verificada (sobre `460fac0`)

- **Serialización.** El backend usa `JsonNamingPolicy.SnakeCaseLower` (`Program.cs`) y los enums salen como strings.
  Tatana (`Factum.Agent/Program.cs`) hace lo mismo y además usa `DefaultIgnoreCondition = WhenWritingNull`: un `null`
  **no viaja** desde Tatana. Las **claves de diccionario no pasan por la política de nombres**, así que las claves de
  `fields` se escriben ya en snake_case literal (§8).
- **Mongo sin convenciones.** Los campos se guardan con el nombre C# (PascalCase). Toda clase persistida lleva
  `[BsonIgnoreExtraElements]`. Los campos nuevos son anulables o tienen default, sin migración.
- **Lectura actual de Tatana.**
  - `AdbService.EnrichDeviceAsync` (~L785) corre 5 `getprop` + `service call iphonesubinfo 1` **por dispositivo en cada
    sondeo de 3 s** (`Program.cs`, bucle de `devices_changed`) y en `GET /devices`. No se toca: la ficha va por un
    endpoint aparte (D-T1).
  - `IosService.ListDevicesAsync` corre `ios_helper.py devices` en cada sondeo, que abre lockdown y lee `all_values`.
  - `Device.Operator` en iOS lleva el **número de teléfono**. Sigue igual (D7, sub-duda).
- **Ejecución de adb.** `ProcessRunner.RunArgumentListAsync(exe, args, ct, env, timeout)` acepta timeout: al vencerse
  mata el árbol y devuelve `ExitCode = ProcessRunner.TimeoutExitCode` (-2). Si falta el ejecutable devuelve -1. Hay que
  replicar el patrón de `AndroidScreenProbe`: un script de shell como **un solo argumento** después de `shell`, el
  ejecutor inyectable para los tests y comillas simples dentro del script.
- **Helper de iPhone.** `IosHelper.RunAsync(python, helper, subcommand, args, timeout, log, ct)` devuelve el stdout
  (una línea JSON). Ante un error lanza `IosException` con el código de `TATANA_ERROR`. El helper usa
  pymobiledevice3 **10.7.4**, que en esta Mac está en `/opt/homebrew/lib/python3.14/site-packages`. En esa versión,
  `LockdownClient.get_value(domain=None, key=None)` es **async** y `all_values` es un dict que se llena al conectar.
- **Capabilities.** Están en `HealthController.Capabilities` (array estático, con un test que verifica su contenido).
  El cliente las lee con `agent.hasCapability(name)`, que las cachea por conexión.
- **Alta del caso.** `dashboard/page.tsx` `handleSaveCase` → `api.createCase({... device: {...}})` → `go(3)`. En
  Android, `os_version` sale como `` `Android ${android_version}` ``, que es un entero: "8.1.0" queda en "Android 0".
  No se toca (HU). La ficha guarda la versión como texto.
- **Paso 2 del wizard.** Es `CaseFormStep` (el paso 1 es `DeviceConnect`, la selección). El equipo se ve en
  `CaseSummaryCard` (columna lateral `aside` en lg+, `drawer` en < lg), que tiene un plegable de especificaciones
  (`specsOpen`) con `SpecRow` IMEI / **"Serial"** (que en realidad es el identificador de conexión ADB o el UDID) /
  Nombre. La columna lateral tiene **alto fijo sin scroll interno**, así que la ficha completa no entra ahí (D-T11).
- **Retomar.** `proceedResume(cas)` arma `selDevice` desde `cas.device` y, en el flujo agent, recarga el caso con
  `api.getCase`. `handleResume` espera al dispositivo con `ResumeDeviceModal` si no está conectado.
- **Generación agent.** El orden en `handleGenerateAgent` es:
  1. `ensureAgentIdentity`;
  2. `api.prepareGeneration`, que crea `PendingGeneration` y **no cambia el status**;
  3. `agent.buildZip`;
  4. `api.finishGeneration`;
  5. `agent.commitZip`.

  En el backend, `FinishGenerationAsync` relee el caso (`fresh`), verifica `fresh.PendingGeneration?.Id` y llama a
  `_reports.GenerateReportAsync(fresh, …)`. El flujo viejo es `CaseService.GenerateAsync` → `ReportService.GenerateAsync`.
- **Escrituras de caso.** Usan `MongoCaseRepository.Editable(id)` (status distinto de `Generating` y `Completed`), y
  `CaseService.IsEditable` aplica el mismo criterio. **"Borrador"** en esta SDD significa editable: `draft` o `error`.
- **Informe.** `ReportService.GenerateDocxAsync`, en este orden:
  - B-R1 borra los placeholders que no están en `KnownPlaceholders` ni en `BlockKeys`;
  - B-R2 `ResolveBlocks`: los marcadores `{#k}`/`{/k}` son **párrafos hermanos**, y entre ellos se borra todo, tablas
    incluidas;
  - B-R4 resuelve los multilínea (`ReplaceParagraphPerLine` para texto plano, que es como se hizo
    `{descripcionSelloTiempo}`);
  - B-R6 reemplaza el texto con `ReportValues.Placeholders(cas, settings, brand)`.
- **Plantilla.**
  - `build_plantilla_v4.py` arma la v4 desde la plantilla privada de `docs/`. #78 agregó el bloque `selloTiempo`
    sumando `{descripcionSelloTiempo}` a `BODY_PLACEHOLDERS`, `"selloTiempo"` a `BODY_BLOCKS` e insertando los
    marcadores y el párrafo con `marker(...)` y `ids.new(...)`.
  - `build_plantilla_v6.py` hereda esos conjuntos (`set(v4.BODY_PLACEHOLDERS)`).
  - En v5/v6, las filas del dispositivo (`14341833` Tipo, `15EF193E` Marca y modelo, `490B4C01` IMEI, `28E6822B`
    Línea, `2E1C810A` Titular) se convierten en una **ficha etiqueta/valor** con `data_card(ids, paras, rows)` y
    `split_row`. `split_row` exige que el primer run sea la etiqueta en negrita terminada en ":".
  - `v5.fingerprint` comprueba que título, intro y filas de `BLOCK_ELEMENTOS` sean **consecutivos** en el orden de la
    v4.
- **Configuración.** `Report:*` se lee en `ReportOptions`/`ReportSettings` (singleton, warnings al arrancar). En los
  compose de nube y Windows, las claves se mapean como `Seccion__Clave: ${FACTUM_<NOMBRE_ES>:-default}`, por ejemplo
  `Timestamping__TimeoutSeconds: ${FACTUM_SELLADO_TIMEOUT_SEGUNDOS:-10}`. `restaurar.ps1` tiene una lista fija de
  claves.
- **Config pública.** `PublicConfigResponse` (`DTOs/ConfigDtos.cs`) lo consume `usePublicConfig`. El test
  `TatanaMinVersionTests.PublicConfig_SinMinimo_EsNullYSiempreEsta` fija **el orden** de las propiedades.
- **Base de desarrollo.** No hay contenedor de Factum corriendo: el Mongo de `:27017` es `evidentia-v2-mongo-1`. El
  diseño no depende de datos existentes: un campo nuevo anulable y sin backfill.
- **Releases de Tatana.** El último tag es `tatana-v1.6.0`, que **no** incluye #78 (`case_seals_v1`). La próxima
  release (1.7.0) lleva las dos HU (§11.4).

### 3.1 Lecturas reales en el Samsung SM-M127F (Android 11, `R58RA3R5AGA`), solo lectura, 2026-10-14

Se corrieron con `subprocess.run([adb, "-s", serial, "shell", script])`, que es el mismo vector de argumentos que usará
Tatana. No hubo ninguna escritura en el dispositivo.

| Script (§4.2) | Salida | Tiempo |
|---|---|---|
| `PROPS` | 15 líneas `clave=valor` + `__FX_SEP__` + `uname -a` (fixture §13.1) | 404 ms |
| `SETTINGS` | `1` / `__FX_SEP__` / `1` | 205 ms |
| `WIFI` (rama `cmd wifi status`) | `Wifi is enabled` / `Wifi is not connected` | 123 ms |
| `WIFI` (rama `dumpsys`, forzada a mano) | `__FX_DUMPSYS__` / `Wi-Fi is enabled` / `state=DISCONNECTED ssid=<unknown ssid>` | ~190 ms |
| `date +%s.%N` ×3 | `1791991369.166298293` (toybox 0.8.3 **sí** soporta `%N`) | RTT 98–115 ms |

Hallazgos:

- **Saltos de línea.** Sin `\r` (shell v2 de adb). Igual, los parsers hacen `TrimEnd('\r')`, porque un adb o un
  equipo viejo puede usar `\r\n`.
- **Serie.** `ro.serialno` coincide acá con el identificador ADB. Con `uid=2000(shell)` se lee sin root.
- **Arranque.** `ro.boot.verifiedbootstate=orange`, `ro.boot.flash.locked=0` y `ro.boot.vbmeta.device_state=unlocked`:
  el bootloader de este equipo está desbloqueado. Es el valor crudo que el informe consigna sin calificar (D9).
- **SIM.** Sin SIM: `gsm.sim.state=ABSENT` y `gsm.operator.alpha` vacío, pero `gsm.operator.numeric=722310`, un
  remanente de red. Se consigna crudo.
- **Reloj.** El celular **adelanta ~10,04 s** respecto de la Mac, con un margen de 50 ms en la mejor muestra.
- **`adb devices -l`.** Informa `model:SM_M127F` (guion bajo), mientras que `getprop` da `SM-M127F`. No afecta a esta
  HU.
- **Wifi.** El equipo no estaba conectado a wifi: el formato "conectado" no se pudo capturar en vivo. Los fixtures de
  §13.1 marcados **sintéticos** siguen el formato de AOSP 11 (`WifiShellCommand`), que el implementador confirma si
  tiene un equipo conectado.
- **iOS.** No hay iPhone conectado. La forma de los valores de lockdown (`SerialNumber`, `BuildVersion`, `TimeZone`,
  `SIMStatus`, `CarrierBundleInfoArray`, `TimeIntervalSince1970`) sale de la API de pymobiledevice3 10.7.4, que sí se
  inspeccionó. Los fixtures de iOS son **sintéticos**, y la confirmación con un iPhone real queda en la prueba manual
  (§15.2, paso 9).

---

## 4. Lecturas en Tatana

### 4.1 Claves de la ficha (las mismas en Tatana, la API y la web)

Cada dato es un `DetailField` con **exactamente uno** de `value` (string) o `reason` (código de §4.4). Las claves y su
orden de presentación son fijos:

| # | Clave (`fields.<clave>`) | Android | iOS | Regla de validación (§4.5) |
|---|---|---|---|---|
| 1 | `serial_number` | `getprop ro.serialno` | `all_values["SerialNumber"]` | `^[A-Za-z0-9._:-]{1,64}$`; `unknown` → `empty` |
| 2 | `os_version` | `getprop ro.build.version.release` (texto, conserva "8.1.0") | `ProductVersion` | `^[0-9A-Za-z.]{1,16}$` |
| 3 | `os_api_level` | `getprop ro.build.version.sdk` | `not_applicable` | `^\d{1,3}$` |
| 4 | `build_id` | `getprop ro.build.id` | `BuildVersion` | `^[\x21-\x7E]{1,64}$` |
| 5 | `build_fingerprint` | `getprop ro.build.fingerprint` | `not_applicable` | `^[\x21-\x7E]{1,200}$` |
| 6 | `security_patch` | `getprop ro.build.version.security_patch` | `not_applicable` | `^\d{4}-\d{2}-\d{2}$` (fecha válida) |
| 7 | `kernel` | `uname -a` | `not_exposed` | `^[\x20-\x7E]{1,300}$` |
| 8 | `network_operator` | `getprop gsm.operator.alpha` | `not_exposed` | texto libre (§4.5), ≤ 64 |
| 9 | `network_operator_numeric` | `getprop gsm.operator.numeric` | `not_exposed` | `^[0-9,]{1,32}$` |
| 10 | `sim_operator` | `getprop gsm.sim.operator.alpha` | `CarrierBundleInfoArray[].CFBundleIdentifier` sin el prefijo `com.apple.`, unidos con `,` | texto libre, ≤ 64 |
| 11 | `sim_operator_numeric` | `getprop gsm.sim.operator.numeric` | `MCC`+`MNC` de cada entrada, unidos con `,` | `^[0-9,]{1,32}$` |
| 12 | `sim_state` | `getprop gsm.sim.state` | `SIMStatus` sin el prefijo `kCTSIMSupportSIMStatus` | `^[A-Za-z_,]{1,64}$` |
| 13 | `verified_boot_state` | `getprop ro.boot.verifiedbootstate` | `not_applicable` | `^[a-z]{1,16}$` |
| 14 | `bootloader_locked` | `getprop ro.boot.flash.locked` | `not_applicable` | `^[01]$` |
| 15 | `vbmeta_device_state` | `getprop ro.boot.vbmeta.device_state` | `not_applicable` | `^[a-z]{1,16}$` |
| 16 | `timezone` | `getprop persist.sys.timezone` | `TimeZone` | `^[A-Za-z0-9_+\-/]{1,64}$` |
| 17 | `auto_time` | `settings get global auto_time` | `not_exposed` | `^[01]$`; `null` → `empty` |
| 18 | `auto_time_zone` | `settings get global auto_time_zone` | `not_exposed` | `^[01]$`; `null` → `empty` |
| 19 | `wifi_state` | script `WIFI` (§4.2) | `not_exposed` | enum `connected` \| `disconnected` \| `disabled` |
| 20 | `wifi_ssid` | script `WIFI` | `not_exposed` | texto libre, ≤ 64; `<unknown ssid>` → `masked`; sin conexión → `not_connected` |

En doble SIM, Android devuelve los valores separados por coma (`READY,ABSENT`). Se guardan así, crudos.

**Nunca se leen ni se guardan** (D1, D3, D7): IMEI (sigue su flujo actual), IMSI, ICCID, BSSID, MAC, IP, número de
línea, redes guardadas, cuentas, ubicación ni contenido del dispositivo. En iOS, el helper **no reenvía**
`IntegratedCircuitCardIdentity`, `InternationalMobileSubscriberIdentity`, `PhoneNumber`, `WiFiAddress`,
`BluetoothAddress` ni `EthernetAddress`, aunque lockdown los exponga (§4.3).

### 4.2 Android: scripts exactos y timeouts

Todos los comandos son de lectura: `getprop`, `uname`, `settings get`, `cmd wifi status`, `dumpsys wifi | grep | sed`
y `date`. Ninguno escribe en el dispositivo. Cada script se ejecuta así:

```
ProcessRunner.RunArgumentListAsync(adbPath, ["-s", serial, "shell", SCRIPT], ct, timeout: T)
```

con el script como **un único elemento** de argv (patrón de `AndroidScreenProbe.Script`). Las constantes van como
`internal const string` en `AndroidDetailsReader`. Los tests fijan el texto exacto, verificado en el SM-M127F
(§3.1).

**`PROPS`** (timeout **5 s**). Trae las claves 1–16; la 7 sale de `uname -a` después del separador:

```sh
for p in ro.serialno ro.build.version.release ro.build.version.sdk ro.build.id ro.build.fingerprint ro.build.version.security_patch gsm.operator.alpha gsm.operator.numeric gsm.sim.operator.alpha gsm.sim.operator.numeric gsm.sim.state ro.boot.verifiedbootstate ro.boot.flash.locked ro.boot.vbmeta.device_state persist.sys.timezone; do echo "$p=$(getprop $p)"; done; echo __FX_SEP__; uname -a
```

Así viajan solo las 15 propiedades necesarias, no la salida completa de `getprop`.

Parseo (`ParseProps`):
- se separa en `__FX_SEP__`;
- en la primera parte, cada línea que empieza con `<clave conocida>=` toma el valor hasta el fin de la línea (split en
  el **primer** `=`);
- una clave que no aparece o con valor vacío da `empty`;
- la segunda parte, recortada, es `kernel`.

**`SETTINGS`** (timeout **5 s**). Claves 17–18:

```sh
settings get global auto_time; echo __FX_SEP__; settings get global auto_time_zone
```

`null` (el ajuste no está definido) da `empty`.

**`WIFI`** (timeout **5 s**). Claves 19–20:

```sh
s=$(cmd wifi status 2>/dev/null | grep -E '^Wifi is '); if [ -n "$s" ]; then echo "$s"; else echo __FX_DUMPSYS__; dumpsys wifi 2>/dev/null | grep -m1 -E '^Wi-Fi is '; dumpsys wifi 2>/dev/null | grep -m1 'mWifiInfo' | sed -E 's/^.*mWifiInfo SSID: (.*), BSSID: .*Supplicant state: ([A-Z_]+).*$/state=\2 ssid=\1/' | grep '^state='; fi
```

- `cmd wifi status` existe desde Android 11. En Android 10 o anterior el script cae a `dumpsys`.
- El `sed` + `grep '^state='` **en el celular** asegura que BSSID, MAC, IP y RSSI **nunca salen del dispositivo**. Si el
  formato no coincide, no sale nada y el resultado es `not_exposed`.

Parseo (`ParseWifi`, en este orden):
- `Wifi is disabled` → `wifi_state=disabled`, `wifi_ssid=not_connected`.
- `Wifi is connected to <x>` → `connected`; el SSID es `<x>` sin las comillas dobles de los extremos. `<unknown ssid>` o
  vacío → `masked`.
- `Wifi is not connected` → `disconnected`, `wifi_ssid=not_connected`. Solo `Wifi is enabled`, sin otra línea, se
  trata igual.
- Rama `__FX_DUMPSYS__`:
  - `Wi-Fi is disabled` → `disabled`;
  - `state=COMPLETED ssid=<x>` → `connected` (SSID como arriba);
  - cualquier otro `state=` → `disconnected`;
  - sin línea `state=` → los dos campos `not_exposed`.
- Salida vacía sin error → los dos campos `not_exposed`.

**`DATE`** (timeout **3 s** por muestra, **3 muestras** seguidas). Medición del reloj:

```sh
date +%s.%N
```

Antes y después de cada muestra se toma `TimeProvider.GetUtcNow()` de la PC (§5.1).

**Presencia** (timeout **3 s**), antes de todo lo anterior: `adb -s <serial> get-state`. Si el stdout recortado no es
`device`, el endpoint responde 404 `device_not_found` y no corre nada más.

**Orden y presupuesto.** Primero `get-state`, después las 3 muestras de `DATE` (la medición queda lo más cerca posible
del instante de la ficha) y después `PROPS`, `SETTINGS` y `WIFI`, en ese orden y secuenciales. Un script que falla no
aborta los demás. En el SM-M127F el total es de ~1,1 s; el peor caso por timeouts es 3 + 9 + 15 = **27 s**. El cliente
espera hasta **45 s** (§10.1).

**Errores por script** (se aplican a **todos** los campos que llena ese script):

| Resultado de `ProcessRunner` | `reason` |
|---|---|
| `ExitCode == TimeoutExitCode` (-2) | `timeout` |
| `ExitCode == -1` (falta adb) o `!= 0` con stdout vacío | `error` |
| `!= 0` **con** stdout | se parsea igual (un `grep` sin coincidencia en la última etapa da 1) |

### 4.3 iOS: subcomando `details` del helper

`ios_helper.py details --udid U`. Pasa el mismo `main`/`HANDLERS` que el resto. Timeout en Tatana: **30 s**
(`DetailsTimeout`).

```python
DETAIL_KEYS = ("SerialNumber", "ProductVersion", "BuildVersion", "TimeZone", "SIMStatus")

async def cmd_details(args):
    lockdown = await _open_lockdown(args.udid)
    try:
        values = lockdown.all_values or {}
        out = {k: str(values[k]) for k in DETAIL_KEYS if values.get(k) is not None}
        carriers = []
        for c in (values.get("CarrierBundleInfoArray") or [])[:4]:
            if isinstance(c, dict):
                # SOLO estas tres claves: nunca ICCID, IMSI, GID1/GID2 ni el número de línea.
                carriers.append({"bundle": str(c.get("CFBundleIdentifier") or ""),
                                 "mcc": str(c.get("MCC") or ""), "mnc": str(c.get("MNC") or "")})
        samples, clock_error = [], None
        try:
            for _ in range(3):
                before = time.time_ns()
                device = await lockdown.get_value(key="TimeIntervalSince1970")
                after = time.time_ns()
                if isinstance(device, (int, float)):
                    samples.append({"pc_before_ns": before, "device": device, "pc_after_ns": after})
        except Exception as exc:  # noqa: BLE001
            clock_error = _detail(exc)
        _stdout_json({"udid": args.udid, "values": out, "carriers": carriers,
                      "clock": samples, "clock_error": clock_error})
    finally:
        await _close_quietly(lockdown)
    return EXIT_OK
```

- Es **solo lectura**: `GetValue` de lockdown no cambia nada en el iPhone. No necesita Modo Desarrollador ni DDI
  (no se llama a `EnsurePreparedAsync`).
- Se suma `"details"` al `_parser()` (con `--udid` obligatorio), a `HANDLERS` y al docstring del módulo. También al
  test `IosHelperTests.El_helper_es_un_recurso_embebido…`, que lista los subcomandos.
- Un error de lockdown (sin "Confiar", dispositivo no encontrado) sale por `TATANA_ERROR` como siempre. Tatana lo
  transforma en `IosException` y el endpoint responde con su código (§7.2).

Parseo en Tatana (`IosDetailsParser.Parse(stdout)`, puro):
- **Valores de lockdown:** `values` se mapea según la tabla de §4.1. Una clave ausente da `empty`.
- **`sim_operator`:** une los `bundle` no vacíos sin el prefijo `com.apple.` (comparación ordinal). Si no hay ninguno,
  da `empty`.
- **`sim_operator_numeric`:** une `mcc+mnc` de cada entrada donde los dos son dígitos.
- **`sim_state`:** se quita el prefijo `kCTSIMSupportSIMStatus` si está (`Ready`, `NotInserted`, `PINLocked`…).
- **Reloj:** `clock` sale de las muestras (§5.1). Se usa `JsonElement.GetDecimal()` para `device`, **no** `double`, y
  `pc_*_ns` va a ticks con `/ 100`.
- **Sin muestras:** `clock_error` no nulo, o una lista vacía sin error, da `clock.reason = error`. Si las muestras
  vinieron pero ninguna es válida, da `invalid`.
- **Claves sin lectura en iOS:** `os_api_level`, `build_fingerprint`, `security_patch` y las tres de arranque van con
  `not_applicable`. `kernel`, `network_operator`, `network_operator_numeric`, `auto_time`, `auto_time_zone`,
  `wifi_state` y `wifi_ssid` van con `not_exposed`.

> **A verificar con un iPhone real** (lo hace el implementador si tiene uno, y si no, la prueba manual §15.2 paso 9):
> que `TimeIntervalSince1970` traiga fracción de segundo, y la forma de `SIMStatus` y de `CarrierBundleInfoArray`. El
> parser tolera la ausencia de cualquiera de las tres.

### 4.4 Motivos de "No disponible" (`reason`)

| Código | Cuándo | Texto (web e informe) |
|---|---|---|
| `empty` | El comando respondió, pero sin valor (o con `unknown`/`null`) | "el dispositivo no devolvió el dato (puede estar restringido por el sistema operativo)" |
| `not_exposed` | La plataforma no lo informa por una vía de solo lectura | "el sistema operativo no lo informa por una consulta de solo lectura" |
| `not_applicable` | No existe en esa plataforma | "no aplica a esta plataforma" |
| `not_connected` | `wifi_ssid` sin conexión wifi | "sin conexión wifi" |
| `masked` | SSID oculto por el sistema (`<unknown ssid>`) | "el sistema no informó el nombre de la red" |
| `timeout` | El script o el helper no terminó a tiempo | "el dispositivo no respondió a tiempo" |
| `error` | Falló la ejecución (adb ausente, salida no parseable, error de lockdown) | "falló la lectura" |
| `invalid` | El valor no cumple la regla de §4.5 y se descartó | "el valor devuelto no tiene un formato válido y se descartó" |

**"Cantidad de datos no disponibles"** (eventos y web) = campos con `reason` **distinto de** `not_applicable` y
`not_connected`.

### 4.5 Validación y saneamiento de lo que devuelve el dispositivo

El valor lo controla el dispositivo (un SSID o un `gsm.operator.alpha` pueden traer cualquier cosa), así que se sanea
**dos veces**: en Tatana, antes de responder, y en la API, antes de guardar. La regla es la misma de los dos lados
(`DetailRules` en Tatana y `DeviceDetailsRules` en la API) y se prueba con la misma tabla de casos.

1. **Limpieza** (equivalente a `SafeText.Clean(v, TextShape.SingleLine)` de la API):
   - se quitan los C0 (los saltos y el tab pasan a espacio), los bidi U+202A–U+202E y U+2066–U+2069, los no-caracteres
     y los surrogates sueltos;
   - después, `Trim()` y colapso de espacios repetidos.

   Tatana no referencia la API: replica la función en `DetailRules.Clean`, con su test. En la API se usa `SafeText`.
2. Si queda vacío → `empty`.
3. Si no cumple la regex de §4.1 → `invalid`. El valor descartado **no se guarda en ninguna parte** ni se loguea.
4. **Texto libre** (`network_operator`, `sim_operator`, `wifi_ssid`): después de limpiar, como máximo 64 elementos de
   texto (`StringInfo`); más largo → `invalid`. No se recorta: un recorte silencioso cambiaría el dato.
5. `security_patch` además tiene que parsear como fecha (`DateOnly.TryParseExact("yyyy-MM-dd")`).
6. **En la API:**
   - un `reason` fuera del enum de §4.4 se convierte en `error`;
   - una clave fuera de las 20 de §4.1 se ignora;
   - una clave que falta llega como `error`;
   - un campo con `value` **y** `reason` conserva solo el `reason`.

   Ninguno de estos casos devuelve 400: la ficha es documental y el dato malo queda marcado.
7. **El informe** recibe los valores ya saneados, y además pasan por `ReportValues.Clean` (B-R6). Un SSID con `{x}` no
   se reinterpreta como placeholder, porque los valores no se vuelven a escanear (regla existente de B-R6).

### 4.6 Logs

Tatana loguea solo `Ficha técnica leída ({Platform}, {Unavailable} no disponibles, {Ms} ms)`. **Nunca** loguea
valores, SSID, salidas crudas ni serial. La API loguea lo mismo, sin valores. Ninguna excepción incluye la salida del
dispositivo en su mensaje.

### 4.7 Modo mock

Con `Agent:Mock=true`, el servicio devuelve **después de 300 ms** los valores del fixture del SM-M127F (§13.1),
`connection_id` = el serial pedido, `wifi_state=connected`, `wifi_ssid="Red de prueba"` y un reloj con
`device_minus_pc_ms = MockDeviceClockSkewSeconds × 1000` (opción nueva `AgentOptions.MockDeviceClockSkewSeconds`,
`int`, default 0) y `margin_ms = 5`. Con esto el usuario prueba el aviso de umbral sin celular. Con `platform=ios`, los
valores de iOS del fixture sintético.

---

## 5. Medición de reloj

### 5.1 Dispositivo ↔ PC (lo mide Tatana)

Convención de signo: **`device_minus_pc_ms` = reloj del dispositivo − reloj de la PC**. Positivo = el dispositivo
**adelanta**; negativo = **atrasa**.

Para cada muestra *i* (3 por medición):

1. `b_i` = `TimeProvider.GetUtcNow()` justo antes de lanzar `date` (iOS: `pc_before_ns` del helper, que corre en la
   misma PC y con el mismo reloj del sistema).
2. `d_i` = hora del dispositivo.
   - Android: `^\s*(\d{9,11})(?:\.(\d{1,9}))?\s*$` sobre el stdout recortado. Con fracción de 1 a 9 dígitos,
     `resolution_ms = 1`; los segundos más los primeros 7 dígitos de la fracción (completados con ceros) van a ticks.
     Sin fracción, o con `%N` literal (`1791991369.%N`, `1791991369.N`), se toman solo los segundos y
     `resolution_ms = 1000`.
   - iOS: `device` como `decimal`. Si tiene parte fraccionaria, `resolution_ms = 1`; si no, `1000`.
3. `a_i` = `GetUtcNow()` justo después de que el proceso terminó.

**Muestra válida:** `a_i ≥ b_i` y `a_i − b_i ≤ 5 s`. Además, `d_i` está a menos de 50 años de `b_i` (descarta basura
como `0`).

Se elige la muestra válida con **menor RTT** (`a_i − b_i`) y, en un empate, la primera. Todo se calcula en **ticks**
(`long`):

```
mid_ticks            = b + (a − b) / 2
device_minus_pc_ms   = round((d − mid_ticks) / 10_000.0, MidpointRounding.AwayFromZero)
margin_ms            = ceil((a − b) / 2 / 10_000.0 + resolution_ms)
```

La hora del dispositivo se tomó en algún instante dentro de [b, a]. Por eso el error del punto medio está acotado por
RTT/2, más la resolución del reloj leído. La cota es estricta (no estadística) y no depende de que la latencia sea
simétrica.

**Resultado** (`clock`):
- con muestra: `pc_before = b`, `pc_after = a`, `device_time = d`, `resolution_ms`, `device_minus_pc_ms`,
  `margin_ms`, `samples` (cantidad de muestras válidas) y sin `reason`;
- sin ninguna válida: solo `samples: 0` y un `reason`:
  - `timeout` si todas vencieron;
  - `invalid` si respondieron pero no parsearon o fallaron las cotas;
  - `error` en cualquier otro caso.

**Ejemplo real** (fixture §13.1, SM-M127F):
- se elige la muestra 2, con RTT 97,964 ms;
- `device_minus_pc_ms = 10038` y `margin_ms = 50`;
- en pantalla: "Adelanta 10 s respecto de esta PC (± 1 s)".

### 5.2 PC ↔ servidor (lo estima el backend, D5)

Es un sondeo NTP de una vuelta, que hace el navegador justo antes de mandar la ficha o la medición de cierre:

1. `pc_before = new Date()`.
2. `GET /api/time` → `{ "server_time": "<ISO UTC>" }` (§7.1).
3. `pc_after = new Date()`.
4. Se manda `pc_server_probe: { pc_before, server_time, pc_after }` (ISO, ms) dentro del PUT.

El backend valida y calcula (`DeviceDetailsRules.ComputePcServer`):
- `pc_after ≥ pc_before` y `pc_after − pc_before ≤ 10 s`;
- `now − 300 s ≤ server_time ≤ now + 1 s` (el sondeo es reciente y lo emitió este servidor);
- `pc_minus_server_ms = round(mid(pc) − server_time)` y `pc_server_margin_ms = ceil(RTT/2) + 1` (1 ms de resolución
  de la hora del servidor).

Si no pasa la validación → `pc_server_reason = invalid`. Si el sondeo no vino → `pc_server_reason = not_measured`.

**Supuesto, el mismo reloj:** el navegador y Tatana corren en la misma PC (`AGENT_URL` en loopback, que es el caso
normal). Si `AGENT_URL` no es loopback (Tatana expuesto en la red), el navegador **no manda** el sondeo y queda
`not_measured`, porque compararía otro reloj (§10.1, `isLoopbackAgent`).

**Riesgo aceptado:** `server_time` vuelve desde el cliente y podría manipularse. Lo puede hacer solo el propio perito,
y no le da nada que no pueda lograr cambiando la hora de su PC. Queda escrito en el README.

### 5.3 Presentación (la misma regla en web e informe)

- `N = round(|device_minus_pc_ms| / 1000)` (half away from zero) y `M = max(1, ceil(margin_ms / 1000))`.
- **Formato de duración** (`formatDuration(segundos)`): partes `d`, `h`, `min`, `s`, sin las partes en cero; con días,
  sin segundos.

  | Segundos | Texto |
  |---|---|
  | 13 | `13 s` |
  | 60 | `1 min` |
  | 133 | `2 min 13 s` |
  | 3600 | `1 h` |
  | 3725 | `1 h 2 min 5 s` |
  | 90061 | `1 d 1 h 1 min` |
- **Web** (presente, "esta PC"):
  - `N = 0` → "Coincide con esta PC (± M s)";
  - si no, "Adelanta {dur(N)} respecto de esta PC (± {dur(M)})" o "Atrasa …".
- **Informe** (pasado, "estación de trabajo"):
  - "el dispositivo coincidía con la estación de trabajo (± M s)";
  - "el dispositivo adelantaba/atrasaba {dur} respecto de la estación de trabajo (± M s)".
- **PC↔servidor:** la misma regla con `pc_minus_server_ms`. Web: "Esta PC adelanta/atrasa … respecto del servidor".
  Informe: "la estación de trabajo adelantaba/atrasaba … respecto del servidor de Factum".
- **Umbral:**
  - se supera con `|device_minus_pc_ms| > umbral × 1000`, estrictamente (D6);
  - para PC↔servidor, con `|pc_minus_server_ms| > umbral × 1000`;
  - el umbral es `Report:ClockSkewWarningSeconds`; la web lo lee de `clock_skew_warning_seconds` (§7.1).

---

## 6. Modelo de datos (API, Mongo)

### 6.1 `Case.DeviceDetails` (`Models/Case.cs`)

```csharp
/// <summary>
/// metadatos-dispositivo: ficha técnica leída por Tatana (D1). null = caso anterior a esta HU, alta con un Tatana sin
/// device_details_v1, o lectura todavía no registrada. JSON: <c>device_details</c>.
/// </summary>
[BsonIgnoreIfNull]
public DeviceDetails? DeviceDetails { get; set; }
```

### 6.2 `Models/DeviceDetails.cs` (nuevo)

```csharp
[BsonIgnoreExtraElements]
public sealed class DeviceDetails
{
    public DateTime ReadAt { get; set; }                       // read_at: reloj del SERVIDOR al recibir la ficha (UTC)
    public string Platform { get; set; } = "android";          // platform: "android" | "ios"
    public string ConnectionId { get; set; } = "";             // connection_id: serial ADB o UDID (== Case.Device.Serial)
    public string AgentVersion { get; set; } = "";             // agent_version: versión de Tatana que leyó
    public Dictionary<string, DeviceDetailField> Fields { get; set; } = new();  // fields: las 20 claves de §4.1
    [BsonIgnoreIfNull] public ClockMeasurement? ClockStart { get; set; }        // clock_start
    [BsonIgnoreIfNull] public ClockMeasurement? ClockClose { get; set; }        // clock_close
    public bool IncludeSsidInReport { get; set; }              // include_ssid_in_report (D7, default false)
}

[BsonIgnoreExtraElements]
public sealed class DeviceDetailField
{
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Value { get; set; }
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Reason { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class ClockMeasurement
{
    public string Kind { get; set; } = ClockKinds.Start;       // kind: "start" | "close"
    public DateTime RecordedAt { get; set; }                   // recorded_at: reloj del servidor al recibirla (UTC)
    // Dispositivo ↔ PC (§5.1). Si no hubo hora: Reason y el resto en null.
    [BsonIgnoreIfNull] public string? Reason { get; set; }                 // reason (timeout|error|invalid|not_exposed)
    [BsonIgnoreIfNull] public DateTime? PcBefore { get; set; }             // pc_before (UTC, reloj de la PC)
    [BsonIgnoreIfNull] public DateTime? PcAfter { get; set; }              // pc_after
    [BsonIgnoreIfNull] public DateTime? DeviceTime { get; set; }           // device_time (reloj del dispositivo)
    [BsonIgnoreIfNull] public int? ResolutionMs { get; set; }              // resolution_ms (1 | 1000)
    [BsonIgnoreIfNull] public long? DeviceMinusPcMs { get; set; }          // device_minus_pc_ms (recalculado por la API)
    [BsonIgnoreIfNull] public long? MarginMs { get; set; }                 // margin_ms (recalculado por la API)
    // PC ↔ servidor (§5.2).
    [BsonIgnoreIfNull] public long? PcMinusServerMs { get; set; }          // pc_minus_server_ms
    [BsonIgnoreIfNull] public long? PcServerMarginMs { get; set; }         // pc_server_margin_ms
    [BsonIgnoreIfNull] public string? PcServerReason { get; set; }         // pc_server_reason (not_measured|invalid)
}

public static class ClockKinds { public const string Start = "start", Close = "close"; }

/// <summary>Las 20 claves de la ficha (§4.1), en orden de presentación. Literales snake_case.</summary>
public static class DeviceDetailKeys
{
    public const string SerialNumber = "serial_number", OsVersion = "os_version", OsApiLevel = "os_api_level",
        BuildId = "build_id", BuildFingerprint = "build_fingerprint", SecurityPatch = "security_patch",
        Kernel = "kernel", NetworkOperator = "network_operator", NetworkOperatorNumeric = "network_operator_numeric",
        SimOperator = "sim_operator", SimOperatorNumeric = "sim_operator_numeric", SimState = "sim_state",
        VerifiedBootState = "verified_boot_state", BootloaderLocked = "bootloader_locked",
        VbmetaDeviceState = "vbmeta_device_state", Timezone = "timezone", AutoTime = "auto_time",
        AutoTimeZone = "auto_time_zone", WifiState = "wifi_state", WifiSsid = "wifi_ssid";
    public static readonly string[] All = [ /* las 20, en este orden */ ];
}

public static class DeviceDetailReasons { /* empty, not_exposed, not_applicable, not_connected, masked, timeout, error, invalid */ }
```

- `Fields` se guarda como subdocumento con claves snake_case. Se serializa al JSON con esas mismas claves, porque el
  diccionario no pasa por la política de nombres. Siempre lleva las 20 claves.
- **Compatibilidad:**
  - un documento viejo no tiene `DeviceDetails` y deserializa `null`;
  - no hay backfill;
  - `[BsonIgnoreExtraElements]` en las tres clases tolera un rollback del binario.
- **Índices:** ninguno nuevo.
- **Listado:** `ListByOfficerAsync` **no** proyecta `DeviceDetails` afuera (son ~1,5 KB por caso). El modal de detalle
  (`CaseDetailModal`) usa el objeto del listado y necesita la ficha (D-T15).

### 6.3 `CaseEvent` (`Models/CaseEvent.cs`)

`CaseEventTypes` suma:

```csharp
/// <summary>metadatos-dispositivo: ficha técnica registrada (alta, retoma o "Volver a leer"). Sin SSID ni valores.</summary>
public const string DeviceDetailsRecorded = "device_details_recorded";
/// <summary>metadatos-dispositivo: medición de reloj al generar (clock_kind = "close").</summary>
public const string DeviceClockMeasured = "device_clock_measured";
```

`CaseEventDetail` suma estos campos, todos con `[BsonIgnoreIfNull, JsonIgnore(WhenWritingNull)]`:

| C# | JSON | Tipo | Eventos |
|---|---|---|---|
| `UnavailableCount` | `unavailable_count` | `int?` | `device_details_recorded` (§4.4) |
| `Reread` | `reread` | `bool?` | `device_details_recorded`: `true` si ya había ficha |
| `ClockKind` | `clock_kind` | `string?` | los dos (`start` \| `close`) |
| `DeviceMinusPcMs` | `device_minus_pc_ms` | `long?` | los dos (null si no hubo hora) |
| `ClockMarginMs` | `clock_margin_ms` | `long?` | los dos |
| `PcMinusServerMs` | `pc_minus_server_ms` | `long?` | los dos (null si no se midió) |

Se reutiliza `Reason` (string) para el motivo del reloj cuando no hubo hora. **El evento no lleva SSID, ni valores de
la ficha, ni números de línea** (HU, trazabilidad).

---

## 7. Endpoints

### 7.1 API (`Factum.Backend`), todos con `[Authorize]`

| Método y ruta | Entrada | Respuesta OK | Errores |
|---|---|---|---|
| `GET /api/time` (nuevo `TimeController`) | — | `200 { "server_time": "2026-10-14T15:22:39.1234567Z" }` (`DateTime.UtcNow`) | — |
| `PUT /api/cases/{id}/device-details` | `SaveDeviceDetailsRequest` | `200 Case` (completo, como `GET /api/cases/{id}`) | 404/403 (dueño, como siempre); 409 `NotEditableMessage` con `code: "case_not_editable"`; 409 `code: "device_mismatch"`; 400 `code: "invalid_device_details"` |
| `PUT /api/cases/{id}/device-details/clock` | `SaveDeviceClockRequest` | `200 Case` | ídem + 409 `code: "no_device_details"` |
| `PUT /api/cases/{id}/device-details/options` | `DeviceDetailsOptionsRequest` | `200 Case` | 404/403, 409 `case_not_editable`, 409 `no_device_details`, 400 `invalid_device_details` (`include_ssid_in_report` ausente) |
| `GET /api/config/public` (existente) | — | suma `clock_skew_warning_seconds: int` **al final** | — |

Los errores van con `Result.Fail(kind, mensaje, new Dictionary<string, object?> { ["code"] = … })`. El cuerpo sale
como `{ error, code }`, igual que las demás rutas con código.

Mensajes (en castellano, el web los puede mostrar):
- `device_mismatch`: "El dispositivo conectado no es el del caso."
- `no_device_details`: "El caso no tiene ficha técnica registrada."
- `invalid_device_details`: "Datos técnicos del dispositivo inválidos."

**`PUT …/device-details`** (`CaseService.SaveDeviceDetailsAsync`):
1. `LoadOwnedAsync`; `IsEditable(cas)`, o 409 `case_not_editable`.
2. `platform` ∈ {`android`, `ios`} y `connection_id` no vacío, o 400 `invalid_device_details`.
3. `SafeText.Clean(connection_id).Trim()` tiene que ser **igual** (ordinal) a `cas.Device.Serial`, o 409
   `device_mismatch` (D10: "mismo identificador").
4. `fields = DeviceDetailsRules.NormalizeFields(request.Fields)` (§4.5).
5. `ClockStart` con `Kind = start`, `RecordedAt = now`, `ComputeClock(request.Clock)` (§5.1, **recalculado** por la API
   desde `pc_before`/`pc_after`/`device_time`/`resolution_ms`) y `ComputePcServer(request.PcServerProbe, now)` (§5.2).
6. `IncludeSsidInReport = request.IncludeSsidInReport ?? cas.DeviceDetails?.IncludeSsidInReport ?? false`.
7. `ClockClose = cas.DeviceDetails?.ClockClose` (se conserva: la próxima generación la pisa o la filtra, D-T7).
8. `AgentVersion` = `SafeText.Clean(...)`, como máximo 32 caracteres (si es más largo, `""`).
9. `ReadAt = now`.
10. `_repo.SetDeviceDetailsAsync(id, details)` con el filtro `Editable(id)`. Si no hubo match → 409
    `case_not_editable`.
11. Evento `device_details_recorded` (best-effort) con `unavailable_count`, `reread = (cas.DeviceDetails != null)` (o
    null si es la primera), `clock_kind = start` y los ms.
12. Devuelve el caso recargado (`FindByIdAsync` + `ResolveStorage`).

**`PUT …/device-details/clock`** (`SaveDeviceClockAsync`):
- los mismos chequeos 1 y 3;
- `cas.DeviceDetails` no nulo, o 409 `no_device_details`;
- arma `ClockMeasurement { Kind = close, … }` y lo escribe con `_repo.SetClockCloseAsync(id, m)`, con el filtro
  `Editable(id) & Exists(DeviceDetails)`;
- evento `device_clock_measured`.

**`PUT …/device-details/options`** (`SetDeviceDetailsOptionsAsync`): los chequeos 1, `no_device_details` y
`_repo.SetIncludeSsidAsync(id, bool)` con el mismo filtro. **No** registra evento (D7: el SSID no va a la
trazabilidad).

**Generación (filtro de la medición de cierre, D-T7):**
- En `FinishGenerationAsync`, justo antes de `_reports.GenerateReportAsync(fresh, …)`: si
  `fresh.DeviceDetails?.ClockClose is { } c && c.RecordedAt < pending.CreatedAt`, entonces
  `fresh.DeviceDetails.ClockClose = null` (**solo en memoria**). Una medición de cierre de un intento anterior no
  entra al informe.
- En el flujo viejo (`CaseService.GenerateAsync`), `cas.DeviceDetails.ClockClose = null` en memoria antes de
  `_reports.GenerateAsync`.

### 7.2 Tatana (`Factum.Agent`), API local

Nuevo `Controllers/DeviceDetailsController.cs`. Los `GET` no los bloquea el modo mantenimiento ni los cuenta
`OperationTracker` (regla existente). Tatana no agrega ningún mensaje WebSocket nuevo: `devices_changed` queda
igual.

| Método y ruta | Query | Respuesta OK | Errores |
|---|---|---|---|
| `GET /devices/{serial}/details` | `platform=android\|ios` (default `android`) | `200 DeviceDetailsResponse` | 404 `{ error, code: "device_not_found" }`; iOS: `{ error, code }` con el código de `IosException` (404 si es `ios_device_not_found`, 500 en otro caso); 500 `{ error }` inesperado |
| `GET /devices/{serial}/clock` | ídem | `200 DeviceClockResponse` | ídem |

- Para iOS se llama a `EnsureAppleServiceAsync` primero. Sin el servicio de Apple, 500 `ios_apple_service_missing`.
- **Concurrencia:** un `SemaphoreSlim(1,1)` por serial (`ConcurrentDictionary`) en `DeviceDetailsService`. Dos
  pestañas que piden la ficha del mismo celular se serializan y no se pisan los `date`.
- **Capability nueva:** `device_details_v1` al final de `HealthController.Capabilities`, con su comentario al estilo de
  los anteriores.

---

## 8. Contrato compartido

### 8.1 Tatana → web (JSON snake_case_lower, `WhenWritingNull`: los `null` no viajan)

`GET /devices/{serial}/details` → `DeviceDetailsResponse`:

```json
{
  "serial": "R58RA3R5AGA",
  "platform": "android",
  "agent_version": "1.7.0",
  "read_started_at": "2026-10-14T15:22:38.9600000+00:00",
  "read_finished_at": "2026-10-14T15:22:40.0700000+00:00",
  "fields": {
    "serial_number": { "value": "R58RA3R5AGA" },
    "os_version": { "value": "11" },
    "os_api_level": { "value": "30" },
    "build_id": { "value": "RP1A.200720.012" },
    "build_fingerprint": { "value": "samsung/a12sub/a12s:11/RP1A.200720.012/A127MUBU3AUJ5:user/release-keys" },
    "security_patch": { "value": "2021-10-01" },
    "kernel": { "value": "Linux localhost 4.19.111-22565658 #1 SMP PREEMPT Thu Nov 11 01:08:09 KST 2021 aarch64" },
    "network_operator": { "reason": "empty" },
    "network_operator_numeric": { "value": "722310" },
    "sim_operator": { "reason": "empty" },
    "sim_operator_numeric": { "reason": "empty" },
    "sim_state": { "value": "ABSENT" },
    "verified_boot_state": { "value": "orange" },
    "bootloader_locked": { "value": "0" },
    "vbmeta_device_state": { "value": "unlocked" },
    "timezone": { "value": "America/Argentina/Buenos_Aires" },
    "auto_time": { "value": "1" },
    "auto_time_zone": { "value": "1" },
    "wifi_state": { "value": "disconnected" },
    "wifi_ssid": { "reason": "not_connected" }
  },
  "clock": {
    "pc_before": "2026-10-14T15:22:39.0791760+00:00",
    "pc_after": "2026-10-14T15:22:39.1771400+00:00",
    "device_time": "2026-10-14T15:22:49.1662982+00:00",
    "resolution_ms": 1,
    "device_minus_pc_ms": 10038,
    "margin_ms": 50,
    "samples": 3
  }
}
```

Sin hora de reloj: `"clock": { "samples": 0, "reason": "timeout" }`.

`GET /devices/{serial}/clock` → `DeviceClockResponse`: `{ "serial", "platform", "agent_version", "clock": { …igual… } }`.

| Lado | Archivo |
|---|---|
| Tatana | `server/src/Factum.Agent/Models/DeviceDetailsModels.cs`: `DetailField(string? Value, string? Reason)`, `DeviceClockReading(DateTimeOffset? PcBefore, DateTimeOffset? PcAfter, DateTimeOffset? DeviceTime, int? ResolutionMs, long? DeviceMinusPcMs, long? MarginMs, int Samples, string? Reason)`, `DeviceDetailsResponse(string Serial, string Platform, string AgentVersion, DateTimeOffset ReadStartedAt, DateTimeOffset ReadFinishedAt, IReadOnlyDictionary<string, DetailField> Fields, DeviceClockReading Clock)`, `DeviceClockResponse(string Serial, string Platform, string AgentVersion, DeviceClockReading Clock)`, `DetailKeys`, `DetailReasons` |
| Web | `client/src/lib/agent.ts`: `AgentDetailField`, `AgentDeviceClock`, `AgentDeviceDetails`, `AgentDeviceClockResponse`; `agent.supportsDeviceDetails()`, `agent.getDeviceDetails(serial, platform, signal?)`, `agent.getDeviceClock(serial, platform, signal?)` |

```ts
export interface AgentDetailField { value?: string; reason?: DeviceDetailReason }
export interface AgentDeviceClock {
  pc_before?: string; pc_after?: string; device_time?: string;   // ISO con 7 decimales: el web NO los parsea
  resolution_ms?: number; device_minus_pc_ms?: number; margin_ms?: number;
  samples: number; reason?: ClockReason;
}
export interface AgentDeviceDetails {
  serial: string; platform: "android" | "ios"; agent_version: string;
  read_started_at: string; read_finished_at: string;
  fields: Record<DeviceDetailKey, AgentDetailField>;
  clock: AgentDeviceClock;
}
export interface AgentDeviceClockResponse { serial: string; platform: "android" | "ios"; agent_version: string; clock: AgentDeviceClock }
```

**Regla de transporte:** el web reenvía `pc_before`, `pc_after` y `device_time` **como strings, tal como vinieron**.
Si los pasa por `Date`, se truncan a ms y la API recalcularía otro valor.

### 8.2 Web → API (snake_case_lower)

`PUT /api/cases/{id}/device-details`:

```json
{
  "platform": "android",
  "connection_id": "R58RA3R5AGA",
  "agent_version": "1.7.0",
  "fields": { "serial_number": { "value": "R58RA3R5AGA" }, "...": {} },
  "clock": { "pc_before": "…", "pc_after": "…", "device_time": "…", "resolution_ms": 1, "reason": null },
  "pc_server_probe": { "pc_before": "2026-10-14T15:22:41.010Z", "server_time": "2026-10-14T15:22:41.080Z", "pc_after": "2026-10-14T15:22:41.150Z" },
  "include_ssid_in_report": false
}
```

- `clock` puede traer además `device_minus_pc_ms`, `margin_ms` y `samples`: la API **los ignora** y recalcula.
- `pc_server_probe` es opcional (si no viene, `not_measured`).
- `include_ssid_in_report` es opcional (si no viene, se conserva el valor guardado o queda en `false`).

`PUT /api/cases/{id}/device-details/clock`:
`{ "connection_id", "agent_version", "clock": {…}, "pc_server_probe": {…} }`.

`PUT /api/cases/{id}/device-details/options`: `{ "include_ssid_in_report": true }`.

| Lado | Archivo |
|---|---|
| API | `server/src/Factum.Backend/DTOs/DeviceDetailsDtos.cs`: `DeviceDetailFieldDto(string? Value = null, string? Reason = null)`, `DeviceClockDto(DateTimeOffset? PcBefore = null, DateTimeOffset? PcAfter = null, DateTimeOffset? DeviceTime = null, int? ResolutionMs = null, string? Reason = null)`, `PcServerProbeDto(DateTimeOffset? PcBefore = null, DateTimeOffset? ServerTime = null, DateTimeOffset? PcAfter = null)`, `SaveDeviceDetailsRequest(string? Platform = null, string? ConnectionId = null, string? AgentVersion = null, Dictionary<string, DeviceDetailFieldDto>? Fields = null, DeviceClockDto? Clock = null, PcServerProbeDto? PcServerProbe = null, bool? IncludeSsidInReport = null)`, `SaveDeviceClockRequest(string? ConnectionId = null, string? AgentVersion = null, DeviceClockDto? Clock = null, PcServerProbeDto? PcServerProbe = null)`, `DeviceDetailsOptionsRequest(bool? IncludeSsidInReport = null)`, `ServerTimeResponse(DateTime ServerTime)`. Todo anulable y sin `[Required]` (convención de `CaseDtos.cs`). |
| Web | `client/src/lib/api.ts`: `SaveDeviceDetailsRequest`, `SaveDeviceClockRequest`, `PcServerProbe`; `api.getServerTime()`, `api.saveDeviceDetails(caseId, body)`, `api.saveDeviceClock(caseId, body)`, `api.setDeviceDetailsOptions(caseId, { include_ssid_in_report })` |

### 8.3 API → web: `Case.device_details`

```json
"device_details": {
  "read_at": "2026-10-14T15:22:41.0921234Z",
  "platform": "android",
  "connection_id": "R58RA3R5AGA",
  "agent_version": "1.7.0",
  "fields": { "serial_number": { "value": "R58RA3R5AGA" }, "wifi_ssid": { "reason": "not_connected" }, "…": {} },
  "clock_start": {
    "kind": "start", "recorded_at": "2026-10-14T15:22:41.0921234Z",
    "pc_before": "…", "pc_after": "…", "device_time": "…", "resolution_ms": 1,
    "device_minus_pc_ms": 10038, "margin_ms": 50,
    "pc_minus_server_ms": -120, "pc_server_margin_ms": 71
  },
  "clock_close": null,
  "include_ssid_in_report": false
}
```

- En la API `Case`, `clock_start`/`clock_close` llevan solo `[BsonIgnoreIfNull]`, así que en el JSON un `null` **sí
  viaja**: la API no usa `WhenWritingNull` global.
- Los campos `reason`, `pc_server_reason` y similares, cuando no aplican, salen `null` o ausentes. El web trata los dos
  casos igual.
- **El caso viejo trae `"device_details": null` o no trae la clave.**

| Lado | Archivo |
|---|---|
| API | `server/src/Factum.Backend/Models/DeviceDetails.cs` (§6.2), `Models/Case.cs` |
| Web | `client/src/lib/api.ts`: `DeviceDetails`, `DeviceDetailField`, `ClockMeasurement`, `Case.device_details?: DeviceDetails \| null`; re-export en `client/src/types/index.ts` |

```ts
export type DeviceDetailKey =
  | "serial_number" | "os_version" | "os_api_level" | "build_id" | "build_fingerprint" | "security_patch" | "kernel"
  | "network_operator" | "network_operator_numeric" | "sim_operator" | "sim_operator_numeric" | "sim_state"
  | "verified_boot_state" | "bootloader_locked" | "vbmeta_device_state" | "timezone" | "auto_time" | "auto_time_zone"
  | "wifi_state" | "wifi_ssid";
export type DeviceDetailReason =
  | "empty" | "not_exposed" | "not_applicable" | "not_connected" | "masked" | "timeout" | "error" | "invalid";
export type ClockReason = "timeout" | "error" | "invalid" | "not_exposed";
export type PcServerReason = "not_measured" | "invalid";
export interface DeviceDetailField { value?: string | null; reason?: DeviceDetailReason | null }
export interface ClockMeasurement {
  kind: "start" | "close"; recorded_at: string; reason?: ClockReason | null;
  pc_before?: string | null; pc_after?: string | null; device_time?: string | null; resolution_ms?: number | null;
  device_minus_pc_ms?: number | null; margin_ms?: number | null;
  pc_minus_server_ms?: number | null; pc_server_margin_ms?: number | null; pc_server_reason?: PcServerReason | null;
}
export interface DeviceDetails {
  read_at: string; platform: "android" | "ios"; connection_id: string; agent_version: string;
  fields: Partial<Record<DeviceDetailKey, DeviceDetailField>>;
  clock_start?: ClockMeasurement | null; clock_close?: ClockMeasurement | null;
  include_ssid_in_report: boolean;
}
```

`DeviceDetailKey`/`DeviceDetailReason` van en `client/src/lib/device-details.ts` y los importan `api.ts` y `agent.ts`.

### 8.4 Eventos y config pública

- `CaseEventType` (`client/src/lib/api.ts`) suma `"device_details_recorded" | "device_clock_measured"`.
- `CaseEventDetail` suma `unavailable_count?: number`, `reread?: boolean`, `clock_kind?: "start" | "close"`,
  `device_minus_pc_ms?: number`, `clock_margin_ms?: number` y `pc_minus_server_ms?: number`, que corresponden a §6.3.
- Config pública:
  - API: `PublicConfigResponse(…, bool TimestampingEnabled, int ClockSkewWarningSeconds)`. El campo va **último** y el
    JSON es `clock_skew_warning_seconds`.
  - Web: `usePublicConfig` → `clockSkewWarningSeconds: number`. Si falta, o no es un entero entre 1 y 86400, vale 60.

### 8.5 El flag del SSID

| Dónde | Nombre exacto |
|---|---|
| Mongo | `Case.DeviceDetails.IncludeSsidInReport` (`bool`, default `false`) |
| JSON del caso | `device_details.include_ssid_in_report` |
| Requests | `include_ssid_in_report` en `PUT …/device-details` (opcional) y `PUT …/device-details/options` (obligatorio) |
| Informe | Lo lee `ReportDeviceTexts.Wifi(...)`: el SSID va a `{fichaWifi}` solo si es `true` y `wifi_ssid.value` no es nulo |
| Trazabilidad | **Nunca** (ni el flag ni el SSID) |

---

## 9. Informe

### 9.1 Plantilla v4 (`ops/plantilla/build_plantilla_v4.py`)

Sigue el camino de #78 (`selloTiempo`).

1. `BODY_PLACEHOLDERS` suma:
   - `"{fechaFichaTecnica}"` y `"{horaFichaTecnica}"`;
   - `"{fichaSistemaOperativo}"`, `"{fichaCompilacion}"`, `"{fichaParcheSeguridad}"`, `"{fichaNumeroSerie}"`;
   - `"{fichaIdentificadorConexion}"`, `"{fichaKernel}"`, `"{fichaOperadorSim}"`, `"{fichaEstadoArranque}"`;
   - `"{fichaZonaHoraria}"`, `"{fichaWifi}"`, `"{descripcionRelojDispositivo}"`.

   `BODY_BLOCKS` suma `"fichaTecnica"`.
2. En `build_document`, el párrafo `2E1C810A` ("Titular") queda igual y **después** se agrega:

   ```
   marker("{#fichaTecnica}", "ficha-open")
   simple-like párrafo de cuerpo (pPr normalizado norm_ppr, rPr base de 2E1C810A), ids.new("a6:ficha-intro"):
     "Datos técnicos obtenidos automáticamente por Factum mediante consultas de solo lectura al dispositivo el
      {fechaFichaTecnica} a las {horaFichaTecnica} horas:"
   11 filas, cada una como las del dispositivo (mismo pPr/rPr que 2E1C810A, build_runs con "**Etiqueta:** {x}"),
   ids.new("a6:ficha-<clave>"):
     **Sistema operativo:** {fichaSistemaOperativo}
     **Compilación:** {fichaCompilacion}
     **Parche de seguridad:** {fichaParcheSeguridad}
     **Número de serie:** {fichaNumeroSerie}
     **Identificador de conexión:** {fichaIdentificadorConexion}
     **Kernel:** {fichaKernel}
     **Operador / SIM:** {fichaOperadorSim}
     **Estado de arranque:** {fichaEstadoArranque}
     **Zona horaria:** {fichaZonaHoraria}
     **Wifi:** {fichaWifi}
   párrafo de cuerpo con norm_ppr, ids.new("a6:ficha-reloj"): "{descripcionRelojDispositivo}"
   marker("{/fichaTecnica}", "ficha-close")
   ```

   El bloque queda **entre** "Titular" y `07342EEC` (`{capturasImeiModelo}`), dentro de la sección "Elementos
   ofrecidos".
3. Con eso, el título, la intro y las filas de `BLOCK_ELEMENTOS` siguen consecutivos, y `v5.fingerprint` no cambia.
   Si alguna otra huella de v5/v6 mira lo que viene después de `2E1C810A`, se ajusta **sin tocar** el texto pericial y
   se documenta en el progress.

### 9.2 Plantilla v6 (`ops/plantilla/build_plantilla_v6.py`)

- `BODY_PLACEHOLDERS`/`BODY_BLOCKS` se heredan de la v4 y no hay que tocarlos.
- **Las 10 filas** se convierten en una **segunda ficha** etiqueta/valor con el mismo `data_card` (grilla 2490/6157,
  `CARD_BORDERS`, `split_row`), para que se vean como la ficha del dispositivo:
  - las filas se ubican por el placeholder que contienen (constante
    `FICHA_ROWS = ["{fichaSistemaOperativo}", …, "{fichaWifi}"]`, en orden), no por paraId;
  - se exige que sean consecutivas y que estén entre los marcadores `{#fichaTecnica}`/`{/fichaTecnica}`; si no, da
    `FALLA: ancla ficha`;
  - la intro y el párrafo del reloj quedan como párrafos de cuerpo (Arial por `tnr_to_arial`);
  - los marcadores quedan como párrafos hermanos del cuerpo, lo que necesita `ResolveBlocks`: se borra todo lo que
    está entre ellos, tabla incluida.
- `check()` suma el chequeo: el cuerpo de la v6 tiene **una** tabla entre los dos marcadores, con 10 filas.
- La etiqueta más larga ("Identificador de conexión:") puede partirse en dos renglones en la columna de 2490 dxa. Se
  acepta: no se cambia la grilla, porque es la de las demás fichas.
- **Regenerar las dos plantillas con los scripts** (v4 desde `docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx`,
  que existe en esta máquina; v6 desde la v4) y correr `--check` en las dos. **Nunca** editarlas a mano.
- `ops/plantilla/README.md` documenta el bloque nuevo.

### 9.3 Backend (`ReportService`, `ReportValues`, `ReportDeviceTexts`)

- `ReportService.KnownPlaceholders` suma los 13 placeholders de §9.1. `BlockKeys` suma `"fichaTecnica"`.
- `conditions["fichaTecnica"] = cas.DeviceDetails is not null`.
- B-R4, texto plano, al lado del de `selloTiempo`: si `cas.DeviceDetails is not null`, se llama a
  `ReplaceParagraphPerLine(body, "{descripcionRelojDispositivo}", ReportDeviceTexts.ClockBlock(cas.DeviceDetails, settings), resolved)`.
- `ReportValues.Placeholders` suma los 12 de una línea. Si no hay ficha, valen `""`, porque el bloque desaparece
  igual. Con ficha (`ReportDeviceTexts`, puro, testeable):

| Placeholder | Android | iOS |
|---|---|---|
| `{fechaFichaTecnica}` / `{horaFichaTecnica}` | `ReadAt` en `settings.Zone`, `dd/MM/yyyy` y `HH:mm` (`ReportValues.Inspeccion`) | ídem |
| `{fichaSistemaOperativo}` | `Android {os_version} (nivel de API {os_api_level})`. Sin API: `Android {os_version}`. Sin versión: `No disponible ({motivo})` | `iOS {os_version}` |
| `{fichaCompilacion}` | `{build_id}; huella: {build_fingerprint}` (las partes disponibles; las dos faltan → No disponible) | `{build_id}` |
| `{fichaParcheSeguridad}` | `security_patch` como `dd/MM/yyyy` | `No aplica (iOS no informa el parche por separado; lo determina la versión del sistema)` |
| `{fichaNumeroSerie}` | valor o `No disponible ({motivo})` | ídem |
| `{fichaIdentificadorConexion}` | `{connection_id} (identificador de conexión ADB; no es necesariamente el número de serie)` | `{connection_id} (UDID)` |
| `{fichaKernel}` | valor o No disponible | `No disponible ({motivo de not_exposed})` |
| `{fichaOperadorSim}` | `Red: {network_operator o "No disponible"} ({network_operator_numeric}); SIM: {sim_operator} ({sim_operator_numeric}), estado {sim_state}`. Las partes faltantes salen como `No disponible`; los paréntesis vacíos se omiten | `Red: No disponible (…); SIM: …` |
| `{fichaEstadoArranque}` | `verifiedbootstate: {v}; flash.locked: {v}; vbmeta.device_state: {v}`, valores crudos o `No disponible`, **sin calificar** (D9) | `No aplica (iOS no lo informa)` |
| `{fichaZonaHoraria}` | `{timezone}; hora automática: activada\|desactivada\|No disponible; zona horaria automática: …` (`1` = activada, `0` = desactivada) | `{timezone}` |
| `{fichaWifi}` | `disabled` → `Desactivado`; `disconnected` → `Activado, sin conexión`; `connected` + `include_ssid_in_report` + SSID → `Conectado a la red «{ssid}»`; `connected` sin el flag → `Conectado (el nombre de la red no se consigna)`; `connected` + `masked` → `Conectado (el sistema no informó el nombre de la red)`; `wifi_state` con motivo → `No disponible ({motivo})` | `No disponible (…)` |

`No disponible ({motivo})` usa los textos de §4.4.

`ReportDeviceTexts.ClockBlock(details, settings)` arma las líneas, un párrafo cada una:

1. `Reloj del dispositivo al inicio (medición registrada el dd/MM/yyyy a las HH:mm:ss): el dispositivo adelantaba 10 s respecto de la estación de trabajo (± 1 s).`

   La fecha es `ClockStart.RecordedAt` en `Zone`. Sin hora: `Reloj del dispositivo al inicio: No disponible ({motivo}).`
   Sin `ClockStart`: se omite la línea.
2. La misma línea con "al cierre", si `ClockClose` no es nulo (ya filtrado, §7.1).
3. Si alguna medición tiene `|pc_minus_server_ms|` por encima del umbral:
   `A su vez, la estación de trabajo adelantaba N respecto del servidor de Factum (± M s).` Se usa la de mayor valor
   absoluto. Debajo del umbral no se escribe nada (D5 validada).
4. Si alguna medición tiene `|device_minus_pc_ms|` por encima del umbral, la frase de D6, una sola vez:
   `Se deja constancia de que el reloj del dispositivo presentaba una diferencia de {dur} respecto de la estación de trabajo; las horas visibles en las capturas corresponden al reloj del dispositivo.`
   `{dur}` es la mayor de las dos.
5. Si la zona horaria del dispositivo está disponible: `Zona horaria configurada en el dispositivo: {timezone}.`

### 9.4 Tokens de los textos por defecto (`ReportValues.DefaultTextTokens`)

- `diferenciaReloj`: la frase de §5.3 en pasado para `ClockStart`, por ejemplo "el dispositivo adelantaba 10 s
  respecto de la estación de trabajo (± 1 s)". Sin ficha o sin hora: `No disponible`.
- `numeroSerieDispositivo`, `compilacionDispositivo` (= `build_id`) y `zonaHorariaDispositivo`: el valor o
  `No disponible`.
- `sistemaOperativo`:
  - con ficha y `os_version` disponible: `Android {os_version}` / `iOS {os_version}`, que arregla el "Android 0" de las
    versiones con punto;
  - si no, `cas.Device.OsLabel`, como hoy.

Los textos por defecto de `ReportDefaultTexts` **no cambian**: la ficha ya está en la sección del dispositivo. Los
tokens quedan disponibles para los `Report:DefaultTexts:*` de cada estudio. Se documentan en el README.

---

## 10. Web (`client/`)

### 10.1 Lógica pura y hooks

**`client/src/lib/device-details.ts`** (nuevo, puro):
- `DEVICE_DETAIL_KEYS` (las 20, en orden).
- `FIELD_LABEL: Record<DeviceDetailKey, string>`:
  - "Número de serie", "Versión del sistema", "Nivel de API", "Compilación", "Huella de compilación";
  - "Parche de seguridad", "Kernel", "Operador de red", "Código de red (MCC/MNC)";
  - "Operador de la SIM", "Código de la SIM", "Estado de la SIM";
  - "Arranque verificado", "Bootloader bloqueado", "Estado de vbmeta";
  - "Zona horaria", "Hora automática", "Zona horaria automática", "Wifi", "Nombre de la red (SSID)".
- `REASON_LABEL` con los textos de §4.4.
- `formatDuration(seconds)`, `describeDeviceSkew(ms, marginMs)` (web, presente) y `describePcServerSkew` (§5.3).
- `exceedsThreshold(ms, thresholdSeconds)`.
- `unavailableCount(fields)` (§4.4).
- `isLoopbackAgent()`: `new URL(AGENT_URL).hostname` es `localhost`, `127.0.0.1` o `[::1]`.
- `probeServerClock(): Promise<PcServerProbe | null>` (§5.2; `null` si falla o si `!isLoopbackAgent()`).
- `toSaveRequest(reading: AgentDeviceDetails, probe, includeSsid)`.
- `detailsViewFromAgent(reading)` y `detailsViewFromCase(details)`: un modelo de vista común para los componentes.

**`client/src/hooks/useDeviceDetails.ts`** (nuevo):
- **Estado:** `{ status: "idle" | "unsupported" | "loading" | "ready" | "error", reading: AgentDeviceDetails | null, readAt: number | null, error: string | null }`.
- **Acciones:** `read()` (abortable) y `reset()`.
- **`unsupported`:** cuando `!(await agent.supportsDeviceDetails())`.
- **Errores** (`AgentError`):
  - `device_not_found` → "El dispositivo no está conectado o no autorizó la conexión."
  - el resto → `agentErrorMessage(e)`.
- **Timeout:** 45 s, con `AbortSignal.timeout(45000)` combinado con el abort del hook.
- **Ciclo de vida:** se resetea cuando cambia el `serial`.

**`agent.ts`:** `getDeviceDetails`/`getDeviceClock` usan `agentJSON` (lanzan `AgentError`) y
`encodeURIComponent(serial)`. El timeout del reloj es de 15 s.

**`api.ts`:** las 4 funciones de §8.2. El `PUT` devuelve `Case`.

**`usePublicConfig`:** `clockSkewWarningSeconds`, con default 60 (§8.4).

### 10.2 Paso 2 (`CaseFormStep` + `CaseSummaryCard`)

- **`page.tsx`:**
  - Instancia `useDeviceDetails(selDevice)`.
  - Lanza `read()` al entrar al paso 2 con un dispositivo conectado (`connection === "connected"`), en modo `create`.
  - En modo `edit` se muestra lo guardado (`currentCase.device_details`) y no se lee solo.
- **`CaseSummaryCard`**, bloque del equipo (las dos variantes):
  - fila compacta `DeviceClockLine` siempre visible, con tres estados:
    - "Leyendo datos del dispositivo…" con un esqueleto y `aria-busy`;
    - "Adelanta 10 s respecto de esta PC (± 1 s)";
    - aviso de umbral (abajo);
  - botón de texto "Ver datos técnicos" (+ "· N no disponibles" si N > 0), que abre `DeviceDetailsDialog`;
  - el `SpecRow` que hoy dice **"Serial"** pasa a **"Id. de conexión"**, porque es el serial ADB o el UDID (HU,
    escenario "Número de serie vs identificador de conexión");
  - si hay ficha y `serial_number` tiene valor, se suma un `SpecRow` "N.° de serie" en el plegable `specsOpen`.
- **`DeviceDetailsDialog`** (PrimeReact `Dialog`, nuevo), que envuelve a `DeviceDetailsPanel`:
  - las 20 filas: valor, o "No disponible" en gris con el motivo como texto secundario visible (no solo tooltip:
    accesibilidad y táctil);
  - las filas con `not_applicable` van al final, plegadas, como "No aplica a esta plataforma (N)";
  - la medición de reloj de inicio y la de cierre, con su fecha y hora;
  - la fila PC↔servidor, solo con la ficha ya guardada;
  - Wifi: "Conectado a «SSID»" + `Checkbox` "Incluir el nombre de la red en el informe" (desmarcado por defecto, D7).
    En `create` cambia el estado local `includeSsid`; en `edit` llama a `api.setDeviceDetailsOptions` y actualiza
    `currentCase`;
  - botón **"Volver a leer"**: visible si el caso es editable (o todavía no existe), el dispositivo es el mismo
    (`selDevice.serial === currentCase.device.serial`) y está conectado. En `edit` hace
    `read()` → `probeServerClock()` → `api.saveDeviceDetails(..., include_ssid_in_report: undefined)` → `setCase`.
- **Aviso de umbral:** si `exceedsThreshold`, va un `FxBanner tone="warn"` no bloqueante, con ícono y texto:
  "El reloj del celular difiere más de {dur(umbral)} del de esta PC. Las horas visibles en las capturas corresponden al
  reloj del celular; la diferencia queda registrada en el informe." **No** sugiere corregir la hora del celular.
- **Lectura fallida** (`error`): aviso con el mensaje y "Volver a leer".
- **Tatana viejo** (`unsupported`): aviso informativo "Actualizá Tatana para registrar los datos técnicos del
  dispositivo." No bloquea (D12).

### 10.3 Alta del caso (`handleSaveCase`, solo en `create`)

Después de `api.createCase` y `setCase(cas)`, **antes** de `go(3)` y sin bloquear el alta, se corre
`registerDeviceDetails(cas)`:

1. Si el hook está `unsupported`, no se hace nada.
2. Si `reading` existe, es del mismo serial y `Date.now() − readAt ≤ 60 000` ms, se reusa. Si no, se hace `read()`
   (D-T5).
3. `probe = await probeServerClock()`.
4. `updated = await api.saveDeviceDetails(cas.id, toSaveRequest(reading, probe, includeSsid))` → `setCase(updated)`.
5. Ante cualquier error en 2–4, `go(3)` igual y aparece un aviso informativo en el paso 3 (estado nuevo
   `deviceDetailsNotice` en `page.tsx`): "No se pudieron registrar los datos técnicos del dispositivo." Lleva un botón
   "Volver a leer", que repite 2–4 con `read()` forzado. El aviso desaparece al lograrlo o al salir del paso 3.

El botón "Guardar" muestra "Guardando…" hasta el final del paso 4, con un tope de 45 s para la lectura. Si la ficha
tarda, el caso ya está creado.

### 10.4 Retomar un borrador sin ficha (D10)

Hay un efecto en `page.tsx`: con `mode === "wizard"`, `currentCase` editable,
`currentCase.device_details == null`, el dispositivo del caso presente en `devices`, la capability presente y el
`case.id` todavía no intentado en esta sesión (un `Set` en un `ref`), se corre `registerDeviceDetails(currentCase)` una
sola vez.

- **Éxito:** "Se registraron los datos técnicos del dispositivo (lectura del dd/MM/yyyy HH:mm)." con `role="status"`,
  sin bloquear.
- **Falla:** el mismo aviso de §10.3.

No hay medición intermedia automática si el caso ya tiene ficha (D4, sub-duda).

### 10.5 Generación (`handleGenerateAgent` + `GenerateStep`)

Después de `api.prepareGeneration` y **antes** de `agent.buildZip`, la medición de cierre corre si se cumple todo esto:
- `cas.device_details` existe;
- `await agent.supportsDeviceDetails()`;
- `devices.some(d => d.serial === cas.device.serial)`.

Con eso:
1. `setGenerateProgress({ stage: "clock" })`.
2. `c = await agent.getDeviceClock(serial, platform)`.
3. `probe = await probeServerClock()`.
4. `await api.saveDeviceClock(cas.id, { connection_id: serial, agent_version: c.agent_version, clock: c.clock, pc_server_probe: probe })`.

Cualquier error se traga **sin mensaje** y la generación sigue. Si no se cumplen las condiciones, la etapa se omite y
tampoco hay mensaje.

`GenerateProgress.stage` suma `"clock"`. `GenerateStages` muestra la etapa "Midiendo el reloj del dispositivo…" (ícono
`Clock`) **solo** si el progreso pasó por ella.

En el flujo viejo (server) no hay medición de cierre.

### 10.6 Detalle del caso (`CaseDetailContent`)

- Hay una sección nueva "Datos técnicos del dispositivo" con `DeviceDetailsPanel` en modo de solo lectura: sin
  checkbox editable (se muestra el estado del flag en texto) y sin "Volver a leer". Va debajo de la grilla del equipo.
  Muestra las mediciones con fecha y hora, el aviso si superó el umbral y la fila PC↔servidor.
- Un caso sin ficha muestra una nota informativa (tono neutro): "Sin ficha técnica (caso anterior a esta función)."
- La fila "Serial", si existe, pasa a "Id. de conexión".

### 10.7 Trazabilidad (`lib/case-events.ts`, `CaseTimeline`)

- **Íconos:** `device_details_recorded` → `Cpu`; `device_clock_measured` → `Clock`.
- **Tono:** `warning` si el evento trae `|device_minus_pc_ms|` por encima del umbral. La función de tono recibe el
  umbral como parámetro opcional, y sin él el tono es `neutral`.
- **Títulos:**
  - "Se registraron los datos técnicos del dispositivo";
  - con `reread`, "Se volvieron a leer los datos técnicos del dispositivo";
  - "Se midió el reloj del dispositivo al generar".
- **Línea secundaria** (función nueva `eventDeviceDetail(ev)`): "El dispositivo adelanta 10 s respecto de la PC (± 1 s)
  · 3 datos no disponibles". Sin hora: "Reloj no disponible ({motivo})".

### 10.8 Accesibilidad y criterios visuales

Los avisos llevan ícono + texto, nunca solo color. La diferencia se dice en palabras ("adelanta"/"atrasa"). La fila de
reloj en estado de carga usa `aria-busy` y el texto "Leyendo datos del dispositivo…". El diálogo tiene foco inicial y
`Escape`, como los demás `Dialog` del repo. Los valores largos (huella, kernel) usan `break-all` y monoespaciado.

---

## 11. Configuración, compatibilidad, riesgos y release

### 11.1 Configuración (umbral, D6)

- `ReportOptions.ClockSkewWarningSeconds` (`int`, default **60**). Variable: `Report__ClockSkewWarningSeconds`.
- `ReportSettings`:
  - fuera de 1…86400 da un warning al arrancar ("Report: ClockSkewWarningSeconds fuera de rango; se usa 60") y se usa
    60;
  - se expone como `IReportSettings.ClockSkewWarningSeconds`;
  - hay que actualizar los dobles: `ReportTestSupport.FakeSettings` y cualquier otro `IReportSettings` de los tests.
- `appsettings.json` (`Report`): `"ClockSkewWarningSeconds": 60`.
- `deploy/cloud/docker-compose.yml` y `deploy/windows/docker-compose.yml` (`backend.environment`), al lado de las
  claves de `Report` o del sellado:

  ```yaml
  # Umbral del aviso por diferencia de reloj celular/PC (metadatos-dispositivo, D6). Segundos.
  Report__ClockSkewWarningSeconds: ${FACTUM_RELOJ_UMBRAL_SEGUNDOS:-60}
  ```
- `deploy/cloud/factum.env.example` y `deploy/windows/.env.example`: `FACTUM_RELOJ_UMBRAL_SEGUNDOS=60`, con un
  comentario.
- `deploy/windows/scripts/restaurar.ps1`: sumar `'FACTUM_RELOJ_UMBRAL_SEGUNDOS'` a la lista fija de claves.
  `deploy/windows/tests/comun.Tests.ps1`: un `It` que verifica que el `.env.example` la trae con `60`.
- `docker-compose.yml` de la raíz (desarrollo): sin cambio, usa el default.
- `README.md`: la clave nueva en la tabla de configuración de `Report`, con la variable de entorno.

### 11.2 Compatibilidad

| Situación | Comportamiento |
|---|---|
| **Caso generado antes de la HU** | Sin `device_details`: no se modifica ni se regenera nada. La web muestra "Sin ficha técnica (caso anterior a esta función)". El informe ya emitido no se toca. |
| **Borrador previo a la HU** | Obtiene la ficha al retomarlo con el mismo dispositivo conectado (§10.4), con la fecha real de lectura. Si no, genera sin ficha y el bloque desaparece. |
| **Tatana viejo** (sin `device_details_v1`) | El caso se crea como hoy, sin ficha, con el aviso "Actualizá Tatana…" (D12). La generación no mide el reloj. |
| **Backend viejo con web nueva** | No puede pasar en un despliegue normal (salen juntos). Igual, un 404/405 en `PUT …/device-details` cae en el aviso de §10.3, sin romper el alta. |
| **Web vieja con backend nuevo** | La web no manda la ficha y `device_details` queda `null`. Sin efecto. |
| **Rollback del backend** | `[BsonIgnoreExtraElements]` en `Case` ignora `DeviceDetails`. Los tipos de evento nuevos los muestra el cliente viejo como "Actividad del caso". |
| **Plantilla personalizada sin el bloque** | Los placeholders nuevos se borran si aparecen sueltos (B-R1 los conoce, no los borra), y un bloque ausente no rompe nada. |
| **Informe de un caso con ficha** | Lo único nuevo es el bloque. El resto del texto pericial es idéntico (`ReportDesignTests` lo compara). |

### 11.3 Riesgos

1. **Variación entre fabricantes.** `gsm.*`, `ro.boot.*` y el formato de `cmd wifi status` cambian según la marca. Un
   valor inesperado termina en `invalid` o `empty`, nunca en basura dentro del informe (§4.5).
2. **Android ≤ 10 sin `cmd wifi status`.** Cae a `dumpsys`. Si el formato de `mWifiInfo` no coincide, queda
   `not_exposed`.
3. **`date` sin `%N`** (toybox viejo): la resolución pasa a 1 s y el margen sube a ~1 s. Igual es correcto.
4. **iOS no verificado en vivo** (§4.3). El parser tolera las ausencias.
5. **Mismo reloj navegador/Tatana** (§5.2): fuera de loopback, `not_measured`.
6. **El SSID en Mongo y en el JSON del caso** (D7 lo registra). No va a logs ni a eventos. Al informe va solo con el
   flag.
7. **Etiqueta larga en la ficha v6** (§9.2): se acepta.

### 11.4 Release de Tatana necesaria

- Esta HU necesita **Tatana 1.7.0**: `device_details_v1`, el helper con `details` y el endpoint. La release 1.7.0
  llevaría también #78 (`case_seals_v1`), que tampoco está publicado (el último tag es `tatana-v1.6.0`).
- Se publica con el flujo existente: tag `tatana-vX.Y.Z` **desde `main`** después de promover `develop`, a pedido del
  usuario (memoria: no promover sin pedido).
- **No** se sube `Tatana:MinVersion`, porque la ficha es documental (D12).
- En el cierre, el orquestador avisa al usuario que hace falta la release para que los peritos tengan la ficha.

---

## 12. Decisiones técnicas

Ninguna necesita validación del usuario: todas aplican las decisiones D1–D12 ya validadas.

- **D-T1. Endpoint bajo demanda, separado del sondeo.** Se crean `GET /devices/{serial}/details` y `/clock`.
  `EnrichDeviceAsync` y el `Device` del sondeo no cambian. Se suma la capability `device_details_v1`.
- **D-T2. Tres scripts de shell agrupados, más tres muestras de `date`, con timeouts de 5 s / 3 s.** Así hay 7
  procesos adb en lugar de ~20, y cada grupo falla por separado.
- **D-T3. Reloj por la muestra de menor RTT entre 3**, con margen = RTT/2 + resolución, una cota estricta. La API
  **recalcula** desde los valores crudos, así que la cifra guardada no depende del cliente.
- **D-T4. PC↔servidor por sondeo NTP del navegador contra `GET /api/time`, calculado por la API.** Supone que el
  navegador y Tatana comparten el reloj (loopback). Si no, `not_measured`.
- **D-T5. La ficha se guarda con un `PUT` aparte, después de `POST /api/cases`.** Se reusa la lectura del paso 2 si
  tiene ≤ 60 s. Así el alta no depende de la ficha, y "Volver a leer" y la retoma usan el mismo camino.
- **D-T6. `read_at` y `recorded_at` usan el reloj del servidor al recibir** (HU). Las horas de la PC quedan guardadas
  aparte (`pc_before`/`pc_after`).
- **D-T7. Medición de cierre después de `prepare`.** El informe la usa solo si
  `RecordedAt ≥ PendingGeneration.CreatedAt`, para que no entre la de un intento anterior. El flujo viejo no la usa.
- **D-T8. Ficha como diccionario de 20 claves fijas, cada una con `value` o `reason`.** Los motivos son un enum
  cerrado.
- **D-T9. Validación duplicada (Tatana y API) con la misma tabla.** Un valor inválido se convierte en `invalid`, no en
  400.
- **D-T10. Plantilla.** En la v4, el bloque `{#fichaTecnica}` va después de "Titular", con filas "**Etiqueta:**
  {token}". En la v6, segunda ficha etiqueta/valor. El reloj es texto plano multilínea
  (`{descripcionRelojDispositivo}`).
- **D-T11. Paso 2.** Lo compacto (reloj + aviso + "Ver datos técnicos") va en `CaseSummaryCard`, y la ficha completa en
  un `Dialog`. La columna lateral tiene alto fijo sin scroll, así que una sección colapsable larga no entra. Se ajusta
  la UX de la HU ("sección colapsable") sin cambiar el contenido.
- **D-T12. Umbral en `Report:ClockSkewWarningSeconds` (60, entre 1 y 86400)**, con `FACTUM_RELOJ_UMBRAL_SEGUNDOS` y
  expuesto en la config pública.
- **D-T13. Eventos `device_details_recorded` y `device_clock_measured`**, sin valores ni SSID. "Volver a leer" queda
  como `reread: true`.
- **D-T14. El flag del SSID va por un endpoint propio**, sin evento, y se conserva al volver a leer.
- **D-T15. El listado incluye `device_details`**, porque el detalle usa el objeto del listado.
- **D-T16. Retoma (D10): una lectura automática por caso y por sesión**, solo si no hay ficha.
- **D-T17. Mock de Tatana con el fixture real** y `MockDeviceClockSkewSeconds` para probar el aviso.
- **D-T18. iOS: subcomando `details` con lista blanca de claves.** ICCID, IMSI, número de línea y MAC nunca salen del
  helper.
- **D-T19. `{sistemaOperativo}` usa la versión textual de la ficha** cuando la hay.
- **D-T20. "Volver a leer" solo en el wizard**, no en el modal de detalle, porque el modal no tiene el contexto del
  dispositivo conectado.

---

## 13. Tests

### 13.1 Fixtures (Tatana)

Van en `server/tests/Factum.Agent.Tests/Fixtures/DeviceDetails/`. Se suma al `.csproj`:
`<None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />`. Lo de `samsung-sm-m127f-a11/` es **real**
(§3.1), copiado byte a byte. Lo `synthetic-*` lleva un `README.md` que dice que es sintético y en qué formato se basa.

`samsung-sm-m127f-a11/props.txt` (real):

```
ro.serialno=R58RA3R5AGA
ro.build.version.release=11
ro.build.version.sdk=30
ro.build.id=RP1A.200720.012
ro.build.fingerprint=samsung/a12sub/a12s:11/RP1A.200720.012/A127MUBU3AUJ5:user/release-keys
ro.build.version.security_patch=2021-10-01
gsm.operator.alpha=
gsm.operator.numeric=722310
gsm.sim.operator.alpha=
gsm.sim.operator.numeric=
gsm.sim.state=ABSENT
ro.boot.verifiedbootstate=orange
ro.boot.flash.locked=0
ro.boot.vbmeta.device_state=unlocked
persist.sys.timezone=America/Argentina/Buenos_Aires
__FX_SEP__
Linux localhost 4.19.111-22565658 #1 SMP PREEMPT Thu Nov 11 01:08:09 KST 2021 aarch64
```

`samsung-sm-m127f-a11/settings.txt` (real): `1\n__FX_SEP__\n1\n`

`samsung-sm-m127f-a11/wifi.txt` (real): `Wifi is enabled\nWifi is not connected\n`

`samsung-sm-m127f-a11/wifi-dumpsys.txt` (real, rama forzada):
`__FX_DUMPSYS__\nWi-Fi is enabled\nstate=DISCONNECTED ssid=<unknown ssid>\n`

`samsung-sm-m127f-a11/date-samples.json` (real; `pc_*` en segundos Unix con µs, `out` = stdout de `date +%s.%N`):

```json
[
  { "pc_before": "1791991358.964579", "out": "1791991369.055745024", "pc_after": "1791991359.079131" },
  { "pc_before": "1791991359.079176", "out": "1791991369.166298293", "pc_after": "1791991359.177140" },
  { "pc_before": "1791991359.177240", "out": "1791991369.244860831", "pc_after": "1791991359.275305" }
]
```

El resultado esperado elige la muestra 2: `device_minus_pc_ms = 10038`, `margin_ms = 50`, `resolution_ms = 1`,
`samples = 3`. Es decir, `pc_before = 2026-10-14T15:22:39.0791760Z`, `pc_after = 2026-10-14T15:22:39.1771400Z` y
`device_time = 2026-10-14T15:22:49.1662982Z`.

Sintéticos:

- `synthetic-wifi/connected-a11.txt`: `Wifi is enabled\nWifi is connected to "Casa Ana"\n`. Esperado: `connected` y
  SSID `Casa Ana`.
- `synthetic-wifi/connected-unknown.txt`: `Wifi is connected to <unknown ssid>`. Esperado: `masked`.
- `synthetic-wifi/disabled.txt`: `Wifi is disabled`. Esperado: `disabled`, `not_connected`.
- `synthetic-wifi/dumpsys-completed.txt`: `__FX_DUMPSYS__\nWi-Fi is enabled\nstate=COMPLETED ssid="Oficina 2"`.
  Esperado: `connected` y SSID `Oficina 2`.
- `synthetic-wifi/dumpsys-noline.txt`: `__FX_DUMPSYS__\n`. Esperado: `not_exposed` ×2.
- `synthetic-wifi/ssid-hostile.txt`: `Wifi is connected to "Red‮evil\u0007"` (con RLO U+202E y BEL). Esperado: SSID
  saneado sin bidi ni control (`Redevil`).
- `synthetic-props/dual-sim.txt`: como el real, con `gsm.sim.state=READY,ABSENT` y
  `gsm.operator.alpha=Personal,`. Esperado: valores crudos con coma; `Personal,` es válido como texto libre.
- `synthetic-props/crlf.txt`: el real con `\r\n`. Esperado: el mismo resultado que el real.
- `synthetic-props/unknown-serial.txt`: `ro.serialno=unknown`. Esperado: `empty`.
- `synthetic-props/bad-patch.txt`: `ro.build.version.security_patch=2021-13-45`. Esperado: `invalid`.
- `synthetic-date/`:
  - `1791991369` → `resolution_ms = 1000`;
  - `1791991369.%N` → segundos, 1000;
  - `garbage` → muestra inválida;
  - `0` → inválida (fuera de los 50 años).
- `synthetic-ios/details.json`:

  ```json
  {"udid":"00008110-001A2B3C4D5E801E","values":{"SerialNumber":"F2LZK0ABCDEF","ProductVersion":"17.4.1","BuildVersion":"21E236","TimeZone":"America/Argentina/Buenos_Aires","SIMStatus":"kCTSIMSupportSIMStatusReady"},"carriers":[{"bundle":"com.apple.Personal_ar","mcc":"722","mnc":"34"}],"clock":[{"pc_before_ns":1791991358900000000,"device":1791991356.512345,"pc_after_ns":1791991358960000000},{"pc_before_ns":1791991358970000000,"device":1791991356.56,"pc_after_ns":1791991359000000000},{"pc_before_ns":1791991359010000000,"device":1791991356.62,"pc_after_ns":1791991359090000000}],"clock_error":null}
  ```

  Esperado:
  - se elige la muestra 2 (RTT 30 ms): `device_minus_pc_ms = -2425`, `margin_ms = 16`; en pantalla, "Atrasa 2 s… (± 1
    s)";
  - `sim_operator = Personal_ar`, `sim_operator_numeric = 72234`, `sim_state = Ready`;
  - `kernel = not_exposed`, `security_patch = not_applicable`.
- `synthetic-ios/details-no-clock.json`: `"clock":[], "clock_error":"InvalidService"`. Esperado:
  `clock.reason = error`, `samples = 0`.

### 13.2 Tests de Tatana (`server/tests/Factum.Agent.Tests`)

- `DeviceDetailsAndroidParsingTests`:
  - cada fixture contra el resultado esperado (las 20 claves);
  - los scripts `PROPS`/`SETTINGS`/`WIFI` son exactamente el texto de §4.2;
  - el lector, con un ejecutor falso que devuelve los fixtures, arma la respuesta completa;
  - timeout (-2) en `SETTINGS` da `auto_time*`=`timeout` y el resto intacto;
  - -1 en `PROPS` da `error` en las 16 claves de ese script;
  - `get-state` ≠ `device` da `device_not_found` sin correr más scripts.
- `DeviceClockMathTests`:
  - la muestra real da 10038/50;
  - empate de RTT → la primera;
  - todas inválidas → `invalid`; todas -2 → `timeout`;
  - el redondeo half away from zero en negativos;
  - la conversión a ticks sin `double` (fracción de 9 dígitos).
- `IosDetailsParsingTests`: los dos fixtures de iOS, más uno sin `carriers` ni `SIMStatus` (dan `empty`).
- `DetailRulesTests`: la tabla de §4.1/§4.5 (válidos e inválidos por clave, limpieza bidi/C0, 64 elementos de texto,
  `unknown`/`null`).
- `IosHelperTests` (existente): el subcomando `"details"` en la lista. Además, el helper **no** contiene las claves
  `IntegratedCircuitCardIdentity`, `InternationalMobileSubscriberIdentity`, `PhoneNumber` ni `WiFiAddress` dentro de
  `cmd_details`. Se verifica con un `Assert` sobre el texto del método, extraído entre `async def cmd_details` y la
  próxima `def`.
- **Capability:** `Assert.Contains("device_details_v1", HealthController.Capabilities)`, al lado del test de
  `case_seals_v1`.
- **Mock:** con `Mock=true` y `MockDeviceClockSkewSeconds=90`, la respuesta da `device_minus_pc_ms = 90000`.

### 13.3 Tests de la API (`server/tests/Factum.Backend.Tests`, carpeta nueva `DeviceDetails/`)

- `DeviceDetailsRulesTests`:
  - la misma tabla de validación que Tatana;
  - `reason` desconocido → `error`; clave desconocida ignorada; clave faltante → `error`; `value` + `reason` → solo
    `reason`;
  - `ComputeClock` con las ISO del fixture real da 10038/50;
  - `ComputePcServer`: válido, `server_time` futuro, `server_time` de hace más de 300 s, RTT > 10 s y sin sondeo
    (`not_measured`).
- `DeviceDetailsFlowTests`, con los dobles de repositorio existentes o uno nuevo en memoria:
  - el `PUT` guarda, recalcula, emite `device_details_recorded` con `unavailable_count` correcto y **sin SSID** (el JSON
    del evento no contiene el SSID);
  - `device_mismatch`;
  - caso no editable → 409 `case_not_editable`;
  - el segundo `PUT` da `reread = true` y conserva `include_ssid_in_report` y `clock_close`;
  - `clock` sin ficha → `no_device_details`;
  - `options` cambia el flag sin evento.
- `ReportDeviceTextsTests`:
  - cada fila de §9.3 para Android (fixture real) e iOS (sintético);
  - Wifi en sus 6 variantes (con y sin flag);
  - `ClockBlock`: coincide, adelanta, atrasa, sin hora, con cierre, frase de umbral (61 s con umbral 60 → sí; 60 s →
    no), línea PC↔servidor solo por encima del umbral;
  - `formatDuration` con la tabla de §5.3.
- `ReportDeviceDetailsDocxTests` (con `ReportTestSupport.Generate`, plantilla v6):
  - sin ficha, el texto del cuerpo es **igual** al de hoy y no hay "Datos técnicos obtenidos";
  - con ficha, aparecen la intro, las 10 etiquetas, "verifiedbootstate: orange", "adelantaba 10 s" y una tabla más;
  - un SSID con `{caratula}` sale literal;
  - sin el flag, el SSID no aparece en ningún lado del DOCX.
- `ReportDesignTests` y `ReportQaRegressionTests` (existentes): se ajustan solo los conteos de tablas o párrafos que
  cambien por la ficha nueva y se deja constancia. El texto pericial v4 = v6 tiene que seguir pasando.
- `TatanaMinVersionTests.PublicConfig_SinMinimo_EsNullYSiempreEsta`: la lista de propiedades suma
  `"clock_skew_warning_seconds"` al final. Más un test del valor (default 60; 0 → 60 con warning).
- **Opcional, con `[MongoFact]`** (`FACTUM_TEST_MONGO`, base propia `factum_test_<guid>` que el test crea y borra,
  patrón de `MongoCaseTimestampRepositoryTests`): `SetDeviceDetailsAsync` / `SetClockCloseAsync` /
  `SetIncludeSsidAsync` respetan `Editable` y un documento sin `DeviceDetails` deserializa `null`. **Nunca contra la
  base de desarrollo.**

### 13.4 Web

No hay infraestructura de tests en `client/`. La verificación es `npx tsc --noEmit`, más los casos de
`formatDuration`/`describeDeviceSkew` de §5.3 revisados contra los tests de C#. El implementador los deja como tabla en
su progress.

---

## 14. Checklists atómicos

### 14.1 `implementer-backend` (Tatana + API + plantillas + deploy + docs)

**Tatana**
- [ ] `Models/DeviceDetailsModels.cs`: los records de §8.1, `DetailKeys` (las 20, `All` en orden) y `DetailReasons`.
- [ ] `Services/DeviceDetails/DetailRules.cs`: `Clean` (equivalente a `SafeText` SingleLine) y `Normalize(key, raw)` →
      `DetailField` con la tabla de §4.1/§4.5.
- [ ] `Services/DeviceDetails/DeviceClockMath.cs`: `ParseAndroidDate`, `FromIosSample`, `Pick` y `Compute`, en ticks
      (§5.1).
- [ ] `Services/DeviceDetails/AndroidDetailsReader.cs`:
  - [ ] constantes `GetStateArgs`, `PropsScript`, `SettingsScript`, `WifiScript` y `DateScript` (texto exacto de §4.2);
  - [ ] `ParseProps`, `ParseSettings` y `ParseWifi` puros;
  - [ ] ejecutor inyectable `(args, timeout, ct)` y `TimeProvider`;
  - [ ] orden y errores de §4.2.
- [ ] `Services/Ios/ios_helper.py`: `cmd_details` (§4.3), parser y `HANDLERS`, docstring. Sin claves sensibles.
- [ ] `IosService`: `Task<string> RunDetailsHelperAsync(string udid, CancellationToken ct)` con `DetailsTimeout = 30 s`
      y `EnsureAppleServiceAsync`. Se suma a `IIosService`.
- [ ] `Services/DeviceDetails/IosDetailsParser.cs` (§4.3).
- [ ] `Services/DeviceDetails/DeviceDetailsService.cs` (+ interfaz):
  - [ ] `ReadAsync(serial, platform, ct)` y `MeasureClockAsync(serial, platform, ct)`;
  - [ ] semáforo por serial;
  - [ ] mock (§4.7);
  - [ ] log sin valores (§4.6).
- [ ] `AgentOptions.MockDeviceClockSkewSeconds` (`int`, default 0) y su documentación en el comentario de la clase.
- [ ] `AgentErrorCodes.DeviceNotFound = "device_not_found"`.
- [ ] `Controllers/DeviceDetailsController.cs` (§7.2) y el registro en DI en `Program.cs`.
- [ ] `HealthController.Capabilities` += `device_details_v1`, con su comentario.
- [ ] Fixtures (§13.1) + `.csproj` de tests + tests (§13.2).
- [ ] Opcional: si hay un iPhone a mano, `python3 -I <helper> details --udid <udid>` y confirmar §4.3. Dejar
      constancia en el progress. Con el SM-M127F se pueden re-correr **solo** los scripts de §4.2 (lectura) para
      confirmar los fixtures. **No cambiar nada en el dispositivo.**

**API**
- [ ] `Models/DeviceDetails.cs` (§6.2) y `Case.DeviceDetails` (§6.1).
- [ ] `Models/CaseEvent.cs`: los 2 tipos y los 6 campos de detalle (§6.3).
- [ ] `DTOs/DeviceDetailsDtos.cs` (§8.2). `DTOs/ConfigDtos.cs`: `ClockSkewWarningSeconds` al final, con el
      `<summary>` actualizado.
- [ ] `ReportOptions.ClockSkewWarningSeconds`, `IReportSettings`/`ReportSettings` (rango y warning) y
      `appsettings.json`.
- [ ] `Services/Cases/DeviceDetailsRules.cs`: `NormalizeFields`, `ComputeClock`, `ComputePcServer` y
      `UnavailableCount`.
- [ ] `Infrastructure/MongoRepository.cs` + `ICaseRepository`: `SetDeviceDetailsAsync`, `SetClockCloseAsync` y
      `SetIncludeSsidAsync` (filtro `Editable(id)`, más `Exists(DeviceDetails)` en los dos últimos). Actualizar
      **todos** los dobles de `ICaseRepository` en los tests (`AdminTestDoubles.cs` y otros).
- [ ] `Services/Cases/DeviceDetailsFlow.cs` (`partial class CaseService`): los tres métodos de §7.1 + `ICaseService`.
- [ ] `Controllers/CasesController.cs`: las 3 rutas `PUT`, con el mismo patrón de `Officer` y `this.ErrorResult(result)` que las
      existentes.
- [ ] `Controllers/TimeController.cs`: `GET /api/time`.
- [ ] `ConfigController.Public`: `ClockSkewWarningSeconds`.
- [ ] `AgentGeneration.FinishGenerationAsync`: filtro en memoria de `ClockClose` (§7.1). `CaseService.GenerateAsync`:
      `ClockClose = null` en memoria.
- [ ] `Services/Reports/ReportDeviceTexts.cs` (§9.3, §5.3), `ReportValues.Placeholders` + `DefaultTextTokens` (§9.4),
      `ReportService` (`KnownPlaceholders`, `BlockKeys`, `conditions`, B-R4).
- [ ] Tests (§13.3).

**Plantillas**
- [ ] `build_plantilla_v4.py` (§9.1) y `build_plantilla_v6.py` (§9.2): conjuntos, bloque, segunda ficha y chequeo.
- [ ] Regenerar `Templates/plantilla_informe_v4.docx` y `plantilla_informe_v6.docx` con los scripts. `--check` = `OK`
      en las dos.
- [ ] `ops/plantilla/README.md`.

**Deploy y docs**
- [ ] Los compose, los `.env.example`, `restaurar.ps1` y `comun.Tests.ps1` (§11.1).
- [ ] `README.md`:
  - [ ] los 13 placeholders y el bloque `{#fichaTecnica}` en "Placeholders de la plantilla del informe";
  - [ ] los tokens nuevos de los textos por defecto;
  - [ ] `Report:ClockSkewWarningSeconds`;
  - [ ] el párrafo "Diferencia de reloj" con el algoritmo (§5.1–§5.2) y el riesgo aceptado.
- [ ] `AGENTE_TATANA.md`: caso de uso "Leer la ficha técnica y el reloj del dispositivo", con el endpoint, los
      comandos de solo lectura y la capability.

**Regla dura de datos (AGENTS.md):** no borrar ni modificar documentos de negocio preexistentes de la base de
desarrollo. Una prueba que escribe limpia **solo por los `_id` que insertó**. Los tests de Mongo usan su propia base
`factum_test_<guid>`. No tocar la carpeta `Storage:DataDirectory` ajena a la prueba. No escribir nada en el celular
conectado.

### 14.2 `implementer-frontend` (solo `client/`)

Skills obligatorios: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como criterio, sin
3D) y `web-design-guidelines` (autochequeo). Según corresponda, también `ui-styling` y
`mblode-agent-skills-ui-animation`. Leer `client/AGENTS.md` (Next.js 16) antes de tocar código. App: **solo
`client/`**; `agent-ui/` no se toca.

- [ ] `lib/device-details.ts` (§10.1): tipos de clave y motivo, etiquetas, `formatDuration`, `describeDeviceSkew`,
      `describePcServerSkew`, `exceedsThreshold`, `unavailableCount`, `isLoopbackAgent`, `probeServerClock`,
      `toSaveRequest` y los modelos de vista.
- [ ] `lib/agent.ts`: tipos de §8.1, `supportsDeviceDetails`, `getDeviceDetails` y `getDeviceClock`. Documentar
      `device_details_v1` en el comentario de `AgentHealth`.
- [ ] `lib/api.ts`: tipos de §8.2–§8.4, `Case.device_details`, eventos y las 4 funciones. `types/index.ts`:
      re-exports.
- [ ] `hooks/usePublicConfig.ts`: `clockSkewWarningSeconds` (default 60).
- [ ] `hooks/useDeviceDetails.ts` (§10.1).
- [ ] `components/device-details/DeviceClockLine.tsx`, `DeviceDetailsPanel.tsx` y `DeviceDetailsDialog.tsx`
      (§10.2, §10.6, §10.8).
- [ ] `CaseSummaryCard` + `CaseFormStep`:
  - [ ] fila de reloj, aviso, botón "Ver datos técnicos" y avisos de error/Tatana viejo;
  - [ ] "Serial" → "Id. de conexión";
  - [ ] "N.° de serie" en el plegable.
- [ ] `app/dashboard/page.tsx`:
  - [ ] hook;
  - [ ] lectura al entrar al paso 2 (create);
  - [ ] `registerDeviceDetails` en el alta (§10.3);
  - [ ] retoma (§10.4);
  - [ ] "Volver a leer" y SSID en edit;
  - [ ] `deviceDetailsNotice` en el paso 3;
  - [ ] etapa `clock` en `handleGenerateAgent` (§10.5).
- [ ] `GenerateStep`: la etapa `clock` condicional.
- [ ] `CaseDetailContent`: sección "Datos técnicos del dispositivo" o la nota "Sin ficha técnica…"; "Serial" → "Id. de
      conexión".
- [ ] `lib/case-events.ts` (+ `CaseTimeline` si hace falta pasar el umbral): íconos, títulos, tono y la línea
      secundaria (§10.7).
- [ ] No mostrar el SSID en ningún texto de trazabilidad. No loguearlo en la consola.
- [ ] Reenviar `clock.pc_before`/`pc_after`/`device_time` como strings sin parsear (§8.1).
- [ ] Dejar en el progress la tabla de `formatDuration`/`describeDeviceSkew` revisada contra §5.3.

**Regla dura de datos:** la prueba en el navegador contra la base de desarrollo crea casos **propios**. No edita ni
genera casos del usuario. No mueve tarjetas del Project ni toca `progress/sesiones/`.

---

## 15. Verificación

### 15.1 Antes de declararse `done`

**`implementer-backend`**
```bash
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test  server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
dotnet test  server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
python3 ops/plantilla/build_plantilla_v4.py --check "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" server/src/Factum.Backend/Templates/plantilla_informe_v4.docx
python3 ops/plantilla/build_plantilla_v6.py --check server/src/Factum.Backend/Templates/plantilla_informe_v4.docx server/src/Factum.Backend/Templates/plantilla_informe_v6.docx
```

Más: `Invoke-Pester deploy/windows/tests` si `pwsh` está disponible; si no, se deja constancia. Sin warnings nuevos en
el build. Opcional: una prueba en vivo contra el SM-M127F con `curl localhost:8765/devices/R58RA3R5AGA/details`, con
Tatana corriendo desde la rama, adjuntando la respuesta **sin** el SSID.

**`implementer-frontend`**
```bash
cd client && npx tsc --noEmit
```

Más una pasada en el navegador con Tatana en mock (`MockDeviceClockSkewSeconds=90`), si el entorno lo permite.

### 15.2 Prueba manual del usuario

Preparación: backend, web y **Tatana compilado desde esta rama**. Samsung SM-M127F conectado.

1. **Alta con ficha.** Wizard → elegir el Samsung → paso 2.
   - La fila de reloj dice "Adelanta ~10 s respecto de esta PC (± 1 s)" (o lo que mida ese día).
   - "Ver datos técnicos" muestra serie `R58RA3R5AGA`, Android 11 (API 30), compilación `RP1A.200720.012`, parche
     2021-10-01, kernel 4.19.111, SIM ABSENT, arranque `orange` / `0` / `unlocked`, zona
     `America/Argentina/Buenos_Aires` y hora automática activada.
   - El "Id. de conexión" se ve aparte.
2. **Guardar el caso.**
   - El detalle del caso muestra la ficha con la fecha de lectura.
   - La trazabilidad muestra "Se registraron los datos técnicos…" con el desfase y "N datos no disponibles", y no
     muestra SSID.
3. **Wifi.** Conectar el celular a una wifi y "Volver a leer" en el paso 2 (edit).
   - Aparece "Conectado a «…»" con la casilla desmarcada.
   - Generar sin marcarla: el informe dice "Conectado (el nombre de la red no se consigna)".
   - (En otro caso) marcarla y generar: el informe dice "Conectado a la red «…»".
4. **Informe.** Generar con el celular conectado.
   - Aparece la etapa "Midiendo el reloj del dispositivo…".
   - En la sección "Elementos ofrecidos" del DOCX: la aclaración, la segunda ficha con las 10 filas y el párrafo del
     reloj con las mediciones de inicio y de cierre.
   - La trazabilidad suma "Se midió el reloj del dispositivo al generar".
5. **Generar desconectado.** Crear otro caso, desenchufar el celular antes de generar y generar. No hay error y el
   informe muestra solo la medición de inicio.
6. **Umbral.**
   - Con Tatana en mock y `Agent:MockDeviceClockSkewSeconds=90`, crear un caso: aviso ámbar no bloqueante en el paso 2
     y en el detalle, y en el informe la frase "Se deja constancia de que el reloj del dispositivo presentaba una
     diferencia de 1 min 30 s…".
   - Con `Report__ClockSkewWarningSeconds=120` en el backend, el mismo caso no muestra ni el aviso ni la frase.
7. **Tatana viejo.** Con Tatana 1.6.0, crear un caso: aviso "Actualizá Tatana…". El caso se crea, y el informe sale
   sin el bloque y sin la etapa de reloj.
8. **Casos anteriores.**
   - Un caso generado antes de la HU muestra "Sin ficha técnica (caso anterior a esta función)" y su informe no cambia.
   - Un borrador previo, retomado con el celular conectado, obtiene la ficha con la fecha de hoy.
9. **iPhone** (si hay uno): la ficha muestra la serie, iOS y la compilación, "No aplica" en parche y arranque, "No
   disponible" en kernel y wifi, y el desfase.

   Confirmar que no aparecen ICCID, IMSI ni el número de línea en la ficha. El número de línea sigue precompletando
   "Línea", como hoy.
10. **Nada se escribió en el celular.** Los ajustes de hora y wifi del celular siguen igual que antes de la prueba.

## 16. Regla dura de datos (AGENTS.md)

- La base de desarrollo puede tener casos cargados a mano por el usuario: **ningún implementador borra ni modifica
  documentos de negocio preexistentes**.
- Las pruebas limpian **solo por los `_id` que insertaron**, nunca con filtros amplios.
- Los tests automáticos de Mongo usan una base propia efímera (`factum_test_<guid>`).
- La carpeta `Storage:DataDirectory` y las carpetas de evidencia de Tatana tienen evidencia real: no se borra nada que
  no haya creado la propia prueba.
- **El celular conectado es de solo lectura:** ningún comando que escriba, instale, cambie ajustes o sincronice la
  hora.
