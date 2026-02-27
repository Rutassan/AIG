using System.Numerics;
using AIG.Game.Player;
using AIG.Game.World;
using Raylib_cs;

namespace AIG.Game.Core;

internal static class CameraViewBuilder
{
    private const float ThirdPersonDistance = 3.9f;
    private const float ThirdPersonHeight = 1.0f;
    private const float CollisionProbeStep = 0.16f;
    private const float CollisionPullback = 0.12f;

    internal readonly record struct CameraView(Camera3D Camera, Vector3 RayOrigin, Vector3 RayDirection);

    public static CameraView Build(PlayerController player, WorldMap world, CameraMode mode, float cameraBobOffset)
    {
        return mode switch
        {
            CameraMode.ThirdPerson => BuildThirdPerson(player, world),
            _ => BuildFirstPerson(player, cameraBobOffset)
        };
    }

    public static CameraMode Toggle(CameraMode current)
    {
        return current == CameraMode.FirstPerson ? CameraMode.ThirdPerson : CameraMode.FirstPerson;
    }

    private static CameraView BuildFirstPerson(PlayerController player, float cameraBobOffset)
    {
        var position = player.EyePosition + new Vector3(0f, cameraBobOffset * 0.08f, 0f);
        var forward = player.LookDirection;
        var camera = new Camera3D
        {
            Position = position,
            Target = position + forward,
            Up = Vector3.UnitY,
            FovY = 75f,
            Projection = CameraProjection.Perspective
        };

        return new CameraView(camera, position, forward);
    }

    private static CameraView BuildThirdPerson(PlayerController player, WorldMap world)
    {
        var forward = player.LookDirection;
        var anchor = player.Position + new Vector3(0f, 1.35f, 0f);

        var retreat = Vector3.Normalize(new Vector3(forward.X, forward.Y * 0.35f, forward.Z));
        var desired = anchor - retreat * ThirdPersonDistance + Vector3.UnitY * ThirdPersonHeight;
        var position = ResolveCollision(world, anchor, desired);

        var camera = new Camera3D
        {
            Position = position,
            Target = anchor + forward * 2.2f,
            Up = Vector3.UnitY,
            FovY = 75f,
            Projection = CameraProjection.Perspective
        };

        var rayDirection = Vector3.Normalize(camera.Target - camera.Position);
        return new CameraView(camera, camera.Position, rayDirection);
    }

    internal static Vector3 ResolveCollision(WorldMap world, Vector3 anchor, Vector3 desired)
    {
        var delta = desired - anchor;
        var distance = delta.Length();
        if (distance <= 0.0001f)
        {
            return desired;
        }

        var direction = delta / distance;
        var steps = Math.Max(1, (int)MathF.Ceiling(distance / CollisionProbeStep));
        var previous = anchor;

        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            var sample = anchor + delta * t;
            if (world.IsSolidAt(sample))
            {
                var pulled = previous - direction * CollisionPullback;
                var guard = anchor - direction * 0.25f;
                return Vector3.Lerp(pulled, guard, 0.4f);
            }

            previous = sample;
        }

        return desired;
    }
}
