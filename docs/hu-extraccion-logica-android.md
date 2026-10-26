# HU: Extracción lógica de Android (extraer y preservar, con acta y hashes)

**Slug:** `extraccion-logica-android` · **Issue:** #100 · **Rama:** `feat/extraccion-logica-android`
**Apps afectadas (preliminar, lo confirma la SDD):** agente Tatana (`server/src/Factum.Agent`: servicio nuevo de
extracción, `AdbService`, reuso de `DeviceDetailsService` y del cálculo de SHA-256, `OperationTracker`, `/health`),
API (`server/src/Factum.Backend`: registro de la extracción y asociación a un caso, `case_events`), web (`client/`:
sección nueva, `lib/agent.ts`, `lib/api.ts`, `AppNavbar`, detalle del caso). Posible proyecto Android nuevo para el
APK auxiliar (D3). `agent-ui/` sin cambios salvo lo que decida D9.
**HU relacionadas:** #101 (análisis de extracciones con IPED: consume lo que esta HU produce), #102 (extracción de
iPhone). Insumo técnico: `docs/spike-extraccion-logica.md` (2026-10-25).

**Como** perito que tiene un celular Android secuestrado o entregado conectado por USB a su PC
**quiero** hacer desde Factum una extracción lógica del dispositivo (contactos, mensajes, registro de llamadas,
calendario, lista de apps y los archivos del almacenamiento que elija) a una carpeta o un disco externo que yo elijo
**para que** quede una copia preservada, con hash por archivo, manifiesto y un acta de extracción que diga quién,
cuándo, con qué método y qué se modificó en el dispositivo, lista para analizarla después (#101) y para sostener la
cadena de custodia en el expediente, sin depender de una herramienta comercial como MOBILedit Forensic.

---

## Contexto

### Qué existe hoy (arqueología sobre `3abc3fc`)

**Factum hoy es una herramienta de inspección asistida, no de extracción.** El flujo central es el wizard de
`client/src/app/dashboard/page.tsx` (guía USB → conectar dispositivo → datos del expediente → captura → generar →
resultado): el perito navega el celular y Factum documenta lo que se ve (capturas, clips, grabación de la sesión
completa, ficha técnica) y emite un informe DOCX más un ZIP cifrado. No hay en el repo ningún flujo que copie en bloque
datos del celular, ni una entidad "extracción" en el backend, ni un proyecto Android.

**Lo que se puede reusar:**

- **Detección del dispositivo.** Tatana sondea `adb devices -l` cada 3 s (`AdbService.ListDevicesAsync` →
  `EnrichDeviceAsync`) y publica `devices_changed` por WebSocket; `GET /devices` da la lista. La web ya sabe mostrar
  el equipo conectado y su estado de autorización (`DeviceConnect`).
- **Ficha técnica y reloj (#79).** `GET /devices/{serial}/details` (capability `device_details_v1`,
  `Services/DeviceDetails/*`) lee con comandos de solo lectura número de serie, versión/compilación, parche, kernel,
  operador/SIM, estado de arranque crudo, zona horaria, wifi y la diferencia de reloj dispositivo/PC con margen. Es
  justo la identificación del equipo que necesita el acta.
- **Traer archivos del celular.** `DeviceFilesController` (`devices/{serial}/explorer`) lista carpetas
  (`ls -la`), busca por app (WhatsApp/Instagram/Facebook en `/sdcard`) y trae archivos puntuales con
  `adb exec-out cat` (`AdbService.PullFileAsync`) a la carpeta de trabajo, uno por uno y como evidencia del caso
  (`EvidenceClass.DevicePull`). Sirve de base, pero no está pensado para miles de archivos ni decenas de GB, no
  conserva fechas del dispositivo y no produce manifiesto propio.
- **Preservación en el flujo de inspección** (README § "Evidencia en la PC del perito"): la evidencia queda en la PC
  del perito (`<Agent:DataDirectory>/cases/<id>/`), el backend guarda **solo un manifiesto** (nombre, tamaño,
  SHA-256, ruta de origen, PC) con `PUT /api/cases/{id}/evidence`, Tatana verifica cada archivo contra ese manifiesto
  y arma el ZIP AES-256 (`CaseZipService`, `Services/Evidence/EvidenceZip.cs`, ZIP64 verificado con 10 GiB) en
  `<Agent:EvidenceDirectory>/<causa>_<id8>/`. **Ningún byte de evidencia llega al servidor.**
- **Sellado de tiempo RFC 3161 (#78).** El backend sella hashes (nunca contenido) con una TSA y guarda los `.tsr`;
  Tatana los copia a `sellos/` con un `LEEME-verificacion.txt` (`case_seals_v1`).
- **Trazabilidad.** `case_events` en el backend y `client/src/lib/case-events.ts` en la web (p. ej.
  `device_details_recorded`).
- **Operaciones largas.** `OperationTracker` impide que el autoupdate de Tatana corte una sesión larga (grabaciones).
  Una extracción de horas tiene que registrarse ahí.
- **Precedente de modificación del dispositivo documentada:** la grabación de sesión pone
  `stay_on_while_plugged_in = 7` y restaura el valor original al terminar (README). Hasta hoy es lo único que Factum
  escribe en un celular.
- **Navegación de la web:** `AppNavbar` tiene dos secciones (`Inspecciones` → `/dashboard`, `Métricas` →
  `/metricas`). No hay lugar para un flujo largo fuera del wizard.
- **Despliegues.** Tatana corre nativo (Windows, macOS, Linux) y, en el piloto de fiscalía, **en Docker** con solo
  `~/Factum/Evidencia` montada (`docs/tatana-docker.md`): desde el contenedor no se ve cualquier carpeta ni un disco
  USB enchufado después, lo que condiciona "el destino lo elige el usuario" (D9).

### Por qué hace falta

El producto compite con MOBILedit Forensic. La inspección asistida documenta lo que el perito ve, pero no preserva
el contenido del celular para analizarlo después ni para que otra parte lo verifique. Una extracción lógica es la
puerta de entrada al análisis (#101, con IPED) y a la paridad funcional básica con las herramientas comerciales.
Puede durar horas (decenas de GB de `/sdcard`), así que no cabe en el wizard.

### Qué es nuevo

1. Un **flujo de extracción** separado del wizard, iniciado desde una sección nueva (D13), que Tatana conduce en
   segundo plano.
2. Extracción de **datos estructurados** (contactos, SMS/MMS, calendario, registro de llamadas, lista de apps) y de
   **archivos** del almacenamiento compartido según selección (D1, D8).
3. Un **APK auxiliar de Factum** instalado temporalmente para el registro de llamadas (D3).
4. **Preservación:** SHA-256 por archivo, manifiesto, acta de extracción y registro del hash del manifiesto en el
   backend con sello de tiempo si está activo (D4, D6, D11).
5. Una entidad **extracción** asociable a un caso (D6).

---

## Criterios de aceptación

> Redactados con las opciones recomendadas de las dudas; donde dependen de una, se indica (Dn).

```gherkin
Feature: Extracción lógica de un Android con preservación forense

  Background:
    Given un perito logueado en la web de Factum
    And Tatana corriendo en su PC con la capability de extracción en /health
    And un Android conectado por USB, con la depuración USB autorizada para esa PC

  Scenario: Entrada al flujo fuera del wizard (D13)
    When el perito entra a la sección "Extracciones"
    Then ve sus extracciones anteriores y el botón "Nueva extracción"
    And iniciar una extracción no crea ni requiere un caso

  Scenario: Elegir dispositivo
    Given hay un Android y un iPhone conectados
    When el perito inicia una nueva extracción
    Then puede elegir el Android
    And el iPhone aparece deshabilitado con "La extracción de iPhone todavía no está disponible"

  Scenario: Elegir qué extraer (D1, D8)
    When el perito llega al paso de selección
    Then ve las categorías estructuradas (Contactos, Mensajes SMS/MMS, Registro de llamadas, Calendario, Apps instaladas) marcadas por defecto
    And ve las categorías de archivos (Fotos y videos, Descargas, Documentos, Audio, Medios de WhatsApp, Todo el almacenamiento) con su tamaño estimado
    And puede abrir "Elegir carpetas" para marcar carpetas puntuales de /sdcard
    And ve el total estimado a copiar

  Scenario: Elegir el destino (D9)
    When el perito elige el destino
    Then puede elegir una carpeta de la PC o un disco externo de la lista de unidades que le muestra Tatana, con su espacio libre
    And si el espacio libre no alcanza para el total estimado más un margen, no puede iniciar y ve cuánto falta
    And la extracción se guarda en una subcarpeta nueva con nombre único dentro del destino, nunca sobre archivos existentes

  Scenario: Advertencia y confirmación de modificaciones al dispositivo (D3, D12)
    Given eligió "Registro de llamadas"
    When pasa a confirmar
    Then ve la lista de lo que Factum va a modificar temporalmente en el dispositivo (instalar y desinstalar la app auxiliar de Factum)
    And tiene que marcar una confirmación explícita para iniciar
    And si desmarca "Registro de llamadas" no se instala nada en el dispositivo

  Scenario: Extracción en segundo plano
    When el perito inicia la extracción
    Then Tatana la ejecuta aunque se cierre la pestaña del navegador
    And la web muestra el progreso por categoría (pendiente, en curso, lista, con error), archivos y bytes copiados y tiempo estimado
    And al volver a abrir la sección se reengancha con la extracción en curso
    And mientras dure, Tatana no se actualiza solo

  Scenario: Datos estructurados sin instalar nada
    When se extraen contactos, SMS/MMS, calendario y apps instaladas
    Then se usan solo consultas de lectura por adb (content query, pm list)
    And se guarda la salida cruda de cada consulta como evidencia primaria y, aparte, una versión normalizada marcada como derivada (D5)

  Scenario: Registro de llamadas con la app auxiliar (D3)
    When se extrae el registro de llamadas
    Then Tatana verifica que la app auxiliar sea la versión firmada que trae Factum (hash conocido)
    And la instala con el permiso de lectura del registro concedido
    And exporta el registro y desinstala la app al terminar, también si la extracción falla o se cancela
    And el acta registra la hora de instalación, la de desinstalación, la versión y el hash de la app
    And si la instalación no se puede hacer (por ejemplo, el fabricante bloquea instalar por USB), la categoría queda "No extraída" con el motivo y el resto sigue

  Scenario: Archivos del almacenamiento
    When se copian los archivos seleccionados
    Then se conserva la ruta original del dispositivo dentro de la carpeta de la extracción
    And el manifiesto registra por archivo la ruta en el dispositivo, el tamaño, la fecha de modificación en el dispositivo y el SHA-256 calculado en la PC
    And si Factum pudo calcular el SHA-256 en el dispositivo, lo registra y marca si coincide con el de la PC (D10)
    And un archivo que no se puede leer queda en el manifiesto como error con el motivo, sin abortar la extracción

  Scenario: Cierre y preservación (D4, D11)
    When termina la extracción
    Then en la carpeta de la extracción quedan los datos, el manifiesto, el acta y el hash del manifiesto
    And el resumen muestra cantidades por categoría (contactos, mensajes, llamadas, eventos, apps, archivos) y tamaño total
    And la web registra la extracción en el backend con el hash del manifiesto, nunca con el contenido
    And si el sellado de tiempo está activo, el hash del manifiesto se sella y el sello se copia a la carpeta de la extracción

  Scenario: Acta de extracción
    Then el acta indica quién la hizo (usuario de Factum), en qué PC, inicio y fin (reloj de la PC y del servidor), la versión de Factum y de Tatana
    And la ficha técnica del dispositivo (#79) con la diferencia de reloj
    And el método por categoría (comando o mecanismo) y su resultado (cantidad, "No extraída" con motivo)
    And la lista de modificaciones hechas al dispositivo con hora, o "Ninguna"
    And el hash del manifiesto

  Scenario: Desconexión o corte (D10)
    Given una extracción en curso
    When el celular se desconecta, se revoca la autorización o se cierra Tatana
    Then la extracción queda "Interrumpida" con lo ya copiado hasheado en el manifiesto y el acta parcial con la hora y el motivo del corte
    And si la app auxiliar quedó instalada, al reconectar el mismo dispositivo Tatana la desinstala y lo agrega al acta
    And no se reanuda: el perito puede iniciar una extracción nueva

  Scenario: Cancelación por el perito
    When el perito cancela una extracción en curso y confirma
    Then Tatana detiene la copia, desinstala la app auxiliar si estaba instalada y cierra el manifiesto y el acta como "Cancelada"
    And lo copiado no se borra

  Scenario: Asociar a un caso (D6)
    Given una extracción terminada o interrumpida
    When el perito la asocia a un caso existente desde la extracción o desde el detalle del caso
    Then el caso muestra la extracción (fecha, dispositivo, resumen, ubicación, hash del manifiesto)
    And la trazabilidad del caso registra el evento de asociación
    And asociar o desasociar no modifica un informe ya generado

  Scenario: Verificar una extracción después (D11)
    Given una extracción guardada en un disco
    When alguien recalcula los SHA-256 con una herramienta estándar
    Then puede compararlos contra el manifiesto sin Factum, siguiendo las instrucciones del LEEME de la carpeta

  Scenario: Tatana viejo
    Given Tatana no declara la capability de extracción
    Then la sección muestra "Actualizá Tatana para hacer extracciones" y no permite iniciar
```

---

## Datos que se registran

### En la carpeta de la extracción (PC o disco externo)

| Dato | Obligatorio | Uso |
|---|---|---|
| Salida cruda de cada consulta (`content query` por proveedor, `pm list packages -f -i -U --show-versioncode`, export del APK auxiliar) | Sí, por categoría elegida | Evidencia primaria |
| Datos normalizados por categoría (JSON/CSV: contactos, mensajes, llamadas, eventos, apps) marcados como derivados | Sí (D5) | Resumen, insumo de #101 |
| Archivos de `/sdcard` seleccionados, con su ruta original | Según selección | Evidencia |
| Manifiesto: por archivo ruta relativa, ruta en el dispositivo, tamaño, fecha de modificación en el dispositivo, SHA-256 en la PC, SHA-256 en el dispositivo (si se pudo) y estado (ok / error + motivo) | Sí | Integridad, verificación |
| Hash SHA-256 del manifiesto (archivo aparte) | Sí | Cierre de integridad; se registra en el backend |
| Acta de extracción (legible por humanos + versión estructurada) | Sí | Cadena de custodia |
| Sello RFC 3161 del hash del manifiesto + `LEEME-verificacion.txt` | Si el sellado está activo | Fecha cierta |

### En el backend (solo metadatos, nunca contenido)

| Dato | Obligatorio | Uso |
|---|---|---|
| Id de la extracción, usuario, PC (nombre del host), versión de Tatana | Sí | Trazabilidad |
| Inicio y fin (servidor y PC), estado final (completa / interrumpida / cancelada / con errores) | Sí | Listado, acta |
| Identificación del dispositivo (ficha técnica #79 + diferencia de reloj) | Sí | Listado, acta, caso |
| Categorías elegidas y resultado por categoría (cantidad o motivo) | Sí | Resumen |
| Cantidad de archivos y bytes; cantidad con error | Sí | Resumen |
| Ruta del destino en la PC (texto informativo) | Sí | Ubicar la copia |
| SHA-256 del manifiesto y sello (si hay) | Sí | Integridad |
| Modificaciones hechas al dispositivo (qué, cuándo) | Sí ("ninguna" si no hubo) | Acta, caso |
| Caso asociado | No (D6) | Vinculación |

No se registran en el backend nombres de contactos, números, textos de mensajes, nombres de archivos ni ningún
contenido extraído.

---

## Diseño UX/UI (`client/`, web)

**Entrada (D13).** Sección nueva **"Extracciones"** en `AppNavbar` (entre Inspecciones y Métricas) y un acceso
"Nueva extracción" en el dashboard. La página lista las extracciones del usuario (fecha, dispositivo, estado,
tamaño, caso asociado) y una tarjeta destacada si hay una en curso.

**Asistente de nueva extracción** (pasos cortos, no reusa el wizard de inspección):
1. **Dispositivo:** tarjeta del equipo conectado (reusa `DeviceConnect`) con la ficha técnica; iPhone deshabilitado
   con el aviso de "todavía no disponible"; recordatorio "Mantené la pantalla desbloqueada" y nivel de batería si se
   puede leer (aviso por debajo de 20 %).
2. **Qué extraer:** casillas de categorías estructuradas y de archivos con tamaño estimado; "Elegir carpetas" abre un
   árbol de `/sdcard` (reusa la exploración de `DeviceFilesController`). Total estimado siempre visible.
3. **Destino:** lista de unidades (disco interno, discos externos) con espacio libre, navegador de carpetas y la ruta
   final que se va a crear (`<destino>/Factum_Extraccion_<marca-modelo>_<fecha-hora>/`). Error en línea si no
   alcanza el espacio o no se puede escribir.
4. **Confirmar:** resumen, lista de "Modificaciones al dispositivo" (o "Ninguna: solo lectura") y casilla de
   confirmación cuando corresponde.

**En curso:** barra global y fila por categoría con estado, archivos/bytes, velocidad y tiempo estimado; aviso "Podés
cerrar esta pestaña; la extracción sigue en Tatana. No desconectes el celular." Botón "Cancelar" con confirmación.

**Resultado:** estado final con ícono y texto (no solo color), resumen por categoría, hash del manifiesto con botón
copiar, ubicación con "Abrir carpeta" (reusa `FolderReveal`), "Asociar a un caso" y los errores por archivo
desplegables.

**Detalle del caso:** bloque "Extracciones asociadas" con fecha, dispositivo, resumen, hash y ubicación; acción
"Asociar extracción". Evento en la trazabilidad (`CaseTimeline`).

**Estados de error:** Tatana no responde o viejo, sin dispositivo, autorización revocada, sin espacio, destino no
escribible, app auxiliar no instalable (la categoría muestra el motivo), desconexión a mitad.

**`agent-ui/` (Electron):** sin cambios en la opción recomendada de D9.

---

## Fuera de alcance

- **Análisis, parseo profundo y visualización del contenido** (mensajes, chats, línea de tiempo): #101 con IPED.
  Acá solo el resumen de cantidades y tamaño.
- **Extracción de iPhone:** #102.
- **APK downgrade y `adb backup` de datos privados de apps (WhatsApp, etc.):** HU aparte (D2).
- Extracción física, por root, exploits, bootloader, modo EDL/descarga o desbloqueo de pantalla.
- Datos privados de apps (`/data/data`) y `Android/data`/`Android/obb` de otras apps (Android 11+ no los expone a adb).
- Reanudar una extracción interrumpida (D10).
- Varias extracciones simultáneas en la misma PC.
- Cifrado de la carpeta de extracción por Factum (D4).
- Informe DOCX de la extracción; el acta es el documento de esta HU (D11).
- Copiar la extracción al servidor.
- Hashes MD5/SHA-1 adicionales (D14).
- Cambios en el wizard de inspección.

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- La extracción la conduce **Tatana** como trabajo de fondo con estado consultable (endpoint + evento WebSocket) y se
  declara como operación larga en `OperationTracker`. Capability nueva en `/health` (p. ej. `extraction_android_v1`).
- Reusar `DeviceDetailsService` para la identificación y el reloj; `content query` con proyección completa por
  proveedor; `adb pull -a` (o equivalente que conserve fechas) para archivos en lote, no `exec-out cat` uno por uno.
- Validar las rutas de destino del lado de Tatana (no escribir fuera de la carpeta creada, no seguir symlinks).
- Lo dicho en el spike sin verificar: Android 12–15 (proveedores y `READ_CALL_LOG` vía APK), instalación por USB en
  Xiaomi/MIUI ("Instalar vía USB" desactivado) y Play Protect. La SDD tiene que listar pruebas con equipos reales.
- `sha256sum` de toybox existe en Android 8+; confirmar en el equipo.
- Contrato client ↔ Tatana y client ↔ backend en `snake_case_lower`, en la sección **Contrato compartido** de la SDD.
- Regla dura de datos (`AGENTS.md`): no tocar casos ni eventos existentes en la base de desarrollo; las pruebas
  limpian solo sus `_id`. Tampoco escribir pruebas en `Agent:EvidenceDirectory` real.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **recomendada** con su porqué.

**D1. Qué categorías entran en esta HU.**
- A) **Contactos (+ data), SMS/MMS, calendario, registro de llamadas (con APK auxiliar), lista de apps instaladas
  (`pm list packages`, solo lectura) y archivos de `/sdcard` por selección.**
- B) Lo mismo sin registro de llamadas (sin APK auxiliar: cero modificaciones al dispositivo).
- C) A más datos de apps por APK downgrade.
- **Recomendada: A.** Es la paridad mínima con una extracción lógica comercial; el registro de llamadas es de lo que
  más se pide en una pericia y solo sale con el APK. La lista de apps no modifica nada y orienta #101 y la HU de
  downgrade. C se trata en D2.

**D2. APK downgrade (datos privados de apps como WhatsApp).**
- A) Entra en esta HU como opción avanzada, con confirmación explícita y registro en el acta.
- B) **HU aparte, después de esta**, con un spike previo en Android 12–15 y WhatsApp real.
- **Recomendada: B.** El mecanismo solo se probó en Android 11 con una app de prueba; el paso riesgoso (abrir una
  versión vieja sobre una base más nueva) puede **dañar los datos del dispositivo**, y eso en un celular secuestrado
  es irreversible. Además `adb backup` está deprecado y en Android 12+ excluye apps con target SDK ≥ 31, que hoy son
  casi todas. Necesita su propio análisis de riesgo, catálogo de versiones viejas por app y su propia UX de
  confirmación. Meterlo acá demora la parte segura y bien conocida.

