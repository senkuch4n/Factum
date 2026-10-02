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

    /// <summary>
    /// Mismas reglas que <see cref="TryDetect(byte[], out string, out int, out int)"/>, leyendo
    /// solo las cabeceras de un stream con <c>CanSeek</c> (editor-imagenes-informe §6.3): PNG =
    /// firma + IHDR (24 bytes); JPEG = marcador y longitud de cada segmento (se saltan con
    /// <c>Seek</c>) hasta el SOF. Memoria constante para cualquier tamaño de archivo. Lee desde
    /// la posición 0 y deja el stream en una posición indeterminada (el que llama rebobina).
    /// </summary>
    public static bool TryDetect(Stream stream, out string contentType, out int w, out int h)
    {
        contentType = "";
        w = 0;
        h = 0;
        if (stream is null || !stream.CanSeek || !stream.CanRead) return false;

        var length = stream.Length;
        stream.Position = 0;
        Span<byte> head = stackalloc byte[24];

        if (length >= 24)
        {
            ReadFully(stream, head);
            if (head[..8].SequenceEqual(PngSignature))
            {
                var len = BinaryPrimitives.ReadUInt32BigEndian(head.Slice(8, 4));
                if (len != 13 || head[12] != 'I' || head[13] != 'H' || head[14] != 'D' || head[15] != 'R')
                    return false;
                var pw = BinaryPrimitives.ReadUInt32BigEndian(head.Slice(16, 4));
                var ph = BinaryPrimitives.ReadUInt32BigEndian(head.Slice(20, 4));
                if (pw == 0 || ph == 0 || pw > int.MaxValue || ph > int.MaxValue) return false;
                contentType = "image/png";
                w = (int)pw;
                h = (int)ph;
                return true;
            }
        }

        if (length < 4) return false;
        stream.Position = 0;
        Span<byte> soi = stackalloc byte[3];
        ReadFully(stream, soi);
        if (soi[0] != 0xFF || soi[1] != 0xD8 || soi[2] != 0xFF) return false;

        // Mismo recorrido que la versión de byte[]: pos es el offset del 0xFF del marcador.
        long pos = 2;
        Span<byte> seg = stackalloc byte[4];
        Span<byte> sof = stackalloc byte[5];
        while (pos + 4 <= length)
        {
            stream.Position = pos;
            ReadFully(stream, seg);
            if (seg[0] != 0xFF) return false;
            var marker = seg[1];
            if (marker == 0xFF) { pos++; continue; }
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7) { pos += 2; continue; }
            if (marker is 0xD9 or 0xDA) return false;

            var segLen = (seg[2] << 8) | seg[3];
            if (segLen < 2 || pos + 2 + segLen > length) return false;

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (segLen < 7) return false;
                // Después de la longitud: precisión (1), alto (2), ancho (2).
                ReadFully(stream, sof);
                var jh = (sof[1] << 8) | sof[2];
                var jw = (sof[3] << 8) | sof[4];
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

    private static void ReadFully(Stream stream, Span<byte> buffer)
    {
        // ReadExactly lanza EndOfStreamException si el archivo se acorta mientras se lee: el que
        // llama lo trata como "no es imagen".
        stream.ReadExactly(buffer);
    }
}
