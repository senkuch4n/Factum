using Factum.Agent.Common;

namespace Factum.Agent.Tests;

/// <summary>Versión real de Tatana (tatana-instalador-autoupdate D12, D-T1, §5.5).</summary>
public sealed class AgentVersionTests
{
    [Theory]
    [InlineData("1.4.0+abc", "1.4.0")]
    [InlineData("1.4.0", "1.4.0")]
    [InlineData(" 1.4.0 ", "1.4.0")]
    [InlineData("0.0.0-dev+0123456789abcdef", "0.0.0-dev")]
    [InlineData(null, "0.0.0-dev")]
    [InlineData("", "0.0.0-dev")]
    [InlineData("   ", "0.0.0-dev")]
    [InlineData("+abc", "0.0.0-dev")]
    public void Normalize(string? informational, string expected) =>
        Assert.Equal(expected, AgentVersion.Normalize(informational));

    [Fact]
    public void Current_EsLaDelCsprojSinSha()
    {
        // Build local / de tests: <Version>0.0.0-dev</Version> y sin "+<sha>"
        // (IncludeSourceRevisionInInformationalVersion=false).
        Assert.Equal("0.0.0-dev", AgentVersion.Current);
    }
}
