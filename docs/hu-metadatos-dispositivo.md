# HU: Más metadatos automáticos del dispositivo (serie, build, kernel, parche, wifi) y diferencia de reloj celular/PC

**Slug:** `metadatos-dispositivo` · **Issue:** #79 · **Rama:** `feat/metadatos-dispositivo`
**Apps afectadas (preliminar, lo confirma la SDD):** agente Tatana (`server/src/Factum.Agent`: `AdbService`,
`IosService` + `Services/Ios/ios_helper.py`, modelo `Device`, endpoint nuevo de lectura bajo demanda), API
(`server/src/Factum.Backend`: `Models/DeviceInfo.cs`, `DTOs/CaseDtos.cs` `DeviceInfoDto`, `CaseService`,
`ReportValues`/`ReportService`, plantilla v6 vía `ops/plantilla/build_plantilla_v6.py`, `case_events`) y web
(`client/`: `lib/agent.ts`, `lib/api.ts`, `app/dashboard/page.tsx`, `DeviceConnect`, `CaseFormStep`,
`case-detail/CaseDetailContent`, `CaseTimeline`). `agent-ui/` sin cambios.

**Como** perito de parte que examina un celular
**quiero** que Factum lea y registre automáticamente más datos técnicos del dispositivo (número de serie, versión y
compilación del sistema, parche de seguridad, kernel, operador/SIM, estado de arranque, red wifi y zona horaria) y la
diferencia entre el reloj del celular y el de mi PC
**para que** el informe identifique sin ambigüedad el equipo examinado y deje constancia del desfase horario, que es un
punto que la contraparte cuestiona en juicio cuando compara horas de capturas, mensajes y archivos.

**Origen:** sugerencia de un perito de parte. En su proyecto académico la app capturaba automáticamente compañía
telefónica, IMEI, número de serie, ubicación, wifi actual, sistema operativo, kernel y modelo.

---

## Contexto

### Qué existe hoy (arqueología sobre `460fac0`)

**Qué lee Tatana del dispositivo.**
- **Android** (`AdbService.ListDevicesAsync` → `EnrichDeviceAsync`, `AdbService.cs` ~L785): por cada equipo en
  `adb devices -l` hace `getprop` de `ro.product.manufacturer`, `ro.product.model`, `ro.product.name`,
  `gsm.operator.alpha` y `ro.build.version.release` (parseado a `int`, así que "13" sale bien pero una versión tipo
  "8.1.0" queda en 0). El IMEI se intenta con `service call iphonesubinfo 1`; si no sale (lo habitual en Android 10+
  sin privilegios), queda el centinela `INGRESAR_MANUALMENTE` (`Device.ImeiManualEntry`).
- El `Serial` del `Device` es el **identificador de transporte de ADB** (primera columna de `adb devices`), no
  `ro.serialno` leído explícitamente. En muchos equipos coinciden, pero no siempre (en Samsung el número de serie de
  la etiqueta es otro valor).
- **iOS** (`ios_helper.py cmd_devices` → `IosService.ParseDevices`): de `lockdown.all_values` toma `DeviceName`,
  `ProductType` (traducido a nombre comercial con `FriendlyModel`), `ProductVersion`,
  `InternationalMobileEquipmentIdentity` y `PhoneNumber`. El `Serial` es el **UDID**. Rareza existente: en iOS el
  campo `Operator` del `Device` lleva el **número de teléfono** propio (`phone_number`), y el web lo usa para
  precompletar la línea (`isPhoneLike` en `client/src/lib/pericial.ts`, `dashboard/page.tsx` ~L981). No se lee
  `SerialNumber`, `BuildVersion`, `TimeZone` ni la hora del iPhone, aunque lockdown los expone.
- Esa lectura corre **en el sondeo de dispositivos cada 3 s** (`Program.cs` ~L358, evento WebSocket
  `devices_changed`) y en `GET /devices`. Sumar comandos ahí multiplica las llamadas a `adb`: los datos nuevos
  conviene leerlos bajo demanda (Notas de implementación).

