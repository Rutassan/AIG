using System.Numerics;
using System.IO;
using AIG.Game.Config;
using AIG.Game.Player;
using AIG.Game.World;
using Raylib_cs;

namespace AIG.Game.Core;

public class GameApp : IGameRunner
{
    private const int MenuButtonWidth = 320;
    private const int MenuButtonHeight = 56;
    private const int MenuButtonsGap = 18;

    private readonly GameConfig _config;
    private readonly IGamePlatform _platform;
    private readonly WorldMap _world;

    private PlayerController _player = null!;
    private AppState _state = AppState.MainMenu;

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

    private enum AppState
    {
        MainMenu,
        Playing,
        PauseMenu
    }

    public void Run()
    {
        _platform.SetConfigFlags(ConfigFlags.ResizableWindow);
        _platform.InitWindow(_config.WindowWidth, _config.WindowHeight, _config.Title);
        _platform.SetExitKey(KeyboardKey.Null);
        _platform.SetTargetFps(_config.TargetFps);
        _platform.LoadUiFont(ResolveUiFontPath(), 42);
        _platform.EnableCursor();

        _player = new PlayerController(_config, new Vector3(_world.Width / 2f, 3f, _world.Depth / 2f));

        var shouldExit = false;
        while (!shouldExit && !_platform.WindowShouldClose())
        {
            if (_state == AppState.Playing && _platform.IsKeyPressed(KeyboardKey.Escape))
            {
                _state = AppState.PauseMenu;
                _platform.EnableCursor();
            }

            var delta = _platform.GetFrameTime();
            if (_state == AppState.Playing)
            {
                var input = ReadInput(_platform);
                _player.Update(_world, input, delta);
            }
            else
            {
                var menuAction = ReadMenuAction();
                if (menuAction == MenuAction.Start)
                {
                    _state = AppState.Playing;
                    _platform.DisableCursor();
                }
                else if (menuAction == MenuAction.Exit)
                {
                    shouldExit = true;
                }
            }

            DrawFrame();
        }

        _platform.UnloadUiFont();
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

    private MenuAction ReadMenuAction()
    {
        var startRect = GetStartButtonRect();
        var exitRect = GetExitButtonRect();

        if (IsButtonClicked(startRect))
        {
            return MenuAction.Start;
        }

        if (IsButtonClicked(exitRect))
        {
            return MenuAction.Exit;
        }

        return MenuAction.None;
    }

    private bool IsButtonClicked((int X, int Y, int W, int H) rect)
    {
        if (!_platform.IsMouseButtonPressed(MouseButton.Left))
        {
            return false;
        }

        var mouse = _platform.GetMousePosition();
        return mouse.X >= rect.X
            && mouse.X <= rect.X + rect.W
            && mouse.Y >= rect.Y
            && mouse.Y <= rect.Y + rect.H;
    }

    private void DrawFrame()
    {
        _platform.BeginDrawing();
        _platform.ClearBackground(new Color(140, 197, 248, 255));

        if (_state != AppState.MainMenu)
        {
            var camera = new Camera3D
            {
                Position = _player.EyePosition,
                Target = _player.EyePosition + _player.LookDirection,
                Up = Vector3.UnitY,
                FovY = 75f,
                Projection = CameraProjection.Perspective
            };

            _platform.BeginMode3D(camera);
            DrawWorld();
            _platform.EndMode3D();

            DrawHud(_state == AppState.Playing);
        }

        if (_state != AppState.Playing)
        {
            DrawMenu();
        }

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

    private void DrawHud(bool drawCrosshair)
    {
        if (drawCrosshair)
        {
            var centerX = _platform.GetScreenWidth() / 2;
            var centerY = _platform.GetScreenHeight() / 2;
            _platform.DrawLine(centerX - 8, centerY, centerX + 8, centerY, Color.Black);
            _platform.DrawLine(centerX, centerY - 8, centerX, centerY + 8, Color.Black);
        }

        _platform.DrawRectangle(8, 8, 290, 64, new Color(255, 255, 255, 190));
        _platform.DrawUiText($"FPS: {_platform.GetFps()}", new Vector2(16, 14), 20, 1f, Color.Black);
        _platform.DrawUiText($"Pos: {_player.Position.X:0.00}, {_player.Position.Y:0.00}, {_player.Position.Z:0.00}", new Vector2(16, 38), 20, 1f, Color.DarkGray);
    }

    private void DrawMenu()
    {
        var title = _state == AppState.MainMenu ? "AIG 0.001" : "Пауза";
        var startLabel = _state == AppState.MainMenu ? "Начать игру" : "Продолжить";

        _platform.DrawRectangle(0, 0, _platform.GetScreenWidth(), _platform.GetScreenHeight(), new Color(10, 16, 26, 150));
        _platform.DrawUiText(title, new Vector2(_platform.GetScreenWidth() / 2f - 95, 120), 48, 1f, Color.White);

        var start = GetStartButtonRect();
        var exit = GetExitButtonRect();

        _platform.DrawRectangle(start.X, start.Y, start.W, start.H, new Color(228, 233, 241, 240));
        _platform.DrawRectangle(exit.X, exit.Y, exit.W, exit.H, new Color(220, 98, 98, 245));

        _platform.DrawUiText(startLabel, new Vector2(start.X + 70, start.Y + 16), 28, 1f, Color.Black);
        _platform.DrawUiText("Выход", new Vector2(exit.X + 115, exit.Y + 16), 28, 1f, Color.White);
    }

    private (int X, int Y, int W, int H) GetStartButtonRect()
    {
        var x = _platform.GetScreenWidth() / 2 - MenuButtonWidth / 2;
        var y = _platform.GetScreenHeight() / 2 - MenuButtonHeight - MenuButtonsGap / 2;
        return (x, y, MenuButtonWidth, MenuButtonHeight);
    }

    private (int X, int Y, int W, int H) GetExitButtonRect()
    {
        var x = _platform.GetScreenWidth() / 2 - MenuButtonWidth / 2;
        var y = _platform.GetScreenHeight() / 2 + MenuButtonsGap / 2;
        return (x, y, MenuButtonWidth, MenuButtonHeight);
    }

    private enum MenuAction
    {
        None,
        Start,
        Exit
    }

    internal static string ResolveUiFontPath(IEnumerable<string>? candidates = null)
    {
        candidates ??= new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "NotoSans-Regular.ttf"),
            Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "DejaVuSans.ttf"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "AIG.Game", "assets", "fonts", "NotoSans-Regular.ttf"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "fonts", "NotoSans-Regular.ttf"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "fonts", "DejaVuSans.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
            "/usr/share/fonts/noto/NotoSans-Regular.ttf"
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}
