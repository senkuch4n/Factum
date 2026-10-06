# Review — marca-por-cliente (#13)

**Veredicto:** APROBADA

Verificado por el reviewer: `dotnet build` Backend (0 errores, 4 warnings) y Agent (0 warnings, 0 errores); `dotnet test server/tests/Factum.Backend.Tests` = 790 OK, 7 omitidos (Mongo opcional), 0 fallos; `npx tsc --noEmit` en `client/` limpio.

## Checkpoints
- C1: [x] rama `feat/marca-por-cliente`; el estado del Project y `verify.sh` los maneja el orquestador (no verificados por mí).
- C2: [x] HU, SDD y contrato; los campos C# (`BrandingDtos.cs`) y TS (`client/src/lib/api.ts` ~L363-420) coinciden en snake_case (`organization_name`, `contact_lines`, `primary_color`, `accent_color`, `logo_action`, `isotype_action`, `expected_updated_at`, `suggested_organization_name`, `changed`, `index`, `contrast`, `min_contrast`, `max_bytes`).
- C3: [x] solo backend y client. Modelo `AccountBranding` con `[BsonIgnoreExtraElements]`; colección nueva con `_id` = DNI; no hay delete, y las escrituras filtran por `_id`. Tatana y agent-ui sin cambios.
- C4: [x] build, tsc y tests OK. Hay test del DOCX con la marca del dueño (`Branding/ReportOwnerBrandingDocxTests.cs`).
- C5: [x] reportes presentes. El de frontend deja constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` y `web-design-guidelines`. El test de integración Mongo usa una DB propia (`FACTUM_TEST_MONGO`) y no toca la de desarrollo.

## Puntos verificados
- Autorización: `BrandingController` toma el DNI solo de la sesión (`BrandingController.cs:63`, `BrandingHttp.UserOf`); ninguna ruta propia recibe DNI o id. `AdminBrandingController.cs:84-87` exige `[Authorize]`, `RequireLocalAuthMode` y `RequireSuperadmin`, y el servicio resuelve el DNI desde `users._id` (`AccountBrandingService.cs`). Fuera de local da 404 `not_available`.
- Imágenes: el tipo se detecta por magic bytes y las dimensiones se leen de IHDR/SOF (`ImageProbe.cs`), sin mirar la extensión. Solo PNG/JPEG, así que no hay SVG. Límite de 1 MiB, lado entre 16 y 4096 y tope de request (413). Se sirven con `nosniff`, CSP `sandbox`, `ETag`, `Cache-Control: private, max-age=86400` y `Content-Type` del valor validado (`BrandingHttp.cs:282-296`).
- Resolución: `ReportBrandingResolver` usa la marca de la cuenta y, si no hay, `appsettings` (D4). `ReportService.cs` la resuelve con `cas.Officer?.Dni` en los dos flujos y antes de escribir artefactos (D8). `ConfigController` y el login siguen con el default (D6).
- Colores: el servidor valida con contraste >= 4.5 y el cliente implementa el mismo algoritmo (`client/src/lib/branding.ts`, D11).
- Auditoría: `update_branding` en `user_admin_events` con reintento; sin bytes ni contactos en los logs. Una marca sin cambios no escribe ni audita.
- Control optimista: 409 `stale_update` por `UpdatedAt`; 413 con `max_bytes`; 400 con `field`, `index` y `contrast`.
- Accesibilidad: el campo de archivo usa botones reales operables por teclado; el `input` file va oculto con `sr-only`.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `ImageProbe` valida cabecera y dimensiones, no decodifica la imagen completa. Es suficiente porque se sirve con `nosniff` y `sandbox`, y el DOCX se arma con esos bytes. Un archivo corrupto con cabecera válida podría fallar recién al generar el informe.
- Con `Content-Length` ausente (chunked) el tope lo aplica Kestrel con `MaxRequestBodySize`; está cubierto.
- No corrí `verify.sh` ni miré el Project: son del orquestador.
