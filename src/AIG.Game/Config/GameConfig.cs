namespace AIG.Game.Config;

public sealed class GameConfig
{
    public int WindowWidth { get; init; } = 1280;
    public int WindowHeight { get; init; } = 720;
    public string Title { get; init; } = "AIG 0.001";
    public float MouseSensitivity { get; init; } = 0.0025f;
    public float MoveSpeed { get; init; } = 5.5f;
    public float JumpSpeed { get; init; } = 6.2f;
    public float Gravity { get; init; } = 18f;
    public int TargetFps { get; init; } = 120;
}
