using System.Globalization;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Reports;

namespace Factum.Backend.Services.Cases;

/// <summary>Campos de la causa ya normalizados (trim; null → "").</summary>
public sealed record CaseFields(
    string NroReferencia, string NombreDenunciante, string DniDenunciante,
    string NombreTribunal, string OrganismoTribunal, string SalaTribunal, string IntegrantesTribunal,
    string TipoCausa, string Caratula, string ParteDenunciante, string ParteDenunciada,
    string ObjetoCausa, string AmbitoCausa, string FechaIntervencion,
    string NombreProponente, string ProfesionProponente, string MatriculaProponente,
    string TipoDispositivo, string LineaDispositivo, string? Observaciones,
    // null = el request no trajo la lista (cliente viejo): vale IntegrantesTribunal tal cual.
    // No null = lista limpia (sin vacíos, en orden) e IntegrantesTribunal = Join(lista).
    IReadOnlyList<string>? Integrantes = null)
{
    public static CaseFields From(CreateCaseRequest r)
    {
        var integrantes = r.Integrantes is null ? null : IntegrantesFormatter.Clean(r.Integrantes);
        return new(
            C(r.NroReferencia), C(r.NombreDenunciante), C(r.DniDenunciante),
            C(r.NombreTribunal), C(r.OrganismoTribunal), C(r.SalaTribunal),
            integrantes is null ? C(r.IntegrantesTribunal) : IntegrantesFormatter.Join(integrantes),
            C(r.TipoCausa), C(r.Caratula), C(r.ParteDenunciante), C(r.ParteDenunciada),
            C(r.ObjetoCausa), C(r.AmbitoCausa), C(r.FechaIntervencion),
            C(r.NombreProponente), C(r.ProfesionProponente), C(r.MatriculaProponente),
            C(r.TipoDispositivo), C(r.LineaDispositivo), r.Observaciones?.Trim(), integrantes);
    }

    public static CaseFields From(UpdateCaseRequest r)
    {
        var integrantes = r.Integrantes is null ? null : IntegrantesFormatter.Clean(r.Integrantes);
        return new(
            C(r.NroReferencia), C(r.NombreDenunciante), C(r.DniDenunciante),
            C(r.NombreTribunal), C(r.OrganismoTribunal), C(r.SalaTribunal),
            integrantes is null ? C(r.IntegrantesTribunal) : IntegrantesFormatter.Join(integrantes),
            C(r.TipoCausa), C(r.Caratula), C(r.ParteDenunciante), C(r.ParteDenunciada),
            C(r.ObjetoCausa), C(r.AmbitoCausa), C(r.FechaIntervencion),
            C(r.NombreProponente), C(r.ProfesionProponente), C(r.MatriculaProponente),
            C(r.TipoDispositivo), C(r.LineaDispositivo), r.Observaciones?.Trim(), integrantes);
    }

    private static string C(string? s) => (s ?? string.Empty).Trim();
}

/// <summary>Resultado de una validación: obligatorios faltantes y/o un error (largo o valor inválido).</summary>
public sealed record ValidationOutcome(IReadOnlyList<string> Missing, string? Error)
{
    public bool IsValid => Missing.Count == 0 && Error is null;
}

/// <summary>
/// Obligatorios y largos máximos del informe pericial. Las claves de <c>missing</c> son las de
/// la SDD §6.4 y tienen que ser idénticas a las de <c>client/src/lib/pericial.ts</c>.
/// </summary>
public static class CaseValidation
{
    public const int MaxLine = 300;
    public const int MaxLong = 500;
    public const int MaxText = 20_000;
    public const int MaxProfileText = 150;
    public const int MaxMatricula = 60;
    public const int MaxIntegrantes = 20;
    public const string TooManyIntegrantesMessage = "Se pueden cargar hasta 20 integrantes";
    public const string ManualImei = "INGRESAR_MANUALMENTE";
    public const string MissingMessage = "Faltan datos obligatorios";

