# impl_backend — informe-diseno-modelo

**Estado:** done (sin commit, como se pidió)
**Rama:** `feat/informe-diseno-modelo` (base: `9b1ccaf`, con `formulario-caso-catalogos` ya commiteada)
**SDD:** `Refactorizaciones/informe-diseno-modelo.md`. Decisiones: las recomendadas; DP1, DP2 y DP3 = A.
**Frontend:** la SDD no tiene tareas de `client/` ni de `agent-ui/`. No hay Contrato compartido: ningún campo nuevo cruza a la web ni a Tatana. `GET /api/config/public` y `ConfigController` no cambian, y el isotipo no se expone.

## Archivos tocados

| Archivo | Qué |
|---|---|
| `ops/plantilla/build_plantilla_v5.py` (nuevo) | Genera y verifica la v5 desde la v4 (B1–B9). Reutiliza helpers del v4 (`import build_plantilla_v4`, con `sys.dont_write_bytecode` para no dejar `__pycache__`). |
| `ops/plantilla/README.md` | Sección v5, con la cadena "plantilla del usuario → v4 → v5", uso, `--check`, render y muestras (B11). La parte de la v4 queda igual, debajo. |
| `server/src/Factum.Backend/Templates/plantilla_informe_v5.docx` (nuevo, generado) | Plantilla v5 (B9). |
| `server/src/Factum.Backend/Services/Branding/BrandingColors.cs` (nuevo) | `DefaultPrimary`/`DefaultAccent`/`MinPrimaryContrast`, `Normalize` y `ContrastWithWhite` (WCAG) (B12). |
| `server/src/Factum.Backend/Services/Branding/BrandingOptions.cs` | `OrganizationIsotype`, `PrimaryColor` y `AccentColor` (B13). |
| `server/src/Factum.Backend/Services/Branding/BrandingService.cs` | `BrandingSnapshot` suma `Isotype`, `PrimaryColor` y `AccentColor` **al final y con default**. `LoadLogo` pasa a `LoadImage(..., label)`. Validación de hex y de contraste ≥ 4.5:1 para el primario, con los warnings de §3.1 y el log de inicio `Branding: colores primario {P} y acento {A}` (B14). |
| `server/src/Factum.Backend/Services/Reports/BrandColors.cs` (nuevo) | Pasada B-R2b: reemplaza los centinelas en los atributos `val`/`fill`/`color` de document, headers, footers, numbering y styles. Es un no-op con la paleta neutra (B15). |
| `server/src/Factum.Backend/Services/Reports/ReportService.cs` | Plantilla v5 y constructor `internal` con `templateFileName` (DI sigue usando el público, que encadena al interno). Bloques `ISOTIPO` y `NOMBRE_EN_BANDA` con sus condiciones, B-R2b, `ReplaceImagePlaceholder` generalizado (logo + `IsotypePlaceholderRegex`, isotipo de 2 × 2 por default), `IsKnownPlaceholder`, `BodyFont = "Arial"` y comentarios v4 → v5 (B16). |
| `server/src/Factum.Backend/appsettings.json` | `Branding.OrganizationIsotype`, `PrimaryColor` y `AccentColor` vacíos (B17). |
| `.gitignore` | `docs/informe_modelo*` y `docs/Logo_del_estudio*` debajo de `docs/INFORME*` (B18). |
| `README.md` | Tabla de Branding (3 claves nuevas, defaults neutros, contraste, isotipo), ejemplo local y de Docker con colores **ficticios** (`#203040`/`#B0A080`), placeholders nuevos (`{ISOTIPO_ORGANIZACION[:WxH]}`, bloques `MEMBRETE`/`ISOTIPO`/`NOMBRE_EN_BANDA`), centinelas y v4 → v5 (B19). |
| `server/tests/Factum.Backend.Tests/BrandingColorsTests.cs` (nuevo) | B20. |
| `server/tests/Factum.Backend.Tests/ReportDesignTests.cs` (nuevo) | B21 (casos 1 a 11) más un generador de muestras opcional (ver B24). |
| `server/tests/Factum.Backend.Tests/TestImages.cs` (nuevo) | PNG generados en memoria, sin archivos de imagen en el repo. |

**Fuera del repo (DP3 = A, autorizado por el usuario):**

- `docs/Logo_del_estudio` copiado a `server/src/Factum.Backend/branding/logo-estudio.jpg` (JPEG de 1408 × 768 y 128 KB: pasa la validación del logo). `git check-ignore -v` → `.gitignore:39:server/src/Factum.Backend/branding/`.
- `server/src/Factum.Backend/appsettings.Local.json`: le **agregué** solo `Branding.OrganizationLogo = "branding/logo-estudio.jpg"`, `PrimaryColor = "<primario del estudio>"` y `AccentColor = "<acento del estudio>"`, al final de `Branding`. Las claves existentes (`OrganizationName`, `ContactLines`) quedaron sin tocar y en su orden. `git status` no muestra ninguno de los dos archivos (`.gitignore:38:**/appsettings.Local.json`).
- Ningún dato del estudio (nombre, domicilio, teléfonos, logo ni colores) quedó en archivos versionables: lo revisé con grep sobre `ops/`, `README.md`, `Services/`, `tests/`, `appsettings.json`, `.gitignore` y el XML de la v5. En el ejemplo de `Normalize` usé `#1a2b3c`, y no el <primario del estudio> que proponía la SDD en B20, para no versionar el color del estudio.

