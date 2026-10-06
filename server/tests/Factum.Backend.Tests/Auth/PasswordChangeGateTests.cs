using Factum.Backend.Controllers;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Factum.Backend.Tests.Auth;

/// <summary>usuarios-locales §6.5 y §6.6 (B34).</summary>
public sealed class PasswordChangeGateTests
{
    private static readonly AuthSession MustChange = new(UserRoles.Cliente, true);
    private static readonly AuthSession NoChange = new(UserRoles.Cliente, false);

    private static EndpointMetadataCollection Meta(params object[] items) => new(items);

    public static TheoryData<string, bool> Cases => new()
    {
        // metadata, ¿bloquea con MustChange?
        { "authorize", true },
        { "authorize+anonymous", false },
        { "authorize+mark", false },
        { "anonymous", false },
        { "none", false },
        { "mark", false },
    };

    private static EndpointMetadataCollection? Build(string kind) => kind switch
    {
        "authorize" => Meta(new AuthorizeAttribute()),
        "authorize+anonymous" => Meta(new AuthorizeAttribute(), new AllowAnonymousAttribute()),
        "authorize+mark" => Meta(new AuthorizeAttribute(), new AllowDuringPasswordChangeAttribute()),
        "anonymous" => Meta(new AllowAnonymousAttribute()),
        "mark" => Meta(new AllowDuringPasswordChangeAttribute()),
        _ => Meta(),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ShouldBlock_WithMustChange(string kind, bool expected)
    {
        Assert.Equal(expected, PasswordChangeGateMiddleware.ShouldBlock(Build(kind), MustChange));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NeverBlocks_WithoutMustChange(string kind, bool _)
    {
        Assert.False(PasswordChangeGateMiddleware.ShouldBlock(Build(kind), NoChange));
        Assert.False(PasswordChangeGateMiddleware.ShouldBlock(Build(kind), null));
    }

    [Fact]
    public void NullMetadata_DoesNotBlock()
    {
        Assert.False(PasswordChangeGateMiddleware.ShouldBlock(null, MustChange));
    }

    [Fact]
    public void AuthEndpoints_HaveTheMark()
    {
        var me = typeof(AuthController).GetMethod(nameof(AuthController.Me))!;
        var change = typeof(AuthController).GetMethod(nameof(AuthController.ChangePassword))!;
        Assert.NotNull(me.GetCustomAttributes(typeof(AllowDuringPasswordChangeAttribute), false).SingleOrDefault());
        Assert.NotNull(change.GetCustomAttributes(typeof(AllowDuringPasswordChangeAttribute), false).SingleOrDefault());
        Assert.NotNull(me.GetCustomAttributes(typeof(AuthorizeAttribute), false).SingleOrDefault());
        Assert.NotNull(change.GetCustomAttributes(typeof(AuthorizeAttribute), false).SingleOrDefault());
    }

    [Fact]
    public void RequireSuperadmin_Predicate()
    {
        Assert.True(RequireSuperadminAttribute.IsAllowed(new AuthSession(UserRoles.Superadmin, false)));
        Assert.True(RequireSuperadminAttribute.IsAllowed(new AuthSession(UserRoles.Superadmin, true)));
        Assert.False(RequireSuperadminAttribute.IsAllowed(new AuthSession(UserRoles.Cliente, false)));
        Assert.False(RequireSuperadminAttribute.IsAllowed(null));
    }

    [Fact]
    public void GetSession_DefaultsToCliente()
    {
        var ctx = new DefaultHttpContext();
        Assert.Equal(new AuthSession(UserRoles.Cliente, false), ctx.GetSession());
        ctx.Items[AuthContextKeys.Session] = new AuthSession(UserRoles.Superadmin, true);
        Assert.Equal(new AuthSession(UserRoles.Superadmin, true), ctx.GetSession());
    }

    [Theory]
    [InlineData("unauthenticated", "Iniciá sesión para continuar.")]
    [InlineData("invalid_token", "Tu sesión terminó. Volvé a ingresar.")]
    [InlineData("session_revoked", "Tu sesión terminó. Volvé a ingresar.")]
    [InlineData("account_suspended", "Tu cuenta fue suspendida. Comunicate con Factum para reactivarla.")]
    public void ChallengeBody_Table(string code, string error)
    {
        var (status, body) = FactumBearerHandler.ChallengeBody(code);
        Assert.Equal(401, status);
        Assert.Equal(code, body["code"]);
        Assert.Equal(error, body["error"]);
    }
}
