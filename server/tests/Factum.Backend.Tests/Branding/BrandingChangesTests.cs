using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;
using static Factum.Backend.Tests.Branding.BrandingTestData;

namespace Factum.Backend.Tests.Branding;

/// <summary>marca-por-cliente B20: diff de la auditoría con lista blanca y formato de §4.2.</summary>
public sealed class BrandingChangesTests
{
    private static AccountBranding Before(AccountBrandingImage? logo = null) => new()
    {
        Id = DniA,
        OrganizationName = "Estudio Ficticio A",
        ContactLines = ["Calle Ficticia 123", "Tel. 000"],
        PrimaryColor = "1F3A93",
        AccentColor = "",
        Logo = logo,
        UpdatedAt = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc),
    };

    private static NormalizedBranding Same(AccountBranding b, ImageChange? logo = null) =>
        new(b.OrganizationName, b.ContactLines.ToList(), b.PrimaryColor, b.AccentColor,
            logo ?? ImageChange.Keep, ImageChange.Keep);

    [Fact]
    public void SinCambios_ListaVacia()
    {
        var b = Before();
        Assert.Empty(BrandingChanges.Diff(b, Same(b)));
    }

    [Fact]
    public void MismaImagen_MismaVersion_SinCambio()
    {
        var img = Image(TestImages.Png(64, 32));
        var b = Before(img);
        var again = Image(TestImages.Png(64, 32));
        Assert.Empty(BrandingChanges.Diff(b, Same(b, new ImageChange(BrandingImageAction.Replace, again))));
    }

    [Fact]
    public void Remove_ToVacio_FromVersion()
    {
        var img = Image(TestImages.Png(64, 32));
        var c = Assert.Single(BrandingChanges.Diff(Before(img), Same(Before(img), ImageChange.Remove)));
        Assert.Equal("logo", c.Field);
        Assert.Equal(img.Version, c.From);
        Assert.Equal("", c.To);
    }

    [Fact]
    public void RemoveSinImagenActual_SinCambio()
    {
        var b = Before();
        Assert.Empty(BrandingChanges.Diff(b, Same(b, ImageChange.Remove)));
    }

    [Fact]
    public void Lineas_UnidasConSaltoDeLinea()
    {
        var b = Before();
        var after = Same(b) with { ContactLines = ["Calle Ficticia 123"] };
        var c = Assert.Single(BrandingChanges.Diff(b, after));
        Assert.Equal("contact_lines", c.Field);
        Assert.Equal("Calle Ficticia 123\nTel. 000", c.From);
        Assert.Equal("Calle Ficticia 123", c.To);
    }

    [Fact]
    public void Colores_ConNumeral_YVacioParaElDefault()
    {
        var b = Before();
        var after = Same(b) with { PrimaryColor = "", AccentColor = "E6ECFA" };
        var changes = BrandingChanges.Diff(b, after);
        Assert.Equal(["primary_color", "accent_color"], changes.Select(c => c.Field));
        Assert.Equal(("#1F3A93", ""), (changes[0].From, changes[0].To));
        Assert.Equal(("", "#E6ECFA"), (changes[1].From, changes[1].To));
    }

    [Fact]
    public void SinDocumentoPrevio_FromVacio_NuncaNull_OrdenDeLaListaBlanca()
    {
        var logo = Image(TestImages.Png(64, 32));
        var iso = Image(TestImages.Png(32, 32));
        var after = new NormalizedBranding("Nombre", ["L1", "L2"], "1F3A93", "E6ECFA",
            new ImageChange(BrandingImageAction.Replace, logo), new ImageChange(BrandingImageAction.Replace, iso));
        var changes = BrandingChanges.Diff(null, after);
        Assert.Equal(BrandingChanges.AllowedFields, changes.Select(c => c.Field));
        Assert.All(changes, c => Assert.Equal("", c.From));
        Assert.All(changes, c => Assert.NotNull(c.To));
        Assert.Equal(logo.Version, changes.Single(c => c.Field == "logo").To);
    }

    [Fact]
    public void NingunCampoFueraDeLaListaBlanca()
    {
        Assert.Equal(["organization_name", "contact_lines", "primary_color", "accent_color", "logo", "isotype"],
            BrandingChanges.AllowedFields);
        var after = new NormalizedBranding("Otro", ["X"], "", "", ImageChange.Remove,
            new ImageChange(BrandingImageAction.Replace, Image(TestImages.Png(20, 20))));
        Assert.All(BrandingChanges.Diff(Before(Image(TestImages.Png(64, 32))), after),
            c => Assert.Contains(c.Field, BrandingChanges.AllowedFields));
    }
}
