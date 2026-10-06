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
// subida-archivos-grandes §5.1 / DT11: un tope o margen fuera de rango dejaría la subida inutilizable.
var storageSettings = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
if (storageSettings.MaxUploadBytes <= 0)
    configErrors.Add("Storage:MaxUploadBytes tiene que ser mayor que 0");
if (storageSettings.MinFreeBytes < 0)
    configErrors.Add("Storage:MinFreeBytes tiene que ser mayor o igual que 0");
// zip-local-informe-servidor §4.1: tope de generate/finish y orígenes CORS permitidos.
var reportSettingsRaw = builder.Configuration.GetSection("Report").Get<ReportOptions>() ?? new ReportOptions();
if (reportSettingsRaw.MaxGenerateUploadBytes <= 0)
    configErrors.Add("Report:MaxGenerateUploadBytes tiene que ser mayor que 0");
var (corsOrigins, corsErrors) = CorsOrigins.Parse(builder.Configuration);
configErrors.AddRange(corsErrors);
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
// FactumBearerHandler (Infrastructure/) es el único handler: valida el JWT y, en Auth:Mode=local,
// la cuenta en users en cada request (usuarios-locales §6.4).
builder.Services.AddAuthentication("FactumBearerScheme")
    .AddScheme<AuthenticationSchemeOptions, FactumBearerHandler>(
        "FactumBearerScheme", _ => { });
builder.Services.AddAuthorization();

// ── CORS (zip-local-informe-servidor §5.9) ────────────────────────────────────
// Solo los orígenes de Cors:AllowedOrigins (default: el front local). Sin credenciales: el token
// viaja en el header Authorization.
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins([.. corsOrigins]).AllowAnyHeader().AllowAnyMethod()));

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
builder.Services.AddSingleton<IDiskSpaceProbe, DriveInfoDiskSpaceProbe>();
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddSingleton<IBrandingService, BrandingService>();
builder.Services.AddSingleton<IReportSettings, ReportSettings>();
builder.Services.AddSingleton<IExpertProfileRepository, ExpertProfileRepository>();
builder.Services.AddSingleton<ICatalogRepository, CatalogRepository>();

// ── Auth provider (dev, external o local según AuthSettingsResolver) ──────────
// usuarios-locales §7.1: el repositorio de users, la validación de sesión y las cuentas se
// registran siempre (el handler pide ISessionValidator); en dev/external no tocan la base.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IUserRepository, UserRepository>();
builder.Services.AddSingleton<ISessionValidator, SessionValidator>();
builder.Services.AddSingleton<IUserAccountService, UserAccountService>();
builder.Services.AddSingleton<LocalUserBootstrapper>();
// abm-clientes §6.9: panel de cuentas. Se registran siempre; los repositorios no conectan hasta el
// primer uso, así que en dev/external no crean nada.
builder.Services.AddSingleton<IUserAdminEventRepository, UserAdminEventRepository>();
builder.Services.AddSingleton<IAdminLockRepository, AdminLockRepository>();
builder.Services.AddSingleton<Factum.Backend.Services.Admin.ITemporaryPasswordGenerator,
    Factum.Backend.Services.Admin.TemporaryPasswordGenerator>();
builder.Services.AddSingleton<Factum.Backend.Services.Admin.IUserAdminService,
    Factum.Backend.Services.Admin.UserAdminService>();
if (authSettings.Mode == AuthModes.External)
{
    builder.Services.AddHttpClient<IAuthProvider, ExternalHttpAuthProvider>(c =>
        c.Timeout = TimeSpan.FromSeconds(authSettings.External!.TimeoutSeconds));
}
else if (authSettings.Mode == AuthModes.Local)
{
    builder.Services.AddSingleton(authSettings.Local!);
    builder.Services.AddSingleton<IAuthProvider, LocalAuthProvider>();
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

// En local, la línea de modo la escribe el bootstrapper, con la cantidad de superadmins activos.
if (authSettings.Mode != AuthModes.Local)
    app.Logger.LogInformation("Auth: modo {Mode}", authSettings.Mode);
app.Logger.LogInformation("CORS: orígenes permitidos {Lista}", string.Join(", ", corsOrigins));
if (authSettings.ExternalLoginUri is { } loginUri)
    app.Logger.LogInformation("Auth: login externo en {Url}",
        loginUri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped));
if (supportSettings.Enabled)
    app.Logger.LogInformation("Soporte: habilitado ({Host})", new Uri(supportSettings.Options.BaseUrl).Authority);
else
    app.Logger.LogInformation("Soporte: deshabilitado");
foreach (var warning in authSettings.Warnings.Concat(supportSettings.Warnings))
    app.Logger.LogWarning("{Warning}", warning);

// usuarios-locales §7.2: índices de users, superadmins iniciales, reset de emergencia y chequeo de
// que haya al menos un superadmin activo. Si falla (Mongo caído, sin superadmins), no arranca.
// abm-clientes §4.2: índice de la auditoría del panel, solo en local (si falla, tampoco arranca).
if (authSettings.Mode == AuthModes.Local)
{
    await app.Services.GetRequiredService<LocalUserBootstrapper>().RunAsync(CancellationToken.None);
    await app.Services.GetRequiredService<IUserAdminEventRepository>().EnsureIndexesAsync(CancellationToken.None);
}

// Branding se carga una sola vez; resolverlo acá hace que sus warnings (logo inválido,
// textos truncados) salgan en el log de arranque y no en el primer request.
app.Services.GetRequiredService<IBrandingService>();
// Ídem la config del informe (zona horaria, domicilio, textos por defecto).
app.Services.GetRequiredService<IReportSettings>();
// subida-archivos-grandes §5.1: temporales de subidas de un proceso anterior (nunca hay subidas en
// curso al arrancar) y una línea con el tope y el espacio que ve el contenedor (D12).
{
    var storage = app.Services.GetRequiredService<IStorageService>();
    var orphans = storage.CleanupOrphanUploads();
    // zip-local-informe-servidor §5.8: capturas de generaciones que no llegaron al finally.
    var orphanGenerations = storage.CleanupOrphanGenerations();
    var storageOpts = app.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
    var free = storage.GetAvailableFreeBytes();
    app.Logger.LogInformation(
        "Subidas: tope {Max}, margen libre {MinFree}, espacio libre en {DataDirectory}: {Free} (temporales huérfanos borrados: {N}; generaciones huérfanas borradas: {G})",
        EvidenceUpload.FormatBytes(storageOpts.MaxUploadBytes), EvidenceUpload.FormatBytes(storageOpts.MinFreeBytes),
        Path.GetFullPath(storageOpts.DataDirectory), free is { } f ? EvidenceUpload.FormatBytes(f) : "desconocido",
        orphans, orphanGenerations);
}

app.UseForwardedHeaders();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
// usuarios-locales §6.5: con el cambio de contraseña pendiente, 403 password_change_required.
app.UseMiddleware<PasswordChangeGateMiddleware>();

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
await app.RunAsync();
