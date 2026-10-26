using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.Services.Ios;
using Factum.Agent.Services.Linux;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Controllers;

[ApiController]
[Route("/")]
public sealed class HealthController(IOptions<AgentOptions> opts, IIosService ios,
    ToolInventory inventory, ICaseZipService zips, AppleServiceProbe probe, LinuxHostProbe linuxProbe) : ControllerBase
{
    // tools: adb/scrcpy/ffmpeg/python/uxplay con found/source/path/version (SDD grabacion-android-windows
    // §4.2; ios-herramientas-windows §4.2 suma uxplay y python.pymobiledevice3_version). La
    // resolución es real también en mock. Las versiones salen de un caché que se llena en segundo
    // plano; la sonda del servicio de Apple tarda como mucho 500 ms y se cachea 5 s.
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var appleService = opts.Value.Mock ? AppleServiceState.Ok : await probe.GetStateAsync(ct);
        return Ok(new
        {
            status = "ok",
            // tatana-instalador-autoupdate D12/D-T1: la versión real (antes, siempre "2.0.0").
            version = AgentVersion.Current,
            mock = opts.Value.Mock,
            // tatana-linux §10.A: SO del agente ("windows" | "macos" | "linux") y, solo en Linux, distro + chequeos
            // de "Esta PC" (null fuera de Linux: no viaja por WhenWritingNull).
            os = HostOsInfo.JsonName(HostOsInfo.Current),
            linux = linuxProbe.GetStatus(),
            ios_available = ios.IsAvailable,
            tools = inventory.Snapshot(),
            ios = new
            {
                apple_service = AppleServiceProbe.ToJson(appleService),
                airplay_available = ios.AirplayAvailable,
                airplay_unavailable_reason = ios.AirplayUnavailableReason,
            },
            // zip-local-informe-servidor §6.1: la web detecta un Tatana viejo por esta lista.
            // ios_developer_mode_v1: POST /devices/{serial}/ios/developer-mode (ios-herramientas-windows §4.3).
            // real_version_v1: "version" es la real (D-T2; sin esto la web la trata como desconocida).
            // agent_state_v1: GET /agent/state y POST/DELETE /agent/maintenance (tatana-instalador-autoupdate §5.4).
            // video_postprocess_v1: variants_expected/variants en el stop, video_variant_status, GET
            // /video-postprocess/status y /agent/state.background_operations (ios-grabacion-larga §4.7).
            // recording_state_v1: case_id/started_at en el start, validation en el stop, recordings y
            // unclaimed_recordings en /agent/state, y los códigos nuevos (grabacion-android-robusta §5.12).
            // event_attribution_v1: serial/case_id/origin_id en screenshot_taken, photo_taken, recording_started y
            // recording_stopped (wizard-concurrencia-pestanas §5.1). device_preview_v1: POST /devices/{serial}/preview
            // + GET /previews/{filename}, sin evento WS (DT5).
            // screen_check_v1: screenshot y record/start de Android responden 409 screen_off/screen_locked con la
            // pantalla apagada o bloqueada; skip_screen_check los saltea (pulido-post-qa-final D9). La web no lo lee.
            // case_seals_v1: PUT /cases/{id}/zip/seals copia los .tsr y el LEEME a <carpeta del caso>/sellos/
            // (sellado-tiempo §7.3); sin esto la web ofrece solo la descarga.
            // device_details_v1: GET /devices/{serial}/details (ficha técnica de solo lectura + reloj) y GET
            // /devices/{serial}/clock (metadatos-dispositivo §7.2); sin esto la web crea el caso sin ficha.
            // recording_session_v1: grabación de la sesión completa en Android (grabacion-sesion-completa §7.4):
            // /cases/{id}/session/*, /session/capacity, eventos session_*, zip_folder en el ZIP, session_active en
            // record/start, session_file_protected y /agent/prepare-shutdown.
            // recording_session_camera_v1 (grabacion-sesion-completa-f2 §7.2.8): /cameras*, …/session/camera/*, camera en
            // el start y la ubicación de las marcas en la cámara. Solo con Agent:Session:CameraEnabled efectivo.
            capabilities = CapabilitiesFor(CameraEnabledFor(opts.Value)),
        });
    }

    // El client (que sí tiene el JWT del fiscal) usa esto para reenviar la
    // identidad de esta PC al backend y armar la auditoría de uso del agente —
    // el agente en sí no lleva ningún token, corre local sin auth.
    [HttpGet("info")]
    public IActionResult Info() => Ok(new
    {
        hostname = Environment.MachineName,
        os_user = Environment.UserName,
        version = AgentVersion.Current,
        mode = ResolveMode(opts.Value),
        // Carpeta base del ZIP (DP4) y si cae dentro de OneDrive/iCloud (la web lo avisa).
        evidence_directory = zips.EvidenceDirectory,
        evidence_directory_synced = Factum.Agent.Common.AgentFileNames.IsSyncedFolder(zips.EvidenceDirectory),
    });

    internal static readonly string[] Capabilities =
        ["case_evidence_v1", "ios_developer_mode_v1", "real_version_v1", "agent_state_v1", "video_postprocess_v1",
         "recording_state_v1", "event_attribution_v1", "device_preview_v1", "screen_check_v1", "case_seals_v1",
         "device_details_v1", "recording_session_v1", "extraction_android_v1"];
    // extraction_android_v1: extracción lógica de Android (extraccion-logica-android §8.1). /extractions*,
    // /devices/{serial}/extraction/*, /extraction/destinations*, eventos extraction_*. Sin esto la web muestra
    // "Actualizá Tatana para hacer extracciones" y no deja iniciar.

    public const string SessionCameraCapability = "recording_session_camera_v1";

    /// <summary>Lista de capabilities: la fija más <c>recording_session_camera_v1</c> si la cámara está habilitada.</summary>
    internal static string[] CapabilitiesFor(bool cameraEnabled) =>
        cameraEnabled ? [.. Capabilities, SessionCameraCapability] : Capabilities;

    /// <summary><c>Agent:Session:CameraEnabled</c> efectivo (null = solo macOS, D-T12).</summary>
    internal static bool CameraEnabledFor(AgentOptions o) => (o.Session ?? new Models.SessionOptions()).EffectiveCameraEnabled;

    /// <summary><c>--mode</c> gana (D-T15); sin él, la heurística de <c>tools/</c> (portátil).</summary>
    internal static string ResolveMode(AgentOptions o) => o.InstallMode ?? (IsPortable() ? "portable" : "installed");

    // Distingue el modo portátil (zip descomprimido con adb/pymobiledevice3
    // embebidos en tools/ al lado del exe) del instalado (Tatana vía Electron,
    // que resuelve esas herramientas por PATH del sistema).
    private static bool IsPortable() =>
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "tools"));
}
