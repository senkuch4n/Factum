using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services;

/// <summary>
/// Grabación en curso en <c>GET /agent/state</c> → <c>recordings[]</c> (grabacion-android-robusta §5.7, §6.5).
/// <c>State</c> ∈ <c>starting</c> | <c>recording</c> | <c>stopping</c>. En HTTP los null se omiten.
/// </summary>
public sealed record RecordingStateDto(string Platform, string Serial, string State, DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt, string? Filename, string? CaseId, bool? AndroidWithMic, string? IosMode);

/// <summary>Grabación detenida (ok/incomplete) que todavía está en la raíz de Tatana: <c>unclaimed_recordings[]</c>.</summary>
public sealed record UnclaimedRecordingDto(string Platform, string Serial, string Filename, string? CaseId,
    DateTimeOffset StoppedAt, RecordingValidationDto? Validation);

/// <summary>Fuente de grabaciones en curso (AdbService, IosService). Mismo filtro que su <c>ActiveOperations</c>.</summary>
public interface IRecordingStateSource
{
    IEnumerable<RecordingStateDto> ActiveRecordings { get; }
}

/// <summary>
/// Grabaciones detenidas que no se importaron (DA10). En memoria: se listan mientras el archivo siga en la
/// raíz y tengan menos de 24 h; como máximo 20, las más nuevas primero. Si Tatana se reinicia, se pierde la
/// lista (el archivo sigue en la raíz).
/// </summary>
public sealed class RecordingRegistry
{
    public const int MaxEntries = 20;
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private readonly string _root;
    private readonly TimeProvider _time;
    private readonly List<UnclaimedRecordingDto> _items = [];
    private readonly Lock _gate = new();

    public RecordingRegistry(IOptions<AgentOptions> opts, TimeProvider time)
    {
        _root = Path.GetFullPath(opts.Value.DataDirectory);
        _time = time;
    }

    /// <summary>Agrega (o reemplaza, si ya estaba ese <c>Filename</c>) y recorta a las 20 más nuevas.</summary>
    public void AddStopped(UnclaimedRecordingDto r)
    {
        lock (_gate)
        {
            _items.RemoveAll(x => string.Equals(x.Filename, r.Filename, StringComparison.Ordinal));
            _items.Add(r);
            _items.Sort((a, b) => b.StoppedAt.CompareTo(a.StoppedAt));
            if (_items.Count > MaxEntries) _items.RemoveRange(MaxEntries, _items.Count - MaxEntries);
        }
    }

    /// <summary>Las que siguen en la raíz y tienen menos de 24 h, las más nuevas primero.</summary>
    public IReadOnlyList<UnclaimedRecordingDto> Unclaimed()
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            // Las vencidas se sacan; las que no están en disco solo se ocultan (no se tocan archivos).
            _items.RemoveAll(x => now - x.StoppedAt >= MaxAge);
            return _items
                .Where(x => AgentFileNames.SafeChild(_root, x.Filename) is { } p && File.Exists(p))
                .ToList();
        }
    }
}
