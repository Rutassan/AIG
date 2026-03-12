using System.Numerics;
using System.Diagnostics.CodeAnalysis;
using Raylib_cs;
using AIG.Game.World;

namespace AIG.Game.Core;

[ExcludeFromCodeCoverage]
public readonly record struct WorldMaterialPassSettings(
    Vector3 CameraPosition,
    Vector3 SunDirection,
    Color FogColor,
    float FogStart,
    float FogEnd,
    float Strength,
    float ShadowStrength,
    float AtmosphereStrength,
    float WarmLightStrength,
    float CoolShadowStrength,
    float ContrastStrength,
    float GlowStrength,
    float MaterialSeparationStrength,
    float ShadowDepthStrength,
    float SkyBlendStrength,
    float SunScatterStrength,
    float AmbientLiftStrength);

public interface IGamePlatform
{
    void SetConfigFlags(ConfigFlags flags);
    void SetExitKey(KeyboardKey key);
    void ToggleFullscreen();
    bool IsWindowFullscreen();
    void InitWindow(int width, int height, string title);
    void SetTargetFps(int fps);
    void DisableCursor();
    void EnableCursor();
    void WarmupWorldRenderResources();
    void CloseWindow();
    bool WindowShouldClose();
    float GetFrameTime();
    bool IsKeyDown(KeyboardKey key);
    bool IsKeyPressed(KeyboardKey key);
    Vector2 GetMouseDelta();
    Vector2 GetMousePosition();
    bool IsMouseButtonPressed(MouseButton button);
    void LoadUiFont(string fontPath, int fontSize);
    void UnloadUiFont();
    void BeginDrawing();
    void ClearBackground(Color color);
    void BeginMode3D(Camera3D camera);
    void EndMode3D();
    void DrawCube(Vector3 position, float width, float height, float length, Color color);
    void DrawCubeInstanced(IReadOnlyList<Matrix4x4> transforms, Color color);
    void ConfigureWorldMaterialPass(WorldMaterialPassSettings settings);
    void DrawTexturedBlockInstanced(BlockType block, IReadOnlyList<Matrix4x4> transforms);
    void DrawTexturedChunkMesh(int chunkX, int chunkZ, int revision, ChunkSurfaceMeshData mesh);
    void DrawCubeWires(Vector3 position, float width, float height, float length, Color color);
    int GetScreenWidth();
    int GetScreenHeight();
    void DrawLine(int startPosX, int startPosY, int endPosX, int endPosY, Color color);
    void DrawRectangle(int posX, int posY, int width, int height, Color color);
    int GetFps();
    void DrawUiText(string text, Vector2 position, float fontSize, float spacing, Color color);
    void DrawText(string text, int posX, int posY, int fontSize, Color color);
    void TakeScreenshot(string filePath);
    void EndDrawing();
}
