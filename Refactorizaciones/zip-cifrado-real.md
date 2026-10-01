# SDD: Cifrado real del ZIP de evidencia

**Slug:** `zip-cifrado-real`
**HU:** `docs/hu-zip-cifrado-real.md`. Validada el 2026-10-01 en modo autónomo, con las
recomendadas de D1 a D11.
**Base de código:** `f3471e7` (informe pericial commiteado), **más** lo que deje la HU
`auth-e-integraciones-sin-mpf` cuando se mergee. Ver §9, "Archivos compartidos con la HU en curso".

---

## 1. Resumen funcional

El ZIP de evidencia que arma `ReportService` pasa a escribirse **cifrado con AES-256 (WinZip
AE-2)** con la contraseña que ya genera Factum. Para eso se usa `SharpZipLib 1.4.2`. El hash
informado (tabla del DOCX, `zip_hash`, pantallas) es el SHA-256 del **ZIP cifrado final**.
Antes de borrar los archivos sueltos, el backend reabre el ZIP en solo lectura con la
contraseña y compara el SHA-256 de cada entrada con el del original. Si algo falla, la
generación aborta, los sueltos se conservan y se borran el ZIP y el DOCX de ese intento. El
caso registra `zip_encrypted` (y `zip_encryption = "aes256-ae2"`). La contraseña deja de
viajar en las respuestas del `Case` y se recupera solo desde un endpoint dedicado del dueño.
Un interruptor global, `Report:EncryptZip` (default `true`), permite generar sin cifrar. La
config pública expone `encrypt_zip` para que el wizard sepa qué prometer. El texto por
defecto de aseguramiento menciona el cifrado cuando está activo. La UI (`GenerateStep`,
`ResultStep`, `CaseCard`, `/design-system`) solo dice "cifrado" cuando corresponde. Los
casos viejos no se tocan.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí** | Librería, `ReportService` + nuevo `EvidenceZip`, `Case`, DTOs, repositorio, `CaseService`, `CasesController`, `ConfigController`, config `Report`, textos por defecto, README/AGENTS.md. |
| backend tests `server/tests/Factum.Backend.Tests` | **sí (nuevo)** | Proyecto xUnit chico para el ZIP (§8, DT6). |
| backend (Tatana) `server/src/Factum.Agent` | **no** | — |
| client `client/` | **sí** | `api.ts`, `usePublicConfig.ts`, `GenerateStep`, `ResultStep`, `CaseCard`, `dashboard/page.tsx`, `DesignSystemShowcase`, nuevo `CopyButton`. |
| agent-ui `agent-ui/` | **no** | — |

---

## 3. Librería: escritura de ZIP AES-256

### 3.1 Evaluación (verificada el 2026-10-01 contra nuget.org y con un spike)

| Librería | Última versión | Licencia | CVEs en la última versión | ¿Escribe AES-256 (AE-2)? | Veredicto |
|---|---|---|---|---|---|
| `System.IO.Compression` (BCL) | .NET 10 | MIT | — | **No** (no cifra) | Descartada |
| `SharpCompress` | (transitiva 0.30.1 vía MongoDB.Driver) | MIT | NU1902 en 0.30.1 | **No**: lee ZIP AES, pero no lo escribe | Descartada |
| `DotNetZip` | 1.16.0 (2021-11) | Ms-PL | **GHSA-xhg6-9j5j-w4vf** (severidad 2 = moderate). Marcado **deprecated (Legacy)** en NuGet | Sí | Descartada: abandonada, con CVE, sumaría NU1902 |
| `ProDotNetZip` (fork de DotNetZip) | 1.20.0 (2024-12) | Ms-PL (archivo de licencia) | Ninguno reportado | Sí | No hace falta: fork de un solo mantenedor, de un código base con historial de CVEs |
| **`SharpZipLib`** | **1.4.2** (2023-01-30) | **MIT** | **Ninguno** (los GHSA de ≤1.3.2 están corregidos desde 1.3.3) | **Sí**: `ZipOutputStream.Password` + `ZipEntry.AESKeySize = 256` | **Elegida** |

Datos de `SharpZipLib 1.4.2` (registro de NuGet): target `net6.0` **sin dependencias**
transitivas (en `netstandard2.0` suma `System.Memory`, pero no aplica a net10.0). La versión
1.3.3 ya cerró GHSA-m22m-h4rf-pwq3, GHSA-2x7h-96h5-rq84 y GHSA-mm6g-mmq6-53ff. Las tres son
de extracción (Zip Slip / Tar), no de escritura.

### 3.2 Spike (scratchpad, fuera del repo, .NET 10 + SharpZipLib 1.4.2)

Se escribió un ZIP con `ZipOutputStream { Password, UseZip64 = Dynamic }` y
`ZipEntry { AESKeySize = 256, IsUnicodeText = true, Size = <largo> }`. Archivos: tres PNG
aleatorios, uno vacío y uno con nombre `device_pull_canción ñ.txt`. Resultado:

- **Directorio central:** método `99` y extra field `0x9901` con **vendor version 2 (AE-2)**,
  strength `3` (AES-256) y CRC `0` (AE-2 no guarda CRC). Confirmado parseando los bytes.
- **`ZipFile` + `Password` (misma librería):** abre, `IsCrypted = true` y
  `AESKeySize = 256` en cada entrada, y el SHA-256 de cada entrada descifrada coincide con el
  original. El archivo vacío también funciona.
