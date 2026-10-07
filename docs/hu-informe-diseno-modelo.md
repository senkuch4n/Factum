# HU: Diseño visual del informe pericial según el modelo del estudio

**Slug:** `informe-diseno-modelo`
**Apps afectadas:** `server/src/Factum.Backend` (plantilla del informe, `ReportService`,
config `Branding`) y `ops/plantilla/` (script que construye la plantilla). `client/` y
`agent-ui/` **no cambian** salvo lo que se decida en **D4** (variante del logo para la web).
`server/src/Factum.Agent` (Tatana) no cambia.
**Pedido del usuario (2026-10-01), perito que usa Factum en un estudio jurídico:** "te dejé
un modelo de informe que quiero que usemos, en `docs/informe_modelo.pdf`; también te dejé un
logo del estudio en la misma carpeta".

**Como** perito informático de parte que trabaja para un estudio jurídico
**quiero** que el informe pericial que genera Factum tenga la estética del modelo que elegí
(portada, bandas de color, bloques en dos columnas, tipografía sans) con los colores y el
logo del estudio
**para que** el documento que presento tenga una imagen profesional y reconocible del
estudio, sin tener que maquetarlo a mano en Word después de cada generación.

---

## Contexto

### Los dos archivos de referencia (no versionados)

- **`docs/informe_modelo.pdf`** (5 páginas, A4): plantilla genérica de "Informe financiero"
  de una empresa ficticia. **El contenido no aplica**; lo que se toma es el **estilo**:
  - **Portada:** título muy grande en dos líneas (sans geométrica, color primario),
    subtítulo debajo, logo + nombre de la organización abajo a la izquierda, y a la derecha
    una **franja vertical de color** a sangre (≈ 20 % del ancho) que termina abajo en formas
    curvas decorativas de un segundo color.
  - **Páginas interiores:** una **banda superior** de color primario a sangre (≈ 20 % del alto)
    con el título de la sección en mayúsculas y blanco, y a la derecha un bloque del color de
    acento con las mismas formas curvas.
  - **Layout de dos columnas:** título del bloque a la izquierda (mayúsculas, color primario)
    y contenido a la derecha.
  - **Tablas etiqueta/valor** sin bordes verticales, separadas por líneas horizontales de
    color primario; totales destacados en color.
  - **Tipografía** sans geométrica en títulos y cuerpo, con interletrado amplio.
  - **Isotipo** de la organización solo, al pie de la última página.
  - **Paleta del modelo:** azul (≈ `#1A91F0`) + verde lima (≈ `#B5EB3A`).
- **`docs/Logo_del_estudio`** (sin extensión; JPEG 1408×768): logo del estudio. Monograma
  dorado con balanza sobre **fondo azul marino con textura y viñeteado**, marco de columnas
  dorado, y **con los datos del estudio impresos dentro de la imagen** (nombres de los
  profesionales, domicilio y teléfonos). **Paleta:** azul marino + dorado. Los valores exactos se muestrean de la imagen y viven solo en la config local.

**Los dos archivos tienen datos del cliente.** No se copian a `Templates/` ni a ningún
archivo versionado, y esta HU no transcribe sus datos. La identidad del estudio es
**configuración local** (`Branding:*` en `appsettings.Local.json`). Hoy están excluidos de git
**solo por `.git/info/exclude`** (`docs/informe_modelo.pdf`, `docs/Logo_del_estudio*`), que es
local a este clon: en otro clon no estarían ignorados. `.gitignore` versionado solo tiene
`docs/INFORME*` (ver **D11**).

### Qué existe hoy (arqueología del 2026-10-01)

**Generación** (`server/src/Factum.Backend/Services/Reports/ReportService.cs`):
- El informe es un **DOCX** que sale de `Templates/plantilla_informe_v4.docx`. La plantilla
  **no se edita a mano**: la construye `ops/plantilla/build_plantilla_v4.py` a partir de la
  plantilla del usuario (`docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx`, no
  versionada), editando el XML **por `w14:paraId`** con huellas no sensibles de cada ancla
  (ver `ops/plantilla/README.md` y Anexo A de `Refactorizaciones/informe-pericial-de-parte.md`).
