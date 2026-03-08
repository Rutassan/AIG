using System.Numerics;
using System.Reflection;
using System.Threading;
using AIG.Game.Config;
using AIG.Game.Player;
using AIG.Game.World;
using AIG.Game.World.Chunks;

namespace AIG.Game.Tests;

public sealed class WorldAndPlayerTests
{
    private static bool WaitUntil(Func<bool> condition, int maxAttempts = 120, int sleepMs = 5)
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(sleepMs);
        }

        return condition();
    }

    [Fact(DisplayName = "Генерация мира: нижний слой камень, верхний слой земля, выше воздух")]
    public void World_GeneratesExpectedFlatLayers()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8);

        Assert.Equal(BlockType.Stone, world.GetBlock(3, 0, 3));
        Assert.Equal(BlockType.Dirt, world.GetBlock(3, 1, 3));
        Assert.Equal(BlockType.Air, world.GetBlock(3, 2, 3));
    }

    [Fact(DisplayName = "Мир возвращает воздух и не пишет блок за пределами по всем осям")]
    public void World_BoundsChecks_WorkOnAllAxes()
    {
        var world = new WorldMap(width: 4, height: 4, depth: 4);

        Assert.Equal(BlockType.Air, world.GetBlock(-1, 1, 1));
        Assert.Equal(BlockType.Air, world.GetBlock(4, 1, 1));
        Assert.Equal(BlockType.Air, world.GetBlock(1, -1, 1));
        Assert.Equal(BlockType.Air, world.GetBlock(1, 4, 1));
        Assert.Equal(BlockType.Air, world.GetBlock(1, 1, -1));
        Assert.Equal(BlockType.Air, world.GetBlock(1, 1, 4));

        world.SetBlock(-1, 2, 2, BlockType.Stone);
        world.SetBlock(4, 2, 2, BlockType.Stone);
        world.SetBlock(2, -1, 2, BlockType.Stone);
        world.SetBlock(2, 4, 2, BlockType.Stone);
        world.SetBlock(2, 2, -1, BlockType.Stone);
        world.SetBlock(2, 2, 4, BlockType.Stone);

        Assert.Equal(BlockType.Air, world.GetBlock(2, 2, 2));
    }

    [Fact(DisplayName = "Чанковый доступ корректно работает на границе чанка")]
    public void World_ChunkBoundaryAccess_Works()
    {
        var world = new WorldMap(width: 64, height: 8, depth: 64, chunkSize: 16, seed: 0);
        world.SetBlock(15, 2, 15, BlockType.Stone);
        world.SetBlock(16, 2, 16, BlockType.Dirt);

        Assert.Equal(BlockType.Stone, world.GetBlock(15, 2, 15));
        Assert.Equal(BlockType.Dirt, world.GetBlock(16, 2, 16));
    }

    [Fact(DisplayName = "Генерация по seed повторяема между экземплярами мира")]
    public void World_SeedGeneration_IsDeterministic()
    {
        var worldA = new WorldMap(width: 64, height: 8, depth: 64, chunkSize: 16, seed: 12345);
        var worldB = new WorldMap(width: 64, height: 8, depth: 64, chunkSize: 16, seed: 12345);

        Assert.Equal(worldA.GetBlock(10, 2, 10), worldB.GetBlock(10, 2, 10));
        Assert.Equal(worldA.GetBlock(22, 2, 31), worldB.GetBlock(22, 2, 31));
        Assert.Equal(worldA.GetBlock(40, 2, 5), worldB.GetBlock(40, 2, 5));
    }

    [Fact(DisplayName = "Лесной мир имеет травяной верхний слой на поверхности")]
    public void World_SeededGeneration_UsesGrassOnTopLayer()
    {
        var world = new WorldMap(width: 64, height: 32, depth: 64, chunkSize: 16, seed: 777);
        var x = 20;
        var z = 20;
        var topY = world.GetTerrainTopY(x, z);

        Assert.Equal(BlockType.Grass, world.GetBlock(x, topY, z));
        Assert.NotEqual(BlockType.Air, world.GetBlock(x, Math.Max(0, topY - 1), z));
    }

    [Fact(DisplayName = "Стриминг чанков загружает, помечает и выгружает дальние чанки")]
    public void World_ChunkStreaming_LoadAndUnload_Works()
    {
        var world = new WorldMap(width: 96, height: 32, depth: 96, chunkSize: 16, seed: 777);

        Assert.Equal(0, world.LoadedChunkCount);
        Assert.False(world.IsChunkLoaded(1, 1));

        world.UnloadFarChunks(new Vector3(0f, 0f, 0f), keepRadiusInChunks: 0);
        Assert.Equal(0, world.LoadedChunkCount);

        world.EnsureChunksAround(new Vector3(24f, 0f, 24f), radiusInChunks: 1);

        Assert.True(world.LoadedChunkCount >= 4);
        Assert.True(world.IsChunkLoaded(1, 1));

        var loadedBeforeUnload = world.LoadedChunkCount;
        world.UnloadFarChunks(new Vector3(8f, 0f, 8f), keepRadiusInChunks: 0);
        Assert.True(world.LoadedChunkCount < loadedBeforeUnload);

        world.UnloadFarChunks(new Vector3(8f, 0f, 8f), keepRadiusInChunks: -1);
        Assert.Equal(0, world.LoadedChunkCount);
    }

    [Fact(DisplayName = "Budgeted-стриминг чанков догружает мир по лимиту и не превышает бюджет")]
    public void World_EnsureChunksAroundBudgeted_LoadsChunksIncrementally()
    {
        var world = new WorldMap(width: 96, height: 32, depth: 96, chunkSize: 16, seed: 777);

        Assert.Equal(0, world.EnsureChunksAroundBudgeted(24, 24, radiusInChunks: 1, maxNewChunks: 0));
        Assert.Equal(0, world.LoadedChunkCount);

        var createdFirstStep = world.EnsureChunksAroundBudgeted(24, 24, radiusInChunks: 1, maxNewChunks: 2);
        Assert.Equal(2, createdFirstStep);
        Assert.Equal(2, world.LoadedChunkCount);

        var createdSecondStep = world.EnsureChunksAroundBudgeted(24, 24, radiusInChunks: 1, maxNewChunks: 20);
        Assert.Equal(7, createdSecondStep);
        Assert.Equal(9, world.LoadedChunkCount);

        var zeroDepthWorld = new WorldMap(width: 16, height: 8, depth: 0, chunkSize: 8, seed: 0);
        Assert.Equal(0, zeroDepthWorld.EnsureChunksAroundBudgeted(0, 0, radiusInChunks: 1, maxNewChunks: 3));
    }

    [Fact(DisplayName = "Async-стриминг чанков загружает чанк и сохраняет override после выгрузки")]
    public void World_AsyncChunkStreaming_LoadsChunkAndKeepsOverride()
    {
        var world = new WorldMap(width: 96, height: 40, depth: 96, chunkSize: 16, seed: 777);
        world.SetBlock(20, 9, 20, BlockType.Wood);
        world.UnloadFarChunks(new Vector3(0f, 0f, 0f), keepRadiusInChunks: -1);
        Assert.Equal(0, world.LoadedChunkCount);

        Assert.Equal(0, world.EnsureChunksAroundBudgetedAsync(20, 20, radiusInChunks: 0, maxNewChunks: 0));
        var queued = world.EnsureChunksAroundBudgetedAsync(20, 20, radiusInChunks: 0, maxNewChunks: 4);
        Assert.Equal(1, queued);

        var loaded = WaitUntil(() =>
        {
            _ = world.ApplyBackgroundStreamingResults(maxChunkApplies: 4, maxSurfaceApplies: 4);
            return world.IsChunkLoaded(1, 1);
        });

        Assert.True(loaded);
        Assert.Equal(BlockType.Wood, world.GetBlock(20, 9, 20));
    }

    [Fact(DisplayName = "Async rebuild поверхностей отбрасывает устаревшую ревизию и применяет свежую")]
    public void World_AsyncSurfaceRebuild_DropsStaleRevisionAndAppliesFresh()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 16, seed: 0);
        world.EnsureChunksAround(new Vector3(24f, 0f, 24f), radiusInChunks: 1);
        var chunkKey = (ChunkX: 1, ChunkZ: 1);

        var dirtyField = typeof(WorldMap).GetField("_dirtySurfaceChunks", BindingFlags.Instance | BindingFlags.NonPublic);
        var pendingField = typeof(WorldMap).GetField("_pendingSurfaceRebuilds", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(dirtyField);
        Assert.NotNull(pendingField);
        var dirty = (HashSet<(int ChunkX, int ChunkZ)>)dirtyField!.GetValue(world)!;
        var pending = (HashSet<(int ChunkX, int ChunkZ)>)pendingField!.GetValue(world)!;
        dirty.Clear();
        dirty.Add(chunkKey);

        Assert.Equal(0, world.QueueDirtyChunkSurfacesAsync(centerChunkX: chunkKey.ChunkX, centerChunkZ: chunkKey.ChunkZ, maxChunks: 0));
        var firstQueued = world.QueueDirtyChunkSurfacesAsync(centerChunkX: chunkKey.ChunkX, centerChunkZ: chunkKey.ChunkZ, maxChunks: 1);
        Assert.Equal(1, firstQueued);

        world.SetBlock(17, 2, 17, BlockType.Stone);

        var firstCompleted = WaitUntil(() =>
        {
            _ = world.ApplyBackgroundStreamingResults(maxChunkApplies: 0, maxSurfaceApplies: 1);
            return !pending.Contains(chunkKey);
        }, maxAttempts: 300, sleepMs: 8);
        Assert.True(firstCompleted);

        Assert.True(world.TryGetChunkSurfaceBlocks(1, 1, out var staleSurface));
        Assert.Empty(staleSurface);

        dirty.Clear();
        dirty.Add(chunkKey);
        var secondQueued = world.QueueDirtyChunkSurfacesAsync(centerChunkX: chunkKey.ChunkX, centerChunkZ: chunkKey.ChunkZ, maxChunks: 1);
        Assert.Equal(1, secondQueued);

        var rebuilt = WaitUntil(() =>
        {
            _ = world.ApplyBackgroundStreamingResults(maxChunkApplies: 0, maxSurfaceApplies: 1);
            return world.TryGetChunkSurfaceBlocks(1, 1, out var refreshedSurface) && refreshedSurface.Count > 0;
        }, maxAttempts: 300, sleepMs: 8);

        Assert.True(rebuilt);
    }

    [Fact(DisplayName = "Async-очередь поверхностей чистит stale dirty-ключи и обрабатывает edge-case бюджеты")]
    public void World_AsyncSurfaceQueue_CleansStaleDirtyAndHandlesEdgeBudgets()
    {
        var emptyWorld = new WorldMap(width: 0, height: 8, depth: 0, chunkSize: 16, seed: 0);
        Assert.Equal(0, emptyWorld.EnsureChunksAroundBudgetedAsync(0, 0, radiusInChunks: 2, maxNewChunks: 3));
        Assert.Equal(0, emptyWorld.QueueDirtyChunkSurfacesAsync(new Vector3(0f, 0f, 0f), maxChunks: 2));
        Assert.Equal(0, emptyWorld.ApplyBackgroundStreamingResults(maxChunkApplies: -1, maxSurfaceApplies: -1));

        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 16, seed: 0);
        world.EnsureChunksAround(8, 8, radiusInChunks: 0);

        var dirtyField = typeof(WorldMap).GetField("_dirtySurfaceChunks", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(dirtyField);
        var dirty = (HashSet<(int ChunkX, int ChunkZ)>)dirtyField!.GetValue(world)!;
        dirty.Add((3, 3)); // stale: чанк не загружен

        var queued = world.QueueDirtyChunkSurfacesAsync(centerChunkX: 0, centerChunkZ: 0, maxChunks: 1);
        Assert.Equal(1, queued);
        Assert.DoesNotContain((3, 3), dirty);

        var applied = WaitUntil(() => world.ApplyBackgroundStreamingResults(maxChunkApplies: 0, maxSurfaceApplies: 1) > 0);
        Assert.True(applied);
    }

    [Fact(DisplayName = "QueueDirtyChunkSurfacesAsync(Vector3) использует центр чанка из позиции")]
    public void World_QueueDirtyChunkSurfacesAsync_VectorOverload_QueuesByPosition()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 16, seed: 0);
        world.EnsureChunksAround(new Vector3(24f, 0f, 24f), radiusInChunks: 1);
        world.SetBlock(17, 2, 17, BlockType.Wood);

        var queued = world.QueueDirtyChunkSurfacesAsync(new Vector3(17.5f, 2f, 17.5f), maxChunks: 1);
        Assert.True(queued >= 1);
    }

    [Fact(DisplayName = "ApplyBackgroundStreamingResults пропускает surface-результат для не загруженного чанка")]
    public void World_ApplyBackgroundStreamingResults_SkipsSurfaceForUnloadedChunk()
    {
        var world = new WorldMap(width: 32, height: 8, depth: 32, chunkSize: 16, seed: 0);

        var queueField = typeof(WorldMap).GetField("_completedSurfaceRebuilds", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(queueField);
        var queue = queueField!.GetValue(world)!;
        var queueType = queue.GetType();
        var enqueue = queueType.GetMethod("Enqueue");
        Assert.NotNull(enqueue);

        var resultType = typeof(WorldMap).GetNestedType("SurfaceRebuildResult", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(resultType);
        var result = Activator.CreateInstance(
            resultType!,
            [
                1,
                1,
                1,
                (IReadOnlyList<WorldMap.SurfaceBlock>)Array.Empty<WorldMap.SurfaceBlock>()
            ])!;
        enqueue!.Invoke(queue, [result]);

        var applied = world.ApplyBackgroundStreamingResults(maxChunkApplies: 0, maxSurfaceApplies: 1);
        Assert.Equal(0, applied);
    }

    [Fact(DisplayName = "ApplyBackgroundStreamingResults пропускает chunk-результат для уже загруженного чанка")]
    public void World_ApplyBackgroundStreamingResults_SkipsAlreadyLoadedGeneratedChunk()
    {
        var world = new WorldMap(width: 32, height: 8, depth: 32, chunkSize: 16, seed: 0);
        world.EnsureChunksAround(0, 0, radiusInChunks: 0);
        Assert.True(world.IsChunkLoaded(0, 0));

        var queueField = typeof(WorldMap).GetField("_completedChunkGenerations", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(queueField);
        var queue = queueField!.GetValue(world)!;
        var enqueue = queue.GetType().GetMethod("Enqueue");
        Assert.NotNull(enqueue);

        var resultType = typeof(WorldMap).GetNestedType("GeneratedChunkResult", BindingFlags.NonPublic);
        Assert.NotNull(resultType);
        var generated = Activator.CreateInstance(resultType!, [0, 0, new Chunk(world.ChunkSize, world.Height)])!;
        enqueue!.Invoke(queue, [generated]);

        var applied = world.ApplyBackgroundStreamingResults(maxChunkApplies: 1, maxSurfaceApplies: 0);
        Assert.Equal(0, applied);
    }

    [Fact(DisplayName = "TryQueueChunkGeneration отклоняет координаты чанка вне границ")]
    public void World_TryQueueChunkGeneration_RejectsOutOfBounds()
    {
        var world = new WorldMap(width: 32, height: 8, depth: 32, chunkSize: 16, seed: 0);
        var method = typeof(WorldMap).GetMethod("TryQueueChunkGeneration", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var negative = (bool)method!.Invoke(world, [-1, 0])!;
        var beyond = (bool)method.Invoke(world, [99, 99])!;
        Assert.False(negative);
        Assert.False(beyond);
    }

    [Fact(DisplayName = "TryQueueSurfaceRebuild покрывает pending и snapshot-fail cleanup ветки")]
    public void World_TryQueueSurfaceRebuild_CoversPendingAndSnapshotFailCleanup()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 16, seed: 0);
        world.EnsureChunksAround(8, 8, radiusInChunks: 0);
        var method = typeof(WorldMap).GetMethod("TryQueueSurfaceRebuild", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var pendingField = typeof(WorldMap).GetField("_pendingSurfaceRebuilds", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(pendingField);
        var pending = (HashSet<(int ChunkX, int ChunkZ)>)pendingField!.GetValue(world)!;
        pending.Add((0, 0));
        var pendingRejected = (bool)method!.Invoke(world, [0, 0])!;
        Assert.False(pendingRejected);
        pending.Clear();

        var dirtyField = typeof(WorldMap).GetField("_dirtySurfaceChunks", BindingFlags.Instance | BindingFlags.NonPublic);
        var cacheField = typeof(WorldMap).GetField("_chunkSurfaceCache", BindingFlags.Instance | BindingFlags.NonPublic);
        var revisionsField = typeof(WorldMap).GetField("_surfaceRevisions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(dirtyField);
        Assert.NotNull(cacheField);
        Assert.NotNull(revisionsField);

        var dirty = (HashSet<(int ChunkX, int ChunkZ)>)dirtyField!.GetValue(world)!;
        var cache = (Dictionary<(int ChunkX, int ChunkZ), IReadOnlyList<WorldMap.SurfaceBlock>>)cacheField!.GetValue(world)!;
        var revisions = (Dictionary<(int ChunkX, int ChunkZ), int>)revisionsField!.GetValue(world)!;
        dirty.Add((3, 3));
        cache[(3, 3)] = Array.Empty<WorldMap.SurfaceBlock>();
        revisions[(3, 3)] = 7;

        var queued = (bool)method.Invoke(world, [3, 3])!;
        Assert.False(queued);
        Assert.DoesNotContain((3, 3), dirty);
        Assert.DoesNotContain((3, 3), cache.Keys);
        Assert.DoesNotContain((3, 3), revisions.Keys);
    }

    [Fact(DisplayName = "TryCreateSurfaceSnapshot возвращает false для отсутствующего root-чанка")]
    public void World_TryCreateSurfaceSnapshot_ReturnsFalseForMissingRootChunk()
    {
        var world = new WorldMap(width: 32, height: 8, depth: 32, chunkSize: 16, seed: 0);
        var method = typeof(WorldMap).GetMethod("TryCreateSurfaceSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        object[] args = [1, 1, null!];
        var ok = (bool)method!.Invoke(world, args)!;
        Assert.False(ok);
    }

    [Fact(DisplayName = "RebuildChunkSurfaceBlocksFromSnapshot покрывает missing-root и ветки границ width/depth")]
    public void World_RebuildChunkSurfaceBlocksFromSnapshot_CoversMissingAndBoundsSkips()
    {
        var world = new WorldMap(width: 17, height: 8, depth: 17, chunkSize: 16, seed: 0);
        var rebuild = typeof(WorldMap).GetMethod("RebuildChunkSurfaceBlocksFromSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        var snapshotFactory = typeof(WorldMap).GetMethod("TryCreateSurfaceSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(rebuild);
        Assert.NotNull(snapshotFactory);

        var missing = (IReadOnlyList<WorldMap.SurfaceBlock>)rebuild!.Invoke(
            world,
            [1, 1, new Dictionary<(int ChunkX, int ChunkZ), BlockType[,,]>()])!;
        Assert.Empty(missing);

        world.EnsureChunksAround(16, 16, radiusInChunks: 0);
        object[] args = [1, 1, null!];
        var snapshotOk = (bool)snapshotFactory!.Invoke(world, args)!;
        Assert.True(snapshotOk);
        var snapshot = (Dictionary<(int ChunkX, int ChunkZ), BlockType[,,]>)args[2];

        var rebuilt = (IReadOnlyList<WorldMap.SurfaceBlock>)rebuild.Invoke(world, [1, 1, snapshot])!;
        Assert.NotNull(rebuilt);
    }

    [Fact(DisplayName = "SnapshotOverridesForChunk отфильтровывает override вне чанка и по высоте")]
    public void World_SnapshotOverridesForChunk_SkipsOutOfChunkAndInvalidHeight()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 16, seed: 0);
        var overridesField = typeof(WorldMap).GetField("_overrides", BindingFlags.Instance | BindingFlags.NonPublic);
        var method = typeof(WorldMap).GetMethod("SnapshotOverridesForChunk", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(overridesField);
        Assert.NotNull(method);

        var overrides = (Dictionary<(int X, int Y, int Z), BlockType>)overridesField!.GetValue(world)!;
        overrides.Clear();
        overrides[(0, 1, 0)] = BlockType.Stone;     // валидно для (0,0)
        overrides[(17, 1, 0)] = BlockType.Wood;     // вне чанка (0,0)
        overrides[(1, -1, 1)] = BlockType.Leaves;   // некорректная высота

        var snapshot = (Dictionary<(int X, int Y, int Z), BlockType>)method!.Invoke(world, [0, 0])!;
        Assert.Contains((0, 1, 0), snapshot.Keys);
        Assert.DoesNotContain((17, 1, 0), snapshot.Keys);
        Assert.DoesNotContain((1, -1, 1), snapshot.Keys);
    }

    [Fact(DisplayName = "SnapshotOverridesForChunk возвращает пустой словарь при отсутствии override")]
    public void World_SnapshotOverridesForChunk_ReturnsEmpty_WhenOverridesMissing()
    {
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 16, seed: 0);
        var method = typeof(WorldMap).GetMethod("SnapshotOverridesForChunk", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var snapshot = (Dictionary<(int X, int Y, int Z), BlockType>)method!.Invoke(world, [0, 0])!;
        Assert.Empty(snapshot);
    }

    [Fact(DisplayName = "ApplyChunkOverridesSnapshot игнорирует некорректные override по высоте и локальным координатам")]
    public void World_ApplyChunkOverridesSnapshot_SkipsInvalidEntries()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 16, seed: 0);
        var method = typeof(WorldMap).GetMethod("ApplyChunkOverridesSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var chunk = new Chunk(size: 16, height: 16);
        var overrides = new Dictionary<(int X, int Y, int Z), BlockType>
        {
            [(1, -1, 1)] = BlockType.Stone, // invalid y
            [(33, 2, 1)] = BlockType.Wood,  // out of local x for chunk (0,0)
            [(1, 2, 33)] = BlockType.Wood   // out of local z for chunk (0,0)
        };

        method!.Invoke(world, [chunk, 0, 0, overrides]);
        Assert.Equal(BlockType.Air, chunk.Get(1, 2, 1));
    }

    [Fact(DisplayName = "GetTerrainTopY обрабатывает границы, а пустой мир не загружает чанки")]
    public void World_TerrainTopAndEmptyWorldBranches_AreCovered()
    {
        var world = new WorldMap(width: 48, height: 24, depth: 48, chunkSize: 16, seed: 777);

        Assert.Equal(0, world.GetTerrainTopY(-1, 10));
        Assert.Equal(0, world.GetTerrainTopY(10, 48));
        Assert.InRange(world.GetTerrainTopY(10, 10), 2, 22);

        var emptyWorld = new WorldMap(width: 0, height: 8, depth: 0, chunkSize: 16, seed: 777);
        emptyWorld.EnsureChunksAround(0, 0, 2);
        Assert.Equal(0, emptyWorld.LoadedChunkCount);
    }

    [Fact(DisplayName = "GetTopSolidY возвращает верхний твёрдый блок и учитывает кроны деревьев")]
    public void World_GetTopSolidY_IncludesTreeCanopy()
    {
        var world = new WorldMap(width: 128, height: 48, depth: 128, chunkSize: 16, seed: 777);
        world.EnsureChunksAround(new Vector3(64f, 0f, 64f), radiusInChunks: 3);

        var foundTreeColumn = false;
        for (var x = 40; x <= 88 && !foundTreeColumn; x++)
        {
            for (var z = 40; z <= 88; z++)
            {
                var terrainTop = world.GetTerrainTopY(x, z);
                var solidTop = world.GetTopSolidY(x, z);
                Assert.True(solidTop >= terrainTop);

                if (solidTop > terrainTop)
                {
                    foundTreeColumn = true;
                    break;
                }
            }
        }

        Assert.True(foundTreeColumn, "Ожидали найти колонку, где верхний блок выше рельефа (дерево/листва).");
    }

    [Fact(DisplayName = "GetTopSolidY возвращает 0 вне границ и для полностью пустой колонки")]
    public void World_GetTopSolidY_ReturnsZero_ForOutOfBoundsAndEmptyColumn()
    {
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        for (var y = 0; y < world.Height; y++)
        {
            world.SetBlock(2, y, 2, BlockType.Air);
        }

        Assert.Equal(0, world.GetTopSolidY(-1, 2));
        Assert.Equal(0, world.GetTopSolidY(2, -1));
        Assert.Equal(0, world.GetTopSolidY(world.Width, 2));
        Assert.Equal(0, world.GetTopSolidY(2, world.Depth));
        Assert.Equal(0, world.GetTopSolidY(2, 2));
    }

    [Fact(DisplayName = "Изменения блоков сохраняются после выгрузки и повторной генерации чанка")]
    public void World_BlockOverride_PersistsAfterChunkReload()
    {
        var world = new WorldMap(width: 96, height: 40, depth: 96, chunkSize: 16, seed: 777);
        world.SetBlock(20, 9, 20, BlockType.Wood);

        world.UnloadFarChunks(new Vector3(0f, 0f, 0f), keepRadiusInChunks: -1);
        Assert.Equal(0, world.LoadedChunkCount);

        world.EnsureChunksAround(20, 20, 0);
        Assert.Equal(BlockType.Wood, world.GetBlock(20, 9, 20));
    }

    [Fact(DisplayName = "Лесная генерация создаёт дерево и листву в зоне загрузки")]
    public void World_ForestGeneration_CreatesWoodAndLeaves()
    {
        var world = new WorldMap(width: 600, height: 72, depth: 600, chunkSize: 16, seed: 777);
        world.EnsureChunksAround(new Vector3(300f, 0f, 300f), radiusInChunks: 2);

        var woodCount = 0;
        var leavesCount = 0;
        for (var x = 260; x <= 340; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 260; z <= 340; z++)
                {
                    var block = world.GetBlock(x, y, z);
                    if (block == BlockType.Wood)
                    {
                        woodCount++;
                    }
                    else if (block == BlockType.Leaves)
                    {
                        leavesCount++;
                    }
                }
            }
        }

        Assert.True(woodCount > 0, "Ожидали хотя бы одно дерево (ствол).");
        Assert.True(leavesCount > 0, "Ожидали хотя бы одну листву.");
    }

    [Fact(DisplayName = "Кэш поверхностей чанка прогревается бюджетно и обновляется после изменения блока")]
    public void World_SurfaceCache_RebuildsInBudgetAndRefreshesAfterSetBlock()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 16, seed: 0);
        world.EnsureChunksAround(new Vector3(24f, 0f, 24f), radiusInChunks: 1);

        Assert.True(world.TryGetChunkSurfaceBlocks(1, 1, out var coldSurface));
        Assert.Empty(coldSurface);

        var rebuilt = world.RebuildDirtyChunkSurfaces(centerChunkX: 1, centerChunkZ: 1, maxChunks: 1);
        Assert.Equal(1, rebuilt);

        Assert.True(world.TryGetChunkSurfaceBlocks(1, 1, out var warmSurface));
        Assert.NotEmpty(warmSurface);

        world.SetBlock(17, 2, 17, BlockType.Stone);

        Assert.True(world.TryGetChunkSurfaceBlocks(1, 1, out var dirtySurface));
        Assert.Empty(dirtySurface);

        var rebuiltAfterChange = world.RebuildDirtyChunkSurfaces(new Vector3(17.5f, 2f, 17.5f), maxChunks: 2);
        Assert.True(rebuiltAfterChange >= 1);

        Assert.True(world.TryGetChunkSurfaceBlocks(1, 1, out var refreshedSurface));
        Assert.NotEmpty(refreshedSurface);
    }

    [Fact(DisplayName = "RebuildDirtyChunkSurfaces корректно обрабатывает пустой мир и нулевой бюджет")]
    public void World_RebuildDirtyChunkSurfaces_HandlesEdgeCases()
    {
        var emptyWorld = new WorldMap(width: 0, height: 8, depth: 0, chunkSize: 16, seed: 0);
        Assert.Equal(0, emptyWorld.RebuildDirtyChunkSurfaces(new Vector3(0f, 0f, 0f), maxChunks: 4));

        var world = new WorldMap(width: 32, height: 8, depth: 32, chunkSize: 16, seed: 0);
        world.EnsureChunksAround(new Vector3(8f, 0f, 8f), radiusInChunks: 0);
        Assert.Equal(0, world.RebuildDirtyChunkSurfaces(centerChunkX: 0, centerChunkZ: 0, maxChunks: 0));
    }

    [Fact(DisplayName = "TryGetChunkSurfaceBlocks возвращает false для не загруженного чанка")]
    public void World_TryGetChunkSurfaceBlocks_ReturnsFalseForUnloadedChunk()
    {
        var world = new WorldMap(width: 32, height: 8, depth: 32, chunkSize: 16, seed: 0);

        var ok = world.TryGetChunkSurfaceBlocks(1, 1, out var blocks);

        Assert.False(ok);
        Assert.Empty(blocks);
    }

    [Fact(DisplayName = "RebuildChunkSurfaceBlocks очищает кэш для отсутствующего чанка")]
    public void World_RebuildChunkSurfaceBlocks_MissingChunk_ReturnsEmpty()
    {
        var world = new WorldMap(width: 32, height: 8, depth: 32, chunkSize: 16, seed: 0);
        var method = typeof(WorldMap).GetMethod("RebuildChunkSurfaceBlocks", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var rebuilt = (IReadOnlyList<WorldMap.SurfaceBlock>)method!.Invoke(world, [1, 1])!;

        Assert.Empty(rebuilt);
    }

    [Fact(DisplayName = "TryGetClosestDirtyLoadedChunk удаляет устаревшие dirty-ключи")]
    public void World_TryGetClosestDirtyLoadedChunk_RemovesStaleDirtyEntries()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 16, seed: 0);
        world.EnsureChunksAround(8, 8, 0);

        var dirtyField = typeof(WorldMap).GetField("_dirtySurfaceChunks", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(dirtyField);
        var dirty = (HashSet<(int ChunkX, int ChunkZ)>)dirtyField!.GetValue(world)!;
        dirty.Add((3, 3)); // заведомо stale: чанк не загружен

        var method = typeof(WorldMap).GetMethod("TryGetClosestDirtyLoadedChunk", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        object[] args = [0, 0, null!];
        var found = (bool)method!.Invoke(world, args)!;

        Assert.True(found);
        Assert.DoesNotContain((3, 3), dirty);
    }

    [Fact(DisplayName = "IsLocalTreePeak пропускает соседей вне мира на границе")]
    public void World_IsLocalTreePeak_SkipsOutOfBoundsNeighbors()
    {
        var world = new WorldMap(width: 16, height: 16, depth: 16, chunkSize: 8, seed: 777);
        var method = typeof(WorldMap).GetMethod("IsLocalTreePeak", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        float SignalAt(int x, int z) => (x == 0 && z == 0) ? 0.9f : 0.1f;
        var ok = (bool)method!.Invoke(world, [0, 0, 0.9f, (Func<int, int, float>)SignalAt])!;

        Assert.True(ok);
    }

    [Fact(DisplayName = "SetBlockInChunk игнорирует блоки вне мира по высоте")]
    public void World_SetBlockInChunk_IgnoresOutOfBoundsHeight()
    {
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 16, seed: 777);
        var method = typeof(WorldMap).GetMethod("SetBlockInChunk", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var chunk = new Chunk(size: 16, height: 16);
        method!.Invoke(world, [chunk, 0, 0, 2, -1, 2, BlockType.Leaves, true]);

        Assert.Equal(BlockType.Air, chunk.Get(2, 0, 2));
    }

    [Fact(DisplayName = "FractalNoise возвращает 0 при нулевом числе октав")]
    public void World_FractalNoise_ZeroOctaves_ReturnsZero()
    {
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 16, seed: 777);
        var method = typeof(WorldMap).GetMethod("FractalNoise", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var value = (float)method!.Invoke(world, [0.5f, 0.75f, 0, 2f, 0.5f])!;
        Assert.Equal(0f, value);
    }

    [Fact(DisplayName = "Свойства чанка Size и Height доступны корректно")]
    public void Chunk_SizeAndHeight_AreAccessible()
    {
        var chunk = new Chunk(size: 16, height: 24);
        Assert.Equal(16, chunk.Size);
        Assert.Equal(24, chunk.Height);
    }

    [Fact(DisplayName = "Игрок падает на землю и корректно становится на поверхность")]
    public void Player_FallsAndGetsGrounded()
    {
        var world = new WorldMap(width: 16, height: 16, depth: 16);
        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(6f, 5f, 6f));

        for (var i = 0; i < 300; i++)
        {
            player.Update(world, new PlayerInput(0f, 0f, false, 0f, 0f), 1f / 120f);
        }

        Assert.True(player.IsGrounded);
        Assert.InRange(player.Position.Y, 1.98f, 2.02f);
    }

    [Fact(DisplayName = "Игрок не проходит сквозь блок по горизонтали")]
    public void Player_CannotMoveThroughWall()
    {
        var world = new WorldMap(width: 16, height: 16, depth: 16);
        world.SetBlock(2, 2, 4, BlockType.Stone);

        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(4.2f, 2f, 4.5f));

        for (var i = 0; i < 180; i++)
        {
            player.Update(world, new PlayerInput(0f, 1f, false, 0f, 0f), 1f / 120f);
        }

        Assert.True(player.Position.X >= 3.29f, $"Ожидалась граница у стены, фактический X={player.Position.X:0.000}");
    }

    [Fact(DisplayName = "Вертикальный обзор ограничен безопасным углом")]
    public void Player_LookPitchIsClamped()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8);
        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(4f, 2f, 4f));

        player.Update(world, new PlayerInput(0f, 0f, false, 0f, -100_000f), 1f / 60f);
        Assert.InRange(player.Pitch, 1.53f, 1.54f);

        player.Update(world, new PlayerInput(0f, 0f, false, 0f, 100_000f), 1f / 60f);
        Assert.InRange(player.Pitch, -1.54f, -1.53f);
    }

    [Fact(DisplayName = "Свойства камеры игрока корректны: позиция глаз и вектор взгляда")]
    public void Player_EyeAndLookDirection_AreValid()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8);
        var player = new PlayerController(new GameConfig(), new Vector3(4f, 2f, 4f));

        _ = world;
        Assert.InRange(player.EyePosition.Y, 3.65f, 3.66f);

        var direction = player.LookDirection;
        Assert.InRange(direction.Length(), 0.999f, 1.001f);
        Assert.True(direction.Z < 0f);
        Assert.Equal(0.3f, player.ColliderHalfWidth);
        Assert.Equal(1.8f, player.ColliderHeight);
    }

    [Fact(DisplayName = "SetPose задает позицию и направление взгляда игрока")]
    public void Player_SetPose_UpdatesPositionAndLook()
    {
        var player = new PlayerController(new GameConfig(), new Vector3(4f, 2f, 4f));
        var newPosition = new Vector3(10f, 3f, 10f);
        var lookDirection = new Vector3(1f, -0.2f, -1f);

        player.SetPose(newPosition, lookDirection);

        Assert.Equal(newPosition, player.Position);
        var expected = Vector3.Normalize(lookDirection);
        var actual = player.LookDirection;
        Assert.True(MathF.Abs(actual.X - expected.X) < 0.001f);
        Assert.True(MathF.Abs(actual.Y - expected.Y) < 0.001f);
        Assert.True(MathF.Abs(actual.Z - expected.Z) < 0.001f);
    }

    [Fact(DisplayName = "SetPose с нулевым вектором взгляда меняет позицию без изменения ориентации")]
    public void Player_SetPose_ZeroLookDirection_OnlyMovesPlayer()
    {
        var player = new PlayerController(new GameConfig(), new Vector3(4f, 2f, 4f));
        var initialDirection = player.LookDirection;
        var targetPosition = new Vector3(7f, 3f, 8f);

        player.SetPose(targetPosition, Vector3.Zero);

        Assert.Equal(targetPosition, player.Position);
        var actualDirection = player.LookDirection;
        Assert.True(MathF.Abs(actualDirection.X - initialDirection.X) < 0.001f);
        Assert.True(MathF.Abs(actualDirection.Y - initialDirection.Y) < 0.001f);
        Assert.True(MathF.Abs(actualDirection.Z - initialDirection.Z) < 0.001f);
    }

    [Fact(DisplayName = "Прыжок под потолок останавливает вертикальную скорость без прохода сквозь блок")]
    public void Player_JumpIntoCeiling_DoesNotClipThrough()
    {
        var world = new WorldMap(width: 16, height: 16, depth: 16);
        world.SetBlock(6, 4, 6, BlockType.Stone);

        var player = new PlayerController(new GameConfig(), new Vector3(6f, 2f, 6f));
        player.Update(world, new PlayerInput(0f, 0f, true, 0f, 0f), 1f / 60f);

        for (var i = 0; i < 90; i++)
        {
            player.Update(world, new PlayerInput(0f, 0f, false, 0f, 0f), 1f / 120f);
        }

        Assert.True(player.Position.Y < 2.21f, $"Игрок не должен пройти через потолок. Y={player.Position.Y:0.000}");
    }

    [Fact(DisplayName = "Движение на D смещает игрока вправо по оси X")]
    public void Player_MoveRight_KeyD_IncreasesX()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64);
        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(32f, 2f, 32f));
        var startX = player.Position.X;

        player.Update(world, new PlayerInput(0f, 1f, false, 0f, 0f), 1f / 10f);

        Assert.True(player.Position.X > startX, $"Ожидали рост X, фактический X={player.Position.X:0.000}, старт={startX:0.000}");
    }

    [Fact(DisplayName = "Движение на A смещает игрока влево по оси X")]
    public void Player_MoveLeft_KeyA_DecreasesX()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64);
        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(32f, 2f, 32f));
        var startX = player.Position.X;

        player.Update(world, new PlayerInput(0f, -1f, false, 0f, 0f), 1f / 10f);

        Assert.True(player.Position.X < startX, $"Ожидали уменьшение X, фактический X={player.Position.X:0.000}, старт={startX:0.000}");
    }

    [Fact(DisplayName = "Движение на W смещает игрока вперед по оси Z")]
    public void Player_MoveForward_KeyW_DecreasesZ()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64);
        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(32f, 2f, 32f));
        var startZ = player.Position.Z;

        player.Update(world, new PlayerInput(1f, 0f, false, 0f, 0f), 1f / 10f);

        Assert.True(player.Position.Z < startZ, $"Ожидали уменьшение Z, фактический Z={player.Position.Z:0.000}, старт={startZ:0.000}");
    }

    [Fact(DisplayName = "Движение на S смещает игрока назад по оси Z")]
    public void Player_MoveBackward_KeyS_IncreasesZ()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64);
        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(32f, 2f, 32f));
        var startZ = player.Position.Z;

        player.Update(world, new PlayerInput(-1f, 0f, false, 0f, 0f), 1f / 10f);

        Assert.True(player.Position.Z > startZ, $"Ожидали рост Z, фактический Z={player.Position.Z:0.000}, старт={startZ:0.000}");
    }

    [Fact(DisplayName = "Диагональное движение нормализуется по скорости")]
    public void Player_DiagonalMove_IsNormalized()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64);
        var player = new PlayerController(new GameConfig(), new Vector3(32f, 2f, 32f));

        var start = player.Position;
        player.Update(world, new PlayerInput(1f, 1f, false, 0f, 0f), 1f / 10f);
        var moved = Vector3.Distance(start, player.Position);

        Assert.InRange(moved, 0.54f, 0.56f);
    }
}