- **Contraseña incorrecta:** `ZipException: Invalid password for AES`.
- **El SHA-256 del ZIP no cambia** después de leerlo con `ZipFile` sobre un stream de solo
  lectura.
- **`bsdtar` (libarchive, viene con macOS):** con `--passphrase <ok>` extrae todo (exit 0) y
  los SHA-256 coinciden, incluido el nombre con tildes. Con una contraseña incorrecta sale con
  **exit 1** ("Incorrect passphrase"). **Ojo:** igual deja archivos del tamaño correcto con
  contenido basura. La prueba tiene que mirar el exit code y los hashes, no si los archivos
  existen. Sin `--passphrase` y sin TTY, **pide la contraseña en loop**: siempre se corre con
  `</dev/null`.
- **Zip64:** con `UseZip64.On`, `bsdtar` también abre el ZIP (extra field `0x0001`). No se
  probó un ZIP real de más de 4 GB: el disco de la máquina del spike tenía 4,6 GB libres. Ver
  la prueba manual M7.
- `dotnet build` con 0 advertencias. `dotnet list package --vulnerable` no reporta paquetes
  vulnerables.

### 3.3 Reglas de uso (obligatorias para el implementador)

1. `zos.Password` se fija **antes** del primer `PutNextEntry`.
2. Cada entrada lleva `AESKeySize = 256`. **Si se pone `Password` sin `AESKeySize`,
   SharpZipLib escribe ZipCrypto (roto, D1-B).** La verificación de §5.3 lo detecta y corta la
   generación.
3. `UseZip64 = UseZip64.Dynamic` y `entry.Size = new FileInfo(path).Length` **siempre**. Con
   `Dynamic`, la decisión de usar Zip64 por entrada depende de que `Size` se conozca antes de
   escribir. Sin `Size`, una grabación de más de 4 GB rompe.
4. `entry.IsUnicodeText = true` (bit 11, nombres UTF-8: los `device_pull_*` pueden traer
   tildes).
5. Para leer se usa `ZipFile` (con acceso aleatorio), **no** `ZipInputStream`: este último no
   soporta AES.

---

## 4. Modelo de datos (Mongo, colección `cases`)

Los nombres de elemento BSON son los de C# (PascalCase, no hay `ConventionRegistry`).
`factum_dev` hoy tiene `cases` vacía (inspección de solo lectura del 2026-10-01). Igual se
diseña para documentos viejos.

