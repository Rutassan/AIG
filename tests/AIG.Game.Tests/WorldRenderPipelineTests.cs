using System.Collections;
using System.Linq;
using System.Numerics;
using System.Reflection;
using AIG.Game.Config;
using AIG.Game.Core;
using AIG.Game.Player;
using AIG.Game.Tests.Fakes;
using AIG.Game.World;
using Raylib_cs;

namespace AIG.Game.Tests;

public sealed class WorldRenderPipelineTests
{
    [Fact(DisplayName = "ChunkSurfaceMeshData возвращает переданные массивы и вычисляет counts")]
    public void ChunkSurfaceMeshData_ExposesArraysAndCounts()
    {
        var vertices = new float[12];
        var texCoords = new float[8];
        var normals = new float[12];
        var colors = new byte[16];
        var indices = new ushort[6];

        var mesh = new ChunkSurfaceMeshData(vertices, texCoords, normals, colors, indices);

        Assert.Same(vertices, mesh.Vertices);
        Assert.Same(texCoords, mesh.TexCoords);
        Assert.Same(normals, mesh.Normals);
        Assert.Same(colors, mesh.Colors);
        Assert.Same(indices, mesh.Indices);
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(2, mesh.TriangleCount);
        Assert.False(mesh.IsEmpty);
    }

    [Fact(DisplayName = "WorldTextureAtlas возвращает ожидаемые тайлы для всех типов блоков")]
    public void WorldTextureAtlas_ReturnsExpectedFaceTiles()
    {
        Assert.Equal(
            new WorldTextureAtlas.FaceTiles(WorldTextureAtlas.WorldAtlasTile.GrassTop, WorldTextureAtlas.WorldAtlasTile.Dirt, WorldTextureAtlas.WorldAtlasTile.GrassSide),
            WorldTextureAtlas.GetFaceTiles(BlockType.Grass));
        Assert.Equal(
            new WorldTextureAtlas.FaceTiles(WorldTextureAtlas.WorldAtlasTile.Dirt, WorldTextureAtlas.WorldAtlasTile.Dirt, WorldTextureAtlas.WorldAtlasTile.Dirt),
            WorldTextureAtlas.GetFaceTiles(BlockType.Dirt));
        Assert.Equal(
            new WorldTextureAtlas.FaceTiles(WorldTextureAtlas.WorldAtlasTile.Stone, WorldTextureAtlas.WorldAtlasTile.Stone, WorldTextureAtlas.WorldAtlasTile.Stone),
            WorldTextureAtlas.GetFaceTiles(BlockType.Stone));
        Assert.Equal(
            new WorldTextureAtlas.FaceTiles(WorldTextureAtlas.WorldAtlasTile.WoodTop, WorldTextureAtlas.WorldAtlasTile.WoodTop, WorldTextureAtlas.WorldAtlasTile.WoodSide),
            WorldTextureAtlas.GetFaceTiles(BlockType.Wood));
        Assert.Equal(
            new WorldTextureAtlas.FaceTiles(WorldTextureAtlas.WorldAtlasTile.Leaves, WorldTextureAtlas.WorldAtlasTile.Leaves, WorldTextureAtlas.WorldAtlasTile.Leaves),
            WorldTextureAtlas.GetFaceTiles(BlockType.Leaves));
        Assert.Equal(
            new WorldTextureAtlas.FaceTiles(WorldTextureAtlas.WorldAtlasTile.Stone, WorldTextureAtlas.WorldAtlasTile.Stone, WorldTextureAtlas.WorldAtlasTile.Stone),
            WorldTextureAtlas.GetFaceTiles((BlockType)99));
    }

    [Fact(DisplayName = "TexturedBlockMeshFactory строит куб с UV и shading для дерева")]
    public void TexturedBlockMeshFactory_BuildsExpectedWoodCube()
    {
        var mesh = TexturedBlockMeshFactory.Build(BlockType.Wood);

        Assert.Equal(24, mesh.VertexCount);
        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(24 * 3, mesh.Vertices.Length);
        Assert.Equal(24 * 2, mesh.TexCoords.Length);
        Assert.Equal(24 * 3, mesh.Normals.Length);
        Assert.Equal(24 * 4, mesh.Colors.Length);
        Assert.Equal(12 * 3, mesh.Indices.Length);

        var topUv = WorldTextureAtlas.GetTileUv(WorldTextureAtlas.WorldAtlasTile.WoodTop);
        var sideUv = WorldTextureAtlas.GetTileUv(WorldTextureAtlas.WorldAtlasTile.WoodSide);

        Assert.Equal(sideUv.U0, mesh.TexCoords[0]);
        Assert.Equal(sideUv.V1, mesh.TexCoords[1]);

        var topFaceOffset = 2 * 4 * 2;
        Assert.Equal(topUv.U0, mesh.TexCoords[topFaceOffset + 0]);
        Assert.Equal(topUv.V1, mesh.TexCoords[topFaceOffset + 1]);

        Assert.True(mesh.Colors[2 * 4 * 4] > mesh.Colors[3 * 4 * 4], "Верхняя грань должна быть светлее нижней.");
        Assert.NotEqual(mesh.Colors[0], mesh.Colors[1]);
        Assert.NotEqual(mesh.Colors[0], mesh.Colors[2]);
    }

    [Fact(DisplayName = "TexturedBlockMeshFactory использует fallback-материал для неизвестного блока")]
    public void TexturedBlockMeshFactory_UnknownBlock_UsesFallbackMaterialChannel()
    {
        var mesh = TexturedBlockMeshFactory.Build((BlockType)99);
        var stoneUv = WorldTextureAtlas.GetTileUv(WorldTextureAtlas.WorldAtlasTile.Stone);

        Assert.Equal(stoneUv.U0, mesh.TexCoords[0]);
        Assert.Equal(stoneUv.V1, mesh.TexCoords[1]);
        Assert.Contains(Enumerable.Range(0, mesh.VertexCount), index => mesh.Colors[index * 4 + 3] == 255);
    }

    [Fact(DisplayName = "TexturedBlockMeshFactory кодирует material channel для всех atlas-блоков")]
    public void TexturedBlockMeshFactory_EncodeMaterialChannel_CoversAllAtlasBlocks()
    {
        var method = typeof(TexturedBlockMeshFactory).GetMethod("EncodeMaterialChannel", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.Equal((byte)32, method!.Invoke(null, [BlockType.Grass]));
        Assert.Equal((byte)72, method.Invoke(null, [BlockType.Dirt]));
        Assert.Equal((byte)128, method.Invoke(null, [BlockType.Stone]));
        Assert.Equal((byte)184, method.Invoke(null, [BlockType.Wood]));
        Assert.Equal((byte)232, method.Invoke(null, [BlockType.Leaves]));
    }

    [Fact(DisplayName = "ChunkSurfaceMeshFactory убирает внутреннюю грань между соседними блоками")]
    public void ChunkSurfaceMeshFactory_HidesInternalFacesBetweenAdjacentBlocks()
    {
        var world = new WorldMap(8, 8, 8, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(4, 1, 4, BlockType.Stone);
        world.SetBlock(5, 1, 4, BlockType.Stone);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 2.2f, 4.5f), maxChunks: 16);
        Assert.True(world.TryGetChunkSurfaceBlocks(0, 0, out var surfaces));

        var mesh = ChunkSurfaceMeshFactory.Build(world, surfaces);

        Assert.Equal(10 * 2, mesh.TriangleCount);
        Assert.Equal(10 * 4, mesh.VertexCount);
    }

    [Fact(DisplayName = "WorldMap.TryGetBlockNoLoad не генерирует отсутствующий чанк и возвращает false")]
    public void WorldMap_TryGetBlockNoLoad_ReturnsFalseForUnloadedChunk()
    {
        var world = new WorldMap(16, 8, 16, chunkSize: 8, seed: 0);

        var found = world.TryGetBlockNoLoad(12, 1, 12, out var block);

        Assert.False(found);
        Assert.Equal(BlockType.Air, block);
        Assert.Equal(0, world.LoadedChunkCount);
    }

    [Fact(DisplayName = "ChunkSurfaceMeshFactory не пишет грани, если индекс вершины вышел бы за ushort")]
    public void ChunkSurfaceMeshFactory_AddFaceIfVisible_StopsOnIndexOverflow()
    {
        var faceType = typeof(ChunkSurfaceMeshFactory).GetNestedType("FaceDefinition", BindingFlags.NonPublic);
        var ctor = faceType!.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single();
        var face = ctor.Invoke(
        [
            new Vector3(0f, 1f, 0f),
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.One,
            Vector3.UnitY,
            WorldTextureAtlas.WorldAtlasTile.Stone,
            (byte)200,
            (byte)180,
            (byte)160,
            (byte)128
        ]);

        var vertices = Enumerable.Repeat(0f, (ushort.MaxValue - 3) * 3).ToList();
        var texCoords = new List<float>();
        var normals = new List<float>();
        var colors = new List<byte>();
        var indices = new List<ushort>();
        var method = typeof(ChunkSurfaceMeshFactory).GetMethod("AddFaceIfVisible", BindingFlags.Static | BindingFlags.NonPublic)!;

        method.Invoke(null, [face, true, vertices, texCoords, normals, colors, indices]);

        Assert.Empty(texCoords);
        Assert.Empty(normals);
        Assert.Empty(colors);
        Assert.Empty(indices);
        Assert.Equal((ushort.MaxValue - 3) * 3, vertices.Count);
    }

