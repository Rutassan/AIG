using AIG.Game.Config;
using Raylib_cs;

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
    public Color FogColor { get; private set; }
    public float TextureNoiseStrength { get; private set; }
    public float ViewBobScale { get; private set; }
    public int DistantViewDistance { get; private set; }

    public int ResolveRenderDistance(int measuredFps)
    {
        if (measuredFps <= 0)
        {
            return RenderDistance;
        }

        if (Quality == GraphicsQuality.High)
        {
            var highAdaptiveDistance = measuredFps switch
            {
                < 45 => 24,
                < 50 => 24,
                < 55 => 24,
                < 60 => 24,
                < 65 => 28,
                < 70 => 34,
                < 75 => 40,
                < 80 => 46,
                < 90 => 56,
                < 100 => 68,
                _ => RenderDistance
            };

            return Math.Clamp(highAdaptiveDistance, 24, RenderDistance);
        }

        var minAdaptiveDistance = Quality == GraphicsQuality.Low ? 12 : 13;
        var reduction = measuredFps switch
        {
            < 55 => 4,
            < 60 => 3,
            < 75 => 2,
            < 90 => 1,
            _ => 0
        };

        return Math.Max(minAdaptiveDistance, RenderDistance - reduction);
    }

    public int ResolveDistantViewDistance(int measuredFps)
    {
        if (measuredFps <= 0)
        {
            return DistantViewDistance;
        }

        if (Quality == GraphicsQuality.High)
        {
            return measuredFps switch
            {
                < 45 => 260,
                < 50 => 320,
                < 55 => 420,
                < 60 => 520,
                < 65 => 620,
                < 70 => 700,
                < 80 => 820,
                < 90 => 920,
                _ => DistantViewDistance
            };
        }

        if (Quality == GraphicsQuality.Medium)
        {
            return measuredFps switch
            {
                < 55 => 240,
                < 60 => 280,
                _ => DistantViewDistance
            };
        }

        return measuredFps switch
        {
            < 55 => 180,
            < 60 => 210,
            _ => DistantViewDistance
        };
    }

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
                RenderDistance = 13;
                DistantViewDistance = 220;
                DrawBlockWires = _forceBlockWires;
                FogNear = 14f;
                FogFar = 34f;
                LightStrength = 0.7f;
                Contrast = 0.95f;
                FogColor = new Color(166, 174, 179, 255);
                TextureNoiseStrength = 0.35f;
                ViewBobScale = 0.75f;
                break;
            case GraphicsQuality.Medium:
                RenderDistance = 15;
                DistantViewDistance = 420;
                DrawBlockWires = _forceBlockWires;
                FogNear = 16f;
                FogFar = 46f;
                LightStrength = 0.9f;
                Contrast = 1.03f;
                FogColor = new Color(162, 171, 176, 255);
                TextureNoiseStrength = 0.5f;
                ViewBobScale = 1f;
                break;
            default:
                RenderDistance = 100;
                DistantViewDistance = 1000;
                DrawBlockWires = _forceBlockWires;
                FogNear = 58f;
                FogFar = 108f;
                LightStrength = 1f;
                Contrast = 1.1f;
                FogColor = new Color(156, 166, 171, 255);
                TextureNoiseStrength = 0.6f;
                ViewBobScale = 1.15f;
                break;
        }
    }
}