**Cómo llega al backend.** Al crear el caso (paso 2 del wizard), `dashboard/page.tsx` (~L685) arma
`device: { serial, manufacturer, model, android_version, imei, platform, os_version, name }` desde el `Device` del
agente y lo manda en `POST /api/cases`. `CaseService` (~L269) lo copia sanitizado a `Case.Device`
(`Models/DeviceInfo.cs`: `Serial`, `Name`, `Manufacturer`, `Model`, `AndroidVersion`, `Imei`, `Platform`,
`OsVersion`, más el getter `OsLabel`). **El operador (`gsm.operator.alpha`) se lee pero no se persiste.** Después de
creado, el `device` no se vuelve a mandar: `PUT /api/cases/{id}` solo acepta `imei` (corrección manual).

**IMEI.** Si el agente no lo leyó, el perito lo escribe a mano en el paso 2 (`imeiOverride`), y la evidencia del IMEI
es la **captura de `*#06#`** marcada con el rol `imei_modelo` (obligatoria para generar:
`capture_roles.imei_modelo` en `pericial.ts`). Sale en el informe como `{capturasImeiModelo}`.

**Qué figura hoy en el informe** (README § "Placeholders de la plantilla del informe", `ReportValues.Placeholders`):
- Bloque del dispositivo (plantilla v4/v6): **Tipo** `{tipoDispositivo}`, **Marca y modelo**
  `{marcaModeloDispositivo}`, **IMEI** `{imeiDispositivo}`, **Línea** `{lineaDispositivo}`, **Titular**
  `{titularDispositivo}`.
- El texto por defecto de Operaciones (`ReportDefaultTexts.cs`) menciona "sistema operativo `{sistemaOperativo}`" y
  las Notas técnicas "zona horaria `{zonaHoraria}`" (la de `Report:TimeZone`, no la del celular).
- **No figuran** número de serie, compilación, parche, kernel, operador, estado de arranque, wifi, zona horaria del
  dispositivo ni ninguna medición de su reloj.

**En la web.** `DeviceConnect` muestra modelo, versión e IMEI del equipo conectado; `CaseDetailContent` muestra
"Sistema op." e "IMEI".

**Reloj.** No hay en el repo ninguna medición de la hora del dispositivo. Todas las horas del expediente salen del
reloj de la PC (nombres de archivo de las capturas, `estado.json`) o del servidor (`created_at`, eventos,
`{fechaInspeccion}`). Desde #78 hay además sellos RFC 3161 con hora de un tercero sobre los hashes, pero eso no dice
nada del reloj del celular, que es el que se ve en pantalla en las capturas (barra de estado, horas de mensajes).

### Por qué hace falta

1. **Identificación del equipo.** Marca, modelo e IMEI (a veces manual) no distinguen bien el equipo ni su estado de
   software. Número de serie, compilación y parche de seguridad lo fijan y permiten responder preguntas típicas
   ("¿el sistema estaba actualizado?", "¿estaba modificado / desbloqueado?").
2. **Diferencia de reloj.** Si el celular adelanta o atrasa respecto de la PC, las horas visibles en las capturas no
   coinciden con las de los nombres de archivo, la trazabilidad o el sello de tiempo. Medirla al inicio de la
   inspección y dejarla escrita evita que la contraparte lo use para desacreditar la línea de tiempo.

### Qué es nuevo

1. Tatana lee, **solo con comandos de lectura**, un conjunto ampliado de datos del dispositivo (D1) y mide la
   diferencia de reloj dispositivo/PC.
2. Esos datos se guardan en el caso como una "ficha técnica" con fecha y hora de lectura, y con constancia explícita
   de lo que no se pudo leer (D2).
3. Se muestran en la web y en el informe (D8, D9), con advertencia si el desfase supera un umbral (D6).
4. Los datos sensibles (wifi, números de línea, identificadores de SIM) se tratan según D7.

---

## Criterios de aceptación

> Algunos escenarios dependen de las dudas; donde aplica, se indica (Dn). Están redactados con las opciones
> recomendadas.

