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

        Assert.Equal(14, platform.DrawCubeCalls);
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

        Assert.Equal(28, platform.DrawCubeCalls);
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

        Assert.Equal(14, platform.DrawCubeCalls);
        Assert.Contains(platform.DrawnCubes, cube => cube.Width <= 0.09f && cube.Color.R >= 200);
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

        Assert.Equal(14, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "DrawSunShaftOverlay безопасно выходит при нулевом размере экрана")]
    public void DrawSunShaftOverlay_ReturnsWhenScreenSizeIsZero()
    {
        var platform = new FakeGamePlatform
        {
            ScreenWidth = 0,
            ScreenHeight = 0
        };
        var app = new GameApp(new GameConfig { FullscreenByDefault = false }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("DrawSunShaftOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(app,
        [
            new CameraViewBuilder.CameraView(
                new Camera3D
                {
                    Position = new Vector3(4f, 3.6f, 4f),
                    Target = new Vector3(4f, 3.6f, 3f),
                    Up = Vector3.UnitY,
                    Projection = CameraProjection.Perspective
                },
                new Vector3(4f, 3.6f, 4f),
                new Vector3(0f, 0f, -1f)),
            1f
        ]);

        Assert.Empty(platform.DrawnRectangles);
    }

    [Fact(DisplayName = "GameApp DrawFirstPersonHand при screen-space браслете не рисует вторую 3D-руку")]
    public void GameApp_DrawFirstPersonHand_WithDevice_DoesNotDrawSecond3DHand()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var deviceField = typeof(GameApp).GetField("_botDevice", BindingFlags.Instance | BindingFlags.NonPublic);
        var visualField = typeof(GameApp).GetField("_botDeviceVisual", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(deviceField);
        Assert.NotNull(visualField);

        var device = (BotWristDeviceState)deviceField!.GetValue(app)!;
        var visual = (BotWristDeviceVisualState)visualField!.GetValue(app)!;
        device.OpenMain();
        visual.Update(true, 0.1f);
        visual.TriggerTap();

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

        Assert.DoesNotContain(platform.DrawnCubes, cube =>
            cube.Color.R == 230
            && cube.Color.G == 198
            && cube.Color.B == 168);
        Assert.DoesNotContain(platform.DrawnCubes, cube =>
            cube.Color.R == 206
            && cube.Color.G == 178
            && cube.Color.B == 152);
        Assert.Contains(platform.DrawnCubes, cube => cube.Position.X < 3.8f);
    }

    [Fact(DisplayName = "GameApp DrawFirstPersonHand без активного tap не рисует вторую руку")]
    public void GameApp_DrawFirstPersonHand_WithDeviceWithoutTap_HidesSecondHand()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        var deviceField = typeof(GameApp).GetField("_botDevice", BindingFlags.Instance | BindingFlags.NonPublic);
        var visualField = typeof(GameApp).GetField("_botDeviceVisual", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(deviceField);
        Assert.NotNull(visualField);

        var device = (BotWristDeviceState)deviceField!.GetValue(app)!;
        var visual = (BotWristDeviceVisualState)visualField!.GetValue(app)!;
        device.OpenMain();
        visual.Update(true, 0.1f);

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

        Assert.DoesNotContain(platform.DrawnCubes, cube =>
            cube.Color.R == 230
            && cube.Color.G == 198
            && cube.Color.B == 168);
        Assert.DoesNotContain(platform.DrawnCubes, cube =>
            cube.Color.R == 206
            && cube.Color.G == 178
            && cube.Color.B == 152);
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
        Assert.Equal((byte)100, grass.R);
        Assert.Equal((byte)154, grass.G);
        Assert.Equal((byte)84, grass.B);

        var wood = (Color)method!.Invoke(null, [BlockType.Wood])!;
        Assert.Equal((byte)146, wood.R);
        Assert.Equal((byte)108, wood.G);
        Assert.Equal((byte)68, wood.B);

        var leaves = (Color)method!.Invoke(null, [BlockType.Leaves])!;
        Assert.Equal((byte)88, leaves.R);
        Assert.Equal((byte)144, leaves.G);
        Assert.Equal((byte)80, leaves.B);
    }

    [Fact(DisplayName = "GameApp DrawHotbar рисует финальный фон, цветные мини-блоки и акцент выбранного слота")]
    public void GameApp_DrawHotbar_RendersStyledSlots()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(new GameConfig { FullscreenByDefault = false }, platform, new WorldMap(8, 8, 8, chunkSize: 8, seed: 0));
        SetPrivateField(app, "_selectedHotbarIndex", 1);

        var method = typeof(GameApp).GetMethod("DrawHotbar", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.True(platform.DrawRectangleCalls >= 13);
        Assert.Contains(platform.DrawnRectangles, rect => rect.Width == 18 && rect.Height == 18);
        Assert.Contains(platform.DrawnRectangles, rect => rect.Height == 4 && rect.Color.R >= 250);
        Assert.Contains(platform.DrawnUiTexts, text => text.Contains("2.", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "GetBotStatusAccent возвращает цветовой акцент для всех состояний бота")]
    public void GetBotStatusAccent_ReturnsAccentPerStatus()
    {
        var statusType = typeof(GameApp).Assembly.GetType("AIG.Game.Bot.BotStatus");
        Assert.NotNull(statusType);
        var method = typeof(GameApp).GetMethod("GetBotStatusAccent", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var idle = (Color)method!.Invoke(null, [Enum.Parse(statusType!, "Idle")])!;
        var moving = (Color)method.Invoke(null, [Enum.Parse(statusType!, "Moving")])!;
        var gathering = (Color)method.Invoke(null, [Enum.Parse(statusType!, "Gathering")])!;
        var building = (Color)method.Invoke(null, [Enum.Parse(statusType!, "Building")])!;
        var noPath = (Color)method.Invoke(null, [Enum.Parse(statusType!, "NoPath")])!;

        Assert.Equal((byte)120, idle.R);
        Assert.Equal((byte)122, moving.R);
        Assert.Equal((byte)222, gathering.G);
        Assert.Equal((byte)244, building.R);
        Assert.Equal((byte)108, noPath.G);
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

        var near = (Color)method!.Invoke(app, [new Color(86, 134, 66, 255), 3, 0.6f, 4f])!;
        var far = (Color)method.Invoke(app, [new Color(86, 134, 66, 255), 3, 0.6f, 40f])!;

        Assert.Equal(near.R, far.R);
        Assert.Equal(near.G, far.G);
        Assert.Equal(near.B, far.B);
    }

    [Fact(DisplayName = "ApplyFarVisualStyle без тумана возвращает цвет без fog-смешивания")]
    public void ApplyFarVisualStyle_WithoutFog_ReturnsContrastedColor()
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

        var method = typeof(GameApp).GetMethod("ApplyFarVisualStyle", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var color = (Color)method!.Invoke(app, [new Color(120, 88, 54, 255), BlockType.Wood, 4, 2, 5, true, 24f])!;
        Assert.NotEqual((byte)255, color.R);
        Assert.NotEqual((byte)255, color.G);
        Assert.NotEqual((byte)255, color.B);
    }

    [Fact(DisplayName = "Legacy-обёртки visual-style проксируют в surface-style реализацию")]
    public void VisualStyleWrappers_ProxyToSurfaceStyleImplementations()
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

        var visualWrapper = typeof(GameApp).GetMethod(
            "ApplyVisualStyle",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Color), typeof(BlockType), typeof(int), typeof(int), typeof(int), typeof(bool), typeof(int), typeof(int), typeof(float)],
            modifiers: null);
        var visualSurface = typeof(GameApp).GetMethod(
            "ApplyVisualSurfaceStyle",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Color), typeof(WorldMap.SurfaceBlock), typeof(float)],
            modifiers: null);
        var midWrapper = typeof(GameApp).GetMethod(
            "ApplyMidVisualStyle",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Color), typeof(BlockType), typeof(int), typeof(int), typeof(int), typeof(bool), typeof(int), typeof(int), typeof(float)],
            modifiers: null);
        var midSurface = typeof(GameApp).GetMethod(
            "ApplyMidSurfaceStyle",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Color), typeof(WorldMap.SurfaceBlock), typeof(float)],
            modifiers: null);
        Assert.NotNull(visualWrapper);
        Assert.NotNull(visualSurface);
        Assert.NotNull(midWrapper);
        Assert.NotNull(midSurface);

        var surface = new WorldMap.SurfaceBlock(4, 2, 5, BlockType.Stone, VisibleFaces: 4, TopVisible: true, SkyExposure: 3);
        var baseColor = new Color(124, 121, 112, 255);

        var wrappedVisual = (Color)visualWrapper!.Invoke(app, [baseColor, BlockType.Stone, 4, 2, 5, true, 4, 3, 8f])!;
        var directVisual = (Color)visualSurface!.Invoke(app, [baseColor, surface, 8f])!;
        Assert.Equal(directVisual.R, wrappedVisual.R);
        Assert.Equal(directVisual.G, wrappedVisual.G);
        Assert.Equal(directVisual.B, wrappedVisual.B);

        var wrappedMid = (Color)midWrapper!.Invoke(app, [baseColor, BlockType.Stone, 4, 2, 5, true, 4, 3, 18f])!;
        var directMid = (Color)midSurface!.Invoke(app, [baseColor, surface, 18f])!;
        Assert.Equal(directMid.R, wrappedMid.R);
        Assert.Equal(directMid.G, wrappedMid.G);
        Assert.Equal(directMid.B, wrappedMid.B);
    }

    [Fact(DisplayName = "Legacy-обёртка far-style покрывает ветку side-face")]
    public void ApplyFarVisualStyle_Wrapper_CoversSideFaceBranch()
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

        var farWrapper = typeof(GameApp).GetMethod(
            "ApplyFarVisualStyle",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Color), typeof(BlockType), typeof(int), typeof(int), typeof(int), typeof(bool), typeof(float)],
            modifiers: null);
        var farSurface = typeof(GameApp).GetMethod(
            "ApplyFarSurfaceStyle",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Color), typeof(WorldMap.SurfaceBlock), typeof(float)],
            modifiers: null);
        Assert.NotNull(farWrapper);
        Assert.NotNull(farSurface);

        var surface = new WorldMap.SurfaceBlock(6, 2, 6, BlockType.Wood, VisibleFaces: 3, TopVisible: false, SkyExposure: 1);
        var baseColor = new Color(128, 94, 58, 255);

        var wrapped = (Color)farWrapper!.Invoke(app, [baseColor, BlockType.Wood, 6, 2, 6, false, 20f])!;
        var direct = (Color)farSurface!.Invoke(app, [baseColor, surface, 20f])!;

        Assert.Equal(direct.R, wrapped.R);
        Assert.Equal(direct.G, wrapped.G);
        Assert.Equal(direct.B, wrapped.B);
    }

    [Fact(DisplayName = "Legacy-обёртка leaf-style покрывает ветку с включённым туманом")]
    public void ApplyLeafStyle_Wrapper_CoversFogEnabledBranch()
    {
        var app = new GameApp(
            new GameConfig
            {
                FullscreenByDefault = false,
                FogEnabled = true,
                GraphicsQuality = GraphicsQuality.High
            },
            new FakeGamePlatform(),
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod(
            "ApplyLeafStyle",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Color), typeof(int), typeof(float), typeof(float)],
            modifiers: null);
        Assert.NotNull(method);

        var color = (Color)method!.Invoke(app, [new Color(84, 132, 68, 255), 3, 0.6f, 40f])!;
        Assert.InRange(color.R, 0, 255);
        Assert.InRange(color.G, 0, 255);
        Assert.InRange(color.B, 0, 255);
    }

    [Fact(DisplayName = "ApplyNearSurfaceAccent покрывает все ветки материалов")]
    public void ApplyNearSurfaceAccent_CoversMaterialBranches()
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

        var method = typeof(GameApp).GetMethod(
            "ApplyNearSurfaceAccent",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Color), typeof(WorldMap.SurfaceBlock), typeof(float)],
            modifiers: null);
        Assert.NotNull(method);

        static WorldMap.SurfaceBlock FindSurface(BlockType block, Func<int, int, bool> predicate)
        {
            for (var x = 0; x < 10; x++)
            {
                for (var y = 0; y < 4; y++)
                {
                    for (var z = 0; z < 10; z++)
                    {
                        var detailNoise = Math.Abs((x * 1597334677) ^ (z * 1402024253) ^ (y * 9586891));
                        var detail = detailNoise % 7;
                        var broadDetail = detailNoise % 11;
                        if (predicate(detail, broadDetail))
                        {
                            return new WorldMap.SurfaceBlock(x, y, z, block, VisibleFaces: 4, TopVisible: true, SkyExposure: 3);
                        }
                    }
                }
            }

            throw new InvalidOperationException("Не нашли подходящую координату для ветки detail.");
        }

        var baseColor = new Color(120, 120, 120, 255);
        var farSurface = new WorldMap.SurfaceBlock(0, 0, 0, BlockType.Grass, VisibleFaces: 4, TopVisible: true, SkyExposure: 3);
        var untouchedFar = (Color)method!.Invoke(app, [baseColor, farSurface, 20f])!;
        Assert.Equal(baseColor.R, untouchedFar.R);

        var grassA = FindSurface(BlockType.Grass, (detail, _) => detail <= 1);
        var grassB = FindSurface(BlockType.Grass, (detail, _) => detail == 2);
        var grassC = FindSurface(BlockType.Grass, (_, broadDetail) => broadDetail >= 9);
        var dirtA = FindSurface(BlockType.Dirt, (detail, _) => detail <= 1);
        var dirtB = FindSurface(BlockType.Dirt, (detail, _) => detail == 2);
        var stoneA = FindSurface(BlockType.Stone, (detail, _) => detail <= 1);
        var stoneB = FindSurface(BlockType.Stone, (detail, _) => detail == 2);
        var stoneC = FindSurface(BlockType.Stone, (_, broadDetail) => broadDetail >= 9);
        var woodA = FindSurface(BlockType.Wood, (_, broadDetail) => broadDetail <= 2);
        var woodB = FindSurface(BlockType.Wood, (_, broadDetail) => broadDetail == 3);
        var leavesA = FindSurface(BlockType.Leaves, (_, broadDetail) => broadDetail <= 1);
        var leavesB = FindSurface(BlockType.Leaves, (_, broadDetail) => broadDetail == 2);

        Assert.NotEqual(baseColor.G, ((Color)method.Invoke(app, [baseColor, grassA, 6f])!).G);
        Assert.NotEqual(baseColor.R, ((Color)method.Invoke(app, [baseColor, grassB, 6f])!).R);
        Assert.NotEqual(baseColor.G, ((Color)method.Invoke(app, [baseColor, grassC, 6f])!).G);
        Assert.NotEqual(baseColor.R, ((Color)method.Invoke(app, [baseColor, dirtA, 6f])!).R);
        Assert.NotEqual(baseColor.R, ((Color)method.Invoke(app, [baseColor, dirtB, 6f])!).R);
        Assert.NotEqual(baseColor.R, ((Color)method.Invoke(app, [baseColor, stoneA, 6f])!).R);
        Assert.NotEqual(baseColor.R, ((Color)method.Invoke(app, [baseColor, stoneB, 6f])!).R);
        Assert.NotEqual(baseColor.R, ((Color)method.Invoke(app, [baseColor, stoneC, 6f])!).R);
        Assert.NotEqual(baseColor.R, ((Color)method.Invoke(app, [baseColor, woodA, 6f])!).R);
        Assert.NotEqual(baseColor.R, ((Color)method.Invoke(app, [baseColor, woodB, 6f])!).R);
        Assert.NotEqual(baseColor.G, ((Color)method.Invoke(app, [baseColor, leavesA, 6f])!).G);
        Assert.NotEqual(baseColor.R, ((Color)method.Invoke(app, [baseColor, leavesB, 6f])!).R);
    }

    [Fact(DisplayName = "ApplyMaterialTopographyTint покрывает все ветки материалов и ослабевает вдали")]
    public void ApplyMaterialTopographyTint_CoversAllBlocks_AndFadesByDistance()
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

        var method = typeof(GameApp).GetMethod(
            "ApplyMaterialTopographyTint",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Color), typeof(WorldMap.SurfaceBlock), typeof(float)],
            modifiers: null);
        Assert.NotNull(method);

        var baseColor = new Color(118, 120, 112, 255);
        var grass = new WorldMap.SurfaceBlock(4, 2, 6, BlockType.Grass, VisibleFaces: 4, TopVisible: true, SkyExposure: 4, AmbientOcclusion: 6, ReliefExposure: 1, SunVisibility: 1);
        var dirt = new WorldMap.SurfaceBlock(5, 2, 7, BlockType.Dirt, VisibleFaces: 4, TopVisible: true, SkyExposure: 2, AmbientOcclusion: 5, ReliefExposure: 3, SunVisibility: 2);
        var stone = new WorldMap.SurfaceBlock(6, 2, 8, BlockType.Stone, VisibleFaces: 3, TopVisible: false, SkyExposure: 1, AmbientOcclusion: 7, ReliefExposure: 0, SunVisibility: 0);
        var wood = new WorldMap.SurfaceBlock(7, 3, 9, BlockType.Wood, VisibleFaces: 4, TopVisible: true, SkyExposure: 4, AmbientOcclusion: 2, ReliefExposure: 4, SunVisibility: WorldMap.MaxSunVisibility);
        var leaves = new WorldMap.SurfaceBlock(8, 5, 10, BlockType.Leaves, VisibleFaces: 4, TopVisible: true, SkyExposure: 5, AmbientOcclusion: 3, ReliefExposure: 2, SunVisibility: WorldMap.MaxSunVisibility);

        Assert.NotEqual(baseColor.R, ((Color)method!.Invoke(app, [baseColor, grass, 6f])!).R);
        Assert.NotEqual(baseColor.R, ((Color)method.Invoke(app, [baseColor, dirt, 6f])!).R);
        Assert.NotEqual(baseColor.B, ((Color)method.Invoke(app, [baseColor, stone, 6f])!).B);
        Assert.NotEqual(baseColor.R, ((Color)method.Invoke(app, [baseColor, wood, 6f])!).R);
        Assert.NotEqual(baseColor.G, ((Color)method.Invoke(app, [baseColor, leaves, 6f])!).G);

        var near = (Color)method.Invoke(app, [baseColor, grass, 2f])!;
        var far = (Color)method.Invoke(app, [baseColor, grass, 40f])!;
        var nearDelta = Math.Abs(near.R - baseColor.R) + Math.Abs(near.G - baseColor.G) + Math.Abs(near.B - baseColor.B);
        var farDelta = Math.Abs(far.R - baseColor.R) + Math.Abs(far.G - baseColor.G) + Math.Abs(far.B - baseColor.B);
        Assert.True(nearDelta > farDelta);
    }

    [Fact(DisplayName = "ApplyLeafSurfaceStyle меняет листву в зависимости от солнца и плотности")]
    public void ApplyLeafSurfaceStyle_RespondsToSunAndCavity()
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

        var method = typeof(GameApp).GetMethod(
            "ApplyLeafSurfaceStyle",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Color), typeof(WorldMap.SurfaceBlock), typeof(int), typeof(float), typeof(float)],
            modifiers: null);
        Assert.NotNull(method);

        var baseColor = new Color(84, 132, 68, 255);
        var sunlit = new WorldMap.SurfaceBlock(10, 6, 10, BlockType.Leaves, VisibleFaces: 4, TopVisible: true, SkyExposure: 5, AmbientOcclusion: 1, ReliefExposure: 2, SunVisibility: WorldMap.MaxSunVisibility);
        var dense = new WorldMap.SurfaceBlock(10, 6, 10, BlockType.Leaves, VisibleFaces: 4, TopVisible: true, SkyExposure: 2, AmbientOcclusion: 7, ReliefExposure: 0, SunVisibility: 0);

        var sunlitColor = (Color)method!.Invoke(app, [baseColor, sunlit, 3, 0.6f, 6f])!;
        var denseColor = (Color)method.Invoke(app, [baseColor, dense, 3, 0.6f, 6f])!;

        Assert.NotEqual(sunlitColor.G, denseColor.G);
        Assert.NotEqual(sunlitColor.B, denseColor.B);
    }

    [Fact(DisplayName = "GetDecorativeVegetationKind покрывает ветки quality, distance и block guards")]
    public void GetDecorativeVegetationKind_CoversQualityAndGuards()
    {
        static MethodInfo GetKindMethod()
        {
            var method = typeof(GameApp).GetMethod(
                "GetDecorativeVegetationKind",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(WorldMap.SurfaceBlock), typeof(float)],
                modifiers: null);
            Assert.NotNull(method);
            return method!;
        }

        static WorldMap.SurfaceBlock FindSurface(GameApp app, string expectedKind, float distance)
        {
            var method = GetKindMethod();
            for (var x = 0; x < 18; x++)
            {
                for (var z = 0; z < 18; z++)
                {
                    var surface = new WorldMap.SurfaceBlock(x, 2, z, BlockType.Grass, VisibleFaces: 4, TopVisible: true, SkyExposure: 4);
                    var kind = method.Invoke(app, [surface, distance])!.ToString();
                    if (kind == expectedKind)
                    {
                        return surface;
                    }
                }
            }

            throw new InvalidOperationException($"Не нашли поверхность для декоративного вида {expectedKind}.");
        }

        var highApp = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High, FogEnabled = false },
            new FakeGamePlatform(),
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));
        var mediumApp = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Medium, FogEnabled = false },
            new FakeGamePlatform(),
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));
        var lowApp = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low, FogEnabled = false },
            new FakeGamePlatform(),
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = GetKindMethod();
        Assert.Equal("None", method.Invoke(highApp, [new WorldMap.SurfaceBlock(0, 2, 0, BlockType.Stone, VisibleFaces: 4, TopVisible: true, SkyExposure: 4), 2f])!.ToString());
        Assert.Equal("None", method.Invoke(highApp, [new WorldMap.SurfaceBlock(0, 2, 0, BlockType.Grass, VisibleFaces: 4, TopVisible: false, SkyExposure: 4), 2f])!.ToString());
        Assert.Equal("None", method.Invoke(highApp, [new WorldMap.SurfaceBlock(0, 2, 0, BlockType.Grass, VisibleFaces: 4, TopVisible: true, SkyExposure: 4), 20f])!.ToString());

        Assert.Equal("Bush", method.Invoke(highApp, [FindSurface(highApp, "Bush", 3f), 3f])!.ToString());
        Assert.Equal("Flower", method.Invoke(highApp, [FindSurface(highApp, "Flower", 3f), 3f])!.ToString());
        Assert.Equal("Grass", method.Invoke(highApp, [FindSurface(highApp, "Grass", 3f), 3f])!.ToString());
        Assert.Equal("Flower", method.Invoke(mediumApp, [FindSurface(mediumApp, "Flower", 3f), 3f])!.ToString());
        Assert.Equal("Grass", method.Invoke(mediumApp, [FindSurface(mediumApp, "Grass", 3f), 3f])!.ToString());
        Assert.Equal("Grass", method.Invoke(lowApp, [FindSurface(lowApp, "Grass", 3f), 3f])!.ToString());
    }

    [Fact(DisplayName = "DrawDecorativeVegetationAccent рисует траву, цветок и куст без лишнего рендера")]
    public void DrawDecorativeVegetationAccent_DrawsExpectedShapes()
    {
        static MethodInfo GetMethod()
        {
            var method = typeof(GameApp).GetMethod(
                "DrawDecorativeVegetationAccent",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(WorldMap.SurfaceBlock), typeof(float), typeof(float)],
                modifiers: null);
            Assert.NotNull(method);
            return method!;
        }

        static MethodInfo GetKindMethod()
        {
            var method = typeof(GameApp).GetMethod(
                "GetDecorativeVegetationKind",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(WorldMap.SurfaceBlock), typeof(float)],
                modifiers: null);
            Assert.NotNull(method);
            return method!;
        }

        static WorldMap.SurfaceBlock FindSurface(GameApp app, string expectedKind)
        {
            var kindMethod = GetKindMethod();
            for (var x = 0; x < 18; x++)
            {
                for (var z = 0; z < 18; z++)
                {
                    var surface = new WorldMap.SurfaceBlock(x, 2, z, BlockType.Grass, VisibleFaces: 4, TopVisible: true, SkyExposure: 4);
                    if (kindMethod.Invoke(app, [surface, 3f])!.ToString() == expectedKind)
                    {
                        return surface;
                    }
                }
            }

            throw new InvalidOperationException($"Не нашли поверхность для декоративного вида {expectedKind}.");
        }

        var platform = new FakeGamePlatform();
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High, FogEnabled = false },
            platform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));
        var method = GetMethod();

        method.Invoke(app, [new WorldMap.SurfaceBlock(0, 2, 0, BlockType.Stone, VisibleFaces: 4, TopVisible: true, SkyExposure: 4), 2f, 1f]);
        Assert.Equal(0, platform.DrawCubeCalls);

        method.Invoke(app, [FindSurface(app, "Grass"), 3f, 1f]);
        Assert.Equal(3, platform.DrawCubeCalls);

        method.Invoke(app, [FindSurface(app, "Flower"), 3f, 1f]);
        Assert.Equal(5, platform.DrawCubeCalls);

        method.Invoke(app, [FindSurface(app, "Bush"), 3f, 1f]);
        Assert.Equal(8, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "ApplyVisualSurfaceStyle затемняет одинаковый блок при низкой солнечной видимости")]
    public void ApplyVisualSurfaceStyle_DarkensShadowedSurface()
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

        var method = typeof(GameApp).GetMethod(
            "ApplyVisualSurfaceStyle",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Color), typeof(WorldMap.SurfaceBlock), typeof(float)],
            modifiers: null);
        Assert.NotNull(method);

        var baseColor = new Color(126, 92, 58, 255);
        var litSurface = new WorldMap.SurfaceBlock(4, 2, 4, BlockType.Wood, VisibleFaces: 4, TopVisible: true, SkyExposure: 4, AmbientOcclusion: 1, ReliefExposure: 2, SunVisibility: WorldMap.MaxSunVisibility);
        var shadowedSurface = litSurface with { SunVisibility = 0 };

        var lit = (Color)method!.Invoke(app, [baseColor, litSurface, 8f])!;
        var shadowed = (Color)method.Invoke(app, [baseColor, shadowedSurface, 8f])!;

        var litSum = lit.R + lit.G + lit.B;
        var shadowedSum = shadowed.R + shadowed.G + shadowed.B;
        Assert.True(shadowedSum < litSum);
    }

    [Fact(DisplayName = "DrawSkyGradient завершает работу при невалидном размере экрана")]
    public void DrawSkyGradient_ReturnsForInvalidViewport()
    {
        var platform = new FakeGamePlatform
        {
            ScreenWidth = 0,
            ScreenHeight = 720
        };
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("DrawSkyGradient", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var view = new CameraViewBuilder.CameraView(
            new Camera3D
            {
                Position = new Vector3(4f, 3f, 4f),
                Target = new Vector3(4f, 3f, 3f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective
            },
            new Vector3(4f, 3f, 4f),
            Vector3.UnitZ);

        method!.Invoke(app, [view]);

        Assert.Equal(0, platform.DrawRectangleCalls);
    }

    [Fact(DisplayName = "DrawSkyGradient добавляет солнечный glow только когда солнце попадает в кадр")]
    public void DrawSkyGradient_AddsSunGlowOnlyWhenSunIsVisible()
    {
        var method = typeof(GameApp).GetMethod("DrawSkyGradient", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var towardSunPlatform = new FakeGamePlatform();
        var towardSunApp = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            towardSunPlatform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));
        var towardSunView = new CameraViewBuilder.CameraView(
            new Camera3D
            {
                Position = new Vector3(4f, 3f, 4f),
                Target = new Vector3(4.62f, 3.74f, 4.24f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective,
                FovY = 75f
            },
            new Vector3(4f, 3f, 4f),
            Vector3.Normalize(new Vector3(0.62f, 0.74f, 0.24f)));

        method!.Invoke(towardSunApp, [towardSunView]);

        var awayPlatform = new FakeGamePlatform();
        var awayApp = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            awayPlatform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));
        var awayView = new CameraViewBuilder.CameraView(
            new Camera3D
            {
                Position = new Vector3(4f, 3f, 4f),
                Target = new Vector3(3.38f, 2.26f, 3.76f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective,
                FovY = 75f
            },
            new Vector3(4f, 3f, 4f),
            Vector3.Normalize(new Vector3(-0.62f, -0.74f, -0.24f)));

        method.Invoke(awayApp, [awayView]);
        Assert.True(awayPlatform.DrawRectangleCalls >= 50);
        Assert.True(towardSunPlatform.DrawRectangleCalls > awayPlatform.DrawRectangleCalls);
    }

    [Fact(DisplayName = "TryProjectDirectionToScreen покрывает guard-ветки и успешную проекцию")]
    public void TryProjectDirectionToScreen_CoversGuardsAndSuccess()
    {
        var method = typeof(GameApp).GetMethod("TryProjectDirectionToScreen", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var normalCamera = new Camera3D
        {
            Position = new Vector3(4f, 3f, 4f),
            Target = new Vector3(5f, 3f, 4f),
            Up = Vector3.UnitY,
            Projection = CameraProjection.Perspective,
            FovY = 75f
        };

        object[] invalidViewportArgs = [normalCamera, Vector3.UnitX, 0, 720, null!];
        Assert.False((bool)method!.Invoke(null, invalidViewportArgs)!);

        var zeroForwardCamera = new Camera3D
        {
            Position = new Vector3(4f, 3f, 4f),
            Target = new Vector3(4f, 3f, 4f),
            Up = Vector3.UnitY,
            Projection = CameraProjection.Perspective,
            FovY = 75f
        };
        object[] zeroForwardArgs = [zeroForwardCamera, Vector3.UnitX, 1280, 720, null!];
        Assert.False((bool)method.Invoke(null, zeroForwardArgs)!);

        var zeroRightCamera = new Camera3D
        {
            Position = new Vector3(4f, 3f, 4f),
            Target = new Vector3(4f, 4f, 4f),
            Up = Vector3.UnitY,
            Projection = CameraProjection.Perspective,
            FovY = 75f
        };
        object[] zeroRightArgs = [zeroRightCamera, Vector3.UnitY, 1280, 720, null!];
        Assert.False((bool)method.Invoke(null, zeroRightArgs)!);

        object[] behindArgs = [normalCamera, -Vector3.UnitX, 1280, 720, null!];
        Assert.False((bool)method.Invoke(null, behindArgs)!);

        var zeroFovCamera = new Camera3D
        {
            Position = new Vector3(4f, 3f, 4f),
            Target = new Vector3(5f, 3f, 4f),
            Up = Vector3.UnitY,
            Projection = CameraProjection.Perspective,
            FovY = 0f
        };
        object[] zeroFovArgs = [zeroFovCamera, Vector3.UnitX, 1280, 720, null!];
        Assert.False((bool)method.Invoke(null, zeroFovArgs)!);

        object[] offscreenXArgs = [normalCamera, Vector3.Normalize(new Vector3(1f, 0f, 3f)), 1280, 720, null!];
        Assert.False((bool)method.Invoke(null, offscreenXArgs)!);

        object[] offscreenYArgs = [normalCamera, Vector3.Normalize(new Vector3(1f, 3f, 0f)), 1280, 720, null!];
        Assert.False((bool)method.Invoke(null, offscreenYArgs)!);

        object[] successArgs = [normalCamera, Vector3.UnitX, 1280, 720, null!];
        Assert.True((bool)method.Invoke(null, successArgs)!);
        var projected = Assert.IsType<Vector2>(successArgs[4]);
        Assert.InRange(projected.X, 0f, 1280f);
        Assert.InRange(projected.Y, 0f, 720f);
    }

    [Fact(DisplayName = "DrawSunGlow завершает работу при невалидном размере экрана")]
    public void DrawSunGlow_ReturnsForInvalidViewport()
    {
        var platform = new FakeGamePlatform
        {
            ScreenWidth = 0,
            ScreenHeight = 720
        };
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("DrawSunGlow", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var view = new CameraViewBuilder.CameraView(
            new Camera3D
            {
                Position = new Vector3(4f, 3f, 4f),
                Target = new Vector3(4.62f, 3.74f, 4.24f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective,
                FovY = 75f
            },
            new Vector3(4f, 3f, 4f),
            Vector3.Normalize(new Vector3(0.62f, 0.74f, 0.24f)));

        method!.Invoke(app, [view]);

        Assert.Equal(0, platform.DrawRectangleCalls);
    }

    [Fact(DisplayName = "DrawSkyCloudBands завершает работу при невалидном размере экрана")]
    public void DrawSkyCloudBands_ReturnsForInvalidViewport()
    {
        var platform = new FakeGamePlatform
        {
            ScreenWidth = 0,
            ScreenHeight = 720
        };
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("DrawSkyCloudBands", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var view = new CameraViewBuilder.CameraView(
            new Camera3D
            {
                Position = new Vector3(4f, 3f, 4f),
                Target = new Vector3(4f, 3f, 3f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective,
                FovY = 75f
            },
            new Vector3(4f, 3f, 4f),
            Vector3.UnitZ);

        method!.Invoke(app, [view]);

        Assert.Equal(0, platform.DrawRectangleCalls);
    }

    [Fact(DisplayName = "DrawFarHorizonRidges завершает работу при невалидном размере экрана")]
    public void DrawFarHorizonRidges_ReturnsForInvalidViewport()
    {
        var platform = new FakeGamePlatform
        {
            ScreenWidth = 1280,
            ScreenHeight = 0
        };
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("DrawFarHorizonRidges", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var view = new CameraViewBuilder.CameraView(
            new Camera3D
            {
                Position = new Vector3(4f, 3f, 4f),
                Target = new Vector3(4f, 3f, 3f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective,
                FovY = 75f
            },
            new Vector3(4f, 3f, 4f),
            Vector3.UnitZ);

        method!.Invoke(app, [view]);

        Assert.Equal(0, platform.DrawRectangleCalls);
    }

    [Fact(DisplayName = "DrawFarHorizonRidges завершает работу и при нулевой ширине экрана")]
    public void DrawFarHorizonRidges_ReturnsForZeroWidthViewport()
    {
        var platform = new FakeGamePlatform
        {
            ScreenWidth = 0,
            ScreenHeight = 720
        };
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("DrawFarHorizonRidges", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var view = new CameraViewBuilder.CameraView(
            new Camera3D
            {
                Position = new Vector3(4f, 3f, 4f),
                Target = new Vector3(4f, 3f, 3f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective,
                FovY = 75f
            },
            new Vector3(4f, 3f, 4f),
            Vector3.UnitZ);

        method!.Invoke(app, [view]);

        Assert.Equal(0, platform.DrawRectangleCalls);
    }

    [Fact(DisplayName = "DrawScreenFogOverlay завершает работу при невалидном размере экрана")]
    public void DrawScreenFogOverlay_ReturnsForInvalidViewport()
    {
        var platform = new FakeGamePlatform
        {
            ScreenWidth = 0,
            ScreenHeight = 720
        };
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("DrawScreenFogOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var view = new CameraViewBuilder.CameraView(
            new Camera3D
            {
                Position = new Vector3(4f, 3f, 4f),
                Target = new Vector3(4f, 3f, 3f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective,
                FovY = 75f
            },
            new Vector3(4f, 3f, 4f),
            -Vector3.UnitZ);

        method!.Invoke(app, [view]);

        Assert.Equal(0, platform.DrawRectangleCalls);
    }

    [Fact(DisplayName = "DrawHorizonBand покрывает guard-ветки и нормальный рендер")]
    public void DrawHorizonBand_CoversGuardsAndVisibleDraw()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("DrawHorizonBand", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(app, [1280, 720, 100, 0, new Color(0, 0, 0, 255)]);
        method.Invoke(app, [1280, 720, -100, 1, new Color(0, 0, 0, 255)]);
        method.Invoke(app, [1280, 720, 100, 12, new Color(0, 0, 0, 255)]);

        Assert.Equal(1, platform.DrawRectangleCalls);
    }

    [Fact(DisplayName = "ApplyFilmicTonemap сжимает слишком яркий цвет и сохраняет альфу")]
    public void ApplyFilmicTonemap_CompressesBrightColor()
    {
        var method = typeof(GameApp).GetMethod("ApplyFilmicTonemap", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var baseColor = new Color(255, 244, 232, 201);
        var tonemapped = (Color)method!.Invoke(null, [baseColor, 1.18f])!;

        Assert.True(tonemapped.R < baseColor.R);
        Assert.True(tonemapped.G < baseColor.G);
        Assert.True(tonemapped.B < baseColor.B);
        Assert.Equal(baseColor.A, tonemapped.A);
    }

    [Fact(DisplayName = "ApplySceneColorGrade греет светлые зоны и охлаждает тени")]
    public void ApplySceneColorGrade_WarmsHighlightsAndCoolsShadows()
    {
        var method = typeof(GameApp).GetMethod("ApplySceneColorGrade", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var bright = new Color(224, 202, 170, 255);
        var dark = new Color(52, 58, 66, 255);

        var gradedBright = (Color)method!.Invoke(null, [bright, 0.12f, 0.05f, 1.05f])!;
        var gradedDark = (Color)method.Invoke(null, [dark, 0.05f, 0.12f, 1.03f])!;

        Assert.True(gradedBright.R >= gradedBright.B);
        Assert.True(gradedDark.B >= gradedDark.R);
    }

    [Fact(DisplayName = "DrawCinematicPostProcessOverlay завершает работу при невалидном viewport")]
    public void DrawCinematicPostProcessOverlay_ReturnsForInvalidViewport()
    {
        var platform = new FakeGamePlatform
        {
            ScreenWidth = 0,
            ScreenHeight = 720
        };
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("DrawCinematicPostProcessOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var view = new CameraViewBuilder.CameraView(
            new Camera3D
            {
                Position = new Vector3(4f, 3f, 4f),
                Target = new Vector3(4.62f, 3.74f, 4.24f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective,
                FovY = 75f
            },
            new Vector3(4f, 3f, 4f),
            Vector3.Normalize(new Vector3(0.62f, 0.74f, 0.24f)));

        method!.Invoke(app, [view]);

        Assert.Equal(0, platform.DrawRectangleCalls);
    }

    [Fact(DisplayName = "DrawCinematicPostProcessOverlay рисует bloom, haze и vignette поверх мира")]
    public void DrawCinematicPostProcessOverlay_DrawsBloomHazeAndVignette()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("DrawCinematicPostProcessOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var towardSunView = new CameraViewBuilder.CameraView(
            new Camera3D
            {
                Position = new Vector3(4f, 3f, 4f),
                Target = new Vector3(4.62f, 3.74f, 4.24f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective,
                FovY = 75f
            },
            new Vector3(4f, 3f, 4f),
            Vector3.Normalize(new Vector3(0.62f, 0.74f, 0.24f)));

        method!.Invoke(app, [towardSunView]);

        Assert.True(platform.DrawRectangleCalls >= 13);
        Assert.Contains(platform.DrawnRectangles, rect => rect.Width >= 220 && rect.Color.R >= 250 && rect.Color.G >= 200);
        Assert.Contains(platform.DrawnRectangles, rect => rect.X == 0 && rect.Height == platform.ScreenHeight);
        Assert.Contains(platform.DrawnRectangles, rect => rect.Y == platform.ScreenHeight - rect.Height && rect.Width == platform.ScreenWidth);
        Assert.Contains(platform.DrawnRectangles, rect => rect.Width == platform.ScreenWidth && rect.Color.R == 255 && rect.Color.G == 216);
    }

    [Fact(DisplayName = "DrawCinematicSunBloomOverlay завершает работу при невалидном viewport")]
    public void DrawCinematicSunBloomOverlay_ReturnsForInvalidViewport()
    {
        var platform = new FakeGamePlatform
        {
            ScreenWidth = 0,
            ScreenHeight = 720
        };
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("DrawCinematicSunBloomOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var towardSunView = new CameraViewBuilder.CameraView(
            new Camera3D
            {
                Position = new Vector3(4f, 3f, 4f),
                Target = new Vector3(4.62f, 3.74f, 4.24f),
                Up = Vector3.UnitY,
                Projection = CameraProjection.Perspective,
                FovY = 75f
            },
            new Vector3(4f, 3f, 4f),
            Vector3.Normalize(new Vector3(0.62f, 0.74f, 0.24f)));

        method!.Invoke(app, [towardSunView, 1f]);

        Assert.Equal(0, platform.DrawRectangleCalls);
    }

    [Fact(DisplayName = "ApplyCinematicPostProcessColor усиливает цветокор ближней поверхности сильнее дальней")]
    public void ApplyCinematicPostProcessColor_StrongerNearThanFar()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High, FogEnabled = false },
            new FakeGamePlatform(),
            new WorldMap(16, 16, 16, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod(
            "ApplyCinematicPostProcessColor",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Color), typeof(float), typeof(bool)],
            modifiers: null);
        Assert.NotNull(method);

        var baseColor = new Color(156, 142, 118, 255);
        var near = (Color)method!.Invoke(app, [baseColor, 6f, true])!;
        var far = (Color)method.Invoke(app, [baseColor, 72f, true])!;

        var nearDelta = Math.Abs(near.R - baseColor.R) + Math.Abs(near.G - baseColor.G) + Math.Abs(near.B - baseColor.B);
        var farDelta = Math.Abs(far.R - baseColor.R) + Math.Abs(far.G - baseColor.G) + Math.Abs(far.B - baseColor.B);
        Assert.True(nearDelta > farDelta);
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
