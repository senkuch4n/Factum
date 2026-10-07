using Factum.Backend.Common;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Admin;
using Factum.Backend.Services.Auth;
using Factum.Backend.Services.Branding;
using Factum.Backend.Tests.Admin;
using Factum.Backend.Tests.Auth;
using static Factum.Backend.Tests.Branding.BrandingTestData;

namespace Factum.Backend.Tests.Branding;

/// <summary>marca-por-cliente B21: servicio sobre repositorios en memoria (sin Mongo). DNIs ficticios 99000001…</summary>
public sealed class AccountBrandingServiceTests
{
    private sealed class Fixture
    {
        public InMemoryAccountBrandingRepository Brandings { get; } = new();
        public InMemoryUserRepository Users { get; } = new();
        public InMemoryUserAdminEventRepository Events { get; } = new();
        public MutableTimeProvider Time { get; } = new();
        public CapturingLogger<AccountBrandingService> Log { get; } = new();
        public AccountBrandingService Service { get; }

        public Fixture(string mode = AuthModes.Local)
        {
            var settings = new AuthSettings(mode, null, null, [], [],
                mode == AuthModes.Local ? new LocalAuthOptions() : null);
            Service = new AccountBrandingService(settings, Brandings, Users, Events, Time, Log);
        }

        public UserAccount SeedUser(string dni, string name, string organization = "", string role = UserRoles.Cliente)
        {
            var acc = new UserAccount
            {
                Dni = dni, Name = name, Role = role, Organization = organization, PasswordHash = "x",
                CreatedAt = Time.GetUtcNow().UtcDateTime, UpdatedAt = Time.GetUtcNow().UtcDateTime,
            };
            Users.Seed(acc);
            return acc;
        }

        public DateTime Now => AuthTime.TruncateToMs(Time.GetUtcNow().UtcDateTime);
    }

    private static User U(string dni, string name = "Ana Ficticia") => new() { Dni = dni, Name = name };
    private static BrandingActor Actor(string dni, string name = "Ana Ficticia") => new(dni, name, "10.0.0.7");

    private static string? Code<T>(Result<T> r) => AdminErrors.CodeOf(r);
    private static object? Detail<T>(Result<T> r, string key) => r.Details!.TryGetValue(key, out var v) ? v : null;

    private static BrandingSaveInput Full(byte[]? logo = null, DateTime? expected = null) =>
        Input(Meta(name: "Estudio Ficticio A", lines: ["Calle Ficticia 123", "Tel. 000"], primary: "#1F3A93",
            accent: "#E6ECFA", logoAction: logo is null ? "keep" : "replace", expected: expected), logo: logo);

    // ── primer guardado ──────────────────────────────────────────────────────

    [Fact]
    public async Task PrimerGuardado_Inserta_YAuditaConTargetUserIdDeUsers()
    {
        var f = new Fixture();
        var acc = f.SeedUser(DniA, "Ana Ficticia");
        var png = TestImages.Png(64, 32);

        var r = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Full(png));

        Assert.True(r.IsSuccess, r.Error);
        Assert.True(r.Value!.Changed);
        var dto = r.Value.Branding;
        Assert.True(dto.Exists);
        Assert.Equal("Estudio Ficticio A", dto.OrganizationName);
        Assert.Equal(["Calle Ficticia 123", "Tel. 000"], dto.ContactLines);
        Assert.Equal("#1F3A93", dto.PrimaryColor);
        Assert.Equal("#E6ECFA", dto.AccentColor);
        Assert.Equal(f.Now, dto.UpdatedAt);
        Assert.Equal(DniA, dto.UpdatedBy);
        Assert.Null(dto.SuggestedOrganizationName);
        Assert.NotNull(dto.Logo);
        Assert.Equal($"/api/branding/logo?v={dto.Logo!.Version}", dto.Logo.Url);
        Assert.Equal(("image/png", 64, 32, png.LongLength), (dto.Logo.ContentType, dto.Logo.Width, dto.Logo.Height, dto.Logo.Size));
        Assert.Null(dto.Isotype);

        var doc = f.Brandings.Get(DniA)!;
        Assert.Equal(png, doc.Logo!.Data);
        Assert.Equal(f.Now, doc.CreatedAt);

