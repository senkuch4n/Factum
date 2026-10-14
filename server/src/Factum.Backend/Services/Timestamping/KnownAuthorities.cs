using System.Security.Cryptography.X509Certificates;

namespace Factum.Backend.Services.Timestamping;

/// <summary>Autoridad conocida (sellado-tiempo §5.4): nombre legible y raíz oficial para el LEEME.</summary>
public sealed record KnownAuthority(
    string Host,
    string Name,
    /// <summary>Título de la variante del LEEME (§10).</summary>
    string ReadmeTitle,
    string RootUrl,
    /// <summary>true si la raíz se publica en DER y hay que convertirla a PEM.</summary>
    bool RootIsDer,
    string RootDerFileName,
    /// <summary>Ruta sugerida, relativa a la carpeta del caso (p. ej. <c>sellos/ca-freetsa.pem</c>).</summary>
    string CaFile);

/// <summary>Catálogo en código por host de la URL que respondió (sellado-tiempo §5.4).</summary>
public static class KnownAuthorities
{
    public static readonly IReadOnlyList<KnownAuthority> All =
    [
        new("freetsa.org", "Free TSA", "Free TSA (freetsa.org)", "https://freetsa.org/files/cacert.pem", false,
            "", "sellos/ca-freetsa.pem"),
        new("timestamp.digicert.com", "DigiCert", "DigiCert", "https://cacerts.digicert.com/DigiCertTrustedRootG4.crt",
            true, "DigiCertTrustedRootG4.crt", "sellos/ca-digicert.pem"),
    ];

    public static KnownAuthority? ForHost(string? host) =>
        host is null ? null : All.FirstOrDefault(a => string.Equals(a.Host, host, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Nombre legible: el del catálogo si el host es conocido; si no, el <c>O</c> del firmante, su <c>CN</c> o,
    /// en último caso, el host.
    /// </summary>
    public static string AuthorityFor(string? host, X509Certificate2? signer)
    {
        if (ForHost(host) is { } known) return known.Name;
        if (signer is not null)
        {
            if (RdnValue(signer.SubjectName, "O") is { Length: > 0 } o) return o;
            var cn = signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            if (!string.IsNullOrWhiteSpace(cn)) return cn.Trim();
        }
        return string.IsNullOrWhiteSpace(host) ? "Autoridad de sellado de tiempo" : host;
    }

    /// <summary>Archivo sugerido para la raíz: el del catálogo o <c>sellos/ca-&lt;host con . → -&gt;.pem</c>.</summary>
    public static string CaFileFor(string? host) =>
        ForHost(host)?.CaFile ?? $"sellos/ca-{(string.IsNullOrWhiteSpace(host) ? "autoridad" : host.ToLowerInvariant().Replace('.', '-'))}.pem";

    /// <summary>Host de una URL ya saneada (o null si no parsea).</summary>
    public static string? HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : null;

    // El primer RDN con ese tipo (OID corto: O = 2.5.4.10).
    private static string? RdnValue(X500DistinguishedName name, string shortName)
    {
        var oid = shortName switch { "O" => "2.5.4.10", "CN" => "2.5.4.3", _ => null };
        if (oid is null) return null;
        foreach (var rdn in name.EnumerateRelativeDistinguishedNames())
        {
            if (rdn.HasMultipleElements) continue;
            if (rdn.GetSingleElementType().Value == oid)
                return rdn.GetSingleElementValue()?.Trim();
        }
        return null;
    }
}
