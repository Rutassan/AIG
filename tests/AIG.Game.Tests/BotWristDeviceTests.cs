using AIG.Game.Bot;
using AIG.Game.Core;

namespace AIG.Game.Tests;

public sealed class BotWristDeviceTests
{
    [Fact(DisplayName = "BotWristDeviceState переключает экраны, вводит количество и хранит сообщение")]
    public void BotWristDeviceState_TracksScreenAmountAndMessage()
    {
        var device = new BotWristDeviceState();

        Assert.False(device.IsOpen);
        device.OpenMain();
        Assert.True(device.IsOpen);
        Assert.Equal(BotWristDeviceScreen.Main, device.Screen);

        device.OpenGatherResource();
        device.SelectResource(BotResourceType.Stone);
        device.ResetAmount();
        Assert.Equal(BotResourceType.Stone, device.SelectedResource);
        Assert.True(device.BackspaceAmount());
        Assert.True(device.AppendDigit(2));
        Assert.True(device.AppendDigit(4));
        Assert.True(device.TryGetAmount(out var amount));
        Assert.Equal(124, amount);

        device.SetMessage("ok");
        Assert.Equal("ok", device.Message);

        device.OpenBuildHouse();
        Assert.Equal(BotWristDeviceScreen.BuildHouse, device.Screen);
        device.BackToMain();
        Assert.Equal(BotWristDeviceScreen.Main, device.Screen);

        device.Close();
        Assert.False(device.IsOpen);
        Assert.Equal(string.Empty, device.Message);
    }

    [Fact(DisplayName = "BotWristDeviceState покрывает invalid и limit ветки количества")]
    public void BotWristDeviceState_AmountValidationBranches_Work()
    {
        var device = new BotWristDeviceState();

        Assert.False(device.AppendDigit(-1));
        Assert.False(device.AppendDigit(12));
        device.ResetAmount();
        Assert.True(device.AppendDigit(3));
        Assert.True(device.AppendDigit(4));
        Assert.True(device.AppendDigit(5));
        Assert.False(device.AppendDigit(6));

        var amountField = typeof(BotWristDeviceState).GetField("<AmountText>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(amountField);

        amountField!.SetValue(device, string.Empty);
        Assert.False(device.BackspaceAmount());

        amountField.SetValue(device, "abc");
        Assert.False(device.TryGetAmount(out _));

        amountField.SetValue(device, "0");
        Assert.False(device.TryGetAmount(out _));

        amountField.SetValue(device, "7");
        Assert.True(device.BackspaceAmount());
        Assert.Equal("0", device.AmountText);

        device.SetMessage(null!);
        Assert.Equal(string.Empty, device.Message);
    }

    [Fact(DisplayName = "BotWristDeviceVisualState плавно поднимает устройство и затухает tap-анимацию")]
    public void BotWristDeviceVisualState_UpdateAndTap_Work()
    {
        var visual = new BotWristDeviceVisualState();

        visual.Update(targetRaised: true, deltaTime: 1f / 30f);
        Assert.True(visual.RaiseBlend > 0f);

        visual.TriggerTap();
        Assert.Equal(1f, visual.TapBlend);

        visual.Update(targetRaised: true, deltaTime: 0.1f);
        Assert.True(visual.TapBlend < 1f);

        visual.Update(targetRaised: false, deltaTime: 0.1f);
        Assert.True(visual.RaiseBlend < 1f);
    }
}
