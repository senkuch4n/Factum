# QA manual — HU #79 ficha técnica del dispositivo (`metadatos-dispositivo`, SDD §15.2)

- **Fecha:** 2026-10-14, de 16:10 a 16:20 UTC (13:10 a 13:20, UTC-03:00).
- **Rama:** `feat/metadatos-dispositivo`, sin commitear. No se cambió código ni se hicieron commits.
- **Entorno:**
  - backend compartido en :8080 (Development, `factum_dev`, umbral 60 s, sellado activo);
  - Tatana real en :8765 (`0.0.0.0-dev`, con `device_details_v1`);
  - Samsung SM-M127F por USB (`R58RA3R5AGA`, Android 11), **solo lectura**: no se mandó ningún input ni se cambió
    ningún ajuste.
- **Instancias propias** (ya bajadas):
  - Tatana mock en :8766: copia del build de la rama, `--mock --port 8766 --data <scratch>/mockdata`, con
    `Agent__MockDeviceClockSkewSeconds=90` y `Agent__EvidenceDirectory=<scratch>/mockevid`. La carpeta de evidencia
    propia evita escribir en `~/Factum/Evidencia`.
  - Backend en :8081, solo para la variante de umbral: `PORT=8081`, `Storage__DataDirectory` propio y
    `Report__ClockSkewWarningSeconds=120`.
- **Método:** flujo `agent` por API, en el mismo orden que la web:
  - `handleSaveCase` → `registerDeviceDetails` → `probeServerClock`;
  - `handleGenerateAgent`: `prepare` → medición de cierre → `zip` → `finish` → `commit`.
- **Evidencia:** la captura es un PNG generado en la PC y subido por `POST /cases/{id}/evidence/upload` de Tatana.
  Así no hizo falta despertar ni tocar el celular.
- **Usuario QA:** DNI `99000079` (Auth `dev`), con un perfil de perito propio que se borró al final.
- **SSID:** el Samsung no estaba en wifi (`wifi_state=disconnected`), así que no hubo SSID real que enmascarar. El
  único SSID que aparece es el del mock (`"Red de prueba"`), que es ficticio.

## Resultado

| # | Qué | Resultado |
|---|---|---|
| 1 | Ficha al alta con el Samsung real: campos, "No disponible" con motivo, reloj dispositivo↔PC, PC↔servidor | **OK** |
| 2 | "Volver a leer" en borrador: la lectura anterior queda en la trazabilidad, sin SSID | **OK** |
| 3 | Generar: segunda medición de reloj; DOCX con la aclaración, las 10 filas, SSID ausente y arranque en crudo | **OK** |
| 4 | SSID con el flag marcado y sin marcar (mock) | **OK** |
| 5 | Desfase de 90 s (mock): frase neutra en el informe | **OK** (el aviso ámbar de la web no se vio, ver 8) |
| 5b | Umbral en 120 s (backend :8081): ni la frase ni el aviso | **OK** |
| 6 | Caso sin ficha: informe sin el bloque, igual que hoy | **OK** |
| 6b | Generar "desconectado" (SDD paso 5): sin error y solo con la medición de inicio | **OK** |
| 7 | Tatana viejo (sin `device_details_v1`) | **OK, por lectura de código** + equivalencia con el paso 6 |
| 8a | Casos anteriores: los 5 casos preexistentes no tienen `DeviceDetails` | **OK** (dato en Mongo; el texto de la web no se vio) |
| 8b | El celular no se modificó (SDD paso 10) | **OK** |
| 9 | Lo visual (Claude in Chrome) | **No verificable acá** |
| 10 | Wifi real conectado en el Samsung (SDD paso 3) | **No verificable acá** (no se puede cambiar el wifi del celular) |
| 11 | iPhone (SDD paso 9) | **No verificable acá** (no hay iPhone) |
| 12 | Borrador previo retomado con el celular (SDD paso 8, efecto §10.4 de la web) | **No verificable acá** (es lógica de la web y no hubo sesión de navegador) |

**11 OK / 0 FALLA / 4 no verificables.** No hay hallazgos de severidad Crítica, Alta ni Media. Abajo van las
observaciones de severidad Baja o informativas.

---

