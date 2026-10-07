# SDD: Marca por cuenta (cada informe sale con la marca de su dueño)

**HU:** `docs/hu-marca-por-cliente.md` (issue #13), validada el 2026-10-06 con todas las
recomendadas (D1 = C … D13 = A).
**Slug / rama:** `marca-por-cliente` / `feat/marca-por-cliente` (desde `develop` en `4e0ce40`).
**Base:** #6 `usuarios-locales`, #12 `abm-clientes` y `marca-comercial-sin-mpf-gfd`, todas en `develop`.

---

## 1. Resumen funcional

Cada cuenta tiene su propia marca del informe: nombre de la organización, logo, isotipo,
hasta 6 líneas de contacto, color primario y color de acento. Se guarda en MongoDB, en la
colección nueva `account_brandings`, con `_id` = DNI y las imágenes como binario dentro del
documento. La edita el propio usuario desde "Marca del informe" en el `UserMenu`, y un
superadmin la de cualquier cuenta desde "Editar marca" en `/admin/cuentas`. Los dos usan el
mismo formulario, que tiene vista previa en vivo, validación estricta (400 con el campo
exacto, sin truncar) y control optimista de concurrencia. Al generar un informe (en los dos
flujos), `ReportService` resuelve la marca **del dueño del caso** (`Officer.Dni`) en ese
instante. Si la cuenta nunca guardó una marca, usa la `Branding` de `appsettings`, que pasa
a ser el default de la instalación. Cada guardado con cambios se audita en
`user_admin_events` con la acción `update_branding`. El login y `/api/config/public` no
cambian: siguen mostrando el default de la instalación. El `UserMenu` muestra el logo y el
nombre de la cuenta logueada.

## 2. Toca

| Parte | ¿Toca? | Detalle |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí** | Colección, repositorio, servicio, reglas, resolver para el informe, 2 controllers y cambios en `ReportService` y `Program.cs`. |
| tests `server/tests/Factum.Backend.Tests` | **sí** | Reglas, diff, servicio, resolver, DOCX con la marca del dueño e integración Mongo opcional. |
| backend (Tatana) `server/src/Factum.Agent` | **no** | |
| client `client/` | **sí** | Formulario compartido + diálogo, store de la marca propia, `UserMenu`, `AppNavbar` y panel `/admin/cuentas`. |
| agent-ui `agent-ui/` | **no** | |

---

## 3. Hallazgos sobre el código real (2026-10-06)

1. `ReportService` es **singleton** y recibe `IBrandingService` (singleton de `appsettings`).
   Usa `branding.Current` en `GenerateAsync` (L155) y `GenerateReportAsync` (L215). Las dos
   firmas públicas reciben el `Case`, así que `cas.Officer.Dni` está disponible sin tocar
   `CaseService` ni `AgentGeneration`.
2. `BrandingSnapshot` / `BrandingLogo` ya son el formato que consume el DOCX (`ReportValues`,
   `InsertBrandImage`, `BrandColors.Apply`). La marca por cuenta **se mapea a ese mismo
   record**, así que el DOCX no cambia.
3. Los tests de informe construyen `new ReportService(new FakeBranding(...), …)` en cuatro
   lugares: `ReportTestSupport.FakeBranding`, `ReportDesignTests.FakeBranding` (clase privada
   propia), `ReportServiceAgentTests` y `ReportBodyImagesDocxTests`. Si cambia el tipo del
   constructor, **las dos clases `FakeBranding` tienen que implementar también la interfaz
   nueva** (ver §6.6).
4. Mongo guarda las propiedades en PascalCase: no hay `ConventionPack`. `byte[]` se serializa
   como `BinData` por defecto.
5. `user_admin_events` se lista por `TargetUserId` (= `users._id`). `UserAdminService.AuditAsync`
   es privado, así que el servicio de marca reimplementa el mismo patrón: insert con
   `CancellationToken.None`, un reintento y log sin `Changes`.
6. El multipart existente (`generate/finish`) fija el tope con `IHttpMaxRequestBodySizeFeature`
   y chequea `Content-Length` antes de leer. Se replica ese patrón.
7. En el client, `<img src>` no puede mandar `Authorization`. El patrón existente para
   imágenes autenticadas es `getReportImagePreview`, que hace `fetch` → `Blob` → object URL.
   La alternativa `?token=` de `downloadURL` **no** se usa (deja el token en logs).
8. `usePublicConfig` es anónimo y lo usan `LoginHero`, `UserMenu`, `SiteFooter`,
   `GenerateStep`, `dashboard/page` y `DesignSystemShowcase`. Solo el `UserMenu` pasa a la
   marca de la cuenta (D5). `SiteFooter` solo se monta en `/design-system` y queda como está.
9. `UserDto` (`/api/auth/me`) no trae `organization`. La sugerencia de D10 viaja en la
   respuesta de la marca (`suggested_organization_name`), sin tocar `UserDto`.
10. La base de desarrollo (`factum_dev`, inspección de solo lectura) **no** tiene
    `account_brandings`. `user_admin_events` tiene solo `create`/`update`. No hay documentos
    previos que migrar.

---

## 4. Modelo de datos

### 4.1 Colección nueva `account_brandings` (`Models/AccountBranding.cs`) — D2, D7

Un documento por cuenta. `_id` = DNI del dueño, el mismo que `cases.Officer.Dni` y
`expert_profiles._id`, en los tres modos de auth.

```csharp
[BsonIgnoreExtraElements]
public sealed class AccountBranding
{
    [BsonId] public string Id { get; set; } = "";              // DNI (7-8 dígitos)
    public string OrganizationName { get; set; } = "";          // trim, ≤ 150, "" = sin nombre
    public List<string> ContactLines { get; set; } = [];        // ≤ 6, cada una trim y ≤ 150, sin vacías
    public AccountBrandingImage? Logo { get; set; }             // null = sin logo
    public AccountBrandingImage? Isotype { get; set; }          // null = sin isotipo
    public string PrimaryColor { get; set; } = "";              // "RRGGBB" mayúsculas sin '#'; "" = verde Factum
    public string AccentColor { get; set; } = "";               // ídem; "" = tinte Factum
    public DateTime CreatedAt { get; set; }                     // UTC, truncado a ms
    public DateTime UpdatedAt { get; set; }                     // UTC, truncado a ms; token optimista
    public string UpdatedBy { get; set; } = "";                 // DNI de quien guardó
}

[BsonIgnoreExtraElements]
public sealed class AccountBrandingImage
{
    public byte[] Data { get; set; } = [];                      // BinData, ≤ 1 MiB
    public string ContentType { get; set; } = "";               // "image/png" | "image/jpeg" (por contenido)
    public int Width { get; set; }
    public int Height { get; set; }
    public long Size { get; set; }                              // bytes
    public string Version { get; set; } = "";                   // 12 hex del SHA-256
}
```

- **Tamaño máximo del documento:** ~2 MiB + textos, lejos de los 16 MB de BSON.
- **Índices:** ninguno además de `_id`. Todas las lecturas y escrituras van por `_id`
  (= DNI), y no hay listados ni búsquedas por otro campo. No hace falta `EnsureIndexes`, y
  la colección la crea Mongo con el primer insert.
- **Compatibilidad:** la colección es nueva. `[BsonIgnoreExtraElements]` y los defaults de
  C# toleran campos futuros o faltantes.
- **Lo que no se guarda:** el nombre del archivo original, EXIF ni ningún otro metadato de
  la subida.

### 4.2 `user_admin_events`: acción nueva (`Models/UserAdminEvent.cs`) — D9

- `UserAdminActions.UpdateBranding = "update_branding"`.
- `Changes` usa solo los campos de la lista blanca `BrandingChanges.AllowedFields`:
  `organization_name`, `contact_lines`, `primary_color`, `accent_color`, `logo` e `isotype`
  (nombres JSON).
- Formato de `from`/`to` (string, `""` = vacío; nunca `null` en este evento):
  - `organization_name`: el texto.
  - `contact_lines`: las líneas unidas con `"\n"`.
  - `primary_color` / `accent_color`: `"#RRGGBB"`, o `""` si es el default de Factum.
  - `logo` / `isotype`: la `Version` (12 hex), o `""` si no hay imagen. **Nunca los bytes.**
- `TargetUserId`:
  - en `local`, el `users._id` de la cuenta dueña, para que aparezca en el historial del
    panel;
  - en `dev`/`external`, `""`, porque no existe `users` (DT6).
- `TargetDni` = DNI dueño, `TargetName` = nombre de la cuenta (local) o de la sesión (dev/external).
- `Reason` = null. `Ip` = la del request.
- Si el usuario edita su propia marca: `ActorDni == TargetDni`.

### 4.3 Lo que **no** se escribe nunca

`users`, `expert_profiles`, `cases`, `catalog_entries`, `catalog_seeds` y `agent_events`: ni
una escritura. `users` se **lee** (por DNI o `_id`) para resolver la cuenta del panel, el
`TargetUserId` y la sugerencia de D10. Las únicas escrituras de esta HU son en
`account_brandings`, por `_id`, y el insert en `user_admin_events`.

---

## 5. Reglas de validación (`Services/Branding/BrandingRules.cs`, estática y pura) — D3, D11

Reutilizan las constantes de `BrandingService` (`MaxNameLength` = 150, `MaxContactLines` = 6,
`MaxContactLineLength` = 150, `MaxLogoBytes` = 1 048 576, `MinLogoSide` = 16,
`MaxLogoSide` = 4096), `ImageProbe.TryDetect(byte[])` y `BrandingColors`. Al guardar desde la
web, las reglas **rechazan**. `BrandingService` (lectura de `appsettings`) sigue tolerante y
no cambia.

| Campo (JSON) | Normalización | Rechazo (400 `validation_failed`, `field`) |
|---|---|---|
| `organization_name` | `Trim()` | más de 150 caracteres → `MsgNameTooLong`. Algún carácter de control (`char.IsControl`) → `MsgControlChars`. |
| `contact_lines` | `Trim()` de cada línea y se descartan las vacías | Algún elemento `null` se trata como `""`. Una línea (índice **en el array recibido**, base 0) de más de 150 → `MsgContactLineTooLong` + `index`. Una línea con un carácter de control → `MsgControlChars` + `index`. Más de 6 no vacías → `MsgTooManyContactLines`. El orden de chequeo es: largo y control por línea, y después la cantidad. |
| `primary_color` | `""`/`null` → `""` (default). Si no, `BrandingColors.Normalize` | `Normalize == null` → `MsgColorFormat`. `ContrastWithWhite < 4.5` → `MsgPrimaryContrast(c)` + `contrast` + `min_contrast`. |
| `accent_color` | ídem | Formato → `MsgColorFormat`. `Contrast(hex, InkColor) < 4.5` → `MsgAccentContrast(c)` + `contrast` + `min_contrast`. |
| `logo` / `isotype` (archivo) | — | Largo 0 o `ImageProbe` falla → `MsgImageFormat`. Más de 1 MiB → `MsgImageTooBig`. Algún lado fuera de 16-4096 → `MsgImageSize(w,h)`. Se chequean en ese orden: el peso primero, sin leer más de `MaxLogoBytes + 1` bytes. |
| `logo_action` / `isotype_action` | — | Distinto de `keep`/`replace`/`remove` → `MsgInvalidRequest` (`field` = `metadata`). `replace` sin archivo → `MsgImageMissing` (`field` = `logo`/`isotype`). Un archivo con `keep`/`remove` → `MsgInvalidRequest` (`field` = `metadata`). |
| `expected_updated_at` | — | No viene y la marca **existe** → 409 `stale_update` (ver §6.3). |

**Contraste mostrado:** `contrast` = `Math.Floor(ratio * 10) / 10` (truncado a 1 decimal,
para que nunca se lea "4.5:1, mínimo 4.5:1" con un valor de 4.49). En el texto se formatea
con `"0.0"` y `CultureInfo.InvariantCulture` (punto decimal). El client usa **el mismo**
truncado. La comparación con 4.5 se hace siempre sobre el valor **sin truncar**.

Si hay varios errores, la primera falla corta la validación y se devuelve un solo `field`,
como en #12. El orden es: `metadata` → `organization_name` → `contact_lines` →
`primary_color` → `accent_color` → `logo` → `isotype`. El client valida todo antes de enviar,
así que el 400 es la red de seguridad.

### 5.1 Textos exactos (`Services/Branding/BrandingErrors.cs` = `client/src/lib/branding.ts` `BRANDING_MESSAGES`)

| Clave | Texto |
|---|---|
| `MsgNameTooLong` / `name_long` | `El nombre de la organización puede tener hasta 150 caracteres.` |
| `MsgControlChars` / `control_chars` | `No puede tener saltos de línea ni caracteres de control.` |
| `MsgContactLineTooLong` / `contact_line_long` | `Cada línea de contacto puede tener hasta 150 caracteres.` |
| `MsgTooManyContactLines` / `contact_lines_max` | `Podés cargar hasta 6 líneas de contacto.` |
| `MsgColorFormat` / `color_format` | `Ingresá un color en formato #RRGGBB.` |
| `MsgPrimaryContrast(c)` / `primary_contrast` | `Contraste {c}:1 con blanco, mínimo 4.5:1.` |
| `MsgAccentContrast(c)` / `accent_contrast` | `Contraste {c}:1 con el texto, mínimo 4.5:1.` |
| `MsgImageFormat` / `image_format` | `El archivo no es una imagen PNG ni JPEG.` |
| `MsgImageTooBig` / `image_too_big` | `La imagen supera el máximo de 1 MB.` |
| `MsgImageSize(w,h)` / `image_size` | `La imagen mide {w}×{h} px; cada lado tiene que estar entre 16 y 4096 px.` |
| `MsgImageMissing` / `image_missing` | `Falta el archivo de la imagen.` |
| `MsgInvalidRequest` / `invalid_request` | `La solicitud no es válida. Recargá e intentá de nuevo.` |
| `MsgStaleBranding` / `stale_update` | `La marca se modificó desde otra sesión. Recargá para ver los cambios.` |
| `MsgRequestTooLarge` / `request_too_large` | `La marca supera el tamaño máximo permitido (2,5 MB en total).` |
| `MsgBrandingImageNotFound` / `image_not_found` | `La imagen no existe.` |

`{c}` es el contraste truncado con un decimal y punto (`2.1`). `{w}` y `{h}` son enteros, y
el `×` es U+00D7.

---

## 6. Diseño backend

### 6.1 Repositorio (`Infrastructure/AccountBrandingRepository.cs`)

```csharp
public interface IAccountBrandingRepository
{
    /// Documento completo, con bytes. Lo usan el resolver del informe y el armado de la respuesta.
    Task<AccountBranding?> FindAsync(string dni, CancellationToken ct = default);
    /// Proyección sin Logo.Data ni Isotype.Data (GET y diff del guardado).
    Task<AccountBranding?> FindMetaAsync(string dni, CancellationToken ct = default);
    /// Solo la subimagen pedida (con bytes), para servirla.
    Task<AccountBrandingImage?> FindImageAsync(string dni, BrandingImageKind kind, CancellationToken ct = default);
    /// InsertOne. false si ya existe (DuplicateKey) → stale_update.
    Task<bool> TryInsertAsync(AccountBranding doc, CancellationToken ct = default);
    /// UpdateOne con filtro { _id: dni, UpdatedAt: expectedUpdatedAt }. $set de OrganizationName,
    /// ContactLines, PrimaryColor, AccentColor, UpdatedAt y UpdatedBy. Logo/Isotype según la acción:
    /// replace → $set (subdocumento nuevo), remove → $set null, keep → no se toca.
    /// false si no matcheó (otro guardado en el medio).
    Task<bool> UpdateIfUnchangedAsync(string dni, BrandingUpdate update, DateTime expectedUpdatedAt,
        CancellationToken ct = default);
}
public enum BrandingImageKind { Logo, Isotype }
public sealed record BrandingUpdate(string OrganizationName, List<string> ContactLines,
    string PrimaryColor, string AccentColor, ImageChange Logo, ImageChange Isotype,
    DateTime UpdatedAt, string UpdatedBy);
public sealed record ImageChange(BrandingImageAction Action, AccountBrandingImage? Image);
public enum BrandingImageAction { Keep, Replace, Remove }
```

Colección `account_brandings`, `MongoClient` lazy como `UserRepository` (no conecta hasta el
primer uso). Todas las operaciones filtran por `_id`. **No hay delete.**

### 6.2 Resolver del informe (`Services/Branding/ReportBrandingResolver.cs`) — D4, D8

```csharp
public interface IReportBrandingResolver
{
    /// Marca con la que sale el informe de un caso cuyo dueño es ownerDni. Nunca devuelve null.
    Task<BrandingSnapshot> ResolveAsync(string? ownerDni, CancellationToken ct = default);
}
```

Implementación `ReportBrandingResolver(IAccountBrandingRepository repo, IBrandingService installation, ILogger)`, singleton:

1. Si `ownerDni` es null/vacío o no cumple `DniFormat.IsValid`, devuelve
   `installation.Current` (casos viejos sin dueño válido).
2. `var doc = await repo.FindAsync(ownerDni, ct)`. **Una sola lectura por generación.**
3. Si `doc == null`, devuelve `installation.Current` (D4: la `Branding` de `appsettings`,
   entera).
4. Si `doc != null`, devuelve **solo** lo de la cuenta (D4-A, sin mezclar campo por campo):
   - `OrganizationName` = `doc.OrganizationName` (`""` → `null`);
   - `ContactLines` = `doc.ContactLines`;
   - `Logo`/`Isotype` = `ToLogo(doc.Logo)`, que vuelve a validar con `ImageProbe` y los
     límites (defensivo). Si falla, `null` con un warning `"Marca de cuenta {Dni}: logo
     ignorado ({motivo})"`, sin bytes en el log. `Extension` = `.png` / `.jpg` según
     `ContentType`;
   - `PrimaryColor`/`AccentColor`: `""` → default de Factum. Si no, `Normalize` y contraste.
     Si un valor no es válido, va el default con un warning (no debería pasar, porque se
     validó al escribir).
5. Si Mongo tira, **se propaga**: la generación falla como cualquier otro error y el caso
   queda en `Error`. Así nunca sale un informe con la marca equivocada (DT3).

Sin caché (DT2). Como el documento se lee entero de una vez y se escribe con un solo
`UpdateOne`/`InsertOne`, el informe nunca mezcla dos versiones: Mongo garantiza la
atomicidad por documento. Esto cubre el escenario "Cambio de marca durante una generación".

### 6.3 Servicio (`Services/Branding/AccountBrandingService.cs`)

```csharp
public sealed record BrandingActor(string Dni, string Name, string? Ip);
public sealed record BrandingSaveInput(BrandingSaveMetadata Metadata, byte[]? LogoBytes, byte[]? IsotypeBytes);

public interface IAccountBrandingService
{
    Task<Result<AccountBrandingResponse>> GetOwnAsync(User user, CancellationToken ct = default);
    Task<Result<AccountBrandingSaveResponse>> SaveOwnAsync(BrandingActor actor, User user, BrandingSaveInput input, CancellationToken ct = default);
    Task<Result<AccountBrandingImage>> GetOwnImageAsync(string dni, BrandingImageKind kind, CancellationToken ct = default);

    // Panel (solo local; id = users._id)
    Task<Result<AccountBrandingResponse>> GetForAccountAsync(string userId, CancellationToken ct = default);
    Task<Result<AccountBrandingSaveResponse>> SaveForAccountAsync(BrandingActor actor, string userId, BrandingSaveInput input, CancellationToken ct = default);
    Task<Result<AccountBrandingImage>> GetAccountImageAsync(string userId, BrandingImageKind kind, CancellationToken ct = default);
}
```

Dependencias: `AuthSettings`, `IAccountBrandingRepository`, `IUserRepository`,
`IUserAdminEventRepository`, `TimeProvider` y `ILogger`. Singleton.

**Resolución del dueño.**

- Propia: el DNI sale de la sesión (`User.Dni`). Nunca de un parámetro, así que no hay forma
  de pedir la marca de otro (escenario de aislamiento).
- Panel: `IsLocal`, o 404 `not_available`. Se busca con `users.FindAdminViewByIdAsync(id)` y,
  si es null, 404 `user_not_found`. El dueño es `acc.Dni`.

**`GET`.** `FindMetaAsync(dni)` → DTO (§7):

- `exists = doc != null`.
- `suggested_organization_name`: solo si `!exists`, en `local`, y si `users.Organization` de
  esa cuenta no está vacío. Si no, `null` (D10, solo lectura de `users`).

**`Save` (mismo flujo para los dos caminos).**

1. Validar con `BrandingRules`. Si falla, 400 y no se escribe ni se audita.
2. Normalizar: nombre, líneas, colores e imágenes nuevas. Para cada imagen nueva se arma un
   `AccountBrandingImage` con `Version` = `Convert.ToHexStringLower(SHA256.HashData(data))[..12]`.
3. `before = FindMetaAsync(dni)`.
4. Diff (`BrandingChanges.Diff(before, normalized)`, pura). Una imagen `replace` con la misma
   `Version` que la actual **no** es cambio. `remove` sin imagen actual tampoco. Si no hay
   cambios, `Ok(changed: false)` con el DTO actual, **sin escribir ni auditar** y **sin
   mirar** `expected_updated_at` (igual que E4 de #12). Si `before == null` y todo viene vacío
   (sin nombre, sin líneas, colores `""` y sin imágenes), también es `changed: false` y **no
   se crea** el documento. Así, abrir y guardar el formulario vacío no le quita a la cuenta
   el fallback de D4.
5. Control optimista (D11 de #12):
   - Si `before == null`: si `expected_updated_at` viene con valor, 409 `stale_update` (otra
     sesión la borró, cosa que hoy no pasa, o el client está desfasado). Si no, `TryInsertAsync`
     con `CreatedAt = UpdatedAt = now` y `UpdatedBy = actor.Dni`. Si devuelve false
     (DuplicateKey: otra sesión creó la marca en el medio), 409 `stale_update`.
   - Si `before != null`: si `expected_updated_at` falta, 409 `stale_update`. Si no, se
     normaliza a UTC y se trunca a ms. Si difiere de `before.UpdatedAt`, 409. Si no,
     `UpdateIfUnchangedAsync(dni, update, before.UpdatedAt)`, y si devuelve false, 409.
6. Auditar `update_branding` (§4.2) **después** de escribir, con el mismo patrón que
   `UserAdminService.AuditAsync`: `CancellationToken.None`, 2 intentos y `LogError` sin
   `Changes`. Si falla, la respuesta igual sale como éxito.
   - `TargetUserId`: en el panel, `acc.Id`. Si es la propia en `local`, el `Id` de
     `users.FindSessionByDniAsync(dni)`, o `""` si no hubiera cuenta.
   - `TargetName`: `acc.Name`, o `user.Name` si es la propia.
7. Releer `FindMetaAsync` y devolver `Ok(changed: true)` con el DTO.

`now` = `AuthTime.TruncateToMs(time.GetUtcNow().UtcDateTime)`.

**Logs:** nunca bytes, nunca `Changes` ni textos de contacto. Sí: `"Marca guardada para DNI
{Dni} por {ActorDni} (campos: {Fields})"`, con la lista de nombres de campo.

**Imágenes:** `FindImageAsync(dni, kind)`. Si es null, 404 `image_not_found`. Si no, `Ok(img)`.

### 6.4 Diff (`Services/Branding/BrandingChanges.cs`, pura)

`AllowedFields = ["organization_name", "contact_lines", "primary_color", "accent_color", "logo", "isotype"]`.
`Diff(AccountBranding? before, NormalizedBranding after) → List<UserAdminChange>`. Con
`before == null`, todo "from" es `""`. La comparación es ordinal sobre los valores ya
formateados como en §4.2 (las imágenes, por `Version`). El orden de salida es el de
`AllowedFields`.

### 6.5 Controllers

**`Controllers/BrandingController.cs`**: `[Route("api/branding")]`, `[Authorize]`, cualquier
rol y cualquier modo de auth (D7-A).

| Método | Ruta | Respuesta |
|---|---|---|
| `GET` | `/api/branding` | 200 `{ branding }`. `Cache-Control: no-store`. |
| `PUT` | `/api/branding` | `multipart/form-data` (§6.5.1). 200 `{ branding, changed }`. 400, 409 o 413. `no-store`. |
| `GET`/`HEAD` | `/api/branding/logo` | Bytes (§6.5.2). 404 si no hay. |
| `GET`/`HEAD` | `/api/branding/isotype` | ídem. |

**`Controllers/AdminBrandingController.cs`**: `[Route("api/admin/users/{id}/branding")]`,
`[Authorize]`, `[RequireLocalAuthMode]`, `[RequireSuperadmin]`, con el mismo orden de
filtros que `AdminUsersController`. Un cliente recibe 403 `superadmin_required`, y cualquiera
fuera de `local`, 404 `not_available`.

| Método | Ruta | Respuesta |
|---|---|---|
| `GET` | `/api/admin/users/{id}/branding` | 200 `{ branding }`. 404 `user_not_found`. `no-store`. |
| `PUT` | `/api/admin/users/{id}/branding` | Igual que el `PUT` propio. Además, 404 `user_not_found`. |
| `GET`/`HEAD` | `/api/admin/users/{id}/branding/logo` | Bytes. 404 `user_not_found` / `image_not_found`. |
| `GET`/`HEAD` | `/api/admin/users/{id}/branding/isotype` | ídem. |

Ninguno de los dos lleva `[AllowDuringPasswordChange]`: con la contraseña temporal
pendiente, el gate responde 403 como en el resto de la app.

El mapeo de errores sigue el formato `{ error, code[, field][, index][, contrast][, min_contrast][, max_bytes] }`,
con un `StatusFor(code)` propio del mismo estilo que el de `AdminUsersController`:

| Código | HTTP |
|---|---|
| `validation_failed` | 400 |
| `user_not_found`, `not_available`, `image_not_found` | 404 |
| `stale_update` | 409 |
| `request_too_large` | 413 |

Los errores se arman con `AdminErrors.Fail<T>(code, msg, field, extra)`. Ese helper ya
soporta `extra`, y con él se mandan `index`, `contrast` (double) y `min_contrast` (4.5).
`KindFor` se amplía: `image_not_found` → `NotFound`, `request_too_large` →
`PayloadTooLarge`.

#### 6.5.1 Lectura del `PUT` (helper compartido `BrandingMultipart.ReadAsync`)

1. `Response.Headers.CacheControl = "no-store"`.
2. Tope del cuerpo: `BrandingLimits.MaxRequestBytes = 2_621_440` (2,5 MiB). Hay margen sobre
   2 × 1 MiB para que una imagen de 1-1,25 MiB llegue a la validación y reciba
   `image_too_big` (con su `field`) en lugar de un 413 genérico.
   `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize = MaxRequestBytes`. Si
   `Content-Length > MaxRequestBytes`, 413 `request_too_large` con `max_bytes`.
3. Si `!Request.HasFormContentType` o el content type no es `multipart/form-data`, 400
   `invalid_request` (`field` = `metadata`).
4. `Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = MaxRequestBytes }, ct)`.
   `BadHttpRequestException` (413) e `InvalidDataException` se mapean a 413
   `request_too_large`.
5. Partes:
   - `metadata` (campo de texto, JSON en snake_case). Se deserializa con las
     `JsonSerializerOptions` de MVC (`IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>`). Si
     falta o es inválido, 400 `invalid_request` (`field` = `metadata`).
   - `logo` e `isotype` (archivos, opcionales). Si `IFormFile.Length > MaxLogoBytes`, se
     rechaza sin leerlo (`image_too_big`). Si no, se copia a `byte[]`. Cualquier otra parte
     se ignora.
6. Arma `BrandingSaveInput` y llama al servicio.

#### 6.5.2 Servir imágenes (las dos rutas, helper `BrandingImageResult`)

- 200 con `File(img.Data, img.ContentType)`.
- Headers:
  - `X-Content-Type-Options: nosniff`;
  - `Content-Security-Policy: default-src 'none'; sandbox`;
  - `ETag: "<version>"`;
  - `Cache-Control: private, max-age=86400`. Es `private` porque es contenido de una cuenta
    y nunca se guarda en caches compartidos.
- `If-None-Match` igual al ETag (también `W/` o `*`): 304, con la misma lógica que
  `ConfigController.Logo`.
- El parámetro `?v=<version>` es solo cache-busting del client y se ignora (DT4).
- No son anónimas, y la ruta del cliente no lleva identificador. La del panel usa
  `users._id` (GUID) y exige superadmin. No hay forma de enumerar cuentas.

### 6.6 `ReportService` (`Services/Reports/ReportService.cs`) — D8

- Los dos constructores pasan de `IBrandingService branding` a
  `IReportBrandingResolver brandings`. El constructor `internal` con `templateFileName` se
  mantiene.
- `GenerateAsync`: antes del paso 4 (ZIP), `var brand = await brandings.ResolveAsync(cas.Officer?.Dni, ct);`.
  Se resuelve **antes** de escribir artefactos, así un fallo de Mongo no deja ZIP a medias.
  Después pasa `brand` a `GenerateDocxAsync` en lugar de `branding.Current`.
- `GenerateReportAsync`: lo mismo, antes del bloque `try` que escribe el DOCX.
- `LoadSello`, la atribución, la plantilla, los nombres de archivo y los hashes no cambian (D13).
- **Tests existentes:** `ReportTestSupport.FakeBranding` y `ReportDesignTests.FakeBranding`
  pasan a implementar **también** `IReportBrandingResolver`
  (`ResolveAsync(...) => Task.FromResult(Current)`). Ningún test cambia sus asserts.

### 6.7 `ConfigController` y `BrandingService` — D6

Sin cambios de comportamiento. Solo se actualizan los `<summary>`: la `Branding` de
`appsettings` es "el default de la instalación: login y fallback de D4". `/api/config/public`
sigue anónimo y con la misma forma.

### 6.8 Registro (`Program.cs`)

```csharp
builder.Services.AddSingleton<IAccountBrandingRepository, AccountBrandingRepository>();
builder.Services.AddSingleton<IReportBrandingResolver, ReportBrandingResolver>();
builder.Services.AddSingleton<IAccountBrandingService, AccountBrandingService>();
```

No hay `EnsureIndexes` nuevo. `IBrandingService` se sigue resolviendo al arrancar.

### 6.9 Modos `dev` / `external` (D7-A)

- `/api/branding*` funciona igual en los tres modos: la llave es el DNI de la sesión.
- `/api/admin/users/{id}/branding*` responde 404 `not_available` fuera de `local`, igual que
  el panel.
- `suggested_organization_name` es siempre `null` fuera de `local`, y no se consulta `users`.
- La auditoría se inserta igual en `user_admin_events`, con `TargetUserId = ""` (DT6).
  `MongoClient` es lazy, y si `users` no existe solo se pierde la sugerencia. Nada falla.

---

## 7. DTOs (`DTOs/BrandingDtos.cs`)

```csharp
public sealed record BrandingImageDto(string Url, string ContentType, int Width, int Height, long Size, string Version);

public sealed record AccountBrandingDto(
    bool Exists,
    string OrganizationName,
    List<string> ContactLines,
    string? PrimaryColor,          // "#1F3A93" o null (= Factum)
    string? AccentColor,
    BrandingImageDto? Logo,
    BrandingImageDto? Isotype,
    DateTime? UpdatedAt,
    string? UpdatedBy,
    string? SuggestedOrganizationName);

public sealed record AccountBrandingResponse(AccountBrandingDto Branding);
public sealed record AccountBrandingSaveResponse(AccountBrandingDto Branding, bool Changed);

/// Parte "metadata" del PUT. Sin [Required]: el servicio valida.
public sealed record BrandingSaveMetadata(
    string? OrganizationName,
    List<string?>? ContactLines,
    string? PrimaryColor,
    string? AccentColor,
    string? LogoAction,            // "keep" | "replace" | "remove"
    string? IsotypeAction,
    DateTime? ExpectedUpdatedAt);
```

`Url` es una ruta relativa a la raíz del backend con `?v=<version>`:

- propia: `/api/branding/logo?v=…` y `/api/branding/isotype?v=…`;
- panel: `/api/admin/users/{Uri.EscapeDataString(id)}/branding/logo?v=…` y `/isotype?v=…`.

Con `exists = false`, todos los campos van vacíos o `null`, `contact_lines = []` y
`updated_at = null`.

---

## 8. Contrato compartido (client ↔ server)

JSON en **snake_case_lower** (`JsonNamingPolicy.SnakeCaseLower` en `Program.cs`). Las claves
de los errores son literales (diccionario). Archivos:

- backend: `server/src/Factum.Backend/DTOs/BrandingDtos.cs`,
  `Services/Branding/BrandingErrors.cs` y `Models/UserAdminEvent.cs`;
- client: `client/src/lib/api.ts` (tipos + métodos), reexportados en
  `client/src/types/index.ts`, y `client/src/lib/branding.ts` (textos y reglas).

### 8.1 Tipos (`client/src/lib/api.ts`)

```ts
export type BrandingImageKind = "logo" | "isotype";
export type BrandingImageAction = "keep" | "replace" | "remove";

export interface BrandingImage {
  /** Ruta relativa al backend, con ?v=<version>. Se pide con Authorization (blob). */
  url: string;
  content_type: "image/png" | "image/jpeg";
  width: number;
  height: number;
  size: number;
  /** 12 hex del SHA-256. */
  version: string;
}

export interface AccountBranding {
  /** false = la cuenta nunca guardó marca: sus informes usan la de la instalación (D4). */
  exists: boolean;
  organization_name: string;          // "" = sin nombre
  contact_lines: string[];
  primary_color: string | null;       // "#RRGGBB" mayúsculas, null = verde de Factum
  accent_color: string | null;        // null = tinte de Factum
  logo: BrandingImage | null;
  isotype: BrandingImage | null;
  updated_at: string | null;          // ISO UTC (ms); token optimista
  updated_by: string | null;          // DNI
  /** Solo con exists=false, en modo local y si users.organization tiene valor (D10). */
  suggested_organization_name: string | null;
}

/** Parte `metadata` (JSON) del PUT multipart. Archivos en las partes `logo` / `isotype`. */
export interface BrandingSaveMetadata {
  organization_name: string;
  contact_lines: string[];
  primary_color: string;              // "#RRGGBB" o "" (= Factum)
  accent_color: string;
  logo_action: BrandingImageAction;
  isotype_action: BrandingImageAction;
  expected_updated_at: string | null; // updated_at tal como vino; null si exists=false
}

export interface AccountBrandingSaveResponse { branding: AccountBranding; changed: boolean; }

export type BrandingField =
  | "metadata" | "organization_name" | "contact_lines" | "primary_color" | "accent_color" | "logo" | "isotype";
export type BrandingErrorCode =
  | "validation_failed" | "stale_update" | "request_too_large" | "image_not_found"
  | "user_not_found" | "not_available" | "superadmin_required";
```

`ApiErrorBody` suma:

- `index?: number`: índice de `contact_lines` en el array enviado;
- `contrast?: number`: truncado a 1 decimal;
- `min_contrast?: number`;
- `field?` amplía la unión con `BrandingField`.

`max_bytes` ya existe.

`AdminAction` suma `"update_branding"`.

### 8.2 Endpoints

| Método | Ruta | Request | 200 |
|---|---|---|---|
| GET | `/api/branding` | — | `{ branding: AccountBranding }` |
| PUT | `/api/branding` | `multipart/form-data`: `metadata` (string JSON `BrandingSaveMetadata`), `logo?` (file), `isotype?` (file) | `AccountBrandingSaveResponse` |
| GET/HEAD | `/api/branding/logo`, `/api/branding/isotype` | `?v=` opcional | bytes PNG/JPEG |
| GET | `/api/admin/users/{id}/branding` | — | `{ branding }` |
| PUT | `/api/admin/users/{id}/branding` | igual que el propio | `AccountBrandingSaveResponse` |
| GET/HEAD | `/api/admin/users/{id}/branding/logo`, `…/isotype` | `?v=` opcional | bytes |

Errores: `{ error, code, field?, index?, contrast?, min_contrast?, max_bytes? }`, con los HTTP
de §6.5. Si el `PUT` no tiene cambios, devuelve 200 con `changed: false`.

### 8.3 Auditoría en el historial (`GET /api/admin/users/{id}/events`, sin cambios de forma)

- `action: "update_branding"`.
- `changes[].field` ∈ `organization_name | contact_lines | primary_color | accent_color | logo | isotype`.
- `from`/`to` siguen §4.2: líneas separadas por `\n`, colores `#RRGGBB` o `""`, e imágenes
  como versión o `""`.

### 8.4 Constantes duplicadas (backend = client)

| Constante | Valor | Backend | Client (`lib/branding.ts`) |
|---|---|---|---|
| Nombre máx. | 150 | `BrandingService.MaxNameLength` | `BRANDING_LIMITS.name` |
| Líneas máx. | 6 | `MaxContactLines` | `BRANDING_LIMITS.contactLines` |
| Largo de línea | 150 | `MaxContactLineLength` | `BRANDING_LIMITS.contactLine` |
| Imagen máx. | 1 048 576 | `MaxLogoBytes` | `BRANDING_LIMITS.imageBytes` |
| Lado | 16-4096 | `MinLogoSide`/`MaxLogoSide` | `BRANDING_LIMITS.minSide`/`maxSide` |
| Request máx. | 2 621 440 | `BrandingLimits.MaxRequestBytes` | (solo informativo) |
| Primario default | `2F6F12` | `BrandingColors.DefaultPrimary` | `FACTUM_PRIMARY` |
| Acento default | `E8F3DF` | `DefaultAccent` | `FACTUM_ACCENT` |
| Tinta | `0E1013` | `InkColor` | `INK_COLOR` |
| Contraste mín. | 4.5 | `MinPrimaryContrast`/`MinAccentContrastWithInk` | `MIN_CONTRAST` |

---

## 9. Diseño frontend (`client/` solamente; `agent-ui/` **no** se toca)

Antes de escribir código, leé `client/AGENTS.md` (Next.js 16). Todo lo de esta HU son
componentes `"use client"`. Skills obligatorias del arnés: `ui-ux-pro-max` (antes del JSX
final), `senior-frontend`, `3d-web-experience` (como criterio, sin 3D) y
`web-design-guidelines` (autochequeo). Además, `ui-styling` y
`mblode-agent-skills-ui-animation` donde corresponda.

### 9.1 `lib/api.ts` y `types/index.ts`

- Los tipos de §8.1, y `AdminAction` con `"update_branding"`.
- Métodos (todos con `requestSafe`, o con `fetch` manual + `toApiError` si son multipart o blob):
  - `getMyBranding(): Promise<AccountBranding>` → `GET /api/branding` (desempaqueta `branding`).
  - `saveMyBranding(form: FormData): Promise<AccountBrandingSaveResponse>` → `PUT`, sin
    `Content-Type` manual y con `Authorization`, con el mismo patrón que `finishGeneration`
    (red caída → `ApiError(SERVER_UNREACHABLE, 0)`).
  - `adminGetBranding(id)` / `adminSaveBranding(id, form)`: lo mismo sobre
    `/api/admin/users/${encodeURIComponent(id)}/branding`.
  - `getBrandingImage(url: string, signal?): Promise<Blob>`: `fetch(BACKEND_URL + url)` con
    `Authorization`, igual que `getReportImagePreview`. Sirve para las dos rutas, porque la
    URL viene del DTO.
- Reexportar los tipos nuevos en `types/index.ts`.

### 9.2 `lib/branding.ts` (nuevo, puro, sin React)

- Constantes de §8.4 y `BRANDING_MESSAGES` con los textos **idénticos** a §5.1. Las funciones
  `primaryContrastMessage(c)`, `accentContrastMessage(c)` e `imageSizeMessage(w, h)` arman los
  textos con parámetros.
- `normalizeHex(raw)` (el mismo algoritmo que `BrandingColors.Normalize`),
  `contrast(a, b)` (WCAG 2.x, el mismo algoritmo y umbral 0.03928 que el backend),
  `contrastWithWhite` y `displayContrast(r) = Math.floor(r * 10) / 10`, formateado con
  `toFixed(1)` (punto).
- `validateBrandingForm(form) → Partial<Record<BrandingField | \`contact_lines.${number}\`, string>>`,
  con las mismas reglas y el mismo orden que §5. Las vacías se descartan al enviar.
- `inspectImageFile(file) → Promise<{ ok: true, width, height, contentType } | { ok: false, message }>`:
  1. tamaño > 1 MiB → `image_too_big`;
  2. los primeros bytes (`file.slice(0, 32).arrayBuffer()`) con la firma PNG
     (`89 50 4E 47 0D 0A 1A 0A`) o JPEG (`FF D8 FF`) → si no, `image_format`;
  3. dimensiones con `createImageBitmap(file)`, que se cierra después → fuera de 16-4096,
     `image_size`. Si falla la decodificación, `image_format`.

  El backend es la autoridad.
- `brandingToForm(b)`, `sameBrandingForm(a, b)` (para el "cambios sin guardar") y
  `buildBrandingFormData(form, base: AccountBranding)`, que arma la `metadata` y adjunta
  `logo`/`isotype` solo con `replace`.
- `brandingErrorMessage(err)`, que mapea `ApiError` a un texto:
  - `stale_update` → `BRANDING_MESSAGES.stale_update`;
  - red → `SERVER_UNREACHABLE`;
  - si no, el `error` del body.

Estado del formulario:

```ts
interface BrandingFormValues {
  organization_name: string;
  contact_lines: string[];                // en edición pueden estar vacías
  primary_color: string;                  // "" = Factum; si no, "#RRGGBB"
  accent_color: string;
  logo: { action: BrandingImageAction; file: File | null; previewUrl: string | null; width?: number; height?: number; size?: number };
  isotype: { … mismo … };
}
```

### 9.3 Store de la marca propia (`hooks/useMyBranding.ts`, nuevo)

Store a nivel de módulo (un solo fetch compartido), con `useSyncExternalStore` o un patrón
de suscripción equivalente:

- estado `{ dni: string | null, branding: AccountBranding | null, logoSrc: string | null, status }`;
- `useMyBranding(dni: string | null)`:
  - si `dni` cambia (otro login en la misma pestaña), **descarta** el estado anterior y
    revoca el object URL. Nunca muestra la marca de la sesión previa;
  - carga `getMyBranding()` y, si hay `logo`, `getBrandingImage(logo.url)` → `URL.createObjectURL`;
- `setMyBranding(next: AccountBranding)`: lo llama el diálogo al guardar (o el panel si
  `target.dni === selfDni`). Actualiza el store, recarga el logo si cambió `version` y
  revoca el anterior. Así el `UserMenu` se actualiza **sin recargar**;
- en logout (`useAuth.handleLogout`), `resetMyBranding()`.

Si falla, queda `branding: null`, y el `UserMenu` cae a la config pública, como hoy.

### 9.4 Componentes nuevos (`components/branding/`)

- **`BrandingDialog.tsx`**:
  `{ visible, onHide, target: { kind: "self" } | { kind: "account"; userId: string; name: string; dni: string }, onSaved?(b: AccountBranding) }`.
  - `Dialog` de Prime con el mismo `pt` que `ExpertProfileDialog`: ancho
    `w-[min(64rem,100%)]` y pantalla completa en `max-sm`.
  - Encabezado: "Marca del informe" o "Marca de {name}".
  - Subtítulo secundario fijo: "Se aplica a los informes que se generen desde ahora. Los
    informes ya generados no cambian."
  - En cada apertura: `get…Branding` → `brandingToForm`.
  - Estados: skeleton al cargar; `FxBanner` de error con "Reintentar" si falla la red.
  - Sin marca (`exists=false`), un aviso informativo:
    - si `usePublicConfig().organizationName` o el logo de la instalación existen: "Todavía no
      cargaste tu marca. Tus informes salen con la marca predeterminada de la instalación."
      más la frase de DP1;
    - si no: "Todavía no cargaste tu marca. Tus informes salen sin membrete."
    - En la variante superadmin, en tercera persona ("Esta cuenta todavía no cargó su marca…").
    - Con `suggested_organization_name` y el nombre vacío, ofrece un botón-chip "Usar
      «{sugerencia}»", que solo lo copia al campo y no escribe nada.
  - Footer:
    - "Cancelar", que pide confirmación con `components/overlay/ConfirmDialog` (mismo patrón que `AccountFormDialog`) si `!sameBrandingForm`;
    - "Guardar marca" / "Guardando…", deshabilitado con errores de cliente o mientras carga.
  - Al guardar:
    - `changed=true` → toast "Marca guardada. Se aplica a los próximos informes.";
    - `changed=false` → toast "No había cambios para guardar.";
    - en los dos casos `onSaved` y, si es la propia, `setMyBranding`, y se cierra.
  - Errores:
    - 400: error en el campo (`field`/`index`), con `aria-invalid` y foco al primero;
    - 409: `FxBanner` con el texto de `stale_update` y el botón "Recargar", que vuelve a
      pedir la marca y descarta lo editado;
    - 413 o red: `FxBanner`.
  - Libera los object URLs de las previsualizaciones al cerrar.
- **`BrandingForm.tsx`**: grilla de dos columnas desde `lg` (formulario | vista previa
  `sticky`) y una columna en mobile, con la vista previa debajo. Campos de §"Formulario" de la
  HU, contador `n/150` y las ayudas exactas de la HU.
- **`BrandingImageField.tsx`**: zona de arrastrar y soltar + botón "Elegir archivo"
  (`<input type="file" accept="image/png,image/jpeg">`).
  - Se opera con teclado: Enter/Espacio abren el selector, y tiene `aria-describedby` a la
    ayuda.
  - Miniatura sobre fondo blanco con el peso y las dimensiones, y los botones "Reemplazar" y
    "Quitar".
  - La imagen actual se carga con `getBrandingImage(url)` (blob). "Quitar" pone
    `action="remove"`, y elegir un archivo válido pone `action="replace"`.
  - Si el archivo no es válido, se rechaza en el momento con el mensaje y no cambia el
    `action`.
- **`ContactLinesField.tsx`**: lista con "Agregar línea" (deshabilitado con 6), "Quitar" por
  línea y el placeholder de la HU.
- **`ColorField.tsx`**: `<input type="color">` + campo hex `#RRGGBB`. Al lado, un badge con
  texto **e ícono** ("Contraste 7.2:1" con `Check`, o "2.1:1, mínimo 4.5:1" con
  `AlertTriangle`), dentro de una región `aria-live="polite"`. Si el valor es `""`, muestra el
  swatch del default con "Color de Factum". Debajo de los dos campos va el botón "Usar
  colores de Factum", que pone los dos en `""`.
- **`BrandingPreview.tsx`**: mini-hoja A4 (`aspect-[210/297]`, fondo blanco y tinta
  `#0E1013` fijos, sin depender del tema). Tiene:
  - membrete: el logo si hay; si no, el nombre;
  - las líneas de contacto;
  - un filete y un número de sección en el primario efectivo;
  - una fila de tabla con fondo de acento;
  - el isotipo al cierre;
  - la nota "Vista aproximada. El informe final respeta la plantilla.".

  Si el color no es válido, usa el default de Factum (igual que el informe). Las imágenes
  llevan `alt` descriptivo y el contenedor tiene `aria-label="Vista previa del informe"`.

### 9.5 `UserMenu.tsx` + `AppNavbar.tsx` — D5

- `UserMenu` recibe la prop nueva `onOpenBranding?: () => void` y agrega el ítem **"Marca del
  informe"** (ícono `Palette`) **después** de "Mi perfil de perito".
- El ítem de organización usa `useMyBranding(user.dni)`:
  - si `branding?.exists`, muestra `organization_name` y `logoSrc` de la cuenta. Si los dos
    están vacíos, no hay ítem: no cae a la marca de la instalación, porque los informes de
    esa cuenta tampoco la usan (D4);
  - si no, usa `usePublicConfig()`, como hoy (la cuenta usa el fallback).
- `AppNavbar`: estado `brandingOpen` + `<BrandingDialog target={{ kind: "self" }} …/>`.
  `onOpenBranding` se pasa siempre que se muestre el `UserMenu`, en todos los modos (D7-A).
- `useAuth.handleLogout` llama a `resetMyBranding()`.
- `LoginHero`, `SiteFooter`, `GenerateStep` y `usePublicConfig` **no cambian** (D6).

### 9.6 Panel `/admin/cuentas` — D1-C

- `AccountActionsMenu`: `AccountAction` suma `"branding"`, con el ítem "Editar marca"
  (`Palette`) después de "Editar". Está disponible para cualquier cuenta, incluida la propia
  y las suspendidas.
- `AccountDetailDialog`: botón "Editar marca" junto a "Editar" →
  `onAction("branding", user)`.
- `AdminAccountsScreen.handleAction`, `case "branding"`: abre
  `<BrandingDialog target={{ kind: "account", userId, name, dni }} />`. En `onSaved`:
  - incrementa `historyKey` para recargar el historial;
  - si `dni === user.dni` (el superadmin editó la suya), `setMyBranding(b)`.
- `lib/admin-accounts.ts`:
  - `ACTION_LABELS.update_branding = "Marca del informe"`;
  - `FIELD_LABELS`: `organization_name: "Nombre en el informe"`, `contact_lines: "Contacto"`,
    `primary_color: "Color primario"`, `accent_color: "Color de acento"`, `logo: "Logo"` e
    `isotype: "Isotipo"`;
  - `formatChangeValue`:
    - `primary_color`/`accent_color` vacío → "Color de Factum";
    - `logo`/`isotype` vacío → "(sin imagen)", y con valor → "versión {v}";
    - el resto, como hoy.
- `AccountHistory`: `ACTION_ICONS.update_branding = Palette`, y `expandable` incluye
  `update_branding`.

### 9.7 Accesibilidad (checklist del implementador)

- El diálogo atrapa el foco y lo devuelve al disparador (Prime).
- Las zonas de subida se operan con teclado y tienen `aria-describedby` a la ayuda de
  formato.
- Los errores llevan `aria-invalid`, `aria-describedby` al mensaje y foco al primero.
- El contraste se anuncia con `aria-live="polite"`, y su badge no depende solo del color.
- Las áreas táctiles miden ≥ 44 px en mobile.
- Se respeta `prefers-reduced-motion` en las animaciones.

---

## 10. Checklist atómico

### 10.1 `implementer-backend` (`server/src/Factum.Backend`, `server/tests/Factum.Backend.Tests`)

**Modelo y repositorio**

- [ ] B1. `Models/AccountBranding.cs` con `AccountBranding` y `AccountBrandingImage` (§4.1).
- [ ] B2. `Models/UserAdminEvent.cs`: `UserAdminActions.UpdateBranding = "update_branding"`.
- [ ] B3. `Infrastructure/AccountBrandingRepository.cs`: interfaz + implementación de §6.1.
  La proyección de `FindMetaAsync` excluye `Logo.Data` e `Isotype.Data`. `TryInsertAsync`
  atrapa `DuplicateKey` y devuelve false. `UpdateIfUnchangedAsync` filtra por `_id` +
  `UpdatedAt`. No hay delete.

**Reglas, errores y diff**

- [ ] B4. `Services/Branding/BrandingErrors.cs`: códigos (`image_not_found`,
  `request_too_large` y los reutilizados de `AdminErrors`), nombres de campo y textos de §5.1.
- [ ] B5. `Services/Branding/BrandingRules.cs` (§5): validación + normalización y
  `TruncatedContrast(double)`.
- [ ] B6. `Services/Branding/BrandingChanges.cs` (§6.4) con lista blanca.
- [ ] B7. `AdminErrors.KindFor` mapea `image_not_found` → `NotFound` y `request_too_large` →
  `PayloadTooLarge`. No cambia nada de #12.

**Servicios**

- [ ] B8. `Services/Branding/ReportBrandingResolver.cs` (§6.2).
- [ ] B9. `Services/Branding/AccountBrandingService.cs` (§6.3), con auditoría,
  `changed:false` sin escritura, el caso "todo vacío sin documento" y el control optimista.
- [ ] B10. `Services/Branding/BrandingLimits.cs` (`MaxRequestBytes = 2_621_440`).

**API**

- [ ] B11. `DTOs/BrandingDtos.cs` (§7).
- [ ] B12. `Controllers/BrandingController.cs` (§6.5), con el helper multipart (§6.5.1) y el
  de imágenes (§6.5.2) compartidos con el controller del panel (por ejemplo,
  `Controllers/BrandingHttp.cs`).
- [ ] B13. `Controllers/AdminBrandingController.cs`, con los filtros en el mismo orden que
  `AdminUsersController`.
- [ ] B14. `ReportService` (§6.6): cambio de dependencia y resolución antes de escribir
  artefactos, en los dos flujos.
- [ ] B15. `Program.cs` (§6.8).
- [ ] B16. Los `<summary>` de `ConfigController`, `BrandingService` y `BrandingOptions` dicen
  "default de la instalación".
- [ ] B17. `README.md`, sección "Identidad de la organización (Branding)": explica que
  `Branding` es el default de la instalación (login + cuentas sin marca) y que cada cuenta la
  edita desde la web. Una nota: en la nube conviene dejar `Branding` vacío.

**Tests** (`server/tests/Factum.Backend.Tests/Branding/`, datos ficticios, DNIs `99000001…`)

- [ ] B18. Ajustar `ReportTestSupport.FakeBranding` y `ReportDesignTests.FakeBranding` para que
  implementen también `IReportBrandingResolver` (§6.6). Ningún assert cambia.
- [ ] B19. `BrandingRulesTests`:
  - nombre de 150/151 caracteres;
  - nombre con `\n`;
  - 6/7 líneas;
  - líneas vacías descartadas;
  - línea de 151 con su `index`;
  - colores: `#1f3a93` válido → `1F3A93`, `#FFFF00` → contraste 1.0 (truncado), `zzz` →
    formato, `""` → default;
  - acento `#123456` rechazado;
  - imágenes: PNG válido; JPEG mínimo válido (`TestImages`); bytes al azar → formato;
    1 MiB + 1 → `image_too_big`; 15×20 y 4097×16 → `image_size` con el texto exacto;
  - truncado del contraste: un ratio de 4.49 → `4.4`.
- [ ] B20. `BrandingChangesTests`:
  - sin cambios → lista vacía;
  - misma imagen con la misma versión → sin cambio;
  - `remove` → `to = ""`;
  - las líneas se unen con `\n`;
  - los colores salen con `#` y `""` para el default;
  - ningún campo fuera de la lista blanca.
- [ ] B21. `AccountBrandingServiceTests`, con repositorios fake en memoria
  (`InMemoryAccountBrandingRepository`, un fake de `IUserAdminEventRepository` que guarda los
  inserts y un `IUserRepository` fake):
  - primer guardado → inserta y audita `update_branding` con `ActorDni == TargetDni` y
    `TargetUserId = users._id`;
  - guardado sin cambios → `changed:false`, sin escritura y sin evento;
  - formulario vacío sin documento → no crea nada;
  - `expected_updated_at` viejo → 409 `stale_update`, sin escritura y sin evento;
  - documento existente sin `expected_updated_at` → 409;
  - `TryInsert` que devuelve false → 409;
  - 400 con `field`/`index`/`contrast` correctos y nada escrito;
  - el panel en un modo distinto de `local` → `not_available`; un `id` inexistente →
    `user_not_found`;
  - `dev`: guardar la propia funciona, la auditoría lleva `TargetUserId = ""` y
    `suggested_organization_name` es null sin tocar `users` (el fake de users tira si lo
    llaman).
  - los `Changes` del evento nunca contienen bytes: cada `from`/`to` ≤ 1000 caracteres y las
    imágenes cumplen `^[0-9a-f]{12}$|^$`.
- [ ] B22. `ReportBrandingResolverTests`:
  - sin documento → `installation.Current`, la misma instancia;
  - documento con nombre vacío y sin logo → snapshot sin nombre ni logo, **sin** caer a la
    instalación aunque la instalación tenga logo (D4-A estricto);
  - colores `""` → los defaults de Factum;
  - imagen corrupta en la base → `Logo = null` sin excepción;
  - DNI vacío o no válido → la instalación, sin consultar el repositorio;
  - una sola llamada a `FindAsync` por `ResolveAsync`;
  - excepción del repositorio → se propaga.
- [ ] B23. **`ReportOwnerBrandingDocxTests`** (pedido explícito): un `ReportService` real con
  la plantilla v6, en una carpeta temporal propia, y el resolver real sobre el repositorio en
  memoria, con:
  - una marca `A` para `99000001`: "Estudio Ficticio A", primario `1F3A93`, acento `E6ECFA`,
    logo PNG 64×32;
  - una marca `B` para `99000002`: "Perito Ficticio B", primario `7A1F1F`, sin logo;
  - la instalación: "Instalación Ficticia".

  Casos:
  1. `GenerateAsync` con `Officer.Dni = 99000001`. El texto de `document.xml` + headers +
     footers contiene "Estudio Ficticio A" y `1F3A93`. **No** contiene "Perito Ficticio B",
     `7A1F1F` ni "Instalación Ficticia". El header lleva una imagen (membrete con logo).
  2. `GenerateReportAsync` (flujo agent) con `99000002` → "Perito Ficticio B" y `7A1F1F`, sin
     A ni la instalación. Membrete sin logo.
  3. `99000003`, sin documento → "Instalación Ficticia" (fallback D4).
  4. La atribución de Factum (`factum-sello`) está en el pie en los tres casos (D13).
- [ ] B24. `BrandingControllerTests` (contrato, sin Mongo, como `AdminContractTests`):
  - el JSON de `AccountBrandingDto` serializado con snake_case trae exactamente las claves de
    §8.1;
  - `StatusFor` de cada código;
  - los headers de la imagen: ETag, `private, max-age=86400`, `nosniff`, y 304 con
    `If-None-Match`.
- [ ] B25. `MongoBrandingIntegrationTests` (`[MongoFact]`, base propia `factum_test_<guid>`
  que se borra al final, como `MongoAdminIntegrationTests`):
  - insert → `FindMetaAsync` sin `Data`;
  - `TryInsert` duplicado → false;
  - `UpdateIfUnchanged` con un `UpdatedAt` viejo → false, y con el correcto → true;
  - `keep` conserva los bytes y `remove` deja null;
  - `FindImageAsync` trae solo la imagen pedida.

### 10.2 `implementer-frontend` (solo `client/`; `agent-ui/` **no** se toca)

- [ ] F1. Leer `client/AGENTS.md` e invocar las skills obligatorias. Dejarlo anotado en el
  progress.
- [ ] F2. `lib/api.ts`: los tipos y métodos de §8.1/§9.1, `ApiErrorBody` ampliado y
  `AdminAction` con `update_branding`. `types/index.ts`: reexportarlos.
- [ ] F3. `lib/branding.ts` (§9.2), con los textos idénticos a §5.1 y las constantes de §8.4.
- [ ] F4. `hooks/useMyBranding.ts` (§9.3), con reset por cambio de DNI y en logout.
- [ ] F5. `components/branding/ColorField.tsx`, `ContactLinesField.tsx` y
  `BrandingImageField.tsx`.
- [ ] F6. `components/branding/BrandingPreview.tsx`.
- [ ] F7. `components/branding/BrandingForm.tsx` + `BrandingDialog.tsx` (estados, errores,
  confirmación al cancelar y sugerencia de D10).
- [ ] F8. `UserMenu.tsx`: ítem "Marca del informe" y organización de la cuenta (§9.5).
  `AppNavbar.tsx`: el diálogo. `useAuth.ts`: `resetMyBranding()` en logout.
- [ ] F9. Panel: `AccountActionsMenu` ("Editar marca"), `AccountDetailDialog` (botón),
  `AdminAccountsScreen` (diálogo, `historyKey` y `setMyBranding` si es la propia).
- [ ] F10. `lib/admin-accounts.ts` (labels y `formatChangeValue`) + `AccountHistory.tsx`
  (ícono y expandible).
- [ ] F11. Autochequeo con `web-design-guidelines` y la checklist de §9.7. Probar mobile
  (pantalla completa) y los temas claro y oscuro: la vista previa queda siempre en blanco.

---

## 11. Decisiones técnicas

- **DT1. La llave es el DNI y no hay índice extra** (D7-A). `_id` = DNI cubre todas las
  consultas. El panel entra por `users._id` y lo traduce a DNI.
- **DT2. Sin caché de la marca en el backend.** El resolver hace una lectura por `_id` por
  generación (≤ 2 MiB, poco frecuente), y el `GET` de metadatos no trae bytes. Así no hace
  falta invalidar nada y funciona igual con varias instancias en la nube. Las imágenes para
  la web se cachean en el navegador con ETag + `?v=`.
- **DT3. Si falla la lectura de la marca, falla la generación.** No se cae en silencio a la
  instalación: un informe pericial con la marca de otro emisor es peor que un reintento.
- **DT4. Las imágenes se sirven con `Cache-Control: private, max-age=86400` + ETag**, y `?v=`
  solo invalida la caché. `private` porque es contenido de una cuenta. Se pide con
  `Authorization` por `fetch`/blob, nunca con `?token=`.
- **DT5. Un solo `PUT` multipart con la `metadata` + las imágenes** y `logo_action` /
  `isotype_action` explícitos (`keep`/`replace`/`remove`). Se elige así por tres razones: un
  guardado da un solo evento de auditoría, el control optimista es uno solo, y la escritura es
  un solo `UpdateOne` atómico, así que el informe nunca ve una marca a medias.
- **DT6. En `dev`/`external` se audita con `TargetUserId = ""`.** Queda el registro de quién
  cambió qué, aunque no haya panel donde mostrarlo. No se consulta `users`.
- **DT7. Guardar vacío sin marca previa no crea el documento.** Preserva el fallback de D4
  para quien abre y guarda sin cargar nada. Con la marca ya creada, vaciar los campos sí se
  guarda y el informe sale sin membrete (D4-A estricto).
- **DT8. El `UserMenu` muestra la marca efectiva.** Si la cuenta tiene marca, la suya (aunque
  esté vacía). Si no, la de la instalación. Coincide con lo que van a mostrar sus informes.
- **DT9. Validación estricta con un solo error por respuesta, como #12.** El client valida
  todo antes de enviar. El contraste se muestra truncado (no redondeado) en los dos lados.
- **DT10. `BrandingService` (appsettings) no cambia.** Sigue tolerante y se carga al
  arrancar. Las reglas estrictas viven aparte (`BrandingRules`) y comparten las constantes.
- **DT11. Tope de 2,5 MiB por request.** Deja que una imagen apenas mayor a 1 MiB reciba el
  error de campo exacto en lugar de un 413 genérico.

## 12. Decisiones pendientes del usuario

- **DP1. Una cuenta sin marca que guarda solo un dato pierde el resto de la marca de la
  instalación.**
  - El caso: en una instalación dedicada con `Branding` en `appsettings` (nombre, logo y
    contacto), si el cliente entra a "Marca del informe" y guarda solo un color, desde ese
    momento sus informes salen sin el nombre, el logo y el contacto de la instalación. Es lo
    que define D4-A ("si la cuenta guardó su marca alguna vez, se usa solo la suya").
  - Opciones:
    - **A)** mostrar un aviso en el formulario cuando la cuenta no tiene marca y la
      instalación sí: "Cuando guardes, tus informes van a usar solo lo que cargues acá, en
      lugar de la marca predeterminada de la instalación." No hay precarga;
    - **B)** precargar en el formulario el nombre, el contacto y los colores de la
      instalación, y las imágenes con la acción "copiar". Esto expone las líneas de contacto
      de la instalación, que hoy no son públicas, a todas las cuentas;
    - **C)** sumar un botón "Volver a la marca de la instalación" que borre el documento.
  - **Recomendada: A.** En la nube, `Branding` queda vacío y el problema no existe. En una
    instalación dedicada hay un solo cliente, que puede cargar todo de una vez. B y C agregan
    endpoints y casos borde por un escenario poco común.
  - La SDD queda escrita con A (§9.4). Si el usuario elige otra, cambian §9.4 y, para C, un
    `DELETE` nuevo.

