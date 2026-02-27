using System.Numerics;
using AIG.Game.Config;
using AIG.Game.World;

namespace AIG.Game.Player;

public sealed class PlayerController
{
    private const float HalfWidth = 0.3f;
    private const float Height = 1.8f;
    private const float JumpBufferSeconds = 0.14f;
    private const float CoyoteSeconds = 0.10f;
    private const float MaxPhysicsStep = 1f / 120f;
    private const int MaxSubsteps = 8;

    private readonly GameConfig _config;
    private float _verticalVelocity;
    private float _jumpBufferTimer;
    private float _coyoteTimer;

    public PlayerController(GameConfig config, Vector3 startPosition)
    {
        _config = config;
        Position = startPosition;
        Yaw = MathF.PI;
    }

    public Vector3 Position { get; private set; }
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public bool IsGrounded { get; private set; }
    public float ColliderHalfWidth => HalfWidth;
    public float ColliderHeight => Height;

    public Vector3 EyePosition => Position + new Vector3(0f, Height * 0.92f, 0f);

    public Vector3 LookDirection
    {
        get
        {
            var x = MathF.Sin(Yaw) * MathF.Cos(Pitch);
            var y = MathF.Sin(Pitch);
            var z = MathF.Cos(Yaw) * MathF.Cos(Pitch);
            return Vector3.Normalize(new Vector3(x, y, z));
        }
    }

    internal void SetPose(Vector3 position, Vector3 lookDirection)
    {
        Position = position;
        _verticalVelocity = 0f;
        _jumpBufferTimer = 0f;
        _coyoteTimer = 0f;
        IsGrounded = false;

        if (lookDirection.LengthSquared() <= 0.000001f)
        {
            return;
        }

        var dir = Vector3.Normalize(lookDirection);
        var limit = 1.54f;
        Pitch = Math.Clamp(MathF.Asin(Math.Clamp(dir.Y, -1f, 1f)), -limit, limit);

        var horizontalLenSq = dir.X * dir.X + dir.Z * dir.Z;
        if (horizontalLenSq > 0.000001f)
        {
            Yaw = MathF.Atan2(dir.X, dir.Z);
        }
    }

    public void Update(WorldMap world, PlayerInput input, float deltaTime)
    {
        ApplyLook(input);

        if (input.Jump)
        {
            _jumpBufferTimer = JumpBufferSeconds;
        }
        else
        {
            _jumpBufferTimer = MathF.Max(0f, _jumpBufferTimer - deltaTime);
        }

        var clampedDelta = Math.Clamp(deltaTime, 0f, 0.2f);
        var substeps = Math.Clamp((int)MathF.Ceiling(clampedDelta / MaxPhysicsStep), 1, MaxSubsteps);
        var stepDelta = clampedDelta / substeps;

        for (var i = 0; i < substeps; i++)
        {
            ApplyHorizontalMovement(world, input, stepDelta);
            ApplyVerticalMovement(world, stepDelta);
        }
    }

    private void ApplyLook(PlayerInput input)
    {
        Yaw -= input.LookDeltaX * _config.MouseSensitivity;
        Pitch -= input.LookDeltaY * _config.MouseSensitivity;

        var limit = 1.54f;
        Pitch = Math.Clamp(Pitch, -limit, limit);
    }

    private void ApplyHorizontalMovement(WorldMap world, PlayerInput input, float deltaTime)
    {
        var forward = new Vector3(MathF.Sin(Yaw), 0f, MathF.Cos(Yaw));
        var right = new Vector3(-forward.Z, 0f, forward.X);

        var move = forward * input.MoveForward + right * input.MoveRight;
        if (move.LengthSquared() > 1f)
        {
            move = Vector3.Normalize(move);
        }

        var displacement = move * (_config.MoveSpeed * deltaTime);
        var next = Position;

        var xMove = new Vector3(displacement.X, 0f, 0f);
        if (!CollidesAt(world, next + xMove))
        {
            next += xMove;
        }

        var zMove = new Vector3(0f, 0f, displacement.Z);
        if (!CollidesAt(world, next + zMove))
        {
            next += zMove;
        }

        Position = next;
    }

    private void ApplyVerticalMovement(WorldMap world, float deltaTime)
    {
        IsGrounded = IsStandingOnSolid(world);
        if (IsGrounded)
        {
            _coyoteTimer = CoyoteSeconds;
            if (_verticalVelocity < 0f)
            {
                _verticalVelocity = 0f;
            }
        }
        else
        {
            _coyoteTimer = MathF.Max(0f, _coyoteTimer - deltaTime);
        }

        if (_jumpBufferTimer > 0f && _coyoteTimer > 0f)
        {
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            _verticalVelocity = _config.JumpSpeed;
            IsGrounded = false;
        }

        _verticalVelocity -= _config.Gravity * deltaTime;
        var candidate = Position + new Vector3(0f, _verticalVelocity * deltaTime, 0f);

        if (!CollidesAt(world, candidate))
        {
            Position = candidate;
            return;
        }

        if (_verticalVelocity < 0f)
        {
            while (!CollidesAt(world, Position + new Vector3(0f, -0.01f, 0f)))
            {
                Position += new Vector3(0f, -0.01f, 0f);
            }

            IsGrounded = true;
        }

        _verticalVelocity = 0f;
    }

    private bool IsStandingOnSolid(WorldMap world)
    {
        var probeY = Position.Y - 0.05f;
        var minX = Position.X - HalfWidth + 0.03f;
        var maxX = Position.X + HalfWidth - 0.03f;
        var minZ = Position.Z - HalfWidth + 0.03f;
        var maxZ = Position.Z + HalfWidth - 0.03f;

        return world.IsSolidAt(new Vector3(minX, probeY, minZ))
            || world.IsSolidAt(new Vector3(minX, probeY, maxZ))
            || world.IsSolidAt(new Vector3(maxX, probeY, minZ))
            || world.IsSolidAt(new Vector3(maxX, probeY, maxZ));
    }

    private static bool CollidesAt(WorldMap world, Vector3 position)
    {
        var min = new Vector3(position.X - HalfWidth, position.Y, position.Z - HalfWidth);
        var max = new Vector3(position.X + HalfWidth, position.Y + Height, position.Z + HalfWidth);

        var minX = (int)MathF.Floor(min.X);
        var maxX = (int)MathF.Floor(max.X);
        var minY = (int)MathF.Floor(min.Y);
        var maxY = (int)MathF.Floor(max.Y);
        var minZ = (int)MathF.Floor(min.Z);
        var maxZ = (int)MathF.Floor(max.Z);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    if (world.IsSolid(x, y, z))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
