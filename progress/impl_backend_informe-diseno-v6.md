# impl_backend — informe-diseno-v6

**Estado:** done (sin commit, como se pidió)
**Rama:** `feat/informe-diseno-v6` (base `c8b8fe9`)
**SDD:** `Refactorizaciones/informe-diseno-v6.md`, con la dirección "Filete". Decisiones: las recomendadas; DP1 = A y DP2 = A, confirmadas por el usuario (se conservan los ":" de las etiquetas y la sangría de primera línea de la v4).
**Frontend / contrato:** no hay trabajo de `client/` ni de `agent-ui/` y no hay Contrato compartido: ningún campo cruza a la web ni a Tatana. `GET /api/config/public` y `ConfigController` no cambian.

## Archivos tocados

| Archivo | Qué |
|---|---|
| `ops/plantilla/build_plantilla_v6.py` (nuevo) | Construye y verifica la v6 desde la v4 (B1–B8). Importa `build_plantilla_v4` y `build_plantilla_v5` con `sys.dont_write_bytecode`, así no queda `__pycache__`. Define su propio `para`/`marker`/`tiny` con la semilla `v6:`. |
| `server/src/Factum.Backend/Templates/plantilla_informe_v6.docx` (nuevo, generado) | Plantilla v6 (B9). |
| `server/src/Factum.Backend/Templates/plantilla_informe_v5.docx` | **Borrada** con `git rm` (B10). El borrado queda en el index. `build_plantilla_v5.py` no cambió. |
| `server/src/Factum.Backend/Services/Branding/BrandingColors.cs` | Defaults de Factum (`2F6F12`/`E8F3DF`), `InkColor`, `MinAccentContrastWithInk`, `Contrast(a, b)` simétrico y `ContrastWithWhite` construido sobre `Contrast` (B13). |
| `server/src/Factum.Backend/Services/Branding/BrandingService.cs` | `NormalizeAccent`: además de validar el hex, exige contraste ≥ 4.5:1 con la tinta. Si no lo cumple, loguea el warning de §3.1 y usa el default (B14). Comentarios actualizados. |
| `server/src/Factum.Backend/Services/Branding/BrandingOptions.cs` | Solo comentarios (B15). |
| `server/src/Factum.Backend/Services/Reports/BrandColors.cs` | Solo comentarios: v6 y los valores nuevos de los centinelas. La lógica no cambió (B16). |
| `server/src/Factum.Backend/Services/Reports/ReportService.cs` | Plantilla v6. `BlockKeys` suma `MEMBRETE_CON_LOGO`/`MEMBRETE_SIN_LOGO` y saca `NOMBRE_EN_BANDA`, con sus condiciones. `KnownPlaceholders` suma `{ATRIBUCION_FACTUM}`. B-R5 aplica el tinte, la negrita y la leyenda recta a la fila del ZIP. B-R8 llena el slot (`BuildAttributionRuns`, `TryFillAttributionSlot`). Comentarios actualizados (B17). |
| `server/tests/Factum.Backend.Tests/BrandingColorsTests.cs` | B20. |
| `server/tests/Factum.Backend.Tests/ReportDesignTests.cs` | B21: casos 1 a 14 para la v6 (detalle abajo). |
| `README.md` | Tabla de Branding (defaults de Factum, validación del acento contra la tinta, el acento como tinte de la fila del ZIP, el isotipo solo al cierre), descripción de la v6, placeholders (`{ATRIBUCION_FACTUM}`, `MEMBRETE_CON_LOGO`/`MEMBRETE_SIN_LOGO`, `{LOGO_ORGANIZACION:4.5x2.5}`, sin `NOMBRE_EN_BANDA`) y el centinela (B19). |
| `ops/plantilla/README.md` | La cadena pasa a ser v4 → v6, con una sección v6 (qué hace, uso, `--check`, render, muestras). Queda una nota de cómo reproducir la v5 fuera del repo (B12). |

