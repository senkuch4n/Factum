---
name: qa-factum
description: QA de robustez de Factum de punta a punta — API del backend, agente Tatana y recorrido en navegador con un celular real — con reglas de datos de desarrollo y reporte por gravedad. Usar antes de promover develop a main, después de cerrar una o varias HU, o cuando el usuario pida "probá que no se rompa", "QA", "test de robustez", "verificá que funciona" o "qué tan robusto es el sistema".
---

# QA de robustez de Factum

Probar el sistema como lo usaría (y lo rompería) un perito real, sin tocar los datos del usuario,
y terminar con un informe de hallazgos por gravedad que se convierte en HU.

## Entorno

| Pieza | Dónde | Cómo se levanta |
|---|---|---|
| Web (`client/`) | http://localhost:3000 | `npm run dev` en `client/` |
| Backend | http://localhost:8080 | `dotnet run` en `server/src/Factum.Backend` (perfil Development → base `factum_dev`) |
| Tatana real | http://localhost:8765 | `dotnet run` en `server/src/Factum.Agent` (con el celular por USB) |
| Mongo | localhost:27017 | contenedor Docker **compartido con otros proyectos**: nunca detenerlo |

Antes de empezar: `git switch develop && git pull`, reiniciar backend y Tatana para que corran ese código,
y verificar `curl localhost:8765/devices` (¿hay celular?).

## Reglas duras

1. **Datos de desarrollo** (AGENTS.md): la base y `~/Factum/Evidencia` tienen datos cargados a mano.
   No se modifica ni borra nada ajeno. Todo lo creado lleva prefijo `QA-ROBUSTEZ-`, se anotan sus ids y
   al final se borra **solo por `_id`/ruta exacta**. Nunca `deleteMany` con filtros amplios.
2. **Sin bypass de login**: no hay registro ni emails de QA. En `Auth:Mode=dev` el login emite JWT para cualquier DNI (usar DNIs `990000NN`); en `local` se siembran usuarios
   `qa-robustez-N` directo en Mongo con un script temporal (ver `references/usuarios-qa.md`) y se borran
   al final por `_id`. En el navegador se usa la sesión ya iniciada del usuario; el agente no escribe
   contraseñas ni cierra esa sesión.
3. **No matar lo compartido**: backend, Tatana real y Mongo los usan otros frentes. Las pruebas
   destructivas de Tatana van contra **una instancia mock propia** en otro puerto con `--data` temporal.
4. **El celular lo usa un solo frente** (el de navegador). Nadie más graba ni captura en paralelo.
5. **Sin diálogos nativos** (`alert`/`confirm`) en el navegador: bloquean la automatización.
6. No se cambia código de producción: los fallos se reportan y pasan por el arnés como HU.

## Workflow

### 1. Lanzar los frentes en paralelo
Un subagente `general-purpose` por frente (con `run_in_background`). Cada uno recibe su checklist de
`references/` y escribe su reporte completo en el scratchpad (`qa/<frente>.md`), devolviendo 5 líneas.

| Frente | Checklist | Herramientas |
|---|---|---|
| API backend | `references/api-backend.md` | curl/scripts + usuarios QA sembrados |
| Tatana | `references/tatana.md` | instancia `--mock` propia en :8766, ffmpeg/dd para medios |
| E2E navegador | `references/e2e-navegador.md` | Claude in Chrome (`mcp__claude-in-chrome__*`, cargadas en una sola ToolSearch), pestaña propia, celular real |
| Formato del informe | `references/informe-formato.md` | generador del backend + `soffice` → PDF → imágenes |

No existe la CLI `agent-browser`: el navegador se maneja con Claude in Chrome. Toda captura que se saca
se mira (no sirve guardarla sin verla).

### 2. Consolidar
Juntar los tres reportes en un informe único con:
- Resumen y veredicto ("listo para promover" / "hay bloqueantes").
- Tabla de hallazgos: ID, **gravedad** (Crítica / Alta / Media / Baja), frente, reproducción mínima,
  esperado vs obtenido, `archivo:línea` probable.
- Lo que anduvo bien (con números: tiempos, tamaños, memoria).
- **Pruebas físicas para el usuario** (ver abajo).
- Constancia de limpieza: ids creados y borrados por frente.

Ofrecer el informe como Artifact si es para compartir con el equipo.

### 3. Convertir hallazgos en trabajo
- Crítica/Alta → HU con `hu.mjs alta` (agrupar por zona de código) y pasan por el arnés.
- Media/Baja → listar y preguntar al usuario cuáles entran.
- Anotar el resultado en `progress/sesiones/<login>.md`.

## Pruebas físicas que hace el usuario
Ningún agente puede hacerlas; se listan al final del informe:
- Desconectar el USB a mitad de una captura y a mitad de una grabación; reconectar.
- Bloquear la pantalla del celular durante la grabación.
- iPhone real (DVT y AirPlay) con grabación de 10+ min.
- Windows 11: guardar el original mientras se genera la variante suavizada; instalador de Tatana.
- PC sin GPU dedicada / escritorio remoto: fluidez del teléfono 3D del paso 2.
