using System.Globalization;
using System.Text;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Valores de los placeholders del informe (§7.2) y de los tokens de los textos por defecto
/// (§7.6). Compartido por <see cref="ReportService"/> y el endpoint de defaults.
/// </summary>
public static class ReportValues
{
    public const string NoInformado = "No informado";

    /// <summary>
    /// §7.2 + informe-formato-fixes §5.3: primero <see cref="SafeText"/> (multilínea: quita C0 salvo
    /// \t \n \r, bidi, no-caracteres y surrogates sueltos; conserva DEL/C1), después \r\n → \n,
    /// \r → \n y \t → espacio. Espejo exacto de <c>cleanText</c> en <c>client/src/lib/report-markdown.ts</c>.
    /// </summary>
    public static string Clean(string? value)
    {
        var s = SafeText.Clean(value, TextShape.MultiLine);
        if (s.Length == 0) return s;
        return s.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
    }

    /// <summary>
    /// informe-formato-fixes §6.5: hasta <paramref name="maxTextElements"/> elementos de texto
    /// (<see cref="StringInfo"/>: no parte pares surrogate ni secuencias ZWJ). Si pasa, se cortan
    /// max-1, TrimEnd y "…".
    /// </summary>
    public static string Ellipsize(string value, int maxTextElements)
    {
        if (string.IsNullOrEmpty(value) || maxTextElements <= 0) return value ?? string.Empty;
        var info = new StringInfo(value);
        if (info.LengthInTextElements <= maxTextElements) return value;
        return info.SubstringByTextElements(0, Math.Max(0, maxTextElements - 1)).TrimEnd() + "…";
    }

    public static string ElSuscripto(PeritoSnapshot? perito) =>
        perito?.Tratamiento == ExpertProfile.TratamientoSuscripta ? "La suscripta" : "El suscripto";