```gherkin
Feature: Metadatos automáticos del dispositivo y diferencia de reloj

  Background:
    Given un perito logueado en la web de Factum
    And Tatana corriendo en su PC
    And un celular conectado por USB y autorizado (depuración USB aceptada en Android / "Confiar" en iOS)

  Scenario: Lectura de la ficha técnica al dar de alta el caso (D4)
    Given el perito eligió el dispositivo en el paso 2
    When guarda el caso por primera vez
    Then Tatana lee la ficha técnica del dispositivo con comandos de solo lectura
    And el caso guarda la ficha con la fecha y hora de la lectura (reloj del servidor) y la plataforma
    And la ficha incluye los datos de D1 que el dispositivo haya permitido leer
    And la lectura no instala nada, no cambia ninguna configuración del celular y no escribe en él

  Scenario: Datos que el sistema operativo no deja leer (D2)
    Given un Android 10 o superior sin root
    When Tatana no puede leer el IMEI o el número de serie
    Then el dato queda registrado como "No disponible" con el motivo (por ejemplo "restringido por el sistema operativo")
    And nunca se completa con otro identificador presentado como si fuera ese dato
    And el IMEI sigue el flujo actual: campo manual y captura de *#06# con el rol imei_modelo

  Scenario: Número de serie vs identificador de conexión (D2)
    Given un Android donde ro.serialno no coincide con el identificador de ADB
    Then la ficha muestra el número de serie (ro.serialno) y, aparte y rotulado como tal, el identificador de conexión ADB
    And en iOS muestra el número de serie (SerialNumber) y el UDID por separado

  Scenario: Medición de la diferencia de reloj (D5)
    When Tatana mide la hora del dispositivo
    Then registra la hora de la PC justo antes y justo después de leer la del dispositivo
    And calcula la diferencia contra el punto medio, con su margen de error (mitad del tiempo de ida y vuelta más la resolución del reloj leído)
    And registra la zona horaria configurada en el dispositivo y si tiene la hora automática activada (cuando se puede leer)
    And la diferencia se expresa como "el dispositivo adelanta/atrasa N s respecto de la PC (± M s)"

  Scenario: Advertencia por desfase grande (D6)
    Given la diferencia absoluta supera el umbral configurado
    Then la web muestra un aviso no bloqueante en el paso 2 y en el detalle del caso
    And el informe consigna la diferencia con una redacción neutra que aclara que las horas visibles en las capturas corresponden al reloj del dispositivo
    And la inspección y la generación no se bloquean

  Scenario: Nueva medición del reloj al generar (D4)
    Given un caso con ficha técnica
    And el mismo dispositivo (mismo identificador) conectado al generar
    When el perito genera el caso
    Then se registra una segunda medición de reloj
    And el informe muestra las dos mediciones (inicio y cierre)
    And si el dispositivo no está conectado al generar, el informe muestra solo la primera, sin error

  Scenario: Datos sensibles (D7)
    Given el dispositivo está conectado a una red wifi
    Then la ficha registra si hay conexión wifi
    And el nombre de la red (SSID) se muestra en la web pero solo va al informe si el perito lo marca para ese caso
    And no se registran BSSID, dirección IP, MAC, ICCID ni IMSI

  Scenario: Constancia en el informe (D8, D9)
    Given un caso con ficha técnica
    When se genera el informe
    Then la sección del dispositivo incluye los datos de la ficha (los no disponibles como "No disponible")
    And incluye la diferencia de reloj con fecha y hora de cada medición en Report:TimeZone
    And los datos técnicos los leyó Factum automáticamente y así se aclara en el informe

  Scenario: Constancia en la trazabilidad
    When se registra una ficha técnica o una medición de reloj
    Then la trazabilidad del caso muestra el evento con la diferencia medida y la cantidad de datos no disponibles
    And el evento no incluye el SSID ni números de línea

  Scenario: Falla la lectura
    Given el dispositivo se desconecta o un comando falla durante la lectura
    Then el caso se crea igual con los datos básicos de hoy
    And la ficha queda incompleta con el motivo, y la web ofrece "Volver a leer" mientras el caso sea borrador y el mismo dispositivo esté conectado

  Scenario: Los datos automáticos no se editan a mano (D11)
    Given una ficha técnica registrada
    Then el perito no puede editar sus valores desde la web
    And las correcciones manuales siguen siendo las de hoy (IMEI, línea, tipo de dispositivo)

  Scenario: Casos existentes (D10)
    Given casos generados antes de esta HU
    Then no se modifican ni se regeneran sus informes
    And la web muestra "Sin ficha técnica (caso anterior a esta función)" en tono informativo
    And un borrador existente sin ficha la obtiene al retomarlo con el mismo dispositivo conectado
```

