using System.Numerics;
using AIG.Game.Config;
using AIG.Game.Player;
using AIG.Game.World;

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