**D3. APK auxiliar de Factum ("connector"): dónde vive, firma y CI.**
- A) **Proyecto Android mínimo en el repo (`android/connector/`, Kotlin, sin UI salvo lo indispensable, solo
  `READ_CALL_LOG` y lo que pida la exportación), compilado en GitHub Actions con Gradle, firmado con una clave de
  release de Factum guardada como secreto del repo, embebido en el paquete de Tatana (nativo y Docker). Tatana
  verifica su SHA-256 contra un valor fijo antes de instalarlo y se desinstala siempre al final.**
- B) Igual, pero el APK se compila a mano y se versiona como binario en el repo.
- C) Sin APK: el registro de llamadas queda fuera de esta HU.
- **Recomendada: A.** Una build reproducible y firmada siempre con la misma clave es lo que permite decir en el acta
  "se instaló la app X versión Y con hash Z". B no deja rastro de cómo se compiló. Sub-dudas: (a) ¿quién custodia la
  clave de firma y su respaldo? Recomendado: el dueño del producto, fuera del repo, con copia offline, porque perderla
  obliga a cambiar el hash publicado; (b) `targetSdk`/`minSdk`: recomendado `minSdk` 23 (Android 6) y `targetSdk` el
  actual; (c) la app exporta por `adb` (stdout / archivo temporal que se borra) y no pide red: recomendado sin permiso
  `INTERNET`, así se puede afirmar que no saca datos del dispositivo por otra vía.

