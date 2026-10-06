using System.Text.Json;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;

namespace Factum.Backend.Tests.Auth;

/// <summary>usuarios-locales §7.2 (B33).</summary>
public sealed class LocalUserBootstrapperTests
{
    private const string TempA = "temporal-de-prueba-1";
    private const string TempB = "temporal-de-prueba-2";
    private const string ResetPwd = "reset-de-emergencia-1";

    private sealed class Env
    {
        public LocalAuthFixture F { get; }
        public CapturingLogger<LocalUserBootstrapper> Log { get; } = new();
        public LocalUserBootstrapper Boot { get; }

        public Env(LocalAuthOptions opts, LocalAuthFixture? shared = null)
        {
            F = shared ?? new LocalAuthFixture(opts);
            var settings = new AuthSettings(AuthModes.Local, null, null, [], [], opts);
            var accounts = new UserAccountService(settings, F.Repo, F.Hasher, F.Auth, F.Time);
            Boot = new LocalUserBootstrapper(settings, F.Repo, accounts, F.Hasher, F.Time, Log);
        }
    }

    private static LocalAuthOptions Opts(SuperadminReset? reset = null, params BootstrapSuperadmin[] admins) =>
        new() { BootstrapSuperadmins = admins, ResetSuperadmin = reset };

    private static string Snapshot(UserAccount u) => JsonSerializer.Serialize(u);

    [Fact]
    public async Task EmptyDb_TwoSuperadmins_Created()
    {
        var env = new Env(Opts(null,
            new BootstrapSuperadmin("99000001", "Prueba Uno", TempA),
            new BootstrapSuperadmin("99000002", "Prueba Dos", TempB)));

        var active = await env.Boot.RunAsync(CancellationToken.None);

        Assert.Equal(2, active);
        Assert.Equal(1, env.F.Repo.EnsureIndexesCalls);
        foreach (var (dni, temp) in new[] { ("99000001", TempA), ("99000002", TempB) })
        {
            var u = env.F.Repo.Get(dni)!;
            Assert.Equal(UserRoles.Superadmin, u.Role);
            Assert.Equal(UserStatuses.Activo, u.Status);
            Assert.True(u.MustChangePassword);
            Assert.Equal("bootstrap", u.CreatedBy);
            Assert.Equal(u.CreatedAt, u.PasswordChangedAt);
            Assert.True(env.F.Hasher.Verify(temp, u.PasswordHash));
            Assert.Contains($"Superadmin inicial creado: DNI {dni}", env.Log.Messages.First(m => m.Contains(dni)));
        }
        Assert.Contains(env.Log.Messages, m => m.StartsWith("Auth: modo local (2 superadmins activos)"));
    }

    [Fact]
    public async Task AlreadyExists_DocumentIdentical_EvenIfTemporaryChanges()
    {
        var first = new Env(Opts(null, new BootstrapSuperadmin("99000001", "Prueba Uno", TempA)));
        await first.Boot.RunAsync(CancellationToken.None);
        var before = Snapshot(first.F.Repo.Get("99000001")!);

        first.F.Time.Advance(TimeSpan.FromHours(1));
        var second = new Env(Opts(null, new BootstrapSuperadmin("99000001", "Otro Nombre", TempB)), first.F);
        await second.Boot.RunAsync(CancellationToken.None);

        Assert.Equal(before, Snapshot(second.F.Repo.Get("99000001")!));
        Assert.Contains(second.Log.Messages, m => m.StartsWith("Superadmin inicial ya existe: DNI 99000001; no se modifica"));
    }

    [Fact]
    public async Task ExistingAsClienteOrSuspended_Warns_NotModified()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync(dni: "99000001", role: UserRoles.Cliente);
        await f.CreateUserAsync(dni: "99000003", role: UserRoles.Superadmin);
        var before = Snapshot(f.Repo.Get("99000001")!);

        var env = new Env(Opts(null, new BootstrapSuperadmin("99000001", "Prueba", TempA)), f);
        await env.Boot.RunAsync(CancellationToken.None);

