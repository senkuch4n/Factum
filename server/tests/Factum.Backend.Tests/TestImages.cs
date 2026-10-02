using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Factum.Backend.Tests;

/// <summary>PNG (y JPEG mínimos) generados en memoria (sin archivos de imagen en el repo).</summary>
internal static class TestImages
{
    /// <summary>PNG RGB de w × h de un solo color.</summary>
    public static byte[] Png(int w, int h, byte r = 0x40, byte g = 0x80, byte b = 0xC0)
    {
        var raw = new byte[h * (1 + w * 3)];
        for (var y = 0; y < h; y++)
        {
            var row = y * (1 + w * 3);
            raw[row] = 0; // filtro None
            for (var x = 0; x < w; x++)
            {
                raw[row + 1 + x * 3] = r;
                raw[row + 2 + x * 3] = g;
                raw[row + 3 + x * 3] = b;
            }
        }

        byte[] idat;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(raw);
            idat = ms.ToArray();
        }

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8;  // bits por canal
        ihdr[9] = 2;  // RGB

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", idat);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>
    /// JPEG mínimo (SOI + APP0 + segmentos APPn de relleno + SOF0 + EOI) que ImageProbe reconoce;
    /// no se puede decodificar (no tiene tablas ni datos) y no hace falta. Cada elemento de
    /// <paramref name="appSegments"/> es el tamaño del payload de un APP1 (máx. 65533 bytes).
    /// </summary>
    public static byte[] Jpeg(int w, int h, params int[] appSegments)
    {
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]);
        // APP0 JFIF (longitud 16).
        ms.Write([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00,
            0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
        foreach (var size in appSegments)
        {
            if (size is < 0 or > 65533) throw new ArgumentOutOfRangeException(nameof(appSegments));
            var len = size + 2;
            ms.Write([0xFF, 0xE1, (byte)(len >> 8), (byte)len]);
            ms.Write(new byte[size]);
        }
        // SOF0: longitud 17, precisión 8, alto, ancho, 3 componentes.
        ms.Write([0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 0x03,
            0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);
        ms.Write([0xFF, 0xD9]);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32(typeBytes.Concat(data).ToArray());
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        s.Write(crcBytes);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }
}
