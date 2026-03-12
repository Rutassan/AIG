using System.Numerics;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using AIG.Game.World.Chunks;

namespace AIG.Game.World;

public sealed class WorldMap
{
    private const int MaxChunkGenerationsInFlight = 2;
    private const int MaxSurfaceRebuildsInFlight = 2;
    private const int TreeVariantCount = 5;
    public const int MaxSunVisibility = 6;
    private static readonly Vector3 SunLightDirection = Vector3.Normalize(new Vector3(-0.62f, -0.74f, -0.24f));
    private static readonly Vector3 SunTraceDirection = -SunLightDirection;

    public readonly record struct SurfaceBlock(
        int X,
        int Y,
        int Z,
        BlockType Block,
        int VisibleFaces,
        bool TopVisible,
        int SkyExposure,
        int AmbientOcclusion = 0,
        int ReliefExposure = 0,
        int SunVisibility = MaxSunVisibility);

    private static readonly IReadOnlyList<SurfaceBlock> EmptySurfaceBlocks = Array.Empty<SurfaceBlock>();
    private readonly record struct GeneratedChunkResult(int ChunkX, int ChunkZ, Chunk Chunk);
    private readonly record struct SurfaceRebuildResult(int ChunkX, int ChunkZ, int Revision, IReadOnlyList<SurfaceBlock> Blocks);
    private readonly Dictionary<(int ChunkX, int ChunkZ), Chunk> _chunks = new();
    private readonly Dictionary<(int ChunkX, int ChunkZ), IReadOnlyList<SurfaceBlock>> _chunkSurfaceCache = new();
    private readonly Dictionary<(int ChunkX, int ChunkZ), int> _chunkSurfaceCacheRevision = new();
    private readonly HashSet<(int ChunkX, int ChunkZ)> _dirtySurfaceChunks = new();
    private readonly Dictionary<(int ChunkX, int ChunkZ), int> _surfaceRevisions = new();
    private readonly Dictionary<(int X, int Y, int Z), BlockType> _overrides = new();
    private readonly object _backgroundSync = new();
    private readonly HashSet<(int ChunkX, int ChunkZ)> _pendingChunkGenerations = new();
    private readonly HashSet<(int ChunkX, int ChunkZ)> _pendingSurfaceRebuilds = new();
    private readonly ConcurrentQueue<GeneratedChunkResult> _completedChunkGenerations = new();
    private readonly ConcurrentQueue<SurfaceRebuildResult> _completedSurfaceRebuilds = new();
    private int _chunkGenerationsInFlight;
    private int _surfaceRebuildsInFlight;
    private readonly int _chunkCountX;
    private readonly int _chunkCountZ;

    public WorldMap(int width, int height, int depth, int chunkSize = 16, int seed = 0)
    {
        Width = width;
        Height = height;
        Depth = depth;
        ChunkSize = Math.Max(1, chunkSize);
        Seed = seed;
        _chunkCountX = (Width + ChunkSize - 1) / ChunkSize;
        _chunkCountZ = (Depth + ChunkSize - 1) / ChunkSize;
    }

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public int ChunkSize { get; }
    public int Seed { get; }
    public int LoadedChunkCount => _chunks.Count;
    public int ChunkCountX => _chunkCountX;
    public int ChunkCountZ => _chunkCountZ;
    internal static Vector3 GetSunLightDirection() => SunLightDirection;

    public BlockType GetBlock(int x, int y, int z)
    {
        if (!IsInside(x, y, z))
        {
            return BlockType.Air;
        }

        var (chunk, localX, localZ) = ResolveChunkCoords(x, z);
        return chunk.Get(localX, y, localZ);
    }

    public void SetBlock(int x, int y, int z, BlockType blockType)
    {
        if (!IsInside(x, y, z))
        {
            return;
        }

        var (chunk, localX, localZ) = ResolveChunkCoords(x, z);
        chunk.Set(localX, y, localZ, blockType);
        _overrides[(x, y, z)] = blockType;

        var chunkX = x / ChunkSize;
        var chunkZ = z / ChunkSize;
        MarkChunkAndNeighborsDirty(chunkX, chunkZ);
    }

    public bool IsSolid(int x, int y, int z)
    {
        return GetBlock(x, y, z) != BlockType.Air;
    }

    public bool IsSolidAt(Vector3 point)
    {
        var x = (int)MathF.Floor(point.X);
        var y = (int)MathF.Floor(point.Y);
        var z = (int)MathF.Floor(point.Z);
        return IsSolid(x, y, z);
    }

