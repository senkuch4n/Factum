# HU: Informe pericial v6, diseño moderno y empresarial con la paleta de Factum

**Slug:** `informe-diseno-v6`
**Apps afectadas:** `server/src/Factum.Backend` (plantilla del informe, `ReportService`, defaults de
`Branding`) y `ops/plantilla/` (script que construye la plantilla). `client/`, `agent-ui/` y
`server/src/Factum.Agent` (Tatana) **no cambian**.
**Pedido del usuario (2026-10-01), perito, tras abrir en Word el informe v5 de
`informe-diseno-modelo`:** "lo que no me gusta es el cuadro arriba a la derecha después de la
portada en el encabezado, ¿podés innovar en creación de un informe moderno y que respete la
paleta de colores de factum mejor? Quiero algo moderno pero también empresarial para el
informe, siempre respetando la estructura, viste: objeto, elementos ofrecidos, etc."

**Como** perito informático de parte que presenta informes generados con Factum
**quiero** que el informe tenga un diseño moderno y sobrio, de estilo consultora, con la paleta
de Factum
**para que** el documento se vea profesional y actual sin perder la estructura ni el texto
pericial que el tribunal espera leer.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-01)

**La v5** (`informe-diseno-modelo`, commit `c8b8fe9`, en la rama `feat/informe-diseno-modelo`)
es el diseño vigente:

- **Plantilla:** `server/src/Factum.Backend/Templates/plantilla_informe_v5.docx`, generada por
  `ops/plantilla/build_plantilla_v5.py` **a partir de la v4 versionada**. El script ancla por
  `w14:paraId` y tiene `--check`. La v4 y su script no se tocan y sirven de línea de base para
  los tests de texto.
- **Dos secciones** (portada con márgenes propios + interior), sin `titlePg`.
- **Portada:** título "Informe / Pericial Técnico / Informático" en tres líneas (Century
  Gothic 34 pt, color primario), `{tipoCausa} N° {numeroCausa}`, la carátula entre comillas,
  el perito con su matrícula y la fecha. A la derecha, una **franja vertical primaria a sangre**
  (20 % del ancho) con **tres bandas de acento** abajo. En el pie (`footer2`): logo del estudio
  como "tarjeta" de 7 × 3.8 cm, nombre y contacto (`{#MEMBRETE}`).
- **Encabezado interior (`header1`):** **banda primaria a sangre de 2.5 cm** con
  "INFORME PERICIAL TÉCNICO INFORMÁTICO" y la causa en blanco, más un **bloque de acento en el
  margen derecho** con el isotipo o el nombre del estudio. **Ese bloque es "el cuadro" que no
  le gusta al usuario.**
- **Títulos de sección:** Century Gothic 11 pt negrita, mayúsculas, color primario, línea
  inferior primaria, numeración romana del mismo color.
- **Bloques de datos** (Referencia de la actuación, Identificación, Elementos ofrecidos) en
  tablas de dos columnas: título a la izquierda, etiqueta/valor a la derecha, con líneas
  horizontales primarias.
- **Tabla de hashes:** encabezado sombreado en primario con texto blanco, líneas horizontales,
  hash en Courier New 8 pt.
- **Cuerpo:** Arial 11 pt, interlineado 1.5, justificado.
- **Pie interior:** "Página N de M" (la portada cuenta como página 1) y "Realizado con Factum"
  con el sello, que agrega el código (B-R8) en **todas** las páginas.
- **Colores:** centinelas `2F3B4C` (primario) y `9AA5B1` (acento) en la plantilla, que la
  pasada B-R2b (`Services/Reports/BrandColors.cs`) reemplaza por `Branding:PrimaryColor` y
  `Branding:AccentColor`. Defaults versionados neutros (gris pizarra + gris claro, en
  `BrandingColors`). El primario se rechaza si tiene contraste < 4.5:1 con blanco.
- **Config local de esta instalación** (`appsettings.Local.json`, no versionada): tiene
  `OrganizationName`, `ContactLines`, `OrganizationLogo` (JPEG con **fondo oscuro propio y
  viñeteado**) y **colores propios del estudio** en `PrimaryColor`/`AccentColor`. Por eso hoy
  la v5 sale con la paleta del estudio y **no** con la de Factum. Sin isotipo configurado.

**Lo que se ve en el render de la v5** (muestras en el scratchpad, no versionadas), además del
cuadro:

- El título del escrito aparece **tres veces seguidas** al pasar de la portada a la página 2:
  portada, banda y título subrayado del cuerpo.
