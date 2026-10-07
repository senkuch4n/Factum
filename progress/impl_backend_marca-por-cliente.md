# impl_backend — marca-por-cliente (#13)

**Estado:** done
**Rama:** `feat/marca-por-cliente` (sin commit, como se pidió)
**Alcance:** checklist §10.1 (B1-B25) de `Refactorizaciones/marca-por-cliente.md`. DP1 = A, que no afecta al backend.
Tatana (`server/src/Factum.Agent`), `client/` y `agent-ui/` no se tocaron. `server/src/Factum.Agent/appsettings.json` conserva el cambio local del usuario y no lo toqué.

## Archivos

### Nuevos (backend)
| Archivo | Ítem |
|---|---|
| `server/src/Factum.Backend/Models/AccountBranding.cs` | B1: `AccountBranding` + `AccountBrandingImage`, con `[BsonIgnoreExtraElements]` |
| `server/src/Factum.Backend/Infrastructure/AccountBrandingRepository.cs` | B3: interfaz + implementación, `BrandingImageKind`, `BrandingImageAction`, `ImageChange`, `BrandingUpdate`. Sin delete. Un solo `GetCollection<AccountBranding>("account_brandings")` |
| `server/src/Factum.Backend/Services/Branding/BrandingErrors.cs` | B4: códigos, campos, claves extra y textos de §5.1 |
| `server/src/Factum.Backend/Services/Branding/BrandingRules.cs` | B5: `Validate`, `ValidateImage`, `TruncatedContrast`, `VersionOf` + `BrandingSaveInput`, `NormalizedBranding`, `BrandingFieldError` |
| `server/src/Factum.Backend/Services/Branding/BrandingChanges.cs` | B6: diff con lista blanca |
| `server/src/Factum.Backend/Services/Branding/BrandingLimits.cs` | B10: `MaxRequestBytes = 2_621_440` |
| `server/src/Factum.Backend/Services/Branding/ReportBrandingResolver.cs` | B8 |
| `server/src/Factum.Backend/Services/Branding/AccountBrandingService.cs` | B9 (+ `BrandingActor`) |
| `server/src/Factum.Backend/DTOs/BrandingDtos.cs` | B11 |
| `server/src/Factum.Backend/Controllers/BrandingHttp.cs` | B12: helper compartido (`StatusFor`, cuerpo de error, lectura multipart §6.5.1 e imagen §6.5.2) |
| `server/src/Factum.Backend/Controllers/BrandingController.cs` | B12: `/api/branding`, `/logo`, `/isotype` (GET/HEAD) |
| `server/src/Factum.Backend/Controllers/AdminBrandingController.cs` | B13: `/api/admin/users/{id}/branding*`, con los filtros en el mismo orden que `AdminUsersController` |

### Modificados (backend)
| Archivo | Ítem |
|---|---|
| `Models/UserAdminEvent.cs` | B2: `UserAdminActions.UpdateBranding = "update_branding"` |
| `Services/Admin/AdminErrors.cs` | B7: `KindFor` agrega `image_not_found` → `NotFound` y `request_too_large` → `PayloadTooLarge`. Lo de #12 no cambia |
| `Services/Reports/ReportService.cs` | B14: dependencia `IReportBrandingResolver brandings`, en los dos constructores (el `internal` con `templateFileName` se mantiene). `GenerateAsync` resuelve `cas.Officer?.Dni` al principio, antes de borrar restos y de escribir ZIP/DOCX. `GenerateReportAsync` lo hace antes del `try` que escribe el DOCX |
| `Program.cs` | B15: los 3 singletons de §6.8 |
| `Controllers/ConfigController.cs`, `Services/Branding/BrandingService.cs`, `Services/Branding/BrandingOptions.cs` | B16: solo `<summary>` ("default de la instalación: login y fallback"). Sin cambios de comportamiento |
| `README.md` | B17: sección "Identidad de la organización (Branding)", con los dos niveles y la nota de dejar `Branding` vacío en la nube |

