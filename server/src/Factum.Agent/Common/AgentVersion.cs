using System.Reflection;

namespace Factum.Agent.Common;

/// <summary>
/// Versión real de Tatana (SDD tatana-instalador-autoupdate D12, D-T1). Sale de
/// <c>AssemblyInformationalVersion</c>, que fija <c>dotnet publish -p:Version=X.Y.Z</c> (armado del
/// portátil y workflow de release). Un build local dice <c>0.0.0-dev</c> (el <c>&lt;Version&gt;</c> del
/// .csproj). La reportan <c>/health.version</c>, <c>/info.version</c> y <c>/agent/state.version</c>.
/// </summary>
public static class AgentVersion
{
    public const string Fallback = "0.0.0-dev";

    // Se lee del ensamblado de Factum.Agent (no del "entry assembly"): es el mismo en producción y
    // en los tests el entry assembly sería el host de xunit.
    public static string Current { get; } = Normalize(
        typeof(AgentVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Corta la metadata de build (<c>+sha</c>) y recorta; null o vacío → <see cref="Fallback"/>.</summary>
    internal static string Normalize(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return Fallback;
        var plus = informational.IndexOf('+');
        var v = (plus >= 0 ? informational[..plus] : informational).Trim();
        return v.Length == 0 ? Fallback : v;
    }
}