        Assert.Equal(before, Snapshot(f.Repo.Get("99000001")!));
        Assert.Contains(env.Log.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("99000001"));
    }

    [Fact]
    public async Task NoSuperadmins_NoConfig_Throws()
    {
        var env = new Env(Opts());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Boot.RunAsync(CancellationToken.None));
        Assert.Contains("Auth:Local:BootstrapSuperadmins:0:Dni", ex.Message);
        Assert.Contains("Auth__Local__BootstrapSuperadmins__0__Dni", ex.Message);
    }

    [Fact]
    public async Task OnlySuspendedSuperadmin_Throws()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync(dni: "99000001", role: UserRoles.Superadmin, status: UserStatuses.Suspendido);
        await f.CreateUserAsync(dni: "99000002", role: UserRoles.Cliente);
        var env = new Env(Opts(), f);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Boot.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reset_OnSuspendedSuperadmin_Reactivates_MustChange_NewPca()
    {
        var f = new LocalAuthFixture();
        var acc = await f.CreateUserAsync(dni: "99000001", role: UserRoles.Superadmin, mustChange: false,
            status: UserStatuses.Suspendido);
        acc.SuspendedAt = f.Now;
        acc.SuspendedBy = "20333444";
        acc.FailedLoginCount = 2;
        acc.LockedUntil = f.Now.AddMinutes(10);
        f.Repo.Seed(acc);
        f.Time.Advance(TimeSpan.FromMinutes(1));

        var env = new Env(Opts(new SuperadminReset("99000001", ResetPwd)), f);
        Assert.Equal(1, await env.Boot.RunAsync(CancellationToken.None));

        var u = f.Repo.Get("99000001")!;
        Assert.Equal(UserStatuses.Activo, u.Status);
        Assert.Null(u.SuspendedAt);
        Assert.Null(u.SuspendedBy);
        Assert.True(u.MustChangePassword);
        Assert.NotEqual(acc.PasswordChangedAt, u.PasswordChangedAt);
        Assert.Equal(AuthTime.TruncateToMs(f.Now), u.PasswordChangedAt);
        Assert.Equal(0, u.FailedLoginCount);
        Assert.Null(u.LockedUntil);
        Assert.True(f.Hasher.Verify(ResetPwd, u.PasswordHash));
        Assert.NotNull(u.LastEmergencyResetHash);
        Assert.NotEqual(u.PasswordHash, u.LastEmergencyResetHash); // otra sal
        Assert.Contains(env.Log.Messages, m => m.StartsWith("Reset de emergencia aplicado al superadmin DNI 99000001"));
    }

    [Fact]
    public async Task Reset_SecondStartWithSameValue_NoChange_Warns()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync(dni: "99000001", role: UserRoles.Superadmin);
        await new Env(Opts(new SuperadminReset("99000001", ResetPwd)), f).Boot.RunAsync(CancellationToken.None);
        var before = Snapshot(f.Repo.Get("99000001")!);

        f.Time.Advance(TimeSpan.FromMinutes(5));
        var second = new Env(Opts(new SuperadminReset("99000001", ResetPwd)), f);
        await second.Boot.RunAsync(CancellationToken.None);

        Assert.Equal(before, Snapshot(f.Repo.Get("99000001")!));
        Assert.Contains(second.Log.Messages,
            m => m.StartsWith("Auth:Local:ResetSuperadmin ya se aplicó para DNI 99000001; borralo de la configuración"));
    }

    [Fact]
    public async Task Reset_OnCliente_Ignored()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync(dni: "99000001", role: UserRoles.Superadmin);
        await f.CreateUserAsync(dni: "99000002", role: UserRoles.Cliente);
        var before = Snapshot(f.Repo.Get("99000002")!);

        var env = new Env(Opts(new SuperadminReset("99000002", ResetPwd)), f);
        await env.Boot.RunAsync(CancellationToken.None);

        Assert.Equal(before, Snapshot(f.Repo.Get("99000002")!));
        Assert.Contains(env.Log.Messages,
            m => m.StartsWith("Auth:Local:ResetSuperadmin se ignora: el DNI 99000002 no es un superadmin"));
    }

    [Fact]
    public async Task Reset_OnUnknownDni_Ignored_AndStillNeedsSuperadmin()
    {
        var env = new Env(Opts(new SuperadminReset("99000009", ResetPwd)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Boot.RunAsync(CancellationToken.None));
        Assert.Empty(env.F.Repo.All);
        Assert.Contains(env.Log.Messages, m => m.Contains("se ignora"));
    }

    [Fact]
    public async Task NoLoggedMessage_ContainsTemporaryPasswordsOrHashes()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync(dni: "99000003", role: UserRoles.Superadmin);
        var env = new Env(Opts(new SuperadminReset("99000003", ResetPwd),
            new BootstrapSuperadmin("99000001", "Prueba Uno", TempA),
            new BootstrapSuperadmin("99000002", "Prueba Dos", TempB)), f);
        await env.Boot.RunAsync(CancellationToken.None);
        // Segunda pasada: rama "ya existe" y "ya se aplicó".
        var again = new Env(Opts(new SuperadminReset("99000003", ResetPwd),
            new BootstrapSuperadmin("99000001", "Prueba Uno", TempA)), f);
        await again.Boot.RunAsync(CancellationToken.None);

        var hashes = f.Repo.All.SelectMany(u => new[] { u.PasswordHash, u.LastEmergencyResetHash })
            .Where(h => !string.IsNullOrEmpty(h)).ToList();
        var all = env.Log.Messages.Concat(again.Log.Messages).ToList();
        Assert.NotEmpty(all);
        foreach (var m in all)
        {
            Assert.DoesNotContain(TempA, m);
            Assert.DoesNotContain(TempB, m);
            Assert.DoesNotContain(ResetPwd, m);
            Assert.DoesNotContain("pbkdf2-sha256", m);
            foreach (var h in hashes) Assert.DoesNotContain(h!, m);
        }
    }
}