### Tests (`server/tests/Factum.Backend.Tests`)
| Archivo | Ítem |
|---|---|
| `ReportTestSupport.cs`, `ReportDesignTests.cs` | B18: las dos `FakeBranding` implementan además `IReportBrandingResolver` (`ResolveAsync → Current`). Ningún assert cambió |
| `Branding/BrandingTestDoubles.cs` | `InMemoryAccountBrandingRepository` (copias, `FindMeta` sin bytes, contadores, `InsertReturnsFalse`, `FindThrows`), `FixedInstallationBranding`, `BrandingTestData` (DNIs 99000001-3) |
| `Branding/BrandingRulesTests.cs` | B19 |
| `Branding/BrandingChangesTests.cs` | B20 |
| `Branding/AccountBrandingServiceTests.cs` | B21 (reusa `InMemoryUserRepository`, `InMemoryUserAdminEventRepository`, `MutableTimeProvider` y `CapturingLogger`) |
| `Branding/ReportBrandingResolverTests.cs` | B22 |
| `Branding/ReportOwnerBrandingDocxTests.cs` | **B23**: DOCX con la marca del dueño del caso |
| `Branding/BrandingControllerTests.cs` | B24, más multipart y filtros |
| `Branding/MongoBrandingIntegrationTests.cs` | B25 (`[MongoFact]`, base `factum_test_<guid>`, se borra al final) |

## Verificación

```
$ dotnet build server/src/Factum.Backend/Factum.Backend.csproj --no-incremental
    4 Advertencia(s)      ← todas NU1902/NU1903 (SharpCompress / Snappier), ya estaban antes; 0 warning CS
    0 Errores

$ dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
Correctas! - Con error: 0, Superado: 790, Omitido: 7, Total: 797     (línea de base antes del cambio: 687 superados / 4 omitidos)
  (los 7 omitidos son [MongoFact] sin FACTUM_TEST_MONGO: 4 de #12 + 3 de esta HU)

$ FACTUM_TEST_MONGO="mongodb://localhost:27017" dotnet test … --filter "FullyQualifiedName~MongoBranding"
Correctas! - Con error: 0, Superado: 3, Omitido: 0, Total: 3
  → después, listDatabases: admin config evidentia evidentia_dev factum_dev local (no quedó ninguna factum_test_*)

$ grep -rn "GetCollection<" server/src/Factum.Backend/Infrastructure/AccountBrandingRepository.cs
61: …GetCollection<AccountBranding>(CollectionName);   ← solo account_brandings
```

Las warnings CS8714/CS8619 que muestra el build de tests vienen de `EvidenceZipTests.cs`, que no toqué.

**B23 (DOCX con la marca del dueño), `ReportOwnerBrandingDocxTests`:** `ReportService` real con la plantilla v6, resolver real y repositorio en memoria, todo en `Path.GetTempPath()`.
- `GenerateAsync` con el dueño 99000001 → "Estudio Ficticio A" + `1F3A93` + logo en el pie de portada. No aparecen B, `7A1F1F` ni "Instalación Ficticia".
- `GenerateReportAsync` (flujo agent) con 99000002 → "Perito Ficticio B" + `7A1F1F`, sin logo y sin A ni la instalación.
- 99000003, sin documento, en los dos flujos → "Instalación Ficticia" (D4).
- Los tres casos llevan "Realizado con Factum" y el `factum-sello` en todos los pies (D13).
- Caso extra: si el resolver tira, la generación falla y no deja ni ZIP ni DOCX (DT3).

**E2E manual contra una API levantada por mí.** Puerto propio `:18099`, `Auth:Mode=local`, base propia `factum_e2e_marca` y `Storage:DataDirectory` en el scratchpad. Se recorrieron 30 chequeos y todos pasaron:
- gate con la temporal → 403; sin token → 401;
- GET sin marca con `suggested_organization_name`, claves del contrato y `no-store`;
- 400 de contraste con `contrast`/`min_contrast` y texto exacto, 400 con `index`, 400 de formato, 400 `image_too_big` con 1 MiB+, 413 `request_too_large` con `max_bytes`;
- PUT inicial; logo con ETag, `private, max-age=86400`, `nosniff` y CSP sandbox; 304 con If-None-Match; HEAD; isotipo 404 `image_not_found`;
- `changed:false`; 409 sin `expected_updated_at` y con un token viejo;
- panel: cliente → 403 `superadmin_required`, GET/PUT/logo del panel, id inexistente → 404 `user_not_found`;
- historial con 3 `update_branding`, sin bytes y con el actor correcto.

El log muestra solo `Marca guardada para DNI … por … (campos: …)`, sin textos ni bytes. Después detuve el backend por su PID (28930, el que lancé yo) y borré **solo** `factum_e2e_marca`. `factum_dev` no se tocó: sigue sin `account_brandings`. No toqué ningún proceso del usuario (`:8080`, Tatana, `next dev`).

