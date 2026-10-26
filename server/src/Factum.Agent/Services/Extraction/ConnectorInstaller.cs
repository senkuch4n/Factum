using Factum.Agent.Common;

namespace Factum.Agent.Services.Extraction;

/// <summary>reason_code de la categoría call_log (§4.4).</summary>
public static class ConnectorReasonCodes
{
    public const string Unavailable = "connector_unavailable", InstallBlocked = "connector_install_blocked",
        InstallFailed = "connector_install_failed", Conflict = "connector_conflict";
}

/// <summary>Resultado de instalar el connector.</summary>
public sealed record ConnectorInstallResult(bool Installed, string? ReasonCode, string? Reason);

/// <summary>
/// Verificación, instalación, consulta y desinstalación del APK del connector (extraccion-logica-android §5.5, §7).
/// adb se inyecta como delegado para poder testear el mapeo de <c>INSTALL_FAILED_*</c> sin dispositivo
/// (§12.1 <c>ConnectorInstallerTests</c>). La exportación de las llamadas la hace el <c>StructuredExtractor</c> con
/// <c>content query</c> sobre el proveedor del connector.
/// </summary>
public sealed class ConnectorInstaller(
    Func<IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> adb)
{
    public const string Package = "app.factum.connector";

    /// <summary>Instala <c>-r -g</c> (concede READ_CALL_LOG), con timeout de 120 s para el prompt de Play Protect.</summary>
    public async Task<ConnectorInstallResult> InstallAsync(string serial, string apkPath, CancellationToken ct)
    {
        ProcessResult r;
        try { r = await adb(["-s", serial, "install", "-r", "-g", apkPath], ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new ConnectorInstallResult(false, ConnectorReasonCodes.InstallFailed, ex.Message); }

        var output = (r.Stdout + "\n" + r.Stderr);
        if (r.ExitCode == 0 && output.Contains("Success", StringComparison.OrdinalIgnoreCase))
            return new ConnectorInstallResult(true, null, null);
        if (r.ExitCode == ProcessRunner.TimeoutExitCode)
            return new ConnectorInstallResult(false, ConnectorReasonCodes.InstallBlocked, "timeout");
        return new ConnectorInstallResult(false, Classify(output), FirstInstallFailure(output) ?? output.Trim());
    }

    /// <summary>Desinstala el connector (en <c>finally</c>). true si se fue (o ya no estaba).</summary>
    public async Task<bool> UninstallAsync(string serial, CancellationToken ct)
    {
        try
        {
            var r = await adb(["-s", serial, "uninstall", Package], ct);
            var output = r.Stdout + r.Stderr;
            return r.ExitCode == 0 && output.Contains("Success", StringComparison.OrdinalIgnoreCase)
                || output.Contains("not installed", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Unknown package", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    /// <summary>¿Ya está instalado? (pm path devuelve algo). null si no se pudo determinar.</summary>
    public async Task<bool?> IsInstalledAsync(string serial, CancellationToken ct)
    {
        try
        {
            var r = await adb(["-s", serial, "shell", "pm", "path", Package], ct);
            if (r.ExitCode != 0) return false;
            return r.Stdout.Contains("package:", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    /// <summary>Mapea el stderr de <c>adb install</c> a un reason_code (§5.5 paso 3).</summary>
    public static string Classify(string output)
    {
        if (output.Contains("INSTALL_FAILED_USER_RESTRICTED", StringComparison.Ordinal) ||
            output.Contains("INSTALL_FAILED_VERIFICATION_FAILURE", StringComparison.Ordinal))
            return ConnectorReasonCodes.InstallBlocked;
        if (output.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.Ordinal))
            return ConnectorReasonCodes.Conflict;
        return ConnectorReasonCodes.InstallFailed;
    }

    private static string? FirstInstallFailure(string output)
    {
        foreach (var token in output.Split([' ', '\n', '\r', '[', ']', '(', ')'], StringSplitOptions.RemoveEmptyEntries))
            if (token.StartsWith("INSTALL_FAILED_", StringComparison.Ordinal)) return token;
        return null;
    }
}
