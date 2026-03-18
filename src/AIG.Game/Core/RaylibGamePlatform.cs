using System.Numerics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Raylib_cs;
using AIG.Game.World;

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
    private Mesh _instancedCubeMesh;
    private Material _instancedCubeMaterial;
    private bool _hasInstancedCubeResources;
    private readonly Dictionary<BlockType, Mesh> _texturedBlockMeshes = new();
    private readonly Dictionary<(int ChunkX, int ChunkZ), (int Revision, Mesh Mesh)> _texturedChunkMeshes = new();
    private Material _texturedChunkMaterial;
    private bool _hasTexturedChunkMaterial;
    private Shader _worldAtlasShader;
    private bool _hasWorldAtlasShader;
    private int _worldAtlasCameraPosLoc = -1;
    private int _worldAtlasSunDirectionLoc = -1;
    private int _worldAtlasFogColorLoc = -1;
    private int _worldAtlasFogRangeLoc = -1;
    private int _worldAtlasStrengthLoc = -1;
    private int _worldAtlasShadowStrengthLoc = -1;
    private int _worldAtlasAtmosphereStrengthLoc = -1;
    private int _worldAtlasWarmLightStrengthLoc = -1;
    private int _worldAtlasCoolShadowStrengthLoc = -1;
    private int _worldAtlasContrastStrengthLoc = -1;
    private int _worldAtlasGlowStrengthLoc = -1;
    private int _worldAtlasMaterialSeparationStrengthLoc = -1;
    private int _worldAtlasShadowDepthStrengthLoc = -1;
    private int _worldAtlasSkyBlendStrengthLoc = -1;
    private int _worldAtlasSunScatterStrengthLoc = -1;
    private int _worldAtlasAmbientLiftStrengthLoc = -1;
    private int _worldAtlasHazeStrengthLoc = -1;
    private int _worldAtlasMaterialShadowStrengthLoc = -1;
    private int _worldAtlasHorizonDepthStrengthLoc = -1;
    private int _worldAtlasFoliageTranslucencyStrengthLoc = -1;
    private int _worldAtlasSecondaryBounceStrengthLoc = -1;
    private int _worldAtlasDistanceMaterialStrengthLoc = -1;
    private int _worldAtlasSkyResponseStrengthLoc = -1;
    private int _worldAtlasFarGradientStrengthLoc = -1;
    private int _worldAtlasShadowContourStrengthLoc = -1;
    private int _worldAtlasAtmosphereGradientStrengthLoc = -1;
    private int _worldAtlasDistanceShadowLiftStrengthLoc = -1;
    private int _worldAtlasSkyContourStrengthLoc = -1;
    private int _worldAtlasDistantSilhouetteStrengthLoc = -1;
    private int _worldAtlasAtmosphericContourStrengthLoc = -1;
    private int _worldAtlasReliefBridgeStrengthLoc = -1;
    private int _worldAtlasShadowHazeFusionStrengthLoc = -1;
    private int _worldAtlasLightPlasticityStrengthLoc = -1;
    private int _worldAtlasFarReadabilityStrengthLoc = -1;
    private int _worldAtlasFinalCohesionStrengthLoc = -1;
    private int _worldAtlasViewMaterialStrengthLoc = -1;
    private int _worldAtlasShadowCascadeBlendStrengthLoc = -1;
    private int _worldAtlasFarWorldCohesionStrengthLoc = -1;
    private int _worldAtlasShadowMapNearLoc = -1;
    private int _worldAtlasShadowMapFarLoc = -1;
    private int _worldAtlasShadowMapEnabledLoc = -1;
    private int _worldAtlasShadowNearOriginLoc = -1;
    private int _worldAtlasShadowNearRightLoc = -1;
    private int _worldAtlasShadowNearUpLoc = -1;
    private int _worldAtlasShadowNearForwardLoc = -1;
    private int _worldAtlasShadowNearHalfExtentsLoc = -1;
    private int _worldAtlasShadowFarOriginLoc = -1;
    private int _worldAtlasShadowFarRightLoc = -1;
    private int _worldAtlasShadowFarUpLoc = -1;
    private int _worldAtlasShadowFarForwardLoc = -1;
    private int _worldAtlasShadowFarHalfExtentsLoc = -1;
    private int _worldAtlasShadowDistanceRangeLoc = -1;
    private int _worldAtlasShadowFarProxyRangeLoc = -1;
    private int _worldAtlasShadowFarProxyStrengthLoc = -1;
    private int _worldAtlasShadowFilterRadiusLoc = -1;
    private int _worldAtlasShadowCascadeBlendWidthLoc = -1;
    private int _worldAtlasShadowMapBiasLoc = -1;
    private int _worldAtlasShadowSlopeBiasStrengthLoc = -1;
    private int _worldAtlasShadowMapStrengthLoc = -1;
    private WorldMaterialPassSettings _worldMaterialPassSettings;
    private bool _hasWorldMaterialPassSettings;
    private WorldShadowPassSettings _worldShadowPassSettings;
    private bool _hasWorldShadowPassSettings;
    private SkyPassSettings _skyPassSettings;
    private ScreenSpacePassSettings _screenSpacePassSettings;
    private SelectionPassSettings _selectionPassSettings;
    private HeldBlockPassSettings _heldBlockPassSettings;
    private ObjectPassSettings _objectPassSettings;
    private FinalCompositePassSettings _finalCompositePassSettings;
    private Texture2D _worldAtlasTexture;
    private bool _hasWorldAtlasTexture;
    private Texture2D _worldShadowNearTexture;
    private bool _hasWorldShadowNearTexture;
    private Texture2D _worldShadowFarTexture;
    private bool _hasWorldShadowFarTexture;

    public void SetConfigFlags(ConfigFlags flags) => Raylib.SetConfigFlags(flags);
    public void SetExitKey(KeyboardKey key) => Raylib.SetExitKey(key);
    public void ToggleFullscreen() => Raylib.ToggleFullscreen();
    public bool IsWindowFullscreen() => Raylib.IsWindowFullscreen();
    public void InitWindow(int width, int height, string title) => Raylib.InitWindow(width, height, title);
    public void SetTargetFps(int fps) => Raylib.SetTargetFPS(fps);
    public void DisableCursor() => Raylib.DisableCursor();
    public void EnableCursor() => Raylib.EnableCursor();
    public void WarmupWorldRenderResources()
    {
        EnsureWorldAtlasTexture();
        _ = EnsureTexturedBlockResources(BlockType.Grass, out _, out _);
        _ = EnsureTexturedBlockResources(BlockType.Dirt, out _, out _);
        _ = EnsureTexturedBlockResources(BlockType.Stone, out _, out _);
        _ = EnsureTexturedBlockResources(BlockType.Wood, out _, out _);
        _ = EnsureTexturedBlockResources(BlockType.Leaves, out _, out _);
    }

    public void CloseWindow()
    {
        ReleaseTexturedBlockResources();
        ReleaseInstancedCubeResources();
        Raylib.CloseWindow();
    }
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
    public void DrawCubeInstanced(IReadOnlyList<Matrix4x4> transforms, Color color)
    {
        if (transforms.Count == 0)
        {
            return;
        }

        // Fallback path: explicit cubes are visually stable across drivers and matrix layouts.
        for (var i = 0; i < transforms.Count; i++)
        {
            var t = transforms[i];
            var center = new Vector3(t.M41, t.M42, t.M43);
            Raylib.DrawCube(center, 1f, 1f, 1f, color);
        }
    }

    public void ConfigureWorldMaterialPass(WorldMaterialPassSettings settings)
    {
        _worldMaterialPassSettings = settings;
        _hasWorldMaterialPassSettings = true;
        EnsureWorldAtlasTexture();
        EnsureWorldMaterialShader();
        ApplyWorldMaterialPassSettings();
    }

    public void ConfigureWorldShadowPass(WorldShadowPassSettings settings, byte[] nearShadowMap, byte[] farShadowMap)
    {
        _worldShadowPassSettings = settings;
        _hasWorldShadowPassSettings = true;
        EnsureWorldMaterialShader();
        EnsureShadowTexture(ref _worldShadowNearTexture, ref _hasWorldShadowNearTexture, Math.Max(1, settings.NearResolution));
        EnsureShadowTexture(ref _worldShadowFarTexture, ref _hasWorldShadowFarTexture, Math.Max(1, settings.FarResolution));
        UpdateShadowTexture(_worldShadowNearTexture, nearShadowMap);
        UpdateShadowTexture(_worldShadowFarTexture, farShadowMap);
        ApplyWorldShadowPassSettings();
    }

    public void ConfigureSkyPass(SkyPassSettings settings)
    {
        _skyPassSettings = settings;
    }

    public void ConfigureScreenSpacePass(ScreenSpacePassSettings settings)
    {
        _screenSpacePassSettings = settings;
    }

    public void ConfigureSelectionPass(SelectionPassSettings settings)
    {
        _selectionPassSettings = settings;
    }

    public void ConfigureHeldBlockPass(HeldBlockPassSettings settings)
    {
        _heldBlockPassSettings = settings;
    }

    public void ConfigureObjectPass(ObjectPassSettings settings)
    {
        _objectPassSettings = settings;
    }

    public void ConfigureFinalCompositePass(FinalCompositePassSettings settings)
    {
        _finalCompositePassSettings = settings;
    }

    public void DrawTexturedBlockInstanced(BlockType block, IReadOnlyList<Matrix4x4> transforms)
    {
        if (transforms.Count == 0)
        {
            return;
        }

        if (!EnsureTexturedBlockResources(block, out var mesh, out var material))
        {
            DrawCubeInstanced(transforms, GetFallbackBlockColor(block));
            return;
        }

        for (var i = 0; i < transforms.Count; i++)
        {
            Raylib.DrawMesh(mesh, material, Matrix4x4.Transpose(transforms[i]));
        }
    }

    public void DrawTexturedChunkMesh(int chunkX, int chunkZ, int revision, ChunkSurfaceMeshData meshData)
    {
        if (meshData.IsEmpty)
        {
            return;
        }

        if (!EnsureTexturedChunkMeshResource(chunkX, chunkZ, revision, meshData, out var mesh, out var material))
        {
            return;
        }

        Raylib.DrawMesh(mesh, material, Matrix4x4.Identity);
    }

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

    private void EnsureInstancedCubeResources()
    {
        if (_hasInstancedCubeResources)
        {
            return;
        }

        _instancedCubeMesh = Raylib.GenMeshCube(1f, 1f, 1f);
        _instancedCubeMaterial = Raylib.LoadMaterialDefault();
        _hasInstancedCubeResources = true;
    }

    private void ReleaseInstancedCubeResources()
    {
        if (!_hasInstancedCubeResources)
        {
            return;
        }

        Raylib.UnloadMaterial(_instancedCubeMaterial);
        Raylib.UnloadMesh(_instancedCubeMesh);
        _hasInstancedCubeResources = false;
    }

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

    private bool EnsureTexturedBlockResources(BlockType block, out Mesh mesh, out Material material)
    {
        EnsureWorldAtlasTexture();
        EnsureWorldMaterialShader();

        EnsureTexturedChunkMaterial();

        if (_hasWorldAtlasTexture && _hasTexturedChunkMaterial && _texturedBlockMeshes.TryGetValue(block, out mesh))
        {
            material = _texturedChunkMaterial;
            return true;
        }

        if (!_hasWorldAtlasTexture || !_hasTexturedChunkMaterial)
        {
            mesh = default;
            material = default;
            return false;
        }

        var meshData = TexturedBlockMeshFactory.Build(block);
        mesh = new Mesh(meshData.VertexCount, meshData.TriangleCount);
        mesh.AllocVertices();
        mesh.AllocTexCoords();
        mesh.AllocNormals();
        mesh.AllocColors();
        mesh.AllocIndices();

        meshData.Vertices.AsSpan().CopyTo(mesh.VerticesAs<float>().Slice(0, meshData.Vertices.Length));
        meshData.TexCoords.AsSpan().CopyTo(mesh.TexCoordsAs<float>().Slice(0, meshData.TexCoords.Length));
        meshData.Normals.AsSpan().CopyTo(mesh.NormalsAs<float>().Slice(0, meshData.Normals.Length));
        meshData.Colors.AsSpan().CopyTo(mesh.ColorsAs<byte>().Slice(0, meshData.Colors.Length));
        meshData.Indices.AsSpan().CopyTo(mesh.IndicesAs<ushort>().Slice(0, meshData.Indices.Length));
        Raylib.UploadMesh(ref mesh, false);

        material = _texturedChunkMaterial;
        _texturedBlockMeshes[block] = mesh;
        return true;
    }

    private bool EnsureTexturedChunkMeshResource(int chunkX, int chunkZ, int revision, ChunkSurfaceMeshData meshData, out Mesh mesh, out Material material)
    {
        EnsureWorldAtlasTexture();
        EnsureWorldMaterialShader();
        EnsureTexturedChunkMaterial();

        if (!_hasWorldAtlasTexture || !_hasTexturedChunkMaterial)
        {
            mesh = default;
            material = default;
            return false;
        }

        var key = (chunkX, chunkZ);
        if (_texturedChunkMeshes.TryGetValue(key, out var cached) && cached.Revision == revision)
        {
            mesh = cached.Mesh;
            material = _texturedChunkMaterial;
            ApplyWorldShader(ref material);
            return true;
        }

        if (_texturedChunkMeshes.TryGetValue(key, out cached))
        {
            Raylib.UnloadMesh(cached.Mesh);
            _texturedChunkMeshes.Remove(key);
        }

        mesh = UploadMesh(meshData.Vertices, meshData.TexCoords, meshData.Normals, meshData.Colors, meshData.Indices, meshData.VertexCount, meshData.TriangleCount);
        _texturedChunkMeshes[key] = (revision, mesh);
        material = _texturedChunkMaterial;
        ApplyWorldShader(ref material);
        return true;
    }

    private void EnsureWorldAtlasTexture()
    {
        if (_hasWorldAtlasTexture)
        {
            return;
        }

        var atlasPath = ResolveAssetPath(WorldTextureAtlas.RelativePath);
        if (!File.Exists(atlasPath))
        {
            return;
        }

        _worldAtlasTexture = Raylib.LoadTexture(atlasPath);
        Raylib.SetTextureFilter(_worldAtlasTexture, TextureFilter.Point);
        _hasWorldAtlasTexture = true;
    }

    private void EnsureTexturedChunkMaterial()
    {
        if (_hasTexturedChunkMaterial || !_hasWorldAtlasTexture)
        {
            return;
        }

        _texturedChunkMaterial = Raylib.LoadMaterialDefault();
        Raylib.SetMaterialTexture(ref _texturedChunkMaterial, MaterialMapIndex.Albedo, _worldAtlasTexture);
        ApplyWorldShader(ref _texturedChunkMaterial);
        _hasTexturedChunkMaterial = true;
    }

    private void EnsureWorldMaterialShader()
    {
        if (_hasWorldAtlasShader)
        {
            return;
        }

        var vertexPath = ResolveAssetPath("assets/shaders/world_atlas.vs");
        var fragmentPath = ResolveAssetPath("assets/shaders/world_atlas.fs");
        if (!File.Exists(vertexPath) || !File.Exists(fragmentPath))
        {
            return;
        }

        _worldAtlasShader = Raylib.LoadShader(vertexPath, fragmentPath);
        if (_worldAtlasShader.Id <= 0)
        {
            return;
        }

        _worldAtlasCameraPosLoc = Raylib.GetShaderLocation(_worldAtlasShader, "cameraPos");
        _worldAtlasSunDirectionLoc = Raylib.GetShaderLocation(_worldAtlasShader, "sunDirection");
        _worldAtlasFogColorLoc = Raylib.GetShaderLocation(_worldAtlasShader, "fogColor");
        _worldAtlasFogRangeLoc = Raylib.GetShaderLocation(_worldAtlasShader, "fogRange");
        _worldAtlasStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shaderStrength");
        _worldAtlasShadowStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowStrength");
        _worldAtlasAtmosphereStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "atmosphereStrength");
        _worldAtlasWarmLightStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "warmLightStrength");
        _worldAtlasCoolShadowStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "coolShadowStrength");
        _worldAtlasContrastStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "contrastStrength");
        _worldAtlasGlowStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "glowStrength");
        _worldAtlasMaterialSeparationStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "materialSeparationStrength");
        _worldAtlasShadowDepthStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowDepthStrength");
        _worldAtlasSkyBlendStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "skyBlendStrength");
        _worldAtlasSunScatterStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "sunScatterStrength");
        _worldAtlasAmbientLiftStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "ambientLiftStrength");
        _worldAtlasHazeStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "hazeStrength");
        _worldAtlasMaterialShadowStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "materialShadowStrength");
        _worldAtlasHorizonDepthStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "horizonDepthStrength");
        _worldAtlasFoliageTranslucencyStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "foliageTranslucencyStrength");
        _worldAtlasSecondaryBounceStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "secondaryBounceStrength");
        _worldAtlasDistanceMaterialStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "distanceMaterialStrength");
        _worldAtlasSkyResponseStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "skyResponseStrength");
        _worldAtlasFarGradientStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "farGradientStrength");
        _worldAtlasShadowContourStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowContourStrength");
        _worldAtlasAtmosphereGradientStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "atmosphereGradientStrength");
        _worldAtlasDistanceShadowLiftStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "distanceShadowLiftStrength");
        _worldAtlasSkyContourStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "skyContourStrength");
        _worldAtlasDistantSilhouetteStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "distantSilhouetteStrength");
        _worldAtlasAtmosphericContourStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "atmosphericContourStrength");
        _worldAtlasReliefBridgeStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "reliefBridgeStrength");
        _worldAtlasShadowHazeFusionStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowHazeFusionStrength");
        _worldAtlasLightPlasticityStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "lightPlasticityStrength");
        _worldAtlasFarReadabilityStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "farReadabilityStrength");
        _worldAtlasFinalCohesionStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "finalCohesionStrength");
        _worldAtlasViewMaterialStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "viewMaterialStrength");
        _worldAtlasShadowCascadeBlendStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowCascadeBlendStrength");
        _worldAtlasFarWorldCohesionStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "farWorldCohesionStrength");
        _worldAtlasShadowMapNearLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowMapNear");
        _worldAtlasShadowMapFarLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowMapFar");
        _worldAtlasShadowMapEnabledLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowMapEnabled");
        _worldAtlasShadowNearOriginLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowNearOrigin");
        _worldAtlasShadowNearRightLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowNearRight");
        _worldAtlasShadowNearUpLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowNearUp");
        _worldAtlasShadowNearForwardLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowNearForward");
        _worldAtlasShadowNearHalfExtentsLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowNearHalfExtents");
        _worldAtlasShadowFarOriginLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowFarOrigin");
        _worldAtlasShadowFarRightLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowFarRight");
        _worldAtlasShadowFarUpLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowFarUp");
        _worldAtlasShadowFarForwardLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowFarForward");
        _worldAtlasShadowFarHalfExtentsLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowFarHalfExtents");
        _worldAtlasShadowDistanceRangeLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowDistanceRange");
        _worldAtlasShadowFarProxyRangeLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowFarProxyRange");
        _worldAtlasShadowFarProxyStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowFarProxyStrength");
        _worldAtlasShadowFilterRadiusLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowFilterRadius");
        _worldAtlasShadowCascadeBlendWidthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowCascadeBlendWidth");
        _worldAtlasShadowMapBiasLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowMapBias");
        _worldAtlasShadowSlopeBiasStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowSlopeBiasStrength");
        _worldAtlasShadowMapStrengthLoc = Raylib.GetShaderLocation(_worldAtlasShader, "shadowMapStrength");
        _hasWorldAtlasShader = true;
        ApplyWorldMaterialPassSettings();
        ApplyWorldShadowPassSettings();
    }

    private void ApplyWorldShader(ref Material material)
    {
        if (!_hasWorldAtlasShader)
        {
            return;
        }

        material.Shader = _worldAtlasShader;
    }

    private void ApplyWorldMaterialPassSettings()
    {
        if (!_hasWorldAtlasShader || !_hasWorldMaterialPassSettings)
        {
            return;
        }

        if (_worldAtlasCameraPosLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasCameraPosLoc, new[]
            {
                _worldMaterialPassSettings.CameraPosition.X,
                _worldMaterialPassSettings.CameraPosition.Y,
                _worldMaterialPassSettings.CameraPosition.Z
            }, ShaderUniformDataType.Vec3);
        }

        if (_worldAtlasSunDirectionLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasSunDirectionLoc, new[]
            {
                _worldMaterialPassSettings.SunDirection.X,
                _worldMaterialPassSettings.SunDirection.Y,
                _worldMaterialPassSettings.SunDirection.Z
            }, ShaderUniformDataType.Vec3);
        }

        if (_worldAtlasFogColorLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasFogColorLoc, new[]
            {
                _worldMaterialPassSettings.FogColor.R / 255f,
                _worldMaterialPassSettings.FogColor.G / 255f,
                _worldMaterialPassSettings.FogColor.B / 255f,
                _worldMaterialPassSettings.FogColor.A / 255f
            }, ShaderUniformDataType.Vec4);
        }

        if (_worldAtlasFogRangeLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasFogRangeLoc, new[]
            {
                _worldMaterialPassSettings.FogStart,
                _worldMaterialPassSettings.FogEnd
            }, ShaderUniformDataType.Vec2);
        }

        if (_worldAtlasStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasStrengthLoc, _worldMaterialPassSettings.Strength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasShadowStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowStrengthLoc, _worldMaterialPassSettings.ShadowStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasAtmosphereStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasAtmosphereStrengthLoc, _worldMaterialPassSettings.AtmosphereStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasWarmLightStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasWarmLightStrengthLoc, _worldMaterialPassSettings.WarmLightStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasCoolShadowStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasCoolShadowStrengthLoc, _worldMaterialPassSettings.CoolShadowStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasContrastStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasContrastStrengthLoc, _worldMaterialPassSettings.ContrastStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasGlowStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasGlowStrengthLoc, _worldMaterialPassSettings.GlowStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasMaterialSeparationStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasMaterialSeparationStrengthLoc, _worldMaterialPassSettings.MaterialSeparationStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasShadowDepthStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowDepthStrengthLoc, _worldMaterialPassSettings.ShadowDepthStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasSkyBlendStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasSkyBlendStrengthLoc, _worldMaterialPassSettings.SkyBlendStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasSunScatterStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasSunScatterStrengthLoc, _worldMaterialPassSettings.SunScatterStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasAmbientLiftStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasAmbientLiftStrengthLoc, _worldMaterialPassSettings.AmbientLiftStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasHazeStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasHazeStrengthLoc, _worldMaterialPassSettings.HazeStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasMaterialShadowStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasMaterialShadowStrengthLoc, _worldMaterialPassSettings.MaterialShadowStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasHorizonDepthStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasHorizonDepthStrengthLoc, _worldMaterialPassSettings.HorizonDepthStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasFoliageTranslucencyStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasFoliageTranslucencyStrengthLoc, _worldMaterialPassSettings.FoliageTranslucencyStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasSecondaryBounceStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasSecondaryBounceStrengthLoc, _worldMaterialPassSettings.SecondaryBounceStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasDistanceMaterialStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasDistanceMaterialStrengthLoc, _worldMaterialPassSettings.DistanceMaterialStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasSkyResponseStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasSkyResponseStrengthLoc, _worldMaterialPassSettings.SkyResponseStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasFarGradientStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasFarGradientStrengthLoc, _worldMaterialPassSettings.FarGradientStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasShadowContourStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowContourStrengthLoc, _worldMaterialPassSettings.ShadowContourStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasAtmosphereGradientStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasAtmosphereGradientStrengthLoc, _worldMaterialPassSettings.AtmosphereGradientStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasDistanceShadowLiftStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasDistanceShadowLiftStrengthLoc, _worldMaterialPassSettings.DistanceShadowLiftStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasSkyContourStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasSkyContourStrengthLoc, _worldMaterialPassSettings.SkyContourStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasDistantSilhouetteStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasDistantSilhouetteStrengthLoc, _worldMaterialPassSettings.DistantSilhouetteStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasAtmosphericContourStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasAtmosphericContourStrengthLoc, _worldMaterialPassSettings.AtmosphericContourStrength, ShaderUniformDataType.Float);
        }
        if (_worldAtlasReliefBridgeStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasReliefBridgeStrengthLoc, _worldMaterialPassSettings.ReliefBridgeStrength, ShaderUniformDataType.Float);
        }
        if (_worldAtlasShadowHazeFusionStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowHazeFusionStrengthLoc, _worldMaterialPassSettings.ShadowHazeFusionStrength, ShaderUniformDataType.Float);
        }
        if (_worldAtlasLightPlasticityStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasLightPlasticityStrengthLoc, _worldMaterialPassSettings.LightPlasticityStrength, ShaderUniformDataType.Float);
        }
        if (_worldAtlasFarReadabilityStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasFarReadabilityStrengthLoc, _worldMaterialPassSettings.FarReadabilityStrength, ShaderUniformDataType.Float);
        }
        if (_worldAtlasFinalCohesionStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasFinalCohesionStrengthLoc, _worldMaterialPassSettings.FinalCohesionStrength, ShaderUniformDataType.Float);
        }
        if (_worldAtlasViewMaterialStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasViewMaterialStrengthLoc, _worldMaterialPassSettings.ViewMaterialStrength, ShaderUniformDataType.Float);
        }
        if (_worldAtlasShadowCascadeBlendStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowCascadeBlendStrengthLoc, _worldMaterialPassSettings.ShadowCascadeBlendStrength, ShaderUniformDataType.Float);
        }
        if (_worldAtlasFarWorldCohesionStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasFarWorldCohesionStrengthLoc, _worldMaterialPassSettings.FarWorldCohesionStrength, ShaderUniformDataType.Float);
        }
    }

    private void ApplyWorldShadowPassSettings()
    {
        if (!_hasWorldAtlasShader || !_hasWorldShadowPassSettings)
        {
            return;
        }

        if (_hasWorldShadowNearTexture && _worldAtlasShadowMapNearLoc >= 0)
        {
            Raylib.SetShaderValueTexture(_worldAtlasShader, _worldAtlasShadowMapNearLoc, _worldShadowNearTexture);
        }

        if (_hasWorldShadowFarTexture && _worldAtlasShadowMapFarLoc >= 0)
        {
            Raylib.SetShaderValueTexture(_worldAtlasShader, _worldAtlasShadowMapFarLoc, _worldShadowFarTexture);
        }

        var enabled = _worldShadowPassSettings.Enabled ? 1f : 0f;
        if (_worldAtlasShadowMapEnabledLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowMapEnabledLoc, enabled, ShaderUniformDataType.Float);
        }

        if (_worldAtlasShadowNearOriginLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowNearOriginLoc, ToArray(_worldShadowPassSettings.NearOrigin), ShaderUniformDataType.Vec3);
        }

        if (_worldAtlasShadowNearRightLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowNearRightLoc, ToArray(_worldShadowPassSettings.NearRight), ShaderUniformDataType.Vec3);
        }

        if (_worldAtlasShadowNearUpLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowNearUpLoc, ToArray(_worldShadowPassSettings.NearUp), ShaderUniformDataType.Vec3);
        }

        if (_worldAtlasShadowNearForwardLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowNearForwardLoc, ToArray(_worldShadowPassSettings.NearForward), ShaderUniformDataType.Vec3);
        }

        if (_worldAtlasShadowNearHalfExtentsLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowNearHalfExtentsLoc, new[]
            {
                _worldShadowPassSettings.NearHalfWidth,
                _worldShadowPassSettings.NearHalfHeight,
                _worldShadowPassSettings.NearHalfDepth
            }, ShaderUniformDataType.Vec3);
        }

        if (_worldAtlasShadowFarOriginLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowFarOriginLoc, ToArray(_worldShadowPassSettings.FarOrigin), ShaderUniformDataType.Vec3);
        }

        if (_worldAtlasShadowFarRightLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowFarRightLoc, ToArray(_worldShadowPassSettings.FarRight), ShaderUniformDataType.Vec3);
        }

        if (_worldAtlasShadowFarUpLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowFarUpLoc, ToArray(_worldShadowPassSettings.FarUp), ShaderUniformDataType.Vec3);
        }

        if (_worldAtlasShadowFarForwardLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowFarForwardLoc, ToArray(_worldShadowPassSettings.FarForward), ShaderUniformDataType.Vec3);
        }

        if (_worldAtlasShadowFarHalfExtentsLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowFarHalfExtentsLoc, new[]
            {
                _worldShadowPassSettings.FarHalfWidth,
                _worldShadowPassSettings.FarHalfHeight,
                _worldShadowPassSettings.FarHalfDepth
            }, ShaderUniformDataType.Vec3);
        }

        if (_worldAtlasShadowDistanceRangeLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowDistanceRangeLoc, new[]
            {
                _worldShadowPassSettings.NearDistance,
                _worldShadowPassSettings.FarDistance
            }, ShaderUniformDataType.Vec2);
        }

        if (_worldAtlasShadowFarProxyRangeLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowFarProxyRangeLoc, new[]
            {
                _worldShadowPassSettings.FarProxyStartDistance,
                _worldShadowPassSettings.FarProxyEndDistance
            }, ShaderUniformDataType.Vec2);
        }

        if (_worldAtlasShadowFarProxyStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowFarProxyStrengthLoc, _worldShadowPassSettings.FarProxyStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasShadowFilterRadiusLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowFilterRadiusLoc, new[]
            {
                _worldShadowPassSettings.NearFilterRadius,
                _worldShadowPassSettings.FarFilterRadius
            }, ShaderUniformDataType.Vec2);
        }

        if (_worldAtlasShadowCascadeBlendWidthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowCascadeBlendWidthLoc, _worldShadowPassSettings.CascadeBlendWidth, ShaderUniformDataType.Float);
        }

        if (_worldAtlasShadowMapBiasLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowMapBiasLoc, _worldShadowPassSettings.Bias, ShaderUniformDataType.Float);
        }

        if (_worldAtlasShadowSlopeBiasStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowSlopeBiasStrengthLoc, _worldShadowPassSettings.SlopeBiasStrength, ShaderUniformDataType.Float);
        }

        if (_worldAtlasShadowMapStrengthLoc >= 0)
        {
            Raylib.SetShaderValue(_worldAtlasShader, _worldAtlasShadowMapStrengthLoc, _worldShadowPassSettings.Strength, ShaderUniformDataType.Float);
        }
    }

    private void ReleaseTexturedBlockResources()
    {
        foreach (var mesh in _texturedBlockMeshes.Values)
        {
            Raylib.UnloadMesh(mesh);
        }

        foreach (var mesh in _texturedChunkMeshes.Values)
        {
            Raylib.UnloadMesh(mesh.Mesh);
        }

        _texturedBlockMeshes.Clear();
        _texturedChunkMeshes.Clear();

        if (_hasTexturedChunkMaterial)
        {
            Raylib.UnloadMaterial(_texturedChunkMaterial);
            _hasTexturedChunkMaterial = false;
        }

        _hasWorldAtlasShader = false;
        _worldAtlasCameraPosLoc = -1;
        _worldAtlasSunDirectionLoc = -1;
        _worldAtlasFogColorLoc = -1;
        _worldAtlasFogRangeLoc = -1;
        _worldAtlasStrengthLoc = -1;
        _worldAtlasShadowStrengthLoc = -1;
        _worldAtlasAtmosphereStrengthLoc = -1;
        _worldAtlasWarmLightStrengthLoc = -1;
        _worldAtlasCoolShadowStrengthLoc = -1;
        _worldAtlasContrastStrengthLoc = -1;
        _worldAtlasGlowStrengthLoc = -1;
        _worldAtlasMaterialSeparationStrengthLoc = -1;
        _worldAtlasShadowDepthStrengthLoc = -1;
        _worldAtlasSkyBlendStrengthLoc = -1;
        _worldAtlasSunScatterStrengthLoc = -1;
        _worldAtlasAmbientLiftStrengthLoc = -1;
        _worldAtlasHazeStrengthLoc = -1;
        _worldAtlasMaterialShadowStrengthLoc = -1;
        _worldAtlasHorizonDepthStrengthLoc = -1;
        _worldAtlasFoliageTranslucencyStrengthLoc = -1;
        _worldAtlasSecondaryBounceStrengthLoc = -1;
        _worldAtlasDistanceMaterialStrengthLoc = -1;
        _worldAtlasSkyResponseStrengthLoc = -1;
        _worldAtlasFarGradientStrengthLoc = -1;
        _worldAtlasShadowContourStrengthLoc = -1;
        _worldAtlasAtmosphereGradientStrengthLoc = -1;
        _worldAtlasDistanceShadowLiftStrengthLoc = -1;
        _worldAtlasSkyContourStrengthLoc = -1;
        _worldAtlasDistantSilhouetteStrengthLoc = -1;
        _worldAtlasAtmosphericContourStrengthLoc = -1;
        _worldAtlasReliefBridgeStrengthLoc = -1;
        _worldAtlasShadowHazeFusionStrengthLoc = -1;
        _worldAtlasLightPlasticityStrengthLoc = -1;
        _worldAtlasFarReadabilityStrengthLoc = -1;
        _worldAtlasFinalCohesionStrengthLoc = -1;
        _worldAtlasViewMaterialStrengthLoc = -1;
        _worldAtlasShadowCascadeBlendStrengthLoc = -1;
        _worldAtlasFarWorldCohesionStrengthLoc = -1;
        _worldAtlasShadowMapNearLoc = -1;
        _worldAtlasShadowMapFarLoc = -1;
        _worldAtlasShadowMapEnabledLoc = -1;
        _worldAtlasShadowNearOriginLoc = -1;
        _worldAtlasShadowNearRightLoc = -1;
        _worldAtlasShadowNearUpLoc = -1;
        _worldAtlasShadowNearForwardLoc = -1;
        _worldAtlasShadowNearHalfExtentsLoc = -1;
        _worldAtlasShadowFarOriginLoc = -1;
        _worldAtlasShadowFarRightLoc = -1;
        _worldAtlasShadowFarUpLoc = -1;
        _worldAtlasShadowFarForwardLoc = -1;
        _worldAtlasShadowFarHalfExtentsLoc = -1;
        _worldAtlasShadowDistanceRangeLoc = -1;
        _worldAtlasShadowFarProxyRangeLoc = -1;
        _worldAtlasShadowFarProxyStrengthLoc = -1;
        _worldAtlasShadowFilterRadiusLoc = -1;
        _worldAtlasShadowCascadeBlendWidthLoc = -1;
        _worldAtlasShadowMapBiasLoc = -1;
        _worldAtlasShadowSlopeBiasStrengthLoc = -1;
        _worldAtlasShadowMapStrengthLoc = -1;

        if (_hasWorldAtlasTexture)
        {
            Raylib.UnloadTexture(_worldAtlasTexture);
            _hasWorldAtlasTexture = false;
        }

        if (_hasWorldShadowNearTexture)
        {
            Raylib.UnloadTexture(_worldShadowNearTexture);
            _hasWorldShadowNearTexture = false;
        }

        if (_hasWorldShadowFarTexture)
        {
            Raylib.UnloadTexture(_worldShadowFarTexture);
            _hasWorldShadowFarTexture = false;
        }
    }

    private static float[] ToArray(Vector3 value) => [value.X, value.Y, value.Z];

    private static void EnsureShadowTexture(ref Texture2D texture, ref bool hasTexture, int resolution)
    {
        if (hasTexture && texture.Width == resolution && texture.Height == resolution)
        {
            return;
        }

        if (hasTexture)
        {
            Raylib.UnloadTexture(texture);
            hasTexture = false;
        }

        var image = Raylib.GenImageColor(resolution, resolution, new Color(255, 255, 255, 255));
        texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        hasTexture = texture.Id > 0;
    }

    private static unsafe void UpdateShadowTexture(Texture2D texture, byte[] values)
    {
        if (texture.Id <= 0 || values.Length == 0)
        {
            return;
        }

        fixed (byte* ptr = values)
        {
            Raylib.UpdateTexture(texture, ptr);
        }
    }

    private static Mesh UploadMesh(float[] vertices, float[] texCoords, float[] normals, byte[] colors, ushort[] indices, int vertexCount, int triangleCount)
    {
        var mesh = new Mesh(vertexCount, triangleCount);
        mesh.AllocVertices();
        mesh.AllocTexCoords();
        mesh.AllocNormals();
        mesh.AllocColors();
        mesh.AllocIndices();

        vertices.AsSpan().CopyTo(mesh.VerticesAs<float>().Slice(0, vertices.Length));
        texCoords.AsSpan().CopyTo(mesh.TexCoordsAs<float>().Slice(0, texCoords.Length));
        normals.AsSpan().CopyTo(mesh.NormalsAs<float>().Slice(0, normals.Length));
        colors.AsSpan().CopyTo(mesh.ColorsAs<byte>().Slice(0, colors.Length));
        indices.AsSpan().CopyTo(mesh.IndicesAs<ushort>().Slice(0, indices.Length));
        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }

    private static string ResolveAssetPath(string relativePath)
    {
        var basePath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(basePath))
        {
            return basePath;
        }

        return Path.GetFullPath(relativePath);
    }

    private static Color GetFallbackBlockColor(BlockType block)
    {
        return block switch
        {
            BlockType.Grass => new Color(98, 144, 82, 255),
            BlockType.Dirt => new Color(148, 111, 76, 255),
            BlockType.Stone => new Color(134, 129, 121, 255),
            BlockType.Wood => new Color(132, 98, 61, 255),
            BlockType.Leaves => new Color(82, 130, 74, 255),
            _ => Color.White
        };
    }
}