    // ── Claves de missing (§6.4) ─────────────────────────────────────────────
    public const string KeyPerfil = "perfil";
    public const string KeyPerito = "perito";
    public const string KeyImei = "imei";
    public const string KeyCapturaImei = "capture_roles.imei_modelo";

    // ── Perfil ────────────────────────────────────────────────────────────────

    public static ValidationOutcome ValidateProfile(string nombre, string matricula, string profesion,
        string caracter, string? tratamiento)
    {
        var missing = new List<string>();
        if (nombre.Length == 0) missing.Add("nombre");
        if (matricula.Length == 0) missing.Add("matricula");
        if (profesion.Length == 0) missing.Add("profesion");
        if (caracter.Length == 0) missing.Add("caracter");

        string? error = null;
        if (nombre.Length > MaxProfileText) error = TooLong("nombre", MaxProfileText);
        else if (matricula.Length > MaxMatricula) error = TooLong("matricula", MaxMatricula);
        else if (profesion.Length > MaxProfileText) error = TooLong("profesion", MaxProfileText);
        else if (caracter.Length > MaxProfileText) error = TooLong("caracter", MaxProfileText);
        else if (tratamiento is not null && tratamiento.Trim().Length > 0 &&
                 tratamiento.Trim() is not (ExpertProfile.TratamientoSuscripto or ExpertProfile.TratamientoSuscripta))
            error = "El tratamiento tiene que ser \"suscripto\" o \"suscripta\"";

        return new ValidationOutcome(missing, error);
    }

    // ── Datos de la causa (crear / editar) ───────────────────────────────────

    public static ValidationOutcome ValidateCaseData(CaseFields f, string imei)
    {
        var lengthError = ValidateIntegrantes(f) ?? CheckLengths(
            ("nro_referencia", f.NroReferencia, MaxLine),
            ("nombre_denunciante", f.NombreDenunciante, MaxLine),
            ("dni_denunciante", f.DniDenunciante, MaxLine),
            ("nombre_tribunal", f.NombreTribunal, MaxLine),
            ("organismo_tribunal", f.OrganismoTribunal, MaxLine),
            ("sala_tribunal", f.SalaTribunal, MaxLine),
            // Con lista, el texto es derivado y queda exento del límite de 500 (D6).
            ("integrantes_tribunal", f.Integrantes is null ? f.IntegrantesTribunal : string.Empty, MaxLong),
            ("tipo_causa", f.TipoCausa, MaxLine),
            ("caratula", f.Caratula, MaxLong),
            ("parte_denunciante", f.ParteDenunciante, MaxLine),
            ("parte_denunciada", f.ParteDenunciada, MaxLine),
            ("objeto_causa", f.ObjetoCausa, MaxLine),
            ("ambito_causa", f.AmbitoCausa, MaxLine),
            ("fecha_intervencion", f.FechaIntervencion, MaxLine),
            ("nombre_proponente", f.NombreProponente, MaxLine),
            ("profesion_proponente", f.ProfesionProponente, MaxLine),
            ("matricula_proponente", f.MatriculaProponente, MaxLine),
            ("tipo_dispositivo", f.TipoDispositivo, MaxLine),
            ("linea_dispositivo", f.LineaDispositivo, MaxLine),
            ("imei", imei, MaxLine),
            // Observaciones ya no se pide; si un cliente viejo la manda, se acepta como texto largo.
            ("observaciones", f.Observaciones ?? string.Empty, MaxText));

        return new ValidationOutcome(MissingCaseFields(f, imei), lengthError);
    }

    // Lista de integrantes: hasta 20 ítems de hasta 300 caracteres cada uno (D6).
    private static string? ValidateIntegrantes(CaseFields f)
    {
        if (f.Integrantes is null) return null;
        if (f.Integrantes.Count > MaxIntegrantes) return TooManyIntegrantesMessage;
        foreach (var item in f.Integrantes)
            if (item.Length > MaxLine) return TooLong("integrantes", MaxLine);
        return null;
    }

