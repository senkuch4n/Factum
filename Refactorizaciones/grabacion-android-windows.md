# SDD: Grabar la pantalla de un Android desde Tatana en Windows

**Slug:** `grabacion-android-windows`
**HU:** `docs/hu-grabacion-android-windows.md`, validada el 2026-10-02: D1–D10 en **a)**, D11 y D12
en **b)**. El paquete que lleva el arreglo es **1.1.0**, que incluye también `subida-archivos-grandes`.
**Rama:** `feat/grabacion-android-windows`, encadenada sobre `feat/subida-archivos-grandes`. Va contra `develop`.
**Implementadores:** `implementer-backend` (agente Tatana, tests .NET, `deploy/`, `packaging/`,
guía) e `implementer-frontend` (solo `client/`). **No se toca `agent-ui/` ni `Factum.Backend`.**

> **Árbol de trabajo:** `server/src/Factum.Agent/appsettings.json` tiene un cambio local del
> usuario (`"Mock": true`). **No se stagea ni se edita.** Los `agent-ui/*.tsbuildinfo` modificados
> tampoco son de esta HU. Para armar el portátil se usa un árbol limpio (ver §10.1, V6).

---

## 1. Resumen funcional

En la PC Windows del estudio, Tatana graba la pantalla de un Android igual que en la Mac:

- El portátil trae **scrcpy 4.1**, con versión fija y SHA-256 verificado. Va en `tools/scrcpy/`,
  sin el `adb.exe` ni las `AdbWin*.dll` propios de scrcpy, y con su `LICENSE.txt` y un
  `THIRD-PARTY-NOTICES.txt`.
- Tatana encuentra sus herramientas con un **helper común** que busca en este orden:
  `tools/<dir>/<exe>` junto al exe, después el `PATH` (respetando `.exe`/`PATHEXT` en Windows) y por
  último Homebrew (solo fuera de Windows). Lo usan `AdbService` e `IosService`.
- Tatana lanza scrcpy con `ADB=<adb de platform-tools>`, así hay un solo servidor adb.
- Al detener, scrcpy se cierra **limpio** en Windows. Tatana manda un `CTRL_C_EVENT` a la consola
  oculta de scrcpy con un proceso auxiliar, que es el mismo `Factum.Agent.exe` en modo
  `--ctrl-c <pid>`. Si scrcpy no termina a tiempo, se lo mata y se remuxea el MKV con
  `ffmpeg -c copy` para que tenga duración e índice.
- El modo "con micrófono de la PC" anda en Windows. Usa el ffmpeg de `tools/ffmpeg`, captura con
  `dshow` y se cierra limpio mandando `q` por stdin. El arreglo se comparte con el `with_mic` de iOS.
- Si iniciar o detener falla, Factum muestra el `error` real que manda Tatana. Una grabación que no
  se pudo guardar ya no se pierde en silencio.
- `/health` de Tatana suma un bloque `tools` (encontrada, origen, ruta y versión) y "Diagnóstico de
  Factum" lo imprime con OK/FALTA.
- `actualizar.ps1` frena también los procesos que corren desde la carpeta de Tatana (adb), para que
  `xcopy` pueda reemplazar `tools/` y quede `tools/scrcpy` instalado.
- El audio del celular se sigue escuchando por los parlantes de la PC (D12 b). iOS y la webcam quedan
  fuera (D11 b, HU `ios-herramientas-windows`).

## 2. Toca

| Lado | ¿Toca? | Detalle |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **no** | — |
| backend (Tatana) `server/src/Factum.Agent` | **sí** | Nuevos `Common/ToolResolver.cs`, `Common/ProcessStop.cs`, `Common/WindowsConsoleSignal.cs`, `Services/MicCapture.cs`, `Services/ToolInventory.cs`. Cambios en `Services/AdbService.cs`, `Services/IosService.cs` (solo resolución de herramientas + `with_mic`), `Controllers/HealthController.cs`, `Program.cs`, `Models/AgentModels.cs` (`AgentOptions.MicDevice`), `Factum.Agent.csproj` (`InternalsVisibleTo`). **No** se toca `WebcamService.cs` (D11 b, ver DP13). |
| Tests .NET | **sí** | **Nuevo** `server/tests/Factum.Agent.Tests/` (xUnit, mismo patrón que `Factum.Backend.Tests`). |
| client `client/` | **sí** | `src/lib/agent.ts` (`startRecording`, `stopRecording`) y `src/hooks/useRecording.ts` (aviso al fallar el stop). Sin cambios visuales en el flujo feliz. |
| agent-ui `agent-ui/` | **no** | — |
| Infra / empaquetado | **sí** | `deploy/windows/armar-tatana-portable.sh` (paso de scrcpy + chequeos del zip), `packaging/portable/install-portable.bat` (cortar si `xcopy` falla), `deploy/windows/scripts/_comun.ps1` (`Install-TatanaPortable`, nueva `Get-LineasHerramientasTatana`), `deploy/windows/scripts/diagnostico.ps1`, `deploy/windows/tests/comun.Tests.ps1`. |
| Docs | **sí** | `docs/instalacion-windows.md` (sección 8 "Comprobar Tatana" y la parte de armado del paquete). |
| MongoDB | **no** | Sin datos nuevos. |

## 3. Modelo de datos

No aplica: no hay cambios en Mongo. La grabación sigue siendo
`%LOCALAPPDATA%\Tatana\data\grabacion_<yyyyMMdd_HHmmss_fff>.mkv`. Los archivos temporales nuevos
viven al lado del MKV, en la misma carpeta, y se borran al terminar:

| Temporal | Cuándo existe |
|---|---|
| `<grabacion>.mic.mka` | Modo con mic, mientras se graba (antes `.mic.m4a`; ver DP5). |
| `<grabacion>.mux.mkv` | Durante el mux del mic (ya existía). |
| `<grabacion>.remux.mkv` | Solo si hubo que matar a scrcpy, durante el remux (nuevo). |

## 4. Endpoints de Tatana (API local `localhost:8765`)

Las rutas no cambian. Cambian los cuerpos de error y `/health`.

### 4.1 `POST /devices/{serial}/record/start` y `POST /devices/{serial}/record/stop`

- La forma de éxito no cambia: start devuelve `{ "filename", "state" }` y stop devuelve
  `{ "filename", "url" }`.
- Error: **HTTP 500** con `{ "error": "<mensaje en castellano, para mostrar al perito>" }`. La forma
  es la misma que hoy (`RecordingController` L49/L76). Lo que cambia son los **textos** (§4.3) y que
  ahora el client los muestra.

### 4.2 `GET /health`: bloque `tools` (aditivo)

```json
{
  "status": "ok",
  "version": "2.0.0",
  "mock": false,
  "ios_available": true,
  "tools": {
    "adb":    { "found": true,  "source": "portable", "path": "C:\\Users\\perito\\AppData\\Local\\Programs\\Tatana\\tools\\platform-tools\\adb.exe", "version": "37.0.1-13426479" },
    "scrcpy": { "found": true,  "source": "portable", "path": "C:\\...\\tools\\scrcpy\\scrcpy.exe", "version": "4.1" },
    "ffmpeg": { "found": true,  "source": "portable", "path": "C:\\...\\tools\\ffmpeg\\ffmpeg.exe", "version": "N-121345-g0a1b2c3d4e-20261001" },
    "python": { "found": false }
  }
}
```

- `tools` es un objeto con **claves fijas en minúscula**: `adb`, `scrcpy`, `ffmpeg`, `python`.
  Siempre están las cuatro, también en mock: la resolución es real.
- Cada valor tiene `found` (bool, siempre presente). Además:
  - `source`: `"portable"` | `"path"` | `"homebrew"`. Se omite si `found` es false.
  - `path`: ruta absoluta. Se omite si `found` es false.
  - `version`: string. Se omite si todavía no se obtuvo o falló. La política global
    `WhenWritingNull` de `Program.cs` hace que los null **no viajen**.
- `/health` tiene que seguir siendo instantáneo, porque el client lo sondea con un timeout de 2 s
  (`agent.isOnline`). Por eso la ruta se resuelve en cada llamada (son solo `File.Exists`) y la
  versión sale de un caché en memoria. Ese caché se llena en segundo plano al arrancar y cuando
  aparece una ruta nueva (§6.6).

