# HU: Cifrado real del ZIP de evidencia

**Slug:** `zip-cifrado-real`
**Apps afectadas:** `server/src/Factum.Backend` (`ReportService`, `CaseService`, modelo
`Case`, DTOs, config `Report`, textos por defecto) y `client/` (web: `GenerateStep`,
`ResultStep`, `CaseCard`, `/design-system`). `server/src/Factum.Agent` (Tatana) y
`agent-ui/` **no cambian** (ver Contexto).
**Origen:** hallazgo 2 del `afinador` de `informe-pericial-de-parte`
(`docs/hu-informe-pericial-de-parte.md`, "Hallazgos que afectan a esta HU"; su
validación del 2026-10-01 la mandó a una HU aparte).

**Como** perito informático de parte que entrega la evidencia a un tribunal o a un cliente
**quiero** que el ZIP de evidencia que genera Factum esté cifrado de verdad con la
contraseña que me muestra la herramienta
**para que** el contenido (capturas, grabaciones, fotos de identidad y archivos del
teléfono) no pueda leerlo quien intercepte o reciba el archivo sin la contraseña, y para
que la herramienta no afirme una protección que no existe.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-01, sobre `f3471e7`)

**Generación del ZIP** (`server/src/Factum.Backend/Services/Reports/ReportService.cs`).
El flujo D10 ya está implementado y commiteado (SDD `Refactorizaciones/informe-pericial-de-parte.md`,
§7.1). `GenerateAsync` (L58-107) hace lo siguiente:
1. Borra restos de intentos anteriores.
2. Ordena la evidencia por nombre ordinal.
3. Genera la contraseña (`GeneratePassword()`, L1118: 16 caracteres del alfabeto
   `A-Z` sin `I`/`O` y `2-9`, 32 símbolos, `RandomNumberGenerator`, unos 80 bits) y
   calcula el SHA-256 de cada archivo.
4. Arma el ZIP **solo con la evidencia** (`CreateZipAsync`, L1079-1094) y calcula
   `zipHash = SHA-256(zip)`. El ZIP no se vuelve a abrir.
5. Genera el DOCX con `zipHash` en la tabla de hashes y calcula `reportHash`.
6. **Borra los archivos sueltos de la evidencia** (L98-103). Desde ahí, el ZIP es la
   **única copia** de la evidencia en el servidor.

**El ZIP no está cifrado.** `CreateZipAsync` usa `System.IO.Compression.ZipArchive`,
que no soporta cifrado. Ni siquiera recibe la contraseña: el ZIP sale en claro y
cualquier programa lo abre sin pedir nada. La contraseña se genera, se guarda y se
muestra, pero **no protege nada**.

**Librerías** (`Factum.Backend.csproj`): `MongoDB.Driver 3.4.0`, `DocumentFormat.OpenXml`,
`QuestPDF`, `Swashbuckle` y `JwtBearer`. Ninguna escribe ZIP cifrados. El warning
**NU1902** viene de **`SharpCompress 0.30.1`**, que entra como dependencia
**transitiva de `MongoDB.Driver`** (`obj/project.assets.json`), igual que `Snappier 1.0.0`
(NU1903). Factum no usa SharpCompress directamente, y además SharpCompress **no sabe
escribir** ZIP cifrados: solo los lee. No es la herramienta para esta HU.

**De dónde sale la contraseña y por dónde viaja:**

