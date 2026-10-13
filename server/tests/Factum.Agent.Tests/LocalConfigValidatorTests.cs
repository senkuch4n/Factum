using System.Text;
using Factum.Agent.Common;
using Microsoft.Extensions.Configuration;

namespace Factum.Agent.Tests;

/// <summary>Valores inválidos en la config local → se ignora el archivo entero (tatana-ws-y-hosts §4.4, T-04).</summary>
public sealed class LocalConfigValidatorTests
{
    private static string? Validate(string json) =>
        LocalConfigValidator.Validate(new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build());

    [Theory]
    [InlineData("{\"Agent\":{\"MaxUploadBytes\":\"16GB\"}}", "Agent:MaxUploadBytes no es un número: '16GB'")]
    [InlineData("{\"Agent\":{\"MaxUploadBytes\":0}}", "Agent:MaxUploadBytes tiene que ser mayor que 0")]
    [InlineData("{\"Agent\":{\"MinFreeBytes\":-1}}", "Agent:MinFreeBytes tiene que ser mayor o igual que 0")]
    [InlineData("{\"Agent\":{\"Port\":\"ocho\"}}", "Agent:Port no es un número: 'ocho'")]
    [InlineData("{\"Agent\":{\"Port\":70000}}", "Agent:Port tiene que estar entre 1 y 65535")]
    [InlineData("{\"Agent\":{\"Mock\":\"si\"}}", "Agent:Mock tiene que ser true o false: 'si'")]
    [InlineData("{\"Agent\":{\"BindAddress\":\"mi-pc\"}}", "Agent:BindAddress inválido: 'mi-pc'. Usá localhost o una IP")]
    [InlineData("{\"Agent\":{\"AllowedHosts\":[\"*\"]}}", "Agent:AllowedHosts no admite \"*\": listá los hosts permitidos")]
    [InlineData("{\"Agent\":{\"AllowedOrigins\":[\"*\"]}}", "Agent:AllowedOrigins no admite \"*\": listá los orígenes permitidos")]
    [InlineData("{\"Agent\":{\"VideoPostprocess\":{\"TimeoutFactor\":\"x\"}}}", "Agent:VideoPostprocess:TimeoutFactor no es un número: 'x'")]
    [InlineData("{\"Agent\":{\"Recording\":{\"StayAwake\":\"quizas\"}}}", "Agent:Recording:StayAwake tiene que ser true o false: 'quizas'")]
    public void Invalido_NombraLaClave(string json, string expected) => Assert.Equal(expected, Validate(json));

    [Fact]
    public void OrigenConPath_EsInvalido()
    {
        var error = Validate("{\"Agent\":{\"AllowedOrigins\":[\"http://localhost:3000/app\"]}}");
        Assert.NotNull(error);
        Assert.StartsWith("Agent:AllowedOrigins: \"http://localhost:3000/app\" no es un origen válido", error);
        Assert.False(error!.EndsWith('.'));
    }

    [Fact]
    public void VariosErrores_SeUnenConPuntoYComa() =>
        Assert.Equal("Agent:Port no es un número: 'x'; Agent:MinFreeBytes tiene que ser mayor o igual que 0",
            Validate("{\"Agent\":{\"Port\":\"x\",\"MinFreeBytes\":-5}}"));

    [Fact]
    public void ValorLargo_SeCortaA60()
    {
        var error = Validate("{\"Agent\":{\"MaxUploadBytes\":\"" + new string('9', 30) + new string('x', 70) + "\"}}");
        Assert.Equal($"Agent:MaxUploadBytes no es un número: '{new string('9', 30) + new string('x', 30)}'", error);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"AllowedHosts\":\"*\"}")] // el de ASP.NET, en la raíz: se ignora
    [InlineData("{\"Agent\":3}")]
    [InlineData("{\"Agent\":{\"Mock\":true,\"Port\":8766,\"BindAddress\":\"0.0.0.0\",\"AllowedHosts\":[\"tatana.local\"],\"AllowedOrigins\":[\"https://x.example.com\"],\"MaxUploadBytes\":1,\"MinFreeBytes\":0}}")]
    [InlineData("{\"Agent\":{\"BindAddress\":\"\"}}")]
    [InlineData("{\"Agent\":{\"VideoPostprocess\":{\"TimeoutFactor\":-1}}}")] // valores: los corrige Normalize()
    [InlineData("{\"Agent\":{\"EvidenceDirectory\":\"D:\\\\Evidencia\",\"MicDevice\":null}}")]
    public void Valido(string json) => Assert.Null(Validate(json));
}