## Paso 1 — Ficha al alta · OK

Caso `QA-FICHA-01` (`854db494-eb29-47cb-8835-4d1f988f3544`), creado con el Samsung de `GET :8765/devices`.

- `POST /api/cases` → 201. `device_details` vale `null` hasta registrar la ficha, como en la SDD: la ficha va
  después del alta.
- `GET :8765/devices/R58RA3R5AGA/details` → 200 en 0,96 s; las demás lecturas tardaron entre 0,9 y 1,7 s.
- `GET /api/time`: `pc_before 16:13:01.918Z`, `server_time 16:13:01.923912Z`, `pc_after 16:13:01.926Z`.
- `PUT /api/cases/{id}/device-details` (`include_ssid_in_report: false`) → 200 en 0,14 s.

Campos guardados (`device_details.fields`), recortados:

```
serial_number R58RA3R5AGA            os_version 11            os_api_level 30
build_id RP1A.200720.012             security_patch 2021-10-01
build_fingerprint samsung/a12sub/a12s:11/RP1A.200720.012/A127MUBU3AUJ5:user/release-keys
kernel Linux localhost 4.19.111-22565658 #1 SMP PREEMPT Thu Nov 11 01:08:09 KST 2021 aarch64
network_operator {reason: empty}     network_operator_numeric 722310
sim_operator {reason: empty}         sim_operator_numeric {reason: empty}     sim_state ABSENT
verified_boot_state orange           bootloader_locked 0      vbmeta_device_state unlocked
timezone America/Argentina/Buenos_Aires   auto_time 1   auto_time_zone 1
wifi_state disconnected              wifi_ssid {reason: not_connected}
```

- Coincide con lo esperado en §15.2 paso 1: serie, Android 11 (API 30), compilación, parche, kernel 4.19.111, SIM
  ABSENT, arranque `orange`/`0`/`unlocked`, zona y hora automática.
- "No disponible" con motivo:
  - `empty` en el operador de red y en los dos de la SIM;
  - `not_connected` en el SSID.
- `unavailable_count = 3`: `not_connected` no cuenta, como dice §4.4.

Reloj:

```
clock_start: device_minus_pc_ms 10136, margin_ms 42, resolution_ms 1,
             pc_minus_server_ms -2, pc_server_margin_ms 5, pc_server_reason null
```

- La API recalcula el desfase desde `pc_before`/`pc_after`/`device_time`. En la relectura del paso 2, Tatana informó
  10142/48 y la API guardó 10142/48: coinciden.
- En la web eso se leería como "Adelanta 10 s respecto de esta PC (± 1 s)".
- PC↔servidor: −2 ms, por debajo del umbral, así que no se escribe en el informe (D5).

Trazabilidad: `device_details_recorded {unavailable_count: 3, clock_kind: start, device_minus_pc_ms: 10136,
clock_margin_ms: 42, pc_minus_server_ms: -2}`. El evento no lleva SSID ni valores de la ficha.

## Paso 2 — Volver a leer · OK

- Segunda lectura con el mismo serial y `PUT …/device-details` sin `include_ssid_in_report`, como hace el modo
  `edit` → 200.
- `read_at` y `clock_start` se actualizaron. `include_ssid_in_report` se conservó en `false`.
- La lectura anterior queda en la trazabilidad (Mongo, `case_events`):

```
device_details_recorded  {UnavailableCount 3, ClockKind start, DeviceMinusPcMs 10136, ClockMarginMs 42, PcMinusServerMs -2}
device_details_recorded  {UnavailableCount 3, Reread true, ClockKind start, DeviceMinusPcMs 10142, ClockMarginMs 48, PcMinusServerMs -3}
```

- En el JSON de `/events` no aparecen "ssid" ni "wifi".
- Otro serial (`connection_id: "OTRO123"`, probado en QA-FICHA-05) → 409 `device_mismatch`, "El dispositivo conectado no
  es el del caso."

## Paso 3 — Generar el informe · OK

Mismo caso. `prepare` → `GET :8765/devices/R58RA3R5AGA/clock` (0,39 s) → `PUT …/device-details/clock` (200) → `zip` →
`finish` (1,02 s) → `commit`.

