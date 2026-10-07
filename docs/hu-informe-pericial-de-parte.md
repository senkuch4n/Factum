# HU: Informe Pericial Técnico Informático (perito de parte) en lugar del informe "de extracción"

**Slug:** `informe-pericial-de-parte`
**Apps afectadas:** `client/` (web: wizard, historial, perfil) y `server/src/Factum.Backend`
(modelo, endpoints, `ReportService`, plantilla). `server/src/Factum.Agent` (Tatana) y
`agent-ui/` **no cambian** (ver Contexto: los datos del equipo que hacen falta ya llegan
del agente).
**Pedido del usuario (2026-10-01):** el primer cliente es un estudio jurídico; el
informe tiene que ser un *Informe Pericial Técnico Informático* hecho por un perito de
parte, con la plantilla que armó el usuario. "Pediré más datos u otros datos que antes
no se pedían."
**Absorbe:** `B-PLANTILLA` (`plantilla_informe_v4.docx`, sección 8.3 de
`Refactorizaciones/marca-comercial-sin-mpf-gfd.md`) y DP5 de esa SDD (terminología
"Fiscal", "DNI Fiscal", "Foto del fiscal", "Funcionario", "Denunciante").

**Como** perito informático de parte que trabaja para un estudio jurídico
**quiero** que Factum me pida los datos de la actuación, de las partes y del análisis, y
genere con ellos el Informe Pericial Técnico Informático con la identidad del estudio
**para que** el informe que presento ante el tribunal salga completo desde la
herramienta, sin reescribirlo a mano, y con la evidencia digital y sus hashes ya
integrados.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-01)

**Flujo del wizard** (`client/src/app/dashboard/page.tsx`, `STEPS` L36-42):
1. **Dispositivo** (`DeviceConnect`): el agente Tatana lista los equipos.
2. **Expediente** (`CaseFormStep`): pide `NroReferencia` ("Número de expediente"),
   `NombreDenunciante`, `DNIDenunciante` (los tres obligatorios), `Observaciones`
   (opcional) e `imeiOverride` (solo si el agente no detectó el IMEI). Al confirmar
   hace `POST /api/cases` y crea el caso. Toma una captura automática para el mockup
   del teléfono, **que no se guarda como evidencia** (en el paso 2 `screenshot_taken`
   se ignora, L140).
3. **Captura** (`CaptureStep`): capturas de pantalla (`screenshot_<ts>.png`),
   grabaciones, archivos del explorador (`device_pull_*`, con `SourcePath`) y dos fotos
   de identidad por webcam: `foto_funcionario_*.jpg` ("Foto del fiscal", rol
   "Funcionario") y `foto_denunciante_*.jpg` ("Titular").
4. **Generar** (`GenerateStep`): resumen y `POST /api/cases/{id}/generate`.
5. **Listo** (`ResultStep`): descarga de ZIP y DOCX, hash y "clave".

**Modelo** (`server/src/Factum.Backend/Models/Case.cs`, `DeviceInfo.cs`, `User.cs`):
`Case { NroReferencia, NombreDenunciante, DniDenunciante, Observaciones, Officer: User,
Device: DeviceInfo, Status, CreatedAt, GeneratedAt, ZipPassword, ZipHash, ZipFilename,
PdfFilename, FileSources[] }`. `User { Dni, Name, Sigla }` viene **entero del proveedor
de login**: Factum no tiene almacén de usuarios ni perfil (en modo `dev`,
`Sigla = "-"`). **No hay endpoint para editar un caso**: los datos se fijan al
crearlo, y el repositorio solo actualiza estado, datos de generación y `FileSources`.

**Datos del equipo que manda el agente** (`client/src/lib/agent.ts` `Device`):
`serial, manufacturer, model, android_version, imei, name, operator, platform,
ios_version`. Al crear el caso **solo se guardan** `serial, manufacturer, model,
android_version, imei, platform, os_version` (`DeviceInfoDto`); `name` y `operator`
se pierden. Ojo con su semántica:
- `name`: en iOS es el nombre visible del equipo (`DeviceName`, "iPhone de …"); en
  Android es `ro.product.name`, un nombre de producto interno, **no** el nombre que el
  usuario le puso al teléfono.
- `operator`: en Android es el nombre de la operadora (`gsm.operator.alpha`); en iOS es
  el **número de línea** (`PhoneNumber`). El mismo campo significa dos cosas.

**Informe** (`ReportService.cs`): copia `Templates/plantilla_informe_v3.docx` (la
"de extracción", con el parche interino de la HU anterior), reemplaza placeholders
`UPPER_SNAKE` (`{NROREF}`, `{NOMBRE_DENUNCIANTE}`, `{NOMBRE_FUNCIONARIO}`, `{MARCA}`,
`{IMEI}`, `{HASH}`, `{CLAVE}`, …, más los de Branding `{ORGANIZACION}`, `{CONTACTO}`,
`{CONTACTO_EN_LINEA}`, `{LOGO_ORGANIZACION}`), arma la lista `{ARCHIVOS}` (por archivo:
nombre, tamaño, SHA-256 y origen) e inserta `{FOTO_FUNCIONARIO}`,
`{FOTO_DENUNCIANTE}` y `{CAPTURAS}` (todas las capturas juntas, clasificadas por el
nombre del archivo). El pie "Realizado con Factum" lo inyecta el código y **no cambia**
con esta HU. `{FECHA_HORA}`/`{fHora}` usan el **momento de generación en UTC**.

**Branding** (HU aprobada `marca-comercial-sin-mpf-gfd`): `Branding:OrganizationName`,
`OrganizationLogo` y `ContactLines` en config local no versionada. Es la identidad del
estudio emisor.

**Terminología fiscal visible hoy:** `CaseCard` ("Fiscal", "DNI Fiscal"),
`CaptureStep`/`IdentityCard`/`GenerateStep` ("Foto del fiscal", "Fiscal ·
Funcionario", "Denunciante · Titular"), `CaseHistory` (columna "Denunciante",
búsqueda "Buscar por fiscal o sigla…"), `CaseFormStep` ("Datos del expediente",
"Nombre/DNI del denunciante") y los mensajes de validación de `page.tsx`.

### Hallazgos que afectan a esta HU

1. **El hash del ZIP que se informa no es el del ZIP que se descarga.**
   `ReportService.GenerateAsync` calcula `zipHash` sobre el ZIP **solo con la
   evidencia** (fase 1) y después **le agrega el DOCX** con `AppendToZipAsync` (fase 3),
   que reescribe el archivo. El hash que va al informe, a `Case.ZipHash` y a la
   pantalla `ResultStep` no coincide con el SHA-256 del ZIP descargado. En un informe
   cuya sección "Cadena de custodia" dice "Integridad: garantizada mediante hash", esto
   es un problema serio (ver **D10**).
2. **El ZIP no está cifrado.** `CreateZipAsync` usa `ZipArchive`, que no cifra; la
   "clave" (`ZipPassword`, `{CLAVE}`) no protege nada. La plantilla nueva no menciona
   clave. Queda **fuera de alcance** (ver Fuera de alcance), pero se registra.