**D4. Formato de la salida.**
- A) **Carpeta abierta** (sin cifrar) con estructura fija: `datos/` (crudo y normalizado), `archivos/` (con la ruta
  del dispositivo), `MANIFIESTO`, su `.sha256`, `ACTA` y `LEEME`.
- B) ZIP AES-256 como el de los casos.
- C) Carpeta abierta y, al final, opción de empaquetar en ZIP cifrado.
- **Recomendada: A.** IPED (#101) procesa carpetas directamente; un ZIP cifrado de decenas de GB duplica el espacio y
  el tiempo y obligaría a descomprimirlo para analizar. La integridad la dan el manifiesto, su hash registrado en el
  backend y el sello. La confidencialidad del medio se recomienda en la UI (disco cifrado con BitLocker/LUKS/FileVault)
  en vez de que Factum la imponga. Si la institución exige cifrado, C se puede sumar después.

**D5. Datos estructurados: crudo, normalizado o los dos.**
- A) **Las dos cosas:** la salida cruda de cada consulta tal como la devolvió el dispositivo (es la evidencia primaria,
  hasheada) y un JSON/CSV normalizado, marcado en el manifiesto como derivado.
- B) Solo crudo (el parseo queda para #101).
- C) Solo normalizado.
- **Recomendada: A.** El crudo permite que cualquiera reprocese y verifique; el normalizado hace falta para el resumen
  de cantidades y le simplifica la vida a #101. C pierde la evidencia original si el parser tiene un error.

