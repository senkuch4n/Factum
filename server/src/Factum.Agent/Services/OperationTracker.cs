namespace Factum.Agent.Services;

/// <summary>
/// Una operación en curso de Tatana (SDD tatana-instalador-autoupdate D5, D-T10). <c>Kind</c> ∈
/// <c>recording_android</c> | <c>recording_ios</c> | <c>airplay_session</c> | <c>video_postprocess</c> |
/// <c>request</c>. <c>Detail</c> solo en <c>request</c>: <c>"POST /cases"</c> (nunca el id del caso ni la query).
/// </summary>
public sealed record AgentOperation(string Kind, DateTimeOffset Since, string? Detail = null);

/// <summary>Un servicio con sesiones largas que no pueden cortarse por una actualización.</summary>
public interface IOperationSource
{
    IEnumerable<AgentOperation> ActiveOperations { get; }
}

/// <summary>
/// Registro de operaciones en curso + modo mantenimiento atómico (§5.3). Cuenta las requests
/// mutantes en vuelo (que pasan por el middleware de <c>Program.cs</c>) y pregunta a las
/// <see cref="IOperationSource"/> por las sesiones largas. <see cref="TryEnterMaintenance"/> decide
/// "está libre → entro" bajo el mismo lock con el que entran las requests: no hay carrera entre
/// "está libre" y "lo detengo". Desde ahí toda request mutante nueva se rechaza (503) hasta
/// <see cref="ExitMaintenance"/> o hasta que vence el TTL.
/// </summary>
public sealed class OperationTracker(IEnumerable<IOperationSource> sources, TimeProvider time)
{
    public const string KindRequest = "request";
    public const string MaintenancePath = "/agent/maintenance";

    private readonly IOperationSource[] _sources = sources.ToArray();
    private readonly object _gate = new();
    private readonly Dictionary<long, AgentOperation> _requests = [];
    private long _nextId;
    private DateTimeOffset? _maintenanceUntil;

    /// <summary>Si el método es mutante (POST/PUT/PATCH/DELETE) y la ruta no es la de mantenimiento.</summary>
    public static bool IsTracked(string method, string path) =>
        (HttpMethodsEq(method, "POST") || HttpMethodsEq(method, "PUT") || HttpMethodsEq(method, "PATCH") ||
         HttpMethodsEq(method, "DELETE")) &&
        !string.Equals(path.TrimEnd('/'), MaintenancePath, StringComparison.OrdinalIgnoreCase);

    private static bool HttpMethodsEq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>"POST /cases"</c>: método y primer segmento de la ruta, sin ids ni query.</summary>
    internal static string DescribeRequest(string method, string path)
    {
        var segment = path.TrimStart('/').Split('/', 2)[0];
        var q = segment.IndexOfAny(['?', '#']);
        if (q >= 0) segment = segment[..q];
        return $"{method.ToUpperInvariant()} /{segment}";
    }

    /// <summary>
    /// Middleware: false si Tatana está en mantenimiento (→ 503 <c>agent_updating</c>). Si da true,
    /// <paramref name="token"/> hay que disponerlo al terminar la request (baja el contador).
    /// </summary>
    public bool TryEnterRequest(string method, string path, out IDisposable? token)
    {
        lock (_gate)
        {
            if (InMaintenanceLocked())
            {
                token = null;
                return false;
            }
            var id = ++_nextId;
            _requests[id] = new AgentOperation(KindRequest, time.GetUtcNow(), DescribeRequest(method, path));
            token = new RequestToken(this, id);
            return true;
        }
    }

    private void ExitRequest(long id)
    {
        lock (_gate) _requests.Remove(id);
    }

    /// <summary>Requests en vuelo + sesiones de las fuentes, ordenadas por inicio.</summary>
    public IReadOnlyList<AgentOperation> Snapshot()
    {
        lock (_gate) return SnapshotLocked();
    }

    private List<AgentOperation> SnapshotLocked()
    {
        var list = new List<AgentOperation>(_requests.Values);
        foreach (var s in _sources)
        {
            try { list.AddRange(s.ActiveOperations); }
            catch (Exception) { /* una fuente rota no puede trabar la actualización ni el estado */ }
        }
        list.Sort((a, b) => a.Since.CompareTo(b.Since));
        return list;
    }

    public bool IsBusy => Snapshot().Count > 0;

    /// <summary>
    /// Atómico: si hay operaciones en curso devuelve false y la lista en <paramref name="busy"/>;
    /// si no, entra en mantenimiento por <paramref name="ttl"/> y devuelve la expiración.
    /// Repetirlo estando en mantenimiento renueva el TTL.
    /// </summary>
    public bool TryEnterMaintenance(TimeSpan ttl, out DateTimeOffset expiresAt, out IReadOnlyList<AgentOperation> busy)
    {
        lock (_gate)
        {
            var ops = SnapshotLocked();
            if (ops.Count > 0)
            {
                busy = ops;
                expiresAt = default;
                return false;
            }
            busy = [];
            expiresAt = time.GetUtcNow() + ttl;
            _maintenanceUntil = expiresAt;
            return true;
        }
    }

    public void ExitMaintenance()
    {
        lock (_gate) _maintenanceUntil = null;
    }

    /// <summary>false si nunca entró, si salió o si venció el TTL.</summary>
    public bool InMaintenance
    {
        get { lock (_gate) return InMaintenanceLocked(); }
    }

    private bool InMaintenanceLocked()
    {
        if (_maintenanceUntil is not { } until) return false;
        if (time.GetUtcNow() < until) return true;
        _maintenanceUntil = null;
        return false;
    }

    private sealed class RequestToken(OperationTracker owner, long id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.ExitRequest(id);
        }
    }
}
