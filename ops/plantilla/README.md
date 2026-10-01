# Plantilla del informe pericial (v4)

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
