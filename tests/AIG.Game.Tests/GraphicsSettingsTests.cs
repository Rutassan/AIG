using AIG.Game.Config;
using AIG.Game.Core;
using Raylib_cs;

namespace AIG.Game.Tests;

public sealed class GraphicsSettingsTests
{
    [Fact(DisplayName = "GraphicsSettings применяет low-профиль из конфига")]
    public void GraphicsSettings_AppliesLowPreset()
    {
        var settings = new GraphicsSettings(new GameConfig
        {
            GraphicsQuality = GraphicsQuality.Low
        });

        Assert.Equal(GraphicsQuality.Low, settings.Quality);
        Assert.Equal(12, settings.RenderDistance);
        Assert.False(settings.DrawBlockWires);
        Assert.Equal(14f, settings.FogNear);
        Assert.Equal(34f, settings.FogFar);
        Assert.Equal(new Color(166, 174, 179, 255).R, settings.FogColor.R);
        Assert.Equal(0.35f, settings.TextureNoiseStrength);
        Assert.Equal(0.75f, settings.ViewBobScale);
    }

    [Fact(DisplayName = "CycleQuality проходит все пресеты по кругу")]
    public void GraphicsSettings_CyclesAllQualities()
    {
        var settings = new GraphicsSettings(new GameConfig
        {
            GraphicsQuality = GraphicsQuality.Low
        });

        settings.CycleQuality();
        Assert.Equal(GraphicsQuality.Medium, settings.Quality);
        Assert.Equal(14, settings.RenderDistance);
        Assert.Equal(0.5f, settings.TextureNoiseStrength);

        settings.CycleQuality();
        Assert.Equal(GraphicsQuality.High, settings.Quality);
        Assert.False(settings.DrawBlockWires);
        Assert.Equal(14, settings.RenderDistance);
        Assert.Equal(new Color(156, 166, 171, 255).G, settings.FogColor.G);
        Assert.Equal(1.15f, settings.ViewBobScale);

        settings.CycleQuality();
        Assert.Equal(GraphicsQuality.Low, settings.Quality);
    }

    [Fact(DisplayName = "ToggleFog и ToggleReliefContours переключают флаги")]
    public void GraphicsSettings_TogglesFlags()
    {
        var settings = new GraphicsSettings(new GameConfig());
        var initialFog = settings.FogEnabled;
        var initialRelief = settings.ReliefContoursEnabled;

        settings.ToggleFog();
        settings.ToggleReliefContours();

        Assert.Equal(!initialFog, settings.FogEnabled);
        Assert.Equal(!initialRelief, settings.ReliefContoursEnabled);
    }

    [Fact(DisplayName = "GraphicsSettings включает DrawBlockWires по конфигу во всех пресетах")]
    public void GraphicsSettings_RespectsDrawBlockWiresConfig()
    {
        var settings = new GraphicsSettings(new GameConfig
        {
            GraphicsQuality = GraphicsQuality.Low,
            DrawBlockWires = true
        });

        Assert.True(settings.DrawBlockWires);

        settings.CycleQuality();
        Assert.True(settings.DrawBlockWires);

        settings.CycleQuality();
        Assert.True(settings.DrawBlockWires);
    }
}
