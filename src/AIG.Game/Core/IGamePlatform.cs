using System.Numerics;
using Raylib_cs;

namespace AIG.Game.Core;

public interface IGamePlatform
{
    void SetConfigFlags(ConfigFlags flags);
    void InitWindow(int width, int height, string title);
    void SetTargetFps(int fps);
    void DisableCursor();
    void EnableCursor();
    void CloseWindow();
    bool WindowShouldClose();
    float GetFrameTime();
    bool IsKeyDown(KeyboardKey key);
    bool IsKeyPressed(KeyboardKey key);
    Vector2 GetMouseDelta();
    void BeginDrawing();
    void ClearBackground(Color color);
    void BeginMode3D(Camera3D camera);
    void EndMode3D();
    void DrawCube(Vector3 position, float width, float height, float length, Color color);
    void DrawCubeWires(Vector3 position, float width, float height, float length, Color color);
    int GetScreenWidth();
    int GetScreenHeight();
    void DrawLine(int startPosX, int startPosY, int endPosX, int endPosY, Color color);
    void DrawRectangle(int posX, int posY, int width, int height, Color color);
    int GetFps();
    void DrawText(string text, int posX, int posY, int fontSize, Color color);
    void EndDrawing();
}