- `GenerateDocxAsync` hace pasadas B-R0…B-R9: placeholders desconocidos fuera, bloques
  condicionales (`{#MEMBRETE}`, `{#organismoTribunal}`, `{#objetoInforme}`,
  `{#capturasNombreDispositivo}`, `{#descripcionNotasTecnicas}`, `{#descripcionReserva}`,
  `{#anexoCapturas}`), logo (`{LOGO_ORGANIZACION:5x1.5}`), multilínea (`{CONTACTO}` y textos
  del perito), tabla de hashes repetible, texto, imágenes, pie de atribución y control de
  restos.
- **Estructura actual del documento** (formato de escrito judicial, sin portada):
  título "INFORME PERICIAL…", destinatario (`{nombreTribunal}`, organismo), "S____", 
  presentación del perito ("…me presento y respetuosamente digo:"), párrafo de la causa,
  datos de la actuación (Carátula, Partes, Objeto, Fecha de intervención), manifestación de
  objetividad, objeto del informe, identificación (perito, proponente, ámbito), elementos
  ofrecidos (fecha/hora, tipo, marca y modelo, IMEI, línea, titular, capturas de
  identificación), operaciones realizadas, aseguramiento de la evidencia, tabla
  `NOMBRE | HASH SHA-256`, resultados, valoración técnica, conclusiones, notas técnicas,
  reserva, constancia final, **firma del perito** y **anexo de capturas** (salto de página).
- **Tipografía:** Times New Roman en todo (`BodyFont` en `ReportService`, `TNR` en el
  script); hashes en Courier New 8 pt; pies de foto en gris `595959`.
- **Membrete** (`word/header1.xml`, en **todas las páginas**, dentro de `{#MEMBRETE}` que se
  borra si no hay Branding): tabla de dos celdas con el logo a la izquierda
  (`{LOGO_ORGANIZACION:5x1.5}`, 5 × 1,5 cm, ajustado sin recortar) y a la derecha
  `{ORGANIZACION}` en negrita y `{CONTACTO}` en gris, con una línea inferior gris `7F7F7F`.
- **"Realizado con Factum":** lo inyecta el código (B-R8, `AddFactumAttributionFooter`) en el
  pie de **todas** las páginas (incluidas primera/pares si la plantilla las define), con el
  sello `Templates/factum-sello.png`, Arial 8 pt gris `5C656E`. Es obligatorio por la HU
  `marca-comercial-sin-mpf-gfd` ("siempre, aunque la organización esté configurada") y
  ninguna plantilla lo puede sacar.
- **Branding** (`Services/Branding/`): `OrganizationName`, `OrganizationLogo` (ruta a PNG o
  JPEG, ≤ 1 MB, lados 16–4096 px; si no valida se ignora con warning) y `ContactLines`. Se
  lee una vez al arrancar. El mismo logo se expone a la web
  (`GET /api/config/public` → `organization_logo_url`) y `client/` lo muestra en
  `LoginHero`, `UserMenu`, `SiteFooter`, `GenerateStep` y el showcase del design system.
  **Hoy no hay ninguna config de colores ni de tipografía.**
- **Config `Report`** (`ReportOptions`/`ReportSettings`): zona horaria, domicilio
  constituido, `EncryptZip` y textos por defecto (`ReportDefaultTexts`). Sin nada visual.
- **PDF:** `ConvertDocxToPdfAsync` (LibreOffice headless) **existe pero no se usa**: el
  informe se entrega en DOCX desde `informe-pericial-de-parte` (T16 de esa SDD; sacarlo,
  junto con `libreoffice-writer` del `Dockerfile`, quedó para otra HU). `ReportResult`
  conserva los nombres `PdfPath`/`PdfFilename`, que apuntan al DOCX.
- **Hashes** (`zip-cifrado-real`): el DOCX **no va dentro del ZIP**. El informe lleva el
  SHA-256 del ZIP de evidencia (cifrado) y `ReportHash` es el SHA-256 del DOCX ya cerrado,
  que se muestra en `ResultStep`/`CaseCard`. Un caso `Completed` no se regenera (409).
- **Trabajo en curso que toca lo mismo:** `formulario-caso-catalogos` está modificando
  `ReportValues`/`ReportService` (integrantes como lista, `{fraseIntegracion}`). Ese código no
  es estable al momento de este afinado.

### Qué es lo nuevo

1. Un **diseño visual** del informe inspirado en el modelo: portada, banda de color en las
   páginas interiores, bloques de datos en dos columnas, tablas etiqueta/valor con líneas de
   color, títulos de sección en mayúsculas y color, tipografía sans, isotipo al cierre.
