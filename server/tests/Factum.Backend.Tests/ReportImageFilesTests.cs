using System.Security.Cryptography;
using System.Text;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Reports;

namespace Factum.Backend.Tests;

/// <summary>
/// "Captura disponible" (editor-imagenes-informe §4.2, §6.3, T10-T11). Carpeta temporal propia,
/// archivos ficticios generados en memoria; se comprueba que ningún archivo cambie.
/// </summary>
public sealed class ReportImageFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "factum-imgfiles-" + Guid.NewGuid().ToString("N"));

    public ReportImageFilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string Write(string name, byte[] data)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, data);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc));
        return path;
    }

    private Dictionary<string, (string Sha, DateTime Mtime, long Length)> Snapshot() =>
        Directory.EnumerateFiles(_dir).ToDictionary(p => Path.GetFileName(p), p =>
        {
            using var fs = File.OpenRead(p);
            return (Convert.ToHexStringLower(SHA256.HashData(fs)), File.GetLastWriteTimeUtc(p), new FileInfo(p).Length);
        });

    // ── T10 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void T10_Inspect_YTryOpen()
    {
        Write("screenshot_ok.png", TestImages.Png(30, 60));
        Write("screenshot_jpeg_disfrazado.png", TestImages.Jpeg(1080, 2400));
        Write("captura_real.jpg", TestImages.Jpeg(640, 480));
        Write("screenshot_vacio.png", []);
        Write("screenshot_texto.png", Encoding.UTF8.GetBytes("esto no es una imagen, es texto"));
        Write("screenshot_gif.png", [.. "GIF89a"u8.ToArray(), 1, 0, 1, 0, 0, 0, 0]);
        Write("screenshot_svg.png", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"></svg>"));
        Write("screenshot_webp.png", [.. "RIFF"u8.ToArray(), 0x1A, 0, 0, 0, .. "WEBPVP8 "u8.ToArray(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Write("foto_funcionario_1.jpg", TestImages.Jpeg(100, 100));
        Write("screenshot_x.mp4", TestImages.Png(10, 10));
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllBytes(Path.Combine(_dir, "sub", "screenshot_sub.png"), TestImages.Png(10, 10));
        var before = Snapshot();

        var ok = ReportImageFiles.Inspect(_dir, "screenshot_ok.png");
        Assert.Equal(new ReportImageInspection(ReportImageStatus.Available, "image/png", 30, 60), ok);
        Assert.True(ok.IsAvailable);

        Assert.Equal(new ReportImageInspection(ReportImageStatus.Available, "image/jpeg", 1080, 2400),
            ReportImageFiles.Inspect(_dir, "screenshot_jpeg_disfrazado.png"));
        Assert.Equal(new ReportImageInspection(ReportImageStatus.Available, "image/jpeg", 640, 480),
            ReportImageFiles.Inspect(_dir, "captura_real.jpg"));

        Assert.Equal(ReportImageStatus.Missing, ReportImageFiles.Inspect(_dir, "screenshot_no_existe.png").Status);
        Assert.Equal(ReportImageStatus.Empty, ReportImageFiles.Inspect(_dir, "screenshot_vacio.png").Status);
        foreach (var name in new[] { "screenshot_texto.png", "screenshot_gif.png", "screenshot_svg.png", "screenshot_webp.png" })
        {
            var r = ReportImageFiles.Inspect(_dir, name);
            Assert.Equal(ReportImageStatus.NotAnImage, r.Status);
            Assert.Null(r.ContentType);
            Assert.False(ReportImageFiles.TryOpen(_dir, name, out _, out _, out _, out _));
        }
        Assert.Equal(ReportImageStatus.NotInsertable, ReportImageFiles.Inspect(_dir, "foto_funcionario_1.jpg").Status);
        Assert.Equal(ReportImageStatus.NotInsertable, ReportImageFiles.Inspect(_dir, "screenshot_x.mp4").Status);
        Assert.Equal(ReportImageStatus.NotInsertable, ReportImageFiles.Inspect(_dir, "sub/screenshot_sub.png").Status);
        Assert.Equal(ReportImageStatus.NotInsertable, ReportImageFiles.Inspect(_dir, "../screenshot_ok.png").Status);
        Assert.Equal(ReportImageStatus.NotInsertable, ReportImageFiles.Inspect(_dir, "..").Status);

        Assert.True(ReportImageFiles.TryOpen(_dir, "screenshot_jpeg_disfrazado.png", out var stream, out var type,
            out var w, out var h));
        using (stream)
        {
            Assert.Equal(("image/jpeg", 1080, 2400), (type, w, h));
            Assert.Equal(0, stream.Position);
            Assert.False(stream.CanWrite);
            Assert.Equal(before["screenshot_jpeg_disfrazado.png"].Sha, Convert.ToHexStringLower(SHA256.HashData(stream)));
        }

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void T10_PngDe600MB_DisponibleSinCargarloEnMemoria()
    {
        var path = Path.Combine(_dir, "screenshot_enorme.png");
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            // Firma + IHDR de un PNG de 1×1 con el tamaño cambiado a 4000×9000 (el CRC no se mira).
            var head = TestImages.Png(1, 1)[..33];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(16), 4000);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(20), 9000);
            fs.Write(head);
            fs.SetLength(600L * 1024 * 1024);           // relleno disperso
        }
        var before = Snapshot();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memBefore = GC.GetTotalMemory(forceFullCollection: true);
        var allocBefore = GC.GetAllocatedBytesForCurrentThread();

        var r = ReportImageFiles.Inspect(_dir, "screenshot_enorme.png");
        Assert.True(ReportImageFiles.TryOpen(_dir, "screenshot_enorme.png", out var s, out _, out _, out _));
        s.Dispose();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        var memAfter = GC.GetTotalMemory(forceFullCollection: true);
        Assert.Equal(new ReportImageInspection(ReportImageStatus.Available, "image/png", 4000, 9000), r);
        // Lo que asignó ESTE hilo (Inspect/TryOpen son sincrónicos) es la medida estable: el
        // GC.GetTotalMemory es de todo el proceso y los otros tests corren en paralelo, así que
        // solo se usa como cota amplia.
        Assert.True(allocated < 4 * 1024 * 1024, $"Asignó {allocated} bytes");
        Assert.True(memAfter - memBefore < 64 * 1024 * 1024, $"Creció {memAfter - memBefore} bytes");

        Assert.Equal(before, Snapshot());
    }

    // ── T11 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void T11_TryDetectStream_JpegConAppnGrandesAntesDelSof()
    {
        // 300 KB de APP1 (un segmento JPEG tiene como máximo 64 KB, así que van en 5 de 60 KB)
        // y otro de 60 KB, antes del SOF.
        var data = TestImages.Jpeg(1080, 2400, 61_440, 61_440, 61_440, 61_440, 61_440, 61_440);
        Assert.True(data.Length > 360_000);
        using var ms = new MemoryStream(data, writable: false);
        Assert.True(ImageProbe.TryDetect(ms, out var type, out var w, out var h));
        Assert.Equal(("image/jpeg", 1080, 2400), (type, w, h));
        Assert.True(ImageProbe.TryDetect(data, out var type2, out var w2, out var h2));
        Assert.Equal((type, w, h), (type2, w2, h2));
    }

    public static TheoryData<string, byte[]> ProbeCases()
    {
        var png = TestImages.Png(7, 3);
        var jpeg = TestImages.Jpeg(320, 200, 100);
        var badIhdr = (byte[])png.Clone();
        badIhdr[12] = (byte)'X';
        var zeroW = (byte[])png.Clone();
        zeroW[16] = zeroW[17] = zeroW[18] = zeroW[19] = 0;
        var jpegSosFirst = new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x04, 0, 0 };
        var jpegTruncated = jpeg[..30];
        var jpegLongSeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, 0xFF, 0xFF, 0, 0, 0, 0 };
        var jpegPadding = new byte[] { 0xFF, 0xD8, 0xFF, 0xFF, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x10, 0x00, 0x20, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1 };
        return new TheoryData<string, byte[]>
        {
            { "png", png }, { "jpeg", jpeg }, { "png corto", png[..23] }, { "ihdr malo", badIhdr },
            { "ancho 0", zeroW }, { "sos antes del sof", jpegSosFirst }, { "jpeg truncado", jpegTruncated },
            { "segmento más largo que el archivo", jpegLongSeg }, { "relleno 0xFF", jpegPadding },
            { "vacío", [] }, { "tres bytes", [0xFF, 0xD8, 0xFF] }, { "gif", "GIF89a\u0001\0\u0001\0"u8.ToArray() },
            { "texto", "hola"u8.ToArray() },
        };
    }

    [Theory]
    [MemberData(nameof(ProbeCases))]
    public void T11_TryDetectStream_IgualQueBytes(string label, byte[] data)
    {
        var fromBytes = ImageProbe.TryDetect(data, out var t1, out var w1, out var h1);
        using var ms = new MemoryStream(data, writable: false);
        var fromStream = ImageProbe.TryDetect(ms, out var t2, out var w2, out var h2);
        Assert.True((fromBytes, t1, w1, h1) == (fromStream, t2, w2, h2), label);
    }

    [Fact]
    public void T11_TryOpen_StreamEnCero_YBloqueaLaEscritura()
    {
        var path = Write("screenshot_lock.png", TestImages.Png(12, 8));
        var before = Snapshot();
        Assert.True(ReportImageFiles.TryOpen(_dir, "screenshot_lock.png", out var stream, out var type, out _, out _));
        using (stream)
        {
            Assert.Equal("image/png", type);
            Assert.Equal(0, stream.Position);
            Assert.ThrowsAny<IOException>(() => File.OpenWrite(path).Dispose());
        }
        Assert.Equal(before, Snapshot());
    }
}
