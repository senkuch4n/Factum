# Checklist — formato del informe pericial (DOCX)

Generador: `server/src/Factum.Backend/Services/Reports/` (plantilla v6). Herramientas: `soffice --headless
--convert-to pdf` y ver las páginas como imágenes (mirarlas, no solo convertirlas).

1. **Generar sin tocar la base** desde un proyecto/test temporal fuera del repo: caso típico, campos
   mínimos (sin "null", etiquetas huérfanas ni líneas vacías), extremos (carátula de 500, nombres largos,
   8+ integrantes, emoji, RTL/control, HTML, comillas tipográficas, ñ/acentos, markdown con listas
   anidadas y negritas), 30+ capturas verticales/horizontales, fechas límite y formato en español.
2. **Revisar**: desbordes y cortes, tablas fuera de margen, imágenes deformadas o fuera de página,
   numeración de listas, títulos huérfanos, encabezado/pie, fuentes y espaciados, XML válido (sin
   caracteres de control ilegales: Word no abriría el archivo).
3. **DOCX real** del recorrido E2E: guardarlo en `qa/informe/` antes de borrar el caso QA y revisarlo igual.
