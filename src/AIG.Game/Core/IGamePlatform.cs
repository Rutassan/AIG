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
    float AmbientLiftStrength,
    float HazeStrength,
    float MaterialShadowStrength,
    float HorizonDepthStrength,
    float FoliageTranslucencyStrength,
    float SecondaryBounceStrength,
    float DistanceMaterialStrength,
    float SkyResponseStrength,
    float FarGradientStrength,
    float ShadowContourStrength,
    float AtmosphereGradientStrength,
    float DistanceShadowLiftStrength,
    float SkyContourStrength,
    float DistantSilhouetteStrength,
    float AtmosphericContourStrength,
    float ReliefBridgeStrength,
    float ShadowHazeFusionStrength,
    float LightPlasticityStrength,
    float FarReadabilityStrength,
    float FinalCohesionStrength,
    float ViewMaterialStrength,
    float ShadowCascadeBlendStrength,
    float FarWorldCohesionStrength);

[ExcludeFromCodeCoverage]
public readonly record struct WorldShadowPassSettings(
    bool Enabled,
    int NearResolution,
    int FarResolution,
    float NearFilterRadius,
    float FarFilterRadius,
    Vector3 NearOrigin,
    Vector3 NearRight,
    Vector3 NearUp,
    Vector3 NearForward,
    float NearHalfWidth,
    float NearHalfHeight,
    float NearHalfDepth,
    Vector3 FarOrigin,
    Vector3 FarRight,
    Vector3 FarUp,
    Vector3 FarForward,
    float FarHalfWidth,
    float FarHalfHeight,
    float FarHalfDepth,
    float NearDistance,
    float FarDistance,
    float FarProxyStartDistance,
    float FarProxyEndDistance,
    float FarProxyStrength,
    float CascadeBlendWidth,
    float Bias,
    float SlopeBiasStrength,
    float Strength);

[ExcludeFromCodeCoverage]
public readonly record struct SkyPassSettings(
    Color TopColor,
    Color MidColor,
    Color HorizonColor,
    Color GlowColor,
    int HorizonY,
    float CloudStrength,
    float RidgeStrength);

[ExcludeFromCodeCoverage]
public readonly record struct ScreenSpacePassSettings(
    Color FogColor,
    int HorizonY,
    float Strength,
    float OverlayAlpha,
    bool DeviceOpen);

[ExcludeFromCodeCoverage]
public readonly record struct SelectionPassSettings(
    Color FillColor,
    Color OutlineColor,
    Color CoolOutlineColor,
    Color WarmOutlineColor,
    Color OuterCoolOutlineColor,
    float Thickness,
    float Size);

[ExcludeFromCodeCoverage]
public readonly record struct HeldBlockPassSettings(
    BlockType Block,
    Color BaseColor,
    Color ShadowColor,
    Color AccentColor,
    Color EdgeColor,
    Color CoolFacetColor,
    Color WarmRimColor,
    Color WarmWireColor,
    Color CoolWireColor);

[ExcludeFromCodeCoverage]
public readonly record struct ObjectPassSettings(
    Color ShadowColor,
    Color HighlightColor,
    Color WarmRimColor,
    float GroundShadowOffset,
    float GroundShadowAlpha,
    float ContactShadowAlpha);

[ExcludeFromCodeCoverage]
public readonly record struct FinalCompositePassSettings(
    Color FogColor,
    int HorizonY,
    float Strength,
    float OverlayAlpha,
    bool DeviceOpen,
    float FogBandStrength,
    float BloomStrength,
    float AtmosphereStrength,
    float VignetteStrength);

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
    void ConfigureWorldShadowPass(WorldShadowPassSettings settings, byte[] nearShadowMap, byte[] farShadowMap);
    void ConfigureSkyPass(SkyPassSettings settings);
    void ConfigureScreenSpacePass(ScreenSpacePassSettings settings);
    void ConfigureSelectionPass(SelectionPassSettings settings);
    void ConfigureHeldBlockPass(HeldBlockPassSettings settings);
    void ConfigureObjectPass(ObjectPassSettings settings);
    void ConfigureFinalCompositePass(FinalCompositePassSettings settings);
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
