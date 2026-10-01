using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Backend.Infrastructure;
using Microsoft.Extensions.Options;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Reports;
using Factum.Backend.Services.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "8080");
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));

// ── Configuración tipada ──────────────────────────────────────────────────────
builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection("MongoDb"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<MpfOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<FaroIntegrationOptions>(builder.Configuration.GetSection("FaroIntegration"));
builder.Services.Configure<Factum.Backend.Controllers.AuditOptions>(builder.Configuration.GetSection("Audit"));
builder.Services.Configure<Factum.Backend.Services.Updates.TatanaUpdatesOptions>(builder.Configuration.GetSection("TatanaUpdates"));

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

// ── Auth provider (dev o MPF según config) ────────────────────────────────────
var authMode = builder.Configuration["Auth:Mode"] ?? "dev";
if (authMode == "mpf")
{
    builder.Services.AddHttpClient<IAuthProvider, MpfAuthProvider>();
}
else
{
    builder.Services.AddSingleton<IAuthProvider, DevAuthProvider>();
}
builder.Services.AddSingleton<IAuthService, AuthService>();

// ── Servicios de negocio ──────────────────────────────────────────────────────
builder.Services.AddSingleton<IReportService, ReportService>();
builder.Services.AddScoped<ICaseService, CaseService>();
builder.Services.AddHttpClient<ISupportService, SupportService>();
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
    auth_mode = authMode
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
