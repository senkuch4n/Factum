using Factum.Backend.Services.Admin;

namespace Factum.Backend.Tests.Admin;

/// <summary>abm-clientes §6.4 (B24).</summary>
public sealed class AccountFieldRulesTests
{
    [Theory]
    [InlineData("1234567", true)]
    [InlineData("12345678", true)]
    [InlineData(" 30111222 ", true)]
    [InlineData("123456", false)]
    [InlineData("123456789", false)]
    [InlineData("12a45678", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Dni(string? v, bool ok)
    {
        var e = AccountFieldRules.Dni(v);
        Assert.Equal(ok, e is null);
        if (!ok) { Assert.Equal("dni", e!.Field); Assert.Equal(AdminErrors.MsgDni, e.Message); }
    }

    [Fact]
    public void Name_RequiredAndMax120()
    {
        Assert.Equal(AdminErrors.MsgNameRequired, AccountFieldRules.Name("   ")!.Message);
        Assert.Equal(AdminErrors.MsgNameRequired, AccountFieldRules.Name(null)!.Message);
        Assert.Null(AccountFieldRules.Name(new string('a', 120)));
        Assert.Null(AccountFieldRules.Name("  " + new string('a', 120) + "  "));
        var e = AccountFieldRules.Name(new string('a', 121))!;
        Assert.Equal("name", e.Field);
        Assert.Equal(AdminErrors.MsgNameTooLong, e.Message);
    }

    [Fact]
    public void Sigla_Max30()
    {
        Assert.Null(AccountFieldRules.Sigla(""));
        Assert.Null(AccountFieldRules.Sigla(new string('s', 30)));
        Assert.Equal("sigla", AccountFieldRules.Sigla(new string('s', 31))!.Field);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("12345", false)]          // 5 dígitos
    [InlineData("123456", true)]          // 6 dígitos
    [InlineData("+54 (11) 4555-1234", true)]
    [InlineData("011 4555.1234", true)]
    [InlineData("++5411455512", false)]
    [InlineData("54+1145551234", false)]
    [InlineData("-123456", false)]
    [InlineData("12345678901234567890", true)]   // 20 dígitos
    [InlineData("123456789012345678901", false)] // 21 dígitos
    [InlineData("tel 1234567", false)]
    public void ContactPhone(string v, bool ok)
    {
        var e = AccountFieldRules.ContactPhone(v);
        Assert.Equal(ok, e is null);
        if (!ok) Assert.Equal(AdminErrors.MsgContactPhone, e!.Message);
    }

    [Fact]
    public void ContactPhone_Max40Chars()
    {
        // 8 dígitos y 33 espacios = 41 caracteres.
        Assert.NotNull(AccountFieldRules.ContactPhone("1234" + new string(' ', 33) + "5678"));
        Assert.Null(AccountFieldRules.ContactPhone("1234" + new string(' ', 32) + "5678"));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("ana@estudio.com", true)]
    [InlineData("ana.estudio.com", false)] // sin @
    [InlineData("ana@estudio", false)]
    [InlineData("a na@estudio.com", false)]
    public void ContactEmail(string v, bool ok)
    {
        var e = AccountFieldRules.ContactEmail(v);
        Assert.Equal(ok, e is null);
        if (!ok) { Assert.Equal("contact_email", e!.Field); Assert.Equal(AdminErrors.MsgContactEmail, e.Message); }
    }

    [Fact]
    public void ContactEmail_Max254()
    {
        var local = new string('a', 64);
        var ok = local + "@" + new string('b', 254 - 64 - 1 - 4) + ".com";
        Assert.Equal(254, ok.Length);
        Assert.Null(AccountFieldRules.ContactEmail(ok));
        Assert.NotNull(AccountFieldRules.ContactEmail("x" + ok));
    }

    [Fact]
    public void Organization_Max120()
    {
        Assert.Null(AccountFieldRules.Organization(new string('o', 120)));
        Assert.Equal(AdminErrors.MsgOrganization, AccountFieldRules.Organization(new string('o', 121))!.Message);
    }

    [Fact]
    public void Notes_Max1000_KeepsInnerNewlines()
    {
        Assert.Null(AccountFieldRules.Notes(new string('n', 1000)));
        Assert.Equal(AdminErrors.MsgNotes, AccountFieldRules.Notes(new string('n', 1001))!.Message);
        Assert.Equal("línea 1\nlínea 2", AccountFieldRules.Normalize("  línea 1\nlínea 2 \n"));
    }

    [Fact]
    public void Reason_Max300()
    {
        Assert.Null(AccountFieldRules.Reason(null));
        Assert.Null(AccountFieldRules.Reason(new string('r', 300)));
        var e = AccountFieldRules.Reason(new string('r', 301))!;
        Assert.Equal("reason", e.Field);
        Assert.Equal(AdminErrors.MsgReason, e.Message);
    }

    [Fact]
    public void ValidateCreate_ReturnsFirstErrorInTableOrder()
    {
        Assert.Equal("dni", AccountFieldRules.ValidateCreate("1", "", "", "x", "x", "", "")!.Field);
        Assert.Equal("name", AccountFieldRules.ValidateCreate("30111222", "", "", "x", "x", "", "")!.Field);
        Assert.Equal("contact_phone", AccountFieldRules.ValidateCreate("30111222", "Ana", "", "x", "x", "", "")!.Field);
        Assert.Null(AccountFieldRules.ValidateCreate("30111222", "Ana", null, null, null, null, null));
    }
}