**Fuera del repo (B18, D1, autorizado por el usuario):** de `server/src/Factum.Backend/appsettings.Local.json` borré solo las líneas `Branding:PrimaryColor` y `Branding:AccentColor`, y la coma final de la línea anterior. No cambié el orden ni el contenido de nada más. El JSON es válido. Las claves que quedan en `Branding` son `OrganizationName`, `ContactLines` y `OrganizationLogo` (listado sin valores). `git status` no muestra el archivo (`.gitignore:38:**/appsettings.Local.json`). Ningún color ni dato del estudio quedó en archivos versionados ni en este progress. Lo verifiqué con grep sobre el diff y sobre el XML de la v6.

**No tocados:** `client/`, `agent-ui/`, `server/src/Factum.Agent/` (su `appsettings.json` ya venía modificado de antes y no es mío), `backlog.json`, `progress/current.md`, `build_plantilla_v4.py`, `build_plantilla_v5.py`, `plantilla_informe_v4.docx`, `ReportValues.cs`, `ConfigController` y `appsettings.json`.

## Verificación

```
$ python3 ops/plantilla/build_plantilla_v6.py --check server/src/Factum.Backend/Templates/plantilla_informe_v4.docx server/src/Factum.Backend/Templates/plantilla_informe_v6.docx
OK
```

**Determinismo (B9):** construí la v6 tres veces (una en `Templates/` y dos en el scratchpad). Las tres dan el mismo SHA-256:
`4244c03a7a59cf0b5393e084b7dcdd409d11d1bd3556c68fae959cad47c79c74  plantilla_informe_v6.docx`

**v5 regenerada (B10):** regeneré la v5 en el scratchpad con `build_plantilla_v5.py`, que no cambió. `--check` da `OK` y el SHA-256 es `7c672e325f7d97dc81e47409f8e913940088dca3beb8b272df52d309dc44df1c`, el mismo que figura en el progress de la v5.

**v4 (B11):** `build_plantilla_v4.py --check "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" .../plantilla_informe_v4.docx` da `OK`. La v4 no cambió.

**Guarda de centinelas (B2):** armé una copia de la v4 con `2f6f12` en `styles.xml`. El build imprime `FALLA: centinela-en-origen`, sale con código 1 y no escribe el destino.

```
$ dotnet build server/src/Factum.Backend/Factum.Backend.csproj
    0 Errores
    4 Advertencia(s)   → solo NU1902 (SharpCompress) y NU1903 (Snappier), avisos de NuGet que ya existían
$ dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
Correctas! - Con error: 0, Superado: 88, Omitido: 0, Total: 88
```

Pasan todos, incluidos `ReportValuesIntegrantesTests` y `EvidenceZipTests`. Los warnings CS8714/CS8619 de `EvidenceZipTests.cs:56-57` ya estaban y no son de esta HU.

**Casos de `ReportDesignTests`, según la numeración de B21:**
1. `Estructura_DosSecciones_SinTitlePg`
2. `SinFormasFlotantes_EnNingunaParte`: con logo e isotipo
3. `Pies_AtribucionUnaVez_EnLaMismaLineaQuePaginaNdeM`
4. `EncabezadoInterior_TextoCausaYNombre_SinDibujos`
5. `Titulos_NumeroArriba_FileteAbajo_MismoMargen` (11 números, 13 títulos) y `Titulos_SinNotasNiReserva_NueveNumeros`
6. `Colores_Configurados_ReemplazanLaPaletaDeFactum` y `BrandColors_Apply_DevuelveReemplazos`
7. `SinBranding_SinMarcadoresNiImagenes_PaletaDeFactum`
8. `ConLogo_SoloEnElPieDeLaPortada_EnTabla`, con extent ≤ 1 620 000 × 900 000 EMU, y `SinLogo_ConNombre_PieDeLaPortadaSinTabla`
9. `ConIsotipo_SoloAlCierre`
10. `TablaDeHashes_EncabezadoSinRelleno_FilaDelZipConTinte`
11. `TituloDelEscrito_SinSubrayado`
12. `ContenidoPericial_IgualALaV4`: texto v4 vs v6, normalizado sin espacios, **igual**
13. `Validez_SinErroresNuevosRespectoDeLaV4`: **ningún error nuevo**. La v4 trae dos errores de origen, que también se ven en la salida del test: `footer1.xml` `Sch_UndeclaredAttribute` (`o:spt` del VML viejo, que en la v6 ya no está) y `styles.xml` `Sch_UnexpectedElementContentExpectingComplex` (`w:uiPriority` fuera de orden).
14. `Muestras_SiHayCarpetaDeRender`: genera cuatro DOCX (sin Branding; nombre y contacto sin logo; logo, nombre y contacto con la paleta de Factum; logo, isotipo y colores ficticios). Con `FACTUM_RENDER_LOGO` se prueba un logo local sin versionarlo.

