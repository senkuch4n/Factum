using Factum.Agent.Common;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests.Session;

/// <summary>"Pantalla siempre encendida" (grabacion-sesion-completa §5.5, DU2=A), con adb falso.</summary>
public sealed class StayAwakeKeeperTests
{
    private const string Serial = "R58RA3R5AGA";

    /// <summary>Celular falso: guarda el valor y registra cada comando.</summary>
    private sealed class FakePhone
    {
        public string Value = "0";
        public bool Fail;
        public List<string> Commands { get; } = [];

        public Task<ProcessResult> Run(IReadOnlyList<string> args, CancellationToken ct)
        {
            var cmd = args[^1];
            Commands.Add(cmd);
            if (Fail) return Task.FromResult(new ProcessResult(1, "", "error: device not found"));
            if (cmd.StartsWith("settings get", StringComparison.Ordinal))
                return Task.FromResult(new ProcessResult(0, Value + "\n", ""));
            if (cmd.StartsWith("settings put global stay_on_while_plugged_in ", StringComparison.Ordinal))
            {
                Value = cmd.Split(' ')[^1];
                return Task.FromResult(new ProcessResult(0, "", ""));
            }
            return Task.FromResult(new ProcessResult(1, "", "?"));
        }
    }

    [Fact]
    public async Task Begin_LeeElOriginalUnaSolaVez()
    {
        var phone = new FakePhone { Value = "0" };
        var keeper = new StayAwakeKeeper(true, null, phone.Run);
        var state = await keeper.BeginAsync(null, Serial);
        Assert.Equal("0", state!.Original);
        phone.Value = "7";
        var again = await keeper.BeginAsync(state, Serial);
        Assert.Same(state, again);
        Assert.Equal("0", again!.Original);
        Assert.Single(phone.Commands);
    }

    [Fact]
    public async Task Reassert_SoloSiNoEs7()
    {
        var phone = new FakePhone { Value = "7" };
        var keeper = new StayAwakeKeeper(true, null, phone.Run);
        var state = new StayAwakeState { Serial = Serial, Original = "0" };
        await keeper.ReassertAsync(state, Serial);
        Assert.False(state.Owned);
        Assert.DoesNotContain(phone.Commands, c => c.StartsWith("settings put"));

        phone.Value = "0"; // el scrcpy saliente restauró el original con el siguiente grabando (§4.2.4)
        await keeper.ReassertAsync(state, Serial);
        Assert.True(state.Owned);
        Assert.Equal("7", phone.Value);
    }

    [Fact]
    public async Task Restore_SoloSiEs7YEsPropio()
    {
        var phone = new FakePhone { Value = "7" };
        var keeper = new StayAwakeKeeper(true, null, phone.Run);
        var notOwned = new StayAwakeState { Serial = Serial, Original = "0" };
        await keeper.RestoreAsync(notOwned, connected: true);
        Assert.Equal("7", phone.Value); // no lo escribió Tatana: no se toca

        var owned = new StayAwakeState { Serial = Serial, Original = "0", Owned = true };
        await keeper.RestoreAsync(owned, connected: true);
        Assert.Equal("0", phone.Value);
        Assert.False(owned.Owned);
        Assert.False(owned.RestorePending);
    }

    [Fact]
    public async Task Restore_ValorCambiadoPorOtro_NoSeToca()
    {
        var phone = new FakePhone { Value = "3" };
        var keeper = new StayAwakeKeeper(true, null, phone.Run);
        var owned = new StayAwakeState { Serial = Serial, Original = "0", Owned = true };
        await keeper.RestoreAsync(owned, connected: true);
        Assert.Equal("3", phone.Value);
        Assert.False(owned.Owned);
    }

    [Fact]
    public async Task Restore_SinCelular_QuedaPendiente()
    {
        var phone = new FakePhone { Value = "7" };
        var keeper = new StayAwakeKeeper(true, null, phone.Run);
        var owned = new StayAwakeState { Serial = Serial, Original = "0", Owned = true };
        await keeper.RestoreAsync(owned, connected: false);
        Assert.True(owned.RestorePending);
        Assert.Empty(phone.Commands);

        // Vuelve a aparecer: se restaura.
        await keeper.RestoreAsync(owned, connected: true);
        Assert.False(owned.RestorePending);
        Assert.Equal("0", phone.Value);
    }

    [Fact]
    public async Task AdbFalla_NoCortaYQuedaPendiente()
    {
        var phone = new FakePhone { Value = "7", Fail = true };
        var keeper = new StayAwakeKeeper(true, null, phone.Run);
        var owned = new StayAwakeState { Serial = Serial, Original = "0", Owned = true };
        await keeper.RestoreAsync(owned, connected: true);
        Assert.True(owned.RestorePending);
        await keeper.ReassertAsync(owned, Serial); // no tira
    }

    [Fact]
    public async Task Deshabilitado_NoHaceNada()
    {
        var phone = new FakePhone();
        var keeper = new StayAwakeKeeper(false, null, phone.Run);
        Assert.Null(await keeper.BeginAsync(null, Serial));
        await keeper.ReassertAsync(new StayAwakeState { Serial = Serial }, Serial);
        Assert.Empty(phone.Commands);
    }
}