| Lugar | Qué hace hoy |
|---|---|
| `ReportService.GeneratePassword()` | La genera. |
| `ReportResult.Password` → `CaseService.GenerateAsync` (L354-360) | La persiste y la devuelve. |
| `CaseRepository.UpdateGeneratedAsync` (`Infrastructure/MongoRepository.cs` L90-98) | La guarda **en claro** en `cases.ZipPassword` (Mongo). |
| `Models/Case.cs` L62 `ZipPassword` | Se serializa como `zip_password` (la API usa `SnakeCaseLower`, `Program.cs` L62/67) en **todas** las respuestas que devuelven el `Case`: `GET /api/cases` (el listado solo excluye `ReportTexts`, L75), `GET /api/cases/{id}` y `POST …/generate`. |
| `DTOs/CaseDtos.cs` `GenerateResponse.Password` | JSON `password`. |
| `client/src/app/dashboard/page.tsx` L310 | La lee de `res.password` y se la pasa a `ResultStep`. |
| `client/src/components/ResultStep.tsx` L93-102 | "Contraseña del ZIP", con la ayuda "Guardala por seguridad". |
| `client/src/components/CaseCard.tsx` L121-129 | "Contraseña ZIP" **siempre visible** en el historial para cualquier caso con `zip_password`. Botón "ZIP cifrado" (L159). |
| `client/src/lib/api.ts` L149 | `Case.zip_password?: string`. |
| Informe DOCX | La plantilla v4 **no** tiene `{CLAVE}` y `ReportService` no la escribe en el informe. Bien. |

**Textos que hoy afirman un cifrado que no existe:**
- `client/src/components/dashboard/GenerateStep.tsx` L161: "ZIP cifrado con AES-256 y
  contraseña única".
- `client/src/components/CaseCard.tsx` L159: botón "ZIP cifrado".
- `client/src/components/design-system/DesignSystemShowcase.tsx` L125 y L428: demos
  ("Informe PDF + ZIP cifrado", "El PDF y el ZIP cifrado quedaron guardados").
- `README.md` L6, L49 y L423 ("el ZIP final queda cifrado con contraseña"),
  `AGENTS.md` (tabla de stack: "informes (PDF + ZIP cifrado)").
- La SDD `informe-pericial-de-parte` (§3, punto 74) dejó estas frases a propósito para
  esta HU.

**Informe y cadena de custodia.** La plantilla v4 (`ops/plantilla/build_plantilla_v4.py`)
no tiene texto fijo sobre el cifrado. La sección de aseguramiento es
"A fin de resguardar la información obtenida:" seguida de
`{descripcionAseguramientoEvidencia}`, cuyo texto por defecto está en
`Services/Reports/ReportDefaultTexts.cs` (L21-22) y se puede reemplazar con
`Report:DefaultTexts:*`. Ese texto dice que los archivos "se agruparon en un contenedor
ZIP, cuyo valor hash SHA-256 se calculó una vez cerrado el contenedor" y **no menciona el
cifrado**. Los textos por defecto se resuelven al pedirlos
(`GET /api/cases/{id}/report-texts/defaults`) y quedan guardados en el caso
(`Case.ReportTexts`) cuando el perito los edita o confirma.

**Descarga.** `GET /api/cases/{id}/download/{filename}?token=…` entrega el archivo tal
cual, con `application/zip`.

**Agente Tatana / `agent-ui/`.** No participan: no arman ZIP ni manejan la contraseña.
La única mención a "zip" en `Factum.Agent` es el modo portátil de su propio paquete
(`HealthController`). `agent-ui/src` no menciona ZIP ni cifrado.

**Casos ya generados.** Todos los casos `completed` que hay hoy en la base (con
`schema_version` 0 y 1) tienen un ZIP **sin cifrar** y un `ZipPassword` guardado que no
abre nada. Por la regla dura de datos de `AGENTS.md`, ni sus ZIP ni sus documentos se
tocan.

**Config pública.** Ya existe `GET /api/config/public` (`ConfigController`). Sirve para
avisarle al cliente si el cifrado está activo antes de generar.

### Qué es lo nuevo

1. El ZIP de evidencia se escribe **cifrado** con la contraseña generada, con el
   algoritmo que se valide en D1.
2. El hash informado (tabla del DOCX, `zip_hash` y pantallas) es el SHA-256 del **ZIP
   cifrado que se descarga** (se mantiene D10).
3. Antes de borrar los archivos sueltos, Factum **verifica** que el ZIP cifrado se abre
   con la contraseña y que cada archivo coincide con su hash (D6).
4. El caso registra **si su ZIP está cifrado** (campo nuevo, D8). La UI solo afirma
   "cifrado" cuando ese campo lo confirma.
5. La contraseña deja de viajar en el listado y en el detalle del caso: se muestra una vez
   en `ResultStep` y después solo bajo pedido explícito (D3).
