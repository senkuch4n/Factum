using Factum.Agent.Common;
using Factum.Agent.Services;
using Factum.Agent.Services.Ios;
using Factum.Agent.Services.Session;
using Factum.Agent.WebSockets;
using Factum.Agent.Models;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

[ApiController]
[Route("devices/{serial}/record")]
public sealed class RecordingController(
    IAdbService adb,
    IIosService ios,
    IFileStorageService storage,
    AgentWebSocketHub hub,
    RecordingRegistry registry,
    IAndroidScreenProbe screenProbe,
    SessionRecorder sessions) : ControllerBase
{
    // grabacion-android-robusta §5.8 / §6.1. El trabajo con el dispositivo no depende del RequestAborted (DA4):
    // si el navegador recarga en "Iniciando…", la grabación igual arranca y se recupera por /agent/state.
    [HttpPost("start")]
    public async Task<IActionResult> Start(string serial,
        [FromBody] StartRecordingRequest request, CancellationToken ct)
    {
        string? caseId = null;
        if (request.CaseId is not null)
        {
            if (!AgentFileNames.TryParseCaseId(request.CaseId, out var canonical))
                return BadRequest(new { error = "Identificador de caso inválido", code = AgentErrorCodes.InvalidCaseId });
            caseId = canonical;
        }
        // wizard-concurrencia-pestanas DT4: la pestaña que la inicia. Inválido → null (es solo atribución).
        var originId = EventAttribution.Normalize(request.OriginId);

        try
        {
            string path;
            DateTimeOffset startedAt;
            string platform;
            if (request.Platform == "ios")
            {
                platform = "ios";
                path = storage.NewFilePath("grabacion_ios", "mp4");
                await ios.StartRecordingAsync(serial, request.IosMode ?? "video_only", path, caseId, CancellationToken.None);
                startedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                platform = "android";
                // grabacion-sesion-completa §5.12: con la sesión grabando este celular, se usa «Marcar momento».
                if (sessions.IsActiveOn(serial))
                    throw new AgentHttpException(409, AgentErrorCodes.SessionActive,
                        "Hay una grabación de la sesión en curso en este celular. Usá «Marcar momento».");
                // pulido-post-qa-final D9: antes de NewFilePath, StartRecordingAsync y SetOwner. Apagada o
                // bloqueada → 409 (lo devuelve el catch de AgentHttpException) sin recording_started ni estado.
                if (!request.SkipScreenCheck)
                {
                    var screen = await screenProbe.GetStateAsync(serial, ct);
                    if (AndroidScreenProbe.ScreenBlock(screen.State, "grabar") is { } block) throw block;
                }
                path = storage.NewFilePath("grabacion", "mkv"); // scrcpy graba MKV
                var r = await adb.StartRecordingAsync(serial, path, request.AndroidVersion, request.AndroidWithMic,
                    caseId, CancellationToken.None);
                startedAt = r.StartedAt;
            }

            var filename = Path.GetFileName(path);
            // Recién con el inicio confirmado (si el servicio tiró, no queda atribución huérfana).
            registry.SetOwner(serial, new EventAttribution(caseId, originId));
            await hub.BroadcastAsync(new AgentEvent
            {
                Type = "recording_started",
                Data = new { serial, platform, case_id = caseId, origin_id = originId, started_at = startedAt, filename },
            }, CancellationToken.None);
            return Ok(new StartRecordingResponse(filename, "recording", startedAt, caseId));
        }
        catch (AgentHttpException ex)
        {
            return StatusCode(ex.Status, ex.ToBody());
        }
        catch (IosException ex)
        {
            return StatusCode(500, new { error = ex.Message, code = ex.Code });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // §6.2 / §6.4. Android valida la grabación: ok/incomplete → 200; empty/unreadable → 422 y además un
    // recording_stopped con filename = null, así las otras pestañas salen de "Grabando".
    [HttpPost("stop")]
    // wizard-concurrencia-pestanas DT4: recording_stopped lleva serial/case_id/origin_id de quien la inició
    // (case_id del servicio, origin_id del registro). Los del query se usan solo si no hay registro.
    public async Task<IActionResult> Stop(string serial,
        [FromQuery] string? platform,
        [FromQuery(Name = "case_id")] string? queryCaseId,
        [FromQuery(Name = "origin_id")] string? queryOriginId,
        CancellationToken ct)
    {
        var plat = platform == "ios" ? "ios" : "android";
        var queryAttr = EventAttribution.From(queryCaseId, queryOriginId);
        try
        {
            string outputPath;
            IReadOnlyList<string> variants;
            string stoppedSerial;
            string? caseId;
            RecordingValidationDto? validation = null;
            if (plat == "ios")
            {
                var r = await ios.StopRecordingAsync(serial, CancellationToken.None);
                outputPath = r.OutputPath;
                variants = r.Variants;
                stoppedSerial = string.IsNullOrEmpty(r.Serial) ? serial : r.Serial;
                caseId = r.CaseId;
            }
            else
            {
                var r = await adb.StopRecordingAsync(serial, CancellationToken.None);
                outputPath = r.OutputPath;
                variants = [];
                stoppedSerial = r.Serial;
                caseId = r.CaseId;
                validation = r.Validation;
            }

            // ios-grabacion-larga §4.1/§4.2: variants_expected/variants siempre presentes. Si es true, Tatana
            // emite exactamente un video_variant_status terminal por cada etiqueta.
            var filename = Path.GetFileName(outputPath);
            var url = $"/files/{filename}";
            var variantsExpected = variants.Count > 0;
            var (evCaseId, evOriginId) = ResolveAttribution(stoppedSerial, serial, caseId, queryAttr);

            // DA10: queda en unclaimed_recordings hasta que se importe. Un archivo vacío (mock de iOS sin fuente)
            // no se ofrece: la importación lo rechazaría con empty_evidence.
            if ((validation is null || validation.Status is RecordingValidationStatus.Ok or RecordingValidationStatus.Incomplete)
                && FileLength(outputPath) > 0)
                registry.AddStopped(new UnclaimedRecordingDto(plat, stoppedSerial, filename, caseId,
                    validation?.StoppedAt ?? DateTimeOffset.UtcNow, validation));

            await hub.BroadcastAsync(new AgentEvent
            {
                Type = "recording_stopped",
                Data = new RecordingStoppedEvent(filename, url, plat, stoppedSerial, evCaseId, variantsExpected, variants,
                    validation, null, null, evOriginId),
            }, CancellationToken.None);

            return Ok(new StopRecordingResponse(filename, url, variantsExpected, variants, plat, stoppedSerial, caseId,
                validation));
        }
        catch (RecordingNotActiveException ex)
        {
            return Conflict(new { error = ex.Message, code = AgentErrorCodes.NotRecording });
        }
        catch (AgentHttpException ex)
        {
            if (ex.Code is AgentErrorCodes.RecordingEmpty or AgentErrorCodes.RecordingUnreadable)
            {
                var validation = ex.Extra is not null && ex.Extra.TryGetValue("validation", out var v)
                    ? v as RecordingValidationDto
                    : null;
                var failedSerial = ex.Data[AdbService.ExDataSerial] as string ?? serial;
                var (evCaseId, evOriginId) = ResolveAttribution(failedSerial, serial,
                    ex.Data[AdbService.ExDataCaseId] as string, queryAttr);
                await hub.BroadcastAsync(new AgentEvent
                {
                    Type = "recording_stopped",
                    Data = new RecordingStoppedEvent(null, null, plat, failedSerial, evCaseId,
                        false, [], validation, ex.Message, ex.Code, evOriginId),
                }, CancellationToken.None);
            }
            return StatusCode(ex.Status, ex.ToBody());
        }
        catch (IosException ex)
        {
            return StatusCode(500, new { error = ex.Message, code = ex.Code });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
        finally
        {
            // Haya salido bien o mal, el servicio ya limpió su estado: la atribución no sobrevive al stop.
            registry.TakeOwner(serial);
        }
    }

    /// <summary>
    /// case_id: el del servicio (grabacion-android-robusta) o, si no lo tiene, el del registro; origin_id: el del
    /// registro. Solo si no hay registro se usan los del query del stop. Consume el registro.
    /// </summary>
    private (string? CaseId, string? OriginId) ResolveAttribution(string recordingSerial, string routeSerial,
        string? serviceCaseId, EventAttribution queryAttr)
    {
        var owner = registry.TakeOwner(recordingSerial)
            ?? (recordingSerial != routeSerial ? registry.TakeOwner(routeSerial) : null);
        return owner is not null
            ? (serviceCaseId ?? owner.CaseId, owner.OriginId)
            : (serviceCaseId ?? queryAttr.CaseId, queryAttr.OriginId);
    }

    private static long FileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}

/// <summary>
/// Body del start. <c>case_id</c> (opcional) = caso que inició la grabación (Duda 5 A). <c>skip_screen_check</c>
/// (opcional, default false) = "Grabar igual": no chequea la pantalla de Android (pulido-post-qa-final D9).
/// </summary>
public sealed record StartRecordingRequest(
    int AndroidVersion,
    string Platform = "android",
    string? IosMode = null,
    bool AndroidWithMic = false,
    string? CaseId = null,
    string? OriginId = null,
    bool SkipScreenCheck = false);

/// <summary>200 del start (§6.1): <c>filename</c>, <c>state</c>, <c>started_at</c>, <c>case_id</c> (se omite si es null).</summary>
public sealed record StartRecordingResponse(string Filename, string State, DateTimeOffset StartedAt, string? CaseId);

/// <summary>200 del stop (§6.2). <c>validation</c> solo en Android (en iOS se omite).</summary>
public sealed record StopRecordingResponse(string Filename, string Url, bool VariantsExpected,
    IReadOnlyList<string> Variants, string Platform, string Serial, string? CaseId, RecordingValidationDto? Validation);

/// <summary>
/// <c>recording_stopped.data</c> (§6.4). En el WS los null viajan como null: en éxito <c>error</c>/<c>code</c>
/// son null; en una falla 422, <c>filename</c>/<c>url</c> son null. <c>origin_id</c> = pestaña que la inició
/// (wizard-concurrencia-pestanas DT4).
/// </summary>
public sealed record RecordingStoppedEvent(string? Filename, string? Url, string Platform, string Serial,
    string? CaseId, bool VariantsExpected, IReadOnlyList<string> Variants, RecordingValidationDto? Validation,
    string? Error, string? Code, string? OriginId = null);
