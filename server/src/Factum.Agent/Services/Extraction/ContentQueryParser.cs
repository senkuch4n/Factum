using System.Text.RegularExpressions;

namespace Factum.Agent.Services.Extraction;

/// <summary>
/// Parser PURO de la salida de <c>adb shell content query</c> (extraccion-logica-android §5.3.3). El formato real
/// es <c>Row: &lt;n&gt; col1=val1, col2=val2, …</c>, con valores que pueden tener <c>, </c>, <c>=</c> y saltos de
/// línea, así que es ambiguo por naturaleza. Estrategia: filas por <c>^Row: \d+ </c>; columnas = secuencia de
/// claves más frecuente (moda); cada valor anclado en ese orden tomando la ÚLTIMA aparición del marcador siguiente
/// que deje lugar a los demás. Las filas que no anclan quedan con <c>_raw</c> + <c>_parse_warning</c>.
/// El crudo es la evidencia; esto solo alimenta el normalizado y el conteo.
/// </summary>
public static class ContentQueryParser
{
    private static readonly Regex RowStart = new(@"^Row: \d+ ", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex KeySeq = new(@"(?:^|, )([A-Za-z_][A-Za-z0-9_]*)=", RegexOptions.Compiled);

    public static ParsedQuery Parse(string? output)
    {
        var text = output ?? "";
        if (text.Contains("No result found."))
            return new ParsedQuery([], []);

        // 1) Partir en filas: cada fila es desde después de "Row: N " hasta el próximo "Row:" o el final.
        var starts = RowStart.Matches(text);
        if (starts.Count == 0)
            return new ParsedQuery([], []);

        var bodies = new List<string>(starts.Count);
        for (int i = 0; i < starts.Count; i++)
        {
            var from = starts[i].Index + starts[i].Length;
            var to = i + 1 < starts.Count ? starts[i + 1].Index : text.Length;
            bodies.Add(text[from..to].TrimEnd('\r', '\n'));
        }

        // 2) Columnas por moda de la secuencia de claves.
        var seqCounts = new Dictionary<string, (int Count, List<string> Cols)>();
        foreach (var body in bodies)
        {
            var cols = KeySeq.Matches(body).Select(m => m.Groups[1].Value).ToList();
            if (cols.Count == 0) continue;
            var key = string.Join('\u0001', cols);
            if (seqCounts.TryGetValue(key, out var cur)) seqCounts[key] = (cur.Count + 1, cur.Cols);
            else seqCounts[key] = (1, cols);
        }
        if (seqCounts.Count == 0)
            return new ParsedQuery([], bodies.Select(b => QueryRow.Unparsed(b)).ToList());

        var columns = seqCounts.Values
            .OrderByDescending(v => v.Count)
            .ThenByDescending(v => v.Cols.Count)
            .First().Cols;

        // 3) Cada fila anclada en ese orden.
        var rows = new List<QueryRow>(bodies.Count);
        foreach (var body in bodies)
            rows.Add(ParseRow(body, columns));

        return new ParsedQuery(columns, rows);
    }

    private static QueryRow ParseRow(string body, List<string> columns)
    {
        int k = columns.Count;
        var markers = new string[k];
        markers[0] = columns[0] + "=";
        for (int i = 1; i < k; i++) markers[i] = ", " + columns[i] + "=";

        var idx = new int[k];

        // De derecha a izquierda: cada marcador, la última aparición ANTES del siguiente ya ubicado.
        idx[k - 1] = body.LastIndexOf(markers[k - 1], StringComparison.Ordinal);
        if (idx[k - 1] < 0) return QueryRow.Unparsed(body);
        for (int i = k - 2; i >= 0; i--)
        {
            var limit = idx[i + 1];
            idx[i] = body.LastIndexOf(markers[i], limit, StringComparison.Ordinal);
            if (idx[i] < 0) return QueryRow.Unparsed(body);
        }

        // La primera columna tiene que estar al inicio (tolerando espacios sueltos).
        if (body[..idx[0]].Trim().Length != 0) return QueryRow.Unparsed(body);

        var values = new Dictionary<string, string?>(k);
        for (int i = 0; i < k; i++)
        {
            var start = idx[i] + markers[i].Length;
            var end = i + 1 < k ? idx[i + 1] : body.Length;
            var raw = body[start..end];
            values[columns[i]] = raw == "NULL" ? null : raw;
        }
        return new QueryRow(values, false, null);
    }
}

/// <summary>Resultado del parseo de una consulta.</summary>
public sealed record ParsedQuery(List<string> Columns, List<QueryRow> Rows)
{
    public int RowCount => Rows.Count;
    public int ParseWarnings => Rows.Count(r => r.ParseWarning);
}

/// <summary>Una fila: valores por columna (NULL → null), o <c>_raw</c> + <c>_parse_warning</c> si no parseó.</summary>
public sealed record QueryRow(Dictionary<string, string?> Values, bool ParseWarning, string? Raw)
{
    public static QueryRow Unparsed(string line) => new(new Dictionary<string, string?>(), true, line);
}