6. La UI le indica al perito con qué programas se abre el ZIP y que entregue la
   contraseña por un canal distinto al del ZIP (D2, D4).
7. Se corrigen los textos de README, AGENTS.md, `GenerateStep`, `CaseCard` y
   `/design-system`.

---

## Criterios de aceptación

```gherkin
Feature: Cifrado real del ZIP de evidencia

  Background:
    Given el perito inició sesión
    And el cifrado del ZIP está activo en la configuración (valor por defecto)

  Scenario: El ZIP generado está cifrado con la contraseña mostrada
    Given un caso pericial en borrador con evidencia capturada y los obligatorios completos
    When el perito genera el informe
    Then se descarga un archivo "evidencia_<ref>_<titular>.zip"
    And cada archivo de evidencia dentro del ZIP está cifrado con AES-256 (WinZip AE-2)
    And al abrirlo con 7-Zip sin contraseña no se puede extraer ningún archivo
    And al abrirlo con 7-Zip con la contraseña mostrada en "Listo" se extraen todos los archivos
    And el SHA-256 de cada archivo extraído coincide con el de la tabla del informe

  Scenario: El hash informado es el del ZIP cifrado que se descarga
    Given un caso recién generado
    When el perito calcula el SHA-256 del ZIP descargado
    Then coincide con el "Hash SHA-256 del ZIP de evidencia" de la pantalla "Listo"
    And coincide con la fila del ZIP en la tabla de hashes del informe
    And coincide con el zip_hash del caso en el historial
    And para calcularlo no hace falta la contraseña

  Scenario: Verificación del ZIP antes de borrar los archivos sueltos
    Given que el ZIP cifrado ya se escribió
    When Factum lo reabre en modo lectura con la contraseña
    And cada archivo descifrado tiene el mismo SHA-256 que el original
    Then recién ahí se borran los archivos sueltos de la evidencia
    And el hash del ZIP no cambia por haberlo leído

  Scenario: Falla la verificación del ZIP
    Given que el ZIP cifrado no se puede abrir con la contraseña o un archivo no coincide
    When termina la verificación
    Then la generación falla con el caso en estado "error"
    And los archivos sueltos de la evidencia NO se borran
    And el ZIP inválido y el DOCX de ese intento se borran
    And el perito ve "No se pudo generar el informe" con el detalle

  Scenario: La contraseña no va en el informe
    When se genera el informe
    Then el DOCX no contiene la contraseña del ZIP

  Scenario: La contraseña se muestra al terminar
    When termina la generación
    Then la pantalla "Listo" muestra la contraseña con un botón "Copiar"
    And avisa que el ZIP se abre con 7-Zip, WinRAR, Keka o The Unarchiver, y no con el Explorador de Windows ni con la Utilidad de Archivo de macOS
    And recomienda entregar la contraseña por un canal distinto al del ZIP
    And advierte que sin la contraseña la evidencia no se puede recuperar

  Scenario: La contraseña no viaja en el listado ni en el detalle del caso
    When el cliente pide GET /api/cases o GET /api/cases/{id}
    Then la respuesta no incluye zip_password

  Scenario: El perito recupera la contraseña de un caso cifrado desde el historial
    Given un caso generado con ZIP cifrado, del perito que inició sesión
    When abre el caso en el historial y toca "Mostrar contraseña"
    Then se pide la contraseña a un endpoint dedicado
    And se muestra con un botón "Copiar"

  Scenario: Otro usuario no puede pedir la contraseña
    Given un caso generado por otro perito
    When se pide su contraseña al endpoint dedicado
    Then la respuesta es la misma que hoy da un caso ajeno (404)

  Scenario: Caso viejo con ZIP sin cifrar
    Given un caso generado antes de esta HU (sin zip_encrypted, o con false)
    When el perito lo abre en el historial
    Then el botón de descarga dice "ZIP de evidencia" y no "ZIP cifrado"
    And se muestra la aclaración "Este ZIP se generó sin cifrar"
    And no se muestra contraseña ni el botón "Mostrar contraseña"
    And ni el documento del caso ni su ZIP se modifican

  Scenario: Resumen antes de generar
    Given el cifrado está activo
    When el perito llega a "Revisá y generá el informe"
    Then la garantía dice "ZIP cifrado con AES-256 y contraseña única"

  Scenario: Cifrado desactivado por configuración
    Given Report:EncryptZip = false
    When el perito genera el informe
    Then el ZIP se genera sin cifrar, como hoy
    And el caso queda con zip_encrypted = false y sin contraseña
    And "Revisá y generá el informe" y "Listo" no hablan de cifrado ni de contraseña

  Scenario: Texto de aseguramiento por defecto
    Given el cifrado está activo
    When el perito pide los textos por defecto del paso Informe
    Then el texto de aseguramiento menciona que el contenedor ZIP se cifró con AES-256
    And dice que la contraseña se entrega por un canal separado
    And no incluye la contraseña
```

