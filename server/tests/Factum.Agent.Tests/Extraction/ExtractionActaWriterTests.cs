using Factum.Agent.Services.Extraction;

namespace Factum.Agent.Tests.Extraction;

public class ExtractionActaWriterTests
{
    private static ActaData Base(
        IReadOnlyList<ActaModification>? mods = null,
        string? interruptionReason = null,
        ActaVerification? verification = null) => new(
        ExtractionId: "3f2a9c1e-0000-4000-8000-0000000000aa",
        Status: "completed",
        StatusLabel: "Completa",
        OperatorDni: "30111222",
        OperatorName: "Ana Pérez",
        Hostname: "PC-PERITO-01",
        OsUser: "perito",
        Os: "Windows",
        AgentVersion: "1.9.0",
        AgentMode: "installed",
        ClientVersion: null,
        Device: new ActaDevice("R58RA3R5AGA", "samsung", "SM-M127F", "11"),
        StartedAtPcUtc: new DateTime(2026, 10, 25, 17, 30, 0, DateTimeKind.Utc),
        FinishedAtPcUtc: new DateTime(2026, 10, 25, 17, 58, 0, DateTimeKind.Utc),
        PcMinusServerMs: -120,
        PcServerMarginMs: 35,
        DestinationPath: "/media/perito/EVIDENCIA_01/Factum_Extraccion_...",
        FileSystem: "exfat",
        FreeBytesAtStart: 900000000000,
        Ficha: [new ActaFichaField("serial_number", "R58", null), new ActaFichaField("wifi_ssid", null, "masked")],
        ClockStartText: "dispositivo +100 ms (± 1000 ms)",
        ClockCloseText: null,
        Categories:
        [
            new ActaCategoryLine("contacts", "Contactos", "content query ...", "done", 22, null, null, null, null, null),
            new ActaCategoryLine("call_log", "Registro de llamadas", "connector", "not_extracted", null, null, null,
                null, null, "La instalación de la app auxiliar quedó bloqueada"),
        ],
        Verification: verification ?? new ActaVerification(Requested: true, CapabilityAvailable: true,
            Binary: "toybox sha256sum", FilesComputed: 1, Mismatches: 0, FilesNotComputed: 0),
        Modifications: mods ?? [],
        InterruptionReasonText: interruptionReason,
        InterruptionAtUtc: interruptionReason is null ? null : new DateTime(2026, 10, 25, 17, 45, 0, DateTimeKind.Utc),
        ManifestSha256: new string('a', 64));

    [Fact]
    public void SinModificaciones_DiceNinguna()
    {
        var txt = ExtractionActaWriter.BuildActaText(Base());
        Assert.Contains("7. MODIFICACIONES AL DISPOSITIVO", txt);
        Assert.Contains(ExtractionActaWriter.NoModifications, txt);
        Assert.Contains(ExtractionActaWriter.AdbEffectsText, txt);
    }

    [Fact]
    public void ConModificaciones_ListaConHoras()
    {
        var mods = new List<ActaModification>
        {
            new("connector_installed", new DateTime(2026, 10, 25, 17, 40, 0, DateTimeKind.Utc), "app.factum.connector 1.0.0"),
            new("connector_uninstalled", new DateTime(2026, 10, 25, 17, 44, 0, DateTimeKind.Utc), null),
        };
        var txt = ExtractionActaWriter.BuildActaText(Base(mods));
        Assert.Contains("2026-10-25T17:40:00 UTC — connector_installed: app.factum.connector 1.0.0", txt);
        Assert.Contains("2026-10-25T17:44:00 UTC — connector_uninstalled", txt);
        Assert.DoesNotContain(ExtractionActaWriter.NoModifications + "\r\n", txt);
    }

    [Fact]
    public void Interrupcion_AparecenSeccionYMotivo()
    {
        var txt = ExtractionActaWriter.BuildActaText(Base(interruptionReason: "El dispositivo se desconectó"));
        Assert.Contains("8. INTERRUPCIÓN O CANCELACIÓN", txt);
        Assert.Contains("El dispositivo se desconectó", txt);
    }

    [Fact]
    public void Acta_IdentificaElDispositivo_TxtYJson()
    {
        var d = Base();
        var txt = ExtractionActaWriter.BuildActaText(d);
        Assert.Contains("Marca: samsung", txt);
        Assert.Contains("Modelo: SM-M127F", txt);
        Assert.Contains("Número de serie (conexión): R58RA3R5AGA", txt);
        Assert.Contains("Versión de Android: 11", txt);

        using var doc = System.Text.Json.JsonDocument.Parse(ExtractionActaWriter.BuildActaJson(d));
        var ident = doc.RootElement.GetProperty("device_identity");
        Assert.Equal("R58RA3R5AGA", ident.GetProperty("serial").GetString());
        Assert.Equal("samsung", ident.GetProperty("manufacturer").GetString());
        Assert.Equal("SM-M127F", ident.GetProperty("model").GetString());
        Assert.Equal("11", ident.GetProperty("android_version").GetString());
    }

    // ── §6 VERIFICACIÓN: el texto deriva de lo que REALMENTE pasó (QA reintento 2, H1) ──

