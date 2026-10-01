using System.Globalization;
using System.Text;
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

    /// <summary>Sanitizado de §7.2: \r\n → \n, \t → espacio, sin controles &lt; 0x20 salvo \n.</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var s = value.Replace("\r\n", "\n").Replace('\r', '\n');
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '\t') sb.Append(' ');
            else if (ch == '\n' || ch >= 0x20) sb.Append(ch);
        }
        return sb.ToString();
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
            ["{marcaModeloDispositivo}"] = MarcaModelo(cas.Device),
            ["{imeiDispositivo}"] = cas.Device.Imei.Trim(),
            // Identidad de la organización emisora (Branding). Vacío si no está configurada.
            ["{ORGANIZACION}"] = brand.OrganizationName ?? string.Empty,
            ["{CONTACTO_EN_LINEA}"] = string.Join(" · ", brand.ContactLines),
        };

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
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fechaInspeccion"] = fecha,
            ["horaInspeccion"] = hora,
            ["tipoDispositivo"] = Clean(cas.TipoDispositivo.Trim()),
            ["marcaModeloDispositivo"] = Clean(MarcaModelo(cas.Device)),
            ["imeiDispositivo"] = Clean(cas.Device.Imei.Trim()),
            ["elSuscripto"] = ElSuscripto(cas.Perito),
            ["sistemaOperativo"] = Clean(cas.Device.OsLabel),
            ["zonaHoraria"] = settings.ZoneLabel,
            ["cantidadCapturas"] = cap.ToString(inv),
            ["cantidadGrabaciones"] = rec.ToString(inv),
            ["cantidadArchivosExtraidos"] = pull.ToString(inv),
            ["cantidadOtros"] = other.ToString(inv),
        };
    }

    /// <summary>Defaults renderizados del paso Informe; "" en los campos sin default.</summary>
    public static ReportTextsDto RenderDefaults(Case cas, IReportSettings settings, IEnumerable<FileInfoDto> files)
    {
        var tokens = DefaultTextTokens(cas, settings, files);
        return new ReportTextsDto(
            ObjetoInforme: "",
            OperacionesRealizadas: ReportTextRenderer.Render(settings.DefaultOperacionesRealizadas, tokens,
                omitZeroCountLines: true),
            AseguramientoEvidencia: ReportTextRenderer.Render(settings.DefaultAseguramientoEvidencia, tokens),
            Resultados: "",
            ValoracionTecnica: "",
            Conclusiones: "",
            NotasTecnicas: ReportTextRenderer.Render(settings.DefaultNotasTecnicas, tokens),
            Reserva: ReportTextRenderer.Render(settings.DefaultReserva, tokens));
    }
}
