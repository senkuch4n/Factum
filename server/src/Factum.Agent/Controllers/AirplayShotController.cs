using Factum.Agent.Services;
using Factum.Agent.Services.Ios;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

// Sesión de "espejar para capturas" (iOS + AirPlay): conecta una vez, permite marcar N momentos
// mientras el fiscal navega libremente por el teléfono, y extrae un PNG por marca al finalizar.
// No aplica a Android, por eso no toma un query param "platform" como el resto de los endpoints.
[ApiController]
[Route("devices/{serial}/screenshot/airplay")]
public sealed class AirplayShotController(IIosService ios) : ControllerBase
{
    [HttpPost("start")]
    public async Task<IActionResult> Start(string serial, CancellationToken ct)
    {
        try
        {
            var receiverName = await ios.StartAirplayShotSessionAsync(ct);
            return Ok(new { receiver_name = receiverName });
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

    [HttpPost("mark")]
    public async Task<IActionResult> Mark(string serial, CancellationToken ct)
    {
        try
        {
            var count = await ios.MarkAirplayShotAsync(ct);
            return Ok(new { count });
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

    [HttpPost("stop")]
    public async Task<IActionResult> Stop(string serial, CancellationToken ct)
    {
        try
        {
            var files = await ios.StopAirplayShotSessionAsync(ct);
            return Ok(new { files = files.Select(f => new { filename = f.Filename, url = f.Url }) });
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
}
