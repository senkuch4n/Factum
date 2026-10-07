using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Factum.Backend.Services.Reports;
using ICSharpCode.SharpZipLib.Zip;
using Xunit.Abstractions;

namespace Factum.Backend.Tests;

/// <summary>
/// Ida y vuelta del ZIP de evidencia (Refactorizaciones/zip-cifrado-real.md §8 B15). Cada test
/// trabaja en su propia carpeta temporal (Path.GetTempPath()/&lt;guid&gt;) y la borra al final:
/// no toca Mongo ni Storage:DataDirectory.
/// </summary>
public sealed class EvidenceZipTests : IDisposable
{
    private const string Password = "ABCDEFGH23456789";
    private const string WrongPassword = "WRONGWRONGWRONG2";
    private const string UnicodeName = "device_pull_canción ñ.txt";

    private readonly ITestOutputHelper _output;
    private readonly string _root;
    private readonly string _src;

    public EvidenceZipTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), "factum-evidencezip-" + Guid.NewGuid().ToString("N"));
        _src = Path.Combine(_root, "src");
        Directory.CreateDirectory(_src);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (List<string> Paths, Dictionary<string, string> Expected) MakeEvidence()
    {
        var paths = new List<string>();
        void Add(string name, byte[] data)
        {
            var p = Path.Combine(_src, name);
            File.WriteAllBytes(p, data);
            paths.Add(p);
        }

        Add("screenshot_1.png", RandomNumberGenerator.GetBytes(64 * 1024));
        Add("screenshot_2.png", RandomNumberGenerator.GetBytes(10 * 1024));
        Add("recording_1.mp4", RandomNumberGenerator.GetBytes(300 * 1024));
        Add("vacio.txt", []);
        Add(UnicodeName, Encoding.UTF8.GetBytes("contenido con tildes: áéíóú ñ"));

        var expected = paths.ToDictionary(Path.GetFileName, p => Sha256(p)!, StringComparer.Ordinal)!;
        return (paths, expected!);
    }

    private static string Sha256(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(fs));
    }

    private string ZipPath(string name = "evidencia_test.zip") => Path.Combine(_root, name);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_Encrypted()
    {
        var (paths, expected) = MakeEvidence();
        var zip = ZipPath();

        await EvidenceZip.WriteAsync(zip, paths, Password, CancellationToken.None);
        await EvidenceZip.VerifyAsync(zip, expected, Password, CancellationToken.None);

        using var zf = new ZipFile(zip);
        Assert.Equal(expected.Count, (int)zf.Count);
        foreach (ZipEntry e in zf)
        {
            Assert.True(e.IsCrypted, $"{e.Name} no está cifrado");
            Assert.Equal(256, e.AESKeySize);
            var (vendorVersion, strength) = ReadAesExtra(e.ExtraData);
            Assert.Equal(2, vendorVersion); // AE-2
            Assert.Equal(3, strength);      // AES-256
        }
        Assert.Contains(expected.Keys, n => n == UnicodeName);
    }

    [Fact]
    public async Task Verify_WrongPassword_Throws()
    {
        var (paths, expected) = MakeEvidence();
        var zip = ZipPath();
        await EvidenceZip.WriteAsync(zip, paths, Password, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<EvidenceZipVerificationException>(() =>
            EvidenceZip.VerifyAsync(zip, expected, WrongPassword, CancellationToken.None));
        Assert.DoesNotContain(WrongPassword, ex.Message);
        Assert.DoesNotContain(Password, ex.Message);
        _output.WriteLine(ex.Message);
    }

    [Fact]
    public async Task Verify_HashMismatch_Throws()
    {
        var (paths, expected) = MakeEvidence();
        var zip = ZipPath();
        await EvidenceZip.WriteAsync(zip, paths, Password, CancellationToken.None);

        expected["screenshot_2.png"] = new string('0', 64);
        var ex = await Assert.ThrowsAsync<EvidenceZipVerificationException>(() =>
            EvidenceZip.VerifyAsync(zip, expected, Password, CancellationToken.None));
        Assert.Contains("screenshot_2.png", ex.Message);
    }

    [Fact]
    public async Task Verify_MissingOrExtraEntry_Throws()
    {
        var (paths, expected) = MakeEvidence();
        var zip = ZipPath();
        await EvidenceZip.WriteAsync(zip, paths, Password, CancellationToken.None);

        // Falta uno en lo esperado (el ZIP tiene uno de más).
        var fewer = new Dictionary<string, string>(expected, StringComparer.Ordinal);
        fewer.Remove("screenshot_1.png");
        await Assert.ThrowsAsync<EvidenceZipVerificationException>(() =>
            EvidenceZip.VerifyAsync(zip, fewer, Password, CancellationToken.None));

        // Se espera uno que el ZIP no tiene.
        var more = new Dictionary<string, string>(expected, StringComparer.Ordinal)
        {
            ["no_existe.png"] = new string('a', 64),
        };
        await Assert.ThrowsAsync<EvidenceZipVerificationException>(() =>
            EvidenceZip.VerifyAsync(zip, more, Password, CancellationToken.None));

        // Mismo conteo, pero un nombre distinto.
        var renamed = new Dictionary<string, string>(fewer, StringComparer.Ordinal)
        {
            ["otro.png"] = expected["screenshot_1.png"],
        };
        await Assert.ThrowsAsync<EvidenceZipVerificationException>(() =>
            EvidenceZip.VerifyAsync(zip, renamed, Password, CancellationToken.None));
    }

    [Fact]
    public async Task Verify_EncryptedExpected_ButPlain_Throws()
    {
        var (paths, expected) = MakeEvidence();
        var zip = ZipPath();
        await EvidenceZip.WriteAsync(zip, paths, password: null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<EvidenceZipVerificationException>(() =>
            EvidenceZip.VerifyAsync(zip, expected, Password, CancellationToken.None));
        Assert.Contains("AES-256", ex.Message);
    }

    [Fact]
    public async Task Verify_PlainExpected_ButEncrypted_Throws()
    {
        var (paths, expected) = MakeEvidence();
        var zip = ZipPath();
        await EvidenceZip.WriteAsync(zip, paths, Password, CancellationToken.None);

        await Assert.ThrowsAsync<EvidenceZipVerificationException>(() =>
            EvidenceZip.VerifyAsync(zip, expected, password: null, CancellationToken.None));
    }

    [Fact]
    public async Task RoundTrip_Plain()
    {
        var (paths, expected) = MakeEvidence();
        var zip = ZipPath();

        await EvidenceZip.WriteAsync(zip, paths, password: null, CancellationToken.None);
        await EvidenceZip.VerifyAsync(zip, expected, password: null, CancellationToken.None);

        using var zf = new ZipFile(zip);
        foreach (ZipEntry e in zf)
            Assert.False(e.IsCrypted);
    }

    [Fact]
    public async Task Write_DoesNotOverwriteExistingFile()
    {
        var (paths, _) = MakeEvidence();
        var zip = ZipPath();
        await File.WriteAllTextAsync(zip, "previo");

        await Assert.ThrowsAsync<IOException>(() =>
            EvidenceZip.WriteAsync(zip, paths, Password, CancellationToken.None));
        Assert.Equal("previo", await File.ReadAllTextAsync(zip));
    }

    [Fact]
    public async Task ZipHash_Unchanged_ByVerify()
    {
        var (paths, expected) = MakeEvidence();
        var zip = ZipPath();
        await EvidenceZip.WriteAsync(zip, paths, Password, CancellationToken.None);

        var before = Sha256(zip);
        var mtimeBefore = File.GetLastWriteTimeUtc(zip);
        await EvidenceZip.VerifyAsync(zip, expected, Password, CancellationToken.None);
        Assert.Equal(before, Sha256(zip));
        Assert.Equal(mtimeBefore, File.GetLastWriteTimeUtc(zip));
    }

    [Fact]
    public async Task Bsdtar_OpensWithPassword_FailsWithout()
    {
        const string bsdtar = "/usr/bin/bsdtar";
        if (!File.Exists(bsdtar))
        {
            _output.WriteLine("bsdtar no está en /usr/bin: test omitido (sin aserciones).");
            return;
        }

        var (paths, expected) = MakeEvidence();
        var zip = ZipPath();
        await EvidenceZip.WriteAsync(zip, paths, Password, CancellationToken.None);

        // Contraseña correcta: exit 0 y los hashes coinciden.
        var okDir = Path.Combine(_root, "ok");
        Directory.CreateDirectory(okDir);
        var okExit = await RunBsdtar(bsdtar, Password, zip, okDir);
        Assert.Equal(0, okExit);
        foreach (var (name, hash) in expected)
        {
            var extracted = Path.Combine(okDir, name);
            Assert.True(File.Exists(extracted), $"bsdtar no extrajo {name}");
            Assert.Equal(hash, Sha256(extracted));
        }

        // Contraseña incorrecta: exit != 0 (bsdtar igual puede dejar archivos con basura: no se
        // mira si existen, solo el exit code).
        var koDir = Path.Combine(_root, "ko");
        Directory.CreateDirectory(koDir);
        var koExit = await RunBsdtar(bsdtar, WrongPassword, zip, koDir);
        Assert.NotEqual(0, koExit);
        // Nunca se corre sin --passphrase: sin TTY pide la contraseña en loop.
    }

    private async Task<int> RunBsdtar(string exe, string passphrase, string zip, string dest)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--passphrase");
        psi.ArgumentList.Add(passphrase);
        psi.ArgumentList.Add("-xf");
        psi.ArgumentList.Add(zip);
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(dest);

        using var p = Process.Start(psi)!;
        p.StandardInput.Close(); // stdin cerrado, como </dev/null
        var stderr = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException("bsdtar no terminó en 60 s");
        }
        _output.WriteLine($"bsdtar exit {p.ExitCode}: {(await stderr).Trim()} {(await stdout).Trim()}");
        return p.ExitCode;
    }

    // Extra field 0x9901 (WinZip AES): vendor version (2 bytes LE), "AE", strength (1 byte), método.
    private static (int VendorVersion, int Strength) ReadAesExtra(byte[]? extra)
    {
        Assert.NotNull(extra);
        var i = 0;
        while (i + 4 <= extra!.Length)
        {
            var tag = extra[i] | (extra[i + 1] << 8);
            var size = extra[i + 2] | (extra[i + 3] << 8);
            if (tag == 0x9901)
            {
                Assert.True(size >= 7, "extra field 0x9901 demasiado corto");
                var data = extra.AsSpan(i + 4, size);
                Assert.Equal((byte)'A', data[2]);
                Assert.Equal((byte)'E', data[3]);
                return (data[0] | (data[1] << 8), data[4]);
            }
            i += 4 + size;
        }
        Assert.Fail("No está el extra field 0x9901 (WinZip AES)");
        return default;
    }
}