**D6. Registro en el backend y asociación a un caso.**
- A) **La extracción es una entidad propia en el backend (solo metadatos y hash del manifiesto), se crea al iniciar
  sin caso y se asocia a 0 o 1 caso después, desde la extracción o desde el detalle del caso. Se puede asociar a
  casos en borrador o generados; no cambia informes ya emitidos y deja un evento en la trazabilidad del caso.**
- B) Solo local (Tatana), sin registro en el backend; la asociación es escribir la ruta en el caso a mano.
- C) Toda extracción nace dentro de un caso (se elige o crea el caso al iniciar).
- **Recomendada: A.** El hash del manifiesto registrado en el servidor (y sellado) es lo que permite probar después
  que la copia no cambió; sin backend eso se pierde. Iniciar sin caso respeta el pedido (la extracción puede empezar
  antes de que exista el expediente). Sub-dudas: (a) ¿una extracción puede asociarse a **varios** casos? Recomendado:
  no, una; (b) ¿el informe del caso la menciona? Recomendado: **no en esta HU** (solo web y trazabilidad); que el
  informe la cite es una mejora chica posterior, y evita tocar la plantilla; (c) ¿quién la ve? Recomendado: el mismo
  criterio de visibilidad que los casos del usuario/cliente.

**D7. Qué cuenta como "modificación del dispositivo" y qué se permite.**
- A) **Solo: instalar/desinstalar la app auxiliar (si se elige registro de llamadas) y, si hace falta para que no se
  bloquee la pantalla durante horas, poner `stay_on_while_plugged_in` y restaurarlo (mismo precedente que la grabación
  de sesión). Las dos van al acta con hora; todo lo demás es lectura.** Los efectos colaterales inevitables de adb
  (autorización de la PC, logs del sistema) se mencionan en el acta como texto fijo.