### 4.3 Mensajes de error (textos exactos)

| # | Cuándo | `error` |
|---|---|---|
| E1 | No se encuentra scrcpy, o el `scrcpy.exe` portátil no tiene `scrcpy-server` al lado | `No se encontró el componente de grabación de Android (scrcpy). Actualizá o reinstalá Tatana con el paquete de Factum.` En macOS, y **solo** ahí, se agrega: ` En la Mac: brew install scrcpy.` |
| E2 | scrcpy termina en menos de 1.5 s después de lanzarlo | `scrcpy no pudo iniciar la grabación: <últimas líneas de error de scrcpy, máx. 3, unidas con " · ">` (si no hay líneas: `código de salida <n>`) |
| E3 | Ya hay una grabación Android en curso | `Ya hay una grabación de Android en curso.` |
| E4 | Con mic, no hay ffmpeg | `No se encontró ffmpeg, necesario para grabar con el micrófono de la PC. Actualizá o reinstalá Tatana con el paquete de Factum.` |
| E5 | Con mic, no hay micrófono | `No se encontró ningún micrófono en esta PC.` |
| E6 | Con mic, ffmpeg termina en menos de 1.5 s | `No se pudo abrir el micrófono de la PC: <última línea de error de ffmpeg>` |
| E7 | Stop sin grabación activa | `No hay una grabación en curso.` (reemplaza a `Sin grabación activa` del lado Android) |
| E8 | Stop: scrcpy no dejó archivo | `scrcpy no generó el archivo de la grabación. <últimas líneas de error de scrcpy, si hay>` |

El client antepone `No se pudo guardar la grabación: ` a los errores del stop (§7). Los mensajes de
iOS no cambian.

## 5. Contrato compartido

### 5.1 Tatana → client (`client/src/lib/agent.ts`)

| Campo JSON | Tipo | Dónde lo escribe Tatana | Dónde lo lee el client |
|---|---|---|---|
| `error` | `string` | `server/src/Factum.Agent/Controllers/RecordingController.cs` (`new { error = ex.Message }`, ya existe) y el middleware de `Program.cs` (L120, ya existe) | `client/src/lib/agent.ts`: `startRecording` y `stopRecording`, con un helper nuevo `readAgentError(res, fallback)` |

- El casing es `error` en minúscula: es un objeto anónimo con política `SnakeCaseLower`, y queda
  igual.
- No hace falta un tipo nuevo en `client/src/types/`: el client solo lee `error`, defensivamente
  (`typeof data.error === "string"`).
- El **client no consume `tools`**. No se agrega tipo TS para `/health` (fuera de alcance).

### 5.2 Tatana → `diagnostico.ps1` (PowerShell)

| Campo JSON | Tipo | Escribe | Lee |
|---|---|---|---|
| `tools` | objeto | `server/src/Factum.Agent/Controllers/HealthController.cs` (desde `Services/ToolInventory.cs`) | `deploy/windows/scripts/_comun.ps1` → `Get-LineasHerramientasTatana` (usada por `diagnostico.ps1`) |
| `tools.<adb\|scrcpy\|ffmpeg\|python>.found` | bool | ídem | ídem |
| `tools.<...>.source` | `"portable"`\|`"path"`\|`"homebrew"` (opcional) | ídem | ídem (solo para mostrar) |
| `tools.<...>.path` | string (opcional) | ídem | ídem |
| `tools.<...>.version` | string (opcional) | ídem | ídem |

En C#, el bloque es un `Dictionary<string, ToolStatus>`. Las claves no pasan por la naming policy
(`DictionaryKeyPolicy` es null), así que **se escriben ya en minúscula**. `ToolStatus` es un record
con `Found`, `Source`, `Path` y `Version`; `Source` se serializa como **string** (no como enum, para
no depender del converter) con los valores literales `portable`, `path` y `homebrew`. Con un Tatana
1.0.0 el bloque no existe y el script lo tiene que tolerar (§8.4).

## 6. Diseño del agente

### 6.1 `Common/ToolResolver.cs` (helper común, D4)

```csharp
namespace Factum.Agent.Common;

public enum ToolSource { Portable, Path, Homebrew }
public sealed record ToolSpec(string Key, string ToolDir, string UnixName, string? WinName);
public sealed record ToolResolution(string Path, ToolSource Source);

// Todo lo que depende del SO/proceso, inyectable para los tests.
public sealed record ToolEnvironment(
    string BaseDirectory, string? PathVariable, string? PathExt, bool IsWindows,
    IReadOnlyList<string> FallbackDirs)
{
    public static ToolEnvironment Current { get; }   // AppContext.BaseDirectory, env PATH/PATHEXT,
                                                     // OperatingSystem.IsWindows(),
                                                     // ["/opt/homebrew/bin", "/usr/local/bin"]
}

public static class AgentTools
{
    public static readonly ToolSpec Adb     = new("adb",     "platform-tools", "adb",     "adb.exe");
    public static readonly ToolSpec Scrcpy  = new("scrcpy",  "scrcpy",         "scrcpy",  "scrcpy.exe");
    public static readonly ToolSpec Ffmpeg  = new("ffmpeg",  "ffmpeg",         "ffmpeg",  "ffmpeg.exe");
    public static readonly ToolSpec Ffprobe = new("ffprobe", "ffmpeg",         "ffprobe", "ffprobe.exe");
    public static readonly ToolSpec Python  = new("python",  "python-embed",   "python3", "python.exe");
    public static readonly ToolSpec Uxplay  = new("uxplay",  "uxplay",         "uxplay",  "uxplay.exe");
    public static readonly ToolSpec Qvh     = new("qvh",     "qvh",            "qvh",     null);
    public static readonly ToolSpec Rife    = new("rife",    "rife",           "rife-ncnn-vulkan", "rife-ncnn-vulkan.exe");
}

public static class ToolResolver
{
    public static ToolResolution? Find(ToolSpec spec) => Find(spec, ToolEnvironment.Current);
    internal static ToolResolution? Find(ToolSpec spec, ToolEnvironment env);
}
```

Algoritmo de `Find` (exacto; los tests lo fijan):

1. `name = env.IsWindows ? (spec.WinName ?? spec.UnixName) : spec.UnixName`.
2. **Portátil:** `Path.Combine(env.BaseDirectory, "tools", spec.ToolDir, name)`. Si existe, se
   devuelve `(Path.GetFullPath(p), Portable)`.
3. **PATH:** se parte `env.PathVariable` con `';'` en Windows y `':'` en el resto. En cada entrada
   se hace `Trim()` y se le sacan las comillas `"` de los extremos. Se ignoran las vacías y las **no
   absolutas** (`!Path.IsPathRooted`, para que `.` en el PATH no resuelva contra el directorio
   actual).
   - Nombres a probar en Windows: si `Path.HasExtension(name)`, solo `[name]`. Si no, `name + ext`
     por cada `ext` de `env.PathExt`, que por defecto es `".COM;.EXE;.BAT;.CMD"`. Solo valen
     **`.exe` y `.com`**: `.bat`/`.cmd`/`.vbs`/`.js` se descartan, porque `Process.Start` sin shell
     no debe correr scripts. Cada extensión se prueba **en minúscula y tal cual viene** (los tests
     corren en macOS/Linux, donde el FS distingue mayúsculas, o puede hacerlo).
   - Nombres a probar fuera de Windows: `[name]`.
   - Con el primer `File.Exists` se devuelve `(Path.GetFullPath(Path.Combine(dir, n)), Path)`.
4. **Homebrew:** solo si `!env.IsWindows`, se prueba `Path.Combine(dir, name)` por cada
   `env.FallbackDirs` y se devuelve `(…, Homebrew)`.
5. Si no se encontró nada, devuelve `null`.

Siempre devuelve **ruta absoluta**: nunca un nombre pelado. Esto reemplaza:

- En `AdbService`: `ScrcpyPaths`, `FfmpegPaths`, `FindBinary`, `IsOnPath`, `FindScrcpy` y el cuerpo
  de `AdbPath`. Este último queda `Lazy<string>(() => ToolResolver.Find(AgentTools.Adb)?.Path ?? "adb")`,
  con el **fallback pelado `"adb"` que hay hoy**: así `ProcessRunner` sigue devolviendo -1 y los
  mensajes actuales de "ADB no instalado" no cambian.
