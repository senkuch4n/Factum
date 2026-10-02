# Review — informe-diseno-v6

**Veredicto:** APROBADA

## Checkpoints
- C3 (alcance backend/ops/README, sin client/agent-ui, sin Mongo, sin console/TODO): [x]
- C4 build: [x] `dotnet build` Backend 0 errores (4 avisos NuGet preexistentes)
- C4 tests: [x] `dotnet test` 88/88
- C4 salida real: [x] se revisaron PDFs de muestra (a_sin_branding p1-2, d p4) contra la maqueta "Filete"
- Fidelidad "Filete": [x] portada sin franja ni bandas (filete verde 3 cm, título grande, ficha a todo el ancho con líneas grises); interior con encabezado gris de una línea + línea; romano verde arriba del título + filete; fichas a todo el ancho con ":" y sangría (DP1/DP2 A); tabla de hashes con encabezado sin relleno y línea verde, fila ZIP con tinte; pie "Realizado con Factum" + "Página N de M". Sin w:drawing/v:shape/wps/pict en la plantilla.
- Paleta Factum por defecto y override: [x] (tests 6/7 pasan; muestra d con colores ficticios aplica el override)
- Texto legal idéntico a v4/v5: [x] test `ContenidoPericial_IgualALaV4` pasa
- Privacidad: [x] `appsettings.Local.json` y `branding/` ignorados (`.gitignore:38-39`); sin nombres/teléfono/logo del estudio en la plantilla v6 descomprimida, README, tests ni appsettings; los colores nuevos en el diff son de Factum o ficticios (123456, ABCDEF). `104862` en styles.xml viene de la v4 (preexistente).
- Determinismo: [x] `--check` OK; reconstrucción desde la v4 da SHA-256 idéntico al versionado (4244c03a…); no deja `__pycache__`
- Informes ya generados: [x] sin cambios de modelo/DB; plantilla v5 no referenciada en código

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `server/src/Factum.Agent/appsettings.json` figura modificado (`Mock: true`); es ajeno a la HU y estaba así antes. No incluirlo en el commit de la HU.
- README: la fila de `{LOGO_ORGANIZACION}` conserva "caja de 5 × 1.5 cm" como default al lado del ejemplo `4.5x2.5`; verificar que el default descrito sea correcto.
- El backend del usuario sigue con la v5 en memoria: reiniciarlo para ver la v6.
- Render en Word/WPS no verificado (solo LibreOffice); queda como prueba manual del usuario.
