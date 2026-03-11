using System.Numerics;
using AIG.Game.World;

namespace AIG.Game.Bot;

internal readonly record struct HouseBuildStep(int X, int Y, int Z, BlockType Block)
{
    internal bool ConsumesResource => Block != BlockType.Air;
}

internal sealed class HouseBlueprint
{
    private const int FootprintSize = 7;
    private const int PadMargin = 2;
    private const int SearchRadius = 14;
    private const int WallHeight = 4;
    private const int RoofBaseOffset = 5;
    private const int RoofPeakOffset = 8;

    private readonly record struct BuildSite(int OriginX, int FloorY, int OriginZ);

    private readonly HouseBuildStep[] _steps;
    private readonly Dictionary<(int X, int Y, int Z), BlockType> _finalBlocks;
    private readonly Dictionary<BlockType, int> _requiredResources;

    internal HouseBlueprint(HouseTemplateKind template, string name, int originX, int floorY, int originZ, HouseBuildStep[] steps)
    {
        Template = template;
        Name = name;
        OriginX = originX;
        FloorY = floorY;
        OriginZ = originZ;
        _steps = steps;
        _finalBlocks = BuildFinalBlocks(steps);
        _requiredResources = CountRequiredResources(steps);
    }

    internal HouseTemplateKind Template { get; }
    internal string Name { get; }
    internal int OriginX { get; }
    internal int FloorY { get; }
    internal int OriginZ { get; }
    internal IReadOnlyList<HouseBuildStep> Steps => _steps;
    internal IReadOnlyDictionary<BlockType, int> RequiredResources => _requiredResources;
    internal Vector3 Center => new(OriginX + 3.5f, FloorY + 1.05f, OriginZ + 3.5f);

    internal bool IsPlannedSolidBlock(int x, int y, int z, BlockType block)
    {
        return block != BlockType.Air
            && _finalBlocks.TryGetValue((x, y, z), out var planned)
            && planned == block;
    }

    internal bool IsInsideFootprint(int x, int z)
    {
        return x >= OriginX
            && x <= OriginX + FootprintSize - 1
            && z >= OriginZ
            && z <= OriginZ + FootprintSize - 1;
    }

    internal bool IsInsideInterior(int x, int z)
    {
        return x > OriginX
            && x < OriginX + FootprintSize - 1
            && z > OriginZ
            && z < OriginZ + FootprintSize - 1;
    }

    internal bool IsInsideGatherKeepout(int x, int z)
    {
        const int gatherMargin = PadMargin + 1;
        var minX = Math.Max(0, OriginX - gatherMargin);
        var minZ = Math.Max(0, OriginZ - gatherMargin);
        var maxX = OriginX + FootprintSize - 1 + gatherMargin;
        var maxZ = OriginZ + FootprintSize - 1 + gatherMargin;
        return x >= minX && x <= maxX && z >= minZ && z <= maxZ;
    }

    internal int CountRemaining(BlockType block, int fromIndex)
    {
        var remaining = 0;
        for (var i = Math.Clamp(fromIndex, 0, _steps.Length); i < _steps.Length; i++)
        {
            if (_steps[i].Block == block && !IsSupersededStep(i))
            {
                remaining++;
            }
        }

        return remaining;
    }

    internal bool IsSupersededStep(int stepIndex)
    {
        if ((uint)stepIndex >= (uint)_steps.Length)
        {
            return false;
        }

        var step = _steps[stepIndex];
        for (var i = stepIndex + 1; i < _steps.Length; i++)
        {
            var next = _steps[i];
            if (next.X == step.X && next.Y == step.Y && next.Z == step.Z)
            {
                return true;
            }
        }

        return false;
    }

    internal static HouseBlueprint CreateCabinS(WorldMap world, Vector3 playerPosition, Vector3 lookDirection)
    {
        var horizontalLook = new Vector3(lookDirection.X, 0f, lookDirection.Z);
        if (horizontalLook.LengthSquared() <= 0.00001f)
        {
            horizontalLook = new Vector3(0f, 0f, -1f);
        }

        horizontalLook = Vector3.Normalize(horizontalLook);
        var buildCenter = playerPosition + horizontalLook * 12f;
        var site = SelectBuildSite(world, buildCenter);
        var steps = BuildCabinSteps(world, site.OriginX, site.FloorY, site.OriginZ);
        return new HouseBlueprint(HouseTemplateKind.CabinS, "Дом S", site.OriginX, site.FloorY, site.OriginZ, steps);
    }

    private static int ClampOrigin(int desired, int size, int footprint)
    {
        if (size <= footprint)
        {
            return 0;
        }

        return Math.Clamp(desired, 0, size - footprint);
    }