2. **Colores configurables** por estudio (primario y acento), con default neutro versionado.
3. **Variantes del logo** (completo para la portada, isotipo para interiores/cierre), según
   **D4**.
4. **Nada cambia en el contenido pericial**: mismos datos, mismos textos legales, mismos
   placeholders, misma tabla de hashes, mismo flujo de generación y de hashes.

---

## Criterios de aceptación

```gherkin
Feature: Diseño visual del informe pericial según el modelo del estudio

  Background:
    Given un estudio con Branding configurado en la config local (nombre, logo, colores)
    And un caso con todos los datos obligatorios para generar

  # ── Portada ────────────────────────────────────────────────────────
  Scenario: El informe abre con una portada
    When el perito genera el informe
    Then la primera página es una portada (según D1)
    And muestra "Informe Pericial Técnico Informático" en tipografía grande y color primario
    And debajo los datos de identificación que defina D9 (p. ej. carátula, causa N°, fecha)
    And el logo del estudio y el nombre de la organización abajo a la izquierda
    And una franja vertical del color primario a la derecha, a sangre, con el remate decorativo que defina D6
    And la portada no tiene la banda interior ni el membrete de las páginas siguientes

  Scenario: Portada sin Branding configurado
    Given una instalación sin OrganizationName, sin logo y sin colores configurados
    When se genera el informe
    Then la portada sale igual, con la paleta neutra por defecto, sin logo y sin nombre de organización
    And no quedan huecos visibles, placeholders ni imágenes rotas

  # ── Páginas interiores ─────────────────────────────────────────────
  Scenario: Banda superior en las páginas interiores
    When se genera el informe
    Then cada página posterior a la portada tiene una banda superior del color primario (según D2)
    And la banda muestra el título en mayúsculas y blanco, y el isotipo o el nombre del estudio (según D4)
    And el texto del cuerpo nunca queda tapado por la banda

  Scenario: Bloques de datos en dos columnas
    When se genera el informe
    Then los bloques de datos (referencia de la actuación, identificación, elementos ofrecidos) se ven en dos columnas: título del bloque a la izquierda, filas etiqueta/valor a la derecha
    And las filas están separadas por líneas horizontales del color primario, sin bordes verticales
    And los textos largos del perito (operaciones, aseguramiento, resultados, valoración, conclusiones, notas, reserva) siguen a todo el ancho y justificados

  Scenario: Títulos de sección
    When se genera el informe
    Then los títulos de sección están en mayúsculas, en el color primario y en la tipografía de títulos (según D5)
    And el texto de cada título es el mismo que hoy

  Scenario: Tabla de hashes con el estilo nuevo
    Given un caso con N archivos de evidencia
    When se genera el informe
    Then la tabla "NOMBRE | HASH SHA-256" tiene el encabezado en el color primario y líneas horizontales de color entre filas
    And sigue teniendo N filas más la fila del contenedor ZIP, con los mismos hashes que hoy
    And el hash sigue en fuente monoespaciada, sin partirse de forma ilegible
    And el encabezado se repite si la tabla pasa de página

  Scenario: Cierre con isotipo
    When se genera el informe
    Then después de la firma del perito aparece el isotipo del estudio (según D4)
    And si no hay isotipo configurado, ese lugar queda vacío sin dejar un hueco notable

  # ── Contenido y reglas que no cambian ──────────────────────────────
  Scenario: El contenido pericial es el mismo
    Given el mismo caso generado con el diseño anterior y con el nuevo
    Then el texto del informe (datos, frases fijas, textos del perito, tabla de hashes, firma, anexo) es el mismo palabra por palabra, salvo lo que D9 agregue a la portada
    And no queda ningún placeholder "{…}" sin reemplazar

  Scenario: "Realizado con Factum" sigue presente
    When se genera el informe
    Then todas las páginas, incluida la portada, tienen en el pie la leyenda "Realizado con Factum" con el sello

  Scenario: Sigue siendo un DOCX editable
    When el perito descarga el informe
    Then es un .docx que abre sin errores ni avisos de reparación en Microsoft Word (Windows y macOS), LibreOffice Writer y WPS
    And el perito puede editar cualquier texto del cuerpo sin desarmar la banda, la portada ni las tablas
    And la estructura visual se ve igual en los tres programas, salvo diferencias menores de tipografía que acepte D5

  Scenario: Impresión en blanco y negro
    When el informe se imprime en escala de grises
    Then todos los textos siguen siendo legibles (los títulos en color tienen contraste suficiente)
    And ningún dato depende solo del color para entenderse

  # ── Configuración ──────────────────────────────────────────────────
  Scenario: Colores configurables
    Given Branding:PrimaryColor y Branding:AccentColor configurados en la config local con valores hex válidos
    When se genera el informe
    Then la portada, la banda, los títulos y las líneas usan esos colores

  Scenario: Color inválido
    Given Branding:PrimaryColor con un valor que no es un hex válido
    When arranca el backend
    Then se registra un warning en el log de inicio
    And los informes se generan con el color por defecto, sin fallar

  Scenario: Datos del estudio fuera del repo
    Then ni la plantilla versionada, ni el script, ni los defaults de código contienen el logo del estudio, sus nombres, su domicilio, sus teléfonos ni sus colores como default
    And informe_modelo.pdf y el logo del estudio no se versionan (según D11)

  # ── Compatibilidad ─────────────────────────────────────────────────
  Scenario: Informes ya generados no cambian
    Given casos generados antes de esta HU, con su ZIP y su DOCX en Storage:DataDirectory
    When se despliega esta HU
    Then sus archivos, sus documentos en Mongo y sus hashes (zip_hash, report_hash) quedan byte a byte iguales
    And se siguen descargando como antes

  Scenario: Casos en borrador al desplegar
    Given un caso creado antes de esta HU que todavía no se generó
    When el perito lo genera
    Then sale con el diseño nuevo

  Scenario: El hash del informe se calcula sobre el DOCX final
    When se genera el informe
    Then report_hash es el SHA-256 del DOCX con el diseño nuevo, tal como se descarga
    And el ZIP de evidencia y su hash no cambian por esta HU

  # ── Regresión ──────────────────────────────────────────────────────
  Scenario: El resto no cambia
    When el perito hace una inspección completa y genera el informe
    Then el wizard, la captura, la descarga, el cifrado del ZIP y los hashes funcionan igual que antes
    And el script de la plantilla con --check da OK
    And "dotnet build" del Backend termina sin errores
```

