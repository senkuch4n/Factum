using Factum.Backend.Controllers;
using Factum.Backend.Models;

namespace Factum.Backend.Tests;

/// <summary>AgentAction → tipo de evento de caso (trazabilidad-caso DT9 + wizard-concurrencia-pestanas DT11, B8).</summary>
public sealed class AgentAuditMappingTests
{
    [Theory]
    [InlineData(AgentAction.Screenshot, CaseEventTypes.CaptureScreenshot)]
    [InlineData(AgentAction.CaptureStart, CaseEventTypes.CaptureVideoStart)]
    [InlineData(AgentAction.CaptureStop, CaseEventTypes.CaptureVideoStop)]
    [InlineData(AgentAction.Webcam, CaseEventTypes.CapturePhoto)]
    [InlineData(AgentAction.CaptureFailed, CaseEventTypes.CaptureFailed)]
    public void Mapea_cada_accion(AgentAction action, string expected) =>
        Assert.Equal(expected, AgentAuditController.MapCaptureType(action));

    [Fact]
    public void Startup_no_genera_evento() => Assert.Null(AgentAuditController.MapCaptureType(AgentAction.Startup));

    [Fact]
    public void CaptureFailed_va_al_final_del_enum()
    {
        // Se guarda como int en Mongo: los valores viejos no se pueden correr.
        Assert.Equal(0, (int)AgentAction.Startup);
        Assert.Equal(4, (int)AgentAction.Webcam);
        Assert.Equal(5, (int)AgentAction.CaptureFailed);
    }

    [Theory]
    [InlineData(AgentAction.CaptureStart, true)]
    [InlineData(AgentAction.CaptureStop, true)]
    [InlineData(AgentAction.Screenshot, true)]
    [InlineData(AgentAction.Webcam, true)]
    [InlineData(AgentAction.Startup, false)]
    [InlineData(AgentAction.CaptureFailed, false)]
    [InlineData(null, false)]
    public void Acciones_que_pueden_fallar(AgentAction? action, bool ok) =>
        Assert.Equal(ok, AgentAuditController.IsFailableAction(action));

    [Fact]
    public void Motivo_se_recorta_y_trunca_a_300()
    {
        Assert.Null(AgentAuditController.TruncateReason(null));
        Assert.Null(AgentAuditController.TruncateReason("   "));
        Assert.Equal("Ya hay una grabación", AgentAuditController.TruncateReason("  Ya hay una grabación \n"));
        Assert.Equal(300, AgentAuditController.TruncateReason(new string('x', 400))!.Length);
        // No corta un emoji a la mitad.
        var withEmoji = new string('x', 299) + "😀" + "yyy";
        Assert.Equal(299, AgentAuditController.TruncateReason(withEmoji)!.Length);
    }
}