| Campo C# | BSON | Tipo | Default | Nuevo | Notas |
|---|---|---|---|---|---|
| `ZipEncrypted` | `ZipEncrypted` | `bool` | `false` | **sí** | Lo fija `UpdateGeneratedAsync`. Si falta en un documento viejo, deserializa `false` (la clase ya tiene `[BsonIgnoreExtraElements]` y el default de C# cubre la ausencia). |
| `ZipEncryption` | `ZipEncryption` | `string?` | `null` | **sí** | `"aes256-ae2"` si `ZipEncrypted`; `null` si no. Deja asentado el algoritmo si después cambia (DT2). |
| `ZipPassword` | `ZipPassword` | `string?` | `null` | no | Se sigue guardando en claro (D3-B). Con `EncryptZip=false` queda `null`. **`[JsonIgnore]`**: no sale en ningún JSON de `Case`. Tampoco viene en la proyección del listado. |
| `ZipHash` | `ZipHash` | `string?` | — | no | SHA-256 del ZIP **cifrado** final (D5-A). |

- **Sin migración ni índices nuevos.** Ningún documento preexistente se reescribe. El único
  `$set` nuevo es el de `UpdateGeneratedAsync` sobre el caso que se está generando.
- **Casos viejos (D8-A):** sin `ZipEncrypted`, o con `false`, se leen como "sin cifrar". Su
  `ZipPassword` sigue en el documento, pero la API ya no lo devuelve: `[JsonIgnore]` y el
  endpoint dedicado lo rechaza si `!ZipEncrypted`.

---

## 5. Backend: flujo y endpoints

### 5.1 Config (D9-B)

`Report:EncryptZip` (bool, default `true`) va en `ReportOptions`. `IReportSettings` suma
`bool EncryptZip { get; }`. `ReportSettings` lo copia tal cual y, si es `false`, loguea **al
arrancar** `LogWarning("Report: EncryptZip=false; los ZIP de evidencia se generan SIN cifrar")`.
En `appsettings.json` se agrega `"EncryptZip": true` dentro de `"Report"`. La variable de
entorno es `Report__EncryptZip`. **`Program.cs` no cambia:** `ReportSettings` ya es singleton
y ya se resuelve al arrancar (L107/L179).

### 5.2 Contraseña

- **Generación:** sigue igual, `ReportService.GeneratePassword()` (16 caracteres de
  `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, `RandomNumberGenerator`, unos 80 bits; D10-A). Como
  32 divide 256, `bytes[i] % 32` no tiene sesgo. **Solo se llama si `EncryptZip`.** Si no,
  `password = null`.
- **Guardado (D3-B):** `Case.ZipPassword` en claro, solo si se cifró.
- **Salida por API:** (a) `GenerateResponse.password` en el `POST …/generate`, una sola vez;
  (b) `GET /api/cases/{id}/zip-password`, bajo pedido (§5.5). En ningún otro lado.
- **Nunca en logs ni en mensajes de error:** ningún `Log*` recibe la contraseña ni un objeto
  que la contenga. Los mensajes de las excepciones de §5.3 se arman sin ella (los de
  SharpZipLib tampoco la incluyen: "Invalid password for AES"). No hay `UseHttpLogging` en
  `Program.cs`. No agregarlo.
- **Nunca en el DOCX (D4-A):** la plantilla v4 no tiene `{CLAVE}` y `ReportService` no la
  escribe. No cambia.

### 5.3 Nuevo `Services/Reports/EvidenceZip.cs` (`internal static class`)

Se extrae del `ReportService` para poder testearlo sin Mongo ni plantilla.

```text
internal static class EvidenceZip
{
    public const string EncryptionAes256Ae2 = "aes256-ae2";

    // Escribe el ZIP (cifrado si password != null) con FileMode.CreateNew.
    static Task WriteAsync(string zipPath, IReadOnlyList<string> filePaths, string? password, CancellationToken ct);

    // Abre el ZIP en SOLO LECTURA (FileStream FileMode.Open, FileAccess.Read, FileShare.Read)
    // con ZipFile y verifica. Lanza EvidenceZipVerificationException si algo no cuadra.
    static Task VerifyAsync(string zipPath, IReadOnlyDictionary<string,string> expectedSha256ByName,
                            string? password, CancellationToken ct);
}

internal sealed class EvidenceZipVerificationException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);
```

**`WriteAsync`:** sigue las reglas de §3.3. Usa `SetLevel(6)` (equivale al `Optimal` de hoy).
`entry.DateTime` es la hora de generación (como hoy). Copia con
`await src.CopyToAsync(zos, ct)` y cierra con `zos.Finish()`. El `FileStream` lo cierra el
propio método y no deja el archivo abierto. Si `password is null`, no fija `Password` ni
`AESKeySize`: queda un ZIP sin cifrar, con el mismo escritor (DT3).

**`VerifyAsync`** falla, con un mensaje en castellano y **sin la contraseña**, si pasa
cualquiera de estas cosas:
1. no se puede abrir el ZIP, o la contraseña no sirve (`ZipException` envuelta);
2. `zf.Count != expected.Count`;
3. una entrada no es archivo, tiene un nombre que no está en `expected` o está repetida;
4. con `password != null`: alguna entrada con `!IsCrypted` o `AESKeySize != 256` (impide
   caer en silencio a ZipCrypto o a un ZIP en claro). Con `password == null`: alguna entrada
   con `IsCrypted`;
5. el SHA-256 de `zf.GetInputStream(e)` (`SHA256.HashDataAsync(stream, ct)`, hex en
   minúsculas) no es igual en ordinal a `expected[e.Name]`.

Mensajes sugeridos: `"No se pudo verificar el ZIP de evidencia: la contraseña no lo abre"`,
`"No se pudo verificar el ZIP de evidencia: el archivo <nombre> no coincide con su hash"`,
`"No se pudo verificar el ZIP de evidencia: falta el archivo <nombre>"`,
`"No se pudo verificar el ZIP de evidencia: <nombre> no está cifrado con AES-256"`.

`Factum.Backend.csproj`: `<InternalsVisibleTo Include="Factum.Backend.Tests" />` (ver B1).

### 5.4 `ReportService.GenerateAsync` (flujo D10 + D5/D6)

Nuevo orden. Los cambios respecto de §7.1 de `informe-pericial-de-parte` están en **negrita**:

1. Limpieza de restos (igual).
2. Evidencia ordenada (igual). `evidencePaths` = los que existen (igual).
3. **`password = settings.EncryptZip ? GeneratePassword() : null`.** `hashes =
   BuildHashesAsync(...)` (igual).
4. **`try {`**
5. **`EvidenceZip.WriteAsync(zipPath, evidencePaths, password, ct)`.**
6. `zipHash = Sha256Async(zipPath)`: sobre el ZIP **cifrado** final (D5-A).
7. **`EvidenceZip.VerifyAsync(zipPath, expected, password, ct)`**, donde `expected` es
   `hashes` restringido a los nombres de `evidencePaths` (son los SHA-256 calculados **antes**
   de zipear, los mismos de la tabla). Como abre en solo lectura, el hash del paso 6 no cambia.
8. DOCX con `zipHash` (igual). `reportHash` (igual).
9. **`} catch { borrar zipPath y docxPath si existen (solo esos dos, los de este intento);
   throw; }`**. Los archivos sueltos **nunca** se tocan en el catch.
10. Recién acá: borrar los archivos sueltos (igual que hoy, después del `try`).
11. `return new ReportResult(..., ZipHash: zipHash, Password: password, ZipEncrypted:
    password is not null, ZipEncryption: password is null ? null :
    EvidenceZip.EncryptionAes256Ae2, ...)`.

- **`ReportResult`** pasa a ser `(string ZipPath, string ZipFilename, string ZipHash, string?
  Password, bool ZipEncrypted, string? ZipEncryption, string PdfPath, string PdfFilename,
  string ReportHash)`.
- Se borran `CreateZipAsync` y el `using System.IO.Compression` si queda sin uso.
- **Sal aleatoria (nota técnica):** AES-ZIP usa sal y IV aleatorios por entrada. El mismo
  contenido da otro `zipHash` en cada generación. El caso no se regenera (`Completed` → 409),
  así que no tiene efecto. Dejarlo en un comentario sobre el paso 6.
- **Costo de D6:** se relee y descifra toda la evidencia una vez. Con videos grandes son
  algunos segundos más. El request de `generate` ya es síncrono y largo.
- **Mensaje al perito:** `CaseService` ya devuelve `"Error generando informe: {ex.Message}"`
  y pone el caso en `Error`. Con la excepción de §5.3, el perito ve "Error generando informe:
  No se pudo verificar el ZIP de evidencia: …". Es el camino de error que pide la UX de la HU.
  El caso en `Error` se puede reintentar, porque los sueltos siguen ahí.

### 5.5 Endpoints

| Método y ruta | Auth | Entrada | Respuesta | Cambio |
|---|---|---|---|---|
| `GET /api/cases` | JWT | — | `{ cases: Case[] }` | `Case` **sin `zip_password`**, **con `zip_encrypted`** y `zip_encryption`. La proyección de Mongo suma `.Exclude(c => c.ZipPassword)` (defensa en profundidad). |
| `GET /api/cases/{id}` | JWT | — | `{ cas: Case, files }` | Igual: sin `zip_password`, con `zip_encrypted`/`zip_encryption`. |
| `POST /api/cases`, `PUT /api/cases/{id}` | JWT | igual | `Case` | Igual (por `[JsonIgnore]`). |
| `POST /api/cases/{id}/generate` | JWT | — | `GenerateResponse` | `password` pasa a `string \| null` (`null` si no se cifró). `case.zip_encrypted` ya viene fijado. |
| **`GET /api/cases/{id}/zip-password`** | JWT | — | **200** `{ "password": "ABCD…" }` | **Nuevo.** Header `Cache-Control: no-store`. |
| `GET /api/config/public` | anónimo | — | `PublicConfigResponse` | **Suma `encrypt_zip: bool`** (= `IReportSettings.EncryptZip`). |

**`GET /api/cases/{id}/zip-password`:**
- `CaseService.GetZipPasswordAsync(id, officerDni, ct)` usa `LoadOwnedAsync`.
- Caso inexistente → **404** `{ error }` (`Result.NotFound`).
- Caso de otro perito → **lo mismo que hoy da un caso ajeno** con `LoadOwnedAsync`:
  `Result.Forbidden` → `Forbid()` → **403 sin cuerpo**. La HU dice "404", pero también
  dice "la misma respuesta que hoy". Ver **P1**.
- Caso propio con `Status != Completed`, `!ZipEncrypted` o `ZipPassword` vacío → **404**
  `{ "error": "Este caso no tiene un ZIP cifrado" }`. Cubre los casos viejos (D8) y los
  generados con el cifrado apagado.
- OK → `Result.Ok(new ZipPasswordResponse(cas.ZipPassword!))`.
- El controlador pone `Response.Headers.CacheControl = "no-store"` antes del `Ok`.
- No se loguea nada con la contraseña. Auditar quién la revela queda fuera de alcance.

### 5.6 Texto por defecto de aseguramiento (D2-B + D7-A)

En `ReportDefaultTexts` el constante actual se renombra a `AseguramientoEvidenciaSinCifrar`
(**mismo texto, sin cambios**) y se agrega `AseguramientoEvidenciaCifrado`:

> Cada archivo obtenido durante la inspección fue identificado con un nombre único y se le
> calculó su valor hash mediante el algoritmo SHA-256. Los archivos se agruparon en un
> contenedor ZIP cifrado con el algoritmo AES-256 (formato WinZip AE-2) y protegido con una
> contraseña generada aleatoriamente para este caso, que no figura en el presente informe y
> se entrega por un canal separado. Antes de eliminar las copias de trabajo, se verificó que
> el contenedor cifrado se abre con esa contraseña y que cada archivo contenido conserva su
> valor hash original. El valor hash SHA-256 del contenedor se calculó sobre el archivo
> cifrado, una vez cerrado, por lo que puede verificarse sin necesidad de la contraseña. El
> contenedor se abre con herramientas de compresión de uso habitual como 7-Zip, WinRAR, Keka
> o The Unarchiver; algunos exploradores de archivos integrados en el sistema operativo no
> admiten este tipo de cifrado. Los valores obtenidos se detallan en la tabla siguiente y
> permiten verificar, en cualquier momento posterior, que la evidencia no fue alterada.

(Sin `{tokens}`: el renderer lo deja tal cual. El texto va en una sola línea en el `const`,
como los demás.)

`ReportSettings`:
`DefaultAseguramientoEvidencia = Pick(d.AseguramientoEvidencia, o.EncryptZip ?
ReportDefaultTexts.AseguramientoEvidenciaCifrado :
ReportDefaultTexts.AseguramientoEvidenciaSinCifrar)`. El override
`Report:DefaultTexts:AseguramientoEvidencia` **gana en los dos modos** (DT5): si el estudio
lo define, es responsable de que hable del cifrado. Va en el README.

Limitación (de la HU): los casos que ya guardaron `ReportTexts` conservan los suyos. Los
textos se resuelven al pedir `…/report-texts/defaults`.

---

## 6. Contrato compartido (JSON snake_case_lower)

La política está en `Program.cs`: `JsonNamingPolicy.SnakeCaseLower` en `AddControllers().
AddJsonOptions` y en `ConfigureHttpJsonOptions`. Los enums también van en snake_case_lower.

| JSON (exacto) | Tipo JSON | C# (archivo → miembro) | TS (archivo → miembro) | Notas |
|---|---|---|---|---|
| `zip_encrypted` | `boolean` | `Models/Case.cs` → `Case.ZipEncrypted` (`bool`, default `false`) | `client/src/lib/api.ts` → `Case.zip_encrypted?: boolean` | Siempre presente en respuestas nuevas. El TS lo marca opcional por robustez. **Solo `=== true` es "cifrado".** |
| `zip_encryption` | `string \| null` | `Models/Case.cs` → `Case.ZipEncryption` (`string?`) | `client/src/lib/api.ts` → `Case.zip_encryption?: string \| null` | `"aes256-ae2"` o `null`. El cliente no lo usa para decidir. |
| `zip_password` | — (**deja de existir** en `Case`) | `Models/Case.cs` → `Case.ZipPassword` con `[JsonIgnore]` (el BSON no cambia) | `client/src/lib/api.ts` → **se borra** `Case.zip_password` | `CaseCard` deja de leerlo. |
| `password` (en `POST /api/cases/{id}/generate`) | `string \| null` | `DTOs/CaseDtos.cs` → `GenerateResponse.Password` (`string?`) | `client/src/lib/api.ts` → `generateCase(): { …; password: string \| null }` | `null` si no se cifró. |
| `password` (en `GET /api/cases/{id}/zip-password`) | `string` | `DTOs/CaseDtos.cs` → **nuevo** `public sealed record ZipPasswordResponse(string Password);` | `client/src/lib/api.ts` → **nuevo** `getZipPassword(caseId: string): Promise<{ password: string }>` | Ruta exacta: `/api/cases/${caseId}/zip-password`, método `GET`. |
| `encrypt_zip` | `boolean` | `DTOs/ConfigDtos.cs` → `PublicConfigResponse.EncryptZip` (`bool`, nuevo 4.º parámetro posicional) | `client/src/lib/api.ts` → `PublicConfig.encrypt_zip: boolean`; `client/src/hooks/usePublicConfig.ts` → `PublicClientConfig.encryptZip: boolean` (`cfg.encrypt_zip === true`; `EMPTY.encryptZip = false`) | Un backend viejo sin el campo = `false` (texto neutro). |

`client/src/types/index.ts` reexporta `Case`/`PublicConfig` desde `api.ts`. **No hace falta
tocarlo.**

---

## 7. Decisiones técnicas

- **DT1. SharpZipLib 1.4.2** (MIT, sin CVEs, sin dependencias en net6+) como escritor y
  lector de ZIP para el ZIP de evidencia (§3). Versión fija, no flotante.
- **DT2. Se agrega `ZipEncryption`** además del bool. Cuesta un campo y deja asentado el
  algoritmo si mañana cambia (p. ej., 7z, D1-C). La UI decide **solo** con `zip_encrypted`.
- **DT3. Un solo escritor para los dos modos:** con `EncryptZip=false` también se usa
  SharpZipLib (sin `Password`). `System.IO.Compression` sale del flujo. La verificación de
  D6 corre **también sin cifrado**: es barata y protege igual el borrado de los sueltos.
- **DT4. Orden: escribir → hash → verificar → DOCX.** Se verifica antes del DOCX para fallar
  rápido y no generar un informe con el hash de un ZIP inválido. El hash se calcula antes de
  verificar. La verificación abre en `FileAccess.Read`, así que no puede alterar el archivo.
- **DT5. El override `Report:DefaultTexts:AseguramientoEvidencia` aplica en los dos modos.**
  No se suma una segunda clave `…SinCifrar` porque es configuración de más para un caso raro.
- **DT6. Se crea `server/tests/Factum.Backend.Tests`** (xUnit) solo para `EvidenceZip`. Es la
  ruta donde un bug deja la evidencia irrecuperable. Merece un test de ida y vuelta que no
  dependa de Mongo, de la plantilla ni de un celular (§8).
- **DT7. `[JsonIgnore]` en `Case.ZipPassword`** en vez de un DTO de salida: el `Case` se
  serializa directo en 5 endpoints y el atributo los cubre a todos. La exclusión en la
  proyección del listado suma defensa en profundidad.
- **DT8. Caso ajeno en `zip-password` → mismo resultado que `LoadOwnedAsync` (403).**
  Necesita validación: ver P1.
- **DT9. El texto de aseguramiento cifrado de §5.6** lo revisa el usuario en la prueba manual
  (lo pide D7). Ver P2.

### Decisiones pendientes (modo autónomo: se aplica la recomendada salvo que el usuario diga otra cosa)

- **P1. Respuesta a un caso ajeno en `GET …/zip-password`.** La HU dice "la misma respuesta
  que hoy da un caso ajeno (404)", pero hoy un caso ajeno da **403** (`LoadOwnedAsync` →
  `Result.Forbidden` → `Forbid()`), no 404.
  - **A) 403, igual que el resto de los endpoints del caso (recomendada).** Coherente y sin
    código especial. Revela que el id existe, igual que hoy `GET /api/cases/{id}`.
  - B) 404 solo en este endpoint, para no revelar que el id existe. Es inconsistente con el
    resto: si se quiere, se cambia `LoadOwnedAsync` para todos en otra HU.
- **P2. Redacción del texto de aseguramiento cifrado (§5.6).**
  - **A) El texto propuesto, tal cual (recomendada).** El usuario lo revisa en la prueba
    manual M5 y, si quiere otra redacción, se ajusta la constante (o la pone en
    `Report:DefaultTexts:AseguramientoEvidencia`).
  - B) Una versión corta, sin la frase de compatibilidad ni la de verificación.

---

## 8. Checklist atómico

### implementer-backend (`server/`)

> **Regla dura de datos:** no se borra ni se modifica ningún documento de `cases` ni ningún
> archivo de `Storage:DataDirectory` preexistente. Las pruebas que generen casos limpian
> **solo por los `_id` que insertaron**. Los casos viejos no se migran.
> No tocar `backlog.json` ni `progress/current.md`. Resultado completo en
> `progress/impl_backend_zip-cifrado-real.md`.

- [ ] **B1.** `Factum.Backend.csproj`: `<PackageReference Include="SharpZipLib" Version="1.4.2" />`
  e `<ItemGroup><InternalsVisibleTo Include="Factum.Backend.Tests" /></ItemGroup>`.
- [ ] **B2.** `ReportOptions`: `public bool EncryptZip { get; set; } = true;` con un
  `<summary>`.
- [ ] **B3.** `IReportSettings` + `ReportSettings`: `bool EncryptZip`. Warning al arrancar si
  es `false`. `DefaultAseguramientoEvidencia` elige según §5.6.
- [ ] **B4.** `ReportDefaultTexts`: renombrar `AseguramientoEvidencia` →
  `AseguramientoEvidenciaSinCifrar` (sin cambiar el texto) y agregar
  `AseguramientoEvidenciaCifrado` con el texto de §5.6. Actualizar las referencias.
- [ ] **B5.** `appsettings.json`: `"EncryptZip": true` en `"Report"`.
- [ ] **B6.** `Models/Case.cs`: `public bool ZipEncrypted { get; set; }`, `public string?
  ZipEncryption { get; set; }` y `[JsonIgnore]` en `ZipPassword` (con un comentario: "se
  recupera solo por GET …/zip-password").
- [ ] **B7.** Nuevo `Services/Reports/EvidenceZip.cs` con `WriteAsync`, `VerifyAsync`,
  `EncryptionAes256Ae2` y `EvidenceZipVerificationException` (§5.3, reglas de §3.3).
- [ ] **B8.** `ReportService`: nuevo `ReportResult` (§5.4). `GenerateAsync` con el orden de
  §5.4, try/catch de limpieza (solo el ZIP y el DOCX de este intento) y borrado de los sueltos
  después del `try`. Se borra `CreateZipAsync`. Comentario sobre la sal aleatoria.
- [ ] **B9.** `ICaseRepository`/`CaseRepository.UpdateGeneratedAsync`: firma `(…, string?
  zipPassword, bool zipEncrypted, string? zipEncryption, …)`. `$set` de `ZipPassword`,
  `ZipEncrypted` y `ZipEncryption`. `ListByOfficerAsync`: proyección
  `Exclude(ReportTexts).Exclude(ZipPassword)`.
- [ ] **B10.** `DTOs/CaseDtos.cs`: `GenerateResponse.Password` → `string?`. Nuevo `record
  ZipPasswordResponse(string Password)`.
- [ ] **B11.** `CaseService`: `GenerateAsync` pasa `ZipEncrypted`/`ZipEncryption`/`Password`.
  Nuevo `GetZipPasswordAsync` en la interfaz y en la implementación (§5.5).
- [ ] **B12.** `CasesController`: `[HttpGet("{id}/zip-password")]` con `Cache-Control:
  no-store`, `ProducesResponseType<ZipPasswordResponse>(200)`, `403` y `404`.
- [ ] **B13.** `ConfigDtos.PublicConfigResponse`: suma `bool EncryptZip` (al final) y
  actualiza el `<summary>` con `encrypt_zip`. `ConfigController`: inyecta `IReportSettings` y
  pasa `report.EncryptZip`. **Partir de la versión de la HU `auth-e-integraciones-sin-mpf`**
  (ya tiene `SupportSettings`/`support_enabled`).
- [ ] **B14.** Revisar que ningún `Log*` ni ningún mensaje de excepción nuevo incluya la
  contraseña (`grep -n "password\|Password" Services Controllers`).
- [ ] **B15.** Proyecto `server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj`
  (`net10.0`, `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.5`,
  `Microsoft.NET.Test.Sdk 18.10.1`, `ProjectReference` a `../../src/Factum.Backend`). Tests
  sobre `EvidenceZip`, cada uno en su propio `Path.GetTempPath()/<guid>` y borrado al final:
  - `RoundTrip_Encrypted`: escribe tres archivos aleatorios, uno vacío y uno con nombre
    `device_pull_canción ñ.txt`; `VerifyAsync` con la contraseña no lanza. Cada entrada
    tiene `IsCrypted` y `AESKeySize == 256`, y el extra field `0x9901` tiene vendor
    version 2.
  - `Verify_WrongPassword_Throws` (`EvidenceZipVerificationException`).
  - `Verify_HashMismatch_Throws` (se altera un valor de `expected`).
  - `Verify_MissingOrExtraEntry_Throws`.
  - `Verify_EncryptedExpected_ButPlain_Throws` (ZIP escrito con `password: null` y
    verificado con contraseña).
  - `RoundTrip_Plain` (`password: null`).
  - `ZipHash_Unchanged_ByVerify`: SHA-256 del ZIP antes y después de `VerifyAsync`.
  - `Bsdtar_OpensWithPassword_FailsWithout`: solo si existe `/usr/bin/bsdtar` (si no, el test
    termina sin aserciones y lo dice por `ITestOutputHelper`). Corre `bsdtar --passphrase
    <ok> -xf zip -C dirA` con stdin cerrado: exit 0 y los hashes coinciden. Corre con
    `--passphrase WRONG…`: exit ≠ 0. **No** corre sin `--passphrase` (pide en loop).
- [ ] **B16.** `README.md` (sobre la versión de la HU de auth, que también lo toca): L6 y L51
  ("ZIP cifrado" pasa a ser cierto; aclarar "cifrado AES-256"). L477: "el ZIP final queda
  cifrado con AES-256 y su contraseña se entrega por separado". Fila nueva
  `Report:EncryptZip` en la tabla de "Informe pericial: configuración", con la nota de DT5
  sobre el override de aseguramiento. Párrafo **Hashes** (~L364): el hash es el del ZIP
  cifrado, se puede verificar sin la contraseña, se verifica el ZIP antes de borrar los
  sueltos y la sal aleatoria hace que el hash no sea reproducible. Agregar el endpoint
  `zip-password` donde se listan los endpoints, si los hay. `AGENTS.md` L15: "informes (DOCX +
  ZIP cifrado AES-256)".

### implementer-frontend (`client/` solamente; `agent-ui/` **no** se toca)

> Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`,
> `3d-web-experience` (como criterio, sin 3D) y `web-design-guidelines` (autochequeo final).
> Leer `client/AGENTS.md`. No tocar `backlog.json` ni `progress/current.md`. Resultado
> completo en `progress/impl_frontend_zip-cifrado-real.md`. **Depende del backend** (§6):
> arrancar cuando B6/B10–B13 estén hechos, o trabajar contra el contrato de §6.

- [ ] **F1.** `client/src/lib/api.ts` (versión de la HU de auth): en `Case`, borrar
  `zip_password` y agregar `zip_encrypted?: boolean` y `zip_encryption?: string | null`;
  `generateCase` → `password: string | null`; nuevo `getZipPassword(caseId)` →
  `request<{ password: string }>(\`/api/cases/${caseId}/zip-password\`)`;
  `PublicConfig.encrypt_zip: boolean` (con JSDoc).
- [ ] **F2.** `client/src/hooks/usePublicConfig.ts`: `encryptZip` en `PublicClientConfig`,
  `EMPTY` (`false`) y en el mapeo (`cfg.encrypt_zip === true`).
- [ ] **F3.** Nuevo `client/src/components/ui/CopyButton.tsx`: botón "Copiar" con
  `navigator.clipboard.writeText`. Feedback "Copiada" durante unos 2 s en una región
  `aria-live="polite"`, `aria-label` con contexto ("Copiar contraseña del ZIP"). Si el
  portapapeles falla, el texto queda seleccionable (`select-all`) y se avisa "No se pudo
  copiar".
- [ ] **F4.** `GenerateStep.tsx`: usa `usePublicConfig()`. Garantía "ZIP cifrado con
  AES-256 y contraseña única" (ícono `Lock`) solo si `encryptZip`. Si no, "ZIP de evidencia
  con hash SHA-256 verificable" (ícono neutro, p. ej. `Archive`).
- [ ] **F5.** `ResultStep.tsx`: props `password: string | null` y `encrypted: boolean`. Si
  `encrypted && password`: bloque "Contraseña del ZIP" en monoespaciada + `CopyButton` + el
  texto "Sin esta contraseña la evidencia no se puede abrir. Entregala por un canal distinto
  al del ZIP (no en el mismo correo ni en el mismo pendrive).". Nota de compatibilidad
  junto a "Descargar ZIP": "Cifrado AES-256. Se abre con 7-Zip o WinRAR (Windows) y con Keka
  o The Unarchiver (macOS). El Explorador de Windows y la Utilidad de Archivo de macOS no lo
  abren.". El check "ZIP cifrado con AES-256" se suma a la lista. Si `!encrypted`: sin
  bloque, sin nota y sin ese check.
- [ ] **F6.** `app/dashboard/page.tsx` (versión de la HU de auth): `result` suma `encrypted:
  res.case.zip_encrypted === true` y `password: res.password` (`string | null`). Se pasan a
  `ResultStep`.
- [ ] **F7.** `CaseCard.tsx`: no lee más `zip_password`. Si `cas.zip_encrypted === true`:
  botón "ZIP cifrado" y una fila "Contraseña ZIP" con el botón **Mostrar contraseña**. Al
  tocarlo, `api.getZipPassword(cas.id)` con estado de carga (`aria-busy`, botón
  deshabilitado). Si sale bien, muestra la contraseña en monoespaciada + `CopyButton`. Si
  falla, error en línea "No se pudo obtener la contraseña. Probá de nuevo." y el botón vuelve
  a estar disponible. La contraseña vive en el estado local del card y se descarta al
  desmontarlo. Si no: botón "ZIP de evidencia" y una nota tenue "Este ZIP se generó sin
  cifrar". El hash se muestra igual que hoy.
- [ ] **F8.** `DesignSystemShowcase.tsx` L125 → "Informe Word + ZIP de evidencia". L428 →
  "El informe y el ZIP de evidencia quedaron guardados.".
- [ ] **F9.** `grep -rn "zip_password\|ZIP cifrado\|cifrad" client/src`: solo quedan las
  apariciones condicionadas por `encryptZip`/`zip_encrypted`.

---

## 9. Archivos compartidos con la HU en curso (`auth-e-integraciones-sin-mpf`)

Esa HU está sin commitear en `feat/auth-e-integraciones-sin-mpf`. Según "Ramas de HU
encadenadas" (`AGENTS.md`), **esta HU sale de esa rama una vez commiteada** (o de `develop`
si ya se mergeó). Los implementadores editan sobre esa versión, nunca sobre `f3471e7`.

