namespace AIG.Game.Bot;

internal sealed class BotCommandSelection
{
    private static readonly BotResourceType[] ResourceCycle =
    [
        BotResourceType.Wood,
        BotResourceType.Stone,
        BotResourceType.Dirt,
        BotResourceType.Leaves
    ];

    private static readonly int[] AmountCycle = [16, 32, 64, 128];

    private int _resourceIndex;
    private int _amountIndex;

    public BotResourceType SelectedResource => ResourceCycle[_resourceIndex];
    public int SelectedAmount => AmountCycle[_amountIndex];

    public void CycleResource()
    {
        _resourceIndex = (_resourceIndex + 1) % ResourceCycle.Length;
    }

    public void CycleAmount()
    {
        _amountIndex = (_amountIndex + 1) % AmountCycle.Length;
    }
}
