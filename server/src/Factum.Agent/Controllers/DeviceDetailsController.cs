using Factum.Agent.Common;
using Factum.Agent.Services.DeviceDetails;
using Factum.Agent.Services.Ios;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

// metadatos-dispositivo §7.2: ficha técnica y reloj del dispositivo, bajo demanda y de solo lectura. Son GET: el
// modo mantenimiento no los bloquea ni los cuenta OperationTracker. 404 { error, code: "device_not_found" } sin
// dispositivo (Android); iPhone: { error, code } con el código de IosException (404 si es ios_device_not_found).
[ApiController]
[Route("devices/{serial}")]
public sealed class DeviceDetailsController(IDeviceDetailsService details) : ControllerBase
{
    [HttpGet("details")]
    public Task<IActionResult> Details(string serial, [FromQuery] string? platform, CancellationToken ct) =>
        Run(platform, p => details.ReadAsync(serial, p, ct));

    [HttpGet("clock")]
    public Task<IActionResult> Clock(string serial, [FromQuery] string? platform, CancellationToken ct) =>
        Run(platform, p => details.MeasureClockAsync(serial, p, ct));

    private async Task<IActionResult> Run<T>(string? platform, Func<string, Task<T>> action)
    {
        var p = string.IsNullOrWhiteSpace(platform) ? DeviceDetailsService.Android : platform.Trim().ToLowerInvariant();
        if (p is not (DeviceDetailsService.Android or DeviceDetailsService.Ios))
            return BadRequest(new { error = "Plataforma inválida (android o ios).", code = AgentErrorCodes.InvalidRequest });
        try
        {
            return Ok(await action(p));
        }
        catch (DeviceNotFoundException ex)
        {
            return NotFound(new { error = ex.Message, code = AgentErrorCodes.DeviceNotFound });
        }
        catch (IosException ex)
        {
            var status = ex.Code == AgentErrorCodes.IosDeviceNotFound ? 404 : 500;
            return StatusCode(status, new { error = ex.Message, code = ex.Code });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