- **Segunda medición:** `clock_close {kind: close, device_minus_pc_ms 10144, margin_ms 49, pc_minus_server_ms -1}`. El
  evento es `device_clock_measured {clock_kind: close, …}`.
- **DOCX** (`informe_pericial_QA_FICHA_01_QA_Denunciante.docx`, plantilla v6, pasado a texto), en la sección
  "ELEMENTOS OFRECIDOS", después de "Titular":

```
Datos técnicos obtenidos automáticamente por Factum mediante consultas de solo lectura al dispositivo el 14/10/2026 a las 13:13 horas:
| Sistema operativo: | Android 11 (nivel de API 30) |
| Compilación: | RP1A.200720.012; huella: samsung/a12sub/a12s:11/RP1A.200720.012/A127MUBU3AUJ5:user/release-keys |
| Parche de seguridad: | 01/10/2021 |
| Número de serie: | R58RA3R5AGA |
| Identificador de conexión: | R58RA3R5AGA (identificador de conexión ADB; no es necesariamente el número de serie) |
| Kernel: | Linux localhost 4.19.111-22565658 #1 SMP PREEMPT Thu Nov 11 01:08:09 KST 2021 aarch64 |
| Operador / SIM: | Red: No disponible (722310); SIM: No disponible, estado ABSENT |
| Estado de arranque: | verifiedbootstate: orange; flash.locked: 0; vbmeta.device_state: unlocked |
| Zona horaria: | America/Argentina/Buenos_Aires; hora automática: activada; zona horaria automática: activada |
| Wifi: | Activado, sin conexión |
Reloj del dispositivo al inicio (medición registrada el 14/10/2026 a las 13:13:19): el dispositivo adelantaba 10 s respecto de la estación de trabajo (± 1 s).
Reloj del dispositivo al cierre (medición registrada el 14/10/2026 a las 13:13:52): el dispositivo adelantaba 10 s respecto de la estación de trabajo (± 1 s).
Zona horaria configurada en el dispositivo: America/Argentina/Buenos_Aires.
```

- Hay una sola tabla con las 10 filas.
- El estado de arranque va en crudo y sin calificar (D9).
- No hay SSID y el wifi dice "Activado, sin conexión".
- No queda ningún placeholder sin resolver (`{…}`).

## Paso 4 — SSID con el flag (mock) · OK

Mock :8766, dispositivo `EMU001`, `wifi_state=connected`, `wifi_ssid="Red de prueba"`.

| Caso | Flag | Línea "Wifi" del DOCX | ¿"Red de prueba" en el DOCX? |
|---|---|---|---|
| `QA-FICHA-02` (`a3e1643e-…`) | `false` (default) | `Conectado (el nombre de la red no se consigna)` | no (0) |
| `QA-FICHA-03` (`665bfe72-…`) | `true` por `PUT …/device-details/options` | `Conectado a la red «Red de prueba»` | sí (1) |

- `PUT …/options` con cuerpo `{}` → 400 `invalid_device_details`, como dice §7.1.
- `PUT …/options` **no** genera evento, y el SSID no aparece en ningún evento de los dos casos (D7).

## Paso 5 — Desfase de 90 s · OK

- Mock con `MockDeviceClockSkewSeconds=90`: `device_minus_pc_ms 90000`, `margin_ms 5`, tanto al inicio como al cierre.
- DOCX de QA-FICHA-02 y QA-FICHA-03:

```
Reloj del dispositivo al inicio (…13:14:33): el dispositivo adelantaba 1 min 30 s respecto de la estación de trabajo (± 1 s).
Reloj del dispositivo al cierre (…13:14:34): el dispositivo adelantaba 1 min 30 s respecto de la estación de trabajo (± 1 s).
Se deja constancia de que el reloj del dispositivo presentaba una diferencia de 1 min 30 s respecto de la estación de trabajo; las horas visibles en las capturas corresponden al reloj del dispositivo.
```

- La frase es neutra, sale una sola vez y no sugiere corregir la hora.
- El aviso ámbar del paso 2 y del detalle es de la web: no se vio (paso 9).

### 5b — Umbral en 120 s · OK

