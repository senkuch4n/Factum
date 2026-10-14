using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Cases;

/// <summary>Códigos estables de las rutas de la ficha técnica (metadatos-dispositivo §7.1, campo <c>code</c>).</summary>
public static class DeviceDetailsErrorCodes
{
    public const string CaseNotEditable = "case_not_editable";
    public const string DeviceMismatch = "device_mismatch";
    public const string NoDeviceDetails = "no_device_details";
    public const string InvalidDeviceDetails = "invalid_device_details";

    public const string DeviceMismatchMessage = "El dispositivo conectado no es el del caso.";
    public const string NoDeviceDetailsMessage = "El caso no tiene ficha técnica registrada.";
    public const string InvalidDeviceDetailsMessage = "Datos técnicos del dispositivo inválidos.";
}

/// <summary>
/// Ficha técnica del dispositivo y mediciones de reloj (metadatos-dispositivo §7.1). Ningún método lanza: los
/// rechazos salen como Result con <c>code</c>. Los valores se sanean y el reloj se recalcula acá
/// (<see cref="DeviceDetailsRules"/>); la hora de registro es la del servidor (D-T6). Los logs y los eventos nunca
/// llevan valores de la ficha ni el SSID.
/// </summary>
public sealed partial class CaseService
{
    private static Result<T> DetailsFail<T>(ErrorKind kind, string error, string code) =>
        UploadFail<T>(kind, error, code);

    // Chequeos 1 (dueño + editable) y 3 (mismo identificador de conexión, D10) de §7.1.
    private async Task<(Case? Case, Result<Case>? Error)> LoadForDeviceDetailsAsync(string id, string officerDni,
        string? connectionId, bool checkDevice, CancellationToken ct)
    {
        var (cas, error) = await LoadOwnedAsync<Case>(id, officerDni, ct);
        if (cas is null) return (null, error);
        if (!IsEditable(cas))
            return (null, DetailsFail<Case>(ErrorKind.Conflict, NotEditableMessage, DeviceDetailsErrorCodes.CaseNotEditable));
        return checkDevice ? CheckDevice(cas, connectionId) : (cas, null);
    }

    private async Task<Result<Case>> ReloadAsync(string id, CancellationToken ct)
    {
        var fresh = await _repo.FindByIdAsync(id, ct);
        return fresh is null ? Result.NotFound<Case>() : Result.Ok(ResolveStorage(fresh));
    }

    private static ClockMeasurement BuildClock(string kind, DateTime now, DeviceClockDto? clock, PcServerProbeDto? probe)
    {
        var m = new ClockMeasurement { Kind = kind, RecordedAt = now };
        DeviceDetailsRules.ComputeClock(clock, m);
        DeviceDetailsRules.ComputePcServer(probe, now, m);
        return m;
    }

    private static CaseEventDetail ClockDetail(ClockMeasurement m) => new()
    {
        ClockKind = m.Kind,
        DeviceMinusPcMs = m.DeviceMinusPcMs,
        ClockMarginMs = m.MarginMs,
        PcMinusServerMs = m.PcMinusServerMs,
        Reason = m.DeviceMinusPcMs is null ? m.Reason : null,
    };

    /// <summary><c>PUT /api/cases/{id}/device-details</c> (alta, retoma o "Volver a leer").</summary>
    public async Task<Result<Case>> SaveDeviceDetailsAsync(string id, SaveDeviceDetailsRequest request,
        string officerDni, CancellationToken ct = default)
    {
        // 1. Dueño + editable.
        var (cas, error) = await LoadForDeviceDetailsAsync(id, officerDni, null, checkDevice: false, ct);
        if (cas is null) return error!;

        // 2. Plataforma y connection_id.
        var platform = request.Platform?.Trim();
        if (platform is not ("android" or "ios") ||
            SafeText.Clean(request.ConnectionId, TextShape.SingleLine).Trim().Length == 0)
            return DetailsFail<Case>(ErrorKind.Validation, DeviceDetailsErrorCodes.InvalidDeviceDetailsMessage,
                DeviceDetailsErrorCodes.InvalidDeviceDetails);

        // 3. Mismo identificador de conexión (D10).
        if (CheckDevice(cas, request.ConnectionId).Error is { } mismatch) return mismatch;

        var now = DateTime.UtcNow;
        var previous = cas.DeviceDetails;
        var details = new DeviceDetails
        {
            ReadAt = now,
            Platform = platform,
            ConnectionId = cas.Device.Serial,
            AgentVersion = DeviceDetailsRules.CleanAgentVersion(request.AgentVersion),
            Fields = DeviceDetailsRules.NormalizeFields(request.Fields),
            ClockStart = BuildClock(ClockKinds.Start, now, request.Clock, request.PcServerProbe),
            // Se conserva: la próxima generación la pisa o la filtra (D-T7).
            ClockClose = previous?.ClockClose,
            IncludeSsidInReport = request.IncludeSsidInReport ?? previous?.IncludeSsidInReport ?? false,
        };

        if (!await _repo.SetDeviceDetailsAsync(id, details, ct))
            return DetailsFail<Case>(ErrorKind.Conflict, NotEditableMessage, DeviceDetailsErrorCodes.CaseNotEditable);

        var unavailable = DeviceDetailsRules.UnavailableCount(details.Fields);
        var evt = CaseEventFor(cas, CaseEventTypes.DeviceDetailsRecorded);
        evt.Detail = ClockDetail(details.ClockStart);
        evt.Detail.UnavailableCount = unavailable;
        evt.Detail.Reread = previous is not null ? true : null;
        await RecordEventAsync(evt);
        _log.LogInformation("Ficha técnica registrada para caso {CaseId} ({Platform}, {Unavailable} no disponibles)",
            id, platform, unavailable);

        return await ReloadAsync(id, ct);
    }