    private static List<string> MissingCaseFields(CaseFields f, string imei)
    {
        var missing = new List<string>();
        if (f.NombreTribunal.Length == 0) missing.Add("nombre_tribunal");
        if (f.TipoCausa.Length == 0) missing.Add("tipo_causa");
        if (f.NroReferencia.Length == 0) missing.Add("nro_referencia");
        if (f.Caratula.Length == 0) missing.Add("caratula");
        if (f.ParteDenunciante.Length == 0) missing.Add("parte_denunciante");
        if (f.ParteDenunciada.Length == 0) missing.Add("parte_denunciada");
        if (!IsValidDate(f.FechaIntervencion)) missing.Add("fecha_intervencion");
        if (f.NombreProponente.Length == 0) missing.Add("nombre_proponente");
        if (f.NombreDenunciante.Length == 0) missing.Add("nombre_denunciante");
        if (f.TipoDispositivo.Length == 0) missing.Add("tipo_dispositivo");
        if (IsMissingImei(imei)) missing.Add(KeyImei);
        return missing;
    }

    // ── Antes de generar (§7.8) ──────────────────────────────────────────────

    /// <param name="hasImeiCapture">Hay al menos una captura marcada imei_modelo que existe y no está vacía.</param>
    public static IReadOnlyList<string> ValidateForGenerate(Case cas, bool hasImeiCapture)
    {
        var missing = new List<string>();
        if (cas.Perito is null || !cas.Perito.IsComplete) missing.Add(KeyPerito);

        var f = new CaseFields(cas.NroReferencia.Trim(), cas.NombreDenunciante.Trim(), cas.DniDenunciante.Trim(),
            cas.NombreTribunal.Trim(), cas.OrganismoTribunal.Trim(), cas.SalaTribunal.Trim(),
            cas.IntegrantesTribunal.Trim(), cas.TipoCausa.Trim(), cas.Caratula.Trim(),
            cas.ParteDenunciante.Trim(), cas.ParteDenunciada.Trim(), cas.ObjetoCausa.Trim(),
            cas.AmbitoCausa.Trim(), cas.FechaIntervencion.Trim(), cas.NombreProponente.Trim(),
            cas.ProfesionProponente.Trim(), cas.MatriculaProponente.Trim(), cas.TipoDispositivo.Trim(),
            cas.LineaDispositivo.Trim(), null, Integrantes: null);
        missing.AddRange(MissingCaseFields(f, cas.Device.Imei.Trim()));

        var t = cas.ReportTexts;
        // "Vacío" según el formato del caso (editor-texto-enriquecido §4.5).
        if (ReportTextRules.IsBlank(t, t?.OperacionesRealizadas)) missing.Add("report_texts.operaciones_realizadas");
        if (ReportTextRules.IsBlank(t, t?.AseguramientoEvidencia)) missing.Add("report_texts.aseguramiento_evidencia");
        if (ReportTextRules.IsBlank(t, t?.Resultados)) missing.Add("report_texts.resultados");
        if (ReportTextRules.IsBlank(t, t?.ValoracionTecnica)) missing.Add("report_texts.valoracion_tecnica");
        if (ReportTextRules.IsBlank(t, t?.Conclusiones)) missing.Add("report_texts.conclusiones");

        if (!hasImeiCapture) missing.Add(KeyCapturaImei);
        return missing;
    }

    // ── Textos del paso Informe ──────────────────────────────────────────────

    public const string InvalidFormatMessage = "El campo formato no es válido";

