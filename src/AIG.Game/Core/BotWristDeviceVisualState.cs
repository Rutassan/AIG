namespace AIG.Game.Core;

internal sealed class BotWristDeviceVisualState
{
    public float RaiseBlend { get; private set; }
    public float TapBlend { get; private set; }
    public BotWristDeviceTarget TapTarget { get; private set; }

    public void Update(bool targetRaised, float deltaTime)
    {
        var safeDelta = Math.Clamp(deltaTime, 0f, 0.1f);
        var raiseTarget = targetRaised ? 1f : 0f;
        var raiseT = Math.Clamp(safeDelta * (targetRaised ? 9f : 7f), 0f, 1f);
        RaiseBlend += (raiseTarget - RaiseBlend) * raiseT;
        TapBlend = MathF.Max(0f, TapBlend - safeDelta * 4.8f);
        if (TapBlend <= 0.0001f)
        {
            TapTarget = BotWristDeviceTarget.None;
        }
    }

    public void TriggerTap(BotWristDeviceTarget target = BotWristDeviceTarget.None)
    {
        TapBlend = 1f;
        TapTarget = target;
    }

    public void TriggerTap()
    {
        TriggerTap(BotWristDeviceTarget.None);
    }
}