## 13. Concurrencia con otras HU

Toca `ReportService` (solo el constructor y dos líneas), `lib/api.ts`, `UserMenu`,
`AppNavbar`, `admin-accounts.ts` y `AccountHistory`. Si otra HU en curso toca esos archivos,
el segundo PR resuelve el conflicto (AGENTS.md).

## 14. Verificación

### 14.1 `implementer-backend`, antes de declararse `done`

```bash
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
# Opcional, integración Mongo en una base propia (la crea y la borra el test):
FACTUM_TEST_MONGO="mongodb://localhost:27017" dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj --filter "FullyQualifiedName~MongoBranding"
# Sin escrituras fuera de account_brandings / user_admin_events:
grep -rn "GetCollection<" server/src/Factum.Backend/Infrastructure/AccountBrandingRepository.cs
```

Regla dura de datos (AGENTS.md): no escribir en la base de desarrollo
(`factum_dev`/`factum`). Los tests usan repositorios en memoria o `factum_test_<guid>`. Los
DOCX se generan en `Path.GetTempPath()`, nunca en `Storage:DataDirectory`.

### 14.2 `implementer-frontend`, antes de declararse `done`

```bash
cd client && npx tsc --noEmit   # client/ no tiene script de lint
```

Dejar en `progress/impl_frontend_marca-por-cliente.md` constancia de las skills invocadas.