        var evt = Assert.Single(f.Events.All);
        Assert.Equal(UserAdminActions.UpdateBranding, evt.Action);
        Assert.Equal(evt.ActorDni, evt.TargetDni);
        Assert.Equal(acc.Id, evt.TargetUserId);
        Assert.Equal("Ana Ficticia", evt.TargetName);
        Assert.Null(evt.Reason);
        Assert.Equal("10.0.0.7", evt.Ip);
        Assert.Equal(["organization_name", "contact_lines", "primary_color", "accent_color", "logo"],
            evt.Changes.Select(c => c.Field));
    }

    [Fact]
    public async Task SinCambios_ChangedFalse_SinEscrituraNiEvento_SinMirarLaVersion()
    {
        var f = new Fixture();
        f.SeedUser(DniA, "Ana");
        var png = TestImages.Png(64, 32);
        Assert.True((await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Full(png))).IsSuccess);
        var writes = f.Brandings.Writes;
        var events = f.Events.All.Count;

        // Mismos valores, la misma imagen otra vez como replace y SIN expected_updated_at.
        var r = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Full(png));

        Assert.True(r.IsSuccess, r.Error);
        Assert.False(r.Value!.Changed);
        Assert.True(r.Value.Branding.Exists);
        Assert.Equal(writes, f.Brandings.Writes);
        Assert.Equal(events, f.Events.All.Count);
    }

    [Fact]
    public async Task FormularioVacio_SinDocumento_NoCreaNada()
    {
        var f = new Fixture();
        f.SeedUser(DniA, "Ana", organization: "Org Ficticia");

        var r = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Input(Meta(logoAction: "remove")));

        Assert.True(r.IsSuccess, r.Error);
        Assert.False(r.Value!.Changed);
        Assert.False(r.Value.Branding.Exists);
        Assert.Equal("Org Ficticia", r.Value.Branding.SuggestedOrganizationName);
        Assert.Equal(0, f.Brandings.Count);
        Assert.Empty(f.Events.All);
    }

    // ── control optimista ────────────────────────────────────────────────────

    [Fact]
    public async Task ExpectedViejo_409_SinEscrituraNiEvento()
    {
        var f = new Fixture();
        f.SeedUser(DniA, "Ana");
        var first = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Full());
        var token = first.Value!.Branding.UpdatedAt!.Value;
        f.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.True((await f.Service.SaveOwnAsync(Actor(DniA), U(DniA),
            Input(Meta(name: "Otro nombre", expected: token)))).IsSuccess);
        var writes = f.Brandings.Writes;
        var events = f.Events.All.Count;

        // Otra pestaña con el token viejo.
        var r = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Input(Meta(name: "Tercero", expected: token)));

        Assert.False(r.IsSuccess);
        Assert.Equal("stale_update", Code(r));
        Assert.Equal(ErrorKind.Conflict, r.Kind);
        Assert.Equal("La marca se modificó desde otra sesión. Recargá para ver los cambios.", r.Error);
        Assert.Equal(writes, f.Brandings.Writes);
        Assert.Equal(events, f.Events.All.Count);
        Assert.Equal("Otro nombre", f.Brandings.Get(DniA)!.OrganizationName);
    }

    [Fact]
    public async Task ExpectedCorrecto_ConOtraZonaHoraria_Actualiza()
    {
        var f = new Fixture();
        f.SeedUser(DniA, "Ana");
        var token = (await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Full())).Value!.Branding.UpdatedAt!.Value;
        f.Time.Advance(TimeSpan.FromSeconds(1));

        var r = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA),
            Input(Meta(name: "Nuevo", expected: DateTime.SpecifyKind(token, DateTimeKind.Unspecified))));

        Assert.True(r.IsSuccess, r.Error);
        Assert.True(r.Value!.Changed);
        Assert.Equal("Nuevo", r.Value.Branding.OrganizationName);
        // El guardado reemplaza todo: sin líneas ni colores en este PUT, quedan vacíos.
        Assert.Empty(r.Value.Branding.ContactLines);
        Assert.Null(r.Value.Branding.PrimaryColor);
        Assert.Equal(f.Now, r.Value.Branding.UpdatedAt);
    }

    [Fact]
    public async Task DocumentoExistente_SinExpected_409()
    {
        var f = new Fixture();
        f.SeedUser(DniA, "Ana");
        Assert.True((await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Full())).IsSuccess);
        var writes = f.Brandings.Writes;

        var r = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Input(Meta(name: "Otro")));

        Assert.Equal("stale_update", Code(r));
        Assert.Equal(writes, f.Brandings.Writes);
        Assert.Single(f.Events.All);
    }

    [Fact]
    public async Task SinDocumento_ConExpected_409()
    {
        var f = new Fixture();
        f.SeedUser(DniA, "Ana");
        var r = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Input(Meta(name: "X", expected: f.Now)));
        Assert.Equal("stale_update", Code(r));
        Assert.Equal(0, f.Brandings.Count);
    }

    [Fact]
    public async Task TryInsertFalse_409_SinEvento()
    {
        var f = new Fixture();
        f.SeedUser(DniA, "Ana");
        f.Brandings.InsertReturnsFalse = true;

        var r = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Full());

        Assert.Equal("stale_update", Code(r));
        Assert.Empty(f.Events.All);
    }

    // ── validación ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Validacion_400_ConFieldIndexContrast_NadaEscrito()
    {
        var f = new Fixture();
        f.SeedUser(DniA, "Ana");

        var lines = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA),
            Input(Meta(lines: ["ok", new string('x', 151)])));
        Assert.Equal("validation_failed", Code(lines));
        Assert.Equal(ErrorKind.Validation, lines.Kind);
        Assert.Equal("contact_lines", Detail(lines, "field"));
        Assert.Equal(1, Detail(lines, "index"));
        Assert.Null(Detail(lines, "contrast"));

        var color = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Input(Meta(primary: "#FFFF00")));
        Assert.Equal("validation_failed", Code(color));
        Assert.Equal("primary_color", Detail(color, "field"));
        Assert.Equal(1.0, Detail(color, "contrast"));
        Assert.Equal(4.5, Detail(color, "min_contrast"));
        Assert.Equal("Contraste 1.0:1 con blanco, mínimo 4.5:1.", color.Error);

        Assert.Equal(0, f.Brandings.Count);
        Assert.Empty(f.Events.All);
    }

    // ── panel ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AuthModes.Dev)]
    [InlineData(AuthModes.External)]
    public async Task Panel_FueraDeLocal_NotAvailable_SinTocarUsers(string mode)
    {
        var f = new Fixture(mode);
        f.Users.ThrowOnUse = true;

        Assert.Equal("not_available", Code(await f.Service.GetForAccountAsync("id-x")));
        Assert.Equal("not_available", Code(await f.Service.SaveForAccountAsync(Actor(DniA), "id-x", Full())));
        var img = await f.Service.GetAccountImageAsync("id-x", BrandingImageKind.Logo);
        Assert.Equal("not_available", Code(img));
        Assert.Equal(ErrorKind.NotFound, img.Kind);
    }

    [Fact]
    public async Task Panel_IdInexistente_UserNotFound()
    {
        var f = new Fixture();
        Assert.Equal("user_not_found", Code(await f.Service.GetForAccountAsync("no-existe")));
        Assert.Equal("user_not_found", Code(await f.Service.SaveForAccountAsync(Actor(DniA), "no-existe", Full())));
        Assert.Equal("user_not_found", Code(await f.Service.GetAccountImageAsync("no-existe", BrandingImageKind.Isotype)));
        Assert.Equal(0, f.Brandings.Count);
    }

    [Fact]
    public async Task Panel_SuperadminGuardaLaDeOtraCuenta_AuditaConLaCuenta()
    {
        var f = new Fixture();
        var admin = f.SeedUser(DniC, "Sup Ficticio", role: UserRoles.Superadmin);
        var target = f.SeedUser(DniB, "Perito Ficticio B", organization: "Org B");

        var get = await f.Service.GetForAccountAsync(target.Id);
        Assert.False(get.Value!.Branding.Exists);
        Assert.Equal("Org B", get.Value.Branding.SuggestedOrganizationName);

        var r = await f.Service.SaveForAccountAsync(Actor(admin.Dni, admin.Name), target.Id,
            Input(Meta(name: "Perito Ficticio B", primary: "#7A1F1F", isotypeAction: "replace"),
                isotype: TestImages.Png(32, 32)));

        Assert.True(r.IsSuccess, r.Error);
        Assert.Equal($"/api/admin/users/{target.Id}/branding/isotype?v={r.Value!.Branding.Isotype!.Version}",
            r.Value.Branding.Isotype.Url);
        Assert.NotNull(f.Brandings.Get(DniB));
        Assert.Null(f.Brandings.Get(DniC));
        var evt = Assert.Single(f.Events.All);
        Assert.Equal((DniC, DniB, target.Id, "Perito Ficticio B"), (evt.ActorDni, evt.TargetDni, evt.TargetUserId, evt.TargetName));

        var img = await f.Service.GetAccountImageAsync(target.Id, BrandingImageKind.Isotype);
        Assert.True(img.IsSuccess);
        Assert.Equal(TestImages.Png(32, 32), img.Value!.Data);
        Assert.Equal("image_not_found", Code(await f.Service.GetAccountImageAsync(target.Id, BrandingImageKind.Logo)));
    }

    // ── dev / external ───────────────────────────────────────────────────────

    [Fact]
    public async Task Dev_GuardarLaPropia_AuditaConTargetUserIdVacio_SinTocarUsers()
    {
        var f = new Fixture(AuthModes.Dev);
        f.Users.ThrowOnUse = true;

        var get = await f.Service.GetOwnAsync(U(DniA));
        Assert.True(get.IsSuccess, get.Error);
        Assert.False(get.Value!.Branding.Exists);
        Assert.Null(get.Value.Branding.SuggestedOrganizationName);

        var r = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA, "Sesión Dev"), Full());
        Assert.True(r.IsSuccess, r.Error);
        var evt = Assert.Single(f.Events.All);
        Assert.Equal("", evt.TargetUserId);
        Assert.Equal(DniA, evt.TargetDni);
        Assert.Equal("Sesión Dev", evt.TargetName);

        var empty = await f.Service.SaveOwnAsync(Actor(DniB), U(DniB), Input(Meta()));
        Assert.True(empty.IsSuccess);
        Assert.Null(empty.Value!.Branding.SuggestedOrganizationName);
        Assert.Equal(0, f.Users.Calls);
    }

    [Fact]
    public async Task Local_PropiaSinCuentaEnUsers_TargetUserIdVacio()
    {
        var f = new Fixture();
        var r = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Full());
        Assert.True(r.IsSuccess, r.Error);
        Assert.Equal("", Assert.Single(f.Events.All).TargetUserId);
    }

    // ── auditoría y logs sin bytes ───────────────────────────────────────────

    [Fact]
    public async Task Changes_NuncaLlevanBytes_LogsSinContacto()
    {
        var f = new Fixture();
        f.SeedUser(DniA, "Ana");
        var big = TestImages.Png(1200, 900);
        var r1 = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Input(Meta(name: "Estudio",
            lines: ["Domicilio Secreto 1"], logoAction: "replace", isotypeAction: "replace"),
            logo: big, isotype: TestImages.Jpeg(64, 64)));
        Assert.True(r1.IsSuccess, r1.Error);
        f.Time.Advance(TimeSpan.FromSeconds(1));
        var r2 = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Input(Meta(name: "Estudio",
            lines: ["Domicilio Secreto 1"], logoAction: "remove", isotypeAction: "replace",
            expected: r1.Value!.Branding.UpdatedAt), isotype: TestImages.Png(48, 48)));
        Assert.True(r2.IsSuccess, r2.Error);

        Assert.Equal(2, f.Events.All.Count);
        foreach (var c in f.Events.All.SelectMany(e => e.Changes))
        {
            Assert.Contains(c.Field, BrandingChanges.AllowedFields);
            Assert.NotNull(c.From);
            Assert.NotNull(c.To);
            Assert.True(c.From!.Length <= 1000 && c.To!.Length <= 1000);
            if (c.Field is "logo" or "isotype")
            {
                Assert.Matches("^[0-9a-f]{12}$|^$", c.From);
                Assert.Matches("^[0-9a-f]{12}$|^$", c.To);
            }
        }
        Assert.Contains(f.Log.Messages, m => m.Contains("Marca guardada para DNI 99000001 por 99000001"));
        Assert.DoesNotContain(f.Log.Messages, m => m.Contains("Domicilio Secreto"));
    }

    [Fact]
    public async Task AuditoriaQueFalla_ElGuardadoIgualEsExito()
    {
        var f = new Fixture();
        f.SeedUser(DniA, "Ana");
        f.Events.FailNextInserts = 2;

        var r = await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Full());

        Assert.True(r.IsSuccess, r.Error);
        Assert.True(r.Value!.Changed);
        Assert.Equal(2, f.Events.InsertAttempts);
        Assert.Empty(f.Events.All);
        Assert.Contains(f.Log.Messages, m => m.Contains("Auditoría no registrada"));
    }

    [Fact]
    public async Task GetPropia_EnLocal_SugiereLaOrganizacionSoloSinMarca()
    {
        var f = new Fixture();
        f.SeedUser(DniA, "Ana", organization: "  Estudio de users  ");

        var before = (await f.Service.GetOwnAsync(U(DniA))).Value!.Branding;
        Assert.False(before.Exists);
        Assert.Equal("Estudio de users", before.SuggestedOrganizationName);
        Assert.Empty(before.ContactLines);
        Assert.Null(before.UpdatedAt);

        await f.Service.SaveOwnAsync(Actor(DniA), U(DniA), Full());
        var after = (await f.Service.GetOwnAsync(U(DniA))).Value!.Branding;
        Assert.True(after.Exists);
        Assert.Null(after.SuggestedOrganizationName);
    }

    [Fact]
    public async Task ImagenPropia_SinMarca_ImageNotFound()
    {
        var f = new Fixture();
        var r = await f.Service.GetOwnImageAsync(DniA, BrandingImageKind.Logo);
        Assert.Equal("image_not_found", Code(r));
        Assert.Equal(ErrorKind.NotFound, r.Kind);
        Assert.Equal("La imagen no existe.", r.Error);
    }
}
