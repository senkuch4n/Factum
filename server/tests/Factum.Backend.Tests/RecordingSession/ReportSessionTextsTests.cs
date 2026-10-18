using Factum.Backend.Models;
using Factum.Backend.Services.Reports;

namespace Factum.Backend.Tests.SesionGrabacion;

/// <summary>Textos exactos de "Registro audiovisual de la recolección" (grabacion-sesion-completa §8.4, DU2=A).</summary>
public sealed class ReportSessionTextsTests
{
    private static readonly TimeZoneInfo Ar = TimeZoneInfo.CreateCustomTimeZone("AR", TimeSpan.FromHours(-3), "AR", "AR");
    // 14/10/2026 14:02:10 hora AR.
    private static readonly DateTime T0 = new(2026, 10, 14, 17, 2, 10, DateTimeKind.Utc);

    private static SessionSegment Seg(int i, double start, double seconds, string track = "pantalla",
        string validation = "ok", bool recovered = false, string? stopReason = "rotacion", double? duration = null) => new()
    {
        Id = i.ToString("x32"), Track = track, Index = i, Filename = $"sesion_{track}_{i:000}_20261014_140210.mkv",
        State = "saved", StartedAt = T0.AddSeconds(start), StoppedAt = T0.AddSeconds(start + seconds),
        RecordedSeconds = seconds, DurationSeconds = duration ?? seconds, Validation = validation, Recovered = recovered,
        StopReason = stopReason, Size = 1000, Sha256 = new string('a', 64),
    };

    private static Models.RecordingSession Session(params SessionSegment[] segs) => new()
    {
        Enabled = true, Platform = "android", Audio = "device", SegmentMinutes = 30, State = "stopped",
        Segments = [.. segs],
    };

    [Fact]
    public void Descripcion_AndroidSinCamaraConSelloYReloj()
    {
        var rows = new List<SessionSegment> { Seg(1, 0, 1800), Seg(2, 1800, 1500, stopReason: "cambio_de_paso") };
        var details = new DeviceDetails
        {
            ClockStart = new ClockMeasurement { Kind = "start", DeviceMinusPcMs = 10_200, MarginMs = 400 },
        };
        var lines = ReportSessionTexts.Description(Session([.. rows]), rows, details, timestampsEnabled: true,
            sealedCount: 1, Ar).Split('\n');
        Assert.Equal(
        [
            "Durante la recolección se grabó en video, de forma continua, la pantalla del dispositivo. La grabación se " +
            "guardó en tramos de hasta 30 minutos: cada tramo se cerró, se le calculó su valor hash SHA-256 y se " +
            "incorporó al caso apenas terminó. Los archivos se entregan sin edición ni recodificación dentro del " +
            "contenedor de la evidencia, en la carpeta \"sesion\".",
            "La pantalla se capturó por USB con scrcpy, a un máximo de 1080 píxeles de alto, con el audio interno del " +
            "dispositivo. Para no perder imagen entre tramos, cada tramo nuevo comenzó antes de cerrar el anterior, por " +
            "lo que tramos consecutivos se superponen alrededor de un segundo.",
            "Se grabaron 55 min en 2 tramos, entre el 14/10/2026 14:02:10 y el 14:57:10.",
            "1 de 2 tramos cuentan con sello de tiempo propio, indicado en la tabla siguiente.",
            "Las horas de esta sección corresponden al reloj de la estación de trabajo. Al inicio del caso, el " +
            "dispositivo adelantaba 10 s respecto de la estación de trabajo (± 1 s): para comparar la hora que se ve en " +
            "la pantalla del dispositivo dentro del video con las de esta sección, debe tenerse en cuenta esa diferencia.",
        ], lines);
    }

