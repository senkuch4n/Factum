using System.Security.Cryptography;
using Factum.Backend.DTOs;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Infrastructure;

public sealed class StorageOptions
{
    public string DataDirectory { get; set; } = "./data";
}

public interface IStorageService
{
    string CaseDir(string caseId);
    Task<(string Path, string Hash)> SaveFileAsync(string caseId, string filename,
        Stream content, CancellationToken ct = default);
    Task<List<FileInfoDto>> ListFilesAsync(string caseId);
    Task<string> Sha256Async(string path);
    void DeleteCaseFiles(string caseId);
}

public sealed class StorageService : IStorageService
{
    private readonly string _root;

    public StorageService(IOptions<StorageOptions> opts)
    {
        _root = Path.GetFullPath(opts.Value.DataDirectory);
        Directory.CreateDirectory(_root);
    }

    public string CaseDir(string caseId)
    {
        var dir = Path.Combine(_root, "cases", caseId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<(string Path, string Hash)> SaveFileAsync(string caseId, string filename,
        Stream content, CancellationToken ct = default)
    {
        var dir = CaseDir(caseId);
        var path = Path.Combine(dir, filename);
        using var sha = SHA256.Create();
        await using var fs = File.Create(path);
        using var cs = new CryptoStream(fs, sha, CryptoStreamMode.Write);
        await content.CopyToAsync(cs, ct);
        cs.FlushFinalBlock();
        var hash = Convert.ToHexStringLower(sha.Hash!);
        return (path, hash);
    }

    public Task<List<FileInfoDto>> ListFilesAsync(string caseId)
    {
        var dir = CaseDir(caseId);
        var files = Directory.EnumerateFiles(dir)
            .Select(p =>
            {
                var info = new FileInfo(p);
                return new FileInfoDto(info.Name, info.Length, string.Empty, info.LastWriteTimeUtc);
            })
            .ToList();
        return Task.FromResult(files);
    }

    public async Task<string> Sha256Async(string path)
    {
        using var sha = SHA256.Create();
        await using var fs = File.OpenRead(path);
        var hash = await sha.ComputeHashAsync(fs);
        return Convert.ToHexStringLower(hash);
    }

    public void DeleteCaseFiles(string caseId)
    {
        var dir = Path.Combine(_root, "cases", caseId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }
}
