using System.Numerics;
using System.Diagnostics.CodeAnalysis;
using Raylib_cs;

namespace AIG.Game.Core;

[ExcludeFromCodeCoverage]
public sealed class RaylibGamePlatform : IGamePlatform
{
    public void SetConfigFlags(ConfigFlags flags) => Raylib.SetConfigFlags(flags);
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
    public void DrawText(string text, int posX, int posY, int fontSize, Color color) => Raylib.DrawText(text, posX, posY, fontSize, color);
    public void EndDrawing() => Raylib.EndDrawing();
}
