namespace Factum.Agent.Common;

/// <summary>Clasificación de errores de disco (tatana-ws-y-hosts T-07). Pura.</summary>
public static class DiskErrors
{
    /// <summary>
    /// "Disco lleno" por <c>HResult</c>: <c>28</c> (ENOSPC en macOS/Linux), <c>0x80070070</c>
    /// (ERROR_DISK_FULL) y <c>0x80070027</c> (ERROR_HANDLE_DISK_FULL) en Windows.
    /// </summary>
    public static bool IsDiskFull(IOException ex) =>
        ex.HResult is 28 or unchecked((int)0x80070070) or unchecked((int)0x80070027);
}
