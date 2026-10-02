# Plantillas del informe pericial (v4, v5 y v6)

La cadena es **plantilla del usuario (`docs/`, no versionada) → v4 → v6**:

| Plantilla | Script | Qué es |
|---|---|---|
| `Templates/plantilla_informe_v4.docx` | `build_plantilla_v4.py` | Contenido pericial, placeholders y bloques, sin datos reales. Es la entrada de la v6 y la línea de base de los tests. El backend ya no la usa en runtime. |
| `Templates/plantilla_informe_v6.docx` | `build_plantilla_v6.py` | La v4 con el diseño "Filete" y la paleta de Factum. **Es la que usa `ReportService`.** |
| (no versionada) v5 | `build_plantilla_v5.py` | El diseño anterior (portada con franja, banda interior). El `.docx` se borró del repo, pero el script queda sin cambios: lo importa el v6 y reproduce la v5 desde la v4 byte a byte si alguna vez hace falta. |

La v6 se reconstruye **solo desde la v4 versionada**: no necesita la plantilla
privada de `docs/`.

## v6: qué agrega el script

Especificación: `Refactorizaciones/informe-diseno-v6.md` (secciones 5 y 8.1).
Reutiliza por import los helpers de `build_plantilla_v4.py` y
`build_plantilla_v5.py` (con `sys.dont_write_bytecode`, para no dejar
`__pycache__`).

- **Sin formas flotantes** en ninguna parte: encabezado, pie, portada, filetes
  y fichas son párrafos o tablas de flujo.
- **Portada** como sección propia (`sectPr` en el último párrafo de la
  portada): un filete verde de ~3 cm, "Informe pericial / técnico
  informático" en Century Gothic 34 pt, el subtítulo y una ficha
  etiqueta/valor (causa, carátula, perito, fecha) con líneas grises.
  `header2.xml` queda vacío. `footer2.xml` lleva el bloque `MEMBRETE` con dos
  variantes: `MEMBRETE_CON_LOGO` (tabla logo 4.5 × 2.5 | nombre y contacto) y
  `MEMBRETE_SIN_LOGO` (nombre y contacto en texto).
- **Interior:** `header1.xml` es una tabla de texto chico gris ("Informe
  pericial técnico informático · {tipoCausa} N° {numeroCausa}" a la izquierda,
  `{ORGANIZACION}` a la derecha) con una línea fina debajo. `footer1.xml` es
  una línea fina con el slot `{ATRIBUCION_FACTUM}` a la izquierda (lo llena el
  backend con el sello y "Realizado con Factum") y "Página N de M" a la
  derecha (campos `PAGE`/`NUMPAGES`, que cuentan la portada).
- **Títulos:** cada título numerado lleva arriba un párrafo vacío con la
  numeración romana (Century Gothic 20 pt verde, `lvlText "%1"`, sin punto) y
  debajo un filete verde de ~1 cm (borde superior de un párrafo vacío). Los
  títulos van en Arial negrita, mayúscula por formato, tinta e interletrado,
  todos en el mismo margen. Los separadores vacíos de la v4 antes de los
  títulos pasan a párrafos mínimos.
- "Referencia de la actuación", "Identificación" y "Elementos ofrecidos" son
  **fichas** a todo el ancho (etiqueta / valor, grilla 2490/6157, líneas
  grises). La intro de Elementos queda como párrafo de cuerpo.
- **Tabla de hashes** a todo el ancho (3090/5557), encabezado gris sin relleno
  con una línea verde debajo; el tinte de la fila del ZIP lo pone el backend.
- Título del escrito sin subrayado ni negrita; anexo con el aspecto de un
  título; cierre con un filete centrado y `{#ISOTIPO}{ISOTIPO_ORGANIZACION:2x2}{/ISOTIPO}`.
- Cuerpo en Arial 11 pt gris `3D444C` (estilo Normal y `docDefaults`).
  `fontTable.xml` declara Century Gothic y Arial.
- **Centinela:** la v6 lleva el verde `2F6F12` (default del primario) en los
  filetes, los números (`numbering.xml`) y la línea de la tabla. El backend lo
  reemplaza al generar por `Branding:PrimaryColor`. El tinte `E8F3DF` no va en
  la plantilla. Si la v4 de entrada ya trae alguno de los dos valores, el
  script aborta con `FALLA: centinela-en-origen`.

Igual que los anteriores: edita el XML como texto, ubica cada párrafo por
`w14:paraId` con una huella no sensible (si no coincide, `FALLA: ancla
<paraId>`), los paraIds nuevos son deterministas (semilla `v6:`) y el ZIP
también (la misma v4 da el mismo SHA-256 de la v6).

### Uso

```bash
# Construir (y verificar al final)
python3 ops/plantilla/build_plantilla_v6.py \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx \
  server/src/Factum.Backend/Templates/plantilla_informe_v6.docx

# Solo verificar
python3 ops/plantilla/build_plantilla_v6.py --check \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx \
  server/src/Factum.Backend/Templates/plantilla_informe_v6.docx
```