### 14.3 Reviewer

- §5.1 = `BRANDING_MESSAGES`, y las constantes de §8.4 coinciden entre los dos lados.
- Ningún endpoint de marca es anónimo. `/api/branding*` no recibe un DNI ni un id.
- `ReportService` resuelve la marca por `cas.Officer.Dni` en los dos flujos. B23 pasa.
- Ningún log ni evento lleva bytes de imagen.
- `ConfigController` y `LoginHero` no cambiaron (D6).
- `agent-ui/` y `server/src/Factum.Agent` no tienen cambios.

### 14.4 Prueba manual para el usuario (backend en `Auth:Mode=local`)

1. Como cliente: "Marca del informe" en el `UserMenu`.
   - Cargar el nombre, un logo PNG, un isotipo PNG, 3 líneas y los colores `#1F3A93` /
     `#E6ECFA`. La vista previa se actualiza en vivo.
   - Guardar → toast. El `UserMenu` muestra el logo y el nombre sin recargar.
2. Probar errores:
   - un `.gif` renombrado a `.png` → "no es una imagen PNG ni JPEG";
   - un PNG de más de 1 MB;
   - el primario `#FFFF00` → "Contraste 1.0:1 con blanco, mínimo 4.5:1", y no deja guardar
     hasta corregirlo o elegir "Usar colores de Factum".
3. Generar el informe de un caso propio (los dos flujos, si se puede). El DOCX lleva el
   nombre, el logo, el contacto, los colores y el isotipo, y la atribución de Factum al pie.
4. Con otra cuenta cliente sin marca, generar un informe → sale con la `Branding` de
   `appsettings` (o sin membrete, si está vacía).
5. Como superadmin: `/admin/cuentas` → detalle de una cuenta → "Editar marca". Encabezado
   "Marca de {nombre}". Guardar y verificar el historial: "Marca del informe", con los
   campos cambiados y las imágenes como "versión …".
6. Abrir el mismo formulario en dos pestañas, guardar en una y después en la otra → mensaje
   de "se modificó desde otra sesión".
7. Cerrar sesión: el login sigue mostrando la marca de la instalación (o solo Factum).
8. Un caso `Completed` generado antes de cambiar el logo conserva su DOCX y su hash.

> **Decisión del usuario (2026-10-06):** DP1 = A (aviso en el formulario, sin precarga ni botón para volver a la marca de la instalación).
