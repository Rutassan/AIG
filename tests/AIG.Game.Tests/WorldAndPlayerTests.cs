using System.Numerics;
using System.Reflection;
using AIG.Game.Config;
using AIG.Game.Player;
using AIG.Game.World;
using AIG.Game.World.Chunks;

namespace AIG.Game.Tests;

public sealed class WorldAndPlayerTests
{
    [Fact(DisplayName = "Генерация мира: нижний слой камень, верхний слой земля, выше воздух")]
    public void World_GeneratesExpectedFlatLayers()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8);

        Assert.Equal(BlockType.Stone, world.GetBlock(3, 0, 3));
        Assert.Equal(BlockType.Dirt, world.GetBlock(3, 1, 3));
        Assert.Equal(BlockType.Air, world.GetBlock(3, 2, 3));
    }

    [Fact(DisplayName = "Мир возвращает воздух и не пишет блок за пределами по всем осям")]
    public void World_BoundsChecks_WorkOnAllAxes()
    {
        var world = new WorldMap(width: 4, height: 4, depth: 4);

        Assert.Equal(BlockType.Air, world.GetBlock(-1, 1, 1));
        Assert.Equal(BlockType.Air, world.GetBlock(4, 1, 1));
        Assert.Equal(BlockType.Air, world.GetBlock(1, -1, 1));
        Assert.Equal(BlockType.Air, world.GetBlock(1, 4, 1));
        Assert.Equal(BlockType.Air, world.GetBlock(1, 1, -1));
        Assert.Equal(BlockType.Air, world.GetBlock(1, 1, 4));

        world.SetBlock(-1, 2, 2, BlockType.Stone);
        world.SetBlock(4, 2, 2, BlockType.Stone);
        world.SetBlock(2, -1, 2, BlockType.Stone);
        world.SetBlock(2, 4, 2, BlockType.Stone);
        world.SetBlock(2, 2, -1, BlockType.Stone);
        world.SetBlock(2, 2, 4, BlockType.Stone);

        Assert.Equal(BlockType.Air, world.GetBlock(2, 2, 2));
    }

    [Fact(DisplayName = "Чанковый доступ корректно работает на границе чанка")]
    public void World_ChunkBoundaryAccess_Works()
    {
        var world = new WorldMap(width: 64, height: 8, depth: 64, chunkSize: 16, seed: 0);
        world.SetBlock(15, 2, 15, BlockType.Stone);
        world.SetBlock(16, 2, 16, BlockType.Dirt);

        Assert.Equal(BlockType.Stone, world.GetBlock(15, 2, 15));
        Assert.Equal(BlockType.Dirt, world.GetBlock(16, 2, 16));
    }

    [Fact(DisplayName = "Генерация по seed повторяема между экземплярами мира")]
    public void World_SeedGeneration_IsDeterministic()
    {
        var worldA = new WorldMap(width: 64, height: 8, depth: 64, chunkSize: 16, seed: 12345);
        var worldB = new WorldMap(width: 64, height: 8, depth: 64, chunkSize: 16, seed: 12345);

        Assert.Equal(worldA.GetBlock(10, 2, 10), worldB.GetBlock(10, 2, 10));
        Assert.Equal(worldA.GetBlock(22, 2, 31), worldB.GetBlock(22, 2, 31));
        Assert.Equal(worldA.GetBlock(40, 2, 5), worldB.GetBlock(40, 2, 5));
    }

    [Fact(DisplayName = "Лесной мир имеет травяной верхний слой на поверхности")]
    public void World_SeededGeneration_UsesGrassOnTopLayer()
    {
        var world = new WorldMap(width: 64, height: 32, depth: 64, chunkSize: 16, seed: 777);
        var x = 20;
        var z = 20;
        var topY = world.GetTerrainTopY(x, z);

        Assert.Equal(BlockType.Grass, world.GetBlock(x, topY, z));
        Assert.NotEqual(BlockType.Air, world.GetBlock(x, Math.Max(0, topY - 1), z));
    }

    [Fact(DisplayName = "Стриминг чанков загружает, помечает и выгружает дальние чанки")]
    public void World_ChunkStreaming_LoadAndUnload_Works()
    {
        var world = new WorldMap(width: 96, height: 32, depth: 96, chunkSize: 16, seed: 777);

        Assert.Equal(0, world.LoadedChunkCount);
        Assert.False(world.IsChunkLoaded(1, 1));

        world.UnloadFarChunks(new Vector3(0f, 0f, 0f), keepRadiusInChunks: 0);
        Assert.Equal(0, world.LoadedChunkCount);

        world.EnsureChunksAround(new Vector3(24f, 0f, 24f), radiusInChunks: 1);

        Assert.True(world.LoadedChunkCount >= 4);
        Assert.True(world.IsChunkLoaded(1, 1));

        var loadedBeforeUnload = world.LoadedChunkCount;
        world.UnloadFarChunks(new Vector3(8f, 0f, 8f), keepRadiusInChunks: 0);
        Assert.True(world.LoadedChunkCount < loadedBeforeUnload);

        world.UnloadFarChunks(new Vector3(8f, 0f, 8f), keepRadiusInChunks: -1);
        Assert.Equal(0, world.LoadedChunkCount);
    }

    [Fact(DisplayName = "GetTerrainTopY обрабатывает границы, а пустой мир не загружает чанки")]
    public void World_TerrainTopAndEmptyWorldBranches_AreCovered()
    {
        var world = new WorldMap(width: 48, height: 24, depth: 48, chunkSize: 16, seed: 777);

        Assert.Equal(0, world.GetTerrainTopY(-1, 10));
        Assert.Equal(0, world.GetTerrainTopY(10, 48));
        Assert.InRange(world.GetTerrainTopY(10, 10), 2, 22);

        var emptyWorld = new WorldMap(width: 0, height: 8, depth: 0, chunkSize: 16, seed: 777);
        emptyWorld.EnsureChunksAround(0, 0, 2);
        Assert.Equal(0, emptyWorld.LoadedChunkCount);
    }

    [Fact(DisplayName = "Изменения блоков сохраняются после выгрузки и повторной генерации чанка")]
    public void World_BlockOverride_PersistsAfterChunkReload()
    {
        var world = new WorldMap(width: 96, height: 40, depth: 96, chunkSize: 16, seed: 777);
        world.SetBlock(20, 9, 20, BlockType.Wood);

        world.UnloadFarChunks(new Vector3(0f, 0f, 0f), keepRadiusInChunks: -1);
        Assert.Equal(0, world.LoadedChunkCount);

        world.EnsureChunksAround(20, 20, 0);
        Assert.Equal(BlockType.Wood, world.GetBlock(20, 9, 20));
    }

    [Fact(DisplayName = "Лесная генерация создаёт дерево и листву в зоне загрузки")]
    public void World_ForestGeneration_CreatesWoodAndLeaves()
    {
        var world = new WorldMap(width: 600, height: 72, depth: 600, chunkSize: 16, seed: 777);
        world.EnsureChunksAround(new Vector3(300f, 0f, 300f), radiusInChunks: 2);

        var woodCount = 0;
        var leavesCount = 0;
        for (var x = 260; x <= 340; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 260; z <= 340; z++)
                {
                    var block = world.GetBlock(x, y, z);
                    if (block == BlockType.Wood)
                    {
                        woodCount++;
                    }
                    else if (block == BlockType.Leaves)
                    {
                        leavesCount++;
                    }
                }
            }
        }

        Assert.True(woodCount > 0, "Ожидали хотя бы одно дерево (ствол).");
        Assert.True(leavesCount > 0, "Ожидали хотя бы одну листву.");
    }

    [Fact(DisplayName = "SetBlockInChunk игнорирует блоки вне мира по высоте")]
    public void World_SetBlockInChunk_IgnoresOutOfBoundsHeight()
    {
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 16, seed: 777);
        var method = typeof(WorldMap).GetMethod("SetBlockInChunk", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var chunk = new Chunk(size: 16, height: 16);
        method!.Invoke(world, [chunk, 0, 0, 2, -1, 2, BlockType.Leaves, true]);

        Assert.Equal(BlockType.Air, chunk.Get(2, 0, 2));
    }

    [Fact(DisplayName = "FractalNoise возвращает 0 при нулевом числе октав")]
    public void World_FractalNoise_ZeroOctaves_ReturnsZero()
    {
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 16, seed: 777);
        var method = typeof(WorldMap).GetMethod("FractalNoise", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var value = (float)method!.Invoke(world, [0.5f, 0.75f, 0, 2f, 0.5f])!;
        Assert.Equal(0f, value);
    }

    [Fact(DisplayName = "Свойства чанка Size и Height доступны корректно")]
    public void Chunk_SizeAndHeight_AreAccessible()
    {
        var chunk = new Chunk(size: 16, height: 24);
        Assert.Equal(16, chunk.Size);
        Assert.Equal(24, chunk.Height);
    }

    [Fact(DisplayName = "Игрок падает на землю и корректно становится на поверхность")]
    public void Player_FallsAndGetsGrounded()
    {
        var world = new WorldMap(width: 16, height: 16, depth: 16);
        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(6f, 5f, 6f));

        for (var i = 0; i < 300; i++)
        {
            player.Update(world, new PlayerInput(0f, 0f, false, 0f, 0f), 1f / 120f);
        }

        Assert.True(player.IsGrounded);
        Assert.InRange(player.Position.Y, 1.98f, 2.02f);
    }

    [Fact(DisplayName = "Игрок не проходит сквозь блок по горизонтали")]
    public void Player_CannotMoveThroughWall()
    {
        var world = new WorldMap(width: 16, height: 16, depth: 16);
        world.SetBlock(2, 2, 4, BlockType.Stone);

        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(4.2f, 2f, 4.5f));

        for (var i = 0; i < 180; i++)
        {
            player.Update(world, new PlayerInput(0f, 1f, false, 0f, 0f), 1f / 120f);
        }

        Assert.True(player.Position.X >= 3.29f, $"Ожидалась граница у стены, фактический X={player.Position.X:0.000}");
    }

    [Fact(DisplayName = "Вертикальный обзор ограничен безопасным углом")]
    public void Player_LookPitchIsClamped()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8);
        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(4f, 2f, 4f));

        player.Update(world, new PlayerInput(0f, 0f, false, 0f, -100_000f), 1f / 60f);
        Assert.InRange(player.Pitch, 1.53f, 1.54f);

        player.Update(world, new PlayerInput(0f, 0f, false, 0f, 100_000f), 1f / 60f);
        Assert.InRange(player.Pitch, -1.54f, -1.53f);
    }

    [Fact(DisplayName = "Свойства камеры игрока корректны: позиция глаз и вектор взгляда")]
    public void Player_EyeAndLookDirection_AreValid()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8);
        var player = new PlayerController(new GameConfig(), new Vector3(4f, 2f, 4f));

        _ = world;
        Assert.InRange(player.EyePosition.Y, 3.65f, 3.66f);

        var direction = player.LookDirection;
        Assert.InRange(direction.Length(), 0.999f, 1.001f);
        Assert.True(direction.Z < 0f);
        Assert.Equal(0.3f, player.ColliderHalfWidth);
        Assert.Equal(1.8f, player.ColliderHeight);
    }

    [Fact(DisplayName = "SetPose задает позицию и направление взгляда игрока")]
    public void Player_SetPose_UpdatesPositionAndLook()
    {
        var player = new PlayerController(new GameConfig(), new Vector3(4f, 2f, 4f));
        var newPosition = new Vector3(10f, 3f, 10f);
        var lookDirection = new Vector3(1f, -0.2f, -1f);

        player.SetPose(newPosition, lookDirection);

        Assert.Equal(newPosition, player.Position);
        var expected = Vector3.Normalize(lookDirection);
        var actual = player.LookDirection;
        Assert.True(MathF.Abs(actual.X - expected.X) < 0.001f);
        Assert.True(MathF.Abs(actual.Y - expected.Y) < 0.001f);
        Assert.True(MathF.Abs(actual.Z - expected.Z) < 0.001f);
    }

    [Fact(DisplayName = "SetPose с нулевым вектором взгляда меняет позицию без изменения ориентации")]
    public void Player_SetPose_ZeroLookDirection_OnlyMovesPlayer()
    {
        var player = new PlayerController(new GameConfig(), new Vector3(4f, 2f, 4f));
        var initialDirection = player.LookDirection;
        var targetPosition = new Vector3(7f, 3f, 8f);

        player.SetPose(targetPosition, Vector3.Zero);

        Assert.Equal(targetPosition, player.Position);
        var actualDirection = player.LookDirection;
        Assert.True(MathF.Abs(actualDirection.X - initialDirection.X) < 0.001f);
        Assert.True(MathF.Abs(actualDirection.Y - initialDirection.Y) < 0.001f);
        Assert.True(MathF.Abs(actualDirection.Z - initialDirection.Z) < 0.001f);
    }

    [Fact(DisplayName = "Прыжок под потолок останавливает вертикальную скорость без прохода сквозь блок")]
    public void Player_JumpIntoCeiling_DoesNotClipThrough()
    {
        var world = new WorldMap(width: 16, height: 16, depth: 16);
        world.SetBlock(6, 4, 6, BlockType.Stone);

        var player = new PlayerController(new GameConfig(), new Vector3(6f, 2f, 6f));
        player.Update(world, new PlayerInput(0f, 0f, true, 0f, 0f), 1f / 60f);

        for (var i = 0; i < 90; i++)
        {
            player.Update(world, new PlayerInput(0f, 0f, false, 0f, 0f), 1f / 120f);
        }

        Assert.True(player.Position.Y < 2.21f, $"Игрок не должен пройти через потолок. Y={player.Position.Y:0.000}");
    }

    [Fact(DisplayName = "Движение на D смещает игрока вправо по оси X")]
    public void Player_MoveRight_KeyD_IncreasesX()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64);
        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(32f, 2f, 32f));
        var startX = player.Position.X;

        player.Update(world, new PlayerInput(0f, 1f, false, 0f, 0f), 1f / 10f);

        Assert.True(player.Position.X > startX, $"Ожидали рост X, фактический X={player.Position.X:0.000}, старт={startX:0.000}");
    }

    [Fact(DisplayName = "Движение на A смещает игрока влево по оси X")]
    public void Player_MoveLeft_KeyA_DecreasesX()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64);
        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(32f, 2f, 32f));
        var startX = player.Position.X;

        player.Update(world, new PlayerInput(0f, -1f, false, 0f, 0f), 1f / 10f);

        Assert.True(player.Position.X < startX, $"Ожидали уменьшение X, фактический X={player.Position.X:0.000}, старт={startX:0.000}");
    }

    [Fact(DisplayName = "Движение на W смещает игрока вперед по оси Z")]
    public void Player_MoveForward_KeyW_DecreasesZ()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64);
        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(32f, 2f, 32f));
        var startZ = player.Position.Z;

        player.Update(world, new PlayerInput(1f, 0f, false, 0f, 0f), 1f / 10f);

        Assert.True(player.Position.Z < startZ, $"Ожидали уменьшение Z, фактический Z={player.Position.Z:0.000}, старт={startZ:0.000}");
    }

    [Fact(DisplayName = "Движение на S смещает игрока назад по оси Z")]
    public void Player_MoveBackward_KeyS_IncreasesZ()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64);
        var config = new GameConfig();
        var player = new PlayerController(config, new Vector3(32f, 2f, 32f));
        var startZ = player.Position.Z;

        player.Update(world, new PlayerInput(-1f, 0f, false, 0f, 0f), 1f / 10f);

        Assert.True(player.Position.Z > startZ, $"Ожидали рост Z, фактический Z={player.Position.Z:0.000}, старт={startZ:0.000}");
    }

    [Fact(DisplayName = "Диагональное движение нормализуется по скорости")]
    public void Player_DiagonalMove_IsNormalized()
    {
        var world = new WorldMap(width: 64, height: 16, depth: 64);
        var player = new PlayerController(new GameConfig(), new Vector3(32f, 2f, 32f));

        var start = player.Position;
        player.Update(world, new PlayerInput(1f, 1f, false, 0f, 0f), 1f / 10f);
        var moved = Vector3.Distance(start, player.Position);

        Assert.InRange(moved, 0.54f, 0.56f);
    }
}