- B) Además permitir conceder permisos o cambiar ajustes para destrabar categorías.
- **Recomendada: A.** Lista corta, declarada y reversible; cualquier otra escritura obliga a una justificación que no
  hace falta para esta etapa.

**D8. Selección de archivos de `/sdcard`.**
- A) **Categorías predefinidas por carpeta (Fotos y videos: `DCIM`, `Pictures`, `Movies`; Descargas; Documentos;
  Audio/grabaciones; Medios de WhatsApp, reusando las rutas de `KnownAppFolders`; "Todo el almacenamiento"), más un
  árbol para elegir carpetas puntuales. Tamaño estimado antes de empezar. Por defecto ninguna de archivos marcada.**
- B) Siempre todo `/sdcard`.
- C) Solo árbol de carpetas.
- **Recomendada: A.** Una copia de 50 GB "por las dudas" puede tardar horas y no siempre hace falta; las categorías
  cubren lo habitual y el árbol lo particular. Por defecto sin archivos para que el perito decida conscientemente el
  volumen (las categorías estructuradas sí vienen marcadas porque pesan poco). ¿Preferís que "Todo el almacenamiento"
  venga marcado? Recomendado: no.

**D9. Cómo se elige el destino (el navegador no puede abrir un diálogo de carpetas de la PC para Tatana).**
- A) **Tatana expone la lista de unidades (disco interno y discos externos montados, con espacio libre) y un listado
  de carpetas; la web muestra un navegador y Tatana valida que se pueda escribir. En Tatana Docker solo se ofrecen
  las carpetas montadas (`~/Factum/Evidencia` y, si la SDD lo resuelve, `/media/<usuario>` para discos USB).**
