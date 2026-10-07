using Factum.Backend.Services.Auth;

namespace Factum.Backend.Tests.Auth;

/// <summary>usuarios-locales §4.2 (B26).</summary>
public sealed class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new(1000);

    [Fact]
    public void Hash_HasVersionedFormat()
    {
        var encoded = _hasher.Hash("una-contraseña-larga");
        var parts = encoded.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal("1000", parts[1]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
        Assert.DoesNotContain("una-contraseña-larga", encoded);
    }

    [Fact]
    public void DefaultIterations_Is600k()
    {
        Assert.Equal(600_000, Pbkdf2PasswordHasher.DefaultIterations);
    }

    [Fact]
    public void Verify_CorrectAndIncorrect()
    {
        var encoded = _hasher.Hash("correcta-123");
        Assert.True(_hasher.Verify("correcta-123", encoded));
        Assert.False(_hasher.Verify("Correcta-123", encoded));
        Assert.False(_hasher.Verify("", encoded));
    }

    [Fact]
    public void SamePassword_DifferentHashes()
    {
        Assert.NotEqual(_hasher.Hash("misma-contraseña"), _hasher.Hash("misma-contraseña"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x$y")]
    [InlineData("pbkdf2-sha256$1000$!!!roto$###")]
    [InlineData("pbkdf2-sha256$abc$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$0$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$-5$AAAA$AAAA")]
    [InlineData("otro-algoritmo$1000$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$1000$$")]
    public void Verify_MalformedEncoded_FalseWithoutThrowing(string encoded)
    {
        Assert.False(_hasher.Verify("lo-que-sea-123", encoded));
        Assert.True(_hasher.NeedsRehash(encoded));
    }

    [Fact]
    public void Verify_NullEncoded_False()
    {
        Assert.False(_hasher.Verify("lo-que-sea-123", null!));
    }

    [Fact]
    public void NeedsRehash_LowerIterations()
    {
        var old = new Pbkdf2PasswordHasher(500).Hash("contraseña-vieja");
        Assert.True(_hasher.NeedsRehash(old));
        Assert.True(_hasher.Verify("contraseña-vieja", old)); // igual verifica: las iteraciones van en el formato
        Assert.False(_hasher.NeedsRehash(_hasher.Hash("contraseña-nueva")));
        Assert.False(new Pbkdf2PasswordHasher(500).NeedsRehash(_hasher.Hash("x-123456789")));
    }
}
