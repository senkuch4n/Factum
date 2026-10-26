using Factum.Agent.Models;
using Factum.Agent.Services.Extraction;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Tests.Extraction;

public class DestinationServiceTests
{
    [Fact]
    public void IsUnderAllowedRoots_AceptaYRechaza()
    {
        var dir = Directory.CreateTempSubdirectory("factum-roots");
        try
        {
            var roots = new[] { dir.FullName };
            Assert.True(DestinationService.IsUnderAllowedRoots(dir.FullName, roots));
            Assert.True(DestinationService.IsUnderAllowedRoots(Path.Combine(dir.FullName, "sub", "x"), roots));
            Assert.False(DestinationService.IsUnderAllowedRoots(Path.GetTempPath(), roots));
            // Un hermano con prefijo de nombre no cuenta (dir + "X").
            Assert.False(DestinationService.IsUnderAllowedRoots(dir.FullName + "X", roots));
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData("FAT32", true)]
    [InlineData("vfat", true)]
    [InlineData("exfat", false)]
    [InlineData("ext4", false)]
    public void IsFat32(string fs, bool expected) => Assert.Equal(expected, DestinationService.IsFat32(fs));

    [Fact]
    public void Check_EscribibleYConEspacio_Ok()
    {
        var dir = Directory.CreateTempSubdirectory("factum-check");
        try
        {
            var svc = new DestinationService(Options.Create(new AgentOptions { MinFreeBytes = 1 }),
                new FakeDiskProbe(100L * 1024 * 1024 * 1024));
            var r = svc.Check(dir.FullName, requiredBytes: 1024, "samsung", "SM-M127F");
            Assert.True(r.Ok);
            Assert.True(r.Writable);
            Assert.Contains("Factum_Extraccion_samsung-SM_M127F", r.FinalFolderPreview);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Check_SinEspacio_InsufficientSpaceConMissing()
    {
        var dir = Directory.CreateTempSubdirectory("factum-check2");
        try
        {
            var svc = new DestinationService(Options.Create(new AgentOptions { MinFreeBytes = 1_000_000 }),
                new FakeDiskProbe(500));
            var r = svc.Check(dir.FullName, requiredBytes: 1024, null, null);
            Assert.False(r.Ok);
            Assert.Equal("insufficient_space", r.Reason);
            Assert.NotNull(r.MissingBytes);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Browse_FueraDeAllowedRoots_403()
    {
        var allowed = Directory.CreateTempSubdirectory("factum-allowed");
        try
        {
            var svc = new DestinationService(Options.Create(new AgentOptions
            { Extraction = new ExtractionOptions { AllowedRoots = [allowed.FullName] } }), new FakeDiskProbe(1));
            Assert.True(svc.Restricted);
            var ex = Assert.Throws<Factum.Agent.Services.AgentHttpException>(() => svc.Browse(Path.GetTempPath()));
            Assert.Equal(403, ex.Status);
        }
        finally { allowed.Delete(true); }
    }
}