- Los títulos numerados quedan con **sangrías distintas** (los de bloque de datos van a la
  izquierda dentro de la tabla; el resto hereda la sangría de 2552 dxa de la v4 y queda casi
  centrado).
- La banda y la franja son superficies grandes de color: mucha tinta al imprimir y un aire
  más "folleto" que "informe técnico".

### Paleta de Factum (sistema de diseño), traducida a impresión

Fuentes: `client/src/app/globals.css` (tokens `--fx-*`, tema claro L35-64),
`Refactorizaciones/rediseno-base-primereact.md` y el logo "Sello" de
`docs/hu-marca-comercial-sin-mpf-gfd.md` (cuadrado verde con una "F" y la **esquina superior
derecha cortada en diagonal**; wordmark "Factum" en Sora 700). La web usa Inter (sans) e IBM
Plex Mono; Sora solo vive convertida a trazos en los SVG del logo.

Un informe se imprime en papel blanco: se toman los valores del **tema claro**.

| Rol en el informe | Token de Factum | Hex | Contraste con blanco | Uso propuesto |
|---|---|---|---|---|
| Verde de marca | `--fx-accent` (claro) | `#2F6F12` | ≈ 6.2:1 | Filetes, números de sección, detalles. Apto como texto. |
| Verde como texto | `--fx-accent-text` (claro) | `#2D6A10` | ≈ 6.5:1 | Texto chico en verde, si hiciera falta. |
| Tinte verde | `--fx-accent-soft` (claro) | `#E8F3DF` | — (fondo) | Fondo suave de una fila destacada (contenedor ZIP). |
| Tinta | `--fx-text` (claro) | `#0E1013` | ≈ 19:1 | Títulos y título de portada. |
| Texto secundario | `--fx-text-2` (claro) | `#3D444C` | ≈ 10:1 | Cuerpo (o tinta, según la SDD), valores de fichas. |
| Texto terciario | `--fx-text-3` (claro) | `#5C656E` | ≈ 5.9:1 | Etiquetas, encabezado y pie (ya se usa en la atribución). |
| Filete gris | `--fx-border` (claro) | `#D9DDE1` | — (línea) | Líneas finas entre filas y separadores. |
| Superficie | `--fx-surface-2` (claro) | `#EEF0F2` | — (fondo) | Opcional, fondos muy suaves. |

El **verde lima** de la marca (`#7FD34E`, tema oscuro) tiene ≈ 1.9:1 con blanco: **no** se usa
en el informe (ni como texto ni como línea fina).

### Qué es lo nuevo

1. **Plantilla v6** con un lenguaje visual "consultora": mucho blanco, **un solo color de
   acento usado en filetes**, jerarquía tipográfica fuerte y **numeración de sección
   prominente**. Se van la banda interior, el bloque de acento ("el cuadro") y la franja de la
   portada.
2. **Paleta de Factum** como paleta del informe (cómo convive con el Branding del estudio:
   **D1**).
3. **Nada cambia en el contenido pericial**: mismas secciones, en el mismo orden, mismos
   textos legales, mismos placeholders, misma tabla de hashes, mismo flujo de generación y de
   hashes.

### Estructura pericial que se conserva (no cambia)

Relevada de `docs/hu-informe-pericial-de-parte.md`, `build_plantilla_v4.py`/`v5.py` (huellas
de `fingerprint`) y el render de la v5. Entre corchetes, lo condicional.

| # | Bloque | Numeración actual |
|---|---|---|
| 1 | Título del escrito: "INFORME PERICIAL TÉCNICO INFORMÁTICO" / "INSPECCIÓN TÉCNICA DE DISPOSITIVO MÓVIL" | — |
| 2 | Destinatario `{nombreTribunal}` [+ organismo] y "S____/____D" | — |
| 3 | Presentación del perito ("…me presento y respetuosamente digo:") | — |
| 4 | Párrafo de la causa ("Que vengo a emitir…") | — |
| 5 | **Referencia de la actuación**: Carátula, Parte denunciante, Parte denunciada, Objeto, Fecha de intervención | — |
| 6 | **Declaración de imparcialidad y rigor técnico** | — |
| 7 | **Objeto del informe** [`{#objetoInforme}`] | I |
| 8 | **Identificación**: Perito de parte, Parte que lo propone, Ámbito | II |
| 9 | **Elementos ofrecidos**: "En fecha…", Tipo, Marca y modelo, IMEI, Línea, Titular, "El mismo fue aportado…", [capturas de identificación] | III |
| 10 | **Operaciones realizadas** | IV |
| 11 | **Generación y aseguramiento de evidencia digital** + tabla `NOMBRE \| HASH SHA-256` (archivos + contenedor ZIP) | V |
| 12 | **Cadena de custodia digital** (Origen controlado, Integridad, Trazabilidad, Custodia) | VI |
| 13 | **Resultados** | VII |
| 14 | **Valoración técnica** | VIII |
| 15 | **Conclusiones** | IX |
| 16 | [**Notas técnicas**] | X |
| 17 | [**Reserva**] | XI |
| 18 | "SE DEJA CONSTANCIA que…" | — |
| 19 | Firma (línea, nombre, profesión y matrícula, carácter) | — |
| 20 | [Anexo – Capturas de pantalla] (salto de página) | — |

