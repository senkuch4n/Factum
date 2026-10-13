using Factum.Backend.Models;
using Factum.Backend.Services.Cases;

namespace Factum.Backend.Tests.Hardening;

/// <summary>api-hardening §4.7 / T6: caso ajeno = Foreign (el servicio lo responde como 404).</summary>
public sealed class CaseAccessTests
{
    private static Case Owned(string dni) => new() { Officer = new User { Dni = dni } };

    [Fact]
    public void Null_NotFound() =>
        Assert.Equal(CaseAccessResult.NotFound, CaseAccess.Check(null, "99000001"));

    [Fact]
    public void Dueno_Ok() =>
        Assert.Equal(CaseAccessResult.Ok, CaseAccess.Check(Owned("99000001"), "99000001"));

    [Fact]
    public void Ajeno_Foreign() =>
        Assert.Equal(CaseAccessResult.Foreign, CaseAccess.Check(Owned("99000001"), "99000002"));

    [Fact]
    public void Ajeno_Superadmin_ConAllow_Ok() =>
        Assert.Equal(CaseAccessResult.Ok,
            CaseAccess.Check(Owned("99000001"), "99000002", allowSuperadmin: true, requesterIsSuperadmin: true));

    [Fact]
    public void Ajeno_Superadmin_SinAllow_Foreign() =>
        Assert.Equal(CaseAccessResult.Foreign,
            CaseAccess.Check(Owned("99000001"), "99000002", allowSuperadmin: false, requesterIsSuperadmin: true));

    [Fact]
    public void Ajeno_NoSuperadmin_ConAllow_Foreign() =>
        Assert.Equal(CaseAccessResult.Foreign,
            CaseAccess.Check(Owned("99000001"), "99000002", allowSuperadmin: true, requesterIsSuperadmin: false));
}
