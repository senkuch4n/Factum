using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Definiciones de lista de los textos en Markdown (editor-texto-enriquecido, SDD §6.3.4),
/// creadas al vuelo en <c>numbering.xml</c>. Una instancia por documento y perezosa: si ninguna
/// sección tiene listas, <c>numbering.xml</c> no se toca (ni se carga). No toca ninguna
/// definición existente (en la v6, <c>abstractNum 1</c>/<c>numId 2</c> es la numeración romana
/// de los títulos). Corre después de B-R2b: las definiciones nuevas van en tinta fija.
/// </summary>
internal sealed class ReportListNumbering(MainDocumentPart main)
{
    internal const string BulletNsid = "FAC70001";
    internal const string DecimalNsid = "FAC70002";
    internal const string InkColor = "0E1013";
    internal const string MarkerFont = "Arial";
    /// <summary>Sangría por nivel (0.635 cm), D12.</summary>
    internal const int IndentStep = 360;
    private const int Levels = 9;
    private static readonly string[] BulletSymbols = ["•", "◦", "▪"];

    private Numbering? numbering;
    private int bulletAbstractId;
    private int decimalAbstractId;

    /// <summary>
    /// Crea un <c>w:num</c> nuevo (cada lista del Markdown tiene el suyo, así ninguna sigue la
    /// cuenta de otra) y devuelve su <c>numId</c>. Las numeradas llevan <c>startOverride</c> en
    /// <paramref name="ilvl"/>.
    /// </summary>
    public int Create(bool ordered, int start, int ilvl)
    {
        var root = EnsureDefinitions();
        var numId = root.Elements<NumberingInstance>()
            .Select(n => n.NumberID?.Value ?? 0).DefaultIfEmpty(0).Max() + 1;

        var instance = new NumberingInstance { NumberID = numId };
        instance.Append(new AbstractNumId { Val = ordered ? decimalAbstractId : bulletAbstractId });
        if (ordered)
            instance.Append(new LevelOverride(new StartOverrideNumberingValue { Val = Math.Max(0, start) })
            {
                LevelIndex = ilvl,
            });

        // Los w:num van después del último w:num existente (o del último abstractNum), antes de
        // cualquier elemento posterior del esquema (w:numIdMacAtCleanup).
        OpenXmlElement? anchor = (OpenXmlElement?)root.Elements<NumberingInstance>().LastOrDefault()
                                 ?? root.Elements<AbstractNum>().LastOrDefault();
        if (anchor is not null) anchor.InsertAfterSelf(instance);
        else root.PrependChild(instance);
        return numId;
    }

    private Numbering EnsureDefinitions()
    {
        if (numbering is not null) return numbering;

        var part = main.NumberingDefinitionsPart ?? main.AddNewPart<NumberingDefinitionsPart>();
        part.Numbering ??= new Numbering();
        var root = part.Numbering;

        var maxAbstract = root.Elements<AbstractNum>()
            .Select(a => a.AbstractNumberId?.Value ?? 0).DefaultIfEmpty(-1).Max();
        bulletAbstractId = maxAbstract + 1;
        decimalAbstractId = maxAbstract + 2;

        var bullet = BuildAbstract(bulletAbstractId, BulletNsid, ordered: false);
        var dec = BuildAbstract(decimalAbstractId, DecimalNsid, ordered: true);

        // Orden del esquema: abstractNum después del último abstractNum existente y antes del
        // primer w:num.
        OpenXmlElement? lastAbstract = root.Elements<AbstractNum>().LastOrDefault();
        if (lastAbstract is not null)
        {
            lastAbstract.InsertAfterSelf(bullet);
        }
        else
        {
            OpenXmlElement? firstNum = (OpenXmlElement?)root.Elements<NumberingInstance>().FirstOrDefault()
                                       ?? root.Elements<NumberingIdMacAtCleanup>().FirstOrDefault();
            if (firstNum is not null) firstNum.InsertBeforeSelf(bullet);
            else root.AppendChild(bullet);
        }
        bullet.InsertAfterSelf(dec);

        numbering = root;
        return root;
    }

    private static AbstractNum BuildAbstract(int id, string nsid, bool ordered)
    {
        var abs = new AbstractNum { AbstractNumberId = id };
        abs.Append(new Nsid { Val = nsid });
        abs.Append(new MultiLevelType { Val = MultiLevelValues.HybridMultilevel });
        for (var n = 0; n < Levels; n++)
        {
            var level = new Level { LevelIndex = n };
            level.Append(new StartNumberingValue { Val = 1 });
            level.Append(new NumberingFormat { Val = ordered ? NumberFormatValues.Decimal : NumberFormatValues.Bullet });
            level.Append(new LevelText { Val = ordered ? $"%{n + 1}." : BulletSymbols[Math.Min(n, 2)] });
            level.Append(new LevelJustification { Val = LevelJustificationValues.Left });
            level.Append(new PreviousParagraphProperties(new Indentation
            {
                Left = (IndentStep * (n + 1)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Hanging = IndentStep.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }));
            level.Append(new NumberingSymbolRunProperties(
                new RunFonts { Hint = FontTypeHintValues.Default, Ascii = MarkerFont, HighAnsi = MarkerFont, ComplexScript = MarkerFont },
                new Color { Val = InkColor }));
            abs.Append(level);
        }
        return abs;
    }
}