La numeración romana depende de qué secciones condicionales queden; la calcula Word con la
lista numerada, como hoy.

---

## Criterios de aceptación

```gherkin
Feature: Informe pericial v6, moderno y empresarial con la paleta de Factum

  Background:
    Given una instalación con la paleta del informe resuelta según D1
    And un caso con todos los datos obligatorios para generar

  # ── Interior ──────────────────────────────────────────────────────
  Scenario: Sin banda ni cuadro en el encabezado interior
    When el perito genera el informe
    Then ninguna página interior tiene una banda de color a sangre ni un bloque de color en el margen
    And el encabezado interior es el que defina D3 (recomendado: texto chico gris con la causa y una línea fina)
    And el texto del cuerpo nunca queda tapado por el encabezado

  Scenario: Títulos de sección con numeración prominente
    When se genera el informe
    Then cada sección numerada muestra su número (según D5) en el verde de la paleta, más grande que el título
    And el título de la sección va en mayúsculas, en tinta, con interletrado amplio
    And todos los títulos de sección arrancan alineados al margen izquierdo, con la misma sangría
    And las secciones sin número (Referencia de la actuación, Declaración de imparcialidad) usan el mismo estilo, sin número
    And el texto de cada título es el mismo que hoy

  Scenario: Bloques de datos como ficha
    When se genera el informe
    Then Referencia de la actuación, Identificación y Elementos ofrecidos muestran sus datos como filas etiqueta / valor (según D6)
    And la etiqueta va en gris, más chica, y el valor en tinta
    And las filas se separan con líneas finas grises, sin bordes verticales
    And "El mismo fue aportado…" y las capturas de identificación siguen fuera de la ficha, a todo el ancho

  Scenario: Tabla de hashes sobria
    Given un caso con N archivos de evidencia
    When se genera el informe
    Then la tabla "NOMBRE | HASH SHA-256" tiene el encabezado en gris, mayúsculas chicas, con una línea verde debajo y sin relleno de color
    And las filas se separan con líneas finas grises
    And la fila del contenedor ZIP se distingue con un fondo de tinte suave y su leyenda "Contenedor de la evidencia"
    And sigue teniendo N filas más la del contenedor, con los mismos hashes que hoy, en fuente monoespaciada
    And el encabezado se repite si la tabla pasa de página

  Scenario: Título del escrito sin duplicación visual
    When se genera el informe
    Then el título del escrito del cuerpo conserva su texto y su posición
    And se ve en tinta, sin subrayado, y no se repite en una banda justo encima

  Scenario: Firma y cierre
    When se genera el informe
    Then "SE DEJA CONSTANCIA…" y el bloque de firma conservan su texto y su orden
    And el cierre lleva el filete verde y, si hay isotipo configurado, el isotipo (según D2)
    And si no hay isotipo, no queda un hueco notable

  # ── Portada ───────────────────────────────────────────────────────
  Scenario: Portada sobria
    When se genera el informe
    Then la primera página es una portada sin franjas ni bandas de color a sangre (según D4 y D9)
    And muestra un filete verde corto, el título grande en tinta y la ficha de la causa (causa, carátula, perito, fecha)
    And la identidad del estudio (logo, nombre, contacto) va abajo, según D2
    And la portada no tiene el encabezado interior

  Scenario: Portada sin Branding del estudio
    Given una instalación sin OrganizationName, sin logo y sin isotipo
    When se genera el informe
    Then la portada sale igual con la paleta de Factum, sin logo ni nombre de organización
    And no quedan huecos visibles, placeholders ni imágenes rotas

  Scenario: Carátula larga en la portada
    Given una carátula de 300 caracteres
    When se genera el informe
    Then la carátula se parte en varias líneas dentro de la ficha
    And la identidad del estudio no pasa a otra página

  # ── Paleta ────────────────────────────────────────────────────────
  Scenario: Paleta de Factum por defecto
    Given una instalación sin Branding:PrimaryColor ni Branding:AccentColor
    When se genera el informe
    Then filetes, números de sección y líneas destacadas usan el verde de Factum (#2F6F12)
    And el tinte suave usa #E8F3DF
    And títulos y textos usan los grises/tinta de Factum

  Scenario: Colores del Branding (según D1)
    Given Branding:PrimaryColor configurado con un hex válido
    When se genera el informe
    Then el comportamiento es el que defina D1 (recomendado: el color configurado reemplaza al verde de Factum en los mismos lugares)

  # ── Lo que no cambia ──────────────────────────────────────────────
  Scenario: El contenido pericial es el mismo
    Given el mismo caso generado con la v5 y con la v6
    Then el texto del cuerpo (datos, frases fijas, textos del perito, tabla de hashes, firma, anexo) es el mismo palabra por palabra
    And las secciones aparecen en el mismo orden y con el mismo título
    And no queda ningún placeholder "{…}" sin reemplazar

  Scenario: "Realizado con Factum" y numeración
    When se genera el informe
    Then todas las páginas, incluida la portada, tienen "Realizado con Factum" con el sello en el pie
    And las páginas interiores muestran "Página N de M", contando la portada como hoy

  Scenario: Sigue siendo un DOCX editable
    When el perito descarga el informe
    Then es un .docx que abre sin avisos de reparación en Microsoft Word (Windows y macOS), LibreOffice Writer y WPS
    And el perito puede editar cualquier texto sin desarmar la portada, los títulos ni las tablas
    And no hay formas flotantes en las páginas interiores

  Scenario: Impresión en blanco y negro
    When el informe se imprime en escala de grises
    Then todos los textos siguen siendo legibles y ningún dato depende solo del color

  # ── Compatibilidad ────────────────────────────────────────────────
  Scenario: Informes ya generados no cambian
    Given casos generados con la v4 o la v5, con su ZIP y su DOCX en Storage:DataDirectory
    When se despliega esta HU
    Then sus archivos, sus documentos en Mongo y sus hashes (zip_hash, report_hash) quedan byte a byte iguales

  Scenario: Casos en borrador al desplegar
    Given un caso creado antes de esta HU que todavía no se generó
    When el perito lo genera
    Then sale con la v6

  Scenario: El hash del informe se calcula sobre el DOCX final
    When se genera el informe
    Then report_hash es el SHA-256 del DOCX v6 tal como se descarga
    And el ZIP de evidencia y su hash no cambian por esta HU

  # ── Regresión ─────────────────────────────────────────────────────
  Scenario: El resto no cambia
    When el perito hace una inspección completa y genera el informe
    Then el wizard, la captura, la descarga, el cifrado del ZIP y los hashes funcionan igual
    And el --check del script de la plantilla v6 da OK
    And "dotnet build" y "dotnet test" del Backend terminan sin errores
```

