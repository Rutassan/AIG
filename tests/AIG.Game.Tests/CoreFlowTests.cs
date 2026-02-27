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

    [Fact(DisplayName = "Run выполняет один кадр, отрисовывает мир и закрывает окно")]
    public void Run_ExecutesFrameAndClosesResources()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, true);

        var world = new WorldMap(width: 2, height: 3, depth: 2);
        world.SetBlock(1, 2, 1, (BlockType)999);

        var app = new GameApp(new GameConfig(), platform, world);
        app.Run();

        Assert.True(platform.InitWindowCalled);
        Assert.True(platform.DisableCursorCalled);
        Assert.True(platform.EnableCursorCalled);
        Assert.True(platform.CloseWindowCalled);
        Assert.Equal(120, platform.SetTargetFpsValue);
        Assert.Equal(1, platform.BeginDrawingCalls);
        Assert.Equal(1, platform.EndDrawingCalls);
        Assert.True(platform.DrawCubeCalls > 0);
        Assert.Equal(platform.DrawCubeCalls, platform.DrawCubeWiresCalls);
        Assert.True(platform.DrawTextCalls >= 2);
    }

    [Fact(DisplayName = "Run корректно завершает окно даже без входа в цикл")]
    public void Run_ClosesWhenWindowShouldCloseImmediately()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(true);

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.True(platform.EnableCursorCalled);
        Assert.True(platform.CloseWindowCalled);
        Assert.Equal(0, platform.BeginDrawingCalls);
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
