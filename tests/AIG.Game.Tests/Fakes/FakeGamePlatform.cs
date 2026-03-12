using System.Numerics;
using AIG.Game.Core;
using AIG.Game.World;
using Raylib_cs;

namespace AIG.Game.Tests.Fakes;

internal sealed class FakeGamePlatform : IGamePlatform
{
    internal sealed record CubeCall(Vector3 Position, float Width, float Height, float Length, Color Color);
    internal sealed record CubeWireCall(Vector3 Position, float Width, float Height, float Length, Color Color);
    internal sealed record RectangleCall(int X, int Y, int Width, int Height, Color Color);
    internal sealed record TexturedBlockCall(BlockType Block, int InstanceCount);
    internal sealed record TexturedChunkMeshCall(int ChunkX, int ChunkZ, int Revision, int TriangleCount);

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
    private readonly List<CubeCall> _cubeCalls = [];
    private readonly List<CubeWireCall> _cubeWireCalls = [];
    private readonly List<RectangleCall> _rectangleCalls = [];
    private readonly List<TexturedBlockCall> _texturedBlockCalls = [];
    private readonly List<TexturedChunkMeshCall> _texturedChunkMeshCalls = [];
    private readonly List<WorldMaterialPassSettings> _worldMaterialPassCalls = [];
    private readonly List<string> _screenshots = [];

    public int DrawCubeCalls { get; private set; }
    public int DrawCubeInstancedCalls { get; private set; }
    public int DrawCubeInstancedInstances { get; private set; }
    public int LegacyDrawCubeInstancedCalls { get; private set; }
    public int LegacyDrawCubeInstancedInstances { get; private set; }
    public int DrawTexturedBlockInstancedCalls { get; private set; }
    public int DrawTexturedBlockInstancedInstances { get; private set; }
    public int DrawTexturedChunkMeshCalls { get; private set; }
    public int ConfigureWorldMaterialPassCalls { get; private set; }
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
    public bool WarmupWorldRenderResourcesCalled { get; private set; }
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
    public IReadOnlyList<CubeCall> DrawnCubes => _cubeCalls;
    public IReadOnlyList<CubeWireCall> DrawnCubeWires => _cubeWireCalls;
    public IReadOnlyList<RectangleCall> DrawnRectangles => _rectangleCalls;
    public IReadOnlyList<TexturedBlockCall> DrawnTexturedBlocks => _texturedBlockCalls;
    public IReadOnlyList<TexturedChunkMeshCall> DrawnTexturedChunkMeshes => _texturedChunkMeshCalls;
    public IReadOnlyList<WorldMaterialPassSettings> WorldMaterialPasses => _worldMaterialPassCalls;
    public IReadOnlyList<string> SavedScreenshots => _screenshots;

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

    public void WarmupWorldRenderResources()
    {
        WarmupWorldRenderResourcesCalled = true;
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
        _cubeCalls.Add(new CubeCall(position, width, height, length, color));
    }

    public void DrawCubeInstanced(IReadOnlyList<Matrix4x4> transforms, Color color)
    {
        DrawCubeInstancedCalls++;
        DrawCubeInstancedInstances += transforms.Count;
        LegacyDrawCubeInstancedCalls++;
        LegacyDrawCubeInstancedInstances += transforms.Count;
        DrawCubeCalls += transforms.Count;
        foreach (var t in transforms)
        {
            _cubeCalls.Add(new CubeCall(new Vector3(t.M41, t.M42, t.M43), 1f, 1f, 1f, color));
        }
    }

    public void ConfigureWorldMaterialPass(WorldMaterialPassSettings settings)
    {
        ConfigureWorldMaterialPassCalls++;
        _worldMaterialPassCalls.Add(settings);
    }

    public void DrawTexturedBlockInstanced(BlockType block, IReadOnlyList<Matrix4x4> transforms)
    {
        DrawTexturedBlockInstancedCalls++;
        DrawTexturedBlockInstancedInstances += transforms.Count;
        DrawCubeInstancedCalls++;
        DrawCubeInstancedInstances += transforms.Count;
        DrawCubeCalls += transforms.Count;
        _texturedBlockCalls.Add(new TexturedBlockCall(block, transforms.Count));

        var color = block switch
        {
            BlockType.Grass => new Color(98, 144, 82, 255),
            BlockType.Dirt => new Color(148, 111, 76, 255),
            BlockType.Stone => new Color(134, 129, 121, 255),
            BlockType.Wood => new Color(132, 98, 61, 255),
            BlockType.Leaves => new Color(82, 130, 74, 255),
            _ => Color.White
        };

        foreach (var t in transforms)
        {
            _cubeCalls.Add(new CubeCall(new Vector3(t.M41, t.M42, t.M43), 1f, 1f, 1f, color));
        }
    }

    public void DrawTexturedChunkMesh(int chunkX, int chunkZ, int revision, ChunkSurfaceMeshData mesh)
    {
        DrawTexturedChunkMeshCalls++;
        _texturedChunkMeshCalls.Add(new TexturedChunkMeshCall(chunkX, chunkZ, revision, mesh.TriangleCount));
    }

    public void DrawCubeWires(Vector3 position, float width, float height, float length, Color color)
    {
        DrawCubeWiresCalls++;
        _cubeWireCalls.Add(new CubeWireCall(position, width, height, length, color));
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
        _rectangleCalls.Add(new RectangleCall(posX, posY, width, height, color));
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

    public void TakeScreenshot(string filePath)
    {
        _screenshots.Add(filePath);
    }

    public void EndDrawing()
    {
        EndDrawingCalls++;
    }
}