---

## Datos que se registran

| Dato | Dónde | Obligatorio | Uso |
|---|---|---|---|
| `ZipEncrypted` (JSON `zip_encrypted`) | `cases` (nuevo) | Sí en los casos generados desde esta HU. Ausente en los viejos, que se leen como `false` | Única fuente para que la UI diga "cifrado". Lo fija `GenerateAsync`. |
| `ZipEncryption` (JSON `zip_encryption`, p. ej. `"aes256-ae2"`) | `cases` (nuevo, opcional) | No | Deja asentado el algoritmo si después cambia. A definir por el `architect` si alcanza con el bool. |
| `ZipPassword` | `cases` (existe) | Solo si `ZipEncrypted` | Se sigue guardando para que el perito pueda recuperarla (D3). Deja de salir en el listado y en el detalle. |
| `ZipHash` | `cases` (existe) | Sí | SHA-256 del ZIP **cifrado** final (D5). |
| `Report:EncryptZip` | config (`appsettings`/local) | No (default `true`) | Interruptor global (D9). |
| `encrypt_zip` en `GET /api/config/public` | respuesta (nuevo) | — | Para que `GenerateStep` sepa qué prometer antes de generar. |

Los casos ya existentes **no se migran** ni se reescriben.

---

## Diseño UX/UI (`client/`, web)

**`GenerateStep` (paso "Revisá y generá el informe"):** la garantía "ZIP cifrado con AES-256
y contraseña única" se muestra solo si `encrypt_zip` es `true` en la config pública. Si
es `false`, dice "ZIP de evidencia con hash SHA-256 verificable".

**`ResultStep` (pantalla "Listo"):**
- Bloque "Contraseña del ZIP" (solo si el ZIP salió cifrado):
  - la contraseña en monoespaciada;
  - botón **Copiar** con feedback "Copiada" accesible (`aria-live`);
  - en lugar de "Guardala por seguridad": "Sin esta contraseña la evidencia no se puede
    abrir. Entregala por un canal distinto al del ZIP (no en el mismo correo ni en el
    mismo pendrive)."
- Nota de compatibilidad junto al botón "Descargar ZIP": "Cifrado AES-256. Se abre con
  7-Zip o WinRAR (Windows) y con Keka o The Unarchiver (macOS). El Explorador de Windows
  y la Utilidad de Archivo de macOS no lo abren."
- Los checks suman "ZIP cifrado con AES-256".
- Si el cifrado está desactivado: no hay bloque de contraseña ni nota de cifrado.

**`CaseCard` (historial):**
- Ya no lee `cas.zip_password`.
- Si `zip_encrypted`: botón "ZIP cifrado" y una fila "Contraseña ZIP" con el botón
  **Mostrar contraseña**. Al tocarlo se llama al endpoint dedicado y se muestra la
  contraseña con **Copiar**. Mientras carga, se ve un estado de carga. Si falla, aparece
  un error en línea ("No se pudo obtener la contraseña. Probá de nuevo.").
- Si no: botón "ZIP de evidencia" y una nota tenue "Este ZIP se generó sin cifrar".
- El hash se muestra igual que hoy.

**`/design-system`:** las demos dejan de decir "ZIP cifrado" (texto neutro, p. ej.
"Informe Word + ZIP de evidencia").