---

## Datos que se registran

> Lista con la opción recomendada de D1. Comandos y claves "a verificar" con equipos reales en la SDD.

| Dato | Android (ADB, solo lectura) | iOS (pymobiledevice3 / lockdown) | Obligatorio | Uso |
|---|---|---|---|---|
| Número de serie | `getprop ro.serialno` (puede venir vacío o restringido) | `SerialNumber` | No ("No disponible" + motivo) | Informe, web |
| Identificador de conexión | serial de `adb devices` (ya existe en `Case.Device.Serial`) | UDID (ya existe) | Sí | Web, informe (rotulado aparte), verificar que es el mismo equipo al re-medir |
| Versión del sistema | `ro.build.version.release` (como string, sin perder "8.1.0") + `ro.build.version.sdk` | `ProductVersion` | Sí | Informe, web |
| Compilación | `ro.build.id` + `ro.build.fingerprint` | `BuildVersion` | No | Informe, web |
| Parche de seguridad | `ro.build.version.security_patch` | — (iOS no lo expone aparte; la versión lo implica) | No | Informe, web |
| Kernel | `uname -a` o `cat /proc/version` | No disponible por lockdown (a verificar en la SDD) | No | Informe, web |
| Operador de red | `gsm.operator.alpha` (+ `gsm.operator.numeric`, MCC/MNC) | a verificar (`CarrierBundleInfoArray` u otro) | No | Informe, web |
| Operador de la SIM y estado | `gsm.sim.operator.alpha`, `gsm.sim.state` (doble SIM: valores separados por coma) | a verificar (`SIMStatus`) | No | Informe, web |
| Estado de arranque | `ro.boot.verifiedbootstate`, `ro.boot.flash.locked` / `ro.boot.vbmeta.device_state` (valores crudos) | — | No | Informe, web |
| Zona horaria del dispositivo | `persist.sys.timezone` | `TimeZone` | No | Informe, web |
| Hora automática | `settings get global auto_time` / `auto_time_zone` | a verificar | No | Informe (contexto del desfase) |
| Hora del dispositivo + PC antes/después | `date +%s` (o con ms si el `toybox` lo soporta) | `TimeIntervalSince1970` de lockdown | Sí (o "No disponible") | Diferencia de reloj |
| Diferencia de reloj y margen | calculada por Tatana | ídem | Sí si hubo hora | Informe, web, advertencia |
| Wifi: conectado sí/no | `cmd wifi status` / `dumpsys wifi` | a verificar (lockdown no da el SSID) | No | Web, informe |
| Wifi: SSID | ídem; puede salir `<unknown ssid>` o enmascarado | normalmente no disponible | No; al informe solo si el perito lo marca (D7) | Web, informe opcional |
| Fecha y hora de cada lectura (reloj del servidor) y origen (`tatana`, versión) | — | — | Sí | Trazabilidad, informe |
| Motivo por dato no disponible | — | — | Si falta | Web, informe |
| Elección del perito "incluir SSID en el informe" | — | — | No (default: no) | Informe |

No se registran: ubicación GPS (D3), BSSID, IP, MAC, ICCID, IMSI, lista de redes guardadas, cuentas del usuario ni
ningún contenido del dispositivo.

---

## Diseño UX/UI (`client/`, web)

**Paso 2 (selección de dispositivo y alta del caso).**
- La tarjeta del dispositivo (`DeviceConnect`) suma una sección colapsable "Datos técnicos" que se completa al leer la
  ficha. Mientras lee: esqueleto/spinner con "Leyendo datos del dispositivo…".
- Cada dato con su valor o "No disponible" en gris con tooltip del motivo.
- Fila destacada "Reloj del dispositivo": "Adelanta 2 min 13 s respecto de esta PC (± 1 s)" o "Coincide con esta PC
  (± 1 s)". Si supera el umbral (D6): aviso ámbar no bloqueante "El reloj del celular difiere más de N s del de esta
  PC. Las horas visibles en las capturas corresponden al reloj del celular; la diferencia queda registrada en el
  informe." No se le sugiere al perito corregir la hora del celular (eso modificaría el dispositivo).
