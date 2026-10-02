using Factum.Agent.Common;

namespace Factum.Agent.Tests;

// T2 de la SDD grabacion-android-windows: cada test arma su propio layout falso de tools/ y
// directorios de PATH en una carpeta temporal, y construye el ToolEnvironment a mano (nunca
// toca el PATH real ni el Homebrew de la máquina).
public sealed class ToolResolverTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "factum-tools-" + Guid.NewGuid().ToString("N"));

    public ToolResolverTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Dir(params string[] parts)
    {
        var d = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(d);
        return d;
    }

    private static string Touch(string dir, string name)
    {
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, "fake");
        return p;
    }

    private string BaseDir => Dir("app");

    private ToolEnvironment WinEnv(string? path, string? pathExt = ".COM;.EXE;.BAT;.CMD",
        IReadOnlyList<string>? fallback = null) =>
        new(BaseDir, path, pathExt, IsWindows: true, fallback ?? []);

    private ToolEnvironment UnixEnv(string? path, IReadOnlyList<string>? fallback = null) =>
        new(BaseDir, path, null, IsWindows: false, fallback ?? []);

    [Fact] // T2a
    public void Windows_portable_scrcpy_exe_gana_y_es_absoluta()
    {
        var exe = Touch(Dir("app", "tools", "scrcpy"), "scrcpy.exe");

        var r = ToolResolver.Find(AgentTools.Scrcpy, WinEnv(null));

        Assert.NotNull(r);
        Assert.Equal(ToolSource.Portable, r!.Source);
        Assert.True(Path.IsPathRooted(r.Path));
        Assert.Equal(Path.GetFullPath(exe), r.Path);
    }

    [Fact] // T2b
    public void Windows_portable_gana_aunque_este_en_el_PATH()
    {
        var exe = Touch(Dir("app", "tools", "scrcpy"), "scrcpy.exe");
        var pathDir = Dir("bin1");
        Touch(pathDir, "scrcpy.exe");

        var r = ToolResolver.Find(AgentTools.Scrcpy, WinEnv(pathDir));

        Assert.Equal(ToolSource.Portable, r!.Source);
        Assert.Equal(Path.GetFullPath(exe), r.Path);
    }

    [Fact] // T2c: el bug del reporte (antes se buscaba "scrcpy" sin .exe)
    public void Windows_sin_portable_scrcpy_exe_en_el_segundo_dir_del_PATH()
    {
        var d1 = Dir("bin1");
        var d2 = Dir("bin2");
        var exe = Touch(d2, "scrcpy.exe");

        var r = ToolResolver.Find(AgentTools.Scrcpy, WinEnv(d1 + ";" + d2));

        Assert.NotNull(r);
        Assert.Equal(ToolSource.Path, r!.Source);
        Assert.Equal(Path.GetFullPath(exe), r.Path);
    }

    [Fact] // T2d
    public void Windows_archivo_sin_extension_en_el_PATH_no_cuenta()
    {
        var d = Dir("bin1");
        Touch(d, "scrcpy");

        Assert.Null(ToolResolver.Find(AgentTools.Scrcpy, WinEnv(d)));
    }

    [Fact] // T2e
    public void Windows_sin_WinName_usa_PATHEXT_y_descarta_bat()
    {
        var d = Dir("bin1");
        Touch(d, "qvh.bat");
        var exe = Touch(d, "qvh.exe");
        var spec = new ToolSpec("qvh", "qvh", "qvh", null);

        var r = ToolResolver.Find(spec, WinEnv(d, pathExt: ".BAT;.EXE"));

        Assert.NotNull(r);
        Assert.Equal(ToolSource.Path, r!.Source);
        Assert.Equal(Path.GetFullPath(exe), r.Path);
    }

    [Fact]
    public void Windows_sin_WinName_solo_bat_da_null()
    {
        var d = Dir("bin1");
        Touch(d, "qvh.bat");
        Touch(d, "qvh.cmd");

        Assert.Null(ToolResolver.Find(new ToolSpec("qvh", "qvh", "qvh", null), WinEnv(d, pathExt: ".BAT;.CMD;.EXE")));
    }

    [Fact] // T2f
    public void Windows_tolera_entradas_vacias_relativas_y_con_comillas()
    {
        var d = Dir("con espacio");
        var exe = Touch(d, "scrcpy.exe");
        // Vacías, solo espacios, "." y relativas se ignoran; la entrada con comillas resuelve.
        var path = ";;  ;.;relativo\\bin;\"" + d + "\";";

        var r = ToolResolver.Find(AgentTools.Scrcpy, WinEnv(path));

        Assert.NotNull(r);
        Assert.Equal(ToolSource.Path, r!.Source);
        Assert.Equal(Path.GetFullPath(exe), r.Path);
    }

    [Fact]
    public void Windows_entrada_relativa_punto_no_resuelve_contra_el_directorio_actual()
    {
        // "." y rutas relativas se ignoran siempre, aunque el archivo exista relativo al cwd.
        var r = ToolResolver.Find(AgentTools.Scrcpy, WinEnv(".;tools"));
        Assert.Null(r);
    }

    [Fact] // T2g
    public void Windows_ignora_los_FallbackDirs()
    {
        var brew = Dir("homebrew");
        Touch(brew, "scrcpy.exe");
        Touch(brew, "scrcpy");

        Assert.Null(ToolResolver.Find(AgentTools.Scrcpy, WinEnv(null, fallback: [brew])));
    }

    [Fact] // T2h
    public void Unix_PATH_separado_con_dos_puntos()
    {
        var d1 = Dir("bin1");
        var d2 = Dir("bin2");
        var bin = Touch(d2, "scrcpy");

        var r = ToolResolver.Find(AgentTools.Scrcpy, UnixEnv(d1 + ":" + d2));

        Assert.NotNull(r);
        Assert.Equal(ToolSource.Path, r!.Source);
        Assert.Equal(Path.GetFullPath(bin), r.Path);
    }

    [Fact] // T2i
    public void Unix_sin_PATH_cae_a_Homebrew()
    {
        var brew = Dir("homebrew");
        var bin = Touch(brew, "scrcpy");

        var r = ToolResolver.Find(AgentTools.Scrcpy, UnixEnv(Dir("vacio"), fallback: [Dir("otro"), brew]));

        Assert.NotNull(r);
        Assert.Equal(ToolSource.Homebrew, r!.Source);
        Assert.Equal(Path.GetFullPath(bin), r.Path);
    }

    [Fact]
    public void Unix_portable_usa_el_nombre_unix()
    {
        var bin = Touch(Dir("app", "tools", "platform-tools"), "adb");
        Touch(Dir("app", "tools", "platform-tools"), "adb.exe");

        var r = ToolResolver.Find(AgentTools.Adb, UnixEnv(null));

        Assert.Equal(ToolSource.Portable, r!.Source);
        Assert.Equal(Path.GetFullPath(bin), r.Path);
    }

    [Fact] // T2j
    public void Nada_en_ningun_lado_da_null()
    {
        Assert.Null(ToolResolver.Find(AgentTools.Scrcpy, WinEnv(Dir("bin1"), fallback: [Dir("brew")])));
        Assert.Null(ToolResolver.Find(AgentTools.Scrcpy, UnixEnv(Dir("bin2"), fallback: [Dir("brew2")])));
        Assert.Null(ToolResolver.Find(AgentTools.Scrcpy, UnixEnv(null)));
    }
}
