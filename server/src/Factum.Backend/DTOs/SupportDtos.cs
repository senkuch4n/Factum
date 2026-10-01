namespace Factum.Backend.DTOs;

public sealed record ReportarProblemaRequest(
    string Categoria,
    string Descripcion,
    string? NumeroInterno,
    string? Telefono
);

public sealed record ReportarProblemaResponse(int TokenNumero);

public sealed record MiTokenDto(
    int IdToken,
    string Servicio,
    string Estado,
    string Descripcion,
    string? DescripcionResolucion,
    DateTime FechaCreacion,
    int? Puntuacion,
    string? Comentario
);

public sealed record CalificarTokenRequest(int Puntuacion, string? Comentario);

public sealed record FaroSsoLinkResponse(string Url);
