using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Tests.Auth;

/// <summary>
/// <see cref="IUserRepository"/> en memoria (usuarios-locales §10.1 B25): un diccionario por <c>_id</c>,
/// DNI único, y devuelve copias (como una base real) para que un test no mute el "documento" guardado.
/// Con <see cref="ThrowOnUse"/> tira en cualquier llamada (para probar que algo no consulta la base).
/// </summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly Dictionary<string, UserAccount> _docs = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public bool ThrowOnUse { get; set; }
    public int Calls { get; private set; }
    public int EnsureIndexesCalls { get; private set; }

    public IReadOnlyList<UserAccount> All
    {
        get { lock (_lock) return _docs.Values.Select(Clone).ToList(); }
    }

    /// <summary>Copia del documento guardado (para asserts), o null.</summary>
    public UserAccount? Get(string dni)
    {
        lock (_lock) return _docs.Values.FirstOrDefault(u => u.Dni == dni) is { } u ? Clone(u) : null;
    }

    /// <summary>Inserta o pisa directamente (setup de tests).</summary>
    public void Seed(UserAccount user)
    {
        lock (_lock) _docs[user.Id] = Clone(user);
    }

    private void Touch()
    {
        Calls++;
        if (ThrowOnUse) throw new InvalidOperationException("El repositorio de users no debería usarse acá.");
    }

    private T Write<T>(string id, Func<UserAccount, T> apply, T missing)
    {
        Touch();
        lock (_lock) return _docs.TryGetValue(id, out var u) ? apply(u) : missing;
    }

    public Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        Touch();
        EnsureIndexesCalls++;
        return Task.CompletedTask;
    }

    public Task<UserAccount?> FindByDniAsync(string dni, CancellationToken ct = default)
    {
        Touch();
        return Task.FromResult(Get(dni));
    }

    public Task<UserAccount?> FindSessionByDniAsync(string dni, CancellationToken ct = default)
    {
        Touch();
        var u = Get(dni);
        if (u is not null) { u.PasswordHash = string.Empty; u.LastEmergencyResetHash = null; }
        return Task.FromResult(u);
    }

    public Task<UserAccount?> FindByIdAsync(string id, CancellationToken ct = default)
    {
        Touch();
        lock (_lock) return Task.FromResult(_docs.TryGetValue(id, out var u) ? Clone(u) : null);
    }

    public Task<List<UserAccount>> ListAsync(string? role = null, string? status = null, CancellationToken ct = default)
    {
        Touch();
        lock (_lock)
            return Task.FromResult(_docs.Values
                .Where(u => (role is null || u.Role == role) && (status is null || u.Status == status))
                .OrderBy(u => u.Name, StringComparer.Ordinal)
                .Select(u => { var c = Clone(u); c.PasswordHash = string.Empty; c.LastEmergencyResetHash = null; return c; })
                .ToList());
    }

    public Task<long> CountActiveSuperadminsAsync(CancellationToken ct = default)
    {
        Touch();
        lock (_lock)
            return Task.FromResult((long)_docs.Values.Count(u =>
                u.Role == UserRoles.Superadmin && u.Status == UserStatuses.Activo));
    }

    public Task<bool> TryInsertAsync(UserAccount user, CancellationToken ct = default)
    {
        Touch();
        lock (_lock)
        {
            if (_docs.ContainsKey(user.Id) || _docs.Values.Any(u => u.Dni == user.Dni))
                return Task.FromResult(false);
            _docs[user.Id] = Clone(user);
            return Task.FromResult(true);
        }
    }

    public Task<int> IncrementFailedLoginAsync(string id, DateTime now, CancellationToken ct = default) =>
        Task.FromResult(Write(id, u => { u.FailedLoginCount++; return u.FailedLoginCount; }, 0));

    public Task LockAsync(string id, DateTime until, DateTime now, CancellationToken ct = default) =>
        Task.FromResult(Write(id, u => { u.LockedUntil = until; u.FailedLoginCount = 0; return true; }, false));

    public Task RegisterSuccessfulLoginAsync(string id, DateTime now, CancellationToken ct = default) =>
        Task.FromResult(Write(id, u => { u.FailedLoginCount = 0; u.LockedUntil = null; u.LastLoginAt = now; return true; }, false));

    public Task UpdatePasswordHashAsync(string id, string hash, DateTime now, CancellationToken ct = default) =>
        Task.FromResult(Write(id, u => { u.PasswordHash = hash; return true; }, false));

    public Task<bool> SetPasswordAsync(string id, string hash, bool mustChange, DateTime changedAt, CancellationToken ct = default) =>
        Task.FromResult(Write(id, u =>
        {
            u.PasswordHash = hash;
            u.MustChangePassword = mustChange;
            u.PasswordChangedAt = changedAt;
            u.FailedLoginCount = 0;
            u.LockedUntil = null;
            u.UpdatedAt = changedAt;
            return true;
        }, false));

    // ── abm-clientes §4.4: misma semántica condicional que Mongo ─────────────

    public Task<UserAccount?> FindAdminViewByIdAsync(string id, CancellationToken ct = default)
    {
        Touch();
        lock (_lock)
        {
            if (!_docs.TryGetValue(id, out var u)) return Task.FromResult<UserAccount?>(null);
            var c = Clone(u);
            c.PasswordHash = string.Empty;
            c.LastEmergencyResetHash = null;
            return Task.FromResult<UserAccount?>(c);
        }
    }

    public Task<long> CountOtherActiveSuperadminsAsync(string excludedId, CancellationToken ct = default)
    {
        Touch();
        lock (_lock)
            return Task.FromResult((long)_docs.Values.Count(u =>
                u.Id != excludedId && u.Role == UserRoles.Superadmin && u.Status == UserStatuses.Activo));
    }

    public Task<bool> SuspendIfActiveAsync(string id, string byDni, string? reason, DateTime now, CancellationToken ct = default) =>
        Task.FromResult(Write(id, u =>
        {
            if (u.Status != UserStatuses.Activo) return false;
            u.Status = UserStatuses.Suspendido;
            u.SuspendedAt = now;
            u.SuspendedBy = byDni;
            u.SuspensionReason = reason;
            u.UpdatedAt = now;
            return true;
        }, false));

    public Task<bool> ReactivateIfSuspendedAsync(string id, DateTime now, CancellationToken ct = default) =>
        Task.FromResult(Write(id, u =>
        {
            if (u.Status != UserStatuses.Suspendido) return false;
            u.Status = UserStatuses.Activo;
            u.SuspendedAt = null;
            u.SuspendedBy = null;
            u.SuspensionReason = null;
            u.UpdatedAt = now;
            return true;
        }, false));

    public int EditableWrites { get; private set; }

    public Task<bool> UpdateEditableFieldsIfUnchangedAsync(string id, AccountEditableFields fields,
        DateTime expectedUpdatedAt, DateTime now, CancellationToken ct = default) =>
        Task.FromResult(Write(id, u =>
        {
            if (u.UpdatedAt != expectedUpdatedAt) return false;
            EditableWrites++;
            u.Name = fields.Name;
            u.Sigla = fields.Sigla;
            u.ContactPhone = fields.ContactPhone;
            u.ContactEmail = fields.ContactEmail;
            u.Organization = fields.Organization;
            u.Notes = fields.Notes;
            u.UpdatedAt = now;
            return true;
        }, false));

    public Task<bool> UnlockIfLockedAsync(string id, DateTime now, CancellationToken ct = default) =>
        Task.FromResult(Write(id, u =>
        {
            if (u.LockedUntil is not { } until || until <= now) return false;
            u.FailedLoginCount = 0;
            u.LockedUntil = null;
            u.UpdatedAt = now;
            return true;
        }, false));

    public Task<bool> ApplyEmergencyResetAsync(string id, string hash, string markerHash, DateTime changedAt,
        CancellationToken ct = default) =>
        Task.FromResult(Write(id, u =>
        {
            u.PasswordHash = hash;
            u.LastEmergencyResetHash = markerHash;
            u.MustChangePassword = true;
            u.PasswordChangedAt = changedAt;
            u.FailedLoginCount = 0;
            u.LockedUntil = null;
            u.Status = UserStatuses.Activo;
            u.SuspendedAt = null;
            u.SuspendedBy = null;
            u.SuspensionReason = null;
            u.UpdatedAt = changedAt;
            return true;
        }, false));

    public static UserAccount Clone(UserAccount u) => new()
    {
        Id = u.Id,
        Dni = u.Dni,
        Name = u.Name,
        Sigla = u.Sigla,
        Role = u.Role,
        Status = u.Status,
        PasswordHash = u.PasswordHash,
        MustChangePassword = u.MustChangePassword,
        PasswordChangedAt = u.PasswordChangedAt,
        FailedLoginCount = u.FailedLoginCount,
        LockedUntil = u.LockedUntil,
        CreatedAt = u.CreatedAt,
        UpdatedAt = u.UpdatedAt,
        CreatedBy = u.CreatedBy,
        SuspendedAt = u.SuspendedAt,
        SuspendedBy = u.SuspendedBy,
        LastLoginAt = u.LastLoginAt,
        LastEmergencyResetHash = u.LastEmergencyResetHash,
        ContactPhone = u.ContactPhone,
        ContactEmail = u.ContactEmail,
        Organization = u.Organization,
        Notes = u.Notes,
        SuspensionReason = u.SuspensionReason,
    };
}

