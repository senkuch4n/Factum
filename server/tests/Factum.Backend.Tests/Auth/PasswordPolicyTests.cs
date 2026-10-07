using Factum.Backend.Services.Auth;

namespace Factum.Backend.Tests.Auth;

/// <summary>usuarios-locales §6.7 (B27).</summary>
public sealed class PasswordPolicyTests
{
    private const string Dni = "30111222";

    private static PasswordRuleError? Validate(string pwd, string? confirmation = null) =>
        PasswordPolicy.ValidateNew(pwd, confirmation ?? pwd, Dni, 10);

    [Fact]
    public void Length9_TooShort()
    {
        var e = Validate(new string('a', 9));
        Assert.NotNull(e);
        Assert.Equal("password_too_short", e.Code);
        Assert.Equal("new_password", e.Field);
        Assert.Equal("La contraseña nueva tiene que tener al menos 10 caracteres.", e.Message);
    }

    [Fact]
    public void Length10_Ok() => Assert.Null(Validate(new string('a', 10)));

    [Fact]
    public void Length128_Ok() => Assert.Null(Validate(new string('a', 128)));

    [Fact]
    public void Length129_TooLong()
    {
        var e = Validate(new string('a', 129));
        Assert.NotNull(e);
        Assert.Equal("password_too_long", e.Code);
        Assert.Equal("new_password", e.Field);
        Assert.Equal("La contraseña nueva puede tener hasta 128 caracteres.", e.Message);
    }

    [Fact]
    public void NoTrim_SpacesCount()
    {
        Assert.Null(Validate("    abc   x")); // 11 caracteres con espacios
    }

    [Fact]
    public void ContainsDni()
    {
        var e = Validate("clave-30111222-x");
        Assert.NotNull(e);
        Assert.Equal("password_contains_dni", e.Code);
        Assert.Equal("new_password", e.Field);
        Assert.Equal("La contraseña nueva no puede contener tu DNI.", e.Message);
    }

    [Fact]
    public void Mismatch()
    {
        var e = Validate("una-buena-clave", "otra-buena-clave");
        Assert.NotNull(e);
        Assert.Equal("password_mismatch", e.Code);
        Assert.Equal("new_password_confirmation", e.Field);
        Assert.Equal("Las contraseñas nuevas no coinciden.", e.Message);
    }

    [Fact]
    public void Order_MismatchBeforeLength()
    {
        Assert.Equal("password_mismatch", Validate("corta", "distinta")!.Code);
    }

    [Fact]
    public void Order_TooShortBeforeContainsDni()
    {
        Assert.Equal("password_too_short", Validate("30111222")!.Code);
    }

    [Fact]
    public void Order_TooLongBeforeContainsDni()
    {
        Assert.Equal("password_too_long", Validate(Dni + new string('a', 125))!.Code);
    }

    [Fact]
    public void SameAsCurrent_AndInvalidCurrent()
    {
        var same = PasswordPolicy.SameAsCurrent();
        Assert.Equal(("password_same_as_current", "new_password"), (same.Code, same.Field));
        Assert.Equal("La contraseña nueva tiene que ser distinta de la actual.", same.Message);
        var cur = PasswordPolicy.InvalidCurrent();
        Assert.Equal(("invalid_current_password", "current_password"), (cur.Code, cur.Field));
        Assert.Equal("La contraseña actual no es correcta.", cur.Message);
    }
}
