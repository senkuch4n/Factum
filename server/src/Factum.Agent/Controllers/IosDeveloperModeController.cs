using Factum.Agent.Services;
using Factum.Agent.Services.Ios;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

// Modo Desarrollador del iPhone desde la web (SDD ios-herramientas-windows §4.3, D5): reemplaza el
// comando de Terminal de la Mac. 200 { status: "enabled" | "restarting" | "manual_required" };
// 500 { error, code } con los códigos de §4.1.
[ApiController]
[Route("devices/{serial}/ios/developer-mode")]
public sealed class IosDeveloperModeController(IIosService ios) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Enable(string serial, CancellationToken ct)
    {
        try
        {
            var status = await ios.EnableDeveloperModeAsync(serial, ct);
            return Ok(new { status });
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
