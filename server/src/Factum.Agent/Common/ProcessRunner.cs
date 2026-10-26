using System.Diagnostics;
using System.Text;

namespace Factum.Agent.Common;

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
}

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string executable, string arguments,
        CancellationToken ct = default, string? workingDir = null)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory()
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Executable not found — return a failed result instead of crashing
            return new ProcessResult(-1, "", $"No se encontró '{executable}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);
        return new ProcessResult(process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
    }

    public static Task<ProcessResult> RunAsync(string executable, IEnumerable<string> args,
        CancellationToken ct = default, string? workingDir = null) =>
        RunAsync(executable, string.Join(' ', args.Select(QuoteArg)), ct, workingDir);

    // Igual que RunAsync, pero con ProcessStartInfo.ArgumentList: cada argumento queda citado
    // correctamente por .NET (rutas con espacios, acentos o comillas del perfil de Windows).
    // `environment` (opcional, aditivo) suma/pisa variables del proceso hijo (ej. PYTHONUTF8 para
    // el Python de iPhone, SDD ios-herramientas-windows §6.1). `timeout` (opcional): si se vence,
    // se mata el árbol del proceso y se devuelve ExitCode = TimeoutExitCode. Con `timeout`, cancelar
    // `ct` también mata el árbol y relanza OperationCanceledException. `onStdoutLine` (opcional) se
    // invoca por cada línea de stdout además de acumularla (progreso de ffmpeg, ios-grabacion-larga
    // §5.2); una excepción del callback se ignora.
    public const int TimeoutExitCode = -2;

    public static async Task<ProcessResult> RunArgumentListAsync(string executable,
        IReadOnlyList<string> args, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null,
        Action<string>? onStdoutLine = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = executable,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (environment is not null)
            foreach (var (k, v) in environment) psi.Environment[k] = v;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (stdout) stdout.AppendLine(e.Data);
            if (onStdoutLine is not null)
                try { onStdoutLine(e.Data); } catch { /* un callback roto no corta el proceso */ }
        };
        process.ErrorDataReceived  += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };

        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new ProcessResult(-1, "", $"No se encontró '{executable}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (timeout is { } limit)
        {
            using var timeoutCts = new CancellationTokenSource(limit);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                try { process.WaitForExit(5000); } catch (InvalidOperationException) { }
                ct.ThrowIfCancellationRequested();
                lock (stdout) lock (stderr)
                    return new ProcessResult(TimeoutExitCode, stdout.ToString().Trim(),
                        (stderr.ToString().Trim() + $"\nNo terminó en {limit.TotalSeconds:0} s").Trim());
            }
        }
        else
        {
            await process.WaitForExitAsync(ct);
        }
        process.WaitForExit(); // termina de vaciar los pipes
        lock (stdout) lock (stderr)
            return new ProcessResult(process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
    }

    // Manda stdout (texto) DIRECTO a un archivo y captura las últimas 4 KiB de stderr, con timeout opcional
    // (extraccion-logica-android §5.3.1). Para `content query`/`find`/`sha256sum` por adb, que pueden tirar MB
    // de salida: no se acumula stdout en memoria. Usa ArgumentList (sin shell local). Al cancelar o vencer el
    // timeout, mata el árbol del proceso. Devuelve (exit code, cola de stderr). Exit = TimeoutExitCode si venció.
    public static async Task<(int ExitCode, string StderrTail)> RunToFileAsync(string executable,
        IReadOnlyList<string> args, string outputPath, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        const int StderrTailBytes = 4096;
        var psi = new ProcessStartInfo
        {
            FileName               = executable,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (stderr)
            {
                stderr.AppendLine(e.Data);
                // Conservar solo la cola: recorta por el frente si creció mucho.
                if (stderr.Length > StderrTailBytes * 2)
                    stderr.Remove(0, stderr.Length - StderrTailBytes);
            }
        };

        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return (-1, $"No se encontró '{executable}': {ex.Message}");
        }

        process.BeginErrorReadLine();

        var copyTask = Task.Run(async () =>
        {
            await using var fs = File.Create(outputPath);
            await process.StandardOutput.BaseStream.CopyToAsync(fs, ct);
        }, ct);

        try
        {
            if (timeout is { } limit)
            {
                using var timeoutCts = new CancellationTokenSource(limit);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                try
                {
                    await process.WaitForExitAsync(linked.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    try { process.WaitForExit(5000); } catch (InvalidOperationException) { }
                    try { await copyTask; } catch { /* el pipe se cortó al matar */ }
                    ct.ThrowIfCancellationRequested();
                    return (TimeoutExitCode, Tail(stderr) + $"\nNo terminó en {limit.TotalSeconds:0} s");
                }
            }
            else
            {
                await process.WaitForExitAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await copyTask; } catch { /* idem */ }
            throw;
        }

        try { await copyTask; } catch (IOException) { /* el proceso cerró stdout */ }
        process.WaitForExit();
        return (process.ExitCode, Tail(stderr));

        static string Tail(StringBuilder sb) { lock (sb) return sb.ToString().Trim(); }
    }

    // Copia stdout binario directo a un archivo — necesario para screencap PNG, pull de video, etc.
    public static async Task<int> RunBinaryToFileAsync(string executable, IEnumerable<string> args,
        string outputPath, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable, string.Join(' ', args.Select(QuoteArg)))
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = false,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var process = new Process { StartInfo = psi };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception) { return -1; }

        await using var fs = File.Create(outputPath);
        await process.StandardOutput.BaseStream.CopyToAsync(fs, ct);
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    private static string QuoteArg(string arg) =>
        arg.Contains(' ') ? $"\"{arg}\"" : arg;
}