En el caso 5 los títulos se comparan con `ToUpperInvariant`, porque "Referencia de la actuación:" está en minúsculas en el texto y la mayúscula es solo de formato (`caps`).

## B23: hashes existentes (solo lectura)

`factum_dev` (contenedor `evidentia-v2-mongo-1`) tiene 2 casos en `Status` 2 (Completed): `e661dce0-…` y `92fdedc1-…`. Para los dos, `ZipHash` y `ReportHash` coinciden con `shasum -a 256` de sus archivos en `dev-data/`. Calculé el `shasum` de los 68 `.docx`/`.zip`/`.pdf` de `dev-data/` antes y después de implementar, y son idénticos (`cmp` sin diferencias). La consulta a Mongo antes y después da lo mismo. No escribí nada en Mongo ni en `dev-data/`. Los tests trabajan en `Path.GetTempPath()/factum-*-<guid>` y borran solo esa carpeta.

## B24: render (LibreOffice, `soffice --headless --convert-to pdf`)

Las muestras están en el scratchpad y no se versionan:
`/private/tmp/claude-501/-Users-joelmiguelserrudo-Documents-Projects-Factum/d331c5ef-f445-4af8-98b0-4d017b56de51/scratchpad/muestras-v6/`
- `ficticias/`: las cuatro muestras con un logo ficticio (un PNG generado en memoria). Los PDF están en `ficticias/pdf/`.
- `logo-real/`: las mismas cuatro, pero `c_logo_nombre_y_contacto.docx` y `d_…` llevan el logo real de `server/src/Factum.Backend/branding/` (vía `FACTUM_RENDER_LOGO`). La `c` usa la paleta por defecto (la config local ya sin colores). Nombre, contacto y caso son ficticios. Los PDF están en `logo-real/pdf/`.

Lo que se ve, comparado con la maqueta A (5 páginas cada una):
- **Portada:** el filete verde de ~3 cm, "Informe pericial / técnico informático" grande en tinta, el subtítulo en gris y la ficha (CAUSA, CARÁTULA, PERITO, FECHA) con líneas grises finas. En el pie, con logo, aparece el logo (≤ 4.5 × 2.5 cm) a la izquierda y el nombre en negrita y el contacto en gris a la derecha. Sin logo, el nombre y el contacto quedan en texto, sin una celda vacía. Sin Branding, solo queda "Realizado con Factum" centrado, sin huecos raros. No hay franjas ni bandas.
- **Interior:** el encabezado queda en una línea: "Informe pericial técnico informático · Expediente N° 4321/2026" a la izquierda y el nombre a la derecha (vacío sin nombre), sobre una línea fina. El pie también queda en una línea fina: el sello con "Realizado con Factum" a la izquierda y "Página N de 5" a la derecha. La atribución sale una vez por página, en todas. El título del escrito queda sin subrayado (CG + Arial gris, interletrado).
- **D6, el número romano en su propio párrafo: se ve.** Cada romano (I … IX en la muestra sin Notas ni Reserva) aparece en verde, arriba de su título, y la numeración se corre sola cuando falta una sección. Los títulos van en mayúsculas, tinta e interletrado, y todos arrancan en el mismo margen. Cada uno tiene debajo un filete verde de ~1 cm. Con títulos seguidos (Operaciones, Valoración y Conclusiones sin texto), cada filete se ve igual: no se agrupan.
- **Fichas:** Referencia, Identificación y Elementos van a todo el ancho, con líneas grises. Las etiquetas están en mayúsculas grises y conservan los ":" (DP1 A). La intro "En fecha…" queda como párrafo de cuerpo arriba de la ficha, con su sangría (DP2 A).
- **Tabla de hashes:** el encabezado está en gris, sin relleno, con una línea verde debajo. El hash va en Courier. La fila del ZIP lleva el tinte suave, el nombre en negrita y la leyenda recta "Contenedor de la evidencia". Con colores ficticios, el tinte y la línea toman los colores configurados.
- **Cierre:** la firma no cambió. Debajo hay un filete corto centrado y, si hay isotipo, el isotipo.

