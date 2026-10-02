using System.Security.Cryptography;
using Factum.Backend.Infrastructure;

namespace Factum.Backend.Tests;

/// <summary>
/// Staging atómico de subidas (subida-archivos-grandes §11 T3). Cada test trabaja en su propia
/// carpeta temporal (Path.GetTempPath()/factum-upload-&lt;guid&gt;) y la borra al final: no toca
/// Mongo ni Storage:DataDirectory.
/// </summary>
public sealed class UploadStorageTests : IDisposable
{
    private const int MiB = 1024 * 1024;
    private const string CaseId = "caso-test";

    private readonly string _root;
    private readonly FakeDiskSpaceProbe _probe = new() { Free = long.MaxValue / 4 };

    public UploadStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "factum-upload-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private StorageService NewStorage(Func<string, Stream>? openTemp = null) =>
        new(new StorageOptions { DataDirectory = _root, MinFreeBytes = 1024 }, _probe, openTemp);

    private string UploadTmp => Path.Combine(_root, ".upload-tmp");
    private string CaseDir => Path.Combine(_root, "cases", CaseId);

    private string[] TempFiles() =>
        Directory.Exists(UploadTmp) ? Directory.GetFiles(UploadTmp) : [];

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
    private static string ShaFile(string path) => Sha(File.ReadAllBytes(path));

