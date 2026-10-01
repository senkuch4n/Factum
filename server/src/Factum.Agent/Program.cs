using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.WebSockets;

var builder = WebApplication.CreateBuilder(args);

// ── CLI args override: --mock, --port, --data ─────────────────────────────────
var mock = args.Contains("--mock");
var portArg = args.SkipWhile(a => a != "--port").Skip(1).FirstOrDefault();
var dataArg = args.SkipWhile(a => a != "--data").Skip(1).FirstOrDefault();

builder.Services.Configure<AgentOptions>(opts =>
{
    // Bind primero (appsettings.json), después los overrides de CLI —
    // al revés pisaba --mock/--port/--data con los valores de config.
    builder.Configuration.GetSection("Agent").Bind(opts);
    if (mock) opts.Mock = true;
    if (portArg is not null && int.TryParse(portArg, out var p)) opts.Port = p;
    if (dataArg is not null) opts.DataDirectory = dataArg;
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

// ── Puerto desde config ────────────────────────────────────────────────────────
var port = builder.Configuration.GetValue<int>("Agent:Port", 8765);
if (portArg is not null && int.TryParse(portArg, out var cliPort)) port = cliPort;
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

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

logger.LogInformation("Factum Agent en http://0.0.0.0:{Port} (mock={Mock})", port, mock);
app.Run();
