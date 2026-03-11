using System.Numerics;
using System.Linq;
using System.Reflection;
using AIG.Game.Bot;
using AIG.Game.Core;
using AIG.Game.Config;
using AIG.Game.World;
using Raylib_cs;

namespace AIG.Game.Tests;

public sealed class BotWristDeviceTests
{
    [Fact(DisplayName = "BotWristDeviceState переключает экраны, вводит количество и хранит сообщение")]
    public void BotWristDeviceState_TracksScreenAmountAndMessage()
    {
        var device = new BotWristDeviceState();

        Assert.False(device.IsOpen);
        device.OpenMain();
        Assert.True(device.IsOpen);
        Assert.Equal(BotWristDeviceScreen.Main, device.Screen);

        device.OpenGatherResource();
        device.SelectResource(BotResourceType.Stone);
        device.ResetAmount();
        Assert.Equal(BotResourceType.Stone, device.SelectedResource);
        Assert.True(device.BackspaceAmount());
        Assert.True(device.AppendDigit(2));
        Assert.True(device.AppendDigit(4));
        Assert.True(device.TryGetAmount(out var amount));
        Assert.Equal(124, amount);

        device.SetMessage("ok");
        Assert.Equal("ok", device.Message);

        device.OpenBuildHouse();
        Assert.Equal(BotWristDeviceScreen.BuildHouse, device.Screen);
        device.BackToMain();
        Assert.Equal(BotWristDeviceScreen.Main, device.Screen);

        device.Close();
        Assert.False(device.IsOpen);
        Assert.Equal(string.Empty, device.Message);
    }

