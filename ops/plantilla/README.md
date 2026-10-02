# Plantillas del informe pericial (v4 y v5)

La cadena es **plantilla del usuario (`docs/`, no versionada) → v4 → v5**:

| Plantilla | Script | Qué es |
|---|---|---|
| `Templates/plantilla_informe_v4.docx` | `build_plantilla_v4.py` | Contenido pericial, placeholders y bloques, sin datos reales. Es la entrada de la v5 y la línea de base de los tests. El backend ya no la usa en runtime. |
| `Templates/plantilla_informe_v5.docx` | `build_plantilla_v5.py` | La v4 con el diseño del modelo (portada, banda, tipografía, colores). **Es la que usa `ReportService`.** |

La v5 se reconstruye **solo desde la v4 versionada**: no necesita la plantilla
privada de `docs/`.

## v5: qué agrega el script

Especificación: `Refactorizaciones/informe-diseno-modelo.md` (secciones 5 y 8.1).

- **Portada** como sección propia (márgenes propios, `sectPr` en el último
  párrafo de la portada): título grande, `{tipoCausa} N° {numeroCausa}`,
  carátula, perito y fecha. `header2.xml` dibuja la franja vertical del color
  primario y tres bandas de acento (rectángulos DrawingML anclados a la
  página, detrás del texto, sin fallback VML). `footer2.xml` lleva el bloque
  `MEMBRETE` (logo 7 × 3.8, nombre y contacto).
- **Interior:** `header1.xml` pasa a ser la banda del color primario con
  "INFORME PERICIAL TÉCNICO INFORMÁTICO", la causa y, a la derecha, el bloque
  `ISOTIPO` o `NOMBRE_EN_BANDA`. `footer1.xml` es "Página N de M" (campos
  `PAGE` y `NUMPAGES`, que cuentan la portada).
- Títulos de sección en Century Gothic, mayúscula por formato y color
  primario, con una línea fina debajo (el texto no cambia). Numeración romana
  en el mismo estilo.
- "Referencia de la actuación", "Identificación" y "Elementos ofrecidos" en
  tablas de dos columnas (título / etiqueta / valor, grilla 2700/2000/3947).
- Tabla de hashes con encabezado sombreado y solo líneas horizontales.
- Cierre con `{#ISOTIPO}{ISOTIPO_ORGANIZACION:2x2}{/ISOTIPO}` después de la firma.
- Cuerpo en Arial 11 pt (estilo Normal y `docDefaults`; todo Times New Roman
  pasa a Arial). Los hashes siguen en Courier New. `fontTable.xml` declara
  Century Gothic.
- **Colores centinela:** la v5 lleva `2F3B4C` (primario) y `9AA5B1` (acento),
  que son los defaults neutros. El backend los reemplaza al generar por
  `Branding:PrimaryColor` / `Branding:AccentColor`. Si la v4 de entrada ya
  tuviera alguno de los dos valores, el script aborta con
  `FALLA: centinela-en-origen` (el reemplazo pisaría un color ajeno).

Igual que el v4: edita el XML como texto, ubica cada párrafo por `w14:paraId`
con una huella no sensible (si no coincide, `FALLA: ancla <paraId>`), los
paraIds nuevos son deterministas y el ZIP también (la misma v4 da el mismo
SHA-256 de la v5).

### Uso

```bash
# Construir (y verificar al final)
python3 ops/plantilla/build_plantilla_v5.py \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx \
  server/src/Factum.Backend/Templates/plantilla_informe_v5.docx

# Solo verificar
python3 ops/plantilla/build_plantilla_v5.py --check \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx \
  server/src/Factum.Backend/Templates/plantilla_informe_v5.docx
```

Imprime solo `OK` o líneas `FALLA:<tipo>`. `--check` verifica: placeholders y
bloques por parte (cuerpo, `header1`, `footer2`; `header2` y `footer1` sin
placeholders), las dos secciones con su header y footer, los centinelas en las
formas, títulos, numeración y tabla de hashes, que no quede Times New Roman,
XML bien formado, `w14:paraId` únicos, sin `w:author`, autor `Factum` en
`core.xml` y `PAGE`/`NUMPAGES` en `footer1`.

### Render

```bash
soffice --headless --convert-to pdf --outdir /tmp/render \
  server/src/Factum.Backend/Templates/plantilla_informe_v5.docx
```

Para ver informes completos con datos ficticios, el test
`ReportDesignTests.Muestras_SiHayCarpetaDeRender` genera tres DOCX (sin
Branding, con nombre y colores, con logo e isotipo) si se le pasa una carpeta
fuera del repo:

```bash
FACTUM_RENDER_DIR=/tmp/render dotnet test server/tests/Factum.Backend.Tests \
  --filter "FullyQualifiedName~Muestras"
# opcional, para probar una identidad local sin versionarla:
#   FACTUM_RENDER_LOGO=<ruta> FACTUM_RENDER_PRIMARY=<RRGGBB> FACTUM_RENDER_ACCENT=<RRGGBB>
```

Después de regenerar la v5: `--check`, mirar el render, abrirla en Word y
correr `dotnet test` (los tests comparan el texto pericial contra la v4).

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