    [Fact]
    public void Descripcion_SinSelloSinRelojSinAudio_OtroDia()
    {
        var rows = new List<SessionSegment> { Seg(1, 0, 40000) };
        var s = Session([.. rows]);
        s.Audio = "none";
        var lines = ReportSessionTexts.Description(s, rows, null, timestampsEnabled: false, 0, Ar).Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("La pantalla se capturó por USB con scrcpy, a un máximo de 1080 píxeles de alto. Para", lines[1]);
        Assert.Equal("Se grabaron 11 h 6 min 40 s en 1 tramos, entre el 14/10/2026 14:02:10 y el 15/10/2026 01:08:50.", lines[2]);
        Assert.Equal("Las horas de esta sección corresponden al reloj de la estación de trabajo.", lines[3]);
    }

    [Fact]
    public void Descripcion_ConCamara()
    {
        var rows = new List<SessionSegment> { Seg(1, 0, 600), Seg(2, 1, 600, track: "camara") };
        var s = Session([.. rows]);
        s.Camera = new SessionCamera { Name = "Integrated Webcam", Width = 1280, Height = 720, Fps = 15 };
        var lines = ReportSessionTexts.Description(s, rows, null, false, 0, Ar).Split('\n');
        Assert.EndsWith("la pantalla del dispositivo, y con una cámara de la estación de trabajo, el procedimiento " +
                        "realizado sobre el equipo. La grabación se guardó en tramos de hasta 30 minutos: cada tramo se " +
                        "cerró, se le calculó su valor hash SHA-256 y se incorporó al caso apenas terminó. Los archivos " +
                        "se entregan sin edición ni recodificación dentro del contenedor de la evidencia, en la carpeta \"sesion\".",
            lines[0]);
        // F2 (§6.4): la configuración sale del tramo si la tiene, si no de la cabecera; sin "los mismos cortes".
        Assert.Equal("Se registró en video el procedimiento sobre el equipo con la cámara «Integrated Webcam» (1280×720, " +
                     "15 cuadros por segundo, sin audio), en archivos separados de los de la pantalla." +
                     " La cámara se grabó en tramos propios de hasta 30 minutos, contados desde que empezó a grabar, y siguió " +
                     "grabando aunque se cortara la grabación de la pantalla. Para cruzar los dos videos deben usarse las " +
                     "horas de inicio y fin de cada tramo y la ubicación de las marcas de momento en cada video.",
            lines[2]);
        Assert.Equal("Se grabaron 10 min de pantalla en 1 tramos y 10 min de cámara en 1 tramos, entre el 14/10/2026 " +
                     "14:02:10 y el 14:12:11.",
            lines[3]);
        Assert.DoesNotContain("mismos cortes", string.Join("\n", lines));
    }

    private static SessionCamera Cam(string name = "Cámara de la MacBook Air", int w = 1280, int h = 720, int fps = 15,
        bool audio = false) => new() { Name = name, Width = w, Height = h, Fps = fps, WithAudio = audio };

    [Fact]
    public void Descripcion_ConCamara_DosConfiguraciones_TotalesPorPistaConSolapamiento()
    {
        // Pantalla: 0–600 y 599–1200 (1 s de solapamiento) = 1200 s cubiertos. Cámara: 1 y 2 con la Mac, 3 con otra.
        var c1 = Seg(1, 0, 300, track: "camara"); c1.Camera = Cam();
        var c2 = Seg(2, 300, 300, track: "camara"); c2.Camera = Cam();
        var c3 = Seg(3, 700, 100, track: "camara"); c3.Camera = Cam("Integrated Webcam", 640, 480, 30, audio: true);
        var rows = new List<SessionSegment> { Seg(1, 0, 600), Seg(2, 599, 601), c1, c2, c3 };
        var s = Session([.. rows]);
        s.Camera = c3.Camera;
        var lines = ReportSessionTexts.Description(s, rows, null, false, 0, Ar).Split('\n');
        Assert.Equal("Se registró en video el procedimiento sobre el equipo, en archivos separados de los de la pantalla, " +
                     "con la cámara «Cámara de la MacBook Air» (1280×720, 15 cuadros por segundo, sin audio) en los tramos " +
                     "de cámara 1 a 2 y con la cámara «Integrated Webcam» (640×480, 30 cuadros por segundo, con el " +
                     "micrófono de la estación de trabajo) en el tramo de cámara 3." +
                     " La cámara se grabó en tramos propios de hasta 30 minutos, contados desde que empezó a grabar, y siguió " +
                     "grabando aunque se cortara la grabación de la pantalla. Para cruzar los dos videos deben usarse las " +
                     "horas de inicio y fin de cada tramo y la ubicación de las marcas de momento en cada video.",
            lines[2]);
        Assert.Equal("Se grabaron 20 min de pantalla en 2 tramos y 11 min 40 s de cámara en 3 tramos, entre el " +
                     "14/10/2026 14:02:10 y el 14:22:10.", lines[3]);
    }

