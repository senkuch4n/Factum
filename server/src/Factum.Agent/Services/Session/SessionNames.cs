using System.Globalization;
using System.Text.RegularExpressions;

namespace Factum.Agent.Services.Session;

/// <summary>
/// Nombres y rutas de la grabación de la sesión (grabacion-sesion-completa §5.2). Puro.
/// <para>
/// Tramo: <c>sesion_&lt;pista&gt;_&lt;NNN&gt;_&lt;yyyyMMdd&gt;_&lt;HHmmss&gt;.mkv</c>, con la fecha y la hora LOCALES de la PC al
/// iniciar el tramo y <c>NNN</c> correlativo por pista en todo el caso (3 dígitos mínimo). El prefijo <c>sesion_</c>
/// queda reservado: solo la sesión crea esos archivos, por rename interno.
/// </para>
/// </summary>
public static partial class SessionNames
{
    public const string Prefix = "sesion_";
    public const string TrackScreen = "pantalla";
    public const string TrackCamera = "camara";
    /// <summary>Carpeta de los tramos dentro del ZIP (<c>zip_folder</c>).</summary>
    public const string ZipFolder = "sesion";
    /// <summary><c>&lt;DataDirectory&gt;/sessions/</c>.</summary>
    public const string SessionsDirName = "sessions";
    public const string JournalFileName = "sesion.json";
    public const string JournalTmpFileName = "sesion.json.tmp";
    public const string UnreadableSuffix = ".ilegible-";
    public const int MaxIndex = 9999;

    public const string ReservedPrefixMessage =
        "El prefijo «sesion_» está reservado para la grabación de la sesión";

    [GeneratedRegex(@"^sesion_(pantalla|camara)_\d{3,4}_\d{8}_\d{6}\.mkv$", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentRegex();

    /// <summary><c>^sesion_(pantalla|camara)_\d{3,4}_\d{8}_\d{6}\.mkv$</c>.</summary>
    public static bool IsSegmentName(string? name) => name is not null && SegmentRegex().IsMatch(name);

    /// <summary>Igual que <see cref="IsSegmentName(string?)"/> y además de la pista indicada.</summary>
    public static bool IsSegmentName(string? name, string track) =>
        IsSegmentName(name) && name!.StartsWith(Prefix + track + "_", StringComparison.Ordinal);

    /// <summary>true si el nombre empieza con <c>sesion_</c> (sin distinguir mayúsculas): reservado.</summary>
    public static bool HasReservedPrefix(string? name) =>
        name is not null && name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsTrack(string? track) => track is TrackScreen or TrackCamera;

    /// <summary>Nombre final de un tramo. <paramref name="localStart"/> = hora local de la PC al iniciarlo.</summary>
    public static string SegmentName(string track, int index, DateTime localStart)
    {
        if (!IsTrack(track)) throw new ArgumentOutOfRangeException(nameof(track), track, "Pista desconocida");
        if (index is < 1 or > MaxIndex) throw new ArgumentOutOfRangeException(nameof(index), index, "Índice fuera de rango");
        return string.Create(CultureInfo.InvariantCulture,
            $"{Prefix}{track}_{index:000}_{localStart:yyyyMMdd}_{localStart:HHmmss}.mkv");
    }
}
