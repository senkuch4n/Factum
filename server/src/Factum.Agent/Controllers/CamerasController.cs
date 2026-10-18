using Factum.Agent.Common;
using Factum.Agent.Services;
using Factum.Agent.Services.Session;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

/// <summary>Body de <c>POST /cameras/preview</c> (§7.2.2).</summary>
public sealed record CameraPreviewRequest(string? Id = null, string? Name = null);

/// <summary>
/// Cámaras de la PC y vista previa antes de grabar (grabacion-sesion-completa-f2 §5.2, §5.9, §7.2.1–§7.2.3). Con la cámara
/// deshabilitada (<c>Agent:Session:CameraEnabled</c>), todo responde 400 <c>session_camera_unsupported</c>.
/// </summary>
[ApiController]
[Route("cameras")]
public sealed class CamerasController(SessionRecorder recorder, CameraDevices devices, CameraPreviewService preview)
    : ControllerBase
{
    private IActionResult Error(AgentHttpException ex) => StatusCode(ex.Status, ex.ToBody());

    private IActionResult? Unsupported() => recorder.CameraEnabled
        ? null
        : Error(new AgentHttpException(400, AgentErrorCodes.SessionCameraUnsupported,
            "La cámara del procedimiento no está habilitada en este Tatana."));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? refresh)
    {
        if (Unsupported() is { } u) return u;
        Response.Headers.CacheControl = "no-store";
        return Ok(await devices.ListAsync(refresh == true));
    }

    [HttpPost("preview")]
    public async Task<IActionResult> StartPreview([FromBody] CameraPreviewRequest? request)
    {
        if (Unsupported() is { } u) return u;
        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200 || name.Any(char.IsControl))
            return Error(new AgentHttpException(400, AgentErrorCodes.InvalidRequest, "Falta el nombre de la cámara o no es válido"));
        if (recorder.IsCameraRecording)
            return Error(new AgentHttpException(409, AgentErrorCodes.CameraBusy,
                "La cámara del procedimiento está grabando. Detenela para elegir otra."));
        var id = string.IsNullOrWhiteSpace(request!.Id) ? null : request.Id.Trim();
        return Ok(await preview.StartAsync(id, name));
    }

    [HttpDelete("preview")]
    public async Task<IActionResult> StopPreview()
    {
        if (Unsupported() is { } u) return u;
        await preview.StopAsync();
        return NoContent();
    }

    /// <summary>La de la grabación en curso o, si no hay, la de la previa. <c>?t=</c> se ignora.</summary>
    [HttpGet("preview.jpg")]
    public async Task<IActionResult> PreviewImage()
    {
        if (Unsupported() is { } u) return u;
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var path = recorder.SessionCameraPreviewPath;
        if (path is null)
        {
            preview.Touch();
            path = preview.ImagePath;
        }
        byte[]? bytes = null;
        try
        {
            if (System.IO.File.Exists(path)) bytes = await System.IO.File.ReadAllBytesAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { bytes = null; }
        if (bytes is null || bytes.Length == 0)
            return Error(new AgentHttpException(404, AgentErrorCodes.CameraPreviewUnavailable,
                "Todavía no hay vista previa de la cámara."));
        return File(bytes, "image/jpeg");
    }
}