- En `IosService`: `BuildToolCandidates`, `FfmpegPaths`, `FfprobePaths`, `RifePaths`, `QvhPaths`,
  `UxplayPaths`, `FindBinary` e `IsOnPath`. Queda un
  `private static string? FindBinary(ToolSpec s) => ToolResolver.Find(s)?.Path;` y las llamadas
  pasan de `FindBinary(FfmpegPaths)` a `FindBinary(AgentTools.Ffmpeg)`, etc. Los `?? "ffmpeg"` que ya
  existen se mantienen. `PythonCandidates`/`BuildPythonCandidates` **no se tocan**: tienen su lógica
  propia de "el primero que tenga pymobiledevice3". Tampoco se toca el script Python embebido
  `DvtRecorderScript` (D11 b).

Comportamiento en la Mac: antes se devolvía `"scrcpy"` pelado si estaba en el PATH, y ahora se
devuelve la ruta absoluta del mismo archivo. Es equivalente.

### 6.2 Lanzar scrcpy (`AdbService.StartRecordingAsync`)

1. Si `_recordingProcess` no es null y no terminó, se lanza E3.
2. `var scrcpy = ToolResolver.Find(AgentTools.Scrcpy) ?? throw E1`. Si `Source == Portable` y no
   existe `scrcpy-server` en la misma carpeta, también se lanza E1.
3. Se arma el `ProcessStartInfo` con **`ArgumentList`**, no con `Arguments`, para que la ruta con
   espacios o acentos del perfil quede bien citada:
   `--serial <s> --record <outputPath> --record-format mkv --no-video-playback --max-size 1080 --video-bit-rate 4M <audioArg>`.
   `audioArg` es igual que hoy (`--audio-source=output` / `--no-audio`) y **no** se agrega
   `--no-audio-playback` (D12 b).
4. Variables de entorno (D3):
   - `psi.Environment["ADB"] = adbPath`, si `ToolResolver.Find(AgentTools.Adb)` resolvió algo.
   - `psi.Environment["SCRCPY_SERVER_PATH"] = <dir de scrcpy>/scrcpy-server`, solo si
     `Source == Portable`.
5. `UseShellExecute = false`, `CreateNoWindow = true` (igual que hoy). Esto le da a scrcpy **su
   propia consola oculta**, que es lo que permite el Ctrl+C de §6.3. **No** se usa
   `CreateNewProcessGroup`, porque deshabilitaría el Ctrl+C en scrcpy.
6. Drenaje de stdout/stderr como hoy, pero:
   - Las líneas que empiezan con `ERROR` o `WARN`, o que contienen
     `adb server version` / `doesn't match`, se loguean como **Warning**. El resto sigue en Debug.
   - Las últimas 20 líneas se guardan en un buffer circular con lock (`_scrcpyTail`), que se usa en
     E2 y E8.
7. Se espera hasta 1.5 s: `await Task.WhenAny(proc.WaitForExitAsync(ct), Task.Delay(1500, ct))`. Si
   scrcpy terminó en ese lapso, se limpia el estado y se lanza E2.
8. Si `withMic`, se inicia `MicCapture` (§6.4). Si eso falla (E4/E5/E6), se detiene scrcpy con
   `ProcessStop` (§6.3), se borran **solo** el MKV y el `.mic.mka` que creó este intento, se limpia
   el estado y se relanza el error.
9. Log Information: `scrcpy iniciado: serial=… audio=… mic=… adb=<ruta> output=…`.

### 6.3 Detener scrcpy limpio (D5): `Common/ProcessStop.cs` + `Common/WindowsConsoleSignal.cs`

**Técnica elegida: proceso auxiliar `Factum.Agent.exe --ctrl-c <pid>`.**

En Windows, `GenerateConsoleCtrlEvent(CTRL_C_EVENT, …)` solo llega a procesos que **comparten la
consola del que lo llama**, y con `CTRL_C_EVENT` no se puede apuntar a un grupo puntual. scrcpy corre
en su propia consola oculta (`CreateNoWindow`). Hacer el attach desde el propio agente obligaría a
hacerle `FreeConsole()` a su consola, que es la ventana minimizada "Tatana Agent" donde se ve el log.
Además se mezclaría con el `ConsoleLifetime` de ASP.NET, que escucha Ctrl+C y apagaría Tatana. Por
eso lo hace un proceso aparte, de vida corta:

```
Factum.Agent.exe --ctrl-c <pid>
  1. FreeConsole()                          // suelta la consola oculta propia
  2. AttachConsole(pid)                     // se pega a la consola de scrcpy; si falla → exit 2
  3. SetConsoleCtrlHandler(IntPtr.Zero, true) // este proceso ignora el Ctrl+C que va a generar
  4. GenerateConsoleCtrlEvent(CTRL_C_EVENT=0, 0) // a todos los procesos de esa consola; si falla → exit 3
  5. FreeConsole(); exit 0
```

- La rama va en la **primera línea de `Program.cs`**, antes de `WebApplication.CreateBuilder`, para
  que no levante el host ni registre handlers de consola:
  `if (args.Length == 2 && args[0] == "--ctrl-c") { Environment.Exit(WindowsConsoleSignal.RunHelper(args[1])); }`.
  Fuera de Windows, `RunHelper` devuelve 1 sin hacer nada.
- P/Invoke a `kernel32.dll`: `FreeConsole`, `AttachConsole(uint)`,
  `SetConsoleCtrlHandler(IntPtr, bool)` y `GenerateConsoleCtrlEvent(uint, uint)`, con
  `[SupportedOSPlatform("windows")]` para no sumar warnings CA1416.
- Cómo se lanza el auxiliar: `WindowsConsoleSignal.BuildHelperCommand(string processPath, string? entryAssemblyPath, int pid)`
  es una función **pura**, testeada:
  - Si `Path.GetFileNameWithoutExtension(processPath)` es `dotnet` (sin distinguir mayúsculas), se
    lanza `(processPath, [entryAssemblyPath, "--ctrl-c", pid])`. Es el caso de desarrollo con
    `dotnet Factum.Agent.dll`.
  - Si no, se lanza `(processPath, ["--ctrl-c", pid])`.
  - En producción, `processPath = Environment.ProcessPath` y `entryAssemblyPath = typeof(Program).Assembly.Location`.
    Ojo: en single-file `Location` es `""`, pero en ese caso nunca es `dotnet`.
  - Se lanza con `UseShellExecute=false`, `CreateNoWindow=true` y sin redirecciones, y se espera
    hasta **3 s**.
- **Defensivo, DP12:** en `Program.cs` (modo normal, solo Windows) se llama una vez a
  `SetConsoleCtrlHandler(IntPtr.Zero, false)` al arrancar. El atributo "ignorar Ctrl+C" se hereda a
  los hijos: si algún lanzador lo dejó encendido, scrcpy lo heredaría y no cortaría nunca.

`ProcessStop.StopGracefullyAsync(Process p, TimeSpan timeout, ILogger log, string name, CancellationToken ct) → Task<bool>`
devuelve `true` si cerró limpio y `false` si hubo que matarlo:

1. Si `p.HasExited`, devuelve `true`.
2. Manda la señal:
   - Windows: el auxiliar de arriba. Si devuelve un código ≠ 0 o no arranca, se loguea Warning
     `No se pudo enviar Ctrl+C a {name} (código …)` y se pasa directo al paso 4 con 1 s de espera.
   - No-Windows: `kill -2 <pid>`, **igual que hoy**.
3. `WaitForExitAsync` con `timeout`, que para scrcpy es **10 s** (DP3).
4. Si no terminó: `p.Kill(entireProcessTree: true)`, `WaitForExitAsync(ct)` y Warning
   `{name} no respondió a la señal; se forzó el cierre`. Devuelve `false`.
5. Si terminó: Information `{name} detenido limpiamente (exit {code})`. Devuelve `true`.

`StopRecordingAsync` (Android), en orden:

1. Si no hay grabación activa, se lanza E7.
2. `clean = await ProcessStop.StopGracefullyAsync(_recordingProcess, 10 s, …, "scrcpy")`.
3. Se limpia el estado de scrcpy (`Dispose`, null) **siempre**, en `finally`.
4. **Siempre** se detiene el mic si existe, aunque el MKV no exista: hoy se fuga el ffmpeg del mic
   en ese caso. `await _mic.StopAsync()` (§6.4).
5. Si el MKV no existe: se borra el `.mic.mka` propio si quedó y se lanza E8.
6. Si había mic y el `.mic.mka` existe, se hace `MuxMicAudioIntoVideoAsync`, igual que hoy pero con
   ffmpeg resuelto y `ArgumentList`. Ese mux reescribe el contenedor, así que **reemplaza al remux**.
   Solo si el mux falla y `clean == false`, se hace el remux del paso 7.
7. Si `clean == false` y no se hizo el mux: `RemuxAsync(outPath)` corre
   `ffmpeg -hide_banner -y -i <out> -map 0 -c copy <out>.remux.mkv`. Si sale bien, hace
   `File.Move(tmp, out, overwrite: true)` y loguea Information `MKV remuxeado tras cierre forzado`.
   Si falla o no hay ffmpeg, loguea Warning, borra el tmp y **deja el MKV crudo** (sigue siendo
   reproducible).
8. Devuelve `outPath`.

### 6.4 `Services/MicCapture.cs`: captura del mic de la PC (D6), compartida Android/iOS

```csharp
internal sealed class MicCapture
{
    // Lanza ffmpeg grabando el mic a `audioPath` (.mka). Lanza InvalidOperationException con E4/E5/E6.
    public static async Task<MicCapture> StartAsync(string audioPath, string? configuredDevice,
        ILogger log, CancellationToken ct);
    public string AudioPath { get; }
    // Manda "q" por stdin, espera 5 s, y si no, Kill(entireProcessTree). Nunca lanza.
    public Task StopAsync();
}

internal static class DshowDevices
{
    // Parsea el stderr de `ffmpeg -hide_banner -list_devices true -f dshow -i dummy`.
    public static IReadOnlyList<(string Name, string? AlternativeName)> ParseAudioDevices(string stderr);
}
```

- ffmpeg sale de `ToolResolver.Find(AgentTools.Ffmpeg)`; si no se encuentra, E4.
- Entrada por SO:
  - **Windows:** `-f dshow -i audio=<dispositivo>`, pasado como **un** elemento de `ArgumentList`
    (`"audio=" + dispositivo`). Para elegir el dispositivo:
    1. Si `Agent:MicDevice` (nuevo `AgentOptions.MicDevice`, `string?`, default null) tiene valor,
       se usa ese.
    2. Si no, se corre `ffmpeg -hide_banner -list_devices true -f dshow -i dummy`. Sale con código
       ≠ 0, y eso es normal. Se lee stderr con `StandardErrorEncoding = UTF8` y se toma el **primer
       dispositivo de audio**: su `AlternativeName` si existe (`@device_cm_{…}\wave_{…}`, ASCII y
       sin problemas de acentos), y si no, su `Name`.
    3. Si no hay ninguno, E5. Se loguea Information con el nombre amigable elegido.
  - **macOS:** `-f avfoundation -i :0`, igual que hoy.
  - **Linux:** `-f alsa -i default`, igual que hoy.
- Salida: `-y <entrada> -c:a aac -b:a 128k <audioPath>`. `audioPath` termina en **`.mka`**
  (Matroska): si hay que matar a ffmpeg, el archivo sigue siendo legible. Con `.m4a` quedaba sin
  `moov` y el mux fallaba (DP5).
- `RedirectStandardInput = true` (para `q`), stdout y stderr drenados, y las últimas 10 líneas de
  stderr en un buffer para E6. `CreateNoWindow = true`.
- Tras lanzar ffmpeg se espera hasta 1.5 s. Si terminó, E6 con la última línea de stderr. Lo mismo
  si `Process.Start` devuelve null.
- `ParseAudioDevices` tiene que soportar los dos formatos de ffmpeg:
  - **Nuevo (≥ 4.4, el de BtbN 8.x):** líneas `[in#0 @ 0x…] "Micrófono (Realtek(R) Audio)" (audio)`
    o `[dshow @ 0x…] "…" (audio)`, seguidas de `[…]   Alternative name "@device_cm_{…}\wave_{…}"`.
    El alternative name se asocia al dispositivo inmediatamente anterior. Las entradas `(video)` y
    `(none)` se ignoran.
  - **Viejo:** sección `DirectShow audio devices`, con líneas `"nombre"` y
    `Alternative name "…"` hasta `DirectShow video devices` o el fin.
- **Android:** `AdbService` reemplaza `StartMicAudio`/`StopMicAudio` por `MicCapture`. El orden se
  mantiene: primero scrcpy y después el mic, así no cambia el desfase actual. El archivo es
  `videoPath + ".mic.mka"`.
- **iOS (`with_mic`), DP7:** `IosService.StartAudio`/`StopAudio` pasan a usar `MicCapture`, con
  `session.Mic` en lugar de `AudioProc`/`AudioPath`. Pero **si falla, solo se loguea Warning**, como
  hoy (no se lanza error), porque iOS no se puede probar en esta HU. Además se corrige que en el
  fallback burst (`StopBurstAsync`) el mic **nunca se detenía** antes del ffmpeg final: ahora se
  llama a `await session.Mic.StopAsync()` antes de los dos caminos de stop (DVT y burst). Los
  comandos de mux de iOS no cambian: reciben la ruta, ahora `.mka`.

### 6.5 Logs que la prueba manual busca (texto fijo, Information/Warning)

| Momento | Texto (prefijo) |
|---|---|
| Inicio | `scrcpy iniciado: serial=` |
| Stop limpio | `scrcpy detenido limpiamente (exit ` |
| Stop forzado | `scrcpy no respondió a la señal; se forzó el cierre` y luego `MKV remuxeado tras cierre forzado` |
| Mic elegido | `Micrófono de la PC: ` + nombre amigable |
| Conflicto adb | Cualquier línea de scrcpy con `adb server version` (Warning; con D3 no debería aparecer) |

### 6.6 `Services/ToolInventory.cs` + `/health` (D8)

- Se registra como singleton en `Program.cs` y se inyecta en `HealthController`. `Health()` sigue
  siendo sincrónico.
- `Snapshot()` devuelve un `Dictionary<string, ToolStatus>` con `adb`, `scrcpy`, `ffmpeg` y
  `python`. Por cada uno llama a `ToolResolver.Find`, que es barato, y busca la versión en un
  `ConcurrentDictionary<string path, string?>`. Si la ruta no está en el caché, dispara
  **una sola vez** por ruta, en segundo plano, la obtención de la versión, y en esa respuesta
  devuelve `version` null.
- `WarmUp()` se llama en `Program.cs` justo antes del polling: lanza en segundo plano la obtención
  de las 4 versiones, así cuando el perito abre "Diagnóstico de Factum" ya están.
- Obtener la versión: se corre el binario con **timeout de 5 s** y, si se pasa, `Kill`. Se usa un
  `Process` local, porque `ProcessRunner` no mata al proceso cuando hay timeout. Los parsers son
  `internal static` en `VersionParser` y están testeados:

| Tool | Args | Parseo |
|---|---|---|
| adb | `version` | Línea que empieza con `Version ` → resto (`37.0.1-13426479`). Si no está, la 1ª línea. |
| scrcpy | `--version` | 1ª línea `scrcpy 4.1 <https://…>` → 2º token (`4.1`). |
| ffmpeg | `-version` | 1ª línea `ffmpeg version <X> Copyright…` → token después de `version`. |
| python | `--version` | `Python 3.11.9` → `3.11.9` (stdout o stderr). |

- No se agrega `ffprobe`, `uxplay`, `qvh` ni `rife` al bloque: la HU pide las cuatro de arriba.

## 7. Diseño del client (`implementer-frontend`, solo `client/`)

`client/src/lib/agent.ts`:

```ts
/** Lee `{ error }` de una respuesta no-2xx de Tatana; si no viene, usa `fallback`. */
async function readAgentError(res: Response, fallback: string): Promise<Error> {
  const data = (await res.json().catch(() => null)) as { error?: unknown } | null;
  const msg = data && typeof data.error === "string" && data.error.trim() ? data.error.trim() : fallback;
  return new Error(msg);
}
const AGENT_UNREACHABLE = "No se pudo conectar con Tatana. Revisá que esté abierto en esta PC.";
```

- `startRecording`: si `fetch` lanza (Tatana caído, `TypeError`), `throw new Error(AGENT_UNREACHABLE)`.
  Si `!res.ok`, `throw await readAgentError(res, "Error iniciando grabación")`.
- `stopRecording`: lo mismo, con fallback `"Error deteniendo grabación"`.
- **No** se tocan los demás métodos (fuera de alcance), aunque tengan el mismo patrón.

`client/src/hooks/useRecording.ts`, rama `isRecording` de `handleToggleRecord`:

```ts
try {
  const stopped = await agent.stopRecording(device.serial, platform);
  if (stopped?.filename) addFile(stopped.filename);
} catch (e) {
  onError(`No se pudo guardar la grabación: ${e instanceof Error ? e.message : "error desconocido"}`);
} finally {
  setLoad("stopRecord", false);
}
```

Después se mantiene `setRecording(false)` y `setIosRecordMode(null)`. El estado del agente ya se
limpió, así que reintentar el stop no sirve. La rama de start ya hace `onError(e.message)`: no cambia,
pero ahora el mensaje es el de Tatana. El aviso sale en el `FxBanner` de `globalError` que ya existe,
así que no hay cambios visuales. iOS usa el mismo `startRecording`/`stopRecording`: también se
beneficia, sin que haga falta otro cambio.

## 8. Empaquetado (`implementer-backend`)

### 8.1 `deploy/windows/armar-tatana-portable.sh`

Constantes al principio del script, con un comentario que diga cómo actualizarlas (cambiar las dos y
sacar el hash del `SHA256SUMS.txt` del release):

```bash
SCRCPY_VERSION="4.1"
SCRCPY_WIN64_SHA256="5b12172b3264b2889f4583ee64752ce832e29bc8b1089dca81093459697165db"
```

El hash está verificado por el architect el 2026-10-02 contra el `digest` de la API de GitHub y con
`shasum -a 256` del zip descargado. El zip de v4.1 trae: `scrcpy.exe`, `scrcpy-server`, `SDL3.dll`,
`avcodec-62.dll`, `avformat-62.dll`, `avutil-60.dll`, `swresample-6.dll`, `libusb-1.0.dll`,
`LICENSE.txt`, `scrcpy.png`, `disconnected.png`, `scrcpy-noconsole.vbs`, `open_a_terminal_here.bat`,
**`adb.exe`, `AdbWinApi.dll` y `AdbWinUsbApi.dll`**.

Los pasos se renumeran de `[n/6]` a `[n/7]`. **Paso nuevo [4/7] "scrcpy <versión> (Genymobile, Apache-2.0)…"**,
después de platform-tools:

1. `curl -fsSL -o "$TMP/scrcpy-win64.zip" https://github.com/Genymobile/scrcpy/releases/download/v$SCRCPY_VERSION/scrcpy-win64-v$SCRCPY_VERSION.zip`.
2. `echo "$SCRCPY_WIN64_SHA256  $TMP/scrcpy-win64.zip" | shasum -a 256 -c -`. Si falla:
   `ERROR: el SHA-256 de scrcpy no coincide (¿descarga corrupta o release reemplazado?).` y `exit 1`.
3. Se descomprime en `$TMP/scrcpy`, se copia el contenido de `scrcpy-win64-v$SCRCPY_VERSION/` a
   `$OUT/tools/scrcpy/` y **se borran** `adb.exe`, `AdbWinApi.dll` y `AdbWinUsbApi.dll` de ahí.
4. Se genera `$OUT/tools/scrcpy/THIRD-PARTY-NOTICES.txt` con CRLF (mismo `sed 's/$/\r/'` que el
   `.ini`):
   - scrcpy `$SCRCPY_VERSION`: Apache-2.0, https://github.com/Genymobile/scrcpy, SHA-256 del zip,
     "texto completo en LICENSE.txt". Se aclara que se quitaron `adb.exe` y `AdbWin*.dll` y que
     Tatana usa el adb de `tools/platform-tools`.
   - FFmpeg 8.1.2 (`avcodec`/`avformat`/`avutil`/`swresample`): LGPL v2.1 o posterior,
     https://ffmpeg.org. El código fuente es el de la release, y la configuración de build está en
     https://github.com/Genymobile/scrcpy/blob/v4.1/app/deps/ffmpeg.sh.
   - SDL 3.4.12 (`SDL3.dll`): zlib, https://libsdl.org.
   - libusb 1.0.30 (`libusb-1.0.dll`): LGPL v2.1 o posterior, https://libusb.info.
   - dav1d 1.5.3 (enlazado en FFmpeg): BSD-2-Clause, https://code.videolan.org/videolan/dav1d.

   Las versiones salen de `app/deps/*.sh` del tag v4.1 (verificado por el architect). El
   comentario de las constantes recuerda revisarlas al cambiar `SCRCPY_VERSION`.
5. `OK  scrcpy $SCRCPY_VERSION (sin adb propio)`.

**Chequeo del zip final (D9)**, después del `zip -rq`. Se recorre `unzip -Z1 "$ZIP"` y el script
falla con un mensaje claro si:
- Falta alguno de: `tools/scrcpy/scrcpy.exe`, `tools/scrcpy/scrcpy-server`, `tools/scrcpy/SDL3.dll`,
  `tools/scrcpy/LICENSE.txt`, `tools/scrcpy/THIRD-PARTY-NOTICES.txt`,
  `tools/platform-tools/adb.exe`, `tools/ffmpeg/ffmpeg.exe`, `Factum.Agent.exe`.
- Aparece `tools/scrcpy/adb.exe` o algún `tools/scrcpy/AdbWin*.dll`.

Si todo está, imprime `OK  contenido del zip verificado`. platform-tools y ffmpeg siguen en "latest",
**sin cambios** (fuera de alcance).

### 8.2 `packaging/portable/install-portable.bat`

Después del `xcopy … >nul` se agrega:
`if errorlevel 1 ( echo ERROR: no se pudieron copiar los archivos de Tatana a %DEST% ^(¿algun programa de Tatana sigue abierto?^). & exit /b 1 )`.
Hoy, si `xcopy` falla a mitad de camino, el `.bat` sigue y deja un Tatana viejo con `tools/` a medias.

### 8.3 `deploy/windows/scripts/_comun.ps1` → `Install-TatanaPortable` (DP9)

Hoy solo frena `Factum.Agent`. El servidor adb (`tools\platform-tools\adb.exe start-server`) queda
vivo, con `adb.exe` bloqueado, y `xcopy` falla. Se agrega, después de frenar el agente:

```powershell
$destino = Join-Path $env:LOCALAPPDATA 'Programs\Tatana'
$deTatana = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $ruta = $null; try { $ruta = $_.Path } catch { }
    $ruta -and $ruta.StartsWith($destino + '\', [StringComparison]::OrdinalIgnoreCase) })
if ($deTatana.Count -gt 0) {
    Write-Host ('    Cerrando ' + $deTatana.Count + ' proceso(s) de Tatana (adb, scrcpy, ffmpeg)...') -ForegroundColor DarkGray
    $deTatana | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}
```

Efecto visible: durante la actualización se cierra el servidor adb de Tatana y los celulares
"desaparecen" hasta que Tatana arranca de nuevo, lo que pasa al final del mismo paso.

### 8.4 `diagnostico.ps1` + `_comun.ps1` → `Get-LineasHerramientasTatana` (D8)

Función nueva en `_comun.ps1`. Recibe el objeto `$Health` y devuelve una lista de
`[pscustomobject]@{ Ok = [bool]; Texto = [string] }`, para que se pueda testear sin consola:

- `$Health` null: lista vacía (el aviso de "no responde" ya lo da `Test-TatanaReal`).
- Sin `tools` (Tatana ≤ 1.0.0): una entrada `Ok=$false`,
  `Texto='Esta versión de Tatana no informa sus herramientas. Actualizala con "Actualizar Factum".'`.
