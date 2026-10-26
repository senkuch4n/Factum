using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Agent.Models;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Factum.Agent.Services;
using Factum.Agent.Services.Ios;
using Factum.Agent.Services.Session;
using Factum.Agent.WebSockets;
using Factum.Agent.Common;

// Modo auxiliar `--ctrl-c <pid>`: manda Ctrl+C a la consola de scrcpy y sale. Va antes de todo
// para no levantar el host ni registrar handlers de consola (SDD grabacion-android-windows §6.3).
if (args.Length == 2 && args[0] == WindowsConsoleSignal.HelperFlag)
{
    Environment.Exit(WindowsConsoleSignal.RunHelper(args[1]));
}

// Modo `--autoprueba-ios <carpeta>`: prueba de humo del Python/helper/ffmpeg de iPhone sin iPhone
// (SDD ios-herramientas-windows §6.9). No levanta el host ni toca DataDirectory.
if (args.Length == 2 && args[0] == IosSelfTest.Flag)
{
    Environment.Exit(await IosSelfTest.RunAsync(args[1]));
}

// Modo `--autoprueba-linux <carpeta>`: prueba de humo de lo propio de Linux (herramientas portátiles, ffmpeg con
// pulse/v4l2, SIGTERM, estado de la PC y glibc) sin celular (SDD tatana-linux §5.7). No levanta el host.
if (args.Length == 2 && args[0] == Factum.Agent.Services.Linux.LinuxSelfTest.Flag)
{
    Environment.Exit(await Factum.Agent.Services.Linux.LinuxSelfTest.RunAsync(args[1]));
}

// DP12: que scrcpy no herede un "ignorar Ctrl+C" que haya dejado algún lanzador (solo Windows).
WindowsConsoleSignal.EnableCtrlCForChildren();

var builder = WebApplication.CreateBuilder(args);

// ── CLI args override: --mock, --port, --data, --bind, --mode, --local-config ─
var mock = args.Contains("--mock");
var portArg = args.SkipWhile(a => a != "--port").Skip(1).FirstOrDefault();
var dataArg = args.SkipWhile(a => a != "--data").Skip(1).FirstOrDefault();
var bindArg = args.SkipWhile(a => a != "--bind").Skip(1).FirstOrDefault();
// tatana-instalador-autoupdate D-T15/D-T16: el instalado (Electron) pasa --mode installed y
// --local-config %APPDATA%\Tatana\appsettings.Local.json.
var modeArg = args.SkipWhile(a => a != "--mode").Skip(1).FirstOrDefault();
var localConfigArg = args.SkipWhile(a => a != "--local-config").Skip(1).FirstOrDefault();
string? installMode = null;
if (args.Contains("--mode"))
{
    installMode = modeArg?.Trim().ToLowerInvariant();
    if (installMode is null || !AgentOptions.InstallModes.Contains(installMode))
        throw new InvalidOperationException(
            $"--mode inválido: '{modeArg}'. Usá {string.Join(" o ", AgentOptions.InstallModes)}.");
}

// ── Config local fuera del repo (appsettings.Local.json) ──────────────────────
// Mismo bloque que Factum.Backend/Program.cs: se inserta justo DESPUÉS del último
// appsettings*.json, así pisa a la config versionada pero las variables de entorno y la
// línea de comandos siguen ganando. Sirve para un "Agent": { "Mock": true } local de
// desarrollo sin tocar appsettings.json, y en la PC del perito para fijar orígenes/carpetas
// (D9). El .csproj lo excluye de bin/ y publish/.
// Con --local-config <ruta> se usa ESE archivo en lugar del de al lado del exe (D-T16). En los dos
// casos se valida antes: un JSON inválido se ignora (queda en /agent/state.local_config.error) en
// vez de impedir que Tatana arranque.
var localConfigStatus = LocalConfigInspector.Inspect(
    string.IsNullOrWhiteSpace(localConfigArg)
        ? Path.Combine(builder.Environment.ContentRootPath, "appsettings.Local.json")
        : localConfigArg.Trim());