---

## Datos que se registran

No hay datos nuevos por caso ni cambios en Mongo. Cambia el significado de configuración que ya
existe:

| Dato | Obligatorio | Uso |
|---|---|---|
| `Branding:PrimaryColor` | No | Según D1. Recomendado: si está, reemplaza al verde de Factum en filetes, números y línea de la tabla; si no, verde de Factum (`#2F6F12`). Sigue el chequeo de contraste ≥ 4.5:1. |
| `Branding:AccentColor` | No | Según D1. Recomendado: tinte suave (fila del contenedor ZIP); default `#E8F3DF`. Deja de usarse en formas grandes porque ya no hay. |
| `Branding:OrganizationLogo`, `OrganizationIsotype`, `OrganizationName`, `ContactLines` (ya existen) | No | Identidad del estudio en portada, encabezado y cierre (D2). |
| Defaults de `BrandingColors` (versionados) | — | Pasan de gris pizarra / gris claro a la paleta de Factum (es la marca del producto, no datos de un cliente). |

---

## Diseño UX/UI (documento DOCX generado por `server/`; sin cambios en `client/` ni `agent-ui/`)

### Principios de la dirección propuesta: **"Filete"** (recomendada, D9)

- **Mucho blanco, un solo color.** El verde de Factum aparece en **líneas finas y números**,
  nunca en superficies grandes. Casi todo el documento es tinta y grises.
- **Jerarquía por tipografía,** no por cajas: título de portada grande, números de sección
  grandes, títulos en mayúsculas con interletrado, etiquetas chicas en gris.
- **Una grilla:** todo arranca del mismo margen izquierdo (se corrigen las sangrías desparejas
  de la v5).
- **Sin formas flotantes en el interior.** Todo es párrafo o tabla de flujo: lo que mejor
  respetan Word, LibreOffice y WPS, y lo que el perito puede editar sin mover nada.
