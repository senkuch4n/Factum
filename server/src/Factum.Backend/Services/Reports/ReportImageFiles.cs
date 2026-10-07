using Factum.Backend.Services.Branding;

namespace Factum.Backend.Services.Reports;

public enum ReportImageStatus { Available, NotInsertable, Missing, Empty, NotAnImage }

public sealed record ReportImageInspection(ReportImageStatus Status, string? ContentType, int Width, int Height)
{
    public bool IsAvailable => Status == ReportImageStatus.Available;
}

/// <summary>
/// "Captura disponible" (editor-imagenes-informe §4.2 y §6.3): insertable por el nombre, en el
/// primer nivel de la carpeta del caso, con tamaño &gt; 0 y PNG/JPEG por contenido (magic bytes).
/// Sin tope de tamaño (DP2 B): solo se leen las cabeceras. Los archivos se abren SIEMPRE en solo
/// lectura (<c>FileAccess.Read</c>, <c>FileShare.Read</c>); nunca se escriben ni se les cambia
/// la fecha.
/// </summary>
public static class ReportImageFiles
{
    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, FileOptions.SequentialScan);

    /// <summary>
    /// Valida el nombre, arma la ruta, comprueba que esté en el primer nivel de
    /// <paramref name="caseDir"/>, mira existencia y tamaño y detecta el tipo leyendo solo
    /// cabeceras.
    /// </summary>
    public static ReportImageInspection Inspect(string caseDir, string filename)
    {
        var status = TryOpenCore(caseDir, filename, out var stream, out var contentType, out var w, out var h);
        stream?.Dispose();
        return status == ReportImageStatus.Available
            ? new ReportImageInspection(status, contentType, w, h)
            : new ReportImageInspection(status, null, 0, 0);
    }

    /// <summary>
    /// Igual que <see cref="Inspect"/>, pero devuelve el <see cref="FileStream"/> ABIERTO
    /// (<c>FileShare.Read</c>) sobre el que se hizo la detección, rebobinado a la posición 0. El
    /// que llama lo usa (lo sirve o lo embebe) y lo cierra. Mientras está abierto nadie puede
    /// escribir el archivo: lo validado es exactamente lo que se usa, sin cargarlo en memoria.
    /// </summary>
    public static bool TryOpen(string caseDir, string filename, out FileStream stream, out string contentType,
        out int width, out int height)
    {
        var status = TryOpenCore(caseDir, filename, out var s, out contentType, out width, out height);
        if (status == ReportImageStatus.Available && s is not null)
        {
            stream = s;
            return true;
        }
        s?.Dispose();
        stream = null!;
        contentType = "";
        width = 0;
        height = 0;
        return false;
    }

    private static ReportImageStatus TryOpenCore(string caseDir, string filename, out FileStream? stream,
        out string contentType, out int width, out int height)
    {
        stream = null;
        contentType = "";
        width = 0;
        height = 0;

        if (!ReportImageRef.IsInsertableName(filename)) return ReportImageStatus.NotInsertable;

        string path;
        try
        {
            var root = Path.GetFullPath(caseDir);
            path = Path.GetFullPath(Path.Combine(root, filename));
            // Primer nivel de la carpeta del caso (defensa además del nombre plano).
            if (!string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(root),
                    StringComparison.Ordinal))
                return ReportImageStatus.NotInsertable;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ReportImageStatus.NotInsertable;
        }

        if (!File.Exists(path)) return ReportImageStatus.Missing;

        FileStream? fs = null;
        try
        {
            // Un enlace simbólico podría apuntar fuera de la carpeta del caso: no es una captura
            // del caso (el almacenamiento nunca los crea).
            if (new FileInfo(path).LinkTarget is not null) return ReportImageStatus.NotInsertable;
            fs = OpenRead(path);
            if (fs.Length == 0)
            {
                fs.Dispose();
                return ReportImageStatus.Empty;
            }
            if (!ImageProbe.TryDetect(fs, out contentType, out width, out height) ||
                contentType is not ("image/png" or "image/jpeg"))
            {
                fs.Dispose();
                contentType = "";
                width = 0;
                height = 0;
                return ReportImageStatus.NotAnImage;
            }
            fs.Position = 0;
            stream = fs;
            return ReportImageStatus.Available;
        }
        catch (FileNotFoundException)
        {
            fs?.Dispose();
            return ReportImageStatus.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            fs?.Dispose();
            return ReportImageStatus.Missing;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            fs?.Dispose();
            contentType = "";
            width = 0;
            height = 0;
            return ReportImageStatus.NotAnImage;
        }
    }
}