- Wifi: "Conectado a «SSID»" con una casilla "Incluir el nombre de la red en el informe" (desmarcada por defecto, D7).
- Si la lectura falló: aviso con botón "Volver a leer" (solo con el mismo dispositivo conectado y el caso en borrador).

**Detalle del caso (`CaseDetailContent`).** Bloque "Datos técnicos del dispositivo" con la ficha, la(s) medición(es)
de reloj con fecha y hora, y el aviso si superó el umbral. Casos viejos: "Sin ficha técnica (caso anterior a esta
función)".

**Generación.** Si el dispositivo sigue conectado, el progreso suma una etapa corta "Midiendo el reloj del
dispositivo…". Si no está, se omite sin mensaje de error.

**Trazabilidad (`CaseTimeline`).** Evento(s) nuevos de ficha leída / reloj medido, con el estilo de
`client/src/lib/case-events.ts`.

**Informe DOCX** (backend, plantilla v6): ver D9.

**`agent-ui/` (Electron):** sin cambios.

**Accesibilidad:** los avisos no dependen solo del color (ícono + texto); la diferencia se escribe en palabras
("adelanta"/"atrasa"), no solo con signo.

---

## Fuera de alcance

- Sellado de tiempo RFC 3161 (#78, ya hecho).
- Exportación JSON del caso o de la ficha (#81).
- Grabación de la sesión completa (#80).
- Ubicación GPS del dispositivo (D3).
- Leer el IMEI o el número de serie con root, exploits, apps instaladas en el celular o códigos de servicio: el IMEI
  sigue documentándose con la captura de `*#06#`.
- Corregir o sincronizar la hora del celular, o cualquier otra escritura en el dispositivo.
- Determinar si el equipo está "rooteado" o "jailbreakeado" con heurísticas (`which su`, apps conocidas). Solo se
  consignan los valores crudos de estado de arranque que el sistema expone.
- Medir el reloj de la PC contra una fuente externa (NTP) más allá de lo que defina D5.
- Re-leer fichas de casos ya generados.
- Cambios en `agent-ui/`.

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- **No en el sondeo de 3 s.** La ficha y la hora se leen con un endpoint nuevo de Tatana bajo demanda (por ejemplo
  `GET /devices/{serial}/details`), no dentro de `EnrichDeviceAsync`, que corre en cada sondeo. Requiere declarar la
  capacidad en `/health` (como `case_seals_v1`) para que el web detecte un Tatana viejo y siga sin ficha.
- Todos los comandos son de lectura. Cada uno con timeout corto; uno que falla no aborta los demás.
- **Verificación con equipos reales pendiente:** no pude correr comandos contra celulares. La SDD (o el implementador)
  tiene que confirmar en al menos un Android ≥ 10 (idealmente Samsung y Pixel/Motorola) y un iPhone qué devuelve
  cada clave de la tabla, en especial `ro.serialno`, el SSID por `cmd wifi status`, `date` con milisegundos y las
  claves de operador/SIM de lockdown.
- Medición del reloj: tomar hora de la PC (UTC) antes y después del comando que lee la del dispositivo, usar el punto
  medio, margen = RTT/2 + resolución (1 s con `date +%s`). En iOS, `TimeIntervalSince1970` viene de lockdown.
- `AndroidVersion` es `int` y pierde versiones como "8.1.0": la ficha guarda la versión como texto (no cambiar el
  campo viejo).
- Contrato client ↔ server y client ↔ Tatana: la SDD define la sección **Contrato compartido** (JSON en
  `snake_case_lower`). La ficha la manda el web al backend; el backend valida y sanitiza cada valor
  (`SafeText.Clean`, largos máximos), igual que hoy con `device`.
- Plantilla: cambios vía `ops/plantilla/build_plantilla_v6.py`, nunca a mano; documentar los placeholders nuevos en
  el README.
- Regla dura de datos (`AGENTS.md`): no tocar casos ni eventos existentes en la base de desarrollo; las pruebas
  limpian solo sus `_id`.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **recomendada** con su porqué.

**D1. Qué datos entran en esta HU.**
- A) **La tabla de "Datos que se registran"**: número de serie, identificador de conexión, versión y compilación,
  parche, kernel, operador de red y de SIM, estado de arranque (crudo), zona horaria, hora automática, hora del
  dispositivo/diferencia de reloj y wifi (conectado + SSID con D7).
- B) Solo lo "duro" de identificación y reloj: serie, compilación, parche, kernel, zona horaria y diferencia de reloj.
  Sin wifi, operador ni estado de arranque.
- C) Todo lo de A más ubicación, IMSI/ICCID, MAC e IP.
- **Recomendada: A.** Cubre la lista del perito (compañía, serie, wifi, sistema, kernel, modelo) salvo ubicación
  (D3) e IMEI (que ya tiene su flujo). Todo se lee sin instalar nada y sin root. El estado de arranque es útil para
  responder "¿estaba modificado el equipo?" sin que Factum haga un juicio: se consignan los valores tal cual. C suma
  identificadores de abonado y de red que no ayudan a identificar el equipo y sí exponen datos personales.

