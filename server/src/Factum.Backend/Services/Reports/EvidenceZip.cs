using System.Collections.Frozen;
using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.Zip;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Escritura y verificación del ZIP de evidencia con SharpZipLib (Refactorizaciones/zip-cifrado-real.md
/// §3.3 y §5.3). Con contraseña, cada entrada va cifrada con AES-256 (WinZip AE-2); sin contraseña,
/// el mismo escritor genera un ZIP en claro (DT3).
/// <para>
/// Método por entrada (zip-evidencia-sin-recomprimir D2): los formatos que ya vienen comprimidos
/// (<see cref="StoredExtensions"/>: video, audio, imagen, archivos comprimidos) se guardan con Stored,
/// sin recomprimir; el resto va con Deflate nivel 6. El cifrado no cambia según el método.
/// </para>
/// <para>
/// Ningún mensaje de error de esta clase incluye la contraseña.
/// </para>
/// </summary>
internal static class EvidenceZip
{
    /// <summary>Valor de <c>Case.ZipEncryption</c> para un ZIP escrito por <see cref="WriteAsync"/> con contraseña.</summary>
    public const string EncryptionAes256Ae2 = "aes256-ae2";

    private const int AesKeySize = 256;
    // Solo aplica a las entradas Deflate (ver MethodFor); equivale al CompressionLevel.Optimal anterior.
    private const int CompressionLevel = 6;
    private const string ErrorPrefix = "No se pudo verificar el ZIP de evidencia: ";

    /// <summary>
    /// Extensiones (sin punto, en minúsculas) de formatos que ya vienen comprimidos: se guardan con Stored
    /// (zip-evidencia-sin-recomprimir D2). El resto va con Deflate nivel 6.
    /// </summary>
    internal static readonly FrozenSet<string> StoredExtensions = new[]
    {
        // video
        "mp4", "mkv", "mov", "m4v", "webm", "3gp", "avi",
        // audio
        "mp3", "m4a", "aac", "ogg", "opus", "amr",
        // imagen
        "jpg", "jpeg", "png", "heic", "heif", "webp", "gif",
        // comprimidos / paquetes ("x.tar.gz" cae en "gz")
        "zip", "7z", "rar", "gz", "apk", "ipa",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Método de compresión de una entrada según la extensión de su nombre (sin mirar el contenido,
    /// sin distinguir mayúsculas): Stored si está en <see cref="StoredExtensions"/>, Deflated si no.
    /// </summary>
    internal static CompressionMethod MethodFor(string fileName)
    {
        var ext = Path.GetExtension(fileName);          // ".MP4", "" si no tiene
        return ext.Length > 1 && StoredExtensions.Contains(ext[1..])
            ? CompressionMethod.Stored
            : CompressionMethod.Deflated;
    }

    /// <summary>
    /// Escribe <paramref name="zipPath"/> (FileMode.CreateNew) con cada archivo de
    /// <paramref name="filePaths"/> como entrada plana (solo el nombre). Cifrado AES-256 si
    /// <paramref name="password"/> no es null. Cada entrada va con Stored o Deflate nivel 6 según
    /// <see cref="MethodFor"/> (zip-evidencia-sin-recomprimir). Cierra el archivo antes de volver.
    /// </summary>
    public static async Task WriteAsync(string zipPath, IReadOnlyList<string> filePaths,
        string? password, CancellationToken ct)
    {
        var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        using var zos = new ZipOutputStream(fs) { IsStreamOwner = true };
        zos.SetLevel(CompressionLevel);
        // Dynamic: decide Zip64 por entrada según el Size declarado (grabaciones > 4 GB).
        zos.UseZip64 = UseZip64.Dynamic;
        // Regla 1: la contraseña se fija ANTES del primer PutNextEntry.
        if (password is not null) zos.Password = password;

        // Hora de generación en UTC, igual que el ZipArchive anterior (LastWriteTime = UtcNow).
        var now = DateTime.UtcNow;
        foreach (var path in filePaths)
        {
            ct.ThrowIfCancellationRequested();
            var entry = new ZipEntry(Path.GetFileName(path))
            {
                DateTime = now,
                // Stored para lo que ya viene comprimido (video, imagen, ...), Deflate para el resto.
                CompressionMethod = MethodFor(path),
                // Regla 3: Size siempre, para que Dynamic sepa si la entrada necesita Zip64.
                Size = new FileInfo(path).Length,
                // Regla 4: nombres en UTF-8 (bit 11): los device_pull_* pueden traer tildes.
                IsUnicodeText = true,
            };
            // Regla 2: sin AESKeySize, SharpZipLib escribiría ZipCrypto (roto). VerifyAsync lo
            // detectaría igual y cortaría la generación.
            if (password is not null) entry.AESKeySize = AesKeySize;

            await zos.PutNextEntryAsync(entry, ct);
            await using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                             bufferSize: 81920, useAsync: true))
            {
                await src.CopyToAsync(zos, ct);
            }
            await zos.CloseEntryAsync(ct);
        }

        await zos.FinishAsync(ct);
    }

    /// <summary>
    /// Reabre el ZIP en SOLO LECTURA y comprueba que tenga exactamente las entradas de
    /// <paramref name="expectedSha256ByName"/>, cifradas (o no) según <paramref name="password"/>,
    /// y que el SHA-256 de cada entrada (descomprimida si corresponde; Stored o Deflate) coincida con el
    /// del manifiesto. Una entrada Stored alterada se detecta por ese SHA-256 (SharpZipLib no valida el
    /// HMAC de AES al leer). Lanza
    /// <see cref="EvidenceZipVerificationException"/> si algo no cuadra. No modifica el archivo.
    /// </summary>
    public static async Task VerifyAsync(string zipPath,
        IReadOnlyDictionary<string, string> expectedSha256ByName, string? password, CancellationToken ct)
    {
        await using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        ZipFile zf;
        try
        {
            zf = new ZipFile(fs, leaveOpen: true);
        }
        catch (Exception ex) when (ex is ICSharpCode.SharpZipLib.SharpZipBaseException
                                       or IOException or InvalidDataException)
        {
            throw new EvidenceZipVerificationException(ErrorPrefix + "el archivo no se puede abrir", ex);
        }

        using (zf)
        {
            if (password is not null) zf.Password = password;

            if (zf.Count != expectedSha256ByName.Count)
                throw new EvidenceZipVerificationException(
                    $"{ErrorPrefix}tiene {zf.Count} archivos y se esperaban {expectedSha256ByName.Count}");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ZipEntry e in zf)
            {
                ct.ThrowIfCancellationRequested();

                if (!e.IsFile)
                    throw new EvidenceZipVerificationException($"{ErrorPrefix}{e.Name} no es un archivo");
                if (!expectedSha256ByName.TryGetValue(e.Name, out var expectedHash))
                    throw new EvidenceZipVerificationException($"{ErrorPrefix}{e.Name} no es parte de la evidencia");
                if (!seen.Add(e.Name))
                    throw new EvidenceZipVerificationException($"{ErrorPrefix}{e.Name} está repetido");

                if (password is not null)
                {
                    if (!e.IsCrypted || e.AESKeySize != AesKeySize)
                        throw new EvidenceZipVerificationException(
                            $"{ErrorPrefix}{e.Name} no está cifrado con AES-256");
                }
                else if (e.IsCrypted)
                {
                    throw new EvidenceZipVerificationException(
                        $"{ErrorPrefix}{e.Name} está cifrado y no debería");
                }

                string actualHash;
                try
                {
                    await using var entryStream = zf.GetInputStream(e);
                    actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(entryStream, ct));
                }
                catch (ZipException ex) when (password is not null &&
                                              ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
                {
                    throw new EvidenceZipVerificationException(ErrorPrefix + "la contraseña no lo abre", ex);
                }
                catch (Exception ex) when (ex is ICSharpCode.SharpZipLib.SharpZipBaseException
                                               or IOException or InvalidDataException)
                {
                    throw new EvidenceZipVerificationException($"{ErrorPrefix}{e.Name} no se puede leer", ex);
                }

                if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                    throw new EvidenceZipVerificationException(
                        $"{ErrorPrefix}el archivo {e.Name} no coincide con su hash");
            }

            // Con Count igual y sin repetidos no puede faltar ninguno; se deja por robustez.
            foreach (var name in expectedSha256ByName.Keys)
                if (!seen.Contains(name))
                    throw new EvidenceZipVerificationException($"{ErrorPrefix}falta el archivo {name}");
        }
    }
}

/// <summary>El ZIP de evidencia no pasó la verificación (D6). El mensaje nunca incluye la contraseña.</summary>
internal sealed class EvidenceZipVerificationException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);