    [Fact(DisplayName = "BotWristDeviceState покрывает invalid и limit ветки количества")]
    public void BotWristDeviceState_AmountValidationBranches_Work()
    {
        var device = new BotWristDeviceState();

        Assert.False(device.AppendDigit(-1));
        Assert.False(device.AppendDigit(12));
        device.ResetAmount();
        Assert.True(device.AppendDigit(3));
        Assert.True(device.AppendDigit(4));
        Assert.True(device.AppendDigit(5));
        Assert.False(device.AppendDigit(6));

        var amountField = typeof(BotWristDeviceState).GetField("<AmountText>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(amountField);

        amountField!.SetValue(device, string.Empty);
        Assert.False(device.BackspaceAmount());

        amountField.SetValue(device, "abc");
        Assert.False(device.TryGetAmount(out _));

        amountField.SetValue(device, "0");
        Assert.False(device.TryGetAmount(out _));

        amountField.SetValue(device, "7");
        Assert.True(device.BackspaceAmount());
        Assert.Equal("0", device.AmountText);

        device.SetMessage(null!);
        Assert.Equal(string.Empty, device.Message);
    }

    [Fact(DisplayName = "BotWristDeviceVisualState плавно поднимает устройство и затухает tap-анимацию")]
    public void BotWristDeviceVisualState_UpdateAndTap_Work()
    {
        var visual = new BotWristDeviceVisualState();

        visual.Update(targetRaised: true, deltaTime: 1f / 30f);
        Assert.True(visual.RaiseBlend > 0f);

        visual.TriggerTap();
        Assert.Equal(1f, visual.TapBlend);
        Assert.Equal(BotWristDeviceTarget.None, visual.TapTarget);

        visual.TriggerTap(BotWristDeviceTarget.Wood);
        Assert.Equal(BotWristDeviceTarget.Wood, visual.TapTarget);

        visual.Update(targetRaised: true, deltaTime: 0.1f);
        Assert.True(visual.TapBlend < 1f);

        visual.Update(targetRaised: false, deltaTime: 0.1f);
        Assert.True(visual.RaiseBlend < 1f);

        visual.Update(targetRaised: false, deltaTime: 1f);
        Assert.Equal(BotWristDeviceTarget.None, visual.TapTarget);
    }

    [Fact(DisplayName = "BotWristDeviceLayout совмещает tap-цель, hitbox и экранную кнопку ресурса")]
    public void BotWristDeviceLayout_AlignsTapTargetWithResourceRect()
    {
        var device = new BotWristDeviceState();
        device.OpenGatherResource();
        device.SelectResource(BotResourceType.Wood);

        var camera = new Camera3D
        {
            Position = new Vector3(4f, 3.6f, 4f),
            Target = new Vector3(4f, 3.6f, 3f),
            Up = Vector3.UnitY,
            FovY = 75f,
            Projection = CameraProjection.Perspective
        };

        var forward = Vector3.Normalize(camera.Target - camera.Position);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        var center = camera.Position + forward * 0.90f + right * 0.06f - up * 0.02f;

        var layout = BotWristDeviceLayout.Create(camera, 1280, 720, device, center, forward, right, up);
        var rect = layout.GetRect(BotWristDeviceTarget.Wood);

        Assert.Equal(BotResourceType.Wood, layout.SelectedResource);
        Assert.True(BotWristDeviceLayout.TryProjectPoint(camera, 1280, 720, layout.GetTapTarget(BotWristDeviceTarget.Wood), out var projectedTap));
        Assert.True(rect.Contains(projectedTap));
        Assert.InRange(Vector2.Distance(projectedTap, rect.Center), 0f, 22f);

        var hit = layout.HitTest(rect.Center);
        Assert.Equal(BotWristDeviceTarget.Wood, hit);
    }

    [Fact(DisplayName = "BotWristDeviceLayout различает экраны main/build и fallback-ветки проекции")]
    public void BotWristDeviceLayout_HitTestAndProjectionBranches_Work()
    {
        var device = new BotWristDeviceState();
        device.OpenBuildHouse();

        var camera = new Camera3D
        {
            Position = new Vector3(0f, 1.7f, 0f),
            Target = new Vector3(0f, 1.7f, 1f),
            Up = Vector3.UnitY,
            FovY = 75f,
            Projection = CameraProjection.Perspective
        };

        var forward = Vector3.Normalize(camera.Target - camera.Position);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        var center = camera.Position + forward * 0.92f + right * 0.04f;

        var layout = BotWristDeviceLayout.Create(camera, 1280, 720, device, center, forward, right, up);

        Assert.Equal(BotWristDeviceTarget.Confirm, layout.HitTest(layout.GetRect(BotWristDeviceTarget.Confirm).Center));
        Assert.Equal(BotWristDeviceTarget.Back, layout.HitTest(layout.GetRect(BotWristDeviceTarget.Back).Center));
        Assert.Equal(BotWristDeviceTarget.None, layout.HitTest(new Vector2(-10f, -10f)));

        Assert.False(BotWristDeviceLayout.TryProjectPoint(camera, 0, 720, center, out _));
        Assert.False(BotWristDeviceLayout.TryProjectPoint(camera, 1280, 720, camera.Position - forward, out _));
    }

    [Fact(DisplayName = "BotWristDeviceLayout на экране сбора скрывает старые main-элементы")]
    public void BotWristDeviceLayout_GatherScreen_HidesMainElements()
    {
        var device = new BotWristDeviceState();
        device.OpenGatherResource();

        var camera = new Camera3D
        {
            Position = new Vector3(4f, 3.6f, 4f),
            Target = new Vector3(4f, 3.6f, 3f),
            Up = Vector3.UnitY,
            FovY = 75f,
            Projection = CameraProjection.Perspective
        };

        var forward = Vector3.Normalize(camera.Target - camera.Position);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        var center = camera.Position + forward * 0.90f + right * 0.06f - up * 0.02f;
        var layout = BotWristDeviceLayout.Create(camera, 1280, 720, device, center, forward, right, up);

        Assert.False(layout.IsVisible(BotWristDeviceTarget.Gather));
        Assert.False(layout.IsVisible(BotWristDeviceTarget.BuildHouse));
        Assert.True(layout.IsVisible(BotWristDeviceTarget.Wood));
        Assert.Equal(BotWristDeviceTarget.None, layout.HitTest(layout.GetRect(BotWristDeviceTarget.Gather).Center));
        Assert.Equal(BotWristDeviceTarget.Wood, layout.HitTest(layout.GetRect(BotWristDeviceTarget.Wood).Center));
    }

    [Fact(DisplayName = "BotWristDeviceLayout покрывает Elements, размеры world-элемента и fallback tap-target")]
    public void BotWristDeviceLayout_ElementsAndFallbackTapTarget_Work()
    {
        var device = new BotWristDeviceState();
        device.OpenMain();

        var camera = new Camera3D
        {
            Position = new Vector3(4f, 3.6f, 4f),
            Target = new Vector3(4f, 3.6f, 3f),
            Up = Vector3.UnitY,
            FovY = 75f,
            Projection = CameraProjection.Perspective
        };

        var forward = Vector3.Normalize(camera.Target - camera.Position);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        var center = camera.Position + forward * 0.90f + right * 0.06f - up * 0.02f;
        var layout = BotWristDeviceLayout.Create(camera, 1280, 720, device, center, forward, right, up);

        Assert.NotEmpty(layout.Elements);
        var gather = layout.GetElement(BotWristDeviceTarget.Gather);
        Assert.True(gather.WorldWidth > 0f);
        Assert.True(gather.WorldHeight > 0f);

        var fallbackTap = layout.GetTapTarget((BotWristDeviceTarget)999);
        Assert.Equal(layout.PanelCenter + layout.PanelForward * (layout.Thickness * 0.85f), fallbackTap);
    }

    [Fact(DisplayName = "BotWristDeviceLayout покрывает zero-panel, неизвестные target и invalid-camera ветки")]
    public void BotWristDeviceLayout_FallbackBranches_Work()
    {
        var device = new BotWristDeviceState();
        device.OpenMain();

        var invalidCamera = new Camera3D
        {
            Position = new Vector3(1f, 2f, 3f),
            Target = new Vector3(1f, 2f, 3f),
            Up = Vector3.UnitY,
            FovY = 75f,
            Projection = CameraProjection.Perspective
        };

        var layout = BotWristDeviceLayout.Create(
            invalidCamera,
            1280,
            720,
            device,
            new Vector3(1f, 2f, 4f),
            Vector3.UnitZ,
            Vector3.UnitX,
            Vector3.UnitY);

        Assert.Equal(invalidCamera, layout.Camera);
        Assert.Equal(Vector2.Zero, layout.GetPanelPoint(24, 24));
        Assert.Equal(0, layout.GetRect((BotWristDeviceTarget)999).W);

        var gather = layout.GetElement(BotWristDeviceTarget.Gather);
        Assert.Equal(BotWristDeviceTarget.Gather, gather.Target);
    }

    [Fact(DisplayName = "GameApp покрывает fallback layout и default-mapping ветки браслета")]
    public void GameApp_BotDeviceFallbackBranches_Work()
    {
        var config = new GameConfig { FullscreenByDefault = false };
        var world = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        var platform = new AIG.Game.Tests.Fakes.FakeGamePlatform
        {
            LeftMousePressed = true,
            MousePosition = new Vector2(320f, 200f)
        };
        var app = new GameApp(config, platform, world);
        var player = new AIG.Game.Player.PlayerController(config, new Vector3(8.5f, 2.02f, 8.5f));
        var device = (BotWristDeviceState)typeof(GameApp).GetField("_botDevice", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;

        typeof(GameApp).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, player);
        typeof(GameApp).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, Enum.Parse(typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic)!, "Playing"));
        device.OpenMain();

