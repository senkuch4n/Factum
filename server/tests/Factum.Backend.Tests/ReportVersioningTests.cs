using System.Reflection;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;

namespace Factum.Backend.Tests;

/// <summary>
/// Refactorizaciones/versionado-informe.md — lógica pura del versionado de las secciones del
/// informe (ReportVersioning). Sin Mongo: no toca la base de desarrollo ni crea datos. Cubre D3
/// (poda conservando hitos), D4 (de-dup) y la normalización del trigger, más el de-dup de la
/// versión "generate" y que el repositorio de eventos sea append-only.
/// </summary>
public sealed class ReportVersioningTests
{
    private static ReportTexts Texts(string resultados, string? formato = null) => new()
    {
        ObjetoInforme = "obj",
        OperacionesRealizadas = "op",
        AseguramientoEvidencia = "aseg",
        Resultados = resultados,
        ValoracionTecnica = "val",
        Conclusiones = "conc",
        NotasTecnicas = "notas",
        Reserva = "res",
        Formato = formato,
    };

    private static ReportTextVersion Version(string resultados, DateTime createdAt,
        string trigger = ReportVersionTriggers.Save, string? formato = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        CreatedAt = createdAt,
        AuthorDni = "1",
        AuthorName = "Perito",
        Trigger = trigger,
        Texts = ReportVersioning.Snapshot(Texts(resultados, formato)),
    };

    // ── D4: de-duplicado ────────────────────────────────────────────────────

    [Fact]
    public void IsDuplicate_HistorialVacio_SiempreFalse()
    {
        // Caso sin el campo (o primera versión): nunca hay con qué comparar → siempre se agrega.
        Assert.False(ReportVersioning.IsDuplicate([], ReportVersioning.Snapshot(Texts("A"))));
    }

