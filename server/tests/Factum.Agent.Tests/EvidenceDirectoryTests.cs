using Factum.Agent.Common;
using Factum.Agent.Models;

namespace Factum.Agent.Tests;

/// <summary>Default de Agent:EvidenceDirectory (DP4) e IsSyncedFolder. Puro: no crea carpetas.</summary>
public sealed class EvidenceDirectoryTests
{
    [Fact]
    public void Windows_Default_IsSystemDriveFactumEvidencia()
    {
        Assert.Equal(@"C:\Factum\Evidencia",
            AgentOptions.ResolveEvidenceDirectory(null, "./agent-data", isWindows: true, "C:", @"C:\Users\perito"));
        Assert.Equal(@"D:\Factum\Evidencia",
            AgentOptions.ResolveEvidenceDirectory("  ", "./agent-data", isWindows: true, @"D:\", @"C:\Users\perito"));
        Assert.Equal(@"C:\Factum\Evidencia",
            AgentOptions.ResolveEvidenceDirectory(null, "./agent-data", isWindows: true, null, null));
    }

    [Fact]
    public void Unix_Default_IsHomeFactumEvidencia_NeverDocuments()
    {
        var dir = AgentOptions.ResolveEvidenceDirectory(null, "./agent-data", isWindows: false, null, "/Users/perito");
        Assert.Equal(Path.Combine("/Users/perito", "Factum", "Evidencia"), dir);
        Assert.DoesNotContain("Documents", dir);
        Assert.DoesNotContain("Mobile Documents", dir);
    }

    [Fact]
    public void Unix_WithoutProfile_FallsBackToDataDirectory()
    {
        var data = Path.Combine(Path.GetTempPath(), "factum-agent-data-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(Path.Combine(data, "evidencia"),
            AgentOptions.ResolveEvidenceDirectory(null, data, isWindows: false, null, ""));
    }

    [Fact]
    public void Configured_Wins_AndRelativeIsResolved()
    {
        Assert.Equal(Path.GetFullPath("zips"),
            AgentOptions.ResolveEvidenceDirectory("zips", "./agent-data", isWindows: false, null, "/home/x"));
        var abs = Path.Combine(Path.GetTempPath(), "factum-zip");
        Assert.Equal(Path.GetFullPath(abs),
            AgentOptions.ResolveEvidenceDirectory(abs, "./agent-data", isWindows: true, "C:", null));
    }

    [Fact]
    public void RealDefault_IsNotUnderDocuments()
    {
        var resolved = new AgentOptions().ResolveEvidenceDirectory();
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(docs))
            Assert.False(resolved.StartsWith(docs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith(Path.Combine("Factum", "Evidencia"), resolved.Replace('\\', Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData(@"C:\Users\perito\OneDrive\Factum\Evidencia", true)]
    [InlineData(@"C:\Users\perito\OneDrive - Empresa SA\Evidencia", true)]
    [InlineData("/Users/perito/Library/Mobile Documents/com~apple~CloudDocs/Evidencia", true)]
    [InlineData("/Users/perito/iCloud Drive/Evidencia", true)]
    [InlineData(@"C:\Factum\Evidencia", false)]
    [InlineData("/Users/perito/Factum/Evidencia", false)]
    [InlineData("/Users/perito/NoOneDrive/Evidencia", false)]
    [InlineData("", false)]
    public void IsSyncedFolder(string path, bool expected) =>
        Assert.Equal(expected, AgentFileNames.IsSyncedFolder(path));
}