## Contrato (§8): coincide con la SDD
- `AccountBrandingDto` serializado: `exists, organization_name, contact_lines, primary_color, accent_color, logo, isotype, updated_at, updated_by, suggested_organization_name`.
- `BrandingImageDto`: `url, content_type, width, height, size, version`.
- Respuestas: `{ branding }` / `{ branding, changed }`. Lo verifica el test y también el E2E.
- `metadata` del PUT: `organization_name, contact_lines, primary_color, accent_color, logo_action, isotype_action, expected_updated_at`. Partes de archivo: `logo`, `isotype`.
- Errores: `{ error, code[, field][, index][, contrast][, min_contrast][, max_bytes] }`, con los HTTP de §6.5.
- `url`: propia `/api/branding/{logo|isotype}?v=<version>`; panel `/api/admin/users/{EscapeDataString(id)}/branding/{logo|isotype}?v=<version>`.
- Auditoría: `action = "update_branding"`, `changes[].field` dentro de la lista blanca, `from`/`to` con el formato de §4.2 (nunca null).
- Textos de §5.1: comparados uno por uno con `client/src/lib/branding.ts` (`BRANDING_MESSAGES`), que el frontend escribe en paralelo. Coinciden.

## Decisiones no obvias
1. **`invalid_request` va como `code: "validation_failed"`, `field: "metadata"`.** §8.1 (`BrandingErrorCode`) no incluye `invalid_request` como código: es solo la clave del texto `MsgInvalidRequest`. Lo usan la falta o invalidez de `metadata`, el content type no multipart, el multipart roto y las acciones inválidas o inconsistentes.
2. **Archivo de más de 1 MiB en el multipart.** El controller no lo lee (§6.5.1) y lo marca en `BrandingSaveInput` (`LogoTooBig`/`IsotypeTooBig`, parámetros opcionales que agregué al record de §6.3). La validación lo informa **en su turno**, para respetar el orden `metadata → … → logo → isotype` y que un nombre inválido no quede tapado por el peso. Un archivo con `keep`/`remove` sigue siendo `metadata` aunque pese de más.
3. **Orden de las reglas de imagen.** La tabla de §5 lista "formato" antes que "peso", pero el texto dice "el peso primero". Lo implementé así: vacío → formato, > 1 MiB → `image_too_big`, ImageProbe → formato, lados → `image_size`. Con esto, 1 MiB + 1 de bytes al azar da `image_too_big` (B19).
4. **`InvalidDataException` al leer el form.** Si el mensaje habla de un límite ("Multipart body length limit … exceeded"), es 413 `request_too_large`. Cualquier otra (un multipart vacío o roto) es 400 `invalid_request`. La SDD mapeaba toda `InvalidDataException` a 413, y eso daba 413 ante un cuerpo simplemente mal formado: lo encontró un test.
5. **`replace` con la misma versión que la actual** se convierte en `keep` antes del diff y de la escritura: no es cambio y no reescribe los bytes.
6. **Acciones de imagen:** exactas y en minúsculas. `null` o `"KEEP"` → `invalid_request`.
7. **Colores vacíos:** si un color viene solo con espacios, se trata como `""` (default), igual que `null`.
8. **Auditoría de la marca propia en `local`.** Si falla la lectura de `users` para resolver el `TargetUserId`, se loguea un warning y se audita con `""`: la auditoría es best-effort, como en #12.
9. **`ReportService.GenerateAsync`** resuelve la marca como paso 0, antes incluso de borrar los restos del intento anterior. Así, ante un fallo de Mongo, no se escribe ni se borra nada.
10. **B23, "el header lleva una imagen".** En la plantilla v6 el membrete con logo va en el **pie de la portada**, no en un header (ya lo verificaba `ReportDesignTests.ConLogo_SoloEnElPieDeLaPortada_EnTabla`). El test chequea que el logo esté en headers/footers con A y que no exista en todo el documento con B.
11. **El resolver es defensivo con las líneas de contacto:** descarta vacías y hace trim aunque ya vengan normalizadas de la escritura.
12. `[DisableFormValueModelBinding]` en los dos PUT: así MVC no lee el formulario antes de que se fijen los topes.

## Bloqueos / pendientes
- Ninguno del lado backend.
- Para el usuario (§14.4): la prueba manual completa con el frontend, que incluye generar un informe real de un caso propio en los dos flujos.
