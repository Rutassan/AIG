using AIG.Game.World;

namespace AIG.Game.Core;

internal static class WorldTextureAtlas
{
    public const int TileSize = 16;
    public const int Columns = 4;
    public const int Rows = 2;
    public const string RelativePath = "assets/textures/world_atlas.png";

    private const float PixelInset = 0.35f;
    private const WorldAtlasTile Missing = WorldAtlasTile.Stone;

    internal readonly record struct UvRect(float U0, float V0, float U1, float V1);
    internal readonly record struct FaceTiles(WorldAtlasTile Top, WorldAtlasTile Bottom, WorldAtlasTile Side);

    internal enum WorldAtlasTile : byte
    {
        GrassTop = 0,
        GrassSide = 1,
        Dirt = 2,
        Stone = 3,
        WoodSide = 4,
        WoodTop = 5,
        Leaves = 6
    }

    public static FaceTiles GetFaceTiles(BlockType block)
    {
        return block switch
        {
            BlockType.Grass => new FaceTiles(WorldAtlasTile.GrassTop, WorldAtlasTile.Dirt, WorldAtlasTile.GrassSide),
            BlockType.Dirt => new FaceTiles(WorldAtlasTile.Dirt, WorldAtlasTile.Dirt, WorldAtlasTile.Dirt),
            BlockType.Stone => new FaceTiles(WorldAtlasTile.Stone, WorldAtlasTile.Stone, WorldAtlasTile.Stone),
            BlockType.Wood => new FaceTiles(WorldAtlasTile.WoodTop, WorldAtlasTile.WoodTop, WorldAtlasTile.WoodSide),
            BlockType.Leaves => new FaceTiles(WorldAtlasTile.Leaves, WorldAtlasTile.Leaves, WorldAtlasTile.Leaves),
            _ => new FaceTiles(Missing, Missing, Missing)
        };
    }

    public static UvRect GetTileUv(WorldAtlasTile tile)
    {
        var tileIndex = (int)tile;
        var tileX = tileIndex % Columns;
        var tileY = tileIndex / Columns;
        var atlasWidth = Columns * TileSize;
        var atlasHeight = Rows * TileSize;

        var left = (tileX * TileSize + PixelInset) / atlasWidth;
        var top = (tileY * TileSize + PixelInset) / atlasHeight;
        var right = ((tileX + 1) * TileSize - PixelInset) / atlasWidth;
        var bottom = ((tileY + 1) * TileSize - PixelInset) / atlasHeight;

        return new UvRect(left, top, right, bottom);
    }
}
