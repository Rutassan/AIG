namespace AIG.Game.Gameplay;

public readonly record struct BlockRaycastHit(
    int X,
    int Y,
    int Z,
    int PreviousX,
    int PreviousY,
    int PreviousZ
);
