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
            var (vendorVersion, strength, _) = ReadAesExtra(e.ExtraData);
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

    // ── zip-evidencia-sin-recomprimir: Stored para lo ya comprimido, Deflate para el resto ──

    // Tamaño extra de una entrada AES-256 AE-2: 16 de sal + 2 del verificador + 10 del HMAC.
    private const int Aes256Overhead = 28;

    [Theory]
    [InlineData("a.mp4")] [InlineData("a.mkv")] [InlineData("a.mov")] [InlineData("a.m4v")]
    [InlineData("a.webm")] [InlineData("a.3gp")] [InlineData("a.avi")]
    [InlineData("a.mp3")] [InlineData("a.m4a")] [InlineData("a.aac")] [InlineData("a.ogg")]
    [InlineData("a.opus")] [InlineData("a.amr")]
    [InlineData("a.jpg")] [InlineData("a.jpeg")] [InlineData("a.png")] [InlineData("a.heic")]
    [InlineData("a.heif")] [InlineData("a.webp")] [InlineData("a.gif")]
    [InlineData("a.zip")] [InlineData("a.7z")] [InlineData("a.rar")] [InlineData("a.gz")]
    [InlineData("a.apk")] [InlineData("a.ipa")]
    [InlineData("VIDEO.MP4")] [InlineData("IMG_0001.JPG")] [InlineData("foto.HEIC")]
    [InlineData("backup.tar.gz")]
    public void MethodFor_CompressedFormats_Stored(string name)
    {
        Assert.Equal(CompressionMethod.Stored, EvidenceZip.MethodFor(name));
        // Con ruta completa decide igual (WriteAsync le pasa la ruta del archivo).
        Assert.Equal(CompressionMethod.Stored, EvidenceZip.MethodFor(Path.Combine("dir", "sub", name)));
    }

    [Theory]
    [InlineData("notas.txt")] [InlineData("datos.xml")] [InlineData("mmssms.db")]
    [InlineData("x.sqlite")] [InlineData("a.json")] [InlineData("volcado.bin")]
    [InlineData("extraccion.tar")] [InlineData("sin_extension")] [InlineData("punto.")]
    public void MethodFor_OtherFormats_Deflated(string name)
    {
        Assert.Equal(CompressionMethod.Deflated, EvidenceZip.MethodFor(name));
    }

    [Fact]
    public void StoredExtensions_HasTheD2List()
    {
        Assert.Equal(26, EvidenceZip.StoredExtensions.Count);
    }

    [Fact]
    public async Task RoundTrip_Encrypted_MixedMethods()
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
            var (vendorVersion, strength, method) = ReadAesExtra(e.ExtraData);
            Assert.Equal(2, vendorVersion);
            Assert.Equal(3, strength);
            if (e.Name.EndsWith(".png", StringComparison.Ordinal) || e.Name.EndsWith(".mp4", StringComparison.Ordinal))
            {
                Assert.Equal(CompressionMethod.Stored, e.CompressionMethod);
                Assert.Equal(0, method);
                Assert.Equal(e.Size + Aes256Overhead, e.CompressedSize);
            }
            else if (e.Size == 0)
            {
                // vacio.txt: SharpZipLib guarda las entradas vacías con Stored sea cual sea el método
                // pedido (ya pasaba antes de esta HU); no hay nada que comprimir.
                Assert.Equal("vacio.txt", e.Name);
                Assert.Equal(CompressionMethod.Stored, e.CompressionMethod);
            }
            else
            {
                Assert.EndsWith(".txt", e.Name);
                Assert.Equal(CompressionMethod.Deflated, e.CompressionMethod);
                Assert.Equal(8, method);
            }
        }
    }

    [Fact]
    public async Task RoundTrip_Plain_MixedMethods()
    {
        var (paths, expected) = MakeEvidence();
        var zip = ZipPath();

        await EvidenceZip.WriteAsync(zip, paths, password: null, CancellationToken.None);
        await EvidenceZip.VerifyAsync(zip, expected, password: null, CancellationToken.None);

        using var zf = new ZipFile(zip);
        foreach (ZipEntry e in zf)
        {
            Assert.False(e.IsCrypted);
            if (e.Name.EndsWith(".png", StringComparison.Ordinal) || e.Name.EndsWith(".mp4", StringComparison.Ordinal))
            {
                Assert.Equal(CompressionMethod.Stored, e.CompressionMethod);
                Assert.Equal(e.Size, e.CompressedSize);
            }
        }
        var unicode = zf.GetEntry(UnicodeName);
        Assert.NotNull(unicode);
        Assert.Equal(CompressionMethod.Deflated, unicode.CompressionMethod);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deflate_StillCompresses(bool encrypted)
    {
        var txt = Path.Combine(_src, "registro.txt");
        var sb = new StringBuilder();
        while (sb.Length < 256 * 1024)
            sb.Append("2026-10-12 12:00:00 evento de prueba repetido para comprimir; línea con tildes áéí\n");
        await File.WriteAllTextAsync(txt, sb.ToString());
        var expected = new Dictionary<string, string>(StringComparer.Ordinal) { ["registro.txt"] = Sha256(txt) };
        var password = encrypted ? Password : null;
        var zip = ZipPath();

        await EvidenceZip.WriteAsync(zip, [txt], password, CancellationToken.None);
        await EvidenceZip.VerifyAsync(zip, expected, password, CancellationToken.None);

        using var zf = new ZipFile(zip);
        var e = zf.GetEntry("registro.txt");
        Assert.NotNull(e);
        Assert.Equal(CompressionMethod.Deflated, e.CompressionMethod);
        Assert.True(e.CompressedSize < e.Size / 4, $"CompressedSize {e.CompressedSize} de {e.Size}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Verify_CorruptStoredEntry_Throws(bool encrypted)
    {
        var mp4 = Path.Combine(_src, "evidencia.mp4");
        await File.WriteAllBytesAsync(mp4, RandomNumberGenerator.GetBytes(256 * 1024));
        var expected = new Dictionary<string, string>(StringComparer.Ordinal) { ["evidencia.mp4"] = Sha256(mp4) };
        var password = encrypted ? Password : null;
        var original = ZipPath("original.zip");
        await EvidenceZip.WriteAsync(original, [mp4], password, CancellationToken.None);
        await EvidenceZip.VerifyAsync(original, expected, password, CancellationToken.None);

        // Copia dentro de _root con un byte invertido en la mitad (cae en los datos de la entrada).
        var corrupt = ZipPath("corrupto.zip");
        File.Copy(original, corrupt);
        await using (var fs = new FileStream(corrupt, FileMode.Open, FileAccess.ReadWrite))
        {
            var pos = fs.Length / 2;
            fs.Position = pos;
            var b = fs.ReadByte();
            fs.Position = pos;
            fs.WriteByte((byte)~b);
        }

        var ex = await Assert.ThrowsAsync<EvidenceZipVerificationException>(() =>
            EvidenceZip.VerifyAsync(corrupt, expected, password, CancellationToken.None));
        Assert.DoesNotContain(Password, ex.Message);
        _output.WriteLine(ex.Message);
    }

    [Fact]
    public async Task SevenZip_TestsMixedZip()
    {
        var sevenZip = new[] { "/opt/homebrew/bin/7zz", "/usr/local/bin/7zz", "/usr/bin/7zz" }
            .FirstOrDefault(File.Exists);
        if (sevenZip is null)
        {
            _output.WriteLine("7zz no está instalado: test omitido (sin aserciones).");
            return;
        }

        var (paths, _) = MakeEvidence();
        var zip = ZipPath();
        await EvidenceZip.WriteAsync(zip, paths, Password, CancellationToken.None);

        var (okExit, okOut) = await RunSevenZipTest(sevenZip, Password, zip);
        Assert.Equal(0, okExit);
        Assert.Contains("Everything is Ok", okOut);

        var (koExit, _) = await RunSevenZipTest(sevenZip, WrongPassword, zip);
        Assert.NotEqual(0, koExit);
    }

    private async Task<(int Exit, string Output)> RunSevenZipTest(string exe, string password, string zip)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("t");
        psi.ArgumentList.Add("-p" + password);
        psi.ArgumentList.Add(zip);

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
            throw new TimeoutException("7zz no terminó en 60 s");
        }
        var output = (await stdout) + (await stderr);
        _output.WriteLine($"7zz exit {p.ExitCode}: {output.Trim()}");
        return (p.ExitCode, output);
    }

    // Extra field 0x9901 (WinZip AES): vendor version (2 bytes LE), "AE", strength (1 byte), método
    // real (2 bytes LE: 0 = Stored, 8 = Deflate).
    private static (int VendorVersion, int Strength, int Method) ReadAesExtra(byte[]? extra)
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
                return (data[0] | (data[1] << 8), data[4], data[5] | (data[6] << 8));
            }
            i += 4 + size;
        }
        Assert.Fail("No está el extra field 0x9901 (WinZip AES)");
        return default;
    }
}
