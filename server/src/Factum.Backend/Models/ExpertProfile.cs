using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

/// <summary>
/// Perfil del perito (colección <c>expert_profiles</c>). Un documento por usuario; el
/// <c>_id</c> es el DNI de login. Se copia al caso (<see cref="PeritoSnapshot"/>) al crearlo
/// o editarlo, así un cambio de perfil no altera casos ya generados.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class ExpertProfile
{
    public const string TratamientoSuscripto = "suscripto";
    public const string TratamientoSuscripta = "suscripta";
    public const string CaracterSugerido = "perito informático de parte";

    [BsonId]
    public string Id { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
    public string Matricula { get; set; } = string.Empty;
    public string Profesion { get; set; } = string.Empty;
    public string Caracter { get; set; } = string.Empty;
    public string Tratamiento { get; set; } = TratamientoSuscripto;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore, BsonIgnore]
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Nombre) && !string.IsNullOrWhiteSpace(Matricula) &&
        !string.IsNullOrWhiteSpace(Profesion) && !string.IsNullOrWhiteSpace(Caracter);

    public PeritoSnapshot ToSnapshot() => new()
    {
        Nombre = Nombre,
        Matricula = Matricula,
        Profesion = Profesion,
        Caracter = Caracter,
        Tratamiento = Tratamiento == TratamientoSuscripta ? TratamientoSuscripta : TratamientoSuscripto,
    };
}
