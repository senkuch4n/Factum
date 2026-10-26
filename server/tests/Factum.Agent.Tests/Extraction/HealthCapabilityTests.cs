using Factum.Agent.Controllers;

namespace Factum.Agent.Tests.Extraction;

public class HealthCapabilityTests
{
    [Fact]
    public void ExtractionCapability_EnLaLista()
    {
        Assert.Contains("extraction_android_v1", HealthController.Capabilities);
    }

    [Fact]
    public void ExtractionCapability_TambienConCamara()
    {
        Assert.Contains("extraction_android_v1", HealthController.CapabilitiesFor(cameraEnabled: true));
    }

    [Fact]
    public void ExtractionReportCapability_EnLaLista()
    {
        Assert.Contains("extraction_report_v1", HealthController.Capabilities);
        Assert.Contains("extraction_report_v1", HealthController.CapabilitiesFor(cameraEnabled: true));
    }
}