**Errores:** la falla de verificación del ZIP aparece por el camino de error que ya
tiene la generación ("Error generando informe: …"). Si la librería no puede cifrar, se
corta la generación: **nunca** se cae silenciosamente a un ZIP sin cifrar cuando la
config pide cifrado.

---

## Fuera de alcance

- Volver a cifrar, regenerar o migrar los ZIP de casos ya generados. Tampoco se borra
  `ZipPassword` de esos documentos.
- Cifrar el DOCX del informe (D11).
- Firma digital del ZIP o del informe.
- Cifrar la contraseña en reposo con una clave de servidor (Data Protection o KMS) y
  rotar claves (D3 lo deja anotado como mejora).
- Elegir el cifrado caso por caso desde el wizard (D9).
- Ocultar los nombres de archivo dentro del ZIP (D1).
- Actualizar `MongoDB.Driver` para sacar el NU1902/NU1903 de SharpCompress/Snappier. Es
  otro tema: se puede anotar como tarea suelta.
- Cambios en Tatana o en `agent-ui/`.
- Cambios en la plantilla v4 (`.docx` y `build_plantilla_v4.py`).
- Auditar quién reveló la contraseña y cuándo.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- `System.IO.Compression` no cifra. Hay que sumar una librería que **escriba** ZIP AES
  (WinZip AE-2). El candidato natural es `SharpZipLib` (`ZipOutputStream` con `Password`
  y `AESKeySize = 256`). DotNetZip está abandonada y tiene CVE, y SharpCompress no
  escribe cifrado. El `architect` confirma versión, licencia, soporte de Zip64 (las
  grabaciones pueden superar los 4 GB acumulados) y que no sume warnings NU19xx.
- AES-ZIP usa sal aleatoria: el mismo contenido da un ZIP con otro hash en cada
  generación. Como el caso no se regenera, no es un problema, pero conviene dejarlo en
  las notas técnicas.
- La verificación (D6) lee el ZIP con la misma librería. Tiene que ser de solo lectura
  para no alterar el hash ya calculado.
- Contrato compartido nuevo:
  - `zip_encrypted` en `Case`;
  - `encrypt_zip` en `/api/config/public`;
  - un endpoint dedicado para la contraseña, del tipo `GET /api/cases/{id}/zip-password` → `{ "password": "…" }`;
  - `zip_password` sale de las respuestas de `Case`;
  - `GenerateResponse.password` se mantiene, vacío o `null` si no hay cifrado.

  Del lado del cliente se toca `client/src/lib/api.ts`.
- El texto por defecto de aseguramiento (`ReportDefaultTexts.AseguramientoEvidencia`)
  cambia según `Report:EncryptZip`. Los casos que ya guardaron sus textos conservan los
  suyos.
- Actualizar `README.md` (L6, L49, L423, sección de hashes ~L338) y la fila de stack
  de `AGENTS.md`.

---

## Dudas para validar con el usuario

### D1. Algoritmo y formato del contenedor
- **A) ZIP con AES-256 (WinZip AE-2).** Sigue siendo `.zip` (el nombre, el flujo D10 y
  la descarga no cambian). Lo abren 7-Zip, WinRAR, Keka y The Unarchiver. El
  **directorio del ZIP queda en claro**: se ven los nombres y tamaños de los archivos,
  pero no su contenido.
- **B) ZipCrypto (el "cifrado ZIP clásico").** Lo abre el Explorador de Windows, pero es
  **criptográficamente roto**: hay ataques de texto plano conocido prácticos, sobre todo
  con PNG/JPG, cuyas cabeceras son predecibles.
- **C) 7z con AES-256 y cabeceras cifradas.** Oculta también los nombres, pero cambia el
  formato a `.7z`, no hay un escritor .NET maduro (habría que invocar el binario de
  7-Zip en el servidor y sumarlo a la imagen Docker) y el destinatario necesita 7-Zip sí
  o sí.
