using AIG.Game.Config;
using AIG.Game.Core;
using AIG.Game.World;

namespace AIG.Game;

public static class Program
{
    internal static Func<IGameRunner> GameFactory { get; set; } = static () => new GameApp();
    internal static Func<string, IGameRunner> AutoCaptureFactory { get; set; } = static outputDir => new AutoCaptureRunner(outputDir);
    internal static Func<GameConfig> AutoCaptureConfigFactory { get; set; } = static () => new GameConfig
    {
        Title = "AIG 0.004 Autocap",
        FullscreenByDefault = false,
        GraphicsQuality = GraphicsQuality.High
    };
    internal static Func<IGamePlatform> PlatformFactory { get; set; } = static () => new RaylibGamePlatform();
    internal static Func<GameConfig, WorldMap> WorldFactory { get; set; } = static config =>
        new WorldMap(width: 96, height: 32, depth: 96, chunkSize: config.ChunkSize, seed: config.WorldSeed);

    public static void Main(string[] args)
    {
        if (TryRunAutoCapture(args))
        {
            return;
        }

        GameFactory().Run();
    }

    internal static bool TryRunAutoCapture(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "autocap", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var outputDir = args.Length > 1
            ? args[1]
            : Path.Combine(Directory.GetCurrentDirectory(), "autocap");

        AutoCaptureFactory(outputDir).Run();
        return true;
    }

    private sealed class AutoCaptureRunner(string outputDir) : IGameRunner
    {
        public void Run()
        {
            var config = AutoCaptureConfigFactory();
            var world = WorldFactory(config);
            var app = new GameApp(config, PlatformFactory(), world);
            app.RunAutoCapture(outputDir);
        }
    }
}
