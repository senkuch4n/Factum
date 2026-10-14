using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Timestamping;

/// <summary>Datos legibles de un sello válido (sellado-tiempo §5.2, paso 7).</summary>
public sealed record TimestampData(
    DateTime GenTime,
    string SerialNumber,
    string PolicyOid,
    string SignerSubject,
    string Authority);

/// <summary>Motivo de un intento fallido: <c>kind</c> de <see cref="TimestampErrorKinds"/> y texto corto.</summary>
public sealed record TimestampFailure(string Kind, string Reason);

/// <summary>Resultado de <see cref="TimestampResponseValidator.Validate"/>: exactamente uno de los dos es no null.</summary>
public sealed record TimestampValidation(TimestampData? Data, TimestampFailure? Failure)
{
    public bool IsValid => Data is not null;
    public static TimestampValidation Ok(TimestampData d) => new(d, null);
    public static TimestampValidation Fail(string kind, string reason) => new(null, new TimestampFailure(kind, reason));
}

/// <summary>
/// Validación pura de una respuesta de TSA (sellado-tiempo §5.2). No construye cadena contra un almacén de
/// confianza (D-T5): garantiza estado, imprint, nonce, certificados, firma + EKU (VerifySignatureForHash) y
/// algoritmo. La confianza en la raíz la establece el tercero con openssl (LEEME).
/// </summary>
public static class TimestampResponseValidator
{
    public const int MaxResponseBytes = 64 * 1024;
    public const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    public static TimestampValidation Validate(Rfc3161TimestampRequest request, byte[] hash, byte[]? body,
        int httpStatus = 200, string? host = null)
    {
        // 1. HTTP.
        if (httpStatus != 200)
            return TimestampValidation.Fail(TimestampErrorKinds.HttpError, $"HTTP {httpStatus}");
        // 2. Tamaño.
        if (body is null || body.Length == 0)
            return TimestampValidation.Fail(TimestampErrorKinds.InvalidResponse, "respuesta vacía");
        if (body.Length > MaxResponseBytes)
            return TimestampValidation.Fail(TimestampErrorKinds.InvalidResponse, "respuesta de más de 64 KiB");

        // 3a. PKIStatus leído a mano: así un rechazo de la TSA se distingue de una respuesta mal formada sin
        // depender del texto de la excepción del runtime.
        if (ReadPkiStatus(body) is not { } status)
            return TimestampValidation.Fail(TimestampErrorKinds.InvalidResponse, "respuesta que no es un TimeStampResp");
        if (status is not (0 or 1))
            return TimestampValidation.Fail(TimestampErrorKinds.Rejected, $"la autoridad rechazó el pedido (estado {status})");

        // 3b. Imprint, nonce y certificados (certReq = true).
        Rfc3161TimestampToken token;
        int consumed;
        try
        {
            token = request.ProcessResponse(body, out consumed);
        }
        catch (Exception ex) when (ex is CryptographicException or AsnContentException)
        {
            return TimestampValidation.Fail(TimestampErrorKinds.InvalidResponse, Trim(ex.Message));
        }

        // 4. Nada de más al final.
        if (consumed != body.Length)
            return TimestampValidation.Fail(TimestampErrorKinds.InvalidResponse, "bytes de más después de la respuesta");

        // 5. Firma (incluye el EKU de sellado del firmante).
        X509Certificate2? signer;
        try
        {
            if (!token.VerifySignatureForHash(hash, HashAlgorithmName.SHA256, out signer, extraCandidates: null) ||
                signer is null)
                return TimestampValidation.Fail(TimestampErrorKinds.InvalidResponse, "firma del sello inválida");
        }
        catch (CryptographicException)
        {
            return TimestampValidation.Fail(TimestampErrorKinds.InvalidResponse, "firma del sello inválida");
        }

        // 6. SHA-256.
        var info = token.TokenInfo;
        if (info.HashAlgorithmId.Value != Sha256Oid)
            return TimestampValidation.Fail(TimestampErrorKinds.InvalidResponse, "el sello no usa SHA-256");

        // 7. Datos.
        var data = new TimestampData(
            info.Timestamp.UtcDateTime,
            SerialHex(info.GetSerialNumber().Span),
            info.PolicyId.Value ?? "",
            signer.Subject,
            KnownAuthorities.AuthorityFor(host, signer));
        signer.Dispose();
        return TimestampValidation.Ok(data);
    }

    /// <summary>
    /// <c>PKIStatusInfo.status</c> del <c>TimeStampResp</c>, o null si los bytes no tienen esa forma.
    /// </summary>
    internal static int? ReadPkiStatus(byte[] body)
    {
        try
        {
            var reader = new AsnReader(body, AsnEncodingRules.DER);
            var resp = reader.ReadSequence();
            var statusInfo = resp.ReadSequence();
            if (!statusInfo.TryReadInt32(out var status)) return null;
            return status;
        }
        catch (AsnContentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Número de serie en hex MAYÚSCULA sin bytes 0x00 a la izquierda (como <c>openssl ts -reply -text</c>). El
    /// runtime lo devuelve big-endian.
    /// </summary>
    internal static string SerialHex(ReadOnlySpan<byte> serial)
    {
        var i = 0;
        while (i < serial.Length - 1 && serial[i] == 0) i++;
        return Convert.ToHexString(serial[i..]);
    }

    private static string Trim(string s)
    {
        s = s.ReplaceLineEndings(" ").Trim();
        return s.Length <= 200 ? s : s[..200];
    }
}
