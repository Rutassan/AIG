using AIG.Game.Config;
using AIG.Game.Core;
using AIG.Game.World;
using System.Globalization;

namespace AIG.Game;

public static class Program
{
    internal static Func<IGameRunner> GameFactory { get; set; } = static () => new GameApp();
    internal static Func<string, IGameRunner> AutoCaptureFactory { get; set; } = static outputDir => new AutoCaptureRunner(outputDir);
    internal static Func<string, float, int, IGameRunner> AutoPerfFactory { get; set; } = static (outputDir, durationSeconds, minFps) => new AutoPerfRunner(outputDir, durationSeconds, minFps);
    internal static Func<GameConfig> AutoCaptureConfigFactory { get; set; } = static () => new GameConfig
    {
        Title = "AIG 0.006 Autocap",
        FullscreenByDefault = false,
        GraphicsQuality = GraphicsQuality.High
    };
    internal static Func<GameConfig> AutoPerfConfigFactory { get; set; } = static () => new GameConfig
    {
        Title = "AIG 0.006 Autoperf",
        FullscreenByDefault = false,
        GraphicsQuality = GraphicsQuality.High
    };
    internal static Func<IGamePlatform> PlatformFactory { get; set; } = static () => new RaylibGamePlatform();
    internal static Func<GameConfig, WorldMap> WorldFactory { get; set; } = static config =>
        new WorldMap(width: 600, height: 72, depth: 600, chunkSize: config.ChunkSize, seed: config.WorldSeed);

    public static void Main(string[] args)
    {
        if (TryRunAutoCapture(args) || TryRunAutoPerf(args))
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

    internal static bool TryRunAutoPerf(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "autoperf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var outputDir = args.Length > 1
            ? args[1]
            : Path.Combine(Directory.GetCurrentDirectory(), "autologs");

        var durationSeconds = ParseDuration(args);
        var minFps = ParseMinFps(args);

        AutoPerfFactory(outputDir, durationSeconds, minFps).Run();
        return true;
    }

    private static float ParseDuration(string[] args)
    {
        if (args.Length <= 2
            || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return 12f;
        }

        return Math.Clamp(parsed, 10f, 15f);
    }

    private static int ParseMinFps(string[] args)
    {
        if (args.Length <= 3 || !int.TryParse(args[3], out var parsed))
        {
            return 60;
        }

        return Math.Clamp(parsed, 30, 240);
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

    private sealed class AutoPerfRunner(string outputDir, float durationSeconds, int minFps) : IGameRunner
    {
        public void Run()
        {
            var config = AutoPerfConfigFactory();
            var world = WorldFactory(config);
            var app = new GameApp(config, PlatformFactory(), world);
            app.RunAutoPerf(outputDir, durationSeconds, minFps);
        }
    }
}
