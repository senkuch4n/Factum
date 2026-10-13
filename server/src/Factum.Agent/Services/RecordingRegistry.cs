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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, RecordingAttribution> _owners =
        new(StringComparer.Ordinal);

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

    // ── Atribución de la grabación en curso (wizard-concurrencia-pestanas DT4) ────────────────────────
    // El case_id ya lo guardan AdbService/IosService (grabacion-android-robusta §5.9); acá se suma la pestaña
    // (origin_id) que la inició, para que recording_stopped la lleve aunque el stop venga de otra pestaña, de
    // otra carga de página o por fuera. Tatana reiniciado = registro vacío (igual que la grabación).

    /// <summary>Guarda (o reemplaza) la atribución de la grabación de <paramref name="serial"/>. Se llama
    /// después de que el servicio confirmó el inicio.</summary>
    public void SetOwner(string serial, EventAttribution attribution) =>
        _owners[serial] = new RecordingAttribution(serial, attribution.CaseId, attribution.OriginId, _time.GetUtcNow());

    /// <summary>Lee y borra la atribución de <paramref name="serial"/> (null si no hay).</summary>
    public RecordingAttribution? TakeOwner(string serial) =>
        _owners.TryRemove(serial, out var a) ? a : null;

    /// <summary>Lee sin borrar.</summary>
    public RecordingAttribution? PeekOwner(string serial) =>
        _owners.TryGetValue(serial, out var a) ? a : null;
}

/// <summary>Quién inició una grabación en curso (DT4).</summary>
public sealed record RecordingAttribution(string Serial, string? CaseId, string? OriginId, DateTimeOffset Since);
