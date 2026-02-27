using System.Numerics;
using System.IO;
using AIG.Game.Config;
using AIG.Game.Gameplay;
using AIG.Game.Player;
using AIG.Game.World;
using Raylib_cs;

namespace AIG.Game.Core;

public class GameApp : IGameRunner
{
    private const int MenuButtonWidth = 360;
    private const int MenuButtonHeight = 56;
    private const int MenuButtonsGap = 14;

    private readonly GameConfig _config;
    private readonly IGamePlatform _platform;
    private readonly WorldMap _world;
    private readonly BlockType[] _hotbar = [BlockType.Dirt, BlockType.Stone];

    private PlayerController _player = null!;
    private AppState _state = AppState.MainMenu;
    private int _selectedHotbarIndex;

    public GameApp()
        : this(CreateDefaultConfig(), new RaylibGamePlatform(), CreateDefaultWorld(CreateDefaultConfig()))
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

        if (_config.FullscreenByDefault && !_platform.IsWindowFullscreen())
        {
            _platform.ToggleFullscreen();
        }

        _platform.LoadUiFont(ResolveUiFontPath(), 42);
        _platform.EnableCursor();

        _player = new PlayerController(_config, new Vector3(_world.Width / 2f, 4f, _world.Depth / 2f));

        var shouldExit = false;
        var currentHit = (BlockRaycastHit?)null;

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
                HandleHotbarInput();

                var input = ReadInput(_platform);
                _player.Update(_world, input, delta);

                currentHit = VoxelRaycaster.Raycast(_world, _player.EyePosition, _player.LookDirection, _config.InteractionDistance);
                if (_platform.IsMouseButtonPressed(MouseButton.Left))
                {
                    BlockInteraction.TryBreak(_world, currentHit);
                }

