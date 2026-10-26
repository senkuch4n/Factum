using Factum.Agent.Services.Extraction;

namespace Factum.Agent.Tests.Extraction;

internal static class ExtractionFixtures
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Extraction", name));
}

public class ContentQueryParserTests
{
    [Fact]
    public void NoResult_DaCeroFilas()
    {
        var parsed = ContentQueryParser.Parse(ExtractionFixtures.Read("no_result.txt"));
        Assert.Equal(0, parsed.RowCount);
        Assert.Empty(parsed.Columns);
    }

    [Fact]
    public void Sms_ColumnasPorModa_YValoresConComasYSaltos()
    {
        var parsed = ContentQueryParser.Parse(ExtractionFixtures.Read("sms_sintetico.txt"));

        // La moda descarta la "columna" espuria 'y' que aparece dentro del body de la fila 0.
        Assert.Equal(new[] { "_id", "thread_id", "address", "date", "type", "body" }, parsed.Columns);
        Assert.Equal(3, parsed.RowCount);
        Assert.Equal(0, parsed.ParseWarnings);

        // Fila 0: el body conserva comas y '=' internos.
        Assert.Equal("Hola, cómo estás, todo bien? x=1, y=2", parsed.Rows[0].Values["body"]);
        Assert.Equal("1", parsed.Rows[0].Values["_id"]);

        // Fila 1: body multilínea y NULL → null.
        Assert.Null(parsed.Rows[1].Values["address"]);
        Assert.Contains("segunda línea, con coma", parsed.Rows[1].Values["body"]);

        // Fila 2.
        Assert.Equal("fin", parsed.Rows[2].Values["body"]);
    }

    [Fact]
    public void ContactsData_NullPasaANull()
    {
        var parsed = ContentQueryParser.Parse(ExtractionFixtures.Read("contacts_data_m12_anon.txt"));
        Assert.Equal(new[] { "_id", "raw_contact_id", "mimetype", "data1", "data2", "data3" }, parsed.Columns);
        Assert.Equal(3, parsed.RowCount);
        Assert.Null(parsed.Rows[1].Values["data3"]);
        Assert.Equal("persona@ejemplo.test", parsed.Rows[2].Values["data1"]);
    }

    [Fact]
    public void FilaQueNoAncla_QuedaConRawYParseWarning()
    {
        // La columna elegida es [a, b] (2 filas), pero la 3ª fila no tiene 'b'.
        var text = "Row: 0 a=1, b=2\nRow: 1 a=3, b=4\nRow: 2 a=5\n";
        var parsed = ContentQueryParser.Parse(text);
        Assert.Equal(new[] { "a", "b" }, parsed.Columns);
        Assert.Equal(1, parsed.ParseWarnings);
        var bad = parsed.Rows[2];
        Assert.True(bad.ParseWarning);
        Assert.Equal("a=5", bad.Raw);
    }
}
