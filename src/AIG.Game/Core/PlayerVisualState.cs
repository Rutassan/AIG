using System.Numerics;

namespace AIG.Game.Core;

internal sealed class PlayerVisualState
{
    private bool _hasPrevious;
    private Vector3 _previousPosition;

    public float WalkPhase { get; private set; }
    public float WalkBlend { get; private set; }
    public float VerticalSpeed { get; private set; }

    public bool IsJumping => VerticalSpeed > 0.75f;
    public bool IsFalling => VerticalSpeed < -0.75f;

    public void Reset(Vector3 position)
    {
        _hasPrevious = true;
        _previousPosition = position;
        WalkPhase = 0f;
        WalkBlend = 0f;
        VerticalSpeed = 0f;
    }

    public void Update(Vector3 position, float deltaTime, float moveSpeed)
    {
        if (!_hasPrevious)
        {
            Reset(position);
            return;
        }

        var safeDelta = MathF.Max(0.0001f, deltaTime);
        var dx = position.X - _previousPosition.X;
        var dz = position.Z - _previousPosition.Z;
        var horizontalSpeed = MathF.Sqrt(dx * dx + dz * dz) / safeDelta;
        var normalized = moveSpeed <= 0f ? 0f : Math.Clamp(horizontalSpeed / moveSpeed, 0f, 1.5f);

        var targetBlend = Math.Clamp(normalized, 0f, 1f);
        var blendT = Math.Clamp(safeDelta * 14f, 0f, 1f);
        WalkBlend = WalkBlend + (targetBlend - WalkBlend) * blendT;
        WalkPhase += safeDelta * (4.5f + normalized * 7.5f);
        VerticalSpeed = (position.Y - _previousPosition.Y) / safeDelta;

        _previousPosition = position;
    }
}