---

## Datos que se registran

No hay datos nuevos por caso ni en Mongo. Solo **configuración local del estudio**
(`appsettings.Local.json` o variables de entorno `Branding__*`, no versionada). Los nombres
son tentativos; los fija la SDD.

| Dato | Obligatorio | Uso |
|---|---|---|
| `Branding:PrimaryColor` (hex `#RRGGBB`) | No (default neutro versionado, D3) | Portada (título, franja), banda interior, títulos de sección, líneas de tablas. Para este estudio: azul marino del logo. |
| `Branding:AccentColor` (hex `#RRGGBB`) | No (default neutro versionado, D3) | Remate decorativo de la portada/banda (D6) y detalles. Nunca para texto sobre blanco si no tiene contraste (ver UX). Para este estudio: dorado del logo. |
| `Branding:OrganizationLogo` (ya existe) | No | Logo completo: portada (D4). Hoy también membrete y web. |
| `Branding:OrganizationIsotype` (nuevo, ruta a PNG/JPEG) | No | Isotipo chico: banda interior y cierre (D4). Si falta, se usa el nombre de la organización en texto o nada. |
| `Branding:OrganizationName`, `ContactLines` (ya existen) | No | Portada y/o banda (D8, D9). |

Las mismas validaciones que el logo actual (tamaño, formato, dimensiones; warning al arrancar
y se ignora si no valida).

---

## Diseño UX/UI (documento DOCX generado por `server/`; sin cambios en `client/` ni `agent-ui/`, salvo D4)

### Principios

- **Es un documento judicial.** El diseño viste la estructura pericial; no la reemplaza
  (D1). La decoración se concentra en la portada (D6); el interior es sobrio.
- **Editable y portable:** todo lo visual se arma con construcciones que Word, LibreOffice y
  WPS respetan (tablas con sombreado, encabezados de página, imágenes ancladas simples). Las
  formas libres flotantes que se desplazan al editar se evitan o se limitan a la portada.
- **Contraste:** el texto sobre blanco va en el color primario (marino) o en gris oscuro. El
  acento (dorado) se usa en líneas, bloques y decoración, **no** en texto chico sobre blanco
  (el dorado sobre blanco no llega a 4.5:1). Texto sobre la banda: blanco.

### Maqueta textual — portada (A4, una página)