    private static HouseBuildStep[] BuildCabinSteps(WorldMap world, int originX, int floorY, int originZ)
    {
        var steps = new List<HouseBuildStep>(512);
        var footprintMaxX = originX + FootprintSize - 1;
        var footprintMaxZ = originZ + FootprintSize - 1;
        var padMinX = Math.Max(0, originX - PadMargin);
        var padMaxX = Math.Min(world.Width - 1, footprintMaxX + PadMargin);
        var padMinZ = Math.Max(0, originZ - PadMargin);
        var padMaxZ = Math.Min(world.Depth - 1, footprintMaxZ + PadMargin);
        var clearTopY = Math.Min(world.Height - 1, floorY + RoofPeakOffset + 2);
        var centerX = originX + 3;
        var wallTopY = floorY + WallHeight;
        var roofMinX = Math.Max(0, originX - 1);
        var roofMaxX = Math.Min(world.Width - 1, footprintMaxX + 1);
        var roofMinZ = Math.Max(0, originZ - 1);
        var roofMaxZ = Math.Min(world.Depth - 1, footprintMaxZ + 1);
        var chimneyX = footprintMaxX - 1;
        var chimneyZ = footprintMaxZ - 1;

        void AddStep(int x, int y, int z, BlockType block)
        {
            if (x < 0 || y < 0 || z < 0 || x >= world.Width || y >= world.Height || z >= world.Depth)
            {
                return;
            }

            steps.Add(new HouseBuildStep(x, y, z, block));
        }

        static bool IsFootprintColumn(int x, int z, int minX, int maxX, int minZ, int maxZ)
        {
            return x >= minX && x <= maxX && z >= minZ && z <= maxZ;
        }

        static bool IsCorner(int x, int z, int minX, int maxX, int minZ, int maxZ)
        {
            return (x == minX || x == maxX) && (z == minZ || z == maxZ);
        }

        static bool IsPorchDeck(int x, int z, int minX, int minZ)
        {
            return z == minZ - 1 && x >= minX + 2 && x <= minX + 4;
        }

        static bool IsPathColumn(int x, int z, int centerX, int minZ)
        {
            return x == centerX && z >= minZ - 2 && z < minZ - 1;
        }

        static bool IsDecorShrub(int x, int z, int padMinX, int padMaxX, int minZ, int maxZ)
        {
            return (x == padMinX + 1 || x == padMaxX - 1)
                && (z == minZ + 1 || z == maxZ - 1);
        }

        static bool IsWallOpening(int x, int y, int z, int minX, int floorY, int minZ, int maxX, int maxZ)
        {
            var isDoor = x == minX + 3 && z == minZ && y >= floorY + 1 && y <= floorY + 2;
            var isFrontWindowLeft = x == minX + 1 && z == minZ && y == floorY + 2;
            var isFrontWindowRight = x == maxX - 1 && z == minZ && y == floorY + 2;
            var isLeftWindow = x == minX && z == minZ + 2 && y == floorY + 2;
            var isRightWindow = x == maxX && z == minZ + 4 && y == floorY + 2;
            var isBackWindow = x == minX + 3 && z == maxZ && y == floorY + 2;
            return isDoor || isFrontWindowLeft || isFrontWindowRight || isLeftWindow || isRightWindow || isBackWindow;
        }

        bool IsFoundationPerimeter(int x, int z)
        {
            return IsFootprintColumn(x, z, originX, footprintMaxX, originZ, footprintMaxZ)
                && (x == originX || x == footprintMaxX || z == originZ || z == footprintMaxZ);
        }

        // 1. Готовим площадку, крыльцо и двор по колонкам: очищаем и сразу выравниваем каждую ячейку.
        for (var x = padMinX; x <= padMaxX; x++)
        {
            for (var z = padMinZ; z <= padMaxZ; z++)
            {
                var topSolid = world.GetTopSolidY(x, z);
                var groundTop = GetGroundTopY(world, x, z);
                var maxClearY = Math.Min(topSolid, clearTopY);
                for (var y = floorY; y <= maxClearY; y++)
                {
                    if (world.GetBlock(x, y, z) != BlockType.Air)
                    {
                        AddStep(x, y, z, BlockType.Air);
                    }
                }

                for (var y = groundTop + 1; y < floorY; y++)
                {
                    AddStep(x, y, z, BlockType.Dirt);
                }

                var surfaceBlock = BlockType.Dirt;
                if (IsFoundationPerimeter(x, z))
                {
                    surfaceBlock = BlockType.Stone;
                }
                else if (IsFootprintColumn(x, z, originX, footprintMaxX, originZ, footprintMaxZ))
                {
                    surfaceBlock = BlockType.Wood;
                }
                else if (IsPorchDeck(x, z, originX, originZ))
                {
                    surfaceBlock = BlockType.Wood;
                }
                else if (IsPathColumn(x, z, centerX, originZ) || (z == originZ - 1 && x == centerX))
                {
                    surfaceBlock = BlockType.Stone;
                }

                AddStep(x, floorY, z, surfaceBlock);
            }
        }

        // 2. Цоколь и стены без последующего "вырезания": проёмы просто не строим.
        for (var y = floorY + 1; y <= wallTopY; y++)
        {
            for (var x = originX; x <= footprintMaxX; x++)
            {
                for (var z = originZ; z <= footprintMaxZ; z++)
                {
                    var isPerimeter = x == originX || x == footprintMaxX || z == originZ || z == footprintMaxZ;
                    if (!isPerimeter || IsWallOpening(x, y, z, originX, floorY, originZ, footprintMaxX, footprintMaxZ))
                    {
                        continue;
                    }

                    var isCornerBeam = IsCorner(x, z, originX, footprintMaxX, originZ, footprintMaxZ);
                    var block = y == floorY + 1 && !isCornerBeam
                        ? BlockType.Stone
                        : BlockType.Wood;
                    AddStep(x, y, z, block);
                }
            }
        }

        // 3. Декоративные балки и крыльцо.
        for (var y = floorY + 1; y <= wallTopY; y++)
        {
            AddStep(originX + 2, y, originZ, BlockType.Wood);
            AddStep(originX + 4, y, originZ, BlockType.Wood);
        }

        for (var y = floorY + 1; y <= floorY + 3; y++)
        {
            AddStep(originX + 2, y, originZ - 1, BlockType.Wood);
            AddStep(originX + 4, y, originZ - 1, BlockType.Wood);
        }

        for (var x = originX + 2; x <= originX + 4; x++)
        {
            for (var z = originZ - 1; z <= originZ; z++)
            {
                AddStep(x, floorY + 4, z, BlockType.Wood);
            }
        }

        // 4. Фронтоны на передней и задней стене.
        for (var x = originX + 1; x < footprintMaxX; x++)
        {
            var roofY = floorY + RoofBaseOffset + (4 - Math.Abs(centerX - x));
            for (var y = wallTopY + 1; y < roofY; y++)
            {
                AddStep(x, y, originZ, BlockType.Wood);
                AddStep(x, y, footprintMaxZ, BlockType.Wood);
            }
        }

        // 5. Крыша с навесами.
        for (var x = roofMinX; x <= roofMaxX; x++)
        {
            var roofRise = Math.Max(0, 4 - Math.Abs(centerX - x));
            var roofY = floorY + RoofBaseOffset + roofRise;
            for (var z = roofMinZ; z <= roofMaxZ; z++)
            {
                if (x == chimneyX && z == chimneyZ && roofY >= floorY + RoofBaseOffset)
                {
                    continue;
                }

                AddStep(x, roofY, z, BlockType.Wood);
            }
        }

        // 6. Дымоход из камня.
        for (var y = floorY + 2; y <= Math.Min(world.Height - 1, floorY + RoofPeakOffset + 1); y++)
        {
            AddStep(chimneyX, y, chimneyZ, BlockType.Stone);
        }

        // 7. Декор вокруг дома: входная дорожка, стояки и кусты.
        AddStep(centerX - 1, floorY, originZ - 2, BlockType.Stone);
        AddStep(centerX + 1, floorY, originZ - 2, BlockType.Stone);
        AddStep(originX + 2, floorY + 1, padMinZ, BlockType.Wood);
        AddStep(originX + 4, floorY + 1, padMinZ, BlockType.Wood);

        for (var x = padMinX; x <= padMaxX; x++)
        {
            for (var z = padMinZ; z <= padMaxZ; z++)
            {
                if (!IsDecorShrub(x, z, padMinX, padMaxX, originZ, footprintMaxZ))
                {
                    continue;
                }

                AddStep(x, floorY + 1, z, BlockType.Leaves);
            }
        }

        return [.. steps];
    }

