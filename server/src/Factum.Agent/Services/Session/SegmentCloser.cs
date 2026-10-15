using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services.Session;

/// <summary>Lo que hace falta para cerrar un tramo (copia, sin compartir el objeto del diario).</summary>
public sealed record SegmentCloseInput(string Filename, DateTime StartedAt, DateTime? StoppedAt, bool Forced,
    DateTime? FallbackStoppedAt = null);

/// <summary>
/// Resultado del cierre: <c>saved</c> (con tamaño, hash, duración y validación) o <c>discarded</c> (0 B o ilegible).
/// <c>Filename</c> puede cambiar solo si ya había otro archivo con ese nombre en la carpeta del caso (no debería pasar).
/// </summary>
public sealed record SegmentCloseResult(string State, string Filename, DateTime StoppedAt, double RecordedSeconds,
    double? DurationSeconds, string? Validation, long? Size, string? Sha256);

/// <summary>
/// Cierre de un tramo (grabacion-sesion-completa §5.6), común a la sesión en curso y a la recuperación (§5.10):
/// tamaño → (remux si hizo falta) → validación con ffmpeg → rename a la carpeta del caso → SHA-256. Un archivo de
/// 0 B se borra; uno ilegible va a <c>descartes/</c>. Nunca borra otra cosa.
/// </summary>
public sealed class SegmentCloser
{
    private readonly SessionJournal _journal;
    private readonly ICaseEvidenceStore _store;
    private readonly RecordingOptions _rec;
    private readonly string _dataDir;
    private readonly ILogger _log;

    public SegmentCloser(SessionJournal journal, ICaseEvidenceStore store, IOptions<AgentOptions> opts,
        ILogger<SegmentCloser> log)
        : this(journal, store, (opts.Value.Recording ?? new RecordingOptions()).Clone().Normalize(null), log)
    {
    }

    internal SegmentCloser(SessionJournal journal, ICaseEvidenceStore store, RecordingOptions rec, ILogger? log = null)
    {
        _journal = journal;
        _store = store;
        _rec = rec;
        _dataDir = journal.DataDirectory;
        _log = log ?? NullLogger.Instance;
    }

    public async Task<SegmentCloseResult> CloseAsync(string caseId, SegmentCloseInput input)
    {
        var staging = _journal.StagingPath(caseId, input.Filename);
        var caseDir = _store.CaseDir(caseId);
        var final = Path.Combine(caseDir, input.Filename);
        var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;

        // Tatana se cerró justo después del rename: el tramo ya está en la carpeta del caso.
        if (!File.Exists(staging) && File.Exists(final) && AdbService.FileSize(final) > 0)
        {
            var probe0 = ffmpeg is null ? null : await FfmpegProbe.ProbeAsync(ffmpeg, final, CancellationToken.None);
            var stopped0 = ResolveStoppedAt(input, probe0?.DurationSeconds);
            var recorded0 = Math.Max(0, (stopped0 - input.StartedAt).TotalSeconds);
            var (status0, duration0, _) = RecordingValidation.Evaluate(AdbService.FileSize(final), probe0, recorded0, _rec);
            var hash0 = await _store.Sha256Async(final, CancellationToken.None);
            return Saved(input.Filename, stopped0, recorded0, duration0,
                status0 == RecordingValidationStatus.Incomplete ? RecordingValidationStatus.Incomplete : RecordingValidationStatus.Ok,
                AdbService.FileSize(final), hash0);
        }

        var size = AdbService.FileSize(staging);
        if (size <= 0)
        {
            AdbService.TryDelete(staging);
            _log.LogWarning("Tramo de la sesión vacío (0 B), descartado: {Name}", input.Filename);
            var stoppedE = ResolveStoppedAt(input, null);
            return Discarded(input.Filename, stoppedE, input.StartedAt);
        }

        FfmpegProbeResult? probe = ffmpeg is null ? null : await FfmpegProbe.ProbeAsync(ffmpeg, staging, CancellationToken.None);
        // Tras un kill (o si ffmpeg no puede leerlo), se reescribe el contenedor sin recodificar y se vuelve a medir.
        if (ffmpeg is not null && (input.Forced || probe?.DurationSeconds is null || probe.HasVideo == false))
        {
            if (await AdbService.RemuxFileAsync(staging, _log, CancellationToken.None))
            {
                size = AdbService.FileSize(staging);
                probe = await FfmpegProbe.ProbeAsync(ffmpeg, staging, CancellationToken.None);
            }
        }

        var stoppedAt = ResolveStoppedAt(input, probe?.DurationSeconds);
        var recorded = Math.Max(0, (stoppedAt - input.StartedAt).TotalSeconds);
        var (status, duration, _) = RecordingValidation.Evaluate(size, probe, recorded, _rec);
        if (status is RecordingValidationStatus.Empty)
        {
            AdbService.TryDelete(staging);
            return Discarded(input.Filename, stoppedAt, input.StartedAt);
        }
        if (status is RecordingValidationStatus.Unreadable)
        {
            var moved = AdbService.MoveToDiscards(_dataDir, staging, _log);
            _log.LogWarning("Tramo de la sesión ilegible movido a descartes: {Path}", moved ?? staging);
            return Discarded(input.Filename, stoppedAt, input.StartedAt);
        }

        Directory.CreateDirectory(caseDir);
        var filename = input.Filename;
        var dest = final;
        if (File.Exists(dest))
        {
            dest = AdbService.UniqueDiscardPath(caseDir, filename, File.Exists);
            filename = Path.GetFileName(dest);
            _log.LogWarning("Ya había un archivo {Name} en la carpeta del caso; el tramo se guarda como {New}",
                input.Filename, filename);
        }
        File.Move(staging, dest, overwrite: false);
        var hash = await _store.Sha256Async(dest, CancellationToken.None);
        _log.LogInformation(
            "Tramo de la sesión guardado: {Name} ({Size} bytes, {Recorded:0.0} s grabados, duración {Duration}, {Status})",
            filename, size, recorded,
            duration?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "-", status);
        return Saved(filename, stoppedAt, recorded, duration, status, size, hash);
    }

    // stopped_at: el pedido de cierre; si no se sabe (recuperación), inicio + duración del video; si tampoco, el
    // último latido; y si no, el inicio.
    private static DateTime ResolveStoppedAt(SegmentCloseInput input, double? duration)
    {
        if (input.StoppedAt is { } s) return s;
        if (duration is { } d && d > 0) return input.StartedAt.AddSeconds(d);
        if (input.FallbackStoppedAt is { } f && f >= input.StartedAt) return f;
        return input.StartedAt;
    }

    private static double Round1(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero);

    private static SegmentCloseResult Saved(string filename, DateTime stoppedAt, double recorded, double? duration,
        string status, long size, string hash) =>
        new(SegmentStates.Saved, filename, stoppedAt, Round1(recorded), duration is { } d ? Round1(d) : null,
            status == RecordingValidationStatus.Incomplete ? RecordingValidationStatus.Incomplete : RecordingValidationStatus.Ok,
            size, hash);

    private static SegmentCloseResult Discarded(string filename, DateTime stoppedAt, DateTime startedAt) =>
        new(SegmentStates.Discarded, filename, stoppedAt, Round1(Math.Max(0, (stoppedAt - startedAt).TotalSeconds)), null,
            null, null, null);
}
