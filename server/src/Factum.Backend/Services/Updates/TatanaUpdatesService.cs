using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Services.Updates;

public sealed class TatanaUpdatesOptions
{
    // API de GitLab para resolver el último tag/release (ej: "https://gitlab.com/api/v4")
    public string GitLabApiBaseUrl { get; set; } = "https://gitlab.com/api/v4";

    // Id o "namespace%2Fproyecto" (URL-encoded) del proyecto en GitLab
    public string ProjectId { get; set; } = string.Empty;

    // Base para armar URLs de "raw job artifacts" (ej: "https://gitlab.com/<grupo>/factum")
    public string ProjectRawBaseUrl { get; set; } = string.Empty;

    // Token de acceso de solo lectura — solo hace falta si el proyecto es privado
    public string? PrivateToken { get; set; }

    // URL pública de este mismo backend — el .bat portátil y el instalador
    // Electron bajan los archivos de ACÁ, nunca de GitLab directo (la PC del
    // fiscal puede no tener salida a internet, pero sí llega a este backend).
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";

    public int CacheMinutes { get; set; } = 5;
}

public interface ITatanaUpdatesService
{
    string PublicBaseUrl { get; }
    Task<string?> GetLatestTagAsync(CancellationToken ct);
    Task<HttpResponseMessage?> ProxyInstallerFileAsync(string filename, CancellationToken ct);
    Task<HttpResponseMessage?> ProxyPortableZipAsync(CancellationToken ct);
}

// Espeja los artifacts de la última release de GitLab (instalador Electron +
// ZIP portátil) para que la PC del fiscal nunca necesite salir a internet
// directo — solo habla con este backend, que sí tiene salida a GitLab.
// Mismo patrón de HttpClient inyectado que SupportService (integración Faro).
public sealed class TatanaUpdatesService : ITatanaUpdatesService
{
    private readonly HttpClient _http;
    private readonly TatanaUpdatesOptions _opts;
    private (string Tag, DateTime FetchedAt)? _cachedTag;

    public string PublicBaseUrl => _opts.PublicBaseUrl;

    public TatanaUpdatesService(IOptions<TatanaUpdatesOptions> opts, HttpClient http)
    {
        _opts = opts.Value;
        _http = http;
        _http.BaseAddress = new Uri(_opts.GitLabApiBaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrEmpty(_opts.PrivateToken))
            _http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", _opts.PrivateToken);
    }

    public async Task<string?> GetLatestTagAsync(CancellationToken ct)
    {
        if (_cachedTag is { } cached &&
            DateTime.UtcNow - cached.FetchedAt < TimeSpan.FromMinutes(_opts.CacheMinutes))
            return cached.Tag;

        try
        {
            var res = await _http.GetAsync(
                $"projects/{_opts.ProjectId}/releases?order_by=released_at&sort=desc&per_page=1", ct);
            if (!res.IsSuccessStatusCode) return _cachedTag?.Tag;

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync(ct));
            var releases = doc.RootElement.EnumerateArray();
            if (!releases.MoveNext()) return _cachedTag?.Tag;

            var tag = releases.Current.GetProperty("tag_name").GetString();
            if (tag is null) return _cachedTag?.Tag;

            _cachedTag = (tag, DateTime.UtcNow);
            return tag;
        }
        catch
        {
            // Sin conexión a GitLab — seguir sirviendo el último tag conocido (o null)
            return _cachedTag?.Tag;
        }
    }

    public async Task<HttpResponseMessage?> ProxyInstallerFileAsync(string filename, CancellationToken ct)
    {
        var tag = await GetLatestTagAsync(ct);
        if (tag is null) return null;
        return await FetchRawArtifactAsync(tag, "build-installer-win", $"agent-ui/dist/{filename}", ct);
    }

    public async Task<HttpResponseMessage?> ProxyPortableZipAsync(CancellationToken ct)
    {
        var tag = await GetLatestTagAsync(ct);
        if (tag is null) return null;
        return await FetchRawArtifactAsync(tag, "build-portable-win", $"Tatana-Portable-{tag}-Windows.zip", ct);
    }

    // Las URLs de "raw job artifacts" de GitLab no requieren pasar por la API
    // de Releases: alcanza con conocer el tag, el nombre del job (fijo, ver
    // .gitlab-ci.yml) y la ruta relativa que ese job publica como artifact.
    private async Task<HttpResponseMessage?> FetchRawArtifactAsync(
        string tag, string jobName, string relativePath, CancellationToken ct)
    {
        var url = $"{_opts.ProjectRawBaseUrl.TrimEnd('/')}/-/jobs/artifacts/{tag}/raw/{relativePath}?job={jobName}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(_opts.PrivateToken))
            req.Headers.Add("PRIVATE-TOKEN", _opts.PrivateToken);

        try
        {
            var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return res.IsSuccessStatusCode ? res : null;
        }
        catch
        {
            return null;
        }
    }
}
