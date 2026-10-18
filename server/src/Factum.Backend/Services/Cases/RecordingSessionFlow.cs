using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Recording;

namespace Factum.Backend.Services.Cases;

/// <summary>Códigos estables de la grabación de la sesión (grabacion-sesion-completa §7.7, campo <c>code</c>).</summary>
public static class RecordingSessionErrorCodes
{
    public const string RecordingSessionDisabled = "recording_session_disabled";
    public const string RecordingSessionRequired = "recording_session_required";
    public const string RecordingSessionNotEnabled = "recording_session_not_enabled";
    public const string InvalidRecordingSession = "invalid_recording_session";
    public const string RecordingSessionConflict = "recording_session_conflict";
    public const string RecordingSessionBusy = "recording_session_busy";
    public const string RecordingSessionOpen = "recording_session_open";
    public const string RecordingSessionFile = "recording_session_file";

    public const string RecordingSessionDisabledMessage = "La grabación de la sesión está deshabilitada en esta instalación.";
    public const string RecordingSessionRequiredMessage = "La configuración de esta instalación exige grabar la sesión.";
    public const string RecordingSessionNotEnabledMessage = "Este caso no tiene activada la grabación de la sesión.";
    public const string InvalidRecordingSessionMessage = "Datos de la grabación de la sesión inválidos.";
    public const string RecordingSessionConflictMessage = "La grabación de la sesión registrada no coincide con la de esta PC.";
    public const string RecordingSessionBusyMessage =
        "Otra pestaña está actualizando la grabación de la sesión. Reintentá en unos segundos.";
    public const string RecordingSessionOpenMessage = "Primero cerrá la grabación de la sesión.";
    public const string RecordingSessionFileMessage = "Los tramos de la grabación de la sesión no se pueden quitar del caso.";
}

/// <summary>
/// Grabación de la sesión en el expediente (grabacion-sesion-completa §6.3): el switch del perito y la copia validada y
/// append-only del diario de Tatana. Ningún método lanza: los rechazos salen como Result con <c>code</c>
/// (<see cref="RecordingSessionErrorCodes"/>). Devuelven el caso recargado.
/// </summary>
public sealed partial class CaseService
{
    public const int MaxRecordingSessionAttempts = 3;

    private static Result<T> SessionFail<T>(ErrorKind kind, string error, string code,
        params (string Key, object? Value)[] extra) => UploadFail<T>(kind, error, code, extra);

    /// <summary><c>PUT /api/cases/{id}/recording-session/settings</c>.</summary>
    public async Task<Result<Case>> SaveRecordingSessionSettingsAsync(string id, RecordingSessionSettingsRequest request,
        string officerDni, CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<Case>(id, officerDni, ct);
        if (cas is null) return error!;
        if (!IsEditable(cas))
            return SessionFail<Case>(ErrorKind.Conflict, NotEditableMessage, EvidenceErrorCodes.CaseNotEditable);
        if (request.Enabled is not { } enabled)
            return SessionFail<Case>(ErrorKind.Validation, RecordingSessionErrorCodes.InvalidRecordingSessionMessage,
                RecordingSessionErrorCodes.InvalidRecordingSession, ("field", "enabled"));
        // F2 (§6.3, D9 = A): no hay cámara sin sesión.
        var withCamera = enabled && (request.WithCamera ?? false);

        var mode = _recordingSession.Mode;
        if (mode == RecordingSessionModes.Disabled && enabled)
            return SessionFail<Case>(ErrorKind.Conflict, RecordingSessionErrorCodes.RecordingSessionDisabledMessage,
                RecordingSessionErrorCodes.RecordingSessionDisabled);
        if (mode == RecordingSessionModes.Required && !enabled)
            return SessionFail<Case>(ErrorKind.Validation, RecordingSessionErrorCodes.RecordingSessionRequiredMessage,
                RecordingSessionErrorCodes.RecordingSessionRequired);

        var existing = cas.RecordingSession;
        var created = existing is null;
        if (!created && existing!.Enabled == enabled && existing.WithCamera == withCamera)
            return Result.Ok(cas); // nada cambió: sin escritura ni evento

        var now = DateTime.UtcNow;
        var ok = created
            ? await _repo.CreateRecordingSessionAsync(id, new RecordingSession
            {
                Enabled = enabled,
                WithCamera = withCamera,
                ModeAtChange = mode,
                SettingsUpdatedAt = now,
                SegmentMinutes = _recordingSession.SegmentMinutes,
            }, ct)
            : await _repo.SetRecordingSessionSettingsAsync(id, enabled, withCamera, mode, now, ct);
        if (!ok)
        {
            // Otra pestaña lo creó entre la lectura y la escritura, o el caso dejó de ser editable.
            var fresh = await _repo.FindByIdAsync(id, ct);
            if (fresh is null || !IsEditable(fresh))
                return SessionFail<Case>(ErrorKind.Conflict, NotEditableMessage, EvidenceErrorCodes.CaseNotEditable);
            if (!await _repo.SetRecordingSessionSettingsAsync(id, enabled, withCamera, mode, now, ct))
                return SessionFail<Case>(ErrorKind.Conflict, NotEditableMessage, EvidenceErrorCodes.CaseNotEditable);
        }

        var evt = CaseEventFor(cas, CaseEventTypes.RecordingSessionSettingsChanged);
        evt.Detail = new CaseEventDetail
        {
            Enabled = enabled,
            WithCamera = withCamera,
            PreviousEnabled = existing?.Enabled,
            PreviousWithCamera = existing?.WithCamera,
        };
        await RecordEventAsync(evt);
        _log.LogInformation("Caso {CaseId}: grabación de la sesión {Estado} (modo {Mode})", id,
            enabled ? "activada" : "desactivada", mode);
        return await ReloadAsync(id, ct);
    }