- `GET :8081/api/config/public` → `"clock_skew_warning_seconds": 120`.
- Caso `QA-FICHA-06` (`9001b9c3-…`), generado por :8081 con el mismo mock de 90 s: el informe trae las dos líneas de
  reloj y la de zona horaria, **sin** la frase "Se deja constancia…".

## Paso 6 — Sin ficha · OK

Caso `QA-FICHA-04` (`df5122f0-…`), mock, sin `PUT …/device-details`.

- `PUT …/device-details/clock` → 409 `no_device_details`.
- En la generación se omitió la etapa de reloj: la condición `cas.device_details` es falsa.
- El DOCX no tiene el bloque. Un `diff` de texto contra QA-FICHA-02 da:
  - solo las 15 líneas del bloque;
  - los datos propios de cada caso (referencia, hora, sellos);
  - la línea de sistema operativo de "Operaciones realizadas" (ver O-1).
- Misma cantidad de párrafos vacíos (48) en los dos XML: el bloque borrado no deja huecos. QA-FICHA-04 tiene una tabla
  menos.

### 6b — Generar desconectado (SDD paso 5) · OK

Caso `QA-FICHA-05` (`d792439e-…`).

- La ficha se leyó del Samsung real.
- La evidencia y la generación fueron por el mock, donde `R58RA3R5AGA` no está en `/devices`. Simula un celular
  desenchufado sin tocar el USB.
- La etapa de reloj se omitió sin error. El informe trae el bloque con **solo** "Reloj del dispositivo al inicio".

## Paso 7 — Tatana viejo · OK (por código)

No se pudo apuntar la web a un Tatana sin la capacidad: `AGENT_URL` es fijo y no había Tatana 1.6.0. Por lectura de
`client/`:

- `agent.supportsDeviceDetails()` = `hasCapability("device_details_v1")` sobre `/health`, cacheado
  (`lib/agent.ts`).
- `useDeviceDetails.read()`: sin la capacidad, `status: "unsupported"` y no se llama a `/details`.
- `DeviceClockLine` en `unsupported`: "Actualizá Tatana para registrar los datos técnicos del dispositivo.", con ícono
  `Info`, sin bloquear.
- `registerDeviceDetails` devuelve `{status: "unsupported"}` antes de leer. `handleSaveCase` solo avisa en
  `read_failed`, así que el alta sigue a `go(3)` en silencio.
- `handleGenerateAgent` exige `await agent.supportsDeviceDetails()` para la etapa de reloj; sin la capacidad, la omite.
- `canReread` excluye `unsupported`.
- El resultado en el backend es un caso sin `device_details`: es el mismo caso que el paso 6, cuyo informe sale sin el
  bloque.

## Paso 8 — Casos anteriores y celular

- **8a.** Los 5 casos preexistentes de `factum_dev`, todos generados, no tienen `DeviceDetails` en Mongo. No se
  tocaron: solo se leyeron `_id`, `Status` y la presencia del campo. El texto "Sin ficha técnica (caso anterior a
  esta función)" es de la web y no se vio.
- **8b.** Todo lo que corrió contra el Samsung fueron las lecturas de Tatana (`getprop`, `uname`, `settings get`,
  `cmd wifi status`, `date`). `auto_time`/`auto_time_zone` dieron `1`/`1` y `wifi_state` dio `disconnected` en todas
  las lecturas, de 16:10 a 16:15 UTC. No hubo `input`, `keyevent` ni capturas de pantalla.
- **Logs:**
  - Tatana loguea solo `Ficha técnica leída (android, 3 no disponibles, N ms)`;
  - el backend loguea `Ficha técnica registrada para caso … (android, 3 no disponibles)` y `Reloj del dispositivo
    medido al generar…`;
  - un `grep` de `Red de prueba`, `orange` y `America/Argentina` en los logs de los dos procesos y del mock da 0
    coincidencias, fuera de la lista de seriales que Tatana ya logueaba antes.

## Paso 9 — Lo visual · No verificable acá

- Se abrió una pestaña nueva de Claude in Chrome en `http://localhost:3000/dashboard`. El navegador **no tenía sesión
  iniciada**: redirigió al login con "Tu sesión terminó. Volvé a ingresar."
