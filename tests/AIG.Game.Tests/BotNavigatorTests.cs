using System.Numerics;
using System.Reflection;
using AIG.Game.Bot;
using AIG.Game.World;

namespace AIG.Game.Tests;

public sealed class BotNavigatorTests
{
    private static readonly BotNavigationSettings Settings = new(ColliderHalfWidth: 0.3f, ColliderHeight: 1.8f, ReachDistance: 7f);

    [Fact(DisplayName = "BotNavigator находит ближайшую стоячую клетку рядом с позой")]
    public void BotNavigator_FindsNearestStandCell()
    {
        var world = CreateFlatWorld(12, 12);

        var found = BotNavigator.TryFindNearestStandCell(world, Settings, new Vector3(4.65f, 2.02f, 4.4f), searchRadius: 1, out var cell);

        Assert.True(found);
        Assert.Equal(new BotNavigationCell(4, 2, 4), cell);
        Assert.Equal(new Vector3(4.5f, 2.02f, 4.5f), cell.ToPose());
    }

    [Fact(DisplayName = "BotNavigator корректно ищет ближайшую стоячую клетку у края мира")]
    public void BotNavigator_FindsNearestStandCell_AtWorldEdge()
    {
        var world = CreateFlatWorld(8, 12);
        world.SetBlock(0, 2, 0, BlockType.Wood);
        world.SetBlock(0, 3, 0, BlockType.Wood);
        world.SetBlock(0, 2, 1, BlockType.Wood);
        world.SetBlock(0, 3, 1, BlockType.Wood);

        var found = BotNavigator.TryFindNearestStandCell(world, Settings, new Vector3(0.2f, 2.02f, 0.2f), searchRadius: 2, out var cell);

        Assert.True(found);
        Assert.Equal(new BotNavigationCell(1, 2, 0), cell);
    }

    [Fact(DisplayName = "BotNavigator строит маршрут обхода вокруг стены для следования")]
    public void BotNavigator_BuildsStandRoute_AroundWall()
    {
        var world = CreateFlatWorld(16, 12);
        for (var z = 3; z <= 12; z++)
        {
            if (z == 8)
            {
                continue;
            }

            world.SetBlock(7, 2, z, BlockType.Wood);
            world.SetBlock(7, 3, z, BlockType.Wood);
        }

        var found = BotNavigator.TryBuildStandRoute(
            world,
            Settings,
            new Vector3(4.5f, 2.02f, 8.5f),
            new Vector3(11.5f, 2.02f, 8.5f),
            goalRadius: 0,
            out var route);

        Assert.True(found);
        Assert.NotEmpty(route);
        Assert.Contains(route, pose => (int)MathF.Floor(pose.X) == 7 && (int)MathF.Floor(pose.Z) == 8);
        Assert.Equal(new Vector3(11.5f, 2.02f, 8.5f), route[^1]);
    }

    [Fact(DisplayName = "BotNavigator для follow умеет строить маршрут в обход запретной зоны игрока")]
    public void BotNavigator_BuildsStandRoute_AroundForbiddenPlayerZone()
    {
        var world = CreateFlatWorld(16, 12);
        var playerPose = new Vector3(7.5f, 2.02f, 6.5f);

        var found = BotNavigator.TryBuildStandRoute(
            world,
            Settings,
            new Vector3(3.5f, 2.02f, 6.5f),
            new Vector3(11.5f, 2.02f, 6.5f),
            goalRadius: 0,
            cell =>
            {
                var pose = cell.ToPose();
                var delta = pose - playerPose;
                return delta.X * delta.X + delta.Z * delta.Z > 1.25f * 1.25f;
            },
            out var route);

        Assert.True(found);
        Assert.NotEmpty(route);
        Assert.DoesNotContain(route, pose =>
        {
            var delta = pose - playerPose;
            return delta.X * delta.X + delta.Z * delta.Z <= 1.25f * 1.25f;
        });
    }

