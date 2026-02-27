using System.Numerics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Raylib_cs;

namespace AIG.Game.Core;

[ExcludeFromCodeCoverage]
public sealed class RaylibGamePlatform : IGamePlatform
{
    private const string UiCharset =
        " !\"#$%&'()*+,-./0123456789:;<=>?@" +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
        "abcdefghijklmnopqrstuvwxyz{|}~" +
        "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ" +
        "абвгдеёжзийклмнопрстуфхцчшщъыьэюя" +
        "№«»—–…";

    private Font _uiFont;
    private bool _hasUiFont;

    public void SetConfigFlags(ConfigFlags flags) => Raylib.SetConfigFlags(flags);
    public void SetExitKey(KeyboardKey key) => Raylib.SetExitKey(key);
    public void ToggleFullscreen() => Raylib.ToggleFullscreen();
    public bool IsWindowFullscreen() => Raylib.IsWindowFullscreen();
    public void InitWindow(int width, int height, string title) => Raylib.InitWindow(width, height, title);
    public void SetTargetFps(int fps) => Raylib.SetTargetFPS(fps);
    public void DisableCursor() => Raylib.DisableCursor();
    public void EnableCursor() => Raylib.EnableCursor();
    public void CloseWindow() => Raylib.CloseWindow();
    public bool WindowShouldClose() => Raylib.WindowShouldClose();
    public float GetFrameTime() => Raylib.GetFrameTime();
    public bool IsKeyDown(KeyboardKey key) => Raylib.IsKeyDown(key);
    public bool IsKeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(key);
    public Vector2 GetMouseDelta() => Raylib.GetMouseDelta();
    public Vector2 GetMousePosition() => Raylib.GetMousePosition();
    public bool IsMouseButtonPressed(MouseButton button) => Raylib.IsMouseButtonPressed(button);
    public void LoadUiFont(string fontPath, int fontSize)
    {
        if (!File.Exists(fontPath))
        {
            _hasUiFont = false;
            return;
        }

        var codepoints = BuildCodepoints(UiCharset);
        _uiFont = Raylib.LoadFontEx(fontPath, fontSize, codepoints, codepoints.Length);
        Raylib.SetTextureFilter(_uiFont.Texture, TextureFilter.Bilinear);
        _hasUiFont = true;
    }

    public void UnloadUiFont()
    {
        if (!_hasUiFont)
        {
            return;
        }

        Raylib.UnloadFont(_uiFont);
        _hasUiFont = false;
    }

    public void BeginDrawing() => Raylib.BeginDrawing();
    public void ClearBackground(Color color) => Raylib.ClearBackground(color);
    public void BeginMode3D(Camera3D camera) => Raylib.BeginMode3D(camera);
    public void EndMode3D() => Raylib.EndMode3D();
    public void DrawCube(Vector3 position, float width, float height, float length, Color color) => Raylib.DrawCube(position, width, height, length, color);
    public void DrawCubeWires(Vector3 position, float width, float height, float length, Color color) => Raylib.DrawCubeWires(position, width, height, length, color);
    public int GetScreenWidth() => Raylib.GetScreenWidth();
    public int GetScreenHeight() => Raylib.GetScreenHeight();
    public void DrawLine(int startPosX, int startPosY, int endPosX, int endPosY, Color color) => Raylib.DrawLine(startPosX, startPosY, endPosX, endPosY, color);
    public void DrawRectangle(int posX, int posY, int width, int height, Color color) => Raylib.DrawRectangle(posX, posY, width, height, color);
    public int GetFps() => Raylib.GetFPS();
    public void DrawUiText(string text, Vector2 position, float fontSize, float spacing, Color color)
    {
        if (_hasUiFont)
        {
            Raylib.DrawTextEx(_uiFont, text, position, fontSize, spacing, color);
            return;
        }

        Raylib.DrawText(text, (int)position.X, (int)position.Y, (int)fontSize, color);
    }

    public void DrawText(string text, int posX, int posY, int fontSize, Color color) => Raylib.DrawText(text, posX, posY, fontSize, color);
    public void TakeScreenshot(string filePath)
    {
        var image = Raylib.LoadImageFromScreen();
        Raylib.ExportImage(image, filePath);
        Raylib.UnloadImage(image);
    }
    public void EndDrawing() => Raylib.EndDrawing();

    private static int[] BuildCodepoints(string value)
    {
        var unique = new HashSet<int>();
        foreach (var rune in value.EnumerateRunes())
        {
            unique.Add(rune.Value);
        }

        var result = new int[unique.Count];
        unique.CopyTo(result);
        return result;
    }
}
