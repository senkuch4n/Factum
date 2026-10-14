using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Tests;

// pulido-post-qa-final §9.3: parser del chequeo de pantalla (D9), mock y cuerpo del 409.
public sealed class AndroidScreenProbeTests
{
    // 1) Salida real: Samsung SM-M127F, Android 11, pantalla apagada (SDD §3).
    private const string RealSamsungOff = """
          getWakefulnessLocked()=Dozing
        Display Power: state=OFF
        __FX_SEP__
            mKeyguardOccluded=false mKeyguardOccludedChanged=false mPendingKeyguardOccluded=false
              showing=false
              showingAndNotOccluded=true
              occluded=false
              dreaming=true
                mIsShowing=false
        """;

    [Fact]
    public void Real_Samsung_Android11_apagada_es_Off() =>
        Assert.Equal(ScreenState.Off, AndroidScreenProbe.Parse(RealSamsungOff));

    // D-T17: salida textual del comando de §9.1 en el SM-M127F (Android 11) con la pantalla apagada, tal como la
    // devuelve el grep (sin la línea dreaming=, que el filtro no deja pasar). El keyguard dice showing=false con
    // la pantalla apagada: la energía se decide antes que el bloqueo.
    private const string RealSamsungOffGrep = """
          getWakefulnessLocked()=Dozing
        Display Power: state=OFF
        __FX_SEP__
            mKeyguardOccluded=false mKeyguardOccludedChanged=false mPendingKeyguardOccluded=false
              showing=false
              showingAndNotOccluded=true
              occluded=false
                mIsShowing=false
        """;

    [Fact]
    public void Real_Samsung_Android11_apagada_salida_del_grep_es_Off() =>
        Assert.Equal(ScreenState.Off, AndroidScreenProbe.Parse(RealSamsungOffGrep));

    // 2)
    [Theory]
    [InlineData("  mWakefulness=Asleep\n__FX_SEP__\n    showing=false\n    occluded=false")]
    [InlineData("Display Power: state=DOZE\n__FX_SEP__\n")]
    [InlineData("Display Power: state=DOZE_SUSPEND\n__FX_SEP__\n    showing=true\n    occluded=false")]
    public void Asleep_o_Doze_es_Off(string output) =>
        Assert.Equal(ScreenState.Off, AndroidScreenProbe.Parse(output));

    // 3)
    [Fact]
    public void Encendida_y_desbloqueada_es_On() =>
        Assert.Equal(ScreenState.On, AndroidScreenProbe.Parse("""
              mWakefulness=Awake
            Display Power: state=ON
            __FX_SEP__
                mKeyguardOccluded=false mKeyguardOccludedChanged=false mPendingKeyguardOccluded=false
                  showing=false
                  showingAndNotOccluded=false
                  occluded=false
                    mIsShowing=false
            """));

    // 4)
    [Fact]
    public void Encendida_y_bloqueada_es_Locked() =>
        Assert.Equal(ScreenState.Locked, AndroidScreenProbe.Parse("""
              getWakefulnessLocked()=Awake
            Display Power: state=ON
            __FX_SEP__
                mKeyguardOccluded=false mKeyguardOccludedChanged=false mPendingKeyguardOccluded=false
                  showing=true
                  showingAndNotOccluded=true
                  occluded=false
                    mIsShowing=true
            """));

    // 5) Pantalla de bloqueo ocluida (llamada, cámara segura): la captura no sale negra → On (D-T3).
    [Fact]
    public void Bloqueada_pero_ocluida_es_On() =>
        Assert.Equal(ScreenState.On, AndroidScreenProbe.Parse("""
              mWakefulness=Awake
            Display Power: state=ON
            __FX_SEP__
                  showing=true
                  occluded=true
            """));

    // 6)
    [Fact]
    public void Solo_isKeyguardShowing_true_con_Awake_es_Locked() =>
        Assert.Equal(ScreenState.Locked,
            AndroidScreenProbe.Parse("  mWakefulness=Awake\n__FX_SEP__\n    isKeyguardShowing=true"));

    [Fact]
    public void Alternativa_true_pero_mKeyguardOccluded_true_es_On() =>
        Assert.Equal(ScreenState.On, AndroidScreenProbe.Parse(
            "  mWakefulness=Awake\n__FX_SEP__\n    mKeyguardOccluded=true\n    mShowingLockscreen=true"));

    [Fact]
    public void Alternativas_todas_false_es_On() =>
        Assert.Equal(ScreenState.On, AndroidScreenProbe.Parse(
            "Display Power: state=ON\n__FX_SEP__\n    isKeyguardShowing=false\n    mIsShowing=false"));

    // 7) mAodShowing= no es showing=.
    [Fact]
    public void mAodShowing_no_se_confunde_con_showing() =>
        Assert.Equal(ScreenState.Unknown,
            AndroidScreenProbe.Parse("  mWakefulness=Awake\n__FX_SEP__\n    mAodShowing=true"));

