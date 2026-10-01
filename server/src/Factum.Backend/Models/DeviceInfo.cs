namespace Factum.Backend.Models;

public sealed class DeviceInfo
{
    public string Serial { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int AndroidVersion { get; set; }
    public string Imei { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public string OsVersion { get; set; } = string.Empty;

    public string OsLabel => Platform == "ios"
        ? $"iOS {(string.IsNullOrEmpty(OsVersion) ? AndroidVersion.ToString() : OsVersion)}"
        : $"Android {(string.IsNullOrEmpty(OsVersion) ? AndroidVersion.ToString() : OsVersion)}";
}
