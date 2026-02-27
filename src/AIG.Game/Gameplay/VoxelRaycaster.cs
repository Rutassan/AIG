using System.Numerics;
using AIG.Game.World;

namespace AIG.Game.Gameplay;

public static class VoxelRaycaster
{
    public static BlockRaycastHit? Raycast(WorldMap world, Vector3 origin, Vector3 direction, float maxDistance)
    {
        if (direction.LengthSquared() <= 0.0001f)
        {
            return null;
        }

        direction = Vector3.Normalize(direction);

        var currentX = (int)MathF.Floor(origin.X);
        var currentY = (int)MathF.Floor(origin.Y);
        var currentZ = (int)MathF.Floor(origin.Z);

        var stepX = direction.X >= 0f ? 1 : -1;
        var stepY = direction.Y >= 0f ? 1 : -1;
        var stepZ = direction.Z >= 0f ? 1 : -1;

        var tDeltaX = direction.X == 0f ? float.PositiveInfinity : MathF.Abs(1f / direction.X);
        var tDeltaY = direction.Y == 0f ? float.PositiveInfinity : MathF.Abs(1f / direction.Y);
        var tDeltaZ = direction.Z == 0f ? float.PositiveInfinity : MathF.Abs(1f / direction.Z);

        var nextVoxelX = direction.X >= 0f ? currentX + 1 : currentX;
        var nextVoxelY = direction.Y >= 0f ? currentY + 1 : currentY;
        var nextVoxelZ = direction.Z >= 0f ? currentZ + 1 : currentZ;

        var tMaxX = direction.X == 0f ? float.PositiveInfinity : (nextVoxelX - origin.X) / direction.X;
        var tMaxY = direction.Y == 0f ? float.PositiveInfinity : (nextVoxelY - origin.Y) / direction.Y;
        var tMaxZ = direction.Z == 0f ? float.PositiveInfinity : (nextVoxelZ - origin.Z) / direction.Z;

        var prevX = currentX;
        var prevY = currentY;
        var prevZ = currentZ;

        var traveled = 0f;
        while (traveled <= maxDistance)
        {
            if (world.IsSolid(currentX, currentY, currentZ))
            {
                return new BlockRaycastHit(currentX, currentY, currentZ, prevX, prevY, prevZ);
            }

            prevX = currentX;
            prevY = currentY;
            prevZ = currentZ;

            if (tMaxX < tMaxY)
            {
                if (tMaxX < tMaxZ)
                {
                    currentX += stepX;
                    traveled = tMaxX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    currentZ += stepZ;
                    traveled = tMaxZ;
                    tMaxZ += tDeltaZ;
                }
            }
            else
            {
                if (tMaxY < tMaxZ)
                {
                    currentY += stepY;
                    traveled = tMaxY;
                    tMaxY += tDeltaY;
                }
                else
                {
                    currentZ += stepZ;
                    traveled = tMaxZ;
                    tMaxZ += tDeltaZ;
                }
            }
        }

        return null;
    }
}