**No tocados:** `client/`, `agent-ui/`, `backlog.json`, `progress/current.md`, `plantilla_informe_v4.docx`, `build_plantilla_v4.py`, `ReportValues.cs` y `ConfigController`. La modificación de `agent-ui/tsconfig.node.tsbuildinfo` y el `tsconfig.web.tsbuildinfo` sin trackear ya estaban antes de empezar y no son míos.

## Reconciliación con `formulario-caso-catalogos` (SDD §9)

1. `ReportValues.cs` no cambió: `{fraseIntegracion}` sigue saliendo de `IntegrantesTribunal` (párrafo `03940239`, al que solo le cambia la fuente). El test 10 compara el texto pericial completo contra la v4 y da igual.
2. `BrandingSnapshot`: los parámetros nuevos van al final y con default. `ReportValuesIntegrantesTests` (`new BrandingSnapshot(null, [], null)`) compila y pasa. Además lo cubre `BrandingColorsTests.SnapshotDeTresArgumentos_PaletaNeutra`.
3. `ReportService.cs` no lo había tocado esa HU. Apliqué los cambios de §5.7 sobre la versión actual.
4. Los tests de esa HU siguen en verde: 81 de 81 en total.

## Verificación

```
$ python3 ops/plantilla/build_plantilla_v5.py --check server/src/Factum.Backend/Templates/plantilla_informe_v4.docx server/src/Factum.Backend/Templates/plantilla_informe_v5.docx
OK
$ python3 ops/plantilla/build_plantilla_v4.py --check "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" server/src/Factum.Backend/Templates/plantilla_informe_v4.docx
OK          (la v4 no cambió: no aparece en git status)
```

**Determinismo (B9):** construí la v5 dos veces (la segunda, en el scratchpad), y otra vez después del último cambio del script. Las tres dan el mismo SHA-256:
`7c672e325f7d97dc81e47409f8e913940088dca3beb8b272df52d309dc44df1c  plantilla_informe_v5.docx`

**Guarda de centinelas (B2):** con una copia de la v4 en el scratchpad que traía `2f3b4c` en `styles.xml`, el build imprime `FALLA: centinela-en-origen`, sale con 1 y no escribe el destino.

```
$ dotnet build server/src/Factum.Backend/Factum.Backend.csproj
    0 Errores
    4 Advertencia(s)   → solo NU1902 (SharpCompress) y NU1903 (Snappier), ya existentes: avisos de NuGet, ninguno de código
$ dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
Correctas! - Con error: 0, Superado: 81, Omitido: 0, Total: 81
```

El único warning de compilación de los tests es CS8619 en `EvidenceZipTests.cs:57`, que ya estaba y no es de esta HU.

**Validador OpenXML (caso 11):** el DOCX generado con la v4 trae dos errores de origen, que también se ven en la salida del test:
- `/word/footer1.xml Sch_UndeclaredAttribute .../mc:Fallback/w:pict/v:group` (atributo `o:spt` del VML del número de página viejo; en la v5 ya no está, porque `footer1` se reescribió);
- `/word/styles.xml Sch_UnexpectedElementContentExpectingComplex /w:styles/w:style` (`w:uiPriority` fuera de orden en un estilo de origen).

La v5 **no agrega ningún error nuevo**. Comparé por parte + `Id` + XPath sin índices posicionales, porque la portada corre la posición de todo el cuerpo.

**B23 (hashes existentes, solo lectura):** `factum_dev` está en el contenedor `evidentia-v2-mongo-1` (puerto 27017) y lo consulté con `docker exec ... mongosh` en modo lectura. Hay un caso `Completed`, `e661dce0-…`, con `ZipHash 67b62b67…be51` y `ReportHash 3fed35f0…b6e3`, y los dos coinciden con `shasum -a 256` de sus archivos en `dev-data/`. Calculé `shasum` de los 66 artefactos `.docx`/`.zip`/`.pdf` de `dev-data/` antes y después de implementar: son idénticos y Mongo sigue igual. No escribí nada en Mongo ni en `dev-data/`. Los tests trabajan en `Path.GetTempPath()/factum-*-<guid>` y borran solo esa carpeta.

## B24: render (sin datos del cliente en este archivo)

Muestras en el scratchpad (no van al repo), generadas con `ReportDesignTests.Muestras_SiHayCarpetaDeRender` y convertidas con `soffice --headless --convert-to pdf`:

