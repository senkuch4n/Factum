using Factum.Agent.WebSockets;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

[ApiController]
public sealed class WebSocketController(AgentWebSocketHub hub) : ControllerBase
{
    [Route("/ws")]
    public async Task Connect()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await hub.HandleAsync(ws, HttpContext.RequestAborted);
    }
}
