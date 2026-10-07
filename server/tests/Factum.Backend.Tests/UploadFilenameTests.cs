using Factum.Backend.Services.Cases;

namespace Factum.Backend.Tests;

/// <summary>
/// Nombre de archivo de la subida (subida-archivos-grandes §11 T1, D11a): hoy un <c>../</c>
/// escribía fuera de la carpeta del caso. Lógica pura, sin disco.
/// </summary>
public sealed class UploadFilenameTests
{
    [Theory]
    [InlineData("grabacion_20261002_101010.mp4")]
    [InlineData("device_pull_20261002_101010.mp4")]
    [InlineData("adjunto_20261002_101010_1.mov")]
    [InlineData("device_pull_canción ñ.txt")]
    [InlineData("a.b.c")]
    [InlineData("file_20261002_101010")]
    [InlineData("CONSOLA.txt")]
    public void Valid_names_pass(string name) => Assert.True(EvidenceUpload.IsValidUploadName(name));

    [Theory]
    // vacíos y puntos
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    // rutas
    [InlineData("../x.mp4")]
    [InlineData("..\\x.mp4")]
    [InlineData("a/b.mp4")]
    [InlineData("a\\b.mp4")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\x.mp4")]
    // control
    [InlineData("x\0y")]
    [InlineData("x\ny")]
    [InlineData("x\u007Fy")]
    // NTFS
    [InlineData("a<b.mp4")]
    [InlineData("a>b.mp4")]
    [InlineData("a:b.mp4")]
    [InlineData("a\"b.mp4")]
    [InlineData("a|b.mp4")]
    [InlineData("a?b.mp4")]
    [InlineData("a*b.mp4")]
    // final inválido
    [InlineData("x.mp4.")]
    [InlineData("x.mp4 ")]
    // reservados de Windows
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("NUL")]
    [InlineData("com1.mp4")]
    [InlineData("LPT9")]
    [InlineData("aux.tar.gz")]
    public void Invalid_names_are_rejected(string? name) => Assert.False(EvidenceUpload.IsValidUploadName(name));

    [Fact]
    public void Name_longer_than_255_is_rejected()
    {
        Assert.True(EvidenceUpload.IsValidUploadName(new string('a', 255)));
        Assert.False(EvidenceUpload.IsValidUploadName(new string('a', 256)));
    }
}
