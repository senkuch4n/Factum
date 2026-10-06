using Factum.Backend.Models;
using Factum.Backend.Services.Branding;
using Factum.Backend.Tests.Auth;
using static Factum.Backend.Tests.Branding.BrandingTestData;

namespace Factum.Backend.Tests.Branding;

/// <summary>marca-por-cliente B22: la marca del dueño del caso, sin mezclar con la instalación (D4-A).</summary>
public sealed class ReportBrandingResolverTests
{
    private static BrandingLogo InstallationLogo()
    {
        var png = TestImages.Png(80, 40);
        return new BrandingLogo(png, "image/png", ".png", 80, 40, BrandingRules.VersionOf(png));
    }

    private static readonly BrandingSnapshot Installation = new("Instalación Ficticia", ["Contacto Instalación"],
        InstallationLogo(), InstallationLogo(), "203040", "E8F3DF");

    private static (ReportBrandingResolver Resolver, InMemoryAccountBrandingRepository Repo,
        CapturingLogger<ReportBrandingResolver> Log) Make()
    {
        var repo = new InMemoryAccountBrandingRepository();
        var log = new CapturingLogger<ReportBrandingResolver>();
        return (new ReportBrandingResolver(repo, new FixedInstallationBranding(Installation), log), repo, log);
    }

    [Fact]
    public async Task SinDocumento_LaInstalacion_MismaInstancia()
    {
        var (resolver, repo, _) = Make();
        Assert.Same(Installation, await resolver.ResolveAsync(DniA));
        Assert.Equal(1, repo.FindCalls);
    }

    [Fact]
    public async Task DocumentoVacio_SinNombreNiLogo_NoCaeALaInstalacion()
    {
        var (resolver, repo, _) = Make();
        repo.Seed(new AccountBranding { Id = DniA });

        var brand = await resolver.ResolveAsync(DniA);

        Assert.NotSame(Installation, brand);
        Assert.Null(brand.OrganizationName);
        Assert.Empty(brand.ContactLines);
        Assert.Null(brand.Logo);
        Assert.Null(brand.Isotype);
    }

    [Fact]
    public async Task ColoresVacios_DefaultsDeFactum()
    {
        var (resolver, repo, _) = Make();
        repo.Seed(new AccountBranding { Id = DniA, OrganizationName = "X", PrimaryColor = "", AccentColor = "" });
        var brand = await resolver.ResolveAsync(DniA);
        Assert.Equal(BrandingColors.DefaultPrimary, brand.PrimaryColor);
        Assert.Equal(BrandingColors.DefaultAccent, brand.AccentColor);
    }

    [Fact]
    public async Task MarcaCompleta_SoloLaDeLaCuenta()
    {
        var (resolver, repo, _) = Make();
        var logo = Image(TestImages.Png(64, 32));
        var iso = Image(TestImages.Jpeg(40, 40));
        repo.Seed(new AccountBranding
        {
            Id = DniA, OrganizationName = "Estudio Ficticio A", ContactLines = ["L1", "L2"],
            PrimaryColor = "1F3A93", AccentColor = "E6ECFA", Logo = logo, Isotype = iso,
        });

        var brand = await resolver.ResolveAsync(DniA);

        Assert.Equal("Estudio Ficticio A", brand.OrganizationName);
        Assert.Equal(["L1", "L2"], brand.ContactLines);
        Assert.Equal(("1F3A93", "E6ECFA"), (brand.PrimaryColor, brand.AccentColor));
        Assert.Equal((".png", 64, 32, logo.Version), (brand.Logo!.Extension, brand.Logo.Width, brand.Logo.Height, brand.Logo.Version));
        Assert.Equal(logo.Data, brand.Logo.Data);
        Assert.Equal(("image/jpeg", ".jpg"), (brand.Isotype!.ContentType, brand.Isotype.Extension));
    }

    [Fact]
    public async Task ImagenCorrupta_LogoNull_SinExcepcion_WarningSinBytes()
    {
        var (resolver, repo, log) = Make();
        repo.Seed(new AccountBranding
        {
            Id = DniA, OrganizationName = "X",
            Logo = new AccountBrandingImage { Data = [1, 2, 3, 4, 5], ContentType = "image/png", Version = "abcdefabcdef" },
        });

        var brand = await resolver.ResolveAsync(DniA);

        Assert.Null(brand.Logo);
        Assert.Equal("X", brand.OrganizationName);
        var warning = Assert.Single(log.Messages, m => m.Contains("logo ignorado"));
        Assert.Contains("Marca de cuenta 99000001", warning);
    }

    [Fact]
    public async Task ColorInvalidoEnLaBase_Default_ConWarning()
    {
        var (resolver, repo, log) = Make();
        repo.Seed(new AccountBranding { Id = DniA, PrimaryColor = "FFFF00", AccentColor = "zz" });
        var brand = await resolver.ResolveAsync(DniA);
        Assert.Equal(BrandingColors.DefaultPrimary, brand.PrimaryColor);
        Assert.Equal(BrandingColors.DefaultAccent, brand.AccentColor);
        Assert.Equal(2, log.Messages.Count(m => m.Contains("Marca de cuenta")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("abcdefgh")]
    [InlineData("1234567890")]
    public async Task DniVacioOInvalido_LaInstalacion_SinConsultar(string? dni)
    {
        var (resolver, repo, _) = Make();
        repo.FindThrows = new InvalidOperationException("no debería consultarse");
        Assert.Same(Installation, await resolver.ResolveAsync(dni));
        Assert.Equal(0, repo.FindCalls);
    }

    [Fact]
    public async Task UnaSolaLecturaPorResolve()
    {
        var (resolver, repo, _) = Make();
        repo.Seed(new AccountBranding { Id = DniA, OrganizationName = "X", Logo = Image(TestImages.Png(20, 20)) });
        await resolver.ResolveAsync(DniA);
        Assert.Equal(1, repo.FindCalls);
        await resolver.ResolveAsync(DniB);
        Assert.Equal(2, repo.FindCalls);
    }

    [Fact]
    public async Task ExcepcionDelRepositorio_SePropaga()
    {
        var (resolver, repo, _) = Make();
        repo.FindThrows = new TimeoutException("mongo caído");
        await Assert.ThrowsAsync<TimeoutException>(() => resolver.ResolveAsync(DniA));
    }
}
