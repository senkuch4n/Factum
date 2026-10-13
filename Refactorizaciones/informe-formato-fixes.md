# SDD: Arreglos de formato y robustez del informe pericial (plantilla v6)

**Slug:** `informe-formato-fixes` · **Issue:** #62
**HU:** `docs/hu-informe-formato-fixes.md`. El usuario la validó el 2026-10-13 con todas las opciones recomendadas: D1 A,
D2 A **sin script** de migración, D3 A, D4 A, D5 A **con la sangría de 4 cm también en las respuestas de una línea** (sin
cambiar "la causa Causa N°"), D6 A.
**Fuente QA:** `docs/qa/2026-10-13/informe-formato.md` (H01-H19, R1-R3).
**Base de código:** `36140d4`. **Rama:** `feat/informe-formato-fixes`, que sale de `develop` actualizado.
**Coordinación:** `Refactorizaciones/api-hardening.md` (#63), §4.8. **Esta HU es dueña del helper `Common/SafeText.cs` y
de su aplicación en todos los puntos de entrada de §4.8.2** (ver §5). #63 depende de ese helper y se implementa después.
**Asignación:** `implementer-backend` (server + plantillas) e `implementer-frontend` (solo `client/src/lib/report-markdown.ts`).
Lanzar primero el backend; el frontend puede ir en paralelo porque el contrato de `cleanText` queda fijo en §5.3.

> **Para el orquestador, en pocas líneas:**
> 1. **Diferencias con #63** (gana la validación de #62; hay que corregir `api-hardening.md` §4.8): DEL y C1
>    (U+007F-U+009F) **se conservan** en la entrada y en el render (en #63 §4.8.1 se quitaban). Además, la limpieza al
>    guardar y el helper son de #62 y no de #63 (§4.8.3 punto 1 de #63 dice lo contrario), y `cleanText` del cliente
>    **sí** se alinea en esta HU (D-A4 de #63 lo dejaba afuera). Detalle en §5.4.
> 2. **DA3 (decisión del arquitecto):** con los límites actuales, la portada no entra en una página solo achicando la
>    fuente de la carátula. Lo probé con renders: con la carátula a 9 pt sigue en 2 páginas. Hace falta además
>    compactar los espacios de la portada y, en el caso extremo, el membrete. Ver §6.4.
> 3. Las frases fijas se cambian en `build_plantilla_v4.py`, que lee la plantilla privada de `docs/`. Esa plantilla está
>    en esta máquina, y hoy v4 y v6 se regeneran **byte a byte** desde ella (verificado: SHA-256 iguales a los
>    versionados).

---

## 1. Resumen funcional

El informe DOCX deja de fallar por un carácter inválido en XML y deja de mostrar texto invertido por controles bidi. El
generador sanea todo lo que emite, y la API ya no guarda esos caracteres: el caso, el perfil, los textos del informe, la
marca, las cuentas, los catálogos y la auditoría del agente pasan por el helper compartido `SafeText`. El sistema
operativo sale una sola vez ("Android 11", no "Android Android 11"), también en los casos ya guardados. La firma no se
separa del firmante. La portada entra en una página aunque los datos lleguen a los límites. El encabezado interior ocupa
como máximo 2 líneas. Las capturas chicas no se amplían más allá de 96 dpi. Se corrigen "siendo las … horas", el
"Se constató:" duplicado, las sangrías, "Marca y modelo: No informado", el "Lic.." y el "causa  N°" con dos espacios, el
idioma es-AR y el orden de esquema de `styles.xml`. No hay campos nuevos ni migraciones, y los informes ya emitidos no
se tocan.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí** | `Common/SafeText.cs` (nuevo), `Services/Reports/{ReportValues,ReportService,ReportMarkdownRenderer}.cs`, `Services/Reports/ReportCoverLayout.cs` (nuevo), `Models/DeviceInfo.cs`, los puntos de entrada de §5.2 y las plantillas `Templates/plantilla_informe_v{4,6}.docx` (regeneradas, nunca a mano). |
| scripts de plantilla `ops/plantilla/` | **sí** | `build_plantilla_v4.py` (frases y sangrías), `build_plantilla_v6.py` (firma, idioma, estilos, anclas de portada en `--check`) y `README.md`. `build_plantilla_v5.py` **no** cambia. |
| backend (Tatana) `server/src/Factum.Agent` | **no** | — |
| tests .NET `server/tests/Factum.Backend.Tests` | **sí** | Nuevos: `SafeTextTests.cs`, `ReportFormatFixesTests.cs`, `ReportCoverLayoutTests.cs`, `ReportQaRegressionTests.cs`. Se ajustan `ReportMarkdownTests.cs` (fixtures F18-F24) y `ReportBodyImagesDocxTests.cs` (tamaño de imagen). |
| client `client/` | **sí, mínimo** | Solo `cleanText` en `client/src/lib/report-markdown.ts` (espejo de `ReportValues.Clean`). Sin cambios visuales. |
| agent-ui `agent-ui/` | **no** | — |
| Mongo / `Storage:DataDirectory` | **no** | Sin migraciones (D2 A sin script). Los DOCX y ZIP existentes no se tocan. |

## 3. Modelo de datos

No hay colecciones, campos ni índices nuevos. Cambia **qué** se guarda: desde esta HU los strings de entrada se guardan
ya limpios (§5). Los documentos viejos siguen leyéndose igual: no hay cambios en clases persistidas, salvo el getter
calculado `DeviceInfo.OsLabel`, que no se persiste.

Inspección de solo lectura de `factum_dev` (contenedor `evidentia-v2-mongo-1`, 2026-10-13):
- `cases` (5 documentos): `Device.OsVersion` siempre vale `"Android 14"` y `Device.Platform` vale `"android"`. Confirma
  H03 (con el código actual sale "Android Android 14"). `OsLabel` no está en Mongo (es un getter).
- `cases`, `expert_profiles`, `account_brandings` y `catalog_entries`: **ninguno** tiene U+FFFE/U+FFFF ni controles
  bidi. No hay datos de desarrollo que se rompan por la limpieza.

Compatibilidad: un documento viejo con caracteres "sucios" se genera bien igual, porque el generador sanea siempre
(§6.1). Se limpia en la base recién cuando el perito lo vuelve a guardar. **No se migra nada.**

## 4. Endpoints / mensajes WebSocket

No hay endpoints nuevos ni rutas cambiadas, y los DTO no cambian. Cambia el comportamiento:

| Método y ruta | Cambio |
|---|---|
| `POST /api/cases`, `PUT /api/cases/{id}` | Los campos de texto se guardan limpios (SingleLine: saltos y tabs pasan a un espacio). `observaciones` usa MultiLine. Los largos se miden **después** de limpiar. |
| `PUT /api/cases/{id}/report-texts` | Los 8 textos se limpian (MultiLine) **antes** de validar y guardar. La respuesta devuelve el texto limpio. `formato`, `trigger` y `restored_from` no se tocan. |
| `PUT /api/profile` (perfil del perito) | `nombre`, `matricula`, `profesion` y `caracter` se guardan limpios (SingleLine + Trim). |
| Guardado de marca por cuenta | `organization_name` y `contact_lines[i]`: se quitan en silencio bidi, no-caracteres y surrogates sueltos. **Se mantiene** el 400 `MsgControlChars` para C0 (contrato existente). Ver §5.2. |
| Panel de cuentas, catálogos, auditoría del agente | Lo indicado en §5.2 (adoptado de #63 §4.8.2). |
| `GET /api/cases/{id}/report-texts/defaults` | `{sistemaOperativo}` sale sin el prefijo duplicado (§6.3). |
| `POST` de generación del informe | Nunca falla por caracteres. El DOCX cambia según §6. |

Los errores controlados existentes no cambian: ni mensajes, ni códigos, ni claves de `missing`.

## 5. Texto de entrada y del render: helper `SafeText` (D1 A; adopta #63 §4.8)

### 5.1 Helper compartido `server/src/Factum.Backend/Common/SafeText.cs`

Se mantienen **el nombre y la firma exactos** de `api-hardening.md` §4.8.1, para que #63 lo reuse sin cambios:

```csharp
namespace Factum.Backend.Common;

public enum TextShape { SingleLine, MultiLine }

/// Caracteres que la API no guarda y que el render no emite (#62 D1 A). Puro e idempotente.
public static class SafeText
{
    /// null → "". NO hace Trim (lo hace quien llama, después). Idempotente.
    public static string Clean(string? value, TextShape shape);
    /// null → null; si no, Clean.
    public static string? CleanOrNull(string? value, TextShape shape);
    /// Solo bidi + no-caracteres + surrogates sueltos; deja los controles C0 (para quien ya los rechaza).
    public static string StripInvisible(string? value);

    public static bool IsBidiControl(int codePoint);     // U+202A–U+202E, U+2066–U+2069
    public static bool IsNonCharacter(int codePoint);    // U+FDD0–U+FDEF, y (cp & 0xFFFE) == 0xFFFE en cualquier plano
    public static bool IsStrippedControl(int codePoint, TextShape shape);
        // #62: SOLO C0 U+0000–U+001F salvo \t \n \r. DEL y C1 (U+007F–U+009F) NO (diferencia con #63, §5.4).
}
```

**Lista exacta.** El recorrido es por code point. Un surrogate alto seguido de uno bajo forma **un** code point; un
surrogate sin pareja se quita. Los topes de largo usan `string.Length`, igual que hoy.

| Entrada | `SingleLine` | `MultiLine` | Render (`ReportValues.Clean`) |
|---|---|---|---|
| `\r\n` | un espacio | se conserva | → `\n` |
| `\r` suelto | un espacio | se conserva | → `\n` |
| `\n` | un espacio | se conserva | se conserva |
| `\t` | un espacio | se conserva | → un espacio |
| Resto de C0 (U+0000–U+0008, U+000B, U+000C, U+000E–U+001F) | se quita | se quita | se quita |
| **DEL y C1 (U+007F–U+009F, incluye U+0085)** | **se conserva** | **se conserva** | **se conserva** |
| Bidi U+202A–U+202E (LRE, RLE, PDF, LRO, RLO) y U+2066–U+2069 (LRI, RLI, FSI, PDI) | se quita | se quita | se quita |
| No-caracteres: U+FDD0–U+FDEF y U+xFFFE/U+xFFFF de los 17 planos (incluye U+FFFE y U+FFFF) | se quita | se quita | se quita |
| Surrogate suelto (alto sin bajo, o bajo sin alto) | se quita | se quita | se quita |
| **Se conserva todo lo demás**: acentos, ñ, comillas tipográficas, «», rayas, emoji con ZWJ U+200D, ZWNJ U+200C, **LRM/RLM U+200E/U+200F**, ALM U+061C, NBSP U+00A0, U+2028/U+2029, BOM U+FEFF y árabe/hebreo sin controles | igual | igual | igual |

La conversión de saltos y tabs a un espacio en SingleLine (en vez de quitarlos) es D-A1 de #63, adoptada: así
"Juan\nPérez" queda "Juan Pérez". No se colapsan espacios.

`StripInvisible` quita solo las filas bidi, no-caracteres y surrogates; los C0 quedan. Todo lo de la tabla es válido en
XML 1.0 después de limpiar: los C0 permitidos (`\t \n \r`) son XML válidos, y los no-caracteres de planos altos también
lo son. Se quitan igual por criterio (no tienen uso legítimo). Con eso alcanza para que `doc.Save()` no falle.

Orden en cada punto de entrada: **`SafeText.Clean` → `Trim` → validaciones de obligatorio/largo**.

### 5.2 Dónde se aplica: puntos de entrada (adoptado de #63 §4.8.2; esta HU los implementa)

| Punto | Campos | Forma |
|---|---|---|
| `CaseFields.From(CreateCaseRequest)` y `From(UpdateCaseRequest)` (`Services/Cases/CaseValidation.cs`) | los 19 campos de la causa vía `C()` | `C(s) => SafeText.Clean(s, TextShape.SingleLine).Trim()` |
|  | `observaciones` | `SafeText.CleanOrNull(r.Observaciones, TextShape.MultiLine)?.Trim()` |
|  | `integrantes` (lista) | por `IntegrantesFormatter.Clean` → `CatalogText.CleanValue` (fila de catálogos) |
| `CaseService.CreateAsync` | `device.imei`, `name`, `serial`, `manufacturer`, `model`, `os_version` (`platform` no: es un enum de hecho) | `SingleLine`; `Trim` solo donde ya existe (`imei`, `name`). Donde hoy no hay `Trim`, no se agrega. |
| `CaseService.UpdateAsync` | `imei` | `SafeText.CleanOrNull(request.Imei, TextShape.SingleLine)?.Trim()` |
| `CaseService.SaveReportTextsAsync` | los 8 textos | `MultiLine`, **antes** de `CaseValidation.ValidateReportTexts`: se arma `request with { ObjetoInforme = SafeText.CleanOrNull(...), … }` y desde ese objeto se valida, se arma `ReportTexts` y se calcula el snapshot de versionado. `formato`, `trigger` y `restored_from` no se tocan. |
| `ExpertProfileService.SaveAsync` | `nombre`, `matricula`, `profesion`, `caracter` | `SafeText.Clean(x, SingleLine).Trim()` (reemplaza el `(x ?? "").Trim()` actual). `tratamiento` no: tiene valores cerrados. |
| `CatalogText.CleanValue` (catálogos e integrantes) | valor | `SafeText.Clean(s, SingleLine)` **antes** del reemplazo `\s+` → `" "` y el `Trim`. |
| `AccountFieldRules.Normalize` (panel de cuentas) | todos los campos de cuenta y `reason` | `SafeText.Clean(value, MultiLine).Trim()`. MultiLine conserva el comportamiento documentado ("los saltos de línea internos se conservan"). |
| `BrandingRules.Validate` (marca por cuenta) | `organization_name`, cada `contact_lines[i]` | `SafeText.StripInvisible(raw).Trim()` y después el `HasControlChars` actual (400 `MsgControlChars`, sin cambios). |
| `BrandingService` (marca de appsettings, tolerante) | `OrganizationName`, `ContactLines` | `SafeText.Clean(x, SingleLine).Trim()` antes del truncado actual. No es entrada de la API, pero va igual a la portada: se agrega por coherencia. |
| `AgentAuditController` (POST de eventos del agente) | `hostname`, `os_user`, `agent_version` | `SafeText.Clean(x, SingleLine)` |

**No se aplica** (igual que #63): contraseñas y DNI de login/cambio de contraseña, nombres de archivo, `source_path`,
manifiesto de evidencia y roles de captura (tienen validación de formato propia), `SupportService` y los textos por
defecto del informe (config; igual pasan por el render).

### 5.3 Render: `ReportValues.Clean` y su espejo en el cliente

```csharp
/// §7.2 + #62: primero SafeText (MultiLine: quita C0 salvo \t\n\r, bidi, no-caracteres y surrogates
/// sueltos; conserva DEL/C1), después \r\n → \n, \r → \n y \t → espacio.
public static string Clean(string? value)
{
    var s = SafeText.Clean(value, TextShape.MultiLine);
    if (s.Length == 0) return s;
    return s.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
}
```

El orden importa para la paridad. Primero se quita y después se normalizan los saltos: `"\r￾\n"` da `"\n"` (un
solo salto). Con los datos de antes de esta HU, el resultado es el mismo que hoy (no cambian F1-F17).

**Cliente, `client/src/lib/report-markdown.ts` → `cleanText(text)`:** hace lo mismo en el mismo orden, recorriendo por
code unit con detección de pares:
1. Recorre el string. Si en `i` hay un surrogate alto y en `i+1` uno bajo, arma el code point. Si es no-carácter
   (`(cp & 0xFFFE) === 0xFFFE`), descarta los dos; si no, los conserva. Un surrogate sin pareja se descarta.
2. Para un code point BMP: se descartan C0 salvo `\t \n \r`, U+202A-202E, U+2066-2069, U+FDD0-FDEF, U+FFFE y U+FFFF.
   DEL y C1 **se conservan**.
3. Después, `.replace(/\r\n?/g, "\n").replace(/\t/g, " ")`.

Se actualiza el JSDoc ("Limpieza igual a `ReportValues.Clean` + `SafeText`…"). No se agregan dependencias.
`plainToMarkdown` no cambia.

**Fixtures nuevos de paridad** (en `ReportMarkdownTests.Fixtures`, para `FromPlainText`). Los mismos vectores los
verifica el frontend con un script descartable (§11.2):

| Id | Entrada | Esperado |
|---|---|---|
| F18 | `"A￾B￿"` | `"AB"` |
| F19 | `"x‮evil‬y"` | `"xevily"` |
| F20 | `"a⁦b⁩c‎d"` | `"abc‎d"` (LRM se conserva) |
| F21 | `"lone\uD800 hi\uDC00 ok 😀"` | `"lone hi ok 😀"` (el par válido se conserva) |
| F22 | `"del\u007Fc1\u0085"` | `"del\u007Fc1\u0085"` (DEL/C1 se conservan) |
| F23 | `"\r￾\nfin"` | `"fin"` (salto de los extremos recortado, igual que F13) |
| F24 | `"﷐x\U0001FFFE"` (en C#, `"﷐x🿾"`) | `"x"` |

### 5.4 Diferencias con #63 (`Refactorizaciones/api-hardening.md`)

Gana la validación de #62. El orquestador tiene que corregir `api-hardening.md`:

1. **DEL/C1 (U+007F-U+009F).** #62 D1 los **conserva**. En #63 §4.8.1, la tabla dice "DEL y C1: se quita" y
   `IsStrippedControl` incluye DEL/C1. En #62, `IsStrippedControl` devuelve `true` solo para C0 (salvo `\t \n \r`). Hay
   que corregir la fila de la tabla, el comentario de la firma y §4.8.3 punto 4.
2. **Dueño de la escritura.** #63 §4.8.3 punto 1 ("#63 es dueña de todo el camino de escritura; #62 no agrega
   limpieza") queda invertido: **#62 implementa el helper y todos los puntos de §5.2**. #63 solo verifica y, si aparece
   un punto de entrada nuevo, lo agrega con este helper (ya está en la nota del orquestador de §4.8).
3. **`cleanText` del cliente.** #63 D-A4 decía "no se toca el cliente". #62 **sí** alinea `cleanText` (espejo del
   render, §5.3). `normalizeCatalogKey` (`client/src/lib/catalogs.ts`) y `admin-accounts.ts` siguen sin tocarse: la
   diferencia solo aparece si se pegan caracteres invisibles (D-A4 de #63 sigue valiendo para esos dos).
4. **Tests del helper.** `server/tests/Factum.Backend.Tests/SafeTextTests.cs` lo crea #62.

## 6. Generación del informe (backend)

### 6.1 Robustez de caracteres en el DOCX (H02, H04)

- Todos los caminos ya pasan por `ReportValues.Clean`, que con §5.3 alcanza para H02 y H04: placeholders,
  `ReplaceParagraphPerLine`, markdown (`ReportMarkdown.ParseWithSource`, `RenderCodeBlock`, `RenderLiteralLines`,
  `AppendText`), epígrafes, tabla de hashes y `{CONTACTO}`.
- **Entidades markdown:** Markdig decodifica `&#x202E;` y `&#xFFFE;` a esos caracteres **después** del primer `Clean`.
  Lo cubre el `Clean` de `AppendText` (sobre el literal ya decodificado). Hay que testearlo.
- **URL de enlaces** (`RenderLink`, `RenderAutolink`): si `SafeText.Clean(url, SingleLine) != url`, el enlace se
  renderiza como texto (sin `Hyperlink`), igual que una URL no permitida. Evita que un U+FFFE decodificado llegue al
  `.rels`.
- **Red de seguridad antes de `doc.Save()` (B-R9b, nuevo):** en todas las partes (cuerpo, headers y footers) se
  recorren `Text`, `FieldCode`, `DocProperties.Name/Description` y `NonVisualDrawingProperties.Name`, y se les aplica
  `SafeText.StripInvisible` más la quita de C0 salvo `\t \n \r`. Si algo cambió, va un `LogWarning` sin el texto
  (solo la parte y el tipo de elemento). Con datos de una plantilla válida no cambia nada; está para que **nunca** vuelva
  a fallar `Save` por un camino nuevo que se olvide de `Clean`.
- Árabe y hebreo sin controles salen igual que hoy: ningún carácter de esas escrituras está en la lista.

### 6.2 Imágenes chicas a 96 dpi como máximo (H07, D4 A)

`BuildImageParagraphForRel(..., bool capAtNative)`:

```csharp
private const long EmuPerPixel96 = 9_525; // 914 400 EMU por pulgada / 96
// fit actual → (dispW, dispH); si capAtNative y dispW > imgW*9525, se usa (imgW*9525, imgH*9525).
```

- Se compara con el ancho nativo. Como la proporción se mantiene, alcanza con ese chequeo: si el fit amplía, se usa el
  tamaño nativo a 96 dpi.
- `capAtNative = true` en las capturas: identificación (`{capturasImeiModelo}`, `{capturasNombreDispositivo}`), anexo y
  cuerpo (B-R7b). `false` en logo, isotipo y sello (`ReplaceImagePlaceholder`, atribución), que mantienen su caja:
  **decisión del arquitecto DA1**, porque la HU solo habla de capturas. `BuildImageParagraph` recibe el parámetro con
  default `false`.
- Ejemplo: 200x150 px → 1 905 000 x 1 428 750 EMU (5,29 x 3,97 cm). 1080x2400 → igual que hoy (manda la caja).
- El pPr no cambia: centrado, `keepNext` y epígrafe.

### 6.3 Sistema operativo idempotente (H03, D2 A)

`Models/DeviceInfo.cs`:

```csharp
public string OsLabel
{
    get
    {
        var prefix = Platform == "ios" ? "iOS" : "Android";
        var v = (OsVersion ?? "").Trim();
        if (v.Length == 0) return $"{prefix} {AndroidVersion}";          // igual que hoy
        // Ya trae el prefijo ("Android 11", "android 11", "iOS 17.4", "Android"): tal cual.
        if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            (v.Length == prefix.Length || !char.IsLetter(v[prefix.Length])))
            return v;
        return $"{prefix} {v}";
    }
}
```

- La regla de "no seguido de una letra" evita tratar como prefijo un `"iOSX"` hipotético. `"iPadOS 17"` en iOS sale
  `"iOS iPadOS 17"`, como hoy, porque no está en el alcance.
- No se migra Mongo ni el texto que ya quedó escrito en `operaciones_realizadas` (D2 A, **sin script**).
- `OsLabel` es un getter público, así que `System.Text.Json` lo serializa como `os_label` en el JSON del caso. Cambia su
  **valor** (sin duplicar), no su nombre. Ni `client/` ni `agent-ui/` lo leen (verificado con grep), así que no hay
  contrato que tocar.

### 6.4 Portada en una página (H05, D3 A)

**Mediciones (renders con LibreOffice de la v6 actual, llenada por script en una carpeta temporal fuera del repo, logo
simulado de 4,5 x 2,5 cm).** Hoy:

| Caso | Págs. de portada |
|---|---|
| carátula 500 + marca normal (logo, nombre 26, 2 líneas de contacto de 40) | 1 |
| carátula 500 + marca al máximo (nombre 150, 6 líneas de 150) **sin** logo | 1 |
| carátula 200 + marca al máximo **con** logo | 1 |
| carátula 300 + marca al máximo con logo | **2** |
| carátula 500 + marca al máximo con logo (H05) | **2**, y sigue en 2 con la carátula a 10, 9,5 o 9 pt o con el contacto unido en 1-2 líneas |
| lo anterior + espaciadores de portada 1800→600 y 1650→600 twips | 1 (incluso a 11 pt) |
| **todo al máximo**: tipo de causa 300 + número 300 + perito 150 + carátula 500 + marca al máximo con logo | 2 incluso compactado a 600/600 y 9 pt; **1** con espaciadores 240/240, carátula 9 pt y contacto unido con " · " a 8 pt |

El membrete del pie de la portada (`footer2`) llega a ~7 cm con la marca al máximo y le come la altura al cuerpo. Por
eso solo con la fuente de la carátula no alcanza. **Decisión del arquitecto DA3:** la política "portada siempre en una
página" de D3 A se implementa con **tres niveles de densidad**, elegidos por una estimación pura de altura. La fuente de
la carátula baja por tramos, como pide la HU, pero se suma compactar los espacios verticales de la portada y, solo en el
nivel 2, el membrete. Con datos normales (nivel 0) la portada queda **idéntica a hoy**.

| Nivel | Espaciador superior (`w:line` exacto del 1er párrafo) | `w:after` del subtítulo | Carátula (ficha) | Contacto del membrete (`{CONTACTO}` en `footer2`) |
|---|---|---|---|---|
| 0 (hoy) | 1800 | 1650 | 11 pt (`sz 22`) | una línea por renglón (`<w:br/>`), 9 pt |
| 1 | 600 | 600 | 10 pt (`sz 20`) | igual que hoy |
| 2 | 240 | 240 | 9 pt (`sz 18`) | todas las líneas unidas con `" · "` en un solo renglón (que envuelve), 8 pt (`sz 16`) |

**Elección del nivel (`Services/Reports/ReportCoverLayout.cs`, nuevo, `internal static`, puro):**

```csharp
internal sealed record CoverInput(string CausaRow, string Caratula, string PeritoRow,
    string OrganizationName, IReadOnlyList<string> ContactLines, bool HasLogo);
internal static int ChooseLevel(CoverInput c);   // 0, 1 o 2
internal static int EstimateTwips(CoverInput c, int level);
```

Estimación en twips (las constantes son las iniciales y se calibran en §11.3; cada una se declara como `const` con
comentario):
- Ficha: para cada fila, `ceil(len / cpl) * lineH`. Valores: carátula con `cpl = 62/68/76` y `lineH = 290/264/238`
  según el nivel; causa (`"{tipo} N° {num}"`) y perito (`"{nombre} · M.P. {mat}"`) siempre con `cpl 62`, `lineH 290`.
  `len` cuenta las comillas.
- Membrete: si no hay nombre, contacto ni logo, vale 0. Si no, `60 + 242 * ceil(org / cplOrg) + Σ contacto`. El ancho de
  celda depende de si hay logo: `cplOrg` es 52 con logo y 78 sin; `cplContact` es 67 con logo y 100 sin. El contacto en
  niveles 0-1 suma `207 * ceil(len_i / cplContact)` por línea; en el nivel 2 es `184 * ceil(len(join " · ") / (cplContact * 1.12))`.
  Con logo se toma `max(membrete, 1418)`.
- Capacidad: nivel 0 si `total ≤ 6000`. Si no, nivel 1 si `total(nivel 1) ≤ 7800`. Si no, nivel 2, que es el último:
  si aun así se estima que no entra, va igual un `LogWarning` y no se trunca nada.
- Puntos de anclaje medidos: la carátula 500 + marca al máximo con logo da ~7700 en nivel 0 (pasa a 1, entra). La
  carátula 200 + marca al máximo con logo da ~6250 (pasa a 1: está en el borde y por eso se elige el lado seguro). Todo
  al máximo pasa a nivel 2. La carátula 500 + marca normal da ~4600 (nivel 0, sin cambios).

**Cómo lo aplica `ReportService`:** paso **B-R2c** nuevo, después de B-R2b y antes de B-R4, solo si `level > 0`. Las
anclas se ubican así, sin placeholders nuevos:
- La **sección de portada** son los hijos del body hasta el primer párrafo con `sectPr` en su `pPr`.
- El **espaciador** es el primer párrafo de la portada con `SpacingBetweenLines.Line == "1800"` y `LineRule == Exact`.
- El **subtítulo** es el párrafo de la portada cuyo texto es `"Inspección técnica de dispositivo móvil"`: se cambia
  su `SpacingBetweenLines.After`.
- La **carátula** es el párrafo de la primera tabla de la portada cuyo texto contiene `{caratula}`: se cambian `FontSize`
  y `FontSizeComplexScript` de todos sus runs y de la marca de párrafo.
- El **contacto (nivel 2)**: `ReplaceMultilinePlaceholder` recibe `lines = [string.Join(" · ", ContactLines)]` **solo
  en la parte `footer2`** (la que contiene `{#MEMBRETE}`), y el run queda en `sz 16`. El header no lleva `{CONTACTO}`.
- Si no se encuentra un ancla, va un `LogWarning` y se sigue: la plantilla es la versionada y `--check` de v6
  garantiza las anclas (§7.2).
- El membrete aparece una sola vez porque la portada no pasa a una segunda página. El cuerpo empieza en la página 2
  (sección `nextPage`, sin cambios).

### 6.5 Encabezado interior de 2 líneas como máximo (H06, D3 A)

En B-R6, los **headers** (`HeaderPart`) usan una copia de `values` con recortes. Footers y cuerpo usan los valores
completos, así que el nombre completo sigue en el membrete de la portada:
- `{ORGANIZACION}`: hasta **60** elementos de texto (`StringInfo`, sin partir pares surrogate ni secuencias ZWJ). Si
  pasa, se cortan 59, se hace `TrimEnd` y se agrega `"…"`.
- `{tipoCausa}` + `{numeroCausa}`: si la suma pasa de **70**, `tipoCausa` se recorta a 30 (con "…" si hacía falta) y
  `numeroCausa` a `70 - len(tipoCausa)` (con "…").
- Calibración: la celda derecha (5,7 cm, Arial 8 pt) mostró 80 caracteres en 2 líneas en LibreOffice. 60 deja margen
  para mayúsculas y para las métricas de Word. **Decisión del arquitecto DA4** (recortar también la referencia de la
  causa, porque el escenario de la HU combina organización larga y número largo). Los valores se ajustan en §11.3 si el
  render lo pide.
- Helper puro `ReportValues.Ellipsize(string value, int maxTextElements)`.

### 6.6 Frases armadas (H13, H14)

- **H13:** `["{marcaModeloDispositivo}"] = OrNoInformado(MarcaModelo(cas.Device))`. El token `marcaModeloDispositivo`
  de los textos por defecto **no** cambia (va en medio de una oración que el perito edita).
- **H14, en `RewriteParagraph`** (B-R6), con una regla general y local al placeholder. La "vista" es el texto completo
  **original** del párrafo (`full`), calculado antes de los reemplazos, como hoy:
  1. Si el valor termina en `.` y el carácter siguiente al match en `full` es `.`, se consume también ese `.` (el match
     se extiende un carácter; el recorte entre `w:t` ya maneja `remaining`). Ejemplo: "…, Lic.**.**" queda "…, Lic.".
     Con "Lic..." queda "Lic...".
  2. Si el valor es `""`, el carácter anterior al match es un espacio y el siguiente también, se consume el espacio
     siguiente. Ejemplo: "causa  N°" queda "causa N°".
- Las dos reglas se aplican solo a `{…}` reemplazados en B-R6, no a los textos del perito (ya están en `resolved`).
  Hoy H14-b es inalcanzable por API (`tipo_causa` es obligatorio), pero queda cubierto.

### 6.7 Lo que no cambia

- El renderer markdown: la sangría de 4 cm de primera línea se aplica también a respuestas de una línea (D5 A,
  validado). Títulos y tablas markdown no se tocan (D6 A).
- "la causa {tipoCausa} N°" (R2) queda como está.
- Tabla de hashes, numeración, colores de marca, atribución y fechas siguen igual.

## 7. Plantillas (v4 → v6)

### 7.1 `build_plantilla_v4.py` (contenido pericial)

| Cambio | Dónde | Detalle |
|---|---|---|
| H10 | `new["5FECA2F2"]` | `"En fecha {fechaInspeccion}, siendo las {horaInspeccion} horas, se procedió a la inspección técnica del dispositivo:"` (la huella `startswith("En fecha")` no cambia). |
| H12 / D5 A | `new["543E58FA"] = ""` (el párrafo se elimina) | Huella nueva en `fingerprint`: `"543E58FA": lambda t: t == "Se constató:"`. |
| H11 | `7290E48F` y `516E02D3` | `rebuild(..., ppr=norm_ppr)` (primera línea de 4 cm, `firstLine=2268`, igual que el resto de las descripciones). |
| H11 | `05004875` ("Desde su generación…") | Se cambia solo el pPr y se conservan los runs: `open_tag(p) + norm_ppr + "".join(runs_of(p)) + "</w:p>"`. Huella nueva: `startswith("Desde su generación")`. |

`--check` de v4 no cambia: el conjunto de placeholders es el mismo. Se regenera la v4 desde
`docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx` (no versionada; está en esta máquina) y después la v6.

### 7.2 `build_plantilla_v6.py` (diseño)

| Cambio | Detalle |
|---|---|
| **H01, firma indivisible** | Constante `FIRMA_BLOCK = ["4FC2AE2A", "29CDCD40", "21DB9123", "38A8E121", "3EAB1E3F", "6AA98C58", "4F0EBB3D"]`, con huellas: "SE DEJA CONSTANCIA…", vacío, vacío, `"______________________________"`, `"{nombrePerito}"`, `"{profesionPerito} – M.P. {matriculaPerito}"` y `"{caracterPerito}"`. Con `set_children(..., PPR_ORDER, {"w:keepNext": "<w:keepNext/>", "w:keepLines": "<w:keepLines/>"})` se agrega `keepNext` a **los 7**, `keepLines` a `4FC2AE2A`, `3EAB1E3F`, `6AA98C58` y `4F0EBB3D` (los que pueden envolver), y `keepNext` al filete `cierre:fil` (`rule_ppr(..., keep=True)`), así el isotipo queda con el bloque. El párrafo del isotipo no lleva `keepNext`. Sin isotipo (bloque removido en runtime), el filete se encadena con el salto del anexo o con `a4:fin`. Es inocuo. |
| **H16, idioma** | `rPrDefault` + `<w:lang w:val="es-AR" w:eastAsia="en-US" w:bidi="ar-SA"/>` (con `set_children(RPR_ORDER)`). Todo `<w:lang w:val="en-US"/>` de `document.xml` (24 marcas de párrafo) y de `styles.xml` (estilos "Table Grid" y "Tabla con cuadrícula1") pasa a `<w:lang w:val="es-AR"/>`. `settings.xml` ya tiene `themeFontLang es-AR`. |
| **H17, orden de esquema de `styles.xml`** | En `build_styles`, cada `<w:style>` reordena sus hijos según la secuencia de `CT_Style`: `name, aliases, basedOn, next, link, autoRedefine, hidden, uiPriority, semiHidden, unhideWhenUsed, qFormat, locked, personal, personalCompose, personalReply, rsid, pPr, rPr, tblPr, trPr, tcPr, tblStylePr`. Hoy hay 29 de 38 estilos fuera de orden: coincide con los 29 errores del QA. Es **seguro**: se reordenan hermanos sin cambiar ningún valor, y Word ya tolera los dos órdenes. Un hijo desconocido aborta con `FALLA: ancla styles`. |
| `--check` (nuevos) | (a) `FIRMA_BLOCK` con `keepNext` y los 4 con `keepLines`; (b) ningún `w:lang w:val="en-US"` en ninguna parte y `rPrDefault` con es-AR; (c) todos los `w:style` en orden de esquema; (d) anclas de portada de §6.4: 1 párrafo con `w:line="1800" w:lineRule="exact"` en la portada, el subtítulo con `w:after="1650"` y `{caratula}` en la ficha con `sz 22`; (e) `{CONTACTO}` en `footer2` dentro del bloque `MEMBRETE`; (f) que no quede "Se constató:" ni "siendo horas". |

El ZIP sigue siendo determinista: la misma v4 da el mismo SHA-256 de la v6. `README.md` de `ops/plantilla`: se agrega
una sección "v6: arreglos de #62" con la lista de arriba.

## 8. Contrato compartido

No cambia ningún campo JSON (snake_case_lower, `JsonNamingPolicy.SnakeCaseLower` en `Program.cs`). Lo que cruza lados
es **comportamiento**:

| Qué | Servidor | Cliente | Nota |
|---|---|---|---|
| Limpieza de texto del informe (paridad byte a byte) | `server/src/Factum.Backend/Services/Reports/ReportValues.cs` (`Clean`) + `server/src/Factum.Backend/Common/SafeText.cs` | `client/src/lib/report-markdown.ts` (`cleanText`) | Reglas exactas en §5.1 y §5.3; vectores F18-F24. |
| Textos guardados limpios | `PUT /api/cases/{id}/report-texts` devuelve los campos `objeto_informe` … `reserva` ya limpios | `ReportStep.tsx` no cambia: conserva su estado local y el próximo guardado vuelve a limpiar. El versionado deduplica sobre lo limpio. | Sin bucle de guardado: el cliente no compara contra la respuesta. |
| `os_label` (getter de `DeviceInfo`, serializado en el JSON del caso) | `server/src/Factum.Backend/Models/DeviceInfo.cs` | Nadie lo lee (`client/`, `agent-ui/`) | Cambia el valor ("Android 14"), no el nombre. |
| `os_version` que manda el cliente al crear | igual | `client/src/app/dashboard/page.tsx:411-413` **no cambia** (D2 A) | El servidor tolera "Android 11" y "11". |

## 9. Decisiones técnicas

| Id | Decisión | ¿Usuario? |
|---|---|---|
| D1 | Adoptar #63 §4.8 completo (helper, reglas, puntos de entrada), con #62 como dueña y DEL/C1 conservados. | No (lo pidió el orquestador; validación D1 A). |
| D2 | `ReportValues.Clean` = `SafeText.Clean(MultiLine)` y después saltos y tabs, en ese orden. | No |
| D3 | Red de seguridad B-R9b antes de `Save`. | No |
| D4 | El enlace markdown con una URL que cambia al limpiarla se renderiza como texto. | No |
| D5 | Frases fijas y sangrías en la v4; firma, idioma y orden de estilos en la v6. | No |
| DA1 | El tope de 96 dpi solo aplica a capturas, no a logo, isotipo ni sello. | Decisión del arquitecto (conservadora) |
| DA2 | `BrandingService` (appsettings) también limpia con SafeText. | Decisión del arquitecto |
| DA3 | Portada con 3 niveles de densidad (espacios + carátula + membrete en el extremo) en vez de solo la fuente de la carátula; el nivel 0 queda idéntico a hoy. | Decisión del arquitecto (lo exigen los renders; cumple D3 A) |
| DA4 | El encabezado interior recorta también tipo y número de causa (70 en total), además de la organización (60). | Decisión del arquitecto |
| DA5 | H14 se resuelve con reglas generales en `RewriteParagraph` (punto doble y doble espacio junto a un placeholder) y no cambiando la frase de la plantilla. | Decisión del arquitecto |
| DA6 | El token `marcaModeloDispositivo` de los textos por defecto no pasa a "No informado"; solo cambia el placeholder de la ficha. | Decisión del arquitecto |
| DA7 | `CatalogText.CleanValue` pasa por SafeText (adoptado de #63). Cambia la clave de dedupe solo para valores con caracteres invisibles. | Decisión del arquitecto |

Ninguna bloquea la implementación.

## 10. Checklist atómico

### 10.1 `implementer-backend`

**Helper y entrada**
- [ ] Crear `server/src/Factum.Backend/Common/SafeText.cs` con la API exacta de §5.1 (DEL/C1 conservados).
- [ ] `CaseValidation.CaseFields.From` (×2): `C()` con SafeText SingleLine + Trim; `observaciones` con MultiLine.
- [ ] `CatalogText.CleanValue`: SafeText SingleLine antes del `\s+`.
- [ ] `CaseService.CreateAsync`: los campos de `device` según §5.2.
- [ ] `CaseService.UpdateAsync`: `imei`.
- [ ] `CaseService.SaveReportTextsAsync`: `request with { …8 textos limpios… }` antes de validar; todo lo que sigue usa el request limpio.
- [ ] `ExpertProfileService.SaveAsync`: los 4 campos.
- [ ] `AccountFieldRules.Normalize`: MultiLine + Trim.
- [ ] `BrandingRules.Validate`: `StripInvisible` + Trim antes de `HasControlChars` (el 400 no cambia).
- [ ] `BrandingService`: SafeText SingleLine en nombre y contacto antes de truncar.
- [ ] `AgentAuditController`: `hostname`, `os_user`, `agent_version`.

**Render**
- [ ] `ReportValues.Clean` según §5.3.
- [ ] `ReportValues.Placeholders`: `{marcaModeloDispositivo}` con `OrNoInformado`.
- [ ] `ReportValues.Ellipsize` + valores recortados solo para `HeaderPart` en B-R6 (§6.5).
- [ ] `RewriteParagraph`: reglas H14 (§6.6), con la vista del texto original.
- [ ] `ReportMarkdownRenderer.RenderLink`/`RenderAutolink`: guarda de URL (§6.1).
- [ ] B-R9b antes de `doc.Save()` (§6.1).
- [ ] `BuildImageParagraphForRel(..., capAtNative)`: `true` en `ReplaceWithImages` (identificación y anexo) e `InsertBodyImages`; `false` en logo, isotipo y sello.
- [ ] `DeviceInfo.OsLabel` idempotente (§6.3).
- [ ] `Services/Reports/ReportCoverLayout.cs` (puro) + paso B-R2c en `GenerateDocxAsync` + contacto unido en `footer2` en el nivel 2 (§6.4).

**Plantillas**
- [ ] `build_plantilla_v4.py`: H10, H12, H11 y las huellas nuevas (§7.1).
- [ ] `build_plantilla_v6.py`: H01, H16, H17 y el `--check` ampliado (§7.2).
- [ ] Regenerar: `python3 ops/plantilla/build_plantilla_v4.py "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" server/src/Factum.Backend/Templates/plantilla_informe_v4.docx`, después `build_plantilla_v6.py` (v4 → v6). Las dos con `--check` en `OK`. Correr dos veces y verificar el mismo SHA-256 (determinismo).
- [ ] `ops/plantilla/README.md`: sección de #62.

**Tests (`server/tests/Factum.Backend.Tests`, xUnit, carpeta temporal por test, sin Mongo ni Storage)**
- [ ] `SafeTextTests.cs`: cada fila de la tabla §5.1 en las dos formas; `StripInvisible` conserva C0; idempotencia (`Clean(Clean(x)) == Clean(x)`); pares surrogate válidos, sueltos al principio y al final; LRM/RLM, ZWJ, NBSP, U+FEFF, U+0085 y U+007F se conservan; no-caracteres de plano 1 y 16.
- [ ] `ReportMarkdownTests.cs`: agregar F18-F24 a `Fixtures` (T1 y T2 los recorren solos).
- [ ] `ReportFormatFixesTests.cs` (generando con `ReportTestSupport.Generate` y la v6):
  - [ ] H02: carátula `"A￾ B"`, resultados markdown con `￿` y `&#xFFFE;`, integrante con `\uD800` suelto, contacto de la marca con `￿` → se genera, ninguna parte XML contiene U+FFFE, U+FFFF ni surrogates sueltos, y el texto restante está ("A B").
  - [ ] H04: carátula `"‮evil‬"`, integrante con U+2066..U+2069 y `&#x202E;` en markdown → ninguna parte contiene U+202A-202E ni U+2066-2069, y "evil" aparece en portada, referencia y cuerpo.
  - [ ] Hebreo y árabe sin controles se conservan tal cual.
  - [ ] Enlace markdown `[x](https://a.com/&#xFFFE;)` → sin `Hyperlink` y sin falla.
  - [ ] H01: los 7 párrafos del bloque de firma tienen `KeepNext` y los 4 de §7.2 `KeepLines` en el DOCX generado.
  - [ ] H10: el texto contiene "siendo las 13:30 horas" (zona UTC del fake) y no "siendo horas".
  - [ ] H12: no hay párrafo "Se constató:".
  - [ ] H11: los párrafos de Notas técnicas, Reserva y "Desde su generación…" tienen `Indentation.FirstLine == "2268"` y no tienen `Left`.
  - [ ] H13: `Manufacturer = Model = ""` → la ficha dice "No informado" en "Marca y modelo".
  - [ ] H14: profesión "Lic." → no hay ".." en ningún párrafo. Con `TipoCausa = ""` (caso armado a mano) no hay "  " en la frase "Que vengo…" ni en el encabezado.
  - [ ] H16: `styles.xml` tiene `rPrDefault` con `w:lang` es-AR y ninguna parte contiene `en-US`.
  - [ ] H17: `OpenXmlValidator(FileFormatVersions.Microsoft365)` sobre el DOCX generado con marca y logo da **0 errores** en `/word/styles.xml` y `/word/document.xml`. `Validez_SinErroresNuevosRespectoDeLaV4` sigue en verde.
  - [ ] H07: anexo con 200x150 → extent 1 905 000 x 1 428 750; 1080x2400 → igual que hoy (manda la caja); captura del cuerpo de 200x150 → nativo; logo de 140x76 → igual que hoy (caja).
  - [ ] H06: organización de 150 → el `{ORGANIZACION}` de `header1` tiene ≤ 60 elementos y termina en "…"; el `footer2` tiene los 150; número de causa de 300 → la referencia del header cumple el tope de 70.
  - [ ] OsLabel: `("android","Android 11")` → "Android 11"; `("android","android 11")` → "android 11"; `("android","11")` → "Android 11"; `("android","",14)` → "Android 14"; `("ios","17.4")` → "iOS 17.4"; `("ios","iOS 17.4")` → "iOS 17.4". `RenderDefaults` con `"Android 11"` no contiene "Android Android".
- [ ] `ReportCoverLayoutTests.cs`: niveles para los anclajes de §6.4 (normal → 0; carátula 500 + marca al máximo con logo → 1; todo al máximo → 2; carátula 500 + marca normal → 0); además, el DOCX en nivel 1 y 2 tiene el espaciador, el subtítulo, el `sz` de la carátula y (en el nivel 2) el contacto unido con `sz 16`.
- [ ] `ReportBodyImagesDocxTests.cs:177`: el 90x30 ahora espera el tamaño nativo (857 250 EMU de ancho) y la proporción 3.0 sigue igual.
- [ ] `ReportQaRegressionTests.cs` (§11.1).
- [ ] Endpoints de entrada (sin Mongo, sobre las funciones puras): `CaseFields.From` con `"A￾‮ B\n"` → `"A B"`; `BrandingRules.Validate` con `"‮Org"` → `"Org"` sin error y con `"Org\u0001"` → `MsgControlChars`; `CatalogText.CleanValue("a⁦b")` → `"ab"`.

### 10.2 `implementer-frontend` (solo `client/`)

- [ ] `client/src/lib/report-markdown.ts`: reescribir `cleanText` según §5.3 y actualizar el JSDoc.
- [ ] No tocar `plainToMarkdown`, `catalogs.ts`, `admin-accounts.ts` ni `dashboard/page.tsx`.
- [ ] Skills obligatorias de AGENTS.md (`ui-ux-pro-max`, `senior-frontend`, `3d-web-experience`, `web-design-guidelines`): invocarlas y dejar en el progress "sin hallazgos aplicables" (sin cambios visuales).

## 11. Verificación

### 11.1 Repetición de los 11 informes del QA (automatizada: `ReportQaRegressionTests.cs`)

Un test por caso, con datos ficticios armados en el test (`Case` en memoria, imágenes de `TestImages`, carpeta temporal
propia, sin Mongo ni Storage) y la v6 real. Asserts mínimos en todos: se genera, todas las partes XML parsean
(`XDocument.Load` de cada `.xml` del ZIP), no hay caracteres de §5.1 ni `en-US`, y `OpenXmlValidator` (Microsoft365)
da 0 errores en `document.xml` y `styles.xml`.

| Caso | Datos (de `informe-formato.md`) | Asserts propios |
|---|---|---|
| `c1_tipico` | completo, 3 integrantes, Sala, logo e isotipo, capturas IMEI y nombre, 3 de anexo y una en el cuerpo, textos por defecto | portada nivel 0; numeración de figuras; firma con `keepNext` |
| `c2r_minimo_real` | solo obligatorios, sin marca ni marca/modelo | "Marca y modelo: No informado"; sin títulos de Objeto, Notas ni Reserva |
| `c2_minimo` / `c2b_vacio_total` | obligatorios vacíos (defensivo, generador llamado directo) | no lanza; sin "  " por placeholders vacíos |
| `c3_extremos` | carátula 500 con emoji, RTL, U+202E, `<b>x</b>`, `<script>`, comillas; parte 300; palabra de 150; 11 integrantes; U+0001/0007/000B/007F/0085; tab; marca 150 + 6x150; markdown completo | sin bidi; U+007F/U+0085 presentes (se conservan); portada nivel 2 o 1 según la estimación; header recortado |
| `c3b_textoplano` | `Formato = null` | un párrafo por línea, asteriscos literales |
| `c3c_html_crudo` | HTML crudo en markdown | sale literal |
| `c3x_fffe_caratula`, `c3x_d800`, `c3x_ffff_markdown` | los tres que hoy lanzan excepción | **se generan** (H02) |
| `c4_capturas` | 34 de anexo (1080x2400, 2400x1080, 1000x1000, 200x150, 4000x3000, 1080x6000, 3000x300, 720x1600, 1440x3200, 150x900; PNG y JPG), 2 de identificación, 5 en el cuerpo | 200x150 y 150x900 nativos; el resto igual que la caja de hoy |
| `c5` / `c5b` / `c5c` | 29/02/2024; creación 2025-01-01 02:30 UTC con zona AR; fecha inválida; dd/MM | fechas como en el QA (con un `IReportSettings` fake con zona `America/Argentina/Buenos_Aires`) |

**Renders de regresión (opcionales en CI, obligatorios para el implementador antes de `done`):** si
`FACTUM_RENDER_DIR` apunta a una carpeta **fuera del repo**, el mismo test copia ahí cada DOCX. Si además
`FACTUM_SOFFICE=1` y `soffice` está en el PATH, convierte cada uno a PDF y verifica las páginas contando
`/Type /Page` en el PDF:
- `c3_extremos` y `cover_max_logo` (carátula 500 + marca al máximo con logo; caso extra de §6.4): el texto
  "INFORME PERICIAL TÉCNICO INFORMÁTICO" (título del escrito) está en la página 2, o sea, la portada ocupa 1 página.
- `cover_all_max` (tipo 300 + número 300 + perito 150 + carátula 500 + marca al máximo con logo): ídem.
- `c2r_minimo_real`: la firma ("______________________________", el nombre y el carácter) queda en una sola página
  (`pdftotext -f N -l N` si está disponible; si no, se omite con un mensaje en la salida del test).

### 11.2 Antes de declararse `done`

**implementer-backend**
```bash
python3 ops/plantilla/build_plantilla_v4.py --check "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" server/src/Factum.Backend/Templates/plantilla_informe_v4.docx
python3 ops/plantilla/build_plantilla_v6.py --check server/src/Factum.Backend/Templates/plantilla_informe_v4.docx server/src/Factum.Backend/Templates/plantilla_informe_v6.docx
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test server/tests/Factum.Backend.Tests
FACTUM_RENDER_DIR=<carpeta-temporal-fuera-del-repo> FACTUM_SOFFICE=1 dotnet test server/tests/Factum.Backend.Tests --filter "FullyQualifiedName~ReportQaRegression|FullyQualifiedName~Muestras"
```
Además: `pdftoppm -r 60 -png` de las páginas de portada, de la página de la firma de `c2r` y de la página 2 (header)
de `c3`, mirarlas y dejar las rutas y lo observado en `progress/impl_backend_informe-formato-fixes.md`. Las capturas
quedan fuera del repo. Hay que abrir al menos `c1_tipico`, `c3_extremos` y `cover_all_max` en **WPS Office** (instalado
en esta máquina) como prueba de compatibilidad con Word, y anotarlo. La apertura en Word queda para el usuario (§11.4).

**implementer-frontend**
```bash
cd client && npx tsc --noEmit && npx eslint src/lib/report-markdown.ts
```
Además, un script descartable (fuera del repo, p. ej. con `npx tsx` en la carpeta temporal importando
`client/src/lib/report-markdown.ts`) que compruebe `plainToMarkdown` con F1-F24 (las salidas esperadas de la tabla de
`ReportMarkdownTests.Fixtures`). Pegar el resultado en `progress/impl_frontend_informe-formato-fixes.md`.

### 11.3 Calibración de §6.4 y §6.5 (backend)

Si algún render de §11.1 falla (portada en 2 páginas o header en 3 líneas), se ajustan **solo** las constantes de
`ReportCoverLayout` (bajar los umbrales 6000/7800, que es el lado seguro) o los topes 60/70 de §6.5, y se anota el
valor final con el render que lo justifica. No se cambian los niveles ni se agregan otros sin avisar al orquestador.

### 11.4 Prueba manual para el usuario (después del review)

1. Generar el informe de un caso Android real desde la web: "Operaciones realizadas" con los defaults dice "Android NN"
   una sola vez. Un caso viejo con "Android Android" ya escrito queda así hasta que el perito lo corrija o recargue los
   defaults (D2 A, sin script).
2. Abrir el DOCX en **Microsoft Word**: abre sin reparar; la firma queda junta; el idioma de revisión es "Español
   (Argentina)" y no hay subrayado masivo; "siendo las HH:mm horas"; sin "Se constató:" fijo; Notas y Reserva con la
   misma sangría que el resto.
3. Pegar en la carátula un texto con caracteres invisibles (p. ej. copiado de un PDF del sistema judicial), guardar y
   generar: no hay error.
4. Con una marca de nombre largo, mirar el encabezado de las páginas interiores (2 líneas como máximo, con "…") y la
   portada (en una página, con el nombre completo).

### 11.5 Regla de datos (AGENTS.md), para todos los implementadores

- No se modifican ni se borran documentos de negocio preexistentes de la MongoDB de desarrollo. Esta HU **no** escribe
  en la base: todos los tests son en memoria o sobre carpetas temporales propias.
- Si alguna prueba manual del implementador crea un caso, borra **solo por los `_id` que insertó**, nunca con filtros
  amplios.
- No se toca `Storage:DataDirectory`: los DOCX y PDF de prueba van a carpetas temporales fuera del repo.
- No se regeneran informes ya emitidos ni se migran `os_version` ni textos guardados.
- No mover tarjetas del Project ni tocar `progress/sesiones/`.

## 12. Archivos

| Archivo | Cambio |
|---|---|
| `server/src/Factum.Backend/Common/SafeText.cs` | nuevo |
| `server/src/Factum.Backend/Services/Reports/ReportValues.cs` | `Clean`, `Ellipsize`, `OrNoInformado` de marca y modelo |
| `server/src/Factum.Backend/Services/Reports/ReportService.cs` | B-R2c, valores de header, H14 en `RewriteParagraph`, `capAtNative`, B-R9b |
| `server/src/Factum.Backend/Services/Reports/ReportCoverLayout.cs` | nuevo |
| `server/src/Factum.Backend/Services/Reports/ReportMarkdownRenderer.cs` | guarda de URL |
| `server/src/Factum.Backend/Models/DeviceInfo.cs` | `OsLabel` |
| `server/src/Factum.Backend/Services/Cases/{CaseValidation,CaseService}.cs`, `Services/Profile/ExpertProfileService.cs`, `Services/Catalogs/CatalogText.cs`, `Services/Admin/AccountFieldRules.cs`, `Services/Branding/{BrandingRules,BrandingService}.cs`, `Controllers/AgentAuditController.cs` | entrada (§5.2) |
| `server/src/Factum.Backend/Templates/plantilla_informe_v{4,6}.docx` | regeneradas |
| `ops/plantilla/build_plantilla_v{4,6}.py`, `ops/plantilla/README.md` | §7 |
| `server/tests/Factum.Backend.Tests/{SafeTextTests,ReportFormatFixesTests,ReportCoverLayoutTests,ReportQaRegressionTests}.cs` | nuevos |
| `server/tests/Factum.Backend.Tests/{ReportMarkdownTests,ReportBodyImagesDocxTests}.cs` | ajustes |
| `client/src/lib/report-markdown.ts` | `cleanText` |
