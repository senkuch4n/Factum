# Review — instalacion-poca-ram

**Veredicto:** APROBADA

## Verificado por el reviewer (corrido, no copiado del progress)
- `docker compose config` (proyecto `factum-review`, `.env` de scratch): base `cfb97c8` + `.env` sin la clave vs. compose actual + `normal` -> diff VACIO. `normal` vs `poca` -> exactamente 6 diferencias (command mongo, 3 mem_limit 536870912/805306368/402653184, DOTNET_gcServer, NODE_OPTIONS).
- Parseo de todos los .ps1 OK; PSScriptAnalyzer (settings del repo) 0 hallazgos; Pester 35/35 verdes (contenedor pwsh).
- BOM `efbbbf` + CRLF en todos los .ps1 y en comun.Tests.ps1; los 6 `.bat` ASCII+CRLF (sin cambios); overlay y `.env.example` en LF. `bash -n armar-paquete.sh` OK.
- `docker-compose.yml` base: solo 2 lineas de comentario. Ningun script escribe `.wslconfig` ni llama `wsl --shutdown`.
- Sin cambios en client/, server/ (salvo el appsettings local del usuario, ignorado) ni agent-ui/ (salvo tsbuildinfo, ignorados).

## Checkpoints
- C1: [x] arnes no modificado por el implementador (backlog/current son del orquestador); no se corrio verify.sh (alcance del orquestador).
- C2: [x] HU, SDD con Contrato compartido y resolucion de DT8 presentes; nombres del contrato (`FACTUM_PERFIL_MEMORIA`, `-PerfilMemoria`, `RamGb`/`PerfilMemoria`, overlay) coinciden en scripts, tests y guia.
- C3: [x] solo infra `deploy/windows/` + guia, como declara la SDD. Q3: `Get-PerfilMemoria` (_comun.ps1) piso < 3.5 duro, 3.5-<7.5 `poca` con bloque y S/N propia (instalar.ps1, tambien con -Reparar), `normal` con el aviso de 16 GB intacto. Overlay condicionado a `poca` en `Get-FactumComposeArgumento`, usado por todos los scripts via `Invoke-FactumCompose` (backup, restaurar, actualizar, diagnostico, abrir). `-Reparar`+`-PerfilMemoria` se rechaza antes del paso 1. DT8 A: diagnostico hace `up -d` + `restart backend` con el texto exacto de la SDD. Sin Mongo/API afectada.
- C4: [x] build .NET/tsc no aplican (sin cambios en esos lados). Tests Pester reales y verdes. Medicion real en el progress: backend pico `anon` 259-271 MiB (<614), mongo <=242, frontend <=34, cero oom_kill/OOMKilled/RestartCount, ZIP verificado por hash; escenario 128m = OOM gestionado y regeneracion OK. Respalda los topes 512m/768m/384m.
- C5: [x] progress backend completo con numeros reales, limpieza y git status; mongo:7.0.43 restaurado al indice original; no se tocaron datos ajenos. Sin frontend: no aplica constancia de skills.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- La SDD §10.3 indicaba `!reset` para publicar puerto; el implementador uso `!override` (correcto, anotado).
- Los videos >30 MB no entran por `POST /files` (limite de Kestrel); fuera de alcance, conviene revisar como llegan los reales (Tatana).
- El `docker pull --platform linux/arm64` movio el tag `mongo:7.0.43` local; ya restaurado, queda un indice arm64 sin tag en disco.
- Pendiente del usuario: checklist M1-M9 en la PC real de 4 GB (guia §2.1). Los topes no cambiaron respecto de la SDD (DT13 no aplicada).