| Archivo | Esta HU | Esa HU |
|---|---|---|
| `server/src/Factum.Backend/Program.cs` | **No lo toca** (§5.1) | Lo modifica |
| `server/src/Factum.Backend/appsettings.json` | `Report.EncryptZip` | Lo modifica (auth/integraciones) |
| `server/src/Factum.Backend/Controllers/ConfigController.cs` | Inyecta `IReportSettings`, `encrypt_zip` | Agregó `SupportSettings`/`support_enabled` |
| `server/src/Factum.Backend/DTOs/ConfigDtos.cs` | `EncryptZip` | Agregó `SupportEnabled` |
| `client/src/lib/api.ts` | `Case`, `generateCase`, `getZipPassword`, `PublicConfig` | Lo modifica |
| `client/src/hooks/usePublicConfig.ts` | `encryptZip` | Agregó `supportEnabled` |
| `client/src/app/dashboard/page.tsx` | `result.encrypted` | Lo modifica |
| `client/src/types/index.ts` | No lo toca | Lo modifica |
| `README.md` | §8 B16 | Lo modifica |

---

## 10. Verificación

### implementer-backend (antes de `done`)

```bash
dotnet build server/src/Factum.Backend/Factum.Backend.csproj        # 0 errores; sin NU19xx nuevos (los de SharpCompress/Snappier transitivos ya existían)
dotnet list server/src/Factum.Backend/Factum.Backend.csproj package --vulnerable --include-transitive   # SharpZipLib no aparece
dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj   # todos en verde, incluido el de bsdtar en macOS
```

