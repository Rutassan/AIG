using System.Numerics;
using AIG.Game;
using AIG.Game.Config;
using AIG.Game.Core;
using AIG.Game.Gameplay;
using AIG.Game.Tests.Fakes;
using AIG.Game.World;
using Raylib_cs;
using System.Reflection;

namespace AIG.Game.Tests;

public sealed class CoreFlowTests
{
    [Fact(DisplayName = "Program.Main запускает раннер из фабрики")]
    public void Program_Main_UsesFactoryRunner()
    {
        var called = false;
        var previousFactory = Program.GameFactory;

        Program.GameFactory = () => new DelegatingRunner(() => called = true);
        try
        {
            Program.Main([]);
        }
        finally
        {
            Program.GameFactory = previousFactory;
        }

        Assert.True(called);
    }

    [Fact(DisplayName = "Program.Main в режиме autocap использует AutoCaptureFactory")]
    public void Program_Main_Autocap_UsesAutoCaptureFactory()
    {
        var autocapCalled = false;
        var gameCalled = false;
        string? capturedOutputDir = null;

        var previousAutoFactory = Program.AutoCaptureFactory;
        var previousGameFactory = Program.GameFactory;

        Program.AutoCaptureFactory = outputDir => new DelegatingRunner(() =>
        {
            autocapCalled = true;
            capturedOutputDir = outputDir;
        });
        Program.GameFactory = () => new DelegatingRunner(() => gameCalled = true);
        try
        {
            Program.Main(["autocap", "/tmp/aig-autocap-test"]);
        }
        finally
        {
            Program.AutoCaptureFactory = previousAutoFactory;
            Program.GameFactory = previousGameFactory;
        }

        Assert.True(autocapCalled);
        Assert.False(gameCalled);
        Assert.Equal("/tmp/aig-autocap-test", capturedOutputDir);
    }

    [Fact(DisplayName = "TryRunAutoCapture возвращает false для обычного запуска")]
    public void Program_TryRunAutoCapture_ReturnsFalse_ForRegularArgs()
    {
        var previousAutoFactory = Program.AutoCaptureFactory;
        var autoCalled = false;
        Program.AutoCaptureFactory = _ =>
        {
            autoCalled = true;
            return new DelegatingRunner(() => { });
        };

        try
        {
            var result = Program.TryRunAutoCapture([]);
            Assert.False(result);
            Assert.False(autoCalled);
        }
        finally
        {
            Program.AutoCaptureFactory = previousAutoFactory;
        }
    }

