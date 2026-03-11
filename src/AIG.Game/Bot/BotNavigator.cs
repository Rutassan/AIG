using System.Numerics;
using AIG.Game.World;

namespace AIG.Game.Bot;

internal readonly record struct BotNavigationSettings(float ColliderHalfWidth, float ColliderHeight, float ReachDistance);

internal readonly record struct BotNavigationCell(int X, int FeetY, int Z)
{
    internal Vector3 ToPose()
    {
        return new Vector3(X + 0.5f, FeetY + 0.02f, Z + 0.5f);
    }
}

internal static class BotNavigator
{
    private const int MaxVisitedNodes = 4096;
    private static readonly (int X, int Z)[] NeighborDirections =
    [
        (1, 0),
        (-1, 0),
        (0, 1),
        (0, -1)
    ];

    internal static bool TryBuildStandRoute(
        WorldMap world,
        BotNavigationSettings settings,
        Vector3 startPose,
        Vector3 desiredPose,
        int goalRadius,
        Func<BotNavigationCell, bool>? canVisitCell,
        out Vector3[] waypoints)
    {
        var desiredFeetCell = ClampFeetCell(world, (int)MathF.Floor(desiredPose.Y + 0.1f));
        var margin = Math.Max(6, goalRadius + 4);
        var bounds = CreateSearchBounds(world, startPose, desiredPose, margin);

        return TryBuildRouteCore(
            world,
            settings,
            startPose,
            desiredPose,
            bounds,
            cell => IsStandRouteGoal(cell, desiredPose, desiredFeetCell, goalRadius),
            cell => GetStandTraversalPenalty(world, cell, desiredFeetCell),
            canVisitCell,
            out _,
            out waypoints);
    }

    internal static bool TryBuildStandRoute(
        WorldMap world,
        BotNavigationSettings settings,
        Vector3 startPose,
        Vector3 desiredPose,
        int goalRadius,
        out Vector3[] waypoints)
    {
        return TryBuildStandRoute(world, settings, startPose, desiredPose, goalRadius, canVisitCell: null, out waypoints);
    }

    internal static bool TryBuildActionRoute(
        WorldMap world,
        BotNavigationSettings settings,
        Vector3 startPose,
        int targetX,
        int targetY,
        int targetZ,
        int searchRadius,
        HouseBlueprint? blueprint,
        out Vector3[] waypoints,
        out Vector3 destinationPose)
    {
        var targetCenter = new Vector3(targetX + 0.5f, targetY + 0.5f, targetZ + 0.5f);
        var margin = Math.Max(12, searchRadius + 8);
        var bounds = CreateSearchBounds(world, startPose, targetCenter, margin + searchRadius);

        if (!TryBuildRouteCore(
                world,
                settings,
                startPose,
                targetCenter,
                bounds,
                cell => CanActFromCell(settings, cell, targetX, targetY, targetZ)
                    && !DoesPoseOverlapBlock(settings, cell.ToPose(), targetX, targetY, targetZ)
                    && !IsSupportingPoseBlock(settings, cell.ToPose(), targetX, targetY, targetZ),
                cell => GetActionTraversalPenalty(world, blueprint, targetY, cell),
                canVisitCell: null,
                out var goalCell,
                out waypoints))
        {
            destinationPose = startPose;
            return false;
        }

        destinationPose = goalCell.ToPose();
        if (waypoints.Length == 0 && !CanActFromPose(settings, startPose, targetX, targetY, targetZ))
        {
            waypoints = [destinationPose];
        }

        return true;
    }