Prueba de humo con `bsdtar` sobre un ZIP real, si el implementador puede levantar el backend
con un caso de prueba **propio** (y limpia solo ese `_id` y su carpeta):

```bash
bsdtar --passphrase "$PW" -xf evidencia_X.zip -C /tmp/ok </dev/null; echo $?    # 0; los shasum -a 256 coinciden con la tabla del DOCX
bsdtar --passphrase WRONGWRONGWRONG1 -xf evidencia_X.zip -C /tmp/ko </dev/null; echo $?   # 1 ("Incorrect passphrase")
shasum -a 256 evidencia_X.zip    # = zip_hash del caso = fila del ZIP en el DOCX
```

(Si hay 7-Zip: `7zz t -p"$PW" evidencia_X.zip` → "Everything is Ok"; `7zz t -pWRONG
evidencia_X.zip` → "Wrong password". 7-Zip no viene con macOS: `brew install sevenzip`.)

### implementer-frontend (antes de `done`)

```bash
cd client && npx tsc --noEmit
```

Más una pasada visual en `npm run dev` de `GenerateStep`, `ResultStep`, `CaseCard`
(cifrado / sin cifrar / error de `zip-password`) y `/design-system`.

### Prueba manual (usuario)

- **M1. Cifrado activo (default).** Generar un caso con capturas, una grabación y un archivo
  del explorador. En "Listo": aparecen la contraseña, "Copiar" (anuncia "Copiada"), el texto
  del canal separado, la nota de compatibilidad y el check "ZIP cifrado con AES-256".
