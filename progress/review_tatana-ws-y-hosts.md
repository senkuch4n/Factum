# Review — tatana-ws-y-hosts (HU #60)

**Veredicto:** APROBADA

## Checkpoints
- C1: [x] Issue #60 en `en_revision`, rama `feat/tatana-ws-y-hosts`. `./ops/harness/verify.sh` -> "Arnés OK".
- C2: [x] `docs/hu-tatana-ws-y-hosts.md` y `Refactorizaciones/tatana-ws-y-hosts.md` existen (con Contrato compartido). Nombres de campo del contrato (`invalid_password`, `not_recording`, `evidence_changed`+`files[].reason`, `insufficient_storage`+`required_bytes/available_bytes`, `local_config.error`) coinciden con el código.
- C3: [x] Solo Factum.Agent, tests y README (sin client/agent-ui/Backend/Mongo). Sin logs sensibles (la contraseña nunca va en el mensaje). T-14 no se duplicó: `RecordingNotActiveException` y `NotRecording` vienen de #59 y no se tocaron.
- C4: [x] `dotnet build` Factum.Agent: 0 warnings, 0 errores. Tests Agent 496/496 (3 corridas más, sin flakes); Backend 888 OK / 7 skipped. Hay tests reales del hub (FakeWebSocket trabado, orden, timeout, shutdown, un solo envío en vuelo), HostPolicy con `HostFilteringMiddleware` real, validador/inspector y ZIP.
- C5: [x] `progress/impl_backend_tatana-ws-y-hosts.md` completo. Sin temporales sueltos. Skills UX: no aplica (sin frontend).

## Verificado
- Hub: cola `Channel` acotada (256) por cliente con `TryWrite`; `BroadcastAsync` sincrónico, no bloquea; único `SendAsync`/`CloseOutputAsync` en `SendLoopAsync` (salvo el close a un socket que nunca entra al hub); timeout 10 s; `Drop` idempotente con `Abort()`. `ShutdownAsync` encola close 1001 detrás de lo pendiente, espera acotada y cancela `_stopping`.
- Repro propia (instancia mock :8791, build y datos en scratchpad, ya borrados): 1500 capturas con un WS que no lee y otro conectado, peor request 12 ms; apagado con 2 WS abiertos en 0,15 s; `/health` con Host `127.0.0.1` 200, `Host: evil.example` 400, `localhost` con Origin permitido 200.
- Filtro de Host: `PostConfigure` + `UseHostFiltering` primero; `AllowedHosts: "*"` removido; locales + bind + extras; `*` en config principal impide arrancar. Orígenes (OriginPolicy/guarda) intactos.
- Config local: `LocalConfigValidator` + `AddJsonStream` en el inspector; inválido -> `Loaded=false` con error que nombra la clave; `Program.cs` ya saltea el archivo.
- Bajas T-05 a T-09 y T-13 implementadas según la SDD (clasificación antes de borrar el `.part`, carpetas creadas por la llamada solo si quedan vacías, `IsValidName` sin cambios).
- README: fila `Agent:AllowedHosts` y nota en `BindAddress`.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- No reproduje el desborde de cola real por TCP (1500 capturas no llenaron los buffers de loopback); lo cubren los tests con FakeWebSocket y la repro del implementador (3300 capturas -> "cola llena").
- Queda para prueba manual del usuario: web en navegador contra Tatana nueva y cierre/reapertura de Tatana con la web abierta.