    private static string Seccion6(ActaVerification v)
    {
        var txt = ExtractionActaWriter.BuildActaText(Base(verification: v));
        var lines = txt.Split("\r\n");
        var start = Array.FindIndex(lines, l => l.StartsWith("6. VERIFICACIÓN", StringComparison.Ordinal));
        var end = Array.FindIndex(lines, start + 1, l => l.Length > 0 && char.IsDigit(l[0]) && l.Contains('.'));
        return string.Join("\n", lines[start..(end < 0 ? lines.Length : end)]);
    }

    [Fact]
    public void Verif_A_VerificadoYCoincide_NombraElBinarioReal()
    {
        var s6 = Seccion6(new ActaVerification(true, true, "/system/bin/sha256sum", FilesComputed: 17, Mismatches: 0, FilesNotComputed: 0));
        Assert.Contains("se calculó también en el dispositivo", s6);
        Assert.Contains("binario: /system/bin/sha256sum", s6);
        Assert.Contains("coincide con el de la PC en los 17", s6);
        Assert.DoesNotContain("No se calculó", s6);
    }

    [Fact]
    public void Verif_B_ConDiscordancias_LasDeclaraYExplicaElTiming()
    {
        var s6 = Seccion6(new ActaVerification(true, true, "toybox sha256sum", FilesComputed: 10, Mismatches: 2, FilesNotComputed: 0));
        Assert.Contains("se calculó también en el dispositivo", s6);
        Assert.Contains("2 archivo(s) NO coinciden", s6);
        Assert.Contains("MANIFIESTO.tsv", s6);
        Assert.Contains("ANTES de copiar", s6); // observación del timing (no bloqueante, pedida)
    }

    [Fact]
    public void Verif_C_Desactivada_DiceQueNoSeCalculo()
    {
        var s6 = Seccion6(new ActaVerification(Requested: false, CapabilityAvailable: true, Binary: "toybox sha256sum", FilesComputed: 0, Mismatches: 0, FilesNotComputed: 17));
        Assert.Contains("No se calculó el SHA-256 en el dispositivo", s6);
        Assert.Contains("desactivada por el operador", s6);
        Assert.Contains("17 archivo(s) quedaron con coincide=no_calculado", s6);
        Assert.DoesNotContain("se calculó también en el dispositivo", s6);
        Assert.DoesNotContain("binario", s6); // no se menciona el binario cuando no se calculó
    }

    [Fact]
    public void Verif_C_SinSha256sum()
    {
        var s6 = Seccion6(new ActaVerification(Requested: true, CapabilityAvailable: false, Binary: null, FilesComputed: 0, Mismatches: 0, FilesNotComputed: 5));
        Assert.Contains("No se calculó", s6);
        Assert.Contains("el equipo no tiene sha256sum", s6);
    }

    [Fact]
    public void Verif_C_MapaVacio_FaseSinHashes()
    {
        var s6 = Seccion6(new ActaVerification(Requested: true, CapabilityAvailable: true, Binary: "toybox sha256sum", FilesComputed: 0, Mismatches: 0, FilesNotComputed: 3));
        Assert.Contains("No se calculó", s6);
        Assert.Contains("no produjo hashes", s6);
    }

    [Fact]
    public void Verif_Json_ExponeLaVerificacion()
    {
        var d = Base(verification: new ActaVerification(true, true, "toybox sha256sum", 10, 2, 1));
        using var doc = System.Text.Json.JsonDocument.Parse(ExtractionActaWriter.BuildActaJson(d));
        var v = doc.RootElement.GetProperty("verification");
        Assert.True(v.GetProperty("requested").GetBoolean());
        Assert.Equal("toybox sha256sum", v.GetProperty("binary").GetString());
        Assert.Equal(10, v.GetProperty("files_computed_on_device").GetInt32());
        Assert.Equal(2, v.GetProperty("mismatches").GetInt32());
        Assert.Equal(1, v.GetProperty("files_not_computed").GetInt32());
    }

    [Fact]
    public void Json_TieneSchemaYCategorias()
    {
        var json = ExtractionActaWriter.BuildActaJson(Base());
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(ExtractionActaWriter.Schema, doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("categories").GetArrayLength());
        Assert.Equal(ExtractionActaWriter.AdbEffectsText, doc.RootElement.GetProperty("adb_effects").GetString());
    }

    [Fact]
    public void Leeme_TieneComandosDeVerificacion()
    {
        var leeme = ExtractionActaWriter.BuildLeeme("ext-1");
        Assert.Contains("sha256sum -c SHA256SUMS.txt", leeme);
        Assert.Contains("openssl ts -verify -data MANIFIESTO.tsv", leeme);
    }

    [Fact]
    public async Task WriteAsync_EscribeActaConBomYCrlf()
    {
        var dir = Directory.CreateTempSubdirectory("factum-acta-test");
        try
        {
            await ExtractionActaWriter.WriteAsync(dir.FullName, Base());
            var bytes = await File.ReadAllBytesAsync(Path.Combine(dir.FullName, "ACTA.txt"));
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // BOM UTF-8
            var txt = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.Contains("\r\n", txt);                               // CRLF
            Assert.True(File.Exists(Path.Combine(dir.FullName, "ACTA.json")));
            Assert.True(File.Exists(Path.Combine(dir.FullName, "LEEME.txt")));
        }
        finally { dir.Delete(recursive: true); }
    }
}
