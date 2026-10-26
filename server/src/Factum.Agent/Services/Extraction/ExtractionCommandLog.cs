using System.Globalization;
using System.Text;

namespace Factum.Agent.Services.Extraction;

/// <summary>
/// <c>registro/comandos.log</c> (extraccion-logica-android §5.3.1): cada comando de adb con la hora de la PC
/// (inicio y fin), los argumentos, el exit code y el stderr resumido. Append con flush; thread-safe por archivo.
/// </summary>
public sealed class ExtractionCommandLog(string folder)
{
    private readonly string _path = Path.Combine(folder, "registro", "comandos.log");
    private readonly object _gate = new();

    public void Log(string exe, IReadOnlyList<string> args, int exitCode, string stderrTail, DateTime startUtc,
        DateTime endUtc)
    {
        var line = new StringBuilder();
        line.Append(Iso(startUtc)).Append(" → ").Append(Iso(endUtc)).Append("  exit=").Append(exitCode).Append('\n');
        line.Append("  ").Append(exe).Append(' ').Append(string.Join(' ', args)).Append('\n');
        var err = stderrTail.Trim();
        if (err.Length > 0) line.Append("  stderr: ").Append(err.Length > 500 ? err[..500] : err).Append('\n');
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, line.ToString());
        }
    }

    private static string Iso(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
