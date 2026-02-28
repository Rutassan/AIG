using System.Numerics;
using System.Reflection;
using AIG.Game.Config;
using AIG.Game.Core;
using AIG.Game.Player;
using AIG.Game.Tests.Fakes;
using AIG.Game.World;
using Raylib_cs;
using GameCameraMode = AIG.Game.Core.CameraMode;

namespace AIG.Game.Tests;

public sealed class CameraAndVisualTests
{
    [Fact(DisplayName = "Toggle камеры переключает режим между 1-м и 3-м лицом")]
    public void CameraViewBuilder_Toggle_SwitchesModes()
    {
        Assert.Equal(GameCameraMode.ThirdPerson, CameraViewBuilder.Toggle(GameCameraMode.FirstPerson));
        Assert.Equal(GameCameraMode.FirstPerson, CameraViewBuilder.Toggle(GameCameraMode.ThirdPerson));
    }

    [Fact(DisplayName = "Камера от первого лица строится от позиции глаз с учётом bob")]
    public void CameraViewBuilder_BuildFirstPerson_UsesEyeAndBob()
    {
        var player = new PlayerController(new GameConfig(), new Vector3(8f, 2f, 8f));
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);

        var view = CameraViewBuilder.Build(player, world, GameCameraMode.FirstPerson, cameraBobOffset: 0.5f);

