# impl_backend — tatana-docker-audio (fix puntual, rama `fix/tatana-docker-audio`)

Estado: **done** (sin commit, según lo pedido).

## Problema
En Tatana en Docker (sin dispositivo de audio) scrcpy 4.1, aun con `--no-window`, reproduce el audio de
`--audio-source=output` (Android 11+) y muere: `Could not initialize SDL audio: Failed loading libjack.so.0`.

## Cambios
| Archivo | Cambio |
|---|---|
| `server/src/Factum.Agent/Models/AgentModels.cs` | `RecordingOptions.AudioPlayback` (bool, default `true`), documentado como los demás campos. No necesita `Normalize` (bool). |
| `server/src/Factum.Agent/Services/AdbService.cs` | `BuildScrcpyArgs`: si `!o.AudioPlayback && audioArg != "--no-audio"` agrega `--no-audio-playback` (con `--no-audio` no hay audio que reproducir, se omite). Aplica a clips y a `AndroidScreenTrack` (sesión), que comparten el método. |
| `server/src/Factum.Agent/appsettings.json` | `"AudioPlayback": true` en `Agent:Recording`. |
| `deploy/tatana-docker/imagen/Dockerfile` | `ENV ... Agent__Recording__AudioPlayback=false` con comentario del porqué. |
| `docs/tatana-docker.md` | Fila "Audio" en la tabla "Cómo corre el contenedor": el audio se graba en el video pero no suena en la PC. |
| `server/tests/Factum.Agent.Tests/AdbRecordingArgsTests.cs` | 4 casos nuevos: default sin el flag; `AudioPlayback=false` con `no_window` y con `no_video_playback` agrega el flag una sola vez; con `--no-audio` no lo agrega. |

## Decisiones
- **Variables de entorno:** el agente usa `WebApplication.CreateBuilder(args)` (incluye env vars sin prefijo, que
  ganan sobre `appsettings.json` y `appsettings.Local.json`) y bindea `builder.Configuration.GetSection("Agent")` a
  `AgentOptions` (con `Recording`). El contenedor ya usa el mismo mecanismo (`Agent__EvidenceDirectory`).
  `entrypoint.sh` hace `exec setpriv ...` que conserva el entorno. No hizo falta tocar hornear-origenes/entrypoint.
- **Combinación con `no_video_playback`:** `--no-video-playback --no-audio-playback` es válido en scrcpy 4.1
  (equivale a `--no-playback`); con `--no-window` también. Cubierto en el test (Theory con los dos modos).
- `estado-tatana-docker.sh` / `LinuxSelfTest`: no se tocaron (no había nada trivial que reflejar).

## Verificación
- `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` → 0 Warning(s), 0 Error(s).
- `dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj` → Passed 983/983 (AdbRecordingArgsTests 8/8).
- No se rearmó imagen ni paquete; no se corrieron contenedores; no se tocó Mongo ni `Storage:DataDirectory`.
- Pendiente (orquestador/usuario): rearmar la imagen y probar una grabación Android 11+ real en el contenedor.

## Contrato
Sin cambios de contrato client ↔ agente (opción de config interna).
