# Review — auth-e-integraciones-sin-mpf

**Veredicto:** APROBADA

## Verificación propia
- `dotnet build` Backend: 0 errores. Solo warnings NU190x de paquetes (preexistentes); ningún CSxxxx.
- `npx tsc --noEmit` en `client/`: limpio.
- Fail-fast corrido con `dotnet run --no-build` (PORT=18099): `Auth__Mode=extrenal`, `Auth__Mode=external` sin BaseUrl, `Integrations__Support__Enabled=talvez` y `Enabled=true` sin claves terminan con `InvalidOperationException` que nombra la clave y el valor. La ServiceKey no aparece en los mensajes.
- Greps de aceptación: `mpf` en Backend solo en `Services/Auth/AuthSettings.cs` (con `// LEGADO`). `FaroIntegration` solo en `Services/Support/SupportSettings.cs`. En `client/src`, los patrones `"mpf"`, `PublicBranding` y gabinete/fiscalía/uso oficial dan vacío.
- Secretos: `ServiceKey` en `appsettings.json:31` es `""`. En README solo hay placeholders (`<clave-compartida-con-faro>`) y en docker-compose `<clave>`. El valor viejo ya no está en el árbol de trabajo. Sigue en el historial, y eso queda al usuario (rotarlo).
- `client/src/app/page.tsx` y `appsettings.Local.json` sin tocar. `Services/Auth/MpfAuthProvider.cs` borrado (`git rm`, staged). Ningún archivo de `docs/INFORME*` está indexado.

## Checkpoints
- C1 backlog/verify.sh: [x] backlog.json y verify.sh son del orquestador. No hay nada del arnés en el diff de esta HU que lo rompa. No corrí verify.sh.
- C2 cadena de documentos: [x] HU y SDD existen. Contrato campo a campo: `mode` (`"dev"|"external"`) en `ExternalAuthOptions.cs` (`AuthModes`) ↔ `api.ts` `AuthMode`. `support_enabled`: `ConfigDtos.cs` `bool SupportEnabled` ↔ `api.ts` `PublicConfig.support_enabled: boolean`. Casing snake_case_lower por la política de `Program.cs`. `/health.auth_mode` usa el modo normalizado.
- C3 arquitectura: [x] Solo Backend y client (agent-ui y Tatana sin tocar). No hay cambios en Mongo. Sin logs sensibles: la contraseña, el body del request y el DNI no se loguean; el body de 5xx se trunca a 200 caracteres.
- C4 verificación real: [x] build y tsc limpios. Para esta HU no se pidieron tests (D-T12).
- C5 cierre: [x] Existen ambos progress. El frontend deja constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` y `web-design-guidelines`. No quedan temporales.

## Puntos específicos
- D1: `Auth:Mode=mpf` y `Auth:Mpf*` se mapean a `external` con warning (`AuthSettings.cs:40-43,64-87`). Si hay clave nueva y legada, gana la nueva y avisa.
- D2: un modo desconocido o una config incompleta tira excepción antes de `Build()` (`Program.cs:52-58`). Con el modo por defecto o `dev`, solo se emite un warning si no es Development.
- DP1 A: `BaseUrl.TrimEnd('/') + "/" + LoginPath.TrimStart('/')`. DP2 A: solo 401/403 dan "Credenciales inválidas". `User.Dni` es el DNI tipeado (`ExternalHttpAuthProvider.cs`).
- Soporte apagado: `DisabledSupportService` sin `HttpClient` (no se registra el cliente de Faro). `RequireSupportEnabledFilter` da 404 `{error}` en la clase `SupportController`, y corre antes del binding. El cliente oculta el dock (`dashboard/page.tsx` spread condicional), el `onSupport` de la guía y el modal con `supportEnabled`.
- Integridad de `informe-pericial-de-parte`: perfil del perito (`IExpertProfileRepository` y service en `Program.cs`), `UserMenu` (`onOpenProfile` intacto), `api.ts` y `types` se mantienen. Solo se agregaron `AuthMode` y `support_enabled`.
- Los literales legados están en un único archivo por área.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- F13/F15 no se aplicaron: `GuideModal.tsx:67` dice "Guía de uso de Factum" (no "Guía de conexión USB") y el showcase ya estaba neutralizado ("Tribunal"). El progress lo justifica con la HU anterior y el grep de D12 da vacío. Lo acepto.
- Los escenarios 4 a 8 (login contra el proveedor falso, 404 de soporte con JWT) los verifiqué por lectura de código y no los corrí de punta a punta. No levanté el backend completo porque hay un backend vivo en el 8080 y Mongo. Se recomienda la prueba manual 11.4.
- `HttpClient` cautivo dentro del singleton `AuthService`: preexistente (H4), sin impacto.
- La `ServiceKey` histórica sigue en el historial de git. Hay que rotarla fuera del repo (11.4 punto 6).
