using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Timestamping;

/// <summary>
/// Resultado de un pedido completo (todas las URLs en orden, sellado-tiempo §5.3). Si <see cref="Granted"/>,
/// vienen <see cref="TsaUrl"/> (saneada), <see cref="Data"/> y <see cref="Tsr"/>; si no, <see cref="Failure"/>.
/// </summary>
public sealed record TimestampAttemptResult(
    bool Granted,
    string? TsaUrl,
    TimestampData? Data,
    byte[]? Tsr,
    TimestampFailure? Failure)
{
    public static TimestampAttemptResult Ok(string tsaUrl, TimestampData data, byte[] tsr) =>
        new(true, tsaUrl, data, tsr, null);

    public static TimestampAttemptResult Fail(string kind, string reason) =>
        new(false, null, null, null, new TimestampFailure(kind, reason));
}

/// <summary>Pide un sello RFC 3161 sobre un SHA-256 ya calculado. Nunca lanza (salvo cancelación de <c>ct</c>).</summary>
public interface ITimestampAuthorityClient
{
    Task<TimestampAttemptResult> RequestAsync(string sha256, TimeSpan budget, CancellationToken ct);
}

/// <summary>
/// Cliente RFC 3161 (sellado-tiempo §5.1 y §5.3): recorre <c>Timestamping:Urls</c> en orden con un timeout por
/// URL acotado por el presupuesto total, y devuelve el primer sello válido. Un nonce y un pedido nuevos por URL.
/// Solo viaja el hash (32 bytes), nunca el contenido. El userinfo de la URL se manda como Basic y no sale nunca
/// en logs, en <c>TsaUrl</c> ni en el motivo del error (D-T6).
/// </summary>
public sealed class Rfc3161TimestampClient : ITimestampAuthorityClient
{
    private readonly HttpClient _http;
    private readonly TimestampingOptions.Settings _settings;
    private readonly ILogger<Rfc3161TimestampClient> _log;
    private readonly Func<byte[]> _nonce;
    private readonly TimeProvider _time;

    public Rfc3161TimestampClient(HttpClient http, TimestampingOptions.Settings settings,
        ILogger<Rfc3161TimestampClient> log, TimeProvider time)
        : this(http, settings, log, time, () => RandomNumberGenerator.GetBytes(8))
    {
    }

    /// <summary>Para los tests: nonce fijo.</summary>
    internal Rfc3161TimestampClient(HttpClient http, TimestampingOptions.Settings settings,
        ILogger<Rfc3161TimestampClient> log, TimeProvider time, Func<byte[]> nonce)
    {
        _http = http;
        _settings = settings;
        _log = log;
        _time = time;
        _nonce = nonce;
    }

    public async Task<TimestampAttemptResult> RequestAsync(string sha256, TimeSpan budget, CancellationToken ct)
    {
        byte[] hash;
        try { hash = Convert.FromHexString(sha256); }
        catch (FormatException) { return TimestampAttemptResult.Fail(TimestampErrorKinds.InvalidResponse, "hash inválido"); }
        if (hash.Length != 32)
            return TimestampAttemptResult.Fail(TimestampErrorKinds.InvalidResponse, "hash inválido");

        var watch = Stopwatch.StartNew();
        var reasons = new List<string>();
        bool anyInvalid = false, anyRejected = false, anyHttp = false;

        foreach (var url in _settings.Urls)
        {
            var remaining = budget - watch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                reasons.Add($"{TimestampingOptions.HostOf(url)}: sin tiempo para intentar");
                break;
            }
            var perUrl = remaining < _settings.Timeout ? remaining : _settings.Timeout;
            var (result, failure) = await TryOneAsync(url, hash, perUrl, ct);
            if (result is not null) return result;
            switch (failure!.Kind)
            {
                case TimestampErrorKinds.InvalidResponse: anyInvalid = true; break;
                case TimestampErrorKinds.Rejected: anyRejected = true; break;
                case TimestampErrorKinds.HttpError: anyHttp = true; break;
            }
            reasons.Add($"{TimestampingOptions.HostOf(url)}: {failure.Reason}");
        }

        var kind = anyInvalid ? TimestampErrorKinds.InvalidResponse
            : anyRejected ? TimestampErrorKinds.Rejected
            : anyHttp ? TimestampErrorKinds.HttpError
            : TimestampErrorKinds.Unreachable;
        var reason = reasons.Count == 0 ? "sin autoridades configuradas" : string.Join("; ", reasons);
        return TimestampAttemptResult.Fail(kind, Truncate(reason, 300));
    }

    private async Task<(TimestampAttemptResult? Ok, TimestampFailure? Failure)> TryOneAsync(Uri url, byte[] hash,
        TimeSpan timeout, CancellationToken ct)
    {
        var host = TimestampingOptions.HostOf(url);
        var clean = TimestampingOptions.SanitizeUrl(url);
        var request = Rfc3161TimestampRequest.CreateFromHash(hash, HashAlgorithmName.SHA256,
            requestedPolicyId: null, nonce: _nonce(), requestSignerCertificates: true);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, clean)
            {
                Content = new ByteArrayContent(request.Encode()),
            };
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
            msg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/timestamp-reply"));
            if (!string.IsNullOrEmpty(url.UserInfo))
            {
                var raw = Uri.UnescapeDataString(url.UserInfo);
                msg.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
            }

            using var resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var status = (int)resp.StatusCode;
            byte[]? body = null;
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                body = await ReadCappedAsync(resp, cts.Token);
                if (body is null)
                    return (null, new TimestampFailure(TimestampErrorKinds.InvalidResponse, "respuesta de más de 64 KiB"));
            }

            var v = TimestampResponseValidator.Validate(request, hash, body, status, url.Host);
            if (!v.IsValid)
            {
                _log.LogInformation("Sellado de tiempo: {Host} no dio un sello válido ({Kind}: {Reason})", host,
                    v.Failure!.Kind, v.Failure.Reason);
                return (null, v.Failure);
            }
            var data = v.Data!;
            var skew = data.GenTime - _time.GetUtcNow().UtcDateTime;
            if (skew.Duration() > TimeSpan.FromMinutes(5))
                _log.LogWarning("Sellado de tiempo: el reloj de {Host} difiere {Minutes:0} min del servidor", host,
                    skew.TotalMinutes);
            return (TimestampAttemptResult.Ok(clean, data, body!), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogInformation("Sellado de tiempo: {Host} no respondió a tiempo", host);
            return (null, new TimestampFailure(TimestampErrorKinds.Unreachable, "tiempo agotado"));
        }
        catch (HttpRequestException ex)
        {
            // Sin el mensaje de la excepción: puede repetir la URL.
            _log.LogInformation("Sellado de tiempo: sin conexión con {Host} ({Error})", host, ex.HttpRequestError);
            return (null, new TimestampFailure(TimestampErrorKinds.Unreachable, "sin conexión"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("Sellado de tiempo: error inesperado con {Host} ({Type})", host, ex.GetType().Name);
            return (null, new TimestampFailure(TimestampErrorKinds.Unreachable, "error de comunicación"));
        }
    }

    // null si se pasa de 64 KiB (se corta la lectura ahí).
    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.Content.Headers.ContentLength is > TimestampResponseValidator.MaxResponseBytes) return null;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > TimestampResponseValidator.MaxResponseBytes) return null;
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    internal static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