    internal static bool TryFindNearestStandCell(
        WorldMap world,
        BotNavigationSettings settings,
        Vector3 pose,
        int searchRadius,
        out BotNavigationCell cell)
    {
        var baseX = Math.Clamp((int)MathF.Floor(pose.X), 0, Math.Max(0, world.Width - 1));
        var baseZ = Math.Clamp((int)MathF.Floor(pose.Z), 0, Math.Max(0, world.Depth - 1));
        var preferredFeetCell = ClampFeetCell(world, (int)MathF.Floor(pose.Y + 0.1f));
        var bestDistance = float.MaxValue;
        cell = default;

        for (var ring = 0; ring <= Math.Max(0, searchRadius); ring++)
        {
            for (var dx = -ring; dx <= ring; dx++)
            {
                for (var dz = -ring; dz <= ring; dz++)
                {
                    if (ring > 0 && Math.Abs(dx) != ring && Math.Abs(dz) != ring)
                    {
                        continue;
                    }

                    var x = baseX + dx;
                    var z = baseZ + dz;
                    if (!IsInsideXZ(world, x, z))
                    {
                        continue;
                    }

                    foreach (var feetCell in EnumerateFeetCandidates(world, preferredFeetCell, maxStepUp: 1, maxDrop: 3))
                    {
                        var candidate = new BotNavigationCell(x, feetCell, z);
                        if (!CanStandAtCell(world, settings, candidate))
                        {
                            continue;
                        }

                        var distance = Vector3.DistanceSquared(candidate.ToPose(), pose);
                        if (distance >= bestDistance)
                        {
                            continue;
                        }

                        bestDistance = distance;
                        cell = candidate;
                    }
                }
            }

            if (bestDistance < float.MaxValue)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryBuildRouteCore(
        WorldMap world,
        BotNavigationSettings settings,
        Vector3 startPose,
        Vector3 heuristicTarget,
        (int MinX, int MaxX, int MinZ, int MaxZ) bounds,
        Func<BotNavigationCell, bool> isGoal,
        Func<BotNavigationCell, float> traversalPenalty,
        Func<BotNavigationCell, bool>? canVisitCell,
        out BotNavigationCell goalCell,
        out Vector3[] waypoints)
    {
        goalCell = default;
        waypoints = Array.Empty<Vector3>();

        if (!TryFindNearestStandCell(world, settings, startPose, searchRadius: 2, out var startCell))
        {
            return false;
        }

        if (isGoal(startCell))
        {
            goalCell = startCell;
            waypoints = IsCloseToPose(startPose, startCell.ToPose(), 0.1f)
                ? Array.Empty<Vector3>()
                : [startCell.ToPose()];
            return true;
        }

        var open = new PriorityQueue<BotNavigationCell, float>();
        var cameFrom = new Dictionary<BotNavigationCell, BotNavigationCell>();
        var gScore = new Dictionary<BotNavigationCell, float> { [startCell] = 0f };
        var closed = new HashSet<BotNavigationCell>();
        open.Enqueue(startCell, GetHeuristic(startCell, heuristicTarget));
        var visited = 0;

        while (open.TryDequeue(out var current, out _))
        {
            if (!closed.Add(current))
            {
                continue;
            }

            visited++;
            if (visited > MaxVisitedNodes)
            {
                return false;
            }

            if (isGoal(current))
            {
                goalCell = current;
                waypoints = ReconstructWaypoints(cameFrom, startCell, current);
                return true;
            }

            foreach (var neighbor in EnumerateNeighbors(world, settings, current, bounds))
            {
                if (closed.Contains(neighbor))
                {
                    continue;
                }

                if (canVisitCell is not null && !canVisitCell(neighbor))
                {
                    continue;
                }

                var stepCost = 1f
                    + MathF.Abs(neighbor.FeetY - current.FeetY) * 1.2f
                    + traversalPenalty(neighbor);
                var tentative = gScore[current] + stepCost;
                if (gScore.TryGetValue(neighbor, out var currentScore) && tentative >= currentScore)
                {
                    continue;
                }

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentative;
                open.Enqueue(neighbor, tentative + GetHeuristic(neighbor, heuristicTarget));
            }
        }

        return false;
    }

    private static IEnumerable<BotNavigationCell> EnumerateNeighbors(
        WorldMap world,
        BotNavigationSettings settings,
        BotNavigationCell current,
        (int MinX, int MaxX, int MinZ, int MaxZ) bounds)
    {
        for (var i = 0; i < NeighborDirections.Length; i++)
        {
            var nextX = current.X + NeighborDirections[i].X;
            var nextZ = current.Z + NeighborDirections[i].Z;
            if (nextX < bounds.MinX || nextX > bounds.MaxX || nextZ < bounds.MinZ || nextZ > bounds.MaxZ)
            {
                continue;
            }

            foreach (var feetCell in EnumerateFeetCandidates(world, current.FeetY, maxStepUp: 1, maxDrop: 3))
            {
                var neighbor = new BotNavigationCell(nextX, feetCell, nextZ);
                if (CanStandAtCell(world, settings, neighbor))
                {
                    yield return neighbor;
                }
            }
        }
    }

    private static IEnumerable<int> EnumerateFeetCandidates(WorldMap world, int preferredFeetCell, int maxStepUp, int maxDrop)
    {
        yield return ClampFeetCell(world, preferredFeetCell);

        for (var stepUp = 1; stepUp <= Math.Max(0, maxStepUp); stepUp++)
        {
            yield return ClampFeetCell(world, preferredFeetCell + stepUp);
        }

        for (var drop = 1; drop <= Math.Max(0, maxDrop); drop++)
        {
            yield return ClampFeetCell(world, preferredFeetCell - drop);
        }
    }

    private static float GetStandTraversalPenalty(WorldMap world, BotNavigationCell cell, int desiredFeetCell)
    {
        var score = MathF.Abs(cell.FeetY - desiredFeetCell) * 0.8f;
        score += GetColumnPenalty(world, cell.X, cell.FeetY, cell.Z);
        score += GetConfinementPenalty(world, cell.X, cell.FeetY, cell.Z);
        return score;
    }

    private static float GetActionTraversalPenalty(WorldMap world, HouseBlueprint? blueprint, int targetY, BotNavigationCell cell)
    {
        var score = GetColumnPenalty(world, cell.X, cell.FeetY, cell.Z);
        score += GetConfinementPenalty(world, cell.X, cell.FeetY, cell.Z);
        if (blueprint is null)
        {
            return score;
        }

        var buildFeetCell = ClampFeetCell(world, blueprint.FloorY + 1);
        score += MathF.Abs(cell.FeetY - buildFeetCell) * 1.5f;
        if (cell.FeetY < buildFeetCell)
        {
            score += (buildFeetCell - cell.FeetY) * 4f;
        }

        if (blueprint.IsInsideInterior(cell.X, cell.Z))
        {
            score += 12f;
        }
        else if (blueprint.IsInsideFootprint(cell.X, cell.Z))
        {
            score += 5f;
        }

        return score;
    }

    private static float GetColumnPenalty(WorldMap world, int x, int feetCell, int z)
    {
        var topFeetCell = ClampFeetCell(world, world.GetTopSolidY(x, z) + 1);
        return MathF.Max(0f, topFeetCell - feetCell) * 2.5f;
    }

    private static float GetConfinementPenalty(WorldMap world, int x, int feetCell, int z)
    {
        var score = 0f;
        for (var i = 0; i < NeighborDirections.Length; i++)
        {
            var sideX = x + NeighborDirections[i].X;
            var sideZ = z + NeighborDirections[i].Z;
            var blockedAtFeet = world.IsSolid(sideX, feetCell, sideZ);
            var blockedAtBody = world.IsSolid(sideX, feetCell + 1, sideZ);
            if (blockedAtFeet)
            {
                score += 1.5f;
            }

            if (blockedAtFeet && blockedAtBody)
            {
                score += 3f;
            }
        }

        return score;
    }

    private static bool CanStandAtCell(WorldMap world, BotNavigationSettings settings, BotNavigationCell cell)
    {
        return IsPoseClear(world, settings, cell.ToPose());
    }

    private static bool IsStandRouteGoal(BotNavigationCell cell, Vector3 desiredPose, int desiredFeetCell, int goalRadius)
    {
        var pose = cell.ToPose();
        var horizontal = new Vector2(pose.X - desiredPose.X, pose.Z - desiredPose.Z).LengthSquared();
        return horizontal <= goalRadius * goalRadius
            && Math.Abs(cell.FeetY - desiredFeetCell) <= 2;
    }

    private static bool CanActFromCell(
        BotNavigationSettings settings,
        BotNavigationCell cell,
        int targetX,
        int targetY,
        int targetZ)
    {
        return CanActFromPose(settings, cell.ToPose(), targetX, targetY, targetZ);
    }

    private static bool CanActFromPose(
        BotNavigationSettings settings,
        Vector3 pose,
        int targetX,
        int targetY,
        int targetZ)
    {
        var eye = pose + new Vector3(0f, settings.ColliderHeight * 0.92f, 0f);
        var center = new Vector3(targetX + 0.5f, targetY + 0.5f, targetZ + 0.5f);
        return Vector3.DistanceSquared(eye, center) <= settings.ReachDistance * settings.ReachDistance;
    }

    internal static bool IsSupportingPoseBlock(BotNavigationSettings settings, Vector3 pose, int x, int y, int z)
    {
        var supportY = (int)MathF.Floor(pose.Y - 0.05f);
        if (y != supportY)
        {
            return false;
        }

        var minX = (int)MathF.Floor(pose.X - settings.ColliderHalfWidth + 0.03f);
        var maxX = (int)MathF.Floor(pose.X + settings.ColliderHalfWidth - 0.03f);
        var minZ = (int)MathF.Floor(pose.Z - settings.ColliderHalfWidth + 0.03f);
        var maxZ = (int)MathF.Floor(pose.Z + settings.ColliderHalfWidth - 0.03f);
        return x >= minX && x <= maxX && z >= minZ && z <= maxZ;
    }

    private static bool DoesPoseOverlapBlock(BotNavigationSettings settings, Vector3 pose, int x, int y, int z)
    {
        var minX = pose.X - settings.ColliderHalfWidth + 0.03f;
        var maxX = pose.X + settings.ColliderHalfWidth - 0.03f;
        var minY = pose.Y + 0.02f;
        var maxY = pose.Y + settings.ColliderHeight - 0.03f;
        var minZ = pose.Z - settings.ColliderHalfWidth + 0.03f;
        var maxZ = pose.Z + settings.ColliderHalfWidth - 0.03f;
        return maxX > x
            && minX < x + 1f
            && maxY > y
            && minY < y + 1f
            && maxZ > z
            && minZ < z + 1f;
    }

    private static bool IsPoseClear(WorldMap world, BotNavigationSettings settings, Vector3 pose)
    {
        if (pose.X - settings.ColliderHalfWidth < 0f
            || pose.Z - settings.ColliderHalfWidth < 0f
            || pose.X + settings.ColliderHalfWidth >= world.Width
            || pose.Z + settings.ColliderHalfWidth >= world.Depth
            || pose.Y < 0f
            || pose.Y + settings.ColliderHeight >= world.Height)
        {
            return false;
        }

        var minX = (int)MathF.Floor(pose.X - settings.ColliderHalfWidth);
        var maxX = (int)MathF.Floor(pose.X + settings.ColliderHalfWidth);
        var minY = (int)MathF.Floor(pose.Y);
        var maxY = (int)MathF.Floor(pose.Y + settings.ColliderHeight);
        var minZ = (int)MathF.Floor(pose.Z - settings.ColliderHalfWidth);
        var maxZ = (int)MathF.Floor(pose.Z + settings.ColliderHalfWidth);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    if (world.IsSolid(x, y, z))
                    {
                        return false;
                    }
                }
            }
        }

