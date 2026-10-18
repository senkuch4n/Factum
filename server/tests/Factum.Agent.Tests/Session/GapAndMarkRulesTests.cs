using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests.Session;

/// <summary>Reglas puras de huecos y marcas (grabacion-sesion-completa §5.4 paso 6 y §5.9).</summary>
public sealed class GapRulesTests
{
    private static readonly TimeZoneInfo Ar = TimeZoneInfo.CreateCustomTimeZone("AR", TimeSpan.FromHours(-3), "AR", "AR");

    private static List<SessionGapDto> Open(string reason, DateTime from) =>
        [new SessionGapDto { Id = "g", Track = "pantalla", From = from, Reason = reason }];

    [Fact]
    public void Cierra_ElHuecoAbierto()
    {
        var from = new DateTime(2026, 10, 14, 17, 0, 0, DateTimeKind.Utc);
        var gaps = Open("desconexion", from);
        var closed = GapRules.CloseOpenGap(gaps, "pantalla", from.AddSeconds(77), Ar);
        Assert.Equal(from.AddSeconds(77), closed!.To);
        Assert.Equal("desconexion", closed.Reason);
        Assert.Null(GapRules.CloseOpenGap(gaps, "pantalla", from.AddHours(1), Ar)); // ya no hay abierto
    }

    [Fact]
    public void CambioDePaso_OtroDiaLocal_RetomadoOtroDia()
    {
        // 14/10 23:30 local → 15/10 09:00 local.
        var from = new DateTime(2026, 10, 15, 2, 30, 0, DateTimeKind.Utc);
        var gaps = Open("cambio_de_paso", from);
        GapRules.CloseOpenGap(gaps, "pantalla", new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc), Ar);
        Assert.Equal("retomado_otro_dia", gaps[0].Reason);
    }

    [Fact]
    public void CambioDePaso_MismoDiaLocal_NoCambia()
    {
        // 14/10 22:00 local y 14/10 23:50 local (en UTC ya es el 15).
        var from = new DateTime(2026, 10, 15, 1, 0, 0, DateTimeKind.Utc);
        var gaps = Open("cambio_de_paso", from);
        GapRules.CloseOpenGap(gaps, "pantalla", new DateTime(2026, 10, 15, 2, 50, 0, DateTimeKind.Utc), Ar);
        Assert.Equal("cambio_de_paso", gaps[0].Reason);
    }

    [Fact]
    public void OtroMotivo_OtroDia_NoCambia()
    {
        var from = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        var gaps = Open("desconexion", from);
        GapRules.CloseOpenGap(gaps, "pantalla", from.AddDays(2), Ar);
        Assert.Equal("desconexion", gaps[0].Reason);
    }

    [Fact]
    public void OtraPista_NoSeToca()
    {
        var from = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        var gaps = Open("desconexion", from);
        Assert.Null(GapRules.CloseOpenGap(gaps, "camara", from.AddMinutes(1), Ar));
        Assert.Null(gaps[0].To);
    }
}

public sealed class MarkRulesTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 17, 0, 0, DateTimeKind.Utc);

    private static JournalSegment Seg(int index, DateTime start, DateTime? stop, string state = "saved") => new()
    {
        Id = index.ToString(), Index = index, Track = "pantalla", StartedAt = start, StoppedAt = stop, State = state,
    };

    [Fact]
    public void Locate_TramoYSegundo()
    {
        var segs = new[] { Seg(1, T0, T0.AddMinutes(30)) };
        var (i, off) = MarkRules.Locate(segs, T0.AddSeconds(513.64), T0.AddHours(1));
        Assert.Equal(1, i);
        Assert.Equal(513.6, off);
    }

    [Fact]
    public void Locate_Solapamiento_GanaElMasNuevo()
    {
        var segs = new[]
        {
            Seg(1, T0, T0.AddSeconds(1800.7)),
            Seg(2, T0.AddSeconds(1800), null, "recording"),
        };
        var (i, off) = MarkRules.Locate(segs, T0.AddSeconds(1800.3), T0.AddHours(1));
        Assert.Equal(2, i);
        Assert.Equal(0.3, off);
    }

    [Fact]
    public void Locate_TramoAbiertoHastaAhora()
    {
        var segs = new[] { Seg(1, T0, T0.AddMinutes(30)), Seg(2, T0.AddMinutes(40), null, "recording") };
        var (i, off) = MarkRules.Locate(segs, T0.AddMinutes(45), T0.AddMinutes(50));
        Assert.Equal(2, i);
        Assert.Equal(300, off);
    }

    private static JournalSegment Cam(int index, DateTime start, DateTime? stop, string state = "saved")
    {
        var s = Seg(index, start, stop, state);
        s.Track = "camara";
        return s;
    }

    // F2 (§5.7, D-T5): ubicación en la cámara, estricta (sin respaldo al último que empezó antes).
    [Fact]
    public void Locate_Camara_Estricta()
    {
        var segs = new[]
        {
            Seg(1, T0, T0.AddMinutes(30)),
            Cam(1, T0.AddSeconds(42.2), T0.AddSeconds(1842.2)),
            Cam(2, T0.AddSeconds(1842.2), null, "recording"),
        };
        Assert.Equal((1, 471.4), MarkRules.Locate(segs, T0.AddSeconds(513.64), T0.AddHours(1), "camara"));
        Assert.Equal((2, 0.0), MarkRules.Locate(segs, T0.AddSeconds(1842.2), T0.AddHours(1), "camara")); // límite: el nuevo
        Assert.Equal((2, 100.0), MarkRules.Locate(segs, T0.AddSeconds(1942.2), T0.AddHours(1), "camara"));
        Assert.Null(MarkRules.Locate(segs, T0.AddSeconds(10), T0.AddHours(1), "camara")); // antes de la cámara
        Assert.Null(MarkRules.Locate(segs, T0.AddHours(2), T0.AddHours(1), "camara")); // después de "ahora"
        // Pantalla, igual que antes.
        Assert.Equal((1, 513.6), MarkRules.Locate(segs, T0.AddSeconds(513.64), T0.AddHours(1), "pantalla"));
    }

    [Fact]
    public void Locate_Camara_HuecoYDescartados()
    {
        var segs = new[]
        {
            Cam(1, T0, T0.AddSeconds(60)),
            Cam(2, T0.AddSeconds(120), T0.AddSeconds(180), "discarded"),
        };
        Assert.Null(MarkRules.Locate(segs, T0.AddSeconds(90), T0.AddHours(1), "camara")); // en el hueco
        Assert.Null(MarkRules.Locate(segs, T0.AddSeconds(150), T0.AddHours(1), "camara")); // descartado
        Assert.Null(MarkRules.Locate([], T0, T0, "camara"));
        Assert.Null(MarkRules.Locate([], T0, T0, "pantalla"));
    }

    [Theory]
    [InlineData("  hola  ", "  hola")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void CleanText_Validos(string? input, string? expected)
    {
        Assert.Equal(expected, MarkRules.CleanText(input, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("con\nsalto")]
    [InlineData("con\u0007control")]
    [InlineData("bidi‮oculto")]
    public void CleanText_ConControles_Error(string input)
    {
        Assert.Null(MarkRules.CleanText(input, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void CleanText_MasDe300_Error()
    {
        Assert.Null(MarkRules.CleanText(new string('a', 301), out var error));
        Assert.NotNull(error);
        Assert.Equal(300, MarkRules.CleanText(new string('a', 300), out _)!.Length);
    }
}
