using Factum.Backend.Infrastructure;
using Factum.Backend.Services.Cases;

namespace Factum.Backend.Tests;

/// <summary>Prechequeo de tope/espacio y formato de tamaños (subida-archivos-grandes §11 T2, T4).</summary>
public sealed class EvidenceUploadTests
{
    private const long Max = 4L * 1024 * 1024 * 1024;
    private const long MinFree = 1L * 1024 * 1024 * 1024;

    [Fact]
    public void Size_equal_to_max_passes() => Assert.Null(EvidenceUpload.Evaluate(Max, Max, null, MinFree));

    [Fact]
    public void Size_over_max_is_too_large()
    {
        var r = EvidenceUpload.Evaluate(Max + 1, Max, long.MaxValue, MinFree);
        Assert.NotNull(r);
        Assert.Equal(UploadRejectionKind.TooLarge, r.Kind);
        Assert.Equal(Max + 1, r.Size);
        Assert.Equal(Max, r.MaxUploadBytes);
    }

    [Fact]
    public void Unknown_free_space_passes() => Assert.Null(EvidenceUpload.Evaluate(1000, Max, null, MinFree));

    [Fact]
    public void Free_equal_to_size_plus_margin_passes() =>
        Assert.Null(EvidenceUpload.Evaluate(1000, Max, 1000 + MinFree, MinFree));

    [Fact]
    public void Free_one_byte_short_is_insufficient()
    {
        var r = EvidenceUpload.Evaluate(1000, Max, 1000 + MinFree - 1, MinFree);
        Assert.NotNull(r);
        Assert.Equal(UploadRejectionKind.InsufficientStorage, r.Kind);
        Assert.Equal(1000, r.Size);
        Assert.Equal(1000 + MinFree, r.RequiredBytes);
        Assert.Equal(1000 + MinFree - 1, r.AvailableBytes);
    }

    [Fact]
    public void Zero_size_passes() => Assert.Null(EvidenceUpload.Evaluate(0, Max, MinFree, MinFree));

    [Fact]
    public void Too_large_wins_over_insufficient_storage()
    {
        var r = EvidenceUpload.Evaluate(Max + 1, Max, 0, MinFree);
        Assert.Equal(UploadRejectionKind.TooLarge, r!.Kind);
    }

    [Theory]
    [InlineData(4294967296L, "4 GB")]
    [InlineData(5583457485L, "5,2 GB")]
    [InlineData(325058560L, "310 MB")]
    [InlineData(512L, "512 B")]
    [InlineData(0L, "0 B")]
    [InlineData(1536L, "1,5 KB")]
    [InlineData(1073741824L, "1 GB")]
    [InlineData(157286400L, "150 MB")]
    public void FormatBytes_uses_base_1024_and_comma(long bytes, string expected) =>
        Assert.Equal(expected, EvidenceUpload.FormatBytes(bytes));

    [Fact]
    public void Messages_use_formatted_sizes()
    {
        Assert.Equal("El archivo v.mp4 pesa 5,2 GB y el máximo permitido es 4 GB",
            EvidenceUpload.FileTooLargeMessage("v.mp4", 5583457485L, Max));
        Assert.Equal("No hay espacio en el disco del servidor para guardar v.mp4",
            EvidenceUpload.InsufficientStorageMessage("v.mp4", null, null));
    }

    // T4: chequeo de cordura del sondeo real en macOS/Linux.
    [Fact]
    public void DriveInfo_probe_returns_positive_free_space()
    {
        var free = new DriveInfoDiskSpaceProbe().GetAvailableFreeBytes(Path.GetTempPath());
        Assert.NotNull(free);
        Assert.True(free > 0);
    }
}