                if (_platform.IsMouseButtonPressed(MouseButton.Right))
                {
                    BlockInteraction.TryPlace(_world, currentHit, _hotbar[_selectedHotbarIndex], BlockCenterIntersectsPlayer);
                }
            }
            else
            {
                currentHit = null;
                var menuAction = ReadMenuAction();
                switch (menuAction)
                {
                    case MenuAction.Start:
                        _state = AppState.Playing;
                        _platform.DisableCursor();
                        break;
                    case MenuAction.ToggleFullscreen:
                        _platform.ToggleFullscreen();
                        break;
                    case MenuAction.Exit:
                        shouldExit = true;
                        break;
                }
            }

            DrawFrame(currentHit);
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

    internal static int SelectHotbarIndex(int currentIndex, IGamePlatform platform, int hotbarLength)
    {
        var index = currentIndex;
        for (var slot = 0; slot < hotbarLength && slot < 9; slot++)
        {
            var key = (KeyboardKey)((int)KeyboardKey.One + slot);
            if (platform.IsKeyPressed(key))
            {
                index = slot;
            }
        }

        return index;
    }

    private void HandleHotbarInput()
    {
        _selectedHotbarIndex = SelectHotbarIndex(_selectedHotbarIndex, _platform, _hotbar.Length);
    }

    private MenuAction ReadMenuAction()
    {
        if (IsButtonClicked(GetStartButtonRect()))
        {
            return MenuAction.Start;
        }

        if (IsButtonClicked(GetFullscreenButtonRect()))
        {
            return MenuAction.ToggleFullscreen;
        }

        if (IsButtonClicked(GetExitButtonRect()))
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

    private void DrawFrame(BlockRaycastHit? hit)
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
            DrawBlockHighlight(hit);
            _platform.EndMode3D();

            DrawHud(_state == AppState.Playing);
            DrawHotbar();
        }

        if (_state != AppState.Playing)
        {
            DrawMenu();
        }

        _platform.EndDrawing();
    }

    private void DrawWorld()
    {
        var renderDistance = _config.RenderDistance;
        var centerX = (int)MathF.Floor(_player.Position.X);
        var centerY = (int)MathF.Floor(_player.Position.Y);
        var centerZ = (int)MathF.Floor(_player.Position.Z);

        var minX = Math.Max(0, centerX - renderDistance);
        var maxX = Math.Min(_world.Width - 1, centerX + renderDistance);
        var minZ = Math.Max(0, centerZ - renderDistance);
        var maxZ = Math.Min(_world.Depth - 1, centerZ + renderDistance);
        var minY = Math.Max(0, centerY - 20);
        var maxY = Math.Min(_world.Height - 1, centerY + 24);

        var maxDistSq = renderDistance * renderDistance;

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    var dx = x - _player.Position.X;
                    var dz = z - _player.Position.Z;
                    if (dx * dx + dz * dz > maxDistSq)
                    {
                        continue;
                    }

                    var block = _world.GetBlock(x, y, z);
                    if (block == BlockType.Air || !IsBlockVisible(x, y, z))
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
                    if (_config.DrawBlockWires)
                    {
                        _platform.DrawCubeWires(center, 1f, 1f, 1f, new Color(0, 0, 0, 35));
                    }
                }
            }
        }
    }

    private bool IsBlockVisible(int x, int y, int z)
    {
        return !_world.IsSolid(x + 1, y, z)
            | !_world.IsSolid(x - 1, y, z)
            | !_world.IsSolid(x, y + 1, z)
            | !_world.IsSolid(x, y - 1, z)
            | !_world.IsSolid(x, y, z + 1)
            | !_world.IsSolid(x, y, z - 1);
    }

    private void DrawBlockHighlight(BlockRaycastHit? hit)
    {
        if (hit is null || _state != AppState.Playing)
        {
            return;
        }

        var h = hit.Value;
        var center = new Vector3(h.X + 0.5f, h.Y + 0.5f, h.Z + 0.5f);
        _platform.DrawCubeWires(center, 1.02f, 1.02f, 1.02f, Color.Yellow);
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

        _platform.DrawRectangle(8, 8, 320, 64, new Color(255, 255, 255, 190));
        _platform.DrawUiText($"FPS: {_platform.GetFps()}", new Vector2(16, 14), 20, 1f, Color.Black);
        _platform.DrawUiText($"Pos: {_player.Position.X:0.00}, {_player.Position.Y:0.00}, {_player.Position.Z:0.00}", new Vector2(16, 38), 20, 1f, Color.DarkGray);
    }

    private void DrawHotbar()
    {
        var slotWidth = 120;
        var slotHeight = 42;
        var spacing = 8;
        var totalWidth = _hotbar.Length * slotWidth + (_hotbar.Length - 1) * spacing;

        var startX = _platform.GetScreenWidth() / 2 - totalWidth / 2;
        var y = _platform.GetScreenHeight() - slotHeight - 18;

        for (var i = 0; i < _hotbar.Length; i++)
        {
            var x = startX + i * (slotWidth + spacing);
            var isSelected = i == _selectedHotbarIndex;

            _platform.DrawRectangle(x, y, slotWidth, slotHeight, isSelected ? new Color(255, 245, 163, 235) : new Color(245, 245, 245, 210));
            var label = $"{i + 1}: {GetBlockName(_hotbar[i])}";
            _platform.DrawUiText(label, new Vector2(x + 8, y + 12), 18, 1f, Color.Black);
        }
    }

    private void DrawMenu()
    {
        var title = _state == AppState.MainMenu ? "AIG 0.003" : "Пауза";
        var startLabel = _state == AppState.MainMenu ? "Начать игру" : "Продолжить";
        var fullscreenLabel = _platform.IsWindowFullscreen() ? "Оконный режим" : "Полный экран";

        _platform.DrawRectangle(0, 0, _platform.GetScreenWidth(), _platform.GetScreenHeight(), new Color(10, 16, 26, 150));
        _platform.DrawUiText(title, new Vector2(_platform.GetScreenWidth() / 2f - 95, 100), 48, 1f, Color.White);

        var start = GetStartButtonRect();
        var fullscreen = GetFullscreenButtonRect();
        var exit = GetExitButtonRect();

        _platform.DrawRectangle(start.X, start.Y, start.W, start.H, new Color(228, 233, 241, 240));
        _platform.DrawRectangle(fullscreen.X, fullscreen.Y, fullscreen.W, fullscreen.H, new Color(194, 220, 255, 240));
        _platform.DrawRectangle(exit.X, exit.Y, exit.W, exit.H, new Color(220, 98, 98, 245));

        _platform.DrawUiText(startLabel, new Vector2(start.X + 70, start.Y + 16), 28, 1f, Color.Black);
        _platform.DrawUiText(fullscreenLabel, new Vector2(fullscreen.X + 70, fullscreen.Y + 16), 28, 1f, Color.Black);
        _platform.DrawUiText("Выход", new Vector2(exit.X + 125, exit.Y + 16), 28, 1f, Color.White);
    }

    private (int X, int Y, int W, int H) GetStartButtonRect()
    {
        var x = _platform.GetScreenWidth() / 2 - MenuButtonWidth / 2;
        var y = _platform.GetScreenHeight() / 2 - MenuButtonHeight - MenuButtonsGap;
        return (x, y, MenuButtonWidth, MenuButtonHeight);
    }

    private (int X, int Y, int W, int H) GetFullscreenButtonRect()
    {
        var x = _platform.GetScreenWidth() / 2 - MenuButtonWidth / 2;
        var y = _platform.GetScreenHeight() / 2;
        return (x, y, MenuButtonWidth, MenuButtonHeight);
    }

    private (int X, int Y, int W, int H) GetExitButtonRect()
    {
        var x = _platform.GetScreenWidth() / 2 - MenuButtonWidth / 2;
        var y = _platform.GetScreenHeight() / 2 + MenuButtonHeight + MenuButtonsGap;
        return (x, y, MenuButtonWidth, MenuButtonHeight);
    }

    private bool BlockCenterIntersectsPlayer(Vector3 blockCenter)
    {
        var half = _player.ColliderHalfWidth;
        var height = _player.ColliderHeight;

        var playerMin = new Vector3(_player.Position.X - half, _player.Position.Y, _player.Position.Z - half);
        var playerMax = new Vector3(_player.Position.X + half, _player.Position.Y + height, _player.Position.Z + half);

        var blockMin = blockCenter - new Vector3(0.5f, 0.5f, 0.5f);
        var blockMax = blockCenter + new Vector3(0.5f, 0.5f, 0.5f);

        return (playerMin.X <= blockMax.X) & (playerMax.X >= blockMin.X)
            & (playerMin.Y <= blockMax.Y) & (playerMax.Y >= blockMin.Y)
            & (playerMin.Z <= blockMax.Z) & (playerMax.Z >= blockMin.Z);
    }

    internal static string GetBlockName(BlockType block)
    {
        return block switch
        {
            BlockType.Dirt => "Земля",
            BlockType.Stone => "Камень",
            _ => "Блок"
        };
    }

    private enum MenuAction
    {
        None,
        Start,
        ToggleFullscreen,
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

    private static GameConfig CreateDefaultConfig()
    {
        return new GameConfig();
    }

    private static WorldMap CreateDefaultWorld(GameConfig config)
    {
        return new WorldMap(width: 96, height: 32, depth: 96, chunkSize: config.ChunkSize, seed: config.WorldSeed);
    }
}
