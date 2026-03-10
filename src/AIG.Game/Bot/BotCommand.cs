namespace AIG.Game.Bot;

internal enum BotCommandKind
{
    GatherResource = 0,
    BuildHouse = 1
}

internal enum HouseTemplateKind
{
    CabinS = 0
}

internal readonly record struct BotCommand(
    BotCommandKind Kind,
    BotResourceType Resource,
    int Amount,
    HouseBlueprint? Blueprint)
{
    internal static BotCommand Gather(BotResourceType resource, int amount)
    {
        return new BotCommand(BotCommandKind.GatherResource, resource, Math.Max(1, amount), null);
    }

    internal static BotCommand BuildHouse(HouseBlueprint blueprint)
    {
        return new BotCommand(BotCommandKind.BuildHouse, BotResourceType.Wood, 0, blueprint);
    }
}
