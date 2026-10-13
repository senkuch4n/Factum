using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Factum.Agent.Models;

namespace Factum.Agent.Services;

public interface IFileStorageService
{
    string NewFilePath(string prefix, string ext);
    IEnumerable<AgentFile> ListFiles();
    bool Delete(string filename);
    string DataDirectory { get; }

    // ── Vistas previas del paso 2 (wizard-concurrencia-pestanas §4.2, DT5) ────────────────────────────
    // Viven en <DataDirectory>/previews/, nunca en la raíz: ni GET /files ni la importación al caso las ven.

    /// <summary><c>&lt;DataDirectory&gt;/previews</c> (se crea en el ctor).</summary>
    string PreviewDirectory { get; }
    /// <summary>Ruta nueva <c>previews/preview_&lt;k&gt;_&lt;yyyyMMdd_HHmmss_fff&gt;.png</c>.</summary>
    string NewPreviewPath(string serial);
    /// <summary>Borra las vistas previas de ese serial salvo <paramref name="keepPath"/>. Errores de IO: log y sigue.</summary>
    void PrunePreviews(string serial, string keepPath);
    /// <summary>Vacía <c>previews/</c> (al arrancar). Devuelve cuántas entradas borró.</summary>
    int ClearPreviews();
    /// <summary>Lock por celular: cubre captura + poda, así dos pestañas no se borran la imagen a mitad.</summary>
    SemaphoreSlim PreviewGate(string serial);
    /// <summary>Ruta de una vista previa existente, o null si el nombre no es válido o no existe.</summary>
    string? ResolvePreview(string filename);
}

public sealed class AgentFile
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Url { get; set; } = string.Empty;
    public DateTime ModTime { get; set; }
}

public sealed partial class FileStorageService : IFileStorageService
{
    private readonly string _dir;
    private readonly string _previewDir;
    private readonly ILogger<FileStorageService> _log;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _previewGates = new(StringComparer.Ordinal);

    public FileStorageService(IOptions<AgentOptions> opts, ILogger<FileStorageService>? log = null)
    {
        _dir = Path.GetFullPath(opts.Value.DataDirectory);
        Directory.CreateDirectory(_dir);
        _previewDir = Path.Combine(_dir, "previews");
        Directory.CreateDirectory(_previewDir);
        _log = log ?? NullLogger<FileStorageService>.Instance;
    }

    public string DataDirectory => _dir;
    public string PreviewDirectory => _previewDir;

    /// <summary>Primeros 12 hex (minúsculas) del SHA-256 (UTF-8) del serial: el serial no va crudo al nombre.</summary>
    public static string PreviewKey(string serial) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(serial ?? "")))[..12];

    /// <summary><c>^preview_[0-9a-f]{12}_\d{8}_\d{6}_\d{3}\.png$</c> (no admite rutas ni otros prefijos).</summary>
    public static bool IsPreviewName(string? name) => name is not null && PreviewNameRegex().IsMatch(name);

    [GeneratedRegex(@"^preview_[0-9a-f]{12}_[0-9]{8}_[0-9]{6}_[0-9]{3}\.png\z", RegexOptions.CultureInvariant)]
    private static partial Regex PreviewNameRegex();

    public string NewPreviewPath(string serial)
    {
        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(_previewDir, $"preview_{PreviewKey(serial)}_{ts}.png");
    }

    public void PrunePreviews(string serial, string keepPath)
    {
        var keep = Path.GetFullPath(keepPath);
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(_previewDir, $"preview_{PreviewKey(serial)}_*.png").ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "No se pudo listar las vistas previas");
            return;
        }
        foreach (var f in files)
        {
            if (string.Equals(Path.GetFullPath(f), keep, StringComparison.Ordinal)) continue;
            if (!IsPreviewName(Path.GetFileName(f))) continue;
            try { File.Delete(f); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning(ex, "No se pudo borrar la vista previa {File}", Path.GetFileName(f));
            }
        }
    }

    public int ClearPreviews()
    {
        var n = 0;
        Directory.CreateDirectory(_previewDir);
        foreach (var entry in Directory.EnumerateFileSystemEntries(_previewDir).ToList())
        {
            try
            {
                if (Directory.Exists(entry) && !File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                    Directory.Delete(entry, recursive: true);
                else
                    File.Delete(entry); // archivo o enlace (se borra el enlace, no su destino)
                n++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning(ex, "No se pudo borrar {Entry} de previews/", Path.GetFileName(entry));
            }
        }
        return n;
    }

    public SemaphoreSlim PreviewGate(string serial) =>
        _previewGates.GetOrAdd(PreviewKey(serial), _ => new SemaphoreSlim(1, 1));

    public string? ResolvePreview(string filename)
    {
        if (!IsPreviewName(filename)) return null;
        var path = Path.Combine(_previewDir, filename);
        return File.Exists(path) ? path : null;
    }

    public string NewFilePath(string prefix, string ext)
    {
        // Milisegundos incluidos: con solo segundos, dos archivos generados en ráfaga (ej. las
        // marcas de una sesión de captura AirPlay) podían terminar con el mismo nombre y
        // pisarse entre sí.
        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        return Path.Combine(_dir, $"{prefix}_{ts}.{ext}");
    }

    public IEnumerable<AgentFile> ListFiles() =>
        Directory.EnumerateFiles(_dir)
            .Select(p =>
            {
                var fi = new FileInfo(p);
                return new AgentFile
                {
                    Name = fi.Name,
                    Size = fi.Length,
                    Url = $"/files/{fi.Name}",
                    ModTime = fi.LastWriteTimeUtc
                };
            });

    public bool Delete(string filename)
    {
        var path = Path.Combine(_dir, Path.GetFileName(filename));
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }
}
