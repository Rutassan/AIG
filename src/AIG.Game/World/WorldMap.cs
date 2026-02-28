using System.Numerics;
using AIG.Game.World.Chunks;

namespace AIG.Game.World;

public sealed class WorldMap
{
    private readonly Dictionary<(int ChunkX, int ChunkZ), Chunk> _chunks = new();
    private readonly Dictionary<(int X, int Y, int Z), BlockType> _overrides = new();
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

    public void EnsureChunksAround(Vector3 position, int radiusInChunks)
    {
        var worldX = (int)MathF.Floor(position.X);
        var worldZ = (int)MathF.Floor(position.Z);
        EnsureChunksAround(worldX, worldZ, radiusInChunks);
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

    public void UnloadFarChunks(Vector3 position, int keepRadiusInChunks)
    {
        if (_chunks.Count == 0)
        {
            return;
        }

        if (keepRadiusInChunks < 0)
        {
            _chunks.Clear();
            return;
        }

        var worldX = (int)MathF.Floor(position.X);
        var worldZ = (int)MathF.Floor(position.Z);
        var clampedX = Math.Clamp(worldX, 0, Width - 1);
        var clampedZ = Math.Clamp(worldZ, 0, Depth - 1);
        var centerChunkX = clampedX / ChunkSize;
        var centerChunkZ = clampedZ / ChunkSize;

        var loadedChunks = new List<(int ChunkX, int ChunkZ)>(_chunks.Keys);
        foreach (var key in loadedChunks)
        {
            if (Math.Abs(key.ChunkX - centerChunkX) > keepRadiusInChunks
                || Math.Abs(key.ChunkZ - centerChunkZ) > keepRadiusInChunks)
            {
                _chunks.Remove(key);
            }
        }
    }

    public bool IsChunkLoaded(int chunkX, int chunkZ)
    {
        return _chunks.ContainsKey((chunkX, chunkZ));
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
        return chunk;
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

        const int treeReach = 3;
        var chunkMinX = chunkX * ChunkSize;
        var chunkMaxX = Math.Min(Width - 1, chunkMinX + ChunkSize - 1);
        var chunkMinZ = chunkZ * ChunkSize;
        var chunkMaxZ = Math.Min(Depth - 1, chunkMinZ + ChunkSize - 1);

        var scanMinX = Math.Max(0, chunkMinX - treeReach);
        var scanMaxX = Math.Min(Width - 1, chunkMaxX + treeReach);
        var scanMinZ = Math.Max(0, chunkMinZ - treeReach);
        var scanMaxZ = Math.Min(Depth - 1, chunkMaxZ + treeReach);

        for (var rootX = scanMinX; rootX <= scanMaxX; rootX++)
        {
            for (var rootZ = scanMinZ; rootZ <= scanMaxZ; rootZ++)
            {
                if (!ShouldPlaceTree(rootX, rootZ))
                {
                    continue;
                }

                var terrainTop = GetTerrainTopY(rootX, rootZ);
                if (terrainTop + 8 >= Height)
                {
                    continue;
                }

                PlaceTreeIntoChunk(chunk, chunkX, chunkZ, rootX, terrainTop + 1, rootZ);
            }
        }
    }

    private bool ShouldPlaceTree(int x, int z)
    {
        if (x < 2 || z < 2 || x >= Width - 2 || z >= Depth - 2)
        {
            return false;
        }

        var signal = EvaluateTreeSignal(x, z);
        if (signal < 0.58f)
        {
            return false;
        }

        // Keep forest dense but avoid merged mega-canopies:
        // place one trunk per small neighborhood based on local max signal.
        if (!IsLocalTreePeak(x, z, signal))
        {
            return false;
        }

        var h = GetTerrainTopY(x, z);
        var hX = GetTerrainTopY(Math.Min(Width - 1, x + 1), z);
        var hZ = GetTerrainTopY(x, Math.Min(Depth - 1, z + 1));
        var slope = Math.Abs(h - hX) + Math.Abs(h - hZ);
        return slope <= 3;
    }

    private float EvaluateTreeSignal(int x, int z)
    {
        var biome = FractalNoise((x + Seed * 0.19f) / 72f, (z - Seed * 0.13f) / 72f, octaves: 3, lacunarity: 2f, gain: 0.5f);
        var localChance = ValueNoise((x + Seed * 0.51f) / 9f, (z - Seed * 0.47f) / 9f);
        return biome * 0.62f + localChance * 0.38f;
    }

    private bool IsLocalTreePeak(int x, int z, float signal)
    {
        const int radius = 2;
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

                var other = EvaluateTreeSignal(nx, nz);
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
        var trunkHeight = 4 + Math.Abs(Hash(rootX, rootZ, Seed * 97 + 13) % 3); // 4..6
        var trunkTopY = rootY + trunkHeight - 1;

        for (var y = rootY; y <= trunkTopY; y++)
        {
            SetBlockInChunk(chunk, chunkX, chunkZ, rootX, y, rootZ, BlockType.Wood, onlyIfAir: false);
        }

        for (var y = trunkTopY - 1; y <= trunkTopY + 1; y++)
        {
            var layerOffset = Math.Abs(y - trunkTopY);
            var radius = layerOffset == 0 ? 2 : 1;
            for (var x = rootX - radius; x <= rootX + radius; x++)
            {
                for (var z = rootZ - radius; z <= rootZ + radius; z++)
                {
                    if (Math.Abs(x - rootX) + Math.Abs(z - rootZ) > radius + 1)
                    {
                        continue;
                    }

                    if (x == rootX && z == rootZ && y <= trunkTopY)
                    {
                        continue;
                    }

                    SetBlockInChunk(chunk, chunkX, chunkZ, x, y, z, BlockType.Leaves, onlyIfAir: true);
                }
            }
        }

        SetBlockInChunk(chunk, chunkX, chunkZ, rootX, trunkTopY + 2, rootZ, BlockType.Leaves, onlyIfAir: true);
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
