using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;

namespace Factum.Backend.Services.Extractions;

/// <summary>Resultado de aplicar un <see cref="UpsertExtractionRequest"/> (extraccion-logica-android §8.3).</summary>
public enum ExtractionRuleError { None, Invalid, Immutable, Finished }

/// <summary>
/// Lo que devuelve <see cref="ExtractionRules.Apply"/>: la extracción como queda (si no hay error) y las banderas de
/// efectos que mira el servicio (<see cref="ManifestJustSet"/> → sellar; <see cref="BecameFinalFirstTime"/> → fijar
/// <c>FinalRegisteredAt</c>).
/// </summary>
public sealed record ExtractionApplyResult(
    Extraction? Value,
    ExtractionRuleError Error,
    string? Message,
    bool ManifestJustSet,
    bool BecameFinalFirstTime)
{
    public static ExtractionApplyResult Fail(ExtractionRuleError e, string msg) => new(null, e, msg, false, false);
    public static ExtractionApplyResult Ok(Extraction v, bool manifestJustSet, bool becameFinal) =>
        new(v, ExtractionRuleError.None, null, manifestJustSet, becameFinal);
}

/// <summary>
/// Reglas PURAS del registro de una extracción (extraccion-logica-android §8.3). Valida id/estado/transiciones,
/// inmutables (<c>manifest_sha256</c>, <c>started_at_pc</c>, <c>device.serial</c>), lista blanca de categorías y
/// modificaciones (append-only), topes de texto y números ≥ 0, y arma la entidad a persistir. El upsert con el
/// <c>Officer</c> del JWT y el sellado los hace el servicio.
/// </summary>
public static class ExtractionRules
{
    public const int MaxDeviceModifications = 20;

    public static ExtractionApplyResult Apply(Extraction? existing, string id, UpsertExtractionRequest req,
        User officer, DateTime nowServer)
    {
        if (!Guid.TryParseExact(id, "D", out _))
            return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, "El id de la extracción no es un GUID.");

