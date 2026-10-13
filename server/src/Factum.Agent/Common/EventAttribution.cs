using System.Text.RegularExpressions;

namespace Factum.Agent.Common;

/// <summary>
/// Atribución de un evento WS (wizard-concurrencia-pestanas §5.1, DT1): qué caso y qué pestaña
/// (<c>origin_id</c>) pidieron la captura. Es solo un dato de atribución: un valor inválido se trata como
/// null, nunca como 400.
/// </summary>
public sealed partial record EventAttribution(string? CaseId, string? OriginId)
{
    public static readonly EventAttribution None = new(null, null);

    public static EventAttribution From(string? caseId, string? originId) =>
        new(Normalize(caseId), Normalize(originId));

    /// <summary>
    /// Trim; si no matchea <c>^[A-Za-z0-9_-]{1,64}$</c> → null. Un GUID se devuelve en su forma canónica
    /// (<c>"D"</c>, minúsculas), la misma que usa <c>record/start</c> (<see cref="AgentFileNames.TryParseCaseId"/>),
    /// así el <c>case_id</c> de una captura y el de una grabación del mismo caso son iguales.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value) || !Allowed().IsMatch(value)) return null;
        return AgentFileNames.TryParseCaseId(value, out var canonical) ? canonical : value;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Allowed();
}