/// <summary>Reloj controlable.</summary>
public sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public MutableTimeProvider() : this(new DateTimeOffset(2026, 10, 6, 12, 0, 0, 123, TimeSpan.Zero)) { }
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Envuelve <c>Pbkdf2PasswordHasher(1000)</c> y cuenta los Verify.</summary>
public sealed class CountingPasswordHasher : IPasswordHasher
{
    private readonly Pbkdf2PasswordHasher _inner = new(1000);
    public int VerifyCalls { get; private set; }
    public int HashCalls { get; private set; }
    public void Reset() { VerifyCalls = 0; HashCalls = 0; }

    public string Hash(string password) { HashCalls++; return _inner.Hash(password); }
    public bool Verify(string password, string encoded) { VerifyCalls++; return _inner.Verify(password, encoded); }
    public bool NeedsRehash(string encoded) => _inner.NeedsRehash(encoded);
}

/// <summary>Logger que guarda el mensaje ya formateado (y la excepción, si hay).</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];
    public IEnumerable<string> Messages => Entries.Select(e => e.Message);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var msg = formatter(state, exception);
        if (exception is not null) msg += " | " + exception;
        // También las propiedades estructuradas, por si una contraseña viajara como argumento.
        if (state is IEnumerable<KeyValuePair<string, object?>> props)
            msg += " | " + string.Join(";", props.Select(p => $"{p.Key}={p.Value}"));
        Entries.Add((logLevel, msg));
    }
}

