using AIG.Game.Config;

namespace AIG.Game.Core;

internal sealed class GraphicsSettings
{
    private readonly bool _forceBlockWires;

    public GraphicsSettings(GameConfig config)
    {
        _forceBlockWires = config.DrawBlockWires;
        Quality = config.GraphicsQuality;
        FogEnabled = config.FogEnabled;
        ReliefContoursEnabled = config.ReliefContoursEnabled;
        ApplyPreset(Quality);
    }

    public GraphicsQuality Quality { get; private set; }
    public bool FogEnabled { get; private set; }
    public bool ReliefContoursEnabled { get; private set; }
    public int RenderDistance { get; private set; }
    public bool DrawBlockWires { get; private set; }
    public float FogNear { get; private set; }
    public float FogFar { get; private set; }
    public float LightStrength { get; private set; }
    public float Contrast { get; private set; }

    public void CycleQuality()
    {
        var next = Quality switch
        {
            GraphicsQuality.Low => GraphicsQuality.Medium,
            GraphicsQuality.Medium => GraphicsQuality.High,
            _ => GraphicsQuality.Low
        };

        ApplyPreset(next);
    }

    public void ToggleFog() => FogEnabled = !FogEnabled;

    public void ToggleReliefContours() => ReliefContoursEnabled = !ReliefContoursEnabled;

    private void ApplyPreset(GraphicsQuality preset)
    {
        Quality = preset;
        switch (preset)
        {
            case GraphicsQuality.Low:
                RenderDistance = 20;
                DrawBlockWires = _forceBlockWires;
                FogNear = 24f;
                FogFar = 60f;
                LightStrength = 0.7f;
                Contrast = 0.95f;
                break;
            case GraphicsQuality.Medium:
                RenderDistance = 28;
                DrawBlockWires = _forceBlockWires;
                FogNear = 28f;
                FogFar = 78f;
                LightStrength = 0.9f;
                Contrast = 1.03f;
                break;
            default:
                RenderDistance = 32;
                DrawBlockWires = _forceBlockWires;
                FogNear = 34f;
                FogFar = 95f;
                LightStrength = 1f;
                Contrast = 1.1f;
                break;
        }
    }
}
