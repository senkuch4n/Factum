using Factum.Backend.Services.Auth;
using Microsoft.Extensions.Configuration;

namespace Factum.Backend.Tests.Auth;

/// <summary>usuarios-locales §4.3 (B28).</summary>
public sealed class AuthSettingsLocalTests
{
    private const string GoodSecret = "0123456789abcdef0123456789abcdef"; // 32
    private const string Temp = "temporal-de-prueba-1";

    private static AuthSettings Resolve(Dictionary<string, string?> data, bool isDevelopment = false) =>
        AuthSettingsResolver.Resolve(new ConfigurationBuilder().AddInMemoryCollection(data).Build(), isDevelopment);

    private static Dictionary<string, string?> Local(params (string Key, string? Value)[] extra)
    {
        var d = new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "local",
            ["Jwt:Secret"] = GoodSecret,
            ["Auth:Local:BootstrapSuperadmins:0:Dni"] = "99000001",
            ["Auth:Local:BootstrapSuperadmins:0:Name"] = "Prueba SDD",
            ["Auth:Local:BootstrapSuperadmins:0:TemporaryPassword"] = Temp,
        };
        foreach (var (k, v) in extra) d[k] = v;
        return d;
    }

    [Fact]
    public void Local_Valid()
    {
        var s = Resolve(Local(
            ("Auth:Local:BootstrapSuperadmins:1:Dni", "9900002"),
            ("Auth:Local:BootstrapSuperadmins:1:Name", "  Segunda  "),
            ("Auth:Local:BootstrapSuperadmins:1:TemporaryPassword", "otra-temporal-2"),
            ("Auth:Local:PasswordMinLength", "12"),
            ("Auth:Local:MaxFailedAttempts", "3"),
            ("Auth:Local:LockoutMinutes", "30"),
            ("Auth:Local:ResetSuperadmin:Dni", "99000001"),
            ("Auth:Local:ResetSuperadmin:TemporaryPassword", "reset-de-emergencia")));
        Assert.Empty(s.Errors);
        Assert.Equal(AuthModes.Local, s.Mode);
        Assert.NotNull(s.Local);
        Assert.Equal(12, s.Local.PasswordMinLength);
        Assert.Equal(3, s.Local.MaxFailedAttempts);
        Assert.Equal(30, s.Local.LockoutMinutes);
        Assert.Equal(2, s.Local.BootstrapSuperadmins.Count);
        Assert.Equal(new BootstrapSuperadmin("9900002", "Segunda", "otra-temporal-2"), s.Local.BootstrapSuperadmins[1]);
        Assert.Equal(new SuperadminReset("99000001", "reset-de-emergencia"), s.Local.ResetSuperadmin);
    }

    [Fact]
    public void Local_Defaults_AndModeCaseInsensitive()
    {
        var d = Local();
        d["Auth:Mode"] = " LOCAL ";
        var s = Resolve(d);
        Assert.Empty(s.Errors);
        Assert.Equal(AuthModes.Local, s.Mode);
        Assert.Equal(10, s.Local!.PasswordMinLength);
        Assert.Equal(5, s.Local.MaxFailedAttempts);
        Assert.Equal(15, s.Local.LockoutMinutes);
        Assert.Null(s.Local.ResetSuperadmin);
    }

    [Fact]
    public void Local_EmptyResetIsOff()
    {
        var s = Resolve(Local(("Auth:Local:ResetSuperadmin:Dni", ""), ("Auth:Local:ResetSuperadmin:TemporaryPassword", "")));
        Assert.Empty(s.Errors);
        Assert.Null(s.Local!.ResetSuperadmin);
    }

    [Theory]
    [InlineData("factum-dev-secret-change-in-production")]
    [InlineData("0123456789abcdef0123456789abcde")] // 31
    [InlineData("")]
    public void Local_WeakSecret_ErrorOutsideDevelopment_WarningInDevelopment(string secret)
    {
        var prod = Resolve(Local(("Jwt:Secret", secret)));
        Assert.Contains(prod.Errors, e => e.Contains("Jwt:Secret"));

        var dev = Resolve(Local(("Jwt:Secret", secret)), isDevelopment: true);
        Assert.Empty(dev.Errors);
        Assert.Contains(dev.Warnings, w => w.Contains("Jwt:Secret"));
    }

    [Fact]
    public void Bootstrap_Dni6Digits_Error()
    {
        var s = Resolve(Local(("Auth:Local:BootstrapSuperadmins:0:Dni", "123456")));
        var e = Assert.Single(s.Errors);
        Assert.Contains("Auth:Local:BootstrapSuperadmins:0:Dni", e);
        Assert.DoesNotContain(Temp, e);
    }

    [Fact]
    public void Bootstrap_EmptyName_Error()
    {
        var s = Resolve(Local(("Auth:Local:BootstrapSuperadmins:0:Name", "   ")));
        var e = Assert.Single(s.Errors);
        Assert.Contains("Auth:Local:BootstrapSuperadmins:0:Name", e);
        Assert.DoesNotContain(Temp, e);
    }

    [Fact]
    public void Bootstrap_NameTooLong_Error()
    {
        var s = Resolve(Local(("Auth:Local:BootstrapSuperadmins:0:Name", new string('n', 121))));
        Assert.Contains("Auth:Local:BootstrapSuperadmins:0:Name", Assert.Single(s.Errors));
    }

    [Fact]
    public void Bootstrap_ShortTemporary_Error_WithoutValue()
    {
        const string shortTemp = "corta-123"; // 9
        var s = Resolve(Local(("Auth:Local:BootstrapSuperadmins:0:TemporaryPassword", shortTemp)));
        var e = Assert.Single(s.Errors);
        Assert.Equal("Auth:Local:BootstrapSuperadmins:0:TemporaryPassword tiene 9 caracteres; el mínimo es 10.", e);
        Assert.DoesNotContain(shortTemp, e);
    }

    [Fact]
    public void Bootstrap_TemporaryTooLong_Error_WithoutValue()
    {
        var longTemp = new string('z', 129);
        var s = Resolve(Local(("Auth:Local:BootstrapSuperadmins:0:TemporaryPassword", longTemp)));
        var e = Assert.Single(s.Errors);
        Assert.DoesNotContain(longTemp, e);
    }

    [Fact]
    public void Bootstrap_DuplicateDni_Error_WithoutPasswords()
    {
        var s = Resolve(Local(
            ("Auth:Local:BootstrapSuperadmins:1:Dni", "99000001"),
            ("Auth:Local:BootstrapSuperadmins:1:Name", "Otra"),
            ("Auth:Local:BootstrapSuperadmins:1:TemporaryPassword", "segunda-temporal")));
        var e = Assert.Single(s.Errors);
        Assert.Contains("repetido", e);
        Assert.DoesNotContain(Temp, e);
        Assert.DoesNotContain("segunda-temporal", e);
    }

    [Fact]
    public void Reset_OnlyDni_Error()
    {
        var s = Resolve(Local(("Auth:Local:ResetSuperadmin:Dni", "99000001")));
        Assert.Contains("Auth:Local:ResetSuperadmin", Assert.Single(s.Errors));
    }

    [Fact]
    public void Reset_OnlyPassword_Error_WithoutValue()
    {
        var s = Resolve(Local(("Auth:Local:ResetSuperadmin:TemporaryPassword", "reset-de-emergencia")));
        var e = Assert.Single(s.Errors);
        Assert.DoesNotContain("reset-de-emergencia", e);
    }

    [Fact]
    public void Reset_ShortPassword_Error_WithoutValue()
    {
        var s = Resolve(Local(("Auth:Local:ResetSuperadmin:Dni", "99000001"),
            ("Auth:Local:ResetSuperadmin:TemporaryPassword", "corto")));
        var e = Assert.Single(s.Errors);
        Assert.Contains("Auth:Local:ResetSuperadmin:TemporaryPassword", e);
        Assert.DoesNotContain("corto", e.Replace("caracteres", ""));
    }

    [Theory]
    [InlineData("Auth:Local:PasswordMinLength", "7")]
    [InlineData("Auth:Local:PasswordMinLength", "65")]
    [InlineData("Auth:Local:MaxFailedAttempts", "0")]
    [InlineData("Auth:Local:LockoutMinutes", "1441")]
    [InlineData("Auth:Local:LockoutMinutes", "quince")]
    public void Local_IntOutOfRange_Error(string key, string value)
    {
        var s = Resolve(Local((key, value)));
        Assert.Contains(s.Errors, e => e.Contains(key));
    }

    [Fact]
    public void Local_WithoutBootstrap_IsValidConfig()
    {
        // La falta de superadmins la detecta el bootstrapper contra la base, no la config.
        var s = Resolve(new Dictionary<string, string?> { ["Auth:Mode"] = "local", ["Jwt:Secret"] = GoodSecret });
        Assert.Empty(s.Errors);
        Assert.Empty(s.Local!.BootstrapSuperadmins);
    }

    [Fact]
    public void Dev_OutsideDevelopment_WithoutFlag_Error()
    {
        var s = Resolve(new Dictionary<string, string?> { ["Auth:Mode"] = "dev" });
        var e = Assert.Single(s.Errors);
        Assert.Contains("Auth:AllowDevOutsideDevelopment=true", e);
        Assert.Contains("Auth:Mode=local", e);
    }

    [Fact]
    public void Dev_DefaultMode_OutsideDevelopment_WithoutFlag_Error()
    {
        var s = Resolve(new Dictionary<string, string?>());
        Assert.Single(s.Errors);
    }

    [Fact]
    public void Dev_OutsideDevelopment_WithFlag_Warning()
    {
        var s = Resolve(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "dev",
            ["Auth:AllowDevOutsideDevelopment"] = "true",
        });
        Assert.Empty(s.Errors);
        Assert.True(s.AllowDevOutsideDevelopment);
        Assert.Contains(s.Warnings, w => w.Contains("no usar en producción"));
    }

    [Fact]
    public void Dev_OutsideDevelopment_InvalidFlag_Error()
    {
        var s = Resolve(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "dev",
            ["Auth:AllowDevOutsideDevelopment"] = "sí",
        });
        Assert.Contains("Auth:AllowDevOutsideDevelopment", Assert.Single(s.Errors));
    }

    [Fact]
    public void Dev_InDevelopment_NoError()
    {
        var s = Resolve(new Dictionary<string, string?> { ["Auth:Mode"] = "dev" }, isDevelopment: true);
        Assert.Empty(s.Errors);
        Assert.Empty(s.Warnings);
        Assert.Equal(AuthModes.Dev, s.Mode);
        Assert.Null(s.Local);
    }

    [Fact]
    public void InvalidMode_MentionsLocal()
    {
        var s = Resolve(new Dictionary<string, string?> { ["Auth:Mode"] = "foo" });
        var e = Assert.Single(s.Errors);
        Assert.Contains("\"local\"", e);
        Assert.Contains("\"dev\"", e);
        Assert.Contains("\"external\"", e);
    }

    [Fact]
    public void InvalidLocalKeys_IgnoredInDev()
    {
        var s = Resolve(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "dev",
            ["Auth:Local:PasswordMinLength"] = "3",
            ["Auth:Local:BootstrapSuperadmins:0:Dni"] = "1",
            ["Auth:Local:BootstrapSuperadmins:0:TemporaryPassword"] = "x",
            ["Auth:Local:ResetSuperadmin:Dni"] = "99000001",
        }, isDevelopment: true);
        Assert.Empty(s.Errors);
        Assert.Null(s.Local);
    }

    [Fact]
    public void NoErrorOrWarning_ContainsAnyPassword()
    {
        var s = Resolve(Local(
            ("Auth:Local:BootstrapSuperadmins:0:Dni", "1"),
            ("Auth:Local:BootstrapSuperadmins:1:Dni", "99000002"),
            ("Auth:Local:BootstrapSuperadmins:1:Name", ""),
            ("Auth:Local:BootstrapSuperadmins:1:TemporaryPassword", "pw-secreta"),
            ("Auth:Local:ResetSuperadmin:TemporaryPassword", "otra-pw-secreta-larga"),
            ("Jwt:Secret", "corto")), isDevelopment: true);
        Assert.NotEmpty(s.Errors);
        foreach (var m in s.Errors.Concat(s.Warnings))
        {
            Assert.DoesNotContain(Temp, m);
            Assert.DoesNotContain("pw-secreta", m);
        }
    }
}