```
┌──────────────────────────────────────────────┬──────────┐
│                                              │▓▓▓▓▓▓▓▓▓▓│  ← franja vertical a sangre,
│                                              │▓▓▓▓▓▓▓▓▓▓│    color primario (≈ 20 % ancho)
│                                              │▓▓▓▓▓▓▓▓▓▓│
│  Informe                                     │▓▓▓▓▓▓▓▓▓▓│
│  Pericial Técnico                            │▓▓▓▓▓▓▓▓▓▓│  ← título 40-48 pt, sans,
│  Informático                                 │▓▓▓▓▓▓▓▓▓▓│    color primario
│                                              │▓▓▓▓▓▓▓▓▓▓│
│  {tipoCausa} N° {numeroCausa}                │▓▓▓▓▓▓▓▓▓▓│  ← subtítulo 16-18 pt
│  "{caratula}"                                │▓▓▓▓▓▓▓▓▓▓│    (datos según D9)
│                                              │▓▓▓▓▓▓▓▓▓▓│
│  {nombrePerito} · M.P. {matriculaPerito}     │▓▓▓▓▓▓▓▓▓▓│  ← 11 pt, gris oscuro
│  {fechaInspeccion}                           │▓▓▓▓▓▓▓▓▓▓│
│                                              │░░▒▒░░▒▒░░│  ← remate decorativo en el
│  ┌────────────┐                              │▒▒░░▒▒░░▒▒│    color de acento (D6)
│  │   LOGO     │  {ORGANIZACION}              │░░▒▒░░▒▒░░│
│  └────────────┘  {CONTACTO} (según D8)       │▒▒░░▒▒░░▒▒│
│  ─────────────── Realizado con Factum ───────┴──────────│  ← pie obligatorio
└─────────────────────────────────────────────────────────┘
```

- El logo completo del estudio tiene **fondo marino propio**. Sobre fondo blanco se ve como
  un rectángulo oscuro: o se coloca en un bloque del mismo marino (y la SDD muestrea el
  color para que el borde no se note) o se usa la variante que resulte de **D4**.

### Maqueta textual — página interior

```
┌───────────────────────────────────────────┬─────────────┐
│▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓│░░ isotipo ░░│  ← banda a sangre (≈ 2,5-3 cm),
│▓  INFORME PERICIAL TÉCNICO INFORMÁTICO    ▓│░░  (D4)   ░░│    primario + bloque de acento
│▓  {tipoCausa} N° {numeroCausa}            ▓│░░░░░░░░░░░░░│    (título fijo, según D2)
├───────────────────────────────────────────┴─────────────┤
│                                                         │
│  REFERENCIA DE         ─────────────────────────────────│  ← línea color primario
│  LA ACTUACIÓN          Carátula        {caratula}       │
│  (mayúsc., primario)   ─────────────────────────────────│
│                        Parte denunc.   {parteDenunciante}
│                        ─────────────────────────────────│
│                        …                                │
│                                                         │
│  OPERACIONES REALIZADAS (título, mayúsc., primario)     │
│  ─────────────────────────────────────────────────────  │
│  Texto del perito a todo el ancho, justificado…         │
│                                                         │
│  ┌ NOMBRE ─────────────┬ HASH SHA-256 ────────────────┐ │  ← encabezado sombreado
│  │ archivo.png         │ a1b2… (monoespaciada)        │ │    en primario, texto blanco
│  ├─────────────────────┼──────────────────────────────┤ │  ← líneas horizontales
│  …                                                      │
│  ──────────────── Realizado con Factum · pág. N/M ───── │  ← pie (numeración según D8)
└─────────────────────────────────────────────────────────┘
```

- **Dos columnas** solo en los bloques de datos cortos (referencia de la actuación,
  identificación, elementos ofrecidos). El encabezado de escrito (destinatario, "S____",
  presentación) y los textos largos van a todo el ancho, como hoy.
- **Capturas de identificación y anexo:** igual que hoy (centradas, con pie de foto), con el
  pie de foto en la tipografía nueva.
- **Firma:** igual que hoy (línea, nombre en negrita, profesión y matrícula, carácter),
  centrada. Debajo, con aire, el isotipo (cierre, como el modelo).

### Estados y errores

| Situación | Qué se ve |
|---|---|
| Sin Branding | Portada y banda con la paleta neutra; sin logo, sin isotipo, sin nombre; nada roto. |
| Logo o isotipo inválido (formato, peso, tamaño) | Se ignora con warning al arrancar; el informe sale como si no estuviera. |
| Color inválido | Warning al arrancar; se usa el default. |
| Fuente no instalada en la PC del lector | Según D5: fuente embebida, o fallback declarado (Arial) sin romper el layout. |
| Texto largo en la carátula de la portada | Se ajusta en varias líneas sin invadir la franja ni empujar el logo a otra página (límite de líneas y tamaño menor, a definir en la SDD). |