if (localConfigStatus.Loaded)
{
    var sources = builder.Configuration.Sources;
    var lastAppSettings = -1;
    for (var i = 0; i < sources.Count; i++)
    {
        if (sources[i] is JsonConfigurationSource { Path: { } path } &&
            path.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
            lastAppSettings = i;
    }
    var localSource = new JsonConfigurationSource
    {
        Path = Path.GetFileName(localConfigStatus.Path),
        Optional = true,
        ReloadOnChange = false,
        FileProvider = new PhysicalFileProvider(Path.GetDirectoryName(localConfigStatus.Path)!),
    };
    sources.Insert(lastAppSettings >= 0 ? lastAppSettings + 1 : sources.Count, localSource);
}
builder.Services.AddSingleton(localConfigStatus);

builder.Services.Configure<AgentOptions>(opts =>
{
    // Bind primero (appsettings.json), después los overrides de CLI —
    // al revés pisaba --mock/--port/--data con los valores de config.
    builder.Configuration.GetSection("Agent").Bind(opts);
    if (mock) opts.Mock = true;
    if (portArg is not null && int.TryParse(portArg, out var p)) opts.Port = p;
    if (dataArg is not null) opts.DataDirectory = dataArg;
    if (!string.IsNullOrWhiteSpace(bindArg)) opts.BindAddress = bindArg.Trim();
    // Solo por CLI: un "InstallMode" en la config no cuenta.
    opts.InstallMode = installMode;
    // extraccion-logica-android §13.1: saneo de las opciones de extracción (vuelve al default lo inválido).
    opts.Extraction.Normalize(null);
});

// ── JSON ──────────────────────────────────────────────────────────────────────
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// ── Orígenes permitidos (zip-local-informe-servidor §6.1) ─────────────────────
// Reemplaza al CORS "*": solo el front configurado puede llamar a Tatana desde un navegador, y
// cualquier request con otro Origin (incluido el WebSocket) se rechaza con 403 del lado del
// servidor. Se valida al arrancar, como Agent:BindAddress.
var (allowedOrigins, originErrors) = OriginPolicy.Parse(
    builder.Configuration.GetSection("Agent:AllowedOrigins").Get<string[]>());
if (originErrors.Count > 0)
    throw new InvalidOperationException(
        "Configuración inválida, Tatana no arranca:" + string.Concat(originErrors.Select(e => "\n  - " + e)));
{
    var limits = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new AgentOptions();
    if (limits.MaxUploadBytes <= 0)
        throw new InvalidOperationException("Agent:MaxUploadBytes tiene que ser mayor que 0");
    if (limits.MinFreeBytes < 0)
        throw new InvalidOperationException("Agent:MinFreeBytes tiene que ser mayor o igual que 0");
}

// ── Servicios ─────────────────────────────────────────────────────────────────
// AdbService e IosService: UNA instancia cada uno, expuesta por su interfaz y como
// IOperationSource (tatana-instalador-autoupdate §5.3, B10).
builder.Services.AddSingleton<AdbService>();
builder.Services.AddSingleton<IAdbService>(sp => sp.GetRequiredService<AdbService>());
builder.Services.AddSingleton<IOperationSource>(sp => sp.GetRequiredService<AdbService>());
builder.Services.AddSingleton<AppleServiceProbe>();
builder.Services.AddSingleton<Factum.Agent.Services.Linux.LinuxHostProbe>(); // tatana-linux §5.2
builder.Services.AddSingleton<IosService>();
builder.Services.AddSingleton<IIosService>(sp => sp.GetRequiredService<IosService>());
builder.Services.AddSingleton<IOperationSource>(sp => sp.GetRequiredService<IosService>());
// grabacion-android-robusta §5.12: grabaciones en curso y detenidas sin importar en /agent/state.
builder.Services.AddSingleton<IRecordingStateSource>(sp => sp.GetRequiredService<AdbService>());
builder.Services.AddSingleton<IRecordingStateSource>(sp => sp.GetRequiredService<IosService>());
builder.Services.AddSingleton<RecordingRegistry>();
builder.Services.AddSingleton<IAndroidScreenProbe, AndroidScreenProbe>(); // pulido-post-qa-final D9
// metadatos-dispositivo §7.2: ficha técnica y reloj del dispositivo (solo lectura, bajo demanda).
builder.Services.AddSingleton<Factum.Agent.Services.DeviceDetails.IDeviceDetailsService,
    Factum.Agent.Services.DeviceDetails.DeviceDetailsService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<OperationTracker>();
builder.Services.AddSingleton<IWebcamService, WebcamService>();
builder.Services.AddSingleton<IFileStorageService, FileStorageService>();
builder.Services.AddSingleton<AgentWebSocketHub>();
builder.Services.AddSingleton<IAgentEventSink>(sp => sp.GetRequiredService<AgentWebSocketHub>());
builder.Services.AddSingleton<ToolInventory>();
// zip-local-informe-servidor: carpeta de trabajo del caso y ZIP en la PC del perito.
builder.Services.AddSingleton<IAgentDiskProbe, DriveInfoAgentDiskProbe>();
builder.Services.AddSingleton<ICaseEvidenceStore, CaseEvidenceStore>();
builder.Services.AddSingleton<ICaseZipService, CaseZipService>();
builder.Services.AddSingleton<IFolderReveal, FolderReveal>();
// ios-grabacion-larga §5.3: variantes de previsualización de grabaciones iOS, en segundo plano.
builder.Services.AddSingleton<VideoPostprocessService>();
builder.Services.AddSingleton<IVideoPostprocessService>(sp => sp.GetRequiredService<VideoPostprocessService>());
// grabacion-sesion-completa §5.1: grabación de la sesión (diario por caso, tramos, recuperación). La sesión activa es
// una operación en curso (bloquea el auto-update).
builder.Services.AddSingleton<SessionJournal>();
builder.Services.AddSingleton<SegmentCloser>();
builder.Services.AddSingleton<StayAwakeKeeper>();
builder.Services.AddSingleton<ISessionTrackFactory, SessionTrackFactory>();
builder.Services.AddSingleton<SessionRecoveryStatus>();
// grabacion-sesion-completa-f2 §5.1: cámara del procedimiento (lista, pistas de ffmpeg y vista previa).
builder.Services.AddSingleton<CameraDevices>();
builder.Services.AddSingleton<ICameraTrackFactory, CameraTrackFactory>();
builder.Services.AddSingleton<CameraPreviewService>();
builder.Services.AddSingleton<ICameraPreview>(sp => sp.GetRequiredService<CameraPreviewService>());
builder.Services.AddSingleton<SessionRecorder>();
builder.Services.AddSingleton<IOperationSource>(sp => sp.GetRequiredService<SessionRecorder>());
builder.Services.AddSingleton<SessionRecovery>();

// extraccion-logica-android §13.1: extracción lógica de Android (singleton + IOperationSource).
builder.Services.AddSingleton<Factum.Agent.Services.Extraction.DestinationService>();
builder.Services.AddSingleton<Factum.Agent.Services.Extraction.ExtractionService>();
builder.Services.AddSingleton<IOperationSource>(sp =>
    sp.GetRequiredService<Factum.Agent.Services.Extraction.ExtractionService>());
builder.Services.AddSingleton<Factum.Agent.Services.Extraction.ExtractionRecovery>();

// ── Puerto y dirección de escucha (CLI > config > default) ───────────────────
var port = builder.Configuration.GetValue<int>("Agent:Port", 8765);
if (portArg is not null && int.TryParse(portArg, out var cliPort)) port = cliPort;
var bindAddress = builder.Configuration.GetValue<string>("Agent:BindAddress");
if (!string.IsNullOrWhiteSpace(bindArg)) bindAddress = bindArg;
bindAddress = string.IsNullOrWhiteSpace(bindAddress) ? "localhost" : bindAddress.Trim();

// "localhost" → Kestrel escucha en 127.0.0.1 y ::1 (fetch("http://localhost:8765") anda aunque
// el navegador resuelva a IPv6). Una IP → solo esa. Cualquier otra cosa → no arranca.
string listenUrl;
var exposedToNetwork = false;
if (string.Equals(bindAddress, "localhost", StringComparison.OrdinalIgnoreCase))
{
    listenUrl = $"http://localhost:{port}";
}
else if (IPAddress.TryParse(bindAddress, out var bindIp))
{
    listenUrl = bindIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
        ? $"http://[{bindIp}]:{port}"
        : $"http://{bindIp}:{port}";
    exposedToNetwork = bindIp.Equals(IPAddress.Any) || bindIp.Equals(IPAddress.IPv6Any);
}
else
{
    throw new InvalidOperationException(
        $"Agent:BindAddress inválido: '{bindAddress}'. Usá localhost o una IP.");
}
builder.WebHost.UseUrls(listenUrl);

// ── Filtro de Host (tatana-ws-y-hosts §4.3, D2/D3) ────────────────────────────
// Solo se atienden requests DIRIGIDAS a esta PC: cierra el DNS rebinding (un dominio que resuelve a
// 127.0.0.1 llega con su propio Host y recibe 400). La lista la arma HostPolicy y pisa el "AllowedHosts"
// de ASP.NET venga de donde venga (D-A6). Inválida en appsettings/env/CLI → no arranca.
var (allowedHosts, hostErrors) = HostPolicy.Build(bindAddress,
    builder.Configuration.GetSection(HostPolicy.Key).Get<string[]>(), HostPolicy.MachineAddresses);
if (hostErrors.Count > 0)
    throw new InvalidOperationException(
        "Configuración inválida, Tatana no arranca:" + string.Concat(hostErrors.Select(e => "\n  - " + e)));
builder.Services.PostConfigure<HostFilteringOptions>(o =>
{
    o.AllowedHosts = allowedHosts.ToList();
    o.AllowEmptyHosts = true; // default de ASP.NET; HTTP/1.1 sin Host ya lo rechaza Kestrel
});

var app = builder.Build();

// Primer middleware: un Host ajeno recibe 400 sin datos ni CORS en TODAS las rutas (/ws, /health, OPTIONS…).
app.UseHostFiltering();

// Guarda de origen + CORS (§6.1). Los headers se escriben ANTES de next() y el resto va en
// try/catch: si una excepción escapa el pipeline (ej: fallo de DI) no llega a Kestrel, que haría
// Reset() del response y borraría los headers ya escritos.
app.Use(async (ctx, next) =>
{
    var hasOrigin = ctx.Request.Headers.TryGetValue("Origin", out var originValues);
    if (hasOrigin)
    {
        var origin = originValues.ToString();
        if (!OriginPolicy.IsAllowed(origin, allowedOrigins))
        {
            // Sin headers CORS: el navegador tampoco puede leer la respuesta. Vale para OPTIONS y /ws.
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { error = "Origen no permitido", code = AgentErrorCodes.OriginNotAllowed });
            return;
        }
        ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
        ctx.Response.Headers.Append("Vary", "Origin");
        ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
        ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        ctx.Response.Headers["Access-Control-Max-Age"] = "600";
        // Private Network Access (esquema anterior a Local Network Access, §9.3).
        if (ctx.Request.Method == "OPTIONS" &&
            string.Equals(ctx.Request.Headers["Access-Control-Request-Private-Network"], "true",
                StringComparison.OrdinalIgnoreCase))
            ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
    }

    if (ctx.Request.Method == "OPTIONS")
    {
        ctx.Response.StatusCode = 204;
        return;
    }

    try
    {
        await next(ctx);
    }
    catch (Exception ex) when (!ctx.Response.HasStarted)
    {
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

// Modo mantenimiento (tatana-instalador-autoupdate §5.3, D-T10): DESPUÉS de la guarda de origen y
// del OPTIONS. Cada request mutante (POST/PUT/PATCH/DELETE, salvo /agent/maintenance) se cuenta como
// operación en curso mientras dura; en mantenimiento se rechaza con 503 agent_updating. Los GET,
// /ws y /health nunca se bloquean.
var operations = app.Services.GetRequiredService<OperationTracker>();
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "/";
    if (!OperationTracker.IsTracked(ctx.Request.Method, path))
    {
        await next(ctx);
        return;
    }
    if (!operations.TryEnterRequest(ctx.Request.Method, path, out var token))
    {
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ctx.Response.Headers.RetryAfter = "10";
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = "Tatana se está actualizando. Esperá unos segundos y reintentá.",
            code = AgentErrorCodes.AgentUpdating,
        });
        return;
    }
    try
    {
        await next(ctx);
    }
    finally
    {
        token?.Dispose();
    }
});

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(25) });
app.MapControllers();

