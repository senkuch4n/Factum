namespace Factum.Backend.DTOs;

/// <summary>PUT /api/profile. Los cuatro primeros son obligatorios (validados en el servicio).</summary>
public sealed record ExpertProfileRequest(
    string? Nombre = null,
    string? Matricula = null,
    string? Profesion = null,
    string? Caracter = null,
    // "suscripto" | "suscripta"; null → "suscripto".
    string? Tratamiento = null
);

/// <summary>GET/PUT /api/profile. <c>exists=false</c> = valores sugeridos, todavía sin guardar.</summary>
public sealed record ExpertProfileResponse(
    string Dni,
    string Nombre,
    string Matricula,
    string Profesion,
    string Caracter,
    string Tratamiento,
    bool Exists,
    bool IsComplete,
    DateTime? UpdatedAt
);
