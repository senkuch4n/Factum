using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

[BsonIgnoreExtraElements]
public sealed class DeviceInfo
{
    public string Serial { get; set; } = string.Empty;
    /// <summary>Nombre del dispositivo que reporta el agente (device.name). "" en casos viejos.</summary>
    public string Name { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int AndroidVersion { get; set; }
    public string Imei { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public string OsVersion { get; set; } = string.Empty;

    /// <summary>
    /// "Android 11" / "iOS 17.4". Idempotente (informe-formato-fixes §6.3): si <see cref="OsVersion"/>
    /// ya trae el prefijo de la plataforma ("Android 11", "android 11", "iOS 17.4") sale tal cual,
    /// así no queda "Android Android 11". No se persiste (getter).
    /// </summary>
    public string OsLabel
    {
        get
        {
            var prefix = Platform == "ios" ? "iOS" : "Android";
            var v = (OsVersion ?? "").Trim();
            if (v.Length == 0) return $"{prefix} {AndroidVersion}";
            if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (v.Length == prefix.Length || !char.IsLetter(v[prefix.Length])))
                return v;
            return $"{prefix} {v}";
        }
    }
}
