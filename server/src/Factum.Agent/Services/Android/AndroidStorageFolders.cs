namespace Factum.Agent.Services.Android;

/// <summary>
/// Carpetas conocidas de <c>/sdcard</c> por app y por categoría (extraccion-logica-android §5.4.1). Se movió
/// <c>KnownAppFolders</c> desde <c>AdbService</c> (ahora la fuente única) y se sumó <see cref="ForCategory"/> con las
/// raíces por categoría de archivos. <c>internal static</c>: lo usan <c>AdbService</c> (búsqueda por app, sin cambio
/// de comportamiento) y el servicio de extracción.
/// </summary>
internal static class AndroidStorageFolders
{
    // WhatsApp tiene convención de nombre de archivo propia; Instagram/Facebook solo carpetas públicas.
    public static readonly Dictionary<string, string[]> KnownAppFolders = new()
    {
        ["whatsapp"] =
        [
            "/sdcard/WhatsApp/Media",
            "/sdcard/Android/media/com.whatsapp/WhatsApp/Media",
            "/sdcard/Android/media/com.whatsapp.w4b/WhatsApp Business/Media",
        ],
        ["instagram"] = ["/sdcard/Pictures/Instagram", "/sdcard/DCIM/Instagram", "/sdcard/Movies/Instagram"],
        ["facebook"] = ["/sdcard/Pictures/Facebook", "/sdcard/DCIM/Facebook", "/sdcard/Movies/Facebook"],
    };

    /// <summary>Raíces (ya normalizadas) de una categoría de archivos (§5.4.1). <c>custom</c> usa custom_paths del pedido.</summary>
    public static IReadOnlyList<string> ForCategory(string key) => key switch
    {
        "media" => ["/sdcard/DCIM", "/sdcard/Pictures", "/sdcard/Movies"],
        "downloads" => ["/sdcard/Download"],
        "documents" => ["/sdcard/Documents"],
        "audio" =>
        [
            "/sdcard/Music", "/sdcard/Recordings", "/sdcard/Call", "/sdcard/Sounds", "/sdcard/Podcasts",
            "/sdcard/Audiobooks", "/sdcard/Notifications", "/sdcard/Ringtones", "/sdcard/Alarms",
        ],
        "whatsapp_media" => KnownAppFolders["whatsapp"],
        "all_storage" => ["/sdcard"],
        _ => [],
    };
}
