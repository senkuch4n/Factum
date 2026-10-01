using Factum.Agent.Services;
using Factum.Agent.WebSockets;
using Factum.Agent.Models;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

[ApiController]
[Route("devices")]
public sealed class DevicesController(
    IAdbService adb,
    IIosService ios,
    AgentWebSocketHub hub) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var androidDevices = await adb.ListDevicesAsync(ct);
        var iosDevices = await ios.ListDevicesAsync(ct);
        var all = androidDevices.Concat(iosDevices).ToList();

        await hub.BroadcastAsync(new AgentEvent
        {
            Type = "devices_changed",
            Data = new { devices = all }
        }, ct);

        return Ok(new { devices = all });
    }

    [HttpGet("{serial}/info")]
    public async Task<IActionResult> Info(string serial, CancellationToken ct)
    {
        var devices = (await adb.ListDevicesAsync(ct))
            .Concat(await ios.ListDevicesAsync(ct));

        var device = devices.FirstOrDefault(d => d.Serial == serial);
        return device is null ? NotFound(new { error = "Dispositivo no encontrado" }) : Ok(device);
    }
}