    private static (Case? Case, Result<Case>? Error) CheckDevice(Case cas, string? connectionId)
    {
        var conn = SafeText.Clean(connectionId, TextShape.SingleLine).Trim();
        if (conn.Length == 0)
            return (null, DetailsFail<Case>(ErrorKind.Validation, DeviceDetailsErrorCodes.InvalidDeviceDetailsMessage,
                DeviceDetailsErrorCodes.InvalidDeviceDetails));
        if (!string.Equals(conn, cas.Device.Serial, StringComparison.Ordinal))
            return (null, DetailsFail<Case>(ErrorKind.Conflict, DeviceDetailsErrorCodes.DeviceMismatchMessage,
                DeviceDetailsErrorCodes.DeviceMismatch));
        return (cas, null);
    }

    /// <summary><c>PUT /api/cases/{id}/device-details/clock</c>: medición de cierre al generar.</summary>
    public async Task<Result<Case>> SaveDeviceClockAsync(string id, SaveDeviceClockRequest request,
        string officerDni, CancellationToken ct = default)
    {
        var (cas, error) = await LoadForDeviceDetailsAsync(id, officerDni, request.ConnectionId, checkDevice: true, ct);
        if (cas is null) return error!;
        if (cas.DeviceDetails is null)
            return DetailsFail<Case>(ErrorKind.Conflict, DeviceDetailsErrorCodes.NoDeviceDetailsMessage,
                DeviceDetailsErrorCodes.NoDeviceDetails);

        var m = BuildClock(ClockKinds.Close, DateTime.UtcNow, request.Clock, request.PcServerProbe);
        if (!await _repo.SetClockCloseAsync(id, m, ct))
        {
            // Dejó de ser editable o perdió la ficha entre la lectura y la escritura.
            var again = await _repo.FindByIdAsync(id, ct);
            return again is not null && IsEditable(again) && again.DeviceDetails is null
                ? DetailsFail<Case>(ErrorKind.Conflict, DeviceDetailsErrorCodes.NoDeviceDetailsMessage,
                    DeviceDetailsErrorCodes.NoDeviceDetails)
                : DetailsFail<Case>(ErrorKind.Conflict, NotEditableMessage, DeviceDetailsErrorCodes.CaseNotEditable);
        }

        var evt = CaseEventFor(cas, CaseEventTypes.DeviceClockMeasured);
        evt.Detail = ClockDetail(m);
        await RecordEventAsync(evt);
        _log.LogInformation("Reloj del dispositivo medido al generar para caso {CaseId}", id);
        return await ReloadAsync(id, ct);
    }

    /// <summary><c>PUT /api/cases/{id}/device-details/options</c>: el flag del SSID (D7). Sin evento.</summary>
    public async Task<Result<Case>> SetDeviceDetailsOptionsAsync(string id, DeviceDetailsOptionsRequest request,
        string officerDni, CancellationToken ct = default)
    {
        var (cas, error) = await LoadForDeviceDetailsAsync(id, officerDni, null, checkDevice: false, ct);
        if (cas is null) return error!;
        if (cas.DeviceDetails is null)
            return DetailsFail<Case>(ErrorKind.Conflict, DeviceDetailsErrorCodes.NoDeviceDetailsMessage,
                DeviceDetailsErrorCodes.NoDeviceDetails);
        if (request.IncludeSsidInReport is not { } include)
            return DetailsFail<Case>(ErrorKind.Validation, DeviceDetailsErrorCodes.InvalidDeviceDetailsMessage,
                DeviceDetailsErrorCodes.InvalidDeviceDetails);

        if (!await _repo.SetIncludeSsidAsync(id, include, ct))
            return DetailsFail<Case>(ErrorKind.Conflict, NotEditableMessage, DeviceDetailsErrorCodes.CaseNotEditable);
        return await ReloadAsync(id, ct);
    }
}
