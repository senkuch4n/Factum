using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

public sealed class Case
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string NroReferencia { get; set; } = string.Empty;
    public string NombreDenunciante { get; set; } = string.Empty;
    public string DniDenunciante { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;

    public User Officer { get; set; } = new();
    public DeviceInfo Device { get; set; } = new();

    public CaseStatus Status { get; set; } = CaseStatus.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? GeneratedAt { get; set; }

    public string? ZipPassword { get; set; }
    public string? ZipHash { get; set; }
    public string? ZipFilename { get; set; }
    public string? PdfFilename { get; set; }

    // Ruta de origen en el dispositivo para archivos traídos con el explorador de archivos
    // (no todos los archivos tienen una — screenshots/grabaciones/fotos de webcam no aplican).
    public List<FileSource> FileSources { get; set; } = [];
}

public sealed class FileSource
{
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
}

public enum CaseStatus { Draft, Generating, Completed, Error }