---

## Fuera de alcance

- Cambiar el **contenido** del informe: secciones, frases fijas, textos por defecto,
  placeholders, datos del caso, tabla de hashes, firma o anexo (salvo lo que D9 agregue a la
  portada).
- Gráficos de datos (barras, tortas) como los del modelo: el informe pericial no tiene datos
  cuantitativos que los justifiquen.
- Entregar el informe en **PDF**, firmarlo digitalmente o sacar el código de PDF sin uso
  (`ConvertDocxToPdfAsync`, `libreoffice-writer` del `Dockerfile`): otra HU (D7).
- Regenerar, migrar o re-estilar informes ya generados.
- Editor de diseño desde la UI, varias plantillas o diseños elegibles por caso (D10).
- Recortar, vectorizar o retocar el logo del estudio por código (D4).
- Cambios en `agent-ui/`, en Tatana y en el wizard de `client/`.
- Rediseño de la web con la paleta del estudio.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- **Secuencia:** esta HU toca `ReportService`, `ReportValues` y el script de la plantilla,
  que `formulario-caso-catalogos` está modificando ahora. Arquitectura e implementación,
  **después** de que esa HU se apruebe, sobre su código final.
- **Dónde vive el diseño:** la plantilla se deriva por script (`build_plantilla_v4.py`,
  anclado por `w14:paraId` a la plantilla del usuario). El estilo nuevo (portada, banda,
  estilos de título, tablas en dos columnas) se aplica en ese script o en una versión nueva
  (`v5`); los colores y logos configurables los resuelve `ReportService` en la generación
  (sustitución de color y de imágenes en header/portada). Lo decide el `architect`.
- **Colores configurables en el DOCX:** la plantilla versionada lleva colores "marcadores"
  (o un tema de documento) que el código reemplaza; las formas decorativas, si son imágenes,
  no se pueden recolorear sin una biblioteca de imágenes: preferir DrawingML (formas
  vectoriales con color sustituible) o bloques de tabla sombreados.
- **Membrete actual** (`header1.xml`, `{#MEMBRETE}`): lo reemplaza la banda (D8). La portada
  necesita encabezado/pie de primera página distinto (`titlePg`); B-R8 ya agrega la
  atribución también al pie de primera página si existe.
- **Logo con fondo:** `BrandingLogo` hoy se ajusta "sin recortar" a una caja; en la portada
  hace falta una caja más grande y, si se pone sobre bloque de color, que el color del bloque
  coincida con el fondo del JPEG.
- **No versionar** nada derivado de `informe_modelo.pdf` ni del logo (ni recortes, ni
  colores como default de código). Los colores del estudio van solo en la config local.
- Verificación sugerida: render con `soffice --headless --convert-to pdf` a una carpeta
  temporal y revisión visual en Word/WPS (como ya indica `ops/plantilla/README.md`).
- Recordatorios del arnés: regla dura de datos (no tocar casos ni archivos existentes en
  `Storage:DataDirectory`); si D4 toca la web, aplica el reparto backend/frontend y las
  skills de frontend obligatorias.

---

## Dudas para validar con el usuario

### D1. Alcance de "usar el modelo": ¿solo estilo o también estructura?
- **A) Solo estilo** sobre la estructura actual: el documento sigue arrancando con el escrito
  (destinatario, "S____", presentación), con tipografía, colores, títulos, tablas y banda
  nuevos. Sin portada.
- **B) Estilo + portada:** se agrega una **portada** separada (como la del modelo) y, desde la
  página 2, el escrito actual con el estilo nuevo. El texto pericial no cambia.
- **C) Reestructurar** el informe al estilo del modelo (una sección por página con su banda,
  reordenar bloques, sacar el formato de escrito).
- **Recomendada: B.** La portada es lo más reconocible del modelo y no toca el texto legal.
  C cambia la forma de un escrito judicial que hoy cumple su función (el encabezado
  "S____ / me presento y respetuosamente digo" es lo que el tribunal espera ver) y llena el
  informe de páginas a medio usar. **Pregunta asociada:** ¿el tribunal o colegio ante el que
  presentás tiene reglas de formato (tipografía, tamaño, márgenes, interlineado, sin
  portada)? Si las tiene, mandan sobre el modelo.