    /// <summary><c>PUT /api/cases/{id}/recording-session</c>: snapshot del diario de Tatana (merge append-only).</summary>
    public async Task<Result<Case>> SyncRecordingSessionAsync(string id, RecordingSessionSnapshotRequest request,
        string officerDni, CancellationToken ct = default)
    {
        var validation = RecordingSessionRules.Validate(request, DateTime.UtcNow);

        for (var attempt = 1; attempt <= MaxRecordingSessionAttempts; attempt++)
        {
            var (cas, error) = await LoadOwnedAsync<Case>(id, officerDni, ct);
            if (cas is null) return error!;
            if (!IsEditable(cas))
                return SessionFail<Case>(ErrorKind.Conflict, NotEditableMessage, EvidenceErrorCodes.CaseNotEditable);
            if (cas.RecordingSession is not { Enabled: true } existing)
                return SessionFail<Case>(ErrorKind.Conflict, RecordingSessionErrorCodes.RecordingSessionNotEnabledMessage,
                    RecordingSessionErrorCodes.RecordingSessionNotEnabled);
            if (!validation.IsValid)
                return SessionFail<Case>(ErrorKind.Validation, validation.Error!,
                    RecordingSessionErrorCodes.InvalidRecordingSession, ("field", validation.Field));

            var merge = RecordingSessionRules.Merge(existing, validation.Snapshot!);
            if (merge.IsConflict)
            {
                _log.LogWarning("Caso {CaseId}: el diario de la sesión no coincide con el registrado ({Field})", id,
                    merge.ConflictField);
                return SessionFail<Case>(ErrorKind.Conflict, RecordingSessionErrorCodes.RecordingSessionConflictMessage,
                    RecordingSessionErrorCodes.RecordingSessionConflict, ("field", merge.ConflictField));
            }
            var changes = merge.Changes!;
            if (!changes.Any) return Result.Ok(cas); // idéntico: sin escritura ni eventos

            var merged = merge.Merged!;
            merged.SyncedAt = DateTime.UtcNow;
            if (!await _repo.SetRecordingSessionAsync(id, existing.SyncedAt, merged, ct))
            {
                _log.LogInformation("Caso {CaseId}: el diario de la sesión cambió durante el sync (intento {N})", id, attempt);
                if (attempt < MaxRecordingSessionAttempts)
                    await Task.Delay(Random.Shared.Next(15, 60) * attempt, ct);
                continue;
            }

            foreach (var (type, filename, detail) in RecordingSessionRules.Events(changes, merged))
            {
                var evt = CaseEventFor(cas, type);
                evt.Filename = filename;
                evt.Detail = detail;
                await RecordEventAsync(evt);
            }
            _log.LogInformation(
                "Caso {CaseId}: diario de la sesión sincronizado ({Segments} tramo(s) nuevos, {Gaps} hueco(s) nuevos, {Closed} cerrados, {Marks} marca(s))",
                id, changes.NewSegments.Count, changes.NewGaps.Count, changes.ClosedGaps.Count, changes.NewMarks);
            return await ReloadAsync(id, ct);
        }

        _log.LogWarning("Caso {CaseId}: sync del diario de la sesión abandonado tras {N} intentos en carrera", id,
            MaxRecordingSessionAttempts);
        return SessionFail<Case>(ErrorKind.Conflict, RecordingSessionErrorCodes.RecordingSessionBusyMessage,
            RecordingSessionErrorCodes.RecordingSessionBusy);
    }
}
