namespace AIG.Game.Bot;

internal enum BotStatus
{
    Idle = 0,
    Moving = 1,
    Gathering = 2,
    Building = 3,
    NoPath = 4
}

internal static class BotStatusExtensions
{
    internal static string GetLabel(this BotStatus status)
    {
        return status switch
        {
            BotStatus.Idle => "Ждет команд",
            BotStatus.Moving => "Идет",
            BotStatus.Gathering => "Добывает",
            BotStatus.Building => "Строит",
            _ => "Нет пути"
        };
    }
}
