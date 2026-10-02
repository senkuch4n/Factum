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
    public static async Task<ProcessResult> RunArgumentListAsync(string executable,
        IReadOnlyList<string> args, CancellationToken ct = default)
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

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (stdout) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };

        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new ProcessResult(-1, "", $"No se encontró '{executable}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);
        process.WaitForExit(); // termina de vaciar los pipes
        lock (stdout) lock (stderr)
            return new ProcessResult(process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
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
