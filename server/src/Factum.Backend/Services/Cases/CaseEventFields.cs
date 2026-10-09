using Factum.Backend.Models;

namespace Factum.Backend.Services.Cases;

/// <summary>
/// Compara dos <see cref="Case"/> (el leído antes del PUT y el guardado después) y devuelve los
/// NOMBRES snake_case de los campos de datos de la causa que cambiaron (trazabilidad-caso, DT6).
/// Sin valores: solo la lista de campos, para que el evento <c>case_updated</c> no exponga
/// contenido. Si la lista queda vacía, el servicio no registra el evento (evita ruido de un PUT
/// que no cambió nada).
/// </summary>
public static class CaseEventFields
{
    public static List<string> ChangedFields(Case before, Case after)
    {
        var changed = new List<string>();

        void Cmp(string field, string? a, string? b)
        {
            if (!string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal))
                changed.Add(field);
        }

        Cmp("nro_referencia", before.NroReferencia, after.NroReferencia);
        Cmp("nombre_denunciante", before.NombreDenunciante, after.NombreDenunciante);
        Cmp("dni_denunciante", before.DniDenunciante, after.DniDenunciante);
        Cmp("nombre_tribunal", before.NombreTribunal, after.NombreTribunal);
        Cmp("organismo_tribunal", before.OrganismoTribunal, after.OrganismoTribunal);
        Cmp("sala_tribunal", before.SalaTribunal, after.SalaTribunal);
        Cmp("tipo_causa", before.TipoCausa, after.TipoCausa);
        Cmp("caratula", before.Caratula, after.Caratula);
        Cmp("parte_denunciante", before.ParteDenunciante, after.ParteDenunciante);
        Cmp("parte_denunciada", before.ParteDenunciada, after.ParteDenunciada);
        Cmp("objeto_causa", before.ObjetoCausa, after.ObjetoCausa);
        Cmp("ambito_causa", before.AmbitoCausa, after.AmbitoCausa);
        Cmp("fecha_intervencion", before.FechaIntervencion, after.FechaIntervencion);
        Cmp("nombre_proponente", before.NombreProponente, after.NombreProponente);
        Cmp("profesion_proponente", before.ProfesionProponente, after.ProfesionProponente);
        Cmp("matricula_proponente", before.MatriculaProponente, after.MatriculaProponente);
        Cmp("tipo_dispositivo", before.TipoDispositivo, after.TipoDispositivo);
        Cmp("linea_dispositivo", before.LineaDispositivo, after.LineaDispositivo);
        Cmp("imei", before.Device?.Imei, after.Device?.Imei);
        Cmp("observaciones", before.Observaciones, after.Observaciones);

        // Integrantes: se compara la lista normalizada. Si cambió, se reporta como la frase legible
        // que ve el usuario (integrantes_tribunal), por DT6.
        if (!IntegrantesEqual(before.Integrantes, after.Integrantes) ||
            !string.Equals(before.IntegrantesTribunal ?? string.Empty,
                after.IntegrantesTribunal ?? string.Empty, StringComparison.Ordinal))
            changed.Add("integrantes_tribunal");

        return changed;
    }

    private static bool IntegrantesEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        var la = a ?? [];
        var lb = b ?? [];
        if (la.Count != lb.Count) return false;
        for (var i = 0; i < la.Count; i++)
            if (!string.Equals(la[i], lb[i], StringComparison.Ordinal)) return false;
        return true;
    }
}