### D2. Banda de color en las páginas interiores
- **A) Banda corrida en el encabezado** de todas las páginas interiores (≈ 2,5-3 cm), con un
  **título fijo** ("INFORME PERICIAL TÉCNICO INFORMÁTICO" y la causa), y los títulos de
  sección dentro del cuerpo en mayúsculas y color primario.
- **B) Una banda grande por sección** (como el modelo, ≈ 5-6 cm), con el título de la sección
  adentro y **salto de página** antes de cada sección.
- **C) Sin banda:** solo títulos en color y líneas.
- **Recomendada: A.** Mantiene el aire del modelo sin forzar ~10 saltos de página (las
  secciones del informe son de largo muy variable: "Ámbito" es una línea, "Resultados" puede
  ser varias páginas). B además obliga a cortes de sección en Word, que se rompen fácil al
  editar el documento.

### D3. Paleta de colores
- **A) Colores del estudio** (marino + dorado, muestreados del logo), fijos.
- **B) Colores del modelo** (azul + lima).
- **C) Configurables por Branding** (`PrimaryColor`, `AccentColor`), con un **default neutro
  versionado** (p. ej. gris pizarra + gris claro) y, en la config local de este estudio,
  marino + dorado.
- **D) Configurables, con default = colores del estudio** en el código.
- **Recomendada: C.** La identidad del estudio es configuración (regla de
  `marca-comercial-sin-mpf-gfd`): si los colores del estudio fueran el default del código,
  el próximo cliente recibiría informes con la paleta de este. El azul + lima del modelo es
  de una marca ficticia y no combina con un logo marino y dorado. **Pregunta asociada:** ¿te
  sirve que el valor exacto lo saquemos del logo y te lo mostremos en un render de prueba
  para que lo apruebes?

### D4. El logo: variantes, dónde va y qué pasa si falta
El logo que pasaste es un JPEG apaisado con **fondo marino texturado** y con **nombres,
domicilio y teléfonos impresos en la imagen**. En el encabezado actual (5 × 1,5 cm) esos datos
quedan ilegibles, y sobre fondo blanco se ve como un rectángulo oscuro. Además, el mismo logo
se muestra en la web (login, menú de usuario, pie).
- **A) Tal cual en todos lados** (portada, banda, cierre, web).
- **B) Dos variantes configurables:** el logo completo (el que pasaste) **solo en la
  portada**, dentro de un bloque marino; y un **isotipo** (solo el monograma con la balanza,
  PNG con fondo transparente) para la banda interior, el cierre y la web. Si no hay isotipo,
  la banda muestra el nombre del estudio en texto y el cierre queda sin imagen.
- **C) Recortar el monograma** del JPEG por código.
- **Recomendada: B.** Es como trabaja el modelo (logo completo en portada, isotipo solo al
  cierre) y evita repetir datos de contacto en cada página. C da un recorte con el fondo
  texturado pegado y es frágil. **Necesito que me pases el isotipo** (monograma solo, PNG
  transparente, idealmente ≥ 512 px). Si no lo tenés, ¿lo pedís a quien hizo el logo, o
  arrancamos solo con la portada y nombre en texto en la banda? **Pregunta asociada:** ¿la
  web también pasa a usar el isotipo (toca `client/`) o queda como está?

### D5. Tipografía
El modelo usa una sans geométrica; hoy el informe es todo Times New Roman.
- **A) Fuente estándar de Office:** Century Gothic (geométrica) para títulos y Arial para el
  cuerpo. Sin archivos extra; en LibreOffice/Linux Century Gothic se reemplaza por otra
  fuente y cambian algunas medidas.
- **B) Fuente libre embebida en el DOCX** (licencia OFL, p. ej. Montserrat o Lexend) para
  títulos y cuerpo: se ve igual en cualquier PC, pero el archivo pesa más y algunas versiones
  de Word piden confirmación o la descartan al guardar.
- **C) Sans en títulos y Times New Roman en el cuerpo.**
- **Recomendada: A**, con Arial 11-12 en el cuerpo y Courier New para los hashes como hoy.
  Arial es universal y aceptada en presentaciones judiciales; Century Gothic da el aire del
  modelo en títulos y portada; y si falta, el layout no se rompe. B queda como mejora si el
  render con fallback no te convence.