    public static string MarcaModelo(DeviceInfo d)
    {
        var manufacturer = d.Manufacturer.Trim();
        var model = d.Model.Trim();
        if (manufacturer.Length == 0) return model;
        if (model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase)) return model;
        return $"{manufacturer} {model}".Trim();
    }

    public static (string Fecha, string Hora) Inspeccion(DateTime createdAt, TimeZoneInfo zone)
    {
        var utc = createdAt.Kind switch
        {
            DateTimeKind.Utc => createdAt,
            DateTimeKind.Local => createdAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
        };
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        return (local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    /// <summary>"yyyy-MM-dd" → "dd/MM/yyyy". Si no parsea, el valor tal cual.</summary>
    public static string FechaIntervencion(string iso) =>
        DateOnly.TryParseExact(iso.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : iso.Trim();

    private static string OrNoInformado(string value) =>
        string.IsNullOrWhiteSpace(value) ? NoInformado : value.Trim();

    /// <summary>Diccionario "{placeholder}" → valor sanitizado, para el reemplazo de texto (B-R6).</summary>
    public static Dictionary<string, string> Placeholders(Case cas, IReportSettings settings,
        BrandingSnapshot brand)
    {
        var p = cas.Perito ?? new PeritoSnapshot();
        var (fecha, hora) = Inspeccion(cas.CreatedAt, settings.Zone);

        var tribunal = cas.NombreTribunal.Trim();
        var sala = cas.SalaTribunal.Trim();
        // {fraseIntegracion} sale del TEXTO IntegrantesTribunal, no de la lista Integrantes
        // (formulario-caso-catalogos, SDD §6): en un caso guardado con lista, el servidor lo
        // deriva con IntegrantesFormatter.Join en la misma escritura; en un caso viejo es el texto
        // de siempre. Así los casos viejos y un rollback generan el mismo informe.
        var integrantes = cas.IntegrantesTribunal.Trim();

        var proponente = new StringBuilder(cas.NombreProponente.Trim());
        if (!string.IsNullOrWhiteSpace(cas.ProfesionProponente))
            proponente.Append(" – ").Append(cas.ProfesionProponente.Trim());
        if (!string.IsNullOrWhiteSpace(cas.MatriculaProponente))
            proponente.Append(", M.P. ").Append(cas.MatriculaProponente.Trim());

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{nombreTribunal}"] = tribunal,
            ["{organismoTribunal}"] = cas.OrganismoTribunal.Trim(),
            ["{nombrePerito}"] = p.Nombre.Trim(),
            ["{matriculaPerito}"] = p.Matricula.Trim(),
            ["{profesionPerito}"] = p.Profesion.Trim(),
            ["{caracterPerito}"] = p.Caracter.Trim(),
            ["{elSuscripto}"] = ElSuscripto(cas.Perito),
            ["{fraseDomicilio}"] = settings.DomicilioConstituido.Length > 0
                ? ", con domicilio constituido en " + settings.DomicilioConstituido
                : string.Empty,
            ["{tipoCausa}"] = cas.TipoCausa.Trim(),
            ["{numeroCausa}"] = cas.NroReferencia.Trim(),
            ["{parteDenunciante}"] = cas.ParteDenunciante.Trim(),
            ["{parteDenunciada}"] = cas.ParteDenunciada.Trim(),
            ["{caratula}"] = cas.Caratula.Trim(),
            ["{tramiteAnte}"] = sala.Length > 0 ? $"{sala} de {tribunal}" : tribunal,
            ["{fraseIntegracion}"] = integrantes.Length > 0 ? ", con la integración de " + integrantes : string.Empty,
            ["{objetoCausa}"] = OrNoInformado(cas.ObjetoCausa),
            ["{ambitoCausa}"] = OrNoInformado(cas.AmbitoCausa),
            ["{lineaDispositivo}"] = OrNoInformado(cas.LineaDispositivo),
            ["{fechaIntervencion}"] = FechaIntervencion(cas.FechaIntervencion),
            ["{datosProponente}"] = proponente.ToString(),
            ["{fechaInspeccion}"] = fecha,
            ["{horaInspeccion}"] = hora,
            ["{tipoDispositivo}"] = cas.TipoDispositivo.Trim(),
            ["{titularDispositivo}"] = cas.NombreDenunciante.Trim(),
            // informe-formato-fixes H13: "No informado" como Línea/Objeto/Ámbito.
            ["{marcaModeloDispositivo}"] = OrNoInformado(MarcaModelo(cas.Device)),
            ["{imeiDispositivo}"] = cas.Device.Imei.Trim(),
            // Identidad de la organización emisora (Branding). Vacío si no está configurada.
            ["{ORGANIZACION}"] = brand.OrganizationName ?? string.Empty,
            ["{CONTACTO_EN_LINEA}"] = string.Join(" · ", brand.ContactLines),
        };
        // metadatos-dispositivo §9.3: las 12 filas de una línea de la ficha técnica ("" sin ficha: el bloque
        // {#fichaTecnica} desaparece igual).
        foreach (var (k, v) in ReportDeviceTexts.Placeholders(cas.DeviceDetails, settings.Zone))
            values[k] = v;

        foreach (var key in values.Keys.ToList())
            values[key] = Clean(values[key]);
        return values;
    }

    /// <summary>Conteos de evidencia por clase (§7.6), sin artefactos generados.</summary>
    public static (int Capturas, int Grabaciones, int Extraidos, int Otros) Counts(
        IEnumerable<FileInfoDto> files, Case cas)
    {
        var sources = cas.FileSources.Select(s => s.Filename).ToHashSet(StringComparer.Ordinal);
        int cap = 0, rec = 0, pull = 0, other = 0;
        foreach (var f in files)
        {
            if (ReportService.IsGeneratedArtifact(f.Name)) continue;
            switch (EvidenceClassifier.Classify(f.Name, sources.Contains(f.Name)))
            {
                case EvidenceClass.Screenshot: cap++; break;
                case EvidenceClass.Recording: rec++; break;
                case EvidenceClass.DevicePull: pull++; break;
                default: other++; break;
            }
        }
        return (cap, rec, pull, other);
    }

    /// <summary>Tokens (sin llaves) del renderer de textos por defecto (§7.6).</summary>
    public static Dictionary<string, string> DefaultTextTokens(Case cas, IReportSettings settings,
        IEnumerable<FileInfoDto> files)
    {
        var (fecha, hora) = Inspeccion(cas.CreatedAt, settings.Zone);
        var (cap, rec, pull, other) = Counts(files, cas);
        var inv = CultureInfo.InvariantCulture;
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fechaInspeccion"] = fecha,
            ["horaInspeccion"] = hora,
            ["tipoDispositivo"] = Clean(cas.TipoDispositivo.Trim()),
            ["marcaModeloDispositivo"] = Clean(MarcaModelo(cas.Device)),
            ["imeiDispositivo"] = Clean(cas.Device.Imei.Trim()),
            ["elSuscripto"] = ElSuscripto(cas.Perito),
            // metadatos-dispositivo D-T19: la versión textual de la ficha ("Android 8.1.0") cuando la hay.
            ["sistemaOperativo"] = Clean(ReportDeviceTexts.OsLabelFromDetails(cas.DeviceDetails) ?? cas.Device.OsLabel),
            ["zonaHoraria"] = settings.ZoneLabel,
            ["cantidadCapturas"] = cap.ToString(inv),
            ["cantidadGrabaciones"] = rec.ToString(inv),
            ["cantidadArchivosExtraidos"] = pull.ToString(inv),
            ["cantidadOtros"] = other.ToString(inv),
        };
        // metadatos-dispositivo §9.4: diferenciaReloj, numeroSerieDispositivo, compilacionDispositivo y
        // zonaHorariaDispositivo (valor o "No disponible").
        foreach (var (k, v) in ReportDeviceTexts.DefaultTextTokens(cas.DeviceDetails))
            tokens[k] = Clean(v);
        return tokens;
    }

    /// <summary>
    /// Defaults renderizados del paso Informe, ya en el dialecto Markdown de Factum
    /// (editor-texto-enriquecido §6.5): tokens escapados, cada línea del template es un bloque y
    /// <c>Formato = "markdown"</c>. "" en los campos sin default.
    /// </summary>
    public static ReportTextsDto RenderDefaults(Case cas, IReportSettings settings, IEnumerable<FileInfoDto> files)
    {
        var tokens = DefaultTextTokens(cas, settings, files)
            .ToDictionary(kv => kv.Key, kv => ReportMarkdown.EscapeInline(kv.Value), StringComparer.Ordinal);
        string Md(string template, bool omitZeroCountLines = false) =>
            ReportMarkdown.FromLineTemplate(ReportTextRenderer.Render(template, tokens, omitZeroCountLines));
        // wizard-concurrencia-pestanas DT13: las cantidades con las que se armó Operaciones (misma
        // clasificación que el DOCX), así el cliente nunca reimplementa la clasificación.
        var (cap, rec, pull, other) = Counts(files, cas);
        return new ReportTextsDto(
            ObjetoInforme: "",
            OperacionesRealizadas: Md(settings.DefaultOperacionesRealizadas, omitZeroCountLines: true),
            AseguramientoEvidencia: Md(settings.DefaultAseguramientoEvidencia),
            Resultados: "",
            ValoracionTecnica: "",
            Conclusiones: "",
            NotasTecnicas: Md(settings.DefaultNotasTecnicas),
            Reserva: Md(settings.DefaultReserva),
            Formato: ReportTextFormats.Markdown,
            OperacionesBase: new ReportCountsDto(cap, rec, pull, other));
    }
}
