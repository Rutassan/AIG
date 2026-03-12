using System.Numerics;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using AIG.Game.Bot;
using AIG.Game.Config;
using AIG.Game.Core;
using AIG.Game.Player;
using AIG.Game.Tests.Fakes;
using AIG.Game.World;
using Raylib_cs;

namespace AIG.Game.Tests;

public sealed class BotTests
{
    [Fact(DisplayName = "Bot resource/status helpers и выбор команды циклятся корректно")]
    public void BotHelpers_CycleAndMapCorrectly()
    {
        Assert.Equal(BlockType.Wood, BotResourceType.Wood.ToBlockType());
        Assert.Equal(BlockType.Stone, BotResourceType.Stone.ToBlockType());
        Assert.Equal(BlockType.Dirt, BotResourceType.Dirt.ToBlockType());
        Assert.Equal(BlockType.Leaves, BotResourceType.Leaves.ToBlockType());

        Assert.True(BotResourceType.Wood.Matches(BlockType.Wood));
        Assert.True(BotResourceType.Stone.Matches(BlockType.Stone));
        Assert.True(BotResourceType.Dirt.Matches(BlockType.Grass));
        Assert.True(BotResourceType.Leaves.Matches(BlockType.Leaves));
        Assert.False(BotResourceType.Stone.Matches(BlockType.Wood));

        Assert.Equal(BotResourceType.Dirt, BotResourceTypeExtensions.FromBlock(BlockType.Dirt));
        Assert.Equal(BotResourceType.Dirt, BotResourceTypeExtensions.FromBlock(BlockType.Grass));
        Assert.Equal(BotResourceType.Leaves, BotResourceTypeExtensions.FromBlock((BlockType)777));

        Assert.Equal("Ждет команд", BotStatus.Idle.GetLabel());
        Assert.Equal("Идет", BotStatus.Moving.GetLabel());
        Assert.Equal("Добывает", BotStatus.Gathering.GetLabel());
        Assert.Equal("Строит", BotStatus.Building.GetLabel());
        Assert.Equal("Нет пути", BotStatus.NoPath.GetLabel());

        var selection = new BotCommandSelection();
        Assert.Equal(BotResourceType.Wood, selection.SelectedResource);
        Assert.Equal(16, selection.SelectedAmount);

        selection.CycleResource();
        selection.CycleResource();
        selection.CycleResource();
        selection.CycleResource();
        Assert.Equal(BotResourceType.Wood, selection.SelectedResource);

        selection.CycleAmount();
        selection.CycleAmount();
        selection.CycleAmount();
        selection.CycleAmount();
        Assert.Equal(16, selection.SelectedAmount);
    }

    [Fact(DisplayName = "Bot helpers покрывают invalid/default ветки и малый мир blueprint")]
    public void BotHelpers_InvalidBranches_AndTinyBlueprint_Work()
    {
        Assert.False(BotResourceType.Stone.Matches(BlockType.Dirt));
        Assert.False(BotResourceType.Dirt.Matches(BlockType.Stone));
        Assert.False(BotResourceType.Leaves.Matches(BlockType.Wood));
        Assert.False(((BotResourceType)99).Matches(BlockType.Wood));
        Assert.True(((BotResourceType)99).Matches(BlockType.Leaves));
        Assert.Equal(BotResourceType.Stone, BotResourceTypeExtensions.FromBlock(BlockType.Stone));
        Assert.Equal("Земля", BotResourceType.Dirt.GetLabel());
        Assert.Equal("Листва", BotResourceType.Leaves.GetLabel());
        Assert.Equal("Листва", ((BotResourceType)99).GetLabel());
        Assert.Equal(6, (int)InvokePrivateStatic(typeof(CompanionBot), "GetHarvestBatchSize", BotResourceType.Wood)!);
        Assert.Equal(8, (int)InvokePrivateStatic(typeof(CompanionBot), "GetHarvestBatchSize", BotResourceType.Leaves)!);
        Assert.Equal(4, (int)InvokePrivateStatic(typeof(CompanionBot), "GetHarvestBatchSize", BotResourceType.Dirt)!);
        Assert.Equal(4, (int)InvokePrivateStatic(typeof(CompanionBot), "GetHarvestBatchSize", BotResourceType.Stone)!);
        Assert.Equal(1, (int)InvokePrivateStatic(typeof(CompanionBot), "GetHarvestBatchSize", (BotResourceType)99)!);
        Assert.Equal(2, (int)InvokePrivateStatic(typeof(CompanionBot), "GetFocusedResourceHorizontalRadius", BotResourceType.Wood)!);
        Assert.Equal(2, (int)InvokePrivateStatic(typeof(CompanionBot), "GetFocusedResourceHorizontalRadius", BotResourceType.Leaves)!);
        Assert.Equal(1, (int)InvokePrivateStatic(typeof(CompanionBot), "GetFocusedResourceHorizontalRadius", BotResourceType.Stone)!);
        Assert.Equal(6, (int)InvokePrivateStatic(typeof(CompanionBot), "GetFocusedResourceVerticalRadius", BotResourceType.Wood)!);
        Assert.Equal(6, (int)InvokePrivateStatic(typeof(CompanionBot), "GetFocusedResourceVerticalRadius", BotResourceType.Leaves)!);
        Assert.Equal(2, (int)InvokePrivateStatic(typeof(CompanionBot), "GetFocusedResourceVerticalRadius", BotResourceType.Dirt)!);

        var tinyWorld = new WorldMap(width: 6, height: 8, depth: 6, chunkSize: 8, seed: 0);
        var blueprint = HouseBlueprint.CreateCabinS(tinyWorld, new Vector3(2.5f, 2f, 2.5f), Vector3.Zero);

        Assert.Equal(0, blueprint.OriginX);
        Assert.Equal(0, blueprint.OriginZ);
        Assert.All(blueprint.Steps, step =>
        {
            Assert.InRange(step.X, 0, tinyWorld.Width - 1);
            Assert.InRange(step.Y, 0, tinyWorld.Height - 1);
            Assert.InRange(step.Z, 0, tinyWorld.Depth - 1);
        });
    }

    [Fact(DisplayName = "House blueprint для дома S содержит дверь, окна и требуемые ресурсы")]
    public void HouseBlueprint_CreateCabinS_ContainsExpectedStructure()
    {
        var world = new WorldMap(width: 48, height: 16, depth: 48, chunkSize: 8, seed: 0);
        var blueprint = HouseBlueprint.CreateCabinS(world, new Vector3(12.5f, 2f, 12.5f), new Vector3(1f, 0f, 0f));

        Assert.Equal(HouseTemplateKind.CabinS, blueprint.Template);
        Assert.Equal("Дом S", blueprint.Name);
        Assert.True(blueprint.RequiredResources[BlockType.Wood] > 0);
        Assert.True(blueprint.RequiredResources[BlockType.Dirt] > 0);
        Assert.True(blueprint.RequiredResources[BlockType.Stone] > 0);
        Assert.True(blueprint.RequiredResources[BlockType.Leaves] > 0);
        Assert.Contains(blueprint.Steps, step => step.X == blueprint.OriginX + 3 && step.Y == blueprint.FloorY && step.Z == blueprint.OriginZ + 3 && step.Block == BlockType.Wood);
        Assert.Contains(blueprint.Steps, step => step.X == blueprint.OriginX && step.Y == blueprint.FloorY && step.Z == blueprint.OriginZ && step.Block == BlockType.Stone);
        Assert.Contains(blueprint.Steps, step => step.X == blueprint.OriginX + 3 && step.Y == blueprint.FloorY && step.Z == blueprint.OriginZ - 1 && step.Block == BlockType.Wood);
        Assert.Contains(blueprint.Steps, step => step.X == blueprint.OriginX + 3 && step.Y == blueprint.FloorY && step.Z == blueprint.OriginZ - 2 && step.Block == BlockType.Stone);
        Assert.Contains(blueprint.Steps, step => step.X == blueprint.OriginX + 5 && step.Y == blueprint.FloorY + 2 && step.Z == blueprint.OriginZ + 5 && step.Block == BlockType.Stone);
        Assert.Contains(blueprint.Steps, step => step.X == blueprint.OriginX - 1 && step.Y == blueprint.FloorY + 1 && step.Z == blueprint.OriginZ + 1 && step.Block == BlockType.Leaves);
        Assert.True(blueprint.IsPlannedSolidBlock(blueprint.OriginX + 3, blueprint.FloorY, blueprint.OriginZ + 3, BlockType.Wood));
        Assert.True(blueprint.IsPlannedSolidBlock(blueprint.OriginX, blueprint.FloorY, blueprint.OriginZ, BlockType.Stone));
        Assert.True(blueprint.IsInsideFootprint(blueprint.OriginX + 3, blueprint.OriginZ + 3));
        Assert.True(blueprint.IsInsideInterior(blueprint.OriginX + 3, blueprint.OriginZ + 3));
        Assert.False(blueprint.IsInsideInterior(blueprint.OriginX, blueprint.OriginZ));
        Assert.False(blueprint.IsInsideFootprint(blueprint.OriginX - 1, blueprint.OriginZ));
        Assert.False(blueprint.IsPlannedSolidBlock(blueprint.OriginX + 3, blueprint.FloorY + 1, blueprint.OriginZ, BlockType.Wood));
        Assert.False(blueprint.IsPlannedSolidBlock(blueprint.OriginX, blueprint.FloorY + 2, blueprint.OriginZ + 2, BlockType.Wood));
        Assert.False(blueprint.IsPlannedSolidBlock(blueprint.OriginX + 6, blueprint.FloorY + 2, blueprint.OriginZ + 4, BlockType.Wood));
        Assert.True(blueprint.CountRemaining(BlockType.Wood, fromIndex: -5) > 0);
        Assert.True(blueprint.CountRemaining(BlockType.Stone, fromIndex: 0) > 0);
        Assert.True(blueprint.CountRemaining(BlockType.Leaves, fromIndex: 0) > 0);
        Assert.Equal(0, blueprint.CountRemaining(BlockType.Wood, fromIndex: 99_999));
        Assert.InRange(blueprint.Center.X, blueprint.OriginX + 3f, blueprint.OriginX + 4f);
    }

