namespace AIG.Game.Core;

internal sealed class BotWristDeviceVisualState
{
    public float RaiseBlend { get; private set; }
    public float TapBlend { get; private set; }

    public void Update(bool targetRaised, float deltaTime)
    {
        var safeDelta = Math.Clamp(deltaTime, 0f, 0.1f);
        var raiseTarget = targetRaised ? 1f : 0f;
        var raiseT = Math.Clamp(safeDelta * (targetRaised ? 9f : 7f), 0f, 1f);
        RaiseBlend += (raiseTarget - RaiseBlend) * raiseT;
        TapBlend = MathF.Max(0f, TapBlend - safeDelta * 4.8f);
    }

    public void TriggerTap()
    {
        TapBlend = 1f;
    }
}