        var expectedY = player.EyePosition.Y + 0.5f * 0.08f;
        Assert.True(MathF.Abs(view.Camera.Position.Y - expectedY) < 0.001f);
        Assert.True(Vector3.Distance(view.RayDirection, player.LookDirection) < 0.001f);
    }

    [Fact(DisplayName = "Камера от третьего лица отступает назад и дает валидный луч взаимодействия")]
    public void CameraViewBuilder_BuildThirdPerson_ReturnsValidRearCamera()
    {
        var player = new PlayerController(new GameConfig(), new Vector3(8f, 2f, 8f));
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 8, seed: 0);

        var view = CameraViewBuilder.Build(player, world, GameCameraMode.ThirdPerson, cameraBobOffset: 0f);

        Assert.True(Vector3.Distance(view.Camera.Position, player.EyePosition) > 1.0f);
        Assert.InRange(view.RayDirection.Length(), 0.999f, 1.001f);
        Assert.True(Vector3.Distance(view.RayOrigin, view.Camera.Position) < 0.001f);
    }

    [Fact(DisplayName = "ResolveCollision притягивает камеру ближе к игроку при стене позади")]
    public void CameraViewBuilder_ResolveCollision_PullsForwardWhenBlocked()
    {
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        world.SetBlock(8, 3, 10, BlockType.Stone);

        var anchor = new Vector3(8.5f, 3.2f, 8.5f);
        var desired = new Vector3(8.5f, 3.2f, 11.5f);
        var resolved = CameraViewBuilder.ResolveCollision(world, anchor, desired);

        Assert.True(resolved.Z < desired.Z - 0.1f);
    }

    [Fact(DisplayName = "ResolveCollision возвращает desired без изменений при нулевой дистанции")]
    public void CameraViewBuilder_ResolveCollision_ReturnsDesiredForZeroDistance()
    {
        var world = new WorldMap(width: 8, height: 8, depth: 8, chunkSize: 8, seed: 0);
        var anchor = new Vector3(2f, 2f, 2f);
        var desired = anchor;

        var resolved = CameraViewBuilder.ResolveCollision(world, anchor, desired);
        Assert.Equal(desired, resolved);
    }

    [Fact(DisplayName = "PlayerVisualState после Reset обнуляет фазы и скорости")]
    public void PlayerVisualState_Reset_ClearsState()
    {
        var visual = new PlayerVisualState();
        visual.Reset(new Vector3(1f, 2f, 3f));

        Assert.Equal(0f, visual.WalkPhase);
        Assert.Equal(0f, visual.WalkBlend);
        Assert.Equal(0f, visual.VerticalSpeed);
        Assert.False(visual.IsJumping);
        Assert.False(visual.IsFalling);
    }

    [Fact(DisplayName = "PlayerVisualState вычисляет ходьбу и прыжок/падение по дельте позиции")]
    public void PlayerVisualState_Update_ComputesMovementAndVerticalState()
    {
        var visual = new PlayerVisualState();
        visual.Reset(new Vector3(0f, 1f, 0f));

        visual.Update(new Vector3(0.3f, 1.2f, 0f), deltaTime: 0.1f, moveSpeed: 5.5f);
        Assert.True(visual.WalkPhase > 0f);
        Assert.True(visual.WalkBlend > 0f);
        Assert.True(visual.IsJumping);

        visual.Update(new Vector3(0.3f, 0.9f, 0.2f), deltaTime: 0.1f, moveSpeed: 5.5f);
        Assert.True(visual.IsFalling);
    }

    [Fact(DisplayName = "PlayerVisualState первый Update без Reset безопасно инициализирует состояние")]
    public void PlayerVisualState_UpdateWithoutReset_InitializesSafely()
    {
        var visual = new PlayerVisualState();
        visual.Update(new Vector3(2f, 3f, 4f), deltaTime: 0.1f, moveSpeed: 5.5f);

        Assert.Equal(0f, visual.WalkBlend);
        Assert.Equal(0f, visual.VerticalSpeed);
    }

    [Fact(DisplayName = "PlayerVisualState при moveSpeed <= 0 не разгоняет WalkBlend")]
    public void PlayerVisualState_Update_WithZeroMoveSpeed_UsesZeroNormalization()
    {
        var visual = new PlayerVisualState();
        visual.Reset(new Vector3(0f, 1f, 0f));

        visual.Update(new Vector3(1f, 1f, 0f), deltaTime: 0.1f, moveSpeed: 0f);
        Assert.Equal(0f, visual.WalkBlend);
    }

    [Fact(DisplayName = "GameApp DrawPlayerAvatar рисует составного персонажа из кубов")]
    public void GameApp_DrawPlayerAvatar_RendersCharacterCubes()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));

        SetPrivateField(app, "_player", new PlayerController(new GameConfig(), new Vector3(4f, 2f, 4f)));
        SetPrivateField(app, "_cameraMode", GameCameraMode.ThirdPerson);

        var method = typeof(GameApp).GetMethod("DrawPlayerAvatar", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.Equal(7, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "GameApp DrawPlayerAvatar покрывает ветки прыжка и падения")]
    public void GameApp_DrawPlayerAvatar_CoversJumpAndFallBranches()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));

        SetPrivateField(app, "_player", new PlayerController(new GameConfig(), new Vector3(4f, 2f, 4f)));
        SetPrivateField(app, "_cameraMode", GameCameraMode.ThirdPerson);

        var visualField = typeof(GameApp).GetField("_playerVisual", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(visualField);
        var visual = (PlayerVisualState)visualField!.GetValue(app)!;

        var drawMethod = typeof(GameApp).GetMethod("DrawPlayerAvatar", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(drawMethod);

        SetAutoPropertyBackingField(visual, "VerticalSpeed", 1.2f);
        drawMethod!.Invoke(app, null);

        SetAutoPropertyBackingField(visual, "VerticalSpeed", -1.2f);
        drawMethod!.Invoke(app, null);

        Assert.Equal(14, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "GameApp DrawFirstPersonHand рисует руку и блок в режиме 1-го лица")]
    public void GameApp_DrawFirstPersonHand_RendersHandAndHeldBlock()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));

        SetPrivateField(app, "_cameraMode", GameCameraMode.FirstPerson);
        SetPrivateField(app, "_state", GetPrivateAppStateValue("Playing"));

        var method = typeof(GameApp).GetMethod("DrawFirstPersonHand", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app,
        [
            new Camera3D
            {
                Position = new Vector3(4f, 3.6f, 4f),
                Target = new Vector3(4f, 3.6f, 3f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective
            }
        ]);

        Assert.Equal(2, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "GameApp DrawFirstPersonHand имеет fallback для почти вертикального взгляда")]
    public void GameApp_DrawFirstPersonHand_UsesRightFallbackForVerticalView()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));

        SetPrivateField(app, "_cameraMode", GameCameraMode.FirstPerson);
        SetPrivateField(app, "_state", GetPrivateAppStateValue("Playing"));

        var method = typeof(GameApp).GetMethod("DrawFirstPersonHand", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app,
        [
            new Camera3D
            {
                Position = new Vector3(4f, 3.6f, 4f),
                Target = new Vector3(4f, 4.6f, 4f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective
            }
        ]);

        Assert.Equal(2, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "GetCameraModeName возвращает подписи для режимов камеры")]
    public void GetCameraModeName_ReturnsLabels()
    {
        Assert.Equal("1-е лицо", GameApp.GetCameraModeName(GameCameraMode.FirstPerson));
        Assert.Equal("3-е лицо", GameApp.GetCameraModeName(GameCameraMode.ThirdPerson));
    }

    [Fact(DisplayName = "GetHeldBlockColor возвращает fallback-цвет для неизвестного блока")]
    public void GetHeldBlockColor_ReturnsFallbackForUnknown()
    {
        var method = typeof(GameApp).GetMethod("GetHeldBlockColor", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var color = (Color)method!.Invoke(null, [(BlockType)999])!;
        Assert.Equal((byte)220, color.R);
        Assert.Equal((byte)220, color.G);
        Assert.Equal((byte)220, color.B);
    }

    [Fact(DisplayName = "GetHeldBlockColor возвращает цвета дерева и листвы")]
    public void GetHeldBlockColor_ReturnsWoodAndLeavesColors()
    {
        var method = typeof(GameApp).GetMethod("GetHeldBlockColor", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var grass = (Color)method!.Invoke(null, [BlockType.Grass])!;
        Assert.Equal((byte)105, grass.R);
        Assert.Equal((byte)162, grass.G);
        Assert.Equal((byte)82, grass.B);

        var wood = (Color)method!.Invoke(null, [BlockType.Wood])!;
        Assert.Equal((byte)132, wood.R);
        Assert.Equal((byte)96, wood.G);
        Assert.Equal((byte)58, wood.B);

        var leaves = (Color)method!.Invoke(null, [BlockType.Leaves])!;
        Assert.Equal((byte)86, leaves.R);
        Assert.Equal((byte)138, leaves.G);
        Assert.Equal((byte)70, leaves.B);
    }

    [Fact(DisplayName = "ApplyLeafStyle без тумана не зависит от дистанции")]
    public void ApplyLeafStyle_WithoutFog_IsDistanceIndependent()
    {
        var app = new GameApp(
            new GameConfig
            {
                FullscreenByDefault = false,
                FogEnabled = false,
                GraphicsQuality = GraphicsQuality.High
            },
            new FakeGamePlatform(),
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("ApplyLeafStyle", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var near = (Color)method!.Invoke(app, [new Color(86, 134, 66, 255), 4, 4, 4, 4, 3, 0.6f])!;
        var far = (Color)method.Invoke(app, [new Color(86, 134, 66, 255), 40, 40, 4, 4, 3, 0.6f])!;

        Assert.Equal(near.R, far.R);
        Assert.Equal(near.G, far.G);
        Assert.Equal(near.B, far.B);
    }

    private static object GetPrivateAppStateValue(string stateName)
    {
        var appStateType = typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic);
        Assert.NotNull(appStateType);
        return Enum.Parse(appStateType!, stateName);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static void SetAutoPropertyBackingField(object target, string propertyName, object value)
    {
        var backingField = target.GetType().GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(backingField);
        backingField!.SetValue(target, value);
    }
}