- B) Diálogo nativo de carpetas desde `agent-ui/` (Electron).
- C) Ruta escrita a mano por el perito.
- **Recomendada: A.** Funciona igual en Windows, macOS, Linux y desde cualquier navegador, sin depender de que
  `agent-ui` esté abierto (en Docker y en Linux muchas veces no lo está). C es propenso a errores. Sub-duda: en el
  piloto de fiscalía con Tatana en Docker, ¿hace falta extraer a un disco USB desde la primera versión? Recomendado:
  sí, montando `/media` en el contenedor (cambio de despliegue en `deploy/tatana-docker/`, lo detalla la SDD), porque
  es el caso típico de un perito con un disco de evidencia.

**D10. Verificación de la copia y cortes.**
- A) **SHA-256 en la PC de cada archivo y, cuando el `toybox` del dispositivo tiene `sha256sum`, también en el
  dispositivo, marcando coincidencia en el manifiesto. Ante un corte, la extracción queda "Interrumpida" con lo
  copiado y el acta parcial; no se reanuda (se inicia otra).**
- B) Solo SHA-256 en la PC; sin reanudar.
- C) SHA-256 en los dos lados y reanudación de extracciones interrumpidas.
- **Recomendada: A.** El hash del lado del dispositivo prueba que la copia es fiel al origen, no solo que no cambió
  después; cuesta leer cada archivo dos veces en el celular (más tiempo), así que la SDD puede dejarlo como opción
  activada por defecto si medido resulta muy lento. Reanudar complica el acta (dos sesiones, dos ventanas de tiempo)
  y no es imprescindible para la primera versión.