        var status = req.Status?.Trim() ?? "";
        if (!ExtractionStatuses.IsValid(status))
            return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, "El estado no es válido.");

        // Transición (§4.2). Un terminal no cambia; repetir el MISMO terminal es idempotente.
        if (existing is not null && ExtractionStatuses.IsTerminal(existing.Status) && status != existing.Status)
            return ExtractionApplyResult.Fail(ExtractionRuleError.Finished,
                "La extracción ya está en un estado terminal.");

        // manifest_sha256: 64 hex minúscula; inmutable una vez puesto.
        string? manifestSha = null;
        if (req.ManifestSha256 is { } raw)
        {
            manifestSha = raw.Trim().ToLowerInvariant();
            if (!IsSha256(manifestSha))
                return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, "manifest_sha256 tiene que ser 64 hex.");
            if (existing?.ManifestSha256 is { } prev && prev != manifestSha)
                return ExtractionApplyResult.Fail(ExtractionRuleError.Immutable,
                    "El hash del manifiesto ya estaba registrado y no coincide.");
        }
        manifestSha ??= existing?.ManifestSha256;

        // started_at_pc y device.serial inmutables.
        var startedAtPc = existing?.StartedAtPc
            ?? (req.StartedAtPc?.UtcDateTime ?? nowServer);
        if (existing is not null && req.StartedAtPc is { } sp &&
            Math.Abs((sp.UtcDateTime - existing.StartedAtPc).TotalMilliseconds) > 1)
            return ExtractionApplyResult.Fail(ExtractionRuleError.Immutable, "started_at_pc es inmutable.");

        var serial = req.Device?.Serial?.Trim() ?? existing?.Device.Serial ?? "";
        if (existing is not null && !string.IsNullOrEmpty(existing.Device.Serial) &&
            req.Device?.Serial is { } rs && rs.Trim() != existing.Device.Serial)
            return ExtractionApplyResult.Fail(ExtractionRuleError.Immutable, "El serial del dispositivo es inmutable.");

        // Categorías: lista blanca, sin duplicados.
        var cats = req.Categories;
        if (cats is not null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in cats)
            {
                var key = c.Key ?? "";
                if (!ExtractionCategoryKeys.IsValid(key))
                    return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, $"Categoría desconocida: {key}.");
                if (!seen.Add(key))
                    return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, $"Categoría duplicada: {key}.");
                if (c.Status is { } st && !ExtractionCategoryStatuses.IsValid(st))
                    return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, $"Estado de categoría inválido: {st}.");
                if (AnyNegative(c.Count, c.Files, c.Bytes, c.Errors, c.HashMismatches))
                    return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, "Los números de la categoría no pueden ser negativos.");
            }
        }

        if (req.Totals is { } t && AnyNegative(t.Files, t.Bytes, t.Errors, t.HashMismatches))
            return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, "Los totales no pueden ser negativos.");
        if (req.ManifestEntries is < 0 || req.CustomPathsCount is < 0)
            return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, "Los contadores no pueden ser negativos.");

        // Modificaciones: lista blanca + append-only (merge por kind+at), tope 20.
        var mods = MergeModifications(existing?.DeviceModifications ?? [], req.DeviceModifications,
            out var modError);
        if (modError is not null)
            return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, modError);

        // Terminal: requiere finished_at_pc y manifest_sha256. interrupted requiere interruption.
        var finishedAtPc = req.FinishedAtPc?.UtcDateTime ?? existing?.FinishedAtPc;
        if (ExtractionStatuses.IsTerminal(status))
        {
            if (finishedAtPc is null)
                return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, "Un estado terminal requiere finished_at_pc.");
            if (manifestSha is null)
                return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, "Un estado terminal requiere manifest_sha256.");
            if (status == ExtractionStatuses.Interrupted && req.Interruption is null && existing?.Interruption is null)
                return ExtractionApplyResult.Fail(ExtractionRuleError.Invalid, "Una interrupción requiere el motivo.");
        }

        // Se capturan ANTES de mutar: `existing` y `target` pueden ser el mismo objeto.
        var hadManifest = existing?.ManifestSha256 is not null;
        var wasFinalRegistered = existing?.FinalRegisteredAt is not null;

        // Armado de la entidad.
        var target = existing ?? new Extraction { Id = id, CreatedAt = nowServer };
        target.Id = id;
        target.SchemaVersion = 1;
        target.Officer = existing?.Officer.Dni is { Length: > 0 } ? existing.Officer : officer;
        target.Status = status;
        target.Hostname = Cap(req.Hostname, existing?.Hostname, 100);
        target.OsUser = Cap(req.OsUser, existing?.OsUser, 100);
        target.AgentVersion = Cap(req.AgentVersion, existing?.AgentVersion, 100);
        target.AgentMode = ParseAgentMode(req.AgentMode) ?? existing?.AgentMode;
        target.StartedAtPc = startedAtPc;
        target.FinishedAtPc = finishedAtPc;
        target.UpdatedAt = nowServer;
        target.Device = BuildDevice(req.Device, existing?.Device, serial);
        if (req.DeviceDetails is not null)
            target.DeviceDetails = BuildDeviceDetails(req.DeviceDetails, serial, nowServer);
        if (cats is not null)
            target.Categories = cats.Select(ToCategory).ToList();
        if (req.CustomPathsCount is { } cpc) target.CustomPathsCount = cpc;
        if (req.VerifyOnDevice is { } vod) target.VerifyOnDevice = vod;
        if (req.DeviceSha256Available is { } dsa) target.DeviceSha256Available = dsa;
        if (req.Totals is { } tot) target.Totals = ToTotals(tot);
        if (req.DestinationPath is not null) target.DestinationPath = Cap(req.DestinationPath, null, 1024);
        if (req.FolderName is not null) target.FolderName = Cap(req.FolderName, null, 255);
        target.ManifestSha256 = manifestSha;
        if (req.ManifestEntries is { } me) target.ManifestEntries = me;
        else if (existing?.ManifestEntries is { } pe) target.ManifestEntries = pe;
        target.DeviceModifications = mods;
        if (req.Interruption is { } intr)
            target.Interruption = new ExtractionInterruption
            {
                ReasonCode = Cap(intr.ReasonCode, null, 100),
                Message = intr.Message is null ? null : Cap(intr.Message, null, 300),
                At = intr.At?.UtcDateTime ?? nowServer,
            };

        // PC ↔ servidor (§6.1).
        if (req.PcServerProbe is { } probe)
        {
            var m = new ClockMeasurement();
            DeviceDetailsRules.ComputePcServer(probe, nowServer, m);
            if (m.PcMinusServerMs is { } pm && m.PcServerMarginMs is { } pg)
            {
                target.PcMinusServerMs = pm;
                target.PcServerMarginMs = pg;
            }
        }

        var manifestJustSet = manifestSha is not null && !hadManifest;
        var becameFinal = ExtractionStatuses.IsTerminal(status) && !wasFinalRegistered;
        if (becameFinal) target.FinalRegisteredAt = nowServer;

        return ExtractionApplyResult.Ok(target, manifestJustSet, becameFinal);
    }

    // ── helpers ──

    private static bool IsSha256(string s) =>
        s.Length == 64 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool AnyNegative(params long?[] values) => values.Any(v => v is < 0);

    private static string Cap(string? value, string? fallback, int max)
    {
        if (value is null) return fallback ?? "";
        var clean = SafeText.Clean(value, TextShape.SingleLine).Trim();
        return clean.Length > max ? clean[..max] : clean;
    }

    private static AgentMode? ParseAgentMode(string? raw) =>
        Enum.TryParse<AgentMode>(raw, ignoreCase: true, out var m) ? m : null;

    private static ExtractionDevice BuildDevice(ExtractionDeviceDto? dto, ExtractionDevice? existing, string serial) => new()
    {
        Serial = serial,
        Manufacturer = Cap(dto?.Manufacturer, existing?.Manufacturer, 100),
        Model = Cap(dto?.Model, existing?.Model, 100),
        Platform = string.IsNullOrWhiteSpace(dto?.Platform) ? existing?.Platform ?? "android" : dto!.Platform!.Trim(),
        AndroidVersion = dto?.AndroidVersion ?? existing?.AndroidVersion,
        SdkInt = dto?.SdkInt ?? existing?.SdkInt,
    };

    private static ExtractionCategory ToCategory(ExtractionCategoryDto c) => new()
    {
        Key = c.Key ?? "",
        Kind = c.Kind ?? (ExtractionCategoryKeys.KindByKey.TryGetValue(c.Key ?? "", out var k) ? k : ""),
        Status = c.Status ?? "",
        Count = c.Count,
        Files = c.Files,
        Bytes = c.Bytes,
        Errors = c.Errors,
        HashMismatches = c.HashMismatches,
        ReasonCode = c.ReasonCode is null ? null : Cap(c.ReasonCode, null, 100),
        Method = Cap(c.Method, null, 300),
    };

    private static ExtractionTotals ToTotals(ExtractionTotalsDto t) => new()
    {
        Files = (int)(t.Files ?? 0),
        Bytes = t.Bytes ?? 0,
        Errors = (int)(t.Errors ?? 0),
        HashMismatches = (int)(t.HashMismatches ?? 0),
    };

    private static List<DeviceModification> MergeModifications(List<DeviceModification> existing,
        List<DeviceModificationDto>? incoming, out string? error)
    {
        error = null;
        var merged = new List<DeviceModification>(existing);
        var present = new HashSet<string>(existing.Select(Key), StringComparer.Ordinal);
        if (incoming is not null)
        {
            foreach (var dto in incoming)
            {
                var kind = dto.Kind ?? "";
                if (!DeviceModificationKinds.IsValid(kind))
                {
                    error = $"Modificación desconocida: {kind}.";
                    return merged;
                }
                var m = new DeviceModification
                {
                    Kind = kind,
                    At = dto.At?.UtcDateTime ?? default,
                    Detail = dto.Detail is null ? null : Cap(dto.Detail, null, 300),
                    AfterClose = dto.AfterClose ?? false,
                };
                var key = Key(m);
                if (present.Add(key)) merged.Add(m);
            }
        }
        if (merged.Count > MaxDeviceModifications)
            error = $"Demasiadas modificaciones (máximo {MaxDeviceModifications}).";
        return merged;

        static string Key(DeviceModification m) => $"{m.Kind}|{m.At.Ticks}";
    }

    private static DeviceDetails BuildDeviceDetails(ExtractionDeviceDetailsDto dto, string serial, DateTime nowServer)
    {
        var dd = new DeviceDetails
        {
            ReadAt = nowServer,
            Platform = string.IsNullOrWhiteSpace(dto.Platform) ? "android" : dto.Platform!.Trim(),
            ConnectionId = string.IsNullOrWhiteSpace(dto.ConnectionId) ? serial : dto.ConnectionId!.Trim(),
            AgentVersion = DeviceDetailsRules.CleanAgentVersion(dto.AgentVersion),
            Fields = DeviceDetailsRules.NormalizeFields(dto.Fields),
            IncludeSsidInReport = dto.IncludeSsidInReport ?? false,
        };
        if (dto.ClockStart is not null)
        {
            var m = new ClockMeasurement { Kind = ClockKinds.Start, RecordedAt = nowServer };
            DeviceDetailsRules.ComputeClock(dto.ClockStart, m);
            dd.ClockStart = m;
        }
        if (dto.ClockClose is not null)
        {
            var m = new ClockMeasurement { Kind = ClockKinds.Close, RecordedAt = nowServer };
            DeviceDetailsRules.ComputeClock(dto.ClockClose, m);
            dd.ClockClose = m;
        }
        return dd;
    }
}
