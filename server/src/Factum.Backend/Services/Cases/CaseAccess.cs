using Factum.Backend.Models;

namespace Factum.Backend.Services.Cases;

/// <summary>api-hardening §4.7: resultado del chequeo de acceso a un caso.</summary>
public enum CaseAccessResult { Ok, NotFound, Foreign }

/// <summary>
/// Regla pura de acceso a un caso (D7 A). Quien llama mapea <see cref="CaseAccessResult.Foreign"/> a
/// 404 "Caso no encontrado" (igual que inexistente) y lo loguea.
/// </summary>
public static class CaseAccess
{
    /// <summary>null → NotFound; dueño → Ok; superadmin con <paramref name="allowSuperadmin"/> → Ok; resto → Foreign.</summary>
    public static CaseAccessResult Check(Case? cas, string requesterDni, bool allowSuperadmin = false,
        bool requesterIsSuperadmin = false)
    {
        if (cas is null) return CaseAccessResult.NotFound;
        if (cas.Officer.Dni == requesterDni) return CaseAccessResult.Ok;
        if (allowSuperadmin && requesterIsSuperadmin) return CaseAccessResult.Ok;
        return CaseAccessResult.Foreign;
    }
}