**D11. Acta de extracción: formato e integridad.**
- A) **Generada por Tatana en la carpeta, en texto plano UTF-8 (legible sin software) y en JSON; su hash entra al
  manifiesto, el hash del manifiesto se registra en el backend y se sella si el sellado está activo. Un `LEEME` explica
  cómo verificar con `sha256sum`/`Get-FileHash` y `openssl ts -verify`.**
- B) Acta DOCX/PDF generada por el backend con la marca del cliente.
- **Recomendada: A.** No depende de la conexión al servidor en el momento del cierre (el registro y el sello pueden
  quedar pendientes y completarse después, como ya hace el sellado) y se lee en cualquier PC. Un acta DOCX con marca
  del cliente es una mejora posterior. Sub-duda: si al terminar no hay conexión con el backend, ¿la extracción vale?
  Recomendado: sí, queda "pendiente de registro" y la web la registra al volver la conexión; el acta lo dice.

**D12. ¿Quién puede extraer y cuántas a la vez?**
- A) **Cualquier usuario que hoy puede crear casos; una extracción a la vez por PC (Tatana).**
- B) Restringido a un rol nuevo.
- **Recomendada: A.** No hay hoy un rol de "perito extractor" y crearlo excede la HU; una a la vez evita competir por
  el USB y simplifica el acta.

