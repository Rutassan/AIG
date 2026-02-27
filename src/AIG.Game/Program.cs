using AIG.Game.Core;

namespace AIG.Game;

public static class Program
{
    internal static Func<IGameRunner> GameFactory { get; set; } = static () => new GameApp();

    public static void Main(string[] args)
    {
        GameFactory().Run();
    }
}
