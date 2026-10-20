namespace Factum.Agent.Common;

/// <summary>SO donde corre Tatana (SDD tatana-linux §5.1). Decide textos y comportamiento por plataforma.</summary>
public enum HostOs { Windows, MacOS, Linux }

public static class HostOsInfo
{
    /// <summary>SO actual. Cualquier Unix que no sea macOS se trata como Linux.</summary>
    public static HostOs Current { get; } =
        OperatingSystem.IsWindows() ? HostOs.Windows
        : OperatingSystem.IsMacOS() ? HostOs.MacOS
        : HostOs.Linux;

    /// <summary>Valor del campo <c>os</c> de <c>/health</c>: <c>windows</c> | <c>macos</c> | <c>linux</c>.</summary>
    public static string JsonName(HostOs os) => os switch
    {
        HostOs.Windows => "windows",
        HostOs.MacOS => "macos",
        _ => "linux",
    };
}
