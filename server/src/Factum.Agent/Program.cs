using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Agent.Models;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Factum.Agent.Services;
using Factum.Agent.Services.Ios;
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
builder.Services.AddSingleton<IosService>();
builder.Services.AddSingleton<IIosService>(sp => sp.GetRequiredService<IosService>());
builder.Services.AddSingleton<IOperationSource>(sp => sp.GetRequiredService<IosService>());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<OperationTracker>();
builder.Services.AddSingleton<IWebcamService, WebcamService>();
builder.Services.AddSingleton<IFileStorageService, FileStorageService>();
builder.Services.AddSingleton<AgentWebSocketHub>();
builder.Services.AddSingleton<ToolInventory>();
// zip-local-informe-servidor: carpeta de trabajo del caso y ZIP en la PC del perito.
builder.Services.AddSingleton<IAgentDiskProbe, DriveInfoAgentDiskProbe>();
builder.Services.AddSingleton<ICaseEvidenceStore, CaseEvidenceStore>();
builder.Services.AddSingleton<ICaseZipService, CaseZipService>();
builder.Services.AddSingleton<IFolderReveal, FolderReveal>();

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

var app = builder.Build();

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
    logger.LogInformation(
        "Evidencia: carpeta de trabajo {DataDirectory}/cases, ZIP en {EvidenceDirectory}; orígenes permitidos {Lista} (temporales huérfanos borrados: {N})",
        store.DataDirectory, zipDir, string.Join(", ", allowedOrigins), orphanUploads);
    if (AgentFileNames.IsSyncedFolder(zipDir))
        logger.LogWarning(
            "La carpeta de evidencia {Ruta} está dentro de una carpeta sincronizada con la nube; el ZIP se subiría a ese servicio. Configurá Agent:EvidenceDirectory fuera de OneDrive/iCloud.",
            zipDir);
}

if (exposedToNetwork)
    logger.LogWarning(
        "Agente expuesto a la red en {Bind}: cualquiera en la red local puede listar dispositivos y disparar capturas.",
        listenUrl);

await adb.EnsureServerAsync();

// Versiones de adb/scrcpy/ffmpeg/python para /health.tools, en segundo plano.
app.Services.GetRequiredService<ToolInventory>().WarmUp();

_ = Task.Run(async () =>
{
    List<string> lastSerials = [];
    while (true)
    {
        try
        {
            await Task.Delay(3000);
            var devices = (await adb.ListDevicesAsync())
                .Concat(await ios.ListDevicesAsync())
                .ToList();

            var serials = devices.Select(d => d.Serial).OrderBy(s => s).ToList();
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
        catch (Exception ex) { logger.LogWarning(ex, "Error en polling de dispositivos"); }
    }
});

logger.LogInformation("Factum Agent {Version} en {Url} (mock={Mock}, modo={Mode})",
    AgentVersion.Current, listenUrl, agentOptions.Mock, Factum.Agent.Controllers.HealthController.ResolveMode(agentOptions));
app.Run();