    [Fact]
    public void IsDuplicate_IdenticaALaUltima_True()
    {
        var history = new List<ReportTextVersion>
        {
            Version("A", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Version("B", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
        };
        // La última (más reciente por created_at) es "B"; un candidato idéntico es duplicado.
        Assert.True(ReportVersioning.IsDuplicate(history, ReportVersioning.Snapshot(Texts("B"))));
    }

    [Fact]
    public void IsDuplicate_DistintaDeLaUltima_False()
    {
        var history = new List<ReportTextVersion>
        {
            Version("A", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Version("B", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
        };
        Assert.False(ReportVersioning.IsDuplicate(history, ReportVersioning.Snapshot(Texts("C"))));
    }

    [Fact]
    public void IsDuplicate_CompararContraLaMasReciente_NoLaUltimaDeLaLista()
    {
        // Desordenada en la lista: la comparación es contra la de created_at MÁS reciente ("B"),
        // no contra el último elemento del array.
        var history = new List<ReportTextVersion>
        {
            Version("B", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
            Version("A", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        Assert.True(ReportVersioning.IsDuplicate(history, ReportVersioning.Snapshot(Texts("B"))));
        Assert.False(ReportVersioning.IsDuplicate(history, ReportVersioning.Snapshot(Texts("A"))));
    }

    [Fact]
    public void IsDuplicate_FormatoDistinto_NoEsDuplicado()
    {
        var history = new List<ReportTextVersion>
        {
            Version("A", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), formato: "markdown"),
        };
        Assert.False(ReportVersioning.IsDuplicate(history, ReportVersioning.Snapshot(Texts("A", formato: null))));
        Assert.True(ReportVersioning.IsDuplicate(history, ReportVersioning.Snapshot(Texts("A", formato: "markdown"))));
    }

    // ── D5 (restaurar): trigger + restored_from quedan registrados ────────────

    [Fact]
    public void PlanAppend_Restore_ConservaTriggerYRestoredFrom_YLaPreviaSigue()
    {
        var previa = Version("A", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var history = new List<ReportTextVersion> { previa };
        var restore = new ReportTextVersion
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            AuthorDni = "1",
            AuthorName = "Perito",
            Trigger = ReportVersionTriggers.Restore,
            RestoredFrom = previa.Id,
            Texts = ReportVersioning.Snapshot(Texts("A-restaurado")),
        };

        // Por debajo del tope → push simple; la previa no se toca (append-only).
        var (push, pruned) = ReportVersioning.PlanAppend(history, restore);
        Assert.Null(pruned);
        Assert.NotNull(push);
        Assert.Equal(ReportVersionTriggers.Restore, push!.Trigger);
        Assert.Equal(previa.Id, push.RestoredFrom);
        Assert.Contains(previa, history);   // la versión previa sigue en el historial
    }

    // ── D3: retención con poda conservando hitos ──────────────────────────────

    [Fact]
    public void PlanAppend_PorDebajoDelTope_PushSimple()
    {
        var history = Enumerable.Range(0, 10)
            .Select(i => Version($"v{i}", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i)))
            .ToList();
        var nueva = Version("nueva", new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc));

        var (push, pruned) = ReportVersioning.PlanAppend(history, nueva);
        Assert.Null(pruned);
        Assert.Same(nueva, push);
    }

    [Fact]
    public void PlanAppend_EnElTope_PodaConservaPrimeraGenerateYActual_YDescartaIntermedias()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // 50 versiones existentes: la primera (i=0), una "generate" intermedia (i=5), el resto "save".
        var history = new List<ReportTextVersion>();
        for (var i = 0; i < CaseService.MaxReportVersions; i++)
        {
            var trigger = i == 5 ? ReportVersionTriggers.Generate : ReportVersionTriggers.Save;
            history.Add(Version($"v{i}", baseTime.AddMinutes(i), trigger));
        }
        var primera = history[0];
        var generate = history[5];
        var intermediaVieja = history[1];   // la "save" más vieja que no es la primera
        var nueva = Version("nueva", baseTime.AddMinutes(1000));

        var (push, pruned) = ReportVersioning.PlanAppend(history, nueva);

        Assert.Null(push);               // en el tope → $set del array podado
        Assert.NotNull(pruned);
        Assert.Equal(CaseService.MaxReportVersions, pruned!.Count);   // respeta el tope exacto
        Assert.Contains(primera, pruned);          // la primera del caso se conserva
        Assert.Contains(generate, pruned);         // el hito "generate" se conserva
        Assert.Contains(nueva, pruned);            // la recién agregada siempre entra
        Assert.DoesNotContain(intermediaVieja, pruned);   // una intermedia vieja se descarta
        Assert.Equal(nueva, pruned[^1]);           // la nueva queda al final (orden cronológico)
    }

    [Fact]
    public void PlanAppend_EnElTope_ConMuchasGenerate_NoLasBorra()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Todas "generate" salvo la primera: ninguna se poda (hitos forenses).
        var history = new List<ReportTextVersion>();
        for (var i = 0; i < CaseService.MaxReportVersions; i++)
        {
            var trigger = i == 0 ? ReportVersionTriggers.Save : ReportVersionTriggers.Generate;
            history.Add(Version($"v{i}", baseTime.AddMinutes(i), trigger));
        }
        var nueva = Version("nueva", baseTime.AddMinutes(1000), ReportVersionTriggers.Generate);

        var (_, pruned) = ReportVersioning.PlanAppend(history, nueva);

        Assert.NotNull(pruned);
        // Todas las "generate" originales siguen presentes (ninguna se descartó).
        foreach (var g in history.Where(v => v.Trigger == ReportVersionTriggers.Generate))
            Assert.Contains(g, pruned!);
        Assert.Contains(history[0], pruned!);   // la primera también
        Assert.Contains(nueva, pruned!);
    }

    // ── Versión "generate" (D2) con su de-dup ─────────────────────────────────

    [Fact]
    public void GenerateVersionFor_HistorialVacio_CreaVersionGenerate()
    {
        var cas = new Case { Officer = new User { Dni = "1", Name = "Perito" }, ReportTexts = Texts("A") };
        var v = ReportVersioning.GenerateVersionFor(cas);
        Assert.NotNull(v);
        Assert.Equal(ReportVersionTriggers.Generate, v!.Trigger);
        Assert.Equal("1", v.AuthorDni);
        Assert.Equal("Perito", v.AuthorName);
    }

    [Fact]
    public void GenerateVersionFor_IdenticaALaUltima_NoDuplica()
    {
        var cas = new Case
        {
            Officer = new User { Dni = "1", Name = "Perito" },
            ReportTexts = Texts("A"),
            ReportTextVersions = [Version("A", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))],
        };
        Assert.Null(ReportVersioning.GenerateVersionFor(cas));   // de-dup: nada que agregar
    }

    [Fact]
    public void GenerateVersionFor_DistintaDeLaUltima_Crea()
    {
        var cas = new Case
        {
            Officer = new User { Dni = "1", Name = "Perito" },
            ReportTexts = Texts("B"),
            ReportTextVersions = [Version("A", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))],
        };
        Assert.NotNull(ReportVersioning.GenerateVersionFor(cas));
    }