**D2. Qué se hace cuando un dato no se puede leer** (IMEI y serie en Android 10+, kernel en iOS, SSID enmascarado).
- A) **Se registra "No disponible" con el motivo** (restringido por el sistema, sin respuesta, no aplica a la
  plataforma) y el informe lo muestra así.
- B) Se omite la línea del informe si falta el dato.
- C) Se completa con el valor más parecido (por ejemplo, el identificador ADB como "número de serie").
- **Recomendada: A.** Dejar escrito que se intentó y por qué no salió es más defendible que el silencio, y evita que
  parezca que el perito "se olvidó". C es engañosa: el identificador ADB puede no ser el número de serie de la
  etiqueta; por eso se muestran por separado y rotulados. Para el IMEI no cambia nada: sigue el campo manual y la
  captura de `*#06#`. **Sub-duda:** ¿el perito puede cargar a mano el número de serie (leído de la etiqueta o de
  Ajustes) cuando sale "No disponible", como hace con el IMEI? Recomendado: **no en esta HU** (se documenta con una
  captura de Ajustes > Acerca del teléfono, que ya se puede marcar en el anexo); se puede sumar después si lo piden.

**D3. Ubicación GPS.**
- A) **Fuera de alcance.**
- B) Leerla por ADB (`dumpsys location`, última ubicación conocida).
- C) Pedirle al perito que la cargue a mano.
- **Recomendada: A.** La ubicación del celular de una víctima o de un tercero es un dato personal sensible que no
  hace al objeto de la pericia, y la última ubicación de `dumpsys location` puede ser vieja o imprecisa (sería un dato
  confuso en un informe). Una lectura confiable necesitaría una app instalada en el celular, lo que modifica el
  dispositivo. Si el lugar de la inspección importa, lo consigna el perito en el texto del informe.

**D4. Cuándo se toman los datos.**
- A) Una sola ficha al dar de alta el caso (paso 2).
- B) **Ficha al dar de alta el caso + una segunda medición del reloj al generar, si el mismo dispositivo sigue
  conectado.**
- C) Una medición por cada evidencia capturada.
- D) Al generar solamente.
- **Recomendada: B.** El alta es el inicio de la inspección, que es lo que el informe ya fecha con `{fechaInspeccion}`.
  La segunda medición documenta si el reloj se movió durante la inspección (que puede durar horas o días desde
  `sesion-larga`) y cuesta una lectura. C agrega una lectura de `adb` por captura y ruido en la trazabilidad sin
  aportar mucho si las dos mediciones coinciden. D falla si el celular ya se devolvió. **Sub-duda:** si al retomar un
  borrador otro día el dispositivo está conectado, ¿se toma también una medición intermedia? Recomendado: no
  automáticamente; las dos mediciones (inicio y cierre) alcanzan.

**D5. Contra qué reloj se compara y cómo se presenta.**
- A) **Dispositivo vs PC del perito** (lo mide Tatana en la misma máquina, con margen de milisegundos) **más la
  diferencia PC vs servidor**, estimada por el backend al recibir la ficha (con margen de la latencia de red). El
  informe dice "el dispositivo adelanta/atrasa N s respecto de la estación de trabajo (± M s)" y, si la PC difiere
  del servidor más que el umbral, también eso.