    [Fact]
    public void Descripcion_TresGrupos_SeparadorComaY()
    {
        var segs = new[] { Cam("A"), Cam("B"), Cam("A") }.Select((c, i) =>
        {
            var seg = Seg(i + 1, i * 60, 60, track: "camara");
            seg.Camera = c;
            return seg;
        }).ToList();
        var text = ReportSessionTexts.CameraParagraph(Session([.. segs]), segs);
        Assert.StartsWith("Se registró en video el procedimiento sobre el equipo, en archivos separados de los de la " +
                          "pantalla, con la cámara «A» (1280×720, 15 cuadros por segundo, sin audio) en el tramo de cámara " +
                          "1, con la cámara «B» (1280×720, 15 cuadros por segundo, sin audio) en el tramo de cámara 2 y " +
                          "con la cámara «A» (1280×720, 15 cuadros por segundo, sin audio) en el tramo de cámara 3.", text);
    }

    [Fact]
    public void Descripcion_SinCamara_IdenticaAF1()
    {
        // Regresión: los textos aprobados en #80 no cambian si el caso no tiene cámara.
        var rows = new List<SessionSegment> { Seg(1, 0, 600), Seg(2, 599, 601) };
        var lines = ReportSessionTexts.Description(Session([.. rows]), rows, null, false, 0, Ar).Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.Equal("Se grabaron 20 min en 2 tramos, entre el 14/10/2026 14:02:10 y el 14:22:10.", lines[2]);
        Assert.DoesNotContain("cámara", string.Join("\n", lines));
    }

    [Fact]
    public void Reloj_SoloCierre()
    {
        var d = new DeviceDetails { ClockClose = new ClockMeasurement { Kind = "close", DeviceMinusPcMs = -3000, MarginMs = 200 } };
        Assert.Equal("Las horas de esta sección corresponden al reloj de la estación de trabajo. Al cierre del caso, el " +
                     "dispositivo atrasaba 3 s respecto de la estación de trabajo (± 1 s): para comparar la hora que se ve " +
                     "en la pantalla del dispositivo dentro del video con las de esta sección, debe tenerse en cuenta esa diferencia.",
            ReportSessionTexts.ClockLine(d));
        Assert.Equal("Las horas de esta sección corresponden al reloj de la estación de trabajo.",
            ReportSessionTexts.ClockLine(new DeviceDetails { ClockStart = new ClockMeasurement { Kind = "start" } }));
    }

    [Fact]
    public void Filas_CompletoIncompletoRecuperadoYSello()
    {
        var ok = ReportSessionTexts.RowLines(Seg(1, 0, 1800.7), null, timestampsEnabled: false, Ar);
        Assert.Equal(["Pantalla · tramo 1 · 14/10/2026 14:02:10 a 14:32:10 (30 min 1 s)", "Archivo completo"], ok);

        var inc = ReportSessionTexts.RowLines(Seg(2, 1800, 600, validation: "incomplete", recovered: true, duration: 540),
            new TimestampView("Free TSA", T0.AddMinutes(41), "01"), timestampsEnabled: true, Ar);
        Assert.Equal(
        [
            "Pantalla · tramo 2 · 14/10/2026 14:32:10 a 14:42:10 (10 min)",
            "Archivo incompleto: el video dura 9 min de 10 min grabados · recuperado tras el cierre de Tatana",
            "Sello de tiempo: 14/10/2026 14:43:10 (UTC-03:00) · Free TSA",
        ], inc);

        var sinSello = ReportSessionTexts.RowLines(Seg(3, 0, 60, track: "camara"), null, timestampsEnabled: true, Ar);
        Assert.StartsWith("Cámara · tramo 3", sinSello[0]);
        Assert.Equal("Sin sello de tiempo de un tercero al emitir este informe", sinSello[2]);
    }

