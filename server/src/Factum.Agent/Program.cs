using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Agent.Models;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;
using Factum.Agent.Services;
using Factum.Agent.WebSockets;
using Factum.Agent.Common;

// Modo auxiliar `--ctrl-c <pid>`: manda Ctrl+C a la consola de scrcpy y sale. Va antes de todo
// para no levantar el host ni registrar handlers de consola (SDD grabacion-android-windows §6.3).
if (args.Length == 2 && args[0] == WindowsConsoleSignal.HelperFlag)
{
    Environment.Exit(WindowsConsoleSignal.RunHelper(args[1]));
}

// DP12: que scrcpy no herede un "ignorar Ctrl+C" que haya dejado algún lanzador (solo Windows).
WindowsConsoleSignal.EnableCtrlCForChildren();

var builder = WebApplication.CreateBuilder(args);

// ── Config local fuera del repo (appsettings.Local.json, ignorado por git) ────
// Mismo bloque que Factum.Backend/Program.cs: se inserta justo DESPUÉS del último
// appsettings*.json, así pisa a la config versionada pero las variables de entorno y la
// línea de comandos siguen ganando. Sirve para un "Agent": { "Mock": true } local de
// desarrollo sin tocar appsettings.json. El .csproj lo excluye de bin/ y publish/.
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
        Path = "appsettings.Local.json",
        Optional = true,
        ReloadOnChange = false,
        FileProvider = builder.Environment.ContentRootFileProvider,
    };
    sources.Insert(lastAppSettings >= 0 ? lastAppSettings + 1 : sources.Count, localSource);
}

// ── CLI args override: --mock, --port, --data, --bind ─────────────────────────
var mock = args.Contains("--mock");
var portArg = args.SkipWhile(a => a != "--port").Skip(1).FirstOrDefault();
var dataArg = args.SkipWhile(a => a != "--data").Skip(1).FirstOrDefault();
var bindArg = args.SkipWhile(a => a != "--bind").Skip(1).FirstOrDefault();

builder.Services.Configure<AgentOptions>(opts =>
{
    // Bind primero (appsettings.json), después los overrides de CLI —
    // al revés pisaba --mock/--port/--data con los valores de config.
    builder.Configuration.GetSection("Agent").Bind(opts);
    if (mock) opts.Mock = true;
    if (portArg is not null && int.TryParse(portArg, out var p)) opts.Port = p;
    if (dataArg is not null) opts.DataDirectory = dataArg;
    if (!string.IsNullOrWhiteSpace(bindArg)) opts.BindAddress = bindArg.Trim();
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
builder.Services.AddSingleton<IAdbService, AdbService>();
builder.Services.AddSingleton<IIosService, IosService>();
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

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(25) });
app.MapControllers();

// ── Polling de dispositivos → broadcast cada 3 segundos ───────────────────────
var hub = app.Services.GetRequiredService<AgentWebSocketHub>();
var adb = app.Services.GetRequiredService<IAdbService>();
var ios = app.Services.GetRequiredService<IIosService>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
// Valores efectivos (config + appsettings.Local.json + CLI), no solo el flag --mock.
var agentOptions = app.Services.GetRequiredService<IOptions<AgentOptions>>().Value;

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

logger.LogInformation("Factum Agent en {Url} (mock={Mock})", listenUrl, agentOptions.Mock);
app.Run();