// ── Polling de dispositivos → broadcast cada 3 segundos ───────────────────────
var hub = app.Services.GetRequiredService<AgentWebSocketHub>();
var adb = app.Services.GetRequiredService<IAdbService>();
var ios = app.Services.GetRequiredService<IIosService>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
// Valores efectivos (config + appsettings.Local.json + CLI), no solo el flag --mock.
var agentOptions = app.Services.GetRequiredService<IOptions<AgentOptions>>().Value;

// D-T16: config local ignorada por inválida (Tatana arranca igual; la ventana lo avisa).
if (localConfigStatus.Error is not null)
    logger.LogError("La configuración local {Path} tiene un error y se ignoró: {Error}",
        localConfigStatus.Path, localConfigStatus.Error);
else if (localConfigStatus.Loaded)
    logger.LogInformation("Configuración local: {Path} (fija orígenes: {Overrides})",
        localConfigStatus.Path, localConfigStatus.OverridesAllowedOrigins);

// §4.2: carpetas efectivas y orígenes; aviso si el ZIP caería en OneDrive/iCloud (DP4).
{
    var zipDir = app.Services.GetRequiredService<ICaseZipService>().EvidenceDirectory;
    var store = app.Services.GetRequiredService<ICaseEvidenceStore>();
    var orphanUploads = store.CleanupOrphanUploads();
    // tatana-ws-y-hosts T-05: .part huérfanos del ZIP (solo evidencia_*.zip.part en .factum/pendiente/).
    var orphanParts = app.Services.GetRequiredService<ICaseZipService>().CleanupOrphanParts();
    // ios-grabacion-larga §5.6: solo <DataDirectory>/.postprocess/ (ahí escribe únicamente Tatana).
    var orphanPostprocess = 0;
    try { orphanPostprocess = VideoPostprocessService.CleanupWorkDirectory(store.DataDirectory); }
    catch (Exception ex) { logger.LogWarning(ex, "No se pudo borrar la carpeta de post-proceso huérfana"); }
    // wizard-concurrencia-pestanas §4.2: las vistas previas del paso 2 no sobreviven a un reinicio (solo previews/).
    var clearedPreviews = 0;
    try { clearedPreviews = app.Services.GetRequiredService<IFileStorageService>().ClearPreviews(); }
    catch (Exception ex) { logger.LogWarning(ex, "No se pudo vaciar la carpeta de vistas previas"); }
    logger.LogInformation(
        "Evidencia: carpeta de trabajo {DataDirectory}/cases, ZIP en {EvidenceDirectory}; orígenes permitidos {Lista} (temporales huérfanos borrados: {N}; trabajos de post-proceso huérfanos borrados: {P}; ZIP parciales huérfanos borrados: {Z}; vistas previas borradas: {V})",
        store.DataDirectory, zipDir, string.Join(", ", allowedOrigins), orphanUploads, orphanPostprocess, orphanParts,
        clearedPreviews);
    // grabacion-sesion-completa §5.10: tramos que quedaron abiertos si Tatana se cerró grabando la sesión. En segundo
    // plano (puede remuxear y hashear GB); mientras tanto /agent/state.session_recovery = "running".
    _ = app.Services.GetRequiredService<SessionRecovery>().StartInBackground();
    if (AgentFileNames.IsSyncedFolder(zipDir))
        logger.LogWarning(
            "La carpeta de evidencia {Ruta} está dentro de una carpeta sincronizada con la nube; el ZIP se subiría a ese servicio. Configurá Agent:EvidenceDirectory fuera de OneDrive/iCloud.",
            zipDir);
}

