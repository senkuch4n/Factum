namespace Factum.Agent.Services.Linux;

/// <summary>
/// Campos de <c>/etc/os-release</c> que usa Tatana (SDD tatana-linux §5.2). <see cref="Id"/> va en minúsculas y,
/// como manda la especificación de os-release, vale <c>linux</c> si el archivo no lo trae.
/// </summary>
public sealed record OsRelease(string Id, string? VersionId, string? PrettyName, string? IdLike)
{
    public static readonly string[] DefaultPaths = ["/etc/os-release", "/usr/lib/os-release"];

    public static readonly OsRelease Unknown = new("linux", null, null, null);

    /// <summary>Puro: parsea el contenido (líneas <c>CLAVE=valor</c>, con o sin comillas; ignora comentarios).</summary>
    public static OsRelease Parse(string? content)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in (content ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            values[key] = Unquote(line[(eq + 1)..].Trim());
        }

        string? Get(string k) => values.TryGetValue(k, out var v) && v.Length > 0 ? v : null;
        var id = Get("ID")?.ToLowerInvariant() ?? "linux";
        return new OsRelease(id, Get("VERSION_ID"), Get("PRETTY_NAME"), Get("ID_LIKE"));
    }

    /// <summary>Lee el primero de <paramref name="paths"/> que exista (por defecto, <see cref="DefaultPaths"/>).</summary>
    public static OsRelease Read(Func<string, string?> readText, IEnumerable<string>? paths = null)
    {
        foreach (var p in paths ?? DefaultPaths)
        {
            string? text;
            try { text = readText(p); }
            catch { text = null; }
            if (text is not null) return Parse(text);
        }
        return Unknown;
    }

    // "Ubuntu 24.04.1 LTS" / 'x' / sin comillas. Desescapa \" \\ \$ \` como la especificación.
    private static string Unquote(string v)
    {
        if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[^1] == v[0])
        {
            var inner = v[1..^1];
            if (v[0] == '\'') return inner;
            var sb = new System.Text.StringBuilder(inner.Length);
            for (var i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '\\' && i + 1 < inner.Length && inner[i + 1] is '"' or '\\' or '$' or '`')
                {
                    sb.Append(inner[i + 1]);
                    i++;
                }
                else sb.Append(inner[i]);
            }
            return sb.ToString();
        }
        return v;
    }
}
