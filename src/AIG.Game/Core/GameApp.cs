using System.Numerics;
using AIG.Game.Config;
using AIG.Game.Player;
using AIG.Game.World;
using Raylib_cs;

namespace AIG.Game.Core;

public class GameApp : IGameRunner
{
    private readonly GameConfig _config;
    private readonly IGamePlatform _platform;
    private readonly WorldMap _world;

    public GameApp()
        : this(new GameConfig(), new RaylibGamePlatform(), new WorldMap(width: 48, height: 16, depth: 48))
    {
    }

    internal GameApp(GameConfig config, IGamePlatform platform, WorldMap world)
    {
        _config = config;
        _platform = platform;
        _world = world;
    }

    public void Run()
    {
        _platform.SetConfigFlags(ConfigFlags.ResizableWindow);
        _platform.InitWindow(_config.WindowWidth, _config.WindowHeight, _config.Title);
        _platform.SetTargetFps(_config.TargetFps);
        _platform.DisableCursor();

        var player = new PlayerController(_config, new Vector3(_world.Width / 2f, 3f, _world.Depth / 2f));

        while (!_platform.WindowShouldClose())
        {
            var delta = _platform.GetFrameTime();
            var input = ReadInput(_platform);
            player.Update(_world, input, delta);

            var camera = new Camera3D
            {
                Position = player.EyePosition,
                Target = player.EyePosition + player.LookDirection,
                Up = Vector3.UnitY,
                FovY = 75f,
                Projection = CameraProjection.Perspective
            };

            DrawFrame(camera, player);
        }

        _platform.EnableCursor();
        _platform.CloseWindow();
    }

    internal static PlayerInput ReadInput(IGamePlatform platform)
    {
        float forward = 0f;
        float right = 0f;

        if (platform.IsKeyDown(KeyboardKey.W) || platform.IsKeyDown(KeyboardKey.Up))
        {
            forward += 1f;
        }

        if (platform.IsKeyDown(KeyboardKey.S) || platform.IsKeyDown(KeyboardKey.Down))
        {
            forward -= 1f;
        }

        if (platform.IsKeyDown(KeyboardKey.D) || platform.IsKeyDown(KeyboardKey.Right))
        {
            right += 1f;
        }

        if (platform.IsKeyDown(KeyboardKey.A) || platform.IsKeyDown(KeyboardKey.Left))
        {
            right -= 1f;
        }

        var mouse = platform.GetMouseDelta();
        return new PlayerInput(
            MoveForward: forward,
            MoveRight: right,
            Jump: platform.IsKeyPressed(KeyboardKey.Space),
            LookDeltaX: mouse.X,
            LookDeltaY: mouse.Y
        );
    }

    private void DrawFrame(Camera3D camera, PlayerController player)
    {
        _platform.BeginDrawing();
        _platform.ClearBackground(new Color(140, 197, 248, 255));

        _platform.BeginMode3D(camera);
        DrawWorld();
        _platform.EndMode3D();

        DrawHud(player);
        _platform.EndDrawing();
    }

    private void DrawWorld()
    {
        for (var x = 0; x < _world.Width; x++)
        {
            for (var y = 0; y < _world.Height; y++)
            {
                for (var z = 0; z < _world.Depth; z++)
                {
                    var block = _world.GetBlock(x, y, z);
                    if (block == BlockType.Air)
                    {
                        continue;
                    }

                    var center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                    var color = block switch
                    {
                        BlockType.Dirt => new Color(124, 91, 59, 255),
                        BlockType.Stone => new Color(112, 112, 122, 255),
                        _ => Color.White
                    };

                    _platform.DrawCube(center, 1f, 1f, 1f, color);
                    _platform.DrawCubeWires(center, 1f, 1f, 1f, new Color(0, 0, 0, 35));
                }
            }
        }
    }

    private void DrawHud(PlayerController player)
    {
        var centerX = _platform.GetScreenWidth() / 2;
        var centerY = _platform.GetScreenHeight() / 2;
        _platform.DrawLine(centerX - 8, centerY, centerX + 8, centerY, Color.Black);
        _platform.DrawLine(centerX, centerY - 8, centerX, centerY + 8, Color.Black);

        _platform.DrawRectangle(8, 8, 290, 64, new Color(255, 255, 255, 190));
        _platform.DrawText($"FPS: {_platform.GetFps()}", 16, 14, 20, Color.Black);
        _platform.DrawText($"Pos: {player.Position.X:0.00}, {player.Position.Y:0.00}, {player.Position.Z:0.00}", 16, 38, 20, Color.DarkGray);
    }
}