Imprime solo `OK` o líneas `FALLA:<tipo>`. `--check` verifica: placeholders y
bloques por parte (cuerpo, `header1`, `footer1`, `footer2`; `header2` vacío),
las dos secciones con su header y footer (la segunda con `top 1418` y
`header 680`), que ninguna parte tenga `wp:anchor`, `wps:wsp` ni `v:shape`, el
centinela en los 15 filetes, la numeración y las 2 líneas de la tabla (y en
ningún header ni footer), que `E8F3DF` no esté, 11 párrafos de número sin
texto y ningún título con `numPr`, el título del escrito sin subrayado, el
encabezado de la tabla sin `w:shd`, estilos con Arial y `3D444C`, que no quede
Times New Roman, XML bien formado, `w14:paraId` únicos, sin `w:author`, autor
`Factum` en `core.xml` y `PAGE`/`NUMPAGES` en `footer1`.

### Render

```bash
soffice --headless --convert-to pdf --outdir /tmp/render \
  server/src/Factum.Backend/Templates/plantilla_informe_v6.docx
```

Para ver informes completos con datos ficticios, el test
`ReportDesignTests.Muestras_SiHayCarpetaDeRender` genera cuatro DOCX (sin
Branding; nombre y contacto sin logo; logo, nombre y contacto con la paleta de
Factum; logo e isotipo con colores ficticios) si se le pasa una carpeta fuera
del repo:

```bash
FACTUM_RENDER_DIR=/tmp/render dotnet test server/tests/Factum.Backend.Tests \
  --filter "FullyQualifiedName~Muestras"
# opcional, para probar un logo o colores locales sin versionarlos:
#   FACTUM_RENDER_LOGO=<ruta> FACTUM_RENDER_PRIMARY=<RRGGBB> FACTUM_RENDER_ACCENT=<RRGGBB>
```

Después de regenerar la v6: `--check`, mirar el render, abrirla en Word y
correr `dotnet test` (los tests comparan el texto pericial contra la v4).

## v5 (reproducible, sin uso)

Especificación: `Refactorizaciones/informe-diseno-modelo.md`. Para
reproducirla (por ejemplo, para comparar), generarla fuera del repo:

```bash
python3 ops/plantilla/build_plantilla_v5.py \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx /tmp/plantilla_informe_v5.docx
```

---

# v4

`server/src/Factum.Backend/Templates/plantilla_informe_v4.docx` **no se edita a
mano**: se deriva de la plantilla del usuario con `build_plantilla_v4.py`
(Python 3, solo biblioteca estándar). La especificación completa está en
`Refactorizaciones/informe-pericial-de-parte.md`, Anexo A.

## Qué hace el script

- Canoniza los placeholders (`{nombreTribunal}`, `{caratula}`, …): cada uno
  queda en su propio run y en un solo `<w:t>`.
- Reescribe las frases atadas a una causa o a un género con placeholders y
  frases armadas en código (`{tramiteAnte}`, `{fraseDomicilio}`, `{elSuscripto}`, …).
- Saca los datos reales que trae la plantilla de origen **sin escribirlos ni
  imprimirlos nunca**: reconstruye esos párrafos desde cero, ubicándolos por
  `w14:paraId`.
- Agrega los bloques condicionales (`{#clave}` … `{/clave}`), el membrete
  (`word/header1.xml`), la firma del perito y el anexo de capturas.
- Ajusta la tabla de hashes (anchos, encabezado repetido, filas que no se
  parten) y el margen del pie (`w:footer="567"`, 1 cm).
- Reemplaza `docProps/core.xml` (autor `Factum`, sin nombres de personas).
- Empaqueta en forma determinista: la misma entrada da el mismo archivo.

Antes de editar, el script comprueba una huella **no sensible** de cada párrafo
que toca. Si la plantilla de origen cambió y una huella no coincide, aborta con
`FALLA: ancla <paraId>` y no escribe nada útil: hay que revisar el script.

## Uso

La plantilla de origen vive en `docs/` y **no se versiona** (`.gitignore`:
`docs/INFORME*`), porque es del usuario.

```bash
# Construir (y verificar al final)
python3 ops/plantilla/build_plantilla_v4.py \
  "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx

# Solo verificar
python3 ops/plantilla/build_plantilla_v4.py --check \
  "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx
```

Las dos formas imprimen solo `OK` o líneas `FALLA:<tipo>`, nunca el texto que
falló. `--check` verifica:

1. el conjunto exacto de placeholders del cuerpo y del membrete, cada uno
   entero dentro de un `<w:t>`, y que cada bloque abra y cierre una sola vez;
2. que ningún dato real de la plantilla de origen aparezca en el destino;
3. el autor de `core.xml`;
4. la estructura (`headerReference`, `w:footer="567"`, relación y content
   type del header, `w14:paraId` únicos, sin `w:author`);
5. que todas las partes XML estén bien formadas.

## Después de regenerar

1. Correr `--check` (tiene que dar `OK`).
2. Mirar el render: `soffice --headless --convert-to pdf <v4.docx>` en una
   carpeta temporal.
3. Abrir la v4 en Word o WPS y revisar las frases fijas.
4. `dotnet build` del backend: la plantilla se copia a `bin/` como contenido.

Para cambiar la redacción de una frase fija alcanza con editar el texto en
`build_plantilla_v4.py` y regenerar; `ReportService` no cambia mientras los
placeholders sean los mismos.
