using Factum.Agent.Models;
using Factum.Agent.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Controllers;

[ApiController]
[Route("/")]
public sealed class HealthController(IOptions<AgentOptions> opts, IIosService ios) : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Health() => Ok(new
    {
        status = "ok",
        version = "2.0.0",
        mock = opts.Value.Mock,
        ios_available = ios.IsAvailable
    });

    // El client (que sí tiene el JWT del fiscal) usa esto para reenviar la
    // identidad de esta PC al backend y armar la auditoría de uso del agente —
    // el agente en sí no lleva ningún token, corre local sin auth.
    [HttpGet("info")]
    public IActionResult Info() => Ok(new
    {
        hostname = Environment.MachineName,
        os_user = Environment.UserName,
        version = "2.0.0",
        mode = IsPortable() ? "portable" : "installed",
    });

    // Distingue el modo portátil (zip descomprimido con adb/pymobiledevice3
    // embebidos en tools/ al lado del exe) del instalado (Tatana vía Electron,
    // que resuelve esas herramientas por PATH del sistema).
    private static bool IsPortable() =>
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "tools"));
}
