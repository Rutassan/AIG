namespace AIG.Game.Player;

public readonly record struct PlayerInput(
    float MoveForward,
    float MoveRight,
    bool Jump,
    float LookDeltaX,
    float LookDeltaY
);
