using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;

namespace Factum.Backend.Services.Profile;

public interface IExpertProfileService
{
    /// <summary>El perfil del usuario o, si no existe, los valores sugeridos (sin persistir).</summary>
    Task<ExpertProfileResponse> GetAsync(User user, CancellationToken ct = default);
    Task<Result<ExpertProfileResponse>> SaveAsync(User user, ExpertProfileRequest request,
        CancellationToken ct = default);
    /// <summary>El perfil persistido del usuario, o null.</summary>
    Task<ExpertProfile?> FindAsync(string dni, CancellationToken ct = default);
}

public sealed class ExpertProfileService(IExpertProfileRepository repo) : IExpertProfileService
{
    public Task<ExpertProfile?> FindAsync(string dni, CancellationToken ct = default) =>
        repo.GetAsync(dni, ct);

    public async Task<ExpertProfileResponse> GetAsync(User user, CancellationToken ct = default)
    {
        var profile = await repo.GetAsync(user.Dni, ct);
        if (profile is not null) return ToResponse(profile, exists: true);

        var suggested = new ExpertProfile
        {
            Id = user.Dni,
            Nombre = user.Name.Trim(),
            Caracter = ExpertProfile.CaracterSugerido,
            Tratamiento = ExpertProfile.TratamientoSuscripto,
        };
        return ToResponse(suggested, exists: false);
    }

    public async Task<Result<ExpertProfileResponse>> SaveAsync(User user, ExpertProfileRequest request,
        CancellationToken ct = default)
    {
        var nombre = (request.Nombre ?? "").Trim();
        var matricula = (request.Matricula ?? "").Trim();
        var profesion = (request.Profesion ?? "").Trim();
        var caracter = (request.Caracter ?? "").Trim();

        var outcome = CaseValidation.ValidateProfile(nombre, matricula, profesion, caracter, request.Tratamiento);
        if (outcome.Error is not null)
            return Result.Invalid<ExpertProfileResponse>(outcome.Error);
        if (outcome.Missing.Count > 0)
            return Result.Invalid<ExpertProfileResponse>(CaseValidation.MissingMessage, outcome.Missing);

        var existing = await repo.GetAsync(user.Dni, ct);
        var now = DateTime.UtcNow;
        var profile = new ExpertProfile
        {
            Id = user.Dni,
            Nombre = nombre,
            Matricula = matricula,
            Profesion = profesion,
            Caracter = caracter,
            Tratamiento = request.Tratamiento?.Trim() == ExpertProfile.TratamientoSuscripta
                ? ExpertProfile.TratamientoSuscripta
                : ExpertProfile.TratamientoSuscripto,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };
        await repo.UpsertAsync(profile, ct);
        return Result.Ok(ToResponse(profile, exists: true));
    }

    private static ExpertProfileResponse ToResponse(ExpertProfile p, bool exists) => new(
        p.Id, p.Nombre, p.Matricula, p.Profesion, p.Caracter, p.Tratamiento,
        exists, p.IsComplete, exists ? p.UpdatedAt : null);
}