        return world.IsSolidAt(new Vector3(pose.X, pose.Y - 0.08f, pose.Z));
    }

    private static Vector3[] ReconstructWaypoints(
        Dictionary<BotNavigationCell, BotNavigationCell> cameFrom,
        BotNavigationCell start,
        BotNavigationCell goal)
    {
        if (goal == start)
        {
            return Array.Empty<Vector3>();
        }

        var cells = new List<BotNavigationCell> { goal };
        var current = goal;
        while (cameFrom.TryGetValue(current, out var previous))
        {
            current = previous;
            if (current == start)
            {
                break;
            }

            cells.Add(current);
        }

        cells.Reverse();
        return [.. cells.Select(cell => cell.ToPose())];
    }

    private static float GetHeuristic(BotNavigationCell cell, Vector3 heuristicTarget)
    {
        var dx = MathF.Abs(cell.X + 0.5f - heuristicTarget.X);
        var dz = MathF.Abs(cell.Z + 0.5f - heuristicTarget.Z);
        var dy = MathF.Abs(cell.FeetY + 0.02f - heuristicTarget.Y);
        return dx + dz + dy * 1.25f;
    }

    private static bool IsCloseToPose(Vector3 currentPose, Vector3 targetPose, float arrivalDistance)
    {
        var delta = targetPose - currentPose;
        var horizontalDistance = new Vector2(delta.X, delta.Z).Length();
        return horizontalDistance <= arrivalDistance && MathF.Abs(delta.Y) <= 0.95f;
    }

    private static (int MinX, int MaxX, int MinZ, int MaxZ) CreateSearchBounds(WorldMap world, Vector3 from, Vector3 to, int margin)
    {
        var minX = Math.Clamp((int)MathF.Floor(MathF.Min(from.X, to.X)) - margin, 0, Math.Max(0, world.Width - 1));
        var maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(from.X, to.X)) + margin, 0, Math.Max(0, world.Width - 1));
        var minZ = Math.Clamp((int)MathF.Floor(MathF.Min(from.Z, to.Z)) - margin, 0, Math.Max(0, world.Depth - 1));
        var maxZ = Math.Clamp((int)MathF.Ceiling(MathF.Max(from.Z, to.Z)) + margin, 0, Math.Max(0, world.Depth - 1));
        return (minX, maxX, minZ, maxZ);
    }

    private static int ClampFeetCell(WorldMap world, int feetCell)
    {
        return Math.Clamp(feetCell, 1, Math.Max(1, world.Height - 2));
    }

    private static bool IsInsideXZ(WorldMap world, int x, int z)
    {
        return x >= 0 && x < world.Width && z >= 0 && z < world.Depth;
    }
}
