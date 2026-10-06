using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Admin;
using Factum.Backend.Services.Auth;
using Factum.Backend.Tests.Auth;

namespace Factum.Backend.Tests.Admin;

/// <summary>Auditoría en memoria (abm-clientes B22): expone <see cref="All"/> y simula fallas de insert.</summary>
public sealed class InMemoryUserAdminEventRepository : IUserAdminEventRepository
{
    private readonly List<UserAdminEvent> _docs = [];
    private readonly object _lock = new();

    public bool ThrowOnUse { get; set; }
    /// <summary>Cantidad de próximos inserts que van a fallar.</summary>
    public int FailNextInserts { get; set; }
    public int InsertAttempts { get; private set; }

    public IReadOnlyList<UserAdminEvent> All { get { lock (_lock) return _docs.ToList(); } }

    private void Touch()
    {
        if (ThrowOnUse) throw new InvalidOperationException("La auditoría no debería usarse acá.");
    }

    public Task EnsureIndexesAsync(CancellationToken ct = default) { Touch(); return Task.CompletedTask; }

    public Task InsertAsync(UserAdminEvent evt, CancellationToken ct = default)
    {
        Touch();
        lock (_lock)
        {
            InsertAttempts++;
            if (FailNextInserts > 0)
            {
                FailNextInserts--;
                throw new InvalidOperationException("insert simulado que falla");
            }
            _docs.Add(evt);
        }
        return Task.CompletedTask;
    }

    public Task<List<UserAdminEvent>> ListByTargetAsync(string targetUserId, int offset, int limit,
        CancellationToken ct = default)
    {
        Touch();
        lock (_lock)
            return Task.FromResult(_docs
                .Where(e => e.TargetUserId == targetUserId)
                .OrderByDescending(e => e.At).ThenByDescending(e => e.Id, StringComparer.Ordinal)
                .Skip(offset).Take(limit + 1)
                .ToList());
    }
}

/// <summary>Lock con lease en memoria, atómico con <c>lock</c>. <see cref="Busy"/> simula "ocupado".</summary>
public sealed class InMemoryAdminLockRepository : IAdminLockRepository
{
    private readonly Dictionary<string, AdminLock> _docs = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public bool ThrowOnUse { get; set; }
    public bool Busy { get; set; }
    public int AcquireAttempts { get; private set; }
    public int Releases { get; private set; }
    public int Held { get { lock (_lock) return _docs.Count; } }

    public async Task<bool> TryAcquireAsync(string key, string owner, DateTime now, TimeSpan lease,
        CancellationToken ct = default)
    {
        if (ThrowOnUse) throw new InvalidOperationException("El lock no debería usarse acá.");
        await Task.Yield();
        lock (_lock)
        {
            AcquireAttempts++;
            if (Busy) return false;
            if (_docs.TryGetValue(key, out var cur) && cur.ExpiresAt > now) return false;
            _docs[key] = new AdminLock { Id = key, Owner = owner, ExpiresAt = now + lease };
            return true;
        }
    }

    public async Task ReleaseAsync(string key, string owner, CancellationToken ct = default)
    {
        await Task.Yield();
        lock (_lock)
        {
            Releases++;
            if (_docs.TryGetValue(key, out var cur) && cur.Owner == owner) _docs.Remove(key);
        }
    }
}

/// <summary>
/// <see cref="ICaseRepository"/> mínimo: solo el conteo de solo lectura. Cualquier otro método tira,
/// así un test detecta si el panel tocara los casos.
/// </summary>
public sealed class InMemoryCaseCounter : ICaseRepository
{
    public Dictionary<string, long> Counts { get; } = new(StringComparer.Ordinal);
    public bool ThrowOnUse { get; set; }
    public int CountCalls { get; private set; }

    public Task<Dictionary<string, long>> CountByOfficerDnisAsync(IReadOnlyCollection<string> dnis,
        CancellationToken ct = default)
    {
        if (ThrowOnUse) throw new InvalidOperationException("Los casos no deberían consultarse acá.");
        CountCalls++;
        return Task.FromResult(dnis.Where(Counts.ContainsKey).Distinct()
            .ToDictionary(d => d, d => Counts[d], StringComparer.Ordinal));
    }