- Si no, por cada `adb`, `scrcpy`, `ffmpeg`, `python`, en ese orden:
  - `found=$true`: `Ok=$true`, `Texto='<nombre>: OK  <version o "(versión desconocida)">  (<path>)'`.
  - Si no: `Ok=$false`, `Texto='<nombre>: FALTA'`.

`diagnostico.ps1` (sección Tatana, después de `Test-TatanaReal`) imprime cada línea con `Write-Ok` si
`Ok` y con `Write-Falla` si no, bajo un subtítulo `'  Herramientas de Tatana:'`. Para mantener la
compatibilidad con PS 5.1 se usa `Get-PropiedadSegura` y nada de `?.`.

### 8.5 Versión del paquete (D10)

No hay código. El paquete se arma con `deploy/windows/armar-paquete.sh --version 1.1.0`, **después**
del merge de esta HU y de `subida-archivos-grandes`. Eso lo hace el usuario o el orquestador, no el
implementador. `actualizar.ps1` ya ofrece Tatana porque `1.1.0 ≠ 1.0.0`.

### 8.6 Guía (`docs/instalacion-windows.md`)

- Sección 8, "Comprobar Tatana": explicar que el diagnóstico lista `adb`, `scrcpy`, `ffmpeg` y
  `python` con OK/FALTA, y que `scrcpy: FALTA` se arregla con "Actualizar Factum".
- En la parte de armado: "descarga adb, Python embebido + pymobiledevice3, ffmpeg y **scrcpy (versión
  fija, verificada por SHA-256)**".
- Nota de una línea: el micrófono se elige solo (el primero que lista Windows). Si toma el que no es,
  se puede fijar `"Agent": { "MicDevice": "<nombre>" }` en `appsettings.Local.json` junto a
  `Factum.Agent.exe`. El nombre sale de
  `tools\ffmpeg\ffmpeg.exe -hide_banner -list_devices true -f dshow -i dummy`.

## 9. Decisiones técnicas

| # | Decisión | ¿Usuario? |
|---|---|---|
| DP1 | **scrcpy 4.1** (última estable, 2026-07-12) con SHA-256 `5b12172b…65db` del zip win64. En la Mac hay 4.0 de Homebrew. El architect verificó con el binario macOS 4.1 que existen todos los flags que usa Tatana (`--record`, `--record-format`, `--no-video-playback`, `--max-size`, `--video-bit-rate`, `--audio-source`, `--no-audio`, `--serial`) y las variables `ADB` y `SCRCPY_SERVER_PATH`. Se confirma el conflicto de D3: scrcpy 4.1 trae adb 37.0.0 y Google publica hoy 37.0.1. | no (D1) |
| DP2 | Ctrl+C con un proceso auxiliar: el mismo `Factum.Agent.exe --ctrl-c <pid>` (FreeConsole → AttachConsole → ignorar → GenerateConsoleCtrlEvent). Se descartó: hacerlo dentro del agente (le rompe su consola y el `ConsoleLifetime`), `CTRL_BREAK_EVENT` + `CREATE_NEW_PROCESS_GROUP` (el CRT lo mapea a SIGBREAK, scrcpy no lo atrapa y muere sin cerrar el MKV, y además en modo Electron el agente no tiene consola), un `windows-kill.exe` de terceros (un binario más) y PowerShell `Add-Type` (1–2 s por stop). Si `AttachConsole` fallara en la PC real, el fallback Kill + remux igual deja un MKV con duración. | no (D5) |
| DP3 | Timeouts: auxiliar 3 s, scrcpy 10 s (antes 5 s), mic 5 s. En el remux tras matar a scrcpy, **el archivo remuxeado reemplaza al crudo** (mismo nombre), igual que ya hace el mux del mic. Se loguea. | no |
| DP4 | Detección temprana: si scrcpy o ffmpeg-mic mueren en menos de 1.5 s, el start falla con el error real (E2/E6). Suma hasta 1.5 s al start (hasta 3 s con mic). Las líneas `ERROR`/`WARN` de scrcpy pasan a Warning en el log. | no |
| DP5 | Mic: `dshow`, el primer dispositivo de audio (por su *alternative name*), override opcional `Agent:MicDevice` sin UI, temporal en `.mka` en vez de `.m4a` y cierre con `q` por stdin en los tres SO (en la Mac hoy es SIGINT; `q` es igual de limpio). | no |
| DP6 | **Android con mic: si no hay ffmpeg o micrófono, o el mic no abre, el start FALLA con E4/E5/E6.** Hoy graba solo video sin avisar. También cambia en la Mac, pero solo si falta ffmpeg o el permiso de micrófono. | **sí**: confirma un cambio de comportamiento visible (antes: video sin audio en silencio; ahora: error y no graba) |
| DP7 | iOS `with_mic` comparte `MicCapture` (D6 a), pero ante una falla **solo loguea**, como hoy, porque no hay forma de probarlo. Se arregla que en el fallback burst el mic no se detenía antes del ffmpeg final. | no |
| DP8 | `/health.tools` con claves fijas `adb/scrcpy/ffmpeg/python`, `source` como string, versiones en caché con warm-up al arrancar y nulls omitidos. | no |
| DP9 | `Install-TatanaPortable` cierra todo proceso cuyo ejecutable esté en `%LOCALAPPDATA%\Programs\Tatana\` (adb, scrcpy, ffmpeg) antes del `xcopy`, e `install-portable.bat` corta si `xcopy` falla. Sin esto, el escenario "la corrección llega con Actualizar Factum" puede fallar por `adb.exe` bloqueado. | informativa |
| DP10 | El proyecto de tests es `server/tests/Factum.Agent.Tests` con `ProjectReference` al agente, `<ValidateExecutableReferencesMatchSelfContained>false</…>` (el agente es `SelfContained`) e `<InternalsVisibleTo Include="Factum.Agent.Tests" />` en el csproj del agente. El architect probó esta combinación en una copia y compila y corre. No se agrega a `Factum.sln`, igual que `Factum.Backend.Tests`. | no |
| DP11 | Client: mensaje propio si Tatana no responde (`No se pudo conectar con Tatana…`) y el prefijo `No se pudo guardar la grabación: ` solo en el stop. | no |
| DP12 | `SetConsoleCtrlHandler(NULL, FALSE)` al arrancar el agente en Windows, para que scrcpy no herede un "ignorar Ctrl+C". | no |
| DP13 | `WebcamService` **no** se toca. D4 a) lo nombraba, pero D11 b) lo deja explícitamente fuera, y manda D11 por ser más específica. Queda para `ios-herramientas-windows` o una HU de webcam. | no |
| DP14 | Licencias: esta HU agrega `LICENSE.txt` y `THIRD-PARTY-NOTICES.txt` **solo para scrcpy** (D2). El portátil sigue redistribuyendo ffmpeg BtbN (**GPL**), platform-tools y Python/pymobiledevice3 sin ningún aviso de licencia. | **sí**: ¿abrir una HU chica para un aviso de terceros de todo `tools/`? (recomendada: sí, antes de entregar a un segundo cliente) |

## 10. Checklist atómico

### 10.1 Backend (`implementer-backend`): agente

- [ ] B1. `Factum.Agent.csproj`: `<ItemGroup><InternalsVisibleTo Include="Factum.Agent.Tests" /></ItemGroup>`.
- [ ] B2. `Common/ToolResolver.cs`: `ToolSource`, `ToolSpec`, `ToolResolution`, `ToolEnvironment`
      (+ `Current`), `AgentTools` y `ToolResolver.Find` con el algoritmo exacto de §6.1.
- [ ] B3. `AdbService`: borrar `ScrcpyPaths`/`FfmpegPaths`/`FindBinary`/`IsOnPath`/`FindScrcpy`,
      y `AdbPath` pasa a usar `ToolResolver` con el fallback `"adb"`.
- [ ] B4. `IosService`: borrar `BuildToolCandidates`/`*Paths`/`FindBinary(string[])`/`IsOnPath`,
      agregar `FindBinary(ToolSpec)` y actualizar todas las llamadas. `PythonCandidates` y
      `DvtRecorderScript` sin cambios.
- [ ] B5. `Common/WindowsConsoleSignal.cs`: P/Invoke, `RunHelper(string pid)` y la función pura
      `BuildHelperCommand(processPath, entryAssemblyPath, pid)`.
- [ ] B6. `Program.cs`: rama `--ctrl-c` en la **primera** línea; en Windows,
      `SetConsoleCtrlHandler(IntPtr.Zero, false)`; registrar `ToolInventory` como singleton y llamar
      a `WarmUp()` antes del polling.
- [ ] B7. `Common/ProcessStop.cs`: `StopGracefullyAsync` (§6.3). En Unix, `kill -2` como hoy.
- [ ] B8. `AgentOptions.MicDevice` (`string?`).
- [ ] B9. `Services/MicCapture.cs` + `DshowDevices.ParseAudioDevices` (§6.4), con los mensajes
      E4/E5/E6 exactos.
- [ ] B10. `AdbService.StartRecordingAsync`: E3, E1 (+ `scrcpy-server`), `ArgumentList`, `ADB`,
      `SCRCPY_SERVER_PATH`, buffer de líneas, logs Warning, espera de 1.5 s y E2; mic con
      `MicCapture` y rollback si falla.
- [ ] B11. `AdbService.StopRecordingAsync`: E7, `ProcessStop`, mic siempre detenido, E8, mux del mic
      y remux si hubo cierre forzado (§6.3, pasos 1–8).
- [ ] B12. `IosService` `with_mic`: `MicCapture` solo con Warning y stop del mic antes del burst
      (§6.4).
- [ ] B13. `Services/ToolInventory.cs` + `VersionParser` (§6.6), y `HealthController.Health()` agrega
      `tools = inventory.Snapshot()`.
- [ ] B14. Revisar que **ningún** mensaje de error Android mencione `brew` fuera de macOS
      (`grep -n "brew" server/src/Factum.Agent/Services/AdbService.cs`). Los de iOS se quedan como
      están (D11 b).
- [ ] B15. `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` sin warnings nuevos.

### 10.2 Backend (`implementer-backend`): tests .NET (D9)

- [ ] T1. `server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj`: copia del de
      `Factum.Backend.Tests` con `ProjectReference` a `../../src/Factum.Agent/Factum.Agent.csproj` y
      `<ValidateExecutableReferencesMatchSelfContained>false</ValidateExecutableReferencesMatchSelfContained>`.
- [ ] T2. `ToolResolverTests.cs`. Cada test arma su propio layout falso en
      `Path.Combine(Path.GetTempPath(), "factum-tools-" + Guid)` y lo borra en `Dispose`, y construye
      un `ToolEnvironment` a mano (nunca toca el PATH real):
  - T2a. Windows: `tools/scrcpy/scrcpy.exe` existe → `Source=Portable` y ruta absoluta.
  - T2b. Windows: el portátil gana aunque `scrcpy.exe` también esté en el PATH.
  - T2c. Windows: sin portátil, `scrcpy.exe` en el 2º directorio de un PATH separado con `;` →
    `Source=Path` y ruta completa (este es **el bug del reporte**).
  - T2d. Windows: un archivo `scrcpy` **sin extensión** en el PATH, sin `scrcpy.exe` → `null`.
  - T2e. Windows, `WinName=null`, `UnixName="qvh"`, `PATHEXT=".BAT;.EXE"`, con `qvh.bat` y `qvh.exe`
    en el mismo directorio → devuelve `qvh.exe`.
  - T2f. Windows: entradas vacías, con comillas (`"<dir>"`) y relativas (`.`) en el PATH se toleran
    o ignoran; la entrada con comillas resuelve.
  - T2g. Windows: los `FallbackDirs` (Homebrew falsos) se **ignoran**.
  - T2h. Unix: PATH separado con `:` → `Source=Path`.
  - T2i. Unix: no está en el PATH pero sí en un `FallbackDir` falso → `Source=Homebrew`.
  - T2j. Nada en ningún lado → `null`.
- [ ] T3. `DshowDevicesTests.cs`: un fixture del formato nuevo con 1 cámara + 2 mics (uno con
      acentos) devuelve los 2 mics en orden, con su alternative name. Un fixture del formato viejo
      devuelve los mics. Un fixture solo con `(video)` devuelve una lista vacía.
- [ ] T4. `VersionParserTests.cs`: adb, scrcpy (`scrcpy 4.1 <https://github.com/Genymobile/scrcpy>`),
      ffmpeg (`ffmpeg version 8.1.2 Copyright (c) 2000-2026…` y
      `ffmpeg version N-121345-g0a1b2c3d4e-20261001 Copyright…`) y python.