3. **Después de generar se borran los archivos sueltos de la evidencia**, así que un
   informe no se puede regenerar. Todo lo que el perito escriba tiene que estar
   cargado **antes** de generar.
4. **La plantilla del usuario trae datos reales fijos** en el texto: el domicilio
   constituido del estudio y el nombre del colegio profesional ante el que tramita la
   causa. Esa plantilla **no se puede versionar tal cual** en `Templates/` (ver
   inventario, grupo d).
5. **La plantilla no trae membrete ni firma** (al menos en el texto extraído): no hay
   `{LOGO_ORGANIZACION}`, `{ORGANIZACION}` ni `{CONTACTO}`, y termina sin bloque de
   firma del perito (ver **D13**).

### Qué es lo nuevo

1. **Perfil del perito** persistente (nombre, matrícula, profesión, carácter), para no
   cargarlo en cada caso.
2. **Datos de la actuación y de las partes por caso** (tribunal, sala, integrantes,
   causa, carátula, partes, objeto, proponente, fechas, titular y línea del equipo).
3. **Redacción del informe en el wizard**: un paso nuevo con los textos largos
   (operaciones, aseguramiento, resultados, valoración, conclusiones, notas, reserva),
   con texto por defecto editable donde corresponde y guardado en el servidor.
4. **Capturas de identificación del equipo** (IMEI/modelo y nombre del dispositivo)
   marcadas como tales, para que vayan a su lugar en el informe.
5. **Plantilla `plantilla_informe_v4.docx`** a partir de la del usuario, con
   placeholders canónicos, sin datos reales y con la tabla de hashes repetible.
6. **Terminología no fiscal** en el wizard, el historial y el informe.

---

## Inventario de placeholders de la plantilla

Clasificación: **(a)** Factum ya lo tiene; **(b)** dato nuevo por caso; **(c)** dato
del perito; **(d)** dato fijo del estudio, hoy escrito a mano en la plantilla.

**Convención propuesta para los nombres canónicos:** camelCase, `dato + Entidad`
(como ya hace la plantilla con `imeiDispositivo` o `matriculaPersonaPropone`), sin
barras, sin tildes ni typos y con minúscula inicial. Se conservan los nombres de la
plantilla que ya son consistentes (`descripcion*`, `caratula`, `salaTribunal`, …). Los
placeholders de Branding existentes (`{ORGANIZACION}`, `{CONTACTO}`,
`{CONTACTO_EN_LINEA}`, `{LOGO_ORGANIZACION}`) **mantienen su nombre en mayúsculas**,
porque ya son contrato de la SDD anterior (8.1).

### Encabezado y presentación

| # | Placeholder en la plantilla | Canónico propuesto | Clase | Fuente hoy / fuente propuesta |
|---|---|---|---|---|
| P1 | `{tribunal/fiscalia}` | `{nombreTribunal}` | b | Nuevo por caso. Destinatario del escrito (tribunal, fiscalía, juzgado). La barra rompe la convención. |
| P2 | *Texto fijo:* nombre del colegio profesional (línea debajo de P1) | `{organismoTribunal}` | d → b | Hoy está escrito a mano. Es el organismo del que depende el tribunal, y cambia según la causa. Ver **D3**. |
| P3 | `{usuarioNombreCompleto}` | `{nombrePerito}` | c | Hoy `User.Name` (`Officer.Name`, del proveedor de login). Propuesto: perfil del perito, con `User.Name` como valor inicial. |
| P4 | `{matricula}` | `{matriculaPerito}` | c | **No existe.** Perfil del perito. |
| P5 | `{profesion}` | `{profesionPerito}` | c | **No existe.** Perfil del perito. |
| P6 | `{caracter}` | `{caracterPerito}` | c | **No existe.** Perfil del perito (p. ej. "perito informático de parte"). |
| P7 | *Texto fijo:* domicilio constituido del estudio | `{domicilioConstituido}` | d | Hoy está escrito a mano. Propuesto: config del estudio. Ver **D3**. |
| P8 | `{causaDisiplinariaYNumero}` | `{tipoCausa}` + `{numeroCausa}` | b + a | Typo ("Disiplinaria") y dos datos en uno. `{numeroCausa}` = `Case.NroReferencia` (ya existe y es obligatorio). `{tipoCausa}` es nuevo (p. ej. "causa disciplinaria", "expediente civil"). |
| P9 | `{personasContra}` | `{parteDenunciada}` | b | Parece el mismo dato que P14. Ver **D5**. |
| P10 | `{salaTribunal}` | `{salaTribunal}` | b | Nuevo por caso. Se conserva el nombre. |
| P11 | `{personasTribunalPrefijo}` | `{integrantesTribunal}` | b | Nuevo por caso: lista con tratamiento (p. ej. "Dres. Nombre Apellido, …"). El nombre actual no dice qué contiene. |

### Referencia de la actuación

| # | Placeholder | Canónico | Clase | Fuente |
|---|---|---|---|---|
| P12 | `{caratula}` | `{caratula}` | b | Nuevo por caso. |
| P13 | `{parteDenunciante}` | `{parteDenunciante}` | b | Nuevo por caso. **No** es el `NombreDenunciante` de hoy: en el modelo fiscal "denunciante" era quien entregaba el equipo; acá es una parte del proceso. Ver **D5**. |
| P14 | `{parteDenunciada}` | `{parteDenunciada}` | b | Nuevo por caso. |
| P15 | `{objeto}` | `{objetoCausa}` | b | Nuevo por caso (objeto de la actuación). Se renombra para no confundirlo con P17. |
| P16 | `{fechaIntervencion}` | `{fechaIntervencion}` | b | Nuevo por caso. Ver **D5** (¿es la misma fecha que P23?). |

### Objeto e identificación

| # | Placeholder | Canónico | Clase | Fuente |
|---|---|---|---|---|
| P17 | `{objetoModificable}` | `{objetoInforme}` | b (texto largo) | Párrafo opcional que complementa el objeto fijo del informe. |
| P18 | `{nombreUsuario}` | `{nombrePerito}` | c | **Duplicado** de P3 con otro nombre. |
| P19 | `{matriculaUsuario}` | `{matriculaPerito}` | c | **Duplicado** de P4. En la plantilla aparece "M.P {matricula}" (sin punto) y "M.P.{matriculaUsuario}" (sin espacio): la v4 unifica en "M.P. {matriculaPerito}". |
| P20 | `{profesionUsuario}` | `{profesionPerito}` | c | **Duplicado** de P5. |
| P21 | `{personaPropone}`, `{profesionPersonaPropone}`, `{matriculaPersonaPropone}` | `{nombreProponente}`, `{profesionProponente}`, `{matriculaProponente}` | b | Nuevos por caso: quién propone al perito (normalmente un abogado). |
| P22 | `{causa}` | `{ambitoCausa}` | b | Nuevo por caso, **semántica ambigua** ("Ámbito: {causa}"): ¿fuero, organismo o la misma causa de P8? Ver **D5**. |

