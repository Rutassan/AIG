using AIG.Game.Bot;

namespace AIG.Game.Core;

internal enum BotWristDeviceScreen
{
    Closed = 0,
    Main = 1,
    GatherResource = 2,
    BuildHouse = 3
}

internal sealed class BotWristDeviceState
{
    private const string DefaultAmountText = "16";
    private const int MaxDigits = 5;

    public BotWristDeviceScreen Screen { get; private set; }
    public BotResourceType SelectedResource { get; private set; } = BotResourceType.Wood;
    public string AmountText { get; private set; } = DefaultAmountText;
    public string Message { get; private set; } = string.Empty;

    public bool IsOpen => Screen != BotWristDeviceScreen.Closed;

    public void OpenMain()
    {
        Screen = BotWristDeviceScreen.Main;
    }

    public void OpenGatherResource()
    {
        Screen = BotWristDeviceScreen.GatherResource;
    }

    public void OpenBuildHouse()
    {
        Screen = BotWristDeviceScreen.BuildHouse;
    }

    public void BackToMain()
    {
        Screen = BotWristDeviceScreen.Main;
    }

    public void Close()
    {
        Screen = BotWristDeviceScreen.Closed;
        Message = string.Empty;
    }

    public void SelectResource(BotResourceType resource)
    {
        SelectedResource = resource;
    }

    public bool AppendDigit(int digit)
    {
        if (digit < 0 || digit > 9)
        {
            return false;
        }

        if (AmountText.Length >= MaxDigits)
        {
            return false;
        }

        AmountText = AmountText == "0"
            ? digit.ToString()
            : AmountText + digit;
        return true;
    }

    public bool BackspaceAmount()
    {
        if (AmountText.Length == 0)
        {
            return false;
        }

        AmountText = AmountText.Length == 1
            ? "0"
            : AmountText[..^1];
        return true;
    }

    public void ResetAmount()
    {
        AmountText = DefaultAmountText;
    }

    public bool TryGetAmount(out int amount)
    {
        if (!int.TryParse(AmountText, out amount))
        {
            return false;
        }

        return amount > 0;
    }

    public void SetMessage(string message)
    {
        Message = message ?? string.Empty;
    }
}