    private static NotSupportedException No() => new("El panel de cuentas no usa este método de cases.");
    public Task<List<Case>> ListByOfficerAsync(string officerDni, CancellationToken ct = default) => throw No();
    public Task<Case?> FindByIdAsync(string id, CancellationToken ct = default) => throw No();
    public Task InsertAsync(Case cas, CancellationToken ct = default) => throw No();
    public Task UpdateStatusAsync(string id, CaseStatus status, CancellationToken ct = default) => throw No();
    public Task UpdateGeneratedAsync(string id, DateTime generatedAt, string? zipPassword, bool zipEncrypted,
        string? zipEncryption, string zipHash, string zipFilename, string pdfFilename, string reportHash,
        CancellationToken ct = default) => throw No();
    public Task AddFileSourceAsync(string id, string filename, string sourcePath, CancellationToken ct = default) => throw No();
    public Task<bool> UpdateCaseDataAsync(string id, CaseDataUpdate data, CancellationToken ct = default) => throw No();
    public Task<bool> UpdateReportTextsAsync(string id, ReportTexts texts, CancellationToken ct = default) => throw No();
    public Task<List<CaptureRole>?> UpsertCaptureRolesAsync(string id, IReadOnlyList<(string Filename, string? Role)> roles,
        CancellationToken ct = default) => throw No();
    public Task<List<Case>> ListCatalogSourcesAsync(string officerDni, CancellationToken ct = default) => throw No();
    public Task<bool> RegisterEvidenceAsync(string id, List<EvidenceItem> evidence, EvidenceHost host,
        List<FileSource> fileSources, CancellationToken ct = default) => throw No();
    public Task<bool> RemoveEvidenceAsync(string id, string filename, CancellationToken ct = default) => throw No();
    public Task<bool> SetPendingGenerationAsync(string id, PendingGeneration pending, CancellationToken ct = default) => throw No();
    public Task<bool> TryMarkGeneratingAsync(string id, string generationId, CancellationToken ct = default) => throw No();
    public Task CompleteAgentGenerationAsync(string id, DateTime generatedAt, string? zipPassword, string zipHash,
        string zipFilename, string pdfFilename, string reportHash, ZipLocation zipLocation,
        CancellationToken ct = default) => throw No();
    public Task MarkAgentGenerationFailedAsync(string id, CancellationToken ct = default) => throw No();
}

/// <summary>Temporal predecible y distinta en cada llamada (14 caracteres, cumple el mínimo de 10).</summary>
public sealed class FixedTemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    private int _n;
    public bool ThrowOnUse { get; set; }
    public List<string> Issued { get; } = [];

    public string Generate(int minLength)
    {
        if (ThrowOnUse) throw new InvalidOperationException("No debería generarse una temporal acá.");
        var v = $"tmpx-pass-{Interlocked.Increment(ref _n):D4}";
        Issued.Add(v);
        return v;
    }
}

/// <summary>Armado del panel sobre los dobles, reusando <see cref="LocalAuthFixture"/>.</summary>
public sealed class AdminFixture
{
    public const string OwnerDni = "20111111";
    public const string LeoDni = "20222222";
    public const string Pwd = "clave-de-siempre-1";

    public LocalAuthFixture Auth { get; }
    public InMemoryUserRepository Repo => Auth.Repo;
    public MutableTimeProvider Time => Auth.Time;
    public InMemoryUserAdminEventRepository Events { get; } = new();
    public InMemoryAdminLockRepository Locks { get; } = new();
    public InMemoryCaseCounter Cases { get; } = new();
    public FixedTemporaryPasswordGenerator Generator { get; } = new();
    public CapturingLogger<UserAdminService> Log { get; } = new();
    public AuthSettings Settings { get; }
    public UserAdminService Service { get; }

    public AdminFixture(AuthSettings? settings = null)
    {
        Auth = new LocalAuthFixture();
        Settings = settings ?? Auth.Settings;
        Service = new UserAdminService(Settings, Repo, Auth.Accounts, Events, Locks, Cases, Auth.Hasher,
            Generator, Time, Log);
    }

    public DateTime Now => Auth.Now;

    /// <summary>Cuenta sembrada con contraseña <see cref="Pwd"/> y sin cambio pendiente.</summary>
    public async Task<UserAccount> SeedAsync(string dni, string name, string role = UserRoles.Cliente,
        string createdBy = "bootstrap")
    {
        var r = await Auth.Accounts.CreateAsync(new NewUserAccount(dni, name, "", role, Pwd), createdBy);
        Assert.True(r.IsSuccess, r.Error);
        var acc = Repo.Get(dni)!;
        acc.MustChangePassword = false;
        Repo.Seed(acc);
        return Repo.Get(dni)!;
    }

    public async Task<(UserAccount Owner, UserAccount Leo)> SeedSuperadminsAsync() =>
        (await SeedAsync(OwnerDni, "Dueño", UserRoles.Superadmin), await SeedAsync(LeoDni, "Leo", UserRoles.Superadmin));

    public static AdminActor ActorFor(UserAccount acc) => new(acc.Dni, acc.Name, "10.0.0.7");

    public UserAccount Doc(string dni) => Repo.Get(dni)!;
}