### Elementos ofrecidos (el equipo)

| # | Placeholder | Canónico | Clase | Fuente |
|---|---|---|---|---|
| P23 | `{fecha}` | `{fechaInspeccion}` | a | Hoy hay `{FECHA_HORA}` con el momento de **generación** en UTC. Propuesto: inicio de la inspección en hora local. Ver **D11**. |
| P24 | `{hora}` | `{horaInspeccion}` | a | Ídem. |
| P25 | `{tipoDispositivo}` | `{tipoDispositivo}` | b, con valor inicial | No existe. Valor inicial "Teléfono celular", editable (el agente no distingue celular de tablet). |
| P26 | `{MarcaModeloDispositivo}` | `{marcaModeloDispositivo}` | a | `Device.Manufacturer` + `Device.Model` (en iOS `Model` ya es el nombre comercial, `FriendlyModel`). Mayúscula inicial: rompe la convención. |
| P27 | `{imeiDispositivo}` | `{imeiDispositivo}` | a | `Device.Imei` (detectado o `imeiOverride` del paso 2). Solo un IMEI: los equipos dual SIM tienen dos (fuera de alcance). |
| P28 | `{lineaDispositivo}` | `{lineaDispositivo}` | b, con valor inicial en iOS | No se guarda hoy. En iOS se puede precargar con `Device.operator` (que ahí es el número); en Android `operator` es el nombre de la operadora, así que se carga a mano. |
| P29 | `{titularDispositivo}` | `{titularDispositivo}` | b | Nuevo por caso. Semánticamente es lo que hoy se pide como "Nombre del denunciante" (`IdentityCard` ya lo muestra con rol "Titular"). |
| P30 | `{capturasDispositivoMostrandoImeiYModelo}` | `{capturasImeiModelo}` | a (parcial) | Las capturas existen (`screenshot_*`), pero hoy **no hay forma de marcar** cuál muestra el IMEI y el modelo (p. ej. la pantalla de `*#06#` o "Acerca del teléfono"). Todas van a `{CAPTURAS}`. Nuevo: marcar capturas como "IMEI y modelo". |
| P31 | `{capturasMostrandoNombreDispositivo}` | `{capturasNombreDispositivo}` | a (parcial) | Ídem: captura marcada como "Nombre del dispositivo". |

### Desarrollo, evidencia y cierre

| # | Placeholder | Canónico | Clase | Fuente |
|---|---|---|---|---|
| P32 | `{descripcionOperacionesRealizadas}` | igual | b (texto largo) | Nuevo. Borrador generado con lo que hizo Factum en el caso, editable. Ver **D7**. |
| P33 | `{descripcionAseguramientoEvidencia}` | igual | b (texto largo) | Nuevo. Texto por defecto editable. |
| P34 | `{nombreArchivo}` / `{hashArchivo}` | igual | a | Fila de tabla **repetible**. Ver "Tablas y listas repetibles". |
| P35 | `{descripcionResultados}` | igual | b (texto largo) | Nuevo. Lo escribe el perito. |
| P36 | `{descripcionValoracionTecnica}` | igual | b (texto largo) | Nuevo. Lo escribe el perito. |
| P37 | `{descripcionConclusiones}` | igual | b (texto largo) | Nuevo. Lo escribe el perito. |
| P38 | `{descripcionNotasTecnicas}` | igual | b (texto largo) | Nuevo. Texto por defecto editable. |
| P39 | `{descripcionReserva}` | igual | b (texto largo) | Nuevo. Texto por defecto editable. |

### Datos de Factum que la plantilla nueva no usa

| Dato actual | Qué pasa |
|---|---|
| `DniDenunciante` | Pasa a ser el DNI del titular del equipo. No aparece en la plantilla; se propone conservarlo como dato **opcional** del caso (sirve para buscar). Ver **D5**. |
| `Observaciones` | No aparece en la plantilla. Ver **D5**. |
| `Officer.Dni`, `Officer.Sigla` | Siguen siendo la identidad de login (dueño del caso). No van al informe. |
| `Device.Serial`, `Device.OsLabel` | No aparecen en la plantilla. Se pueden mencionar en el borrador de `{descripcionOperacionesRealizadas}`. |
| Fotos de identidad (`foto_funcionario_*`, `foto_denunciante_*`) | La plantilla no tiene lugar para fotos de personas. Ver **D9**. |
| `{CLAVE}` / `ZipPassword` | La plantilla no la menciona (y el ZIP no está cifrado, hallazgo 2). |

### Tablas y listas repetibles

- **Tabla de hashes** (`NOMBRE | HASH SHA-256`, fila `{nombreArchivo} | {hashArchivo}`):
  la fila con los placeholders es el **modelo**. El generador la clona una vez por
  archivo de evidencia y borra la fila modelo. Datos: los mismos que hoy alimentan
  `{ARCHIVOS}`. `files` sale de escanear la carpeta del caso (`StorageService`),
  mezclada con `FileSources` (origen en el equipo) y filtrada con
  `IsGeneratedArtifact`. El hash es el que calcula `BuildHashesAsync`. Al final va una
  **fila del contenedor ZIP** (`evidencia_<…>.zip`), con el hash según lo que se
  decida en **D10**. Si el archivo tiene origen en el equipo (`SourcePath`), se
  muestra debajo del nombre, en la misma celda y con letra más chica (como hoy el
  "Origen:").
- **Capturas de identificación** (`{capturasImeiModelo}`, `{capturasNombreDispositivo}`):
  hoy las capturas salen del botón de captura del paso 3 (agente Tatana: ADB en
  Android; DVT, qvh o AirPlay en iOS) y quedan como `screenshot_<ts>.png`. Lo nuevo es
  que el perito pueda **marcar** una o más capturas con uno de esos dos roles. El
  placeholder se reemplaza por esas imágenes, una debajo de la otra, ajustadas sin
  recortar (como `{CAPTURAS}` hoy). Si no hay ninguna marcada, ver **D8**.
- **El resto de las capturas, grabaciones y archivos** no tiene un placeholder propio en
  la plantilla: quedan en el ZIP y en la tabla de hashes. ¿Van también como anexo de
  imágenes al final del informe? Ver **D8**.

---

## Criterios de aceptación

