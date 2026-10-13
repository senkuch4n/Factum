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

        var realHash = expected["screenshot_2.png"];
        expected["screenshot_2.png"] = new string('0', 64);
        var ex = await Assert.ThrowsAsync<EvidenceZipVerificationException>(() =>
            EvidenceZip.VerifyAsync(zip, expected, Password, CancellationToken.None));
        Assert.Contains("screenshot_2.png", ex.Message);

        // zip-verificacion-stored D-T3: datos de la entrada para el log, sin la contraseña.
        Assert.NotNull(ex.Entry);
        Assert.Equal("screenshot_2.png", ex.Entry.Name);
        Assert.Equal(10 * 1024, ex.Entry.Size);
        Assert.Equal("Stored", ex.Entry.Method);
        Assert.True(ex.Entry.Encrypted);
        Assert.Equal(new string('0', 64), ex.Entry.ExpectedSha256);
        Assert.Equal(realHash, ex.Entry.ActualSha256);
        Assert.DoesNotContain(Password, ex.Message);
        Assert.DoesNotContain(Password, ex.Entry.ToString());
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
        var sevenZip = SevenZipPath();
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

    private Task<(int Exit, string Output)> RunSevenZipTest(string exe, string password, string zip) =>
        RunSevenZip(exe, "t", "-p" + password, zip);

    // ── zip-verificacion-stored: barrido de tamaños (QA 2026-10-13 C1) ──────────

    // Bordes de los bloques AES (16 B), de los buffers de 4 KiB y 80 KiB y los tamaños del QA (T1).
    public static readonly int[] SweepSizes =
    [
        0, 1, 2, 15, 16, 17, 31, 32, 33, 4079, 4080, 4081, 4094, 4095, 4096, 4097, 4098, 4111, 4112, 4113,
        8191, 8192, 8193, 16383, 16384, 16385, 44208, 44210, 44212, 44214, 44216, 44218, 44660, 65535, 65536,
        65537, 81919, 81920, 81921, 163840, 163841,
    ];

    // Secuencia exacta del diagnóstico (SDD §3): new Random(1) en este orden. Con la lectura async de
    // SharpZipLib 1.4.2, 4095 y 44216 dan "Auth code missing" y 44214 da otro hash.
    private static readonly int[] QaSequence = [4095, 4096, 44214, 44216, 44660];

    private (List<string> Paths, Dictionary<string, string> Expected) MakeQaSequence(string dir)
    {
        Directory.CreateDirectory(dir);
        var rnd = new Random(1);
        var paths = new List<string>();
        foreach (var size in QaSequence)
        {
            var data = new byte[size];
            rnd.NextBytes(data);
            var p = Path.Combine(dir, $"captura_{size}.png");
            File.WriteAllBytes(p, data);
            paths.Add(p);
        }
        var txt = Path.Combine(dir, "registro.txt");
        File.WriteAllText(txt, string.Concat(Enumerable.Repeat("línea de registro con tildes áéí\n", 500)));
        paths.Add(txt);
        return (paths, paths.ToDictionary(p => Path.GetFileName(p), Sha256, StringComparer.Ordinal));
    }

    private string MakeSeeded(string dir, int size, string ext)
    {
        Directory.CreateDirectory(dir);
        var data = new byte[size];
        new Random(size).NextBytes(data);
        var p = Path.Combine(dir, $"f{size}{ext}");
        File.WriteAllBytes(p, data);
        return p;
    }

    [Theory]
    [InlineData(".png", true)]
    [InlineData(".png", false)]
    [InlineData(".txt", true)]
    [InlineData(".txt", false)]
    public async Task Sweep_RoundTrip_AllSizes(string ext, bool encrypted)
    {
        var password = encrypted ? Password : null;
        var expectedMethod = ext == ".png" ? CompressionMethod.Stored : CompressionMethod.Deflated;
        var dir = Path.Combine(_root, "sweep");
        var failures = new List<string>();
        foreach (var size in SweepSizes)
        {
            var file = MakeSeeded(dir, size, ext);
            var name = Path.GetFileName(file);
            var expected = new Dictionary<string, string>(StringComparer.Ordinal) { [name] = Sha256(file) };
            var zip = ZipPath($"sweep_{size}.zip");
            try
            {
                await EvidenceZip.WriteAsync(zip, [file], password, CancellationToken.None);
                await EvidenceZip.VerifyAsync(zip, expected, password, CancellationToken.None);
                using var zf = new ZipFile(zip);
                var e = zf.GetEntry(name);
                // Las entradas vacías SharpZipLib las guarda siempre con Stored (ver RoundTrip_Encrypted_MixedMethods).
                var method = size == 0 ? CompressionMethod.Stored : expectedMethod;
                if (e is null || e.CompressionMethod != method)
                    failures.Add($"{size}: método {e?.CompressionMethod} y se esperaba {method}");
            }
            catch (EvidenceZipVerificationException ex)
            {
                failures.Add($"{size}: {ex.Message} ({ex.InnerException?.Message})");
            }
        }
        Assert.True(failures.Count == 0,
            $"{ext} {(encrypted ? "cifrado" : "sin cifrar")}, tamaños que fallan:\n{string.Join("\n", failures)}");
    }

    [Fact]
    public async Task Sweep_QaSequence_Encrypted_Stored()
    {
        var (paths, expected) = MakeQaSequence(Path.Combine(_root, "qa"));
        var zip = ZipPath();

        await EvidenceZip.WriteAsync(zip, paths, Password, CancellationToken.None);
        await EvidenceZip.VerifyAsync(zip, expected, Password, CancellationToken.None);
    }

    [Fact]
    public async Task Sweep_DetectsCorruption_Stored_Encrypted()
    {
        var file = MakeSeeded(Path.Combine(_root, "corrupt"), 44214, ".png");
        var name = Path.GetFileName(file);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal) { [name] = Sha256(file) };
        var zip = ZipPath();
        await EvidenceZip.WriteAsync(zip, [file], Password, CancellationToken.None);
        await EvidenceZip.VerifyAsync(zip, expected, Password, CancellationToken.None);

        // Offset de los datos de la entrada: cabecera local (30 B + nombre + extra), después 16 B de sal y
        // 2 del verificador de AES. Se invierte un byte en la mitad del contenido cifrado.
        long dataStart;
        using (var zf = new ZipFile(zip))
        {
            var e = zf.GetEntry(name);
            Assert.NotNull(e);
            Assert.Equal(CompressionMethod.Stored, e.CompressionMethod);
            await using var raw = File.OpenRead(zip);
            var header = new byte[30];
            raw.Position = e.Offset;
            raw.ReadExactly(header);
            var nameLen = header[26] | (header[27] << 8);
            var extraLen = header[28] | (header[29] << 8);
            dataStart = e.Offset + 30 + nameLen + extraLen + 16 + 2;
        }
        await using (var fs = new FileStream(zip, FileMode.Open, FileAccess.ReadWrite))
        {
            var pos = dataStart + 44214 / 2;
            fs.Position = pos;
            var b = fs.ReadByte();
            fs.Position = pos;
            fs.WriteByte((byte)~b);
        }

        var ex = await Assert.ThrowsAsync<EvidenceZipVerificationException>(() =>
            EvidenceZip.VerifyAsync(zip, expected, Password, CancellationToken.None));
        Assert.True(ex.Message.Contains("no coincide con su hash") || ex.Message.Contains("no se puede leer"),
            ex.Message);
        Assert.DoesNotContain(Password, ex.Message);
        _output.WriteLine(ex.Message);
    }

    [Fact]
    public async Task SevenZip_ExtractsSweepSizes()
    {
        var sevenZip = SevenZipPath();
        if (sevenZip is null)
        {
            _output.WriteLine("7zz no está instalado: test omitido (sin aserciones).");
            return;
        }

        var dir = Path.Combine(_root, "7z");
        var paths = new[] { 4095, 44214, 44216, 44660 }.Select(s => MakeSeeded(dir, s, ".png")).ToList();
        paths.Add(MakeSeeded(dir, 30000, ".txt"));
        var expected = paths.ToDictionary(p => Path.GetFileName(p), Sha256, StringComparer.Ordinal);
        var zip = ZipPath();
        await EvidenceZip.WriteAsync(zip, paths, Password, CancellationToken.None);
        await EvidenceZip.VerifyAsync(zip, expected, Password, CancellationToken.None);

        var (testExit, testOut) = await RunSevenZip(sevenZip, "t", "-p" + Password, zip);
        Assert.Equal(0, testExit);
        Assert.Contains("Everything is Ok", testOut);

        var outDir = Path.Combine(_root, "x");
        var (xExit, _) = await RunSevenZip(sevenZip, "x", "-p" + Password, "-o" + outDir, zip);
        Assert.Equal(0, xExit);
        foreach (var (name, hash) in expected)
            Assert.Equal(hash, Sha256(Path.Combine(outDir, name)));
    }

    // Canario de la librería (D-T8): mientras SharpZipLib no redefina ZipAESStream.ReadAsync(Memory<byte>, …),
    // leer una entrada AES Stored con SHA256.HashDataAsync da otro hash o "Auth code missing" en algún tamaño.
    // Es el motivo de que VerifyAsync lea con Read(byte[], int, int).
    private const string UpstreamIssue =
        "ZipAESStream.ReadAsync(Memory<byte>) de SharpZipLib (no reportado upstream; detalle en progress/impl_backend_zip-verificacion-stored.md)";

    [Fact]
    public async Task SharpZipLib_AesReadAsyncMemory_StillBroken_SeeUpstreamIssue()
    {
        var (paths, expected) = MakeQaSequence(Path.Combine(_root, "canario"));
        var zip = ZipPath();
        await EvidenceZip.WriteAsync(zip, paths, Password, CancellationToken.None);

        var wrong = new List<string>();
        using var zf = new ZipFile(zip) { Password = Password };
        foreach (ZipEntry e in zf)
        {
            try
            {
                await using var s = zf.GetInputStream(e);
                var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(s));
                if (hash != expected[e.Name]) wrong.Add($"{e.Name}: hash distinto");
            }
            catch (ZipException ex)
            {
                wrong.Add($"{e.Name}: {ex.Message}");
            }
        }
        _output.WriteLine(string.Join("\n", wrong));
        Assert.True(wrong.Count > 0,
            $"SharpZipLib ya lee bien con ReadAsync(Memory): ver issue {UpstreamIssue}; se puede borrar este " +
            "test y evaluar volver a async en VerifyAsync");
    }

    private static string? SevenZipPath() =>
        new[] { "/opt/homebrew/bin/7zz", "/usr/local/bin/7zz", "/usr/bin/7zz" }.FirstOrDefault(File.Exists);

    private async Task<(int Exit, string Output)> RunSevenZip(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

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
        _output.WriteLine($"7zz {args[0]} exit {p.ExitCode}: {output.Trim()}");
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
