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

    [Fact(DisplayName = "ReadInput учитывает стрелки вниз и вправо при отпущенных WASD")]
    public void ReadInput_UsesArrowDownAndRight_WhenWASDNotPressed()
    {
        var platform = new FakeGamePlatform();
        platform.SetDownKeys(KeyboardKey.Down, KeyboardKey.Right);

        var input = GameApp.ReadInput(platform);

        Assert.Equal(-1f, input.MoveForward);
        Assert.Equal(1f, input.MoveRight);
    }

    [Fact(DisplayName = "ReadInput учитывает S и A через короткое замыкание OR")]
    public void ReadInput_UsesSAndA_WhenPressed()
    {
        var platform = new FakeGamePlatform();
        platform.SetDownKeys(KeyboardKey.S, KeyboardKey.A);

        var input = GameApp.ReadInput(platform);

        Assert.Equal(-1f, input.MoveForward);
        Assert.Equal(-1f, input.MoveRight);
    }

    [Fact(DisplayName = "На старте показывается меню и мир не рендерится до нажатия кнопки")]
    public void Run_StartsInMainMenu()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, true);

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.True(platform.SetExitKeyCalled);
        Assert.Equal(KeyboardKey.Null, platform.ExitKey);
        Assert.True(platform.LoadUiFontCalled);
        Assert.True(platform.UnloadUiFontCalled);
        Assert.True(platform.InitWindowCalled);
        Assert.Equal(0, platform.DrawCubeCalls);
        Assert.Equal(0, platform.DrawLineCalls);
        Assert.True(platform.DrawRectangleCalls > 0);
        Assert.True(platform.DrawTextCalls > 0);
    }

    [Fact(DisplayName = "Кнопка Начать игру запускает игровой режим")]
    public void Run_StartButton_TransitionsToPlaying()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.True(platform.DisableCursorCalled);
        Assert.True(platform.DrawCubeCalls > 0);
        Assert.True(platform.DrawLineCalls > 0);
    }

    [Fact(DisplayName = "После старта выполняется апдейт игрока в игровом состоянии")]
    public void Run_PlayingState_UpdatesPlayerPosition()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f), downKeys: [KeyboardKey.W]);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f));

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 8, height: 4, depth: 8));
        app.Run();

        var posTexts = platform.DrawnUiTexts.Where(t => t.StartsWith("Pos:", StringComparison.Ordinal)).ToList();
        Assert.True(posTexts.Count >= 2);
        Assert.NotEqual(posTexts[0], posTexts[1]);
    }

    [Fact(DisplayName = "Кнопка Выход закрывает игру из главного меню")]
    public void Run_ExitButton_ClosesFromMenu()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 395f), leftMousePressed: true);

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.True(platform.CloseWindowCalled);
        Assert.Equal(0, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "Клики вне кнопок меню не меняют состояние")]
    public void Run_MenuClickOutsideButtons_DoesNotStartOrExit()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(300f, 320f), leftMousePressed: true); // левее кнопки
        platform.EnqueueFrameInput(mousePosition: new Vector2(980f, 320f), leftMousePressed: true); // правее кнопки
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 250f), leftMousePressed: true); // выше кнопки
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 500f), leftMousePressed: true); // ниже кнопки

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.False(platform.DisableCursorCalled);
        Assert.Equal(0, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "ESC в игре открывает паузу, и игровой HUD перестает рисовать прицел")]
    public void Run_EscapeInGame_OpensPauseMenuAndPausesHud()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, true);

        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f), pressedKeys: [KeyboardKey.Escape]);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f));

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.True(platform.DisableCursorCalled);
        Assert.True(platform.EnableCursorCalled);
        Assert.Equal(2, platform.DrawLineCalls);
        Assert.True(platform.DrawRectangleCalls >= 4);
    }

    [Fact(DisplayName = "Рендер мира использует default-цвет для неизвестного типа блока")]
    public void Run_DrawWorld_UsesDefaultBlockColorForUnknownType()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);

        var world = new WorldMap(width: 3, height: 4, depth: 3);
        world.SetBlock(1, 3, 1, (BlockType)999);

        var app = new GameApp(new GameConfig(), platform, world);
        app.Run();

        Assert.True(platform.DrawCubeCalls > 0);
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