    [Fact]
    public void GenerateVersionFor_SinReportTexts_NoRompe()
    {
        var cas = new Case { Officer = new User { Dni = "1", Name = "Perito" }, ReportTexts = null };
        // Caso sin textos: snapshot vacío, primera versión → se crea (no lanza).
        Assert.NotNull(ReportVersioning.GenerateVersionFor(cas));
    }

    // ── Normalización del trigger ─────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("save")]
    public void TryNormalizeTrigger_VacioOSave_QuedaSave(string? raw)
    {
        Assert.True(ReportVersioning.TryNormalizeTrigger(raw, null, out var trigger, out var error));
        Assert.Equal(ReportVersionTriggers.Save, trigger);
        Assert.Null(error);
    }

    [Fact]
    public void TryNormalizeTrigger_Restore_Ok()
    {
        Assert.True(ReportVersioning.TryNormalizeTrigger("restore", "abc", out var trigger, out var error));
        Assert.Equal(ReportVersionTriggers.Restore, trigger);
        Assert.Null(error);
    }

    [Fact]
    public void TryNormalizeTrigger_ValorInvalido_Falla()
    {
        Assert.False(ReportVersioning.TryNormalizeTrigger("publish", null, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TryNormalizeTrigger_RestoredFromConSave_Falla()
    {
        // restored_from solo es válido con trigger "restore".
        Assert.False(ReportVersioning.TryNormalizeTrigger("save", "abc", out _, out var error));
        Assert.NotNull(error);
    }

    // ── ToDto: mapea el contrato tal cual ─────────────────────────────────────

    [Fact]
    public void ToDto_MapeaTodosLosCampos()
    {
        var v = new ReportTextVersion
        {
            Id = "ver1",
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            AuthorDni = "9",
            AuthorName = "Ana",
            Trigger = ReportVersionTriggers.Restore,
            RestoredFrom = "ver0",
            Texts = ReportVersioning.Snapshot(Texts("R", formato: "markdown")),
        };
        var dto = ReportVersioning.ToDto(v);
        Assert.Equal("ver1", dto.Id);
        Assert.Equal(v.CreatedAt, dto.CreatedAt);
        Assert.Equal("9", dto.AuthorDni);
        Assert.Equal("Ana", dto.AuthorName);
        Assert.Equal(ReportVersionTriggers.Restore, dto.Trigger);
        Assert.Equal("ver0", dto.RestoredFrom);
        Assert.Equal("R", dto.Texts.Resultados);
        Assert.Equal("markdown", dto.Texts.Formato);
    }

    // ── Append-only del repositorio de eventos (C3-append-only) ───────────────

    [Fact]
    public void ICaseEventRepository_EsAppendOnly_SinUpdateNiDelete()
    {
        var names = typeof(ICaseEventRepository).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToArray();
        Assert.Contains("InsertAsync", names);
        Assert.Contains("ListByCaseAsync", names);
        Assert.DoesNotContain(names, n => n.Contains("Update", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains("Delete", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains("Remove", StringComparison.Ordinal));
    }
}
