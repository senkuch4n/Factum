# Review — metadatos-dispositivo (#79)

**Veredicto:** APROBADA

## Checkpoints
- C1 (arnés): [ ] no verificado por el reviewer. Fase del Project y `verify.sh` quedan a cargo del orquestador; los cambios están sin commitear en `feat/metadatos-dispositivo`.
- C2 (documentos): [x] HU, SDD, impl_backend e impl_frontend existen. Contrato verificado contra el código: `DeviceDetailsDtos.cs`, `Models/DeviceDetails.cs`, `DeviceDetailsModels.cs` de Tatana y `client/src/lib/api.ts` y `agent.ts` usan los mismos nombres snake_case (`device_details`, `clock_start`, `clock_close`, `include_ssid_in_report`, `clock_skew_warning_seconds`, etc.).
- C3 (arquitectura): [x] Solo `server/` (API + Tatana), `client/`, plantillas, deploy y docs; `agent-ui/` no se tocó. El campo `DeviceDetails` es `[BsonIgnoreIfNull]` y un caso viejo deserializa `null`. Las escrituras son `$set` campo por campo bajo `Editable(id)`. No hay `console.*` ni logs con valores o SSID.
- C4 (verificación):
  - [x] `dotnet build` de Backend y Agent: 0 errores. Warnings: solo NU1902/NU1903, preexistentes.
  - [x] `npx tsc --noEmit` en `client/`: limpio.
  - [x] Tests: Agent 704 pasan, Backend 1340 pasan y 10 se saltan (los `MongoFact`, sin variable de entorno).
  - [x] `build_plantilla_v4.py --check` y `build_plantilla_v6.py --check`: OK.
  - [x] Salida real del informe: la cubren los tests DOCX sobre la plantilla v6 real; la prueba visual queda manual.
- C5 (cierre): [x] Los dos progress existen. El frontend deja constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` y `web-design-guidelines`. No hay archivos temporales ni scripts de prueba sueltos.

## Puntos pedidos por el orquestador (verificados contra el código)
- Solo lectura: `getprop`, `uname`, `settings get`, `cmd wifi status`, `dumpsys wifi` filtrado y `date`. Cada script tiene timeout (3 s o 5 s). El helper iOS solo agrega `cmd_details` con 5 claves en lista blanca.
- Saneo: `DeviceDetailsRules` (API) re-sanea todo con regex por clave y `SafeText.Clean`, con longitud máxima. Los placeholders y tokens pasan por `Clean`. Hay un test donde un SSID `{caratula}` sale literal.
- D7: el SSID solo sale con `IncludeSsidInReport`, que es false por defecto. Los eventos de trazabilidad no lo llevan. El `sed` de `dumpsys` descarta el BSSID. No hay IP, MAC, ICCID ni IMSI.
- D5/D6: `Offsets` usa ticks y `decimal`, con `mid = b + (a-b)/2`, redondeo half away from zero y margen `ceil(RTT/2 + res)`. El umbral es `Report:ClockSkewWarningSeconds`, con rango 1–86400, default 60, y se publica en `/config/public`.
- D2: los datos no leídos quedan con `reason`, no con valor.
- D11: solo hay `PUT` de lectura, `clock` y `options`. Todos exigen `Editable`. "Volver a leer" deja `reread` en el evento. La medición de cierre vieja se descarta en memoria (`DropStaleClockClose`).
- D10/D12: sin ficha, el bloque `{#fichaTecnica}` desaparece y el texto del resto del informe es idéntico (test). Los casos viejos deserializan `null`. La capability `device_details_v1` se publica en `/health`.
- `.ps1`: `restaurar.ps1` (187/187 líneas) y `comun.Tests.ps1` (342/342) conservan CRLF.
- Datos: el test Mongo usa una base propia `factum_test_<guid>`, que crea y borra.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- Pendiente de prueba visual: el frontend no hizo prueba en navegador. Hay que probar a mano el paso 2 con Tatana en mock y `MockDeviceClockSkewSeconds=90`, el diálogo, el alta, la etapa de reloj al generar, el detalle del caso, la trazabilidad y el desborde con ventana de menos de 800 px.
- Pendiente: `Invoke-Pester deploy/windows/tests` (no hay `pwsh` en esta Mac).
- Pendiente: prueba con iPhone real (fracción de `TimeIntervalSince1970`, forma de `SIMStatus`), con wifi conectada en Android (formato del SSID), y publicar Tatana 1.7.0.
- Un ISO de fecha mal formado en el request da el 400 genérico de model binding y no `invalid_device_details`. Es un caso sin impacto práctico, porque la web reenvía los strings de Tatana.
- ESLint no corre en el repo (no hay `eslint.config.*`), así que el autochequeo del frontend fue manual.
