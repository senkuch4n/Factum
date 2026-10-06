namespace Factum.Backend.Services.Branding;

/// <summary>Topes del guardado de la marca por cuenta (marca-por-cliente §6.5.1, DT11).</summary>
public static class BrandingLimits
{
    /// <summary>
    /// Tope del cuerpo del <c>PUT</c> multipart (2,5 MiB). Deja margen sobre 2 × 1 MiB para que una imagen
    /// apenas mayor a 1 MiB llegue a la validación y reciba <c>image_too_big</c> con su campo.
    /// </summary>
    public const long MaxRequestBytes = 2_621_440;
}