- **Recomendada: A.** Es el estándar de hecho para entregar evidencia con contraseña,
  conserva el `.zip` y el flujo D10, y la protección es real. B es descartable para
  evidencia: el cifrado sería tan simbólico como hoy. La exposición de los nombres en A
  es aceptable porque los nombres de Factum son genéricos (`screenshot_<ts>.png`,
  `foto_*`, `device_pull_*`). Si algún día hace falta ocultarlos, se puede pasar a C en
  otra HU.

### D2. Compatibilidad: qué se le dice al destinatario
El Explorador de Windows y la Utilidad de Archivo de macOS **no abren ZIP AES**. Un
juzgado puede tener solo el Explorador.
- **A) Solo una leyenda en la UI** (`ResultStep`), para que el perito lo sepa.
- **B) Leyenda en la UI y una frase en el texto por defecto de aseguramiento** del
  informe, que nombre el algoritmo y diga que se abre con herramientas como 7-Zip.
- **C) B, más un `LEAME.txt` sin cifrar dentro del ZIP** con instrucciones.
- **Recomendada: B.** El informe es lo que lee el tribunal: que diga el algoritmo y con
  qué se abre evita que la evidencia se tome por "corrupta". C mete en el contenedor un
  archivo que no es evidencia y que tendría que figurar en la tabla de hashes. Es más
  ruido que valor.

### D3. Custodia de la contraseña: ¿se guarda en la base?
Dato clave: después de generar, **los archivos sueltos se borran** y el ZIP es la única
copia. Perder la contraseña es perder la evidencia.
- **A) Como hoy:** se guarda en claro y viaja en **todas** las respuestas del caso
  (listado incluido) y se ve siempre en el historial.
- **B) Se guarda en Mongo, pero sale del listado y del detalle.** Se muestra en "Listo"
  y, después, solo bajo pedido ("Mostrar contraseña") en un endpoint dedicado,
  restringido al perito dueño del caso.
- **C) No se guarda:** se muestra una sola vez en "Listo".
- **D) B, pero guardada cifrada** con una clave del servidor (ASP.NET Data Protection).
- **Recomendada: B.** C convierte un descuido del perito en evidencia irrecuperable. A
  la expone en cada listado y en cualquier captura de pantalla del historial. D protege
  poco: quien accede al servidor ya tiene el ZIP al lado, y si se pierden las claves de
  Data Protection (volumen Docker) se pierden todas las contraseñas. El cifrado protege
  la **entrega** (pendrive, correo, nube), no el servidor. D queda anotada como mejora.

### D4. ¿La contraseña va en el informe?
- **A) No.** El informe solo dice que el ZIP está cifrado y que la contraseña se entrega
  por un canal separado.
- **B) Sí,** en una sección o anexo.
- **Recomendada: A.** El informe y el ZIP se presentan juntos: con la contraseña dentro,
  el cifrado no protege nada. La plantilla v4 ya no tiene `{CLAVE}`, así que A no
  requiere tocarla.

### D5. Sobre qué se calcula el hash del ZIP
- **A) Sobre el ZIP cifrado final** (el que se descarga), como pide D10. La tabla
  conserva además el SHA-256 de cada archivo **en claro**.
- **B) Además, un "hash del contenido"** (por ejemplo, del ZIP sin cifrar o de la
  concatenación de los hashes).
- **Recomendada: A.** Cualquiera verifica la integridad del archivo entregado **sin
  necesitar la contraseña**, y quien la tenga verifica cada archivo extraído contra la
  tabla. B suma un valor que nadie puede recomputar con herramientas comunes. Se deja
  asentado que, por la sal aleatoria de AES, dos generaciones del mismo contenido darían
  hashes distintos (no aplica, porque el caso no se regenera).

### D6. Verificación del ZIP antes de borrar los archivos sueltos
- **A) Sí:** reabrir el ZIP en solo lectura con la contraseña y comparar el SHA-256 de
  cada entrada con el del original. Si algo falla, error y no se borran los sueltos.
- **B) No:** confiar en la librería, como hoy.
- **Recomendada: A.** Al cifrar, un bug de la librería o de la contraseña deja un ZIP que
  "existe" pero no se abre, y después de borrar los sueltos ya no hay vuelta atrás. El
  costo es releer la evidencia una vez (algunos segundos más con videos grandes).