### D6. Elementos decorativos y sobriedad
- **A) Decoración solo en la portada** (remate en el color de acento al pie de la franja,
  con formas **geométricas sobrias**: arcos o bandas paralelas, no las curvas del modelo, que
  son de otra marca) y, en la banda interior, solo un bloque liso de acento.
- **B) Como el modelo:** formas curvas en la portada y en cada banda.
- **C) Sin decoración:** solo color plano.
- **Recomendada: A.** Un informe pericial se lee en un expediente: la portada puede tener
  impronta de marca, pero en el cuerpo la decoración repetida distrae y gasta tinta al
  imprimir. Copiar las formas del modelo, además, toma un rasgo de una marca ajena.

### D7. Formato de salida
- **A) Sigue siendo DOCX editable** (como desde `informe-pericial-de-parte`), con el diseño
  nuevo.
- **B) PDF** generado en el servidor (LibreOffice, código existente sin uso).
- **C) Los dos.**
- **Recomendada: A.** Hoy el perito revisa y ajusta el informe en Word antes de presentarlo;
  un PDF lo impide. B y C sumarían un segundo archivo con su propio hash (el que va a
  `report_hash` y se muestra en pantalla cambiaría de significado) y dependerían de que
  LibreOffice renderice el diseño igual que Word. Si más adelante se quiere PDF, conviene una
  HU propia que decida también qué hash se informa.

### D8. Membrete, contacto, "Realizado con Factum" y numeración
- **Membrete actual** (logo + nombre + contacto en todas las páginas): **recomendada:** lo
  reemplaza la banda de D2; el **contacto del estudio va solo en la portada**, debajo del
  nombre. Los datos de contacto ya están dentro del logo completo; si el logo los muestra
  legibles en la portada, ¿querés igual las `ContactLines` en texto? Recomendado: sí, porque
  el texto se puede copiar y buscar.
- **"Realizado con Factum":** sigue en el pie de **todas** las páginas, incluida la portada
  (regla vigente). **Recomendada:** conservarlo así, discreto (8 pt, gris), sin competir con
  el diseño. Si querés que no figure en la portada, es una excepción a esa regla que tenés
  que confirmar.
- **Numeración de páginas:** hoy el informe no la tiene. **Recomendada:** agregar
  "Página N de M" en el pie de las páginas interiores (no en la portada): en un escrito
  judicial ayuda a verificar que no falten hojas. ¿Lo sumamos en esta HU?

### D9. Qué datos van en la portada
- **Recomendada:** título fijo "Informe Pericial Técnico Informático"; debajo
  `{tipoCausa} N° {numeroCausa}` y la carátula; después nombre del perito con matrícula y la
  fecha de la inspección; abajo logo, nombre y contacto del estudio. **No** van en la portada
  el titular del equipo, el IMEI ni las partes (datos personales que ya están en el cuerpo).
- **Alternativa:** solo el título y el logo, como el modelo (sin datos de la causa).
- ¿Agregarías o sacarías algo (p. ej. el tribunal destinatario)?

### D10. ¿El diseño actual convive como opción?
- **A) Reemplazo:** todos los informes nuevos salen con el diseño nuevo; con Branding vacío,
  con la paleta neutra.
- **B) Opción por config** (`Report:Design = clasico | modelo`).
- **Recomendada: A.** Mantener dos maquetas duplica el script de la plantilla y las pruebas
  visuales, y el diseño nuevo con paleta neutra ya sirve para una instalación sin marca.
  Los informes ya generados no cambian en ningún caso.

### D11. Archivos de referencia fuera de git
`docs/informe_modelo.pdf` y `docs/Logo_del_estudio` están ignorados solo en
`.git/info/exclude` de esta PC. En otro clon (o si se borra ese archivo) se podrían commitear
por error.
- **Recomendada:** agregar al `.gitignore` versionado `docs/informe_modelo*` y
  `docs/Logo_del_estudio*` (los nombres de archivo no tienen datos personales), y guardar la
  ruta real del logo/isotipo solo en `appsettings.Local.json`. ¿Te parece, o preferís sacar
  esos archivos de `docs/` y apuntar la config a una carpeta fuera del repo?

## Validación (2026-10-01)

El usuario validó las 11 dudas en la opción **recomendada**. Pendiente del usuario (no bloquea): el isotipo PNG transparente (mientras no esté, rige el fallback de D4: nombre del estudio en texto en la banda, cierre sin imagen) y las reglas de formato del tribunal (no informó ninguna; se asume que no hay).
