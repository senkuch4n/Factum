using System.Buffers.Binary;

namespace Factum.Backend.Services.Branding;

/// <summary>
/// Detección estricta de formato y dimensiones de una imagen PNG o JPEG a partir de sus
/// bytes (magic bytes + IHDR / SOF). A diferencia de <c>ReportService.GetImageSize</c>, no
/// cae a un tamaño por defecto: si algo no cierra, devuelve <c>false</c>.
/// </summary>
public static class ImageProbe
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool TryDetect(byte[] data, out string contentType, out int w, out int h)
    {
        contentType = "";
        w = 0;
        h = 0;
        if (data is null) return false;

        if (data.Length >= 24 && data.AsSpan(0, 8).SequenceEqual(PngSignature))
        {
            // Primer chunk: longitud (4) = 13, tipo (4) = "IHDR", ancho (4), alto (4).
            var len = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4));
            if (len != 13 || data[12] != 'I' || data[13] != 'H' || data[14] != 'D' || data[15] != 'R')
                return false;
            var pw = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4));
            var ph = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4));
            if (pw == 0 || ph == 0 || pw > int.MaxValue || ph > int.MaxValue) return false;
            contentType = "image/png";
            w = (int)pw;
            h = (int)ph;
            return true;
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            var pos = 2;
            while (pos + 4 <= data.Length)
            {
                if (data[pos] != 0xFF) return false;
                var marker = data[pos + 1];
                // Relleno de 0xFF entre segmentos.
                if (marker == 0xFF) { pos++; continue; }
                // Marcadores sin longitud (TEM, RSTn). SOS/EOI antes de un SOF = no hay SOF.
                if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7) { pos += 2; continue; }
                if (marker is 0xD9 or 0xDA) return false;

                var segLen = (data[pos + 2] << 8) | data[pos + 3];
                if (segLen < 2 || pos + 2 + segLen > data.Length) return false;

                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    // SOF: longitud (2), precisión (1), alto (2), ancho (2).
                    if (segLen < 7) return false;
                    var jh = (data[pos + 5] << 8) | data[pos + 6];
                    var jw = (data[pos + 7] << 8) | data[pos + 8];
                    if (jw == 0 || jh == 0) return false;
                    contentType = "image/jpeg";
                    w = jw;
                    h = jh;
                    return true;
                }

                pos += 2 + segLen;
            }
            return false;
        }

        return false;
    }
}
