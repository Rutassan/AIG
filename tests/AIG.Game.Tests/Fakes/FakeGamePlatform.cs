using System.Numerics;
using AIG.Game.Core;
using Raylib_cs;

namespace AIG.Game.Tests.Fakes;

internal sealed class FakeGamePlatform : IGamePlatform
{
    private sealed record FrameInput(
        Vector2 MousePosition,
        bool LeftMousePressed,
        bool RightMousePressed,
        KeyboardKey[] DownKeys,
        KeyboardKey[] PressedKeys,
        Vector2 MouseDelta
    );

    private readonly Queue<bool> _windowShouldCloseSequence = new();
    private readonly Queue<FrameInput> _frameInputs = new();
    private readonly HashSet<KeyboardKey> _downKeys = new();
    private readonly HashSet<KeyboardKey> _pressedKeys = new();
    private readonly List<string> _uiTexts = [];

    public int DrawCubeCalls { get; private set; }
    public int DrawCubeWiresCalls { get; private set; }
    public int DrawTextCalls { get; private set; }
    public int DrawLineCalls { get; private set; }
    public int DrawRectangleCalls { get; private set; }
    public int BeginDrawingCalls { get; private set; }
    public int EndDrawingCalls { get; private set; }
    public bool DisableCursorCalled { get; private set; }
    public bool EnableCursorCalled { get; private set; }
    public bool InitWindowCalled { get; private set; }
    public bool CloseWindowCalled { get; private set; }
    public bool SetExitKeyCalled { get; private set; }
    public bool ToggleFullscreenCalled { get; private set; }
    public bool IsFullscreen { get; private set; }
    public bool LoadUiFontCalled { get; private set; }
    public bool UnloadUiFontCalled { get; private set; }
    public int SetTargetFpsValue { get; private set; }
    public KeyboardKey ExitKey { get; private set; } = KeyboardKey.Null;
    public Vector2 MouseDelta { get; set; }
    public Vector2 MousePosition { get; set; }
    public bool LeftMousePressed { get; set; }
    public bool RightMousePressed { get; set; }
    public float FrameTime { get; set; } = 1f / 60f;
    public int ScreenWidth { get; set; } = 1280;
    public int ScreenHeight { get; set; } = 720;
    public int Fps { get; set; } = 120;
    public IReadOnlyList<string> DrawnUiTexts => _uiTexts;

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

    public void EnqueueFrameInput(
        Vector2 mousePosition,
        bool leftMousePressed = false,
        bool rightMousePressed = false,
        KeyboardKey[]? downKeys = null,
        KeyboardKey[]? pressedKeys = null,
        Vector2? mouseDelta = null)
    {
        _frameInputs.Enqueue(new FrameInput(
            mousePosition,
            leftMousePressed,
            rightMousePressed,
            downKeys ?? [],
            pressedKeys ?? [],
            mouseDelta ?? Vector2.Zero));
    }

    public void SetConfigFlags(ConfigFlags flags)
    {
    }

    public void SetExitKey(KeyboardKey key)
    {
        SetExitKeyCalled = true;
        ExitKey = key;
    }

    public void ToggleFullscreen()
    {
        ToggleFullscreenCalled = true;
        IsFullscreen = !IsFullscreen;
    }

    public bool IsWindowFullscreen() => IsFullscreen;

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
        if (_frameInputs.Count > 0)
        {
            var nextInput = _frameInputs.Dequeue();

            MousePosition = nextInput.MousePosition;
            LeftMousePressed = nextInput.LeftMousePressed;
            RightMousePressed = nextInput.RightMousePressed;
            MouseDelta = nextInput.MouseDelta;

            _downKeys.Clear();
            foreach (var key in nextInput.DownKeys)
            {
                _downKeys.Add(key);
            }

            _pressedKeys.Clear();
            foreach (var key in nextInput.PressedKeys)
            {
                _pressedKeys.Add(key);
            }
        }
        else
        {
            LeftMousePressed = false;
            RightMousePressed = false;
            MouseDelta = Vector2.Zero;
            _downKeys.Clear();
            _pressedKeys.Clear();
        }

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

    public Vector2 GetMousePosition() => MousePosition;

    public bool IsMouseButtonPressed(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => LeftMousePressed,
            MouseButton.Right => RightMousePressed,
            _ => false
        };
    }

    public void BeginDrawing()
    {
        BeginDrawingCalls++;
    }

    public void LoadUiFont(string fontPath, int fontSize)
    {
        LoadUiFontCalled = true;
    }

    public void UnloadUiFont()
    {
        UnloadUiFontCalled = true;
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
        DrawLineCalls++;
    }

    public void DrawRectangle(int posX, int posY, int width, int height, Color color)
    {
        DrawRectangleCalls++;
    }

    public int GetFps() => Fps;

    public void DrawUiText(string text, Vector2 position, float fontSize, float spacing, Color color)
    {
        DrawTextCalls++;
        _uiTexts.Add(text);
    }

    public void DrawText(string text, int posX, int posY, int fontSize, Color color)
    {
        DrawTextCalls++;
    }

    public void EndDrawing()
    {
        EndDrawingCalls++;
    }
}
