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
        Assert.Equal(13, settings.RenderDistance);
        Assert.Equal(220, settings.DistantViewDistance);
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
        Assert.Equal(15, settings.RenderDistance);
        Assert.Equal(420, settings.DistantViewDistance);
        Assert.Equal(0.5f, settings.TextureNoiseStrength);

        settings.CycleQuality();
        Assert.Equal(GraphicsQuality.High, settings.Quality);
        Assert.False(settings.DrawBlockWires);
        Assert.Equal(100, settings.RenderDistance);
        Assert.Equal(1000, settings.DistantViewDistance);
        Assert.Equal(58f, settings.FogNear);
        Assert.Equal(108f, settings.FogFar);
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

    [Fact(DisplayName = "ResolveRenderDistance уменьшает дальность при просадке FPS и не опускается ниже минимума")]
    public void GraphicsSettings_ResolveRenderDistance_AdaptsForLowFps()
    {
        var settings = new GraphicsSettings(new GameConfig
        {
            GraphicsQuality = GraphicsQuality.High
        });

        Assert.Equal(100, settings.ResolveRenderDistance(120));
        Assert.Equal(56, settings.ResolveRenderDistance(80));
        Assert.Equal(40, settings.ResolveRenderDistance(70));
        Assert.Equal(24, settings.ResolveRenderDistance(58));
        Assert.Equal(24, settings.ResolveRenderDistance(30));
    }

    [Fact(DisplayName = "ResolveRenderDistance покрывает дополнительные high-пороги FPS")]
    public void GraphicsSettings_ResolveRenderDistance_CoversHighThresholdBranches()
    {
        var settings = new GraphicsSettings(new GameConfig
        {
            GraphicsQuality = GraphicsQuality.High
        });

        Assert.Equal(68, settings.ResolveRenderDistance(99));
        Assert.Equal(46, settings.ResolveRenderDistance(79));
        Assert.Equal(34, settings.ResolveRenderDistance(69));
        Assert.Equal(28, settings.ResolveRenderDistance(64));
        Assert.Equal(24, settings.ResolveRenderDistance(54));
        Assert.Equal(24, settings.ResolveRenderDistance(49));
    }

    [Fact(DisplayName = "ResolveRenderDistance для medium/low использует свои минимумы")]
    public void GraphicsSettings_ResolveRenderDistance_UsesPerPresetMinimums()
    {
        var medium = new GraphicsSettings(new GameConfig
        {
            GraphicsQuality = GraphicsQuality.Medium
        });
        Assert.Equal(15, medium.ResolveRenderDistance(0));
        Assert.Equal(13, medium.ResolveRenderDistance(20));
        Assert.Equal(14, medium.ResolveRenderDistance(89));
        Assert.Equal(13, medium.ResolveRenderDistance(74));
        Assert.Equal(13, medium.ResolveRenderDistance(59));

        var low = new GraphicsSettings(new GameConfig
        {
            GraphicsQuality = GraphicsQuality.Low
        });
        Assert.Equal(12, low.ResolveRenderDistance(20));
    }

    [Fact(DisplayName = "ResolveDistantViewDistance для high покрывает все пороги")]
    public void GraphicsSettings_ResolveDistantViewDistance_CoversHighThresholds()
    {
        var high = new GraphicsSettings(new GameConfig
        {
            GraphicsQuality = GraphicsQuality.High
        });

        Assert.Equal(1000, high.ResolveDistantViewDistance(0));
        Assert.Equal(260, high.ResolveDistantViewDistance(44));
        Assert.Equal(320, high.ResolveDistantViewDistance(49));
        Assert.Equal(420, high.ResolveDistantViewDistance(54));
        Assert.Equal(520, high.ResolveDistantViewDistance(59));
        Assert.Equal(620, high.ResolveDistantViewDistance(64));
        Assert.Equal(700, high.ResolveDistantViewDistance(69));
        Assert.Equal(820, high.ResolveDistantViewDistance(79));
        Assert.Equal(920, high.ResolveDistantViewDistance(89));
        Assert.Equal(1000, high.ResolveDistantViewDistance(90));
    }

    [Fact(DisplayName = "ResolveDistantViewDistance для medium/low использует свои ветки")]
    public void GraphicsSettings_ResolveDistantViewDistance_CoversMediumAndLowBranches()
    {
        var medium = new GraphicsSettings(new GameConfig
        {
            GraphicsQuality = GraphicsQuality.Medium
        });
        Assert.Equal(240, medium.ResolveDistantViewDistance(54));
        Assert.Equal(280, medium.ResolveDistantViewDistance(59));
        Assert.Equal(420, medium.ResolveDistantViewDistance(60));

        var low = new GraphicsSettings(new GameConfig
        {
            GraphicsQuality = GraphicsQuality.Low
        });
        Assert.Equal(180, low.ResolveDistantViewDistance(54));
        Assert.Equal(210, low.ResolveDistantViewDistance(59));
        Assert.Equal(220, low.ResolveDistantViewDistance(60));
    }
}
