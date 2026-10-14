using Factum.Backend.Infrastructure;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Timestamping;

/// <summary>
/// Cola de reintentos de sellos pendientes (sellado-tiempo §6.4). Cada vuelta espera <c>PollSeconds</c> o un
/// <see cref="ITimestampQueueSignal.Wake"/>, y reclama hasta <see cref="MaxClaimsPerRound"/> pendientes vencidos
/// con lease (seguro con varias réplicas, o con la cola y un pedido inline a la vez). Tres <c>unreachable</c>
/// seguidos cortan la vuelta (sin red) hasta la próxima.
/// </summary>
public sealed class TimestampRetryService : BackgroundService
{
    public const int MaxClaimsPerRound = 25;
    public const int MaxConsecutiveUnreachable = 3;
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(120);

    private readonly TimestampService _service;
    private readonly ICaseTimestampRepository _repo;
    private readonly ITimestampQueueSignal _signal;
    private readonly TimeProvider _time;
    private readonly ILogger<TimestampRetryService> _log;

    public TimestampRetryService(ITimestampService service, ICaseTimestampRepository repo,
        ITimestampQueueSignal signal, TimeProvider time, ILogger<TimestampRetryService> log)
    {
        _service = (TimestampService)service;
        _repo = repo;
        _signal = signal;
        _time = time;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_service.IsEnabled)
        {
            _log.LogInformation("Sellado de tiempo: deshabilitado; la cola no corre");
            return;
        }
        var poll = _service.Settings.Poll;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Cola de sellos: la vuelta falló; se reintenta en la próxima");
            }
            try
            {
                await _signal.WaitAsync(poll, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Una vuelta de la cola. Devuelve cuántos pendientes procesó.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var processed = 0;
        var unreachableInARow = 0;
        while (processed < MaxClaimsPerRound && !ct.IsCancellationRequested)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var claimed = await _repo.ClaimDueAsync(now, now + Lease, Guid.NewGuid().ToString("N"), ct);
            if (claimed is null) break;
            processed++;
            var kind = await _service.ProcessClaimedAsync(claimed);
            unreachableInARow = kind == TimestampErrorKinds.Unreachable ? unreachableInARow + 1 : 0;
            if (unreachableInARow >= MaxConsecutiveUnreachable)
            {
                _log.LogInformation("Cola de sellos: {N} autoridades sin respuesta seguidas; se corta la vuelta",
                    unreachableInARow);
                break;
            }
        }
        return processed;
    }
}