```gherkin
Feature: Informe Pericial Técnico Informático del perito de parte

  # ── Perfil del perito ──────────────────────────────────────────────
  Scenario: Primer caso de un perito sin perfil
    Given un usuario autenticado que nunca cargó su perfil de perito
    When inicia una inspección nueva y llega al paso "Causa"
    Then ve la tarjeta "Tus datos de perito" desplegada, con el nombre precargado desde su usuario de login
    And no puede avanzar hasta completar nombre, matrícula, profesión y carácter
    And al avanzar los datos quedan guardados en su perfil

  Scenario: Perfil ya cargado
    Given un perito con el perfil completo
    When inicia una inspección nueva
    Then la tarjeta "Tus datos de perito" aparece plegada, con su nombre y matrícula, y un botón "Editar"
    And no tiene que volver a cargarlos

  Scenario: Editar el perfil no cambia casos anteriores
    Given un perito con casos ya generados
    When cambia su matrícula en el perfil
    Then los informes y los datos de esos casos no cambian
    And los casos nuevos usan la matrícula nueva

  Scenario: Editar el perfil fuera del wizard
    Given un perito autenticado
    When abre "Mi perfil de perito" desde el menú de usuario
    Then puede ver y editar nombre, matrícula, profesión y carácter

  # ── Datos de la causa (paso 2) ─────────────────────────────────────
  Scenario: Carga de los datos de la causa
    Given un equipo conectado y seleccionado
    When el perito completa el paso "Causa"
    Then puede cargar: tribunal, organismo, sala, integrantes del tribunal, tipo y número de causa, carátula, parte denunciante, parte denunciada, objeto de la causa, ámbito, fecha de intervención, nombre, profesión y matrícula del proponente, titular del equipo, DNI del titular (opcional), línea y tipo de dispositivo
    And el IMEI se sigue mostrando como detectado o se pide a mano igual que hoy
    And los campos obligatorios (según D6) se marcan como tales y el botón "Crear caso y continuar" valida solo esos

  Scenario: Valores precargados
    Given un perito que ya generó al menos un caso
    When inicia una inspección nueva
    Then tribunal, organismo, sala, integrantes, tipo de causa y los datos del proponente vienen precargados con los del último caso de ese perito, y se pueden editar
    And el tipo de dispositivo viene precargado con "Teléfono celular"
    And en iOS la línea viene precargada con el número que informó el equipo, si lo informó

  Scenario: Validación de un campo obligatorio vacío
    Given el paso "Causa" con la carátula vacía
    When el perito toca "Crear caso y continuar"
    Then el foco va al primer campo con error y se muestra "Ingresá la carátula" debajo del campo
    And el caso no se crea

  # ── Capturas de identificación (paso 3) ────────────────────────────
  Scenario: Marcar capturas de identificación del equipo
    Given el paso de captura con capturas de pantalla tomadas
    When el perito marca una captura como "IMEI y modelo" y otra como "Nombre del dispositivo"
    Then cada una muestra su etiqueta en la galería
    And en el informe aparecen en la sección "Elementos ofrecidos", debajo de los datos del equipo
    And una captura puede tener una sola etiqueta y se puede desmarcar

  # ── Redacción del informe (paso nuevo) ─────────────────────────────
  Scenario: Textos por defecto
    Given un caso con la evidencia subida
    When el perito entra al paso "Informe"
    Then "Operaciones realizadas" trae un borrador con lo que hizo Factum en el caso (equipo, plataforma, cantidad de capturas, grabaciones y archivos extraídos, herramienta y algoritmo de hash)
    And "Aseguramiento de la evidencia", "Notas técnicas" y "Reserva" traen su texto por defecto
    And "Resultados", "Valoración técnica" y "Conclusiones" están vacíos
    And todos son editables

  Scenario: Los textos se guardan en el servidor
    Given el perito escribió los resultados en el paso "Informe"
    When cierra el navegador y retoma el caso desde el historial
    Then el texto sigue ahí

  Scenario: Generación bloqueada por datos faltantes
    Given un caso con "Conclusiones" vacío
    When el perito llega al paso "Generar"
    Then ve la lista de datos obligatorios que faltan, con un enlace al paso donde se cargan
    And el botón "Generar informe" está deshabilitado

  # ── Informe generado ───────────────────────────────────────────────
  Scenario: El informe usa la plantilla pericial
    Given un caso con todos los datos obligatorios
    When el perito genera el informe
    Then el DOCX sale de plantilla_informe_v4.docx
    And no queda ningún placeholder "{…}" sin reemplazar
    And los campos opcionales vacíos no dejan etiquetas huérfanas (según lo que defina la SDD para cada sección)
    And el membrete muestra la identidad del estudio (Branding) y el pie "Realizado con Factum" sigue presente

  Scenario: Tabla de hashes
    Given un caso con N archivos de evidencia
    When se genera el informe
    Then la tabla "NOMBRE | HASH SHA-256" tiene N filas de archivos más 1 fila del contenedor ZIP
    And cada hash coincide con el SHA-256 del archivo correspondiente
    And el hash del contenedor coincide con el SHA-256 del ZIP que se descarga (según D10)

  Scenario: Datos del estudio fuera de la plantilla versionada
    Given la plantilla plantilla_informe_v4.docx versionada en el repo
    Then no contiene domicilios, nombres de personas, matrículas ni nombres de organismos reales
    And el domicilio constituido sale de la configuración local del estudio (según D3)

  Scenario: Fecha y hora de la inspección
    When se genera el informe
    Then {fechaInspeccion} y {horaInspeccion} muestran el inicio de la inspección en la hora local configurada (según D11), no la hora UTC de generación

  # ── Terminología ───────────────────────────────────────────────────
  Scenario: Sin vocabulario fiscal en la web
    Given cualquier pantalla del wizard, el historial o las tarjetas de caso
    Then no aparecen "Fiscal", "DNI Fiscal", "Foto del fiscal", "Funcionario" ni "Buscar por fiscal o sigla"
    And se usan los términos de la tabla de D15

  # ── Casos viejos ───────────────────────────────────────────────────
  Scenario: Los casos ya generados no cambian
    Given casos generados antes de esta HU, con su ZIP y DOCX en Storage:DataDirectory
    When se despliega esta HU
    Then sus documentos en Mongo y sus archivos quedan byte a byte iguales
    And el historial los sigue mostrando, con "Titular" en lugar de "Denunciante" y sin datos periciales
    And se pueden seguir descargando

  Scenario: Caso viejo en borrador
    Given un caso creado antes de esta HU que nunca se generó
    When el perito lo retoma
    Then se comporta como lo defina D12

  # ── Regresión ──────────────────────────────────────────────────────
  Scenario: El resto del flujo no cambia
    When el perito hace una inspección completa en Android y en iOS
    Then la captura, la grabación, el explorador de archivos y la descarga funcionan igual que antes
    And "dotnet build" del Backend y "npx tsc --noEmit" en client/ terminan sin errores
```

---

## Datos que se registran

Los nombres son tentativos; la SDD los fija con su casing JSON real (`snake_case_lower`,
ver hallazgo 6 de la SDD anterior) en la sección **Contrato compartido**.

### Perfil del perito (nuevo, persistente; ver D1)

| Dato | Obligatorio | Uso |
|---|---|---|
| DNI del usuario (clave) | Sí | Vincula el perfil con la identidad de login (`User.Dni`). |
| Nombre completo | Sí | `{nombrePerito}`. Valor inicial: `User.Name`. |
| Matrícula profesional | Sí | `{matriculaPerito}`. Texto libre (puede tener letras y barras). |
| Profesión / título | Sí | `{profesionPerito}`. |
| Carácter | Sí | `{caracterPerito}`. Valor inicial sugerido: "perito informático de parte". |

