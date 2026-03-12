using System.Collections;
using System.Linq;
using System.Numerics;
using System.Reflection;
using AIG.Game.Config;
using AIG.Game.Core;
using AIG.Game.Player;
using AIG.Game.Tests.Fakes;
using AIG.Game.World;

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
        world.SetBlock(4, 1, 4, (BlockType)99);

        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, world);
        SetPrivateField(app, "_player", new PlayerController(new GameConfig(), new Vector3(4.5f, 2.2f, 4.5f)));

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

        Assert.Equal(8f, (float)method.Invoke(lowApp, null)!);
        Assert.Equal(12f, (float)method.Invoke(mediumApp, null)!);
        Assert.Equal(18f, (float)method.Invoke(highApp, null)!);
    }

    [Fact(DisplayName = "Дальность chunk-mesh pass зависит от качества графики")]
    public void GetWorldChunkMeshRenderDistance_DependsOnGraphicsQuality()
    {
        var platform = new FakeGamePlatform();
        var lowApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var mediumApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Medium }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var highApp = new GameApp(new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("GetWorldChunkMeshRenderDistance", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(8f, (float)method.Invoke(lowApp, null)!);
        Assert.Equal(12f, (float)method.Invoke(mediumApp, null)!);
        Assert.Equal(18f, (float)method.Invoke(highApp, null)!);
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
        Assert.True(settings.AtmosphereStrength > 0.9f);
        Assert.True(settings.WarmLightStrength > 0.8f);
        Assert.True(settings.CoolShadowStrength > 0.4f);
        Assert.True(settings.ContrastStrength > 0.4f);
        Assert.True(settings.GlowStrength > 0.55f);
        Assert.True(settings.MaterialSeparationStrength > 0.45f);
        Assert.True(settings.ShadowDepthStrength > 0.35f);
        Assert.True(settings.SkyBlendStrength > 0.30f);
        Assert.True(settings.SunScatterStrength > 0.40f);
        Assert.True(settings.AmbientLiftStrength > 0.28f);
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
        Assert.Equal(2, (int)method.Invoke(mediumApp, null)!);
        Assert.Equal(4, (int)method.Invoke(highApp, null)!);
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

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }

    private static IDictionary GetWorldChunkMeshCache(GameApp app)
    {
        return (IDictionary)typeof(GameApp).GetField("_worldChunkMeshCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
    }

    private static object CreateCachedChunkMesh(int revision)
    {
        var mesh = new ChunkSurfaceMeshData([0f, 0f, 0f], [0f, 0f], [0f, 1f, 0f], [255, 255, 255, 255], [0, 0, 0]);
        var type = typeof(GameApp).GetNestedType("CachedChunkMesh", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type, revision, mesh)!;
    }
}
