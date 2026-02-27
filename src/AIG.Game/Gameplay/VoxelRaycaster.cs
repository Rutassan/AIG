using System.Numerics;
using AIG.Game.World;

namespace AIG.Game.Gameplay;

public static class VoxelRaycaster
{
    private const float BoundaryEpsilon = 0.0001f;
    private const float TieEpsilon = 0.000001f;
    private const float AxisEpsilon = 0.0000001f;

    public static BlockRaycastHit? Raycast(WorldMap world, Vector3 origin, Vector3 direction, float maxDistance)
    {
        if (direction.LengthSquared() <= 0.0001f)
        {
            return null;
        }

        direction = Vector3.Normalize(direction);
        var end = origin + direction * maxDistance;

        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(origin.X, end.X)) - 1);
        var maxX = Math.Min(world.Width - 1, (int)MathF.Floor(MathF.Max(origin.X, end.X)) + 1);
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(origin.Y, end.Y)) - 1);
        var maxY = Math.Min(world.Height - 1, (int)MathF.Floor(MathF.Max(origin.Y, end.Y)) + 1);
        var minZ = Math.Max(0, (int)MathF.Floor(MathF.Min(origin.Z, end.Z)) - 1);
        var maxZ = Math.Min(world.Depth - 1, (int)MathF.Floor(MathF.Max(origin.Z, end.Z)) + 1);

        var bestT = float.PositiveInfinity;
        var candidates = new List<(int X, int Y, int Z)>(capacity: 4);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    if (!world.IsSolid(x, y, z))
                    {
                        continue;
                    }

                    if (!TryIntersectVoxel(origin, direction, maxDistance, x, y, z, out var hitT))
                    {
                        continue;
                    }

                    if (hitT < bestT - TieEpsilon)
                    {
                        bestT = hitT;
                        candidates.Clear();
                        candidates.Add((x, y, z));
                    }
                    else if (MathF.Abs(hitT - bestT) <= TieEpsilon)
                    {
                        candidates.Add((x, y, z));
                    }
                }
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var selected = ResolveCandidate(candidates, origin, direction, bestT, maxDistance);

        var previous = selected;
        if (bestT > BoundaryEpsilon)
        {
            var pointBefore = origin + direction * MathF.Max(0f, bestT - BoundaryEpsilon);
            previous = (
                (int)MathF.Floor(pointBefore.X),
                (int)MathF.Floor(pointBefore.Y),
                (int)MathF.Floor(pointBefore.Z));
        }

        if (!world.IsInside(previous.Item1, previous.Item2, previous.Item3))
        {
            previous = selected;
        }

        return new BlockRaycastHit(
            selected.X,
            selected.Y,
            selected.Z,
            previous.Item1,
            previous.Item2,
            previous.Item3);
    }

    private static (int X, int Y, int Z) ResolveCandidate(
        IReadOnlyList<(int X, int Y, int Z)> candidates,
        Vector3 origin,
        Vector3 direction,
        float hitT,
        float maxDistance)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var probeT = MathF.Min(maxDistance, hitT + BoundaryEpsilon);
        if (probeT > hitT + TieEpsilon)
        {
            var pointAfter = origin + direction * probeT;
            var probe = (
                (int)MathF.Floor(pointAfter.X),
                (int)MathF.Floor(pointAfter.Y),
                (int)MathF.Floor(pointAfter.Z));

            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].X == probe.Item1
                    && candidates[i].Y == probe.Item2
                    && candidates[i].Z == probe.Item3)
                {
                    return candidates[i];
                }
            }
        }

        var hitPoint = origin + direction * hitT;
        var bestCandidate = candidates[0];
        var bestScore = float.NegativeInfinity;

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var center = new Vector3(candidate.X + 0.5f, candidate.Y + 0.5f, candidate.Z + 0.5f);
            var score = Vector3.Dot(center - hitPoint, direction);
            if (score > bestScore + TieEpsilon)
            {
                bestScore = score;
                bestCandidate = candidate;
            }
        }

        return bestCandidate;
    }

    private static bool TryIntersectVoxel(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        int x,
        int y,
        int z,
        out float entryT)
    {
        var boxMin = new Vector3(x, y, z);
        var boxMax = boxMin + Vector3.One;

        var tMin = 0f;
        var tMax = maxDistance;

        if (!ClipAxis(origin.X, direction.X, boxMin.X, boxMax.X, ref tMin, ref tMax)
            || !ClipAxis(origin.Y, direction.Y, boxMin.Y, boxMax.Y, ref tMin, ref tMax)
            || !ClipAxis(origin.Z, direction.Z, boxMin.Z, boxMax.Z, ref tMin, ref tMax))
        {
            entryT = 0f;
            return false;
        }

        entryT = MathF.Max(0f, tMin);
        return true;
    }

    private static bool ClipAxis(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(direction) <= AxisEpsilon)
        {
            return origin >= min && origin <= max;
        }

        var inv = 1f / direction;
        var t1 = (min - origin) * inv;
        var t2 = (max - origin) * inv;
        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
        }

        if (t1 > tMin)
        {
            tMin = t1;
        }

        if (t2 < tMax)
        {
            tMax = t2;
        }

        return tMax >= tMin;
    }
}