    [Fact(DisplayName = "House blueprint не считает перекрытые шаги реальным остатком ресурсов")]
    public void HouseBlueprint_CountRemaining_SkipsSupersededSteps()
    {
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Перекрытые-шаги",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(8, 2, 8, BlockType.Stone),
                new HouseBuildStep(8, 2, 8, BlockType.Air),
                new HouseBuildStep(9, 2, 8, BlockType.Stone)
            ]);

        Assert.True(blueprint.IsSupersededStep(0));
        Assert.False(blueprint.IsSupersededStep(1));
        Assert.False(blueprint.IsSupersededStep(2));
        Assert.False(blueprint.IsSupersededStep(99));
        Assert.Equal(1, blueprint.CountRemaining(BlockType.Stone, 0));
        Assert.Equal(1, blueprint.CountRemaining(BlockType.Stone, 2));
        Assert.Equal(0, blueprint.CountRemaining(BlockType.Stone, 3));
        Assert.Equal(1, blueprint.CountRemaining(BlockType.Air, 0));
    }

    [Fact(DisplayName = "House blueprint готовит колонку площадки целиком, без временной канавы вокруг дома")]
    public void HouseBlueprint_BuildCabinSteps_PreparesPadColumnContiguously()
    {
        var world = new WorldMap(width: 16, height: 12, depth: 16, chunkSize: 8, seed: 0);
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

        world.SetBlock(2, 1, 2, BlockType.Air);
        world.SetBlock(2, 4, 2, BlockType.Wood);
        world.SetBlock(2, 5, 2, BlockType.Leaves);

        var steps = Assert.IsType<HouseBuildStep[]>(InvokePrivateStatic(typeof(HouseBlueprint), "BuildCabinSteps", world, 4, 3, 4)!);
        var columnSteps = steps
            .Select((step, index) => (step, index))
            .Where(entry => entry.step.X == 2 && entry.step.Z == 2)
            .ToArray();

        Assert.True(columnSteps.Length >= 4);
        var contiguousIndices = Enumerable.Range(columnSteps[0].index, columnSteps.Length).ToArray();
        Assert.Equal(contiguousIndices, columnSteps.Select(entry => entry.index).ToArray());
        Assert.Contains(columnSteps, entry => entry.step.Y == 4 && entry.step.Block == BlockType.Air);
        Assert.Contains(columnSteps, entry => entry.step.Y == 5 && entry.step.Block == BlockType.Air);
        Assert.Contains(columnSteps, entry => entry.step.Y == 2 && entry.step.Block == BlockType.Dirt);
        Assert.Equal(new HouseBuildStep(2, 3, 2, BlockType.Dirt), columnSteps[^1].step);
    }

    [Fact(DisplayName = "CompanionBot не выбирает позу внутри самого строящегося блока")]
    public void CompanionBot_TryFindActionPoseNear_DoesNotStandInsideTargetBlock()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        WarmWorld(world, new Vector3(8.5f, 2.02f, 8.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 10.5f));
        var method = typeof(CompanionBot).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "TryFindActionPoseNear" && candidate.GetParameters().Length == 8);
        var args = new object?[] { world, 10, 2, 10, 2, null, null, null };

        var found = (bool)method.Invoke(bot, args)!;

        Assert.True(found);
        var pose = Assert.IsType<Vector3>(args[6]!);
        Assert.False((int)MathF.Floor(pose.X) == 10 && (int)MathF.Floor(pose.Z) == 10);
    }

    [Fact(DisplayName = "CompanionBot при добыче не выбирает позу на опоре самого целевого блока")]
    public void CompanionBot_TryFindActionPoseNear_ForGather_DoesNotStandOnTargetSupport()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(10, 2, 10, BlockType.Stone);

        var bot = new CompanionBot(new GameConfig(), new Vector3(2.5f, 2.02f, 10.5f));
        var method = typeof(CompanionBot).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "TryFindActionPoseNear" && candidate.GetParameters().Length == 8);
        var args = new object?[] { world, 10, 2, 10, 2, null, null, null };

        var found = (bool)method.Invoke(bot, args)!;

        Assert.True(found);
        var pose = Assert.IsType<Vector3>(args[6]!);
        Assert.False((int)MathF.Floor(pose.X) == 10 && (int)MathF.Floor(pose.Z) == 10, $"Pose={pose}");
    }

    [Fact(DisplayName = "CompanionBot при стройке предпочитает рабочую точку на уровне площадки, а не яму рядом со стеной")]
    public void CompanionBot_TryFindActionPoseNear_AvoidsTrenchNearHouse()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
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

        world.SetBlock(7, 1, 10, BlockType.Air);

        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 10.5f));
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Тест-дом",
            originX: 8,
            floorY: 1,
            originZ: 8,
            steps: [new HouseBuildStep(8, 2, 11, BlockType.Wood)]);
        var method = typeof(CompanionBot).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "TryFindActionPoseNear" && candidate.GetParameters().Length == 8);
        var args = new object?[] { world, 8, 2, 11, 1, blueprint, null, null };

        var found = (bool)method.Invoke(bot, args)!;

        Assert.True(found);
        var pose = Assert.IsType<Vector3>(args[6]!);
        Assert.True(pose.Y >= 2f, $"Pose={pose}");
        Assert.False((int)MathF.Floor(pose.X) == 7 && (int)MathF.Floor(pose.Z) == 10);
    }

    [Fact(DisplayName = "CompanionBot при добыче рядом с домом избегает щели у дерева и выбирает верхнюю опору")]
    public void CompanionBot_TryFindActionPoseNear_ForGather_AvoidsTrenchNearTree()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
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

        world.SetBlock(7, 1, 10, BlockType.Air);
        world.SetBlock(8, 2, 10, BlockType.Wood);
        world.SetBlock(8, 3, 10, BlockType.Wood);

        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 10.5f));
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Тест-дом",
            originX: 9,
            floorY: 1,
            originZ: 8,
            steps: [new HouseBuildStep(10, 2, 10, BlockType.Wood)]);

        var method = typeof(CompanionBot).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "TryFindActionPoseNear" && candidate.GetParameters().Length == 8);
        var args = new object?[] { world, 8, 2, 10, 2, blueprint, null, null };

        var found = (bool)method.Invoke(bot, args)!;

        Assert.True(found);
        var pose = Assert.IsType<Vector3>(args[6]!);
        Assert.True(pose.Y >= 2f, $"Pose={pose}");
        Assert.False((int)MathF.Floor(pose.X) == 7 && (int)MathF.Floor(pose.Z) == 10);
    }

    [Fact(DisplayName = "CompanionBot начинает видимо строить дом S из доступных поверхностных ресурсов")]
    public void CompanionBot_StartsVisibleCabinSBuild_WithSurfaceResources()
    {
        var world = new WorldMap(width: 48, height: 16, depth: 48, chunkSize: 8, seed: 0);
        var playerPosition = new Vector3(8.5f, 2.02f, 8.5f);
        WarmWorld(world, playerPosition);

        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 8.5f));
        var blueprint = HouseBlueprint.CreateCabinS(world, playerPosition, new Vector3(1f, 0f, 0f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 480);
        InvokePrivate(bot, "AddStockpile", BlockType.Dirt, 128);
        InvokePrivate(bot, "AddStockpile", BlockType.Stone, 160);
        InvokePrivate(bot, "AddStockpile", BlockType.Leaves, 96);

        Assert.True(bot.Enqueue(BotCommand.BuildHouse(blueprint)));

        StepBot(bot, world, playerPosition, frames: 7000);

        Assert.Null(bot.ActiveCommand);
        Assert.Equal(BlockType.Wood, world.GetBlock(blueprint.OriginX + 3, blueprint.FloorY, blueprint.OriginZ + 3));
        Assert.Equal(BlockType.Wood, world.GetBlock(blueprint.OriginX, blueprint.FloorY + 1, blueprint.OriginZ));
        Assert.Equal(BlockType.Wood, world.GetBlock(blueprint.OriginX + 3, blueprint.FloorY + 9, blueprint.OriginZ + 3));
        Assert.Equal(BlockType.Stone, world.GetBlock(blueprint.OriginX + 3, blueprint.FloorY, blueprint.OriginZ - 2));
        Assert.Equal(BlockType.Leaves, world.GetBlock(blueprint.OriginX - 1, blueprint.FloorY + 1, blueprint.OriginZ + 1));
        Assert.Equal(BlockType.Stone, world.GetBlock(blueprint.OriginX + 5, blueprint.FloorY + 2, blueprint.OriginZ + 5));
        Assert.Equal(BlockType.Air, world.GetBlock(blueprint.OriginX + 3, blueprint.FloorY + 1, blueprint.OriginZ));
    }

    [Fact(DisplayName = "CompanionBot при строительстве дома добывает скрытый камень под грунтом и завершает стройку")]
    public void CompanionBot_BuildsHouseAfterExcavatingBuriedStone()
    {
        var world = new WorldMap(width: 48, height: 16, depth: 48, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                world.SetBlock(x, 0, z, BlockType.Dirt);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Stone);
                world.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        var playerPosition = new Vector3(8.5f, 4.02f, 8.5f);
        WarmWorld(world, playerPosition);

        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 4.02f, 8.5f));
        var blueprint = HouseBlueprint.CreateCabinS(world, playerPosition, new Vector3(1f, 0f, 0f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 480);
        InvokePrivate(bot, "AddStockpile", BlockType.Dirt, 128);
        InvokePrivate(bot, "AddStockpile", BlockType.Leaves, 96);

        Assert.True(bot.Enqueue(BotCommand.BuildHouse(blueprint)));

        StepBot(bot, world, playerPosition, frames: 11000);

        Assert.Null(bot.ActiveCommand);
        Assert.Equal(BlockType.Stone, world.GetBlock(blueprint.OriginX + 3, blueprint.FloorY, blueprint.OriginZ - 2));
        Assert.Equal(BlockType.Stone, world.GetBlock(blueprint.OriginX + 5, blueprint.FloorY + 2, blueprint.OriginZ + 5));
        Assert.Equal(BotStatus.Idle, bot.Status);
    }

    [Fact(DisplayName = "House blueprint выбирает более ровную площадку вместо крутого склона перед игроком")]
    public void HouseBlueprint_PrefersFlatterSite_OverSteepSlope()
    {
        var world = new WorldMap(width: 48, height: 16, depth: 48, chunkSize: 8, seed: 0);

        for (var x = 21; x <= 27; x++)
        {
            for (var z = 9; z <= 15; z++)
            {
                for (var y = 2; y <= 6; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Dirt);
                }

                world.SetBlock(x, 7, z, BlockType.Wood);
                world.SetBlock(x, 8, z, BlockType.Leaves);
            }
        }

        WarmWorld(world, new Vector3(12.5f, 2.02f, 12.5f));
        var blueprint = HouseBlueprint.CreateCabinS(world, new Vector3(12.5f, 2.02f, 12.5f), new Vector3(1f, 0f, 0f));

        var overlapsSteepZone = !(blueprint.OriginX + 6 < 21 || blueprint.OriginX > 27 || blueprint.OriginZ + 6 < 9 || blueprint.OriginZ > 15);
        Assert.False(overlapsSteepZone, $"Origin=({blueprint.OriginX},{blueprint.OriginZ}) FloorY={blueprint.FloorY}");
        Assert.Equal(1, blueprint.FloorY);
    }

    [Fact(DisplayName = "House blueprint готовит полянку: срезает дерево и выравнивает площадку вокруг дома")]
    public void HouseBlueprint_PreparesClearing_AndLevelsPad()
    {
        var world = new WorldMap(width: 10, height: 16, depth: 10, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 2, z, BlockType.Dirt);
            }
        }

        world.SetBlock(5, 3, 5, BlockType.Wood);
        world.SetBlock(5, 4, 5, BlockType.Wood);
        world.SetBlock(5, 5, 5, BlockType.Leaves);
        world.SetBlock(2, 2, 2, BlockType.Air);
        world.SetBlock(2, 1, 2, BlockType.Air);
        WarmWorld(world, new Vector3(4.5f, 2.02f, 4.5f));

        var blueprint = HouseBlueprint.CreateCabinS(world, new Vector3(4.5f, 2.02f, 4.5f), new Vector3(1f, 0f, 0f));

        Assert.Contains(blueprint.Steps, step => step.X == 5 && step.Y == 3 && step.Z == 5 && step.Block == BlockType.Air);
        Assert.Contains(blueprint.Steps, step => step.X == 5 && step.Y == 5 && step.Z == 5 && step.Block == BlockType.Air);
        Assert.Contains(blueprint.Steps, step => step.X == 2 && step.Y == 1 && step.Z == 2 && step.Block == BlockType.Dirt);
        Assert.Contains(blueprint.Steps, step => step.X == Math.Max(0, blueprint.OriginX - 1) && step.Y == blueprint.FloorY && step.Z == Math.Max(0, blueprint.OriginZ - 1) && step.Block == BlockType.Dirt);
        Assert.Contains(blueprint.Steps, step => step.X == blueprint.OriginX + 3 && step.Y == blueprint.FloorY && step.Z == blueprint.OriginZ + 3 && step.Block == BlockType.Wood);
    }

    [Fact(DisplayName = "House blueprint private helper-ветки по пустой площадке, out-of-bounds и foliage-only покрыты")]
    public void HouseBlueprint_PrivateSiteBranches_Work()
    {
        var tryEvaluate = typeof(HouseBlueprint).GetMethod("TryEvaluateBuildSite", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(tryEvaluate);

        var world = new WorldMap(width: 12, height: 12, depth: 12, chunkSize: 8, seed: 0);
        var args = new object?[] { world, -100, -100, new Vector3(1f, 0f, 1f), null, null };
        var result = (bool)tryEvaluate!.Invoke(null, args)!;
        Assert.False(result);

        var getGround = typeof(HouseBlueprint).GetMethod("GetGroundTopY", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(getGround);
        Assert.Equal(0, (int)getGround!.Invoke(null, [world, -1, 0])!);

        var foliageWorld = new WorldMap(width: 8, height: 8, depth: 8, chunkSize: 8, seed: 0);
        for (var y = 0; y <= 1; y++)
        {
            foliageWorld.SetBlock(3, y, 3, BlockType.Air);
        }

        foliageWorld.SetBlock(3, 2, 3, BlockType.Wood);
        foliageWorld.SetBlock(3, 3, 3, BlockType.Leaves);
        Assert.Equal(0, (int)getGround.Invoke(null, [foliageWorld, 3, 3])!);
    }

    [Fact(DisplayName = "CompanionBot не добывает ресурс из уже запланированных твёрдых блоков дома")]
    public void CompanionBot_DoesNotHarvestPlannedHouseBlocks()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        world.SetBlock(8, 2, 8, BlockType.Wood);
        world.SetBlock(2, 2, 2, BlockType.Wood);
        WarmWorld(world, new Vector3(6.5f, 2f, 6.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 6.5f));
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Тест-дом",
            originX: 7,
            floorY: 2,
            originZ: 7,
            steps: [new HouseBuildStep(8, 2, 8, BlockType.Wood)]);

        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);
        Assert.True((bool)nearest.Result!);

        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(2, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(2, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(2, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot не выбирает ресурс под собой, под игроком и внутри защищенной зоны стройки")]
    public void CompanionBot_SkipsProtectedGatherTargets()
    {
        var world = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        WarmWorld(world, new Vector3(8.5f, 2.02f, 8.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 8.5f));
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Тест-дом",
            originX: 10,
            floorY: 1,
            originZ: 10,
            steps: [new HouseBuildStep(13, 1, 13, BlockType.Wood)]);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(9.5f, 2.02f, 8.5f));
        SetPrivateField(bot, "_hasPlayerPosition", true);

        world.SetBlock(8, 1, 8, BlockType.Wood);  // под ботом
        world.SetBlock(9, 1, 8, BlockType.Wood);  // под игроком
        world.SetBlock(10, 1, 10, BlockType.Wood); // внутри keepout-зоны стройки
        world.SetBlock(20, 1, 20, BlockType.Wood); // допустимая цель
        WarmWorld(world, new Vector3(8.5f, 2.02f, 8.5f));

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);
        Assert.True((bool)nearest.Result!);

        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(20, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(1, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(20, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot не переиспользует временно заблокированную цель добычи и выбирает следующую")]
    public void CompanionBot_TryAcquireResourceTarget_SkipsBlockedTarget()
    {
        var world = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        WarmWorld(world, new Vector3(6.5f, 2.02f, 6.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 6.5f));
        world.SetBlock(8, 1, 6, BlockType.Wood);
        world.SetBlock(14, 1, 6, BlockType.Wood);
        WarmWorld(world, new Vector3(6.5f, 2.02f, 6.5f));

        var blockedTarget = CreateResourceTarget(8, 1, 6, BotResourceType.Wood);
        SetPrivateField(bot, "_currentTarget", blockedTarget);
        InvokePrivate(bot, "BlockResourceTarget", blockedTarget, 1.75f);

        var acquire = InvokePrivateWithArgs(bot, "TryAcquireResourceTarget", world, BotResourceType.Wood, null!, null!);
        Assert.True((bool)acquire.Result!);

        var target = acquire.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(14, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(1, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(6, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot снимает временную блокировку цели добычи после истечения cooldown")]
    public void CompanionBot_BlockedResourceTarget_ExpiresAfterCooldown()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        WarmWorld(world, new Vector3(6.5f, 2.02f, 6.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 6.5f));
        world.SetBlock(8, 1, 6, BlockType.Wood);
        WarmWorld(world, new Vector3(6.5f, 2.02f, 6.5f));

        var blockedTarget = CreateResourceTarget(8, 1, 6, BotResourceType.Wood);
        InvokePrivate(bot, "BlockResourceTarget", blockedTarget, 0.5f);

        var beforeExpire = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);
        Assert.False((bool)beforeExpire.Result!);

        InvokePrivate(bot, "UpdateBlockedResourceTargetCooldowns", 0.6f);

        var afterExpire = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);
        Assert.True((bool)afterExpire.Result!);
        var target = afterExpire.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(8, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot при добыче дерева удерживает фокус на текущем стволе, а не прыгает на ближайшее другое дерево")]
    public void CompanionBot_TryFindNearestResource_PrefersFocusedWoodCluster()
    {
        var world = CreateFlatWorld(40, 12);
        world.SetBlock(10, 3, 10, BlockType.Wood);
        world.SetBlock(18, 2, 18, BlockType.Wood);
        WarmWorld(world, new Vector3(18.5f, 2.02f, 18.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(18.5f, 2.02f, 18.5f));
        SetPrivateField(bot, "_resourceFocusTarget", CreateResourceTarget(10, 2, 10, BotResourceType.Wood));

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);
        Assert.True((bool)nearest.Result!);

        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(10, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(3, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(10, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot сбрасывает пустой фокус дерева и переключается на следующий доступный ствол")]
    public void CompanionBot_TryFindNearestResource_ClearsEmptyFocusedWoodCluster()
    {
        var world = CreateFlatWorld(40, 12);
        world.SetBlock(18, 2, 18, BlockType.Wood);
        WarmWorld(world, new Vector3(18.5f, 2.02f, 18.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(18.5f, 2.02f, 18.5f));
        SetPrivateField(bot, "_resourceFocusTarget", CreateResourceTarget(10, 2, 10, BotResourceType.Wood));

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);
        Assert.True((bool)nearest.Result!);

        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(18, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(2, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(18, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Null(GetPrivateField(bot, "_resourceFocusTarget"));
    }

    [Fact(DisplayName = "CompanionBot для глубокой цели умеет выбирать рабочую позу выше уровня самого блока")]
    public void CompanionBot_TryFindActionPoseNear_ForDeepTarget_AllowsHigherReachablePose()
    {
        var world = CreateFlatWorld(24, 16);
        for (var x = 9; x <= 11; x++)
        {
            for (var z = 9; z <= 11; z++)
            {
                if (x == 10 && z == 10)
                {
                    continue;
                }

                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        world.SetBlock(10, 0, 10, BlockType.Stone);
        world.SetBlock(10, 1, 10, BlockType.Air);
        world.SetBlock(11, 4, 10, BlockType.Dirt);
        WarmWorld(world, new Vector3(12.5f, 5.02f, 10.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(12.5f, 5.02f, 10.5f));
        var method = typeof(CompanionBot).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "TryFindActionPoseNear" && candidate.GetParameters().Length == 8);
        var args = new object?[] { world, 10, 0, 10, 2, null, null, null };

        var found = (bool)method.Invoke(bot, args)!;

        Assert.True(found);
        var pose = Assert.IsType<Vector3>(args[6]!);
        Assert.True(pose.Y >= 5f, $"Pose={pose}");
        Assert.True((bool)InvokePrivate(bot, "CanActOnBlockFromPose", pose, 10, 0, 10)!);
    }

    [Fact(DisplayName = "CompanionBot при добыче камня предпочитает достижимую цель вместо ближнего недостижимого дна карьера")]
    public void CompanionBot_TryFindNearestResource_PrefersReachableStoneOverDeepPitTarget()
    {
        var world = CreateFlatWorld(32, 16);
        for (var x = 12; x <= 18; x++)
        {
            for (var z = 14; z <= 18; z++)
            {
                world.SetBlock(x, 5, z, BlockType.Dirt);
            }
        }

        world.SetBlock(19, 1, 16, BlockType.Air);
        world.SetBlock(19, 0, 16, BlockType.Stone);
        world.SetBlock(22, 4, 16, BlockType.Stone);
        WarmWorld(world, new Vector3(16.5f, 6.02f, 16.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(16.5f, 6.02f, 16.5f));

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Stone, null!);
        Assert.True((bool)nearest.Result!);

        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(22, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(4, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(16, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot при отсутствии surface-камня выбирает скрытый камень под верхним слоем")]
    public void CompanionBot_TryFindNearestResource_FindsBuriedStoneCandidate()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                world.SetBlock(x, 0, z, BlockType.Dirt);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        world.SetBlock(10, 2, 10, BlockType.Stone);
        world.SetBlock(10, 3, 10, BlockType.Dirt);
        WarmWorld(world, new Vector3(8.5f, 4.02f, 10.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 4.02f, 10.5f));
        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Stone, null!);
        Assert.True((bool)nearest.Result!);

        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(10, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(2, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(10, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot для стройки дома ищет глубокий камень вокруг стройплощадки, даже если он глубже обычного buried-probe")]
    public void CompanionBot_TryFindNearestResource_BuildHouseUsesDeepBlueprintQuarry()
    {
        var world = new WorldMap(width: 48, height: 24, depth: 48, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                for (var y = 0; y <= 15; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Dirt);
                }
            }
        }

        world.SetBlock(24, 4, 20, BlockType.Stone);
        var bot = new CompanionBot(new GameConfig(), new Vector3(18.5f, 16.02f, 18.5f));
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Глубокий-камень",
            originX: 14,
            floorY: 5,
            originZ: 14,
            steps: [new HouseBuildStep(14, 5, 14, BlockType.Stone)]);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        WarmWorld(world, bot.Position);

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Stone, null!);

        Assert.True((bool)nearest.Result!);
        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(24, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(4, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(20, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot при сборе скрытого камня вскрывает грунт и завершает команду")]
    public void CompanionBot_GathersBuriedStone_ByExcavatingCover()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                world.SetBlock(x, 0, z, BlockType.Dirt);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        world.SetBlock(10, 2, 10, BlockType.Stone);
        world.SetBlock(10, 3, 10, BlockType.Dirt);
        WarmWorld(world, new Vector3(8.5f, 4.02f, 10.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 4.02f, 10.5f));
        Assert.True(bot.Enqueue(BotCommand.Gather(BotResourceType.Stone, 1)));

        StepBot(bot, world, new Vector3(14.5f, 4.02f, 10.5f), frames: 420);

        Assert.Equal(BlockType.Air, world.GetBlock(10, 2, 10));
        Assert.True(bot.GetStockpile(BlockType.Stone) >= 1);
        Assert.Null(bot.ActiveCommand);
        Assert.Equal(BotStatus.Idle, bot.Status);
    }

    [Fact(DisplayName = "CompanionBot helper поиска доступа к скрытому ресурсу покрывает success, exposed, deep и no-load ветки")]
    public void CompanionBot_TryFindBuriedResourceAccessBlock_CoversBranches()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 4.02f, 10.5f));
        var world = new WorldMap(width: 24, height: 16, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                world.SetBlock(x, 0, z, BlockType.Dirt);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        world.SetBlock(10, 2, 10, BlockType.Stone);
        WarmWorld(world, bot.Position);

        var success = InvokePrivateWithArgs(bot, "TryFindBuriedResourceAccessBlock", world, CreateResourceTarget(10, 2, 10, BotResourceType.Stone), 0, 0, 0);
        Assert.True((bool)success.Result!);
        Assert.Equal(10, (int)success.Args[2]!);
        Assert.Equal(3, (int)success.Args[3]!);
        Assert.Equal(10, (int)success.Args[4]!);

        world.SetBlock(10, 3, 10, BlockType.Air);
        var exposed = InvokePrivateWithArgs(bot, "TryFindBuriedResourceAccessBlock", world, CreateResourceTarget(10, 2, 10, BotResourceType.Stone), 0, 0, 0);
        Assert.False((bool)exposed.Result!);

        for (var y = 3; y <= 10; y++)
        {
            world.SetBlock(10, y, 10, BlockType.Dirt);
        }

        var deep = InvokePrivateWithArgs(bot, "TryFindBuriedResourceAccessBlock", world, CreateResourceTarget(10, 2, 10, BotResourceType.Stone), 0, 0, 0);
        Assert.False((bool)deep.Result!);

        var topSolidWorld = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < topSolidWorld.Width; x++)
        {
            for (var z = 0; z < topSolidWorld.Depth; z++)
            {
                for (var y = 0; y < topSolidWorld.Height; y++)
                {
                    topSolidWorld.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        for (var x = 7; x <= 9; x++)
        {
            for (var z = 7; z <= 9; z++)
            {
                for (var y = 0; y < topSolidWorld.Height; y++)
                {
                    topSolidWorld.SetBlock(x, y, z, BlockType.Dirt);
                }
            }
        }

        topSolidWorld.SetBlock(8, 6, 8, BlockType.Stone);
        WarmWorld(topSolidWorld, new Vector3(8.5f, 10.02f, 8.5f));

        var topSolid = InvokePrivateWithArgs(bot, "TryFindBuriedResourceAccessBlock", topSolidWorld, CreateResourceTarget(8, 6, 8, BotResourceType.Stone), 0, 0, 0);
        Assert.True((bool)topSolid.Result!);
        Assert.Equal(topSolidWorld.Height - 1, (int)topSolid.Args[3]!);

        var unloadedWorld = new WorldMap(width: 24, height: 16, depth: 24, chunkSize: 8, seed: 0);
        var noLoad = InvokePrivateWithArgs(bot, "TryFindBuriedResourceAccessBlock", unloadedWorld, CreateResourceTarget(10, 2, 10, BotResourceType.Stone), 0, 0, 0);
        Assert.False((bool)noLoad.Result!);
    }

    [Fact(DisplayName = "CompanionBot UpdateGatherCommand для скрытого камня сначала использует buried-recovery, а не обычный маршрут")]
    public void CompanionBot_UpdateGatherCommand_UsesBuriedRecoveryBeforeNormalRoute()
    {
        var world = new WorldMap(width: 24, height: 16, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                for (var y = 0; y <= 8; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Dirt);
                }
            }
        }

        world.SetBlock(10, 2, 10, BlockType.Stone);
        WarmWorld(world, new Vector3(8.5f, 9.02f, 10.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 9.02f, 10.5f));
        SetPrivateField(bot, "_activeCommand", BotCommand.Gather(BotResourceType.Stone, 1));
        SetPrivateField(bot, "_currentTarget", CreateResourceTarget(10, 2, 10, BotResourceType.Stone));

        InvokePrivate(bot, "UpdateGatherCommand", world, BotResourceType.Stone, 1, 1f / 30f);

        Assert.Equal(BotStatus.Gathering, bot.Status);
        Assert.Equal(BlockType.Air, world.GetBlock(10, 8, 10));
        Assert.NotNull(GetPrivateField(bot, "_currentTarget"));
    }

    [Fact(DisplayName = "CompanionBot helper поиска buried-кандидата покрывает route-score и отказы")]
    public void CompanionBot_TryScoreBuriedResourceCandidate_CoversRouteAndFailure()
    {
        var world = new WorldMap(width: 24, height: 16, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                world.SetBlock(x, 0, z, BlockType.Dirt);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        world.SetBlock(10, 2, 10, BlockType.Stone);
        world.SetBlock(10, 3, 10, BlockType.Dirt);
        WarmWorld(world, new Vector3(2.5f, 4.02f, 2.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(2.5f, 4.02f, 2.5f));
        var scoreArgs = InvokePrivateWithArgs(
            bot,
            "TryScoreBuriedResourceCandidate",
            world,
            new BotNavigationSettings(bot.Actor.ColliderHalfWidth, bot.Actor.ColliderHeight, 7f),
            CreateResourceTarget(10, 2, 10, BotResourceType.Stone),
            null!,
            5f,
            0f);
        Assert.True((bool)scoreArgs.Result!);
        Assert.True((float)scoreArgs.Args[5]! > 5f);

        world.SetBlock(10, 3, 10, BlockType.Air);
        var failArgs = InvokePrivateWithArgs(
            bot,
            "TryScoreBuriedResourceCandidate",
            world,
            new BotNavigationSettings(bot.Actor.ColliderHalfWidth, bot.Actor.ColliderHeight, 7f),
            CreateResourceTarget(10, 2, 10, BotResourceType.Stone),
            null!,
            5f,
            0f);
        Assert.False((bool)failArgs.Result!);
    }

    [Fact(DisplayName = "CompanionBot карьерный recovery умеет снимать верхний слой и временно ставить опору")]
    public void CompanionBot_BuriedResourceRecovery_CoversExcavationAndSupport()
    {
        var excavationWorld = new WorldMap(width: 24, height: 16, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < excavationWorld.Width; x++)
        {
            for (var z = 0; z < excavationWorld.Depth; z++)
            {
                for (var y = 0; y < excavationWorld.Height; y++)
                {
                    excavationWorld.SetBlock(x, y, z, BlockType.Air);
                }

                excavationWorld.SetBlock(x, 0, z, BlockType.Dirt);
                excavationWorld.SetBlock(x, 1, z, BlockType.Dirt);
                excavationWorld.SetBlock(x, 2, z, BlockType.Dirt);
                excavationWorld.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        excavationWorld.SetBlock(10, 2, 10, BlockType.Stone);
        excavationWorld.SetBlock(10, 3, 10, BlockType.Dirt);
        WarmWorld(excavationWorld, new Vector3(8.5f, 4.02f, 10.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 4.02f, 10.5f));
        var excavate = InvokePrivate(
            bot,
            "TryAdvanceBuriedResourceAccess",
            excavationWorld,
            CreateResourceTarget(10, 2, 10, BotResourceType.Stone),
            null,
            1f / 30f,
            true);
        Assert.True((bool)excavate!);
        Assert.Equal(BlockType.Air, excavationWorld.GetBlock(10, 3, 10));
        Assert.Equal(1, bot.GetStockpile(BlockType.Dirt));

        var supportWorld = CreateFlatWorld(24, 12);
        var supportBot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(supportBot, "AddStockpile", BlockType.Dirt, 1);
        SetPrivateField(supportBot, "_noPathTimer", 1f);

        Assert.True((bool)InvokePrivate(supportBot, "TryCreateTemporaryGatherAccess", supportWorld, 8, 4, 5, BlockType.Stone)!);
        Assert.Equal(BotStatus.Gathering, supportBot.Status);
        Assert.Equal(0f, Assert.IsType<float>(GetPrivateField(supportBot, "_noPathTimer")!));
        Assert.Equal(0, supportBot.GetStockpile(BlockType.Dirt));
        Assert.Contains(
            new[]
            {
                supportWorld.GetBlock(6, 1, 5),
                supportWorld.GetBlock(6, 2, 5),
                supportWorld.GetBlock(5, 1, 6),
                supportWorld.GetBlock(5, 2, 6)
            },
            block => block == BlockType.Dirt);
    }

    [Fact(DisplayName = "CompanionBot карьерные helper-ы покрывают fail-ветки опоры и excavation harvest")]
    public void CompanionBot_BuriedResourceHelpers_CoverFailureBranches()
    {
        var world = CreateFlatWorld(24, 12);
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));

        SetPrivateField(bot, "_actionCooldown", 0.1f);
        Assert.False((bool)InvokePrivate(bot, "TryCreateTemporaryGatherAccess", world, 8, 4, 5, BlockType.Stone)!);
        SetPrivateField(bot, "_actionCooldown", 0f);
        Assert.False((bool)InvokePrivate(bot, "TryCreateTemporaryGatherAccess", world, 8, 4, 5, BlockType.Stone)!);

        var target = CreateResourceTarget(8, 2, 5, BotResourceType.Stone);
        world.SetBlock(8, 2, 5, BlockType.Air);
        Assert.False((bool)InvokePrivate(bot, "TryHarvestExcavationBlock", world, target, 8, 2, 5, true, null)!);

        world.SetBlock(8, 2, 5, BlockType.Stone);
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(8.5f, 3.02f, 5.5f));
        SetPrivateField(bot, "_hasPlayerPosition", true);
        Assert.False((bool)InvokePrivate(bot, "TryHarvestExcavationBlock", world, target, 8, 2, 5, true, null)!);

        SetPrivateField(bot, "_hasPlayerPosition", false);
        Assert.True((bool)InvokePrivate(bot, "TryHarvestExcavationBlock", world, target, 8, 2, 5, true, null)!);
        Assert.Equal(1, Assert.IsType<int>(GetPrivateField(bot, "_activeGatheredAmount")!));
        Assert.Equal(1, bot.GetStockpile(BlockType.Stone));
    }

    [Fact(DisplayName = "CompanionBot buried-search покрывает unloaded-chunk, пустую колонку и выбор лучшего buried-кандидата")]
    public void CompanionBot_BuriedSearch_CoversChunkAndRankingBranches()
    {
        var world = new WorldMap(width: 48, height: 12, depth: 48, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                world.SetBlock(x, 0, z, BlockType.Dirt);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        for (var x = 8; x < 16; x++)
        {
            for (var z = 8; z < 16; z++)
            {
                for (var y = 0; y < 4; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 4.02f, 4.5f));
        WarmWorld(world, bot.Position);

        var topSolid = InvokePrivateWithArgs(bot, "TryGetLoadedTopSolidY", world, 8, 8, 0);
        Assert.False((bool)topSolid.Result!);

        world.SetBlock(10, 2, 18, BlockType.Stone);
        world.SetBlock(14, 2, 18, BlockType.Stone);
        world.SetBlock(6, 2, 18, BlockType.Stone);
        world.SetBlock(6, 3, 18, BlockType.Air); // этот кандидат "buried" уже не считается скрытым
        WarmWorld(world, new Vector3(12.5f, 4.02f, 18.5f));

        var scoredType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic);
        Assert.NotNull(scoredType);
        var list = Assert.IsAssignableFrom<System.Collections.IList>(Activator.CreateInstance(typeof(List<>).MakeGenericType(scoredType!))!);
        list.Add(Activator.CreateInstance(scoredType!, [CreateResourceTarget(6, 2, 18, BotResourceType.Stone), 1f])!);
        list.Add(Activator.CreateInstance(scoredType!, [CreateResourceTarget(10, 2, 18, BotResourceType.Stone), 2f])!);
        list.Add(Activator.CreateInstance(scoredType!, [CreateResourceTarget(14, 2, 18, BotResourceType.Stone), 3f])!);

        var selected = InvokePrivateWithArgs(bot, "TrySelectBuriedResourceCandidate", world, null!, list, null!);
        Assert.True((bool)selected.Result!);
        var target = selected.Args[3]!;
        var targetType = target.GetType();
        Assert.Equal(10, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(2, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(18, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Stone, null!);
        Assert.True((bool)nearest.Result!);
    }

    [Fact(DisplayName = "CompanionBot helper сбора buried-кандидатов пропускает unloaded и пустые loaded чанки")]
    public void CompanionBot_CollectBuriedResourceCandidates_SkipsUnloadedAndAirChunks()
    {
        var world = new WorldMap(width: 48, height: 12, depth: 48, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        for (var x = 16; x < 24; x++)
        {
            for (var z = 16; z < 24; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Dirt);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Stone);
                world.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 4.02f, 4.5f));
        world.EnsureChunksAround(bot.Position, radiusInChunks: 0);
        world.EnsureChunksAround(new Vector3(20.5f, 4.02f, 20.5f), radiusInChunks: 0);
        _ = world.RebuildDirtyChunkSurfaces(bot.Position, maxChunks: 256);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(20.5f, 4.02f, 20.5f), maxChunks: 256);

        var scoredType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic);
        Assert.NotNull(scoredType);
        var list = Assert.IsAssignableFrom<System.Collections.IList>(Activator.CreateInstance(typeof(List<>).MakeGenericType(scoredType!))!);

        InvokePrivate(bot, "CollectBuriedResourceCandidates", world, BotResourceType.Stone, null, 0, 0, 3, list);

        Assert.NotEmpty(list);
    }

    [Fact(DisplayName = "CompanionBot buried-candidate helper пропускает blocked, planned, protected и exposed цели")]
    public void CompanionBot_CollectBuriedResourceCandidates_SkipsInvalidTargets()
    {
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                for (var y = 0; y <= 3; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Dirt);
                }
            }
        }

        world.SetBlock(8, 2, 8, BlockType.Stone);
        world.SetBlock(10, 2, 8, BlockType.Stone);
        world.SetBlock(12, 2, 8, BlockType.Stone);
        world.SetBlock(14, 2, 8, BlockType.Stone);
        world.SetBlock(14, 3, 8, BlockType.Air);
        world.SetBlock(16, 2, 8, BlockType.Stone);

        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 4.02f, 8.5f));
        WarmWorld(world, new Vector3(12.5f, 4.02f, 8.5f));
        InvokePrivate(bot, "BlockResourceTarget", CreateResourceTarget(8, 2, 8, BotResourceType.Stone), 1.5f);
        SetPrivateField(bot, "_hasPlayerPosition", true);
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(12.5f, 3.02f, 8.5f));

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Buried-invalid",
            originX: 20,
            floorY: 2,
            originZ: 20,
            steps: [new HouseBuildStep(10, 2, 8, BlockType.Stone)]);

        var scoredType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic);
        Assert.NotNull(scoredType);
        var list = Assert.IsAssignableFrom<System.Collections.IList>(Activator.CreateInstance(typeof(List<>).MakeGenericType(scoredType!))!);

        InvokePrivate(bot, "CollectBuriedResourceCandidates", world, BotResourceType.Stone, blueprint, 1, 1, 1, list);

        Assert.Single(list);
        var entry = list[0]!;
        var target = entry.GetType().GetProperty("Target", BindingFlags.Instance | BindingFlags.Public)!.GetValue(entry)!;
        var targetType = target.GetType();
        Assert.Equal(16, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(2, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(8, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot helper buried-candidate сразу пропускает полностью не загруженный чанк")]
    public void CompanionBot_CollectBuriedResourceCandidates_SkipsCompletelyUnloadedChunk()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        var scoredType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic);
        Assert.NotNull(scoredType);
        var list = Assert.IsAssignableFrom<System.Collections.IList>(Activator.CreateInstance(typeof(List<>).MakeGenericType(scoredType!))!);

        InvokePrivate(bot, "CollectBuriedResourceCandidates", world, BotResourceType.Stone, null, 1, 1, 0, list);

        Assert.Empty(list);
    }

    [Fact(DisplayName = "CompanionBot helper оценки buried-кандидата возвращает false, если доступ к dig-блоку не построить")]
    public void CompanionBot_TryScoreBuriedResourceCandidate_FailsWhenNoRouteToDigBlock()
    {
        var world = new WorldMap(width: 24, height: 16, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                world.SetBlock(x, 0, z, BlockType.Dirt);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        world.SetBlock(13, 2, 5, BlockType.Stone);
        world.SetBlock(13, 3, 5, BlockType.Dirt);
        for (var x = 4; x <= 6; x++)
        {
            for (var z = 4; z <= 6; z++)
            {
                if (x == 5 && z == 5)
                {
                    continue;
                }

                world.SetBlock(x, 4, z, BlockType.Dirt);
                world.SetBlock(x, 5, z, BlockType.Dirt);
            }
        }
        WarmWorld(world, new Vector3(5.5f, 4.02f, 5.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 4.02f, 5.5f));
        var score = InvokePrivateWithArgs(
            bot,
            "TryScoreBuriedResourceCandidate",
            world,
            new BotNavigationSettings(bot.Actor.ColliderHalfWidth, bot.Actor.ColliderHeight, 7f),
            CreateResourceTarget(13, 2, 5, BotResourceType.Stone),
            null!,
            1f,
            0f);

        Assert.False((bool)score.Result!);
    }

    [Fact(DisplayName = "CompanionBot helper оценки buried-кандидата сразу принимает цель, если dig-блок уже в досягаемости")]
    public void CompanionBot_TryScoreBuriedResourceCandidate_SucceedsWhenDigBlockAlreadyReachable()
    {
        var world = new WorldMap(width: 24, height: 16, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                for (var y = 0; y <= 3; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Dirt);
                }
            }
        }

        world.SetBlock(8, 2, 8, BlockType.Stone);
        world.SetBlock(8, 3, 8, BlockType.Dirt);
        WarmWorld(world, new Vector3(8.5f, 4.02f, 8.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 4.02f, 8.5f));
        var score = InvokePrivateWithArgs(
            bot,
            "TryScoreBuriedResourceCandidate",
            world,
            new BotNavigationSettings(bot.Actor.ColliderHalfWidth, bot.Actor.ColliderHeight, 7f),
            CreateResourceTarget(8, 2, 8, BotResourceType.Stone),
            null!,
            5f,
            0f);

        Assert.True((bool)score.Result!);
        Assert.InRange((float)score.Args[5]!, 6.24f, 6.26f);
    }

    [Fact(DisplayName = "CompanionBot helper оценки buried-кандидата возвращает false, если около dig-блока вообще нет рабочей позы")]
    public void CompanionBot_TryScoreBuriedResourceCandidate_FailsWhenNoActionPoseExists()
    {
        var world = new WorldMap(width: 24, height: 16, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                for (var y = 0; y <= 3; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Dirt);
                }
            }
        }

        world.SetBlock(13, 2, 5, BlockType.Stone);
        world.SetBlock(13, 3, 5, BlockType.Dirt);
        for (var x = 11; x <= 15; x++)
        {
            for (var z = 3; z <= 7; z++)
            {
                for (var y = 4; y <= 6; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }
        WarmWorld(world, new Vector3(5.5f, 4.02f, 5.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 4.02f, 5.5f));
        var score = InvokePrivateWithArgs(
            bot,
            "TryScoreBuriedResourceCandidate",
            world,
            new BotNavigationSettings(bot.Actor.ColliderHalfWidth, bot.Actor.ColliderHeight, 7f),
            CreateResourceTarget(13, 2, 5, BotResourceType.Stone),
            null!,
            1f,
            0f);

        Assert.False((bool)score.Result!);
    }

    [Fact(DisplayName = "CompanionBot helper оценки buried-кандидата покрывает ветку: рабочая поза найдена, но stage-route до неё не строится")]
    public void CompanionBot_TryScoreBuriedResourceCandidate_FailsWhenLocalPoseExistsButStageRouteFails()
    {
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        for (var x = 4; x <= 6; x++)
        {
            for (var z = 4; z <= 6; z++)
            {
                for (var y = 0; y <= 4; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Dirt);
                }
            }
        }

        for (var x = 12; x <= 14; x++)
        {
            for (var z = 4; z <= 6; z++)
            {
                for (var y = 0; y <= 4; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Dirt);
                }
            }
        }

        world.SetBlock(13, 2, 5, BlockType.Stone);
        world.SetBlock(13, 3, 5, BlockType.Dirt);
        WarmWorld(world, new Vector3(5.5f, 5.02f, 5.5f));
        WarmWorld(world, new Vector3(13.5f, 5.02f, 5.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 5.02f, 5.5f));
        var score = InvokePrivateWithArgs(
            bot,
            "TryScoreBuriedResourceCandidate",
            world,
            new BotNavigationSettings(bot.Actor.ColliderHalfWidth, bot.Actor.ColliderHeight, 7f),
            CreateResourceTarget(13, 2, 5, BotResourceType.Stone),
            null!,
            1f,
            0f);

        Assert.False((bool)score.Result!);
    }

    [Fact(DisplayName = "CompanionBot buried-recovery после провала route сразу переходит в temporary gather access")]
    public void CompanionBot_TryAdvanceBuriedResourceAccess_UsesTemporarySupportAfterRouteFailure()
    {
        var world = new WorldMap(width: 24, height: 16, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(5, 0, 5, BlockType.Dirt);
        world.SetBlock(5, 1, 5, BlockType.Dirt);
        world.SetBlock(5, 2, 5, BlockType.Dirt);
        world.SetBlock(5, 3, 5, BlockType.Dirt);
        for (var x = 12; x <= 14; x++)
        {
            for (var z = 4; z <= 6; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Dirt);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        world.SetBlock(13, 2, 5, BlockType.Stone);
        WarmWorld(world, new Vector3(5.5f, 4.02f, 5.5f));
        WarmWorld(world, new Vector3(13.5f, 4.02f, 5.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 4.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Dirt, 1);

        Assert.True((bool)InvokePrivate(
            bot,
            "TryAdvanceBuriedResourceAccess",
            world,
            CreateResourceTarget(13, 2, 5, BotResourceType.Stone),
            null,
            1f / 30f,
            false)!);
        Assert.Equal(BotStatus.Gathering, bot.Status);
        Assert.Equal(0, bot.GetStockpile(BlockType.Dirt));
    }

    [Fact(DisplayName = "CompanionBot buried-recovery покрывает moving и arrived-fallback ветки")]
    public void CompanionBot_BuriedRecovery_CoversNavigationBranches()
    {
        var world = CreateFlatWorld(32, 16);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Dirt);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        world.SetBlock(13, 2, 5, BlockType.Stone);
        world.SetBlock(13, 3, 5, BlockType.Dirt);
        WarmWorld(world, new Vector3(5.5f, 4.02f, 5.5f));

        var movingBot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 4.02f, 5.5f));
        Assert.True((bool)InvokePrivate(
            movingBot,
            "TryAdvanceBuriedResourceAccess",
            world,
            CreateResourceTarget(13, 2, 5, BotResourceType.Stone),
            null,
            1f / 30f,
            false)!);
        Assert.Equal(BotStatus.Gathering, movingBot.Status);

        var arrivedBot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 4.02f, 5.5f));
        InvokePrivate(arrivedBot, "AddStockpile", BlockType.Dirt, 1);
        SetPrivateField(arrivedBot, "_navigationGoal", CreateNavigationGoal("GatherAction", 13, 3, 5, 6, null));
        SetPrivateField(arrivedBot, "_navigationWaypoints", new[] { arrivedBot.Position });
        SetPrivateField(arrivedBot, "_navigationWaypointIndex", 0);
        Assert.True((bool)InvokePrivate(
            arrivedBot,
            "TryAdvanceBuriedResourceAccess",
            world,
            CreateResourceTarget(13, 2, 5, BotResourceType.Stone),
            null,
            1f / 30f,
            false)!);
        Assert.Equal(BotStatus.Gathering, arrivedBot.Status);
    }

    [Fact(DisplayName = "CompanionBot buried-recovery покрывает blocked-move ветку и уходит в temporary support fallback")]
    public void CompanionBot_TryAdvanceBuriedResourceAccess_CoversBlockedMoveFallback()
    {
        var world = CreateFlatWorld(32, 16);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Dirt);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        world.SetBlock(13, 2, 5, BlockType.Stone);
        world.SetBlock(13, 3, 5, BlockType.Dirt);
        WarmWorld(world, new Vector3(5.5f, 4.02f, 5.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 4.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Dirt, 1);
        SetPrivateField(bot, "_playerCollisionProbe", new Func<Vector3, bool>(_ => true));
        SetPrivateField(bot, "_stuckTime", 2.3f);

        Assert.True((bool)InvokePrivate(
            bot,
            "TryAdvanceBuriedResourceAccess",
            world,
            CreateResourceTarget(13, 2, 5, BotResourceType.Stone),
            null,
            1f / 30f,
            false)!);
        Assert.Equal(BotStatus.Gathering, bot.Status);
    }

    [Fact(DisplayName = "CompanionBot buried-recovery сразу возвращает false для уже открытой buried-цели")]
    public void CompanionBot_TryAdvanceBuriedResourceAccess_FailsForExposedTarget()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(8, 2, 8, BlockType.Stone);
        world.SetBlock(8, 3, 8, BlockType.Air);
        WarmWorld(world, new Vector3(6.5f, 2.02f, 8.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 8.5f));

        Assert.False((bool)InvokePrivate(
            bot,
            "TryAdvanceBuriedResourceAccess",
            world,
            CreateResourceTarget(8, 2, 8, BotResourceType.Stone),
            null,
            1f / 30f,
            false)!);
    }

    [Fact(DisplayName = "CompanionBot buried-recovery возвращает false, если route не строится и нечем поставить временную опору")]
    public void CompanionBot_TryAdvanceBuriedResourceAccess_FailsWithoutSupportMaterial()
    {
        var world = new WorldMap(width: 24, height: 16, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(5, 0, 5, BlockType.Dirt);
        world.SetBlock(5, 1, 5, BlockType.Dirt);
        world.SetBlock(5, 2, 5, BlockType.Dirt);
        world.SetBlock(5, 3, 5, BlockType.Dirt);
        for (var x = 12; x <= 14; x++)
        {
            for (var z = 4; z <= 6; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Dirt);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
            }
        }

        world.SetBlock(13, 2, 5, BlockType.Stone);
        WarmWorld(world, new Vector3(5.5f, 4.02f, 5.5f));
        WarmWorld(world, new Vector3(13.5f, 4.02f, 5.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 4.02f, 5.5f));

        Assert.False((bool)InvokePrivate(
            bot,
            "TryAdvanceBuriedResourceAccess",
            world,
            CreateResourceTarget(13, 2, 5, BotResourceType.Stone),
            null,
            1f / 30f,
            false)!);
    }

    [Fact(DisplayName = "CompanionBot карьерный harvest не увеличивает gathered amount, если добыт не целевой тип ресурса")]
    public void CompanionBot_TryHarvestExcavationBlock_DoesNotCountNonTargetResource()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(8, 2, 5, BlockType.Dirt);
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));

        Assert.True((bool)InvokePrivate(
            bot,
            "TryHarvestExcavationBlock",
            world,
            CreateResourceTarget(8, 2, 5, BotResourceType.Stone),
            8,
            2,
            5,
            true,
            null)!);
        Assert.Equal(0, Assert.IsType<int>(GetPrivateField(bot, "_activeGatheredAmount")!));
        Assert.Equal(1, bot.GetStockpile(BlockType.Dirt));
    }

    [Fact(DisplayName = "CompanionBot карьерный harvest под action cooldown сразу отказывает")]
    public void CompanionBot_TryHarvestExcavationBlock_FailsDuringCooldown()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(8, 2, 5, BlockType.Stone);
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        SetPrivateField(bot, "_actionCooldown", 0.1f);

        Assert.False((bool)InvokePrivate(
            bot,
            "TryHarvestExcavationBlock",
            world,
            CreateResourceTarget(8, 2, 5, BotResourceType.Stone),
            8,
            2,
            5,
            true,
            null)!);
    }

    [Fact(DisplayName = "CompanionBot карьерный harvest не увеличивает gathered amount, если вызван без countForActiveCommand")]
    public void CompanionBot_TryHarvestExcavationBlock_DoesNotCountWhenFlagIsFalse()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(8, 2, 5, BlockType.Stone);
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));

        Assert.True((bool)InvokePrivate(
            bot,
            "TryHarvestExcavationBlock",
            world,
            CreateResourceTarget(8, 2, 5, BotResourceType.Stone),
            8,
            2,
            5,
            false,
            null)!);
        Assert.Equal(0, Assert.IsType<int>(GetPrivateField(bot, "_activeGatheredAmount")!));
        Assert.Equal(1, bot.GetStockpile(BlockType.Stone));
    }

    [Fact(DisplayName = "CompanionBot карьерный harvest отказывает и для protected keepout blueprint")]
    public void CompanionBot_TryHarvestExcavationBlock_FailsForProtectedBlueprintTarget()
    {
        var world = CreateFlatWorld(32, 12);
        world.SetBlock(22, 2, 22, BlockType.Stone);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Protected-quarry",
            originX: 20,
            floorY: 2,
            originZ: 20,
            steps: []);
        var bot = new CompanionBot(new GameConfig(), new Vector3(18.5f, 2.02f, 22.5f));

        Assert.False((bool)InvokePrivate(
            bot,
            "TryHarvestExcavationBlock",
            world,
            CreateResourceTarget(22, 2, 22, BotResourceType.Stone),
            22,
            2,
            22,
            true,
            blueprint)!);
    }

    [Fact(DisplayName = "CompanionBot в focused-поиске пропускает blocked/keepout цели и выбирает лучший оставшийся вариант")]
    public void CompanionBot_TryFindNearestResource_FocusedSearchSkipsBlockedAndProtectedCandidates()
    {
        var world = CreateFlatWorld(40, 12);
        world.SetBlock(10, 3, 10, BlockType.Wood);
        world.SetBlock(11, 3, 10, BlockType.Wood);
        world.SetBlock(12, 3, 10, BlockType.Wood);
        world.SetBlock(12, 4, 10, BlockType.Wood);
        world.SetBlock(12, 5, 10, BlockType.Wood);
        WarmWorld(world, new Vector3(18.5f, 2.02f, 18.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(18.5f, 2.02f, 18.5f));
        SetPrivateField(bot, "_resourceFocusTarget", CreateResourceTarget(10, 2, 10, BotResourceType.Wood));
        InvokePrivate(bot, "BlockResourceTarget", CreateResourceTarget(10, 3, 10, BotResourceType.Wood), 1.5f);
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(12.5f, 4.02f, 10.5f));
        SetPrivateField(bot, "_hasPlayerPosition", true);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Planned-solid",
            originX: 30,
            floorY: 2,
            originZ: 30,
            steps: [new HouseBuildStep(11, 3, 10, BlockType.Wood)])));

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);
        Assert.True((bool)nearest.Result!);

        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(12, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(4, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(10, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot корректно обновляет blacklist целей добычи и не сокращает блокировку меньшим значением")]
    public void CompanionBot_BlockedResourceTarget_MaintainsLongestCooldown()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 6.5f));
        var blockedTarget = CreateResourceTarget(8, 1, 6, BotResourceType.Wood);

        InvokePrivate(bot, "BlockResourceTarget", blockedTarget, 0f);
        var blockedTargets = Assert.IsAssignableFrom<System.Collections.IDictionary>(GetPrivateField(bot, "_blockedResourceTargets")!);
        Assert.Empty(blockedTargets);

        InvokePrivate(bot, "BlockResourceTarget", blockedTarget, 1.5f);
        InvokePrivate(bot, "BlockResourceTarget", blockedTarget, 0.75f);
        InvokePrivate(bot, "UpdateBlockedResourceTargetCooldowns", 0.25f);

        Assert.Single(blockedTargets);
        Assert.Equal(1.25f, Assert.IsType<float>(blockedTargets[blockedTarget]!), 0.001f);
    }

    [Fact(DisplayName = "CompanionBot пакетной добычей не сносит опору под собой")]
    public void CompanionBot_HarvestCluster_DoesNotMineSupportUnderSelf()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        WarmWorld(world, new Vector3(6.5f, 2.02f, 6.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 6.5f));
        world.SetBlock(6, 1, 6, BlockType.Wood);
        world.SetBlock(7, 1, 6, BlockType.Wood);
        WarmWorld(world, new Vector3(6.5f, 2.02f, 6.5f));

        var targetType = typeof(CompanionBot).GetNestedType("ResourceTarget", BindingFlags.NonPublic);
        Assert.NotNull(targetType);
        var target = Activator.CreateInstance(targetType!, [7, 1, 6, BotResourceType.Wood])!;

        Assert.True((bool)InvokePrivate(bot, "TryHarvestTarget", world, target, false)!);
        Assert.Equal(BlockType.Wood, world.GetBlock(6, 1, 6));
        Assert.Equal(BlockType.Air, world.GetBlock(7, 1, 6));
    }

    [Fact(DisplayName = "CompanionBot умеет ставить активную и ожидающую команды, а затем сбрасывать их")]
    public void CompanionBot_QueuesCommandsAndCancels()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2f, 4.5f));
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Тест-дом",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps: [new HouseBuildStep(5, 2, 5, BlockType.Wood)]);

        Assert.True(bot.Enqueue(BotCommand.Gather(BotResourceType.Wood, 16)));
        Assert.True(bot.Enqueue(BotCommand.BuildHouse(blueprint)));
        Assert.False(bot.Enqueue(BotCommand.Gather(BotResourceType.Stone, 1)));

        Assert.Contains("Сбор Дерево: 0/16", bot.GetActiveSummary(), StringComparison.Ordinal);
        Assert.Equal("Тест-дом", bot.GetQueuedSummary());
        Assert.Equal("дер:0 кам:0 зем:0 лист:0", bot.GetStockpileSummary());

        bot.CancelAll();

        Assert.Null(bot.ActiveCommand);
        Assert.Null(bot.QueuedCommand);
        Assert.Equal(BotStatus.Idle, bot.Status);
        Assert.Equal("нет", bot.GetActiveSummary());
        Assert.Equal("нет", bot.GetQueuedSummary());
    }

    [Fact(DisplayName = "CompanionBot собирает ресурс с поверхности и завершает команду")]
    public void CompanionBot_GathersResource_FromSurface()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        world.SetBlock(6, 2, 6, BlockType.Wood);
        WarmWorld(world, new Vector3(6.5f, 2f, 6.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 6.5f));
        Assert.True(bot.Enqueue(BotCommand.Gather(BotResourceType.Wood, 1)));

        StepBot(bot, world, new Vector3(12.5f, 2.02f, 12.5f), frames: 240);

        Assert.Equal(BlockType.Air, world.GetBlock(6, 2, 6));
        Assert.Equal(1, bot.GetStockpile(BlockType.Wood));
        Assert.Null(bot.ActiveCommand);
        Assert.Equal(BotStatus.Idle, bot.Status);
    }

    [Fact(DisplayName = "CompanionBot при строительстве добирает недостающий ресурс и ставит блок")]
    public void CompanionBot_BuildsAfterAutoGatheringMissingResource()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        world.SetBlock(4, 2, 4, BlockType.Wood);
        WarmWorld(world, new Vector3(4.5f, 2f, 4.5f));

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Тест-дом",
            originX: 8,
            floorY: 2,
            originZ: 8,
            steps:
            [
                new HouseBuildStep(8, 3, 8, BlockType.Air),
                new HouseBuildStep(8, 2, 8, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        Assert.True(bot.Enqueue(BotCommand.BuildHouse(blueprint)));

        StepBot(bot, world, new Vector3(10.5f, 2.02f, 10.5f), frames: 420);

        Assert.Equal(BlockType.Wood, world.GetBlock(8, 2, 8));
        Assert.Equal(0, bot.GetStockpile(BlockType.Wood));
        Assert.Null(bot.ActiveCommand);
    }

    [Fact(DisplayName = "CompanionBot показывает NoPath если целевого ресурса нет и умеет следовать за игроком")]
    public void CompanionBot_NoPathAndFollowStates_Work()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        WarmWorld(world, new Vector3(4.5f, 2f, 4.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        Assert.True(bot.Enqueue(BotCommand.Gather(BotResourceType.Wood, 1)));
        Assert.Equal(0, bot.BuildStepIndex);
        Assert.Equal(0, bot.GatheredAmount);
        Assert.NotNull(bot.Stockpile);

        StepBot(bot, world, new Vector3(18.5f, 2.02f, 18.5f), frames: 20);
        Assert.Equal(BotStatus.NoPath, bot.Status);

        bot.CancelAll();
        bot.Update(world, new Vector3(18.5f, 2.02f, 18.5f), new Vector3(1f, 0f, 0f), 1f / 30f);
        Assert.Equal(BotStatus.Moving, bot.Status);

        StepBot(bot, world, new Vector3(18.5f, 2.02f, 18.5f), 180);
        Assert.Contains(bot.Status, new[] { BotStatus.Idle, BotStatus.Moving });
        Assert.True((bool)InvokePrivate(bot, "IsSafeFollowPose", bot.Position, new Vector3(18.5f, 2.02f, 18.5f), new Vector3(1f, 0f, 0f))!);
    }

    [Fact(DisplayName = "CompanionBot в режиме follow отлипает от игрока и не лезет прямо в камеру")]
    public void CompanionBot_Follow_KeepsOutOfPlayerView()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        WarmWorld(world, new Vector3(10.5f, 2f, 10.5f));

        var playerPosition = new Vector3(10.5f, 2.02f, 10.5f);
        var playerLook = new Vector3(0f, 0f, 1f);
        var bot = new CompanionBot(new GameConfig(), new Vector3(10.5f, 2.02f, 11.4f));

        Func<Vector3, bool> playerCollisionProbe = pose =>
        {
            var min = new Vector3(pose.X - bot.Actor.ColliderHalfWidth, pose.Y, pose.Z - bot.Actor.ColliderHalfWidth);
            var max = new Vector3(pose.X + bot.Actor.ColliderHalfWidth, pose.Y + bot.Actor.ColliderHeight, pose.Z + bot.Actor.ColliderHalfWidth);
            var playerMin = new Vector3(playerPosition.X - 0.3f, playerPosition.Y, playerPosition.Z - 0.3f);
            var playerMax = new Vector3(playerPosition.X + 0.3f, playerPosition.Y + 1.8f, playerPosition.Z + 0.3f);
            return min.X <= playerMax.X && max.X >= playerMin.X
                && min.Y <= playerMax.Y && max.Y >= playerMin.Y
                && min.Z <= playerMax.Z && max.Z >= playerMin.Z;
        };

        bot.Update(world, playerPosition, playerLook, 1f / 30f, playerCollisionProbe);

        StepBot(bot, world, playerPosition, playerLook, 180, playerCollisionProbe);

        Assert.Contains(bot.Status, new[] { BotStatus.Idle, BotStatus.Moving });
        Assert.True(Vector3.Distance(bot.Position, playerPosition) > 1.15f, $"Bot={bot.Position}");
    }

    [Fact(DisplayName = "CompanionBot в follow не телепортируется, даже если оказался слишком близко к игроку")]
    public void CompanionBot_UpdateFollowPlayer_DoesNotTeleportWhenTooClose()
    {
        var world = CreateFlatWorld(32, 12);
        var playerPosition = new Vector3(10.5f, 2.02f, 10.5f);
        var playerLook = Vector3.UnitZ;
        var bot = new CompanionBot(new GameConfig(), new Vector3(10.5f, 2.02f, 11.1f));
        var before = bot.Position;

        InvokePrivate(bot, "UpdateFollowPlayer", world, playerPosition, playerLook, 1f / 30f);

        Assert.Contains(bot.Status, new[] { BotStatus.Idle, BotStatus.Moving });
        Assert.True(Vector3.Distance(before, bot.Position) < 0.5f, $"Before={before} After={bot.Position}");
        Assert.True(Vector3.Distance(bot.Position, playerPosition) < 4.5f, $"Bot={bot.Position}");
    }

    [Fact(DisplayName = "CompanionBot выбирает сторону follow по текущему боку и fallback-стрейфу")]
    public void CompanionBot_GetPreferredFollowSideSign_CoversSideAndFallbackBranches()
    {
        var playerPosition = new Vector3(10.5f, 2.02f, 10.5f);
        var playerLook = Vector3.UnitZ;

        var rightSideBot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 10.5f));
        Assert.Equal(1f, (float)InvokePrivate(rightSideBot, "GetPreferredFollowSideSign", playerPosition, playerLook)!);

        var fallbackBot = new CompanionBot(new GameConfig(), playerPosition);
        SetPrivateField(fallbackBot, "_strafeSign", -1);
        Assert.Equal(-1f, (float)InvokePrivate(fallbackBot, "GetPreferredFollowSideSign", playerPosition, playerLook)!);
    }

    [Fact(DisplayName = "CompanionBot считает follow-позу комфортной только если она и безопасна, и близка к цели")]
    public void CompanionBot_IsComfortableFollowPose_RequiresSafeAndNearDesired()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(10.5f, 2.02f, 4.9f));
        var playerPosition = new Vector3(10.5f, 2.02f, 10.5f);
        var playerLook = Vector3.UnitZ;
        var desired = new Vector3(10.5f, 2.02f, 5.7f);

        Assert.True((bool)InvokePrivate(bot, "IsComfortableFollowPose", bot.Position, desired, playerPosition, playerLook)!);
        Assert.False((bool)InvokePrivate(bot, "IsComfortableFollowPose", new Vector3(10.5f, 2.02f, 12.2f), desired, playerPosition, playerLook)!);
    }

    [Fact(DisplayName = "CompanionBot запрещает follow-маршрут через игрока и через передний конус обзора")]
    public void CompanionBot_IsFollowRouteCellAllowed_BlocksNearAndFrontCells()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        var playerPosition = new Vector3(10.5f, 2.02f, 10.5f);
        var playerLook = Vector3.UnitZ;

        Assert.False((bool)InvokePrivate(bot, "IsFollowRouteCellAllowed", new BotNavigationCell(10, 2, 10), playerPosition, playerLook)!);
        Assert.False((bool)InvokePrivate(bot, "IsFollowRouteCellAllowed", new BotNavigationCell(10, 2, 12), playerPosition, playerLook)!);
        Assert.True((bool)InvokePrivate(bot, "IsFollowRouteCellAllowed", new BotNavigationCell(7, 2, 9), playerPosition, playerLook)!);
    }

    [Fact(DisplayName = "CompanionBot private helper-ветки по summary, сбору и запасам покрыты")]
    public void CompanionBot_PrivateSummaryAndResourceBranches_Work()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        world.SetBlock(6, 2, 6, BlockType.Wood);
        WarmWorld(world, new Vector3(6.5f, 2f, 6.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        var blueprint = new HouseBlueprint(HouseTemplateKind.CabinS, "Дом-тест", 4, 2, 4, [new HouseBuildStep(5, 2, 5, BlockType.Wood)]);

        Assert.True(bot.Enqueue(BotCommand.BuildHouse(blueprint)));
        Assert.Contains("Стройка Дом-тест", bot.GetActiveSummary(), StringComparison.Ordinal);

        SetPrivateField(bot, "_queuedCommand", BotCommand.Gather(BotResourceType.Stone, 3));
        Assert.Equal("Сбор Камень 3", bot.GetQueuedSummary());

        SetPrivateField(bot, "_activeCommand", new BotCommand((BotCommandKind)99, BotResourceType.Wood, 0, null));
        Assert.Equal("нет", bot.GetActiveSummary());
        SetPrivateField(bot, "_queuedCommand", new BotCommand((BotCommandKind)99, BotResourceType.Wood, 0, null));
        Assert.Equal("нет", bot.GetQueuedSummary());

        SetPrivateField(bot, "_activeCommand", BotCommand.Gather(BotResourceType.Wood, 1));
        SetPrivateField(bot, "_queuedCommand", null);
        SetPrivateField(bot, "_activeGatheredAmount", 1);
        InvokePrivate(bot, "UpdateGatherCommand", world, BotResourceType.Wood, 1, 1f / 30f);
        Assert.Null(bot.ActiveCommand);

        SetPrivateField(bot, "_activeCommand", new BotCommand(BotCommandKind.BuildHouse, BotResourceType.Wood, 0, null));
        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);
        Assert.Null(bot.ActiveCommand);

        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 0);
        Assert.Equal(0, bot.GetStockpile(BlockType.Wood));
        Assert.True((bool)InvokePrivate(bot, "TryConsumeStockpile", BlockType.Wood, 0)!);
        Assert.False((bool)InvokePrivate(bot, "TryConsumeStockpile", BlockType.Wood, 1)!);

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);
        Assert.True((bool)nearest.Result!);
        var target = nearest.Args[2];

        SetPrivateField(bot, "_actionCooldown", 0.2f);
        Assert.False((bool)InvokePrivate(bot, "TryHarvestTarget", world, target!, false)!);

        SetPrivateField(bot, "_actionCooldown", 0f);
        world.SetBlock(6, 2, 6, BlockType.Air);
        Assert.False((bool)InvokePrivate(bot, "TryHarvestTarget", world, target!, false)!);

        world.SetBlock(6, 2, 6, BlockType.Wood);
        WarmWorld(world, new Vector3(6.5f, 2f, 6.5f));
        bot.Actor.SetPose(new Vector3(1.5f, 2.02f, 1.5f), new Vector3(1f, 0f, 0f));
        Assert.False((bool)InvokePrivate(bot, "TryHarvestTarget", world, target!, false)!);
        bot.Actor.SetPose(new Vector3(5.5f, 2.02f, 5.5f), new Vector3(1f, 0f, 0f));
        Assert.True((bool)InvokePrivate(bot, "TryHarvestTarget", world, target!, false)!);
        Assert.Equal(1, bot.GetStockpile(BlockType.Wood));
        Assert.True((bool)InvokePrivate(bot, "TryConsumeStockpile", BlockType.Wood, 1)!);

        world.SetBlock(6, 2, 6, BlockType.Wood);
        WarmWorld(world, new Vector3(6.5f, 2f, 6.5f));
        var acquireFirst = InvokePrivateWithArgs(bot, "TryAcquireResourceTarget", world, BotResourceType.Wood, null!, null!);
        Assert.True((bool)acquireFirst.Result!);
        var acquireExisting = InvokePrivateWithArgs(bot, "TryAcquireResourceTarget", world, BotResourceType.Wood, null!, null!);
        Assert.True((bool)acquireExisting.Result!);

        SetPrivateField(bot, "_currentTarget", null);
        SetPrivateField(bot, "_retargetCooldown", 0.2f);
        var acquireCooldown = InvokePrivateWithArgs(bot, "TryAcquireResourceTarget", world, BotResourceType.Wood, null!, null!);
        Assert.False((bool)acquireCooldown.Result!);

        var validateCurrent = (bool)InvokePrivate(bot, "IsResourceTargetValid", world, target!)!;
        Assert.True(validateCurrent);
        world.SetBlock(6, 2, 6, BlockType.Air);
        Assert.False((bool)InvokePrivate(bot, "IsResourceTargetValid", world, target!)!);
    }

    [Fact(DisplayName = "CompanionBot ищет актуальный камень по загруженным чанкам, даже если surface-cache еще грязная")]
    public void CompanionBot_TryFindNearestResource_FindsExposedStoneFromLoadedChunks_WhenSurfaceCacheIsDirty()
    {
        var world = CreateFlatWorld(32, 12);
        WarmWorld(world, new Vector3(8.5f, 2.02f, 8.5f));

        world.SetBlock(10, 2, 8, BlockType.Stone);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(8.5f, 2.02f, 8.5f), maxChunks: 64);
        world.SetBlock(10, 3, 8, BlockType.Air);

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 8.5f));

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Stone, null!);

        Assert.True((bool)nearest.Result!);
        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(10, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(2, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(8, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot при стройке дома предпочитает ближний скрытый камень на стройплощадке, а не дальний fallback-кандидат")]
    public void CompanionBot_TryFindNearestResource_BuildHousePrefersNearbyBuriedStone()
    {
        var world = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        WarmWorld(world, new Vector3(8.5f, 2.02f, 8.5f));

        world.SetBlock(10, 2, 8, BlockType.Stone);
        world.SetBlock(12, 2, 8, BlockType.Stone);
        world.SetBlock(14, 2, 8, BlockType.Stone);
        world.SetBlock(16, 2, 8, BlockType.Stone);

        world.SetBlock(14, 3, 8, BlockType.Dirt);
        world.SetBlock(14, 1, 8, BlockType.Dirt);
        world.SetBlock(13, 2, 8, BlockType.Dirt);
        world.SetBlock(15, 2, 8, BlockType.Dirt);
        world.SetBlock(14, 2, 7, BlockType.Dirt);
        world.SetBlock(14, 2, 9, BlockType.Dirt);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Keepout",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(10, 2, 8, BlockType.Stone)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 8.5f));
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        SetPrivateField(bot, "_hasPlayerPosition", true);
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(12.5f, 3.02f, 8.5f));

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Stone, null!);

        Assert.True((bool)nearest.Result!);
        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(14, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(2, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(8, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot loaded fallback для дерева пропускает planned/protected цели и берёт валидную")]
    public void CompanionBot_TryFindNearestResource_LoadedFallbackForWood_SkipsPlannedAndProtected()
    {
        var world = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(10, 2, 8, BlockType.Wood);
        world.SetBlock(12, 2, 8, BlockType.Wood);
        world.SetBlock(14, 2, 8, BlockType.Wood);
        world.EnsureChunksAround(new Vector3(8.5f, 2.02f, 8.5f), radiusInChunks: 2);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Fallback-wood",
            originX: 20,
            floorY: 2,
            originZ: 20,
            steps: [new HouseBuildStep(10, 2, 8, BlockType.Wood)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 8.5f));
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        SetPrivateField(bot, "_hasPlayerPosition", true);
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(12.5f, 3.02f, 8.5f));

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);

        Assert.True((bool)nearest.Result!);
        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(14, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(2, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(8, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot при стройке дома ищет дерево в более широком лесном поясе вокруг blueprint")]
    public void CompanionBot_TryFindNearestResource_BuildHouseUsesBlueprintForestry()
    {
        var world = new WorldMap(width: 96, height: 16, depth: 96, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Stone);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                for (var y = 2; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        // У дерева верх закрыт листвой, поэтому более полезен целевой поиск по лесному поясу, а не локальный чистый двор.
        world.SetBlock(72, 2, 48, BlockType.Wood);
        world.SetBlock(72, 3, 48, BlockType.Wood);
        world.SetBlock(72, 4, 48, BlockType.Wood);
        world.SetBlock(72, 5, 48, BlockType.Leaves);
        world.SetBlock(71, 5, 48, BlockType.Leaves);
        world.SetBlock(73, 5, 48, BlockType.Leaves);
        world.SetBlock(72, 5, 47, BlockType.Leaves);
        world.SetBlock(72, 5, 49, BlockType.Leaves);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Лесной-дом",
            originX: 44,
            floorY: 2,
            originZ: 44,
            steps: [new HouseBuildStep(44, 2, 44, BlockType.Wood)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(32.5f, 2.02f, 48.5f));
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));

        world.EnsureChunksAround(bot.Position, radiusInChunks: 4);
        world.EnsureChunksAround(blueprint.Center, radiusInChunks: 4);
        _ = world.RebuildDirtyChunkSurfaces(bot.Position, maxChunks: 256);
        _ = world.RebuildDirtyChunkSurfaces(blueprint.Center, maxChunks: 256);

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);

        Assert.True((bool)nearest.Result!);
        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(72, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.InRange((int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!, 2, 4);
        Assert.Equal(48, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot при стройке дома ищет дерево по загруженным chunk вокруг blueprint, даже если лес уже вне forestry-пояса и локального радиуса бота")]
    public void CompanionBot_TryFindNearestResource_BuildHouseUsesBlueprintForestryLoadedFallback()
    {
        var world = new WorldMap(width: 128, height: 16, depth: 128, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Stone);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                for (var y = 2; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(96, 2, 48, BlockType.Wood);
        world.SetBlock(96, 3, 48, BlockType.Wood);
        world.SetBlock(96, 4, 48, BlockType.Wood);
        world.SetBlock(96, 5, 48, BlockType.Leaves);
        world.SetBlock(95, 5, 48, BlockType.Leaves);
        world.SetBlock(97, 5, 48, BlockType.Leaves);
        world.SetBlock(96, 5, 47, BlockType.Leaves);
        world.SetBlock(96, 5, 49, BlockType.Leaves);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Лесной-дом-loaded",
            originX: 44,
            floorY: 2,
            originZ: 44,
            steps: [new HouseBuildStep(44, 2, 44, BlockType.Wood)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(32.5f, 2.02f, 48.5f));
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));

        world.EnsureChunksAround(bot.Position, radiusInChunks: 4);
        world.EnsureChunksAround(blueprint.Center, radiusInChunks: 8);
        _ = world.RebuildDirtyChunkSurfaces(bot.Position, maxChunks: 512);
        _ = world.RebuildDirtyChunkSurfaces(blueprint.Center, maxChunks: 512);

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);

        Assert.True((bool)nearest.Result!);
        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(96, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.InRange((int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!, 2, 4);
        Assert.Equal(48, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot после пустого ближнего поиска переходит на расширенный surface-радиус")]
    public void CompanionBot_TryFindNearestResource_UsesExtendedSurfaceRadius()
    {
        var world = new WorldMap(width: 80, height: 12, depth: 80, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Dirt);
                for (var y = 1; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(42, 1, 12, BlockType.Wood);
        WarmWorld(world, new Vector3(12.5f, 1.02f, 12.5f));
        WarmWorld(world, new Vector3(42.5f, 1.02f, 12.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(12.5f, 1.02f, 12.5f));

        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);

        Assert.True((bool)nearest.Result!);
        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(42, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(1, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(12, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot loaded fallback реально возвращает ресурс, если surface-cache ещё пустой")]
    public void CompanionBot_TryFindNearestResource_LoadedFallback_ReturnsTarget_WhenSurfaceCacheMissing()
    {
        var world = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Stone);
                for (var y = 1; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(10, 1, 8, BlockType.Wood);
        world.EnsureChunksAround(new Vector3(8.5f, 2.02f, 8.5f), radiusInChunks: 2);

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 8.5f));
        var nearest = InvokePrivateWithArgs(bot, "TryFindNearestResource", world, BotResourceType.Wood, null!);

        Assert.True((bool)nearest.Result!);
        var target = nearest.Args[2]!;
        var targetType = target.GetType();
        Assert.Equal(10, (int)targetType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(1, (int)targetType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
        Assert.Equal(8, (int)targetType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(target)!);
    }

    [Fact(DisplayName = "CompanionBot не спамит поиском ресурса на каждом тике, пока действует miss-cooldown")]
    public void CompanionBot_ExecuteGatherObjective_MissingResource_RespectsRetryCooldown()
    {
        var world = CreateFlatWorld(24, 12);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Dirt);
                for (var y = 1; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        WarmWorld(world, new Vector3(6.5f, 2.02f, 6.5f));

        var log = new List<string>();
        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 6.5f), log.Add);

        var first = (bool)InvokePrivate(bot, "ExecuteGatherObjective", world, BotResourceType.Stone, 1, 1f / 30f, false)!;
        var second = (bool)InvokePrivate(bot, "ExecuteGatherObjective", world, BotResourceType.Stone, 1, 1f / 30f, false)!;

        Assert.False(first);
        Assert.False(second);
        Assert.Equal(1, log.Count(entry => entry.Contains("target-search-empty resource=Stone", StringComparison.Ordinal)));
        Assert.Equal(1, log.Count(entry => entry.Contains("gather-target-missing resource=Stone", StringComparison.Ordinal)));
        Assert.True(Assert.IsType<float>(GetPrivateField(bot, "_retargetCooldown")!) >= 0.8f);
    }

    [Fact(DisplayName = "CompanionBot при стройке дома держит более длинный miss-cooldown для камня и не дёргает тяжёлый поиск каждый тик")]
    public void CompanionBot_ExecuteGatherObjective_BuildHouseStoneMissing_UsesLongerRetryCooldown()
    {
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }

                for (var y = 0; y <= 8; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Dirt);
                }
            }
        }

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Нет-камня",
            originX: 10,
            floorY: 4,
            originZ: 10,
            steps: [new HouseBuildStep(10, 4, 10, BlockType.Stone)]);
        var log = new List<string>();
        var bot = new CompanionBot(new GameConfig(), new Vector3(12.5f, 9.02f, 12.5f), log.Add);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        WarmWorld(world, bot.Position);

        var first = (bool)InvokePrivate(bot, "ExecuteGatherObjective", world, BotResourceType.Stone, 1, 1f / 30f, false)!;
        var second = (bool)InvokePrivate(bot, "ExecuteGatherObjective", world, BotResourceType.Stone, 1, 1f / 30f, false)!;

        Assert.False(first);
        Assert.False(second);
        Assert.Equal(1, log.Count(entry => entry.Contains("target-search-empty resource=Stone", StringComparison.Ordinal)));
        Assert.Equal(1, log.Count(entry => entry.Contains("gather-target-missing resource=Stone", StringComparison.Ordinal)));
        Assert.True(Assert.IsType<float>(GetPrivateField(bot, "_retargetCooldown")!) >= 2.3f);
    }

    [Fact(DisplayName = "CompanionBot при стройке дома держит более длинный miss-cooldown и для дерева")]
    public void CompanionBot_ExecuteGatherObjective_BuildHouseWoodMissing_UsesLongerRetryCooldown()
    {
        var world = new WorldMap(width: 48, height: 12, depth: 48, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Stone);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                for (var y = 2; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Нет-дерева",
            originX: 20,
            floorY: 2,
            originZ: 20,
            steps: [new HouseBuildStep(20, 2, 20, BlockType.Wood)]);
        var log = new List<string>();
        var bot = new CompanionBot(new GameConfig(), blueprint.Center, log.Add);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        world.EnsureChunksAround(blueprint.Center, radiusInChunks: 4);
        _ = world.RebuildDirtyChunkSurfaces(blueprint.Center, maxChunks: 256);

        var first = (bool)InvokePrivate(bot, "ExecuteGatherObjective", world, BotResourceType.Wood, 1, 1f / 30f, false)!;
        var second = (bool)InvokePrivate(bot, "ExecuteGatherObjective", world, BotResourceType.Wood, 1, 1f / 30f, false)!;

        Assert.False(first);
        Assert.False(second);
        Assert.Equal(1, log.Count(entry => entry.Contains("target-search-empty resource=Wood", StringComparison.Ordinal)));
        Assert.Equal(1, log.Count(entry => entry.Contains("gather-target-missing resource=Wood", StringComparison.Ordinal)));
        Assert.True(Assert.IsType<float>(GetPrivateField(bot, "_retargetCooldown")!) >= 1.5f);
    }

    [Fact(DisplayName = "CompanionBot build resource cooldown для листьев использует обычный fallback, а не специальные ветки wood/stone")]
    public void CompanionBot_GetMissingResourceRetryCooldown_ForLeavesBuildHouse_UsesDefaultCooldown()
    {
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Листва-cooldown",
            originX: 10,
            floorY: 2,
            originZ: 10,
            steps: [new HouseBuildStep(10, 2, 10, BlockType.Leaves)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(12.5f, 2.02f, 12.5f));

        var cooldown = (float)InvokePrivate(bot, "GetMissingResourceRetryCooldown", BotResourceType.Leaves, blueprint)!;

        Assert.InRange(cooldown, 0.84f, 0.86f);
    }

    [Fact(DisplayName = "CompanionBot surface helper пропускает planned и protected цели")]
    public void CompanionBot_CollectSurfaceResourceCandidates_SkipsPlannedAndProtected()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(10, 2, 8, BlockType.Wood);
        world.SetBlock(12, 2, 8, BlockType.Wood);
        WarmWorld(world, new Vector3(8.5f, 2.02f, 8.5f));

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Surface-skip",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps: [new HouseBuildStep(10, 2, 8, BlockType.Wood)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 8.5f));
        SetPrivateField(bot, "_hasPlayerPosition", true);
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(12.5f, 3.02f, 8.5f));

        var candidateType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(candidateType);
        var candidates = Activator.CreateInstance(listType)!;

        InvokePrivate(
            bot,
            "CollectSurfaceResourceCandidates",
            world,
            BotResourceType.Wood,
            blueprint,
            1,
            1,
            1,
            candidates);

        Assert.Equal(0, (int)listType.GetProperty("Count")!.GetValue(candidates)!);
    }

    [Fact(DisplayName = "CompanionBot loaded helper пропускает planned и protected цели")]
    public void CompanionBot_CollectLoadedChunkResourceCandidates_SkipsPlannedAndProtected()
    {
        var world = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(10, 2, 8, BlockType.Wood);
        world.SetBlock(12, 2, 8, BlockType.Wood);
        world.EnsureChunksAround(new Vector3(8.5f, 2.02f, 8.5f), radiusInChunks: 2);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Loaded-skip",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps: [new HouseBuildStep(10, 2, 8, BlockType.Wood)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 8.5f));
        SetPrivateField(bot, "_hasPlayerPosition", true);
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(12.5f, 3.02f, 8.5f));

        var candidateType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(candidateType);
        var candidates = Activator.CreateInstance(listType)!;

        InvokePrivate(
            bot,
            "CollectLoadedChunkResourceCandidates",
            world,
            BotResourceType.Wood,
            blueprint,
            1,
            1,
            1,
            candidates);

        Assert.Equal(0, (int)listType.GetProperty("Count")!.GetValue(candidates)!);
    }

    [Fact(DisplayName = "CompanionBot blueprint forestry helper пропускает пустые loaded-колонки и невалидные цели")]
    public void CompanionBot_CollectBlueprintForestryCandidates_SkipsLoadedAirAndInvalidTargets()
    {
        var world = new WorldMap(width: 64, height: 12, depth: 64, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Stone);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                for (var y = 2; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(28, 2, 24, BlockType.Wood);
        world.SetBlock(28, 3, 24, BlockType.Leaves);
        world.SetBlock(30, 2, 24, BlockType.Wood);
        world.SetBlock(30, 3, 24, BlockType.Leaves);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Forestry-helper",
            originX: 20,
            floorY: 2,
            originZ: 20,
            steps: [new HouseBuildStep(28, 2, 24, BlockType.Wood)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(18.5f, 2.02f, 24.5f));
        world.EnsureChunksAround(blueprint.Center, radiusInChunks: 4);
        _ = world.RebuildDirtyChunkSurfaces(blueprint.Center, maxChunks: 256);
        InvokePrivate(bot, "BlockResourceTarget", CreateResourceTarget(30, 2, 24, BotResourceType.Wood), 2f);

        // Делает loaded chunk с пустой колонкой: helper должен её пропустить без кандидата.
        for (var y = 0; y < world.Height; y++)
        {
            world.SetBlock(8, y, 8, BlockType.Air);
        }
        world.EnsureChunksAround(new Vector3(8.5f, 2.02f, 8.5f), radiusInChunks: 0);

        var candidateType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(candidateType);
        var candidates = Activator.CreateInstance(listType)!;

        InvokePrivate(bot, "CollectBlueprintForestryCandidates", world, blueprint, candidates);

        Assert.Equal(0, (int)listType.GetProperty("Count")!.GetValue(candidates)!);
    }

    [Fact(DisplayName = "CompanionBot blueprint quarry helper пропускает пустые loaded-колонки и невалидные buried-цели")]
    public void CompanionBot_CollectBlueprintQuarryCandidates_SkipsLoadedAirAndInvalidTargets()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Stone);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
                for (var y = 4; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(28, 2, 24, BlockType.Stone);
        world.SetBlock(28, 3, 24, BlockType.Dirt);
        world.SetBlock(30, 2, 24, BlockType.Stone);
        world.SetBlock(30, 3, 24, BlockType.Dirt);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Quarry-helper",
            originX: 20,
            floorY: 3,
            originZ: 20,
            steps: [new HouseBuildStep(28, 2, 24, BlockType.Stone)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(18.5f, 4.02f, 24.5f));
        world.EnsureChunksAround(blueprint.Center, radiusInChunks: 4);
        _ = world.RebuildDirtyChunkSurfaces(blueprint.Center, maxChunks: 256);
        InvokePrivate(bot, "BlockResourceTarget", CreateResourceTarget(30, 2, 24, BotResourceType.Stone), 2f);

        for (var y = 0; y < world.Height; y++)
        {
            world.SetBlock(8, y, 8, BlockType.Air);
        }
        world.EnsureChunksAround(new Vector3(8.5f, 2.02f, 8.5f), radiusInChunks: 0);

        var candidateType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(candidateType);
        var candidates = Activator.CreateInstance(listType)!;

        InvokePrivate(bot, "CollectBlueprintQuarryCandidates", world, blueprint, candidates);

        Assert.Equal(0, (int)listType.GetProperty("Count")!.GetValue(candidates)!);
    }

    [Fact(DisplayName = "CompanionBot blueprint forestry helper пропускает protected и неэкспонированную древесину")]
    public void CompanionBot_CollectBlueprintForestryCandidates_SkipsProtectedAndUnexposedWood()
    {
        var world = new WorldMap(width: 64, height: 12, depth: 64, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Stone);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                for (var y = 2; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(24, 2, 24, BlockType.Wood);
        world.SetBlock(26, 2, 24, BlockType.Wood);
        world.SetBlock(26, 3, 24, BlockType.Dirt);
        world.SetBlock(27, 2, 24, BlockType.Dirt);
        world.SetBlock(25, 2, 24, BlockType.Dirt);
        world.SetBlock(26, 2, 23, BlockType.Dirt);
        world.SetBlock(26, 2, 25, BlockType.Dirt);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Forestry-protected",
            originX: 20,
            floorY: 2,
            originZ: 20,
            steps: []);
        var bot = new CompanionBot(new GameConfig(), new Vector3(20.5f, 2.02f, 20.5f));
        SetPrivateField(bot, "_hasPlayerPosition", true);
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(24.5f, 3.02f, 24.5f));
        world.EnsureChunksAround(blueprint.Center, radiusInChunks: 4);

        var candidateType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(candidateType);
        var candidates = Activator.CreateInstance(listType)!;

        InvokePrivate(bot, "CollectBlueprintForestryCandidates", world, blueprint, candidates);

        Assert.Equal(0, (int)listType.GetProperty("Count")!.GetValue(candidates)!);
    }

    [Fact(DisplayName = "CompanionBot blueprint quarry helper пропускает protected и уже открытый камень")]
    public void CompanionBot_CollectBlueprintQuarryCandidates_SkipsProtectedAndExposedStone()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Stone);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
                for (var y = 4; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        world.SetBlock(24, 2, 24, BlockType.Stone);
        world.SetBlock(24, 3, 24, BlockType.Dirt);
        world.SetBlock(26, 2, 24, BlockType.Stone);
        world.SetBlock(26, 3, 24, BlockType.Air);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Quarry-protected",
            originX: 20,
            floorY: 3,
            originZ: 20,
            steps: []);
        var bot = new CompanionBot(new GameConfig(), new Vector3(20.5f, 4.02f, 20.5f));
        SetPrivateField(bot, "_hasPlayerPosition", true);
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(24.5f, 3.02f, 24.5f));
        world.EnsureChunksAround(blueprint.Center, radiusInChunks: 4);

        var candidateType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(candidateType);
        var candidates = Activator.CreateInstance(listType)!;

        InvokePrivate(bot, "CollectBlueprintQuarryCandidates", world, blueprint, candidates);

        Assert.Equal(0, (int)listType.GetProperty("Count")!.GetValue(candidates)!);
    }

    [Fact(DisplayName = "CompanionBot blueprint forestry helper покрывает blocked/planned/protected/unexposed и оставляет только валидную древесину")]
    public void CompanionBot_CollectBlueprintForestryCandidates_CoversAllGuardReasons()
    {
        var world = new WorldMap(width: 72, height: 12, depth: 72, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Stone);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                for (var y = 2; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        foreach (var x in new[] { 24, 26, 28, 30, 32 })
        {
            world.SetBlock(x, 2, 24, BlockType.Wood);
            world.SetBlock(x, 3, 24, BlockType.Leaves);
        }

        world.SetBlock(30, 3, 24, BlockType.Dirt);
        world.SetBlock(31, 2, 24, BlockType.Dirt);
        world.SetBlock(29, 2, 24, BlockType.Dirt);
        world.SetBlock(30, 2, 23, BlockType.Dirt);
        world.SetBlock(30, 2, 25, BlockType.Dirt);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Forestry-guards",
            originX: 20,
            floorY: 2,
            originZ: 20,
            steps: [new HouseBuildStep(26, 2, 24, BlockType.Wood)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(20.5f, 2.02f, 20.5f));
        SetPrivateField(bot, "_hasPlayerPosition", true);
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(28.5f, 3.02f, 24.5f));
        InvokePrivate(bot, "BlockResourceTarget", CreateResourceTarget(24, 2, 24, BotResourceType.Wood), 2f);
        world.EnsureChunksAround(blueprint.Center, radiusInChunks: 4);

        var candidateType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(candidateType);
        var candidates = Activator.CreateInstance(listType)!;

        InvokePrivate(bot, "CollectBlueprintForestryCandidates", world, blueprint, candidates);

        Assert.Equal(1, (int)listType.GetProperty("Count")!.GetValue(candidates)!);
    }

    [Fact(DisplayName = "CompanionBot blueprint quarry helper покрывает blocked/planned/protected/exposed и оставляет только валидный buried-камень")]
    public void CompanionBot_CollectBlueprintQuarryCandidates_CoversAllGuardReasons()
    {
        var world = new WorldMap(width: 72, height: 16, depth: 72, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Stone);
                world.SetBlock(x, 1, z, BlockType.Dirt);
                world.SetBlock(x, 2, z, BlockType.Dirt);
                world.SetBlock(x, 3, z, BlockType.Dirt);
                for (var y = 4; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        foreach (var x in new[] { 24, 26, 28, 30, 32 })
        {
            world.SetBlock(x, 2, 24, BlockType.Stone);
            world.SetBlock(x, 3, 24, BlockType.Dirt);
        }

        world.SetBlock(30, 3, 24, BlockType.Air);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Quarry-guards",
            originX: 20,
            floorY: 3,
            originZ: 20,
            steps: [new HouseBuildStep(26, 2, 24, BlockType.Stone)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(20.5f, 4.02f, 20.5f));
        SetPrivateField(bot, "_hasPlayerPosition", true);
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(28.5f, 3.02f, 24.5f));
        InvokePrivate(bot, "BlockResourceTarget", CreateResourceTarget(24, 2, 24, BotResourceType.Stone), 2f);
        world.EnsureChunksAround(blueprint.Center, radiusInChunks: 4);

        var candidateType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(candidateType);
        var candidates = Activator.CreateInstance(listType)!;

        InvokePrivate(bot, "CollectBlueprintQuarryCandidates", world, blueprint, candidates);

        Assert.Equal(1, (int)listType.GetProperty("Count")!.GetValue(candidates)!);
    }

    [Fact(DisplayName = "CompanionBot blueprint forestry/quarry helper покрывают loaded-ветку без верхнего solid-блока")]
    public void CompanionBot_BlueprintHelpers_CoverLoadedChunkWithoutTopSolid()
    {
        var world = new WorldMap(width: 48, height: 12, depth: 48, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Empty-loaded",
            originX: 16,
            floorY: 2,
            originZ: 16,
            steps: []);
        var bot = new CompanionBot(new GameConfig(), new Vector3(16.5f, 2.02f, 16.5f));
        world.EnsureChunksAround(blueprint.Center, radiusInChunks: 2);

        var candidateType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(candidateType);

        var forestryCandidates = Activator.CreateInstance(listType)!;
        InvokePrivate(bot, "CollectBlueprintForestryCandidates", world, blueprint, forestryCandidates);
        Assert.Equal(0, (int)listType.GetProperty("Count")!.GetValue(forestryCandidates)!);

        var quarryCandidates = Activator.CreateInstance(listType)!;
        InvokePrivate(bot, "CollectBlueprintQuarryCandidates", world, blueprint, quarryCandidates);
        Assert.Equal(0, (int)listType.GetProperty("Count")!.GetValue(quarryCandidates)!);
    }

    [Fact(DisplayName = "CompanionBot blueprint forestry/quarry helper пропускают unloaded chunk-колонки")]
    public void CompanionBot_BlueprintHelpers_SkipUnloadedChunks()
    {
        var world = new WorldMap(width: 48, height: 12, depth: 48, chunkSize: 8, seed: 0);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Unloaded-range",
            originX: 16,
            floorY: 2,
            originZ: 16,
            steps: []);
        var bot = new CompanionBot(new GameConfig(), new Vector3(16.5f, 2.02f, 16.5f));

        var candidateType = typeof(CompanionBot).GetNestedType("ScoredResourceCandidate", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(candidateType);

        var forestryCandidates = Activator.CreateInstance(listType)!;
        InvokePrivate(bot, "CollectBlueprintForestryCandidates", world, blueprint, forestryCandidates);
        Assert.Equal(0, (int)listType.GetProperty("Count")!.GetValue(forestryCandidates)!);

        var quarryCandidates = Activator.CreateInstance(listType)!;
        InvokePrivate(bot, "CollectBlueprintQuarryCandidates", world, blueprint, quarryCandidates);
        Assert.Equal(0, (int)listType.GetProperty("Count")!.GetValue(quarryCandidates)!);
    }

    [Fact(DisplayName = "CompanionBot сильнее штрафует карьерный камень, который лежит заметно ниже уровня пола дома")]
    public void CompanionBot_ScoreBlueprintQuarryCandidate_PenalizesDeepStoneBelowFloor()
    {
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Штраф-глубины",
            originX: 14,
            floorY: 12,
            originZ: 14,
            steps: [new HouseBuildStep(14, 12, 14, BlockType.Stone)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(18.5f, 12.02f, 18.5f));

        var shallow = (float)InvokePrivate(bot, "ScoreBlueprintQuarryCandidate", blueprint, 20, 11, 20, 15)!;
        var deep = (float)InvokePrivate(bot, "ScoreBlueprintQuarryCandidate", blueprint, 20, 4, 20, 15)!;

        Assert.True(deep > shallow);
    }

    [Fact(DisplayName = "CompanionBot считает воздушную экспозицию для полностью открытого и полностью зажатого блока")]
    public void CompanionBot_CountAirExposureNoLoad_HandlesOpenAndClosedBlocks()
    {
        var world = new WorldMap(width: 16, height: 12, depth: 16, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 8.5f));

        world.SetBlock(8, 2, 8, BlockType.Stone);
        var openExposure = (int)InvokePrivate(bot, "CountAirExposureNoLoad", world, 8, 2, 8)!;

        world.SetBlock(9, 2, 8, BlockType.Dirt);
        world.SetBlock(7, 2, 8, BlockType.Dirt);
        world.SetBlock(8, 2, 9, BlockType.Dirt);
        world.SetBlock(8, 2, 7, BlockType.Dirt);
        world.SetBlock(8, 3, 8, BlockType.Dirt);
        var closedExposure = (int)InvokePrivate(bot, "CountAirExposureNoLoad", world, 8, 2, 8)!;

        Assert.Equal(5, openExposure);
        Assert.Equal(0, closedExposure);
    }

    [Fact(DisplayName = "CompanionBot private helper-ветки по движению, позе и ориентации покрыты")]
    public void CompanionBot_PrivateMovementBranches_Work()
    {
        var world = new WorldMap(width: 16, height: 12, depth: 16, chunkSize: 8, seed: 0);
        WarmWorld(world, new Vector3(4.5f, 2f, 4.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        SetPrivateField(bot, "_noPathTimer", 0.2f);
        InvokePrivate(bot, "UpdateFollowPlayer", world, new Vector3(12.5f, 2.02f, 12.5f), Vector3.UnitX, 1f / 30f);
        Assert.Equal(BotStatus.NoPath, bot.Status);

        var blockedWorld = new WorldMap(width: 8, height: 12, depth: 8, chunkSize: 8, seed: 0);
        for (var x = 0; x < blockedWorld.Width; x++)
        {
            for (var z = 0; z < blockedWorld.Depth; z++)
            {
                for (var y = 2; y <= 10; y++)
                {
                    blockedWorld.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }

        var blockedBot = new CompanionBot(new GameConfig(), new Vector3(1.5f, 8.02f, 1.5f));
        InvokePrivate(blockedBot, "UpdateFollowPlayer", blockedWorld, new Vector3(7.5f, 8.02f, 7.5f), Vector3.UnitX, 1f / 30f);
        Assert.Equal(BotStatus.Idle, blockedBot.Status);

        var arrived = InvokePrivate(bot, "MoveTowardsPose", world, bot.Position, 1f / 30f)!;
        Assert.Equal("Arrived", arrived.ToString());

        SetPrivateField(bot, "_stuckTime", 3f);
        var blockedMove = InvokePrivate(bot, "MoveTowardsPose", world, bot.Position + new Vector3(3f, 2f, 0f), 1f / 30f)!;
        Assert.Equal("Blocked", blockedMove.ToString());

        Assert.True((bool)InvokePrivate(bot, "TryFindStandPoseNear", world, new Vector3(5.5f, 2f, 5.5f), 2, null!)!);
        Assert.False((bool)InvokePrivate(blockedBot, "TryFindStandPoseNear", blockedWorld, new Vector3(4.5f, 4f, 4.5f), 1, null!)!);

        Assert.False((bool)InvokePrivate(blockedBot, "IsPoseClear", blockedWorld, new Vector3(1.5f, 4.02f, 1.5f))!);
        world.SetBlock(2, 0, 2, BlockType.Air);
        world.SetBlock(2, 1, 2, BlockType.Air);
        Assert.False((bool)InvokePrivate(bot, "IsPoseClear", world, new Vector3(2.5f, 2.02f, 2.5f))!);

        Assert.Equal(new Vector3(0f, 0f, -1f), (Vector3)InvokePrivateStatic(typeof(CompanionBot), "ToHorizontal", new Vector3(0f, 1f, 0f))!);
        Assert.True(((Vector3)InvokePrivateStatic(typeof(CompanionBot), "ToHorizontal", new Vector3(3f, 0f, 4f))!).Length() > 0.99f);
        Assert.InRange((float)InvokePrivateStatic(typeof(CompanionBot), "NormalizeAngle", MathF.PI * 3f)!, -MathF.PI, MathF.PI);
        Assert.InRange((float)InvokePrivateStatic(typeof(CompanionBot), "NormalizeAngle", -MathF.PI * 3f)!, -MathF.PI, MathF.PI);
    }

    [Fact(DisplayName = "CompanionBot private helper-ветки по no-path и очередям команд покрыты")]
    public void CompanionBot_PrivateCommandFlowBranches_Work()
    {
        var world = new WorldMap(width: 16, height: 12, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(10, 2, 10, BlockType.Wood);
        WarmWorld(world, new Vector3(10.5f, 2f, 10.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(3.5f, 2.02f, 3.5f));
        var blockedBlueprint = new HouseBlueprint(HouseTemplateKind.CabinS, "Плохой-дом", 10, 2, 10, [new HouseBuildStep(10, 2, 10, BlockType.Wood)]);
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blockedBlueprint));
        SetPrivateField(bot, "_currentTarget", null);
        SetPrivateField(bot, "_retargetCooldown", 0f);
        SetPrivateField(bot, "_stuckTime", 0f);

        var blockedWorld = new WorldMap(width: 16, height: 12, depth: 16, chunkSize: 8, seed: 0);
        for (var x = 0; x < blockedWorld.Width; x++)
        {
            for (var z = 0; z < blockedWorld.Depth; z++)
            {
                for (var y = 2; y <= 10; y++)
                {
                    blockedWorld.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }

        InvokePrivate(bot, "UpdateBuildCommand", blockedWorld, 1f / 30f);
        Assert.Equal(BotStatus.NoPath, bot.Status);

        var flatWorld = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        flatWorld.SetBlock(20, 2, 20, BlockType.Wood);
        WarmWorld(flatWorld, new Vector3(20.5f, 2f, 20.5f));
        var moveBot = new CompanionBot(new GameConfig(), new Vector3(3.5f, 2.02f, 3.5f));
        InvokePrivate(moveBot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(moveBot, "_activeCommand", BotCommand.BuildHouse(blockedBlueprint));
        SetPrivateField(moveBot, "_stuckTime", 3f);
        InvokePrivate(moveBot, "UpdateBuildCommand", flatWorld, 1f / 30f);
        Assert.Equal(BotStatus.NoPath, moveBot.Status);

        var zeroNeeded = (bool)InvokePrivate(moveBot, "ExecuteGatherObjective", flatWorld, BotResourceType.Wood, 0, 1f / 30f, false)!;
        Assert.True(zeroNeeded);

        var targetArgs = InvokePrivateWithArgs(moveBot, "TryFindNearestResource", flatWorld, BotResourceType.Wood, null!);
        Assert.True((bool)targetArgs.Result!);
        var target = targetArgs.Args[2];
        SetPrivateField(moveBot, "_currentTarget", target);
        SetPrivateField(moveBot, "_stuckTime", 3f);
        InvokePrivate(moveBot, "ExecuteGatherObjective", flatWorld, BotResourceType.Wood, 1, 1f / 30f, false);
        Assert.Equal(BotStatus.NoPath, moveBot.Status);

        var missingResourceBot = new CompanionBot(new GameConfig(), new Vector3(3.5f, 2.02f, 3.5f));
        var noChunks = InvokePrivateWithArgs(missingResourceBot, "TryFindNearestResource", new WorldMap(width: 16, height: 12, depth: 16, chunkSize: 8, seed: 0), BotResourceType.Wood, null!);
        Assert.False((bool)noChunks.Result!);

        var queueBot = new CompanionBot(new GameConfig(), new Vector3(3.5f, 2.02f, 3.5f));
        Assert.True(queueBot.Enqueue(BotCommand.Gather(BotResourceType.Wood, 1)));
        Assert.True(queueBot.Enqueue(BotCommand.BuildHouse(new HouseBlueprint(HouseTemplateKind.CabinS, "Следом", 4, 2, 4, [new HouseBuildStep(5, 2, 5, BlockType.Wood)]))));
        InvokePrivate(queueBot, "CompleteActiveCommand");
        Assert.NotNull(queueBot.ActiveCommand);
        InvokePrivate(queueBot, "CompleteActiveCommand");
        Assert.Equal(BotStatus.Idle, queueBot.Status);
    }

    [Fact(DisplayName = "CompanionBot private ветки по build/apply и геометрическим проверкам покрыты")]
    public void CompanionBot_PrivateBuildAndGeometryBranches_Work()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        WarmWorld(world, new Vector3(5.5f, 2f, 5.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        SetPrivateField(bot, "_activeCommand", new BotCommand(BotCommandKind.BuildHouse, BotResourceType.Wood, 0, null));
        Assert.Contains("Стройка Дом: 0/0", bot.GetActiveSummary(), StringComparison.Ordinal);
        SetPrivateField(bot, "_queuedCommand", new BotCommand(BotCommandKind.BuildHouse, BotResourceType.Wood, 0, null));
        Assert.Equal("Дом", bot.GetQueuedSummary());

        SetPrivateField(bot, "_activeCommand", null);
        SetPrivateField(bot, "_queuedCommand", null);
        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);
        Assert.Null(bot.ActiveCommand);

        var airStep = new HouseBuildStep(6, 2, 6, BlockType.Air);
        var woodStep = new HouseBuildStep(6, 2, 6, BlockType.Wood);

        SetPrivateField(bot, "_actionCooldown", 0.2f);
        Assert.False((bool)InvokePrivate(bot, "TryApplyBuildStep", world, woodStep)!);

        SetPrivateField(bot, "_actionCooldown", 0f);
        world.SetBlock(6, 2, 6, BlockType.Wood);
        Assert.True((bool)InvokePrivate(bot, "TryApplyBuildStep", world, woodStep)!);

        world.SetBlock(6, 2, 6, BlockType.Air);
        Assert.True((bool)InvokePrivate(bot, "TryApplyBuildStep", world, airStep)!);

        SetPrivateField(bot, "_buildStepIndex", 0);
        Assert.False((bool)InvokePrivate(bot, "TryApplyBuildStep", world, woodStep)!);

        var selfOverlapBot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(selfOverlapBot, "AddStockpile", BlockType.Wood, 1);
        var selfOverlapStep = new HouseBuildStep(5, 2, 5, BlockType.Wood);
        Assert.False((bool)InvokePrivate(selfOverlapBot, "TryApplyBuildStep", world, selfOverlapStep)!);
        Assert.Equal(BlockType.Air, world.GetBlock(5, 2, 5));

        var playerOverlapBot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(playerOverlapBot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(playerOverlapBot, "_hasPlayerPosition", true);
        SetPrivateField(playerOverlapBot, "_lastPlayerPosition", new Vector3(7.5f, 2.02f, 5.5f));
        var playerOverlapStep = new HouseBuildStep(7, 2, 5, BlockType.Wood);
        Assert.False((bool)InvokePrivate(playerOverlapBot, "TryApplyBuildStep", world, playerOverlapStep)!);
        Assert.Equal(BlockType.Air, world.GetBlock(7, 2, 5));

        world.SetBlock(6, 2, 6, BlockType.Wood);
        Assert.True((bool)InvokePrivate(bot, "TryApplyBuildStep", world, airStep)!);

        var twoWoodWorld = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        twoWoodWorld.SetBlock(8, 2, 8, BlockType.Wood);
        twoWoodWorld.SetBlock(12, 2, 12, BlockType.Wood);
        WarmWorld(twoWoodWorld, new Vector3(10.5f, 2f, 10.5f));
        var searchBot = new CompanionBot(new GameConfig(), new Vector3(10.5f, 2.02f, 10.5f));
        var nearest = InvokePrivateWithArgs(searchBot, "TryFindNearestResource", twoWoodWorld, BotResourceType.Wood, null!);
        Assert.True((bool)nearest.Result!);

        var zeroSenseBot = new CompanionBot(new GameConfig { MouseSensitivity = 0f }, new Vector3(5.5f, 2.02f, 5.5f));
        var fallbackMove = InvokePrivate(zeroSenseBot, "MoveTowardsPose", world, zeroSenseBot.Position + new Vector3(0f, 2f, 0f), 1f / 30f)!;
        Assert.Equal("Moving", fallbackMove.ToString());

        Assert.False((bool)InvokePrivate(bot, "IsPoseClear", world, new Vector3(-1f, 2f, 4f))!);
        Assert.False((bool)InvokePrivate(bot, "IsPoseClear", world, new Vector3(4f, -1f, 4f))!);
        Assert.False((bool)InvokePrivate(bot, "IsPoseClear", world, new Vector3(world.Width - 0.1f, 2f, 4f))!);
        Assert.False((bool)InvokePrivate(bot, "IsPoseClear", world, new Vector3(4f, world.Height - 0.1f, 4f))!);

        var targetType = typeof(CompanionBot).GetNestedType("ResourceTarget", BindingFlags.NonPublic);
        Assert.NotNull(targetType);
        var outOfBoundsTarget = Activator.CreateInstance(targetType!, [999, 2, 999, BotResourceType.Wood])!;
        Assert.False((bool)InvokePrivate(bot, "IsResourceTargetValid", world, outOfBoundsTarget)!);

        var blockedMoveWorld = new WorldMap(width: 16, height: 12, depth: 16, chunkSize: 8, seed: 0);
        for (var z = 3; z <= 5; z++)
        {
            blockedMoveWorld.SetBlock(5, 2, z, BlockType.Stone);
            blockedMoveWorld.SetBlock(5, 3, z, BlockType.Stone);
        }
        var blockedMoveBot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        _ = InvokePrivate(blockedMoveBot, "MoveTowardsPose", blockedMoveWorld, new Vector3(8.5f, 2.02f, 4.5f), 1f / 30f);
        _ = InvokePrivate(blockedMoveBot, "MoveTowardsPose", blockedMoveWorld, new Vector3(8.5f, 2.02f, 4.5f), 0f);

        var blockedGatherWorld = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 7; x <= 13; x++)
        {
            for (var z = 7; z <= 13; z++)
            {
                if (x == 10 && z == 10)
                {
                    continue;
                }

                for (var y = 2; y <= 10; y++)
                {
                    blockedGatherWorld.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }

        blockedGatherWorld.SetBlock(10, 10, 10, BlockType.Wood);

        WarmWorld(blockedGatherWorld, new Vector3(10.5f, 2f, 10.5f));
        var gatherBot = new CompanionBot(new GameConfig(), new Vector3(3.5f, 2.02f, 3.5f));
        var gatherTarget = InvokePrivateWithArgs(gatherBot, "TryFindNearestResource", blockedGatherWorld, BotResourceType.Wood, null!);
        Assert.False((bool)gatherTarget.Result!);
        _ = InvokePrivate(gatherBot, "ExecuteGatherObjective", blockedGatherWorld, BotResourceType.Wood, 1, 1f / 30f, false);
        Assert.Equal(BotStatus.NoPath, gatherBot.Status);

        var edgeWorld = new WorldMap(width: 4, height: 12, depth: 4, chunkSize: 4, seed: 0);
        for (var x = 0; x < edgeWorld.Width; x++)
        {
            for (var z = 0; z < edgeWorld.Depth; z++)
            {
                for (var y = 2; y <= 10; y++)
                {
                    edgeWorld.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }

        var edgeBot = new CompanionBot(new GameConfig(), new Vector3(1.5f, 11.02f, 1.5f));
        _ = InvokePrivate(edgeBot, "TryFindStandPoseNear", edgeWorld, new Vector3(0.1f, 2f, 0.1f), 2, null!);
        _ = InvokePrivate(edgeBot, "TryFindStandPoseNear", edgeWorld, new Vector3(edgeWorld.Width - 0.1f, 2f, 0.1f), 2, null!);
        _ = InvokePrivate(edgeBot, "TryFindStandPoseNear", edgeWorld, new Vector3(0.1f, 2f, edgeWorld.Depth - 0.1f), 2, null!);
        _ = InvokePrivate(edgeBot, "TryFindStandPoseNear", edgeWorld, new Vector3(edgeWorld.Width - 0.1f, 2f, edgeWorld.Depth - 0.1f), 2, null!);

        var airBlueprint = new HouseBlueprint(HouseTemplateKind.CabinS, "Воздух", 4, 2, 4, [new HouseBuildStep(5, 2, 5, BlockType.Air)]);
        world.SetBlock(5, 2, 5, BlockType.Wood);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(airBlueprint));
        SetPrivateField(bot, "_buildStepIndex", 0);
        SetPrivateField(bot, "_actionCooldown", 0f);
        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        var stockedBuildBot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(stockedBuildBot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(stockedBuildBot, "_activeCommand", BotCommand.BuildHouse(new HouseBlueprint(HouseTemplateKind.CabinS, "Дерево", 4, 2, 4, [new HouseBuildStep(7, 2, 7, BlockType.Wood)])));
        InvokePrivate(stockedBuildBot, "UpdateBuildCommand", world, 1f / 30f);
    }

    [Fact(DisplayName = "CompanionBot считает застревание по прогрессу к waypoint, а не по любому сдвигу")]
    public void CompanionBot_UpdateRouteProgress_TracksWaypointProgress()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));

        SetPrivateField(bot, "_stuckTime", 1.2f);
        InvokePrivate(bot, "UpdateRouteProgress", 1f / 30f, 0.12f, 3f, 2.92f);
        Assert.InRange((float)GetPrivateField(bot, "_stuckTime")!, 0.0f, 1.16f);

        SetPrivateField(bot, "_stuckTime", 0f);
        InvokePrivate(bot, "UpdateRouteProgress", 0.5f, 0.02f, 3f, 2.995f);
        Assert.Equal(0.5f, (float)GetPrivateField(bot, "_stuckTime")!);

        SetPrivateField(bot, "_stuckTime", 0f);
        InvokePrivate(bot, "UpdateRouteProgress", 0.5f, 0.2f, 3f, 2.995f);
        Assert.Equal(0.325f, (float)GetPrivateField(bot, "_stuckTime")!, 0.001f);

        SetPrivateField(bot, "_stuckTime", 0.4f);
        InvokePrivate(bot, "UpdateRouteProgress", 0.25f, 0.01f, 0.45f, 0.43f);
        Assert.Equal(0f, (float)GetPrivateField(bot, "_stuckTime")!);
    }

    [Fact(DisplayName = "CompanionBot рядом с waypoint всё равно накапливает stuck-time, если прогресса к цели нет")]
    public void CompanionBot_UpdateRouteProgress_CloseWithoutProgress_StillTracksStuck()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));

        SetPrivateField(bot, "_stuckTime", 0.4f);
        InvokePrivate(bot, "UpdateRouteProgress", 0.25f, 0.01f, 0.45f, 0.449f);

        Assert.Equal(0.65f, (float)GetPrivateField(bot, "_stuckTime")!, 0.001f);
    }

    [Fact(DisplayName = "CompanionBot использует точный arrival для финальной action-точки и мягкий для follow")]
    public void CompanionBot_MoveAlongNavigationRoute_UsesPreciseArrival_ForActions()
    {
        var world = CreateFlatWorld(24, 12);
        var waypoint = new Vector3(4.57f, 2.62f, 4.5f);

        var gatherBot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        SetPrivateField(gatherBot, "_navigationGoal", CreateNavigationGoal("GatherAction", 6, 2, 6, 1, null));
        SetPrivateField(gatherBot, "_navigationWaypoints", new[] { waypoint });
        SetPrivateField(gatherBot, "_navigationWaypointIndex", 0);

        var gatherMove = InvokePrivate(gatherBot, "MoveAlongNavigationRoute", world, 1f / 30f, 0.08f)!;
        Assert.Equal("Moving", gatherMove.ToString());
        Assert.Equal(0, (int)GetPrivateField(gatherBot, "_navigationWaypointIndex")!);

        var followBot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        SetPrivateField(followBot, "_navigationGoal", CreateNavigationGoal("Follow", 6, 2, 6, 1, null));
        SetPrivateField(followBot, "_navigationWaypoints", new[] { waypoint });
        SetPrivateField(followBot, "_navigationWaypointIndex", 0);

        var followMove = InvokePrivate(followBot, "MoveAlongNavigationRoute", world, 1f / 30f, 0.08f)!;
        Assert.Equal("Arrived", followMove.ToString());
        Assert.Equal(1, (int)GetPrivateField(followBot, "_navigationWaypointIndex")!);
    }

    [Fact(DisplayName = "CompanionBot не зацикливает стройку, если маршрут формально завершен, но блок еще вне досягаемости")]
    public void CompanionBot_UpdateBuildCommand_BreaksArrivedOutOfRangeLoop()
    {
        var world = CreateFlatWorld(32, 12);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Цикл-стройки",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps: [new HouseBuildStep(18, 2, 18, BlockType.Wood)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        SetPrivateField(bot, "_navigationGoal", CreateNavigationGoal("BuildAction", 18, 2, 18, 7, blueprint));
        SetPrivateField(bot, "_navigationWaypoints", new[] { bot.Position });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Equal(BotStatus.NoPath, bot.Status);
        Assert.Empty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
        Assert.True((float)GetPrivateField(bot, "_noPathTimer")! > 0f);
    }

    [Fact(DisplayName = "CompanionBot после build-arrived-out-of-range сразу переключается на достижимый альтернативный шаг")]
    public void CompanionBot_UpdateBuildCommand_UsesAlternateStepAfterArrivedOutOfRange()
    {
        var world = CreateFlatWorld(32, 12);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Альтернатива-после-arrived",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(18, 2, 18, BlockType.Wood),
                new HouseBuildStep(8, 2, 5, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        SetPrivateField(bot, "_navigationGoal", CreateNavigationGoal("BuildAction", 18, 2, 18, 7, blueprint));
        SetPrivateField(bot, "_navigationWaypoints", new[] { bot.Position });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Equal(BlockType.Wood, world.GetBlock(8, 2, 5));
        Assert.Equal(BotStatus.Building, bot.Status);
    }

    [Fact(DisplayName = "CompanionBot после build-arrived-out-of-range уходит в сбор, если для альтернативного шага не хватает ресурса")]
    public void CompanionBot_UpdateBuildCommand_GathersForAlternateStepAfterArrivedOutOfRange()
    {
        var world = CreateFlatWorld(32, 12);
        world.SetBlock(14, 1, 5, BlockType.Wood);
        WarmWorld(world, new Vector3(6.5f, 2.02f, 5.5f));
        WarmWorld(world, new Vector3(14.5f, 2.02f, 5.5f));

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Альтернатива-сбор-после-arrived",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(18, 2, 18, BlockType.Stone),
                new HouseBuildStep(12, 2, 12, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Stone, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        SetPrivateField(bot, "_navigationGoal", CreateNavigationGoal("BuildAction", 18, 2, 18, 7, blueprint));
        SetPrivateField(bot, "_navigationWaypoints", new[] { bot.Position });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Contains(bot.Status, new[] { BotStatus.Gathering, BotStatus.NoPath });
        Assert.Equal(0, bot.GetStockpile(BlockType.Wood));
    }

    [Fact(DisplayName = "CompanionBot после build-arrived-out-of-range начинает движение к альтернативному шагу, если он не в досягаемости")]
    public void CompanionBot_UpdateBuildCommand_MovesTowardAlternateStepAfterArrivedOutOfRange()
    {
        var world = CreateFlatWorld(32, 12);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Альтернатива-move-после-arrived",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(18, 2, 18, BlockType.Wood),
                new HouseBuildStep(12, 2, 12, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        SetPrivateField(bot, "_navigationGoal", CreateNavigationGoal("BuildAction", 18, 2, 18, 7, blueprint));
        SetPrivateField(bot, "_navigationWaypoints", new[] { bot.Position });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Equal(BotStatus.Building, bot.Status);
        Assert.NotEmpty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
        Assert.Equal(BlockType.Air, world.GetBlock(12, 2, 12));
    }

    [Fact(DisplayName = "CompanionBot сбрасывает цель добычи, если маршрут завершен, но ресурс еще вне досягаемости")]
    public void CompanionBot_UpdateGatherCommand_ClearsArrivedOutOfRangeTarget()
    {
        var world = CreateFlatWorld(32, 12);
        world.SetBlock(18, 2, 18, BlockType.Wood);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        SetPrivateField(bot, "_activeCommand", BotCommand.Gather(BotResourceType.Wood, 1));
        SetPrivateField(bot, "_currentTarget", CreateResourceTarget(18, 2, 18, BotResourceType.Wood));
        SetPrivateField(bot, "_navigationGoal", CreateNavigationGoal("GatherAction", 18, 2, 18, 6, null));
        SetPrivateField(bot, "_navigationWaypoints", new[] { bot.Position });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        InvokePrivate(bot, "UpdateGatherCommand", world, BotResourceType.Wood, 1, 1f / 30f);

        Assert.Null(GetPrivateField(bot, "_currentTarget"));
        Assert.Empty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
        Assert.True((float)GetPrivateField(bot, "_retargetCooldown")! > 0f);
    }

    [Fact(DisplayName = "CompanionBot follow-router покрывает ветки blocked и arrived")]
    public void CompanionBot_FollowRouter_CoversBlockedAndArrivedBranches()
    {
        var world = CreateFlatWorld(32, 12);
        var playerPosition = new Vector3(18.5f, 2.02f, 18.5f);
        var playerLookDirection = Vector3.UnitX;
        var desiredProbeBot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        var desired = (Vector3)InvokePrivate(desiredProbeBot, "GetPreferredFollowPose", playerPosition, playerLookDirection, -1f)!;
        var desiredX = (int)MathF.Floor(desired.X);
        var desiredY = (int)MathF.Floor(desired.Y + 0.1f);
        var desiredZ = (int)MathF.Floor(desired.Z);

        var blockedBot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        SetPrivateField(blockedBot, "_navigationGoal", CreateNavigationGoal("Follow", desiredX, desiredY, desiredZ, 1, null));
        SetPrivateField(blockedBot, "_navigationWaypoints", new[] { desired });
        SetPrivateField(blockedBot, "_navigationWaypointIndex", 0);
        SetPrivateField(blockedBot, "_stuckTime", 3f);

        InvokePrivate(blockedBot, "UpdateFollowPlayer", world, playerPosition, playerLookDirection, 1f / 30f);

        Assert.Equal(BotStatus.NoPath, blockedBot.Status);

        var arrivedBot = new CompanionBot(new GameConfig(), desired + new Vector3(-0.2f, 0f, 0f));
        SetPrivateField(arrivedBot, "_navigationGoal", CreateNavigationGoal("Follow", desiredX, desiredY, desiredZ, 1, null));
        SetPrivateField(arrivedBot, "_navigationWaypoints", new[] { desired });
        SetPrivateField(arrivedBot, "_navigationWaypointIndex", 0);

        InvokePrivate(arrivedBot, "UpdateFollowPlayer", world, playerPosition, playerLookDirection, 1f / 30f);

        Assert.Equal(BotStatus.Idle, arrivedBot.Status);
    }

    [Fact(DisplayName = "CompanionBot переводит follow в Idle, если текущий waypoint уже достигнут")]
    public void CompanionBot_UpdateFollowPlayer_IdlesWhenWaypointAlreadyReached()
    {
        var world = CreateFlatWorld(32, 12);
        var playerPosition = new Vector3(18.5f, 2.02f, 18.5f);
        var playerLookDirection = Vector3.UnitX;
        var desiredProbeBot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        var desired = (Vector3)InvokePrivate(desiredProbeBot, "GetPreferredFollowPose", playerPosition, playerLookDirection, -1f)!;
        var desiredX = (int)MathF.Floor(desired.X);
        var desiredY = (int)MathF.Floor(desired.Y + 0.1f);
        var desiredZ = (int)MathF.Floor(desired.Z);
        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        SetPrivateField(bot, "_navigationGoal", CreateNavigationGoal("Follow", desiredX, desiredY, desiredZ, 1, null));
        SetPrivateField(bot, "_navigationWaypoints", new[] { bot.Position });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        InvokePrivate(bot, "UpdateFollowPlayer", world, playerPosition, playerLookDirection, 1f / 30f);

        Assert.Equal(BotStatus.Idle, bot.Status);
    }

    [Fact(DisplayName = "CompanionBot в follow уходит в Idle, если безопасная позиция неразрешима")]
    public void CompanionBot_UpdateFollowPlayer_IdlesWhenNoSafeFollowPoseExists()
    {
        var world = CreateFlatWorld(24, 12);
        var playerPosition = new Vector3(10.5f, 2.02f, 10.5f);
        var playerLook = Vector3.UnitZ;
        var bot = new CompanionBot(new GameConfig(), new Vector3(10.5f, 2.02f, 4.9f));
        SetPrivateField(bot, "_playerCollisionProbe", new Func<Vector3, bool>(_ => true));

        InvokePrivate(bot, "UpdateFollowPlayer", world, playerPosition, playerLook, 1f / 30f);

        Assert.Equal(BotStatus.Idle, bot.Status);
        Assert.Empty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
    }

    [Fact(DisplayName = "CompanionBot в follow очищает маршрут и уходит в Idle, если safe-поза есть, но путь к ней отсутствует")]
    public void CompanionBot_UpdateFollowPlayer_IdlesWhenSafePoseExistsButRouteFails()
    {
        var world = CreateFlatWorld(24, 12);
        for (var x = 9; x <= 11; x++)
        {
            for (var z = 9; z <= 11; z++)
            {
                if (x == 10 && z == 10)
                {
                    continue;
                }

                world.SetBlock(x, 2, z, BlockType.Wood);
                world.SetBlock(x, 3, z, BlockType.Wood);
            }
        }

        var bot = new CompanionBot(new GameConfig(), new Vector3(10.5f, 2.02f, 10.5f));
        SetPrivateField(bot, "_navigationWaypoints", new[] { new Vector3(8.5f, 2.02f, 8.5f) });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        InvokePrivate(bot, "UpdateFollowPlayer", world, new Vector3(18.5f, 2.02f, 18.5f), Vector3.UnitZ, 1f / 30f);

        Assert.Equal(BotStatus.Idle, bot.Status);
        Assert.Empty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
        Assert.True((float)GetPrivateField(bot, "_followRetryTimer")! > 0f);
    }

    [Fact(DisplayName = "CompanionBot TryEnsureStandRoute переиспользует goal и сбрасывает маршрут при неудаче")]
    public void CompanionBot_TryEnsureStandRoute_ReusesGoal_AndClearsRouteOnFailure()
    {
        var world = CreateFlatWorld(32, 12);
        var playerPosition = new Vector3(18.5f, 2.02f, 18.5f);
        var playerLookDirection = Vector3.UnitX;
        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        var desired = (Vector3)InvokePrivate(bot, "GetPreferredFollowPose", playerPosition, playerLookDirection, -1f)!;
        var desiredX = (int)MathF.Floor(desired.X);
        var desiredY = (int)MathF.Floor(desired.Y + 0.1f);
        var desiredZ = (int)MathF.Floor(desired.Z);

        SetPrivateField(bot, "_navigationGoal", CreateNavigationGoal("Follow", desiredX, desiredY, desiredZ, 1, null));
        Assert.True((bool)InvokePrivate(bot, "TryEnsureStandRoute", world, desired, 1, playerPosition, playerLookDirection)!);

        SetPrivateField(bot, "_navigationWaypoints", new[] { new Vector3(desiredX + 0.5f, desiredY + 0.02f, desiredZ + 0.5f) });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);
        var nearbyDesired = desired + new Vector3(1.1f, 0f, 0f);
        Assert.True((bool)InvokePrivate(bot, "TryEnsureStandRoute", world, nearbyDesired, 1, playerPosition, playerLookDirection)!);
        Assert.Equal(CreateNavigationGoal("Follow", desiredX, desiredY, desiredZ, 1, null), GetPrivateField(bot, "_navigationGoal"));

        var blockedWorld = new WorldMap(width: 6, height: 8, depth: 6, chunkSize: 8, seed: 0);
        for (var x = 0; x < blockedWorld.Width; x++)
        {
            for (var z = 0; z < blockedWorld.Depth; z++)
            {
                for (var y = 0; y < blockedWorld.Height; y++)
                {
                    blockedWorld.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }

        SetPrivateField(bot, "_navigationWaypoints", new[] { new Vector3(1.5f, 2.02f, 1.5f) });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);
        Assert.False((bool)InvokePrivate(bot, "TryEnsureStandRoute", blockedWorld, new Vector3(4.5f, 2.02f, 4.5f), 1, playerPosition, playerLookDirection)!);
        Assert.Empty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
    }

    [Fact(DisplayName = "CompanionBot не переиспользует follow-маршрут, если текущая goal-поза вошла в небезопасную зону игрока")]
    public void CompanionBot_CanReuseFollowRoute_ReturnsFalseForUnsafeCurrentGoal()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(4.5f, 2.02f, 4.5f));
        var unsafeGoal = CreateNavigationGoal("Follow", 10, 2, 12, 1, null);
        SetPrivateField(bot, "_navigationGoal", unsafeGoal);
        SetPrivateField(bot, "_navigationWaypoints", new[] { new Vector3(10.5f, 2.02f, 12.5f) });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        var reused = (bool)InvokePrivate(
            bot,
            "CanReuseFollowRoute",
            unsafeGoal,
            new Vector3(10.5f, 2.02f, 12.5f),
            new Vector3(10.5f, 2.02f, 10.5f),
            Vector3.UnitZ)!;

        Assert.False(reused);
    }

    [Fact(DisplayName = "CompanionBot на дальнем follow использует увеличенный reuse-distance для маршрута")]
    public void CompanionBot_CanReuseFollowRoute_UsesLargerThresholdWhenFar()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 8.5f));
        var farGoal = CreateNavigationGoal("Follow", 38, 2, 39, 3, null);
        SetPrivateField(bot, "_navigationGoal", farGoal);
        SetPrivateField(bot, "_navigationWaypoints", new[] { new Vector3(38.5f, 2.02f, 39.5f) });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        var reused = (bool)InvokePrivate(
            bot,
            "CanReuseFollowRoute",
            farGoal,
            new Vector3(44.2f, 2.02f, 39.3f),
            new Vector3(48.5f, 2.02f, 48.5f),
            Vector3.UnitZ)!;

        Assert.True(reused);
    }

    [Fact(DisplayName = "CompanionBot helper-ы дальнего follow выбирают крупный radius и reuse-distance по диапазонам")]
    public void CompanionBot_FollowDistanceHelpers_CoverFarRanges()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 8.5f));

        Assert.Equal(4, (int)InvokePrivate(bot, "GetFollowGoalRadius", new Vector3(80.5f, 2.02f, 8.5f))!);
        Assert.Equal(8f, (float)InvokePrivate(bot, "GetFollowRouteReuseDistance", new Vector3(48.5f, 2.02f, 8.5f))!);
    }

    [Fact(DisplayName = "CompanionBot после failed follow-route не пытается заново строить путь каждый кадр")]
    public void CompanionBot_UpdateFollowPlayer_UsesRetryCooldownAfterFollowRouteFailure()
    {
        var world = CreateFlatWorld(24, 12);
        for (var x = 9; x <= 11; x++)
        {
            for (var z = 9; z <= 11; z++)
            {
                if (x == 10 && z == 10)
                {
                    continue;
                }

                world.SetBlock(x, 2, z, BlockType.Wood);
                world.SetBlock(x, 3, z, BlockType.Wood);
            }
        }

        var diagnostics = new List<string>();
        var bot = new CompanionBot(new GameConfig(), new Vector3(10.5f, 2.02f, 10.5f), diagnostics.Add);
        var playerPosition = new Vector3(40.5f, 2.02f, 40.5f);

        InvokePrivate(bot, "UpdateFollowPlayer", world, playerPosition, Vector3.UnitZ, 1f / 30f);
        var failureCountAfterFirstUpdate = diagnostics.Count(line => line.Contains("follow-route-failed", StringComparison.Ordinal));

        InvokePrivate(bot, "UpdateFollowPlayer", world, playerPosition, Vector3.UnitZ, 1f / 30f);
        var failureCountAfterSecondUpdate = diagnostics.Count(line => line.Contains("follow-route-failed", StringComparison.Ordinal));

        Assert.Equal(1, failureCountAfterFirstUpdate);
        Assert.Equal(failureCountAfterFirstUpdate, failureCountAfterSecondUpdate);
        Assert.True((float)GetPrivateField(bot, "_followRetryTimer")! > 0f);
        Assert.Equal(BotStatus.Idle, bot.Status);
    }

    [Fact(DisplayName = "CompanionBot использует локальную рабочую позу и покрывает NavigationGoal")]
    public void CompanionBot_ActionRoute_UsesLocalPoseShortcut_AndNavigationGoal()
    {
        var world = CreateFlatWorld(24, 12);
        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 10.5f));
        var purpose = ParseNestedEnum(typeof(CompanionBot), "NavigationPurpose", "GatherAction");
        var goal = CreateNavigationGoal("GatherAction", 10, 2, 10, 2, null);
        var goalType = goal.GetType();
        Assert.Equal(purpose, goalType.GetProperty("Purpose", BindingFlags.Instance | BindingFlags.Public)!.GetValue(goal));
        Assert.Equal(10, goalType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public)!.GetValue(goal));
        Assert.Equal(2, goalType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public)!.GetValue(goal));
        Assert.Equal(10, goalType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public)!.GetValue(goal));
        Assert.Equal(2, goalType.GetProperty("Radius", BindingFlags.Instance | BindingFlags.Public)!.GetValue(goal));
        Assert.Null(goalType.GetProperty("Blueprint", BindingFlags.Instance | BindingFlags.Public)!.GetValue(goal));

        var local = InvokePrivateWithArgs(bot, "TryFindActionPoseNear", world, 10, 2, 10, 2, null!, null!, null!);
        Assert.True((bool)local.Result!);
        var localPose = Assert.IsType<Vector3>(local.Args[6]!);

        Assert.True((bool)InvokePrivate(bot, "TryEnsureActionRoute", world, purpose, 10, 2, 10, 2, null)!);
        Assert.Single((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);

        bot.Actor.SetPose(localPose, Vector3.UnitX);
        InvokePrivate(bot, "ResetNavigationRoute");
        Assert.True((bool)InvokePrivate(bot, "TryEnsureActionRoute", world, purpose, 10, 2, 10, 2, null)!);
        Assert.Empty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
    }

    [Fact(DisplayName = "CompanionBot перестраивает пустой устаревший маршрут для той же рабочей цели")]
    public void CompanionBot_TryEnsureActionRoute_RebuildsStaleEmptyRoute()
    {
        var world = CreateFlatWorld(32, 12);
        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 10.5f));
        var purpose = ParseNestedEnum(typeof(CompanionBot), "NavigationPurpose", "GatherAction");

        SetPrivateField(bot, "_navigationGoal", CreateNavigationGoal("GatherAction", 15, 2, 10, 6, null));
        SetPrivateField(bot, "_navigationWaypoints", Array.Empty<Vector3>());
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        Assert.True((bool)InvokePrivate(bot, "TryEnsureActionRoute", world, purpose, 15, 2, 10, 6, null)!);
        Assert.NotEmpty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
    }

    [Fact(DisplayName = "CompanionBot считает почти вертикальный спуск к рабочей точке сценарием только для stage-route")]
    public void CompanionBot_RequiresStagedLocalActionRoute_DetectsVerticalDrop()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(10.5f, 5.02f, 10.5f));
        Assert.True((bool)InvokePrivate(bot, "RequiresStagedLocalActionRoute", new Vector3(10.5f, 3.02f, 10.5f))!);
        Assert.False((bool)InvokePrivate(bot, "RequiresStagedLocalActionRoute", new Vector3(10.92f, 3.02f, 10.5f))!);
        Assert.False((bool)InvokePrivate(bot, "RequiresStagedLocalActionRoute", new Vector3(10.5f, 4.60f, 10.5f))!);
    }

    [Fact(DisplayName = "CompanionBot сбрасывает stale action-route, если его единственный waypoint требует почти вертикальный спуск")]
    public void CompanionBot_TryEnsureActionRoute_ResetsStaleVerticalDropWaypoint()
    {
        var world = new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }

        var bot = new CompanionBot(new GameConfig(), new Vector3(10.5f, 10.02f, 10.5f));
        var purpose = ParseNestedEnum(typeof(CompanionBot), "NavigationPurpose", "GatherAction");
        var verticalWaypoint = new Vector3(10.5f, 3.02f, 10.5f);
        SetPrivateField(bot, "_navigationGoal", CreateNavigationGoal("GatherAction", 10, 2, 10, 2, null));
        SetPrivateField(bot, "_navigationWaypoints", new[] { verticalWaypoint });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        Assert.False((bool)InvokePrivate(bot, "TryEnsureActionRoute", world, purpose, 10, 2, 10, 2, null)!);
        Assert.Empty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
    }

    [Fact(DisplayName = "CompanionBot повторно использует ту же рабочую цель, если блок уже в досягаемости")]
    public void CompanionBot_TryEnsureActionRoute_ReusesReachableCurrentGoal()
    {
        var world = CreateFlatWorld(24, 12);
        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 10.5f));
        var purpose = ParseNestedEnum(typeof(CompanionBot), "NavigationPurpose", "GatherAction");

        SetPrivateField(bot, "_navigationGoal", CreateNavigationGoal("GatherAction", 7, 2, 10, 2, null));
        SetPrivateField(bot, "_navigationWaypoints", Array.Empty<Vector3>());
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        Assert.True((bool)InvokePrivate(bot, "TryEnsureActionRoute", world, purpose, 7, 2, 10, 2, null)!);
        Assert.Empty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
    }

    [Fact(DisplayName = "CompanionBot при недоступном текущем шаге уходит на достижимый шаг и добирает ресурс")]
    public void CompanionBot_UpdateBuildCommand_UsesReachableFallbackForGather()
    {
        var world = CreateFlatWorld(32, 12);
        for (var x = 20; x < world.Width; x++)
        {
            for (var z = 20; z < world.Depth; z++)
            {
                for (var y = 2; y <= 10; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }

        world.SetBlock(20, 2, 10, BlockType.Wood);
        WarmWorld(world, new Vector3(5.5f, 2.02f, 5.5f));

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Fallback-сбор",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(28, 2, 28, BlockType.Air),
                new HouseBuildStep(18, 2, 5, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Equal(BotStatus.Gathering, bot.Status);
        Assert.NotNull(GetPrivateField(bot, "_currentTarget"));
    }

    [Fact(DisplayName = "CompanionBot считает нехватку ресурса по всем оставшимся шагам стройки, а не только по текущему")]
    public void CompanionBot_TryGetBuildResourceNeed_CountsRemainingSteps()
    {
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Расчет-ресурса",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(12, 2, 12, BlockType.Wood),
                new HouseBuildStep(12, 3, 12, BlockType.Wood),
                new HouseBuildStep(12, 4, 12, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);

        var firstCheck = InvokePrivateWithArgs(bot, "TryGetBuildResourceNeed", blueprint, 0, blueprint.Steps[0], default(BotResourceType), 0);
        Assert.True((bool)firstCheck.Result!);
        Assert.Equal(BotResourceType.Wood, (BotResourceType)firstCheck.Args[3]!);
        Assert.Equal(2, (int)firstCheck.Args[4]!);

        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 2);

        var secondCheck = InvokePrivateWithArgs(bot, "TryGetBuildResourceNeed", blueprint, 0, blueprint.Steps[0], default(BotResourceType), 0);
        Assert.False((bool)secondCheck.Result!);
        Assert.Equal(0, (int)secondCheck.Args[4]!);
    }

    [Fact(DisplayName = "CompanionBot при стройке продолжает батч-добычу, даже если запас уже стал положительным")]
    public void CompanionBot_UpdateBuildCommand_ContinuesGatheringWhileStockIsStillShort()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(15, 2, 4, BlockType.Wood);
        WarmWorld(world, new Vector3(11.5f, 2.02f, 4.5f));

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Дособор-дерева",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(16, 2, 16, BlockType.Wood),
                new HouseBuildStep(16, 3, 16, BlockType.Wood),
                new HouseBuildStep(16, 4, 16, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(11.5f, 2.02f, 4.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        SetPrivateField(bot, "_buildGatherBlock", BlockType.Wood);
        SetPrivateField(bot, "_buildGatherFromStepIndex", 0);

        bot.Update(world, new Vector3(18.5f, 2.02f, 18.5f), Vector3.UnitX, 1f / 30f);

        Assert.Equal(BotStatus.Gathering, bot.Status);
        Assert.Equal(BlockType.Air, world.GetBlock(16, 2, 16));
        Assert.Equal(BlockType.Wood, (BlockType)GetPrivateField(bot, "_buildGatherBlock")!);
        Assert.True(bot.GetStockpile(BlockType.Wood) >= 1);
        Assert.True(bot.GetStockpile(BlockType.Wood) < blueprint.CountRemaining(BlockType.Wood, 0));
    }

    [Fact(DisplayName = "CompanionBot BeginGatherForBuildStep пропускает шаги без расхода ресурса")]
    public void CompanionBot_BeginGatherForBuildStep_SkipsAirStep()
    {
        var world = CreateFlatWorld(24, 12);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Воздушный-шаг",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps: [new HouseBuildStep(8, 2, 5, BlockType.Air)]);
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));

        InvokePrivate(bot, "BeginGatherForBuildStep", world, blueprint, 0, blueprint.Steps[0], 1f / 30f);

        Assert.Equal(BotStatus.Idle, bot.Status);
        Assert.Null(GetPrivateField(bot, "_currentTarget"));
        Assert.Equal(0, bot.GetStockpile(BlockType.Wood));
    }

    [Fact(DisplayName = "CompanionBot после build-arrived-out-of-range применяет альтернативный Air-шаг без лишнего сбора")]
    public void CompanionBot_UpdateBuildCommand_UsesAlternateAirStepAfterArrivedOutOfRange()
    {
        var world = CreateFlatWorld(32, 12);
        world.SetBlock(8, 2, 5, BlockType.Wood);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Fallback-air",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(999, 2, 999, BlockType.Wood),
                new HouseBuildStep(8, 2, 5, BlockType.Air)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        SetPrivateField(bot, "_navigationGoal", CreateNavigationGoal("BuildAction", 999, 2, 999, 7, blueprint));
        SetPrivateField(bot, "_navigationWaypoints", new[] { bot.Position });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Equal(BlockType.Air, world.GetBlock(8, 2, 5));
        Assert.NotEqual(BotStatus.Gathering, bot.Status);
    }

    [Fact(DisplayName = "CompanionBot при build-no-path использует альтернативный Air-шаг без перехода в сбор")]
    public void CompanionBot_UpdateBuildCommand_UsesAlternateAirStepAfterRouteFailure()
    {
        var world = CreateFlatWorld(32, 12);
        world.SetBlock(8, 2, 5, BlockType.Wood);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Fallback-air-route-failed",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(999, 2, 999, BlockType.Wood),
                new HouseBuildStep(8, 2, 5, BlockType.Air)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Equal(BlockType.Air, world.GetBlock(8, 2, 5));
        Assert.NotEqual(BotStatus.Gathering, bot.Status);
        Assert.NotEqual(BotStatus.NoPath, bot.Status);
    }

    [Fact(DisplayName = "CompanionBot при недоступном текущем шаге может сразу поставить достижимый альтернативный блок")]
    public void CompanionBot_UpdateBuildCommand_UsesReachableFallbackForImmediateBuild()
    {
        var world = CreateFlatWorld(24, 12);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Fallback-стройка",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(999, 2, 999, BlockType.Wood),
                new HouseBuildStep(8, 2, 5, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Equal(BlockType.Wood, world.GetBlock(8, 2, 5));
        Assert.Equal(0, bot.GetStockpile(BlockType.Wood));
    }

    [Fact(DisplayName = "CompanionBot выбирает лучший достижимый шаг стройки и пропускает плохие кандидаты")]
    public void CompanionBot_TrySelectReachableBuildStep_CoversCandidateBranches()
    {
        var world = CreateFlatWorld(40, 12);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Выбор-шага",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(4, 2, 4, BlockType.Wood),
                new HouseBuildStep(6, 2, 6, BlockType.Air),
                new HouseBuildStep(8, 2, 5, BlockType.Wood),
                new HouseBuildStep(999, 2, 999, BlockType.Wood),
                new HouseBuildStep(18, 2, 5, BlockType.Wood),
                new HouseBuildStep(32, 2, 32, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        var selection = InvokePrivateWithArgs(bot, "TrySelectReachableBuildStep", world, blueprint, 0, default(HouseBuildStep));

        Assert.True((bool)selection.Result!);
        Assert.Equal(4, Assert.IsType<int>(selection.Args[2]));
        Assert.Equal(new HouseBuildStep(18, 2, 5, BlockType.Wood), Assert.IsType<HouseBuildStep>(selection.Args[3]!));
        Assert.NotEmpty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
    }

    [Fact(DisplayName = "CompanionBot пропускает временно заблокированный build-step и выбирает следующий достижимый")]
    public void CompanionBot_TrySelectReachableBuildStep_SkipsBlockedBuildStep()
    {
        var world = CreateFlatWorld(24, 12);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Blocked-build-step",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(4, 2, 4, BlockType.Wood),
                new HouseBuildStep(8, 2, 5, BlockType.Wood),
                new HouseBuildStep(10, 2, 5, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 2);
        InvokePrivate(bot, "BlockBuildStep", blueprint.Steps[1], 1.25f);

        var selection = InvokePrivateWithArgs(bot, "TrySelectReachableBuildStep", world, blueprint, 0, default(HouseBuildStep));

        Assert.True((bool)selection.Result!);
        Assert.Equal(2, Assert.IsType<int>(selection.Args[2]));
        Assert.Equal(new HouseBuildStep(10, 2, 5, BlockType.Wood), Assert.IsType<HouseBuildStep>(selection.Args[3]!));
    }

    [Fact(DisplayName = "CompanionBot снимает блокировку build-step после истечения cooldown")]
    public void CompanionBot_UpdateBlockedBuildStepCooldowns_ExpiresBlockedStep()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        var blockedStep = new HouseBuildStep(8, 2, 5, BlockType.Wood);

        InvokePrivate(bot, "BlockBuildStep", blockedStep, 0.5f);
        Assert.True((bool)InvokePrivate(bot, "IsBuildStepBlocked", blockedStep)!);

        InvokePrivate(bot, "UpdateBlockedBuildStepCooldowns", 0.2f);
        Assert.True((bool)InvokePrivate(bot, "IsBuildStepBlocked", blockedStep)!);

        InvokePrivate(bot, "UpdateBlockedBuildStepCooldowns", 0.35f);
        Assert.False((bool)InvokePrivate(bot, "IsBuildStepBlocked", blockedStep)!);
    }

    [Fact(DisplayName = "CompanionBot BlockBuildStep игнорирует нулевую длительность и не укорачивает более длинную блокировку")]
    public void CompanionBot_BlockBuildStep_IgnoresZeroAndShorterDuration()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        var blockedStep = new HouseBuildStep(8, 2, 5, BlockType.Wood);

        InvokePrivate(bot, "BlockBuildStep", blockedStep, 0f);
        Assert.False((bool)InvokePrivate(bot, "IsBuildStepBlocked", blockedStep)!);

        InvokePrivate(bot, "BlockBuildStep", blockedStep, 1.2f);
        var before = new Dictionary<HouseBuildStep, float>(Assert.IsType<Dictionary<HouseBuildStep, float>>(GetPrivateField(bot, "_blockedBuildSteps")));
        InvokePrivate(bot, "BlockBuildStep", blockedStep, 0.3f);
        var after = Assert.IsType<Dictionary<HouseBuildStep, float>>(GetPrivateField(bot, "_blockedBuildSteps"));

        Assert.Equal(before[blockedStep], after[blockedStep]);
    }

    [Fact(DisplayName = "CompanionBot при заблокированном текущем build-step и отсутствии альтернатив честно уходит в NoPath")]
    public void CompanionBot_UpdateBuildCommand_BlockedCurrentStepWithoutAlternative_SetsNoPath()
    {
        var world = CreateFlatWorld(24, 12);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Blocked-current-step",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps: [new HouseBuildStep(8, 2, 5, BlockType.Wood)]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        InvokePrivate(bot, "BlockBuildStep", blueprint.Steps[0], 1.25f);

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Equal(BotStatus.NoPath, bot.Status);
    }

    [Fact(DisplayName = "CompanionBot при недостижимом шаге стройки ставит временную ступень к цели")]
    public void CompanionBot_UpdateBuildCommand_CreatesTemporarySupportForUnreachableBuildStep()
    {
        var world = new WorldMap(width: 24, height: 16, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        for (var x = 4; x <= 5; x++)
        {
            for (var z = 4; z <= 6; z++)
            {
                for (var y = 0; y <= 4; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }

        for (var x = 14; x <= 15; x++)
        {
            for (var z = 4; z <= 6; z++)
            {
                for (var y = 0; y <= 1; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Временный-доступ",
            originX: 12,
            floorY: 2,
            originZ: 4,
            steps: [new HouseBuildStep(14, 2, 5, BlockType.Wood)]);

        var traces = new List<string>();
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 5.02f, 5.5f), traces.Add);
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        InvokePrivate(bot, "AddStockpile", BlockType.Dirt, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Equal(BlockType.Dirt, world.GetBlock(6, 3, 5));
        Assert.Equal(BotStatus.Building, bot.Status);
        Assert.Contains(traces, trace => trace.Contains("build-support", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "CompanionBot не пытается строить временную ступень без подходящего материала")]
    public void CompanionBot_TryCreateTemporaryBuildAccess_RequiresSupportMaterial()
    {
        var world = new WorldMap(width: 24, height: 16, depth: 24, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                for (var y = 0; y < world.Height; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        for (var x = 4; x <= 5; x++)
        {
            for (var z = 4; z <= 6; z++)
            {
                for (var y = 0; y <= 4; y++)
                {
                    world.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Без-материала",
            originX: 12,
            floorY: 2,
            originZ: 4,
            steps: [new HouseBuildStep(14, 2, 5, BlockType.Wood)]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 5.02f, 5.5f));

        Assert.False((bool)InvokePrivate(bot, "TryCreateTemporaryBuildAccess", world, blueprint, blueprint.Steps[0])!);
        Assert.Equal(BlockType.Air, world.GetBlock(6, 3, 5));
    }

    [Fact(DisplayName = "CompanionBot при blocked current step выбирает достижимый альтернативный шаг вместо NoPath")]
    public void CompanionBot_UpdateBuildCommand_BlockedStepUsesReachableAlternative()
    {
        var world = CreateFlatWorld(24, 12);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Blocked-step-reroute",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(999, 2, 999, BlockType.Wood),
                new HouseBuildStep(8, 2, 5, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        InvokePrivate(bot, "BlockBuildStep", blueprint.Steps[0], 1.25f);

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Equal(BlockType.Wood, world.GetBlock(8, 2, 5));
        Assert.NotEqual(BotStatus.NoPath, bot.Status);
    }

    [Fact(DisplayName = "CompanionBot при blocked move стройки переходит на достижимый альтернативный шаг")]
    public void CompanionBot_UpdateBuildCommand_BlockedMoveUsesReachableAlternative()
    {
        var world = CreateFlatWorld(32, 12);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Blocked-move-reroute",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(999, 2, 999, BlockType.Wood),
                new HouseBuildStep(8, 2, 5, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        SetPrivateField(bot, "_navigationGoal", CreateNavigationGoal("BuildAction", 999, 2, 999, 7, blueprint));
        SetPrivateField(bot, "_navigationWaypoints", new[] { new Vector3(20.5f, 2.02f, 20.5f) });
        SetPrivateField(bot, "_navigationWaypointIndex", 0);
        SetPrivateField(bot, "_stuckTime", 3f);

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Equal(BotStatus.Building, bot.Status);
        Assert.Equal(0f, Assert.IsType<float>(GetPrivateField(bot, "_noPathTimer")!));
    }

    [Fact(DisplayName = "CompanionBot support helpers покрывают fallback материала и отказы по stockpile")]
    public void CompanionBot_TemporarySupportHelpers_CoverFallbackMaterialAndStockFailure()
    {
        var world = CreateFlatWorld(24, 12);
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        var step = new HouseBuildStep(8, 2, 5, BlockType.Leaves);
        InvokePrivate(bot, "AddStockpile", BlockType.Leaves, 1);

        var material = InvokePrivateWithArgs(bot, "TryGetTemporarySupportMaterial", step, default(BlockType));
        Assert.True((bool)material.Result!);
        Assert.Equal(BlockType.Leaves, Assert.IsType<BlockType>(material.Args[1]!));
        Assert.False((bool)InvokePrivate(bot, "TryGetTemporarySupportMaterial", new HouseBuildStep(8, 2, 5, BlockType.Air), default(BlockType))!);

        Assert.True((bool)InvokePrivate(bot, "TryConsumeStockpile", BlockType.Leaves, 1)!);
        Assert.False((bool)InvokePrivate(bot, "TryApplyTemporaryBuildSupport", world, new HouseBuildStep(6, 1, 5, BlockType.Wood))!);
    }

    [Fact(DisplayName = "CompanionBot support helper-ы покрывают направления, границы и проверки позы")]
    public void CompanionBot_TemporarySupportHelpers_CoverDirectionAndPoseBranches()
    {
        var world = CreateFlatWorld(24, 12);
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Support-helper-coverage",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps: [new HouseBuildStep(2, 2, 2, BlockType.Wood)]);
        var remoteBlueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Support-remote",
            originX: 12,
            floorY: 2,
            originZ: 12,
            steps: [new HouseBuildStep(14, 2, 14, BlockType.Wood)]);

        Assert.False((bool)InvokePrivate(bot, "CanStandOnTemporarySupport", world, new HouseBuildStep(0, 11, 0, BlockType.Dirt))!);
        Assert.False((bool)InvokePrivate(bot, "CanStandOnTemporarySupport", world, new HouseBuildStep(24, 1, 5, BlockType.Dirt))!);
        Assert.False((bool)InvokePrivate(bot, "CanStandOnTemporarySupport", world, new HouseBuildStep(5, -1, 5, BlockType.Dirt))!);
        world.SetBlock(6, 2, 5, BlockType.Stone);
        Assert.False((bool)InvokePrivate(bot, "CanStandOnTemporarySupport", world, new HouseBuildStep(6, 1, 5, BlockType.Dirt))!);
        world.SetBlock(6, 2, 5, BlockType.Air);

        Assert.False((bool)InvokePrivate(bot, "IsValidTemporarySupportCandidate", world, remoteBlueprint, remoteBlueprint.Steps[0], new HouseBuildStep(-1, 1, 5, BlockType.Dirt))!);
        Assert.False((bool)InvokePrivate(bot, "IsValidTemporarySupportCandidate", world, remoteBlueprint, remoteBlueprint.Steps[0], new HouseBuildStep(14, 2, 14, BlockType.Dirt))!);
        Assert.False((bool)InvokePrivate(bot, "IsValidTemporarySupportCandidate", world, remoteBlueprint, remoteBlueprint.Steps[0], new HouseBuildStep(20, 1, 20, BlockType.Dirt))!);
        Assert.False((bool)InvokePrivate(bot, "IsValidTemporarySupportCandidate", world, blueprint, blueprint.Steps[0], new HouseBuildStep(5, 2, 5, BlockType.Dirt))!);
        Assert.False((bool)InvokePrivate(bot, "IsValidTemporarySupportCandidate", world, remoteBlueprint, remoteBlueprint.Steps[0], new HouseBuildStep(5, 2, 5, BlockType.Dirt))!);

        SetPrivateField(bot, "_hasPlayerPosition", true);
        SetPrivateField(bot, "_lastPlayerPosition", new Vector3(7.5f, 2.02f, 5.5f));
        Assert.False((bool)InvokePrivate(bot, "IsValidTemporarySupportCandidate", world, remoteBlueprint, remoteBlueprint.Steps[0], new HouseBuildStep(7, 2, 5, BlockType.Dirt))!);

        var selection = InvokePrivateWithArgs(
            bot,
            "TrySelectTemporaryBuildSupport",
            world,
            blueprint,
            new BotNavigationCell(0, 2, 0),
            new HouseBuildStep(-2, 0, 1, BlockType.Wood),
            BlockType.Dirt,
            default(HouseBuildStep));

        Assert.True((bool)selection.Result!);
        Assert.Equal(new HouseBuildStep(0, 2, 1, BlockType.Dirt), Assert.IsType<HouseBuildStep>(selection.Args[5]!));

        var centerSelection = InvokePrivateWithArgs(
            bot,
            "TrySelectTemporaryBuildSupport",
            world,
            remoteBlueprint,
            new BotNavigationCell(10, 2, 10),
            new HouseBuildStep(12, 0, 12, BlockType.Wood),
            BlockType.Dirt,
            default(HouseBuildStep));
        Assert.True((bool)centerSelection.Result!);
    }

    [Fact(DisplayName = "CompanionBot support directions покрывают нулевые и доминирующие оси")]
    public void CompanionBot_GetPreferredSupportDirections_CoversAxisBranches()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        var zeroDirections = ((System.Collections.IEnumerable)InvokePrivate(bot, "GetPreferredSupportDirections", new BotNavigationCell(4, 2, 4), new HouseBuildStep(4, 2, 4, BlockType.Wood))!)
            .Cast<object>()
            .Select(item => ((ValueTuple<int, int>)item))
            .ToArray();
        Assert.Equal(4, zeroDirections.Length);

        var verticalDominantDirections = ((System.Collections.IEnumerable)InvokePrivate(bot, "GetPreferredSupportDirections", new BotNavigationCell(4, 2, 4), new HouseBuildStep(5, 2, 9, BlockType.Wood))!)
            .Cast<object>()
            .Select(item => ((ValueTuple<int, int>)item))
            .ToArray();
        Assert.Equal((0, 1), verticalDominantDirections[0]);
    }

    [Fact(DisplayName = "CompanionBot считает нерасходуемый альтернативный шаг доступным по ресурсам")]
    public void CompanionBot_TrySelectReachableBuildStep_CoversNonConsumingCandidate()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(8, 2, 5, BlockType.Wood);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Воздушный-шаг",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(4, 2, 4, BlockType.Wood),
                new HouseBuildStep(8, 2, 5, BlockType.Air)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        var selection = InvokePrivateWithArgs(bot, "TrySelectReachableBuildStep", world, blueprint, 0, default(HouseBuildStep));

        Assert.True((bool)selection.Result!);
        Assert.Equal(new HouseBuildStep(8, 2, 5, BlockType.Air), Assert.IsType<HouseBuildStep>(selection.Args[3]!));
    }

    [Fact(DisplayName = "CompanionBot при fallback стройки пропускает перекрытый шаг для той же клетки")]
    public void CompanionBot_TrySelectReachableBuildStep_SkipsSupersededCandidate()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(8, 2, 5, BlockType.Wood);

        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Перекрытый-кандидат",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(999, 2, 999, BlockType.Wood),
                new HouseBuildStep(8, 2, 5, BlockType.Stone),
                new HouseBuildStep(8, 2, 5, BlockType.Air),
                new HouseBuildStep(9, 2, 5, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        InvokePrivate(bot, "AddStockpile", BlockType.Stone, 1);

        var selection = InvokePrivateWithArgs(bot, "TrySelectReachableBuildStep", world, blueprint, 0, default(HouseBuildStep));

        Assert.True((bool)selection.Result!);
        Assert.Equal(2, Assert.IsType<int>(selection.Args[2]));
        Assert.Equal(new HouseBuildStep(8, 2, 5, BlockType.Air), Assert.IsType<HouseBuildStep>(selection.Args[3]!));
    }

    [Fact(DisplayName = "CompanionBot после успешной добычи сразу снимает cooldown ретаргета")]
    public void CompanionBot_TryHarvestTarget_ClearsRetargetCooldown()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(8, 2, 8, BlockType.Stone);

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 7.5f));
        SetPrivateField(bot, "_retargetCooldown", 0.12f);
        var resourceTargetType = typeof(CompanionBot).GetNestedType("ResourceTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var target = Activator.CreateInstance(resourceTargetType, 8, 2, 8, BotResourceType.Stone)!;

        var harvested = (bool)InvokePrivate(bot, "TryHarvestTarget", world, target, false)!;

        Assert.True(harvested);
        Assert.Equal(0f, Assert.IsType<float>(GetPrivateField(bot, "_retargetCooldown")!));
    }

    [Fact(DisplayName = "CompanionBot после gather-route-failed включает no-path cooldown и не спамит мгновенным ретаргетом")]
    public void CompanionBot_ExecuteGatherObjective_RouteFailureStartsNoPathCooldown()
    {
        var world = CreateFlatWorld(32, 16);
        for (var x = 12; x <= 18; x++)
        {
            for (var z = 14; z <= 18; z++)
            {
                world.SetBlock(x, 5, z, BlockType.Dirt);
            }
        }

        world.SetBlock(19, 1, 16, BlockType.Air);
        world.SetBlock(19, 0, 16, BlockType.Stone);
        WarmWorld(world, new Vector3(16.5f, 6.02f, 16.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(16.5f, 6.02f, 16.5f));

        var result = (bool)InvokePrivate(bot, "ExecuteGatherObjective", world, BotResourceType.Stone, 1, 1f / 30f, false)!;

        Assert.False(result);
        Assert.Equal(BotStatus.NoPath, bot.Status);
        Assert.True(Assert.IsType<float>(GetPrivateField(bot, "_noPathTimer")!) > 0f);
    }

    [Fact(DisplayName = "CompanionBot при зафиксированной недостижимой цели помечает gather-route-failed и блокирует ресурс")]
    public void CompanionBot_ExecuteGatherObjective_WithExistingUnreachableTarget_BlocksIt()
    {
        var world = CreateFlatWorld(32, 12);
        for (var x = 4; x <= 16; x++)
        {
            for (var y = 1; y <= 10; y++)
            {
                for (var z = 4; z <= 16; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Stone);
                }
            }
        }

        WarmWorld(world, new Vector3(1.5f, 2.02f, 1.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(1.5f, 2.02f, 1.5f));
        var resourceTargetType = typeof(CompanionBot).GetNestedType("ResourceTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var target = Activator.CreateInstance(resourceTargetType, 10, 2, 10, BotResourceType.Stone)!;
        SetPrivateField(bot, "_currentTarget", target);

        var result = (bool)InvokePrivate(bot, "ExecuteGatherObjective", world, BotResourceType.Stone, 1, 1f / 30f, false)!;

        Assert.False(result);
        Assert.Equal(BotStatus.NoPath, bot.Status);
        Assert.Null(GetPrivateField(bot, "_currentTarget"));
        Assert.True(Assert.IsType<float>(GetPrivateField(bot, "_retargetCooldown")!) > 0f);
        Assert.True(Assert.IsType<float>(GetPrivateField(bot, "_noPathTimer")!) > 0f);
        var blockedTargets = Assert.IsAssignableFrom<System.Collections.IDictionary>(GetPrivateField(bot, "_blockedResourceTargets")!);
        Assert.Single(blockedTargets);
    }

    [Fact(DisplayName = "CompanionBot при обычной добыче строит GatherAction маршрут без перехода в NoPath")]
    public void CompanionBot_UpdateGatherCommand_BuildsReachableGatherRoute()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(14, 2, 8, BlockType.Wood);
        WarmWorld(world, new Vector3(6.5f, 2.02f, 8.5f));

        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 8.5f));
        SetPrivateField(bot, "_activeCommand", BotCommand.Gather(BotResourceType.Wood, 1));

        InvokePrivate(bot, "UpdateGatherCommand", world, BotResourceType.Wood, 1, 1f / 30f);

        Assert.Equal(BotStatus.Gathering, bot.Status);
        Assert.NotEmpty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
    }

    [Fact(DisplayName = "CompanionBot route-aware выбор ресурса умеет перейти на action-route fallback, если ближайшая local-поза изолирована")]
    public void CompanionBot_TryScoreReachableResourceCandidate_UsesActionRouteFallback()
    {
        var world = CreateFlatWorld(24, 16);
        for (var x = 9; x <= 13; x++)
        {
            for (var z = 9; z <= 11; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        world.SetBlock(10, 0, 10, BlockType.Stone);
        world.SetBlock(11, 4, 10, BlockType.Dirt);
        world.SetBlock(11, 5, 9, BlockType.Stone);
        world.SetBlock(11, 6, 9, BlockType.Stone);
        world.SetBlock(11, 5, 11, BlockType.Stone);
        world.SetBlock(11, 6, 11, BlockType.Stone);
        world.SetBlock(12, 5, 10, BlockType.Stone);
        world.SetBlock(12, 6, 10, BlockType.Stone);
        world.SetBlock(13, 4, 10, BlockType.Dirt);

        var bot = new CompanionBot(new GameConfig(), new Vector3(15.5f, 5.02f, 10.5f));
        var targetType = typeof(CompanionBot).GetNestedType("ResourceTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var target = Activator.CreateInstance(targetType, 10, 0, 10, BotResourceType.Stone)!;
        var result = InvokePrivateWithArgs(
            bot,
            "TryScoreReachableResourceCandidate",
            world,
            new BotNavigationSettings(0.3f, 1.8f, 7f),
            target,
            null!,
            0f,
            0f);

        Assert.True((bool)result.Result!);
        Assert.True(Assert.IsType<float>(result.Args[5]!) >= 0f);
    }

    [Fact(DisplayName = "CompanionBot не пытается заново искать маршрут стройки, пока активен no-path cooldown")]
    public void CompanionBot_UpdateBuildCommand_NoPathCooldownSkipsHeavyRetry()
    {
        var world = CreateFlatWorld(24, 12);
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Cooldown-build",
            originX: 4,
            floorY: 2,
            originZ: 4,
            steps:
            [
                new HouseBuildStep(8, 2, 8, BlockType.Wood)
            ]);

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 7.5f));
        InvokePrivate(bot, "AddStockpile", BlockType.Wood, 1);
        SetPrivateField(bot, "_activeCommand", BotCommand.BuildHouse(blueprint));
        SetPrivateField(bot, "_noPathTimer", 0.25f);

        InvokePrivate(bot, "UpdateBuildCommand", world, 1f / 30f);

        Assert.Equal(BotStatus.NoPath, bot.Status);
        Assert.Equal(BlockType.Air, world.GetBlock(8, 2, 8));
        Assert.Equal(1, bot.GetStockpile(BlockType.Wood));
    }

    [Fact(DisplayName = "CompanionBot не пытается заново добывать, пока активен no-path cooldown")]
    public void CompanionBot_UpdateGatherCommand_NoPathCooldownSkipsHeavyRetry()
    {
        var world = CreateFlatWorld(24, 12);
        world.SetBlock(8, 2, 8, BlockType.Wood);

        var bot = new CompanionBot(new GameConfig(), new Vector3(8.5f, 2.02f, 7.5f));
        SetPrivateField(bot, "_activeCommand", BotCommand.Gather(BotResourceType.Wood, 1));
        SetPrivateField(bot, "_noPathTimer", 0.25f);

        InvokePrivate(bot, "UpdateGatherCommand", world, BotResourceType.Wood, 1, 1f / 30f);

        Assert.Equal(BotStatus.NoPath, bot.Status);
        Assert.Equal(0, bot.GatheredAmount);
        Assert.Equal(BlockType.Wood, world.GetBlock(8, 2, 8));
    }

    [Fact(DisplayName = "CompanionBot score для reroute-кандидата штрафует дальние и нересурсные шаги")]
    public void CompanionBot_ScoreBuildRerouteCandidate_PenalizesDistanceAndMissingResources()
    {
        var bot = new CompanionBot(new GameConfig(), new Vector3(5.5f, 2.02f, 5.5f));
        var nearLoaded = (float)InvokePrivate(bot, "ScoreBuildRerouteCandidate", 1, new HouseBuildStep(6, 2, 5, BlockType.Wood), true)!;
        var farUnloaded = (float)InvokePrivate(bot, "ScoreBuildRerouteCandidate", 24, new HouseBuildStep(30, 6, 30, BlockType.Wood), false)!;

        Assert.True(farUnloaded > nearLoaded);
    }

    [Fact(DisplayName = "CompanionBot строит stage-route к далекой рабочей точке стройки вместо тяжелого прямого action-route")]
    public void CompanionBot_TryEnsureActionRoute_UsesStageRouteForFarBuildTarget()
    {
        var world = CreateFlatWorld(96, 12);
        var traces = new List<string>();
        var bot = new CompanionBot(new GameConfig(), new Vector3(6.5f, 2.02f, 6.5f), traces.Add);
        var purpose = ParseNestedEnum(typeof(CompanionBot), "NavigationPurpose", "BuildAction");
        var blueprint = new HouseBlueprint(
            HouseTemplateKind.CabinS,
            "Stage-route",
            originX: 52,
            floorY: 2,
            originZ: 52,
            steps:
            [
                new HouseBuildStep(58, 2, 58, BlockType.Wood)
            ]);

        Assert.True((bool)InvokePrivate(bot, "TryEnsureActionRoute", world, purpose, 58, 2, 58, 7, blueprint)!);
        Assert.Contains(traces, trace => trace.Contains("action-route-stage", StringComparison.Ordinal));
        Assert.NotEmpty((Vector3[])GetPrivateField(bot, "_navigationWaypoints")!);
    }

    [Fact(DisplayName = "Пауза больше не рисует bot-панель, а HUD по-прежнему показывает статусы бота")]
    public void GameApp_PauseMenu_DoesNotDrawBotPanel_AndHudStillShowsBotStatus()
    {
        var config = new GameConfig { FullscreenByDefault = false };
        var platform = new FakeGamePlatform();
        var world = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.02f, 8.5f));
        var companion = new CompanionBot(config, new Vector3(10.5f, 2.02f, 8.5f));

        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_companion", companion);
        SetPrivateField(app, "_state", ParseNestedEnum(typeof(GameApp), "AppState", "PauseMenu"));

        InvokePrivate(app, "DrawHud", false);
        InvokePrivate(app, "DrawMenu");

        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Бот:", StringComparison.Ordinal));
        Assert.DoesNotContain(platform.DrawnUiTexts, text => text.Contains("Наручный модуль бота", StringComparison.Ordinal));
        Assert.DoesNotContain(platform.DrawnUiTexts, text => text.Contains("Сбор ресурсов", StringComparison.Ordinal));
        Assert.DoesNotContain(platform.DrawnUiTexts, text => text.Contains("Построить Дом S", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "GameApp companion streaming покрывает early-return и оба режима стриминга")]
    public void GameApp_StreamCompanionWorkArea_CoversBranches()
    {
        var config = new GameConfig { FullscreenByDefault = false };
        var platform = new FakeGamePlatform();
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 8, seed: 0);
        var app = new GameApp(config, platform, world);
        var companion = new CompanionBot(config, new Vector3(8.5f, 2.02f, 8.5f));

        SetPrivateField(app, "_companion", null);
        InvokePrivate(app, "StreamCompanionWorkArea", false, false);

        SetPrivateField(app, "_companion", companion);
        InvokePrivate(app, "StreamCompanionWorkArea", false, false);

        Assert.True(companion.Enqueue(BotCommand.Gather(BotResourceType.Dirt, 1)));
        InvokePrivate(app, "StreamCompanionWorkArea", false, true);
        InvokePrivate(app, "StreamCompanionWorkArea", true, false);
    }

    [Fact(DisplayName = "GameApp рисует экран наручного устройства со статусом, сбором и количеством")]
    public void GameApp_DrawBotDeviceOverlay_ShowsGatherScreen()
    {
        var config = new GameConfig { FullscreenByDefault = false };
        var platform = new FakeGamePlatform();
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 8, seed: 0);
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.02f, 8.5f));
        var companion = new CompanionBot(config, new Vector3(10.5f, 2.02f, 8.5f));
        var device = GetPrivateField(app, "_botDevice")!;

        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_companion", companion);
        SetPrivateField(app, "_state", ParseNestedEnum(typeof(GameApp), "AppState", "Playing"));
        InvokePublic(device, "OpenMain");
        InvokePublic(device, "OpenGatherResource");
        InvokePublic(device, "SelectResource", BotResourceType.Stone);
        InvokePublic(device, "SetMessage", "Введите количество.");

        InvokePrivate(app, "DrawBotDeviceOverlay");

        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Наручный модуль бота", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Сбор ресурсов", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Количество: 16", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Камень", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Введите количество.", StringComparison.Ordinal));
        Assert.True(platform.DrawRectangleCalls > 0);
    }

    [Fact(DisplayName = "GameApp hotkey устройства открывает и закрывает модуль без перехода в паузу")]
    public void GameApp_HandlePlayingModeHotkeys_OpensAndClosesDeviceWithoutPause()
    {
        var config = new GameConfig { FullscreenByDefault = false };
        var platform = new FakeGamePlatform();
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 8, seed: 0);
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.02f, 8.5f));
        var companion = new CompanionBot(config, new Vector3(10.5f, 2.02f, 8.5f));
        var device = GetPrivateField(app, "_botDevice")!;

        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_companion", companion);
        SetPrivateField(app, "_state", ParseNestedEnum(typeof(GameApp), "AppState", "Playing"));

        platform.SetPressedKeys(KeyboardKey.B);
        Assert.True((bool)InvokePrivate(app, "HandlePlayingModeHotkeys")!);
        Assert.True((bool)device.GetType().GetProperty("IsOpen")!.GetValue(device)!);
        Assert.Equal("Playing", GetPrivateField(app, "_state")!.ToString());
        Assert.True(platform.EnableCursorCalled);

        platform.SetPressedKeys(KeyboardKey.Escape);
        Assert.True((bool)InvokePrivate(app, "HandlePlayingModeHotkeys")!);
        Assert.False((bool)device.GetType().GetProperty("IsOpen")!.GetValue(device)!);
        Assert.Equal("Playing", GetPrivateField(app, "_state")!.ToString());
        Assert.True(platform.DisableCursorCalled);
    }

    [Fact(DisplayName = "GameApp обрабатывает сбор, стройку и отмену через наручное устройство")]
    public void GameApp_HandleBotDeviceInput_ProcessesCommands()
    {
        var config = new GameConfig { FullscreenByDefault = false };
        var platform = new FakeGamePlatform();
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 8, seed: 0);
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.02f, 8.5f));
        var companion = new CompanionBot(config, new Vector3(10.5f, 2.02f, 8.5f));
        var device = GetPrivateField(app, "_botDevice")!;

        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_companion", companion);
        SetPrivateField(app, "_state", ParseNestedEnum(typeof(GameApp), "AppState", "Playing"));
        InvokePublic(device, "OpenMain");

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceGatherButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;
        AdvanceBotDeviceAction(app);
        Assert.Equal("GatherResource", device.GetType().GetProperty("Screen")!.GetValue(device)!.ToString());

        device.GetType().GetField("<AmountText>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(device, "0");
        platform.SetPressedKeys(KeyboardKey.Three);
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.SetPressedKeys(KeyboardKey.Two);
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.SetPressedKeys();

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceConfirmButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;
        Assert.NotNull(companion.ActiveCommand);
        Assert.Contains("x32", (string)device.GetType().GetProperty("Message")!.GetValue(device)!);

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceBackButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;
        AdvanceBotDeviceAction(app);
        Assert.Equal("Main", device.GetType().GetProperty("Screen")!.GetValue(device)!.ToString());

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceBuildButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;
        AdvanceBotDeviceAction(app);
        Assert.Equal("BuildHouse", device.GetType().GetProperty("Screen")!.GetValue(device)!.ToString());

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceConfirmButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;
        Assert.NotNull(companion.QueuedCommand);

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceBackButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;
        AdvanceBotDeviceAction(app);

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceCancelButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;

        Assert.Null(companion.ActiveCommand);
        Assert.Null(companion.QueuedCommand);
        Assert.Equal("Команды бота сброшены.", (string)device.GetType().GetProperty("Message")!.GetValue(device)!);
    }

    [Fact(DisplayName = "GameApp показывает переполнение очереди через наручное устройство для сбора и стройки")]
    public void GameApp_HandleBotDeviceInput_ShowsQueueFullMessages()
    {
        var config = new GameConfig { FullscreenByDefault = false };
        var platform = new FakeGamePlatform();
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 8, seed: 0);
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.02f, 8.5f));
        var companion = new CompanionBot(config, new Vector3(10.5f, 2.02f, 8.5f));
        var device = GetPrivateField(app, "_botDevice")!;

        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_companion", companion);
        SetPrivateField(app, "_state", ParseNestedEnum(typeof(GameApp), "AppState", "Playing"));
        InvokePublic(device, "OpenMain");

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceGatherButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        AdvanceBotDeviceAction(app);
        platform.MousePosition = Center(GetRect(app, "GetBotDeviceConfirmButtonRect"));
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceBackButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;
        AdvanceBotDeviceAction(app);

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceBuildButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        AdvanceBotDeviceAction(app);
        platform.MousePosition = Center(GetRect(app, "GetBotDeviceConfirmButtonRect"));
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceBackButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;
        AdvanceBotDeviceAction(app);

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceGatherButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        AdvanceBotDeviceAction(app);
        platform.MousePosition = Center(GetRect(app, "GetBotDeviceConfirmButtonRect"));
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;
        Assert.Equal("Очередь бота заполнена.", (string)device.GetType().GetProperty("Message")!.GetValue(device)!);

        companion.CancelAll();
        InvokePublic(device, "BackToMain");

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceBuildButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        AdvanceBotDeviceAction(app);
        platform.MousePosition = Center(GetRect(app, "GetBotDeviceConfirmButtonRect"));
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceBackButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;
        AdvanceBotDeviceAction(app);

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceGatherButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        AdvanceBotDeviceAction(app);
        platform.MousePosition = Center(GetRect(app, "GetBotDeviceConfirmButtonRect"));
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceBackButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;
        AdvanceBotDeviceAction(app);

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceBuildButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        AdvanceBotDeviceAction(app);
        platform.MousePosition = Center(GetRect(app, "GetBotDeviceConfirmButtonRect"));
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;

        Assert.Equal("Очередь бота заполнена.", (string)device.GetType().GetProperty("Message")!.GetValue(device)!);
    }

    [Fact(DisplayName = "GameApp рисует устройство в первом лице и позу рук с устройством в третьем лице")]
    public void GameApp_DrawDeviceBranches_Work()
    {
        var config = new GameConfig { FullscreenByDefault = false };
        var platform = new FakeGamePlatform();
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 8, seed: 0);
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.02f, 8.5f));
        var companion = new CompanionBot(config, new Vector3(10.5f, 2.02f, 8.5f));
        var device = GetPrivateField(app, "_botDevice")!;
        var visual = GetPrivateField(app, "_botDeviceVisual")!;

        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_companion", companion);
        SetPrivateField(app, "_state", ParseNestedEnum(typeof(GameApp), "AppState", "Playing"));
        InvokePublic(device, "OpenMain");
        InvokePublic(visual, "Update", true, 0.1f);
        InvokePublic(visual, "TriggerTap");
        SetPrivateField(app, "_cameraMode", ParseInternalEnum("AIG.Game.Core.CameraMode", "FirstPerson"));

        InvokePrivate(app, "DrawFirstPersonHand", new Camera3D
        {
            Position = player.EyePosition,
            Target = player.EyePosition + player.LookDirection,
            Up = Vector3.UnitY,
            FovY = 60f,
            Projection = CameraProjection.Perspective
        });

        SetPrivateField(app, "_cameraMode", ParseInternalEnum("AIG.Game.Core.CameraMode", "ThirdPerson"));
        InvokePrivate(app, "DrawPlayerAvatar");

        Assert.True(platform.DrawCubeCalls >= 6);
    }

    [Fact(DisplayName = "Run открывает наручный модуль в игровом цикле без паузы")]
    public void Run_BotDeviceHotkey_OpensOverlayWithoutPause()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: Vector2.Zero, pressedKeys: [KeyboardKey.B]);
        platform.EnqueueFrameInput(mousePosition: Vector2.Zero);

        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false },
            platform,
            new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 777));

        app.Run();

        Assert.True(platform.EnableCursorCalled);
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Наручный модуль бота", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "GameApp покрывает ранние return ветки наручного модуля")]
    public void GameApp_BotDeviceEarlyReturnBranches_Work()
    {
        var (app, platform, _, _, companion, device, _) = CreatePlayingBotDeviceApp();

        SetPrivateField(app, "_state", ParseNestedEnum(typeof(GameApp), "AppState", "PauseMenu"));
        InvokePrivate(app, "OpenBotDevice");
        Assert.False(device.IsOpen);

        SetPrivateField(app, "_state", ParseNestedEnum(typeof(GameApp), "AppState", "Playing"));
        SetPrivateField(app, "_companion", null);
        InvokePrivate(app, "OpenBotDevice");
        Assert.False(device.IsOpen);

        InvokePrivate(app, "CloseBotDevice");
        Assert.False(platform.DisableCursorCalled);

        SetDeviceAmount(device, "9");
        device.SetMessage("before");
        InvokePrivate(app, "QueueGatherFromDevice");
        Assert.Equal("before", device.Message);

        device.OpenBuildHouse();
        InvokePrivate(app, "QueueBuildFromDevice");
        Assert.Equal("before", device.Message);

        device.OpenMain();
        SetPrivateField(app, "_companion", companion);
        SetPrivateField(app, "_companion", null);
        InvokePrivate(app, "HandleBotDeviceInput");

        SetPrivateField(app, "_companion", companion);
        device.Close();
        InvokePrivate(app, "HandleBotDeviceInput");

        device.OpenMain();
        platform.SetPressedKeys();
        Assert.False((bool)InvokePrivate(app, "HandlePlayingModeHotkeys")!);

        platform.SetPressedKeys(KeyboardKey.B);
        Assert.True((bool)InvokePrivate(app, "HandlePlayingModeHotkeys")!);
        Assert.False(device.IsOpen);
    }

    [Fact(DisplayName = "GameApp покрывает Backspace, Enter и валидацию количества в наручном модуле")]
    public void GameApp_HandleBotDeviceInput_CoversKeyboardAndValidationBranches()
    {
        var (app, platform, _, _, companion, device, _) = CreatePlayingBotDeviceApp();

        device.OpenGatherResource();

        SetDeviceAmount(device, string.Empty);
        platform.SetPressedKeys(KeyboardKey.Backspace);
        InvokePrivate(app, "HandleBotDeviceInput");

        SetDeviceAmount(device, "18");
        InvokePrivate(app, "HandleBotDeviceInput");
        Assert.Equal("1", device.AmountText);
        Assert.Contains("Количество: 1", device.Message, StringComparison.Ordinal);

        SetDeviceAmount(device, "0");
        platform.SetPressedKeys(KeyboardKey.Enter);
        InvokePrivate(app, "HandleBotDeviceInput");
        Assert.Equal("Введите количество больше нуля.", device.Message);

        SetDeviceAmount(device, "5");
        InvokePrivate(app, "HandleBotDeviceInput");
        Assert.NotNull(companion.ActiveCommand);
        Assert.Contains("x5", device.Message, StringComparison.Ordinal);

        device.OpenBuildHouse();
        InvokePrivate(app, "HandleBotDeviceInput");
        Assert.NotNull(companion.QueuedCommand);
    }

    [Fact(DisplayName = "GameApp покрывает выбор ресурсов и закрытие наручного модуля")]
    public void GameApp_HandleBotDeviceInput_CoversResourceButtonsAndCloseAction()
    {
        var (app, platform, _, _, _, device, _) = CreatePlayingBotDeviceApp();

        device.OpenGatherResource();

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceWoodButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        Assert.Equal(BotResourceType.Wood, device.SelectedResource);

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceStoneButtonRect"));
        InvokePrivate(app, "HandleBotDeviceInput");
        Assert.Equal(BotResourceType.Stone, device.SelectedResource);

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceDirtButtonRect"));
        InvokePrivate(app, "HandleBotDeviceInput");
        Assert.Equal(BotResourceType.Dirt, device.SelectedResource);

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceLeavesButtonRect"));
        InvokePrivate(app, "HandleBotDeviceInput");
        Assert.Equal(BotResourceType.Leaves, device.SelectedResource);
        Assert.Contains("Листва", device.Message, StringComparison.Ordinal);

        device.BackToMain();
        platform.MousePosition = Center(GetRect(app, "GetBotDeviceCloseButtonRect"));
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;
        AdvanceBotDeviceAction(app);

        Assert.False(device.IsOpen);
        Assert.True(platform.DisableCursorCalled);
    }

    [Fact(DisplayName = "GameApp не обрабатывает новый ввод браслета, пока висит pending tap-переход")]
    public void GameApp_HandleBotDeviceInput_IgnoresInputWhileActionPending()
    {
        var (app, platform, _, _, _, device, _) = CreatePlayingBotDeviceApp();

        device.OpenMain();
        InvokePrivate(app, "QueueBotDeviceAction", ParseNestedEnum(typeof(GameApp), "BotDeviceAction", "OpenGatherResource"), BotWristDeviceTarget.Gather);

        platform.MousePosition = Center(GetRect(app, "GetBotDeviceBuildButtonRect"));
        platform.LeftMousePressed = true;
        InvokePrivate(app, "HandleBotDeviceInput");
        platform.LeftMousePressed = false;

        Assert.Equal("Main", device.Screen.ToString());
        AdvanceBotDeviceAction(app);
        Assert.Equal("GatherResource", device.Screen.ToString());
    }

    [Fact(DisplayName = "GameApp QueueBotDeviceAction игнорирует None")]
    public void GameApp_QueueBotDeviceAction_IgnoresNone()
    {
        var (app, _, _, _, _, _, visual) = CreatePlayingBotDeviceApp();

        InvokePrivate(app, "QueueBotDeviceAction", ParseNestedEnum(typeof(GameApp), "BotDeviceAction", "None"), BotWristDeviceTarget.None);

        Assert.Equal(0f, visual.TapBlend);
        Assert.Equal(BotWristDeviceTarget.None, visual.TapTarget);
    }

    [Fact(DisplayName = "GameApp покрывает ReadBotDeviceAction, DrawFrame и DrawBotDeviceOverlay для main/build/early-return")]
    public void GameApp_BotDeviceActionAndOverlayBranches_Work()
    {
        var (app, platform, world, player, companion, device, _) = CreatePlayingBotDeviceApp();

        Assert.Equal("None", InvokePrivate(app, "ReadBotDeviceAction")!.ToString());

        var drawnBefore = platform.DrawnUiTexts.Count;
        InvokePrivate(app, "DrawBotDeviceOverlay");
        Assert.Equal(drawnBefore, platform.DrawnUiTexts.Count);

        device.OpenMain();
        SetPrivateField(app, "_companion", null);
        InvokePrivate(app, "DrawBotDeviceOverlay");

        SetPrivateField(app, "_companion", companion);
        platform.MousePosition = Vector2.Zero;
        platform.LeftMousePressed = false;
        Assert.Equal("None", InvokePrivate(app, "ReadBotDeviceAction")!.ToString());

        var overlayRectanglesBefore = platform.DrawRectangleCalls;
        var overlayLinesBefore = platform.DrawLineCalls;
        InvokePrivate(app, "DrawBotDeviceOverlay");
        Assert.True(platform.DrawRectangleCalls > overlayRectanglesBefore);
        Assert.True(platform.DrawLineCalls > overlayLinesBefore);

        var frameRectanglesBefore = platform.DrawRectangleCalls;
        var view = CameraViewBuilder.Build(player, world, AIG.Game.Core.CameraMode.FirstPerson, 0f);
        InvokePrivate(app, "DrawFrame", null, view);
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Убрать устройство", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("B / ESC: убрать модуль", StringComparison.Ordinal));
        Assert.True(platform.DrawRectangleCalls >= frameRectanglesBefore);

        device.OpenBuildHouse();
        Assert.Equal("None", InvokePrivate(app, "ReadBotDeviceAction")!.ToString());
        var buildRectanglesBefore = platform.DrawRectangleCalls;
        InvokePrivate(app, "DrawBotDeviceOverlay");
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Строительство", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Шаблон: Дом S", StringComparison.Ordinal));
        Assert.True(platform.DrawRectangleCalls > buildRectanglesBefore);

        _ = GetRect(app, "GetBotDeviceCloseButtonRect");
    }

    [Fact(DisplayName = "GameApp рисует screen-space руку нажатия поверх панели браслета")]
    public void GameApp_DrawBotDeviceOverlay_DrawsTapHandOverlay()
    {
        var (app, platform, _, _, _, device, visual) = CreatePlayingBotDeviceApp();

        device.OpenGatherResource();
        visual.TriggerTap(BotWristDeviceTarget.Wood);
        var rectanglesBefore = platform.DrawRectangleCalls;

        InvokePrivate(app, "DrawBotDeviceOverlay");

        Assert.True(platform.DrawRectangleCalls >= rectanglesBefore + 12);
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Дерево", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "GameApp по F3 переключает компактный и расширенный HUD")]
    public void GameApp_HandleGlobalUiHotkeys_TogglesDebugHud()
    {
        var config = new GameConfig { FullscreenByDefault = false };
        var platform = new FakeGamePlatform();
        var world = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.02f, 8.5f));
        var companion = new CompanionBot(config, new Vector3(10.5f, 2.02f, 8.5f));

        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_companion", companion);
        SetPrivateField(app, "_state", ParseNestedEnum(typeof(GameApp), "AppState", "Playing"));

        InvokePrivate(app, "DrawHud", false);
        Assert.DoesNotContain(platform.DrawnUiTexts, text => text.Contains("Pos:", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Бот:", StringComparison.Ordinal));

        platform.SetPressedKeys(KeyboardKey.F3);
        Assert.True((bool)InvokePrivate(app, "HandleGlobalUiHotkeys")!);
        InvokePrivate(app, "DrawHud", false);

        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("DEBUG HUD", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Pos:", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "GameApp рисует debug HUD без ветки бота, если спутник отсутствует")]
    public void GameApp_DrawHud_DebugWithoutCompanion_CoversNullBranch()
    {
        var config = new GameConfig { FullscreenByDefault = false };
        var platform = new FakeGamePlatform();
        var world = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.02f, 8.5f));

        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_companion", null);
        SetPrivateField(app, "_state", ParseNestedEnum(typeof(GameApp), "AppState", "Playing"));

        platform.SetPressedKeys(KeyboardKey.F3);
        Assert.True((bool)InvokePrivate(app, "HandleGlobalUiHotkeys")!);
        InvokePrivate(app, "DrawHud", false);

        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("DEBUG HUD", StringComparison.Ordinal));
        Assert.DoesNotContain(platform.DrawnUiTexts, text => text.Contains("Бот:", StringComparison.Ordinal));
    }

    private static void WarmWorld(WorldMap world, Vector3 position)
    {
        world.EnsureChunksAround(position, radiusInChunks: 3);
        _ = world.RebuildDirtyChunkSurfaces(position, maxChunks: 256);
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

    private static GameApp CreateBotMenuApp(out FakeGamePlatform platform)
    {
        var config = new GameConfig { FullscreenByDefault = false };
        platform = new FakeGamePlatform();
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 8, seed: 0);
        return new GameApp(config, platform, world);
    }

    private static (GameApp App, FakeGamePlatform Platform, WorldMap World, PlayerController Player, CompanionBot Companion, BotWristDeviceState Device, BotWristDeviceVisualState Visual) CreatePlayingBotDeviceApp()
    {
        var config = new GameConfig { FullscreenByDefault = false };
        var platform = new FakeGamePlatform();
        var world = new WorldMap(width: 64, height: 16, depth: 64, chunkSize: 8, seed: 0);
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.02f, 8.5f));
        var companion = new CompanionBot(config, new Vector3(10.5f, 2.02f, 8.5f));
        var device = (BotWristDeviceState)GetPrivateField(app, "_botDevice")!;
        var visual = (BotWristDeviceVisualState)GetPrivateField(app, "_botDeviceVisual")!;

        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_companion", companion);
        SetPrivateField(app, "_state", ParseNestedEnum(typeof(GameApp), "AppState", "Playing"));

        return (app, platform, world, player, companion, device, visual);
    }

    private static void StepBot(CompanionBot bot, WorldMap world, Vector3 playerPosition, int frames)
    {
        StepBot(bot, world, playerPosition, new Vector3(1f, 0f, 0f), frames, null);
    }

    private static void StepBot(CompanionBot bot, WorldMap world, Vector3 playerPosition, Vector3 playerLookDirection, int frames)
    {
        StepBot(bot, world, playerPosition, playerLookDirection, frames, null);
    }

    private static void StepBot(CompanionBot bot, WorldMap world, Vector3 playerPosition, Vector3 playerLookDirection, int frames, Func<Vector3, bool>? playerCollisionProbe)
    {
        for (var i = 0; i < frames; i++)
        {
            world.EnsureChunksAround(bot.Position, radiusInChunks: 3);
            _ = world.RebuildDirtyChunkSurfaces(bot.Position, maxChunks: 256);
            bot.Update(world, playerPosition, playerLookDirection, 1f / 30f, playerCollisionProbe);
        }
    }

    private static object ParseNestedEnum(Type owner, string nestedTypeName, string value)
    {
        var nested = owner.GetNestedType(nestedTypeName, BindingFlags.NonPublic);
        Assert.NotNull(nested);
        return Enum.Parse(nested!, value);
    }

    private static object ParseInternalEnum(string fullTypeName, string value)
    {
        var enumType = typeof(GameApp).Assembly.GetType(fullTypeName);
        Assert.NotNull(enumType);
        return Enum.Parse(enumType!, value);
    }

    private static object? GetPrivateField(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(target);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static object? InvokePrivate(object target, string methodName, params object?[]? args)
    {
        var method = FindPrivateMethod(target.GetType(), methodName, BindingFlags.Instance | BindingFlags.NonPublic, args);
        Assert.NotNull(method);
        return method!.Invoke(target, args);
    }

    private static object? InvokePublic(object target, string methodName, params object?[]? args)
    {
        var method = FindPrivateMethod(target.GetType(), methodName, BindingFlags.Instance | BindingFlags.Public, args);
        Assert.NotNull(method);
        return method!.Invoke(target, args);
    }

    private static object? InvokePrivateStatic(Type type, string methodName, params object?[]? args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(null, args);
    }

    private static (object? Result, object?[] Args) InvokePrivateWithArgs(object target, string methodName, params object?[] args)
    {
        var method = FindPrivateMethod(target.GetType(), methodName, BindingFlags.Instance | BindingFlags.NonPublic, args);
        Assert.NotNull(method);
        var result = method!.Invoke(target, args);
        return (result, args);
    }

    private static MethodInfo? FindPrivateMethod(Type type, string methodName, BindingFlags flags, object?[]? args)
    {
        var candidates = type.GetMethods(flags)
            .Where(method => method.Name == methodName);

        if (args is not null)
        {
            candidates = candidates.Where(method => method.GetParameters().Length == args.Length);
        }

        return candidates
            .OrderByDescending(method => method.GetParameters().Length)
            .FirstOrDefault();
    }

    private static (int X, int Y, int W, int H) GetRect(object target, string methodName)
    {
        return ((int X, int Y, int W, int H))InvokePrivate(target, methodName)!;
    }

    private static Vector2 Center((int X, int Y, int W, int H) rect)
    {
        return new Vector2(rect.X + rect.W / 2f, rect.Y + rect.H / 2f);
    }

    private static void AdvanceBotDeviceAction(GameApp app, float deltaTime = 0.2f)
    {
        InvokePrivate(app, "AdvancePendingBotDeviceAction", deltaTime);
    }

    private static void SetDeviceAmount(BotWristDeviceState device, string amount)
    {
        typeof(BotWristDeviceState)
            .GetField("<AmountText>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(device, amount);
    }

    private static object CreateNavigationGoal(string purposeName, int x, int y, int z, int radius, HouseBlueprint? blueprint)
    {
        var goalType = typeof(CompanionBot).GetNestedType("NavigationGoal", BindingFlags.NonPublic);
        Assert.NotNull(goalType);

        var constructor = goalType!.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single();
        var purpose = ParseNestedEnum(typeof(CompanionBot), "NavigationPurpose", purposeName);
        return constructor.Invoke([purpose, x, y, z, radius, blueprint]);
    }

    private static object CreateResourceTarget(int x, int y, int z, BotResourceType resource)
    {
        var targetType = typeof(CompanionBot).GetNestedType("ResourceTarget", BindingFlags.NonPublic);
        Assert.NotNull(targetType);
        return Activator.CreateInstance(targetType!, [x, y, z, resource])!;
    }
}