    private static async Task<T> CatchAsync<T>(Func<Task> act) where T : Exception
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(act);
        return Assert.IsType<T>(ex);
    }

    // ── 1. Subida completa ───────────────────────────────────────────────────

    [Fact]
    public async Task Complete_upload_commits_same_bytes_and_hash()
    {
        var storage = NewStorage();
        var data = RandomNumberGenerator.GetBytes(5 * MiB);

        using var staged = await storage.StageUploadAsync(CaseId, "video.mp4", new MemoryStream(data),
            data.Length, CancellationToken.None);
        var info = staged.Commit();

        var final = Path.Combine(CaseDir, "video.mp4");
        Assert.Equal(final, info.FullName);
        Assert.Equal(data, File.ReadAllBytes(final));
        Assert.Equal(Sha(data), staged.Hash);
        Assert.Equal(data.Length, staged.Length);
        Assert.Empty(TempFiles());
        var listed = await storage.ListFilesAsync(CaseId);
        Assert.Equal("video.mp4", Assert.Single(listed).Name);
    }

    // ── 2. El temporal nunca se lista ────────────────────────────────────────

    [Fact]
    public async Task Temp_file_is_never_listed_while_in_flight()
    {
        var storage = NewStorage();
        var data = RandomNumberGenerator.GetBytes(4 * MiB);
        var gate = new GatedStream(data, data.Length / 2);

        var task = storage.StageUploadAsync(CaseId, "video.mp4", gate, data.Length, CancellationToken.None);
        await gate.ReachedGate.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(await storage.ListFilesAsync(CaseId));
        Assert.False(File.Exists(Path.Combine(CaseDir, "video.mp4")));
        var temp = Assert.Single(TempFiles());
        Assert.StartsWith(Path.GetFullPath(UploadTmp) + Path.DirectorySeparatorChar, Path.GetFullPath(temp));
        Assert.EndsWith(".part", temp);

        gate.Release();
        using var staged = await task;
        staged.Commit();
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(CaseDir, "video.mp4")));
        Assert.Empty(TempFiles());
    }

    // ── 3/4. Longitud distinta ───────────────────────────────────────────────

    [Fact]
    public async Task Short_body_is_incomplete_and_leaves_nothing()
    {
        var storage = NewStorage();
        var data = RandomNumberGenerator.GetBytes(MiB + 123);

        var ex = await CatchAsync<UploadIncompleteException>(() =>
            storage.StageUploadAsync(CaseId, "x.bin", new MemoryStream(data), data.Length + 10, CancellationToken.None));

        Assert.Equal(data.Length, ex.ReceivedBytes);
        Assert.Equal(data.Length + 10, ex.ExpectedBytes);
        Assert.False(File.Exists(Path.Combine(CaseDir, "x.bin")));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task Long_body_is_incomplete_and_leaves_nothing()
    {
        var storage = NewStorage();
        var data = RandomNumberGenerator.GetBytes(3 * MiB);

        await CatchAsync<UploadIncompleteException>(() =>
            storage.StageUploadAsync(CaseId, "x.bin", new MemoryStream(data), data.Length - 1, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(CaseDir, "x.bin")));
        Assert.Empty(TempFiles());
    }

    // ── 5. Corte a mitad ─────────────────────────────────────────────────────

    [Fact]
    public async Task Connection_reset_is_incomplete_with_inner()
    {
        var storage = NewStorage();
        var data = RandomNumberGenerator.GetBytes(3 * MiB);
        var source = new ScriptedStream(data, failAfter: MiB, () => new IOException("connection reset"));

        var ex = await CatchAsync<UploadIncompleteException>(() =>
            storage.StageUploadAsync(CaseId, "x.bin", source, data.Length, CancellationToken.None));

        Assert.IsType<IOException>(ex.InnerException);
        Assert.Equal(MiB, ex.ReceivedBytes);
        Assert.False(File.Exists(Path.Combine(CaseDir, "x.bin")));
        Assert.Empty(TempFiles());
    }

    // ── 6. Cancelación ───────────────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_after_first_block_leaves_nothing()
    {
        var storage = NewStorage();
        var data = RandomNumberGenerator.GetBytes(4 * MiB);
        using var cts = new CancellationTokenSource();
        var source = new ScriptedStream(data, onRead: n => { if (n == 2) cts.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.StageUploadAsync(CaseId, "x.bin", source, data.Length, cts.Token));

        Assert.False(File.Exists(Path.Combine(CaseDir, "x.bin")));
        Assert.Empty(TempFiles());
    }

    // ── 7. Disco lleno a mitad ───────────────────────────────────────────────

    [Fact]
    public async Task Enospc_while_writing_is_insufficient_storage()
    {
        var storage = NewStorage(p => new FailingWriteStream(p, 2 * MiB,
            () => new IOException("No space left on device", 28)));
        var data = RandomNumberGenerator.GetBytes(5 * MiB);
        _probe.Free = 3 * MiB;

        var ex = await CatchAsync<InsufficientStorageException>(() =>
            storage.StageUploadAsync(CaseId, "x.bin", new MemoryStream(data), data.Length, CancellationToken.None));

        Assert.Equal(data.Length + 1024, ex.RequiredBytes);
        Assert.Equal(3 * MiB, ex.AvailableBytes);
        Assert.False(File.Exists(Path.Combine(CaseDir, "x.bin")));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task Generic_io_error_with_no_free_space_is_insufficient_storage()
    {
        var storage = NewStorage(p => new FailingWriteStream(p, 2 * MiB, () =>
        {
            _probe.Free = 0;
            return new IOException("algo falló");
        }));
        var data = RandomNumberGenerator.GetBytes(5 * MiB);

        await CatchAsync<InsufficientStorageException>(() =>
            storage.StageUploadAsync(CaseId, "x.bin", new MemoryStream(data), data.Length, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(CaseDir, "x.bin")));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task Generic_io_error_with_free_space_is_rethrown()
    {
        var storage = NewStorage(p => new FailingWriteStream(p, 2 * MiB, () => new IOException("algo falló")));
        var data = RandomNumberGenerator.GetBytes(5 * MiB);

        var ex = await CatchAsync<IOException>(() =>
            storage.StageUploadAsync(CaseId, "x.bin", new MemoryStream(data), data.Length, CancellationToken.None));

        Assert.Equal("algo falló", ex.Message);
        Assert.False(File.Exists(Path.Combine(CaseDir, "x.bin")));
        Assert.Empty(TempFiles());
    }

    // ── 8. Reemplazo atómico ─────────────────────────────────────────────────

    [Fact]
    public async Task Replacement_is_atomic()
    {
        var storage = NewStorage();
        var a = RandomNumberGenerator.GetBytes(2 * MiB);
        var b = RandomNumberGenerator.GetBytes(3 * MiB);
        var final = Path.Combine(CaseDir, "video.mp4");

        // (a) existe video.mp4 = A
        using (var sa = await storage.StageUploadAsync(CaseId, "video.mp4", new MemoryStream(a), a.Length,
                   CancellationToken.None))
            sa.Commit();
        Assert.Equal(Sha(a), ShaFile(final));

        // (b) B cortada a mitad → sigue A
        await CatchAsync<UploadIncompleteException>(() => storage.StageUploadAsync(CaseId, "video.mp4",
            new ScriptedStream(b, failAfter: MiB, () => new IOException("reset")), b.Length, CancellationToken.None));
        Assert.Equal(Sha(a), ShaFile(final));
        Assert.Empty(TempFiles());

        // (c) B con compuerta: a mitad sigue A
        var gate = new GatedStream(b, b.Length / 2);
        var task = storage.StageUploadAsync(CaseId, "video.mp4", gate, b.Length, CancellationToken.None);
        await gate.ReachedGate.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(Sha(a), ShaFile(final));
        gate.Release();

        // (d) commit → B, sin temporales
        using var sb = await task;
        Assert.Equal(Sha(a), ShaFile(final));
        sb.Commit();
        Assert.Equal(Sha(b), ShaFile(final));
        Assert.Empty(TempFiles());
        Assert.Single(await storage.ListFilesAsync(CaseId));
    }

    // ── 9. Dispose sin commit ────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_without_commit_deletes_temp()
    {
        var storage = NewStorage();
        var data = RandomNumberGenerator.GetBytes(MiB);

        var staged = await storage.StageUploadAsync(CaseId, "x.bin", new MemoryStream(data), data.Length,
            CancellationToken.None);
        Assert.True(File.Exists(staged.TempPath));
        staged.Dispose();

        Assert.False(File.Exists(staged.TempPath));
        Assert.False(File.Exists(Path.Combine(CaseDir, "x.bin")));
        Assert.Empty(TempFiles());
    }

    // ── 10. Huérfanos ────────────────────────────────────────────────────────

    [Fact]
    public void Cleanup_deletes_only_upload_tmp_files()
    {
        var storage = NewStorage();
        Directory.CreateDirectory(UploadTmp);
        for (var i = 0; i < 3; i++)
            File.WriteAllBytes(Path.Combine(UploadTmp, $"{CaseId}.{Guid.NewGuid():N}.part"),
                RandomNumberGenerator.GetBytes(1000));
        var sub = Path.Combine(UploadTmp, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(sub, "nested.part"), [1, 2, 3]);
        Directory.CreateDirectory(CaseDir);
        var evidence = Path.Combine(CaseDir, "evidencia_real.mp4");
        File.WriteAllBytes(evidence, RandomNumberGenerator.GetBytes(4096));
        var rootFile = Path.Combine(_root, "otro.txt");
        File.WriteAllBytes(rootFile, RandomNumberGenerator.GetBytes(100));
        var evidenceHash = ShaFile(evidence);
        var rootHash = ShaFile(rootFile);

        Assert.Equal(3, storage.CleanupOrphanUploads());

        Assert.Empty(Directory.GetFiles(UploadTmp));
        Assert.True(File.Exists(Path.Combine(sub, "nested.part"))); // sin recursión
        Assert.Equal(evidenceHash, ShaFile(evidence));
        Assert.Equal(rootHash, ShaFile(rootFile));
    }

    [Fact]
    public void Cleanup_without_upload_tmp_creates_it_and_returns_zero()
    {
        var storage = NewStorage();
        Assert.False(Directory.Exists(UploadTmp));
        Assert.Equal(0, storage.CleanupOrphanUploads());
        Assert.True(Directory.Exists(UploadTmp));
    }

    // ── 11. Nombre que escapa (defensa en profundidad) ───────────────────────

    [Theory]
    [InlineData("../x")]
    [InlineData("../../x")]
    [InlineData("sub/x")]
    [InlineData("")]
    public async Task Escaping_name_throws_and_creates_nothing(string name)
    {
        var storage = NewStorage();
        var data = RandomNumberGenerator.GetBytes(1000);

        await CatchAsync<ArgumentException>(() =>
            storage.StageUploadAsync(CaseId, name, new MemoryStream(data), data.Length, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(_root, "cases", "x")));
        Assert.False(File.Exists(Path.Combine(_root, "x")));
        Assert.Empty(TempFiles());
        var all = Directory.GetFiles(_root, "*", SearchOption.AllDirectories);
        Assert.Empty(all);
    }

    [Fact]
    public void GetAvailableFreeBytes_uses_probe_on_root()
    {
        var storage = NewStorage();
        _probe.Free = 12345;
        Assert.Equal(12345, storage.GetAvailableFreeBytes());
        Assert.Equal(Path.GetFullPath(_root), _probe.LastPath);
    }

    // ── Dobles ───────────────────────────────────────────────────────────────

    private sealed class FakeDiskSpaceProbe : IDiskSpaceProbe
    {
        public long? Free { get; set; }
        public string? LastPath { get; private set; }

        public long? GetAvailableFreeBytes(string path)
        {
            LastPath = path;
            return Free;
        }
    }

    /// <summary>Stream de lectura con hooks: falla después de N bytes o avisa en cada lectura.</summary>
    private sealed class ScriptedStream(byte[] data, long failAfter = long.MaxValue,
        Func<Exception>? failure = null, Action<int>? onRead = null) : Stream
    {
        private long _pos;
        private int _reads;

        public ScriptedStream(byte[] data, Action<int> onRead) : this(data, long.MaxValue, null, onRead) { }
        public ScriptedStream(byte[] data, long failAfter, Func<Exception> failure)
            : this(data, failAfter, failure, null) { }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Yield();
            _reads++;
            onRead?.Invoke(_reads);
            ct.ThrowIfCancellationRequested();
            if (_pos >= failAfter && failure is not null) throw failure();
            var max = (int)Math.Min(buffer.Length, Math.Min(data.Length - _pos, failAfter - _pos));
            if (max <= 0) return 0;
            data.AsMemory((int)_pos, max).CopyTo(buffer);
            _pos += max;
            return max;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Entrega hasta <c>gateAt</c> bytes y espera <see cref="Release"/> para seguir.</summary>
    private sealed class GatedStream(byte[] data, int gateAt) : Stream
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _pos;

        public Task ReachedGate => _reached.Task;
        public void Release() => _release.TrySetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pos == gateAt)
            {
                _reached.TrySetResult();
                await _release.Task.WaitAsync(ct);
            }
            var limit = _pos < gateAt ? gateAt : data.Length;
            var max = Math.Min(buffer.Length, limit - _pos);
            if (max <= 0) return 0;
            data.AsMemory(_pos, max).CopyTo(buffer);
            _pos += max;
            return max;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Escribe a un archivo real y lanza la excepción dada al pasar <c>limit</c> bytes.</summary>
    private sealed class FailingWriteStream(string path, long limit, Func<Exception> failure) : Stream
    {
        private readonly FileStream _inner = new(path, FileMode.CreateNew, FileAccess.Write);
        private long _written;

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            if (_written + buffer.Length > limit) throw failure();
            await _inner.WriteAsync(buffer, ct);
            _written += buffer.Length;
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return base.DisposeAsync();
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