    public bool IsInside(int x, int y, int z)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height && z >= 0 && z < Depth;
    }

    public int GetTerrainTopY(int x, int z)
    {
        if (!IsInsideXZ(x, z))
        {
            return 0;
        }

        if (Seed == 0)
        {
            return Math.Min(1, Height - 1);
        }

        return CalculateTerrainTopY(x, z);
    }

    public int GetTopSolidY(int x, int z)
    {
        if (!IsInsideXZ(x, z))
        {
            return 0;
        }

        var (chunk, localX, localZ) = ResolveChunkCoords(x, z);
        for (var y = Height - 1; y >= 0; y--)
        {
            if (chunk.Get(localX, y, localZ) != BlockType.Air)
            {
                return y;
            }
        }

        return 0;
    }

    public void EnsureChunksAround(Vector3 position, int radiusInChunks)
    {
        var worldX = (int)MathF.Floor(position.X);
        var worldZ = (int)MathF.Floor(position.Z);
        EnsureChunksAround(worldX, worldZ, radiusInChunks);
    }

    public int EnsureChunksAroundBudgeted(Vector3 position, int radiusInChunks, int maxNewChunks)
    {
        var worldX = (int)MathF.Floor(position.X);
        var worldZ = (int)MathF.Floor(position.Z);
        return EnsureChunksAroundBudgeted(worldX, worldZ, radiusInChunks, maxNewChunks);
    }

    public void EnsureChunksAround(int worldX, int worldZ, int radiusInChunks)
    {
        if (_chunkCountX == 0 || _chunkCountZ == 0)
        {
            return;
        }

        var clampedX = Math.Clamp(worldX, 0, Width - 1);
        var clampedZ = Math.Clamp(worldZ, 0, Depth - 1);
        var centerChunkX = clampedX / ChunkSize;
        var centerChunkZ = clampedZ / ChunkSize;
        var radius = Math.Max(0, radiusInChunks);

        var minChunkX = Math.Max(0, centerChunkX - radius);
        var maxChunkX = Math.Min(_chunkCountX - 1, centerChunkX + radius);
        var minChunkZ = Math.Max(0, centerChunkZ - radius);
        var maxChunkZ = Math.Min(_chunkCountZ - 1, centerChunkZ + radius);

        for (var chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
        {
            for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
            {
                _ = GetOrCreateChunk(chunkX, chunkZ);
            }
        }
    }

    public int EnsureChunksAroundBudgeted(int worldX, int worldZ, int radiusInChunks, int maxNewChunks)
    {
        if (_chunkCountX == 0 || _chunkCountZ == 0 || maxNewChunks <= 0)
        {
            return 0;
        }

        var clampedX = Math.Clamp(worldX, 0, Width - 1);
        var clampedZ = Math.Clamp(worldZ, 0, Depth - 1);
        var centerChunkX = clampedX / ChunkSize;
        var centerChunkZ = clampedZ / ChunkSize;
        var radius = Math.Max(0, radiusInChunks);
        var created = 0;

        for (var ring = 0; ring <= radius && created < maxNewChunks; ring++)
        {
            var minChunkX = Math.Max(0, centerChunkX - ring);
            var maxChunkX = Math.Min(_chunkCountX - 1, centerChunkX + ring);
            var minChunkZ = Math.Max(0, centerChunkZ - ring);
            var maxChunkZ = Math.Min(_chunkCountZ - 1, centerChunkZ + ring);

            for (var chunkX = minChunkX; chunkX <= maxChunkX && created < maxNewChunks; chunkX++)
            {
                for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ && created < maxNewChunks; chunkZ++)
                {
                    if (ring > 0
                        && chunkX > minChunkX && chunkX < maxChunkX
                        && chunkZ > minChunkZ && chunkZ < maxChunkZ)
                    {
                        continue;
                    }

                    var key = (chunkX, chunkZ);
                    if (_chunks.ContainsKey(key))
                    {
                        continue;
                    }

                    _ = GetOrCreateChunk(chunkX, chunkZ);
                    created++;
                }
            }
        }

        return created;
    }

    public int EnsureChunksAroundBudgetedAsync(Vector3 position, int radiusInChunks, int maxNewChunks)
    {
        var worldX = (int)MathF.Floor(position.X);
        var worldZ = (int)MathF.Floor(position.Z);
        return EnsureChunksAroundBudgetedAsync(worldX, worldZ, radiusInChunks, maxNewChunks);
    }

    public int EnsureChunksAroundBudgetedAsync(int worldX, int worldZ, int radiusInChunks, int maxNewChunks)
    {
        if (_chunkCountX == 0 || _chunkCountZ == 0 || maxNewChunks <= 0)
        {
            return 0;
        }

        var clampedX = Math.Clamp(worldX, 0, Width - 1);
        var clampedZ = Math.Clamp(worldZ, 0, Depth - 1);
        var centerChunkX = clampedX / ChunkSize;
        var centerChunkZ = clampedZ / ChunkSize;
        var radius = Math.Max(0, radiusInChunks);
        var queued = 0;

        for (var ring = 0; ring <= radius && queued < maxNewChunks; ring++)
        {
            var minChunkX = Math.Max(0, centerChunkX - ring);
            var maxChunkX = Math.Min(_chunkCountX - 1, centerChunkX + ring);
            var minChunkZ = Math.Max(0, centerChunkZ - ring);
            var maxChunkZ = Math.Min(_chunkCountZ - 1, centerChunkZ + ring);

            for (var chunkX = minChunkX; chunkX <= maxChunkX && queued < maxNewChunks; chunkX++)
            {
                for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ && queued < maxNewChunks; chunkZ++)
                {
                    if (ring > 0
                        && chunkX > minChunkX && chunkX < maxChunkX
                        && chunkZ > minChunkZ && chunkZ < maxChunkZ)
                    {
                        continue;
                    }

                    if (!TryQueueChunkGeneration(chunkX, chunkZ))
                    {
                        continue;
                    }

                    queued++;
                }
            }
        }

        return queued;
    }

    public int QueueDirtyChunkSurfacesAsync(Vector3 position, int maxChunks)
    {
        if (_chunkCountX == 0 || _chunkCountZ == 0 || maxChunks <= 0)
        {
            return 0;
        }

        var clampedX = Math.Clamp((int)MathF.Floor(position.X), 0, Width - 1);
        var clampedZ = Math.Clamp((int)MathF.Floor(position.Z), 0, Depth - 1);
        var centerChunkX = clampedX / ChunkSize;
        var centerChunkZ = clampedZ / ChunkSize;
        return QueueDirtyChunkSurfacesAsync(centerChunkX, centerChunkZ, maxChunks);
    }

    public int QueueDirtyChunkSurfacesAsync(int centerChunkX, int centerChunkZ, int maxChunks)
    {
        if (maxChunks <= 0 || _dirtySurfaceChunks.Count == 0)
        {
            return 0;
        }

        var queued = 0;
        var attempts = 0;
        var maxAttempts = Math.Max(maxChunks * 4, 8);
        while (queued < maxChunks && attempts < maxAttempts)
        {
            attempts++;
            if (!TryGetClosestDirtyLoadedChunkForBackground(centerChunkX, centerChunkZ, out var key))
            {
                break;
            }

            if (!TryQueueSurfaceRebuild(key.ChunkX, key.ChunkZ))
            {
                continue;
            }

            queued++;
        }

        return queued;
    }

    public int ApplyBackgroundStreamingResults(int maxChunkApplies, int maxSurfaceApplies)
    {
        var applied = 0;
        var chunkBudget = Math.Max(0, maxChunkApplies);
        var surfaceBudget = Math.Max(0, maxSurfaceApplies);

        while (chunkBudget > 0 && _completedChunkGenerations.TryDequeue(out var chunkResult))
        {
            chunkBudget--;
            var key = (chunkResult.ChunkX, chunkResult.ChunkZ);
            if (_chunks.ContainsKey(key))
            {
                continue;
            }

            _chunks[key] = chunkResult.Chunk;
            MarkChunkAndNeighborsDirty(chunkResult.ChunkX, chunkResult.ChunkZ);
            applied++;
        }

        while (surfaceBudget > 0 && _completedSurfaceRebuilds.TryDequeue(out var surfaceResult))
        {
            surfaceBudget--;
            var key = (surfaceResult.ChunkX, surfaceResult.ChunkZ);
            if (!_chunks.ContainsKey(key))
            {
                continue;
            }

            if (GetSurfaceRevision(key) != surfaceResult.Revision)
            {
                continue;
            }

            _chunkSurfaceCache[key] = surfaceResult.Blocks;
            _chunkSurfaceCacheRevision[key] = surfaceResult.Revision;
            _dirtySurfaceChunks.Remove(key);
            applied++;
        }

        return applied;
    }

    public void UnloadFarChunks(Vector3 position, int keepRadiusInChunks)
    {
        if (_chunks.Count == 0)
        {
            return;
        }

        if (keepRadiusInChunks < 0)
        {
            _chunks.Clear();
            _chunkSurfaceCache.Clear();
            _chunkSurfaceCacheRevision.Clear();
            _dirtySurfaceChunks.Clear();
            _surfaceRevisions.Clear();
            return;
        }

        var worldX = (int)MathF.Floor(position.X);
        var worldZ = (int)MathF.Floor(position.Z);
        var clampedX = Math.Clamp(worldX, 0, Width - 1);
        var clampedZ = Math.Clamp(worldZ, 0, Depth - 1);
        var centerChunkX = clampedX / ChunkSize;
        var centerChunkZ = clampedZ / ChunkSize;

        var removedChunks = new List<(int ChunkX, int ChunkZ)>();
        var loadedChunks = new List<(int ChunkX, int ChunkZ)>(_chunks.Keys);
        foreach (var key in loadedChunks)
        {
            if (Math.Abs(key.ChunkX - centerChunkX) > keepRadiusInChunks
                || Math.Abs(key.ChunkZ - centerChunkZ) > keepRadiusInChunks)
            {
                _chunks.Remove(key);
                _chunkSurfaceCache.Remove(key);
                _chunkSurfaceCacheRevision.Remove(key);
                _dirtySurfaceChunks.Remove(key);
                _surfaceRevisions.Remove(key);
                removedChunks.Add(key);
            }
        }

        foreach (var removed in removedChunks)
        {
            MarkChunkAndNeighborsDirty(removed.ChunkX, removed.ChunkZ);
        }
    }

    public bool IsChunkLoaded(int chunkX, int chunkZ)
    {
        return _chunks.ContainsKey((chunkX, chunkZ));
    }

    public bool TryGetChunkSurfaceBlocks(int chunkX, int chunkZ, out IReadOnlyList<SurfaceBlock> blocks)
    {
        var key = (chunkX, chunkZ);
        if (!_chunks.ContainsKey(key))
        {
            blocks = EmptySurfaceBlocks;
            return false;
        }

        var hasCachedSurface = _chunkSurfaceCache.TryGetValue(key, out var cached);
        if (_dirtySurfaceChunks.Contains(key))
        {
            blocks = hasCachedSurface ? cached! : EmptySurfaceBlocks;
            return true;
        }

        if (!hasCachedSurface)
        {
            blocks = EmptySurfaceBlocks;
            return true;
        }

        blocks = cached!;
        return true;
    }

    internal bool TryGetChunkSurfaceState(int chunkX, int chunkZ, out IReadOnlyList<SurfaceBlock> blocks, out int cacheRevision, out bool isDirty)
    {
        var key = (chunkX, chunkZ);
        if (!_chunks.ContainsKey(key))
        {
            blocks = EmptySurfaceBlocks;
            cacheRevision = 0;
            isDirty = false;
            return false;
        }

        var hasCachedSurface = _chunkSurfaceCache.TryGetValue(key, out var cached);
        blocks = hasCachedSurface ? cached! : EmptySurfaceBlocks;
        cacheRevision = _chunkSurfaceCacheRevision.TryGetValue(key, out var revision) ? revision : 0;
        isDirty = _dirtySurfaceChunks.Contains(key);
        return true;
    }

    public int RebuildDirtyChunkSurfaces(Vector3 position, int maxChunks)
    {
        if (_chunkCountX == 0 || _chunkCountZ == 0)
        {
            return 0;
        }

        var clampedX = Math.Clamp((int)MathF.Floor(position.X), 0, Width - 1);
        var clampedZ = Math.Clamp((int)MathF.Floor(position.Z), 0, Depth - 1);
        var centerChunkX = clampedX / ChunkSize;
        var centerChunkZ = clampedZ / ChunkSize;
        return RebuildDirtyChunkSurfaces(centerChunkX, centerChunkZ, maxChunks);
    }

    public int RebuildDirtyChunkSurfaces(int centerChunkX, int centerChunkZ, int maxChunks)
    {
        if (maxChunks <= 0 || _dirtySurfaceChunks.Count == 0)
        {
            return 0;
        }

        var rebuilt = 0;
        while (rebuilt < maxChunks)
        {
            if (!TryGetClosestDirtyLoadedChunk(centerChunkX, centerChunkZ, out var key))
            {
                break;
            }

            _ = RebuildChunkSurfaceBlocks(key.ChunkX, key.ChunkZ);
            rebuilt++;
        }

        return rebuilt;
    }

    private bool IsInsideXZ(int x, int z)
    {
        return x >= 0 && x < Width && z >= 0 && z < Depth;
    }

    private Chunk GetOrCreateChunk(int chunkX, int chunkZ)
    {
        if (_chunks.TryGetValue((chunkX, chunkZ), out var existing))
        {
            return existing;
        }

        var chunk = GenerateChunk(chunkX, chunkZ);
        _chunks[(chunkX, chunkZ)] = chunk;
        MarkChunkAndNeighborsDirty(chunkX, chunkZ);
        return chunk;
    }

    private IReadOnlyList<SurfaceBlock> RebuildChunkSurfaceBlocks(int chunkX, int chunkZ)
    {
        var key = (chunkX, chunkZ);
        if (!_chunks.TryGetValue(key, out var chunk))
        {
            _chunkSurfaceCache.Remove(key);
            _chunkSurfaceCacheRevision.Remove(key);
            _dirtySurfaceChunks.Remove(key);
            return EmptySurfaceBlocks;
        }

        var chunkMinX = chunkX * ChunkSize;
        var chunkMinZ = chunkZ * ChunkSize;
        var blocks = new List<SurfaceBlock>(chunk.Size * chunk.Size * 3);

        for (var localX = 0; localX < chunk.Size; localX++)
        {
            var worldX = chunkMinX + localX;
            if (worldX >= Width)
            {
                continue;
            }

            for (var localZ = 0; localZ < chunk.Size; localZ++)
            {
                var worldZ = chunkMinZ + localZ;
                if (worldZ >= Depth)
                {
                    continue;
                }

                for (var y = 0; y < Height; y++)
                {
                    var block = chunk.Get(localX, y, localZ);
                    if (block == BlockType.Air)
                    {
                        continue;
                    }

                    var visibleFaces = CountVisibleFacesNoLoad(worldX, y, worldZ, out var topVisible);
                    if (visibleFaces == 0)
                    {
                        continue;
                    }

                    var skyExposure = CountSkyExposureNoLoad(worldX, y, worldZ);
                    var ambientOcclusion = CountAmbientOcclusionNoLoad(worldX, y, worldZ, topVisible);
                    var reliefExposure = CountReliefExposureNoLoad(worldX, y, worldZ, topVisible);
                    var sunVisibility = CountSunVisibilityNoLoad(worldX, y, worldZ);
                    blocks.Add(new SurfaceBlock(worldX, y, worldZ, block, visibleFaces, topVisible, skyExposure, ambientOcclusion, reliefExposure, sunVisibility));
                }
            }
        }

        _chunkSurfaceCache[key] = blocks;
        _chunkSurfaceCacheRevision[key] = GetSurfaceRevision(key);
        _dirtySurfaceChunks.Remove(key);
        return blocks;
    }

    internal bool IsSurfaceMeshingSolid(int x, int y, int z) => IsSolidNoLoad(x, y, z);

    private bool IsSolidNoLoad(int x, int y, int z)
    {
        if (!IsInside(x, y, z))
        {
            return false;
        }

        var chunkX = x / ChunkSize;
        var chunkZ = z / ChunkSize;
        if (!_chunks.TryGetValue((chunkX, chunkZ), out var chunk))
        {
            return false;
        }

        var localX = x - chunkX * ChunkSize;
        var localZ = z - chunkZ * ChunkSize;
        return chunk.Get(localX, y, localZ) != BlockType.Air;
    }

    private int CountVisibleFacesNoLoad(int x, int y, int z, out bool topVisible)
    {
        var count = 0;
        if (!IsSolidNoLoad(x + 1, y, z)) count++;
        if (!IsSolidNoLoad(x - 1, y, z)) count++;
        topVisible = !IsSolidNoLoad(x, y + 1, z);
        if (topVisible) count++;
        if (!IsSolidNoLoad(x, y - 1, z)) count++;
        if (!IsSolidNoLoad(x, y, z + 1)) count++;
        if (!IsSolidNoLoad(x, y, z - 1)) count++;
        return count;
    }

    private int CountSkyExposureNoLoad(int x, int y, int z)
    {
        var count = 0;
        if (!IsSolidNoLoad(x, y + 1, z)) count++;
        if (!IsSolidNoLoad(x + 1, y + 1, z)) count++;
        if (!IsSolidNoLoad(x - 1, y + 1, z)) count++;
        if (!IsSolidNoLoad(x, y + 1, z + 1)) count++;
        if (!IsSolidNoLoad(x, y + 1, z - 1)) count++;
        return count;
    }

    private int CountAmbientOcclusionNoLoad(int x, int y, int z, bool topVisible)
    {
        var count = 0;
        if (IsSolidNoLoad(x + 1, y, z)) count++;
        if (IsSolidNoLoad(x - 1, y, z)) count++;
        if (IsSolidNoLoad(x, y, z + 1)) count++;
        if (IsSolidNoLoad(x, y, z - 1)) count++;

        if (!topVisible)
        {
            count += 2;
        }
        else
        {
            if (IsSolidNoLoad(x + 1, y + 1, z)) count++;
            if (IsSolidNoLoad(x - 1, y + 1, z)) count++;
            if (IsSolidNoLoad(x, y + 1, z + 1)) count++;
            if (IsSolidNoLoad(x, y + 1, z - 1)) count++;
        }

        return Math.Clamp(count, 0, 8);
    }

    private int CountReliefExposureNoLoad(int x, int y, int z, bool topVisible)
    {
        if (!topVisible || y <= 0)
        {
            return 0;
        }

        var count = 0;
        if (!IsSolidNoLoad(x + 1, y - 1, z)) count++;
        if (!IsSolidNoLoad(x - 1, y - 1, z)) count++;
        if (!IsSolidNoLoad(x, y - 1, z + 1)) count++;
        if (!IsSolidNoLoad(x, y - 1, z - 1)) count++;
        return count;
    }

    private int CountSunVisibilityNoLoad(int x, int y, int z)
    {
        return CountSunVisibilityCore(x, y, z, IsSolidNoLoad);
    }

    private int GetSurfaceRevision((int ChunkX, int ChunkZ) key)
    {
        return _surfaceRevisions.TryGetValue(key, out var revision) ? revision : 0;
    }

    private void MarkChunkAndNeighborsDirty(int chunkX, int chunkZ)
    {
        MarkChunkDirtyIfLoaded(chunkX, chunkZ);
        MarkChunkDirtyIfLoaded(chunkX - 1, chunkZ);
        MarkChunkDirtyIfLoaded(chunkX + 1, chunkZ);
        MarkChunkDirtyIfLoaded(chunkX, chunkZ - 1);
        MarkChunkDirtyIfLoaded(chunkX, chunkZ + 1);
    }

    private void MarkChunkDirtyIfLoaded(int chunkX, int chunkZ)
    {
        if (chunkX < 0 || chunkZ < 0 || chunkX >= _chunkCountX || chunkZ >= _chunkCountZ)
        {
            return;
        }

        var key = (chunkX, chunkZ);
        if (_chunks.ContainsKey(key))
        {
            _dirtySurfaceChunks.Add(key);
            _surfaceRevisions[key] = GetSurfaceRevision(key) + 1;
        }
    }

    private Chunk GenerateChunk(int chunkX, int chunkZ)
    {
        var chunk = new Chunk(ChunkSize, Height);
        GenerateTerrainColumns(chunk, chunkX, chunkZ);
        GenerateTrees(chunk, chunkX, chunkZ);
        ApplyChunkOverrides(chunk, chunkX, chunkZ);
        return chunk;
    }

    private void GenerateTerrainColumns(Chunk chunk, int chunkX, int chunkZ)
    {
        for (var localX = 0; localX < chunk.Size; localX++)
        {
            for (var localZ = 0; localZ < chunk.Size; localZ++)
            {
                var worldX = chunkX * chunk.Size + localX;
                var worldZ = chunkZ * chunk.Size + localZ;
                if (!IsInsideXZ(worldX, worldZ))
                {
                    continue;
                }

                var topY = GetTerrainTopY(worldX, worldZ);
                var stoneTop = Seed == 0 ? 0 : Math.Max(0, topY - 3);
                for (var y = 0; y <= topY && y < Height; y++)
                {
                    var block = y <= stoneTop
                        ? BlockType.Stone
                        : (Seed != 0 && y == topY ? BlockType.Grass : BlockType.Dirt);
                    chunk.Set(localX, y, localZ, block);
                }
            }
        }
    }

    private void GenerateTrees(Chunk chunk, int chunkX, int chunkZ)
    {
        if (Seed == 0)
        {
            return;
        }

        const int treeReach = 4;
        var chunkMinX = chunkX * ChunkSize;
        var chunkMaxX = Math.Min(Width - 1, chunkMinX + ChunkSize - 1);
        var chunkMinZ = chunkZ * ChunkSize;
        var chunkMaxZ = Math.Min(Depth - 1, chunkMinZ + ChunkSize - 1);

        var scanMinX = Math.Max(0, chunkMinX - treeReach);
        var scanMaxX = Math.Min(Width - 1, chunkMaxX + treeReach);
        var scanMinZ = Math.Max(0, chunkMinZ - treeReach);
        var scanMaxZ = Math.Min(Depth - 1, chunkMaxZ + treeReach);
        var signalCache = new Dictionary<(int X, int Z), float>();
        var terrainCache = new Dictionary<(int X, int Z), int>();

        float GetSignal(int x, int z)
        {
            var key = (x, z);
            if (signalCache.TryGetValue(key, out var signal))
            {
                return signal;
            }

            signal = EvaluateTreeSignal(x, z);
            signalCache[key] = signal;
            return signal;
        }

        int GetTerrain(int x, int z)
        {
            var key = (x, z);
            if (terrainCache.TryGetValue(key, out var topY))
            {
                return topY;
            }

            topY = GetTerrainTopY(x, z);
            terrainCache[key] = topY;
            return topY;
        }

        for (var rootX = scanMinX; rootX <= scanMaxX; rootX++)
        {
            for (var rootZ = scanMinZ; rootZ <= scanMaxZ; rootZ++)
            {
                if (!ShouldPlaceTree(rootX, rootZ, GetSignal, GetTerrain))
                {
                    continue;
                }

                var terrainTop = GetTerrain(rootX, rootZ);
                if (terrainTop + 8 >= Height)
                {
                    continue;
                }

                PlaceTreeIntoChunk(chunk, chunkX, chunkZ, rootX, terrainTop + 1, rootZ);
            }
        }
    }

    private bool ShouldPlaceTree(int x, int z, Func<int, int, float> signalAt, Func<int, int, int> topAt)
    {
        if (x < 2 || z < 2 || x >= Width - 2 || z >= Depth - 2)
        {
            return false;
        }

        var signal = signalAt(x, z);
        if (signal < 0.52f)
        {
            return false;
        }

        var peakRadius = signal >= 0.74f ? 1 : 2;
        // Keep the forest dense inside groves, but still prevent fully merged canopies.
        if (!IsLocalTreePeak(x, z, signal, signalAt, peakRadius))
        {
            return false;
        }

        var h = topAt(x, z);
        var hX = topAt(Math.Min(Width - 1, x + 1), z);
        var hZ = topAt(x, Math.Min(Depth - 1, z + 1));
        var slope = Math.Abs(h - hX) + Math.Abs(h - hZ);
        return slope <= 4;
    }

    private float EvaluateTreeSignal(int x, int z)
    {
        var biome = FractalNoise((x + Seed * 0.19f) / 72f, (z - Seed * 0.13f) / 72f, octaves: 3, lacunarity: 2f, gain: 0.5f);
        var grove = FractalNoise((x - Seed * 0.09f) / 34f, (z + Seed * 0.17f) / 34f, octaves: 2, lacunarity: 2f, gain: 0.54f);
        var localChance = ValueNoise((x + Seed * 0.51f) / 8f, (z - Seed * 0.47f) / 8f);
        return biome * 0.44f + grove * 0.34f + localChance * 0.22f;
    }

    private bool IsLocalTreePeak(int x, int z, float signal, Func<int, int, float> signalAt, int radius)
    {
        for (var nx = x - radius; nx <= x + radius; nx++)
        {
            for (var nz = z - radius; nz <= z + radius; nz++)
            {
                if (nx == x && nz == z)
                {
                    continue;
                }

                if (!IsInsideXZ(nx, nz))
                {
                    continue;
                }

                var other = signalAt(nx, nz);
                if (other > signal + 0.005f)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void PlaceTreeIntoChunk(Chunk chunk, int chunkX, int chunkZ, int rootX, int rootY, int rootZ)
    {
        var variant = GetTreeVariant(rootX, rootZ);
        var trunkHeight = GetTreeTrunkHeight(rootX, rootZ, variant);
        var trunkTopY = rootY + trunkHeight - 1;

        for (var y = rootY; y <= trunkTopY; y++)
        {
            SetBlockInChunk(chunk, chunkX, chunkZ, rootX, y, rootZ, BlockType.Wood, onlyIfAir: false);
        }

        PlaceLowerCanopy(chunk, chunkX, chunkZ, rootX, trunkTopY, rootZ);
        switch (variant)
        {
            case 0:
                PlaceBroadCanopy(chunk, chunkX, chunkZ, rootX, trunkTopY, rootZ);
                break;
            case 1:
                PlaceLayeredCanopy(chunk, chunkX, chunkZ, rootX, trunkTopY, rootZ);
                break;
            case 2:
                PlaceAsymmetricCanopy(chunk, chunkX, chunkZ, rootX, trunkTopY, rootZ);
                break;
            case 3:
                PlaceTallCanopy(chunk, chunkX, chunkZ, rootX, trunkTopY, rootZ);
                break;
            default:
                PlaceWideCanopy(chunk, chunkX, chunkZ, rootX, trunkTopY, rootZ);
                break;
        }

        PruneSparseLeaves(chunk, chunkX, chunkZ, rootX, rootY, rootZ, trunkTopY);
    }

    private int GetTreeVariant(int rootX, int rootZ)
    {
        return Math.Abs(Hash(rootX, rootZ, Seed * 137 + 23)) % TreeVariantCount;
    }

    private int GetTreeTrunkHeight(int rootX, int rootZ, int variant)
    {
        var baseHeight = Math.Abs(Hash(rootX, rootZ, Seed * 97 + 13));
        return variant switch
        {
            0 => 4 + baseHeight % 2, // 4..5
            1 => 5 + baseHeight % 3, // 5..7
            2 => 4 + baseHeight % 3, // 4..6
            3 => 6 + baseHeight % 3, // 6..8
            _ => 5 + baseHeight % 2  // 5..6
        };
    }

    private void PlaceLowerCanopy(Chunk chunk, int chunkX, int chunkZ, int rootX, int trunkTopY, int rootZ)
    {
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY - 2, rootZ, radius: 1, manhattanPadding: 0, keepCenter: true);
        PlaceLeafCross(chunk, chunkX, chunkZ, rootX, trunkTopY - 3, rootZ, armLength: 1);
    }

    private void PlaceBroadCanopy(Chunk chunk, int chunkX, int chunkZ, int rootX, int trunkTopY, int rootZ)
    {
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY - 1, rootZ, radius: 2, manhattanPadding: 1, keepCenter: true);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY, rootZ, radius: 2, manhattanPadding: 0, keepCenter: true);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY + 1, rootZ, radius: 1, manhattanPadding: 0, keepCenter: false);
        SetBlockInChunk(chunk, chunkX, chunkZ, rootX, trunkTopY + 2, rootZ, BlockType.Leaves, onlyIfAir: true);
    }

    private void PlaceLayeredCanopy(Chunk chunk, int chunkX, int chunkZ, int rootX, int trunkTopY, int rootZ)
    {
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY - 2, rootZ, radius: 1, manhattanPadding: 0, keepCenter: true);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY - 1, rootZ, radius: 2, manhattanPadding: 1, keepCenter: true);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY, rootZ, radius: 2, manhattanPadding: 0, keepCenter: true);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY + 1, rootZ, radius: 1, manhattanPadding: 0, keepCenter: false);
        PlaceLeafCross(chunk, chunkX, chunkZ, rootX, trunkTopY + 1, rootZ, armLength: 1);
        SetBlockInChunk(chunk, chunkX, chunkZ, rootX, trunkTopY + 2, rootZ, BlockType.Leaves, onlyIfAir: true);
    }

    private void PlaceAsymmetricCanopy(Chunk chunk, int chunkX, int chunkZ, int rootX, int trunkTopY, int rootZ)
    {
        var leanHash = Math.Abs(Hash(rootX, rootZ, Seed * 191 + 41)) % 4;
        var offsetX = leanHash switch
        {
            0 => 1,
            1 => -1,
            _ => 0
        };
        var offsetZ = leanHash switch
        {
            2 => 1,
            3 => -1,
            _ => 0
        };

        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY - 1, rootZ, radius: 1, manhattanPadding: 0, keepCenter: true);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY, rootZ, radius: 1, manhattanPadding: 0, keepCenter: true);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX + offsetX, trunkTopY, rootZ + offsetZ, radius: 2, manhattanPadding: 1, keepCenter: false);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX + offsetX, trunkTopY + 1, rootZ + offsetZ, radius: 1, manhattanPadding: 0, keepCenter: false);
        PlaceLeafCross(chunk, chunkX, chunkZ, rootX + offsetX, trunkTopY, rootZ + offsetZ, armLength: 1);
        SetBlockInChunk(chunk, chunkX, chunkZ, rootX + offsetX, trunkTopY + 2, rootZ + offsetZ, BlockType.Leaves, onlyIfAir: true);
    }

    private void PlaceTallCanopy(Chunk chunk, int chunkX, int chunkZ, int rootX, int trunkTopY, int rootZ)
    {
        PlaceLeafCross(chunk, chunkX, chunkZ, rootX, trunkTopY - 2, rootZ, armLength: 1);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY - 1, rootZ, radius: 1, manhattanPadding: 0, keepCenter: true);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY, rootZ, radius: 1, manhattanPadding: 0, keepCenter: true);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY + 1, rootZ, radius: 1, manhattanPadding: -1, keepCenter: false);
        PlaceLeafCross(chunk, chunkX, chunkZ, rootX, trunkTopY + 1, rootZ, armLength: 1);
        SetBlockInChunk(chunk, chunkX, chunkZ, rootX, trunkTopY + 2, rootZ, BlockType.Leaves, onlyIfAir: true);
        SetBlockInChunk(chunk, chunkX, chunkZ, rootX, trunkTopY + 3, rootZ, BlockType.Leaves, onlyIfAir: true);
    }

    private void PlaceWideCanopy(Chunk chunk, int chunkX, int chunkZ, int rootX, int trunkTopY, int rootZ)
    {
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY - 1, rootZ, radius: 2, manhattanPadding: 1, keepCenter: true);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY, rootZ, radius: 3, manhattanPadding: 1, keepCenter: true);
        PlaceLeafLayer(chunk, chunkX, chunkZ, rootX, trunkTopY + 1, rootZ, radius: 2, manhattanPadding: 1, keepCenter: false);
        PlaceLeafCross(chunk, chunkX, chunkZ, rootX, trunkTopY + 1, rootZ, armLength: 2);
        SetBlockInChunk(chunk, chunkX, chunkZ, rootX, trunkTopY + 2, rootZ, BlockType.Leaves, onlyIfAir: true);
    }

    private void PlaceLeafLayer(
        Chunk chunk,
        int chunkX,
        int chunkZ,
        int centerX,
        int centerY,
        int centerZ,
        int radius,
        int manhattanPadding,
        bool keepCenter)
    {
        for (var x = centerX - radius; x <= centerX + radius; x++)
        {
            for (var z = centerZ - radius; z <= centerZ + radius; z++)
            {
                if (Math.Abs(x - centerX) + Math.Abs(z - centerZ) > radius + manhattanPadding)
                {
                    continue;
                }

                if (!keepCenter && x == centerX && z == centerZ)
                {
                    continue;
                }

                SetBlockInChunk(chunk, chunkX, chunkZ, x, centerY, z, BlockType.Leaves, onlyIfAir: true);
            }
        }
    }

    private void PlaceLeafCross(Chunk chunk, int chunkX, int chunkZ, int centerX, int centerY, int centerZ, int armLength)
    {
        for (var step = 1; step <= armLength; step++)
        {
            SetBlockInChunk(chunk, chunkX, chunkZ, centerX + step, centerY, centerZ, BlockType.Leaves, onlyIfAir: true);
            SetBlockInChunk(chunk, chunkX, chunkZ, centerX - step, centerY, centerZ, BlockType.Leaves, onlyIfAir: true);
            SetBlockInChunk(chunk, chunkX, chunkZ, centerX, centerY, centerZ + step, BlockType.Leaves, onlyIfAir: true);
            SetBlockInChunk(chunk, chunkX, chunkZ, centerX, centerY, centerZ - step, BlockType.Leaves, onlyIfAir: true);
        }
    }

    private void PruneSparseLeaves(Chunk chunk, int chunkX, int chunkZ, int rootX, int rootY, int rootZ, int trunkTopY)
    {
        var chunkMinX = chunkX * ChunkSize;
        var chunkMaxX = Math.Min(Width - 1, chunkMinX + ChunkSize - 1);
        var chunkMinZ = chunkZ * ChunkSize;
        var chunkMaxZ = Math.Min(Depth - 1, chunkMinZ + ChunkSize - 1);

        var minX = Math.Max(chunkMinX + 1, rootX - 4);
        var maxX = Math.Min(chunkMaxX - 1, rootX + 4);
        var minZ = Math.Max(chunkMinZ + 1, rootZ - 4);
        var maxZ = Math.Min(chunkMaxZ - 1, rootZ + 4);
        var minY = Math.Max(0, rootY - 1);
        var maxY = Math.Min(Height - 1, trunkTopY + 2);
        if (minX > maxX || minZ > maxZ || minY > maxY)
        {
            return;
        }

        var toClear = new List<(int X, int Y, int Z)>();
        for (var worldX = minX; worldX <= maxX; worldX++)
        {
            var localX = worldX - chunkMinX;
            for (var worldZ = minZ; worldZ <= maxZ; worldZ++)
            {
                var localZ = worldZ - chunkMinZ;
                for (var y = minY; y <= maxY; y++)
                {
                    if (chunk.Get(localX, y, localZ) != BlockType.Leaves)
                    {
                        continue;
                    }

                    var neighbors = 0;
                    if (IsLeafOrWood(chunk, localX + 1, y, localZ)) neighbors++;
                    if (IsLeafOrWood(chunk, localX - 1, y, localZ)) neighbors++;
                    if (IsLeafOrWood(chunk, localX, y + 1, localZ)) neighbors++;
                    if (IsLeafOrWood(chunk, localX, y - 1, localZ)) neighbors++;
                    if (IsLeafOrWood(chunk, localX, y, localZ + 1)) neighbors++;
                    if (IsLeafOrWood(chunk, localX, y, localZ - 1)) neighbors++;

                    if (neighbors <= 1)
                    {
                        toClear.Add((localX, y, localZ));
                    }
                }
            }
        }

        for (var i = 0; i < toClear.Count; i++)
        {
            var leaf = toClear[i];
            chunk.Set(leaf.X, leaf.Y, leaf.Z, BlockType.Air);
        }
    }

    private static bool IsLeafOrWood(Chunk chunk, int localX, int y, int localZ)
    {
        if (localX < 0 || localZ < 0 || localX >= chunk.Size || localZ >= chunk.Size || y < 0 || y >= chunk.Height)
        {
            return false;
        }

        var block = chunk.Get(localX, y, localZ);
        return block is BlockType.Leaves or BlockType.Wood;
    }

    private static int Hash(int x, int z, int seed)
    {
        unchecked
        {
            var h = seed;
            h = (h * 397) ^ x;
            h = (h * 397) ^ z;
            h ^= h >> 13;
            h *= 1274126177;
            h ^= h >> 16;
            return h;
        }
    }

    private int CalculateTerrainTopY(int x, int z)
    {
        var nx = (x + Seed * 0.17f) / 96f;
        var nz = (z - Seed * 0.11f) / 96f;

        var baseHeight = FractalNoise(nx, nz, octaves: 4, lacunarity: 2f, gain: 0.5f);
        var detailHeight = FractalNoise(nx * 2.7f + 17.3f, nz * 2.7f - 9.7f, octaves: 2, lacunarity: 2f, gain: 0.45f);
        var ridge = 1f - MathF.Abs(FractalNoise(nx * 1.4f - 5.2f, nz * 1.4f + 8.1f, octaves: 3, lacunarity: 2f, gain: 0.5f) * 2f - 1f);
        var normalized = Math.Clamp(baseHeight * 0.66f + detailHeight * 0.18f + ridge * 0.16f, 0f, 1f);

        var minY = Math.Max(2, Height / 10);
        var maxY = Math.Max(minY + 3, Height - 10);
        maxY = Math.Min(maxY, Height - 2);

        var height = minY + (int)MathF.Round(normalized * (maxY - minY));
        return Math.Clamp(height, minY, maxY);
    }

    private float FractalNoise(float x, float z, int octaves, float lacunarity, float gain)
    {
        var amplitude = 1f;
        var frequency = 1f;
        var total = 0f;
        var norm = 0f;

        for (var i = 0; i < octaves; i++)
        {
            total += ValueNoise(x * frequency, z * frequency) * amplitude;
            norm += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return norm <= 0f ? 0f : total / norm;
    }

    private float ValueNoise(float x, float z)
    {
        var x0 = (int)MathF.Floor(x);
        var z0 = (int)MathF.Floor(z);
        var x1 = x0 + 1;
        var z1 = z0 + 1;

        var fx = x - x0;
        var fz = z - z0;

        var v00 = HashToUnit(x0, z0);
        var v10 = HashToUnit(x1, z0);
        var v01 = HashToUnit(x0, z1);
        var v11 = HashToUnit(x1, z1);

        var u = SmoothStep(fx);
        var v = SmoothStep(fz);

        var nx0 = Lerp(v00, v10, u);
        var nx1 = Lerp(v01, v11, u);
        return Lerp(nx0, nx1, v);
    }

    private float HashToUnit(int x, int z)
    {
        var h = Hash(x, z, Seed * 31 + 17);
        return (h & 0x7fffffff) / (float)int.MaxValue;
    }

    private static float SmoothStep(float t)
    {
        return t * t * (3f - 2f * t);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private void SetBlockInChunk(Chunk chunk, int chunkX, int chunkZ, int worldX, int worldY, int worldZ, BlockType block, bool onlyIfAir)
    {
        if (!IsInside(worldX, worldY, worldZ))
        {
            return;
        }

        var ownerChunkX = worldX / ChunkSize;
        var ownerChunkZ = worldZ / ChunkSize;
        if (ownerChunkX != chunkX || ownerChunkZ != chunkZ)
        {
            return;
        }

        var localX = worldX - chunkX * ChunkSize;
        var localZ = worldZ - chunkZ * ChunkSize;
        if (onlyIfAir && chunk.Get(localX, worldY, localZ) != BlockType.Air)
        {
            return;
        }

        chunk.Set(localX, worldY, localZ, block);
    }

    private bool TryGetClosestDirtyLoadedChunk(int centerChunkX, int centerChunkZ, out (int ChunkX, int ChunkZ) key)
    {
        key = default;
        var found = false;
        var bestDistSq = int.MaxValue;
        List<(int ChunkX, int ChunkZ)>? staleKeys = null;

        foreach (var dirty in _dirtySurfaceChunks)
        {
            if (!_chunks.ContainsKey(dirty))
            {
                staleKeys ??= [];
                staleKeys.Add(dirty);
                continue;
            }

            var dx = dirty.ChunkX - centerChunkX;
            var dz = dirty.ChunkZ - centerChunkZ;
            var distSq = dx * dx + dz * dz;
            if (distSq >= bestDistSq)
            {
                continue;
            }

            bestDistSq = distSq;
            key = dirty;
            found = true;
        }

        if (staleKeys is not null)
        {
            for (var i = 0; i < staleKeys.Count; i++)
            {
                _dirtySurfaceChunks.Remove(staleKeys[i]);
                _chunkSurfaceCache.Remove(staleKeys[i]);
                _chunkSurfaceCacheRevision.Remove(staleKeys[i]);
            }
        }

        return found;
    }

    private bool TryGetClosestDirtyLoadedChunkForBackground(int centerChunkX, int centerChunkZ, out (int ChunkX, int ChunkZ) key)
    {
        key = default;
        var found = false;
        var bestDistSq = int.MaxValue;
        List<(int ChunkX, int ChunkZ)>? staleKeys = null;
        HashSet<(int ChunkX, int ChunkZ)> pendingSnapshot;
        lock (_backgroundSync)
        {
            pendingSnapshot = [.. _pendingSurfaceRebuilds];
        }

        foreach (var dirty in _dirtySurfaceChunks)
        {
            if (pendingSnapshot.Contains(dirty))
            {
                continue;
            }

            if (!_chunks.ContainsKey(dirty))
            {
                staleKeys ??= [];
                staleKeys.Add(dirty);
                continue;
            }

            var dx = dirty.ChunkX - centerChunkX;
            var dz = dirty.ChunkZ - centerChunkZ;
            var distSq = dx * dx + dz * dz;
            if (distSq >= bestDistSq)
            {
                continue;
            }

            bestDistSq = distSq;
            key = dirty;
            found = true;
        }

        if (staleKeys is not null)
        {
            for (var i = 0; i < staleKeys.Count; i++)
            {
                _dirtySurfaceChunks.Remove(staleKeys[i]);
                _chunkSurfaceCache.Remove(staleKeys[i]);
                _chunkSurfaceCacheRevision.Remove(staleKeys[i]);
                _surfaceRevisions.Remove(staleKeys[i]);
            }
        }

        return found;
    }

    private bool TryQueueChunkGeneration(int chunkX, int chunkZ)
    {
        if (chunkX < 0 || chunkZ < 0 || chunkX >= _chunkCountX || chunkZ >= _chunkCountZ)
        {
            return false;
        }

        var key = (chunkX, chunkZ);
        Dictionary<(int X, int Y, int Z), BlockType> overridesSnapshot;
        lock (_backgroundSync)
        {
            if (_chunks.ContainsKey(key) || _pendingChunkGenerations.Contains(key))
            {
                return false;
            }

            if (_chunkGenerationsInFlight >= MaxChunkGenerationsInFlight)
            {
                return false;
            }

            _pendingChunkGenerations.Add(key);
            _chunkGenerationsInFlight++;
            overridesSnapshot = SnapshotOverridesForChunk(chunkX, chunkZ);
        }

        _ = Task.Run(() =>
        {
            try
            {
                var generated = GenerateChunkFromOverrides(chunkX, chunkZ, overridesSnapshot);
                _completedChunkGenerations.Enqueue(new GeneratedChunkResult(chunkX, chunkZ, generated));
            }
            finally
            {
                lock (_backgroundSync)
                {
                    _pendingChunkGenerations.Remove(key);
                    _chunkGenerationsInFlight = Math.Max(0, _chunkGenerationsInFlight - 1);
                }
            }
        });

        return true;
    }

    private bool TryQueueSurfaceRebuild(int chunkX, int chunkZ)
    {
        var key = (chunkX, chunkZ);
        lock (_backgroundSync)
        {
            if (_pendingSurfaceRebuilds.Contains(key))
            {
                return false;
            }

            if (_surfaceRebuildsInFlight >= MaxSurfaceRebuildsInFlight)
            {
                return false;
            }

            _pendingSurfaceRebuilds.Add(key);
            _surfaceRebuildsInFlight++;
        }

        if (!TryCreateSurfaceSnapshot(chunkX, chunkZ, out var snapshot))
        {
            lock (_backgroundSync)
            {
                _pendingSurfaceRebuilds.Remove(key);
                _surfaceRebuildsInFlight = Math.Max(0, _surfaceRebuildsInFlight - 1);
            }

            _dirtySurfaceChunks.Remove(key);
            _chunkSurfaceCache.Remove(key);
            _chunkSurfaceCacheRevision.Remove(key);
            _surfaceRevisions.Remove(key);
            return false;
        }

        var revision = GetSurfaceRevision(key);
        _ = Task.Run(() =>
        {
            try
            {
                var rebuilt = RebuildChunkSurfaceBlocksFromSnapshot(chunkX, chunkZ, snapshot);
                _completedSurfaceRebuilds.Enqueue(new SurfaceRebuildResult(chunkX, chunkZ, revision, rebuilt));
            }
            finally
            {
                lock (_backgroundSync)
                {
                    _pendingSurfaceRebuilds.Remove(key);
                    _surfaceRebuildsInFlight = Math.Max(0, _surfaceRebuildsInFlight - 1);
                }
            }
        });

        return true;
    }

    private bool TryCreateSurfaceSnapshot(int chunkX, int chunkZ, out Dictionary<(int ChunkX, int ChunkZ), BlockType[,,]> snapshot)
    {
        snapshot = [];
        var key = (chunkX, chunkZ);
        if (!_chunks.TryGetValue(key, out var rootChunk))
        {
            return false;
        }

        snapshot[key] = rootChunk.SnapshotBlocks();
        for (var nx = chunkX - 1; nx <= chunkX + 1; nx++)
        {
            for (var nz = chunkZ - 1; nz <= chunkZ + 1; nz++)
            {
                if (nx == chunkX && nz == chunkZ)
                {
                    continue;
                }

                var neighborKey = (nx, nz);
                if (!_chunks.TryGetValue(neighborKey, out var neighbor))
                {
                    continue;
                }

                snapshot[neighborKey] = neighbor.SnapshotBlocks();
            }
        }

        return true;
    }

    private IReadOnlyList<SurfaceBlock> RebuildChunkSurfaceBlocksFromSnapshot(
        int chunkX,
        int chunkZ,
        IReadOnlyDictionary<(int ChunkX, int ChunkZ), BlockType[,,]> snapshot)
    {
        if (!snapshot.TryGetValue((chunkX, chunkZ), out var chunk))
        {
            return EmptySurfaceBlocks;
        }

        var chunkMinX = chunkX * ChunkSize;
        var chunkMinZ = chunkZ * ChunkSize;
        var blocks = new List<SurfaceBlock>(ChunkSize * ChunkSize * 3);

        for (var localX = 0; localX < ChunkSize; localX++)
        {
            var worldX = chunkMinX + localX;
            if (worldX >= Width)
            {
                continue;
            }

            for (var localZ = 0; localZ < ChunkSize; localZ++)
            {
                var worldZ = chunkMinZ + localZ;
                if (worldZ >= Depth)
                {
                    continue;
                }

                for (var y = 0; y < Height; y++)
                {
                    var block = chunk[localX, y, localZ];
                    if (block == BlockType.Air)
                    {
                        continue;
                    }

                    var visibleFaces = CountVisibleFacesSnapshot(worldX, y, worldZ, snapshot, out var topVisible);
                    if (visibleFaces == 0)
                    {
                        continue;
                    }

                    var skyExposure = CountSkyExposureSnapshot(worldX, y, worldZ, snapshot);
                    var ambientOcclusion = CountAmbientOcclusionSnapshot(worldX, y, worldZ, snapshot, topVisible);
                    var reliefExposure = CountReliefExposureSnapshot(worldX, y, worldZ, snapshot, topVisible);
                    var sunVisibility = CountSunVisibilitySnapshot(worldX, y, worldZ, snapshot);
                    blocks.Add(new SurfaceBlock(worldX, y, worldZ, block, visibleFaces, topVisible, skyExposure, ambientOcclusion, reliefExposure, sunVisibility));
                }
            }
        }

        return blocks;
    }

    private int CountVisibleFacesSnapshot(
        int x,
        int y,
        int z,
        IReadOnlyDictionary<(int ChunkX, int ChunkZ), BlockType[,,]> snapshot,
        out bool topVisible)
    {
        var count = 0;
        if (!IsSolidInSnapshot(x + 1, y, z, snapshot)) count++;
        if (!IsSolidInSnapshot(x - 1, y, z, snapshot)) count++;
        topVisible = !IsSolidInSnapshot(x, y + 1, z, snapshot);
        if (topVisible) count++;
        if (!IsSolidInSnapshot(x, y - 1, z, snapshot)) count++;
        if (!IsSolidInSnapshot(x, y, z + 1, snapshot)) count++;
        if (!IsSolidInSnapshot(x, y, z - 1, snapshot)) count++;
        return count;
    }

    private int CountSkyExposureSnapshot(
        int x,
        int y,
        int z,
        IReadOnlyDictionary<(int ChunkX, int ChunkZ), BlockType[,,]> snapshot)
    {
        var count = 0;
        if (!IsSolidInSnapshot(x, y + 1, z, snapshot)) count++;
        if (!IsSolidInSnapshot(x + 1, y + 1, z, snapshot)) count++;
        if (!IsSolidInSnapshot(x - 1, y + 1, z, snapshot)) count++;
        if (!IsSolidInSnapshot(x, y + 1, z + 1, snapshot)) count++;
        if (!IsSolidInSnapshot(x, y + 1, z - 1, snapshot)) count++;
        return count;
    }

    private int CountAmbientOcclusionSnapshot(
        int x,
        int y,
        int z,
        IReadOnlyDictionary<(int ChunkX, int ChunkZ), BlockType[,,]> snapshot,
        bool topVisible)
    {
        var count = 0;
        if (IsSolidInSnapshot(x + 1, y, z, snapshot)) count++;
        if (IsSolidInSnapshot(x - 1, y, z, snapshot)) count++;
        if (IsSolidInSnapshot(x, y, z + 1, snapshot)) count++;
        if (IsSolidInSnapshot(x, y, z - 1, snapshot)) count++;

        if (!topVisible)
        {
            count += 2;
        }
        else
        {
            if (IsSolidInSnapshot(x + 1, y + 1, z, snapshot)) count++;
            if (IsSolidInSnapshot(x - 1, y + 1, z, snapshot)) count++;
            if (IsSolidInSnapshot(x, y + 1, z + 1, snapshot)) count++;
            if (IsSolidInSnapshot(x, y + 1, z - 1, snapshot)) count++;
        }

        return Math.Clamp(count, 0, 8);
    }

    private int CountReliefExposureSnapshot(
        int x,
        int y,
        int z,
        IReadOnlyDictionary<(int ChunkX, int ChunkZ), BlockType[,,]> snapshot,
        bool topVisible)
    {
        if (!topVisible || y <= 0)
        {
            return 0;
        }

        var count = 0;
        if (!IsSolidInSnapshot(x + 1, y - 1, z, snapshot)) count++;
        if (!IsSolidInSnapshot(x - 1, y - 1, z, snapshot)) count++;
        if (!IsSolidInSnapshot(x, y - 1, z + 1, snapshot)) count++;
        if (!IsSolidInSnapshot(x, y - 1, z - 1, snapshot)) count++;
        return count;
    }

    private int CountSunVisibilitySnapshot(
        int x,
        int y,
        int z,
        IReadOnlyDictionary<(int ChunkX, int ChunkZ), BlockType[,,]> snapshot)
    {
        return CountSunVisibilityCore(x, y, z, (sx, sy, sz) => IsSolidInSnapshot(sx, sy, sz, snapshot));
    }

    private int CountSunVisibilityCore(int x, int y, int z, Func<int, int, int, bool> isSolid)
    {
        var origin = new Vector3(x + 0.5f, y + 0.68f, z + 0.5f);

        for (var i = 1; i <= MaxSunVisibility; i++)
        {
            var distance = 0.85f + i * 1.05f;
            var sample = origin + SunTraceDirection * distance;
            var sampleX = (int)MathF.Floor(sample.X);
            var sampleY = (int)MathF.Floor(sample.Y);
            var sampleZ = (int)MathF.Floor(sample.Z);
            if (isSolid(sampleX, sampleY, sampleZ))
            {
                return i - 1;
            }
        }

        return MaxSunVisibility;
    }

    private bool IsSolidInSnapshot(
        int x,
        int y,
        int z,
        IReadOnlyDictionary<(int ChunkX, int ChunkZ), BlockType[,,]> snapshot)
    {
        if (!IsInside(x, y, z))
        {
            return false;
        }

        var chunkX = x / ChunkSize;
        var chunkZ = z / ChunkSize;
        if (!snapshot.TryGetValue((chunkX, chunkZ), out var chunk))
        {
            return false;
        }

        var localX = x - chunkX * ChunkSize;
        var localZ = z - chunkZ * ChunkSize;
        return chunk[localX, y, localZ] != BlockType.Air;
    }

    private Dictionary<(int X, int Y, int Z), BlockType> SnapshotOverridesForChunk(int chunkX, int chunkZ)
    {
        if (_overrides.Count == 0)
        {
            return [];
        }

        var snapshot = new Dictionary<(int X, int Y, int Z), BlockType>();
        var chunkMinX = chunkX * ChunkSize;
        var chunkMaxX = Math.Min(Width - 1, chunkMinX + ChunkSize - 1);
        var chunkMinZ = chunkZ * ChunkSize;
        var chunkMaxZ = Math.Min(Depth - 1, chunkMinZ + ChunkSize - 1);

        foreach (var entry in _overrides)
        {
            var x = entry.Key.X;
            var y = entry.Key.Y;
            var z = entry.Key.Z;
            if (x < chunkMinX || x > chunkMaxX || z < chunkMinZ || z > chunkMaxZ || y < 0 || y >= Height)
            {
                continue;
            }

            snapshot[entry.Key] = entry.Value;
        }

        return snapshot;
    }

    private Chunk GenerateChunkFromOverrides(int chunkX, int chunkZ, IReadOnlyDictionary<(int X, int Y, int Z), BlockType> overridesSnapshot)
    {
        var chunk = new Chunk(ChunkSize, Height);
        GenerateTerrainColumns(chunk, chunkX, chunkZ);
        GenerateTrees(chunk, chunkX, chunkZ);
        ApplyChunkOverridesSnapshot(chunk, chunkX, chunkZ, overridesSnapshot);
        return chunk;
    }

    private void ApplyChunkOverridesSnapshot(
        Chunk chunk,
        int chunkX,
        int chunkZ,
        IReadOnlyDictionary<(int X, int Y, int Z), BlockType> overridesSnapshot)
    {
        if (overridesSnapshot.Count == 0)
        {
            return;
        }

        var chunkMinX = chunkX * ChunkSize;
        var chunkMinZ = chunkZ * ChunkSize;
        foreach (var entry in overridesSnapshot)
        {
            var x = entry.Key.X;
            var y = entry.Key.Y;
            var z = entry.Key.Z;
            if (y < 0 || y >= Height)
            {
                continue;
            }

            var localX = x - chunkMinX;
            var localZ = z - chunkMinZ;
            if (localX < 0 || localX >= ChunkSize || localZ < 0 || localZ >= ChunkSize)
            {
                continue;
            }

            chunk.Set(localX, y, localZ, entry.Value);
        }
    }

    private void ApplyChunkOverrides(Chunk chunk, int chunkX, int chunkZ)
    {
        if (_overrides.Count == 0)
        {
            return;
        }

        var chunkMinX = chunkX * ChunkSize;
        var chunkMaxX = Math.Min(Width - 1, chunkMinX + ChunkSize - 1);
        var chunkMinZ = chunkZ * ChunkSize;
        var chunkMaxZ = Math.Min(Depth - 1, chunkMinZ + ChunkSize - 1);

        foreach (var entry in _overrides)
        {
            var x = entry.Key.X;
            var y = entry.Key.Y;
            var z = entry.Key.Z;

            if (x < chunkMinX || x > chunkMaxX || z < chunkMinZ || z > chunkMaxZ || y < 0 || y >= Height)
            {
                continue;
            }

            var localX = x - chunkMinX;
            var localZ = z - chunkMinZ;
            chunk.Set(localX, y, localZ, entry.Value);
        }
    }

    private (Chunk Chunk, int LocalX, int LocalZ) ResolveChunkCoords(int x, int z)
    {
        var chunkX = x / ChunkSize;
        var chunkZ = z / ChunkSize;

        var localX = x - chunkX * ChunkSize;
        var localZ = z - chunkZ * ChunkSize;

        return (GetOrCreateChunk(chunkX, chunkZ), localX, localZ);
    }
}
