using System.Numerics;
using AIG.Game.Config;
using AIG.Game.Gameplay;
using AIG.Game.Player;
using AIG.Game.World;

namespace AIG.Game.Tests;

public sealed class GameplayTests
{
    [Fact(DisplayName = "Рейкаст возвращает null для нулевого направления")]
    public void Raycast_ReturnsNull_ForZeroDirection()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8, chunkSize: 8, seed: 0);
        var hit = VoxelRaycaster.Raycast(world, new Vector3(1.5f, 1.5f, 1.5f), Vector3.Zero, 5f);
        Assert.Null(hit);
    }

    [Fact(DisplayName = "Рейкаст сразу возвращает блок, если стартовая точка внутри твердого блока")]
    public void Raycast_ReturnsCurrentCell_WhenOriginInsideSolid()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8, chunkSize: 8, seed: 0);
        world.SetBlock(3, 3, 3, BlockType.Stone);

        var hit = VoxelRaycaster.Raycast(world, new Vector3(3.2f, 3.2f, 3.2f), new Vector3(0f, 0f, 1f), 4f);

        Assert.NotNull(hit);
        Assert.Equal(3, hit.Value.X);
        Assert.Equal(3, hit.Value.Y);
        Assert.Equal(3, hit.Value.Z);
        Assert.Equal(3, hit.Value.PreviousX);
        Assert.Equal(3, hit.Value.PreviousY);
        Assert.Equal(3, hit.Value.PreviousZ);
    }

    [Fact(DisplayName = "Рейкаст возвращает null, если блоки вне дистанции")]
    public void Raycast_ReturnsNull_WhenNoHitInDistance()
    {
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 8, seed: 0);
        world.SetBlock(10, 6, 2, BlockType.Stone);

        var hit = VoxelRaycaster.Raycast(world, new Vector3(10.5f, 6.5f, 20.5f), new Vector3(0f, 0f, -1f), 4f);
        Assert.Null(hit);
    }

    [Fact(DisplayName = "Рейкаст попадает в ближайший блок по направлению взгляда")]
    public void Raycast_HitsNearestBlock()
    {
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(8, 3, 8, BlockType.Stone);

        var hit = VoxelRaycaster.Raycast(world, new Vector3(8.5f, 3.5f, 12.5f), new Vector3(0f, 0f, -1f), 10f);

        Assert.NotNull(hit);
        Assert.Equal(8, hit.Value.X);
        Assert.Equal(3, hit.Value.Y);
        Assert.Equal(8, hit.Value.Z);
    }

    [Fact(DisplayName = "Рейкаст корректно работает при отрицательных компонентах направления")]
    public void Raycast_Hits_WithNegativeDirectionComponents()
    {
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 8, seed: 0);
        world.SetBlock(8, 7, 8, BlockType.Stone);

        var hit = VoxelRaycaster.Raycast(world, new Vector3(14.2f, 11.7f, 14.2f), new Vector3(-1f, -1f, -1f), 30f);

        Assert.NotNull(hit);
        Assert.True(hit.Value.X < 14);
        Assert.True(hit.Value.Y < 12);
        Assert.True(hit.Value.Z < 14);
    }

    [Fact(DisplayName = "Рейкаст покрывает ветвление по осям (X/Y/Z шаги)")]
    public void Raycast_CoversAxisBranching()
    {
        var world = new WorldMap(width: 32, height: 32, depth: 32, chunkSize: 8, seed: 0);
        world.SetBlock(15, 10, 11, BlockType.Stone);
        world.SetBlock(10, 15, 11, BlockType.Stone);
        world.SetBlock(11, 10, 15, BlockType.Stone);

        var hitX = VoxelRaycaster.Raycast(world, new Vector3(11.2f, 10.2f, 11.2f), new Vector3(1f, 0.1f, 0.05f), 20f);
        var hitY = VoxelRaycaster.Raycast(world, new Vector3(10.2f, 11.2f, 11.2f), new Vector3(0.05f, 1f, 0.1f), 20f);
        var hitZ = VoxelRaycaster.Raycast(world, new Vector3(11.2f, 10.2f, 11.2f), new Vector3(0.05f, 0.1f, 1f), 20f);

        Assert.NotNull(hitX);
        Assert.NotNull(hitY);
        Assert.NotNull(hitZ);
    }

    [Fact(DisplayName = "Рейкаст покрывает ветку направления с Z == 0")]
    public void Raycast_CoversZeroZBranch()
    {
        var world = new WorldMap(width: 16, height: 16, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(12, 6, 10, BlockType.Stone);

        var hit = VoxelRaycaster.Raycast(world, new Vector3(8.5f, 6.5f, 10.5f), new Vector3(1f, 0f, 0f), 8f);

        Assert.NotNull(hit);
        Assert.Equal(12, hit.Value.X);
    }

    [Fact(DisplayName = "Рейкаст на границе осей выбирает один из граничных кандидатов")]
    public void Raycast_TieBreak_ReturnsEdgeCandidate()
    {
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(3, 2, 2, BlockType.Stone);
        world.SetBlock(2, 2, 3, BlockType.Stone);

        var hit = VoxelRaycaster.Raycast(world, new Vector3(2.5f, 2.5f, 2.5f), new Vector3(1f, 0f, 1f), 5f);

        Assert.NotNull(hit);
        Assert.Equal(2, hit.Value.Y);
        var isXCandidate = hit.Value.X == 3 && hit.Value.Z == 2;
        var isZCandidate = hit.Value.X == 2 && hit.Value.Z == 3;
        Assert.True(isXCandidate || isZCandidate);
    }

    [Fact(DisplayName = "Рейкаст на угле не пропускает Z-кандидат, если X-кандидат пуст")]
    public void Raycast_CornerTouch_HitsZCandidateWhenXIsEmpty()
    {
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(2, 2, 3, BlockType.Stone);

        var hit = VoxelRaycaster.Raycast(world, new Vector3(2.5f, 2.5f, 2.5f), new Vector3(1f, 0f, 1f), 5f);

        Assert.NotNull(hit);
        Assert.Equal(2, hit.Value.X);
        Assert.Equal(2, hit.Value.Y);
        Assert.Equal(3, hit.Value.Z);
    }

    [Fact(DisplayName = "Рейкаст на ребре с Y/Z корректно выбирает Y-кандидат")]
    public void Raycast_EdgeTouch_PrefersYCandidateWhenAvailable()
    {
        var world = new WorldMap(width: 16, height: 16, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(2, 3, 2, BlockType.Stone);

        var hit = VoxelRaycaster.Raycast(world, new Vector3(2.5f, 2.5f, 2.5f), new Vector3(0f, 1f, 1f), 6f);

        Assert.NotNull(hit);
        Assert.Equal(2, hit.Value.X);
        Assert.Equal(3, hit.Value.Y);
        Assert.Equal(2, hit.Value.Z);
    }

    [Fact(DisplayName = "Рейкаст из-за границы мира корректно нормализует previous координаты")]
    public void Raycast_FromOutsideWorld_NormalizesPreviousCell()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8, chunkSize: 8, seed: 0);
        world.SetBlock(0, 2, 2, BlockType.Stone);

        var hit = VoxelRaycaster.Raycast(world, new Vector3(-0.5f, 2.5f, 2.5f), Vector3.UnitX, 2f);

        Assert.NotNull(hit);
        Assert.Equal(0, hit.Value.X);
        Assert.Equal(2, hit.Value.Y);
        Assert.Equal(2, hit.Value.Z);
        Assert.Equal(hit.Value.X, hit.Value.PreviousX);
        Assert.Equal(hit.Value.Y, hit.Value.PreviousY);
        Assert.Equal(hit.Value.Z, hit.Value.PreviousZ);
    }

    [Fact(DisplayName = "Рейкаст на точном пределе дистанции обрабатывает tie без probe-совпадения")]
    public void Raycast_TieAtDistanceLimit_WorksWithoutProbeMatch()
    {
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(3, 2, 2, BlockType.Stone);
        world.SetBlock(2, 2, 3, BlockType.Stone);

        var maxDistance = MathF.Sqrt(0.5f);
        var hit = VoxelRaycaster.Raycast(world, new Vector3(2.5f, 2.5f, 2.5f), new Vector3(1f, 0f, 1f), maxDistance);

        Assert.NotNull(hit);
        Assert.Equal(2, hit.Value.Y);
        var isXCandidate = hit.Value.X == 3 && hit.Value.Z == 2;
        var isZCandidate = hit.Value.X == 2 && hit.Value.Z == 3;
        Assert.True(isXCandidate || isZCandidate);
    }

    [Fact(DisplayName = "Рейкаст на граничном tie выбирает клетку по направлению после пересечения")]
    public void Raycast_TieUsesForwardProbe_ForDeterministicSelection()
    {
        var world = new WorldMap(width: 16, height: 16, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(3, 2, 2, BlockType.Stone);
        world.SetBlock(3, 3, 2, BlockType.Stone);

        var direction = Vector3.Normalize(new Vector3(1f, 1f, 0f));
        var hit = VoxelRaycaster.Raycast(world, new Vector3(2.5f, 2.5f, 2.5f), direction, 6f);

        Assert.NotNull(hit);
        Assert.Equal(3, hit.Value.X);
        Assert.Equal(3, hit.Value.Y);
        Assert.Equal(2, hit.Value.Z);
    }

    [Fact(DisplayName = "Рейкаст отсекает блоки, если вход в них дальше maxDistance")]
    public void Raycast_RejectsBlocksBeyondDistance()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8, chunkSize: 8, seed: 0);
        world.SetBlock(1, 2, 2, BlockType.Stone);

        var hit = VoxelRaycaster.Raycast(world, new Vector3(0.1f, 2.5f, 2.5f), Vector3.UnitX, 0.01f);

        Assert.Null(hit);
    }

    [Fact(DisplayName = "Рейкаст использует fallback Y, если доминирующий X-кандидат пуст")]
    public void Raycast_FallbackY_WhenDominantXCandidateIsEmpty()
    {
        var world = new WorldMap(width: 16, height: 16, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(2, 3, 2, BlockType.Stone);

        var hit = VoxelRaycaster.Raycast(world, new Vector3(2.25f, 2.5f, 2.5f), new Vector3(1.5f, 1f, 0f), 6f);

        Assert.NotNull(hit);
        Assert.Equal(2, hit.Value.X);
        Assert.Equal(3, hit.Value.Y);
        Assert.Equal(2, hit.Value.Z);
    }

    [Fact(DisplayName = "Рейкаст использует fallback Z, если доминирующий X-кандидат пуст")]
    public void Raycast_FallbackZ_WhenDominantXCandidateIsEmpty()
    {
        var world = new WorldMap(width: 16, height: 16, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(2, 2, 3, BlockType.Stone);

        var hit = VoxelRaycaster.Raycast(world, new Vector3(2.25f, 2.5f, 2.5f), new Vector3(1.5f, 0f, 1f), 6f);

        Assert.NotNull(hit);
        Assert.Equal(2, hit.Value.X);
        Assert.Equal(2, hit.Value.Y);
        Assert.Equal(3, hit.Value.Z);
    }

    [Fact(DisplayName = "Луч из позиции глаз игрока попадает в центральный блок по направлению взгляда")]
    public void Raycast_FromPlayerEye_HitsExpectedBlock()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        world.SetBlock(12, 5, 12, BlockType.Air);
        world.SetBlock(12, 5, 11, BlockType.Air);
        world.SetBlock(12, 5, 10, BlockType.Stone);
        var player = new PlayerController(new GameConfig(), new Vector3(12.5f, 4f, 12.5f));

        var hit = VoxelRaycaster.Raycast(world, player.EyePosition, player.LookDirection, 6.5f);

        Assert.NotNull(hit);
        Assert.Equal(12, hit.Value.X);
        Assert.Equal(5, hit.Value.Y);
        Assert.Equal(10, hit.Value.Z);
    }

    [Fact(DisplayName = "Рейкаст на позиции игрока из скрина выбирает ту же грань, что и точное AABB-пересечение")]
    public void Raycast_UserScreenshotPose_MatchesAabbFaceAcrossDirections()
    {
        var world = new WorldMap(width: 96, height: 32, depth: 96, chunkSize: 16, seed: 777);
        var terrainY = world.GetTerrainTopY(47, 45);
        var player = new PlayerController(new GameConfig(), new Vector3(47.73f, terrainY + 3f, 45.37f));
        var origin = player.EyePosition;
        const float maxDistance = 6.5f;

        for (var yaw = -MathF.PI; yaw <= MathF.PI; yaw += 0.16f)
        {
            for (var pitch = -1.25f; pitch <= 1.25f; pitch += 0.14f)
            {
                var direction = BuildDirection(yaw, pitch);
                var hit = VoxelRaycaster.Raycast(world, origin, direction, maxDistance);
                if (hit is null)
                {
                    continue;
                }

                var actual = new Vector3(
                    hit.Value.PreviousX - hit.Value.X,
                    hit.Value.PreviousY - hit.Value.Y,
                    hit.Value.PreviousZ - hit.Value.Z);

                var nonZero = (actual.X != 0 ? 1 : 0) + (actual.Y != 0 ? 1 : 0) + (actual.Z != 0 ? 1 : 0);
                Assert.Equal(1, nonZero);
                Assert.InRange(MathF.Abs(actual.X + actual.Y + actual.Z), 1f, 1f);

                var hasExpected = TryGetExpectedAabbEntryFace(origin, direction, hit.Value, out var expected, out var ambiguous);
                Assert.True(hasExpected);

                if (ambiguous)
                {
                    continue;
                }

                Assert.True(Vector3.Distance(expected, actual) < 0.001f,
                    $"Несовпадение грани. yaw={yaw:0.000}, pitch={pitch:0.000}, expected={expected}, actual={actual}, hit=({hit.Value.X},{hit.Value.Y},{hit.Value.Z}) prev=({hit.Value.PreviousX},{hit.Value.PreviousY},{hit.Value.PreviousZ})");
            }
        }
    }

    private static Vector3 BuildDirection(float yaw, float pitch)
    {
        var x = MathF.Sin(yaw) * MathF.Cos(pitch);
        var y = MathF.Sin(pitch);
        var z = MathF.Cos(yaw) * MathF.Cos(pitch);
        return Vector3.Normalize(new Vector3(x, y, z));
    }

    private static bool TryGetExpectedAabbEntryFace(
        Vector3 origin,
        Vector3 direction,
        BlockRaycastHit hit,
        out Vector3 expectedFace,
        out bool ambiguous)
    {
        const float eps = 0.0001f;
        var min = new Vector3(hit.X, hit.Y, hit.Z);
        var max = min + Vector3.One;

        if (!TryAxis(origin.X, direction.X, min.X, max.X, out var nearX, out var farX)
            || !TryAxis(origin.Y, direction.Y, min.Y, max.Y, out var nearY, out var farY)
            || !TryAxis(origin.Z, direction.Z, min.Z, max.Z, out var nearZ, out var farZ))
        {
            expectedFace = Vector3.Zero;
            ambiguous = false;
            return false;
        }

        var entry = MathF.Max(MathF.Max(nearX, nearY), nearZ);
        var exit = MathF.Min(MathF.Min(farX, farY), farZ);
        if (exit < entry || exit < 0f)
        {
            expectedFace = Vector3.Zero;
            ambiguous = false;
            return false;
        }

        var xHit = MathF.Abs(entry - nearX) <= eps;
        var yHit = MathF.Abs(entry - nearY) <= eps;
        var zHit = MathF.Abs(entry - nearZ) <= eps;
        var faceCount = (xHit ? 1 : 0) + (yHit ? 1 : 0) + (zHit ? 1 : 0);
        ambiguous = faceCount != 1;

        if (xHit)
        {
            expectedFace = new Vector3(direction.X > 0f ? -1f : 1f, 0f, 0f);
            return true;
        }

        if (yHit)
        {
            expectedFace = new Vector3(0f, direction.Y > 0f ? -1f : 1f, 0f);
            return true;
        }

        expectedFace = new Vector3(0f, 0f, direction.Z > 0f ? -1f : 1f);
        return true;
    }

    private static bool TryAxis(float origin, float direction, float min, float max, out float near, out float far)
    {
        const float axisEps = 0.0000001f;
        if (MathF.Abs(direction) <= axisEps)
        {
            if (origin < min || origin > max)
            {
                near = 0f;
                far = 0f;
                return false;
            }

            near = float.NegativeInfinity;
            far = float.PositiveInfinity;
            return true;
        }

        var t1 = (min - origin) / direction;
        var t2 = (max - origin) / direction;
        near = MathF.Min(t1, t2);
        far = MathF.Max(t1, t2);
        return true;
    }

    [Fact(DisplayName = "Ломание удаляет блок в точке попадания")]
    public void Break_RemovesBlock()
    {
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(4, 3, 4, BlockType.Stone);

        var ok = BlockInteraction.TryBreak(world, new BlockRaycastHit(4, 3, 4, 4, 3, 5));

        Assert.True(ok);
        Assert.Equal(BlockType.Air, world.GetBlock(4, 3, 4));
    }

    [Fact(DisplayName = "Ломание не выполняется при null, вне мира и по воздуху")]
    public void Break_Fails_ForInvalidCases()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8, chunkSize: 8, seed: 0);

        Assert.False(BlockInteraction.TryBreak(world, null));
        Assert.False(BlockInteraction.TryBreak(world, new BlockRaycastHit(-1, 1, 1, 0, 0, 0)));
        Assert.False(BlockInteraction.TryBreak(world, new BlockRaycastHit(3, 6, 3, 0, 0, 0)));
    }

    [Fact(DisplayName = "Установка ставит выбранный блок в предыдущую пустую клетку")]
    public void Place_SetsBlockInPreviousCell()
    {
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(5, 3, 5, BlockType.Stone);

        var ok = BlockInteraction.TryPlace(
            world,
            new BlockRaycastHit(5, 3, 5, 5, 3, 6),
            BlockType.Dirt,
            _ => false);

        Assert.True(ok);
        Assert.Equal(BlockType.Dirt, world.GetBlock(5, 3, 6));
    }

    [Fact(DisplayName = "Установка не работает, если место занято игроком")]
    public void Place_FailsWhenPlayerOccupiesCell()
    {
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(5, 3, 5, BlockType.Stone);

        var ok = BlockInteraction.TryPlace(
            world,
            new BlockRaycastHit(5, 3, 5, 5, 3, 6),
            BlockType.Dirt,
            _ => true);

        Assert.False(ok);
        Assert.Equal(BlockType.Air, world.GetBlock(5, 3, 6));
    }

    [Fact(DisplayName = "Установка не выполняется при невалидных условиях")]
    public void Place_Fails_ForInvalidCases()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8, chunkSize: 8, seed: 0);
        world.SetBlock(3, 2, 3, BlockType.Stone);

        Assert.False(BlockInteraction.TryPlace(world, null, BlockType.Dirt, _ => false));
        Assert.False(BlockInteraction.TryPlace(world, new BlockRaycastHit(3, 2, 3, 2, 2, 2), BlockType.Air, _ => false));
        Assert.False(BlockInteraction.TryPlace(world, new BlockRaycastHit(3, 2, 3, -1, 2, 2), BlockType.Dirt, _ => false));

        world.SetBlock(2, 2, 2, BlockType.Stone);
        Assert.False(BlockInteraction.TryPlace(world, new BlockRaycastHit(3, 2, 3, 2, 2, 2), BlockType.Dirt, _ => false));
    }
}
