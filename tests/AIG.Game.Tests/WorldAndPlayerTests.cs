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

    [Fact(DisplayName = "Chunk residency удерживает дальний чанк до истечения TTL")]
    public void World_ChunkResidency_KeepsFarChunkUntilExpiry()
    {
        var world = new WorldMap(width: 96, height: 32, depth: 96, chunkSize: 16, seed: 777);
        var nearCenter = new Vector3(8f, 0f, 8f);
        var farCenter = new Vector3(40f, 0f, 8f);

        world.EnsureChunksAround(nearCenter, radiusInChunks: 0);
        world.EnsureChunksAround(farCenter, radiusInChunks: 0);
        Assert.True(world.IsChunkLoaded(0, 0));
        Assert.True(world.IsChunkLoaded(2, 0));

        world.TouchChunkResidency(farCenter, radiusInChunks: 0, ttlFrames: 2);
        world.UnloadFarChunks(nearCenter, keepRadiusInChunks: 0);
        Assert.True(world.IsChunkLoaded(2, 0));

        world.AdvanceChunkResidency(1);
        world.UnloadFarChunks(nearCenter, keepRadiusInChunks: 0);
        Assert.True(world.IsChunkLoaded(2, 0));

        world.AdvanceChunkResidency(1);
        world.UnloadFarChunks(nearCenter, keepRadiusInChunks: 0);
        Assert.False(world.IsChunkLoaded(2, 0));
    }

    [Fact(DisplayName = "Chunk residency helpers корректно обрабатывают пустой мир и невалидные параметры")]
    public void World_ChunkResidencyHelpers_HandleEmptyAndInvalidInput()
    {
        var emptyWorld = new WorldMap(width: 0, height: 8, depth: 0, chunkSize: 8, seed: 0);
        emptyWorld.TouchChunkResidency(new Vector3(0f, 0f, 0f), radiusInChunks: 0, ttlFrames: 5);
        emptyWorld.TouchChunkResidency(new Vector3(0f, 0f, 0f), radiusInChunks: -1, ttlFrames: 5);
        emptyWorld.TouchChunkResidency(new Vector3(0f, 0f, 0f), radiusInChunks: 0, ttlFrames: 0);
        emptyWorld.AdvanceChunkResidency(0);
        emptyWorld.AdvanceChunkResidency(1);

        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 8, seed: 0);
        world.EnsureChunksAround(new Vector3(4f, 0f, 4f), radiusInChunks: 0);
        var residencyField = typeof(WorldMap).GetField("_chunkResidencyTtl", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(residencyField);
        var residency = (Dictionary<(int ChunkX, int ChunkZ), int>)residencyField!.GetValue(world)!;

        world.TouchChunkResidency(new Vector3(4f, 0f, 4f), radiusInChunks: 0, ttlFrames: 3);
        Assert.Equal(3, residency[(0, 0)]);

        world.TouchChunkResidency(new Vector3(4f, 0f, 4f), radiusInChunks: 0, ttlFrames: 1);
        Assert.Equal(3, residency[(0, 0)]);

        world.AdvanceChunkResidency(0);
        Assert.Equal(3, residency[(0, 0)]);
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

    [Fact(DisplayName = "PlaceTreeIntoChunk покрывает все варианты кроны")]
    public void World_PlaceTreeIntoChunk_CoversAllTreeVariants()
    {
        var world = new WorldMap(width: 64, height: 32, depth: 64, chunkSize: 16, seed: 777);
        var variantCountField = typeof(WorldMap).GetField("TreeVariantCount", BindingFlags.Static | BindingFlags.NonPublic);
        var variantMethod = typeof(WorldMap).GetMethod("GetTreeVariant", BindingFlags.Instance | BindingFlags.NonPublic);
        var placeMethod = typeof(WorldMap).GetMethod("PlaceTreeIntoChunk", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(variantCountField);
        Assert.NotNull(variantMethod);
        Assert.NotNull(placeMethod);

        var variantCount = (int)variantCountField!.GetRawConstantValue()!;
        var foundRoots = new Dictionary<int, (int X, int Z)>();
        for (var x = 3; x <= 24 && foundRoots.Count < variantCount; x++)
        {
            for (var z = 3; z <= 24 && foundRoots.Count < variantCount; z++)
            {
                var variant = (int)variantMethod!.Invoke(world, [x, z])!;
                foundRoots.TryAdd(variant, (x, z));
            }
        }

        Assert.Equal(variantCount, foundRoots.Count);

        foreach (var root in foundRoots.Values)
        {
            var chunk = new Chunk(size: 16, height: 32);
            placeMethod!.Invoke(world, [chunk, 0, 0, root.X, 4, root.Z]);

            var hasWood = false;
            var hasLeaves = false;
            for (var y = 0; y < chunk.Height && !(hasWood && hasLeaves); y++)
            {
                for (var x = 0; x < chunk.Size && !(hasWood && hasLeaves); x++)
                {
                    for (var z = 0; z < chunk.Size && !(hasWood && hasLeaves); z++)
                    {
                        var block = chunk.Get(x, y, z);
                        hasWood |= block == BlockType.Wood;
                        hasLeaves |= block == BlockType.Leaves;
                    }
                }
            }

            Assert.True(hasWood);
            Assert.True(hasLeaves);
        }
    }

    [Fact(DisplayName = "PlaceTreeIntoChunk после prune не оставляет одинокие листья в безопасной зоне")]
    public void World_PlaceTreeIntoChunk_PrunesSparseLeaves()
    {
        var world = new WorldMap(width: 64, height: 32, depth: 64, chunkSize: 16, seed: 777);
        var placeMethod = typeof(WorldMap).GetMethod("PlaceTreeIntoChunk", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(placeMethod);

        var chunk = new Chunk(size: 16, height: 32);
        placeMethod!.Invoke(world, [chunk, 0, 0, 8, 4, 8]);

        static int CountNeighbors(Chunk treeChunk, int x, int y, int z)
        {
            var neighbors = 0;
            if (treeChunk.Get(x + 1, y, z) is BlockType.Leaves or BlockType.Wood) neighbors++;
            if (treeChunk.Get(x - 1, y, z) is BlockType.Leaves or BlockType.Wood) neighbors++;
            if (treeChunk.Get(x, y + 1, z) is BlockType.Leaves or BlockType.Wood) neighbors++;
            if (treeChunk.Get(x, y - 1, z) is BlockType.Leaves or BlockType.Wood) neighbors++;
            if (treeChunk.Get(x, y, z + 1) is BlockType.Leaves or BlockType.Wood) neighbors++;
            if (treeChunk.Get(x, y, z - 1) is BlockType.Leaves or BlockType.Wood) neighbors++;
            return neighbors;
        }

        for (var x = 2; x < chunk.Size - 2; x++)
        {
            for (var y = 1; y < chunk.Height - 1; y++)
            {
                for (var z = 2; z < chunk.Size - 2; z++)
                {
                    if (chunk.Get(x, y, z) != BlockType.Leaves)
                    {
                        continue;
                    }

                    Assert.True(CountNeighbors(chunk, x, y, z) >= 2, $"Нашли одиночную листву в {x},{y},{z}.");
                }
            }
        }
    }

    [Fact(DisplayName = "IsLeafOrWood различает границы, воздух, листву и дерево")]
    public void World_IsLeafOrWood_CoversBoundsAndBlockTypes()
    {
        var method = typeof(WorldMap).GetMethod("IsLeafOrWood", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var chunk = new Chunk(size: 4, height: 4);
        chunk.Set(1, 1, 1, BlockType.Leaves);
        chunk.Set(2, 1, 1, BlockType.Wood);

        Assert.False((bool)method!.Invoke(null, [chunk, -1, 1, 1])!);
        Assert.False((bool)method.Invoke(null, [chunk, 1, 1, -1])!);
        Assert.False((bool)method.Invoke(null, [chunk, 4, 1, 1])!);
        Assert.False((bool)method.Invoke(null, [chunk, 1, 1, 4])!);
        Assert.False((bool)method.Invoke(null, [chunk, 1, -1, 1])!);
        Assert.False((bool)method.Invoke(null, [chunk, 1, 4, 1])!);
        Assert.True((bool)method.Invoke(null, [chunk, 1, 1, 1])!);
        Assert.True((bool)method.Invoke(null, [chunk, 2, 1, 1])!);
        Assert.False((bool)method.Invoke(null, [chunk, 0, 1, 0])!);
    }

    [Fact(DisplayName = "Кэш поверхностей считает ambient occlusion и relief exposure для рельефа")]
    public void World_SurfaceCache_ComputesAmbientOcclusionAndReliefExposure()
    {
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(4, 3, 4, BlockType.Stone);
        world.SetBlock(5, 3, 4, BlockType.Stone);
        world.SetBlock(3, 3, 4, BlockType.Stone);
        world.SetBlock(4, 3, 5, BlockType.Stone);
        world.SetBlock(4, 3, 3, BlockType.Stone);

        var aoMethod = typeof(WorldMap).GetMethod("CountAmbientOcclusionNoLoad", BindingFlags.Instance | BindingFlags.NonPublic);
        var reliefMethod = typeof(WorldMap).GetMethod("CountReliefExposureNoLoad", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(aoMethod);
        Assert.NotNull(reliefMethod);

        var ambientOcclusion = (int)aoMethod!.Invoke(world, [4, 3, 4, true])!;
        var reliefExposure = (int)reliefMethod!.Invoke(world, [4, 3, 4, true])!;

        Assert.True(ambientOcclusion >= 4);
        Assert.Equal(4, reliefExposure);
    }

    [Fact(DisplayName = "Кэш поверхностей считает направленную солнечную видимость и снижает её под блокирующим блоком")]
    public void World_SurfaceCache_ComputesSunVisibility()
    {
        var world = new WorldMap(width: 16, height: 10, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(4, 3, 4, BlockType.Stone);

        var method = typeof(WorldMap).GetMethod("CountSunVisibilityNoLoad", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var openVisibility = (int)method!.Invoke(world, [4, 3, 4])!;
        Assert.Equal(WorldMap.MaxSunVisibility, openVisibility);

        world.SetBlock(5, 5, 4, BlockType.Stone);
        var blockedVisibility = (int)method.Invoke(world, [4, 3, 4])!;
        Assert.True(blockedVisibility < openVisibility);

        world.EnsureChunksAround(new Vector3(4.5f, 3.5f, 4.5f), radiusInChunks: 0);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 3.5f, 4.5f), maxChunks: 1);
        Assert.True(world.TryGetChunkSurfaceBlocks(0, 0, out var surface));
        var block = Assert.Single(surface.Where(s => s.X == 4 && s.Y == 3 && s.Z == 4));
        Assert.Equal(blockedVisibility, block.SunVisibility);
    }

    [Fact(DisplayName = "Кэш поверхностей считает daylight для комнаты с отверстием и без него")]
    public void World_SurfaceCache_ComputesDaylightPropagation()
    {
        var world = new WorldMap(width: 24, height: 10, depth: 24, chunkSize: 8, seed: 0);
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

        BuildRoom(world, originX: 2, originZ: 2, roofOpeningX: 4, roofOpeningZ: 4);
        BuildRoom(world, originX: 12, originZ: 12, roofOpeningX: -1, roofOpeningZ: -1);
        world.SetBlock(4, 2, 4, BlockType.Stone);
        world.SetBlock(14, 2, 14, BlockType.Stone);

        world.EnsureChunksAround(new Vector3(8f, 3f, 8f), radiusInChunks: 3);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(8f, 3f, 8f), maxChunks: 16);

        Assert.True(world.TryGetChunkSurfaceBlocks(0, 0, out var litChunk));
        Assert.True(world.TryGetChunkSurfaceBlocks(1, 1, out var darkChunk));

        var litSurface = Assert.Single(litChunk.Where(s => s.X == 4 && s.Y == 2 && s.Z == 4));
        var darkSurface = Assert.Single(darkChunk.Where(s => s.X == 14 && s.Y == 2 && s.Z == 14));

        Assert.True(litSurface.Daylight > 0);
        Assert.Equal(0, darkSurface.Daylight);
    }

    [Fact(DisplayName = "Закрытие отверстия сверху убирает daylight из шахты")]
    public void World_SurfaceCache_RemovesDaylight_WhenRoofOpeningGetsClosed()
    {
        var world = new WorldMap(width: 24, height: 10, depth: 24, chunkSize: 8, seed: 0);
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

        BuildRoom(world, originX: 2, originZ: 2, roofOpeningX: 4, roofOpeningZ: 4);
        world.SetBlock(4, 2, 4, BlockType.Stone);

        world.EnsureChunksAround(new Vector3(8f, 3f, 8f), radiusInChunks: 2);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(8f, 3f, 8f), maxChunks: 16);

        Assert.True(world.TryGetChunkSurfaceBlocks(0, 0, out var openChunk));
        var litSurface = Assert.Single(openChunk.Where(s => s.X == 4 && s.Y == 2 && s.Z == 4));
        Assert.True(litSurface.Daylight > 0);

        world.SetBlock(4, 4, 4, BlockType.Stone);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(8f, 3f, 8f), maxChunks: 16);

        Assert.True(world.TryGetChunkSurfaceBlocks(0, 0, out var closedChunk));
        var darkSurface = Assert.Single(closedChunk.Where(s => s.X == 4 && s.Y == 2 && s.Z == 4));
        Assert.Equal(0, darkSurface.Daylight);
    }

    [Fact(DisplayName = "LocalDaylightField.SetMax покрывает границы и отказ от ослабления значения")]
    public void World_LocalDaylightField_SetMax_CoversBoundsAndClamp()
    {
        var fieldType = typeof(WorldMap).GetNestedType("LocalDaylightField", BindingFlags.NonPublic);
        Assert.NotNull(fieldType);
        var field = Activator.CreateInstance(fieldType!, 1, 2, 1, 2, 4);
        var setMax = fieldType.GetMethod("SetMax", BindingFlags.Instance | BindingFlags.Public);
        var get = fieldType.GetMethod("Get", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(setMax);
        Assert.NotNull(get);

        Assert.False((bool)setMax!.Invoke(field, [0, 0, 0, 5])!);
        Assert.True((bool)setMax.Invoke(field, [1, 0, 1, WorldMap.MaxDaylight + 4])!);
        Assert.False((bool)setMax.Invoke(field, [1, 0, 1, 7])!);
        Assert.Equal(WorldMap.MaxDaylight, (int)get!.Invoke(field, [1, 0, 1])!);
        Assert.Equal(0, (int)get.Invoke(field, [9, 0, 9])!);
    }

    [Fact(DisplayName = "BuildDaylightField распространяет skylight по длинному коридору и упирается в крышу")]
    public void World_BuildDaylightField_PropagatesAlongCorridor()
    {
        var world = new WorldMap(width: 32, height: 6, depth: 32, chunkSize: 16, seed: 0);

        var buildMethod = typeof(WorldMap).GetMethod("BuildDaylightField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(buildMethod);
        Func<int, int, int, bool> isSolid = (x, y, z) =>
        {
            if (!world.IsInside(x, y, z))
            {
                return false;
            }

            if (y > 4)
            {
                return false;
            }

            if (z != 2)
            {
                return true;
            }

            if (x == 1)
            {
                return false;
            }

            if (x >= 2 && x <= 15)
            {
                return y == 4;
            }

            return true;
        };

        var field = buildMethod!.Invoke(world, [0, 0, isSolid])!;
        var fieldType = field.GetType();
        var get = fieldType.GetMethod("Get", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(get);

        var litNearOpening = (int)get!.Invoke(field, [2, 3, 2])!;
        var litFarCorridor = (int)get.Invoke(field, [15, 3, 2])!;
        var blockedRoof = (int)get.Invoke(field, [2, 3, 1])!;

        Assert.Equal(WorldMap.MaxDaylight - 1, litNearOpening);
        Assert.Equal(1, litFarCorridor);
        Assert.Equal(0, blockedRoof);
    }

    [Fact(DisplayName = "Кэш поверхностей считает local light в закрытой комнате от явного источника света")]
    public void World_SurfaceCache_ComputesLocalLightPropagation()
    {
        var world = new WorldMap(width: 24, height: 10, depth: 24, chunkSize: 8, seed: 0);
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

        BuildRoom(world, originX: 12, originZ: 12, roofOpeningX: -1, roofOpeningZ: -1);
        world.SetBlock(14, 2, 14, BlockType.Stone);
        world.SetBlock(13, 2, 14, BlockType.Stone);
        world.SetLocalLightSource(14, 3, 13, WorldMap.MaxLocalLight);

        world.EnsureChunksAround(new Vector3(16f, 3f, 16f), radiusInChunks: 2);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(16f, 3f, 16f), maxChunks: 16);

        Assert.True(world.TryGetChunkSurfaceBlocks(1, 1, out var surfaces));
        var nearEmitter = Assert.Single(surfaces.Where(s => s.X == 14 && s.Y == 2 && s.Z == 14));
        var fartherFromEmitter = Assert.Single(surfaces.Where(s => s.X == 13 && s.Y == 2 && s.Z == 14));

        Assert.Equal(0, nearEmitter.Daylight);
        Assert.True(nearEmitter.LocalLight > 0);
        Assert.True(nearEmitter.LocalLight >= fartherFromEmitter.LocalLight);
    }

    [Fact(DisplayName = "Кэш поверхностей согласует local light через границу чанков")]
    public void World_SurfaceCache_KeepsLocalLightConsistentAcrossChunkBoundary()
    {
        var world = new WorldMap(width: 24, height: 8, depth: 24, chunkSize: 8, seed: 0);
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

        world.SetBlock(7, 2, 8, BlockType.Stone);
        world.SetBlock(8, 2, 8, BlockType.Stone);
        world.SetLocalLightSource(8, 3, 8, WorldMap.MaxLocalLight);

        world.EnsureChunksAround(new Vector3(8.5f, 3f, 8.5f), radiusInChunks: 2);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(8.5f, 3f, 8.5f), maxChunks: 16);

        Assert.True(world.TryGetChunkSurfaceBlocks(0, 1, out var leftChunk));
        Assert.True(world.TryGetChunkSurfaceBlocks(1, 1, out var rightChunk));

        var leftSurface = Assert.Single(leftChunk.Where(s => s.X == 7 && s.Y == 2 && s.Z == 8));
        var rightSurface = Assert.Single(rightChunk.Where(s => s.X == 8 && s.Y == 2 && s.Z == 8));

        Assert.True(leftSurface.LocalLight > 0);
        Assert.True(rightSurface.LocalLight > 0);
        Assert.InRange(Math.Abs(leftSurface.LocalLight - rightSurface.LocalLight), 0, 2);
    }

    [Fact(DisplayName = "BuildLocalLightField распространяет локальный свет по воздуху и не проходит сквозь стены")]
    public void World_BuildLocalLightField_PropagatesAndStopsAtWalls()
    {
        var world = new WorldMap(width: 32, height: 6, depth: 32, chunkSize: 16, seed: 0);

        var buildMethod = typeof(WorldMap).GetMethod("BuildLocalLightField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(buildMethod);
        Func<int, int, int, bool> isSolid = (x, y, z) =>
        {
            if (!world.IsInside(x, y, z))
            {
                return false;
            }

            if (z != 2)
            {
                return true;
            }

            return x == 10;
        };

        Func<int, int, int, int> emission = (x, y, z) => x == 3 && y == 2 && z == 2 ? WorldMap.MaxLocalLight : 0;

        var field = buildMethod!.Invoke(world, [0, 0, isSolid, emission])!;
        var fieldType = field.GetType();
        var get = fieldType.GetMethod("Get", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(get);

        var atEmitter = (int)get!.Invoke(field, [3, 2, 2])!;
        var alongCorridor = (int)get.Invoke(field, [7, 2, 2])!;
        var behindWall = (int)get.Invoke(field, [11, 2, 2])!;
        var outsideCorridor = (int)get.Invoke(field, [7, 2, 1])!;

        Assert.Equal(WorldMap.MaxLocalLight, atEmitter);
        Assert.Equal(WorldMap.MaxLocalLight - 4, alongCorridor);
        Assert.Equal(0, behindWall);
        Assert.Equal(0, outsideCorridor);
    }

    [Fact(DisplayName = "LocalLightField.SetMax покрывает границы и отказ от ослабления значения")]
    public void World_LocalLightField_SetMax_CoversBoundsAndClamp()
    {
        var fieldType = typeof(WorldMap).GetNestedType("LocalLightField", BindingFlags.NonPublic);
        Assert.NotNull(fieldType);
        var field = Activator.CreateInstance(fieldType!, 1, 2, 1, 2, 4);
        var setMax = fieldType.GetMethod("SetMax", BindingFlags.Instance | BindingFlags.Public);
        var get = fieldType.GetMethod("Get", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(setMax);
        Assert.NotNull(get);

        Assert.False((bool)setMax!.Invoke(field, [0, 0, 0, 5])!);
        Assert.True((bool)setMax.Invoke(field, [1, 0, 1, WorldMap.MaxLocalLight + 4])!);
        Assert.False((bool)setMax.Invoke(field, [1, 0, 1, 7])!);
        Assert.Equal(WorldMap.MaxLocalLight, (int)get!.Invoke(field, [1, 0, 1])!);
        Assert.Equal(0, (int)get.Invoke(field, [9, 0, 9])!);
    }

    [Fact(DisplayName = "SetLocalLightSource помечает чанки грязными и снимет свет при удалении источника")]
    public void World_SetLocalLightSource_RefreshesSurfaceCache()
    {
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
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

        BuildRoom(world, originX: 2, originZ: 2, roofOpeningX: -1, roofOpeningZ: -1);
        world.SetBlock(4, 2, 4, BlockType.Stone);
        world.EnsureChunksAround(new Vector3(4.5f, 3f, 4.5f), radiusInChunks: 1);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 3f, 4.5f), maxChunks: 16);
        Assert.True(world.TryGetChunkSurfaceBlocks(0, 0, out var darkCache));
        var darkSurface = Assert.Single(darkCache.Where(s => s.X == 4 && s.Y == 2 && s.Z == 4));
        Assert.Equal(0, darkSurface.LocalLight);

        world.SetLocalLightSource(4, 3, 3, WorldMap.MaxLocalLight);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 3f, 4.5f), maxChunks: 16);
        Assert.True(world.TryGetChunkSurfaceBlocks(0, 0, out var litCache));
        var litSurface = Assert.Single(litCache.Where(s => s.X == 4 && s.Y == 2 && s.Z == 4));
        Assert.True(litSurface.LocalLight > 0);

        world.SetLocalLightSource(4, 3, 3, 0);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(4.5f, 3f, 4.5f), maxChunks: 16);
        Assert.True(world.TryGetChunkSurfaceBlocks(0, 0, out var clearedCache));
        var clearedSurface = Assert.Single(clearedCache.Where(s => s.X == 4 && s.Y == 2 && s.Z == 4));
        Assert.Equal(0, clearedSurface.LocalLight);
    }

    [Fact(DisplayName = "API локального света обрабатывает выход за границы и отсутствие источника")]
    public void World_LocalLightApi_HandlesBoundsAndMissingSource()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8, chunkSize: 8, seed: 0);

        world.SetLocalLightSource(-1, 2, 2, WorldMap.MaxLocalLight);
        world.SetLocalLightSource(2, 2, 2, WorldMap.MaxLocalLight);

        Assert.Equal(0, world.GetLocalLightSource(-1, 2, 2));
        Assert.Equal(WorldMap.MaxLocalLight, world.GetLocalLightSource(2, 2, 2));
        Assert.Equal(0, world.GetLocalLightSource(3, 2, 2));
    }

    [Fact(DisplayName = "Snapshot local light и snapshot rebuild читают только локальные источники чанка")]
    public void World_RebuildChunkSurfaceBlocksFromSnapshot_UsesLocalLightSnapshot()
    {
        var world = new WorldMap(width: 24, height: 8, depth: 24, chunkSize: 8, seed: 0);
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

        BuildRoom(world, originX: 2, originZ: 2, roofOpeningX: -1, roofOpeningZ: -1);
        world.SetBlock(4, 2, 4, BlockType.Stone);
        world.SetLocalLightSource(4, 3, 3, WorldMap.MaxLocalLight);
        world.SetLocalLightSource(20, 3, 20, WorldMap.MaxLocalLight);

        world.EnsureChunksAround(new Vector3(4.5f, 3f, 4.5f), radiusInChunks: 2);

        var snapshotMethod = typeof(WorldMap).GetMethod("TryCreateSurfaceSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        var rebuildMethod = typeof(WorldMap).GetMethod("RebuildChunkSurfaceBlocksFromSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        var lightSnapshotMethod = typeof(WorldMap).GetMethod("SnapshotLocalLightSourcesForChunk", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(snapshotMethod);
        Assert.NotNull(rebuildMethod);
        Assert.NotNull(lightSnapshotMethod);

        var args = new object?[] { 0, 0, null };
        Assert.True((bool)snapshotMethod!.Invoke(world, args)!);
        var snapshot = Assert.IsAssignableFrom<IReadOnlyDictionary<(int ChunkX, int ChunkZ), BlockType[,,]>>(args[2]);

        var rebuilt = Assert.IsAssignableFrom<IReadOnlyList<WorldMap.SurfaceBlock>>(rebuildMethod!.Invoke(world, [0, 0, snapshot])!);
        var litSurface = Assert.Single(rebuilt.Where(s => s.X == 4 && s.Y == 2 && s.Z == 4));
        Assert.True(litSurface.LocalLight > 0);

        var localSnapshot = Assert.IsType<Dictionary<(int X, int Y, int Z), byte>>(lightSnapshotMethod!.Invoke(world, [0, 0])!);
        Assert.Contains((4, 3, 3), localSnapshot.Keys);
        Assert.DoesNotContain((20, 3, 20), localSnapshot.Keys);
    }

    [Fact(DisplayName = "BuildLocalLightField не распространяет источник света с уровнем 1 дальше своей клетки")]
    public void World_BuildLocalLightField_DoesNotSpreadUnitLight()
    {
        var world = new WorldMap(width: 16, height: 6, depth: 16, chunkSize: 8, seed: 0);
        var buildMethod = typeof(WorldMap).GetMethod("BuildLocalLightField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(buildMethod);

        Func<int, int, int, bool> isSolid = (_, _, _) => false;
        Func<int, int, int, int> emission = (x, y, z) => x == 2 && y == 2 && z == 2 ? 1 : 0;

        var field = buildMethod!.Invoke(world, [0, 0, isSolid, emission])!;
        var get = field.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(get);

        Assert.Equal(1, (int)get!.Invoke(field, [2, 2, 2])!);
        Assert.Equal(0, (int)get.Invoke(field, [3, 2, 2])!);
    }

    [Fact(DisplayName = "Кэш поверхностей чанка не скрывает старую картинку до пересборки и обновляется после изменения блока")]
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
        Assert.NotEmpty(dirtySurface);
        Assert.Same(warmSurface, dirtySurface);

        var rebuiltAfterChange = world.RebuildDirtyChunkSurfaces(new Vector3(17.5f, 2f, 17.5f), maxChunks: 2);
        Assert.True(rebuiltAfterChange >= 1);

        Assert.True(world.TryGetChunkSurfaceBlocks(1, 1, out var refreshedSurface));
        Assert.NotEmpty(refreshedSurface);
    }

    private static void BuildRoom(WorldMap world, int originX, int originZ, int roofOpeningX, int roofOpeningZ)
    {
        for (var x = originX; x < originX + 5; x++)
        {
            for (var z = originZ; z < originZ + 5; z++)
            {
                world.SetBlock(x, 1, z, BlockType.Stone);
                world.SetBlock(x, 5, z, BlockType.Stone);
            }
        }

        for (var y = 1; y <= 5; y++)
        {
            for (var offset = 0; offset < 5; offset++)
            {
                world.SetBlock(originX, y, originZ + offset, BlockType.Stone);
                world.SetBlock(originX + 4, y, originZ + offset, BlockType.Stone);
                world.SetBlock(originX + offset, y, originZ, BlockType.Stone);
                world.SetBlock(originX + offset, y, originZ + 4, BlockType.Stone);
            }
        }

        for (var x = originX + 1; x < originX + 4; x++)
        {
            for (var y = 2; y < 5; y++)
            {
                for (var z = originZ + 1; z < originZ + 4; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        if (roofOpeningX >= 0 && roofOpeningZ >= 0)
        {
            world.SetBlock(roofOpeningX, 5, roofOpeningZ, BlockType.Air);
        }
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

    [Fact(DisplayName = "TryGetChunkSurfaceBlocks возвращает пустой список для загруженного чанка без dirty-флага и без surface-cache")]
    public void World_TryGetChunkSurfaceBlocks_ReturnsEmptyForLoadedChunkWithoutCache()
    {
        var world = new WorldMap(width: 32, height: 8, depth: 32, chunkSize: 16, seed: 0);
        world.EnsureChunksAround(new Vector3(8f, 0f, 8f), radiusInChunks: 0);

        var dirtyField = typeof(WorldMap).GetField("_dirtySurfaceChunks", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(dirtyField);
        var dirty = (HashSet<(int ChunkX, int ChunkZ)>)dirtyField!.GetValue(world)!;
        dirty.Clear();

        var ok = world.TryGetChunkSurfaceBlocks(0, 0, out var blocks);

        Assert.True(ok);
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
        var ok = (bool)method!.Invoke(world, [0, 0, 0.9f, (Func<int, int, float>)SignalAt, 2])!;

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