### Por caso (nuevo)

| Dato | Obligatorio (propuesta, D6) | Uso |
|---|---|---|
| Copia del perfil del perito al crear el caso | Sí (automático) | Congela nombre, matrícula, profesión y carácter en el caso, para que editar el perfil no cambie casos viejos. |
| Tribunal / destinatario | Sí | `{nombreTribunal}` |
| Organismo del tribunal | No | `{organismoTribunal}` (según D3) |
| Sala | No | `{salaTribunal}` |
| Integrantes del tribunal | No | `{integrantesTribunal}` |
| Tipo de causa | Sí | `{tipoCausa}` |
| Número de causa | Sí (ya existe) | `{numeroCausa}` = `NroReferencia` |
| Carátula | Sí | `{caratula}`; también en el historial y la búsqueda |
| Parte denunciante | Sí | `{parteDenunciante}` |
| Parte denunciada | Sí | `{parteDenunciada}` (y P9, según D5) |
| Objeto de la causa | No | `{objetoCausa}` |
| Ámbito | Según D5 | `{ambitoCausa}` |
| Fecha de intervención | Sí | `{fechaIntervencion}` |
| Proponente: nombre, profesión, matrícula | Nombre sí; el resto no | `{nombreProponente}`, `{profesionProponente}`, `{matriculaProponente}` |
| Titular del equipo | Sí | `{titularDispositivo}`. Reemplaza en la UI a "Nombre del denunciante" (`NombreDenunciante`, D5). |
| DNI del titular | No | Búsqueda en el historial. Reemplaza a "DNI del denunciante", que hoy es obligatorio. |
| Línea | No | `{lineaDispositivo}` |
| Tipo de dispositivo | Sí (con valor inicial) | `{tipoDispositivo}` |
| Nombre del equipo según el agente | No (automático) | Hoy se pierde (`Device.name`). Sirve de referencia para la captura de "nombre del dispositivo". |
| Rol de cada captura (`imei_modelo` / `nombre_dispositivo` / ninguno) | No | Ubica la captura en `{capturasImeiModelo}` o `{capturasNombreDispositivo}`. |
| `objetoInforme` | No | `{objetoInforme}` |
| `descripcionOperacionesRealizadas` | Sí (con borrador) | ídem |
| `descripcionAseguramientoEvidencia` | Sí (con texto por defecto) | ídem |
| `descripcionResultados` | Sí | ídem |
| `descripcionValoracionTecnica` | Sí | ídem |
| `descripcionConclusiones` | Sí | ídem |
| `descripcionNotasTecnicas` | No (con texto por defecto) | ídem |
| `descripcionReserva` | No (con texto por defecto) | ídem |
| Inicio de la inspección | Sí (automático) | `{fechaInspeccion}`, `{horaInspeccion}` (D11) |

### Configuración del estudio (nueva, local y no versionada, como `Branding`)

| Dato | Obligatorio | Uso |
|---|---|---|
| Domicilio constituido | No (vacío por defecto) | `{domicilioConstituido}` (D3). Si está vacío, la frase del domicilio se omite o queda según defina la SDD. |
| Zona horaria del informe | No (default `America/Argentina/Buenos_Aires`) | `{fechaInspeccion}`, `{horaInspeccion}` (D11) |
| Textos por defecto (aseguramiento, notas técnicas, reserva y plantilla del borrador de operaciones) | No (hay defaults versionados neutros) | Valor inicial del paso "Informe" (D7) |

**Regla dura:** los datos nuevos se agregan a los documentos **nuevos**. Ningún
proceso migra ni completa casos existentes (D12).

---

## Diseño UX/UI (`client/`, web)

Esta HU **no rediseña** el wizard (D14): reutiliza `FormField`, `.input`, `.card`,
`StepIndicator` y los componentes PrimeReact del sistema de diseño base donde ya estén
adoptados. Agrega campos y un paso.

### Pasos del wizard (de 5 a 6)

| # | Etiqueta (`STEPS`) | Contenido |
|---|---|---|
| 1 | Dispositivo | Sin cambios. |
| 2 | **Causa** (antes "Expediente") | Columna izquierda: identidad del equipo, igual que hoy. Columna derecha, en secciones plegables: **Tus datos de perito** (tarjeta; plegada si el perfil está completo), **Actuación** (tribunal, organismo, sala, integrantes, tipo y número de causa, carátula, objeto, ámbito, fecha de intervención), **Partes** (denunciante, denunciada, proponente con nombre, profesión y matrícula), **Equipo** (titular, DNI del titular, línea, tipo; IMEI como hoy). El indicador de progreso `n/3` pasa a contar los obligatorios de todo el paso. |
| 3 | Captura | Igual que hoy, más: en la galería de capturas cada miniatura tiene un menú "Marcar como…" → "IMEI y modelo" / "Nombre del dispositivo" / "Sin marca". Arriba de la galería, un recordatorio con dos chips de estado ("IMEI y modelo: 1 captura", "Nombre del dispositivo: falta") y una ayuda: "Marcá *#06# o abrí Ajustes › Acerca del teléfono y capturá la pantalla". El bloque de fotos de identidad cambia según D9. |
| 4 | **Informe** (nuevo) | Un `textarea` autoajustable por sección, en el orden de la plantilla: Objeto del informe (opcional), Operaciones realizadas, Aseguramiento de la evidencia, Resultados, Valoración técnica, Conclusiones, Notas técnicas, Reserva. Cada uno con su etiqueta, "· opcional" donde corresponde y un botón "Restaurar texto por defecto" en los que tienen default. **Autoguardado** con indicador discreto ("Guardado hace un momento" / "Guardando…" / "No se pudo guardar · Reintentar"). |
| 5 | Generar | Igual que hoy, más una **lista de verificación** de obligatorios: cada faltante es un enlace al paso y al campo. "Generar informe" está deshabilitado mientras falte algo. El resumen muestra la carátula en lugar del denunciante. |
| 6 | Listo | Igual que hoy (descarga de ZIP y DOCX y hash). La "clave" se conserva tal cual (hallazgo 2, fuera de alcance). |

### Perfil del perito

- **En el wizard:** la tarjeta del paso 2. La primera vez aparece desplegada, con
  nombre precargado. Al confirmar el paso se guarda el perfil y después se crea el
  caso.
- **Fuera del wizard:** un ítem "Mi perfil de perito" en `UserMenu` que abre un
  diálogo (PrimeReact `Dialog`) con los mismos cuatro campos.

### Historial y tarjetas

- `CaseHistory`: la columna "Denunciante" pasa a **"Carátula"** (con el titular
  debajo, en gris). En casos viejos sin carátula se muestra el titular
  (`nombre_denunciante`) como hoy. La búsqueda incluye carátula, partes, titular,
  IMEI y número de causa.