- **Es un escrito judicial.** El diseño viste la estructura; no la reordena ni cambia texto.
- **Tinta:** el documento casi no gasta color al imprimir.

### Maqueta textual: portada (A4)

```
┌─────────────────────────────────────────────────────────┐
│                                                         │
│                                                         │
│   ━━━━━━  ← filete verde (#2F6F12), ~3 cm × 4-6 pt       │
│                                                         │
│   Informe pericial                                      │  ← 32-36 pt, tinta #0E1013,
│   técnico informático                                   │    Century Gothic (D7)
│                                                         │
│   Inspección técnica de dispositivo móvil               │  ← 13-14 pt, gris #3D444C (D4)
│                                                         │
│                                                         │
│   ───────────────────────────────────────────────────── │  ← línea fina gris #D9DDE1
│   CAUSA       {tipoCausa} N° {numeroCausa}              │  ← etiquetas 8 pt mayúsc.
│   ───────────────────────────────────────────────────── │    gris #5C656E, interletrado;
│   CARÁTULA    “{caratula}”                              │    valores 11 pt tinta
│   ───────────────────────────────────────────────────── │
│   PERITO      {nombrePerito} · M.P. {matriculaPerito}   │
│   ───────────────────────────────────────────────────── │
│   FECHA       {fechaInspeccion}                         │
│   ───────────────────────────────────────────────────── │
│                                                         │
│                                                         │
│   [logo]  {ORGANIZACION}                                │  ← identidad del estudio (D2)
│           {CONTACTO}                                    │
│                                                         │
│                ▣ Realizado con Factum                   │  ← pie obligatorio (B-R8)
└─────────────────────────────────────────────────────────┘
```

- El título pasa de tres líneas a **dos**, en caja baja/oración, lo que da un tono más
  editorial. La ficha de la causa reemplaza a las líneas sueltas de la v5 (mismos datos).
- **Sin franja ni bandas** (D4). Nada a sangre.
- La ficha es una **tabla** (etiqueta / valor) sin bordes verticales, con líneas finas grises.

### Maqueta textual: página interior

```
┌─────────────────────────────────────────────────────────┐
│   Informe pericial técnico informático · {tipoCausa}    │  ← encabezado (D3): 8 pt,
│   N° {numeroCausa}                      {ORGANIZACION}  │    gris #5C656E; nombre a la der.
│   ───────────────────────────────────────────────────── │  ← línea fina gris
│                                                         │
│   INFORME PERICIAL TÉCNICO INFORMÁTICO                  │  ← título del escrito: tinta,
│   INSPECCIÓN TÉCNICA DE DISPOSITIVO MÓVIL               │    sin subrayado, alineación de hoy
│                                                         │
│   {nombreTribunal}                                      │
│   S____________________/____________________D           │
│   …presentación y párrafo de la causa (como hoy)…       │
│                                                         │
│   REFERENCIA DE LA ACTUACIÓN                            │  ← título sin número
│   ━━━  ← filete verde corto                             │
│   CARÁTULA        {caratula}                            │  ← ficha (D6)
│   ───────────────────────────────────────────────────── │
│   PARTE DENUNC.   {parteDenunciante}                    │
│   ───────────────────────────────────────────────────── │
│   …                                                     │
│                                                         │
│   I                                                     │  ← número 20-24 pt, verde (D5)
│   OBJETO DEL INFORME                                    │  ← 11-12 pt negrita, tinta,
│   ━━━                                                   │    mayúsc., interletrado
│   Texto del perito a todo el ancho, justificado…        │
│                                                         │
│   V                                                     │
│   GENERACIÓN Y ASEGURAMIENTO DE EVIDENCIA DIGITAL       │
│   ━━━                                                   │
│   NOMBRE                    HASH SHA-256                │  ← encabezado 8 pt gris mayúsc.
│   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │  ← línea verde 1 pt
│   chat_export.txt           3f1b… (Courier New 8 pt)    │
│   ───────────────────────────────────────────────────── │  ← líneas finas grises
│   ░ evidencia_….zip         a865…                     ░ │  ← fila contenedor: tinte #E8F3DF
│   ░ Contenedor de la evidencia                        ░ │
│   ───────────────────────────────────────────────────── │
│                                                         │
│   ───────────────────────────────────────────────────── │  ← línea fina gris
│   ▣ Realizado con Factum                  Página N de M │  ← pie (D3)
└─────────────────────────────────────────────────────────┘
```

Variante de título "en línea" (a definir en la SDD si entra mejor en página): número y título
en la misma línea, `I` en verde a la izquierda con sangría francesa, título en tinta.

