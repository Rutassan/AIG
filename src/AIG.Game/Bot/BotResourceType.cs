using AIG.Game.World;

namespace AIG.Game.Bot;

internal enum BotResourceType
{
    Wood = 0,
    Stone = 1,
    Dirt = 2,
    Leaves = 3
}

internal static class BotResourceTypeExtensions
{
    internal static BlockType ToBlockType(this BotResourceType resource)
    {
        return resource switch
        {
            BotResourceType.Wood => BlockType.Wood,
            BotResourceType.Stone => BlockType.Stone,
            BotResourceType.Dirt => BlockType.Dirt,
            _ => BlockType.Leaves
        };
    }

    internal static bool Matches(this BotResourceType resource, BlockType block)
    {
        return resource switch
        {
            BotResourceType.Wood => block == BlockType.Wood,
            BotResourceType.Stone => block == BlockType.Stone,
            BotResourceType.Dirt => block is BlockType.Dirt or BlockType.Grass,
            _ => block == BlockType.Leaves
        };
    }

    internal static BotResourceType FromBlock(BlockType block)
    {
        return block switch
        {
            BlockType.Wood => BotResourceType.Wood,
            BlockType.Stone => BotResourceType.Stone,
            BlockType.Dirt or BlockType.Grass => BotResourceType.Dirt,
            _ => BotResourceType.Leaves
        };
    }

    internal static string GetLabel(this BotResourceType resource)
    {
        return resource switch
        {
            BotResourceType.Wood => "Дерево",
            BotResourceType.Stone => "Камень",
            BotResourceType.Dirt => "Земля",
            _ => "Листва"
        };
    }
}
