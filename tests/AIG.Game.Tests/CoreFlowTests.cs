using System.Numerics;
using AIG.Game;
using AIG.Game.Config;
using AIG.Game.Core;
using AIG.Game.Tests.Fakes;
using AIG.Game.World;
using Raylib_cs;

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

        var world = new WorldMap(width: 20, height: 12, depth: 20, chunkSize: 8, seed: 0);
        world.SetBlock(10, 5, 9, BlockType.Stone); // previous cell будет рядом с игроком

        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f), rightMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f));

        var app = new GameApp(config, platform, world);
        app.Run();

        Assert.Equal(BlockType.Stone, world.GetBlock(10, 5, 9));
        Assert.True(platform.DrawCubeWiresCalls > 0);
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