logger.LogInformation("Hosts permitidos: {Lista}", string.Join(", ", allowedHosts));

if (exposedToNetwork)
    logger.LogWarning(
        "Agente expuesto a la red en {Bind}: cualquiera en la red local puede listar dispositivos y disparar capturas.",
        listenUrl);

await adb.EnsureServerAsync();

// Apagado (tatana-ws-y-hosts §4.2), UN solo callback para que el orden no dependa del LIFO de Register:
// 0. grabacion-sesion-completa §5.11: primero se corta y guarda la grabación de la sesión (15 s como máximo; un
//    TerminateProcess de Windows no pasa por acá y lo cubre la recuperación).
// 1. ios-grabacion-larga §5.6: se cancela el post-proceso (agent_stopping) y se le dan 5 s para matar ffmpeg
//    y limpiar la carpeta de trabajo (sus eventos terminales quedan encolados en el hub).
// 2. close 1001 "going away" a cada WebSocket, DETRÁS de esos eventos, con 1 s de espera.
{
    var post = app.Services.GetRequiredService<IVideoPostprocessService>();
    var sessionRecorder = app.Services.GetRequiredService<SessionRecorder>();
    var cameraPreview = app.Services.GetRequiredService<CameraPreviewService>();
    app.Lifetime.ApplicationStopping.Register(() =>
    {
        try { sessionRecorder.ShutdownAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult(); }
        catch (Exception ex) { logger.LogWarning(ex, "Error al cerrar la grabación de la sesión al apagar"); }
        // extraccion-logica-android §5.9: cierra la extracción en curso con un presupuesto de 15 s.
        try { app.Services.GetRequiredService<Factum.Agent.Services.Extraction.ExtractionService>()
            .ShutdownAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult(); }
        catch (Exception ex) { logger.LogWarning(ex, "Error al cerrar la extracción al apagar"); }
        // grabacion-sesion-completa-f2 §5.9: la vista previa de la cámara no sobrevive al apagado.
        try { cameraPreview.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
        catch (Exception ex) { logger.LogWarning(ex, "Error al cortar la vista previa de la cámara al apagar"); }
        try
        {
            post.CancelAll("agent_stopping");
            post.WhenIdleAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex) { logger.LogWarning(ex, "Error al cancelar el post-proceso al apagar"); }
        try { hub.ShutdownAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(); }
        catch (Exception ex) { logger.LogWarning(ex, "Error al cerrar los WebSocket al apagar"); }
    });
}

// Versiones de adb/scrcpy/ffmpeg/python para /health.tools, en segundo plano.
app.Services.GetRequiredService<ToolInventory>().WarmUp();

// extraccion-logica-android §5.3: recupera las extracciones que quedaron en curso de una corrida anterior.
var extractions = app.Services.GetRequiredService<Factum.Agent.Services.Extraction.ExtractionService>();
_ = Task.Run(async () =>
{
    try { await app.Services.GetRequiredService<Factum.Agent.Services.Extraction.ExtractionRecovery>().RunAsync(); }
    catch (Exception ex) { logger.LogWarning(ex, "Error recuperando extracciones al arrancar"); }
});

var stopping = app.Lifetime.ApplicationStopping;
var sessions = app.Services.GetRequiredService<SessionRecorder>();
_ = Task.Run(async () =>
{
    List<string> lastSerials = [];
    while (!stopping.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(3000, stopping);
            var devices = (await adb.ListDevicesAsync(stopping))
                .Concat(await ios.ListDevicesAsync(stopping))
                .ToList();

            var serials = devices.Select(d => d.Serial).OrderBy(s => s).ToList();
            // grabacion-sesion-completa §5.7: reconexión del mismo celular y restauraciones pendientes.
            try { sessions.OnDevicesPolled(serials); }
            catch (Exception ex) { logger.LogWarning(ex, "Error avisando dispositivos a la grabación de la sesión"); }
            // extraccion-logica-android §5.5: desconexión a mitad y connector pendiente de desinstalar.
            try { extractions.OnDevicesPolled(serials); }
            catch (Exception ex) { logger.LogWarning(ex, "Error avisando dispositivos a la extracción"); }
            if (!serials.SequenceEqual(lastSerials))
            {
                lastSerials = serials;
                await hub.BroadcastAsync(new Factum.Agent.Models.AgentEvent
                {
                    Type = "devices_changed",
                    Data = new { devices }
                });
                logger.LogInformation("Dispositivos: [{Serials}]", string.Join(", ", serials));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
        catch (Exception ex) when (!stopping.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Error en polling de dispositivos");
        }
        catch { break; } // apagando: sin warnings
    }
});

logger.LogInformation("Factum Agent {Version} en {Url} (mock={Mock}, modo={Mode})",
    AgentVersion.Current, listenUrl, agentOptions.Mock, Factum.Agent.Controllers.HealthController.ResolveMode(agentOptions));
app.Run();