**No probado por mí:** el render en **Microsoft Word** y en **WPS**. No hay forma headless de hacerlo acá, y WPS está abierto como app del usuario, así que no lo usé. Lo de D6 está confirmado solo en LibreOffice. Queda pendiente para el usuario (pasos 1 y 8 de la prueba manual de la SDD).

## B25: arranque con la config local

El backend del usuario estaba corriendo (`dotnet run`, en el puerto 8080). **No lo toqué** y sigue vivo. Levanté por unos segundos una segunda instancia del DLL ya compilado (`dotnet bin/Debug/net10.0/Factum.Backend.dll --contentRoot server/src/Factum.Backend`, en Development). El log de arranque muestra:

```
Branding: logo de la organización cargado (…)
Branding: colores primario 2F6F12 y acento E8F3DF
```

No aparece ningún warning de Branding. Después, esa instancia no pudo tomar el puerto 8080 (lo ocupa el backend del usuario: `address already in use`) y terminó sola. La maté igual por PID y no quedó ningún proceso mío corriendo.

**Ojo:** `dotnet build` compiló sobre el `bin/Debug` del backend del usuario mientras corría. El proceso siguió funcionando, pero conserva el código y la plantilla v5 en memoria (la v5 vieja sigue copiada en `bin/`). **Hay que reiniciarlo** para que use la v6 y la config local sin colores.

## Decisiones no obvias (para el reviewer)

1. **Separador `4A82988C`:** está entre "OPERACIONES REALIZADAS" y su texto, no antes de un título. Igual pasa a `tiny`, como dice la SDD: se saca `numPr` (`numId 0`) e `ind`.
2. **Filete de cierre:** `ind left 4054 right 4053` (8107 repartidos), como la SDD. El filete de portada y los de los títulos llevan `keepNext`. El de cierre no lo lleva.
3. **Fila del ZIP:** la leyenda clona el rPr del nombre **antes** de que se ponga en negrita. Le saco `Bold` y `BoldComplexScript`, y queda recta, en 8 pt y en `3D444C`. La línea "Origen:" de los archivos no cambió (cursiva, 7 pt, `595959`). El sombreado usa la propiedad tipada `tcPr.Shading`, así el SDK lo ubica en el orden del esquema.
4. **`TryFillAttributionSlot`:** si el slot está partido entre runs, borra el texto del slot en todos los párrafos de ese footer y cae al párrafo centrado de siempre. Así cada footer termina con exactamente una atribución.
5. **Título del escrito:** el run del `<w:br/>` toma el formato de la línea 1. Los runs después del salto pasan a Arial 9 `3D444C`. `--check` falla si queda algún `<w:u `.
6. **Header1:** el párrafo de cada celda lleva el rPr gris también en la marca de párrafo, así una celda vacía (sin nombre) no agranda la fila.
7. **Huella extra (B2):** además de la de la v5, se verifica que los 7 separadores estén vacíos, que `6A0EA424` empiece con "ANEXO", que los 11 títulos numerados tengan `numId 2` y los 2 sin número no tengan `numPr`, y que solo `numId 2` use `abstractNum 1`.
8. **Muestras:** se generan con `MakeCaseWithTexts(notas: false, reserva: false)`. Así se ven títulos seguidos y la numeración corrida (B24).