### Cierre (después de Conclusiones / Notas / Reserva)

```
│   SE DEJA CONSTANCIA que el presente informe…           │
│                                                         │
│                 ______________________________           │  ← firma: igual que hoy
│                 {nombrePerito} (negrita)                 │    (centrada, mismos textos)
│                 {profesión} – M.P. {matrícula}           │
│                 {caracterPerito}                         │
│                                                         │
│                         ━━━                              │  ← filete verde corto
│                      [isotipo]                           │  ← solo si hay isotipo (D2)
```

### Tipografía (según D7)

| Uso | Recomendado | Tamaño |
|---|---|---|
| Título de portada | Century Gothic (como la v5) | 32-36 pt |
| Números de sección | Century Gothic | 20-24 pt |
| Títulos de sección | Century Gothic negrita, mayúsculas, interletrado | 11-12 pt |
| Cuerpo | Arial (como la v5), interlineado 1.5 | 11 pt |
| Etiquetas de fichas y encabezado/pie | Arial | 8-9 pt |
| Hashes | Courier New (como hoy) | 8 pt |

### Estados y errores

| Situación | Qué se ve |
|---|---|
| Sin Branding del estudio | Paleta de Factum; portada sin logo ni nombre; encabezado solo con la causa; cierre solo con el filete. Nada roto. |
| Logo o isotipo inválido | Warning al arrancar (como hoy); el informe sale como si no estuviera. |
| Color inválido o con contraste < 4.5:1 | Warning al arrancar (como hoy); se usa el verde de Factum. |
| Sección condicional ausente (Objeto, Notas, Reserva) | Desaparece entera, con su número; la numeración se corre como hoy. |
| Sección sin texto (dos títulos seguidos) | Cada título conserva su filete (en la v5 se perdía una línea por el agrupamiento de bordes; con filete por párrafo propio o tabla no pasa). |
| Fuente no instalada | Sustitución por una sans (fontTable con panose/familia, como en la v5) sin romper la grilla. |

### Alternativas de dirección (D9)

- **B. "Margen editorial":** como "Filete", pero los números de sección van **colgados en el
  margen izquierdo** (una columna angosta de ~1.5 cm, números en verde) y el texto arranca a la
  derecha; en la portada, un **filete verde vertical** fino a la izquierda del título, de
  altura completa del bloque. Muy "consultora", pero achica el ancho útil del cuerpo
  (o exige tablas/sangrías por sección) y es más frágil al editar en Word.
- **C. "Bloque de marca":** interior igual que "Filete"; la portada lleva un **bloque de tinta
  (`#0E1013`) a sangre en el tercio superior** con el título en blanco y el filete verde. Más
  impacto y estilo informe corporativo, pero vuelve a una superficie grande de color (tinta al
  imprimir) y usa una forma flotante en la portada.

---

## Fuera de alcance

- Cambiar el **contenido** del informe: secciones, orden, frases fijas, textos por defecto,
  placeholders de texto, datos del caso, tabla de hashes, firma o anexo.
- Cambiar el formato de escrito judicial (destinatario, "S____/____D", presentación).
- Entregar el informe en PDF o firmarlo digitalmente.
- Regenerar, migrar o re-estilar informes ya generados (v4/v5).
- Varias plantillas elegibles por caso o editor de diseño desde la UI.
- Usar el logo "Sello" de Factum dentro del cuerpo del informe más allá de la atribución
  vigente.
- Rediseño de la web o de `agent-ui/`; cambios en Tatana.
- Recortar o retocar el logo del estudio por código.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- **Plantilla v6 nueva** (`plantilla_informe_v6.docx` + `build_plantilla_v6.py` con
  `--check` y build determinista), como se hizo con la v5. El `architect` decide si deriva de
  la v4 (como la v5) o de la v5; la v4 sigue siendo la línea de base del test de texto.
- **Se van** de la plantilla: la banda y el bloque de acento de `header1`, la franja y las
  bandas de `header2` (formas DrawingML). El interior queda **sin formas flotantes**.
- **Colores:** el mecanismo de centinelas + B-R2b ya existe y sirve; cambian los defaults de
  `BrandingColors` y qué representa cada centinela (D1). Grises y tinta de Factum van fijos en
  la plantilla (no son centinela), como hoy el gris del pie.
- **Contraste:** el verde `#2F6F12` pasa el chequeo de 4.5:1 de `BrandingColors`.
- **Títulos:** filete y número por párrafo (o tabla) para evitar el agrupamiento de bordes
  que en la v5 hacía perder la línea cuando dos títulos quedan seguidos (limitación 7 de
  `progress/impl_backend_informe-diseno-modelo.md`).
