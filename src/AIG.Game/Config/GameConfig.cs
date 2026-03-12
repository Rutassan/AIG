using System.IO;

namespace AIG.Game.Config;

public sealed class GameConfig
{
    public int WindowWidth { get; init; } = 1280;
    public int WindowHeight { get; init; } = 720;
    public string Title { get; init; } = "AIG 0.016";
    public float MouseSensitivity { get; init; } = 0.0025f;
    public float MoveSpeed { get; init; } = 5.5f;
    public float JumpSpeed { get; init; } = 6.2f;
    public float Gravity { get; init; } = 18f;
    public int TargetFps { get; init; } = 120;
    public bool FullscreenByDefault { get; init; } = true;
    public float InteractionDistance { get; init; } = 6.5f;
    public int WorldSeed { get; init; } = 777;
    public int ChunkSize { get; init; } = 16;
    public int RenderDistance { get; init; } = 28;
    public bool DrawBlockWires { get; init; } = false;
    public GraphicsQuality GraphicsQuality { get; init; } = GraphicsQuality.Medium;
    public bool FogEnabled { get; init; } = true;
    public bool ReliefContoursEnabled { get; init; } = true;
    public bool BotDiagnosticsEnabled { get; init; }
    public string BotDiagnosticsDirectory { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "botlogs");
    public string ScreenshotDirectory { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "captures", "screenshots");
    public string VideoDirectory { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "captures", "videos");
    public int VideoCaptureFps { get; init; } = 15;
}