    [Fact]
    public void Detalle_HuecosAbiertosYCerrados_Marcas()
    {
        var s = Session(Seg(1, 0, 1800), Seg(2, 1877, 600, stopReason: "cambio_de_paso"));
        s.Gaps =
        [
            new SessionGap { Id = "g2", Track = "pantalla", From = T0.AddSeconds(2477), Reason = "cambio_de_paso" },
            new SessionGap { Id = "g1", Track = "pantalla", From = T0.AddSeconds(1800), To = T0.AddSeconds(1877), Reason = "desconexion" },
            new SessionGap { Id = "g3", Track = "pantalla", From = T0.AddSeconds(100), To = T0.AddSeconds(120), Reason = "fallo_grabacion", SegmentLost = true },
        ];
        s.Marks =
        [
            new SessionMark { Id = "m2", At = T0.AddSeconds(590.5), SegmentIndex = 1, OffsetSeconds = 590.5, Filename = "screenshot_1.png", Kind = "captura" },
            new SessionMark { Id = "m1", At = T0.AddSeconds(513.6), SegmentIndex = 1, OffsetSeconds = 513.6, Text = "Abre el chat con Juan", Kind = "manual" },
            new SessionMark { Id = "m3", At = T0.AddSeconds(1900), SegmentIndex = 2, OffsetSeconds = 23, Kind = "manual" },
        ];
        Assert.Equal(
        [
            "Huecos en la grabación:",
            "• 14/10/2026 14:03:50 a 14:04:10 (20 s) — Pantalla: la herramienta de captura se interrumpió por un error — el tramo en curso no se pudo recuperar",
            "• 14/10/2026 14:32:10 a 14:33:27 (1 min 17 s) — Pantalla: se desconectó el cable USB del dispositivo",
            "• Desde 14/10/2026 14:43:27 — Pantalla: el perito salió del paso de captura (la grabación no se reanudó)",
            "La grabación terminó el 14/10/2026 14:43:27 (el perito salió del paso de captura).",
            "Marcas de momento (para ubicar hechos en el video de la pantalla):",
            "• 14:10:43 — tramo 1, minuto 08:33 del video — Abre el chat con Juan",
            "• 14:12:00 — tramo 1, minuto 09:50 del video — Captura screenshot_1.png",
            "• 14:33:50 — tramo 2, minuto 00:23 del video — Marca sin texto",
        ], ReportSessionTexts.Detail(s, Ar).Split('\n'));
    }

    [Fact]
    public void Detalle_SinHuecos_YMarcasConCamara()
    {
        var s = Session(Seg(1, 0, 60), Seg(2, 0, 60, track: "camara", stopReason: "detenido_por_perito"));
        s.Marks = [new SessionMark { Id = "m", At = T0.AddSeconds(5), SegmentIndex = 1, OffsetSeconds = 5, Text = "x", Kind = "manual" }];
        var lines = ReportSessionTexts.Detail(s, Ar).Split('\n');
        Assert.Equal("Sin huecos: la grabación fue continua entre el primer y el último tramo.", lines[0]);
        Assert.Equal("La grabación terminó el 14/10/2026 14:03:10 (el perito detuvo la grabación).", lines[1]);
        Assert.Equal("Marcas de momento (para ubicar hechos en los videos de la pantalla y de la cámara):", lines[2]);
        Assert.Equal("• 14:02:15 — pantalla: tramo 1, minuto 00:05; cámara: no grababa en ese momento — x", lines[3]);
        Assert.DoesNotContain("aproximadamente", string.Join("\n", lines));
    }