- B) Solo dispositivo vs PC.
- C) Dispositivo vs una fuente horaria externa (NTP) consultada por Tatana.
- **Recomendada: A.** La PC es el reloj que estampa los nombres de las capturas, así que es la comparación que le
  sirve al lector. Pero la PC tampoco es una referencia confiable por sí sola: la contraparte puede objetar que la PC
  estaba mal. La comparación PC/servidor cierra la cadena con un reloj de un servidor (que suele sincronizarse por
  NTP) sin agregar dependencias. C suma una conexión saliente desde la PC del perito y una configuración más, cosa
  que A resuelve en buena parte. Formato: diferencia en palabras, al segundo, con margen, y la zona horaria del
  dispositivo al lado.

**D6. Umbral de advertencia por desfase.**
- A) Sin umbral: se informa siempre la diferencia y nunca se advierte.
- B) **Umbral fijo por defecto de 60 s, configurable por instalación** (por ejemplo `Report:ClockSkewWarningSeconds`).
  Por encima: aviso no bloqueante en la web y una frase neutra en el informe ("Se deja constancia de que el reloj del
  dispositivo presentaba una diferencia de … respecto de la estación de trabajo; las horas visibles en las capturas
  corresponden al reloj del dispositivo.").
- C) Umbral más fino (5 s).
- **Recomendada: B.** Un celular con hora automática suele estar a pocos segundos; un minuto ya se nota en las capturas
  (la barra de estado muestra minutos) y merece explicación. 5 s dispararía avisos por diferencias irrelevantes en un
  informe que se lee al minuto. La diferencia se informa siempre; el umbral solo agrega el aviso y la frase.

**D7. Datos sensibles (SSID de la red del celular de una víctima, número de línea, identificadores de SIM).**
- A) No leer nada de wifi.
- B) Registrar solo "conectado a wifi: sí/no".
- C) **Registrar estado y SSID; mostrar el SSID en la web y llevarlo al informe solo si el perito lo marca en ese caso
  (desmarcado por defecto). Nunca BSSID, IP, MAC, ICCID ni IMSI. El SSID no va a la trazabilidad.**
- D) SSID siempre en el informe.
- **Recomendada: C.** El SSID puede revelar dónde vive o trabaja la víctima ("Casa de Ana", nombre de un comercio) y
  el informe circula entre las partes. Pero en algunos casos es pertinente (por ejemplo, para vincular el equipo a un
  lugar), así que la decisión es del perito, caso por caso. **Sub-duda:** el número de línea que hoy iOS manda como
  "operador" y precompleta el campo Línea, ¿sigue igual? Recomendado: **sí, sin cambios** (el perito ya lo ve y lo
  puede borrar), pero la ficha técnica no lo vuelve a registrar como operador.

**D8. Dónde aparecen los datos.**
- A) **Informe (sección del dispositivo), web (paso 2 y detalle del caso) y trazabilidad.**
- B) Además, un archivo `dispositivo.txt`/`.json` dentro del ZIP de evidencia o en la carpeta del caso.
- C) Solo en la web.
- **Recomendada: A.** El informe es lo que llega al expediente y la web es donde el perito revisa antes de generar. Un
  archivo con los datos en formato estructurado se superpone con la exportación JSON (#81); conviene que esa HU lo
  incluya. Meter un archivo nuevo dentro del ZIP cambia su contenido y su tabla de hashes sin necesidad.

**D9. Cómo se presenta en el informe.**
- A) **Líneas nuevas en la sección del dispositivo**, debajo de Tipo / Marca y modelo / IMEI / Línea / Titular:
  Sistema operativo y compilación, Parche de seguridad, Número de serie, Identificador de conexión, Kernel, Operador /
  SIM, Estado de arranque, Zona horaria, Wifi (si corresponde), más un párrafo "Reloj del dispositivo" con las
  mediciones. Encabezadas por una aclaración breve: "Datos obtenidos automáticamente por Factum mediante consultas de
  solo lectura el dd/MM/yyyy a las HH:mm". Todo dentro de un bloque condicional (`{#fichaTecnica}`…) que desaparece
  en casos sin ficha. Además, tokens nuevos para los textos por defecto (por ejemplo `{diferenciaReloj}`).
