using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Agent.Common;
using Factum.Agent.Services;
using Factum.Agent.Services.Ios;

namespace Factum.Agent.Tests;

// T4/T5 de la SDD ios-herramientas-windows: helper embebido, ProcessStartInfo de Python, errores
// del stderr, listado y serialización de /health.tools.
public sealed class IosHelperTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "factum-ioshelper-" + Guid.NewGuid().ToString("N"));

    public IosHelperTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void El_helper_es_un_recurso_embebido_sin_senales_ni_PATH_de_Unix()
    {
        var bytes = IosHelper.Script;
        Assert.NotEmpty(bytes);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("TATANA_ERROR", text);
        Assert.Contains("def cmd_record", text);
        Assert.DoesNotContain("add_signal_handler", text);
        Assert.DoesNotContain("split(':')", text);
        Assert.DoesNotContain("split(\":\")", text);
        Assert.DoesNotContain("import establish_userspace_rsd", text);
        foreach (var sub in new[] { "\"devices\"", "\"prepare\"", "\"screenshot\"", "\"record\"", "\"devmode\"" })
            Assert.Contains(sub, text);
    }

    [Fact]
    public void El_nombre_del_archivo_lleva_el_sha8_y_se_escribe_una_vez()
    {
        var sha8 = Convert.ToHexStringLower(SHA256.HashData(IosHelper.Script))[..8];
        Assert.Equal(sha8, IosHelper.ScriptSha8);
        Assert.Equal($"ios_helper_{sha8}.py", IosHelper.HelperFileName);

        var path = IosHelper.EnsureHelperFile(_root);
        Assert.Equal(Path.Combine(_root, "tatana", $"ios_helper_{sha8}.py"), path);
        Assert.Equal(IosHelper.Script, File.ReadAllBytes(path));

        var mtime = File.GetLastWriteTimeUtc(path);
        Assert.Equal(path, IosHelper.EnsureHelperFile(_root));
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(path)); // no lo reescribe
    }

    [Fact]
    public void CreateStartInfo_pone_el_entorno_UTF8_y_rutas_como_un_solo_argumento()
    {
        const string perfil = @"C:\Users\José Pérez\AppData\Local\Temp\tatana\ios_helper_x.py";
        const string salida = @"C:\Users\José Pérez\AppData\Local\Tatana\data\screenshot 1.png";
        var psi = IosHelper.CreateStartInfo(@"C:\Program Files\Tatana\tools\python-embed\python.exe",
            [perfil, "screenshot", "--udid", "00008120-001A", "--output", salida]);

        Assert.Equal([perfil, "screenshot", "--udid", "00008120-001A", "--output", salida], psi.ArgumentList);
        Assert.Equal("1", psi.Environment["PYTHONUTF8"]);
        Assert.Equal("utf-8", psi.Environment["PYTHONIOENCODING"]);
        Assert.Equal("1", psi.Environment["NO_COLOR"]);
        Assert.Equal("dumb", psi.Environment["TERM"]);
        Assert.Equal("1", psi.Environment["PYTHONDONTWRITEBYTECODE"]);
        Assert.Equal(Encoding.UTF8.WebName, psi.StandardOutputEncoding!.WebName);
        Assert.Equal(Encoding.UTF8.WebName, psi.StandardErrorEncoding!.WebName);
        Assert.True(psi.CreateNoWindow);
        Assert.False(psi.UseShellExecute);
        Assert.False(psi.RedirectStandardInput);
    }

    [Fact]
    public void CreateStartInfo_con_stdin_sin_BOM()
    {
        var psi = IosHelper.CreateStartInfo("python3", ["x.py", "record"], redirectStandardInput: true);
        Assert.True(psi.RedirectStandardInput);
        Assert.Empty(psi.StandardInputEncoding!.GetPreamble());
    }

    [Fact]
    public void Error_desde_stderr_usa_la_ultima_linea_TATANA_ERROR()
    {
        const string stderr = "Traceback (most recent call last):\n  File \"x\", line 1\npymobiledevice3.exceptions.PasscodeRequiredError\n" +
                              "TATANA_ERROR {\"code\": \"ios_locked\", \"detail\": \"PasscodeRequiredError\"}\n";
        var e = IosHelper.ErrorFromStderr(stderr, 2, isWindows: true);
        Assert.Equal(IosErrors.Locked, e.Code);
        Assert.Equal("El iPhone está bloqueado. Desbloquealo y reintentá.", e.Message);
    }

    [Fact]
    public void Error_sin_TATANA_ERROR_es_capture_failed_con_el_ultimo_renglon()
    {
        var e = IosHelper.ErrorFromStderr("algo\nModuleNotFoundError: No module named 'foo'\n", 1, isWindows: true);
        Assert.Equal(IosErrors.CaptureFailed, e.Code);
        Assert.Equal("No se pudo capturar la pantalla del iPhone: ModuleNotFoundError: No module named 'foo'", e.Message);
    }

    [Fact]
    public void Done_del_grabador()
    {
        Assert.Equal((8, 0), DvtRecorder.ParseDoneLine("DONE {\"frames\": 8, \"ffmpeg_exit\": 0}"));
        Assert.Equal((0, 1), DvtRecorder.ParseDoneLine("DONE {\"frames\": 0, \"ffmpeg_exit\": 1}"));
        Assert.Equal((0, -1), DvtRecorder.ParseDoneLine("DONE {roto"));
        Assert.Equal((0, -1), DvtRecorder.ParseDoneLine(null));
    }

    [Fact]
    public void Listado_de_devices_con_acentos_escapados()
    {
        const string stdout = "[{\"udid\": \"00008120-001A\", \"name\": \"iPhone de Jos\\u00e9\", \"product_type\": \"iPhone15,3\", " +
                              "\"product_version\": \"26.5.2\", \"imei\": \"35\", \"phone_number\": \"+54 9\"}, " +
                              "{\"udid\": \"00008030-XYZ\", \"name\": \"\", \"product_type\": \"\", \"product_version\": \"\", \"imei\": \"\", \"phone_number\": \"\"}]";
        var devices = IosService.ParseDevices(stdout)!;
        Assert.Equal(2, devices.Count);
        Assert.Equal("iPhone de José", devices[0].Name);
        Assert.Equal("iPhone 14 Pro Max", devices[0].Model);
        Assert.Equal("26.5.2", devices[0].IosVersion);
        Assert.Equal(26, devices[0].AndroidVersion);
        Assert.Equal("35", devices[0].Imei);
        Assert.Equal("+54 9", devices[0].Operator);
        Assert.Equal("ios", devices[0].Platform);
        Assert.Equal("00008030-XYZ", devices[1].Serial);
        Assert.Null(IosService.ParseDevices("no es json"));
        Assert.Empty(IosService.ParseDevices("")!);
    }

    [Theory]
    [InlineData("pymobiledevice3.exceptions.ConnectionFailedToUsbmuxdError", IosErrors.AppleServiceMissing)]
    [InlineData("pymobiledevice3.exceptions.PasscodeRequiredError: x", IosErrors.Locked)]
    [InlineData("AfcFileNotFoundError: DCIM/100APPLE/IMG_1.MOV", IosErrors.CaptureFailed)]
    public void Clasifica_afc_pull(string stderr, string code)
    {
        Assert.Equal(code, IosService.ClassifyAfcError(stderr, isWindows: true).Code);
    }

    [Fact]
    public void ToolStatus_serializa_pymobiledevice3_version()
    {
        // Mismas opciones que Program.cs.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));

        var json = JsonSerializer.Serialize(new ToolStatus(true, "portable", "C:/p/python.exe", "3.11.9", "10.7.4"), options);
        Assert.Equal("{\"found\":true,\"source\":\"portable\",\"path\":\"C:/p/python.exe\",\"version\":\"3.11.9\",\"pymobiledevice3_version\":\"10.7.4\"}", json);

        var sinVersion = JsonSerializer.Serialize(new ToolStatus(false), options);
        Assert.Equal("{\"found\":false}", sinVersion);
    }

    [Fact]
    public void Ffprobe_ya_no_se_declara()
    {
        Assert.Null(typeof(AgentTools).GetField("Ffprobe"));
    }
}
