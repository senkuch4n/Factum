using Factum.Agent.Common;

namespace Factum.Agent.Tests;

/// <summary>Nombres, carpeta del caso e ids (zip-local-informe-servidor §3.4, §6.2, §6.3).</summary>
public sealed class AgentFileNamesTests
{
    [Theory]
    [InlineData("screenshot_20261006_101500_123.png", true)]
    [InlineData("grabación ñ.mkv", true)]
    [InlineData("device_pull_chat.txt", true)]
    [InlineData("../x.png", false)]
    [InlineData("a/b.png", false)]
    [InlineData(@"a\b.png", false)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("a:b.png", false)]
    [InlineData("a?.png", false)]
    [InlineData("nombre.", false)]
    [InlineData("nombre ", false)]
    [InlineData("CON", false)]
    [InlineData("com1.tar.gz", false)]
    [InlineData("evidencia_1234.zip", false)]
    [InlineData("informe_pericial_1234.docx", false)]
    [InlineData("a\u0001.png", false)]
    public void IsValidName(string? name, bool expected) => Assert.Equal(expected, AgentFileNames.IsValidName(name));

    [Fact]
    public void IsValidName_MaxLength()
    {
        Assert.True(AgentFileNames.IsValidName(new string('a', 251) + ".png"));
        Assert.False(AgentFileNames.IsValidName(new string('a', 252) + ".png"));
    }

    [Theory]
    [InlineData("evidencia_1234_titular.zip", true)]
    [InlineData("evidencia_1234_2026_José.zip", true)]
    [InlineData("evidencia_.zip", false)]
    [InlineData("evidencia_a-b.zip", false)]
    [InlineData("evidencia_../x.zip", false)]
    [InlineData("otro_1234.zip", false)]
    [InlineData("evidencia_1234.ZIP", false)]
    public void IsZipFilename(string name, bool expected) => Assert.Equal(expected, AgentFileNames.IsZipFilename(name));

    [Fact]
    public void IsZipFilename_Max60()
    {
        Assert.True(AgentFileNames.IsZipFilename("evidencia_" + new string('a', 60) + ".zip"));
        Assert.False(AgentFileNames.IsZipFilename("evidencia_" + new string('a', 61) + ".zip"));
    }

    [Fact]
    public void CaseFolderName_SanitizesRef_AndUsesId8()
    {
        var id = "3f2a9c1e-0000-4000-8000-000000000001";
        Assert.Equal("1234_2026_3f2a9c1e", AgentFileNames.CaseFolderName("1234/2026", id));
        Assert.Equal("caso_3f2a9c1e", AgentFileNames.CaseFolderName("///", id));
        Assert.Equal("caso_3f2a9c1e", AgentFileNames.CaseFolderName(null, id));
        Assert.Equal("a_b_3f2a9c1e", AgentFileNames.CaseFolderName("../a\\b", id));
        Assert.Equal(new string('x', 60) + "_3f2a9c1e", AgentFileNames.CaseFolderName(new string('x', 80), id));
    }

    [Theory]
    [InlineData("3f2a9c1e-0000-4000-8000-000000000001", true)]
    [InlineData("3F2A9C1E-0000-4000-8000-000000000001", true)]
    [InlineData("3f2a9c1e000040008000000000000001", false)]
    [InlineData("../3f2a9c1e-0000-4000-8000-000000000001", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseCaseId(string? id, bool expected)
    {
        Assert.Equal(expected, AgentFileNames.TryParseCaseId(id, out var canonical));
        if (expected) Assert.Equal("3f2a9c1e-0000-4000-8000-000000000001", canonical);
    }

    [Fact]
    public void SafeChild_RejectsTraversal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "factum-safechild");
        Assert.Equal(Path.Combine(dir, "a.png"), AgentFileNames.SafeChild(dir, "a.png"));
        Assert.Null(AgentFileNames.SafeChild(dir, "../a.png"));
        Assert.Null(AgentFileNames.SafeChild(dir, "sub/a.png"));
    }
}