    private static Dictionary<BlockType, int> CountRequiredResources(IEnumerable<HouseBuildStep> steps)
    {
        var counts = new Dictionary<BlockType, int>();
        foreach (var step in steps)
        {
            if (!step.ConsumesResource)
            {
                continue;
            }

            counts[step.Block] = counts.TryGetValue(step.Block, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static Dictionary<(int X, int Y, int Z), BlockType> BuildFinalBlocks(IEnumerable<HouseBuildStep> steps)
    {
        var blocks = new Dictionary<(int X, int Y, int Z), BlockType>();
        foreach (var step in steps)
        {
            blocks[(step.X, step.Y, step.Z)] = step.Block;
        }

        return blocks;
    }

    private static BuildSite SelectBuildSite(WorldMap world, Vector3 desiredCenter)
    {
        var desiredOriginX = ClampOrigin((int)MathF.Floor(desiredCenter.X) - 3, world.Width, footprint: FootprintSize);
        var desiredOriginZ = ClampOrigin((int)MathF.Floor(desiredCenter.Z) - 3, world.Depth, footprint: FootprintSize);
        var minOriginX = Math.Max(0, desiredOriginX - SearchRadius);
        var maxOriginX = Math.Min(Math.Max(0, world.Width - FootprintSize), desiredOriginX + SearchRadius);
        var minOriginZ = Math.Max(0, desiredOriginZ - SearchRadius);
        var maxOriginZ = Math.Min(Math.Max(0, world.Depth - FootprintSize), desiredOriginZ + SearchRadius);

        var bestSite = new BuildSite(desiredOriginX, Math.Clamp(GetGroundTopY(world, desiredOriginX + 3, desiredOriginZ + 3), 1, Math.Max(1, world.Height - RoofPeakOffset - 2)), desiredOriginZ);
        var bestScore = float.MaxValue;

        for (var originX = minOriginX; originX <= maxOriginX; originX++)
        {
            for (var originZ = minOriginZ; originZ <= maxOriginZ; originZ++)
            {
                if (!TryEvaluateBuildSite(world, originX, originZ, desiredCenter, out var floorY, out var score))
                {
                    continue;
                }

                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestSite = new BuildSite(originX, floorY, originZ);
            }
        }

        return bestSite;
    }

    private static bool TryEvaluateBuildSite(WorldMap world, int originX, int originZ, Vector3 desiredCenter, out int floorY, out float score)
    {
        floorY = 2;
        score = float.MaxValue;

        var footprintMaxX = originX + FootprintSize - 1;
        var footprintMaxZ = originZ + FootprintSize - 1;
        if (footprintMaxX >= world.Width || footprintMaxZ >= world.Depth)
        {
            return false;
        }

        var padMinX = Math.Max(0, originX - PadMargin);
        var padMaxX = Math.Min(world.Width - 1, footprintMaxX + PadMargin);
        var padMinZ = Math.Max(0, originZ - PadMargin);
        var padMaxZ = Math.Min(world.Depth - 1, footprintMaxZ + PadMargin);
        var padHeights = new List<int>((padMaxX - padMinX + 1) * (padMaxZ - padMinZ + 1));

        for (var x = padMinX; x <= padMaxX; x++)
        {
            for (var z = padMinZ; z <= padMaxZ; z++)
            {
                padHeights.Add(GetGroundTopY(world, x, z));
            }
        }

        if (padHeights.Count == 0)
        {
            return false;
        }

        floorY = Math.Clamp(GetMedian(padHeights), 1, Math.Max(1, world.Height - RoofPeakOffset - 2));

        var minGround = int.MaxValue;
        var maxGround = int.MinValue;
        var fillCost = 0;
        var cutCost = 0;
        var vegetationCost = 0;

        for (var x = padMinX; x <= padMaxX; x++)
        {
            for (var z = padMinZ; z <= padMaxZ; z++)
            {
                var groundY = GetGroundTopY(world, x, z);
                var topSolidY = world.GetTopSolidY(x, z);
                minGround = Math.Min(minGround, groundY);
                maxGround = Math.Max(maxGround, groundY);
                fillCost += Math.Max(0, floorY - groundY - 1);
                cutCost += Math.Max(0, groundY - floorY + 1);
                vegetationCost += Math.Max(0, topSolidY - groundY);
            }
        }

        var groundRange = maxGround - minGround;
        if (groundRange > 4)
        {
            return false;
        }

        var centerX = originX + (FootprintSize * 0.5f);
        var centerZ = originZ + (FootprintSize * 0.5f);
        var dx = centerX - desiredCenter.X;
        var dz = centerZ - desiredCenter.Z;
        var distancePenalty = dx * dx + dz * dz;
        var clippedPadPenalty = ((originX - PadMargin) < 0 ? 1 : 0)
            + ((originZ - PadMargin) < 0 ? 1 : 0)
            + ((footprintMaxX + PadMargin) >= world.Width ? 1 : 0)
            + ((footprintMaxZ + PadMargin) >= world.Depth ? 1 : 0);

        score = groundRange * 180f
            + cutCost * 10f
            + fillCost * 6f
            + vegetationCost * 12f
            + distancePenalty * 0.85f
            + clippedPadPenalty * 200f;
        return true;
    }

    private static int GetGroundTopY(WorldMap world, int x, int z)
    {
        if (x < 0 || z < 0 || x >= world.Width || z >= world.Depth)
        {
            return 0;
        }

        for (var y = world.GetTopSolidY(x, z); y >= 0; y--)
        {
            var block = world.GetBlock(x, y, z);
            if (block == BlockType.Air || block == BlockType.Leaves || block == BlockType.Wood)
            {
                continue;
            }

            return y;
        }

        return 0;
    }

    private static int GetMedian(List<int> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }
}
