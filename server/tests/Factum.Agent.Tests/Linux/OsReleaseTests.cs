using Factum.Agent.Services.Linux;

namespace Factum.Agent.Tests.Linux;

/// <summary>tatana-linux §5.2: parseo de /etc/os-release.</summary>
public sealed class OsReleaseTests
{
    [Fact]
    public void Ubuntu_24_04()
    {
        var r = OsRelease.Parse("""
            PRETTY_NAME="Ubuntu 24.04.1 LTS"
            NAME="Ubuntu"
            VERSION_ID="24.04"
            VERSION="24.04.1 LTS (Noble Numbat)"
            ID=ubuntu
            ID_LIKE=debian
            """);
        Assert.Equal(new OsRelease("ubuntu", "24.04", "Ubuntu 24.04.1 LTS", "debian"), r);
    }

    [Fact]
    public void Debian_12()
    {
        var r = OsRelease.Parse("PRETTY_NAME=\"Debian GNU/Linux 12 (bookworm)\"\nNAME=\"Debian GNU/Linux\"\nVERSION_ID=\"12\"\nID=debian\n");
        Assert.Equal(new OsRelease("debian", "12", "Debian GNU/Linux 12 (bookworm)", null), r);
    }

    [Fact]
    public void Fedora_con_comentarios_y_CRLF()
    {
        var r = OsRelease.Parse("# comentario\r\nNAME=\"Fedora Linux\"\r\nVERSION_ID=40\r\nID=fedora\r\nPRETTY_NAME=\"Fedora Linux 40 (Workstation Edition)\"\r\n");
        Assert.Equal(new OsRelease("fedora", "40", "Fedora Linux 40 (Workstation Edition)", null), r);
    }

    [Fact]
    public void Mint_con_ID_LIKE_de_dos_palabras()
    {
        var r = OsRelease.Parse("NAME=\"Linux Mint\"\nID=linuxmint\nID_LIKE=\"ubuntu debian\"\nPRETTY_NAME=\"Linux Mint 22\"\nVERSION_ID=\"22\"\n");
        Assert.Equal("linuxmint", r.Id);
        Assert.Equal("ubuntu debian", r.IdLike);
        Assert.Equal("22", r.VersionId);
    }

    [Fact]
    public void Arch_sin_VERSION_ID()
    {
        var r = OsRelease.Parse("NAME=\"Arch Linux\"\nPRETTY_NAME='Arch Linux'\nID=arch\nBUILD_ID=rolling\n");
        Assert.Equal(new OsRelease("arch", null, "Arch Linux", null), r);
    }

    [Fact]
    public void ID_en_mayusculas_se_pasa_a_minusculas_y_vacio_es_linux()
    {
        Assert.Equal("ubuntu", OsRelease.Parse("ID=\"Ubuntu\"").Id);
        Assert.Equal("linux", OsRelease.Parse("").Id);
        Assert.Equal("linux", OsRelease.Parse(null).Id);
    }

    [Fact]
    public void Escapes_dentro_de_comillas_dobles()
    {
        Assert.Equal("Distro \"X\"", OsRelease.Parse("PRETTY_NAME=\"Distro \\\"X\\\"\"").PrettyName);
    }

    [Fact]
    public void Read_usa_el_fallback_de_usr_lib()
    {
        var r = OsRelease.Read(p => p == "/usr/lib/os-release" ? "ID=debian\nVERSION_ID=12" : null);
        Assert.Equal("debian", r.Id);
        Assert.Equal(OsRelease.Unknown, OsRelease.Read(_ => null));
    }
}