### D7. Cadena de custodia: texto del informe
- **A) Actualizar el texto por defecto de aseguramiento** (`ReportDefaultTexts` y su
  override `Report:DefaultTexts:AseguramientoEvidencia`). Agrega que el contenedor se
  cifró con AES-256, que su hash se calculó sobre el contenedor cifrado y que la
  contraseña se entrega por un canal separado (más la frase de compatibilidad de D2).
  La plantilla v4 no se toca.
- **B) Agregar un párrafo fijo a la plantilla v4.**
- **C) No tocar el informe.**
- **Recomendada: A.** El aseguramiento es justamente la sección de la v4 donde va esto,
  y ya es editable por caso y reemplazable por estudio. B obliga a regenerar la
  plantilla y deja el párrafo aunque el cifrado esté apagado (D9). C deja el informe
  callado sobre una medida que el perito sí tomó. Limitación: los casos en borrador que
  ya guardaron sus textos conservan los suyos y el perito tiene que ajustarlos a mano.
  El `architect` propone la redacción exacta y el usuario la revisa en la prueba manual.

### D8. Casos ya generados (ZIP sin cifrar)
- **A) Campo nuevo `zip_encrypted`** que fija la generación. Si falta o es `false`, el
  ZIP es "sin cifrar": la UI no dice "cifrado" ni muestra la contraseña vieja (aunque
  `ZipPassword` siga en el documento).
- **B) Inferirlo de `schema_version`** o de la fecha de generación.
- **C) Seguir mostrando la contraseña vieja** de los casos anteriores.
- **Recomendada: A.** Es explícito, no depende de cuándo se hizo el deploy y además
  cubre los casos generados con el cifrado apagado (D9). B mezcla dos conceptos
  (versión del informe y cifrado). C sigue mostrando una "contraseña" que no abre nada,
  que es justo lo que esta HU corrige. No se toca ningún documento viejo.

### D9. ¿El cifrado es opcional?
- **A) Siempre cifrado,** sin opción.
- **B) Interruptor global `Report:EncryptZip`** en la config del estudio, con default
  `true`.
- **C) Opción por caso** en el wizard ("Cifrar ZIP").
- **Recomendada: B.** Si los destinatarios de un estudio no pueden abrir AES (D2),
  tiene una salida sin tocar código, y por defecto se cifra. C suma decisiones y estados
  al wizard por un caso raro. Si aparece la necesidad real, se hace en otra HU. Con B,
  `zip_encrypted` (D8) mantiene honesta a la UI caso por caso.

### D10. Formato de la contraseña
Hoy son 16 caracteres de un alfabeto de 32 símbolos sin ambiguos (`I`, `O`, `0`, `1`):
unos 80 bits.
- **A) Mantenerla** y sumar el botón "Copiar".
- **B) Mostrarla en grupos de 4** (`ABCD-EFGH-…`), con o sin guiones en la contraseña
  real.
- **C) Alargarla** (por ejemplo, a 24).
- **Recomendada: A.** 80 bits con la derivación de clave de AES-ZIP es suficiente para
  esta amenaza, y el alfabeto ya evita confusiones al dictarla. B genera la duda de si
  los guiones forman parte de la contraseña. Si se agrupara solo visualmente, el texto
  copiado tendría que salir sin guiones: más complejidad sin beneficio claro.

### D11. ¿Se cifra también el DOCX?
El DOCX va aparte, sin cifrar, y su anexo incluye las **capturas del dispositivo**.
- **A) No,** queda fuera de alcance.
- **B) Sí,** con contraseña de apertura de Word.
- **Recomendada: A.** El informe es el documento que se presenta y firma ante el
  tribunal: tiene que abrirse sin fricción. La contraseña de Word complica su uso y
  protege poco. Si el estudio necesita proteger el informe en tránsito, es otra HU, y
  se deja registrado que hoy las capturas del anexo viajan en claro dentro del DOCX.

## Validación (2026-10-01, modo autónomo)

El usuario activó el modo autónomo. El orquestador toma **todas las opciones recomendadas** (D1–D11) como validadas.