- `CaseCard`/`CaseGridCard`: "Fiscal" → "Perito", "DNI Fiscal" → se quita (el DNI es
  dato de login). Se suma la carátula.

### Estados de error y feedback

- Validación por campo como hoy (`FormField` con `error` y foco en el primer error).
- Si falla el guardado del perfil o del caso: mensaje global (`globalError`) y los
  datos cargados **no se pierden**.
- Si falla el autoguardado del paso 4: el texto queda en el estado local, se muestra
  "No se pudo guardar · Reintentar" y no se puede avanzar a Generar hasta guardar.
- Al salir del wizard con cambios sin guardar: se mantiene el `ConfirmDialog` actual.

### Accesibilidad

Cada `textarea` con `label` asociado; los chips de estado de las capturas con texto
(no solo color); el menú "Marcar como…" operable con teclado; la lista de faltantes
del paso 5 con enlaces reales que llevan el foco al campo.

---

## Fuera de alcance

- Cifrar el ZIP de verdad o sacar la "clave" (hallazgo 2): conviene una tarea aparte.
- Más de un IMEI por equipo (dual SIM).
- Varias plantillas de informe elegibles por caso, o plantillas subidas por el cliente
  desde la UI (salvo que D13 diga otra cosa).
- Rediseño visual del wizard y del dashboard: es de `rediseno-dashboard` (D14).
- Administración de usuarios, roles o un almacén de usuarios propio: el perfil del
  perito se cuelga del DNI de login (la autenticación es de `auth-e-integraciones-sin-mpf`).
- Firma digital del DOCX o conversión a PDF.
- Textos por defecto propios de cada perito (guardar "mis textos"): los defaults son
  del estudio (D7).
- Migrar, completar o regenerar casos e informes existentes (D12).
- Cambios en Tatana o en `agent-ui/`.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- **Plantilla v4:** se arma a partir del `.docx` del usuario (en `docs/`, excluido de
  git). Antes de versionarla en `Templates/` hay que: reemplazar el domicilio y el
  organismo escritos a mano por placeholders, renombrar los placeholders a los
  canónicos, agregar el membrete de Branding y el bloque de firma (D13), y
  verificar que no queda ningún dato real. Los ajustes de layout de la v3
  (`NormalizePageFlow`, `PhotoMaxW/H`, panel de fotos) no aplican a la v4.
- Los placeholders de la plantilla del usuario pueden venir partidos en varios runs:
  `ReplaceTextInBody` ya lo resuelve por párrafo. Los textos largos con saltos de
  línea necesitan el mismo tratamiento que `{CONTACTO}` (un párrafo o `<w:br/>` por
  línea), no un solo `<w:t>`.
- La fila modelo de la tabla de hashes se identifica por contener `{nombreArchivo}`.
- `IsGeneratedArtifact` depende del prefijo `informe_forense_`: si el nombre del
  archivo cambia (sugerido `informe_pericial_<numeroCausa>_<titular>.docx`), hay que
  actualizarlo **sin dejar de reconocer el prefijo viejo**.
- Hace falta un endpoint para actualizar los datos de un caso en borrador (hoy no
  existe) y endpoints para leer y guardar el perfil. Los campos persistidos actuales
  (`NombreDenunciante`, `DniDenunciante`, …) **no se renombran** en Mongo ni en JSON:
  cambia la etiqueta en la UI, no el contrato (D15).
- La marca de rol de cada captura puede vivir en el caso (como `FileSources`) o en el
  nombre del archivo. Lo decide la SDD, teniendo en cuenta que hoy la clasificación es
  por subcadena del nombre (`CaptureStep.fileType`, `GenerateStep`, `EmbedImages`).
- Regla dura de datos para los implementadores: pruebas que escriben en Mongo limpian
  solo por los `_id` propios; los casos y perfiles existentes no se tocan.
- Recordatorios del arnés: skills de frontend obligatorias; reparto backend/frontend.

---

## Dudas para validar con el usuario

### D1. Dónde viven los datos del perito (nombre, matrícula, profesión, carácter)
- **A) Perfil persistente en Mongo**, una colección nueva con clave en el DNI de
  login. Se edita en el wizard (la primera vez) y en "Mi perfil". Al crear un caso se
  **copia** en el caso.
- **B) Configuración del estudio** (como `Branding`): un solo perito por instalación.
- **C) Por caso**: se cargan en cada inspección.
- **Recomendada: A.** Es lo único que permite varios peritos en el mismo estudio sin
  repetir datos, y la copia en el caso garantiza que un cambio de matrícula no altere
  informes viejos. B se rompe con el segundo perito y C es tedioso y propenso a
  errores. **Pregunta asociada:** ¿el usuario que inicia sesión en Factum es siempre el
  perito que firma? Se asume que sí. Si un abogado puede operar en nombre de un
  perito, el perfil tendría que elegirse por caso (otra HU).

### D2. Nombres canónicos de los placeholders
La plantilla tiene duplicados con nombres distintos (`{usuarioNombreCompleto}` y
`{nombreUsuario}`; `{matricula}` y `{matriculaUsuario}`; `{profesion}` y
`{profesionUsuario}`), un typo (`{causaDisiplinariaYNumero}`), una barra
(`{tribunal/fiscalia}`), una mayúscula inicial (`{MarcaModeloDispositivo}`) y nombres
poco claros (`{personasTribunalPrefijo}`, `{objetoModificable}`, `{causa}`).
- **A) Adoptar los canónicos del inventario** (camelCase, `dato + Entidad`:
  `{nombrePerito}`, `{matriculaPerito}`, `{nombreTribunal}`, `{tipoCausa}` +
  `{numeroCausa}`, `{marcaModeloDispositivo}`, `{integrantesTribunal}`,
  `{objetoInforme}`, …) y editar la plantilla para usarlos.
- **B) Mantener los de la plantilla** y aceptar alias en el código.
- **Recomendada: A.** La plantilla es un contrato. Si tiene dos nombres para el mismo
  dato, alguien termina llenando uno y olvidando el otro. El costo es editar una vez el
  `.docx`.

### D3. Domicilio constituido y organismo, hoy escritos a mano en la plantilla
- **Domicilio constituido:** **A) config del estudio** (`{domicilioConstituido}`,
  local y no versionada, junto a `Branding`); **B) perfil del perito**; **C) por
  caso.** **Recomendada: A**, porque es el domicilio procesal del estudio y lo
  comparten sus peritos. Si un perito necesita otro, se puede sumar después al perfil.
- **Organismo** (la línea con el nombre del colegio profesional): **A) por caso**
  (`{organismoTribunal}`, precargado con el del último caso); **B) config del
  estudio**; **C) texto fijo de la plantilla.** **Recomendada: A**: depende de ante
  quién tramita la causa. Un tribunal de ética de un colegio, un juzgado y una fiscalía
  dependen de organismos distintos.

