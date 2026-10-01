using System.ComponentModel.DataAnnotations;
using Factum.Backend.Models;

namespace Factum.Backend.DTOs;

public sealed record CreateCaseRequest(
    [Required] string NroReferencia,
    [Required] string NombreDenunciante,
    [Required] string DniDenunciante,
    string Observaciones,
    DeviceInfoDto? Device
);

public sealed record DeviceInfoDto(
    string Serial,
    string Manufacturer,
    string Model,
    int AndroidVersion,
    string Imei,
    string Platform = "android",
    string OsVersion = ""
);

public sealed record FileInfoDto(
    string Name,
    long Size,
    string Hash,
    DateTime ModifiedAt,
    string? SourcePath = null
);

public sealed record GenerateResponse(
    Case Case,
    string ZipHash,
    string Password,
    FilesDto Files
);

public sealed record FilesDto(string Zip, string Pdf);
