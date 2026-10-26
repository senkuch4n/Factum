using Factum.Agent.Services.Extraction;

namespace Factum.Agent.Tests.Extraction;

public class ConnectorLockTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Otro = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static ConnectorLock Pinned(string sha) =>
        ConnectorLock.Parse($$"""
            { "package": "app.factum.connector", "version_name": "1.0.0", "version_code": 1,
              "sha256": "{{sha}}", "cert_sha256": "cc", "release_tag": "connector-v1.0.0",
              "asset": "factum-connector-1.0.0.apk" }
            """);

    [Fact]
    public void RecursoEmbebido_Carga()
    {
        var lk = ConnectorLock.Load();
        Assert.Equal("app.factum.connector", lk.Package);
        // En el repo, hasta el primer release, el lock embebido va vacío.
        Assert.False(lk.HasPin);
    }

    [Fact]
    public void SinHash_DaNotReleased()
    {
        var lk = Pinned("");
        var v = lk.Verify(actualApkSha256: Hash, isDevelopment: false, allowUnpinned: false);
        Assert.False(v.Available);
        Assert.Equal(ConnectorReasons.NotReleased, v.Reason);
    }

    [Fact]
    public void HashDistinto_DaHashMismatch()
    {
        var lk = Pinned(Hash);
        var v = lk.Verify(actualApkSha256: Otro, isDevelopment: false, allowUnpinned: false);
        Assert.False(v.Available);
        Assert.Equal(ConnectorReasons.HashMismatch, v.Reason);
    }

    [Fact]
    public void HashCoincide_Disponible()
    {
        var lk = Pinned(Hash);
        var v = lk.Verify(actualApkSha256: Hash.ToUpperInvariant(), isDevelopment: false, allowUnpinned: false);
        Assert.True(v.Available);
        Assert.False(v.Unverified);
    }

    [Fact]
    public void ApkNoEmpaquetado_DaNotPackaged()
    {
        var lk = Pinned(Hash);
        var v = lk.Verify(actualApkSha256: null, isDevelopment: true, allowUnpinned: true);
        Assert.False(v.Available);
        Assert.Equal(ConnectorReasons.NotPackaged, v.Reason);
    }

    [Fact]
    public void AllowUnpinned_IgnoradoFueraDeDevelopment()
    {
        var lk = Pinned(Hash);
        // allowUnpinned true pero NO development: el hash distinto sigue bloqueando.
        var v = lk.Verify(actualApkSha256: Otro, isDevelopment: false, allowUnpinned: true);
        Assert.False(v.Available);
        Assert.Equal(ConnectorReasons.HashMismatch, v.Reason);
    }

    [Fact]
    public void AllowUnpinned_EnDevelopment_DejaPasarSinVerificar()
    {
        var lk = Pinned(Hash);
        var v = lk.Verify(actualApkSha256: Otro, isDevelopment: true, allowUnpinned: true);
        Assert.True(v.Available);
        Assert.True(v.Unverified);
    }
}
