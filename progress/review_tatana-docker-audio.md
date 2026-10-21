# Review — tatana-docker-audio (fix fuera de HU)

**Veredicto:** APROBADO

## Verificado
- `dotnet build Factum.Agent.csproj`: 0 warnings, 0 errores. `dotnet test Factum.Agent.Tests`: 983/983 OK (incluye 3 tests nuevos de AdbRecordingArgsTests).
- `AdbService.BuildScrcpyArgs` (AdbService.cs:190-205): único punto que arma args de scrcpy (único llamador en :280), así que clips y sesión lo comparten. Con `AudioPlayback=false` y audio activo agrega `--no-audio-playback` (válido con `--no-window` y `--no-video-playback`); con `--no-audio` no lo agrega.
- Default `true` en `RecordingOptions` y en `appsettings.json`: args idénticos a los actuales (test `PorDefecto_ReproduceAudio`). Documentos/config viejos sin la clave siguen en true.
- Llegada de la variable: Program.cs:39 usa `WebApplication.CreateBuilder(args)` (env vars incluidas) y :97 bindea la sección `Agent` (con `Recording`). `Agent__Recording__AudioPlayback=false` del Dockerfile se mapea bien. `entrypoint.sh:69` hace `exec setpriv` sin limpiar el entorno. `instalar-tatana-docker.sh` solo pasa TATANA_*, HOME, Agent__EvidenceDirectory y TZ con `-e`; no hay `--env-file` ni nada que pise la variable.
- Mock y Windows/macOS/AppImage: sin cambios de comportamiento.
- CHECKPOINTS.md: sin cambios de contrato, modelos Mongo ni agent-ui; no aplica más allá de build/tests.

## Observaciones no bloqueantes
- Falta rearmar la imagen y probar una grabación Android 11+ real en el contenedor (lo declara el implementador como pendiente).
- El audio sigue yendo al MKV; solo deja de sonar en la PC (documentado en docs/tatana-docker.md).
