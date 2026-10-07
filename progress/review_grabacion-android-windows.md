# Review — grabacion-android-windows

**Veredicto:** APROBADA

## Verificación propia (corrida por el reviewer)
- `dotnet build` Factum.Agent: 0 errores, 0 warnings. Factum.Backend: 0 errores (4 warnings NU1903 preexistentes, Snappier).
- `dotnet test` Factum.Agent.Tests: 33/33. Factum.Backend.Tests: 406/406.
- `client/`: `npx tsc --noEmit` limpio.
- `npm run build` de `client/` NO se corrió: la regla dura del reviewer prohíbe `next build` en el checkout principal. tsc limpio; los cambios de client son solo `agent.ts`/`useRecording.ts` sin imports nuevos.
- PSScriptAnalyzer/Pester: no se re-corrieron; se leyó el código (PS 5.1: sin `?.`, `Get-PropiedadSegura`, `.ToArray()`), BOM `efbbbf` presente en los 3 .ps1 y CRLF 100%. El `.bat` es LF y ASCII en la línea nueva, igual que antes (HEAD ya era LF). El implementador declara PSSA 0 hallazgos y Pester 39/39.
- Árbol de trabajo: no se tocó nada fuera de scope (appsettings.json, tsbuildinfo y arnés ignorados según indicación).

## Checkpoints
- C1: [x] (arnés: no es parte del diff de código; no hay cambios de `backlog.json` evaluables aquí; verify.sh no corrido por el reviewer, lo hace el orquestador)
- C2: [x] HU y SDD existen; el Contrato compartido coincide: `error` (string) leído en `client/src/lib/agent.ts` L9-20/L143-168; `tools.<adb|scrcpy|ffmpeg|python>.{found,source,path,version}` escrito por `HealthController` + `ToolInventory` (claves en minúscula, `Source` string, nulls omitidos) y leído por `Get-LineasHerramientasTatana` (`_comun.ps1` L880).
- C3: [x] Solo Tatana, tests, client, deploy/packaging/docs, como declara la SDD. Sin Mongo. Sin `agent-ui/` ni `Factum.Backend`. Sin logs sensibles (se loguean rutas y líneas de scrcpy). Mock de Tatana intacto (`StartRecording` mock retorna antes de resolver herramientas; solo cambia el texto E7, anotado por el implementador).
- C4: [x] build limpio, tests reales (ToolResolver con layouts temporales, dshow, parsers, BuildHelperCommand), tsc limpio. El cierre limpio con Ctrl+C y el MKV válido son prueba manual del usuario en la PC del estudio (guion §11.2), explícito en la SDD (R1).
- C5: [x] Ambos `progress/impl_*` existen; el frontend deja constancia de las 4 skills ("sin hallazgos aplicables"). Sin temporales en el repo.

## Revisión de los puntos de foco
- Helper común (`Common/ToolResolver.cs`): orden portátil → PATH → Homebrew correcto; Windows usa `.exe`/`.com` de PATHEXT (descarta .bat/.cmd), ignora entradas vacías/relativas, quita comillas, no usa Homebrew en Windows; devuelve ruta absoluta. En la Mac equivale a lo anterior (resolución a ruta absoluta del mismo binario). 13 tests lo fijan (T2a-j + extras). `AdbPath` conserva el fallback `"adb"`.
- scrcpy en el armado (`armar-tatana-portable.sh` L13-19, L91-140, L187-208): versión y SHA fijos, `shasum -c` aborta con el mensaje de la SDD, se borran `adb.exe` y `AdbWin*.dll`, LICENSE.txt viene del zip, THIRD-PARTY-NOTICES en CRLF, chequeo final del zip (requeridos + ausentes). Lanzado con `ADB=<adb de platform-tools>` y `SCRCPY_SERVER_PATH` solo si es portátil (`AdbService.cs` StartRecordingAsync).
- Cierre limpio en Windows (`ProcessStop.cs`, `WindowsConsoleSignal.cs`, `Program.cs` L10-19): técnica correcta. El auxiliar corre con su propia consola oculta (CreateNoWindow), hace FreeConsole, AttachConsole(pid de scrcpy), ignora Ctrl+C en sí mismo y genera CTRL_C_EVENT en la consola de scrcpy; así no toca la consola de Tatana ni el ConsoleLifetime, ni a otros procesos de la consola del .bat. Timeout de 3 s al auxiliar, 10 s a scrcpy; si falla o no responde, `Kill(entireProcessTree)` y `RemuxAsync` (ffmpeg -c copy a `.remux.mkv` y reemplazo; si falla queda el crudo). El mux del mic reemplaza al remux. DP12 (`SetConsoleCtrlHandler(NULL,false)`) presente. Rama `--ctrl-c` antes de `CreateBuilder`. El publish win-x64 sin warnings, según el implementador.
- Mic PC (`MicCapture.cs`): dshow con `audio=<dispositivo>` en un solo elemento de ArgumentList, alternative name preferido, override `Agent:MicDevice`, `.mka`, cierre con `q` por stdin y Kill de respaldo; E4/E5/E6 con textos exactos; DP6 A implementado (rollback de scrcpy y borrado solo de archivos propios). macOS/Linux sin cambios de entrada (`avfoundation :0`, `alsa default`).
- `/health.tools` y Diagnóstico: implementados y testeados; `/health` no bloquea (versión en caché con warm-up y timeout de 5 s + Kill).
- `actualizar.ps1` / DP9: `Install-TatanaPortable` cierra procesos bajo `%LOCALAPPDATA%\Programs\Tatana\` (try/catch por `.Path`), y `install-portable.bat` corta si `xcopy` falla.
- Frontend: `readAgentError`, `AGENT_UNREACHABLE` y el aviso `No se pudo guardar la grabación: …` por el `FxBanner` existente (role=alert). Sin cambios visuales, sin tocar otros métodos.
- iOS: solo resolución de herramientas y `with_mic` vía `MicCapture` (solo Warning si falla); se corrige el mic sin detener en burst. Sin otros cambios de comportamiento en iOS (en Windows `with_mic` pasa de `wasapi` a `dshow`, previsto por la SDD/DP7).

## Cambios requeridos (si RECHAZADA)
Ninguno.

## Observaciones no bloqueantes
- `ToolInventory.GetOrStartVersion`: si la lectura de versión falla (null), queda en caché como null para esa ruta y no se reintenta hasta reiniciar Tatana; `version` simplemente se omite. Aceptable según SDD ("una sola vez por ruta").
- `install-portable.bat` (línea del `if errorlevel`): `%DEST%` se expande dentro de un bloque con paréntesis; un nombre de usuario con `)` en el perfil rompería esa línea. Caso muy raro.
- Verificar en la PC del estudio (R1): que `AttachConsole` funcione sobre scrcpy lanzado con CreateNoWindow; si no, el fallback Kill + remux deja el MKV con duración.
- `npm run build` de `client/` no se corrió por la regla dura del reviewer (ver arriba).
