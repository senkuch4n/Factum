using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Agent.Models;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;
using Factum.Agent.Services;
using Factum.Agent.WebSockets;

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

// ── CORS: permite cualquier origen (el agente es local, no tiene auth) ─────────
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ── Servicios ─────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IAdbService, AdbService>();
builder.Services.AddSingleton<IIosService, IosService>();
builder.Services.AddSingleton<IWebcamService, WebcamService>();
builder.Services.AddSingleton<IFileStorageService, FileStorageService>();
builder.Services.AddSingleton<AgentWebSocketHub>();

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

// CORS: headers escritos ANTES de next() y envueltos en try/catch para que si
// una excepción escapa el pipeline (ej: fallo de DI), no llegue a Kestrel
// (Kestrel haría Reset() del response borrando los headers ya escritos).
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
    ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS, PATCH";
    ctx.Response.Headers["Access-Control-Allow-Headers"] = "*";

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

app.UseCors();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(25) });
app.MapControllers();

// ── Polling de dispositivos → broadcast cada 3 segundos ───────────────────────
var hub = app.Services.GetRequiredService<AgentWebSocketHub>();
var adb = app.Services.GetRequiredService<IAdbService>();
var ios = app.Services.GetRequiredService<IIosService>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
// Valores efectivos (config + appsettings.Local.json + CLI), no solo el flag --mock.
var agentOptions = app.Services.GetRequiredService<IOptions<AgentOptions>>().Value;

if (exposedToNetwork)
    logger.LogWarning(
        "Agente expuesto a la red en {Bind}: cualquiera en la red local puede listar dispositivos y disparar capturas.",
        listenUrl);

await adb.EnsureServerAsync();

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
