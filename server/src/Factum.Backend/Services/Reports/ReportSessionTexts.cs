using System.Globalization;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Textos exactos de la sección "Registro audiovisual de la recolección" (grabacion-sesion-completa §8.4). Puros, texto
/// plano (un párrafo por línea). Horas en la zona del informe con <c>dd/MM/yyyy HH:mm:ss</c>; si el fin es el mismo día
/// que el inicio, el fin va solo con <c>HH:mm:ss</c>. Las duraciones usan <see cref="ReportDeviceTexts.FormatDuration"/>.
/// </summary>
public static class ReportSessionTexts
{
    public const string TrackScreen = "pantalla";
    public const string TrackCamera = "camara";

    /// <summary>Motivo de un hueco o de un corte (informe y web).</summary>
    public static string ReasonText(string? reason) => reason switch
    {
        "cambio_de_paso" => "el perito salió del paso de captura",
        "retomado_otro_dia" => "el caso se retomó otro día",
        "desconexion" => "se desconectó el cable USB del dispositivo",
        "tatana_cerrado" => "Tatana se cerró o la estación de trabajo se reinició",
        "sin_espacio" => "quedaba poco espacio en el disco de la estación de trabajo",
        "camara_fallo" => "la cámara dejó de grabar",
        "detenido_por_perito" => "el perito detuvo la grabación",
        "fallo_grabacion" => "la herramienta de captura se interrumpió por un error",
        "rotacion" => "corte técnico entre tramos",
        _ => "motivo no informado",
    };

    public static string TrackLabel(string? track) => track == TrackCamera ? "Cámara" : "Pantalla";

