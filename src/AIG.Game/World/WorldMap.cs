using System.Numerics;

namespace AIG.Game.World;

public sealed class WorldMap
{
    private readonly BlockType[,,] _blocks;

    public WorldMap(int width, int height, int depth)
    {
        Width = width;
        Height = height;
        Depth = depth;
        _blocks = new BlockType[width, height, depth];
        GenerateFlatWorld();
    }

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }

    public BlockType GetBlock(int x, int y, int z)
    {
        if (!IsInside(x, y, z))
        {
            return BlockType.Air;
        }

        return _blocks[x, y, z];
    }

    public void SetBlock(int x, int y, int z, BlockType blockType)
    {
        if (!IsInside(x, y, z))
        {
            return;
        }

        _blocks[x, y, z] = blockType;
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

    private bool IsInside(int x, int y, int z)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height && z >= 0 && z < Depth;
    }

    private void GenerateFlatWorld()
    {
        for (var x = 0; x < Width; x++)
        {
            for (var z = 0; z < Depth; z++)
            {
                _blocks[x, 0, z] = BlockType.Stone;
                _blocks[x, 1, z] = BlockType.Dirt;
            }
        }
    }
}