/// <summary>Armado común de los servicios de auth local sobre los dobles.</summary>
public sealed class LocalAuthFixture
{
    public const string Secret = "secreto-de-prueba-de-al-menos-32-caracteres-xx";

    public InMemoryUserRepository Repo { get; } = new();
    public MutableTimeProvider Time { get; } = new();
    public CountingPasswordHasher Hasher { get; } = new();
    public LocalAuthOptions Options { get; }
    public AuthSettings Settings { get; }
    public CapturingLogger<LocalAuthProvider> ProviderLog { get; } = new();
    public LocalAuthProvider Provider { get; }
    public AuthService Auth { get; }
    public UserAccountService Accounts { get; }
    public SessionValidator Sessions { get; }

    public LocalAuthFixture(LocalAuthOptions? options = null)
    {
        Options = options ?? new LocalAuthOptions();
        Settings = new AuthSettings(AuthModes.Local, null, null, [], [], Options);
        Provider = new LocalAuthProvider(Repo, Hasher, Options, Time, ProviderLog);
        Auth = new AuthService(Provider, Microsoft.Extensions.Options.Options.Create(new JwtOptions { Secret = Secret, ExpiryHours = 8 }));
        Accounts = new UserAccountService(Settings, Repo, Hasher, Auth, Time);
        Sessions = new SessionValidator(Settings, Repo);
    }

    public DateTime Now => Time.GetUtcNow().UtcDateTime;

    /// <summary>Crea una cuenta con la contraseña dada (como el bootstrap: debe cambiarla).</summary>
    public async Task<UserAccount> CreateUserAsync(string dni = "30111222", string password = "temporal-123",
        string role = UserRoles.Cliente, bool mustChange = true, string status = UserStatuses.Activo)
    {
        var created = await Accounts.CreateAsync(new NewUserAccount(dni, "Ana Pérez", "AP", role, password), "test");
        Assert.True(created.IsSuccess, created.Error);
        var acc = Repo.Get(dni)!;
        acc.MustChangePassword = mustChange;
        acc.Status = status;
        Repo.Seed(acc);
        Hasher.Reset();
        return acc;
    }
}