    [Fact]
    public void Detalle_MarcasEnLasDosPistas_YSoloCamara()
    {
        var s = Session(Seg(1, 0, 600), Seg(1, 0, 900, track: "camara"));
        s.Marks =
        [
            new SessionMark { Id = "m1", At = T0.AddSeconds(512.4), SegmentIndex = 1, OffsetSeconds = 512.4,
                CameraSegmentIndex = 1, CameraOffsetSeconds = 470.3, Text = "Abre el chat", Kind = "manual" },
            // [DU1=A] con el cable desconectado: solo ubicación en la cámara.
            new SessionMark { Id = "m2", At = T0.AddSeconds(773), CameraSegmentIndex = 1, CameraOffsetSeconds = 773,
                Text = "Desconecto el cable para cambiarlo", Kind = "manual" },
            new SessionMark { Id = "m3", At = T0.AddSeconds(800), CameraSegmentIndex = 1, CameraOffsetSeconds = 800,
                Filename = "foto_1.jpg", Kind = "captura" },
        ];
        var lines = ReportSessionTexts.Detail(s, Ar).Split('\n');
        Assert.Equal(
        [
            "Marcas de momento (para ubicar hechos en los videos de la pantalla y de la cámara):",
            "• 14:10:42 — pantalla: tramo 1, minuto 08:32; cámara: tramo 1, minuto 07:50 — Abre el chat",
            "• 14:15:03 — pantalla: no grababa en ese momento; cámara: tramo 1, minuto 12:53 — Desconecto el cable para cambiarlo",
            "• 14:15:30 — pantalla: no grababa en ese momento; cámara: tramo 1, minuto 13:20 — Captura foto_1.jpg",
        ], lines[^4..]);
    }

    [Theory]
    [InlineData("cambio_de_paso", "el perito salió del paso de captura")]
    [InlineData("retomado_otro_dia", "el caso se retomó otro día")]
    [InlineData("desconexion", "se desconectó el cable USB del dispositivo")]
    [InlineData("tatana_cerrado", "Tatana se cerró o la estación de trabajo se reinició")]
    [InlineData("sin_espacio", "quedaba poco espacio en el disco de la estación de trabajo")]
    [InlineData("camara_fallo", "la cámara dejó de grabar")]
    [InlineData("detenido_por_perito", "el perito detuvo la grabación")]
    [InlineData("fallo_grabacion", "la herramienta de captura se interrumpió por un error")]
    [InlineData("rotacion", "corte técnico entre tramos")]
    public void Motivos(string reason, string text) => Assert.Equal(text, ReportSessionTexts.ReasonText(reason));

    [Fact]
    public void Plural_UnMinuto()
    {
        var rows = new List<SessionSegment> { Seg(1, 0, 60) };
        var s = Session([.. rows]);
        s.SegmentMinutes = 1;
        Assert.Contains("en tramos de hasta 1 minuto: cada tramo",
            ReportSessionTexts.Description(s, rows, null, false, 0, Ar));
    }

    [Fact]
    public void Total_NoCuentaDosVecesElSolapamiento()
    {
        // Dos tramos de 60 s solapados 2 s y un hueco de 10 s: cubierto = 118 s.
        var rows = new List<SessionSegment> { Seg(1, 0, 60), Seg(2, 58, 60), Seg(3, 128, 0.5) };
        Assert.Equal(118.5, ReportSessionTexts.CoveredSeconds(rows), 3);
        var line = ReportSessionTexts.Description(Session([.. rows]), rows, null, false, 0, Ar).Split('\n')[2];
        Assert.StartsWith("Se grabaron 1 min 59 s en 3 tramos", line);
    }

    [Fact]
    public void MinutoDelVideo() => Assert.Equal("125:03", ReportSessionTexts.MinuteSecond(7503.9));
}