- [ ] T5. `WindowsConsoleSignalTests.cs`: `BuildHelperCommand` con `…\Factum.Agent.exe` y con
      `…/dotnet` + dll.
- [ ] T6. `dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj` en verde.

### 10.3 Backend (`implementer-backend`): empaquetado, scripts y guía

- [ ] P1. `armar-tatana-portable.sh`: constantes, paso [4/7] de scrcpy (descarga, `shasum -c`,
      copia sin adb, `THIRD-PARTY-NOTICES.txt` en CRLF) y renumeración a `/7`.
- [ ] P2. `armar-tatana-portable.sh`: chequeo del contenido del zip final (§8.1).
- [ ] P3. `packaging/portable/install-portable.bat`: corte si `xcopy` falla (§8.2). El `.bat` sigue
      siendo ASCII, sin tildes en los `echo`.
- [ ] P4. `_comun.ps1`: `Install-TatanaPortable` cierra los procesos de Tatana (§8.3).
- [ ] P5. `_comun.ps1`: `Get-LineasHerramientasTatana` (§8.4), y `diagnostico.ps1` la imprime.
- [ ] P6. `deploy/windows/tests/comun.Tests.ps1`: `Describe 'Get-LineasHerramientasTatana'` con 4
      casos: `$null`, sin `tools`, todas OK (con y sin `version`) y `scrcpy` con `found=false`.
- [ ] P7. PSScriptAnalyzer + Pester en verde con el comando de la guía (Docker `pwsh`).
- [ ] P8. `docs/instalacion-windows.md` (§8.6).
- [ ] P9. `progress/impl_backend_grabacion-android-windows.md` con: archivos tocados, salida de
      V1–V7 y lo que queda para la PC del estudio. **No** tocar `backlog.json` ni
      `progress/current.md`.

### 10.4 Frontend (`implementer-frontend`, **solo `client/`**)

- [ ] F1. Leer `client/AGENTS.md`.
- [ ] F2. `client/src/lib/agent.ts`: `readAgentError` + `AGENT_UNREACHABLE` y aplicarlos en
      `startRecording`/`stopRecording` (§7).
- [ ] F3. `client/src/hooks/useRecording.ts`: `try/catch` del stop con
      `onError("No se pudo guardar la grabación: …")` (§7).
- [ ] F4. Skills obligatorias (`ui-ux-pro-max`, `senior-frontend`, `3d-web-experience`,
      `web-design-guidelines`): invocarlas y dejar constancia; lo esperable es "sin hallazgos
      aplicables", porque no hay cambios visuales.
- [ ] F5. `cd client && npx tsc --noEmit` limpio.
- [ ] F6. `progress/impl_frontend_grabacion-android-windows.md`. **No** tocar `backlog.json` ni
      `progress/current.md`.

## 11. Verificación

### 11.1 En la Mac (cada implementador, antes de `done`)

**Backend:**

