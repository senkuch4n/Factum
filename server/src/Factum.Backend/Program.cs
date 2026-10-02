using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Backend.Infrastructure;
using Microsoft.Extensions.Options;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Profile;
using Factum.Backend.Services.Reports;
using Factum.Backend.Services.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── Config local fuera del repo (appsettings.Local.json, ignorado por git) ────
// Se inserta justo DESPUÉS del último appsettings*.json (appsettings.{Environment}.json),
// así pisa a la config versionada pero las variables de entorno y la línea de comandos
// (que vienen después en la lista de fuentes) siguen ganando. Ahí vive la identidad real
// del cliente (Branding:*); ver README, "Identidad de la organización (Branding)".
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

var port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "8080");
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));

// ── Auth y soporte: config resuelta y validada antes de registrar servicios ──
// Si la config es inválida, el backend no arranca (fail-fast): la excepción sale por
// stderr / docker logs con la lista de errores.
var authSettings = AuthSettingsResolver.Resolve(builder.Configuration, builder.Environment.IsDevelopment());
var supportSettings = SupportSettingsResolver.Resolve(builder.Configuration);
var configErrors = authSettings.Errors.Concat(supportSettings.Errors).ToList();
if (configErrors.Count > 0)
    throw new InvalidOperationException(
        "Configuración inválida, el backend no arranca:" + string.Concat(configErrors.Select(e => "\n  - " + e)));
builder.Services.AddSingleton(authSettings);
builder.Services.AddSingleton(supportSettings);

// ── Configuración tipada ──────────────────────────────────────────────────────
builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection("MongoDb"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<Factum.Backend.Controllers.AuditOptions>(builder.Configuration.GetSection("Audit"));
builder.Services.Configure<Factum.Backend.Services.Updates.TatanaUpdatesOptions>(builder.Configuration.GetSection("TatanaUpdates"));
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection("Branding"));
builder.Services.Configure<ReportOptions>(builder.Configuration.GetSection("Report"));

// ── Serialización ─────────────────────────────────────────────────────────────
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

// ── Auth (custom JWT scheme — valida el header y pone User en HttpContext.Items) ─
builder.Services.AddAuthentication("FactumBearerScheme")
    .AddScheme<AuthenticationSchemeOptions, FactumBearerHandler>(
        "FactumBearerScheme", _ => { });
builder.Services.AddAuthorization();

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ── IP real del fiscal cuando el backend corre detrás de un reverse proxy ────
// (nginx, ingress, etc.) — sin esto, la auditoría del agente vería la IP del
// proxy en vez de la PC de origen. KnownNetworks/KnownProxies vacíos porque el
// proxy real (si existe) se decide en el despliegue, no es fijo acá.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── Infraestructura ───────────────────────────────────────────────────────────
builder.Services.AddSingleton<ICaseRepository, CaseRepository>();
builder.Services.AddSingleton<IAgentEventRepository, AgentEventRepository>();
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddSingleton<IBrandingService, BrandingService>();
builder.Services.AddSingleton<IReportSettings, ReportSettings>();
builder.Services.AddSingleton<IExpertProfileRepository, ExpertProfileRepository>();
builder.Services.AddSingleton<ICatalogRepository, CatalogRepository>();

// ── Auth provider (dev o external según AuthSettingsResolver) ─────────────────
if (authSettings.Mode == AuthModes.External)
{
    builder.Services.AddHttpClient<IAuthProvider, ExternalHttpAuthProvider>(c =>
        c.Timeout = TimeSpan.FromSeconds(authSettings.External!.TimeoutSeconds));
}
else
{
    builder.Services.AddSingleton<IAuthProvider, DevAuthProvider>();
}
builder.Services.AddSingleton<IAuthService, AuthService>();

// ── Servicios de negocio ──────────────────────────────────────────────────────
builder.Services.AddSingleton<IReportService, ReportService>();
builder.Services.AddScoped<IExpertProfileService, ExpertProfileService>();
builder.Services.AddScoped<Factum.Backend.Services.Catalogs.ICatalogService, Factum.Backend.Services.Catalogs.CatalogService>();
builder.Services.AddScoped<ICaseService, CaseService>();
// Soporte (Faro) opcional: apagado no se instancia ningún HttpClient hacia Faro.
if (supportSettings.Enabled)
{
    builder.Services.AddHttpClient<ISupportService, SupportService>(c =>
    {
        c.BaseAddress = new Uri(supportSettings.Options.BaseUrl);
        c.Timeout = TimeSpan.FromSeconds(supportSettings.Options.TimeoutSeconds);
    });
}
else
{
    builder.Services.AddSingleton<ISupportService, DisabledSupportService>();
}
builder.Services.AddHttpClient<Factum.Backend.Services.Updates.ITatanaUpdatesService,
    Factum.Backend.Services.Updates.TatanaUpdatesService>();

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v2", new OpenApiInfo { Title = "Factum API", Version = "v2" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        }] = []
    });
});

var app = builder.Build();

app.Logger.LogInformation("Auth: modo {Mode}", authSettings.Mode);
if (authSettings.ExternalLoginUri is { } loginUri)
    app.Logger.LogInformation("Auth: login externo en {Url}",
        loginUri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped));
if (supportSettings.Enabled)
    app.Logger.LogInformation("Soporte: habilitado ({Host})", new Uri(supportSettings.Options.BaseUrl).Authority);
else
    app.Logger.LogInformation("Soporte: deshabilitado");
foreach (var warning in authSettings.Warnings.Concat(supportSettings.Warnings))
    app.Logger.LogWarning("{Warning}", warning);

// Branding se carga una sola vez; resolverlo acá hace que sus warnings (logo inválido,
// textos truncados) salgan en el log de arranque y no en el primer request.
app.Services.GetRequiredService<IBrandingService>();
// Ídem la config del informe (zona horaria, domicilio, textos por defecto).
app.Services.GetRequiredService<IReportSettings>();

app.UseForwardedHeaders();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v2/swagger.json", "Factum API v2"));
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    version = "2.0.0",
    auth_mode = authSettings.Mode
}));

app.MapControllers();
app.Run();

// ── Handler de autenticación custom ──────────────────────────────────────────
public sealed class FactumBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAuthService authService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;

        if (Request.Headers.TryGetValue("Authorization", out var header) &&
            header.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = header.ToString()["Bearer ".Length..].Trim();
        }
        else if (Request.Query.TryGetValue("token", out var q))
        {
            token = q.ToString();
        }

        if (string.IsNullOrEmpty(token))
            return Task.FromResult(AuthenticateResult.NoResult());

        var user = authService.ValidateToken(token);
        if (user is null)
            return Task.FromResult(AuthenticateResult.Fail("Token inválido o expirado"));

        Context.Items["User"] = user;
        var identity = new System.Security.Claims.ClaimsIdentity("Bearer");
        identity.AddClaim(new System.Security.Claims.Claim("dni", user.Dni));
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