    [Fact(DisplayName = "BotNavigator для стройки предпочитает землю у дома, а не крышу рядом с целью")]
    public void BotNavigator_BuildsActionRoute_PrefersGroundPerimeter()
    {
        var world = CreateFlatWorld(32, 16);
        var blueprint = HouseBlueprint.CreateCabinS(world, new Vector3(8.5f, 2.02f, 8.5f), new Vector3(1f, 0f, 0f));

        for (var i = 0; i < blueprint.Steps.Count; i++)
        {
            var step = blueprint.Steps[i];
            if (step.Y <= blueprint.FloorY + 4)
            {
                world.SetBlock(step.X, step.Y, step.Z, step.Block);
            }
        }

        var found = BotNavigator.TryBuildActionRoute(
            world,
            Settings,
            new Vector3(23.5f, 2.02f, 8.5f),
            blueprint.OriginX + 3,
            blueprint.FloorY + 4,
            blueprint.OriginZ + 3,
            searchRadius: 7,
            blueprint,
            out var route,
            out var destinationPose);

        Assert.True(found);
        Assert.NotEmpty(route);
        Assert.True(destinationPose.Y < 4f, $"Destination={destinationPose}");
        Assert.Equal(destinationPose, route[^1]);
    }

    [Fact(DisplayName = "BotNavigator возвращает false, если стартовая поза неразрешима и рядом нет стоячей клетки")]
    public void BotNavigator_Fails_WhenNoStandCellExists()
    {
        var world = new WorldMap(width: 6, height: 8, depth: 6, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 1; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }

        Assert.False(BotNavigator.TryFindNearestStandCell(world, Settings, new Vector3(2.5f, 6.02f, 2.5f), searchRadius: 2, out _));
        Assert.False(BotNavigator.TryBuildStandRoute(world, Settings, new Vector3(2.5f, 6.02f, 2.5f), new Vector3(4.5f, 6.02f, 4.5f), goalRadius: 0, out _));
        Assert.False(BotNavigator.TryBuildActionRoute(world, Settings, new Vector3(2.5f, 6.02f, 2.5f), 4, 6, 4, searchRadius: 2, blueprint: null, out _, out _));
    }

    [Fact(DisplayName = "BotNavigator private core завершает поиск по лимиту посещенных клеток")]
    public void BotNavigator_PrivateCore_StopsAfterVisitLimit()
    {
        var world = CreateFlatWorld(96, 12);
        var args = new object?[]
        {
            world,
            Settings,
            new Vector3(48.5f, 2.02f, 48.5f),
            new Vector3(90.5f, 2.02f, 90.5f),
            (0, world.Width - 1, 0, world.Depth - 1),
            (Func<BotNavigationCell, bool>)(_ => false),
            (Func<BotNavigationCell, float>)(_ => 0f),
            null,
            null,
            null
        };

        var result = (bool)InvokePrivateStatic("TryBuildRouteCore", args)!;

        Assert.False(result);
    }

    [Fact(DisplayName = "BotNavigator private helpers покрывают границы позы и тривиальную реконструкцию пути")]
    public void BotNavigator_PrivateHelpers_CoverBoundsAndReconstruct()
    {
        var world = CreateFlatWorld(12, 12);

        Assert.False((bool)InvokePrivateStatic("IsPoseClear", world, Settings, new Vector3(-0.2f, 2.02f, 4.5f))!);

        var start = new BotNavigationCell(4, 2, 4);
        var route = Assert.IsType<Vector3[]>(InvokePrivateStatic(
            "ReconstructWaypoints",
            new Dictionary<BotNavigationCell, BotNavigationCell>(),
            start,
            start)!);
        Assert.Empty(route);
    }