| # | Comando | Qué tiene que dar |
|---|---|---|
| V1 | `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` | 0 errores, sin warnings nuevos (sobre todo CA1416). |
| V2 | `dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj` | Todo en verde (T2–T5). |
| V3 | `dotnet publish server/src/Factum.Agent/Factum.Agent.csproj -c Release -r win-x64 --self-contained true -o <scratch>/win` | Genera `Factum.Agent.exe` (compila el P/Invoke para Windows). |
| V4 | Agente en mock (`dotnet run --project server/src/Factum.Agent -- --mock --port 8799`) + `curl -s localhost:8799/health` | Trae `tools` con las 4 claves. En la Mac, `scrcpy`/`ffmpeg` dan `found:true, source:"homebrew"` o `"path"`, y la `version` aparece en la 2ª llamada a más tardar. `/health` responde en < 100 ms. |
| V5 | Mismo agente en mock: `curl -s -XPOST localhost:8799/devices/X/record/stop` sin grabación | 500 con `{"error":"No hay una grabación en curso."}`. |
| V6 | Árbol limpio + portátil: `SRC=$(mktemp -d); git archive HEAD \| tar -x -C "$SRC"`. Después se copian encima **solo** los archivos de la HU: `git diff --name-only HEAD -- server packaging deploy docs` + `git ls-files --others --exclude-standard server packaging deploy`, **excluyendo** `server/src/Factum.Agent/appsettings.json`. Luego `deploy/windows/armar-tatana-portable.sh --version 1.1.0-verif --src "$SRC" --salida <scratch>` | Termina con `OK  contenido del zip verificado`. |
| V7 | Sobre el zip de V6: `unzip -Z1 <zip> \| grep '^tools/scrcpy/'` y `unzip -p <zip> tools/scrcpy/THIRD-PARTY-NOTICES.txt` | Están `scrcpy.exe`, `scrcpy-server`, DLLs, `LICENSE.txt` y `THIRD-PARTY-NOTICES.txt`; **no** están `adb.exe` ni `AdbWin*`. Las notices dicen 4.1 y el SHA. Se prueba **una vez** con `SCRCPY_WIN64_SHA256` alterado (copia local del script en el scratch) y el armado aborta con el mensaje de §8.1. |

Además, el implementador backend corre P7 (Pester) y deja la salida en el progress. Lo que se crea en
V4/V5 (mock) va al `agent-data` de desarrollo, que está en `.gitignore`. Rige la regla de datos de
`AGENTS.md`: no se borra nada que no haya creado la prueba.

**Frontend:** V8 `cd client && npx tsc --noEmit`. V9 manual, con el client en dev + agente en mock
(V4):

- (a) Grabar → detener reiniciando el agente en el medio (Ctrl+C a `dotnet run` y relanzarlo) →
  aparece el banner `No se pudo guardar la grabación: No hay una grabación en curso.`
- (b) Con el agente apagado → "Grabar" → banner `No se pudo conectar con Tatana…`.
- (c) Opcional, agente **sin** mock y sin Android conectado → "Grabar" → banner
  `scrcpy no pudo iniciar la grabación: …` con el texto real de scrcpy. Prueba E2 y la Mac sin
  romper nada.

### 11.2 Guion de prueba manual en la PC del estudio (usuario)

Requisitos: el paquete **Factum 1.1.0**, armado con `deploy/windows/armar-paquete.sh --version 1.1.0`
después del merge; un Android 11+ (y, si hay, uno ≤ 10) con depuración USB autorizada; VLC o
"Películas y TV". Para abreviar, `T=%LOCALAPPDATA%\Programs\Tatana` y `D=%LOCALAPPDATA%\Tatana\data`.

1. **Actualizar.** Copiar el paquete y correr "Actualizar Factum". En el paso 9 tiene que preguntar
   `Hay una versión nueva de Tatana (1.1.0; instalada: 1.0.0)`: responder **S**. Debería verse
   `Cerrando N proceso(s) de Tatana…` y ningún error de `xcopy`. Después, en cmd:
   `dir "%T%\tools\scrcpy"`. Tiene que tener `scrcpy.exe`, `scrcpy-server`, `LICENSE.txt` y
   `THIRD-PARTY-NOTICES.txt`, y **no** `adb.exe`. `type "%T%\version.txt"` tiene que dar `1.1.0`.
2. **Diagnóstico.** Correr "Diagnostico de Factum". En la sección Tatana tienen que aparecer
   `adb: OK`, `scrcpy: OK  4.1`, `ffmpeg: OK` y `python: OK`, cada una con su ruta bajo `%T%\tools`.
3. **(Opcional, aísla scrcpy de Tatana)** Correr los comandos de "Notas de implementación" de la HU,
   con `set ADB=%T%\tools\platform-tools\adb.exe` y desde `%T%\tools\scrcpy`.
4. **Grabar sin mic (Android 11+).** En Factum, paso de captura, "Grabar". Reproducir un video con
   sonido en el celular (el audio se escucha en la PC: D12 b), esperar **30 s** y "Detener".
   Resultados esperados:
   - "Detener" responde en ≤ 3 s y el archivo aparece en la bandeja.
   - En la ventana minimizada "Tatana Agent" está `scrcpy detenido limpiamente (exit 0)` y **no**
     está `se forzó el cierre`.
   - **Antes de "Subir y continuar"**:
     `"%T%\tools\ffmpeg\ffmpeg.exe" -hide_banner -i "%D%\<el grabacion_….mkv más nuevo>"`. Tiene que
     mostrar `Duration: 00:00:3x.xx`, **no** `N/A`, más un stream `Video: h264` y uno `Audio: opus`.
     Si hay MediaInfo, "Duración" tiene que tener valor.
   - Abrirlo en VLC: se reproduce entero y se puede adelantar y retroceder con la barra.
5. **Android ≤ 10** (si hay): grabar y detener. El `.mkv` tiene **solo** `Video:` y no aparece
   ningún error.
6. **Con mic de la PC.** Activar "con mic de PC", grabar 20 s hablando cerca de la PC y detener. En
   el log tiene que estar `Micrófono de la PC: <nombre>`. `ffmpeg -i` del archivo muestra
   `Audio: aac` y al reproducirlo se escucha la voz. Si tomó el micrófono equivocado, listar con
   `"%T%\tools\ffmpeg\ffmpeg.exe" -hide_banner -list_devices true -f dshow -i dummy` y fijarlo con
   `MicDevice` (§8.6).
7. **Estabilidad de adb.** Grabar **2 minutos** mirando la lista de dispositivos de Factum: el
   celular no tiene que desaparecer ni reaparecer. En el log de Tatana no tiene que haber ninguna
   línea con `adb server version`.
8. **Falta scrcpy.** Cerrar la ventana "Tatana Agent", renombrar `%T%\tools\scrcpy` a `scrcpy_x` y
   correr `%T%\launch-tatana.bat`. "Grabar" tiene que mostrar el banner `No se encontró el componente
   de grabación de Android (scrcpy). Actualizá o reinstalá Tatana con el paquete de Factum.` (sin
   "brew"). Diagnóstico tiene que decir `scrcpy: FALTA`. **Volver a renombrar a `scrcpy`** y
   reiniciar Tatana.
9. **Falla al detener.** "Grabar", cerrar la ventana "Tatana Agent", relanzar `launch-tatana.bat` y
   "Detener". Tiene que aparecer el banner `No se pudo guardar la grabación: No hay una grabación en
   curso.` Después, en el Administrador de tareas, **finalizar el `scrcpy.exe` huérfano** que quedó
   de la grabación interrumpida. Es una limitación conocida, ver R3.
10. **Si algo de 4/6 falla**, mandar al proveedor: la salida de Diagnóstico, las últimas líneas de la
    ventana "Tatana Agent" y la salida de `ffmpeg -i` del archivo.

### 11.3 Riesgos

- **R1:** que `AttachConsole` a un proceso creado con `CREATE_NO_WINDOW` no funcione en esa PC. En
  ese caso el paso 4 muestra `se forzó el cierre` + `MKV remuxeado…`. El archivo igual queda con
  duración y se puede adelantar (el escenario se cumple), pero se pierde el último ~1 s. Habría que
  reportarlo para iterar.
- **R2:** que el primer micrófono de `dshow` no sea el que el perito usa. Se mitiga con
  `Agent:MicDevice`.
- **R3:** si Tatana se cierra a la fuerza con una grabación en curso, scrcpy queda huérfano y sigue
  grabando. Ya pasaba antes y no es de esta HU. Una *job object* de Windows lo resolvería; queda
  como sugerencia para otra HU.

## Resolución de decisiones (2026-10-02)

- **DP6 → A** (usuario): con "mic de la PC", si falta ffmpeg o micrófono o no abre, la grabación no arranca y se muestra el motivo.
- **DP14 → no** (usuario): no se abre por ahora la HU de avisos de licencia del resto de binarios del portátil (solo scrcpy lleva su aviso en esta HU).
- DP9 informativa: "Actualizar Factum" cierra también el adb de Tatana (los celulares desaparecen un momento).
- Resto: las recomendadas.
