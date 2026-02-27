using System.Numerics;
using AIG.Game.Gameplay;
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
