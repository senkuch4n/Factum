# impl_backend — imei-sentinel-y-log-validacion

Seguimiento del incidente `progress/incidente-2026-10-08-imei-crear-caso.md`.
Dos mejoras técnicas chicas en `server/`. No es una HU. No toca Mongo ni
documentos de negocio. Rama: `chore/imei-sentinel-y-log-validacion`.

## Tarea 1 — Tatana emite el centinela cuando no puede leer el IMEI

Objetivo: que el agente devuelva `INGRESAR_MANUALMENTE` en `device.Imei`
cuando no pudo leer el IMEI, en vez de un string vacío.

Archivos tocados:

- `server/src/Factum.Agent/Models/AgentModels.cs`
  - Nueva constante compartida en la clase `Device`:
    `public const string ImeiManualEntry = "INGRESAR_MANUALMENTE";`
    (con doc-comment). El valor es exactamente el que front y backend ya
    reconocen.
- `server/src/Factum.Agent/Services/AdbService.cs` (`ParseImei`, ~l.464)
  - Tras parsear, si el IMEI queda vacío/blank devuelve `Device.ImeiManualEntry`:
    `return string.IsNullOrWhiteSpace(imei) ? Device.ImeiManualEntry : imei;`
  - El `device.Imei = ParseImei(imeiResult.Stdout);` (l.461) queda igual: la
    lógica del centinela vive dentro de `ParseImei`.
- `server/src/Factum.Agent/Services/IosService.cs` (~l.253)
  - `Imei = Str(item, "imei") is var imei && !string.IsNullOrWhiteSpace(imei) ? imei : Device.ImeiManualEntry,`

No tocado (a propósito):

- Dispositivo mock `AdbService.cs` (`Imei = "000000000000000"`): ya trae un
  IMEI no vacío, queda igual.
- `ParseImei` no se referencia en ningún otro lado del agente (grep
  `ParseImei`/`ImeiManualEntry`/`INGRESAR_MANUALMENTE`): solo en `AdbService`.

Decisión: la constante vive en `Device` (dentro de `Factum.Agent.Models`, que
ambos servicios ya importan) por ser el tipo que porta el campo `Imei`. No se
creó una clase de constantes aparte para no sumar superficie por un único
valor.

## Tarea 2 — Backend loguea las validaciones que terminan en 400

Objetivo: que los 400 de validación queden en el log del backend.

Archivo tocado:

- `server/src/Factum.Backend/Controllers/ResultHttpExtensions.cs`
  - Nuevo helper privado `LogValidation(ControllerBase c, string error, IReadOnlyList<string>? missing)`:
    resuelve el logger desde `c.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Factum.Backend.Validacion")`
    y emite `LogWarning` con plantilla estructurada:
    `"Validación 400 en {Method} {Path}: {Error}. Faltantes: {Missing}"`.
    Devuelve el mismo `ControllerBase` para poder encadenar
    (`LogValidation(...).BadRequest(...)`) dentro del `switch`.
  - En `ErrorResult`: se loguea en el caso `ErrorKind.Validation when missing`
    y en el fallback `_ => BadRequest`.
  - En `DetailedErrorResult`: se loguea solo cuando el `status` resuelto es
    `400` (`if (status == StatusCodes.Status400BadRequest) LogValidation(...)`),
    justo antes de devolver la respuesta.

Privacidad / alcance:

- Solo se loguean método, ruta, mensaje de error (genérico) y las CLAVES
  faltantes (`Join(", ", missing)` o `"ninguno"`). No se loguea ningún valor
  de campo ni el body (posible PII).
- No se tocan cuerpos de respuesta ni status.
- 500 / 404 / 409 / 403 quedan fuera de alcance (no se les agrega log).
  Los dos únicos puntos que resuelven 400 (Validation con missing y el
  fallback Failure) quedan cubiertos.

## Verificación

- `dotnet build server/src/Factum.Agent/Factum.Agent.csproj`
  → Compilación correcta. 0 Advertencias, 0 Errores.
- `dotnet build server/src/Factum.Backend/Factum.Backend.csproj`
  → Compilación correcta. 0 Errores. 4 Advertencias, todas preexistentes
    (NU1902/NU1903: vulnerabilidades conocidas en SharpCompress 0.30.1 y
    Snappier 1.0.0, a nivel de restore NuGet, sin relación con el cambio).
    Ningún warning nuevo de compilación.

No hay proyectos de test en .NET.

## Commits

- `aea8057` fix(agent): Tatana emite el centinela INGRESAR_MANUALMENTE cuando no puede leer el IMEI
- `c654a63` fix(backend): loguear en Warning las validaciones que terminan en 400

Sin push ni PR (lo hace el orquestador). No se movieron tarjetas del Project
ni se tocó `progress/sesiones/`.

## Contrato compartido

El centinela vale exactamente `INGRESAR_MANUALMENTE` (constante
`Device.ImeiManualEntry`), que es el valor que el front y el backend ya
entienden. No se inventó ningún nombre nuevo.
