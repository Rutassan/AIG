using System.Numerics;
using AIG.Game.World.Chunks;

namespace AIG.Game.World;

public sealed class WorldMap
{
    private readonly Dictionary<(int ChunkX, int ChunkZ), Chunk> _chunks = new();

    public WorldMap(int width, int height, int depth, int chunkSize = 16, int seed = 0)
    {
        Width = width;
        Height = height;
        Depth = depth;
        ChunkSize = chunkSize;
        Seed = seed;

        GenerateWorld();
    }

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public int ChunkSize { get; }
    public int Seed { get; }

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

    private void GenerateWorld()
    {
        var chunkCountX = (Width + ChunkSize - 1) / ChunkSize;
        var chunkCountZ = (Depth + ChunkSize - 1) / ChunkSize;

        for (var chunkX = 0; chunkX < chunkCountX; chunkX++)
        {
            for (var chunkZ = 0; chunkZ < chunkCountZ; chunkZ++)
            {
                var chunk = new Chunk(ChunkSize, Height);
                _chunks[(chunkX, chunkZ)] = chunk;
                GenerateChunkColumns(chunk, chunkX, chunkZ);
            }
        }
    }

    private void GenerateChunkColumns(Chunk chunk, int chunkX, int chunkZ)
    {
        for (var localX = 0; localX < chunk.Size; localX++)
        {
            for (var localZ = 0; localZ < chunk.Size; localZ++)
            {
                var worldX = chunkX * chunk.Size + localX;
                var worldZ = chunkZ * chunk.Size + localZ;
                if (worldX >= Width || worldZ >= Depth)
                {
                    continue;
                }

                var topY = GetTopY(worldX, worldZ);
                for (var y = 0; y <= topY && y < Height; y++)
                {
                    var block = y == 0 ? BlockType.Stone : BlockType.Dirt;
                    chunk.Set(localX, y, localZ, block);
                }
            }
        }
    }

    private int GetTopY(int x, int z)
    {
        // For seed==0 keep 0.001/0.002 behavior: exactly two layers (y=0..1).
        if (Seed == 0)
        {
            return 1;
        }

        var hash = Hash(x, z, Seed);
        var offset = Math.Abs(hash % 2); // 0..1
        return 1 + offset;
    }

    private static int Hash(int x, int z, int seed)
    {
        unchecked
        {
            var h = seed;
            h = (h * 397) ^ x;
            h = (h * 397) ^ z;
            return h;
        }
    }

    private (Chunk Chunk, int LocalX, int LocalZ) ResolveChunkCoords(int x, int z)
    {
        var chunkX = x / ChunkSize;
        var chunkZ = z / ChunkSize;

        var localX = x % ChunkSize;
        var localZ = z % ChunkSize;

        return (_chunks[(chunkX, chunkZ)], localX, localZ);
    }
}
