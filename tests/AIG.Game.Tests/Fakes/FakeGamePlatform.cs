using System.Numerics;
using AIG.Game.Core;
using Raylib_cs;

namespace AIG.Game.Tests.Fakes;

internal sealed class FakeGamePlatform : IGamePlatform
{
    private readonly Queue<bool> _windowShouldCloseSequence = new();
    private readonly HashSet<KeyboardKey> _downKeys = new();
    private readonly HashSet<KeyboardKey> _pressedKeys = new();

    public int DrawCubeCalls { get; private set; }
    public int DrawCubeWiresCalls { get; private set; }
    public int DrawTextCalls { get; private set; }
    public int BeginDrawingCalls { get; private set; }
    public int EndDrawingCalls { get; private set; }
    public bool DisableCursorCalled { get; private set; }
    public bool EnableCursorCalled { get; private set; }
    public bool InitWindowCalled { get; private set; }
    public bool CloseWindowCalled { get; private set; }
    public int SetTargetFpsValue { get; private set; }
    public Vector2 MouseDelta { get; set; }
    public float FrameTime { get; set; } = 1f / 60f;
    public int ScreenWidth { get; set; } = 1280;
    public int ScreenHeight { get; set; } = 720;
    public int Fps { get; set; } = 120;

    public void EnqueueWindowShouldClose(params bool[] values)
    {
        foreach (var value in values)
        {
            _windowShouldCloseSequence.Enqueue(value);
        }
    }

    public void SetDownKeys(params KeyboardKey[] keys)
    {
        _downKeys.Clear();
        foreach (var key in keys)
        {
            _downKeys.Add(key);
        }
    }

    public void SetPressedKeys(params KeyboardKey[] keys)
    {
        _pressedKeys.Clear();
        foreach (var key in keys)
        {
            _pressedKeys.Add(key);
        }
    }

    public void SetConfigFlags(ConfigFlags flags)
    {
    }

    public void InitWindow(int width, int height, string title)
    {
        InitWindowCalled = true;
    }

    public void SetTargetFps(int fps)
    {
        SetTargetFpsValue = fps;
    }

    public void DisableCursor()
    {
        DisableCursorCalled = true;
    }

    public void EnableCursor()
    {
        EnableCursorCalled = true;
    }

    public void CloseWindow()
    {
        CloseWindowCalled = true;
    }

    public bool WindowShouldClose()
    {
        if (_windowShouldCloseSequence.Count == 0)
        {
            return true;
        }

        return _windowShouldCloseSequence.Dequeue();
    }

    public float GetFrameTime() => FrameTime;

    public bool IsKeyDown(KeyboardKey key) => _downKeys.Contains(key);

    public bool IsKeyPressed(KeyboardKey key) => _pressedKeys.Contains(key);

    public Vector2 GetMouseDelta() => MouseDelta;

    public void BeginDrawing()
    {
        BeginDrawingCalls++;
    }

    public void ClearBackground(Color color)
    {
    }

    public void BeginMode3D(Camera3D camera)
    {
    }

    public void EndMode3D()
    {
    }

    public void DrawCube(Vector3 position, float width, float height, float length, Color color)
    {
        DrawCubeCalls++;
    }

    public void DrawCubeWires(Vector3 position, float width, float height, float length, Color color)
    {
        DrawCubeWiresCalls++;
    }

    public int GetScreenWidth() => ScreenWidth;

    public int GetScreenHeight() => ScreenHeight;

    public void DrawLine(int startPosX, int startPosY, int endPosX, int endPosY, Color color)
    {
    }

    public void DrawRectangle(int posX, int posY, int width, int height, Color color)
    {
    }

    public int GetFps() => Fps;

    public void DrawText(string text, int posX, int posY, int fontSize, Color color)
    {
        DrawTextCalls++;
    }

    public void EndDrawing()
    {
        EndDrawingCalls++;
    }
}