- **M2.** Abrir el ZIP descargado con 7-Zip/Keka/The Unarchiver: **pide contraseña**. Con
  una incorrecta falla. Con la de "Listo" extrae todo. El `shasum -a 256` de cada archivo
  coincide con la tabla del DOCX. El Explorador de Windows o la Utilidad de Archivo de macOS
  **no** lo abren (esperado).
- **M3.** `shasum -a 256` del ZIP = "Hash SHA-256 del ZIP de evidencia" de "Listo" = fila del
  ZIP en el DOCX = `zip_hash` en el historial. Se calcula sin la contraseña.
- **M4.** Historial: el caso nuevo muestra "ZIP cifrado" y "Mostrar contraseña". Al tocarlo
  aparece la misma contraseña con "Copiar". En DevTools → Network, `GET /api/cases` y
  `GET /api/cases/{id}` **no** traen `zip_password`.
- **M5.** Paso Informe de un caso **nuevo** (sin textos guardados): el texto por defecto de
  aseguramiento es el de §5.6 y no tiene la contraseña. **Revisar la redacción (P2).** El DOCX
  no contiene la contraseña (buscarla con Ctrl+F).
- **M6. Cifrado apagado.** `Report__EncryptZip=false` y reiniciar el backend (aparece el
  warning en el log). "Revisá y generá" dice "ZIP de evidencia con hash SHA-256 verificable".
  "Listo" no muestra contraseña ni nota de cifrado. El ZIP abre sin pedir nada. En el
  historial: "ZIP de evidencia" + "Este ZIP se generó sin cifrar". Volver a `true`.
- **M7. Grabación grande (Zip64), si hay una a mano de más de 4 GB, o evidencia que sume más
  de 4 GB:** generar y abrir con 7-Zip con la contraseña. Es el único caso que el spike no
  pudo cubrir.
- **M8. Casos viejos:** si hay algún caso `completed` previo a esta HU, se ve "ZIP de
  evidencia" + "Este ZIP se generó sin cifrar", sin contraseña ni botón para mostrarla. Su
  ZIP y su documento no cambian (comparar `zip_hash` y `shasum` con lo que había antes).
- **M9.** Con otro usuario, `GET /api/cases/{id}/zip-password` de un caso ajeno → 403 (P1-A).

## Resolución de decisiones pendientes (2026-10-01, modo autónomo)

- **P1 → A** (403 para caso ajeno, coherente con `LoadOwnedAsync`).
- **P2 → A** (texto de aseguramiento cifrado tal cual; el usuario lo revisa en M5).