- **Config local:** si D1 decide sacar los colores del estudio de `appsettings.Local.json`, se
  borran **solo** esas dos claves, sin tocar las demás (regla de datos de `AGENTS.md`). No se
  transcriben a ningún archivo versionado.
- **No versionar** datos del estudio (nombres, domicilio, teléfonos, logo, colores).
- **Tests:** los de la v5 (`ReportDesignTests`, `BrandingColorsTests`) se adaptan; se mantiene
  la comparación de texto contra la v4 y el validador OpenXML sin errores nuevos.
- **Verificación visual:** render con `soffice --headless --convert-to pdf` en el scratchpad y
  revisión del usuario en Word (Windows/macOS) y WPS.
- Recordatorios del arnés: regla dura de datos (no tocar casos ni archivos de
  `Storage:DataDirectory`).

---

## Dudas para validar con el usuario

### D1. Paleta de Factum vs. colores del estudio (Branding)
Hoy la v5 usa los colores del estudio que están en `appsettings.Local.json`. Pediste "respetar
la paleta de Factum".
- **A) Paleta de Factum fija:** el informe siempre sale en verde/tinta de Factum.
  `PrimaryColor`/`AccentColor` dejan de usarse (warning al arrancar si están configurados).
- **B) Paleta de Factum como default; el Branding la puede pisar.** Sin colores
  configurados, verde de Factum. Con colores configurados, esos colores reemplazan al verde en
  los mismos lugares (filetes, números, línea de la tabla, tinte). **En esta instalación se
  borran las dos claves de color de `appsettings.Local.json`** para que salga con la paleta de
  Factum.
- **C) Mixto:** estructura siempre en verde de Factum; el color del estudio solo en su bloque
  de identidad (nombre en la portada).
- **Recomendada: B.** Te da lo que pediste (en tu instalación sale con Factum) y no le quita a
  otro cliente del producto la opción de usar su color, que ya está implementada y testeada
  (contraste incluido). A tira código que funciona; C mezcla dos verdes/azules en un documento
  que se pensó con un solo color. **Pregunta asociada:** ¿confirmás que se borren las claves de
  color de tu config local (el logo, nombre y contacto quedan)?

### D2. Identidad del estudio: dónde va y qué pasa con el logo oscuro
El logo actual es un JPEG con fondo oscuro propio y viñeteado: en una portada blanca y
minimalista es el elemento más pesado de la página.
- **A) Portada abajo a la izquierda, logo más chico** (≈ 4.5 × 2.5 cm) junto al nombre y el
  contacto en texto; **en el interior, solo el nombre en texto** a la derecha del encabezado
  (D3); al cierre, el isotipo si algún día se configura.
- **B) Igual que A, pero sin logo en la portada:** solo nombre y contacto en texto (el logo
  oscuro no "pega" con la estética).
- **C) Logo tarjeta como en la v5** (7 × 3.8 cm), sin cambios.
- **Recomendada: A.** Mantiene la identidad del estudio visible y le baja el peso al bloque
  oscuro. Si conseguís una versión del logo en **PNG con fondo transparente** (o el isotipo
  solo), es solo cambio de config y queda mucho mejor sobre blanco: ¿la podés pedir?

### D3. Encabezado de las páginas interiores (lo que reemplaza a la banda y "el cuadro")
- **A) Texto chico gris + línea fina:** a la izquierda "Informe pericial técnico informático ·
  {causa}", a la derecha el nombre del estudio (si hay), y una línea gris de 0.5 pt debajo.
- **B) Solo un filete verde fino** a lo ancho del texto, sin texto.
- **C) Nada:** el encabezado queda vacío; la causa y la página van solo en el pie.
- **Recomendada: A.** Es la convención de informes técnicos y de consultoras: cada hoja suelta
  del expediente se identifica sola (causa) sin color. El pie queda con "Realizado con Factum"
  y "Página N de M" en la misma línea, separado por una línea fina gris.

### D4. Portada: qué se conserva de la v5
- **Se va:** franja vertical a sangre y las tres bandas de acento.
- **Se conserva:** que haya portada (sección propia), título grande, causa, carátula, perito
  con matrícula, fecha, identidad del estudio abajo y "Realizado con Factum".
- **Cambia:** título en dos líneas y en tinta (no en color); un filete verde arriba del título;
  los datos de la causa pasan a una ficha etiqueta/valor con líneas finas.
- **Se agrega (opcional):** el subtítulo "Inspección técnica de dispositivo móvil" debajo del
  título (es el mismo texto que ya está en el título del escrito).