    [Fact(DisplayName = "TryRunAutoCapture использует директорию по умолчанию")]
    public void Program_TryRunAutoCapture_UsesDefaultOutputDir()
    {
        var previousAutoFactory = Program.AutoCaptureFactory;
        string? outputDir = null;
        Program.AutoCaptureFactory = dir => new DelegatingRunner(() => outputDir = dir);

        try
        {
            var result = Program.TryRunAutoCapture(["autocap"]);
            Assert.True(result);
        }
        finally
        {
            Program.AutoCaptureFactory = previousAutoFactory;
        }

        Assert.NotNull(outputDir);
        Assert.EndsWith("autocap", outputDir, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "TryRunAutoCapture c default AutoCaptureRunner выполняет автокапчер через платформу")]
    public void Program_TryRunAutoCapture_DefaultRunner_CapturesScreenshots()
    {
        var previousPlatformFactory = Program.PlatformFactory;
        var previousConfigFactory = Program.AutoCaptureConfigFactory;
        var previousWorldFactory = Program.WorldFactory;

        var platform = new FakeGamePlatform();
        Program.PlatformFactory = () => platform;
        Program.AutoCaptureConfigFactory = () => new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        Program.WorldFactory = config => new WorldMap(width: 96, height: 32, depth: 96, chunkSize: config.ChunkSize, seed: config.WorldSeed);

        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autocap-{Guid.NewGuid():N}");
        try
        {
            var result = Program.TryRunAutoCapture(["autocap", outputDir]);
            Assert.True(result);
        }
        finally
        {
            Program.PlatformFactory = previousPlatformFactory;
            Program.AutoCaptureConfigFactory = previousConfigFactory;
            Program.WorldFactory = previousWorldFactory;
        }

        Assert.Equal(3, platform.SavedScreenshots.Count);
        Assert.All(platform.SavedScreenshots, screenshot => Assert.StartsWith(outputDir, screenshot, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "ReadInput корректно собирает направление и прыжок")]
    public void ReadInput_CollectsKeysAndMouse()
    {
        var platform = new FakeGamePlatform
        {
            MouseDelta = new Vector2(3.5f, -2.25f)
        };
        platform.SetDownKeys(KeyboardKey.W, KeyboardKey.D);
        platform.SetPressedKeys(KeyboardKey.Space);

        var input = GameApp.ReadInput(platform);

        Assert.Equal(1f, input.MoveForward);
        Assert.Equal(1f, input.MoveRight);
        Assert.True(input.Jump);
        Assert.Equal(3.5f, input.LookDeltaX);
        Assert.Equal(-2.25f, input.LookDeltaY);
    }

    [Fact(DisplayName = "ReadInput учитывает стрелки и противоположные клавиши")]
    public void ReadInput_CancelsOppositeDirections()
    {
        var platform = new FakeGamePlatform();
        platform.SetDownKeys(KeyboardKey.Up, KeyboardKey.Down, KeyboardKey.Left, KeyboardKey.Right);

        var input = GameApp.ReadInput(platform);

        Assert.Equal(0f, input.MoveForward);
        Assert.Equal(0f, input.MoveRight);
        Assert.False(input.Jump);
    }

    [Fact(DisplayName = "ReadInput покрывает ветки для S/Down и A/Left")]
    public void ReadInput_CoversAlternativeDirectionBranches()
    {
        var platformSAndA = new FakeGamePlatform();
        platformSAndA.SetDownKeys(KeyboardKey.S, KeyboardKey.A);
        var inputSAndA = GameApp.ReadInput(platformSAndA);
        Assert.Equal(-1f, inputSAndA.MoveForward);
        Assert.Equal(-1f, inputSAndA.MoveRight);

        var platformDownAndLeft = new FakeGamePlatform();
        platformDownAndLeft.SetDownKeys(KeyboardKey.Down, KeyboardKey.Left);
        var inputDownAndLeft = GameApp.ReadInput(platformDownAndLeft);
        Assert.Equal(-1f, inputDownAndLeft.MoveForward);
        Assert.Equal(-1f, inputDownAndLeft.MoveRight);
    }

    [Fact(DisplayName = "SelectHotbarIndex выбирает слот по цифре")]
    public void SelectHotbarIndex_ChangesByNumberKey()
    {
        var platform = new FakeGamePlatform();
        platform.SetPressedKeys(KeyboardKey.Two);

        var index = GameApp.SelectHotbarIndex(0, platform, hotbarLength: 2);

        Assert.Equal(1, index);
    }

    [Fact(DisplayName = "На старте включается fullscreen по умолчанию")]
    public void Run_EnablesFullscreenByDefault()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, true);

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.True(platform.ToggleFullscreenCalled);
        Assert.True(platform.IsFullscreen);
        Assert.True(platform.SetExitKeyCalled);
        Assert.True(platform.LoadUiFontCalled);
        Assert.True(platform.UnloadUiFontCalled);
    }

    [Fact(DisplayName = "RunAutoCapture делает два скриншота без меню и закрывает окно")]
    public void RunAutoCapture_SavesTwoScreenshots()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autocap-{Guid.NewGuid():N}");
        var platform = new FakeGamePlatform();
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 96, height: 32, depth: 96, chunkSize: config.ChunkSize, seed: config.WorldSeed);
        var app = new GameApp(config, platform, world);

        app.RunAutoCapture(outputDir);

        Assert.Equal(3, platform.SavedScreenshots.Count);
        Assert.All(platform.SavedScreenshots, screenshot => Assert.StartsWith(outputDir, screenshot, StringComparison.Ordinal));
        Assert.Contains(platform.SavedScreenshots, s => s.EndsWith("autocap-warmup.png", StringComparison.Ordinal));
        Assert.Contains(platform.SavedScreenshots, s => s.EndsWith("autocap-1.png", StringComparison.Ordinal));
        Assert.Contains(platform.SavedScreenshots, s => s.EndsWith("autocap-2.png", StringComparison.Ordinal));
        Assert.True(platform.CloseWindowCalled);
        Assert.True(platform.DisableCursorCalled);
    }

    [Fact(DisplayName = "Кнопка Начать игру запускает игровой режим")]
    public void Run_StartButton_TransitionsToPlaying()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f));

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.True(platform.DisableCursorCalled);
        Assert.True(platform.DrawCubeCalls > 0);
    }

    [Fact(DisplayName = "В режиме Playing обрабатываются хотбар, ЛКМ/ПКМ и подсветка блока")]
    public void Run_Playing_HandlesHotbarAndBlockInteractions()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            RenderDistance = 2,
            DrawBlockWires = true,
            InteractionDistance = 10f
        };

        var world = new WorldMap(width: 20, height: 12, depth: 20, chunkSize: 8, seed: 0);
        world.SetBlock(10, 5, 8, BlockType.Stone); // hit + highlight
        world.SetBlock(11, 5, 9, (BlockType)999); // default-color ветка
        world.SetBlock(9, 5, 9, BlockType.Stone);
        world.SetBlock(10, 6, 9, BlockType.Stone);
        world.SetBlock(10, 4, 9, BlockType.Stone);
        world.SetBlock(10, 5, 10, BlockType.Stone);
        // (10,5,8) остается открытым с одной стороны для проверки поздних OR-веток видимости.

        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(
            mousePosition: new Vector2(0f, 0f),
            rightMousePressed: true,
            pressedKeys: [KeyboardKey.Two]);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f));

        var app = new GameApp(config, platform, world);
        app.Run();

        Assert.False(platform.ToggleFullscreenCalled);
        Assert.True(platform.DrawCubeWiresCalls > 0);
        Assert.True(platform.DrawLineCalls > 0);
        Assert.NotEqual(BlockType.Air, world.GetBlock(11, 5, 9));
    }

    [Fact(DisplayName = "BlockCenterIntersectsPlayer покрывается при правом клике вблизи игрока")]
    public void Run_RightClickNearPlayer_InvokesPlayerIntersectionPath()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            RenderDistance = 4,
            InteractionDistance = 8f
        };

        var world = new WorldMap(width: 21, height: 12, depth: 21, chunkSize: 8, seed: 0);
        world.SetBlock(10, 5, 10, BlockType.Air);
        world.SetBlock(10, 5, 9, BlockType.Air); // previous cell будет рядом с игроком
        world.SetBlock(10, 5, 8, BlockType.Stone);

        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f), rightMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f));

        var app = new GameApp(config, platform, world);
        app.Run();

        Assert.Equal(BlockType.Dirt, world.GetBlock(10, 5, 9));
        Assert.True(platform.DrawCubeCalls > 0);
        Assert.NotEmpty(platform.DrawnCubeWires);
    }

    [Fact(DisplayName = "Подсветка блока рисуется на грани попадания, а не по центру всего блока")]
    public void Run_BlockHighlight_UsesRaycastHitCoordinates()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            InteractionDistance = 6.5f
        };

        var world = new WorldMap(width: 21, height: 12, depth: 21, chunkSize: 8, seed: 0);
        world.SetBlock(10, 5, 10, BlockType.Air);
        world.SetBlock(10, 5, 9, BlockType.Air);
        world.SetBlock(10, 5, 8, BlockType.Stone);

        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true); // Start
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f)); // Playing frame with highlight
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f)); // Extra frame

        var app = new GameApp(config, platform, world);
        app.Run();

        var highlight = platform.DrawnCubeWires.FirstOrDefault(c =>
            (Math.Abs(c.Width - 0.04f) < 0.0001f)
            || (Math.Abs(c.Height - 0.04f) < 0.0001f)
            || (Math.Abs(c.Length - 0.04f) < 0.0001f));
        Assert.NotNull(highlight);
        Assert.True(MathF.Abs(highlight.Position.X - 10.5f) < 0.001f);
        Assert.True(MathF.Abs(highlight.Position.Y - 5.5f) < 0.001f);
        Assert.True(MathF.Abs(highlight.Position.Z - 9.01f) < 0.001f);
        Assert.Equal(Color.Yellow.R, highlight.Color.R);
        Assert.Equal(Color.Yellow.G, highlight.Color.G);
        Assert.Equal(Color.Yellow.B, highlight.Color.B);
    }

    [Fact(DisplayName = "Подсветка боковой грани по X использует тонкую толщину по ширине")]
    public void DrawHitFaceHighlight_XFace_UsesThinWidth()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false },
            platform,
            new WorldMap(width: 4, height: 4, depth: 4, chunkSize: 4, seed: 0));

        var method = typeof(GameApp).GetMethod("DrawHitFaceHighlight", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var hit = new BlockRaycastHit(1, 1, 1, 0, 1, 1);
        method!.Invoke(app, [hit, new Vector3(1f, 0f, 0f)]);

        var highlight = platform.DrawnCubeWires.Single();
        Assert.True(MathF.Abs(highlight.Width - 0.04f) < 0.0001f);
        Assert.True(MathF.Abs(highlight.Height - 1.02f) < 0.0001f);
        Assert.True(MathF.Abs(highlight.Length - 1.02f) < 0.0001f);
    }

    [Fact(DisplayName = "TryGetHitFaceNormal отклоняет скачок previous более чем на 1 блок по любой оси")]
    public void TryGetHitFaceNormal_RejectsOutOfRangePreviousDelta()
    {
        var method = typeof(GameApp).GetMethod("TryGetHitFaceNormal", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var cases = new[]
        {
            new BlockRaycastHit(10, 10, 10, 12, 10, 10),
            new BlockRaycastHit(10, 10, 10, 10, 12, 10),
            new BlockRaycastHit(10, 10, 10, 10, 10, 12)
        };

        foreach (var testHit in cases)
        {
            object[] args = [testHit, null!];
            var ok = (bool)method!.Invoke(null, args)!;
            Assert.False(ok);
            var outNormal = (Vector3)args[1];
            Assert.Equal(Vector3.Zero, outNormal);
        }
    }

    [Fact(DisplayName = "Кнопка fullscreen в меню переключает режим")]
    public void Run_MenuFullscreenButton_TogglesMode()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 388f), leftMousePressed: true);

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.False(platform.IsFullscreen);
    }

    [Fact(DisplayName = "Меню применяет графические переключатели и их видно в тексте UI")]
    public void Run_MenuGraphicsButtons_AffectRuntimeSettings()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false
        };

        var world = new WorldMap(width: 96, height: 32, depth: 96, chunkSize: 16, seed: 777);
        var platform = new FakeGamePlatform();

        platform.EnqueueWindowShouldClose(false, false, false, false, false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 500f), leftMousePressed: true); // Графика
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 570f), leftMousePressed: true); // Туман
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 640f), leftMousePressed: true); // Контуры рельефа
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true); // Старт
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f)); // Playing кадр
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f), pressedKeys: [KeyboardKey.Escape]); // Пауза

        var app = new GameApp(config, platform, world);
        app.Run();

        Assert.Contains(platform.DrawnUiTexts, t => t.Contains("Графика: Высокая", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, t => t.Contains("Туман: Выкл", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, t => t.Contains("Контуры рельефа: Выкл", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, t => t.Contains("Render:", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Клики вне кнопок меню не выполняют действий")]
    public void Run_MenuOutsideClicks_DoNotTriggerActions()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(200f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(1080f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 200f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 580f), leftMousePressed: true);

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.False(platform.DisableCursorCalled);
        Assert.True(platform.CloseWindowCalled);
    }

    [Fact(DisplayName = "Кнопка Выход закрывает игру из меню")]
    public void Run_ExitButton_ClosesFromMenu()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 450f), leftMousePressed: true);

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.True(platform.CloseWindowCalled);
        Assert.True(platform.DrawRectangleCalls > 0);
    }

    [Fact(DisplayName = "ESC в игре открывает паузу")]
    public void Run_EscapeInGame_OpensPauseMenu()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, true);

        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f), pressedKeys: [KeyboardKey.Escape]);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f));

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 6, height: 4, depth: 6));
        app.Run();

        Assert.True(platform.DisableCursorCalled);
        Assert.True(platform.EnableCursorCalled);
        Assert.True(platform.DrawRectangleCalls >= 4);
    }

    [Fact(DisplayName = "В игре клавиша V переключает режим камеры, и HUD показывает 3-е лицо")]
    public void Run_ToggleCameraMode_KeyV_UpdatesHudLabel()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, false, true);

        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true); // Start
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f), pressedKeys: [KeyboardKey.V]); // Toggle camera
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f)); // Render frame with new mode
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f));

        var app = new GameApp(new GameConfig { FullscreenByDefault = false }, platform, new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 777));
        app.Run();

        Assert.Contains(platform.DrawnUiTexts, t => t.Contains("Камера: 3-е лицо", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "ResolveUiFontPath возвращает пустую строку, если пути не существуют")]
    public void ResolveUiFontPath_ReturnsEmpty_WhenNoFilesExist()
    {
        var result = GameApp.ResolveUiFontPath(
        [
            "/tmp/aig-missing-font-1.ttf",
            "/tmp/aig-missing-font-2.ttf"
        ]);

        Assert.Equal(string.Empty, result);
    }

    [Fact(DisplayName = "GetBlockName возвращает fallback для неизвестного типа")]
    public void GetBlockName_ReturnsDefault_ForUnknown()
    {
        Assert.Equal("Блок", GameApp.GetBlockName((BlockType)999));
    }

    [Fact(DisplayName = "GetQualityName корректно возвращает все подписи пресетов")]
    public void GetQualityName_ReturnsAllLabels()
    {
        Assert.Equal("Низкая", GameApp.GetQualityName(GraphicsQuality.Low));
        Assert.Equal("Средняя", GameApp.GetQualityName(GraphicsQuality.Medium));
        Assert.Equal("Высокая", GameApp.GetQualityName(GraphicsQuality.High));
    }

    [Fact(DisplayName = "Пустой конструктор GameApp создаёт экземпляр")]
    public void GameApp_DefaultConstructor_CreatesInstance()
    {
        var app = new GameApp();
        Assert.NotNull(app);
    }

    private sealed class DelegatingRunner(Action action) : IGameRunner
    {
        public void Run() => action();
    }
}