- **Para abrir en Word con la identidad del estudio** (logo real de `branding/` y colores <primario del estudio>/<acento del estudio>; nombre, contacto y caso ficticios):
  `/private/tmp/claude-501/-Users-joelmiguelserrudo-Documents-Projects-Factum/d331c5ef-f445-4af8-98b0-4d017b56de51/scratchpad/muestras/estudio/`
  - `a_sin_branding.docx`, `b_nombre_y_colores_sin_isotipo.docx` (el caso real de hoy, sin isotipo) y `c_logo_e_isotipo.docx` (con un isotipo de prueba: un cuadrado rojo), cada uno con su PDF en `pdf/`.
- Las mismas tres con logo y colores ficticios (`123456`/`ABCDEF`): `.../scratchpad/muestras/ficticias/`.

Lo que se ve en LibreOffice (4 páginas cada una):
- **Portada:** título en tres líneas grandes del color primario, la causa, la carátula entre comillas tipográficas, el perito con su matrícula y la fecha. A la derecha, la franja primaria a sangre con tres bandas de acento abajo. En el pie, el logo como tarjeta de 7 × 3.8 cm, el nombre en negrita del color primario, el contacto en gris y "Realizado con Factum". Sin Branding queda solo la atribución. La franja no tapa texto.
- **Interior:** banda primaria arriba con "INFORME PERICIAL TÉCNICO INFORMÁTICO" y la causa en blanco, el bloque de acento en el margen derecho y el nombre del estudio (o el isotipo). El cuerpo empieza debajo de la banda, sin solaparse. Pie: "Página 2 de 4" … "Página 4 de 4" (DP1) y la atribución en todas las páginas.
- Títulos en mayúsculas del color primario con su línea, y la numeración romana del mismo color. Los tres bloques en dos columnas con líneas primarias. La tabla de hashes tiene el encabezado primario con texto blanco, el hash en Courier y solo líneas horizontales. Cuando hay isotipo, aparece al cierre debajo de la firma.

## Decisiones no obvias y desvíos menores (para el reviewer)

1. **Grilla de los bloques de datos: 2700/2000/3947** en lugar de 2400/2200/4047. Con 2400, "IDENTIFICACIÓN" (Century Gothic 11 pt negrita, con la sangría del número romano) se cortaba a mitad de palabra en el render. El total sigue siendo 8647. Está comentado en el script.
2. **Títulos dentro de la tabla con numeración:** `ind left=567 hanging=300` (la v4 trae `left=2552`, que no entra en la celda). La celda del título tiene `tcMar left=0`. El número romano (`lvlJc right`) queda dentro de la celda.
3. **Títulos de sección:** además de lo que pide la SDD, `contextualSpacing w:val="0"` y `jc left`. Con `contextualSpacing` heredado, Word y LibreOffice anulaban el `before 360` entre dos párrafos Normal. Con justificado, "GENERACIÓN Y ASEGURAMIENTO…" se estiraba. El texto no cambia.
4. **Bordes de celda de la tabla de hashes:** la v4 trae `tcBorders` en cada celda, que pisan a `tblBorders`. Por eso también las reescribí: top y bottom single sz 4 primario, left y right `nil`.
5. **"Carátula:" y el valor:** se separan en dos celdas, y el run que era solo `" "` entre etiqueta y valor se descarta. El texto normalizado sin espacios queda idéntico (test 10).
6. **Párrafo ancla de header1/header2:** `line=20 exact` en lugar de sz 2 con interlineado normal, para que no corra la tabla de la banda hacia abajo.
7. **Limitación conocida (no se corrigió):** si dos títulos quedan seguidos porque una sección no tiene texto, Word y LibreOffice agrupan los párrafos con el mismo borde y dibujan solo la línea del segundo. Probé `w:between`: la línea quedaba pegada arriba del título siguiente, así que la saqué. Con el contenido real normal no pasa.
8. **Nombre de la organización en la banda:** la fila mide 2.0 cm exactos. Un nombre muy largo (hasta 150 caracteres) en Century Gothic 9 pt, en 4.2 cm de ancho, puede quedar recortado. El caso real entra en una línea.
9. El orquestador mencionó "portada con titlePg" para los tests. La SDD (§5.2, D3, caso 1 de B21) dice lo contrario: dos secciones y **sin** `titlePg`. Seguí la SDD, y el test 1 verifica que no haya `TitlePage`.
10. El test de muestras (`Muestras_SiHayCarpetaDeRender`) no hace nada si no está `FACTUM_RENDER_DIR`. Acepta logo y colores por variables de entorno, así se puede probar la identidad local sin versionarla.

## Pendiente para el usuario (prueba manual, SDD §10)

- Abrir `muestras/estudio/b_nombre_y_colores_sin_isotipo.docx` en **Microsoft Word (macOS)**: tiene que abrir sin aviso de reparación. Confirmar Century Gothic real (LibreOffice la sustituye) y aprobar o ajustar <primario del estudio>/<acento del estudio>.
- Reiniciar el backend (ya toma la config local de DP3) y generar un caso de prueba **nuevo**: portada con la identidad real, banda con el nombre del estudio y la frase de integrantes igual que antes.
- Abrirlo también en WPS, revisar la impresión en escala de grises y descargar un informe viejo para confirmar que su hash no cambió.

No dejé procesos en background: `soffice` corrió en primer plano y `pgrep soffice` no devuelve nada.
