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
    private WorldMaterialPassSettings _worldMaterialPassSettings;
    private bool _hasWorldMaterialPassSettings;
    private Texture2D _worldAtlasTexture;
    private bool _hasWorldAtlasTexture;

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
        _hasWorldAtlasShader = true;
        ApplyWorldMaterialPassSettings();
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

        if (_hasWorldAtlasTexture)
        {
            Raylib.UnloadTexture(_worldAtlasTexture);
            _hasWorldAtlasTexture = false;
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
