using Factum.Agent.Models;
using Factum.Agent.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Controllers;

[ApiController]
[Route("/")]
public sealed class HealthController(IOptions<AgentOptions> opts, IIosService ios,
    ToolInventory inventory, ICaseZipService zips) : ControllerBase
{
    // tools: adb/scrcpy/ffmpeg/python con found/source/path/version (SDD grabacion-android-windows
    // §4.2). La resolución es real también en mock. Sincrónico e instantáneo: las versiones salen
    // de un caché que se llena en segundo plano.
    [HttpGet("health")]
    public IActionResult Health() => Ok(new
    {
        status = "ok",
        version = "2.0.0",
        mock = opts.Value.Mock,
        ios_available = ios.IsAvailable,
        tools = inventory.Snapshot(),
        // zip-local-informe-servidor §6.1: la web detecta un Tatana viejo por esta lista.
        capabilities = new[] { "case_evidence_v1" },
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
        // Carpeta base del ZIP (DP4) y si cae dentro de OneDrive/iCloud (la web lo avisa).
        evidence_directory = zips.EvidenceDirectory,
        evidence_directory_synced = Factum.Agent.Common.AgentFileNames.IsSyncedFolder(zips.EvidenceDirectory),
    });

    // Distingue el modo portátil (zip descomprimido con adb/pymobiledevice3
    // embebidos en tools/ al lado del exe) del instalado (Tatana vía Electron,
    // que resuelve esas herramientas por PATH del sistema).
    private static bool IsPortable() =>
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "tools"));
}