    [Fact(DisplayName = "BotNavigator покрывает ветки старта в цели, overlap-геометрию и близость позы")]
    public void BotNavigator_PrivateGoalAndGeometryBranches_Work()
    {
        var world = CreateFlatWorld(12, 12);

        Assert.True(BotNavigator.TryBuildStandRoute(world, Settings, new Vector3(4.5f, 2.02f, 4.5f), new Vector3(4.5f, 2.02f, 4.5f), goalRadius: 0, out var exactRoute));
        Assert.Empty(exactRoute);

        Assert.True(BotNavigator.TryBuildStandRoute(world, Settings, new Vector3(4.82f, 2.02f, 4.76f), new Vector3(4.5f, 2.02f, 4.5f), goalRadius: 0, out var snappedRoute));
        Assert.Single(snappedRoute);

        Assert.True((bool)InvokePrivateStatic("DoesPoseOverlapBlock", Settings, new Vector3(4.5f, 2.02f, 4.5f), 4, 2, 4)!);
        Assert.False((bool)InvokePrivateStatic("DoesPoseOverlapBlock", Settings, new Vector3(3.1f, 2.02f, 4.5f), 4, 2, 4)!);
        Assert.False((bool)InvokePrivateStatic("DoesPoseOverlapBlock", Settings, new Vector3(4.5f, 0.1f, 4.5f), 4, 2, 4)!);
        Assert.False((bool)InvokePrivateStatic("DoesPoseOverlapBlock", Settings, new Vector3(4.5f, 2.02f, 3.1f), 4, 2, 4)!);
        Assert.True(BotNavigator.IsSupportingPoseBlock(Settings, new Vector3(4.5f, 3.02f, 4.5f), 4, 2, 4));
        Assert.False(BotNavigator.IsSupportingPoseBlock(Settings, new Vector3(4.5f, 3.02f, 4.5f), 5, 2, 4));
        Assert.False(BotNavigator.IsSupportingPoseBlock(Settings, new Vector3(4.5f, 3.02f, 4.5f), 4, 1, 4));

        Assert.True((bool)InvokePrivateStatic("IsCloseToPose", new Vector3(4.5f, 2.02f, 4.5f), new Vector3(4.55f, 2.3f, 4.52f), 0.1f)!);
        Assert.False((bool)InvokePrivateStatic("IsCloseToPose", new Vector3(4.5f, 2.02f, 4.5f), new Vector3(5.0f, 2.02f, 4.5f), 0.1f)!);
    }

    [Fact(DisplayName = "BotNavigator не возвращает пустой action-route, если старт почти у цели, но блок еще недосягаем")]
    public void BotNavigator_ActionRoute_SnapsToGoalCell_WhenCurrentPoseCannotActYet()
    {
        var world = CreateFlatWorld(32, 16);
        var startPose = new Vector3(10.59f, 2.92f, 10.5f);

        var found = BotNavigator.TryBuildActionRoute(
            world,
            Settings,
            startPose,
            targetX: 4,
            targetY: 0,
            targetZ: 9,
            searchRadius: 8,
            blueprint: null,
            out var route,
            out var destinationPose);

        Assert.True(found);
        Assert.Single(route);
        Assert.Equal(new Vector3(10.5f, 2.02f, 10.5f), destinationPose);
        Assert.Equal(destinationPose, route[0]);
    }

    [Fact(DisplayName = "BotNavigator для добычи не выбирает клетку на верхней грани самого целевого блока")]
    public void BotNavigator_ActionRoute_DoesNotUseTargetBlockAsSupport()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(10, 2, 10, BlockType.Stone);

        var found = BotNavigator.TryBuildActionRoute(
            world,
            Settings,
            new Vector3(2.5f, 2.02f, 10.5f),
            targetX: 10,
            targetY: 2,
            targetZ: 10,
            searchRadius: 6,
            blueprint: null,
            out var route,
            out var destinationPose);

        Assert.True(found);
        Assert.NotEmpty(route);
        Assert.False(BotNavigator.IsSupportingPoseBlock(Settings, destinationPose, 10, 2, 10), $"Destination={destinationPose}");
        Assert.Equal(destinationPose, route[^1]);
    }

    private static WorldMap CreateFlatWorld(int size, int height)
    {
        var world = new WorldMap(width: size, height: height, depth: size, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                world.SetBlock(x, 0, z, BlockType.Stone);
                world.SetBlock(x, 1, z, BlockType.Dirt);
            }
        }

        return world;
    }

    private static object? InvokePrivateStatic(string methodName, params object?[] args)
    {
        var method = typeof(BotNavigator).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(null, args);
    }
}