    /// <summary>
    /// Formato, largos y contenido (editor-texto-enriquecido §4.2 y §6.6). El tope cuenta el
    /// string guardado (D9 A). En Markdown se rechaza el HTML (salvo &lt;u&gt;), las imágenes que
    /// no son una captura del caso como párrafo propio (editor-imagenes-informe §4.3), más de 20
    /// imágenes por sección y los enlaces no permitidos; en texto plano no se interpreta nada.
    /// </summary>
    public static string? ValidateReportTexts(ReportTextsDto d)
    {
        if (!ReportTextFormats.TryNormalize(d.Formato, out var formato)) return InvalidFormatMessage;

        var fields = new (string Key, string Value)[]
        {
            ("objeto_informe", d.ObjetoInforme ?? ""),
            ("operaciones_realizadas", d.OperacionesRealizadas ?? ""),
            ("aseguramiento_evidencia", d.AseguramientoEvidencia ?? ""),
            ("resultados", d.Resultados ?? ""),
            ("valoracion_tecnica", d.ValoracionTecnica ?? ""),
            ("conclusiones", d.Conclusiones ?? ""),
            ("notas_tecnicas", d.NotasTecnicas ?? ""),
            ("reserva", d.Reserva ?? ""),
        };

        var lengthError = CheckLengths(fields.Select(f => (f.Key, f.Value, MaxText)).ToArray());
        if (lengthError is not null || formato != ReportTextFormats.Markdown) return lengthError;

        foreach (var (key, value) in fields)
            if (ReportMarkdown.Validate(value) is { } reason)
                return reason == ReportMarkdown.TooManyImages
                    ? TooManyImagesMessage(key)
                    : $"El campo {key} tiene contenido no permitido: {reason}";
        return null;
    }

    // ── Imágenes del informe (editor-imagenes-informe §4.3 y §4.6) ───────────

    public static string TooManyImagesMessage(string key) =>
        $"El campo {key} supera las {ReportImageRef.MaxPerSection} imágenes";

    /// <summary>Clave de <c>missing</c> de una sección con una captura no disponible.</summary>
    public static string ReportImageKey(string clave) => $"report_texts.{clave}.imagen";

    /// <summary>
    /// Claves <c>report_texts.&lt;clave&gt;.imagen</c> de las secciones con alguna imagen no
    /// disponible, una por sección y en el orden de las secciones. Solo con Formato = markdown.
    /// Es pura: la E/S entra por <paramref name="isAvailable"/>.
    /// </summary>
    public static IReadOnlyList<string> BrokenImageKeys(ReportTexts? t, Func<string, bool> isAvailable)
    {
        if (t is null || t.Formato != ReportTextFormats.Markdown) return [];
        var sections = new (string Key, string? Value)[]
        {
            ("objeto_informe", t.ObjetoInforme),
            ("operaciones_realizadas", t.OperacionesRealizadas),
            ("aseguramiento_evidencia", t.AseguramientoEvidencia),
            ("resultados", t.Resultados),
            ("valoracion_tecnica", t.ValoracionTecnica),
            ("conclusiones", t.Conclusiones),
            ("notas_tecnicas", t.NotasTecnicas),
            ("reserva", t.Reserva),
        };
        var keys = new List<string>();
        var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (key, value) in sections)
        {
            foreach (var (filename, _) in ReportMarkdown.ExtractImages(value))
            {
                if (!cache.TryGetValue(filename, out var ok)) cache[filename] = ok = isAvailable(filename);
                if (ok) continue;
                keys.Add(ReportImageKey(key));
                break;
            }
        }
        return keys;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static bool IsMissingImei(string? imei)
    {
        var v = imei?.Trim() ?? string.Empty;
        return v.Length == 0 || v == ManualImei;
    }

    public static bool IsValidDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static string? CheckLengths(params (string Key, string Value, int Max)[] fields)
    {
        foreach (var (key, value, max) in fields)
            if (value.Length > max) return TooLong(key, max);
        return null;
    }

    private static string TooLong(string key, int max) => $"El campo {key} supera los {max} caracteres";
}