    // 8)
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("/system/bin/sh: grep: not found\n__FX_SEP__\nerror: closed")]
    [InlineData("mWakefulness=\nDisplay Power: state=\n__FX_SEP__\nshowing=maybe")]
    public void Vacia_o_basura_es_Unknown(string? output) =>
        Assert.Equal(ScreenState.Unknown, AndroidScreenProbe.Parse(output));

    // 9)
    [Fact]
    public void Awake_sin_senales_de_keyguard_es_Unknown() =>
        Assert.Equal(ScreenState.Unknown,
            AndroidScreenProbe.Parse("  mWakefulness=Awake\nDisplay Power: state=ON\n__FX_SEP__\n"));

    // ── GetStateAsync ─────────────────────────────────────────────────────────

    private static AndroidScreenProbe Probe(bool mock, string? mockState,
        Func<IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> run) =>
        new(Options.Create(new AgentOptions { Mock = mock, MockAndroidScreenState = mockState }),
            NullLogger<AndroidScreenProbe>.Instance, run);

    private static Task<ProcessResult> NoAdb(IReadOnlyList<string> _, CancellationToken __) =>
        throw new InvalidOperationException("el mock no tiene que llamar a adb");

    [Theory]
    [InlineData("locked", ScreenState.Locked)]
    [InlineData("off", ScreenState.Off)]
    [InlineData("unknown", ScreenState.Unknown)]
    [InlineData("on", ScreenState.On)]
    [InlineData(null, ScreenState.On)]
    public async Task Mock_devuelve_el_estado_configurado_sin_llamar_a_adb(string? cfg, ScreenState expected)
    {
        var r = await Probe(true, cfg, NoAdb).GetStateAsync("SERIAL", CancellationToken.None);
        Assert.Equal(expected, r.State);
    }

    [Fact]
    public async Task Real_arma_el_comando_de_adb_y_parsea()
    {
        IReadOnlyList<string>? seen = null;
        var probe = Probe(false, null, (args, _) =>
        {
            seen = args;
            return Task.FromResult(new ProcessResult(0, RealSamsungOff, ""));
        });
        var r = await probe.GetStateAsync("R58N123", CancellationToken.None);
        Assert.Equal(ScreenState.Off, r.State);
        Assert.Equal(["-s", "R58N123", "shell", AndroidScreenProbe.Script], seen);
    }

    [Theory]
    [InlineData(ProcessRunner.TimeoutExitCode, "", "timeout")]
    [InlineData(-1, "", "adb_error")]
    [InlineData(1, "", "adb_error")]
    [InlineData(0, "nada útil", "no_signals")]
    public async Task Real_no_concluyente_da_Unknown_con_motivo(int exit, string stdout, string reason)
    {
        var r = await Probe(false, null, (_, _) => Task.FromResult(new ProcessResult(exit, stdout, "")))
            .GetStateAsync("X", CancellationToken.None);
        Assert.Equal(ScreenState.Unknown, r.State);
        Assert.Equal(reason, r.Reason);
    }

    [Fact]
    public async Task Real_exit_distinto_de_cero_con_salida_se_parsea()
    {
        // grep del último comando sin coincidencias devuelve 1, pero la parte de energía llegó.
        var r = await Probe(false, null, (_, _) =>
                Task.FromResult(new ProcessResult(1, "Display Power: state=OFF\n__FX_SEP__", "")))
            .GetStateAsync("X", CancellationToken.None);
        Assert.Equal(ScreenState.Off, r.State);
    }

    // ── 409 ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ScreenState.Off, "capturar", "screen_off", "off")]
    [InlineData(ScreenState.Locked, "grabar", "screen_locked", "locked")]
    public void ScreenBlock_arma_el_409_con_code_y_screen_state(ScreenState state, string verb, string code,
        string wire)
    {
        var ex = AndroidScreenProbe.ScreenBlock(state, verb);
        Assert.NotNull(ex);
        Assert.Equal(409, ex.Status);
        var body = ex.ToBody();
        Assert.Equal(code, body["code"]);
        Assert.Equal(wire, body["screen_state"]);
        Assert.Equal(
            $"La pantalla del celular está apagada o bloqueada. Encendela y desbloqueala para {verb}.",
            body["error"]);
    }

    [Theory]
    [InlineData(ScreenState.On)]
    [InlineData(ScreenState.Unknown)]
    public void ScreenBlock_no_frena_On_ni_Unknown(ScreenState state) =>
        Assert.Null(AndroidScreenProbe.ScreenBlock(state, "capturar"));

    [Fact]
    public void Codigos_y_capacidad_del_contrato()
    {
        Assert.Equal("screen_off", AgentErrorCodes.ScreenOff);
        Assert.Equal("screen_locked", AgentErrorCodes.ScreenLocked);
        Assert.Contains("screen_check_v1", Factum.Agent.Controllers.HealthController.Capabilities);
    }
}
