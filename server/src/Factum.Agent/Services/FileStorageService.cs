using Microsoft.Extensions.Options;
using Factum.Agent.Models;

namespace Factum.Agent.Services;

public interface IFileStorageService
{
    string NewFilePath(string prefix, string ext);
    IEnumerable<AgentFile> ListFiles();
    bool Delete(string filename);
    string DataDirectory { get; }
}

public sealed class AgentFile
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Url { get; set; } = string.Empty;
    public DateTime ModTime { get; set; }
}

public sealed class FileStorageService : IFileStorageService
{
    private readonly string _dir;

    public FileStorageService(IOptions<AgentOptions> opts)
    {
        _dir = Path.GetFullPath(opts.Value.DataDirectory);
        Directory.CreateDirectory(_dir);
    }

    public string DataDirectory => _dir;

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