        var readAction = typeof(GameApp).GetMethod("ReadBotDeviceAction", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var panelRect = typeof(GameApp).GetMethod("GetBotDevicePanelRect", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var elementRect = typeof(GameApp).GetMethod("GetBotDeviceElementRect", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var mapAction = typeof(GameApp).GetMethod("MapBotDeviceTargetToAction", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var mapTap = typeof(GameApp).GetMethod("GetTapTargetForResource", BindingFlags.Static | BindingFlags.NonPublic)!;
        var mapResource = typeof(GameApp).GetMethod("MapTargetToResource", BindingFlags.Static | BindingFlags.NonPublic)!;
        var buildLayout = typeof(GameApp).GetMethod("BuildBotDeviceLayout", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var drawHologram = typeof(GameApp).GetMethod("DrawWristHologram", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var messagePoint = typeof(GameApp).GetMethod("GetBotDeviceMessagePoint", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal("None", readAction.Invoke(app, null)!.ToString());
        Assert.Equal((0, 0, 0, 0), panelRect.Invoke(app, null));
        Assert.Equal((0, 0, 0, 0), elementRect.Invoke(app, [BotWristDeviceTarget.Gather]));
        Assert.Equal("None", mapAction.Invoke(app, [BotWristDeviceTarget.None])!.ToString());
        Assert.Equal(BotWristDeviceTarget.None, mapTap.Invoke(null, [(BotResourceType)999])!);
        Assert.Equal(BotResourceType.Wood, mapResource.Invoke(null, [BotWristDeviceTarget.None])!);

        var companion = new AIG.Game.Bot.CompanionBot(config, new Vector3(10.5f, 2.02f, 8.5f));
        typeof(GameApp).GetField("_companion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, companion);

        var livePanel = ((int X, int Y, int W, int H))panelRect.Invoke(app, null)!;
        Assert.True(livePanel.W > 0);
        Assert.True(livePanel.H > 0);
        var liveGather = ((int X, int Y, int W, int H))elementRect.Invoke(app, [BotWristDeviceTarget.Gather])!;
        Assert.True(liveGather.W > 0);

        var verticalCamera = new Camera3D
        {
            Position = new Vector3(4f, 3.6f, 4f),
            Target = new Vector3(4f, 4.6f, 4f),
            Up = Vector3.UnitY,
            FovY = 75f,
            Projection = CameraProjection.Perspective
        };

        device.OpenBuildHouse();
        var layout = (BotWristDeviceLayout)buildLayout.Invoke(app, [verticalCamera])!;
        Assert.True(layout.PanelRect.W > 0);
        Assert.True(layout.PanelRect.H > 0);
        drawHologram.Invoke(app, [layout, 0.5f]);
        _ = messagePoint.Invoke(app, [layout]);

        device.OpenGatherResource();
        device.SelectResource(BotResourceType.Wood);
        var gatherLayout = (BotWristDeviceLayout)buildLayout.Invoke(app, [verticalCamera])!;
        drawHologram.Invoke(app, [gatherLayout, 0.2f]);

        device.OpenMain();
        _ = messagePoint.Invoke(app, [gatherLayout]);
    }

    [Fact(DisplayName = "GameApp покрывает ветки tap-overlay и mapping всех ресурсов браслета")]
    public void GameApp_BotDeviceTapOverlayAndResourceMapping_Work()
    {
        var config = new GameConfig { FullscreenByDefault = false };
        var world = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        var platform = new AIG.Game.Tests.Fakes.FakeGamePlatform();
        var app = new GameApp(config, platform, world);
        var player = new AIG.Game.Player.PlayerController(config, new Vector3(8.5f, 2.02f, 8.5f));
        var companion = new AIG.Game.Bot.CompanionBot(config, new Vector3(10.5f, 2.02f, 8.5f));
        var device = (BotWristDeviceState)typeof(GameApp).GetField("_botDevice", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        var visual = (BotWristDeviceVisualState)typeof(GameApp).GetField("_botDeviceVisual", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;

        typeof(GameApp).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, player);
        typeof(GameApp).GetField("_companion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, companion);
        typeof(GameApp).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, Enum.Parse(typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic)!, "Playing"));
        device.OpenGatherResource();

        var buildLayout = typeof(GameApp).GetMethod("BuildBotDeviceLayout", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var drawTap = typeof(GameApp).GetMethod("DrawBotDeviceTapOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var getRect = typeof(GameApp).GetMethod("GetBotDeviceElementRect", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var mapResource = typeof(GameApp).GetMethod("MapTargetToResource", BindingFlags.Static | BindingFlags.NonPublic)!;

        var camera = new Camera3D
        {
            Position = player.EyePosition,
            Target = player.EyePosition + player.LookDirection,
            Up = Vector3.UnitY,
            FovY = 60f,
            Projection = CameraProjection.Perspective
        };

        var layout = (BotWristDeviceLayout)buildLayout.Invoke(app, [camera])!;
        var before = platform.DrawRectangleCalls;
        drawTap.Invoke(app, [layout]);
        Assert.Equal(before, platform.DrawRectangleCalls);

        visual.TriggerTap();
        drawTap.Invoke(app, [layout]);
        Assert.True(platform.DrawRectangleCalls > before);

        var beforeWood = platform.DrawnRectangles.Count;
        visual.TriggerTap(BotWristDeviceTarget.Wood);
        drawTap.Invoke(app, [layout]);
        var woodRects = platform.DrawnRectangles.Skip(beforeWood).ToArray();
        var woodRect = ((int X, int Y, int W, int H))getRect.Invoke(app, [BotWristDeviceTarget.Wood])!;

        Assert.Contains(woodRects, rect => rect.Width is >= 18 and <= 24 && rect.Height is >= 14 and <= 18);
        Assert.True(woodRects.Max(rect => rect.X + rect.Width) - woodRects.Min(rect => rect.X) <= 36);
        Assert.True(woodRects.Min(rect => rect.X) >= woodRect.X - 2);
        Assert.True(woodRects.Max(rect => rect.X + rect.Width) <= woodRect.X + woodRect.W + 28);

        var beforeStone = platform.DrawnRectangles.Count;
        visual.TriggerTap(BotWristDeviceTarget.Stone);
        drawTap.Invoke(app, [layout]);
        var stoneRects = platform.DrawnRectangles.Skip(beforeStone).ToArray();
        var stoneRect = ((int X, int Y, int W, int H))getRect.Invoke(app, [BotWristDeviceTarget.Stone])!;

        Assert.Contains(stoneRects, rect => rect.Width is >= 18 and <= 24 && rect.Height is >= 14 and <= 18);
        Assert.True(stoneRects.Max(rect => rect.X + rect.Width) - stoneRects.Min(rect => rect.X) <= 36);
        Assert.True(stoneRects.Min(rect => rect.X) >= stoneRect.X + stoneRect.W / 2 - 6);
        Assert.True(stoneRects.Max(rect => rect.X + rect.Width) <= stoneRect.X + stoneRect.W + 28);

        Assert.Equal(BotResourceType.Wood, mapResource.Invoke(null, [BotWristDeviceTarget.Wood]));
        Assert.Equal(BotResourceType.Stone, mapResource.Invoke(null, [BotWristDeviceTarget.Stone]));
        Assert.Equal(BotResourceType.Dirt, mapResource.Invoke(null, [BotWristDeviceTarget.Dirt]));
        Assert.Equal(BotResourceType.Leaves, mapResource.Invoke(null, [BotWristDeviceTarget.Leaves]));
        Assert.Equal(BotResourceType.Wood, mapResource.Invoke(null, [BotWristDeviceTarget.None]));
    }
}
