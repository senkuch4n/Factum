namespace Factum.Backend.Infrastructure;

/// <summary>Sondeo del espacio libre del volumen de un directorio (subida-archivos-grandes §5.3).</summary>
public interface IDiskSpaceProbe
{
    /// <summary>Bytes libres para el proceso, o <c>null</c> si no se pudo sondear.</summary>
    long? GetAvailableFreeBytes(string path);
}

/// <summary>
/// Espacio libre del volumen de la ruta. Nunca lanza.
/// <para>
/// En Linux de 64 bits llama a <c>statvfs(3)</c> y calcula <c>f_bavail × f_frsize</c>.
/// <see cref="DriveInfo.AvailableFreeSpace"/> multiplica por <c>f_bsize</c>, y en el bind mount de
/// Docker Desktop (<c>fakeowner</c>/virtiofs) <c>f_bsize</c> es 1 MiB con <c>f_frsize</c> 4 KiB:
/// sobreestima ×256 (medido en la Mac: 2946,8 GB contra 11,5 GB reales; ver
/// progress/impl_backend_subida-archivos-grandes.md, V1). Si <c>statvfs</c> no está disponible, y en
/// los demás sistemas, usa <see cref="DriveInfo"/>.
/// </para>
/// </summary>
public sealed class DriveInfoDiskSpaceProbe : IDiskSpaceProbe
{
    public long? GetAvailableFreeBytes(string path)
    {
        try
        {
            if (OperatingSystem.IsLinux() && Environment.Is64BitProcess &&
                LinuxStatVfs.TryGetAvailableBytes(path) is { } linux)
                return linux;
            return new DriveInfo(path).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }
}

// statvfs de glibc/musl en LP64: 11 campos de 8 bytes (f_bsize, f_frsize, f_blocks, f_bfree,
// f_bavail, …) + reserva. Se lee de un buffer holgado para no depender del tamaño exacto.
internal static class LinuxStatVfs
{
    private const int OffsetBsize = 0;
    private const int OffsetFrsize = 8;
    private const int OffsetBavail = 32;

    [System.Runtime.InteropServices.DllImport("libc.so.6", EntryPoint = "statvfs", SetLastError = true)]
    private static extern int StatVfsGlibc(string path, [System.Runtime.InteropServices.Out] byte[] buf);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "statvfs", SetLastError = true)]
    private static extern int StatVfsLibc(string path, [System.Runtime.InteropServices.Out] byte[] buf);

    public static long? TryGetAvailableBytes(string path)
    {
        var buf = new byte[256];
        int rc;
        try
        {
            rc = StatVfsGlibc(path, buf);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            try { rc = StatVfsLibc(path, buf); }
            catch (Exception e2) when (e2 is DllNotFoundException or EntryPointNotFoundException) { return null; }
        }
        if (rc != 0) return null;

        var frsize = BitConverter.ToUInt64(buf, OffsetFrsize);
        if (frsize == 0) frsize = BitConverter.ToUInt64(buf, OffsetBsize);
        var bavail = BitConverter.ToUInt64(buf, OffsetBavail);
        if (frsize == 0) return null;
        var bytes = (UInt128)bavail * frsize;
        return bytes > long.MaxValue ? long.MaxValue : (long)bytes;
    }
}

/// <summary>El cuerpo recibido no tiene la longitud anunciada, o se cortó al leerlo.</summary>
public sealed class UploadIncompleteException(long receivedBytes, long expectedBytes, Exception? inner = null)
    : Exception($"Subida incompleta: {receivedBytes} de {expectedBytes} bytes", inner)
{
    public long ReceivedBytes { get; } = receivedBytes;
    public long ExpectedBytes { get; } = expectedBytes;
}

/// <summary>El disco se llenó durante la escritura del temporal.</summary>
public sealed class InsufficientStorageException(long requiredBytes, long? availableBytes, Exception? inner = null)
    : Exception("No hay espacio en el disco para la subida", inner)
{
    public long RequiredBytes { get; } = requiredBytes;
    public long? AvailableBytes { get; } = availableBytes;
}

/// <summary>
/// Subida ya escrita y verificada en <c>.upload-tmp/</c>, todavía fuera del caso. <see cref="Commit"/>
/// la publica con un rename atómico (con reemplazo); <see cref="Dispose"/> sin commit borra el temporal.
/// </summary>
public sealed class StagedUpload : IDisposable
{
    private readonly string _caseDir;
    private readonly Action<string, Exception> _onDeleteFailed;
    private bool _committed;
    private bool _disposed;

    internal StagedUpload(string tempPath, string finalPath, string caseDir, long length, string hash,
        Action<string, Exception> onDeleteFailed)
    {
        TempPath = tempPath;
        FinalPath = finalPath;
        _caseDir = caseDir;
        Length = length;
        Hash = hash;
        _onDeleteFailed = onDeleteFailed;
    }

    public string TempPath { get; }
    public string FinalPath { get; }
    public long Length { get; }
    /// <summary>SHA-256 en hex minúscula, calculado al vuelo.</summary>
    public string Hash { get; }

    public FileInfo Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_committed) throw new InvalidOperationException("La subida ya se publicó");
        Directory.CreateDirectory(_caseDir);
        // Mismo filesystem (DataDirectory): rename(2), atómico y con reemplazo. Nunca queda el
        // nombre final truncado: o está el archivo anterior completo o el nuevo completo.
        File.Move(TempPath, FinalPath, overwrite: true);
        _committed = true;
        return new FileInfo(FinalPath);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_committed) return;
        try
        {
            File.Delete(TempPath);
        }
        catch (Exception ex)
        {
            _onDeleteFailed(TempPath, ex);
        }
    }
}