### D4. Textos fijos de la plantilla que la atan a un tipo de causa y a un género
La plantilla dice en texto fijo "este Tribunal de Ética y Disciplina", "actuación
disciplinaria en trámite", "seguidas en contra **de las** {personasContra}",
"aportado voluntariamente **por la propietaria**" y "**El suscripto** manifiesta".
- **A) Plantilla específica para causas disciplinarias**, tal cual.
- **B) Neutralizar**: "de este Tribunal de Ética y Disciplina" → "de {nombreTribunal}";
  "actuación disciplinaria" → "actuación"; "en contra de las {personasContra}" →
  "seguidas contra {parteDenunciada}" (el artículo va dentro del valor); "por la
  propietaria" → "por su titular" (como ya dice más abajo); "El suscripto" →
  "El/la suscripto/a" o un dato de género en el perfil.
- **C) Varias plantillas** por tipo de causa.
- **Recomendada: B.** Con cambios chicos de redacción, la misma plantilla sirve para
  la próxima causa que no sea disciplinaria o cuya titular no sea mujer. C multiplica
  el mantenimiento. Para "El suscripto", lo más simple es un campo "tratamiento"
  opcional en el perfil (suscripto/suscripta); si preferís, queda masculino fijo.

### D5. Datos de semántica ambigua o superpuesta
- **`{personasContra}` vs `{parteDenunciada}`:** ¿son el mismo dato? **Recomendada:
  sí, uno solo** (`{parteDenunciada}`).
- **`{causa}` ("Ámbito:") vs `{causaDisiplinariaYNumero}` vs `{caratula}`:** ¿qué va
  en "Ámbito"? **Recomendada:** un campo propio `{ambitoCausa}`, opcional, para el
  fuero o jurisdicción (p. ej. "Tribunal de Ética y Disciplina"). Si en la práctica es
  la misma causa, se reemplaza por "{tipoCausa} N° {numeroCausa}" y se elimina el
  campo.
- **`{fechaIntervencion}` vs `{fecha}`:** ¿la fecha de intervención es la del día de
  la inspección, o la de la designación o aceptación del cargo? **Recomendada:** campo
  propio, precargado con la fecha de la inspección y editable.
- **"Denunciante" de hoy vs el titular del equipo:** el `NombreDenunciante` actual
  pasa a ser **titular del dispositivo**, y la parte denunciante es un dato nuevo.
  **Recomendada:** así; el DNI del titular pasa a ser opcional.
- **`Observaciones`:** la plantilla no lo usa. **Recomendada:** se quita del wizard
  (lo cubren "Notas técnicas" y los textos del paso 4) y se sigue mostrando en los
  casos viejos que lo tengan.

### D6. Qué campos son obligatorios
- **Recomendada:**
  - **Perfil:** los cuatro campos.
  - **Causa:** tribunal, tipo y número de causa, carátula, parte denunciante, parte
    denunciada, fecha de intervención, nombre del proponente, titular, tipo de
    dispositivo y el IMEI (como hoy).
  - **Informe:** operaciones realizadas, aseguramiento, resultados, valoración técnica
    y conclusiones.
  - **Capturas:** al menos una "IMEI y modelo" (ver D8).
  - **Opcional:** organismo, sala, integrantes, objeto de la causa, ámbito, profesión
    y matrícula del proponente, DNI del titular, línea, objeto del informe, notas
    técnicas y reserva.

  Criterio: es obligatorio lo que, si falta, deja una frase rota o un informe
  impresentable. Lo demás se omite limpio.
- **Pregunta asociada:** si un opcional queda vacío, ¿se omite la línea entera (p. ej.
  "Línea:" no aparece) o se imprime "No informado"? **Recomendada:** "No informado"
  para los datos del equipo y de las partes, que dejan constancia de que se preguntó, y
  omitir la sección para los textos opcionales (objeto del informe, notas, reserva).

### D7. Textos largos: quién los escribe y de dónde salen los textos por defecto
- **Resultados, valoración y conclusiones:** los escribe el perito, sin texto por
  defecto.
- **Operaciones realizadas:** **borrador generado por Factum** con datos del caso
  (marca y modelo, sistema operativo, conexión por USB, herramienta, cantidad y tipo de
  capturas, grabaciones y archivos extraídos, algoritmo SHA-256), editable.
- **Aseguramiento, notas técnicas y reserva:** texto por defecto editable.
- **¿De dónde salen esos textos por defecto?**
  - **A)** Los redactás vos, **sin datos personales ni de causas**, y se versionan
    como default neutro. Cada estudio los puede reemplazar por config local.
  - **B)** Los extrae el implementador de los `.docx` de referencia.
  - **C)** Sin defaults.
- **Recomendada: A.** Los `.docx` de referencia tienen datos reales de una causa, y por
  regla no se abren ni se copian a archivos versionados. B tiene riesgo de fuga. C
  obliga a escribir cada vez lo que es igual en todos los informes. **Necesito que me
  pases esos textos** (o confirmes que el `architect` proponga una redacción genérica
  para que la revises).

### D8. Capturas de identificación y resto de la evidencia gráfica
- **¿Es obligatoria la captura "IMEI y modelo"?** **Recomendada:** sí, una como
  mínimo (respalda los datos de "Elementos ofrecidos"). "Nombre del dispositivo",
  opcional.
- **¿Cómo se marcan?** **Recomendada:** menú "Marcar como…" sobre cualquier captura
  de la galería del paso 3 (no hace falta un botón de captura aparte).
- **El resto de las capturas** (las del contenido inspeccionado): la plantilla no tiene
  un lugar para ellas. **A)** Quedan solo en el ZIP y en la tabla de hashes. **B)**
  Además se agrega un anexo "Capturas" al final del informe, como hoy `{CAPTURAS}`.
  **Recomendada: B**, con un placeholder `{anexoCapturas}` en la v4. Un informe
  pericial suele mostrar lo que constata, y hoy el informe ya lo hace. Si preferís
  citarlas desde "Resultados" por nombre de archivo, A.

### D9. Fotos de identidad por webcam ("Foto del fiscal" / "Foto del denunciante")
La plantilla no tiene un lugar para fotos de personas.
- **A) Se quitan** del paso 3.
- **B) Se conservan opcionales**, renombradas "Foto del perito" y "Foto del titular",
  y van solo al ZIP (con su hash), no al cuerpo del informe.
- **C) Se conservan y van al informe.**
- **Recomendada: B.** La foto del titular entregando el equipo puede respaldar el
  "aportado en forma voluntaria", pero meter fotos de personas en el cuerpo de un
  informe de parte expone datos personales sin que la plantilla lo pida.

### D10. Hash del contenedor ZIP (hallazgo 1)
Hoy el hash informado es el del ZIP **antes** de agregarle el DOCX, así que no
coincide con el ZIP descargado.
- **A) El DOCX deja de ir dentro del ZIP.** Se entregan dos archivos: el ZIP de
  evidencia (cuyo hash va en el informe y coincide con el descargado) y el DOCX
  aparte. `ResultStep` muestra ambos hashes.