    [Fact(DisplayName = "DrawWorld использует chunk mesh для известных atlas-блоков в ближнем чанке")]
    public void DrawWorld_UsesTexturedChunkMesh_ForKnownBlocks()
    {
        var world = new WorldMap(8, 8, 8, chunkSize: 8, seed: 0);
        world.SetBlock(4, 1, 4, BlockType.Grass);
        world.SetBlock(5, 1, 4, BlockType.Wood);
        world.SetBlock(4, 2, 4, BlockType.Leaves);

        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        SetPrivateField(app, "_player", new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f)));

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        Assert.True(platform.DrawTexturedChunkMeshCalls > 0);
        Assert.Contains(platform.DrawnTexturedChunkMeshes, call => call.ChunkX == 0 && call.ChunkZ == 0 && call.TriangleCount > 0);
    }

    [Fact(DisplayName = "DrawWorld использует chunk mesh и на средней дистанции, если budget позволяет")]
    public void DrawWorld_UsesTexturedChunkMesh_AtMediumDistance()
    {
        var world = new WorldMap(32, 8, 32, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(20, 1, 4, BlockType.Stone);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 2.2f, 4.5f), maxChunks: 64);

        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        SetPrivateField(app, "_player", new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f)));

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        Assert.Contains(platform.DrawnTexturedChunkMeshes, call => call.ChunkX == 2 && call.ChunkZ == 0 && call.TriangleCount > 0);
    }

    [Fact(DisplayName = "DrawWorld держит старый chunk mesh, пока dirty-чанк еще не пересобран")]
    public void DrawWorld_DirtyChunk_KeepsUsingCachedChunkMeshUntilRebuildCompletes()
    {
        var world = new WorldMap(8, 8, 8, chunkSize: 8, seed: 0);
        world.SetBlock(4, 1, 4, BlockType.Grass);
        world.SetBlock(5, 1, 4, BlockType.Wood);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 2.2f, 4.5f), maxChunks: 16);

        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        SetPrivateField(app, "_player", new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f)));

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
        var meshCallsBefore = platform.DrawTexturedChunkMeshCalls;
        var texturedFallbackBefore = platform.DrawTexturedBlockInstancedCalls;
        var legacyFallbackBefore = platform.LegacyDrawCubeInstancedCalls;

        world.SetBlock(6, 1, 4, BlockType.Stone);

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        Assert.True(platform.DrawTexturedChunkMeshCalls > meshCallsBefore);
        Assert.Equal(texturedFallbackBefore, platform.DrawTexturedBlockInstancedCalls);
        Assert.Equal(legacyFallbackBefore, platform.LegacyDrawCubeInstancedCalls);
    }

    [Fact(DisplayName = "DrawWorld оставляет legacy fallback для неизвестного блока рядом с chunk mesh")]
    public void DrawWorld_UnknownBlockType_UsesLegacyCubeFallback()
    {
        var world = new WorldMap(8, 8, 8, chunkSize: 8, seed: 0);
        world.SetBlock(4, 1, 3, BlockType.Stone);
        world.SetBlock(4, 1, 2, (BlockType)99);

        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        var player = new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f));
        SetPlayerPose(player, player.Position, new Vector3(0f, 0f, -1f));
        SetPrivateField(app, "_player", player);

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        Assert.True(platform.DrawTexturedChunkMeshCalls > 0);
        Assert.True(platform.LegacyDrawCubeInstancedCalls > 0);
    }

    [Fact(DisplayName = "Дальность texture-pass зависит от качества графики")]
    public void GetWorldTextureRenderDistance_DependsOnGraphicsQuality()
    {
        var platform = new FakeGamePlatform();
        var lowApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var mediumApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Medium }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var highApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("GetWorldTextureRenderDistance", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(9f, (float)method.Invoke(lowApp, null)!);
        Assert.Equal(14f, (float)method.Invoke(mediumApp, null)!);
        Assert.Equal(22f, (float)method.Invoke(highApp, null)!);
    }

    [Fact(DisplayName = "Дальность chunk-mesh pass зависит от качества графики")]
    public void GetWorldChunkMeshRenderDistance_DependsOnGraphicsQuality()
    {
        var platform = new FakeGamePlatform();
        var lowApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var mediumApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Medium }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var highApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("GetWorldChunkMeshRenderDistance", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(9f, (float)method.Invoke(lowApp, null)!);
        Assert.Equal(16f, (float)method.Invoke(mediumApp, null)!);
        Assert.Equal(24f, (float)method.Invoke(highApp, null)!);
    }

    [Fact(DisplayName = "Дальность far terrain mesh зависит от качества графики")]
    public void GetWorldFarTerrainMeshDistance_DependsOnGraphicsQuality()
    {
        var platform = new FakeGamePlatform();
        var lowApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var mediumApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Medium }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var highApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("GetWorldFarTerrainMeshDistance", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(9f, (float)method.Invoke(lowApp, null)!);
        Assert.Equal(18f, (float)method.Invoke(mediumApp, null)!);
        Assert.Equal(72f, (float)method.Invoke(highApp, null)!);
    }

    [Fact(DisplayName = "DrawWorld настраивает world material pass для atlas-мира")]
    public void DrawWorld_ConfiguresWorldMaterialPass()
    {
        var world = new WorldMap(8, 8, 8, chunkSize: 8, seed: 0);
        world.SetBlock(4, 1, 4, BlockType.Grass);

        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        SetPrivateField(app, "_player", new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f)));

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        Assert.True(platform.ConfigureWorldMaterialPassCalls > 0);
        var settings = Assert.Single(platform.WorldMaterialPasses);
        Assert.True(settings.FogEnd > settings.FogStart);
        Assert.True(settings.Strength > 0.9f);
        Assert.True(settings.ShadowStrength > 0.45f);
        Assert.True(settings.AtmosphereStrength > 0.80f);
        Assert.True(settings.WarmLightStrength > 0.76f);
        Assert.True(settings.CoolShadowStrength > 0.4f);
        Assert.True(settings.ContrastStrength > 0.44f);
        Assert.True(settings.GlowStrength > 0.38f);
        Assert.True(settings.MaterialSeparationStrength > 0.60f);
        Assert.True(settings.ShadowDepthStrength > 0.35f);
        Assert.True(settings.SkyBlendStrength > 0.24f);
        Assert.True(settings.SunScatterStrength > 0.38f);
        Assert.True(settings.AmbientLiftStrength > 0.20f);
        Assert.True(settings.HazeStrength > 0.18f);
        Assert.True(settings.MaterialShadowStrength > 0.28f);
        Assert.True(settings.HorizonDepthStrength > 0.24f);
        Assert.True(settings.FoliageTranslucencyStrength > 0.16f);
        Assert.True(settings.SecondaryBounceStrength > 0.22f);
        Assert.True(settings.DistanceMaterialStrength > 0.28f);
        Assert.True(settings.SkyResponseStrength > 0.24f);
        Assert.True(settings.FarGradientStrength > 0.20f);
        Assert.True(settings.ShadowContourStrength > 0.20f);
        Assert.True(settings.AtmosphereGradientStrength > 0.20f);
        Assert.True(settings.DistanceShadowLiftStrength > 0.18f);
        Assert.True(settings.SkyContourStrength > 0.18f);
        Assert.True(settings.DistantSilhouetteStrength > 0.16f);
        Assert.True(settings.AtmosphericContourStrength > 0.16f);
        Assert.True(settings.ReliefBridgeStrength > 0.18f);
        Assert.True(settings.ShadowHazeFusionStrength > 0.18f);
        Assert.True(settings.LightPlasticityStrength > 0.16f);
        Assert.True(settings.FarReadabilityStrength > 0.16f);
        Assert.True(settings.FinalCohesionStrength > 0.16f);
        Assert.True(settings.ViewMaterialStrength > 0.16f);
        Assert.True(settings.ShadowCascadeBlendStrength > 0.18f);
        Assert.True(settings.FarWorldCohesionStrength > 0.18f);
        Assert.True(settings.FarReadabilityStrength > 0.16f);
    }

    [Fact(DisplayName = "DrawWorld настраивает near/far shadow pass для atlas-мира")]
    public void DrawWorld_ConfiguresWorldShadowPass()
    {
        var world = new WorldMap(16, 8, 16, chunkSize: 8, seed: 0);
        world.SetBlock(4, 1, 4, BlockType.Grass);
        world.SetBlock(5, 2, 4, BlockType.Wood);
        world.SetBlock(4, 3, 5, BlockType.Leaves);

        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        SetPrivateField(app, "_player", new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f)));

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        Assert.True(platform.ConfigureWorldShadowPassCalls > 0);
        var settings = Assert.Single(platform.WorldShadowPasses);
        Assert.True(settings.Enabled);
        Assert.True(settings.NearResolution >= 40);
        Assert.True(settings.FarResolution >= 28);
        Assert.True(settings.NearFilterRadius >= 1f);
        Assert.True(settings.FarFilterRadius >= 0f);
        Assert.True(settings.NearDistance < settings.FarDistance);
        Assert.True(settings.FarDistance < settings.FarProxyStartDistance);
        Assert.True(settings.FarProxyStartDistance < settings.FarProxyEndDistance);
        Assert.True(settings.FarProxyStrength > 0.5f);
        Assert.True(settings.CascadeBlendWidth > 0.2f);
        Assert.True(settings.Strength > 0.4f);
        Assert.True(settings.Bias > 0f);
        Assert.True(settings.SlopeBiasStrength > 0f);
        Assert.True(settings.NearHalfWidth > 0f);
        Assert.True(settings.FarHalfWidth > settings.NearHalfWidth);
    }

    [Fact(DisplayName = "DrawWorld стабилизирует shadow quality profile по quality mode, а не по текущему FPS")]
    public void DrawWorld_ShadowPassScalesByQualityMode()
    {
        var world = new WorldMap(16, 8, 16, chunkSize: 8, seed: 0);
        world.SetBlock(4, 1, 4, BlockType.Grass);
        world.SetBlock(5, 2, 4, BlockType.Wood);
        world.SetBlock(4, 3, 5, BlockType.Leaves);

        var lowPlatform = new FakeGamePlatform { Fps = 25 };
        var highPlatform = new FakeGamePlatform { Fps = 240 };
        var lowApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low }, lowPlatform, world);
        var highApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, highPlatform, world);
        SetPrivateField(lowApp, "_player", new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f)));
        SetPrivateField(highApp, "_player", new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f)));

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(lowApp, null);
        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(highApp, null);

        var lowSettings = Assert.Single(lowPlatform.WorldShadowPasses);
        var highSettings = Assert.Single(highPlatform.WorldShadowPasses);
        Assert.True(highSettings.NearFilterRadius >= lowSettings.NearFilterRadius);
        Assert.True(highSettings.FarFilterRadius >= lowSettings.FarFilterRadius);
        Assert.True(highSettings.CascadeBlendWidth > lowSettings.CascadeBlendWidth);
        Assert.True(highSettings.Strength > lowSettings.Strength);
        Assert.True(highSettings.FarProxyEndDistance > lowSettings.FarProxyEndDistance);
        Assert.True(highSettings.FarProxyStrength < lowSettings.FarProxyStrength);
    }

    [Fact(DisplayName = "Shadow far-proxy profile масштабируется по quality mode")]
    public void ShadowFarProxyProfile_DependsOnGraphicsQuality()
    {
        var platform = new FakeGamePlatform();
        var lowApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var mediumApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Medium }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var highApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));

        var farCoverageMethod = typeof(GameApp).GetMethod("GetWorldShadowFarCoverageDistance", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var farProxyStartMethod = typeof(GameApp).GetMethod("GetWorldShadowFarProxyStartDistance", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var farProxyEndMethod = typeof(GameApp).GetMethod("GetWorldShadowFarProxyEndDistance", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var farProxyStrengthMethod = typeof(GameApp).GetMethod("GetWorldShadowFarProxyStrength", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(24f, (float)farCoverageMethod.Invoke(lowApp, null)!);
        Assert.Equal(32f, (float)farCoverageMethod.Invoke(mediumApp, null)!);
        Assert.Equal(42f, (float)farCoverageMethod.Invoke(highApp, null)!);
        Assert.Equal(26f, (float)farProxyStartMethod.Invoke(lowApp, null)!);
        Assert.Equal(36f, (float)farProxyStartMethod.Invoke(mediumApp, null)!);
        Assert.Equal(46f, (float)farProxyStartMethod.Invoke(highApp, null)!);
        Assert.Equal(40f, (float)farProxyEndMethod.Invoke(lowApp, null)!);
        Assert.Equal(56f, (float)farProxyEndMethod.Invoke(mediumApp, null)!);
        Assert.Equal(78f, (float)farProxyEndMethod.Invoke(highApp, null)!);
        Assert.Equal(0.92f, (float)farProxyStrengthMethod.Invoke(lowApp, null)!);
        Assert.Equal(0.84f, (float)farProxyStrengthMethod.Invoke(mediumApp, null)!);
        Assert.Equal(0.76f, (float)farProxyStrengthMethod.Invoke(highApp, null)!);
    }

    [Fact(DisplayName = "DrawWorld отсекает chunk вне frustum, даже если он не за спиной")]
    public void DrawWorld_FrustumCullsChunkOutsideView()
    {
        var world = new WorldMap(64, 8, 64, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(38, 1, 18, BlockType.Grass);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 2.2f, 4.5f), maxChunks: 128);

        var platform = new FakeGamePlatform { ScreenWidth = 1280, ScreenHeight = 720 };
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        var player = new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f));
        SetPlayerPose(player, player.Position, new Vector3(0f, 0f, 1f));
        SetPrivateField(app, "_player", player);

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        Assert.DoesNotContain(platform.DrawnTexturedChunkMeshes, call => call.ChunkX == 4 && call.ChunkZ == 2);
        Assert.Empty(platform.DrawnTexturedBlocks);
        Assert.Equal(0, platform.LegacyDrawCubeInstancedCalls);
    }

    [Fact(DisplayName = "World visibility profile делит мир на near mid far atmospheric")]
    public void WorldVisibilityProfile_ClassifiesBands()
    {
        var method = typeof(GameApp).GetMethod("BuildWorldVisibilityProfile", BindingFlags.Static | BindingFlags.NonPublic);
        var classify = typeof(GameApp).GetMethod("ClassifyWorldVisibilityBand", BindingFlags.Static | BindingFlags.NonPublic);
        var blend = typeof(GameApp).GetMethod("GetVisibilityBlendWeights", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.NotNull(classify);
        Assert.NotNull(blend);

        var profile = method!.Invoke(null, [8f, 12f, 40f, 46f])!;

        Assert.Equal("Near", classify!.Invoke(null, [4f, profile])!.ToString());
        Assert.Equal("Mid", classify.Invoke(null, [10f, profile])!.ToString());
        Assert.Equal("Far", classify.Invoke(null, [28f, profile])!.ToString());
        Assert.Equal("Atmospheric", classify.Invoke(null, [45f, profile])!.ToString());

        var blended = blend!.Invoke(null, [39.5f, profile])!;
        var far = (float)blended.GetType().GetProperty("Far")!.GetValue(blended)!;
        var atmospheric = (float)blended.GetType().GetProperty("Atmospheric")!.GetValue(blended)!;
        Assert.True(far > 0f);
        Assert.True(atmospheric > 0f);
    }

    [Fact(DisplayName = "GetVisibilityBlendWeights имеет fallback для вырожденного профиля")]
    public void GetVisibilityBlendWeights_UsesFallbackForDegenerateProfile()
    {
        var method = typeof(GameApp).GetMethod("GetVisibilityBlendWeights", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var profileType = typeof(GameApp).GetNestedType("WorldVisibilityProfile", BindingFlags.NonPublic);
        Assert.NotNull(profileType);
        var ctor = profileType!.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single();
        var profile = ctor.Invoke([0f, 0f, 0f, 0f, 0f, 0f, 0f]);

        var blended = method!.Invoke(null, [0f, profile])!;

        Assert.Equal(0f, (float)blended.GetType().GetProperty("Near")!.GetValue(blended)!);
        Assert.Equal(0f, (float)blended.GetType().GetProperty("Mid")!.GetValue(blended)!);
        Assert.Equal(0f, (float)blended.GetType().GetProperty("Far")!.GetValue(blended)!);
        Assert.Equal(1f, (float)blended.GetType().GetProperty("Atmospheric")!.GetValue(blended)!);
    }

    [Fact(DisplayName = "GetVisibilityBlendWeights имеет fallback для невалидной суммы")]
    public void GetVisibilityBlendWeights_UsesFallbackForInvalidSum()
    {
        var method = typeof(GameApp).GetMethod("GetVisibilityBlendWeights", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var buildProfile = typeof(GameApp).GetMethod("BuildWorldVisibilityProfile", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(buildProfile);
        var profile = buildProfile!.Invoke(null, [8f, 12f, 40f, 46f])!;

        var blended = method!.Invoke(null, [float.NaN, profile])!;

        Assert.Equal(0f, (float)blended.GetType().GetProperty("Near")!.GetValue(blended)!);
        Assert.Equal(0f, (float)blended.GetType().GetProperty("Mid")!.GetValue(blended)!);
        Assert.Equal(0f, (float)blended.GetType().GetProperty("Far")!.GetValue(blended)!);
        Assert.Equal(1f, (float)blended.GetType().GetProperty("Atmospheric")!.GetValue(blended)!);
    }

    [Fact(DisplayName = "RasterizeSurfaceBlockShadow записывает depth в shadow map")]
    public void RasterizeSurfaceBlockShadow_WritesDepth()
    {
        var volumeType = typeof(GameApp).GetNestedType("ShadowVolume", BindingFlags.NonPublic);
        Assert.NotNull(volumeType);
        var ctor = volumeType!.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single();
        var volume = ctor.Invoke(
        [
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
            Vector3.UnitZ,
            8f,
            8f,
            8f,
            16,
            8f
        ]);

        var map = Enumerable.Repeat(byte.MaxValue, 16 * 16).ToArray();
        var method = typeof(GameApp).GetMethod("RasterizeSurfaceBlockShadow", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(null, [map, volume, new Vector3(0f, 0f, 0f)]);

        Assert.Contains(map, value => value < byte.MaxValue);
    }

    [Fact(DisplayName = "TryProjectPointToShadowVolume отбрасывает точки за пределами объема")]
    public void TryProjectPointToShadowVolume_RejectsOutsidePoint()
    {
        var volumeType = typeof(GameApp).GetNestedType("ShadowVolume", BindingFlags.NonPublic);
        Assert.NotNull(volumeType);
        var ctor = volumeType!.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single();
        var volume = ctor.Invoke(
        [
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
            Vector3.UnitZ,
            4f,
            4f,
            4f,
            16,
            4f
        ]);

        var method = typeof(GameApp).GetMethod("TryProjectPointToShadowVolume", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var insideArgs = new object?[] { Vector3.Zero, volume, null, null, null };
        Assert.True((bool)method!.Invoke(null, insideArgs)!);

        var outsideArgs = new object?[] { new Vector3(9f, 0f, 0f), volume, null, null, null };
        Assert.False((bool)method.Invoke(null, outsideArgs)!);
    }

    [Fact(DisplayName = "BuildShadowVolume выбирает запасную ось для почти вертикального света")]
    public void BuildShadowVolume_UsesUnitZSeed_ForNearVerticalLight()
    {
        var method = typeof(GameApp).GetMethod("BuildShadowVolume", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var volume = method!.Invoke(null, [Vector3.Zero, Vector3.Normalize(new Vector3(0.03f, -1f, 0.01f)), 12f, 10f, 18f, 32, 64f]);
        Assert.NotNull(volume);

        var rightProperty = volume!.GetType().GetProperty("Right");
        var upProperty = volume.GetType().GetProperty("Up");
        Assert.NotNull(rightProperty);
        Assert.NotNull(upProperty);

        var right = Assert.IsType<Vector3>(rightProperty!.GetValue(volume)!);
        var up = Assert.IsType<Vector3>(upProperty!.GetValue(volume)!);

        Assert.True(MathF.Abs(Vector3.Dot(right, Vector3.UnitY)) < 0.9f);
        Assert.True(right.Length() > 0.9f);
        Assert.True(up.Length() > 0.9f);
    }

    [Fact(DisplayName = "TryBuildWorldViewFrustum возвращает false для невалидных входов")]
    public void TryBuildWorldViewFrustum_InvalidInputs_ReturnFalse()
    {
        var method = typeof(GameApp).GetMethod("TryBuildWorldViewFrustum", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var validCamera = new Camera3D
        {
            Position = new Vector3(1f, 2f, 3f),
            Target = new Vector3(1f, 2f, 4f),
            Up = Vector3.UnitY,
            FovY = 75f,
            Projection = CameraProjection.Perspective
        };
        var zeroForwardCamera = validCamera with { Target = validCamera.Position };
        var degenerateUpCamera = validCamera with { Up = Vector3.UnitZ };
        var zeroFovCamera = validCamera with { FovY = 0f };

        var argsInvalidScreen = new object?[] { validCamera, 0, 720, null };
        Assert.False((bool)method!.Invoke(null, argsInvalidScreen)!);

        var argsZeroForward = new object?[] { zeroForwardCamera, 1280, 720, null };
        Assert.False((bool)method.Invoke(null, argsZeroForward)!);

        var argsDegenerateUp = new object?[] { degenerateUpCamera, 1280, 720, null };
        Assert.False((bool)method.Invoke(null, argsDegenerateUp)!);

        var argsZeroFov = new object?[] { zeroFovCamera, 1280, 720, null };
        Assert.False((bool)method.Invoke(null, argsZeroFov)!);
    }

    [Fact(DisplayName = "DrawWorld откатывается к directional culling, если frustum недоступен")]
    public void DrawWorld_FallsBackToDirectionalCulling_WhenFrustumUnavailable()
    {
        var world = new WorldMap(96, 8, 96, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(10, 1, 40, BlockType.Stone);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(40.5f, 2.2f, 40.5f), maxChunks: 256);

        var platform = new FakeGamePlatform { ScreenWidth = 0, ScreenHeight = 720 };
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        var player = new PlayerController(new GameConfig(), new Vector3(40.5f, 2.2f, 40.5f));
        SetPlayerPose(player, player.Position, new Vector3(1f, 0f, 0f));
        SetPrivateField(app, "_player", player);

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        Assert.DoesNotContain(platform.DrawnCubes, call => Vector3.Distance(call.Position, new Vector3(10.5f, 1.5f, 40.5f)) < 0.01f);
        Assert.DoesNotContain(platform.DrawnTexturedChunkMeshes, call => call.ChunkX == 1 && call.ChunkZ == 5);
    }

    [Fact(DisplayName = "DrawWorld без frustum все еще рисует forward-цель через directional fallback")]
    public void DrawWorld_WithoutFrustum_StillDrawsForwardTarget()
    {
        var world = new WorldMap(96, 8, 96, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(70, 1, 40, BlockType.Stone);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(40.5f, 2.2f, 40.5f), maxChunks: 256);

        var platform = new FakeGamePlatform { ScreenWidth = 0, ScreenHeight = 720 };
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        var player = new PlayerController(new GameConfig(), new Vector3(40.5f, 2.2f, 40.5f));
        SetPlayerPose(player, player.Position, new Vector3(1f, 0f, 0f));
        SetPrivateField(app, "_player", player);

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        Assert.Contains(platform.DrawnCubes, call => Vector3.Distance(call.Position, new Vector3(70.5f, 1.5f, 40.5f)) < 0.01f);
    }

    [Fact(DisplayName = "ChunkSurfaceMeshFactory кодирует отдельные каналы света, солнца и relief")]
    public void ChunkSurfaceMeshFactory_EncodesDistinctLightingChannels()
    {
        var world = new WorldMap(8, 8, 8, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(4, 1, 4, BlockType.Stone);
        world.SetBlock(4, 2, 4, BlockType.Wood);
        world.SetBlock(4, 1, 5, BlockType.Wood);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 2.2f, 4.5f), maxChunks: 16);
        Assert.True(world.TryGetChunkSurfaceBlocks(0, 0, out var surfaces));

        var mesh = ChunkSurfaceMeshFactory.Build(world, surfaces);

        Assert.NotEmpty(mesh.Colors);
        Assert.Contains(Enumerable.Range(0, mesh.VertexCount), index =>
        {
            var offset = index * 4;
            return mesh.Colors[offset + 0] != mesh.Colors[offset + 1]
                || mesh.Colors[offset + 0] != mesh.Colors[offset + 2];
        });
        Assert.Contains(Enumerable.Range(0, mesh.VertexCount), index =>
        {
            var offset = index * 4;
            return mesh.Colors[offset + 3] != 255;
        });
    }

    [Fact(DisplayName = "ChunkSurfaceMeshFactory кодирует daylight в sun channel")]
    public void ChunkSurfaceMeshFactory_EncodeSunChannel_UsesDaylight()
    {
        var method = typeof(ChunkSurfaceMeshFactory).GetMethod("EncodeSunChannel", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var bright = new WorldMap.SurfaceBlock(4, 2, 4, BlockType.Stone, VisibleFaces: 4, TopVisible: true, SkyExposure: 4, AmbientOcclusion: 1, ReliefExposure: 2, SunVisibility: WorldMap.MaxSunVisibility, Daylight: WorldMap.MaxDaylight);
        var dark = bright with { Daylight = 0, SunVisibility = 0, SkyExposure = 0 };

        var brightValue = Assert.IsType<byte>(method!.Invoke(null, [bright, 0.18f, 0.12f])!);
        var darkValue = Assert.IsType<byte>(method.Invoke(null, [dark, 0.18f, 0.12f])!);

        Assert.True(brightValue > darkValue);
    }

    [Fact(DisplayName = "ChunkSurfaceMeshFactory кодирует local light в sun channel")]
    public void ChunkSurfaceMeshFactory_EncodeSunChannel_UsesLocalLight()
    {
        var method = typeof(ChunkSurfaceMeshFactory).GetMethod("EncodeSunChannel", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var dark = new WorldMap.SurfaceBlock(4, 2, 4, BlockType.Stone, VisibleFaces: 4, TopVisible: false, SkyExposure: 0, AmbientOcclusion: 2, ReliefExposure: 1, SunVisibility: 0, Daylight: 0, LocalLight: 0);
        var lit = dark with { LocalLight = WorldMap.MaxLocalLight };

        var darkValue = Assert.IsType<byte>(method!.Invoke(null, [dark, -0.04f, 0.02f])!);
        var litValue = Assert.IsType<byte>(method.Invoke(null, [lit, -0.04f, 0.02f])!);

        Assert.True(litValue > darkValue);
    }

    [Fact(DisplayName = "ChunkSurfaceMeshFactory усиливает base shade от local light")]
    public void ChunkSurfaceMeshFactory_Build_UsesLocalLightInShadeChannel()
    {
        var world = new WorldMap(8, 8, 8, chunkSize: 8, seed: 0);
        var darkSurface = new WorldMap.SurfaceBlock(4, 2, 4, BlockType.Stone, VisibleFaces: 6, TopVisible: true, SkyExposure: 0, AmbientOcclusion: 0, ReliefExposure: 0, SunVisibility: 0, Daylight: 0, LocalLight: 0);
        var litSurface = darkSurface with { LocalLight = WorldMap.MaxLocalLight };

        var darkMesh = ChunkSurfaceMeshFactory.Build(world, [darkSurface]);
        var litMesh = ChunkSurfaceMeshFactory.Build(world, [litSurface]);

        Assert.NotEmpty(darkMesh.Colors);
        Assert.NotEmpty(litMesh.Colors);
        Assert.True(litMesh.Colors[0] > darkMesh.Colors[0]);
    }

    [Fact(DisplayName = "ChunkSurfaceMeshFactory делает закрытую поверхность заметно темнее без источников света")]
    public void ChunkSurfaceMeshFactory_Build_DarkensClosedSurfaceWithoutLightSources()
    {
        var world = new WorldMap(8, 8, 8, chunkSize: 8, seed: 0);
        var closedSurface = new WorldMap.SurfaceBlock(4, 2, 4, BlockType.Stone, VisibleFaces: 6, TopVisible: false, SkyExposure: 0, AmbientOcclusion: 2, ReliefExposure: 0, SunVisibility: 0, Daylight: 0, LocalLight: 0);
        var sunlitSurface = closedSurface with
        {
            TopVisible = true,
            SkyExposure = 4,
            SunVisibility = WorldMap.MaxSunVisibility,
            Daylight = WorldMap.MaxDaylight
        };

        var darkMesh = ChunkSurfaceMeshFactory.Build(world, [closedSurface]);
        var sunlitMesh = ChunkSurfaceMeshFactory.Build(world, [sunlitSurface]);

        Assert.NotEmpty(darkMesh.Colors);
        Assert.NotEmpty(sunlitMesh.Colors);
        Assert.True(darkMesh.Colors[0] < 80);
        Assert.True(sunlitMesh.Colors[0] > darkMesh.Colors[0]);
        Assert.True(darkMesh.Colors[1] < sunlitMesh.Colors[1]);
    }

    [Fact(DisplayName = "ChunkSurfaceMeshFactory кодирует fallback-материал для неизвестного блока")]
    public void ChunkSurfaceMeshFactory_EncodeMaterialChannel_UsesFallbackForUnknownBlock()
    {
        var method = typeof(ChunkSurfaceMeshFactory).GetMethod("EncodeMaterialChannel", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var value = Assert.IsType<byte>(method!.Invoke(null, [(BlockType)99])!);

        Assert.Equal(255, value);
    }

    [Fact(DisplayName = "Бюджет сборки новых chunk mesh зависит от качества графики")]
    public void GetWorldChunkMeshBuildBudget_DependsOnGraphicsQuality()
    {
        var platform = new FakeGamePlatform();
        var lowApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var mediumApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Medium }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var highApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("GetWorldChunkMeshBuildBudget", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(1, (int)method.Invoke(lowApp, null)!);
        Assert.Equal(3, (int)method.Invoke(mediumApp, null)!);
        Assert.Equal(5, (int)method.Invoke(highApp, null)!);
    }

    [Fact(DisplayName = "DrawWorld использует chunk mesh для дальнего terrain-чанка на high")]
    public void DrawWorld_UsesTexturedChunkMesh_ForFarTerrainChunk()
    {
        var world = new WorldMap(64, 8, 64, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        for (var x = 24; x <= 30; x++)
        {
            for (var z = 4; z <= 10; z++)
            {
                world.SetBlock(x, 1, z, BlockType.Stone);
                world.SetBlock(x, 2, z, BlockType.Grass);
            }
        }

        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 2.2f, 4.5f), maxChunks: 256);

        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        SetPrivateField(app, "_player", new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f)));

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        Assert.Contains(platform.DrawnTexturedChunkMeshes, call => call.ChunkX == 3 && call.ChunkZ == 0 && call.TriangleCount > 0);
    }

    [Fact(DisplayName = "ChunkSurfaceMeshFactory строит distant terrain proxy только из top-visible terrain")]
    public void ChunkSurfaceMeshFactory_BuildDistantTerrainProxy_SamplesTopTerrainOnly()
    {
        IReadOnlyList<WorldMap.SurfaceBlock> surfaces =
        [
            new WorldMap.SurfaceBlock(0, 2, 0, BlockType.Grass, 6, true, 5, 0, 0, WorldMap.MaxSunVisibility, WorldMap.MaxDaylight, 0),
            new WorldMap.SurfaceBlock(2, 2, 0, BlockType.Stone, 6, true, 5, 0, 0, WorldMap.MaxSunVisibility, WorldMap.MaxDaylight, 0),
            new WorldMap.SurfaceBlock(1, 2, 0, BlockType.Dirt, 6, true, 5, 0, 0, WorldMap.MaxSunVisibility, WorldMap.MaxDaylight, 0),
            new WorldMap.SurfaceBlock(0, 2, 2, BlockType.Wood, 6, true, 5, 0, 0, WorldMap.MaxSunVisibility, WorldMap.MaxDaylight, 0),
            new WorldMap.SurfaceBlock(2, 2, 2, BlockType.Grass, 6, false, 5, 0, 0, WorldMap.MaxSunVisibility, WorldMap.MaxDaylight, 0)
        ];

        var mesh = ChunkSurfaceMeshFactory.BuildDistantTerrainProxy(
            surfaces,
            sampleStep: 2,
            ChunkSurfaceMeshFactory.DistantTerrainLightingProfile.Far);

        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(4, mesh.TriangleCount);
        Assert.Equal(8 * 4, mesh.Colors.Length);
    }

    [Fact(DisplayName = "Ultra-far distant terrain proxy не зависит от full local-light payload")]
    public void ChunkSurfaceMeshFactory_BuildDistantTerrainProxy_UltraFarIgnoresFullLocalLightPayload()
    {
        var darkSurface = new WorldMap.SurfaceBlock(0, 2, 0, BlockType.Stone, 6, true, 1, 0, 1, 0, 0, 0);
        var litSurface = darkSurface with { LocalLight = WorldMap.MaxLocalLight };

        var darkMesh = ChunkSurfaceMeshFactory.BuildDistantTerrainProxy(
            [darkSurface],
            sampleStep: 1,
            ChunkSurfaceMeshFactory.DistantTerrainLightingProfile.UltraFar);
        var litMesh = ChunkSurfaceMeshFactory.BuildDistantTerrainProxy(
            [litSurface],
            sampleStep: 1,
            ChunkSurfaceMeshFactory.DistantTerrainLightingProfile.UltraFar);

        Assert.Equal(darkMesh.Colors, litMesh.Colors);
    }

    [Fact(DisplayName = "Far distant terrain proxy использует только сжатый local-light payload")]
    public void ChunkSurfaceMeshFactory_BuildDistantTerrainProxy_FarCompressesLocalLightPayload()
    {
        var darkSurface = new WorldMap.SurfaceBlock(0, 2, 0, BlockType.Stone, 6, true, 1, 0, 1, 0, 0, 0);
        var litSurface = darkSurface with { LocalLight = WorldMap.MaxLocalLight };

        var darkMesh = ChunkSurfaceMeshFactory.BuildDistantTerrainProxy(
            [darkSurface],
            sampleStep: 1,
            ChunkSurfaceMeshFactory.DistantTerrainLightingProfile.Far);
        var litMesh = ChunkSurfaceMeshFactory.BuildDistantTerrainProxy(
            [litSurface],
            sampleStep: 1,
            ChunkSurfaceMeshFactory.DistantTerrainLightingProfile.Far);

        Assert.NotEqual(darkMesh.Colors, litMesh.Colors);
        Assert.InRange(litMesh.Colors[0] - darkMesh.Colors[0], 1, 16);
        Assert.InRange(litMesh.Colors[1] - darkMesh.Colors[1], 1, 12);
    }

    [Fact(DisplayName = "Ultra-far distant terrain proxy темнее и суше чем far-профиль для того же surface")]
    public void ChunkSurfaceMeshFactory_BuildDistantTerrainProxy_UltraFarIsDarkerThanFarProfile()
    {
        var surface = new WorldMap.SurfaceBlock(
            0,
            2,
            0,
            BlockType.Stone,
            6,
            true,
            3,
            0,
            1,
            WorldMap.MaxSunVisibility,
            WorldMap.MaxDaylight,
            0);

        var farMesh = ChunkSurfaceMeshFactory.BuildDistantTerrainProxy(
            [surface],
            sampleStep: 1,
            ChunkSurfaceMeshFactory.DistantTerrainLightingProfile.Far);
        var ultraFarMesh = ChunkSurfaceMeshFactory.BuildDistantTerrainProxy(
            [surface],
            sampleStep: 1,
            ChunkSurfaceMeshFactory.DistantTerrainLightingProfile.UltraFar);

        Assert.NotEqual(farMesh.Colors, ultraFarMesh.Colors);
        Assert.True(farMesh.Colors[0] >= ultraFarMesh.Colors[0]);
    }

    [Fact(DisplayName = "EncodeDistantTerrainSunChannel покрывает far и ultra-far ветки")]
    public void ChunkSurfaceMeshFactory_EncodeDistantTerrainSunChannel_CoversFarAndUltraFarBranches()
    {
        var method = typeof(ChunkSurfaceMeshFactory).GetMethod("EncodeDistantTerrainSunChannel", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var topVisible = new WorldMap.SurfaceBlock(0, 2, 0, BlockType.Stone, 6, true, 2, 1, 1, WorldMap.MaxSunVisibility, WorldMap.MaxDaylight, WorldMap.MaxLocalLight);
        var hidden = topVisible with { TopVisible = false, LocalLight = 0, SunVisibility = 0 };

        var farValue = Assert.IsType<byte>(method!.Invoke(null, [topVisible, ChunkSurfaceMeshFactory.DistantTerrainLightingProfile.Far])!);
        var ultraValue = Assert.IsType<byte>(method.Invoke(null, [hidden, ChunkSurfaceMeshFactory.DistantTerrainLightingProfile.UltraFar])!);

        Assert.True(farValue > ultraValue);
    }

    [Fact(DisplayName = "Профиль distant terrain mesh зависит от качества графики")]
    public void DistantTerrainMeshProfile_DependsOnGraphicsQuality()
    {
        var platform = new FakeGamePlatform();
        var lowApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var mediumApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Medium }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var highApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));

        var distanceMethod = typeof(GameApp).GetMethod("GetWorldDistantTerrainMeshDistance", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var budgetMethod = typeof(GameApp).GetMethod("GetWorldDistantTerrainMeshBuildBudget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var sampleStepMethod = typeof(GameApp).GetMethod("GetWorldDistantTerrainMeshSampleStep", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(13f, (float)distanceMethod.Invoke(lowApp, null)!);
        Assert.Equal(26f, (float)distanceMethod.Invoke(mediumApp, null)!);
        Assert.Equal(190f, (float)distanceMethod.Invoke(highApp, null)!);
        Assert.Equal(1, (int)budgetMethod.Invoke(lowApp, null)!);
        Assert.Equal(2, (int)budgetMethod.Invoke(mediumApp, null)!);
        Assert.Equal(2, (int)budgetMethod.Invoke(highApp, null)!);
        Assert.Equal(4, (int)sampleStepMethod.Invoke(lowApp, null)!);
        Assert.Equal(3, (int)sampleStepMethod.Invoke(mediumApp, null)!);
        Assert.Equal(5, (int)sampleStepMethod.Invoke(highApp, null)!);
    }

    [Fact(DisplayName = "DrawWorld использует distant terrain mesh для ultra-far terrain чанка на high")]
    public void DrawWorld_UsesDistantTerrainMesh_ForUltraFarTerrainChunk()
    {
        var world = new WorldMap(256, 8, 64, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        for (var x = 168; x <= 175; x++)
        {
            for (var z = 8; z <= 15; z++)
            {
                world.SetBlock(x, 1, z, BlockType.Stone);
                world.SetBlock(x, 2, z, BlockType.Grass);
            }
        }

        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 2.2f, 4.5f), maxChunks: 512);
        Assert.True(world.TryGetChunkSurfaceBlocks(21, 1, out var surfaces));
        var detailedMesh = ChunkSurfaceMeshFactory.Build(world, surfaces);

        var platform = new FakeGamePlatform { Fps = 120 };
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        var player = new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f));
        SetPlayerPose(player, new Vector3(4.5f, 2.2f, 4.5f), Vector3.UnitX);
        SetPrivateField(app, "_player", player);

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        var call = Assert.Single(platform.DrawnTexturedChunkMeshes.Where(call => call.ChunkX == 21 && call.ChunkZ == 1));
        Assert.True(call.TriangleCount > 0);
        Assert.True(call.TriangleCount < detailedMesh.TriangleCount);
    }

    [Fact(DisplayName = "DrawWorld считает scene metrics и для distant terrain mesh")]
    public void DrawWorld_SceneMetrics_IncludeDistantTerrainMesh()
    {
        var world = new WorldMap(128, 8, 64, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        for (var x = 48; x <= 55; x++)
        {
            for (var z = 8; z <= 15; z++)
            {
                world.SetBlock(x, 1, z, BlockType.Stone);
                world.SetBlock(x, 2, z, BlockType.Grass);
            }
        }

        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 2.2f, 4.5f), maxChunks: 512);

        var platform = new FakeGamePlatform { Fps = 120 };
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        var player = new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f));
        SetPlayerPose(player, new Vector3(4.5f, 2.2f, 4.5f), Vector3.UnitX);
        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_sceneMetricsEnabled", true);

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        var count = (int)typeof(GameApp).GetField("_lastDrawnSurfaceCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        var hash = (ulong)typeof(GameApp).GetField("_lastDrawSceneHash", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;

        Assert.True(count >= 8);
        Assert.NotEqual(0UL, hash);
    }

    [Fact(DisplayName = "DrawWorld обновляет scene hash на восьмой terrain surface в distant mesh path")]
    public void DrawWorld_SceneMetrics_MixHashOnEveryEighthDistantTerrainSurface()
    {
        var world = new WorldMap(256, 8, 32, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        for (var x = 168; x <= 175; x++)
        {
            world.SetBlock(x, 1, 10, BlockType.Stone);
            world.SetBlock(x, 1, 11, BlockType.Stone);
        }

        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 2.2f, 10.5f), maxChunks: 512);

        var platform = new FakeGamePlatform { Fps = 120 };
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        var player = new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 10.5f));
        SetPlayerPose(player, new Vector3(4.5f, 2.2f, 10.5f), Vector3.UnitX);
        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_adaptiveRenderDistance", 190f);
        SetPrivateField(app, "_sceneMetricsEnabled", true);

        typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        var count = (int)typeof(GameApp).GetField("_lastDrawnSurfaceCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        var hash = (ulong)typeof(GameApp).GetField("_lastDrawSceneHash", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;

        Assert.Equal(16, count);
        Assert.NotEqual(0UL, hash);
    }

    [Fact(DisplayName = "StreamFarWorld держит дальний чанк резидентным и не дает ему сразу выгрузиться")]
    public void StreamFarWorld_KeepsFarChunkResident()
    {
        var world = new WorldMap(256, 16, 256, chunkSize: 8, seed: 0);
        var platform = new FakeGamePlatform { Fps = 120 };
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        var player = new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f));
        SetPlayerPose(player, player.Position, Vector3.UnitX);
        SetPrivateField(app, "_player", player);

        world.EnsureChunksAround(player.Position, radiusInChunks: 2);
        var streamFarWorld = typeof(GameApp).GetMethod("StreamFarWorld", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var distanceMethod = typeof(GameApp).GetMethod("GetFarWorldStreamingDistanceBlocks", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var distance = (float)distanceMethod.Invoke(app, null)!;
        var farAhead = new Vector3(player.Position.X + distance, player.Position.Y, player.Position.Z);
        var farChunkX = Math.Clamp((int)MathF.Floor(farAhead.X), 0, world.Width - 1) / world.ChunkSize;
        var farChunkZ = Math.Clamp((int)MathF.Floor(farAhead.Z), 0, world.Depth - 1) / world.ChunkSize;

        streamFarWorld.Invoke(app, [player.Position, 3, true, false, false]);

        Assert.True(world.IsChunkLoaded(farChunkX, farChunkZ));

        world.UnloadFarChunks(player.Position, keepRadiusInChunks: 3);
        Assert.True(world.IsChunkLoaded(farChunkX, farChunkZ));
    }

    [Fact(DisplayName = "GetFarWorldStreamingRadius всегда расширяет near-radius")]
    public void GetFarWorldStreamingRadius_AlwaysExceedsNearRadius()
    {
        var world = new WorldMap(64, 16, 64, chunkSize: 8, seed: 0);
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low }, new FakeGamePlatform(), world);
        var method = typeof(GameApp).GetMethod("GetFarWorldStreamingRadius", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.True((int)method.Invoke(app, [1])! > 1);
        Assert.True((int)method.Invoke(app, [8])! > 8);
        Assert.True((int)method.Invoke(app, [16])! > 16);
    }

    [Fact(DisplayName = "StreamFarWorldAnchor корректно обрабатывает нулевой радиус и нулевые бюджеты")]
    public void StreamFarWorldAnchor_HandlesZeroRadiusAndBudgets()
    {
        var world = new WorldMap(64, 16, 64, chunkSize: 8, seed: 0);
        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        var method = typeof(GameApp).GetMethod("StreamFarWorldAnchor", BindingFlags.Instance | BindingFlags.NonPublic)!;

        method.Invoke(app, [new Vector3(4f, 0f, 4f), 0, 3, 3, false]);
        Assert.Equal(0, world.LoadedChunkCount);

        world.EnsureChunksAround(new Vector3(4f, 0f, 4f), radiusInChunks: 0);
        method.Invoke(app, [new Vector3(4f, 0f, 4f), 1, 0, 0, false]);
        Assert.Equal(1, world.LoadedChunkCount);
    }

    [Fact(DisplayName = "Far-world budgets корректно различаются по качеству и under-pressure")]
    public void FarWorldBudgets_DependsOnQualityAndPressure()
    {
        var platform = new FakeGamePlatform();
        var lowApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low }, platform, new WorldMap(32, 16, 32, chunkSize: 8, seed: 0));
        var mediumApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Medium }, platform, new WorldMap(32, 16, 32, chunkSize: 8, seed: 0));
        var highApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, new WorldMap(32, 16, 32, chunkSize: 8, seed: 0));
        var chunkBudgetMethod = typeof(GameApp).GetMethod("GetFarWorldChunkBudget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var surfaceBudgetMethod = typeof(GameApp).GetMethod("GetFarWorldSurfaceBudget", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(0, (int)chunkBudgetMethod.Invoke(lowApp, [true])!);
        Assert.Equal(1, (int)surfaceBudgetMethod.Invoke(lowApp, [false])!);
        Assert.Equal(0, (int)surfaceBudgetMethod.Invoke(lowApp, [true])!);

        Assert.Equal(1, (int)chunkBudgetMethod.Invoke(mediumApp, [true])!);
        Assert.Equal(2, (int)chunkBudgetMethod.Invoke(mediumApp, [false])!);
        Assert.Equal(1, (int)surfaceBudgetMethod.Invoke(mediumApp, [true])!);
        Assert.Equal(1, (int)surfaceBudgetMethod.Invoke(mediumApp, [false])!);

        Assert.Equal(0, (int)chunkBudgetMethod.Invoke(highApp, [true])!);
        Assert.Equal(2, (int)chunkBudgetMethod.Invoke(highApp, [false])!);
        Assert.Equal(0, (int)surfaceBudgetMethod.Invoke(highApp, [true])!);
        Assert.Equal(1, (int)surfaceBudgetMethod.Invoke(highApp, [false])!);
    }

    [Fact(DisplayName = "Render helper-ветки для cull/hash/build покрывают оба исхода")]
    public void RenderHelpers_CoverCullHashAndBuildBranches()
    {
        var cullMethod = typeof(GameApp).GetMethod("ShouldCullAfterKeep", BindingFlags.Static | BindingFlags.NonPublic)!;
        var atmosphericCullMethod = typeof(GameApp).GetMethod("ShouldCullAtmosphericNonTerrain", BindingFlags.Static | BindingFlags.NonPublic)!;
        var mixHashMethod = typeof(GameApp).GetMethod("ShouldMixSceneHash", BindingFlags.Static | BindingFlags.NonPublic)!;
        var skipBuildMethod = typeof(GameApp).GetMethod("ShouldSkipDistantTerrainMeshBuild", BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.True((bool)cullMethod.Invoke(null, [0f])!);
        Assert.False((bool)cullMethod.Invoke(null, [0.01f])!);

        Assert.True((bool)atmosphericCullMethod.Invoke(null, [0.95f, BlockType.Wood])!);
        Assert.False((bool)atmosphericCullMethod.Invoke(null, [0.91f, BlockType.Wood])!);
        Assert.False((bool)atmosphericCullMethod.Invoke(null, [0.95f, BlockType.Stone])!);

        Assert.False((bool)mixHashMethod.Invoke(null, [7])!);
        Assert.True((bool)mixHashMethod.Invoke(null, [8])!);

        Assert.True((bool)skipBuildMethod.Invoke(null, [0])!);
        Assert.False((bool)skipBuildMethod.Invoke(null, [1])!);
    }

    [Fact(DisplayName = "ComputeAdaptiveRenderRise использует отдельный slow-rise для high 190 мира")]
    public void ComputeAdaptiveRenderRise_UsesHighRangeSlowRampAndDefaultFallback()
    {
        var method = typeof(GameApp).GetMethod("ComputeAdaptiveRenderRise", BindingFlags.Static | BindingFlags.NonPublic)!;

        var highRise = (float)method.Invoke(null, [GraphicsQuality.High, 190f, 100f])!;
        var mediumRise = (float)method.Invoke(null, [GraphicsQuality.Medium, 80f, 60f])!;
        var highNearRise = (float)method.Invoke(null, [GraphicsQuality.High, 88f, 60f])!;

        Assert.InRange(highRise, 0.12f, 0.24f);
        Assert.InRange(mediumRise, 0.6f, 1.2f);
        Assert.InRange(highNearRise, 0.6f, 1.2f);
    }

    [Fact(DisplayName = "ShouldRunFarWorldStreamingStep разрежает far-world streaming по quality mode")]
    public void ShouldRunFarWorldStreamingStep_UsesQualityCadence()
    {
        var platform = new FakeGamePlatform();
        var highApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, new WorldMap(32, 16, 32, chunkSize: 8, seed: 0));
        var lowApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low }, platform, new WorldMap(32, 16, 32, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("ShouldRunFarWorldStreamingStep", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.False((bool)method.Invoke(highApp, null)!);
        Assert.True((bool)method.Invoke(highApp, null)!);

        Assert.False((bool)method.Invoke(lowApp, null)!);
        Assert.False((bool)method.Invoke(lowApp, null)!);
        Assert.False((bool)method.Invoke(lowApp, null)!);
        Assert.True((bool)method.Invoke(lowApp, null)!);
    }

    [Fact(DisplayName = "UpdateWorldSceneMetrics обновляет count и hash только на каждом восьмом surface")]
    public void UpdateWorldSceneMetrics_UpdatesOnlyOnEighthSurface()
    {
        var method = typeof(GameApp).GetMethod("UpdateWorldSceneMetrics", BindingFlags.Static | BindingFlags.NonPublic)!;
        var surface = new WorldMap.SurfaceBlock(12, 3, 9, BlockType.Stone, 4, true, 0);
        var args = new object[] { 0UL, 7, surface, true };

        var hash = (ulong)method.Invoke(null, args)!;

        Assert.NotEqual(0UL, hash);
        Assert.Equal(8, (int)args[1]);

        args = [hash, 3, surface, false];
        hash = (ulong)method.Invoke(null, args)!;
        Assert.NotEqual(0UL, hash);
        Assert.Equal(3, (int)args[1]);
    }

    [Fact(DisplayName = "TryBuildDistantTerrainChunkMesh возвращает false при нулевом budget")]
    public void TryBuildDistantTerrainChunkMesh_ReturnsFalseWhenBudgetIsZero()
    {
        var world = new WorldMap(64, 8, 64, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        for (var x = 32; x < 40; x++)
        {
            world.SetBlock(x, 1, 8, BlockType.Stone);
        }

        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 2.2f, 8.5f), maxChunks: 128);
        Assert.True(world.TryGetChunkSurfaceState(4, 1, out var surfaceBlocks, out var revision, out _));

        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), world);
        var method = typeof(GameApp).GetMethod("TryBuildDistantTerrainChunkMesh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var args = new object[] { (4, 1), revision, surfaceBlocks, ChunkSurfaceMeshFactory.DistantTerrainLightingProfile.Far, 0, 0, null! };

        var result = (bool)method.Invoke(app, args)!;

        Assert.False(result);
    }

    [Fact(DisplayName = "ShouldUseChunkAtlasMeshForBand включает far mesh только для terrain-dominant чанка")]
    public void ShouldUseChunkAtlasMeshForBand_UsesFarMeshOnlyForTerrainDominantChunk()
    {
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("ShouldUseChunkAtlasMeshForBand", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var terrainChunk =
            new[]
            {
                new WorldMap.SurfaceBlock(1, 1, 1, BlockType.Stone, 6, true, 0),
                new WorldMap.SurfaceBlock(2, 1, 1, BlockType.Grass, 6, true, 0),
                new WorldMap.SurfaceBlock(3, 1, 1, BlockType.Dirt, 6, true, 0),
                new WorldMap.SurfaceBlock(4, 1, 1, BlockType.Wood, 6, true, 0),
                new WorldMap.SurfaceBlock(5, 1, 1, BlockType.Stone, 6, true, 0)
            };
        var foliageChunk =
            new[]
            {
                new WorldMap.SurfaceBlock(1, 1, 1, BlockType.Leaves, 6, true, 0),
                new WorldMap.SurfaceBlock(2, 1, 1, BlockType.Wood, 6, true, 0),
                new WorldMap.SurfaceBlock(3, 1, 1, BlockType.Leaves, 6, true, 0),
                new WorldMap.SurfaceBlock(4, 1, 1, BlockType.Wood, 6, true, 0),
                new WorldMap.SurfaceBlock(5, 1, 1, BlockType.Leaves, 6, true, 0)
            };

        var profile = typeof(GameApp).GetMethod("BuildWorldVisibilityProfile", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [8f, 12f, 40f, 46f])!;
        Assert.True((bool)method!.Invoke(app, [Enum.Parse(typeof(GameApp).GetNestedType("WorldVisibilityBand", BindingFlags.NonPublic)!, "Far"), 28f, terrainChunk, profile])!);
        Assert.False((bool)method.Invoke(app, [Enum.Parse(typeof(GameApp).GetNestedType("WorldVisibilityBand", BindingFlags.NonPublic)!, "Far"), 28f, foliageChunk, profile])!);
    }

    [Fact(DisplayName = "ShouldUseChunkAtlasMeshForBand возвращает false для чанка без atlas-блоков")]
    public void ShouldUseChunkAtlasMeshForBand_ReturnsFalse_WhenChunkHasNoAtlasBlocks()
    {
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("ShouldUseChunkAtlasMeshForBand", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var farBand = Enum.Parse(typeof(GameApp).GetNestedType("WorldVisibilityBand", BindingFlags.NonPublic)!, "Far");
        var chunk =
            new[]
            {
                new WorldMap.SurfaceBlock(1, 1, 1, (BlockType)99, 6, true, 0),
                new WorldMap.SurfaceBlock(2, 1, 1, (BlockType)98, 6, true, 0)
            };

        var profile = typeof(GameApp).GetMethod("BuildWorldVisibilityProfile", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [8f, 12f, 40f, 46f])!;
        Assert.False((bool)method!.Invoke(app, [farBand, 20f, chunk, profile])!);
    }

    [Fact(DisplayName = "ShouldUseChunkAtlasMeshForBand держит terrain mesh в overlap-зоне atmospheric")]
    public void ShouldUseChunkAtlasMeshForBand_KeepsTerrainMeshInAtmosphericOverlap()
    {
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("ShouldUseChunkAtlasMeshForBand", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var profile = typeof(GameApp).GetMethod("BuildWorldVisibilityProfile", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [8f, 12f, 40f, 46f])!;
        var atmosphericBand = Enum.Parse(typeof(GameApp).GetNestedType("WorldVisibilityBand", BindingFlags.NonPublic)!, "Atmospheric");
        var terrainChunk =
            new[]
            {
                new WorldMap.SurfaceBlock(1, 1, 1, BlockType.Stone, 6, true, 0),
                new WorldMap.SurfaceBlock(2, 1, 1, BlockType.Grass, 6, true, 0),
                new WorldMap.SurfaceBlock(3, 1, 1, BlockType.Dirt, 6, true, 0),
                new WorldMap.SurfaceBlock(4, 1, 1, BlockType.Stone, 6, true, 0),
                new WorldMap.SurfaceBlock(5, 1, 1, BlockType.Wood, 6, true, 0)
            };

        Assert.True((bool)method!.Invoke(app, [atmosphericBand, 40.2f, terrainChunk, profile])!);
    }

    [Fact(DisplayName = "ShouldUseDistantTerrainMeshForBand включает только дальний terrain за пределом detailed far mesh")]
    public void ShouldUseDistantTerrainMeshForBand_UsesOnlyFarTerrainBeyondDetailedRange()
    {
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("ShouldUseDistantTerrainMeshForBand", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var profile = typeof(GameApp).GetMethod("BuildWorldVisibilityProfile", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [22f, 22f, 190f, 196f])!;
        var farBand = Enum.Parse(typeof(GameApp).GetNestedType("WorldVisibilityBand", BindingFlags.NonPublic)!, "Far");
        var terrainChunk =
            new[]
            {
                new WorldMap.SurfaceBlock(1, 1, 1, BlockType.Stone, 6, true, 0),
                new WorldMap.SurfaceBlock(2, 1, 1, BlockType.Grass, 6, true, 0),
                new WorldMap.SurfaceBlock(3, 1, 1, BlockType.Dirt, 6, true, 0),
                new WorldMap.SurfaceBlock(4, 1, 1, BlockType.Stone, 6, true, 0),
                new WorldMap.SurfaceBlock(5, 1, 1, BlockType.Wood, 6, true, 0)
            };

        Assert.False((bool)method!.Invoke(app, [farBand, 24f, terrainChunk, profile])!);
        Assert.True((bool)method.Invoke(app, [farBand, 96f, terrainChunk, profile])!);
    }

    [Fact(DisplayName = "ShouldUseDistantTerrainMeshForBand отключает distant mesh за пределом его дальности")]
    public void ShouldUseDistantTerrainMeshForBand_ReturnsFalse_BeyondDistantMeshDistance()
    {
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("ShouldUseDistantTerrainMeshForBand", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var profile = typeof(GameApp).GetMethod("BuildWorldVisibilityProfile", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [22f, 22f, 190f, 196f])!;
        var atmosphericBand = Enum.Parse(typeof(GameApp).GetNestedType("WorldVisibilityBand", BindingFlags.NonPublic)!, "Atmospheric");
        var terrainChunk =
            new[]
            {
                new WorldMap.SurfaceBlock(1, 1, 1, BlockType.Stone, 6, true, 0),
                new WorldMap.SurfaceBlock(2, 1, 1, BlockType.Grass, 6, true, 0),
                new WorldMap.SurfaceBlock(3, 1, 1, BlockType.Dirt, 6, true, 0),
                new WorldMap.SurfaceBlock(4, 1, 1, BlockType.Stone, 6, true, 0),
                new WorldMap.SurfaceBlock(5, 1, 1, BlockType.Wood, 6, true, 0)
            };

        Assert.True((bool)method!.Invoke(app, [atmosphericBand, 191f, terrainChunk, profile])!);
        Assert.False((bool)method.Invoke(app, [atmosphericBand, 220f, terrainChunk, profile])!);
    }

    [Fact(DisplayName = "TryDrawDistantTerrainChunkMesh не строит distant mesh без валидной поверхности")]
    public void TryDrawDistantTerrainChunkMesh_ReturnsFalse_ForInvalidSurfaceState()
    {
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("TryDrawDistantTerrainChunkMesh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var farBand = Enum.Parse(typeof(GameApp).GetNestedType("WorldVisibilityBand", BindingFlags.NonPublic)!, "Far");
        object[] args = [0, 0, 0, Array.Empty<WorldMap.SurfaceBlock>(), farBand, 1];

        var drawn = (bool)method.Invoke(app, args)!;

        Assert.False(drawn);
        Assert.Equal(1, (int)args[5]);
    }

    [Fact(DisplayName = "TryDrawDistantTerrainChunkMesh удаляет stale cache если distant proxy пуст")]
    public void TryDrawDistantTerrainChunkMesh_RemovesCache_WhenProxyIsEmpty()
    {
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var cache = GetWorldDistantChunkMeshCache(app);
        cache[(0, 0)] = CreateCachedChunkMesh(revision: 1);

        IReadOnlyList<WorldMap.SurfaceBlock> surfaces =
        [
            new WorldMap.SurfaceBlock(0, 2, 0, BlockType.Wood, 6, true, 5, 0, 0, WorldMap.MaxSunVisibility, WorldMap.MaxDaylight, 0)
        ];

        var method = typeof(GameApp).GetMethod("TryDrawDistantTerrainChunkMesh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var farBand = Enum.Parse(typeof(GameApp).GetNestedType("WorldVisibilityBand", BindingFlags.NonPublic)!, "Far");
        object[] args = [0, 0, 2, surfaces, farBand, 1];

        var drawn = (bool)method.Invoke(app, args)!;

        Assert.False(drawn);
        Assert.False(cache.Contains((0, 0)));
        Assert.Equal(1, (int)args[5]);
    }

    [Fact(DisplayName = "TryDrawDistantTerrainChunkMesh перестраивает cache при смене far lighting profile")]
    public void TryDrawDistantTerrainChunkMesh_RebuildsCache_WhenLightingProfileChanges()
    {
        var world = new WorldMap(16, 8, 16, chunkSize: 8, seed: 0);
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), world);
        var method = typeof(GameApp).GetMethod("TryDrawDistantTerrainChunkMesh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var farBand = Enum.Parse(typeof(GameApp).GetNestedType("WorldVisibilityBand", BindingFlags.NonPublic)!, "Far");
        var atmosphericBand = Enum.Parse(typeof(GameApp).GetNestedType("WorldVisibilityBand", BindingFlags.NonPublic)!, "Atmospheric");

        IReadOnlyList<WorldMap.SurfaceBlock> surfaces =
        [
            new WorldMap.SurfaceBlock(0, 2, 0, BlockType.Stone, 6, true, 2, 1, 2, WorldMap.MaxSunVisibility, WorldMap.MaxDaylight, WorldMap.MaxLocalLight)
        ];

        object[] farArgs = [0, 0, 3, surfaces, farBand, 2];
        Assert.True((bool)method.Invoke(app, farArgs)!);
        Assert.Equal(1, (int)farArgs[5]);

        var cache = GetWorldDistantChunkMeshCache(app);
        Assert.Equal(0, GetCachedChunkMeshVariant(cache[(0, 0)]!));

        object[] atmosphericArgs = [0, 0, 3, surfaces, atmosphericBand, 2];
        Assert.True((bool)method.Invoke(app, atmosphericArgs)!);
        Assert.Equal(1, (int)atmosphericArgs[5]);

        Assert.Equal(1, GetCachedChunkMeshVariant(cache[(0, 0)]!));
    }

    [Fact(DisplayName = "TrimWorldDistantChunkMeshCache удаляет stale distant mesh")]
    public void TrimWorldDistantChunkMeshCache_RemovesStaleEntries()
    {
        var world = new WorldMap(8, 8, 8, chunkSize: 8, seed: 0);
        world.SetBlock(1, 1, 1, BlockType.Stone);

        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), world);
        var cache = GetWorldDistantChunkMeshCache(app);
        cache[(0, 0)] = CreateCachedChunkMesh(revision: 1);
        cache[(1, 0)] = CreateCachedChunkMesh(revision: 1);

        var method = typeof(GameApp).GetMethod("TrimWorldDistantChunkMeshCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(app, [0, 0, 0, 0]);

        Assert.True(cache.Contains((0, 0)));
        Assert.False(cache.Contains((1, 0)));
    }

    [Fact(DisplayName = "TryDrawChunkAtlasMesh не строит новый mesh без бюджета и оставляет fallback")]
    public void TryDrawChunkAtlasMesh_WithoutBudget_DoesNotBuildNewMesh()
    {
        var world = new WorldMap(8, 8, 8, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(4, 1, 4, BlockType.Stone);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 2.2f, 4.5f), maxChunks: 16);
        Assert.True(world.TryGetChunkSurfaceState(0, 0, out var surfaceBlocks, out var surfaceRevision, out var surfaceDirty));
        Assert.False(surfaceDirty);

        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);

        var method = typeof(GameApp).GetMethod("TryDrawChunkAtlasMesh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object[] args = [0, 0, surfaceRevision, surfaceBlocks, 0];

        var drawn = (bool)method.Invoke(app, args)!;

        Assert.False(drawn);
        Assert.Equal(0, platform.DrawTexturedChunkMeshCalls);
    }

    [Fact(DisplayName = "TryDrawChunkAtlasMesh возвращает false для пустого surface списка")]
    public void TryDrawChunkAtlasMesh_ReturnsFalse_ForEmptySurfaceList()
    {
        var world = new WorldMap(8, 8, 8, chunkSize: 8, seed: 0);
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), world);
        var method = typeof(GameApp).GetMethod("TryDrawChunkAtlasMesh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IReadOnlyList<WorldMap.SurfaceBlock> empty = [];
        object[] args = [0, 0, 0, empty, 1];

        var drawn = (bool)method.Invoke(app, args)!;

        Assert.False(drawn);
    }

    [Fact(DisplayName = "BuildLodBlendedColor покрывает near-mid blend ветку")]
    public void BuildLodBlendedColor_CoversNearMidBlendBranch()
    {
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("BuildLodBlendedColor", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var lodType = typeof(GameApp).GetNestedType("LodBlendWeights", BindingFlags.NonPublic);
        var visibilityType = typeof(GameApp).GetNestedType("VisibilityBlendWeights", BindingFlags.NonPublic);
        Assert.NotNull(lodType);
        Assert.NotNull(visibilityType);
        var lodCtor = lodType!.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single();
        var visibilityCtor = visibilityType!.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single();
        var lod = lodCtor.Invoke([0.4f, 0.6f, 0f]);
        var visibility = visibilityCtor.Invoke([0.52f, 0.48f, 0f, 0f]);
        var surface = new WorldMap.SurfaceBlock(2, 1, 2, BlockType.Stone, 6, true, 2);

        var color = Assert.IsType<Color>(method!.Invoke(app, [new Color(134, 129, 121, 255), surface, 10f, 0.2f, lod, visibility])!);

        Assert.NotEqual(0, color.A);
    }

    [Fact(DisplayName = "FlushWorldTexturedBlockInstances пропускает пустые батчи и очищает словарь")]
    public void FlushWorldTexturedBlockInstances_SkipsEmptyBatch()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));

        var field = typeof(GameApp).GetField("_worldTexturedBlockBatches", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var batches = (Dictionary<BlockType, List<Matrix4x4>>)field.GetValue(app)!;
        batches[BlockType.Stone] = [];

        typeof(GameApp).GetMethod("FlushWorldTexturedBlockInstances", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

        Assert.Empty(batches);
    }

    [Fact(DisplayName = "TrimWorldChunkMeshCache ничего не удаляет для loaded чанка внутри границ")]
    public void TrimWorldChunkMeshCache_LeavesLoadedInBoundsChunk()
    {
        var world = new WorldMap(8, 8, 8, chunkSize: 8, seed: 0);
        world.EnsureChunksAround(new Vector3(4.5f, 2.2f, 4.5f), radiusInChunks: 1);
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), world);
        var cache = GetWorldChunkMeshCache(app);
        cache[(0, 0)] = CreateCachedChunkMesh(1);

        typeof(GameApp).GetMethod("TrimWorldChunkMeshCache", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [0, 0, 0, 0]);

        Assert.Single(cache);
        Assert.True(cache.Contains((0, 0)));
    }

    [Fact(DisplayName = "TrimWorldChunkMeshCache удаляет несколько stale чанков")]
    public void TrimWorldChunkMeshCache_RemovesStaleEntries()
    {
        var world = new WorldMap(16, 8, 16, chunkSize: 8, seed: 0);
        world.EnsureChunksAround(new Vector3(4.5f, 2.2f, 4.5f), radiusInChunks: 0);
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, new FakeGamePlatform(), world);
        var cache = GetWorldChunkMeshCache(app);
        cache[(0, 0)] = CreateCachedChunkMesh(1);
        cache[(1, 0)] = CreateCachedChunkMesh(2);
        cache[(0, 1)] = CreateCachedChunkMesh(3);

        typeof(GameApp).GetMethod("TrimWorldChunkMeshCache", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [0, 0, 0, 0]);

        Assert.Single(cache);
        Assert.True(cache.Contains((0, 0)));
        Assert.False(cache.Contains((1, 0)));
        Assert.False(cache.Contains((0, 1)));
    }

    [Fact(DisplayName = "GetLeafDensityDelta покрывает default ветку clusterNoise")]
    public void GetLeafDensityDelta_ReturnsZeroForDefaultClusterNoiseBranch()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("GetLeafDensityDelta", BindingFlags.Instance | BindingFlags.NonPublic)!;

        for (var x = 0; x < 8; x++)
        {
            for (var z = 0; z < 8; z++)
            {
                var surface = new WorldMap.SurfaceBlock(x, 2, z, BlockType.Leaves, 5, true, 5, 0, 0, WorldMap.MaxSunVisibility);
                var delta = (int)method.Invoke(app, [surface, 5f])!;
                if (delta == 0)
                {
                    Assert.Equal(0, delta);
                    return;
                }
            }
        }

        throw new Xunit.Sdk.XunitException("Не найдена координата для default ветки clusterNoise.");
    }

    [Fact(DisplayName = "GetLeafDensityDelta возвращает 2 для дальней листвы с clusterNoise 0")]
    public void GetLeafDensityDelta_ReturnsTwoForFarClusterZeroBranch()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("GetLeafDensityDelta", BindingFlags.Instance | BindingFlags.NonPublic)!;

        for (var x = 0; x < 8; x++)
        {
            for (var z = 0; z < 8; z++)
            {
                var surface = new WorldMap.SurfaceBlock(x, 2, z, BlockType.Leaves, 5, true, 5, 0, 0, WorldMap.MaxSunVisibility);
                var delta = (int)method.Invoke(app, [surface, 12f])!;
                if (delta == 2)
                {
                    Assert.Equal(2, delta);
                    return;
                }
            }
        }

        throw new Xunit.Sdk.XunitException("Не найдена координата для дальней ветки clusterNoise == 0.");
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }

    private static void SetPlayerPose(PlayerController player, Vector3 position, Vector3 lookDirection)
    {
        typeof(PlayerController).GetMethod("SetPose", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(player, [position, lookDirection]);
    }

    private static IDictionary GetWorldChunkMeshCache(GameApp app)
    {
        return (IDictionary)typeof(GameApp).GetField("_worldChunkMeshCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
    }

    private static IDictionary GetWorldDistantChunkMeshCache(GameApp app)
    {
        return (IDictionary)typeof(GameApp).GetField("_worldDistantChunkMeshCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
    }

    private static object CreateCachedChunkMesh(int revision)
    {
        var mesh = new ChunkSurfaceMeshData([0f, 0f, 0f], [0f, 0f], [0f, 1f, 0f], [255, 255, 255, 255], [0, 0, 0]);
        var type = typeof(GameApp).GetNestedType("CachedChunkMesh", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type, revision, 0, mesh)!;
    }

    private static int GetCachedChunkMeshVariant(object cached)
    {
        return (int)cached.GetType().GetProperty("Variant", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(cached)!;
    }
}
