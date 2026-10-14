using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests.Session;

/// <summary>Nombres y prefijo reservado de los tramos (grabacion-sesion-completa §5.2).</summary>
public sealed class SessionNamesTests
{
    [Fact]
    public void SegmentName_TresDigitosYHoraLocal()
    {
        var name = SessionNames.SegmentName("pantalla", 1, new DateTime(2026, 10, 14, 14, 2, 10));
        Assert.Equal("sesion_pantalla_001_20261014_140210.mkv", name);
        Assert.True(SessionNames.IsSegmentName(name));
        Assert.True(SessionNames.IsSegmentName(name, "pantalla"));
        Assert.False(SessionNames.IsSegmentName(name, "camara"));
    }

    [Fact]
    public void SegmentName_CuatroDigitos()
    {
        var name = SessionNames.SegmentName("camara", 1234, new DateTime(2026, 1, 2, 3, 4, 5));
        Assert.Equal("sesion_camara_1234_20260102_030405.mkv", name);
        Assert.True(SessionNames.IsSegmentName(name));
    }

    [Theory]
    [InlineData("sesion_pantalla_01_20261014_140210.mkv")]
    [InlineData("sesion_pantalla_00001_20261014_140210.mkv")]
    [InlineData("sesion_audio_001_20261014_140210.mkv")]
    [InlineData("sesion_pantalla_001_20261014_140210.mp4")]
    [InlineData("sesion_pantalla_001_2026101_140210.mkv")]
    [InlineData("SESION_pantalla_001_20261014_140210.mkv")]
    [InlineData("sesion_pantalla_001_20261014_140210.mkv.part")]
    [InlineData("../sesion_pantalla_001_20261014_140210.mkv")]
    [InlineData(null)]
    public void IsSegmentName_Rechaza(string? name) => Assert.False(SessionNames.IsSegmentName(name));

    [Fact]
    public void SegmentName_IndiceFueraDeRango() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionNames.SegmentName("pantalla", 10000, DateTime.Now));

    [Theory]
    [InlineData("sesion_x.png", true)]
    [InlineData("Sesion_Notas.txt", true)]
    [InlineData("screenshot_1.png", false)]
    [InlineData("mi_sesion_1.png", false)]
    public void HasReservedPrefix(string name, bool expected) => Assert.Equal(expected, SessionNames.HasReservedPrefix(name));

    [Fact]
    public void RequireNewName_RechazaPrefijoReservado()
    {
        var ex = Assert.Throws<AgentHttpException>(() => CaseEvidenceStore.RequireNewName("sesion_x.png"));
        Assert.Equal(400, ex.Status);
        Assert.Equal("invalid_filename", ex.Code);
        Assert.Contains("reservado", ex.Message);
        Assert.Equal("screenshot_1.png", CaseEvidenceStore.RequireNewName("screenshot_1.png"));
    }

    [Fact]
    public void SessionOptions_InvalidosVuelvenAlDefault()
    {
        var o = new SessionOptions
        {
            ScreenBytesPerMinute = 0, MinSegmentMinutes = 10, MaxSegmentMinutes = 5, DiskCheckSeconds = 1,
            ResumeDelaySeconds = 100, ScreenProbeSeconds = 5, RotationRetrySeconds = 1, ShutdownBudgetSeconds = 500,
            MockSegmentSeconds = 3, MockFreeBytes = -1, CutReserveMinutes = 0, WarnMinutesLeft = -1,
        }.Normalize(null);
        Assert.Equal(SessionOptions.DefaultScreenBytesPerMinute, o.ScreenBytesPerMinute);
        Assert.Equal(5, o.MinSegmentMinutes);
        Assert.Equal(120, o.MaxSegmentMinutes);
        Assert.Equal(60, o.DiskCheckSeconds);
        Assert.Equal(3, o.ResumeDelaySeconds);
        Assert.Equal(30, o.ScreenProbeSeconds);
        Assert.Equal(60, o.RotationRetrySeconds);
        Assert.Equal(20, o.ShutdownBudgetSeconds);
        Assert.Equal(0, o.MockSegmentSeconds);
        Assert.Null(o.MockFreeBytes);
        Assert.Equal(2, o.CutReserveMinutes);
        Assert.Equal(60, o.WarnMinutesLeft);

        var off = new SessionOptions { ScreenProbeSeconds = 0, MockSegmentSeconds = 60 }.Normalize(null);
        Assert.Equal(0, off.ScreenProbeSeconds);
        Assert.Equal(60, off.MockSegmentSeconds);
    }
}
