using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

namespace Factum.Agent.Services;

public interface IWebcamService
{
    Task<string> CaptureAsync(string outputPath, CancellationToken ct = default);
}

public sealed class WebcamService : IWebcamService
{
    private readonly bool _mock;

    public WebcamService(IOptions<AgentOptions> opts) => _mock = opts.Value.Mock;

    public async Task<string> CaptureAsync(string outputPath, CancellationToken ct = default)
    {
        if (_mock)
        {
            await File.WriteAllBytesAsync(outputPath, MockJpegBytes(), ct);
            return outputPath;
        }

        var (args, device) = BuildFfmpegArgs(outputPath);
        var r = await ProcessRunner.RunAsync("ffmpeg", args, ct);
        if (!r.Success)
            throw new InvalidOperationException($"ffmpeg webcam falló ({device}): {r.Stderr}");

        return outputPath;
    }

    private static (string Args, string Device) BuildFfmpegArgs(string outputPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ($"-y -f dshow -i video=\"Integrated Camera\" -frames:v 1 -q:v 2 \"{outputPath}\"", "dshow");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return ($"-y -f avfoundation -framerate 30 -i 0 -frames:v 1 -q:v 2 \"{outputPath}\"", "avfoundation");

        return ($"-y -f v4l2 -i /dev/video0 -frames:v 1 -q:v 2 \"{outputPath}\"", "v4l2");
    }

    private static byte[] MockJpegBytes() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a" +
        "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAARC" +
        "AACAAIDASIA//EABQAAQAAAAAAAAAAAAAAAAAAAAn/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFAAB" +
        "AAAAAAAAAAAAAAAAAAAAAP/EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAMAwEAAhEDEQA/ACWQ/9k=");
}
