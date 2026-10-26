using System.Text.Json;
using System.Text.Json.Serialization;

namespace Factum.Agent.Services.Extraction;

/// <summary>
/// Hash fijo de la app auxiliar del registro de llamadas (extraccion-logica-android §7.5). Es el recurso
/// embebido <c>connector-lock.json</c> (ver <c>Factum.Agent.csproj</c>): la ÚNICA fuente del hash que Tatana
/// verifica antes de instalar. Mientras no exista la release firmada, el lock lleva <c>sha256: ""</c> y el
/// connector queda deshabilitado (<see cref="ConnectorReasons.NotReleased"/>).
/// </summary>
public sealed record ConnectorLock
{
    public const string ResourceName = "Factum.Agent.connector-lock.json";

    [JsonPropertyName("package")] public string Package { get; init; } = "app.factum.connector";
    [JsonPropertyName("version_name")] public string VersionName { get; init; } = "";
    [JsonPropertyName("version_code")] public int VersionCode { get; init; }
    /// <summary>64 hex del APK, o vacío si todavía no hay release firmada.</summary>
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
    /// <summary>64 hex del certificado firmante, o vacío.</summary>
    [JsonPropertyName("cert_sha256")] public string CertSha256 { get; init; } = "";
    [JsonPropertyName("release_tag")] public string ReleaseTag { get; init; } = "";
    [JsonPropertyName("asset")] public string Asset { get; init; } = "";

    /// <summary>true si ya hay un hash fijado (hay una release firmada publicada).</summary>
    [JsonIgnore] public bool HasPin => Sha256.Length == 64;

    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    /// <summary>Carga el lock embebido. Lanza si falta o no parsea (es un error de build, no de runtime).</summary>
    public static ConnectorLock Load()
    {
        using var stream = typeof(ConnectorLock).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"No se embebió el recurso {ResourceName}.");
        return Parse(stream);
    }

    /// <summary>Parsea un lock desde un stream (para tests y para <see cref="Load"/>).</summary>
    public static ConnectorLock Parse(Stream json) =>
        JsonSerializer.Deserialize<ConnectorLock>(json, Opts)
            ?? throw new InvalidOperationException("connector-lock.json vacío o nulo.");

    public static ConnectorLock Parse(string json) =>
        JsonSerializer.Deserialize<ConnectorLock>(json, Opts)
            ?? throw new InvalidOperationException("connector-lock.json vacío o nulo.");

    /// <summary>
    /// Decide si el APK encontrado (su SHA-256, o null si no está en el paquete) se puede usar.
    /// <paramref name="allowUnpinned"/> (Agent:Extraction:AllowUnpinnedConnector) SOLO se respeta en Development;
    /// permite un APK de desarrollo con otro hash y marca el resultado como no verificado.
    /// </summary>
    public ConnectorVerification Verify(string? actualApkSha256, bool isDevelopment, bool allowUnpinned)
    {
        var dev = isDevelopment && allowUnpinned;

        if (!HasPin)
        {
            // No hay release firmada todavía.
            if (dev && actualApkSha256 is not null)
                return new ConnectorVerification(true, null, Unverified: true);
            return new ConnectorVerification(false, ConnectorReasons.NotReleased, false);
        }

        if (actualApkSha256 is null)
            return new ConnectorVerification(false, ConnectorReasons.NotPackaged, false);

        if (string.Equals(actualApkSha256, Sha256, StringComparison.OrdinalIgnoreCase))
            return new ConnectorVerification(true, null, false);

        // El hash no coincide: solo se deja pasar en Development con la opción explícita.
        if (dev)
            return new ConnectorVerification(true, null, Unverified: true);

        return new ConnectorVerification(false, ConnectorReasons.HashMismatch, false);
    }
}

/// <summary>Resultado de <see cref="ConnectorLock.Verify"/>.</summary>
/// <param name="Available">true si el connector se puede instalar.</param>
/// <param name="Reason">Motivo si no está disponible (snake_case del contrato), o null.</param>
/// <param name="Unverified">true si se usa un APK sin verificar (modo desarrollo): va al ACTA.</param>
public sealed record ConnectorVerification(bool Available, string? Reason, bool Unverified);

/// <summary>Valores de <c>connector.reason</c> del preflight (extraccion-logica-android §8.2.1).</summary>
public static class ConnectorReasons
{
    public const string NotPackaged = "not_packaged";
    public const string NotReleased = "not_released";
    public const string HashMismatch = "hash_mismatch";
    public const string SdkTooOld = "sdk_too_old";
}
