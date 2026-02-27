namespace AIG.Game.World.Chunks;

internal sealed class Chunk
{
    private readonly BlockType[,,] _blocks;

    public Chunk(int size, int height)
    {
        Size = size;
        Height = height;
        _blocks = new BlockType[size, height, size];
    }

    public int Size { get; }
    public int Height { get; }

    public BlockType Get(int x, int y, int z) => _blocks[x, y, z];

    public void Set(int x, int y, int z, BlockType value) => _blocks[x, y, z] = value;
}
