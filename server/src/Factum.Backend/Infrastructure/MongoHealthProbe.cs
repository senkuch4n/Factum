using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Factum.Backend.Infrastructure;

/// <summary>
/// despliegue-nube DT3: ¿Mongo responde? Lo usa <c>GET /health/ready</c> (monitor externo y script de
/// deploy). <c>GET /health</c> sigue siendo liveness pura y no pasa por acá (D15).
/// </summary>
public interface IMongoHealthProbe
{
    /// <summary><c>true</c> si Mongo respondió el <c>ping</c> dentro del timeout. Nunca tira (salvo cancelación del request).</summary>
    Task<bool> PingAsync(CancellationToken ct);
}

/// <summary>
/// Cliente propio con timeouts cortos (los repositorios crean el suyo con los defaults del driver, que
/// esperan 30 s a elegir servidor). Si el ping falla, loguea solo el TIPO de excepción: el mensaje
/// puede traer el host o el usuario de la connection string. Como mucho un warning por minuto.
/// </summary>
public sealed class MongoHealthProbe : IMongoHealthProbe
{
    internal static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);

    private readonly IMongoDatabase _db;
    private readonly ILogger<MongoHealthProbe> _logger;
    private readonly TimeProvider _time;
    private long _lastWarningTicks = long.MinValue;

    public MongoHealthProbe(IOptions<MongoOptions> options, ILogger<MongoHealthProbe> logger)
        : this(options, logger, TimeProvider.System) { }

    internal MongoHealthProbe(IOptions<MongoOptions> options, ILogger<MongoHealthProbe> logger, TimeProvider time)
    {
        _logger = logger;
        _time = time;
        var opts = options.Value;
        var settings = MongoClientSettings.FromConnectionString(opts.ConnectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
        settings.ConnectTimeout = TimeSpan.FromSeconds(2);
        settings.SocketTimeout = TimeSpan.FromSeconds(3);
        _db = new MongoClient(settings).GetDatabase(opts.DatabaseName);
    }

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PingTimeout);
        try
        {
            await _db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // el cliente cortó el request: no es una falla de Mongo
        }
        catch (Exception ex)
        {
            WarnRateLimited(ex.GetType().Name);
            return false;
        }
    }

    private void WarnRateLimited(string exceptionType)
    {
        var now = _time.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastWarningTicks);
        if (last != long.MinValue && now - last < WarningInterval.Ticks) return;
        if (Interlocked.CompareExchange(ref _lastWarningTicks, now, last) != last) return;
        _logger.LogWarning("/health/ready: Mongo no respondió el ping ({ExceptionType})", exceptionType);
    }
}

/// <summary>Mapeo puro de <c>GET /health/ready</c> (Contrato compartido §5.2 de despliegue-nube).</summary>
public static class HealthReady
{
    public static (int StatusCode, object Body) ToResult(bool mongoOk) => mongoOk
        ? (StatusCodes.Status200OK, new { status = "ok", mongo = "ok" })
        : (StatusCodes.Status503ServiceUnavailable, new { status = "unavailable", mongo = "down" });
}
