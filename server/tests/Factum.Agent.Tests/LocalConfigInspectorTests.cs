using System.Security.Cryptography;
using Factum.Agent.Common;

namespace Factum.Agent.Tests;

/// <summary>appsettings.Local.json externo (tatana-instalador-autoupdate D9, D-T16, §5.5).</summary>
public sealed class LocalConfigInspectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "factum-localcfg-" + Guid.NewGuid().ToString("N"));

    public LocalConfigInspectorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temporal propio */ }
    }

    [Fact]
    public void Inexistente()
    {
        var path = Path.Combine(_dir, "appsettings.Local.json");
        var st = LocalConfigInspector.Inspect(path);
        Assert.Equal(Path.GetFullPath(path), st.Path);
        Assert.False(st.Exists);
        Assert.False(st.Loaded);
        Assert.Null(st.Error);
        Assert.False(st.OverridesAllowedOrigins);
        Assert.False(File.Exists(path)); // no lo crea
    }

    [Fact]
    public void InexistenteDesdeTexto() =>
        Assert.Equal(new LocalConfigStatus("x", false, false, null, false), LocalConfigInspector.InspectText("x", null));

    [Theory]
    [InlineData("{ \"Agent\": { \"Mock\": true ")]
    [InlineData("no es json")]
    [InlineData("")]
    public void JsonInvalido(string text)
    {
        var st = LocalConfigInspector.InspectText("x", text);
        Assert.True(st.Exists);
        Assert.False(st.Loaded);
        Assert.NotNull(st.Error);
        Assert.False(st.OverridesAllowedOrigins);
    }

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("\"texto\"")]
    public void NoEsObjeto(string text)
    {
        var st = LocalConfigInspector.InspectText("x", text);
        Assert.False(st.Loaded);
        Assert.Equal("el archivo no es un objeto JSON", st.Error);
    }

    [Fact]
    public void SinOrigenes()
    {
        var st = LocalConfigInspector.InspectText("x", """
            // comentario, como acepta la config de .NET
            { "Agent": { "Mock": true, "EvidenceDirectory": "D:\\Evidencia", }, "Logging": {} }
            """);
        Assert.True(st.Loaded);
        Assert.Null(st.Error);
        Assert.False(st.OverridesAllowedOrigins);
    }

    [Theory]
    [InlineData("{ \"Agent\": { \"AllowedOrigins\": [\"https://factum.example.com\"] } }")]
    [InlineData("{ \"agent\": { \"allowedorigins\": [] } }")]
    [InlineData("{ \"AGENT\": { \"allowedOrigins\": \"https://x\" } }")]
    public void ConOrigenesEnCualquierCasing(string text)
    {
        var st = LocalConfigInspector.InspectText("x", text);
        Assert.True(st.Loaded);
        Assert.True(st.OverridesAllowedOrigins);
    }

    [Fact]
    public void OrigenesFueraDeAgentNoCuentan()
    {
        var st = LocalConfigInspector.InspectText("x", "{ \"AllowedOrigins\": [\"https://x\"], \"Agent\": 3 }");
        Assert.True(st.Loaded);
        Assert.False(st.OverridesAllowedOrigins);
    }

    // D9/D10: la config local del perito nunca se toca, ni siquiera cuando está rota.
    [Theory]
    [InlineData("{ \"Agent\": { \"AllowedOrigins\": [\"https://ci.invalid\"] } }")]
    [InlineData("{ roto")]
    public void InspeccionarNoModificaElArchivo(string text)
    {
        var path = Path.Combine(_dir, "appsettings.Local.json");
        File.WriteAllText(path, text);
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var mtime = File.GetLastWriteTimeUtc(path);

        var st = LocalConfigInspector.Inspect(path);

        Assert.True(st.Exists);
        Assert.True(File.Exists(path));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(path));
        Assert.Single(Directory.GetFiles(_dir)); // no deja temporales ni copias
    }
}