**D13. Dónde vive en la web.**
- A) **Sección nueva "Extracciones" en la barra (`/extracciones`) más un acceso "Nueva extracción" en el dashboard.**
- B) Solo un botón en el dashboard que abre un modal.
- C) Dentro del detalle del caso.
- **Recomendada: A.** Es un flujo de horas con historial propio; una sección le da lugar al listado y a la extracción
  en curso. C contradice que se pueda iniciar sin caso.

**D14. Algoritmos de hash.**
- A) **Solo SHA-256**, como el resto de Factum.
- B) SHA-256 + MD5 (y/o SHA-1), como ofrecen las herramientas comerciales.
- **Recomendada: A.** Coherente con el ZIP de evidencia y el sellado; MD5 y SHA-1 están rotos para colisiones. Si un
  tribunal o fiscalía lo exige, se agrega después como opción, sin cambiar el formato del manifiesto (columna extra).

## Validación

Validada por el usuario el 2026-10-25: **todas las dudas con la opción recomendada** (D1=A, D2=B → HU aparte
`apk-downgrade`, D3=A con sub-dudas recomendadas, D4=A, D5=A, D6=A con sub-dudas recomendadas, D7=A, D8=A,
D9=A con `/media` montado en Tatana Docker, D10=A, D11=A, D12=A, D13=A, D14=A).
