using System.Security.Cryptography;

namespace Factum.Agent.Common;

/// <summary>
/// SHA-256 de un archivo en streaming (extraccion-logica-android §3, misma técnica que
/// <c>CaseEvidenceStore.Sha256Async</c> con <see cref="IncrementalHash"/>, pero sin depender del store).
/// Lee en bloques y, si se pasa <paramref name="onBytes"/>, reporta el avance acumulado.
/// </summary>
public static class FileHashing
{
    private const int BufferSize = 1024 * 1024; // 1 MiB

    /// <summary>
    /// Devuelve el SHA-256 en hex minúscula (64 caracteres) del archivo. <paramref name="onBytes"/>, si se pasa,
    /// recibe la cuenta ACUMULADA de bytes leídos a medida que avanza.
    /// </summary>
    public static async Task<string> Sha256Async(string path, CancellationToken ct = default,
        Action<long>? onBytes = null)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[BufferSize];
        long total = 0;
        int read;
        while ((read = await fs.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
            total += read;
            onBytes?.Invoke(total);
        }
        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }
}
