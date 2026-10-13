namespace Factum.Agent.Services;

/// <summary>
/// Stop sin grabación en curso (Android o iOS). <c>RecordingController.Stop</c> la traduce a 409
/// <c>not_recording</c>. Mismo nombre, texto y status que la HU tatana-ws-y-hosts (T-14).
/// </summary>
public sealed class RecordingNotActiveException() : InvalidOperationException(DefaultMessage)
{
    public const string DefaultMessage = "No hay una grabación en curso.";
}
