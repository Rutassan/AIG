using AIG.Game.Config;
using AIG.Game.Core;

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
        Assert.Equal(20, settings.RenderDistance);
        Assert.False(settings.DrawBlockWires);
        Assert.Equal(24f, settings.FogNear);
        Assert.Equal(60f, settings.FogFar);
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
        Assert.Equal(28, settings.RenderDistance);

        settings.CycleQuality();
        Assert.Equal(GraphicsQuality.High, settings.Quality);
        Assert.False(settings.DrawBlockWires);
        Assert.Equal(32, settings.RenderDistance);

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