    private static DateTime Local(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    /// <summary><c>dd/MM/yyyy HH:mm:ss</c> en la zona del informe.</summary>
    public static string Full(DateTime utc, TimeZoneInfo zone) =>
        Local(utc, zone).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary><c>HH:mm:ss</c> en la zona del informe.</summary>
    public static string Time(DateTime utc, TimeZoneInfo zone) =>
        Local(utc, zone).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Fin de un intervalo: solo la hora si cae el mismo día (local) que el inicio.</summary>
    public static string End(DateTime startUtc, DateTime endUtc, TimeZoneInfo zone) =>
        Local(startUtc, zone).Date == Local(endUtc, zone).Date ? Time(endUtc, zone) : Full(endUtc, zone);

    private static string Duration(double seconds) =>
        ReportDeviceTexts.FormatDuration((long)Math.Round(Math.Max(0, seconds), MidpointRounding.AwayFromZero));

    /// <summary><c>mm:ss</c> de un desplazamiento dentro del video (los minutos pueden pasar de 59).</summary>
    public static string MinuteSecond(double offsetSeconds)
    {
        var total = (long)Math.Floor(Math.Max(0, offsetSeconds));
        return string.Create(CultureInfo.InvariantCulture, $"{total / 60:00}:{total % 60:00}");
    }

    private static bool HasCamera(RecordingSession session, IReadOnlyList<SessionSegment> rows) =>
        session.Camera is not null || rows.Any(r => r.Track == TrackCamera);

    /// <summary>
    /// <c>{descripcionRegistroAudiovisual}</c>. <paramref name="rows"/> = los tramos guardados de la tabla, en su orden.
    /// <paramref name="sealedCount"/> = cuántos tienen sello (solo se usa con <paramref name="timestampsEnabled"/>).
    /// </summary>
    public static string Description(RecordingSession session, IReadOnlyList<SessionSegment> rows,
        DeviceDetails? deviceDetails, bool timestampsEnabled, int sealedCount, TimeZoneInfo zone)
    {
        var camera = HasCamera(session, rows);
        var lines = new List<string>
        {
            "Durante la recolección se grabó en video, de forma continua, la pantalla del dispositivo" +
            (camera ? ", y con una cámara de la estación de trabajo, el procedimiento realizado sobre el equipo" : "") +
            $". La grabación se guardó en tramos de hasta {session.SegmentMinutes} {(session.SegmentMinutes == 1 ? "minuto" : "minutos")}: cada tramo se cerró, se le " +
            "calculó su valor hash SHA-256 y se incorporó al caso apenas terminó. Los archivos se entregan sin edición ni " +
            "recodificación dentro del contenedor de la evidencia, en la carpeta \"sesion\".",
        };

        lines.Add(session.Platform == "ios"
            ? "La pantalla se capturó por USB a dos cuadros por segundo."
            : "La pantalla se capturó por USB con scrcpy, a un máximo de 1080 píxeles de alto" +
              (session.Audio == "device" ? ", con el audio interno del dispositivo" : "") +
              ". Para no perder imagen entre tramos, cada tramo nuevo comenzó antes de cerrar el anterior, por lo que " +
              "tramos consecutivos se superponen alrededor de un segundo.");

        var screen = rows.Where(r => r.Track != TrackCamera).ToList();
        var cams = rows.Where(r => r.Track == TrackCamera).ToList();

        // F2 (§6.4 punto 3): la cámara tiene sus propios cortes; la configuración sale de cada tramo (D-T4).
        if (camera) lines.Add(CameraParagraph(session, cams));

        if (rows.Count > 0)
        {
            var first = rows.Min(r => r.StartedAt);
            var last = rows.Max(r => r.StoppedAt);
            if (cams.Count > 0)
            {
                // F2 (§6.4 punto 4, D10 = A): total por pista con la unión de intervalos.
                lines.Add($"Se grabaron {Duration(CoveredSeconds(screen))} de pantalla en {screen.Count} tramos y " +
                          $"{Duration(CoveredSeconds(cams))} de cámara en {cams.Count} tramos, entre el " +
                          $"{Full(first, zone)} y el {End(first, last, zone)}.");
            }
            else
            {
                // QA #80 B-3: tiempo cubierto (unión de intervalos), sin contar dos veces los solapamientos de los cortes.
                var total = CoveredSeconds(screen.Count > 0 ? screen : rows);
                lines.Add($"Se grabaron {Duration(total)} en {screen.Count} tramos" +
                          $", entre el {Full(first, zone)} y el {End(first, last, zone)}.");
            }
        }

        if (timestampsEnabled)
            lines.Add($"{sealedCount} de {rows.Count} tramos cuentan con sello de tiempo propio, indicado en la tabla siguiente.");

        lines.Add(ClockLine(deviceDetails));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Párrafo de la cámara (§6.4 punto 3): una sola configuración, o varias agrupadas por tramos consecutivos de cámara
    /// con la misma {nombre, W×H, fps, audio}; siempre seguido de la frase de los cortes propios.
    /// </summary>
    internal static string CameraParagraph(RecordingSession session, IReadOnlyList<SessionSegment> cams)
    {
        var groups = new List<(SessionCamera Camera, List<int> Indexes)>();
        foreach (var seg in cams.OrderBy(c => c.Index))
        {
            var cfg = seg.Camera ?? session.Camera;
            if (cfg is null) continue;
            if (groups.Count > 0 && SameConfig(groups[^1].Camera, cfg)) groups[^1].Indexes.Add(seg.Index);
            else groups.Add((cfg, [seg.Index]));
        }
        if (groups.Count == 0 && session.Camera is { } header) groups.Add((header, []));

        string first;
        var distinct = groups.Select(g => g.Camera).Distinct(CameraConfigComparer.Instance).Count();
        if (groups.Count == 0)
            first = "Se registró en video el procedimiento sobre el equipo, en archivos separados de los de la pantalla.";
        else if (distinct == 1)
            first = $"Se registró en video el procedimiento sobre el equipo con la cámara «{groups[0].Camera.Name}» " +
                    $"({CameraSpec(groups[0].Camera)}), en archivos separados de los de la pantalla.";
        else
        {
            var parts = groups.Select(g =>
                $"con la cámara «{g.Camera.Name}» ({CameraSpec(g.Camera)}) en {SegmentRange(g.Indexes)}").ToList();
            first = "Se registró en video el procedimiento sobre el equipo, en archivos separados de los de la pantalla, " +
                    JoinList(parts) + ".";
        }
        var minutes = session.SegmentMinutes;
        return first + $" La cámara se grabó en tramos propios de hasta {minutes} {(minutes == 1 ? "minuto" : "minutos")}, " +
               "contados desde que empezó a grabar, y siguió grabando aunque se cortara la grabación de la pantalla. Para " +
               "cruzar los dos videos deben usarse las horas de inicio y fin de cada tramo y la ubicación de las marcas de " +
               "momento en cada video.";
    }

    private static string CameraSpec(SessionCamera c) =>
        $"{c.Width}×{c.Height}, {c.Fps} cuadros por segundo, " +
        (c.WithAudio ? "con el micrófono de la estación de trabajo" : "sin audio");

    private static string SegmentRange(List<int> indexes) =>
        indexes.Count == 1 ? $"el tramo de cámara {indexes[0]}" : $"los tramos de cámara {indexes[0]} a {indexes[^1]}";

    private static string JoinList(List<string> parts) =>
        parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " y " + parts[^1];

    private static bool SameConfig(SessionCamera a, SessionCamera b) =>
        a.Name == b.Name && a.Width == b.Width && a.Height == b.Height && a.Fps == b.Fps && a.WithAudio == b.WithAudio;

    private sealed class CameraConfigComparer : IEqualityComparer<SessionCamera>
    {
        public static readonly CameraConfigComparer Instance = new();
        public bool Equals(SessionCamera? a, SessionCamera? b) => a is null ? b is null : b is not null && SameConfig(a, b);
        public int GetHashCode(SessionCamera c) => HashCode.Combine(c.Name, c.Width, c.Height, c.Fps, c.WithAudio);
    }

    /// <summary>Segundos cubiertos por la unión de los intervalos [inicio, fin] de los tramos (los solapados cuentan una vez).</summary>
    public static double CoveredSeconds(IEnumerable<SessionSegment> rows)
    {
        double total = 0;
        DateTime? curStart = null, curEnd = null;
        foreach (var r in rows.OrderBy(r => r.StartedAt))
        {
            var end = r.StoppedAt < r.StartedAt ? r.StartedAt : r.StoppedAt;
            if (curEnd is { } ce && r.StartedAt <= ce)
            {
                if (end > ce) curEnd = end;
                continue;
            }
            if (curStart is { } cs) total += (curEnd!.Value - cs).TotalSeconds;
            curStart = r.StartedAt;
            curEnd = end;
        }
        if (curStart is { } last) total += (curEnd!.Value - last).TotalSeconds;
        return total;
    }

    /// <summary>Línea de correlación con el reloj del dispositivo (§6.8): la medición de inicio, o la de cierre si es la única.</summary>
    public static string ClockLine(DeviceDetails? d)
    {
        const string Base = "Las horas de esta sección corresponden al reloj de la estación de trabajo.";
        var (m, when) = d?.ClockStart is { DeviceMinusPcMs: not null } s ? (s, "inicio")
            : d?.ClockClose is { DeviceMinusPcMs: not null } c ? (c, "cierre")
            : ((ClockMeasurement?)null, "");
        if (m is null) return Base;
        return $"{Base} Al {when} del caso, {ReportDeviceTexts.DeviceSkewPast(m.DeviceMinusPcMs!.Value, m.MarginMs)}: " +
               "para comparar la hora que se ve en la pantalla del dispositivo dentro del video con las de esta sección, " +
               "debe tenerse en cuenta esa diferencia.";
    }

    /// <summary>Líneas extra de la fila de un tramo en la tabla (después del nombre).</summary>
    public static List<string> RowLines(SessionSegment row, TimestampView? timestamp, bool timestampsEnabled,
        TimeZoneInfo zone)
    {
        var lines = new List<string>
        {
            $"{TrackLabel(row.Track)} · tramo {row.Index} · {Full(row.StartedAt, zone)} a " +
            $"{End(row.StartedAt, row.StoppedAt, zone)} ({Duration(row.RecordedSeconds)})",
        };
        var result = row.Validation == "incomplete"
            ? $"Archivo incompleto: el video dura {Duration(row.DurationSeconds ?? 0)} de {Duration(row.RecordedSeconds)} grabados"
            : "Archivo completo";
        if (row.Recovered) result += " · recuperado tras el cierre de Tatana";
        lines.Add(result);
        if (timestampsEnabled) lines.Add(ReportTimestampTexts.FileLine(timestamp, zone));
        return lines;
    }

    private const string NotRecording = "no grababa en ese momento";

    /// <summary>"tramo i, minuto mm:ss" o "no grababa en ese momento" (§6.4, marcas en las dos pistas).</summary>
    public static string Location(int? index, double? offsetSeconds) =>
        index is { } i ? $"tramo {i}, minuto {MinuteSecond(offsetSeconds ?? 0)}" : NotRecording;

    /// <summary><c>{detalleRegistroAudiovisual}</c>: huecos, fin de la grabación y marcas.</summary>
    public static string Detail(RecordingSession session, TimeZoneInfo zone)
    {
        var lines = new List<string>();
        var gaps = session.Gaps.OrderBy(g => g.From).ToList();
        if (gaps.Count == 0)
        {
            lines.Add("Sin huecos: la grabación fue continua entre el primer y el último tramo.");
        }
        else
        {
            lines.Add("Huecos en la grabación:");
            foreach (var g in gaps)
            {
                var lost = g.SegmentLost ? " — el tramo en curso no se pudo recuperar" : "";
                lines.Add(g.To is { } to
                    ? $"• {Full(g.From, zone)} a {End(g.From, to, zone)} ({Duration((to - g.From).TotalSeconds)}) — " +
                      $"{TrackLabel(g.Track)}: {ReasonText(g.Reason)}{lost}"
                    : $"• Desde {Full(g.From, zone)} — {TrackLabel(g.Track)}: {ReasonText(g.Reason)}{lost} " +
                      "(la grabación no se reanudó)");
            }
        }

        var segments = session.Segments.OrderBy(s => s.StoppedAt).ToList();
        if (segments.Count > 0)
        {
            var last = segments[^1];
            var reason = segments.LastOrDefault(s => s.StopReason is not null and not "rotacion")?.StopReason;
            lines.Add($"La grabación terminó el {Full(last.StoppedAt, zone)}" +
                      (reason is null ? "." : $" ({ReasonText(reason)})."));
        }

        var marks = session.Marks.OrderBy(m => m.At).ToList();
        if (marks.Count > 0)
        {
            var camera = session.Camera is not null || session.Segments.Any(s => s.Track == TrackCamera) ||
                         marks.Any(m => m.CameraSegmentIndex is not null);
            lines.Add(camera
                ? "Marcas de momento (para ubicar hechos en los videos de la pantalla y de la cámara):"
                : "Marcas de momento (para ubicar hechos en el video de la pantalla):");
            foreach (var m in marks)
            {
                var what = !string.IsNullOrWhiteSpace(m.Text) ? m.Text!
                    : !string.IsNullOrWhiteSpace(m.Filename) ? "Captura " + m.Filename
                    : "Marca sin texto";
                if (camera)
                    lines.Add($"• {Time(m.At, zone)} — pantalla: {Location(m.SegmentIndex, m.OffsetSeconds)}; cámara: " +
                              $"{Location(m.CameraSegmentIndex, m.CameraOffsetSeconds)} — {what}");
                else if (m.SegmentIndex is { } index)
                    lines.Add($"• {Time(m.At, zone)} — tramo {index}, minuto {MinuteSecond(m.OffsetSeconds ?? 0)} del video — {what}");
                else
                    lines.Add($"• {Time(m.At, zone)} — pantalla: {NotRecording} — {what}");
            }
        }
        return string.Join("\n", lines);
    }
}