- **Recomendada:** todo lo de arriba, **con** el subtítulo. ¿Te parece bien o preferís la
  portada sin subtítulo?

### D5. Numeración de sección "prominente": ¿romanos o arábigos?
- **A) Romanos (I, II, … XI), como hoy,** pero grandes y en verde, arriba del título.
- **B) Arábigos con cero (01, 02, …)**, estilo consultora.
- **Recomendada: A.** Los romanos son lo habitual en escritos judiciales y lo que ya tiene el
  documento; cambiarlos altera cómo se citan las secciones ("ver punto V"). B se ve más
  moderno, pero es un cambio de forma en un documento legal. **Pregunta asociada:** número
  arriba del título (como en la maqueta) o en la misma línea, a la izquierda.

### D6. Bloques de datos: ¿dos columnas como la v5 o ficha a todo el ancho?
- **A) Ficha a todo el ancho:** el título del bloque arriba (como el resto de las secciones,
  con su número y filete) y debajo las filas etiqueta/valor; etiqueta en gris mayúsculas
  chicas, valor en tinta, líneas finas grises.
- **B) Dos columnas como la v5** (título a la izquierda, filas a la derecha), con líneas grises
  en vez de color.
- **Recomendada: A.** Todas las secciones se ven iguales (una sola grilla), el valor tiene
  más ancho (carátulas largas, "Perito de parte" con título) y se evita que "Identificación" o
  "Referencia de la actuación" se corten en la columna angosta, como pasaba en la v5.

### D7. Tipografía (Sora no es una fuente de Office)
- **A) La de la v5:** Century Gothic en títulos (geométrica, cercana a Sora) y Arial en el
  cuerpo. Sin archivos extra.
- **B) Fuentes de Factum embebidas en el DOCX** (licencia OFL): Sora en títulos e Inter en el
  cuerpo. Se ve igual en cualquier PC, pero el archivo pesa más (cientos de KB), algunas
  versiones de Word avisan o descartan las fuentes al guardar, y el perito que edita en una PC
  sin esas fuentes puede escribir en otra.
- **C) Aptos** (fuente por defecto de Microsoft 365 desde 2023): moderna, pero no está en
  Office 2019/2021, LibreOffice ni WPS.
- **Recomendada: A.** Ya está probada en los tres programas con la v5 y el aire geométrico lo
  dan los tamaños, el interletrado y el blanco, no la fuente exacta. B queda como mejora si el
  render no te convence.

### D8. ¿La v6 reemplaza a la v5?
- **A) Reemplazo:** todos los informes nuevos salen con la v6. La v5 queda en el repo solo
  si la necesitan los tests; los informes ya generados no cambian.
- **B) Opción por config** (`Report:Design = v5 | v6`).
- **Recomendada: A.** Mantener dos maquetas duplica plantilla, script y pruebas visuales, y
  la v5 no tiene usuarios que la prefieran. Los informes ya generados (v4/v5) quedan byte a
  byte iguales en cualquier caso.

### D9. Dirección de diseño
- **A) "Filete"** (la descripta arriba): blanco, filetes verdes, números grandes, sin formas
  flotantes.
- **B) "Margen editorial":** números colgados en el margen y filete vertical en la portada.
- **C) "Bloque de marca":** como A, con un bloque de tinta a sangre en el tercio superior de la
  portada.
- **Recomendada: A.** Es la más sobria y la más robusta al editar en Word, y gasta menos tinta.
  Si después de ver el render querés más impacto en la portada, C se puede sumar sin tocar el
  interior. **Pregunta asociada:** ¿querés ver un render de prueba de A (y de C solo para la
  portada) antes de cerrar la SDD?

### D10. Detalles del cuerpo que cambian solo de formato
- Título del escrito ("INFORME PERICIAL TÉCNICO INFORMÁTICO / INSPECCIÓN TÉCNICA…"): **sin
  subrayado**, en tinta; se conserva el texto y el centrado.
- Fila del contenedor ZIP en la tabla de hashes con **tinte verde suave** de fondo.
- Encabezado de la tabla de hashes **sin relleno**: texto gris en mayúsculas y línea verde.
- **Recomendada:** los tres. ¿Alguno lo preferís como está en la v5?

## Validación (2026-10-01)

El usuario validó las 10 dudas en la opción **recomendada**, salvo D9 (dirección de diseño), que elige mirando el canvas comparativo https://claude.ai/artifact/FabuSoobjeFwZKxkn96uU5 (A Filete, B Margen editorial, C Bloque de marca). La SDD espera esa elección.
- **D9 → A "Filete"** (elegida por el usuario sobre el canvas, 2026-10-01).