- B) Una tabla aparte "Datos técnicos del dispositivo" en un anexo.
- C) Solo la diferencia de reloj en el informe; el resto en la web.
- **Recomendada: A.** Los datos quedan donde el lector ya busca la identificación del equipo, y el bloque condicional
  deja igual el informe de casos viejos. El fingerprint y el kernel son largos: si en la maqueta molestan en la
  sección principal, la SDD puede moverlos a una tabla en anexo (B) solo para esos dos. ¿Preferís que el estado de
  arranque figure en el informe o solo en la web? Recomendado: en el informe, con el valor crudo ("verifiedbootstate:
  green, bootloader bloqueado") y sin calificarlo.

**D10. Casos ya generados y borradores existentes.**
- A) **Generados: sin cambios** y "Sin ficha técnica (caso anterior a esta función)" en la web; el informe ya emitido
  no se toca. **Borradores: la ficha se lee al retomarlos si el mismo dispositivo (mismo identificador) está
  conectado**, con la fecha real de lectura a la vista; si no, generan sin ficha y el bloque del informe desaparece.
- B) Borradores sin ficha nunca la obtienen.
- C) Acción "Leer ficha" también en casos generados.
- **Recomendada: A.** Un caso generado ya tiene su informe y su hash (y desde #78, sello); agregarle datos después
  rompería la correspondencia. En un borrador la inspección sigue abierta, así que leer la ficha al retomarlo es
  legítimo, siempre con su fecha real (no se presenta como del inicio).

**D11. ¿El perito puede editar los datos automáticos?**
- A) **No:** son de solo lectura en la web; si un dato está mal o falta, se documenta con capturas o en el texto del
  informe. Puede "Volver a leer" mientras el caso es borrador y el dispositivo es el mismo, y la lectura anterior
  queda en la trazabilidad.
- B) Sí, editables como cualquier campo del formulario.
- **Recomendada: A.** Lo que da valor a estos datos es que los leyó la herramienta y no los tipeó nadie; si se pueden
  editar, el informe ya no puede decir "obtenidos automáticamente". Los campos manuales de hoy (IMEI, línea, tipo)
  no cambian.

**D12. Tatana viejo (sin el endpoint nuevo).**
- A) **El web detecta la capacidad en `/health`; si no está, el caso se crea como hoy, sin ficha, con un aviso
  "Actualizá Tatana para registrar los datos técnicos del dispositivo".**
- B) Bloquear el alta del caso hasta actualizar Tatana.
- **Recomendada: A.** Mismo criterio que las HU anteriores (`case_seals_v1`): no trabar una inspección por una mejora
  documental, y el autoupdate de Tatana resuelve la mayoría de los casos.

## Validación del usuario (2026-10-14)

El usuario validó **D1–D12 con la opción recomendada**, incluidas las sub-dudas:

- D1 = A (tabla completa de "Datos que se registran"); D2 = A ("No disponible" con motivo; sin carga manual del número de serie en esta HU); D3 = A (GPS fuera de alcance).
- D4 = B (ficha al alta + segunda medición de reloj al generar si el mismo dispositivo sigue conectado; sin medición intermedia automática al retomar).
- D5 = A (dispositivo vs PC + PC vs servidor, con margen); D6 = B (umbral 60 s configurable, aviso no bloqueante y frase neutra).
- D7 = C (estado y SSID; SSID al informe solo si el perito lo marca, desmarcado por defecto; nunca BSSID/IP/MAC/ICCID/IMSI; SSID fuera de la trazabilidad; el número de línea de iOS sigue precompletando "Línea" sin cambios).
- D8 = A (informe, web y trazabilidad; archivo estructurado queda para #81); D9 = A (líneas en la sección del dispositivo en bloque condicional; estado de arranque en el informe con valor crudo, sin calificar).
- D10 = A; D11 = A (solo lectura, "Volver a leer" en borrador); D12 = A (capacidad en `/health`, aviso si Tatana es viejo).