- **B) Se mantiene el DOCX dentro** y el informe aclara que el hash es del contenedor
  "antes de incorporar este informe". Es confuso para un tercero que verifique.
- **C) Dos niveles:** el informe lleva el hash del ZIP de evidencia; el paquete final
  (ZIP de evidencia + DOCX) tiene su propio hash, que se muestra en pantalla y se guarda
  en el caso, pero no puede ir en el informe.
- **Recomendada: A.** Es la única opción en que el hash del informe se verifica
  directamente contra lo que se entrega, y es lo que exige la sección "Cadena de
  custodia" de la plantilla. Los casos viejos no se tocan.

### D11. Fecha y hora de la inspección
Hoy el informe pone la hora de **generación** en **UTC**.
- **Qué momento:** **A)** creación del caso (fin del paso 2); **B)** primera captura;
  **C)** generación. **Recomendada: A**: es cuando empieza la inspección con el equipo
  conectado, y ya está guardado (`CreatedAt`).
- **Zona horaria:** **Recomendada:** hora local configurable, por defecto
  `America/Argentina/Buenos_Aires` (UTC-3), con formato `dd/MM/yyyy` y `HH:mm`.

### D12. Casos viejos (regla dura: no se modifican datos preexistentes)
- **Casos ya generados:** no se tocan. Sus DOCX y ZIP siguen en disco y se descargan
  igual. En la UI se muestran con la terminología nueva y sin datos periciales.
  (Decidido por la regla; no es duda.)
- **Casos viejos en borrador** (creados y nunca generados):
  - **A)** Al retomarlos se piden los datos nuevos (paso 2 en modo edición + paso 4) y
    se generan con la plantilla pericial. La app escribe los campos nuevos en ese
    documento **por acción del usuario**, sin migración.
  - **B)** Se generan con la plantilla vieja (v3), que habría que conservar.
  - **C)** No se pueden retomar.
- **Recomendada: A.** No es una migración (nadie toca documentos en bloque). Es el
  usuario completando su caso, y así se evita mantener dos plantillas. Si en tu base de
  desarrollo hay borradores viejos que querés conservar intactos como prueba, decímelo y
  vamos por C para esos.

### D13. Plantilla vieja, membrete y firma
- **¿El informe "de extracción" (v3) convive como segunda plantilla?** **A)** Se
  reemplaza por la v4 pericial y la v3 se borra del repo (queda en el historial de git).
  **B)** Conviven, con un selector de tipo de informe por caso. **Recomendada: A**:
  el producto hoy tiene un solo cliente y un solo tipo de informe, y B obliga a mantener
  dos modelos de datos y dos terminologías. Si aparece un cliente fiscal, se reabre como
  HU.
- **Membrete del estudio:** la plantilla no tiene `{LOGO_ORGANIZACION}`,
  `{ORGANIZACION}` ni `{CONTACTO}`. **Recomendada:** agregarlos en el **encabezado de
  todas las páginas** (logo a la izquierda, nombre y contacto a la derecha). Si
  preferís solo en la primera página, se usa un encabezado de primera página.
- **Firma:** la plantilla termina sin bloque de firma. **Recomendada:** agregar al final
  "Firma y aclaración: {nombrePerito} — {profesionPerito} — M.P. {matriculaPerito}", con
  espacio para la firma manuscrita.
- **¿Quién arma la v4?** **Recomendada:** `implementer-backend`, a partir de tu
  `.docx`, siguiendo el anexo de la SDD. Después la revisás abriéndola en Word o WPS
  antes de aprobar.

### D14. ¿Se rediseña el wizard en esta HU?
- **A) Solo se agregan campos, el paso 4 y la terminología**, con los componentes y
  estilos actuales.
- **B) Se rediseña el wizard completo** con el sistema de diseño nuevo, absorbiendo
  parte de `rediseno-dashboard`.
- **Recomendada: A.** Esta HU ya es grande (modelo, endpoints, plantilla y wizard).
  Mezclar rediseño visual con cambio funcional hace más difícil revisar las dos cosas.
  `rediseno-dashboard` se afina después, sobre el wizard ya con los campos nuevos, y no
  tiene que rehacer el trabajo.

### D15. Terminología nueva
| Hoy | Propuesto | Dónde |
|---|---|---|
| Expediente (paso 2), "Datos del expediente" | Causa, "Datos de la causa" | `STEPS`, `CaseFormStep` |
| Número de expediente | Número de causa / expediente | `CaseFormStep`, historial |
| Fiscal / Funcionario | Perito | `CaseCard`, `IdentityCard`, `CaptureStep`, `GenerateStep` |
| DNI Fiscal | (se quita) | `CaseCard` |
| Foto del fiscal | Foto del perito (si D9 = B) | `CaptureStep` |
| Denunciante (quien entrega el equipo) / Titular | Titular del dispositivo | `CaseFormStep`, `CaptureStep`, `GenerateStep`, historial |
| Nombre / DNI del denunciante | Titular del dispositivo / DNI del titular | `CaseFormStep`, validaciones de `page.tsx` |
| "Buscar por fiscal o sigla…" | "Buscar por carátula, titular o IMEI…" | `CaseHistory` |
| "…quién realiza la extracción" | "…quién realiza la inspección" | `CaptureStep` |
| Informe forense / de extracción | Informe pericial | `GenerateStep`, `ResultStep`, nombre del archivo |

- **Recomendada:** aplicar la tabla **solo en etiquetas y textos visibles**, sin
  renombrar los campos persistidos (`nombre_denunciante`, `officer`, …) ni los nombres
  de archivo `foto_funcionario_*`, que son contrato con los casos viejos. Lo que se ve
  en la UI de "sigla" (`UserMenu`: "DNI · sigla") queda para
  `auth-e-integraciones-sin-mpf`.

### D16. ¿Partir la HU?
- **A) Una sola HU**, con el checklist de la SDD ordenado: perfil y modelo → endpoints
  → wizard → plantilla v4 → terminología.
- **B) Dos HU:** (1) datos, wizard y terminología; (2) plantilla v4 y `ReportService`.
- **Recomendada: A.** Con B, quedaría un estado intermedio en el que se piden datos que
  no aparecen en ningún informe, o un informe que no tiene de dónde sacar los datos.
  Ninguna mitad se puede aprobar ni probar sola.

## Validación del usuario (2026-10-01)

Se aceptan **todas las opciones recomendadas** (D1–D16), con estas precisiones:

- **D1:** el usuario que inicia sesión **es siempre el perito que firma**. Perfil persistente por DNI de login (A), copiado al caso.
- **D7:** los textos por defecto (aseguramiento, notas técnicas, reserva) no fueron entregados todavía. El `architect` propone en la SDD una **redacción genérica, sin datos reales**, y el usuario la revisa antes de implementar ese ítem.
- **D12:** sin pedido de conservar borradores viejos → A (se completan al retomarlos, por acción del usuario).
- **Hallazgo "ZIP no cifrado":** se abre la HU aparte `zip-cifrado-real`; fuera de alcance acá.