- Por la regla de la skill (no escribir contraseñas en el navegador ni saltear el login), no se inició sesión y la
  pestaña se cerró.
- Queda pendiente para el usuario:
  - fila de reloj y "Ver datos técnicos" en el paso 2;
  - el aviso ámbar con el mock de 90 s;
  - el detalle del caso y la trazabilidad;
  - "Id. de conexión";
  - el aviso de Tatana viejo.

---

## Observaciones (sin FALLA)

| ID | Severidad | Qué | Detalle |
|---|---|---|---|
| O-1 | Baja (solo mock) | El fixture del mock no coincide con el dispositivo mock | El mock lista `EMU001` como "Pixel 7 (mock)", Android 14, pero su ficha devuelve los valores del SM-M127F (`serial_number R58RA3R5AGA`, Android 11). Por eso, en el informe de un caso mock con ficha, "Operaciones realizadas" dice "Google Pixel 7 (mock), sistema operativo Android 11", y sin ficha dice "Android 14". El comportamiento es el especificado (§9.4: con ficha manda `os_version`); solo confunde al probar con el mock. Opcional: alinear el `MockDevice` o el fixture. `server/src/Factum.Agent/Services/AdbService.cs` (`MockDevice`) y `Services/DeviceDetails/DeviceDetailsService.cs` (`MockAndroidProps`). |
| O-2 | Info | Las partes faltantes de "Operador / SIM" no llevan motivo | `Red: No disponible (722310); SIM: No disponible, estado ABSENT`. Coincide con §9.3 ("las partes faltantes salen como No disponible"); se anota porque el resto de las filas sí dicen el motivo. |
| O-3 | Info | Un `prepare` abandonado no molesta | Un primer intento de generación del script de QA quedó cortado después de `prepare`. El segundo `prepare` tomó el control y la medición de cierre (posterior) entró al informe. No se probó a propósito el filtro `ClockClose.RecordedAt < pending.CreatedAt`. |
| O-4 | Info | Caché de capacidades | `hasCapability` cachea `/health`. Si el perito actualiza Tatana con la web abierta, puede seguir viendo "Actualizá Tatana…" hasta recargar. No se verificó en vivo. |

## Limpieza

**Mongo (`factum_dev`)**: se borró solo por `_id`. Los conteos finales son iguales al baseline (`cases 5`,
`case_events 0`, `case_timestamps 0`, `catalog_entries 10`, `expert_profiles 1`, `agent_events 17`).

| Colección | Borrados | `_id` |
|---|---|---|
| `cases` | 6/6 | `854db494-eb29-47cb-8835-4d1f988f3544` (01), `a3e1643e-bafa-4041-9b81-4eec8ee4c77e` (02), `665bfe72-aa08-4ae6-8f2c-b6cab6029ebf` (03), `df5122f0-2442-4478-8b52-e31d8e439b81` (04), `d792439e-7f60-43c6-80ee-29b9e0367a6a` (05), `9001b9c3-62ee-4adf-991e-98be2f94b053` (06) |
| `case_events` | 46/46 | los nuevos (no estaban en el baseline) con `CaseId` en esos 6 casos |
| `case_timestamps` | 18/18 | ídem |
| `catalog_entries` | 5/5 | `35df07f5-…`, `9b965f52-…`, `a0577ffc-…`, `d9424e54-…`, `d9ec2389-…` (los cinco con `OwnerDni 99000079`) |
| `expert_profiles` | 1 | `99000079` |
| `agent_events` | 0 | la prueba no creó ninguno |

**Carpetas:**
- `server/src/Factum.Backend/dev-data/cases/<id>` de los casos 01 a 05: borradas. El 06 vivía en el `DataDirectory`
  de :8081.
- `~/Factum/Evidencia/QA_FICHA_01_854db494`: borrada. Es la única carpeta que creó el Tatana real.
- En el scratchpad: `mockevid/`, `mockdata/`, `dd8081/`, la copia del binario del mock y el archivo del token.

**Procesos:** el Tatana mock de :8766 y el backend de :8081 están bajados. Los de :8080, :8765 y :3000 no se tocaron y
siguen respondiendo 200.

**Celular:** sin cambios (paso 8b).
