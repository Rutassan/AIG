using System.Numerics;
using System.IO;
using System.Collections.Generic;
using AIG.Game.Bot;
using AIG.Game.Config;
using AIG.Game.Gameplay;
using AIG.Game.Player;
using AIG.Game.World;
using Raylib_cs;

namespace AIG.Game.Core;

public class GameApp : IGameRunner
{
    private const int MenuButtonWidth = 360;
    private const int MenuButtonHeight = 56;
    private const int MenuButtonsGap = 14;
    private const int BotDevicePanelWidth = 372;
    private const int BotDevicePanelHeight = 480;
    private const int BotDeviceButtonHeight = 42;
    private const int BotDeviceButtonsGap = 10;
    private const float BotDeviceTapArmThreshold = 0.08f;
    private const float BotDeviceActionDelay = 0.10f;
    private const int BotDeviceTapPalmSize = 28;
    private readonly record struct AutoCaptureShot(string FileName, Vector3 Position, Vector3 LookTarget);
    private readonly record struct LodBlendWeights(float Near, float Mid, float Far);
    private readonly record struct InstancedBatchKey(byte R, byte G, byte B, byte A, WorldLodTier LodTier);
    private readonly record struct WristDevicePose(float RaiseBlend, float TapBlend);

    private enum WorldLodTier : byte
    {
        Near = 0,
        Mid = 1,
        Far = 2
    }

    private enum DecorativeVegetationKind : byte
    {
        None = 0,
        Grass = 1,
        Flower = 2,
        Bush = 3
    }

    private struct AutoBotState
    {
        public Vector3 LastPosition;
        public float StuckTime;
        public int TurnSign;
        public float TurnLockTime;
        public float WanderPhase;
    }

    private readonly GameConfig _config;
    private readonly IGamePlatform _platform;
    private readonly WorldMap _world;
    private readonly BlockType[] _hotbar = [BlockType.Dirt, BlockType.Stone, BlockType.Wood, BlockType.Leaves];
    private readonly GraphicsSettings _graphics;
    private readonly PlayerVisualState _playerVisual = new();
    private readonly PlayerVisualState _companionVisual = new();
    private readonly BotWristDeviceState _botDevice = new();
    private readonly BotWristDeviceVisualState _botDeviceVisual = new();
    private readonly GameCaptureManager _captureManager;

    private PlayerController _player = null!;
    private CompanionBot? _companion;
    private AppState _state = AppState.MainMenu;
    private CameraMode _cameraMode = CameraMode.FirstPerson;
    private int _selectedHotbarIndex;
    private float _lastFrameMs;
    private float _cameraBobPhase;
    private int _lastStreamChunkX = int.MinValue;
    private int _lastStreamChunkZ = int.MinValue;
    private int _lastStreamRadius = -1;
    private float _adaptiveRenderDistance = -1f;
    private Vector2 _lastAdaptiveProbePosition;
    private bool _hasAdaptiveProbe;
    private float _adaptiveMovementFreezeTimer;
    private float _runtimeSeconds;
    private int _lastDrawnSurfaceCount;
    private ulong _lastDrawSceneHash;
    private bool _sceneMetricsEnabled;
    private bool _debugHudEnabled;
    private readonly Dictionary<(int ChunkX, int ChunkZ), float> _chunkRevealStartedAt = new();
    private readonly Dictionary<InstancedBatchKey, List<Matrix4x4>> _worldInstanceBatches = new();
    private string? _pendingScreenshotPath;
    private BotDeviceAction _pendingBotDeviceAction;
    private float _pendingBotDeviceActionDelay;

    public GameApp()
        : this(CreateDefaultConfig(), new RaylibGamePlatform(), CreateDefaultWorld(CreateDefaultConfig()))
    {
    }

    internal GameApp(GameConfig config, IGamePlatform platform, WorldMap world, GameCaptureManager? captureManager = null)
    {
        _config = config;
        _platform = platform;
        _world = world;
        _graphics = new GraphicsSettings(config);
        _captureManager = captureManager ?? new GameCaptureManager(
            config.ScreenshotDirectory,
            config.VideoDirectory,
            config.VideoCaptureFps);
    }

    private enum AppState
    {
        MainMenu,
        Playing,
        PauseMenu
    }

    private enum BotDeviceAction
    {
        None = 0,
        OpenGatherResource,
        OpenBuildHouse,
        BackToMain,
        QueueGather,
        QueueBuildHouse,
        CancelCommands,
        CloseDevice,
        SelectWood,
        SelectStone,
        SelectDirt,
        SelectLeaves
    }

    public void Run()
    {
        BotDiagnosticsLog? diagnostics = null;
        var platformInitialized = false;

        try
        {
            diagnostics = BotDiagnosticsLog.Create(_config);
            diagnostics?.Write("app", $"run-start world={_world.Width}x{_world.Height}x{_world.Depth} seed={_world.Seed}");

            InitializePlatform(enableFullscreen: true);
            platformInitialized = true;
            _sceneMetricsEnabled = false;

            _player = new PlayerController(_config, CreateSpawnPosition());
            if (_world.Seed != 0)
            {
                ClearSpawnCanopy(_player.Position, horizontalRadius: 10, verticalSpan: 14);
                var startLook = BuildGroundLookDirection(_player.Position, new Vector3(0f, 0f, -1f));
                _player.SetPose(_player.Position, startLook);
            }

            _playerVisual.Reset(_player.Position);
            _companion = new CompanionBot(
                _config,
                CreateCompanionSpawnPosition(_player.Position),
                message => diagnostics?.Write("bot", message));
            _companionVisual.Reset(_companion.Position);
            diagnostics?.Write("app", $"player-spawn pos={_player.Position.X:0.00},{_player.Position.Y:0.00},{_player.Position.Z:0.00}");
            diagnostics?.Write("app", $"bot-log-file path={diagnostics.FilePath}");
            ResetAdaptiveTracking(_player.Position);
            UpdateWorldStreaming(force: true);

            var shouldExit = false;
            var currentHit = (BlockRaycastHit?)null;
            var currentView = CameraViewBuilder.Build(_player, _world, _cameraMode, 0f);

            while (!shouldExit && !_platform.WindowShouldClose())
            {
                var delta = _platform.GetFrameTime();
                _lastFrameMs = delta * 1000f;
                AdvanceRuntime(delta);
                var cameraBob = 0f;
                HandleGlobalUiHotkeys();
                _botDeviceVisual.Update(_state == AppState.Playing && _botDevice.IsOpen, delta);

                if (_state == AppState.Playing)
                {
                    if (HandlePlayingModeHotkeys())
                    {
                        currentHit = null;
                    }

                    if (!_botDevice.IsOpen && (_platform.IsKeyPressed(KeyboardKey.V) || _platform.IsKeyPressed(KeyboardKey.F5)))
                    {
                        _cameraMode = CameraViewBuilder.Toggle(_cameraMode);
                    }

                    if (_botDevice.IsOpen)
                    {
                        AdvancePendingBotDeviceAction(delta);
                        HandleBotDeviceInput();
                    }
                    else
                    {
                        HandleHotbarInput();
                    }

                    var input = _botDevice.IsOpen
                        ? default
                        : ReadInput(_platform);
                    _player.Update(_world, input, delta, PlayerPoseIntersectsCompanion);
                    _playerVisual.Update(_player.Position, delta, _config.MoveSpeed);
                    UpdateWorldStreaming(force: false);
                    if (_companion is not null)
                    {
                        _companion.Update(_world, _player.Position, _player.LookDirection, delta, CompanionPoseIntersectsPlayer);
                        _companionVisual.Update(_companion.Position, delta, _config.MoveSpeed);
                    }

                    var walkBob = _playerVisual.WalkBlend * _graphics.ViewBobScale;
                    _cameraBobPhase += delta * (7f + walkBob * 6f);
                    cameraBob = MathF.Sin(_cameraBobPhase) * 0.06f * walkBob;

                    currentView = CameraViewBuilder.Build(_player, _world, _cameraMode, cameraBob);

                    currentHit = _botDevice.IsOpen
                        ? null
                        : VoxelRaycaster.Raycast(_world, currentView.RayOrigin, currentView.RayDirection, _config.InteractionDistance);
                    if (!_botDevice.IsOpen && _platform.IsMouseButtonPressed(MouseButton.Left))
                    {
                        BlockInteraction.TryBreak(_world, currentHit);
                    }

                    if (!_botDevice.IsOpen && _platform.IsMouseButtonPressed(MouseButton.Right))
                    {
                        BlockInteraction.TryPlace(_world, currentHit, _hotbar[_selectedHotbarIndex], BlockCenterIntersectsBlockingActor);
                    }
                }
                else
                {
                    currentHit = null;
                    var menuAction = ReadMenuAction();
                    switch (menuAction)
                    {
                        case MenuAction.Start:
                            _state = AppState.Playing;
                            UpdateWorldStreaming(force: true);
                            _platform.DisableCursor();
                            break;
                        case MenuAction.ToggleFullscreen:
                            _platform.ToggleFullscreen();
                            break;
                        case MenuAction.CycleGraphicsQuality:
                            _graphics.CycleQuality();
                            _adaptiveRenderDistance = -1f;
                            _adaptiveMovementFreezeTimer = 0f;
                            _hasAdaptiveProbe = false;
                            _chunkRevealStartedAt.Clear();
                            break;
                        case MenuAction.ToggleFog:
                            _graphics.ToggleFog();
                            break;
                        case MenuAction.ToggleReliefContours:
                            _graphics.ToggleReliefContours();
                            break;
                        case MenuAction.Exit:
                            shouldExit = true;
                            break;
                    }
                }

                if (_state != AppState.Playing)
                {
                    currentView = CameraViewBuilder.Build(_player, _world, _cameraMode, cameraBob);
                }

                DrawFrame(currentHit, currentView);
                FlushCaptureOutputs(delta);
            }
        }
        finally
        {
            diagnostics?.Write("app", "run-stop");
            diagnostics?.Dispose();
            if (platformInitialized)
            {
                if (_captureManager.IsRecording)
                {
                    _ = _captureManager.StopRecording();
                }

                ShutdownPlatform();
            }
        }
    }

    internal void RunAutoCapture(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        _runtimeSeconds = 0f;

        InitializePlatform(enableFullscreen: false);
        _sceneMetricsEnabled = false;
        _state = AppState.Playing;
        _cameraMode = CameraMode.FirstPerson;
        _platform.DisableCursor();

        var shots = GetAutoCaptureShots();
        if (shots.Length > 0)
        {
            _ = PrepareAutoCaptureShot(shots[0]);
            var warmupView = CameraViewBuilder.Build(_player, _world, CameraMode.FirstPerson, 0f);
            for (var i = 0; i < 8; i++)
            {
                AdvanceRuntime(1f / 60f);
                DrawFrame(null, warmupView);
            }
            _platform.TakeScreenshot(Path.Combine(outputDirectory, "autocap-warmup.png"));
        }

        foreach (var shot in shots)
        {
            _ = PrepareAutoCaptureShot(shot);
            var view = CameraViewBuilder.Build(_player, _world, CameraMode.FirstPerson, 0f);
            AdvanceRuntime(1f / 60f);
            DrawFrame(null, view);
            AdvanceRuntime(1f / 60f);
            DrawFrame(null, view);

            _platform.TakeScreenshot(Path.Combine(outputDirectory, shot.FileName));
        }

        ShutdownPlatform();
    }

    internal void RunAutoDeviceCapture(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        _runtimeSeconds = 0f;

        InitializePlatform(enableFullscreen: false);
        _sceneMetricsEnabled = false;
        _state = AppState.Playing;
        _cameraMode = CameraMode.FirstPerson;
        _platform.DisableCursor();

        var shot = GetAutoCaptureShots()[0];
        _ = PrepareAutoCaptureShot(shot);
        if (_world.Seed != 0)
        {
            ClearSpawnCanopy(_player.Position, horizontalRadius: 24, verticalSpan: 24);
            var deviceLook = BuildGroundLookDirection(_player.Position, new Vector3(-1f, -0.12f, 0.28f));
            _player.SetPose(_player.Position, deviceLook);
            UpdateWorldStreaming(force: true);
        }
        _companion = new CompanionBot(_config, CreateCompanionSpawnPosition(_player.Position));
        _companionVisual.Reset(_companion.Position);
        OpenBotDevice();

        for (var i = 0; i < 12; i++)
        {
            DrawAutoDeviceFrame(1f / 60f);
        }
        _platform.TakeScreenshot(Path.Combine(outputDirectory, "autodevice-main.png"));

        QueueBotDeviceAction(BotDeviceAction.OpenGatherResource, BotWristDeviceTarget.Gather);
        for (var i = 0; i < 3; i++)
        {
            DrawAutoDeviceFrame(1f / 60f);
            DrawAutoDeviceFrame(1f / 60f);
            _platform.TakeScreenshot(Path.Combine(outputDirectory, $"autodevice-main-tap-gather-{i + 1}.png"));
        }

        for (var i = 0; i < 20; i++)
        {
            DrawAutoDeviceFrame(1f / 60f);
        }
        _platform.TakeScreenshot(Path.Combine(outputDirectory, "autodevice-gather.png"));

        SelectDeviceResource(BotResourceType.Wood);
        for (var i = 0; i < 2; i++)
        {
            DrawAutoDeviceFrame(1f / 60f);
            DrawAutoDeviceFrame(1f / 60f);
            _platform.TakeScreenshot(Path.Combine(outputDirectory, $"autodevice-gather-tap-wood-{i + 1}.png"));
        }

        ShutdownPlatform();
    }

    internal void RunAutoPerf(string outputDirectory, float durationSeconds = 12f, int minAllowedFps = 60)
    {
        Directory.CreateDirectory(outputDirectory);
        _runtimeSeconds = 0f;

        InitializePlatform(enableFullscreen: false);
        _sceneMetricsEnabled = true;
        _state = AppState.Playing;
        _cameraMode = CameraMode.FirstPerson;
        _platform.DisableCursor();

        _player = new PlayerController(_config, CreateSpawnPosition());
        if (_world.Seed != 0)
        {
            ClearSpawnCanopy(_player.Position, horizontalRadius: 10, verticalSpan: 14);
            var startLook = BuildGroundLookDirection(_player.Position, new Vector3(0f, 0f, -1f));
            _player.SetPose(_player.Position, startLook);
        }
        _playerVisual.Reset(_player.Position);
        ResetAdaptiveTracking(_player.Position);
        UpdateWorldStreaming(force: true);

        var duration = Math.Clamp(durationSeconds, 1f, 300f);
        var fpsThreshold = Math.Clamp(minAllowedFps, 1, 240);
        const int warmupFrameCount = 60;
        var bot = new AutoBotState
        {
            LastPosition = _player.Position,
            TurnSign = 1
        };

        var elapsed = 0f;
        var frameCount = 0;
        var minFpsAll = int.MaxValue;
        var maxFpsAll = 0;
        var sumFpsAll = 0.0;
        var belowThresholdAll = 0;
        var measuredFrameCount = 0;
        var minFpsMeasured = int.MaxValue;
        var maxFpsMeasured = 0;
        var sumFpsMeasured = 0.0;
        var belowThresholdMeasured = 0;
        var autoCaptureEveryFrames = _graphics.Quality switch
        {
            GraphicsQuality.Low => 96,
            GraphicsQuality.Medium => 120,
            _ => 144
        };
        var autoCaptureFramesSaved = 0;
        var hasPreviousScene = false;
        var previousSceneHash = 0UL;
        var previousSurfaceCount = 0;
        var sceneJumpSamples = 0;
        var sceneJumpSum = 0.0;
        var sceneJumpMax = 0.0;
        var sceneJumpSpikes = 0;

        while (elapsed < duration && !_platform.WindowShouldClose())
        {
            var rawDelta = _platform.GetFrameTime();
            var delta = rawDelta > 0f ? rawDelta : 1f / 60f;
            delta = Math.Clamp(delta, 1f / 240f, 0.05f);
            AdvanceRuntime(delta);
            elapsed += delta;
            _lastFrameMs = delta * 1000f;

            var botInput = ReadAutoBotInput(delta, ref bot);
            _player.Update(_world, botInput, delta);
            _playerVisual.Update(_player.Position, delta, _config.MoveSpeed);
            UpdateWorldStreaming(force: false);

            var walkBob = _playerVisual.WalkBlend * _graphics.ViewBobScale;
            _cameraBobPhase += delta * (7f + walkBob * 6f);
            var cameraBob = MathF.Sin(_cameraBobPhase) * 0.06f * walkBob;

            var view = CameraViewBuilder.Build(_player, _world, _cameraMode, cameraBob);
            var hit = VoxelRaycaster.Raycast(_world, view.RayOrigin, view.RayDirection, _config.InteractionDistance);
            DrawFrame(hit, view);

            var currentSurfaceCount = _lastDrawnSurfaceCount;
            var currentSceneHash = _lastDrawSceneHash;
            if (hasPreviousScene)
            {
                var blockJump = Math.Abs(currentSurfaceCount - previousSurfaceCount) / Math.Max(1f, previousSurfaceCount);
                var hashJump = BitOperations.PopCount(currentSceneHash ^ previousSceneHash) / 64f;
                var jumpScore = blockJump * 0.62f + hashJump * 0.38f;
                sceneJumpSamples++;
                sceneJumpSum += jumpScore;
                sceneJumpMax = Math.Max(sceneJumpMax, jumpScore);
                if (jumpScore > 0.28f)
                {
                    sceneJumpSpikes++;
                }
            }

            hasPreviousScene = true;
            previousSurfaceCount = currentSurfaceCount;
            previousSceneHash = currentSceneHash;

            var fps = Math.Max(1, _platform.GetFps());
            frameCount++;
            minFpsAll = Math.Min(minFpsAll, fps);
            maxFpsAll = Math.Max(maxFpsAll, fps);
            sumFpsAll += fps;
            if (fps < fpsThreshold)
            {
                belowThresholdAll++;
            }

            if (frameCount > warmupFrameCount)
            {
                measuredFrameCount++;
                minFpsMeasured = Math.Min(minFpsMeasured, fps);
                maxFpsMeasured = Math.Max(maxFpsMeasured, fps);
                sumFpsMeasured += fps;
                if (fps < fpsThreshold)
                {
                    belowThresholdMeasured++;
                }
            }

            if (frameCount % autoCaptureEveryFrames == 0 && fps >= fpsThreshold + 10)
            {
                var captureName = $"autoperf-cap-{frameCount:D4}.png";
                _platform.TakeScreenshot(Path.Combine(outputDirectory, captureName));
                autoCaptureFramesSaved++;
            }
        }

        if (frameCount == 0)
        {
            minFpsAll = 0;
        }

        var useWarmupTrimmed = measuredFrameCount > 0;
        var effectiveFrameCount = useWarmupTrimmed ? measuredFrameCount : frameCount;
        var minFps = useWarmupTrimmed ? minFpsMeasured : minFpsAll;
        var maxFps = useWarmupTrimmed ? maxFpsMeasured : maxFpsAll;
        var sumFps = useWarmupTrimmed ? sumFpsMeasured : sumFpsAll;
        var belowThresholdFrames = useWarmupTrimmed ? belowThresholdMeasured : belowThresholdAll;

        if (effectiveFrameCount == 0)
        {
            minFps = 0;
        }

        var avgFps = effectiveFrameCount == 0 ? 0f : (float)(sumFps / effectiveFrameCount);
        var avgSceneJump = sceneJumpSamples == 0 ? 0f : (float)(sceneJumpSum / sceneJumpSamples);
        var result = effectiveFrameCount > 0 && belowThresholdFrames == 0 ? "PASS" : "FAIL";
        var logName = $"autoperf-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.log";
        var logPath = Path.Combine(outputDirectory, logName);
        var lines = new[]
        {
            $"timestamp_utc={DateTime.UtcNow:O}",
            $"duration_sec={elapsed:0.00}",
            $"frames={frameCount}",
            $"fps_min={minFps}",
            $"fps_avg={avgFps:0.00}",
            $"fps_max={maxFps}",
            $"fps_threshold={fpsThreshold}",
            $"below_threshold_frames={belowThresholdFrames}",
            $"warmup_frames_ignored={(useWarmupTrimmed ? warmupFrameCount : 0)}",
            $"scene_jump_avg={avgSceneJump:0.000}",
            $"scene_jump_max={sceneJumpMax:0.000}",
            $"scene_jump_spikes={sceneJumpSpikes}",
            $"scene_jump_samples={sceneJumpSamples}",
            $"autocap_interval_frames={autoCaptureEveryFrames}",
            $"autocap_frames_saved={autoCaptureFramesSaved}",
            $"graphics_quality={GetQualityName(_graphics.Quality)}",
            $"render_distance={_graphics.RenderDistance}",
            $"result={result}",
            result == "PASS"
                ? "message=FPS держится не ниже порога."
                : "message=Есть просадки ниже порога. Нужна оптимизация."
        };
        File.WriteAllLines(logPath, lines);
        _platform.TakeScreenshot(Path.Combine(outputDirectory, "autoperf-last.png"));

        ShutdownPlatform();
        _sceneMetricsEnabled = false;
    }

    private void DrawAutoDeviceFrame(float deltaTime)
    {
        AdvanceRuntime(deltaTime);
        _botDeviceVisual.Update(_state == AppState.Playing && _botDevice.IsOpen, deltaTime);
        AdvancePendingBotDeviceAction(deltaTime);
        var view = CameraViewBuilder.Build(_player, _world, CameraMode.FirstPerson, 0f);
        DrawFrame(null, view);
    }

    internal static PlayerInput ReadInput(IGamePlatform platform)
    {
        float forward = 0f;
        float right = 0f;

        if (platform.IsKeyDown(KeyboardKey.W) || platform.IsKeyDown(KeyboardKey.Up))
        {
            forward += 1f;
        }

        if (platform.IsKeyDown(KeyboardKey.S) || platform.IsKeyDown(KeyboardKey.Down))
        {
            forward -= 1f;
        }

        if (platform.IsKeyDown(KeyboardKey.D) || platform.IsKeyDown(KeyboardKey.Right))
        {
            right += 1f;
        }

        if (platform.IsKeyDown(KeyboardKey.A) || platform.IsKeyDown(KeyboardKey.Left))
        {
            right -= 1f;
        }

        var mouse = platform.GetMouseDelta();
        return new PlayerInput(
            MoveForward: forward,
            MoveRight: right,
            Jump: platform.IsKeyPressed(KeyboardKey.Space) || platform.IsKeyDown(KeyboardKey.Space),
            LookDeltaX: mouse.X,
            LookDeltaY: mouse.Y
        );
    }

    internal static int SelectHotbarIndex(int currentIndex, IGamePlatform platform, int hotbarLength)
    {
        var index = currentIndex;
        for (var slot = 0; slot < hotbarLength && slot < 9; slot++)
        {
            var key = (KeyboardKey)((int)KeyboardKey.One + slot);
            if (platform.IsKeyPressed(key))
            {
                index = slot;
            }
        }

        return index;
    }

    private void HandleHotbarInput()
    {
        _selectedHotbarIndex = SelectHotbarIndex(_selectedHotbarIndex, _platform, _hotbar.Length);
    }

    internal bool HandleGlobalUiHotkeys()
    {
        var handled = HandleCaptureHotkeys();
        if (_platform.IsKeyPressed(KeyboardKey.F3))
        {
            _debugHudEnabled = !_debugHudEnabled;
            handled = true;
        }

        return handled;
    }

    internal bool HandleCaptureHotkeys()
    {
        var handled = false;

        if (_platform.IsKeyPressed(KeyboardKey.F12))
        {
            _pendingScreenshotPath = _captureManager.CreateScreenshotPath();
            handled = true;
        }

        if (_platform.IsKeyPressed(KeyboardKey.F10))
        {
            if (_captureManager.IsRecording)
            {
                _ = _captureManager.StopRecording();
            }
            else
            {
                _captureManager.StartRecording();
            }

            handled = true;
        }

        return handled;
    }

    internal void FlushCaptureOutputs(float deltaTime)
    {
        if (_pendingScreenshotPath is not null)
        {
            _platform.TakeScreenshot(_pendingScreenshotPath);
            _captureManager.CleanupLegacyRootScreenshots();
            _pendingScreenshotPath = null;
        }

        if (_captureManager.TryGetNextRecordingFramePath(deltaTime, out var framePath))
        {
            _platform.TakeScreenshot(framePath);
            _captureManager.CleanupLegacyRootScreenshots();
        }
    }

    private bool HandlePlayingModeHotkeys()
    {
        if (_botDevice.IsOpen)
        {
            if (_platform.IsKeyPressed(KeyboardKey.B) || _platform.IsKeyPressed(KeyboardKey.Escape))
            {
                CloseBotDevice();
                return true;
            }

            return false;
        }

        if (_platform.IsKeyPressed(KeyboardKey.B))
        {
            OpenBotDevice();
            return true;
        }

        if (_platform.IsKeyPressed(KeyboardKey.Escape))
        {
            _state = AppState.PauseMenu;
            _platform.EnableCursor();
            return true;
        }

        return false;
    }

    private void OpenBotDevice()
    {
        if (_state != AppState.Playing || _companion is null)
        {
            return;
        }

        CancelPendingBotDeviceAction();
        _botDevice.OpenMain();
        _platform.EnableCursor();
    }

    private void CloseBotDevice()
    {
        if (!_botDevice.IsOpen)
        {
            return;
        }

        CancelPendingBotDeviceAction();
        _botDevice.Close();
        if (_state == AppState.Playing)
        {
            _platform.DisableCursor();
        }
    }

    private void HandleBotDeviceInput()
    {
        if (_companion is null || !_botDevice.IsOpen)
        {
            return;
        }

        if (_pendingBotDeviceAction != BotDeviceAction.None)
        {
            return;
        }

        HandleBotDeviceDigitInput();

        if (_botDevice.Screen == BotWristDeviceScreen.GatherResource && _platform.IsKeyPressed(KeyboardKey.Enter))
        {
            TriggerBotDeviceTap(BotWristDeviceTarget.Confirm);
            QueueGatherFromDevice();
            return;
        }

        if (_botDevice.Screen == BotWristDeviceScreen.BuildHouse && _platform.IsKeyPressed(KeyboardKey.Enter))
        {
            TriggerBotDeviceTap(BotWristDeviceTarget.Confirm);
            QueueBuildFromDevice();
            return;
        }

        switch (ReadBotDeviceAction())
        {
            case BotDeviceAction.OpenGatherResource:
                QueueBotDeviceAction(BotDeviceAction.OpenGatherResource, BotWristDeviceTarget.Gather);
                break;
            case BotDeviceAction.OpenBuildHouse:
                QueueBotDeviceAction(BotDeviceAction.OpenBuildHouse, BotWristDeviceTarget.BuildHouse);
                break;
            case BotDeviceAction.BackToMain:
                QueueBotDeviceAction(BotDeviceAction.BackToMain, BotWristDeviceTarget.Back);
                break;
            case BotDeviceAction.QueueGather:
                TriggerBotDeviceTap(BotWristDeviceTarget.Confirm);
                QueueGatherFromDevice();
                break;
            case BotDeviceAction.QueueBuildHouse:
                TriggerBotDeviceTap(BotWristDeviceTarget.Confirm);
                QueueBuildFromDevice();
                break;
            case BotDeviceAction.CancelCommands:
                TriggerBotDeviceTap(BotWristDeviceTarget.Cancel);
                _companion.CancelAll();
                _botDevice.SetMessage("Команды бота сброшены.");
                break;
            case BotDeviceAction.CloseDevice:
                QueueBotDeviceAction(BotDeviceAction.CloseDevice, BotWristDeviceTarget.Close);
                break;
            case BotDeviceAction.SelectWood:
                SelectDeviceResource(BotResourceType.Wood);
                break;
            case BotDeviceAction.SelectStone:
                SelectDeviceResource(BotResourceType.Stone);
                break;
            case BotDeviceAction.SelectDirt:
                SelectDeviceResource(BotResourceType.Dirt);
                break;
            case BotDeviceAction.SelectLeaves:
                SelectDeviceResource(BotResourceType.Leaves);
                break;
        }
    }

    private void QueueBotDeviceAction(BotDeviceAction action, BotWristDeviceTarget target)
    {
        if (action == BotDeviceAction.None)
        {
            return;
        }

        _pendingBotDeviceAction = action;
        _pendingBotDeviceActionDelay = BotDeviceActionDelay;
        TriggerBotDeviceTap(target);
    }

    private void AdvancePendingBotDeviceAction(float deltaTime)
    {
        if (_pendingBotDeviceAction == BotDeviceAction.None)
        {
            return;
        }

        _pendingBotDeviceActionDelay -= Math.Max(0f, deltaTime);
        if (_pendingBotDeviceActionDelay > 0f)
        {
            return;
        }

        var action = _pendingBotDeviceAction;
        CancelPendingBotDeviceAction();
        ApplyBotDeviceAction(action);
    }

    private void CancelPendingBotDeviceAction()
    {
        _pendingBotDeviceAction = BotDeviceAction.None;
        _pendingBotDeviceActionDelay = 0f;
    }

    private void ApplyBotDeviceAction(BotDeviceAction action)
    {
        switch (action)
        {
            case BotDeviceAction.OpenGatherResource:
                _botDevice.OpenGatherResource();
                _botDevice.SetMessage("Выберите ресурс и введите количество.");
                break;
            case BotDeviceAction.OpenBuildHouse:
                _botDevice.OpenBuildHouse();
                _botDevice.SetMessage("Подтвердите строительство Дом S.");
                break;
            case BotDeviceAction.BackToMain:
                _botDevice.BackToMain();
                _botDevice.SetMessage(string.Empty);
                break;
            case BotDeviceAction.CloseDevice:
                CloseBotDevice();
                break;
        }
    }

    private void HandleBotDeviceDigitInput()
    {
        if (_botDevice.Screen != BotWristDeviceScreen.GatherResource)
        {
            return;
        }

        if (_platform.IsKeyPressed(KeyboardKey.Backspace))
        {
            if (_botDevice.BackspaceAmount())
            {
                _botDevice.SetMessage($"Количество: {_botDevice.AmountText}");
                TriggerBotDeviceTap(BotWristDeviceTarget.Amount);
            }
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            var key = (KeyboardKey)((int)KeyboardKey.Zero + digit);
            if (!_platform.IsKeyPressed(key))
            {
                continue;
            }

            if (_botDevice.AppendDigit(digit))
            {
                _botDevice.SetMessage($"Количество: {_botDevice.AmountText}");
                TriggerBotDeviceTap(BotWristDeviceTarget.Amount);
            }
        }
    }

    private void SelectDeviceResource(BotResourceType resource)
    {
        _botDevice.SelectResource(resource);
        _botDevice.SetMessage($"Ресурс: {resource.GetLabel()}");
        TriggerBotDeviceTap(GetTapTargetForResource(resource));
    }

    private void QueueGatherFromDevice()
    {
        if (_companion is null)
        {
            return;
        }

        if (!_botDevice.TryGetAmount(out var amount))
        {
            _botDevice.SetMessage("Введите количество больше нуля.");
            return;
        }

        _botDevice.SetMessage(_companion.Enqueue(BotCommand.Gather(_botDevice.SelectedResource, amount))
            ? $"Сбор: {_botDevice.SelectedResource.GetLabel()} x{amount}"
            : "Очередь бота заполнена.");
    }

    private void QueueBuildFromDevice()
    {
        if (_companion is null)
        {
            return;
        }

        var blueprint = HouseBlueprint.CreateCabinS(_world, _player.Position, _player.LookDirection);
        _botDevice.SetMessage(_companion.Enqueue(BotCommand.BuildHouse(blueprint))
            ? $"Строю: {blueprint.Name}"
            : "Очередь бота заполнена.");
    }

    private void TriggerBotDeviceTap(BotWristDeviceTarget target = BotWristDeviceTarget.None)
    {
        _botDeviceVisual.TriggerTap(target);
    }

    private BotDeviceAction ReadBotDeviceAction()
    {
        if (!_botDevice.IsOpen)
        {
            return BotDeviceAction.None;
        }

        if (!_platform.IsMouseButtonPressed(MouseButton.Left))
        {
            return BotDeviceAction.None;
        }

        if (!TryBuildBotDeviceLayout(out var layout))
        {
            return BotDeviceAction.None;
        }

        var target = layout.HitTest(_platform.GetMousePosition());
        return MapBotDeviceTargetToAction(target);
    }

    private BotDeviceAction MapBotDeviceTargetToAction(BotWristDeviceTarget target)
    {
        return (_botDevice.Screen, target) switch
        {
            (BotWristDeviceScreen.Main, BotWristDeviceTarget.Gather) => BotDeviceAction.OpenGatherResource,
            (BotWristDeviceScreen.Main, BotWristDeviceTarget.BuildHouse) => BotDeviceAction.OpenBuildHouse,
            (BotWristDeviceScreen.Main, BotWristDeviceTarget.Cancel) => BotDeviceAction.CancelCommands,
            (BotWristDeviceScreen.Main, BotWristDeviceTarget.Close) => BotDeviceAction.CloseDevice,

            (BotWristDeviceScreen.GatherResource, BotWristDeviceTarget.Back) => BotDeviceAction.BackToMain,
            (BotWristDeviceScreen.GatherResource, BotWristDeviceTarget.Wood) => BotDeviceAction.SelectWood,
            (BotWristDeviceScreen.GatherResource, BotWristDeviceTarget.Stone) => BotDeviceAction.SelectStone,
            (BotWristDeviceScreen.GatherResource, BotWristDeviceTarget.Dirt) => BotDeviceAction.SelectDirt,
            (BotWristDeviceScreen.GatherResource, BotWristDeviceTarget.Leaves) => BotDeviceAction.SelectLeaves,
            (BotWristDeviceScreen.GatherResource, BotWristDeviceTarget.Confirm) => BotDeviceAction.QueueGather,

            (BotWristDeviceScreen.BuildHouse, BotWristDeviceTarget.Back) => BotDeviceAction.BackToMain,
            (BotWristDeviceScreen.BuildHouse, BotWristDeviceTarget.Confirm) => BotDeviceAction.QueueBuildHouse,
            _ => BotDeviceAction.None
        };
    }

    private static BotWristDeviceTarget GetTapTargetForResource(BotResourceType resource)
    {
        return resource switch
        {
            BotResourceType.Wood => BotWristDeviceTarget.Wood,
            BotResourceType.Stone => BotWristDeviceTarget.Stone,
            BotResourceType.Dirt => BotWristDeviceTarget.Dirt,
            BotResourceType.Leaves => BotWristDeviceTarget.Leaves,
            _ => BotWristDeviceTarget.None
        };
    }

    private PlayerInput ReadAutoBotInput(float deltaTime, ref AutoBotState state)
    {
        var forward = ToHorizontalForward(_player.LookDirection);
        var aheadEye = _player.EyePosition - new Vector3(0f, 0.7f, 0f);
        var obstacleAhead = VoxelRaycaster.Raycast(_world, aheadEye, forward, 1.1f) is not null;
        var aheadPoint = _player.Position + forward * 0.9f;
        var hasGroundAhead = _world.IsSolidAt(new Vector3(aheadPoint.X, _player.Position.Y - 0.25f, aheadPoint.Z));

        var movedXZ = new Vector2(_player.Position.X - state.LastPosition.X, _player.Position.Z - state.LastPosition.Z).Length();
        if (movedXZ < 0.015f)
        {
            state.StuckTime += deltaTime;
        }
        else
        {
            state.StuckTime = 0f;
        }
        state.LastPosition = _player.Position;

        var needTurn = obstacleAhead || !hasGroundAhead || state.StuckTime > 0.35f;
        if (needTurn && state.TurnLockTime <= 0f)
        {
            state.TurnSign = -state.TurnSign;
            state.TurnLockTime = 0.4f;
        }
        else
        {
            state.TurnLockTime = MathF.Max(0f, state.TurnLockTime - deltaTime);
        }

        state.WanderPhase += deltaTime * 1.15f;

        var strafe = needTurn
            ? state.TurnSign * 0.75f
            : MathF.Sin(state.WanderPhase * 0.7f) * 0.28f;
        var yawSpeed = needTurn
            ? state.TurnSign * 2.8f
            : MathF.Sin(state.WanderPhase) * 0.45f;

        var sensitivity = MathF.Abs(_config.MouseSensitivity) < 0.00001f
            ? 0.0025f
            : _config.MouseSensitivity;
        var lookDeltaX = -(yawSpeed * deltaTime) / sensitivity;
        var jump = obstacleAhead && _player.IsGrounded;

        return new PlayerInput(
            MoveForward: 1f,
            MoveRight: strafe,
            Jump: jump,
            LookDeltaX: lookDeltaX,
            LookDeltaY: 0f);
    }

    private static Vector3 ToHorizontalForward(Vector3 direction)
    {
        var horizontal = new Vector3(direction.X, 0f, direction.Z);
        if (horizontal.LengthSquared() < 0.00001f)
        {
            return Vector3.UnitZ;
        }

        return Vector3.Normalize(horizontal);
    }

    private MenuAction ReadMenuAction()
    {
        if (IsButtonClicked(GetStartButtonRect()))
        {
            return MenuAction.Start;
        }

        if (IsButtonClicked(GetFullscreenButtonRect()))
        {
            return MenuAction.ToggleFullscreen;
        }

        if (IsButtonClicked(GetExitButtonRect()))
        {
            return MenuAction.Exit;
        }

        if (IsButtonClicked(GetGraphicsButtonRect()))
        {
            return MenuAction.CycleGraphicsQuality;
        }

        if (IsButtonClicked(GetFogButtonRect()))
        {
            return MenuAction.ToggleFog;
        }

        if (IsButtonClicked(GetReliefButtonRect()))
        {
            return MenuAction.ToggleReliefContours;
        }

        return MenuAction.None;
    }

    private bool IsButtonClicked((int X, int Y, int W, int H) rect)
    {
        if (!_platform.IsMouseButtonPressed(MouseButton.Left))
        {
            return false;
        }

        var mouse = _platform.GetMousePosition();
        return mouse.X >= rect.X
            && mouse.X <= rect.X + rect.W
            && mouse.Y >= rect.Y
            && mouse.Y <= rect.Y + rect.H;
    }

    private bool TryBuildBotDeviceLayout(out BotWristDeviceLayout layout)
    {
        if (_state != AppState.Playing || _companion is null || !_botDevice.IsOpen)
        {
            layout = null!;
            return false;
        }

        var walkBob = _playerVisual.WalkBlend * _graphics.ViewBobScale;
        var cameraBob = MathF.Sin(_cameraBobPhase) * 0.06f * walkBob;
        var camera = CameraViewBuilder.Build(_player, _world, _cameraMode, cameraBob).Camera;
        layout = BuildBotDeviceLayout(camera);
        return true;
    }

    private BotWristDeviceLayout BuildBotDeviceLayout(Camera3D camera)
    {
        var forward = Vector3.Normalize(camera.Target - camera.Position);
        var rightRaw = Vector3.Cross(forward, Vector3.UnitY);
        var right = rightRaw.LengthSquared() < 0.000001f
            ? Vector3.UnitX
            : Vector3.Normalize(rightRaw);
        var up = Vector3.Normalize(Vector3.Cross(right, forward));

        var bob = MathF.Sin(_playerVisual.WalkPhase * 2f) * 0.03f * _playerVisual.WalkBlend * _graphics.ViewBobScale;
        var bobOffset = up * bob;
        var raise = _botDeviceVisual.RaiseBlend;
        var hologramCenter = ComposeViewPoint(camera.Position, forward, right, up, 0.86f + 0.08f * raise, 0.16f, 0.02f + 0.03f * raise) + bobOffset;
        return BotWristDeviceLayout.Create(
            camera,
            _platform.GetScreenWidth(),
            _platform.GetScreenHeight(),
            _botDevice,
            hologramCenter,
            forward,
            right,
            up);
    }

    private void DrawFrame(BlockRaycastHit? hit, CameraViewBuilder.CameraView view)
    {
        _platform.BeginDrawing();
        _platform.ClearBackground(GetSkyTopColor());
        DrawSkyGradient(view);

        if (_state != AppState.MainMenu)
        {
            _platform.BeginMode3D(view.Camera);
            DrawWorld();
            DrawPlayerAvatar();
            DrawCompanionAvatar();
            DrawBlockHighlight(hit, view.RayOrigin, view.RayDirection);
            DrawFirstPersonHand(view.Camera);
            _platform.EndMode3D();
            DrawScreenFogOverlay(view);
            DrawCinematicPostProcessOverlay(view);

            DrawHud(_state == AppState.Playing && !_botDevice.IsOpen);
            if (_state == AppState.Playing && !_botDevice.IsOpen)
            {
                DrawHotbar();
            }
            if (_state == AppState.Playing && _botDevice.IsOpen)
            {
                DrawBotDeviceOverlay();
            }
        }

        if (_state != AppState.Playing)
        {
            DrawMenu();
        }

        DrawCaptureIndicator();
        _platform.EndDrawing();
    }

    private void DrawSkyGradient(CameraViewBuilder.CameraView view)
    {
        var width = _platform.GetScreenWidth();
        var height = _platform.GetScreenHeight();
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var top = GetSkyTopColor();
        var mid = GetSkyMidColor();
        var horizon = GetSkyHorizonColor();
        var warmGlow = GetSkyGlowColor();
        const int bands = 30;
        for (var i = 0; i < bands; i++)
        {
            var y0 = i * height / bands;
            var y1 = (i + 1) * height / bands;
            var bandHeight = Math.Max(1, y1 - y0);
            var t = i / (float)(bands - 1);
            var shapedT = MathF.Pow(t, 1.12f);
            var coolColor = shapedT < 0.64f
                ? LerpColor(top, mid, SmoothStep01(shapedT / 0.64f))
                : LerpColor(mid, horizon, SmoothStep01((shapedT - 0.64f) / 0.36f));
            var glowT = 1f - MathF.Abs(shapedT - 0.70f) / 0.22f;
            glowT = SmoothStep01(Math.Clamp(glowT, 0f, 1f)) * 0.40f;
            var color = LerpColor(coolColor, warmGlow, glowT);
            _platform.DrawRectangle(0, y0, width, bandHeight, color);
        }

        DrawSunGlow(view);

        var viewY = Math.Clamp(view.RayDirection.Y, -1f, 1f);
        var horizonY = (int)MathF.Round(height * (0.56f + viewY * 0.2f));
        horizonY = Math.Clamp(horizonY, 0, height - 1);
        var fog = _graphics.FogColor;
        DrawHorizonBand(width, height, horizonY - 44, 32, new Color(fog.R, fog.G, fog.B, (byte)22));
        DrawHorizonBand(width, height, horizonY - 10, 24, new Color(fog.R, fog.G, fog.B, (byte)40));
        DrawHorizonBand(width, height, horizonY + 16, 26, new Color(fog.R, fog.G, fog.B, (byte)20));
    }

    private void DrawScreenFogOverlay(CameraViewBuilder.CameraView view)
    {
        var width = _platform.GetScreenWidth();
        var height = _platform.GetScreenHeight();
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var viewY = Math.Clamp(view.RayDirection.Y, -1f, 1f);
        var horizonY = (int)MathF.Round(height * (0.60f + viewY * 0.16f));
        var fog = _graphics.FogColor;
        var overlayAlpha = _botDevice.IsOpen ? 0.72f : 1f;

        DrawHorizonBand(width, height, horizonY - 10, 34, new Color(fog.R, fog.G, fog.B, (byte)(18 * overlayAlpha)));
        DrawHorizonBand(width, height, horizonY + 18, 46, new Color(fog.R, fog.G, fog.B, (byte)(14 * overlayAlpha)));
        DrawHorizonBand(width, height, horizonY + 52, 68, new Color(fog.R, fog.G, fog.B, (byte)(10 * overlayAlpha)));
    }

    private void DrawCinematicPostProcessOverlay(CameraViewBuilder.CameraView view)
    {
        var width = _platform.GetScreenWidth();
        var height = _platform.GetScreenHeight();
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var strength = _graphics.Quality switch
        {
            GraphicsQuality.Low => 0.52f,
            GraphicsQuality.Medium => 0.76f,
            _ => 1f
        };
        if (_botDevice.IsOpen)
        {
            strength *= 0.82f;
        }

        var viewY = Math.Clamp(view.RayDirection.Y, -1f, 1f);
        var horizonY = (int)MathF.Round(height * (0.60f + viewY * 0.14f));
        horizonY = Math.Clamp(horizonY, 0, height - 1);
        var fog = _graphics.FogColor;

        DrawHorizonBand(width, height, horizonY - 26, 24, new Color((byte)255, (byte)207, (byte)156, (byte)MathF.Round(8f * strength)));
        DrawHorizonBand(width, height, horizonY + 6, 36, new Color((byte)fog.R, (byte)fog.G, (byte)fog.B, (byte)MathF.Round(11f * strength)));
        DrawHorizonBand(width, height, horizonY + 40, 54, new Color((byte)90, (byte)110, (byte)132, (byte)MathF.Round(8f * strength)));
        DrawCinematicSunBloomOverlay(view, strength);

        var vignetteSide = Math.Max(26, width / 12);
        var vignetteTop = Math.Max(20, height / 12);
        var vignetteBottom = Math.Max(28, height / 9);
        var edgeAlpha = (byte)MathF.Round(12f * strength);
        _platform.DrawRectangle(0, 0, vignetteSide, height, new Color((byte)8, (byte)16, (byte)24, edgeAlpha));
        _platform.DrawRectangle(width - vignetteSide, 0, vignetteSide, height, new Color((byte)8, (byte)16, (byte)24, edgeAlpha));
        _platform.DrawRectangle(0, 0, width, vignetteTop, new Color((byte)10, (byte)16, (byte)26, (byte)MathF.Round(10f * strength)));
        _platform.DrawRectangle(0, height - vignetteBottom, width, vignetteBottom, new Color((byte)14, (byte)16, (byte)22, (byte)MathF.Round(15f * strength)));
    }

    private void DrawCinematicSunBloomOverlay(CameraViewBuilder.CameraView view, float strength)
    {
        var width = _platform.GetScreenWidth();
        var height = _platform.GetScreenHeight();
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (!TryProjectDirectionToScreen(view.Camera, -WorldMap.GetSunLightDirection(), width, height, out var sunScreen))
        {
            return;
        }

        DrawCenteredGlowRect(sunScreen, 360, 220, new Color((byte)255, (byte)203, (byte)150, (byte)MathF.Round(10f * strength)));
        DrawCenteredGlowRect(sunScreen, 224, 136, new Color((byte)255, (byte)214, (byte)164, (byte)MathF.Round(13f * strength)));
        DrawCenteredGlowRect(sunScreen, 132, 86, new Color((byte)255, (byte)226, (byte)182, (byte)MathF.Round(16f * strength)));
    }

    private void DrawHorizonBand(int width, int height, int y, int bandHeight, Color color)
    {
        if (bandHeight <= 0 || width <= 0 || height <= 0)
        {
            return;
        }

        var top = Math.Clamp(y, 0, height - 1);
        var bottom = Math.Clamp(y + bandHeight, 0, height);
        var drawHeight = bottom - top;
        if (drawHeight <= 0)
        {
            return;
        }

        _platform.DrawRectangle(0, top, width, drawHeight, color);
    }

    private void DrawSunGlow(CameraViewBuilder.CameraView view)
    {
        var width = _platform.GetScreenWidth();
        var height = _platform.GetScreenHeight();
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (!TryProjectDirectionToScreen(view.Camera, -WorldMap.GetSunLightDirection(), width, height, out var sunScreen))
        {
            return;
        }

        DrawCenteredGlowRect(sunScreen, 216, 216, new Color(255, 208, 150, 18));
        DrawCenteredGlowRect(sunScreen, 132, 132, new Color(255, 218, 164, 22));
        DrawCenteredGlowRect(sunScreen, 76, 76, new Color(255, 232, 186, 28));
        DrawCenteredGlowRect(sunScreen, 28, 28, new Color(255, 245, 214, 34));
    }

    private void DrawCenteredGlowRect(Vector2 center, int width, int height, Color color)
    {
        _platform.DrawRectangle(
            (int)MathF.Round(center.X - width * 0.5f),
            (int)MathF.Round(center.Y - height * 0.5f),
            width,
            height,
            color);
    }

    private static bool TryProjectDirectionToScreen(Camera3D camera, Vector3 direction, int screenWidth, int screenHeight, out Vector2 screenPoint)
    {
        screenPoint = default;
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return false;
        }

        var viewForwardRaw = camera.Target - camera.Position;
        if (viewForwardRaw.LengthSquared() <= 0.000001f)
        {
            return false;
        }

        var viewForward = Vector3.Normalize(viewForwardRaw);
        var viewRightRaw = Vector3.Cross(viewForward, camera.Up);
        if (viewRightRaw.LengthSquared() <= 0.000001f)
        {
            return false;
        }

        var viewRight = Vector3.Normalize(viewRightRaw);
        var viewUp = Vector3.Normalize(Vector3.Cross(viewRight, viewForward));
        var normalizedDirection = Vector3.Normalize(direction);
        var forward = Vector3.Dot(normalizedDirection, viewForward);
        if (forward <= 0.02f)
        {
            return false;
        }

        var tanHalfFov = MathF.Tan(camera.FovY * 0.5f * (MathF.PI / 180f));
        if (tanHalfFov <= 0.000001f)
        {
            return false;
        }

        var aspect = screenWidth / (float)screenHeight;
        var x = Vector3.Dot(normalizedDirection, viewRight) / (forward * tanHalfFov * aspect);
        var y = Vector3.Dot(normalizedDirection, viewUp) / (forward * tanHalfFov);
        if (MathF.Abs(x) > 1.35f || MathF.Abs(y) > 1.35f)
        {
            return false;
        }

        screenPoint = new Vector2(
            screenWidth * (0.5f + x * 0.5f),
            screenHeight * (0.5f - y * 0.5f));
        return true;
    }

    private void DrawWorld()
    {
        _lastDrawnSurfaceCount = 0;
        _lastDrawSceneHash = 0UL;
        _worldInstanceBatches.Clear();

        if (_world.ChunkCountX == 0 || _world.ChunkCountZ == 0)
        {
            return;
        }

        var measuredFps = _platform.GetFps();
        var renderDistance = GetAdaptiveRenderDistance(measuredFps, advanceSmoothing: false);
        var distanceFadeBand = GetDistanceFadeBand();
        var softRenderDistance = renderDistance + distanceFadeBand;
        var edgeKeepStartDistance = Math.Max(1f, renderDistance - distanceFadeBand);
        var edgeKeepStartSq = edgeKeepStartDistance * edgeKeepStartDistance;
        var (lodNearDistance, lodMidDistance, lodBlendBand) = GetLodTransitionProfile(renderDistance);
        var centerX = (int)MathF.Floor(_player.Position.X);
        var centerY = (int)MathF.Floor(_player.Position.Y);
        var centerZ = (int)MathF.Floor(_player.Position.Z);
        var belowY = _graphics.Quality switch
        {
            GraphicsQuality.Low => 12,
            GraphicsQuality.Medium => 16,
            _ => 20
        };
        var aboveY = _graphics.Quality switch
        {
            GraphicsQuality.Low => 16,
            GraphicsQuality.Medium => 20,
            _ => 24
        };
        var centerSurfaceY = _world.GetTerrainTopY(centerX, centerZ);
        var minY = Math.Max(0, Math.Min(centerY - belowY, centerSurfaceY - 16));
        var maxY = Math.Min(_world.Height - 1, Math.Max(centerY + aboveY, centerSurfaceY + 20));

        var maxDistSq = softRenderDistance * softRenderDistance;
        var baseRenderDistSq = renderDistance * renderDistance;
        var farLodDistance = _graphics.Quality switch
        {
            GraphicsQuality.Low => Math.Min(renderDistance, 8),
            GraphicsQuality.Medium => Math.Min(renderDistance, 14),
            _ => Math.Min(renderDistance, 30)
        };
        var veryFarLodDistance = _graphics.Quality switch
        {
            GraphicsQuality.Low => Math.Min(renderDistance, 10),
            GraphicsQuality.Medium => Math.Min(renderDistance, 18),
            _ => Math.Min(renderDistance, 52)
        };
        var ultraFarLodDistance = _graphics.Quality switch
        {
            GraphicsQuality.Low => Math.Min(renderDistance, 12),
            GraphicsQuality.Medium => Math.Min(renderDistance, 22),
            _ => Math.Min(renderDistance, 72)
        };
        var farLodSq = farLodDistance * farLodDistance;
        var veryFarLodSq = veryFarLodDistance * veryFarLodDistance;
        var sparseFarSamplingEnabled = renderDistance >= 72;
        var forwardXZ = ToHorizontalForward(_player.LookDirection);
        var directionalCullDistance = _graphics.Quality switch
        {
            GraphicsQuality.Low => Math.Min(renderDistance, 8),
            GraphicsQuality.Medium => Math.Min(renderDistance, 14),
            _ => Math.Min(renderDistance, 20)
        };
        var directionalCullSq = directionalCullDistance * directionalCullDistance;
        var foliageDistance = _graphics.Quality switch
        {
            GraphicsQuality.Low => 10,
            GraphicsQuality.Medium => Math.Min(renderDistance, 16),
            _ => Math.Min(renderDistance, 28)
        };
        var foliageDistSq = foliageDistance * foliageDistance;
        var foliageFadeBand = GetFoliageFadeBand();
        var canopyBand = _graphics.Quality switch
        {
            GraphicsQuality.Low => 10,
            GraphicsQuality.Medium => 12,
            _ => 14
        };
        var chunkRadius = Math.Max(1, ((int)MathF.Ceiling(softRenderDistance) + _world.ChunkSize - 1) / _world.ChunkSize);
        if (_state != AppState.Playing)
        {
            _world.EnsureChunksAround(_player.Position, chunkRadius);
        }

        var clampedCenterX = Math.Clamp(centerX, 0, _world.Width - 1);
        var clampedCenterZ = Math.Clamp(centerZ, 0, _world.Depth - 1);
        var centerChunkX = clampedCenterX / _world.ChunkSize;
        var centerChunkZ = clampedCenterZ / _world.ChunkSize;
        var minChunkX = Math.Max(0, centerChunkX - chunkRadius);
        var maxChunkX = Math.Min(_world.ChunkCountX - 1, centerChunkX + chunkRadius);
        var minChunkZ = Math.Max(0, centerChunkZ - chunkRadius);
        var maxChunkZ = Math.Min(_world.ChunkCountZ - 1, centerChunkZ + chunkRadius);

        if (_state != AppState.Playing)
        {
            _world.RebuildDirtyChunkSurfaces(centerChunkX, centerChunkZ, GetSurfaceDrawFallbackBudget());
        }

        var collectSceneMetrics = _sceneMetricsEnabled;
        var progressiveChunkReveal = _state == AppState.Playing && _runtimeSeconds > 0.05f;
        var drawnSurfaceCount = 0;
        var sceneHash = 1469598103934665603UL;

        for (var chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
        {
            for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
            {
                var chunkMinX = chunkX * _world.ChunkSize;
                var chunkMinZ = chunkZ * _world.ChunkSize;
                var chunkMaxX = Math.Min(_world.Width, chunkMinX + _world.ChunkSize);
                var chunkMaxZ = Math.Min(_world.Depth, chunkMinZ + _world.ChunkSize);
                var nearestX = Math.Clamp(_player.Position.X, chunkMinX, chunkMaxX);
                var nearestZ = Math.Clamp(_player.Position.Z, chunkMinZ, chunkMaxZ);
                var chunkDx = nearestX - _player.Position.X;
                var chunkDz = nearestZ - _player.Position.Z;
                var chunkDistSq = chunkDx * chunkDx + chunkDz * chunkDz;
                if (chunkDistSq > maxDistSq)
                {
                    continue;
                }

                if (chunkDistSq > directionalCullSq)
                {
                    var chunkCenterX = chunkMinX + _world.ChunkSize * 0.5f;
                    var chunkCenterZ = chunkMinZ + _world.ChunkSize * 0.5f;
                    var toChunkX = chunkCenterX - _player.Position.X;
                    var toChunkZ = chunkCenterZ - _player.Position.Z;
                    var toChunkLenSq = toChunkX * toChunkX + toChunkZ * toChunkZ;
                    if (toChunkLenSq > 0.0001f)
                    {
                        var dot = (toChunkX * forwardXZ.X + toChunkZ * forwardXZ.Z) / MathF.Sqrt(toChunkLenSq);
                        if (dot < 0.15f)
                        {
                            continue;
                        }
                    }
                }

                _world.TryGetChunkSurfaceBlocks(chunkX, chunkZ, out var surfaceBlocks);
                var chunkReveal = GetChunkRevealFactor(chunkX, chunkZ);

                for (var i = 0; i < surfaceBlocks.Count; i++)
                {
                    var surface = surfaceBlocks[i];
                    if (surface.Y < minY || surface.Y > maxY + canopyBand)
                    {
                        continue;
                    }

                    var dx = surface.X + 0.5f - _player.Position.X;
                    var dz = surface.Z + 0.5f - _player.Position.Z;
                    var distSq = dx * dx + dz * dz;
                    if (distSq > maxDistSq)
                    {
                        continue;
                    }

                    var distance = 0f;

                    if (distSq > directionalCullSq)
                    {
                        distance = MathF.Sqrt(distSq);
                        var dot = (dx * forwardXZ.X + dz * forwardXZ.Z) / distance;
                        if (dot < 0.15f)
                        {
                            continue;
                        }
                    }

                    if (distSq > farLodSq)
                    {
                        if (!surface.TopVisible)
                        {
                            continue;
                        }

                        if (surface.Block == BlockType.Leaves
                            && (_graphics.Quality != GraphicsQuality.High || distSq > veryFarLodSq))
                        {
                            continue;
                        }
                    }

                    float keepChance;
                    if (distSq <= edgeKeepStartSq)
                    {
                        keepChance = 1f;
                    }
                    else
                    {
                        if (distance <= 0f)
                        {
                            distance = MathF.Sqrt(distSq);
                        }

                        keepChance = GetDistanceEdgeKeep(distance, renderDistance, distanceFadeBand);
                    }

                    if (keepChance <= 0f)
                    {
                        continue;
                    }

                    if (surface.Block == BlockType.Leaves && distSq > foliageDistSq)
                    {
                        keepChance *= GetFoliageKeepChance(distance, foliageDistance, foliageFadeBand);
                    }

                    if (sparseFarSamplingEnabled && distSq > veryFarLodSq)
                    {
                        keepChance *= GetSparseFarKeepChance(surface.Block, distance, veryFarLodDistance, ultraFarLodDistance, renderDistance);
                    }

                    if (keepChance <= 0f)
                    {
                        continue;
                    }

                    if (keepChance < 0.999f && !PassSpatialDither(surface.X, surface.Y, surface.Z, (int)surface.Block * 37 + 17, keepChance))
                    {
                        continue;
                    }

                    if (progressiveChunkReveal && chunkReveal < 0.999f)
                    {
                        var revealKeep = 0.32f + chunkReveal * 0.68f;
                        if (!PassSpatialDither(surface.X, surface.Y, surface.Z, 911, revealKeep))
                        {
                            continue;
                        }
                    }

                    if (distance <= 0f)
                    {
                        distance = MathF.Sqrt(distSq);
                    }

                    var lodBlend = GetLodBlendWeights(distance, lodNearDistance, lodMidDistance, lodBlendBand);
                    keepChance *= GetLodKeepChance(surface, lodBlend);

                    var distanceFactor = distSq / Math.Max(1f, baseRenderDistSq);
                    var center = new Vector3(surface.X + 0.5f, surface.Y + 0.5f, surface.Z + 0.5f);
                    var baseColor = surface.Block switch
                    {
                        BlockType.Grass => new Color(98, 144, 82, 255),
                        BlockType.Dirt => new Color(148, 111, 76, 255),
                        BlockType.Stone => new Color(134, 129, 121, 255),
                        BlockType.Wood => new Color(132, 98, 61, 255),
                        BlockType.Leaves => new Color(82, 130, 74, 255),
                        _ => Color.White
                    };
                    var color = BuildLodBlendedColor(baseColor, surface, distance, distanceFactor, lodBlend);
                    if (chunkReveal < 0.999f)
                    {
                        color = LerpColor(_graphics.FogColor, color, chunkReveal);
                    }

                    QueueWorldCubeInstance(center, color, GetDominantLodTier(lodBlend));
                    DrawDecorativeVegetationAccent(surface, distance, chunkReveal);

                    if (collectSceneMetrics)
                    {
                        drawnSurfaceCount++;
                        if ((drawnSurfaceCount & 7) == 0)
                        {
                            sceneHash = MixSceneHash(sceneHash, surface.X, surface.Y, surface.Z, surface.Block);
                        }
                    }
                }
            }
        }

        FlushWorldCubeInstances();

        if (collectSceneMetrics)
        {
            _lastDrawnSurfaceCount = drawnSurfaceCount;
            _lastDrawSceneHash = drawnSurfaceCount > 0 ? sceneHash : 0UL;
        }
        else
        {
            _lastDrawnSurfaceCount = 0;
            _lastDrawSceneHash = 0UL;
        }
    }

    private (float NearDistance, float MidDistance, float BlendBand) GetLodTransitionProfile(float renderDistance)
    {
        var nearDistance = _graphics.Quality switch
        {
            GraphicsQuality.Low => Math.Min(renderDistance, 5.5f),
            GraphicsQuality.Medium => Math.Min(renderDistance, 8.5f),
            _ => Math.Min(renderDistance, 14f)
        };
        var midDistance = _graphics.Quality switch
        {
            GraphicsQuality.Low => Math.Min(renderDistance, 9f),
            GraphicsQuality.Medium => Math.Min(renderDistance, 15f),
            _ => Math.Min(renderDistance, 28f)
        };
        var blendBand = _graphics.Quality switch
        {
            GraphicsQuality.Low => 1.8f,
            GraphicsQuality.Medium => 2.6f,
            _ => 4.2f
        };

        nearDistance = Math.Max(2f, nearDistance);
        midDistance = Math.Max(nearDistance + 1f, midDistance);
        return (nearDistance, midDistance, blendBand);
    }

    private static LodBlendWeights GetLodBlendWeights(float distance, float nearDistance, float midDistance, float blendBand)
    {
        var band = Math.Max(0.001f, blendBand);
        var nearToMid = SmoothStep01((distance - (nearDistance - band)) / (band * 2f));
        var midToFar = SmoothStep01((distance - (midDistance - band)) / (band * 2f));

        var near = Math.Clamp(1f - nearToMid, 0f, 1f);
        var far = Math.Clamp(midToFar, 0f, 1f);
        var mid = Math.Clamp(nearToMid - midToFar, 0f, 1f);

        var sum = near + mid + far;
        return new LodBlendWeights(near / sum, mid / sum, far / sum);
    }

    private float GetLodKeepChance(WorldMap.SurfaceBlock surface, LodBlendWeights weights)
    {
        var midKeep = surface.Block switch
        {
            BlockType.Leaves => 0.74f,
            BlockType.Wood => 0.9f,
            BlockType.Grass or BlockType.Dirt or BlockType.Stone => 0.9f,
            _ => 0.82f
        };
        var farKeep = surface.Block switch
        {
            BlockType.Leaves => 0.18f,
            BlockType.Wood => 0.62f,
            BlockType.Grass or BlockType.Dirt or BlockType.Stone => 0.72f,
            _ => 0.45f
        };

        if (!surface.TopVisible)
        {
            midKeep *= 0.86f;
            farKeep *= 0.5f;
        }

        return Math.Clamp(weights.Near + weights.Mid * midKeep + weights.Far * farKeep, 0f, 1f);
    }

    private Color BuildLodBlendedColor(
        Color baseColor,
        WorldMap.SurfaceBlock surface,
        float distance,
        float distanceFactor,
        LodBlendWeights lodBlend)
    {
        if (lodBlend.Near >= 0.999f)
        {
            return ApplyVisualSurfaceStyle(baseColor, surface, distance);
        }

        if (lodBlend.Mid >= 0.999f)
        {
            return ApplyMidSurfaceStyle(baseColor, surface, distance);
        }

        if (lodBlend.Far >= 0.999f)
        {
            return ApplyFarSurfaceStyle(baseColor, surface, distance);
        }

        if (lodBlend.Near > 0.0001f && lodBlend.Far <= 0.0001f)
        {
            var nearColor = ApplyVisualSurfaceStyle(baseColor, surface, distance);
            var midColor = ApplyMidSurfaceStyle(baseColor, surface, distance);
            var t = lodBlend.Mid / Math.Max(0.0001f, lodBlend.Near + lodBlend.Mid);
            return LerpColor(nearColor, midColor, t);
        }

        if (lodBlend.Far > 0.0001f && lodBlend.Near <= 0.0001f)
        {
            var midColor = ApplyMidSurfaceStyle(baseColor, surface, distance);
            var farColor = ApplyFarSurfaceStyle(baseColor, surface, distance);
            var t = lodBlend.Far / Math.Max(0.0001f, lodBlend.Mid + lodBlend.Far);
            return LerpColor(midColor, farColor, t);
        }

        var isTerrainBlock = surface.Block is BlockType.Grass or BlockType.Dirt or BlockType.Stone;
        if (isTerrainBlock && distanceFactor > 0.62f)
        {
            return ApplyFarSurfaceStyle(baseColor, surface, distance);
        }

        var blendedNear = ApplyVisualSurfaceStyle(baseColor, surface, distance);
        var blendedFar = ApplyFarSurfaceStyle(baseColor, surface, distance);
        return LerpColor(blendedNear, blendedFar, 0.5f);
    }

    private Color ApplyMidSurfaceStyle(Color baseColor, WorldMap.SurfaceBlock surface, float distance)
    {
        var noise = (Math.Abs((surface.X * 73856093) ^ (surface.Y * 19349663) ^ (surface.Z * 83492791)) % 13) - 6;
        var noiseScale = _graphics.TextureNoiseStrength * 0.82f;
        var textured = ApplyBlockMaterial(baseColor, surface, noise, noiseScale, distance);

        var brightness = surface.TopVisible ? 1.00f : 0.82f;
        brightness += (surface.VisibleFaces - 2) * 0.024f;
        var skyLight = 0.88f + Math.Clamp(surface.SkyExposure / 5f, 0f, 1f) * 0.16f;
        var aoShade = 1f - Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f) * 0.16f;
        var reliefLift = 1f + Math.Clamp(surface.ReliefExposure / 4f, 0f, 1f) * 0.07f;
        var sunVisibility = GetSunVisibility01(surface);
        var shadow = 1f - sunVisibility;
        var brightnessFactor = brightness * skyLight * aoShade * reliefLift * GetSunLightFactor(surface, litStrength: 0.18f, shadowStrength: surface.TopVisible ? 0.18f : 0.12f) * _graphics.LightStrength;
        var lit = MultiplyRgb(textured, brightnessFactor);
        var warmTint = 0.07f + Math.Clamp(surface.SkyExposure / 5f, 0f, 1f) * 0.08f + sunVisibility * 0.07f;
        var coolTint = Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f) * 0.07f + (!surface.TopVisible ? 0.02f : 0f) + shadow * 0.06f;
        var toned = ApplyLightTemperature(lit, warmTint, coolTint);
        var contrasted = ApplyContrast(toned, 1f + (_graphics.Contrast - 1f) * 0.72f);
        var postColor = contrasted;
        if (_graphics.FogEnabled)
        {
            var fogT = Math.Clamp((distance - _graphics.FogNear) / (_graphics.FogFar - _graphics.FogNear), 0f, 1f);
            fogT = SmoothStep01(fogT);
            postColor = LerpColor(contrasted, _graphics.FogColor, fogT);
        }

        return ApplyCinematicPostProcessColor(postColor, distance, surface.TopVisible);
    }

    private Color ApplyMidVisualStyle(Color baseColor, BlockType block, int x, int y, int z, bool topVisible, int visibleFaces, int skyExposure, float distance)
    {
        return ApplyMidSurfaceStyle(baseColor, new WorldMap.SurfaceBlock(x, y, z, block, visibleFaces, topVisible, skyExposure), distance);
    }

    private static WorldLodTier GetDominantLodTier(LodBlendWeights weights)
    {
        if (weights.Far >= weights.Mid && weights.Far >= weights.Near)
        {
            return WorldLodTier.Far;
        }

        return weights.Mid >= weights.Near
            ? WorldLodTier.Mid
            : WorldLodTier.Near;
    }

    private void QueueWorldCubeInstance(Vector3 center, Color color, WorldLodTier lodTier)
    {
        var quantizedColor = QuantizeInstanceColor(color);
        var key = new InstancedBatchKey(quantizedColor.R, quantizedColor.G, quantizedColor.B, quantizedColor.A, lodTier);
        if (!_worldInstanceBatches.TryGetValue(key, out var transforms))
        {
            transforms = [];
            _worldInstanceBatches[key] = transforms;
        }

        transforms.Add(Matrix4x4.CreateTranslation(center));
    }

    private void FlushWorldCubeInstances()
    {
        foreach (var pair in _worldInstanceBatches)
        {
            var key = pair.Key;
            var transforms = pair.Value;
            if (transforms.Count == 0)
            {
                continue;
            }

            var color = new Color(key.R, key.G, key.B, key.A);
            _platform.DrawCubeInstanced(transforms, color);
            if (_graphics.DrawBlockWires)
            {
                for (var i = 0; i < transforms.Count; i++)
                {
                    var transform = transforms[i];
                    var center = new Vector3(transform.M41, transform.M42, transform.M43);
                    _platform.DrawCubeWires(center, 1f, 1f, 1f, new Color(0, 0, 0, 35));
                }
            }
        }

        _worldInstanceBatches.Clear();
    }

    private Color QuantizeInstanceColor(Color color)
    {
        var step = _graphics.Quality switch
        {
            GraphicsQuality.Low => 22,
            GraphicsQuality.Medium => 16,
            _ => 12
        };
        var alphaStep = _graphics.Quality == GraphicsQuality.High ? 8 : 12;
        return new Color(
            QuantizeChannel(color.R, step),
            QuantizeChannel(color.G, step),
            QuantizeChannel(color.B, step),
            QuantizeChannel(color.A, alphaStep));
    }

    private static byte QuantizeChannel(byte value, int step)
    {
        if (value == 255 || step <= 1)
        {
            return value;
        }

        var rounded = (int)MathF.Round(value / (float)step) * step;
        return (byte)Math.Clamp(rounded, 0, 255);
    }

    private float GetDistanceFadeBand()
    {
        return _graphics.Quality switch
        {
            GraphicsQuality.Low => 2.4f,
            GraphicsQuality.Medium => 3.2f,
            _ => 5.5f
        };
    }

    private float GetFoliageFadeBand()
    {
        return _graphics.Quality switch
        {
            GraphicsQuality.Low => 2.0f,
            GraphicsQuality.Medium => 3.0f,
            _ => 4.5f
        };
    }

    private float GetDistanceEdgeKeep(float distance, float renderDistance, float fadeBand)
    {
        var start = Math.Max(1f, renderDistance - fadeBand);
        var end = renderDistance + fadeBand;
        if (distance <= start)
        {
            return 1f;
        }

        if (distance >= end)
        {
            return 0f;
        }

        var t = (distance - start) / Math.Max(0.001f, end - start);
        return 1f - SmoothStep01(t);
    }

    private float GetFoliageKeepChance(float distance, float foliageDistance, float fadeBand)
    {
        var start = foliageDistance;
        var end = foliageDistance + fadeBand;
        if (distance <= start)
        {
            return 1f;
        }

        if (distance >= end)
        {
            return 0f;
        }

        var t = (distance - start) / Math.Max(0.001f, end - start);
        return 1f - SmoothStep01(t);
    }

    private float GetDecorativeVegetationDistance()
    {
        return _graphics.Quality switch
        {
            GraphicsQuality.Low => 4.5f,
            GraphicsQuality.Medium => 7.0f,
            _ => 10.0f
        };
    }

    private DecorativeVegetationKind GetDecorativeVegetationKind(WorldMap.SurfaceBlock surface, float distance)
    {
        if (surface.Block != BlockType.Grass || !surface.TopVisible)
        {
            return DecorativeVegetationKind.None;
        }

        var maxDistance = GetDecorativeVegetationDistance();
        if (distance > maxDistance)
        {
            return DecorativeVegetationKind.None;
        }

        var hash = Math.Abs((surface.X * 130912063) ^ (surface.Z * 19349663) ^ (surface.Y * 83492791)) % 19;
        var fadeStart = Math.Max(2f, maxDistance - 2.6f);
        if (distance > fadeStart)
        {
            var keep = 1f - SmoothStep01((distance - fadeStart) / Math.Max(0.001f, maxDistance - fadeStart));
            if (!PassSpatialDither(surface.X, surface.Y, surface.Z, 5441, keep))
            {
                return DecorativeVegetationKind.None;
            }
        }

        return _graphics.Quality switch
        {
            GraphicsQuality.Low => hash <= 1 ? DecorativeVegetationKind.Grass : DecorativeVegetationKind.None,
            GraphicsQuality.Medium => hash switch
            {
                0 => DecorativeVegetationKind.Flower,
                >= 1 and <= 3 => DecorativeVegetationKind.Grass,
                _ => DecorativeVegetationKind.None
            },
            _ => (surface.ReliefExposure >= 4 ? hash + 2 : hash) switch
            {
                0 => DecorativeVegetationKind.Bush,
                1 => DecorativeVegetationKind.Flower,
                >= 2 and <= 5 => DecorativeVegetationKind.Grass,
                _ => DecorativeVegetationKind.None
            }
        };
    }

    private void DrawDecorativeVegetationAccent(WorldMap.SurfaceBlock surface, float distance, float chunkReveal)
    {
        var kind = GetDecorativeVegetationKind(surface, distance);
        if (kind == DecorativeVegetationKind.None)
        {
            return;
        }

        var sun = GetSunVisibility01(surface);
        var cavity = Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f);
        var fade = 1f - SmoothStep01(distance / Math.Max(0.001f, GetDecorativeVegetationDistance()));
        var visibility = Math.Clamp(0.35f + fade * 0.65f, 0f, 1f) * chunkReveal;
        var ground = new Vector3(surface.X + 0.5f, surface.Y + 1.02f, surface.Z + 0.5f);
        var tint = ApplyLightTemperature(
            new Color(92, 154, 88, 255),
            warmTint: 0.03f + sun * 0.06f,
            coolTint: cavity * 0.06f);
        var stem = ScaleAlpha(ChangeRgb(tint, -8, 4, -6), visibility);
        var leaf = ScaleAlpha(ChangeRgb(tint, -2, 8, -3), visibility);
        var leafShade = ScaleAlpha(ChangeRgb(tint, -10, -6, 2), visibility);

        switch (kind)
        {
            case DecorativeVegetationKind.Grass:
                _platform.DrawCube(ground + new Vector3(-0.12f, 0.12f, 0.02f), 0.06f, 0.26f, 0.06f, stem);
                _platform.DrawCube(ground + new Vector3(0.04f, 0.15f, -0.06f), 0.06f, 0.30f, 0.06f, leaf);
                _platform.DrawCube(ground + new Vector3(0.13f, 0.11f, 0.08f), 0.05f, 0.22f, 0.05f, leafShade);
                break;
            case DecorativeVegetationKind.Flower:
            {
                var bloomHash = Math.Abs((surface.X * 29791) ^ (surface.Z * 8347) ^ (surface.Y * 19391)) % 4;
                var bloom = bloomHash switch
                {
                    0 => new Color(244, 219, 122, 255),
                    1 => new Color(232, 158, 154, 255),
                    2 => new Color(182, 204, 246, 255),
                    _ => new Color(240, 234, 228, 255)
                };
                bloom = ScaleAlpha(ApplyLightTemperature(bloom, 0.02f + sun * 0.04f, cavity * 0.03f), visibility);
                _platform.DrawCube(ground + new Vector3(0.01f, 0.15f, -0.01f), 0.05f, 0.30f, 0.05f, stem);
                _platform.DrawCube(ground + new Vector3(0.01f, 0.34f, -0.01f), 0.13f, 0.11f, 0.13f, bloom);
                break;
            }
            case DecorativeVegetationKind.Bush:
                _platform.DrawCube(ground + new Vector3(-0.10f, 0.14f, 0.01f), 0.18f, 0.20f, 0.18f, leafShade);
                _platform.DrawCube(ground + new Vector3(0.08f, 0.16f, -0.05f), 0.20f, 0.22f, 0.20f, leaf);
                _platform.DrawCube(ground + new Vector3(0.01f, 0.26f, 0.08f), 0.17f, 0.18f, 0.17f, leaf);
                break;
        }
    }

    private float GetSparseFarKeepChance(BlockType block, float distance, float veryFarDistance, float ultraFarDistance, float renderDistance)
    {
        var veryBand = Math.Max(4f, Math.Min(renderDistance * 0.16f, 16f));
        var ultraBand = Math.Max(4f, Math.Min(renderDistance * 0.18f, 18f));
        var veryT = SmoothStep01((distance - veryFarDistance) / veryBand);
        var ultraT = SmoothStep01((distance - ultraFarDistance) / ultraBand);

        var veryMinKeep = block switch
        {
            BlockType.Wood => 0.40f,
            BlockType.Leaves => 0.14f,
            BlockType.Grass or BlockType.Dirt or BlockType.Stone => 0.40f,
            _ => 0.15f
        };

        var ultraMinKeep = block switch
        {
            BlockType.Wood => 0.14f,
            BlockType.Leaves => 0f,
            BlockType.Grass or BlockType.Dirt or BlockType.Stone => 0.24f,
            _ => 0f
        };

        var keepAfterVery = 1f + (veryMinKeep - 1f) * veryT;
        var keepAfterUltra = 1f + (ultraMinKeep - 1f) * ultraT;
        return Math.Clamp(keepAfterVery * keepAfterUltra, 0f, 1f);
    }

    private float GetChunkRevealFactor(int chunkX, int chunkZ)
    {
        var key = (chunkX, chunkZ);
        if (!_chunkRevealStartedAt.TryGetValue(key, out var startedAt))
        {
            startedAt = _runtimeSeconds;
            _chunkRevealStartedAt[key] = startedAt;
        }

        var duration = GetChunkRevealDurationSeconds();
        var t = (_runtimeSeconds - startedAt) / duration;
        return SmoothStep01(t);
    }

    private float GetChunkRevealDurationSeconds()
    {
        return _graphics.Quality switch
        {
            GraphicsQuality.Low => 0.2f,
            GraphicsQuality.Medium => 0.28f,
            _ => 0.34f
        };
    }

    private static bool PassSpatialDither(int x, int y, int z, int salt, float keepChance)
    {
        if (keepChance >= 0.999f)
        {
            return true;
        }

        if (keepChance <= 0f)
        {
            return false;
        }

        return HashToUnit01(x, y, z, salt) < keepChance;
    }

    private static float HashToUnit01(int x, int y, int z, int salt)
    {
        unchecked
        {
            var h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(salt * 2654435761u);
            h ^= h >> 16;
            h *= 2246822519u;
            h ^= h >> 13;
            h *= 3266489917u;
            h ^= h >> 16;
            return (h & 0x00FFFFFF) / 16777215f;
        }
    }

    private static ulong MixSceneHash(ulong current, int x, int y, int z, BlockType block)
    {
        unchecked
        {
            var h = (ulong)(uint)(x * 73856093) ^ (ulong)(uint)(y * 19349663) ^ (ulong)(uint)(z * 83492791) ^ (ulong)(uint)(((int)block + 17) * 2654435761u);
            current ^= h + 0x9E3779B97F4A7C15UL + (current << 6) + (current >> 2);
            return current;
        }
    }

    private static float SmoothStep01(float t)
    {
        var x = Math.Clamp(t, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    private Color ApplyVisualSurfaceStyle(Color baseColor, WorldMap.SurfaceBlock surface, float distance)
    {
        var noise = (Math.Abs((surface.X * 73856093) ^ (surface.Y * 19349663) ^ (surface.Z * 83492791)) % 15) - 7;
        var noiseScale = _graphics.TextureNoiseStrength;

        if (surface.Block == BlockType.Leaves)
        {
            return ApplyLeafSurfaceStyle(baseColor, surface, noise, noiseScale, distance);
        }

        var textured = ApplyBlockMaterial(baseColor, surface, noise, noiseScale, distance);
        var brightness = surface.TopVisible ? 1.08f : 0.84f;
        brightness += (surface.VisibleFaces - 2) * 0.026f;
        var nearRelief = GetNearReliefContrast(distance);
        var skyLight = 0.84f + Math.Clamp(surface.SkyExposure / 5f, 0f, 1f) * 0.18f;
        var ao = Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f);
        var relief = Math.Clamp(surface.ReliefExposure / 4f, 0f, 1f);
        var sunVisibility = GetSunVisibility01(surface);
        var shadow = 1f - sunVisibility;
        brightness *= skyLight;
        brightness *= 1f - ao * (surface.TopVisible ? 0.20f : 0.15f);
        brightness *= 1f + relief * nearRelief * 0.15f;
        brightness *= GetSunLightFactor(surface, litStrength: 0.24f, shadowStrength: surface.TopVisible ? 0.22f : 0.16f);

        if (surface.TopVisible)
        {
            var cavity = Math.Clamp((3f - surface.SkyExposure) / 3f, 0f, 1f);
            brightness *= 1f - cavity * nearRelief * 0.11f;
        }
        else
        {
            brightness *= 1f - nearRelief * 0.08f;
        }

        var lit = MultiplyRgb(textured, brightness * _graphics.LightStrength);
        var warmTint = 0.08f + Math.Clamp(surface.SkyExposure / 5f, 0f, 1f) * 0.10f + sunVisibility * 0.08f;
        var coolTint = ao * 0.08f + (!surface.TopVisible ? 0.04f : 0f) + shadow * 0.08f;
        var toned = ApplyLightTemperature(lit, warmTint, coolTint);
        var contrasted = ApplyContrast(toned, _graphics.Contrast);

        var postColor = contrasted;
        if (_graphics.FogEnabled)
        {
            var fogT = Math.Clamp((distance - _graphics.FogNear) / (_graphics.FogFar - _graphics.FogNear), 0f, 1f);
            fogT = SmoothStep01(fogT);
            postColor = LerpColor(contrasted, _graphics.FogColor, fogT);
        }

        return ApplyCinematicPostProcessColor(postColor, distance, surface.TopVisible);
    }

    private Color ApplyVisualStyle(Color baseColor, BlockType block, int x, int y, int z, bool topVisible, int visibleFaces, int skyExposure, float distance)
    {
        return ApplyVisualSurfaceStyle(baseColor, new WorldMap.SurfaceBlock(x, y, z, block, visibleFaces, topVisible, skyExposure), distance);
    }

    private float GetNearReliefContrast(float distance)
    {
        var nearRange = _graphics.Quality switch
        {
            GraphicsQuality.Low => 10f,
            GraphicsQuality.Medium => 14f,
            _ => 18f
        };

        if (distance <= 0.001f)
        {
            return 1f;
        }

        return 1f - SmoothStep01(distance / nearRange);
    }

    private static float GetSunVisibility01(WorldMap.SurfaceBlock surface)
    {
        return Math.Clamp(surface.SunVisibility / (float)WorldMap.MaxSunVisibility, 0f, 1f);
    }

    private static float GetSunLightFactor(WorldMap.SurfaceBlock surface, float litStrength, float shadowStrength)
    {
        var sunVisibility = GetSunVisibility01(surface);
        var shadow = 1f - sunVisibility;
        var topBias = surface.TopVisible ? 1f : 0.78f;
        var visibleFaceBias = 1f + Math.Clamp((surface.VisibleFaces - 3) * 0.04f, -0.06f, 0.10f);
        var factor = (1f + sunVisibility * litStrength * topBias - shadow * shadowStrength) * visibleFaceBias;
        return Math.Max(0.56f, factor);
    }

    private static int GetSignedNoise(int x, int y, int z, int primeX, int primeY, int primeZ, int modulus)
    {
        var centered = Math.Max(1, modulus / 2);
        return (Math.Abs((x * primeX) ^ (y * primeY) ^ (z * primeZ)) % modulus) - centered;
    }

    private Color ApplyFarSurfaceStyle(Color baseColor, WorldMap.SurfaceBlock surface, float distance)
    {
        var noise = (Math.Abs((surface.X * 73856093) ^ (surface.Y * 19349663) ^ (surface.Z * 83492791)) % 11) - 5;
        var noiseScale = _graphics.TextureNoiseStrength * 0.7f;
        var textured = ApplyBlockMaterial(baseColor, surface, noise, noiseScale, distance);
        var sunVisibility = GetSunVisibility01(surface);
        var shadow = 1f - sunVisibility;
        var brightness = (surface.TopVisible ? 0.98f : 0.80f) * _graphics.LightStrength;
        brightness *= 1f - Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f) * 0.10f;
        brightness *= 1f + Math.Clamp(surface.ReliefExposure / 4f, 0f, 1f) * 0.04f;
        brightness *= GetSunLightFactor(surface, litStrength: 0.12f, shadowStrength: surface.TopVisible ? 0.14f : 0.10f);
        var lit = MultiplyRgb(textured, brightness);
        var toned = ApplyLightTemperature(
            lit,
            warmTint: 0.05f + Math.Clamp(surface.SkyExposure / 5f, 0f, 1f) * 0.05f + sunVisibility * 0.05f,
            coolTint: Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f) * 0.04f + shadow * 0.05f);
        var contrasted = ApplyContrast(toned, 1f + (_graphics.Contrast - 1f) * 0.55f);
        var postColor = contrasted;
        if (_graphics.FogEnabled)
        {
            var fogT = Math.Clamp((distance - _graphics.FogNear) / (_graphics.FogFar - _graphics.FogNear), 0f, 1f);
            fogT = SmoothStep01(fogT);
            postColor = LerpColor(contrasted, _graphics.FogColor, fogT);
        }

        return ApplyCinematicPostProcessColor(postColor, distance, surface.TopVisible);
    }

    private Color ApplyFarVisualStyle(Color baseColor, BlockType block, int x, int y, int z, bool topVisible, float distance)
    {
        return ApplyFarSurfaceStyle(baseColor, new WorldMap.SurfaceBlock(x, y, z, block, VisibleFaces: topVisible ? 4 : 3, TopVisible: topVisible, SkyExposure: topVisible ? 4 : 1), distance);
    }

    private Color ApplyLeafSurfaceStyle(Color baseColor, WorldMap.SurfaceBlock surface, int noise, float noiseScale, float distance)
    {
        var variation = (int)(noise * 0.54f * noiseScale);
        var tintNoise = (Math.Abs((surface.X * 29791) ^ (surface.Z * 19391) ^ (surface.Y * 8347)) % 5) - 2;
        var canopyPatch = GetSignedNoise(surface.X, surface.Y, surface.Z, 46237, 18979, 9157, 9);
        var canopyShade = Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f);
        var canopySun = GetSunVisibility01(surface);
        var textured = ChangeRgb(
            baseColor,
            variation / 5 + tintNoise + canopyPatch / 2 - (int)MathF.Round(canopyShade * 3f),
            variation + GetLeafDensityDelta(surface, distance) + canopyPatch * 2 + (int)MathF.Round(canopySun * 4f),
            variation / 6 - tintNoise + canopyPatch / 3 + (int)MathF.Round(canopyShade * 3f) - (int)MathF.Round(canopySun * 2f));
        textured = ApplyHeightTint(textured, surface.Block, surface.Y);
        textured = ApplyMaterialTopographyTint(textured, surface, distance);
        var sunVisibility = GetSunVisibility01(surface);
        var shadow = 1f - sunVisibility;
        var brightness = (surface.TopVisible ? 1.00f : 0.88f) * _graphics.LightStrength;
        brightness *= 1f - Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f) * 0.12f;
        brightness *= 0.90f + Math.Clamp(surface.SkyExposure / 5f, 0f, 1f) * 0.12f;
        brightness *= GetSunLightFactor(surface, litStrength: 0.10f, shadowStrength: 0.08f);
        var lit = MultiplyRgb(textured, brightness);
        var toned = ApplyLightTemperature(
            lit,
            warmTint: 0.05f + Math.Clamp(surface.SkyExposure / 5f, 0f, 1f) * 0.05f + sunVisibility * 0.05f,
            coolTint: Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f) * 0.06f + shadow * 0.04f);
        var contrasted = ApplyContrast(toned, 1f + (_graphics.Contrast - 1f) * 0.45f);

        var postColor = contrasted;
        if (_graphics.FogEnabled)
        {
            var fogT = Math.Clamp((distance - _graphics.FogNear) / (_graphics.FogFar - _graphics.FogNear), 0f, 1f);
            fogT = SmoothStep01(fogT);
            postColor = LerpColor(contrasted, _graphics.FogColor, fogT);
        }

        return ApplyCinematicPostProcessColor(postColor, distance, surface.TopVisible);
    }

    private Color ApplyLeafStyle(Color baseColor, int noise, float noiseScale, float distance)
    {
        var effectiveDistance = _graphics.FogEnabled ? distance : 6f;
        return ApplyLeafSurfaceStyle(baseColor, new WorldMap.SurfaceBlock(0, 0, 0, BlockType.Leaves, VisibleFaces: 4, TopVisible: true, SkyExposure: 4), noise, noiseScale, effectiveDistance);
    }

    private Color ApplyBlockMaterial(Color baseColor, WorldMap.SurfaceBlock surface, int noise, float noiseScale, float distance)
    {
        var dr = (int)(noise * noiseScale);
        var dg = surface.Block switch
        {
            BlockType.Stone => (int)(noise * 0.80f * noiseScale),
            BlockType.Dirt => (int)(noise * 0.86f * noiseScale),
            BlockType.Wood => (int)(noise * 0.76f * noiseScale),
            _ => (int)(noise * 0.88f * noiseScale)
        };
        var db = surface.Block switch
        {
            BlockType.Stone => (int)(noise * 0.46f * noiseScale),
            BlockType.Dirt => (int)(noise * 0.58f * noiseScale),
            BlockType.Wood => (int)(noise * 0.44f * noiseScale),
            _ => (int)(noise * 0.70f * noiseScale)
        };
        var macroNoise = GetSignedNoise(surface.X, surface.Y, surface.Z, 12582917, 741457, 4256249, 17);
        var grainNoise = GetSignedNoise(surface.Y, surface.X, surface.Z, 92821, 19391, 68917, 7);
        var ringNoise = GetSignedNoise(surface.X, surface.Z, surface.Y, 29791, 8347, 19391, 9);
        var patchNoise = GetSignedNoise(surface.X, surface.Y, surface.Z, 159733, 62819, 1402021, 13);
        var streakNoise = GetSignedNoise(surface.X, surface.Y, surface.Z, 22447, 31847, 42071, 11);
        var layerNoise = GetSignedNoise(surface.X, surface.Y, surface.Z, 6143, 9187, 12289, 9);

        switch (surface.Block)
        {
            case BlockType.Grass:
                dr += macroNoise / 2 + patchNoise / 2 + layerNoise / 3 - (surface.TopVisible ? 4 : 0);
                dg += macroNoise + patchNoise + (surface.TopVisible ? 6 : -3) + Math.Max(0, streakNoise);
                db += streakNoise / 2 - 2 - Math.Abs(ringNoise) / 2;
                break;
            case BlockType.Dirt:
                dr += 5 + macroNoise / 2 + patchNoise / 2 + layerNoise;
                dg += ringNoise / 2 + grainNoise - Math.Abs(layerNoise) / 2;
                db -= 6 + Math.Abs(macroNoise) / 2 + Math.Abs(streakNoise) / 2;
                break;
            case BlockType.Stone:
                dr += ringNoise / 2 + patchNoise / 3;
                dg += macroNoise / 3 + layerNoise / 2;
                db -= 3 + Math.Abs(ringNoise) + Math.Max(0, -layerNoise);
                break;
            case BlockType.Wood:
                dr += grainNoise * 3 + 6 + Math.Max(0, streakNoise);
                dg += grainNoise + ringNoise / 2 - 2 + layerNoise / 2;
                db -= 8 + Math.Abs(grainNoise) + Math.Max(0, patchNoise);
                break;
        }

        var textured = ChangeRgb(baseColor, dr, dg, db);
        textured = ApplyHeightTint(textured, surface.Block, surface.Y);
        textured = ApplyMaterialTopographyTint(textured, surface, distance);
        if (surface.TopVisible)
        {
            textured = ApplyNearSurfaceAccent(textured, surface, distance);
        }

        return textured;
    }

    private Color ApplyMaterialTopographyTint(Color color, WorldMap.SurfaceBlock surface, float distance)
    {
        var cavity = Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f);
        var ridge = Math.Clamp(surface.ReliefExposure / 4f, 0f, 1f);
        var sunVisibility = GetSunVisibility01(surface);
        var shadow = 1f - sunVisibility;
        var near = 0.32f + GetNearReliefContrast(distance) * 0.68f;
        var patchNoise = GetSignedNoise(surface.X, surface.Y, surface.Z, 29791, 8347, 19391, 9);
        var driftNoise = GetSignedNoise(surface.X, surface.Y, surface.Z, 19391, 68917, 92821, 11);

        return surface.Block switch
        {
            BlockType.Grass => ChangeRgb(
                color,
                (int)MathF.Round((ridge * 8f - cavity * 9f + patchNoise) * near),
                (int)MathF.Round((sunVisibility * 5f - ridge * 5f - cavity * 7f + driftNoise) * near),
                (int)MathF.Round((cavity * 4f - ridge * 3f - patchNoise * 0.5f) * near)),
            BlockType.Dirt => ChangeRgb(
                color,
                (int)MathF.Round((ridge * 10f - cavity * 7f + patchNoise * 1.2f) * near),
                (int)MathF.Round((ridge * 3f - cavity * 4f + driftNoise * 0.5f) * near),
                (int)MathF.Round((-ridge * 6f - cavity * 3f - Math.Abs(patchNoise)) * near)),
            BlockType.Stone => ChangeRgb(
                color,
                (int)MathF.Round((ridge * 5f + sunVisibility * 3f - cavity * 6f + patchNoise * 0.6f) * near),
                (int)MathF.Round((ridge * 4f - cavity * 4f + driftNoise * 0.4f) * near),
                (int)MathF.Round((cavity * 6f + shadow * 4f - ridge * 2f - patchNoise) * near)),
            BlockType.Wood => ChangeRgb(
                color,
                (int)MathF.Round((sunVisibility * 4f + driftNoise * 0.8f - cavity * 5f) * near),
                (int)MathF.Round((ridge * 2f - cavity * 3f - Math.Abs(patchNoise) * 0.5f) * near),
                (int)MathF.Round((-ridge * 4f - cavity * 4f - Math.Abs(driftNoise)) * near)),
            BlockType.Leaves => ChangeRgb(
                color,
                (int)MathF.Round((sunVisibility * 3f - cavity * 5f + patchNoise * 0.5f) * near),
                (int)MathF.Round((sunVisibility * 7f - shadow * 3f - cavity * 6f + driftNoise) * near),
                (int)MathF.Round((cavity * 5f + shadow * 2f - ridge * 2f - Math.Abs(patchNoise) * 0.5f) * near)),
            _ => color
        };
    }

    private Color ApplyNearSurfaceAccent(Color color, WorldMap.SurfaceBlock surface, float distance)
    {
        if (distance > 12f)
        {
            return color;
        }

        var detailNoise = Math.Abs((surface.X * 1597334677) ^ (surface.Z * 1402024253) ^ (surface.Y * 9586891));
        var detail = detailNoise % 7;
        var broadDetail = detailNoise % 11;
        var cavity = (int)MathF.Round(Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f) * 6f);
        var ridge = (int)MathF.Round(Math.Clamp(surface.ReliefExposure / 4f, 0f, 1f) * 7f);
        var sun = (int)MathF.Round(GetSunVisibility01(surface) * 5f);
        return surface.Block switch
        {
            BlockType.Grass when detail <= 1 => ChangeRgb(color, -5 + ridge / 2 - cavity, 11 - ridge + sun / 2, -3 + cavity / 2),
            BlockType.Grass when detail == 2 => ChangeRgb(color, 7 + ridge - cavity / 2, -2 - ridge / 2, -6 + cavity),
            BlockType.Grass when broadDetail >= 9 => ChangeRgb(color, -4 + ridge, 6 - cavity + sun / 2, 3 + cavity / 2),
            BlockType.Dirt when detail <= 1 => ChangeRgb(color, 10 + ridge, 3 + ridge / 2 - cavity / 2, -5 - cavity),
            BlockType.Dirt when detail == 2 => ChangeRgb(color, -6 - cavity / 2, 2 + ridge / 2, -4 - ridge / 2),
            BlockType.Stone when detail <= 1 => ChangeRgb(color, 7 + ridge / 2, 6 + ridge / 2, 4 - cavity),
            BlockType.Stone when detail == 2 => ChangeRgb(color, -8 - cavity / 2, -6 - cavity / 2, -4 + cavity),
            BlockType.Stone when broadDetail >= 9 => ChangeRgb(color, 4 + ridge / 2, 2, 1 - cavity / 2),
            BlockType.Wood when broadDetail <= 2 => ChangeRgb(color, 12 + sun / 2, 5 - cavity / 2, -9 - ridge / 2),
            BlockType.Wood when broadDetail == 3 => ChangeRgb(color, -8 - cavity / 2, -2, -10 - ridge / 2),
            BlockType.Leaves when broadDetail <= 1 => ChangeRgb(color, -3 - cavity / 2, 6 + sun / 2, -1 + cavity / 2),
            BlockType.Leaves when broadDetail == 2 => ChangeRgb(color, 4 + sun / 2, -1 - cavity / 2, 3 + cavity / 2),
            _ => color
        };
    }

    private static Color ApplyHeightTint(Color color, BlockType block, int y)
    {
        var heightFactor = Math.Clamp(y / 64f, 0f, 1f);
        return block switch
        {
            BlockType.Grass => ChangeRgb(color, (int)(heightFactor * -4f), (int)(heightFactor * 6f), (int)(heightFactor * -3f)),
            BlockType.Dirt => ChangeRgb(color, (int)(heightFactor * 6f), (int)(heightFactor * 2f), (int)(heightFactor * -4f)),
            BlockType.Stone => ChangeRgb(color, (int)(heightFactor * 3f), (int)(heightFactor * 3f), (int)(heightFactor * 1f)),
            BlockType.Wood => ChangeRgb(color, (int)(heightFactor * 4f), (int)(heightFactor * 2f), (int)(heightFactor * -2f)),
            BlockType.Leaves => ChangeRgb(color, (int)(heightFactor * -3f), (int)(heightFactor * 6f), (int)(heightFactor * -2f)),
            _ => color
        };
    }

    private static Color ScaleAlpha(Color color, float factor)
    {
        return new Color(color.R, color.G, color.B, (byte)Math.Clamp((int)MathF.Round(color.A * Math.Clamp(factor, 0f, 1f)), 0, 255));
    }

    private int GetLeafDensityDelta(WorldMap.SurfaceBlock surface, float distance)
    {
        if (!surface.TopVisible)
        {
            return -2;
        }

        var clusterNoise = Math.Abs((surface.X * 83492791) ^ (surface.Z * 19349663) ^ (surface.Y * 73856093)) % 5;
        if (distance > 10f)
        {
            return clusterNoise == 0 ? 2 : 0;
        }

        return clusterNoise switch
        {
            0 => 7,
            1 => 3,
            _ => 0
        };
    }

    private static Color ApplyLightTemperature(Color color, float warmTint, float coolTint)
    {
        var toned = color;
        if (coolTint > 0.001f)
        {
            toned = LerpColor(toned, new Color(148, 174, 204, 255), Math.Clamp(coolTint, 0f, 0.25f));
        }

        if (warmTint > 0.001f)
        {
            toned = LerpColor(toned, new Color(236, 210, 176, 255), Math.Clamp(warmTint, 0f, 0.25f));
        }

        return toned;
    }

    private static Color MultiplyRgb(Color color, float factor)
    {
        return new Color(
            (byte)Math.Clamp((int)(color.R * factor), 0, 255),
            (byte)Math.Clamp((int)(color.G * factor), 0, 255),
            (byte)Math.Clamp((int)(color.B * factor), 0, 255),
            color.A);
    }

    private static Color ChangeRgb(Color color, int dr, int dg, int db)
    {
        return new Color(
            (byte)Math.Clamp(color.R + dr, 0, 255),
            (byte)Math.Clamp(color.G + dg, 0, 255),
            (byte)Math.Clamp(color.B + db, 0, 255),
            color.A);
    }

    private static Color ApplyContrast(Color color, float contrast)
    {
        int C(int v) => (int)Math.Clamp(((v / 255f - 0.5f) * contrast + 0.5f) * 255f, 0, 255);
        return new Color((byte)C(color.R), (byte)C(color.G), (byte)C(color.B), color.A);
    }

    private Color ApplyCinematicPostProcessColor(Color color, float distance, bool topVisible)
    {
        var qualityStrength = _graphics.Quality switch
        {
            GraphicsQuality.Low => 0.72f,
            GraphicsQuality.Medium => 0.88f,
            _ => 1f
        };
        var nearFactor = 1f - SmoothStep01(distance / Math.Max(18f, _graphics.FogFar * 0.55f));
        var exposure = 1.02f + qualityStrength * (0.06f + nearFactor * 0.06f) + (topVisible ? 0.01f : 0f);
        var tonemapped = ApplyFilmicTonemap(color, exposure);
        var highlightBoost = qualityStrength * (0.05f + nearFactor * 0.05f + (topVisible ? 0.02f : 0f));
        var shadowBoost = qualityStrength * (0.06f + (1f - nearFactor) * 0.02f + (topVisible ? 0f : 0.02f));
        var saturationBoost = 1.03f + qualityStrength * (0.03f + nearFactor * 0.03f);
        return ApplySceneColorGrade(tonemapped, highlightBoost, shadowBoost, saturationBoost);
    }

    private static Color ApplyFilmicTonemap(Color color, float exposure)
    {
        static byte Tone(byte channel, float exposure)
        {
            var x = channel / 255f * Math.Max(0.1f, exposure);
            const float a = 2.51f;
            const float b = 0.03f;
            const float c = 2.43f;
            const float d = 0.59f;
            const float e = 0.14f;
            var mapped = (x * (a * x + b)) / (x * (c * x + d) + e);
            return (byte)Math.Clamp((int)MathF.Round(Math.Clamp(mapped, 0f, 1f) * 255f), 0, 255);
        }

        return new Color(
            Tone(color.R, exposure),
            Tone(color.G, exposure),
            Tone(color.B, exposure),
            color.A);
    }

    private static Color ApplySceneColorGrade(Color color, float highlightBoost, float shadowBoost, float saturationBoost)
    {
        var r = color.R / 255f;
        var g = color.G / 255f;
        var b = color.B / 255f;
        var luma = r * 0.2126f + g * 0.7152f + b * 0.0722f;
        var shadow = SmoothStep01((0.52f - luma) / 0.52f);
        var highlight = SmoothStep01((luma - 0.40f) / 0.60f);

        var coolT = Math.Clamp(shadow * shadowBoost, 0f, 0.24f);
        var warmT = Math.Clamp(highlight * highlightBoost, 0f, 0.24f);
        var graded = color;
        if (coolT > 0.001f)
        {
            graded = LerpColor(graded, new Color(142, 168, 196, 255), coolT);
        }

        if (warmT > 0.001f)
        {
            graded = LerpColor(graded, new Color(238, 210, 176, 255), warmT);
        }

        var sr = graded.R / 255f;
        var sg = graded.G / 255f;
        var sb = graded.B / 255f;
        var grey = sr * 0.2126f + sg * 0.7152f + sb * 0.0722f;
        var sat = Math.Max(0f, saturationBoost);
        sr = grey + (sr - grey) * sat;
        sg = grey + (sg - grey) * sat;
        sb = grey + (sb - grey) * sat;

        return new Color(
            (byte)Math.Clamp((int)MathF.Round(Math.Clamp(sr, 0f, 1f) * 255f), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(Math.Clamp(sg, 0f, 1f) * 255f), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(Math.Clamp(sb, 0f, 1f) * 255f), 0, 255),
            color.A);
    }

    private static Color LerpColor(Color from, Color to, float t)
    {
        byte Lerp(byte a, byte b) => (byte)Math.Clamp((int)(a + (b - a) * t), 0, 255);
        return new Color(
            Lerp(from.R, to.R),
            Lerp(from.G, to.G),
            Lerp(from.B, to.B),
            (byte)255);
    }

    private static Color GetSkyTopColor()
    {
        return ApplySceneColorGrade(ApplyFilmicTonemap(new Color(66, 122, 196, 255), 1.05f), 0.05f, 0.08f, 1.06f);
    }

    private static Color GetSkyMidColor()
    {
        return ApplySceneColorGrade(ApplyFilmicTonemap(new Color(136, 186, 226, 255), 1.04f), 0.06f, 0.05f, 1.05f);
    }

    private static Color GetSkyHorizonColor()
    {
        return ApplySceneColorGrade(ApplyFilmicTonemap(new Color(246, 216, 186, 255), 1.02f), 0.10f, 0.02f, 1.04f);
    }

    private static Color GetSkyGlowColor()
    {
        return ApplySceneColorGrade(ApplyFilmicTonemap(new Color(255, 190, 128, 255), 1.06f), 0.12f, 0f, 1.06f);
    }

    private void DrawPlayerAvatar()
    {
        if (_cameraMode != CameraMode.ThirdPerson)
        {
            return;
        }

        DrawHumanoidAvatar(
            _player.Position + new Vector3(0f, 0.04f, 0f),
            _player.Yaw,
            _playerVisual,
            new Color(88, 145, 205, 255),
            new Color(64, 112, 176, 255),
            new Color(74, 87, 122, 255),
            new Color(232, 200, 170, 255),
            _botDevice.IsOpen ? new WristDevicePose(_botDeviceVisual.RaiseBlend, _botDeviceVisual.TapBlend) : null);
    }

    private void DrawCompanionAvatar()
    {
        if (_companion is null)
        {
            return;
        }

        DrawHumanoidAvatar(
            _companion.Position + new Vector3(0f, 0.04f, 0f),
            _companion.Yaw,
            _companionVisual,
            new Color(110, 171, 96, 255),
            new Color(84, 141, 74, 255),
            new Color(86, 92, 74, 255),
            new Color(228, 196, 164, 255),
            null);
    }

    private void DrawHumanoidAvatar(
        Vector3 root,
        float yaw,
        PlayerVisualState visual,
        Color torsoColor,
        Color armColor,
        Color legColor,
        Color skinColor,
        WristDevicePose? devicePose)
    {
        var forward = new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
        forward = Vector3.Normalize(forward);
        var right = new Vector3(-forward.Z, 0f, forward.X);
        var walkSwing = MathF.Sin(visual.WalkPhase) * 0.26f * visual.WalkBlend;

        var torso = root + new Vector3(0f, 1.08f, 0f);
        var chest = root + new Vector3(0f, 1.42f, 0f);
        var head = root + new Vector3(0f, 1.82f, 0f) + forward * 0.05f;

        var leftShoulder = chest - right * 0.34f;
        var rightShoulder = chest + right * 0.34f;
        var leftElbow = leftShoulder - Vector3.UnitY * 0.28f + forward * walkSwing;
        var rightElbow = rightShoulder - Vector3.UnitY * 0.28f - forward * walkSwing;
        var leftHand = leftElbow - Vector3.UnitY * 0.30f + forward * 0.04f;
        var rightHand = rightElbow - Vector3.UnitY * 0.30f - forward * 0.04f;

        var leftHip = root + Vector3.UnitY * 0.82f - right * 0.16f;
        var rightHip = root + Vector3.UnitY * 0.82f + right * 0.16f;
        var leftKnee = leftHip - Vector3.UnitY * 0.38f - forward * walkSwing;
        var rightKnee = rightHip - Vector3.UnitY * 0.38f + forward * walkSwing;
        var leftFoot = leftKnee - Vector3.UnitY * 0.36f + forward * 0.04f;
        var rightFoot = rightKnee - Vector3.UnitY * 0.36f - forward * 0.04f;

        Vector3? wristDevice = null;
        if (devicePose is WristDevicePose pose)
        {
            leftElbow = leftShoulder - Vector3.UnitY * (0.18f - 0.08f * pose.RaiseBlend) - right * 0.05f + forward * (0.10f + 0.12f * pose.RaiseBlend);
            leftHand = leftElbow - Vector3.UnitY * 0.20f - right * (0.02f - 0.02f * pose.RaiseBlend) + forward * (0.14f + 0.14f * pose.RaiseBlend);
            rightElbow = rightShoulder - Vector3.UnitY * (0.22f - 0.06f * pose.RaiseBlend) + right * 0.03f + forward * (0.06f + 0.08f * pose.RaiseBlend + 0.10f * pose.TapBlend);
            rightHand = rightElbow - Vector3.UnitY * 0.18f + right * (0.05f - 0.04f * pose.TapBlend) + forward * (0.10f + 0.18f * pose.TapBlend);
            wristDevice = leftHand + forward * 0.12f + right * 0.04f + Vector3.UnitY * 0.01f;
        }

        _platform.DrawCube(torso, 0.62f, 0.80f, 0.38f, torsoColor);
        _platform.DrawCube(chest, 0.66f, 0.18f, 0.34f, armColor);
        _platform.DrawCube(head, 0.46f, 0.46f, 0.46f, skinColor);
        DrawAvatarLimb(leftShoulder, leftElbow, leftHand, armColor, skinColor);
        DrawAvatarLimb(rightShoulder, rightElbow, rightHand, armColor, skinColor);
        DrawAvatarLimb(leftHip, leftKnee, leftFoot, legColor, legColor);
        DrawAvatarLimb(rightHip, rightKnee, rightFoot, legColor, legColor);

        if (wristDevice is Vector3 devicePosition)
        {
            _platform.DrawCube(devicePosition, 0.28f, 0.14f, 0.18f, new Color(38, 52, 58, 255));
            _platform.DrawCube(devicePosition + forward * 0.07f, 0.03f, 0.10f, 0.14f, new Color(118, 255, 228, 165));
        }

        var sunShadowDirection = Vector3.Normalize(new Vector3(-WorldMap.GetSunLightDirection().X, 0f, -WorldMap.GetSunLightDirection().Z));
        _platform.DrawCube(root + sunShadowDirection * 0.22f + new Vector3(0f, -0.03f, 0f), 1.00f, 0.02f, 0.62f, new Color(0, 0, 0, 18));
        _platform.DrawCube(root + new Vector3(0f, -0.02f, 0f), 0.82f, 0.02f, 0.82f, new Color(0, 0, 0, 28));
        _platform.DrawCube(root + new Vector3(0f, -0.01f, 0f), 0.48f, 0.02f, 0.48f, new Color(0, 0, 0, 42));
    }

    private void DrawFirstPersonHand(Camera3D camera)
    {
        if (_cameraMode != CameraMode.FirstPerson || _state == AppState.MainMenu)
        {
            return;
        }

        var forward = Vector3.Normalize(camera.Target - camera.Position);
        var rightRaw = Vector3.Cross(forward, Vector3.UnitY);
        var right = rightRaw.LengthSquared() < 0.000001f
            ? Vector3.UnitX
            : Vector3.Normalize(rightRaw);
        var up = Vector3.Normalize(Vector3.Cross(right, forward));

        var bob = MathF.Sin(_playerVisual.WalkPhase * 2f) * 0.03f * _playerVisual.WalkBlend * _graphics.ViewBobScale;
        var bobOffset = up * bob;
        if (_botDevice.IsOpen)
        {
            var raise = _botDeviceVisual.RaiseBlend;
            var tap = _botDeviceVisual.TapBlend;
            var layout = BuildBotDeviceLayout(camera);

            var deviceForearm = ComposeViewPoint(camera.Position, forward, right, up, 0.28f + 0.08f * raise, -0.64f + 0.08f * raise, -0.58f + 0.14f * raise) + bobOffset;
            var deviceWrist = ComposeViewPoint(camera.Position, forward, right, up, 0.44f + 0.10f * raise, -0.49f + 0.08f * raise, -0.44f + 0.13f * raise) + bobOffset;
            var devicePalm = ComposeViewPoint(camera.Position, forward, right, up, 0.58f + 0.10f * raise, -0.38f + 0.07f * raise, -0.34f + 0.08f * raise) + bobOffset;
            var deviceModule = devicePalm + forward * 0.12f + right * 0.03f + up * 0.02f;

            DrawFirstPersonArm(deviceForearm, deviceWrist, devicePalm, skinColor: new Color(226, 196, 168, 255), sleeveColor: new Color(154, 132, 120, 255));

            _platform.DrawCube(deviceModule, 0.20f, 0.11f, 0.16f, new Color(34, 48, 56, 255));
            _platform.DrawCube(deviceModule - right * 0.11f, 0.06f, 0.13f, 0.18f, new Color(26, 36, 44, 255));
            _platform.DrawCube(deviceModule + forward * 0.06f, 0.03f, 0.06f, 0.10f, new Color(122, 255, 230, 195));
            var beamCenter = Vector3.Lerp(deviceModule + forward * 0.05f, layout.PanelCenter, 0.5f) + up * 0.01f;
            _platform.DrawCube(beamCenter, 0.035f, 0.16f + 0.05f * tap, 0.024f, new Color(118, 255, 228, 92));
            DrawWristHologram(layout, tap);
            return;
        }

        var forearm = ComposeViewPoint(camera.Position, forward, right, up, 0.40f, 0.50f, -0.44f) + bobOffset;
        var wrist = ComposeViewPoint(camera.Position, forward, right, up, 0.56f, 0.38f, -0.34f) + bobOffset;
        var palm = ComposeViewPoint(camera.Position, forward, right, up, 0.68f, 0.31f, -0.27f) + bobOffset;
        var held = palm + forward * 0.14f + right * 0.05f;

        DrawFirstPersonArm(forearm, wrist, palm, skinColor: new Color(226, 196, 168, 255), sleeveColor: new Color(154, 132, 120, 255));
        DrawHeldBlock(held, _hotbar[_selectedHotbarIndex]);
        if (_graphics.DrawBlockWires)
        {
            _platform.DrawCubeWires(held, 0.18f, 0.18f, 0.18f, new Color(0, 0, 0, 35));
        }
    }

    private void DrawAvatarLimb(Vector3 start, Vector3 middle, Vector3 end, Color upperColor, Color lowerColor)
    {
        _platform.DrawCube(Vector3.Lerp(start, middle, 0.5f), 0.18f, 0.32f, 0.18f, upperColor);
        _platform.DrawCube(Vector3.Lerp(middle, end, 0.5f), 0.18f, 0.30f, 0.18f, lowerColor);
    }

    private static Vector3 ComposeViewPoint(Vector3 origin, Vector3 forward, Vector3 right, Vector3 up, float forwardOffset, float rightOffset, float upOffset)
    {
        return origin + forward * forwardOffset + right * rightOffset + up * upOffset;
    }

    private void DrawFirstPersonArm(Vector3 forearm, Vector3 wrist, Vector3 palm, Color skinColor, Color sleeveColor)
    {
        var sleeveShadow = MultiplyRgb(sleeveColor, 0.82f);
        var sleeveHighlight = LerpColor(sleeveColor, new Color(212, 196, 182, 255), 0.18f);
        var skinShadow = MultiplyRgb(skinColor, 0.88f);
        var skinHighlight = LerpColor(skinColor, new Color(244, 224, 206, 255), 0.14f);

        _platform.DrawCube(Vector3.Lerp(forearm, wrist, 0.26f) + new Vector3(-0.012f, -0.016f, 0.012f), 0.18f, 0.20f, 0.20f, sleeveShadow);
        _platform.DrawCube(Vector3.Lerp(forearm, wrist, 0.56f) + new Vector3(-0.004f, -0.016f, 0.010f), 0.15f, 0.15f, 0.16f, sleeveColor);
        _platform.DrawCube(Vector3.Lerp(forearm, wrist, 0.82f) + new Vector3(0.008f, -0.012f, 0.008f), 0.11f, 0.08f, 0.12f, sleeveHighlight);
        _platform.DrawCube(Vector3.Lerp(wrist, palm, 0.28f) + new Vector3(0.018f, -0.013f, 0.010f), 0.14f, 0.10f, 0.12f, skinShadow);
        _platform.DrawCube(Vector3.Lerp(wrist, palm, 0.64f) + new Vector3(0.030f, -0.015f, 0.012f), 0.12f, 0.08f, 0.11f, skinColor);
        _platform.DrawCube(palm + new Vector3(0.040f, -0.016f, 0.020f), 0.082f, 0.040f, 0.092f, skinHighlight);
        _platform.DrawCube(palm + new Vector3(0.074f, -0.012f, 0.030f), 0.034f, 0.024f, 0.052f, skinHighlight);
    }

    private void DrawWristHologram(BotWristDeviceLayout layout, float tapBlend)
    {
        var glow = new Color(118, 255, 228, 20);
        var pulse = 1f + tapBlend * 0.18f;
        _platform.DrawCube(layout.PanelCenter, layout.PanelWorldWidth * 0.04f * pulse, layout.PanelWorldHeight * 0.04f * pulse, layout.Thickness * 0.68f, glow);
    }

    private static BotResourceType MapTargetToResource(BotWristDeviceTarget target)
    {
        return target switch
        {
            BotWristDeviceTarget.Wood => BotResourceType.Wood,
            BotWristDeviceTarget.Stone => BotResourceType.Stone,
            BotWristDeviceTarget.Dirt => BotResourceType.Dirt,
            BotWristDeviceTarget.Leaves => BotResourceType.Leaves,
            _ => BotResourceType.Wood
        };
    }

    private static Color GetHeldBlockColor(BlockType block)
    {
        return block switch
        {
            BlockType.Grass => new Color(100, 154, 84, 255),
            BlockType.Dirt => new Color(148, 108, 74, 255),
            BlockType.Stone => new Color(144, 138, 128, 255),
            BlockType.Wood => new Color(146, 108, 68, 255),
            BlockType.Leaves => new Color(88, 144, 80, 255),
            _ => new Color(220, 220, 220, 255)
        };
    }

    private void DrawHeldBlock(Vector3 held, BlockType block)
    {
        var baseColor = GetHeldBlockColor(block);
        var shadowColor = MultiplyRgb(baseColor, 0.68f);
        var accentColor = LerpColor(baseColor, new Color(244, 236, 214, 255), 0.18f);
        var edgeColor = LerpColor(baseColor, new Color(26, 30, 38, 255), 0.10f);

        _platform.DrawCube(held - new Vector3(0.034f, 0.026f, 0.028f), 0.21f, 0.21f, 0.21f, new Color(18, 20, 26, 72));
        _platform.DrawCube(held - new Vector3(0.016f, 0.012f, 0.012f), 0.19f, 0.19f, 0.19f, shadowColor);
        _platform.DrawCube(held, 0.17f, 0.17f, 0.17f, baseColor);
        _platform.DrawCube(held + new Vector3(0.028f, 0.026f, 0.028f), 0.082f, 0.082f, 0.082f, accentColor);
        _platform.DrawCube(held + new Vector3(-0.020f, -0.020f, -0.020f), 0.050f, 0.050f, 0.050f, edgeColor);
    }

    private void DrawBlockHighlight(BlockRaycastHit? hit, Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (hit is null || _state != AppState.Playing)
        {
            return;
        }

        var h = hit.Value;
        if (!TryGetHitFaceNormalFromRay(rayOrigin, rayDirection, h, out var faceNormal)
            && !TryGetHitFaceNormal(h, out faceNormal))
        {
            var fallbackCenter = new Vector3(h.X + 0.5f, h.Y + 0.5f, h.Z + 0.5f);
            _platform.DrawCubeWires(fallbackCenter, 1.02f, 1.02f, 1.02f, Color.Yellow);
            return;
        }

        DrawHitFaceHighlight(h, faceNormal);
    }

    private void DrawHitFaceHighlight(BlockRaycastHit hit, Vector3 faceNormal)
    {
        var center = new Vector3(hit.X + 0.5f, hit.Y + 0.5f, hit.Z + 0.5f);
        var faceCenter = center + faceNormal * 0.51f;

        const float faceSize = 1.02f;
        const float faceThickness = 0.04f;

        var width = MathF.Abs(faceNormal.X) > 0.5f ? faceThickness : faceSize;
        var height = MathF.Abs(faceNormal.Y) > 0.5f ? faceThickness : faceSize;
        var length = MathF.Abs(faceNormal.Z) > 0.5f ? faceThickness : faceSize;

        _platform.DrawCubeWires(faceCenter, width, height, length, Color.Yellow);
    }

    private static bool TryGetHitFaceNormal(BlockRaycastHit hit, out Vector3 faceNormal)
    {
        var dx = hit.PreviousX - hit.X;
        var dy = hit.PreviousY - hit.Y;
        var dz = hit.PreviousZ - hit.Z;

        var axisCount = (dx != 0 ? 1 : 0) + (dy != 0 ? 1 : 0) + (dz != 0 ? 1 : 0);
        if (axisCount != 1)
        {
            faceNormal = Vector3.Zero;
            return false;
        }

        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || Math.Abs(dz) > 1)
        {
            faceNormal = Vector3.Zero;
            return false;
        }

        faceNormal = new Vector3(dx, dy, dz);
        return true;
    }

    private static bool TryGetHitFaceNormalFromRay(Vector3 rayOrigin, Vector3 rayDirection, BlockRaycastHit hit, out Vector3 faceNormal)
    {
        const float axisEpsilon = 0.0000001f;
        const float tieEpsilon = 0.0001f;

        if (rayDirection.LengthSquared() <= axisEpsilon)
        {
            faceNormal = Vector3.Zero;
            return false;
        }

        rayDirection = Vector3.Normalize(rayDirection);

        var min = new Vector3(hit.X, hit.Y, hit.Z);
        var max = min + Vector3.One;

        var tMin = 0f;
        var tMax = float.PositiveInfinity;

        if (!TryAxis(rayOrigin.X, rayDirection.X, min.X, max.X, ref tMin, ref tMax, out var nearX, out var hasX)
            || !TryAxis(rayOrigin.Y, rayDirection.Y, min.Y, max.Y, ref tMin, ref tMax, out var nearY, out var hasY)
            || !TryAxis(rayOrigin.Z, rayDirection.Z, min.Z, max.Z, ref tMin, ref tMax, out var nearZ, out var hasZ))
        {
            faceNormal = Vector3.Zero;
            return false;
        }

        if (tMin <= tieEpsilon)
        {
            faceNormal = Vector3.Zero;
            return false;
        }

        var xHit = hasX && MathF.Abs(nearX - tMin) <= tieEpsilon;
        var yHit = hasY && MathF.Abs(nearY - tMin) <= tieEpsilon;
        var zHit = hasZ && MathF.Abs(nearZ - tMin) <= tieEpsilon;

        var scoreX = xHit ? MathF.Abs(rayDirection.X) : float.NegativeInfinity;
        var scoreY = yHit ? MathF.Abs(rayDirection.Y) : float.NegativeInfinity;
        var scoreZ = zHit ? MathF.Abs(rayDirection.Z) : float.NegativeInfinity;

        if (scoreX >= scoreY && scoreX >= scoreZ)
        {
            faceNormal = new Vector3(rayDirection.X > 0f ? -1f : 1f, 0f, 0f);
            return true;
        }

        if (scoreY >= scoreZ)
        {
            faceNormal = new Vector3(0f, rayDirection.Y > 0f ? -1f : 1f, 0f);
            return true;
        }

        faceNormal = new Vector3(0f, 0f, rayDirection.Z > 0f ? -1f : 1f);
        return true;
    }

    private static bool TryAxis(
        float origin,
        float direction,
        float min,
        float max,
        ref float tMin,
        ref float tMax,
        out float near,
        out bool hasDirection)
    {
        const float axisEpsilon = 0.0000001f;
        hasDirection = MathF.Abs(direction) > axisEpsilon;
        if (!hasDirection)
        {
            near = float.NegativeInfinity;
            return origin >= min && origin <= max;
        }

        var inv = 1f / direction;
        var nearT = (min - origin) * inv;
        var farT = (max - origin) * inv;
        if (nearT > farT)
        {
            (nearT, farT) = (farT, nearT);
        }

        if (nearT > tMin)
        {
            tMin = nearT;
        }

        if (farT < tMax)
        {
            tMax = farT;
        }

        near = nearT;
        return tMax >= tMin;
    }

    private void DrawHud(bool drawCrosshair)
    {
        if (drawCrosshair)
        {
            var centerX = _platform.GetScreenWidth() / 2;
            var centerY = _platform.GetScreenHeight() / 2;
            _platform.DrawLine(centerX - 8, centerY, centerX + 8, centerY, Color.Black);
            _platform.DrawLine(centerX, centerY - 8, centerX, centerY + 8, Color.Black);
        }

        if (_debugHudEnabled)
        {
            var hudHeight = _companion is null ? 110 : 184;
            _platform.DrawRectangle(12, 12, 432, hudHeight, new Color(8, 18, 26, 166));
            _platform.DrawRectangle(16, 16, 112, 26, new Color(32, 98, 92, 188));
            _platform.DrawUiText("DEBUG HUD", new Vector2(24, 20), 16, 1f, new Color(168, 244, 226, 255));
            _platform.DrawUiText($"FPS {_platform.GetFps()}  |  Render {_lastFrameMs:0.0} ms", new Vector2(24, 50), 17, 1f, new Color(225, 235, 239, 255));
            _platform.DrawUiText($"Pos: {_player.Position.X:0.00}, {_player.Position.Y:0.00}, {_player.Position.Z:0.00}", new Vector2(24, 72), 16, 1f, new Color(201, 220, 225, 255));
            _platform.DrawUiText($"Графика: {GetQualityName(_graphics.Quality)}  |  Камера: {GetCameraModeName(_cameraMode)}", new Vector2(24, 94), 16, 1f, new Color(191, 214, 220, 255));
            if (_companion is not null)
            {
                _platform.DrawUiText($"Бот: {_companion.Status.GetLabel()}", new Vector2(24, 120), 16, 1f, Color.White);
                _platform.DrawUiText($"Активно: {_companion.GetActiveSummary()}", new Vector2(24, 142), 16, 1f, new Color(191, 214, 220, 255));
                _platform.DrawUiText($"Далее: {_companion.GetQueuedSummary()}", new Vector2(24, 164), 16, 1f, new Color(191, 214, 220, 255));
                _platform.DrawUiText($"Запасы: {_companion.GetStockpileSummary()}", new Vector2(24, 186), 16, 1f, new Color(191, 214, 220, 255));
            }

            return;
        }

        var compactWidth = _companion is null ? 250 : 340;
        var compactHeight = _companion is null ? 62 : 92;
        _platform.DrawRectangle(14, 14, compactWidth, compactHeight, new Color(10, 18, 26, 112));
        _platform.DrawRectangle(14, 14, compactWidth, 3, new Color(112, 232, 220, 145));
        _platform.DrawRectangle(22, 22, 78, 24, new Color(28, 78, 80, 156));
        _platform.DrawRectangle(108, 22, 108, 24, new Color(20, 34, 44, 144));
        _platform.DrawUiText($"FPS {_platform.GetFps()}", new Vector2(34, 26), 16, 1f, new Color(190, 248, 236, 255));
        _platform.DrawUiText(GetCameraModeName(_cameraMode), new Vector2(122, 26), 16, 1f, new Color(214, 230, 236, 255));
        if (_companion is not null)
        {
            _platform.DrawRectangle(22, 54, compactWidth - 16, 28, new Color(14, 24, 32, 126));
            _platform.DrawRectangle(22, 54, 6, 28, GetBotStatusAccent(_companion.Status));
            _platform.DrawUiText($"Бот: {_companion.Status.GetLabel()}", new Vector2(38, 58), 16, 1f, Color.White);
            _platform.DrawUiText(_companion.GetActiveSummary(), new Vector2(178, 58), 14, 1f, new Color(198, 220, 228, 255));
        }
    }

    private void DrawCaptureIndicator()
    {
        var label = _captureManager.RecordingIndicatorText;
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        var x = Math.Max(16, _platform.GetScreenWidth() - 212);
        _platform.DrawRectangle(x - 16, 10, 196, 34, new Color(20, 12, 16, 148));
        _platform.DrawRectangle(x - 6, 20, 10, 10, new Color(244, 84, 84, 255));
        _platform.DrawUiText(label, new Vector2(x + 16, 15), 20, 1f, new Color(248, 110, 110, 255));
    }

    private void DrawHotbar()
    {
        var slotWidth = 110;
        var slotHeight = 46;
        var spacing = 8;
        var totalWidth = _hotbar.Length * slotWidth + (_hotbar.Length - 1) * spacing;

        var startX = _platform.GetScreenWidth() / 2 - totalWidth / 2;
        var y = _platform.GetScreenHeight() - slotHeight - 20;

        _platform.DrawRectangle(startX - 16, y - 10, totalWidth + 32, slotHeight + 20, new Color(12, 18, 24, 108));

        for (var i = 0; i < _hotbar.Length; i++)
        {
            var x = startX + i * (slotWidth + spacing);
            var isSelected = i == _selectedHotbarIndex;

            var baseColor = isSelected ? new Color(246, 226, 164, 228) : new Color(16, 24, 32, 188);
            var textColor = isSelected ? new Color(26, 24, 18, 255) : new Color(228, 236, 240, 255);
            _platform.DrawRectangle(x, y, slotWidth, slotHeight, baseColor);
            _platform.DrawRectangle(x, y, slotWidth, 4, isSelected ? new Color(255, 247, 190, 255) : new Color(96, 140, 152, 188));
            _platform.DrawRectangle(x + 8, y + 11, 18, 18, GetHeldBlockColor(_hotbar[i]));
            var label = $"{i + 1}. {GetBlockName(_hotbar[i])}";
            _platform.DrawUiText(label, new Vector2(x + 32, y + 12), 16, 1f, textColor);
        }
    }

    private static Color GetBotStatusAccent(BotStatus status)
    {
        return status switch
        {
            BotStatus.Idle => new Color(120, 206, 214, 255),
            BotStatus.Moving => new Color(122, 194, 255, 255),
            BotStatus.Gathering => new Color(158, 222, 126, 255),
            BotStatus.Building => new Color(244, 198, 116, 255),
            _ => new Color(244, 108, 108, 255)
        };
    }

    private void DrawMenu()
    {
        var title = _state == AppState.MainMenu ? _config.Title : "Пауза";
        var startLabel = _state == AppState.MainMenu ? "Начать игру" : "Продолжить";
        var fullscreenLabel = _platform.IsWindowFullscreen() ? "Оконный режим" : "Полный экран";
        var graphicsLabel = $"Графика: {GetQualityName(_graphics.Quality)}";
        var fogLabel = $"Туман: {(_graphics.FogEnabled ? "Вкл" : "Выкл")}";
        var reliefLabel = $"Контуры рельефа: {(_graphics.ReliefContoursEnabled ? "Вкл" : "Выкл")}";

        _platform.DrawRectangle(0, 0, _platform.GetScreenWidth(), _platform.GetScreenHeight(), new Color(10, 16, 26, 150));
        _platform.DrawUiText(title, new Vector2(_platform.GetScreenWidth() / 2f - 95, 100), 48, 1f, Color.White);

        var start = GetStartButtonRect();
        var fullscreen = GetFullscreenButtonRect();
        var exit = GetExitButtonRect();
        var graphics = GetGraphicsButtonRect();
        var fog = GetFogButtonRect();
        var relief = GetReliefButtonRect();

        _platform.DrawRectangle(start.X, start.Y, start.W, start.H, new Color(228, 233, 241, 240));
        _platform.DrawRectangle(fullscreen.X, fullscreen.Y, fullscreen.W, fullscreen.H, new Color(194, 220, 255, 240));
        _platform.DrawRectangle(exit.X, exit.Y, exit.W, exit.H, new Color(220, 98, 98, 245));
        _platform.DrawRectangle(graphics.X, graphics.Y, graphics.W, graphics.H, new Color(231, 240, 217, 240));
        _platform.DrawRectangle(fog.X, fog.Y, fog.W, fog.H, new Color(233, 225, 248, 240));
        _platform.DrawRectangle(relief.X, relief.Y, relief.W, relief.H, new Color(233, 225, 248, 240));

        _platform.DrawUiText(startLabel, new Vector2(start.X + 70, start.Y + 16), 28, 1f, Color.Black);
        _platform.DrawUiText(fullscreenLabel, new Vector2(fullscreen.X + 70, fullscreen.Y + 16), 28, 1f, Color.Black);
        _platform.DrawUiText("Выход", new Vector2(exit.X + 125, exit.Y + 16), 28, 1f, Color.White);
        _platform.DrawUiText(graphicsLabel, new Vector2(graphics.X + 30, graphics.Y + 16), 28, 1f, Color.Black);
        _platform.DrawUiText(fogLabel, new Vector2(fog.X + 30, fog.Y + 16), 26, 1f, Color.Black);
        _platform.DrawUiText(reliefLabel, new Vector2(relief.X + 30, relief.Y + 16), 26, 1f, Color.Black);

    }

    private (int X, int Y, int W, int H) GetStartButtonRect()
    {
        var x = _platform.GetScreenWidth() / 2 - MenuButtonWidth / 2;
        var y = _platform.GetScreenHeight() / 2 - MenuButtonHeight - MenuButtonsGap;
        return (x, y, MenuButtonWidth, MenuButtonHeight);
    }

    private (int X, int Y, int W, int H) GetFullscreenButtonRect()
    {
        var x = _platform.GetScreenWidth() / 2 - MenuButtonWidth / 2;
        var y = _platform.GetScreenHeight() / 2;
        return (x, y, MenuButtonWidth, MenuButtonHeight);
    }

    private (int X, int Y, int W, int H) GetExitButtonRect()
    {
        var x = _platform.GetScreenWidth() / 2 - MenuButtonWidth / 2;
        var y = _platform.GetScreenHeight() / 2 + MenuButtonHeight + MenuButtonsGap;
        return (x, y, MenuButtonWidth, MenuButtonHeight);
    }

    private (int X, int Y, int W, int H) GetGraphicsButtonRect()
    {
        var x = _platform.GetScreenWidth() / 2 - MenuButtonWidth / 2;
        var y = _platform.GetScreenHeight() / 2 + (MenuButtonHeight + MenuButtonsGap) * 2;
        return (x, y, MenuButtonWidth, MenuButtonHeight);
    }

    private (int X, int Y, int W, int H) GetFogButtonRect()
    {
        var x = _platform.GetScreenWidth() / 2 - MenuButtonWidth / 2;
        var y = _platform.GetScreenHeight() / 2 + (MenuButtonHeight + MenuButtonsGap) * 3;
        return (x, y, MenuButtonWidth, MenuButtonHeight);
    }

    private (int X, int Y, int W, int H) GetReliefButtonRect()
    {
        var x = _platform.GetScreenWidth() / 2 - MenuButtonWidth / 2;
        var y = _platform.GetScreenHeight() / 2 + (MenuButtonHeight + MenuButtonsGap) * 4;
        return (x, y, MenuButtonWidth, MenuButtonHeight);
    }

    private void DrawBotDeviceOverlay()
    {
        if (_companion is null || !_botDevice.IsOpen || !TryBuildBotDeviceLayout(out var layout))
        {
            return;
        }

        var panel = layout.PanelRect;
        var accent = new Color(164, 255, 238, 244);
        var glow = new Color(74, 176, 188, 176);
        var dim = new Color(198, 232, 236, 236);
        var panelShadow = new Color(8, 20, 26, 134);
        var panelFill = new Color(18, 44, 52, 198);
        var panelHeader = new Color(28, 92, 102, 214);
        var panelEdge = new Color(110, 255, 232, 110);
        var dividerColor = new Color(74, 176, 188, 184);

        _platform.DrawRectangle(panel.X - 10, panel.Y - 10, panel.W + 20, panel.H + 20, panelShadow);
        _platform.DrawRectangle(panel.X, panel.Y, panel.W, panel.H, panelFill);
        _platform.DrawRectangle(panel.X + 2, panel.Y + 2, panel.W - 4, 58, panelHeader);
        _platform.DrawRectangle(panel.X, panel.Y, panel.W, 2, panelEdge);
        _platform.DrawRectangle(panel.X, panel.Y + panel.H - 2, panel.W, 2, panelEdge);
        _platform.DrawRectangle(panel.X, panel.Y, 2, panel.H, panelEdge);
        _platform.DrawRectangle(panel.X + panel.W - 2, panel.Y, 2, panel.H, panelEdge);

        DrawBotDeviceTapOverlay(layout);

        _platform.DrawUiText("Наручный модуль бота", GetScaledBotDevicePoint(layout, 24, 20), 24, 1f, accent);
        _platform.DrawUiText("Проекция активна", GetScaledBotDevicePoint(layout, 24, 60), 16, 1f, dim);
        _platform.DrawUiText($"Статус: {_companion.Status.GetLabel()}", GetScaledBotDevicePoint(layout, 24, 86), 17, 1f, Color.White);
        _platform.DrawUiText($"Активно: {_companion.GetActiveSummary()}", GetScaledBotDevicePoint(layout, 24, 108), 16, 1f, dim);
        _platform.DrawUiText($"Очередь: {_companion.GetQueuedSummary()}", GetScaledBotDevicePoint(layout, 24, 128), 16, 1f, dim);
        _platform.DrawUiText($"Запасы: {_companion.GetStockpileSummary()}", GetScaledBotDevicePoint(layout, 24, 148), 16, 1f, dim);
        var dividerStart = GetScaledBotDevicePoint(layout, 24, 174);
        var dividerEnd = GetScaledBotDevicePoint(layout, BotDevicePanelWidth - 24, 174);
        _platform.DrawLine((int)dividerStart.X, (int)dividerStart.Y, (int)dividerEnd.X, (int)dividerEnd.Y, dividerColor);

        switch (_botDevice.Screen)
        {
            case BotWristDeviceScreen.Main:
                DrawBotDeviceButton(GetBotDeviceGatherButtonRect(), "Сбор ресурсов", accent);
                DrawBotDeviceButton(GetBotDeviceBuildButtonRect(), "Построить Дом S", new Color(180, 214, 255, 235));
                DrawBotDeviceButton(GetBotDeviceCancelButtonRect(), "Сбросить команды", new Color(255, 176, 168, 235));
                DrawBotDeviceButton(GetBotDeviceCloseButtonRect(), "Убрать устройство", dim);
                _platform.DrawUiText("B / ESC: убрать модуль", GetScaledBotDevicePoint(layout, 24, 430), 16, 1f, accent);
                break;
            case BotWristDeviceScreen.GatherResource:
                _platform.DrawUiText("Сбор ресурсов", GetScaledBotDevicePoint(layout, 24, 190), 24, 1f, accent);
                DrawBotDeviceResourceButton(GetBotDeviceWoodButtonRect(), "Дерево", BotResourceType.Wood);
                DrawBotDeviceResourceButton(GetBotDeviceStoneButtonRect(), "Камень", BotResourceType.Stone);
                DrawBotDeviceResourceButton(GetBotDeviceDirtButtonRect(), "Земля", BotResourceType.Dirt);
                DrawBotDeviceResourceButton(GetBotDeviceLeavesButtonRect(), "Листва", BotResourceType.Leaves);
                var amount = GetBotDeviceAmountRect();
                _platform.DrawUiText($"Количество: {_botDevice.AmountText}", new Vector2(amount.X + 18, amount.Y + 8), 22, 1f, Color.White);
                _platform.DrawUiText("0-9, Backspace, Enter", new Vector2(amount.X + 18, amount.Y + amount.H - 18), 14, 1f, accent);
                DrawBotDeviceButton(GetBotDeviceConfirmButtonRect(), "Подтвердить сбор", accent);
                DrawBotDeviceButton(GetBotDeviceBackButtonRect(), "Назад", dim);
                break;
            case BotWristDeviceScreen.BuildHouse:
                _platform.DrawUiText("Строительство", GetScaledBotDevicePoint(layout, 24, 190), 24, 1f, accent);
                _platform.DrawUiText("Шаблон: Дом S", GetScaledBotDevicePoint(layout, 24, 226), 20, 1f, Color.White);
                _platform.DrawUiText("Бот сам подготовит площадку,", GetScaledBotDevicePoint(layout, 24, 256), 17, 1f, dim);
                _platform.DrawUiText("доберет ресурсы и начнет стройку.", GetScaledBotDevicePoint(layout, 24, 278), 17, 1f, dim);
                DrawBotDeviceButton(GetBotDeviceConfirmButtonRect(), "Запустить стройку", new Color(180, 214, 255, 235));
                DrawBotDeviceButton(GetBotDeviceBackButtonRect(), "Назад", dim);
                break;
        }

        if (!string.IsNullOrWhiteSpace(_botDevice.Message))
        {
            _platform.DrawUiText(_botDevice.Message, GetBotDeviceMessagePoint(layout), 16, 1f, accent);
        }
    }

    private void DrawBotDeviceButton((int X, int Y, int W, int H) rect, string label, Color text)
    {
        _platform.DrawRectangle(rect.X, rect.Y, rect.W, rect.H, new Color(27, 74, 84, 158));
        _platform.DrawRectangle(rect.X, rect.Y, rect.W, 2, new Color(114, 255, 232, 104));
        _platform.DrawRectangle(rect.X, rect.Y + rect.H - 2, rect.W, 2, new Color(114, 255, 232, 80));
        _platform.DrawUiText(label, new Vector2(rect.X + 16, rect.Y + 10), 21, 1f, text);
    }

    private void DrawBotDeviceResourceButton((int X, int Y, int W, int H) rect, string label, BotResourceType resource)
    {
        var isSelected = _botDevice.SelectedResource == resource;
        var text = isSelected
            ? new Color(194, 255, 244, 255)
            : new Color(228, 238, 242, 235);
        DrawBotDeviceButton(rect, label, text);
    }

    private Vector2 GetScaledBotDevicePoint(BotWristDeviceLayout layout, int virtualX, int virtualY) => layout.GetPanelPoint(virtualX, virtualY);

    private Vector2 GetBotDeviceMessagePoint(BotWristDeviceLayout layout)
    {
        return _botDevice.Screen switch
        {
            BotWristDeviceScreen.GatherResource => GetScaledBotDevicePoint(layout, 24, 176),
            BotWristDeviceScreen.BuildHouse => GetScaledBotDevicePoint(layout, 24, 176),
            _ => GetScaledBotDevicePoint(layout, 24, 456)
        };
    }

    private void DrawBotDeviceTapOverlay(BotWristDeviceLayout layout)
    {
        if (_botDeviceVisual.TapBlend <= BotDeviceTapArmThreshold)
        {
            return;
        }

        var targetRect = GetBotDeviceElementRect(_botDeviceVisual.TapTarget);
        if (targetRect.W <= 0 || targetRect.H <= 0)
        {
            targetRect = layout.PanelRect.ToTuple();
        }

        var target = new Vector2(targetRect.X + targetRect.W * 0.5f, targetRect.Y + targetRect.H * 0.5f);
        var pressT = Math.Clamp(_botDeviceVisual.TapBlend, 0f, 1f);
        var palmWidth = Math.Max(18, BotDeviceTapPalmSize - 8);
        var palmHeight = Math.Max(14, (int)MathF.Round(palmWidth * 0.74f));
        var panelCenterX = layout.PanelRect.X + layout.PanelRect.W * 0.5f;
        var approachFromRight = target.X >= panelCenterX;
        var fingerWidth = 6;
        var fingerHeight = 14 + (int)MathF.Round((1f - pressT) * 2f);
        var fingerX = (int)MathF.Round(target.X - fingerWidth * 0.5f);
        var fingerY = (int)MathF.Round(target.Y - 2f);
        var palmX = approachFromRight
            ? fingerX + 1
            : fingerX - palmWidth + fingerWidth - 1;
        var palmY = fingerY + fingerHeight - 7;
        var wristWidth = 8;
        var wristHeight = Math.Max(10, palmHeight - 6);
        var sleeveX = approachFromRight
            ? palmX + palmWidth - 3
            : palmX - wristWidth + 3;
        var sleeveY = palmY + palmHeight - wristHeight + 1;
        var thumbWidth = 7;
        var thumbHeight = 6;
        var thumbX = approachFromRight
            ? palmX + 1
            : palmX + palmWidth - thumbWidth - 1;
        var thumbY = palmY + 7;
        var knuckleWidth = 9;
        var knuckleHeight = 4;
        var knuckleX = approachFromRight
            ? palmX + 6
            : palmX + palmWidth - knuckleWidth - 6;
        var knuckleY = palmY + 2;
        var highlightSize = 8;

        var sleeveShadow = new Color(34, 50, 58, 118);
        var sleeve = new Color(198, 170, 144, 164);
        var skin = new Color(232, 205, 178, 216);
        var skinDark = new Color(214, 186, 160, 198);

        _platform.DrawRectangle(sleeveX + 1, sleeveY + 1, wristWidth, wristHeight, sleeveShadow);
        _platform.DrawRectangle(sleeveX, sleeveY, wristWidth, wristHeight, sleeve);
        _platform.DrawRectangle(palmX + 2, palmY + 2, palmWidth, palmHeight, new Color(0, 0, 0, 34));
        _platform.DrawRectangle(palmX, palmY, palmWidth, palmHeight, skin);
        _platform.DrawRectangle(fingerX, fingerY, fingerWidth, fingerHeight, skin);
        _platform.DrawRectangle(thumbX, thumbY, thumbWidth, thumbHeight, skinDark);
        _platform.DrawRectangle(knuckleX, knuckleY, knuckleWidth, knuckleHeight, skinDark);
        _platform.DrawRectangle(knuckleX + (approachFromRight ? 1 : -1), knuckleY + 5, knuckleWidth - 2, knuckleHeight, skinDark);

        _platform.DrawRectangle(
            (int)MathF.Round(target.X - highlightSize * 0.5f),
            (int)MathF.Round(target.Y - highlightSize * 0.5f),
            highlightSize,
            highlightSize,
            new Color(176, 255, 244, 110));
    }

    private (int X, int Y, int W, int H) GetBotDevicePanelRect()
    {
        return TryBuildBotDeviceLayout(out var layout)
            ? layout.PanelRect.ToTuple()
            : (0, 0, 0, 0);
    }

    private (int X, int Y, int W, int H) GetBotDeviceGatherButtonRect()
    {
        return GetBotDeviceElementRect(BotWristDeviceTarget.Gather);
    }

    private (int X, int Y, int W, int H) GetBotDeviceBuildButtonRect()
    {
        return GetBotDeviceElementRect(BotWristDeviceTarget.BuildHouse);
    }

    private (int X, int Y, int W, int H) GetBotDeviceCancelButtonRect()
    {
        return GetBotDeviceElementRect(BotWristDeviceTarget.Cancel);
    }

    private (int X, int Y, int W, int H) GetBotDeviceCloseButtonRect()
    {
        return GetBotDeviceElementRect(BotWristDeviceTarget.Close);
    }

    private (int X, int Y, int W, int H) GetBotDeviceWoodButtonRect()
    {
        return GetBotDeviceElementRect(BotWristDeviceTarget.Wood);
    }

    private (int X, int Y, int W, int H) GetBotDeviceStoneButtonRect()
    {
        return GetBotDeviceElementRect(BotWristDeviceTarget.Stone);
    }

    private (int X, int Y, int W, int H) GetBotDeviceDirtButtonRect()
    {
        return GetBotDeviceElementRect(BotWristDeviceTarget.Dirt);
    }

    private (int X, int Y, int W, int H) GetBotDeviceLeavesButtonRect()
    {
        return GetBotDeviceElementRect(BotWristDeviceTarget.Leaves);
    }

    private (int X, int Y, int W, int H) GetBotDeviceAmountRect()
    {
        return GetBotDeviceElementRect(BotWristDeviceTarget.Amount);
    }

    private (int X, int Y, int W, int H) GetBotDeviceConfirmButtonRect()
    {
        return GetBotDeviceElementRect(BotWristDeviceTarget.Confirm);
    }

    private (int X, int Y, int W, int H) GetBotDeviceBackButtonRect()
    {
        return GetBotDeviceElementRect(BotWristDeviceTarget.Back);
    }

    private (int X, int Y, int W, int H) GetBotDeviceElementRect(BotWristDeviceTarget target)
    {
        return TryBuildBotDeviceLayout(out var layout)
            ? layout.GetRect(target).ToTuple()
            : (0, 0, 0, 0);
    }

    private bool BlockCenterIntersectsPlayer(Vector3 blockCenter)
    {
        return BlockCenterIntersectsActor(blockCenter, _player.Position, _player.ColliderHalfWidth, _player.ColliderHeight);
    }

    private bool BlockCenterIntersectsCompanion(Vector3 blockCenter)
    {
        return _companion is not null
            && BlockCenterIntersectsActor(blockCenter, _companion.Position, _companion.Actor.ColliderHalfWidth, _companion.Actor.ColliderHeight);
    }

    private bool BlockCenterIntersectsBlockingActor(Vector3 blockCenter)
    {
        return BlockCenterIntersectsPlayer(blockCenter) || BlockCenterIntersectsCompanion(blockCenter);
    }

    private bool PlayerPoseIntersectsCompanion(Vector3 pose)
    {
        return _companion is not null
            && DoActorVolumesOverlap(
                pose,
                _player.ColliderHalfWidth,
                _player.ColliderHeight,
                _companion.Position,
                _companion.Actor.ColliderHalfWidth,
                _companion.Actor.ColliderHeight);
    }

    private bool CompanionPoseIntersectsPlayer(Vector3 pose)
    {
        return _companion is not null
            && DoActorVolumesOverlap(
                pose,
                _companion.Actor.ColliderHalfWidth,
                _companion.Actor.ColliderHeight,
                _player.Position,
                _player.ColliderHalfWidth,
                _player.ColliderHeight);
    }

    private static bool BlockCenterIntersectsActor(Vector3 blockCenter, Vector3 actorPosition, float half, float height)
    {
        var actorMin = new Vector3(actorPosition.X - half, actorPosition.Y, actorPosition.Z - half);
        var actorMax = new Vector3(actorPosition.X + half, actorPosition.Y + height, actorPosition.Z + half);

        var blockMin = blockCenter - new Vector3(0.5f, 0.5f, 0.5f);
        var blockMax = blockCenter + new Vector3(0.5f, 0.5f, 0.5f);

        return (actorMin.X <= blockMax.X) & (actorMax.X >= blockMin.X)
            & (actorMin.Y <= blockMax.Y) & (actorMax.Y >= blockMin.Y)
            & (actorMin.Z <= blockMax.Z) & (actorMax.Z >= blockMin.Z);
    }

    private static bool DoActorVolumesOverlap(
        Vector3 firstPosition,
        float firstHalfWidth,
        float firstHeight,
        Vector3 secondPosition,
        float secondHalfWidth,
        float secondHeight)
    {
        var firstMin = new Vector3(firstPosition.X - firstHalfWidth, firstPosition.Y, firstPosition.Z - firstHalfWidth);
        var firstMax = new Vector3(firstPosition.X + firstHalfWidth, firstPosition.Y + firstHeight, firstPosition.Z + firstHalfWidth);
        var secondMin = new Vector3(secondPosition.X - secondHalfWidth, secondPosition.Y, secondPosition.Z - secondHalfWidth);
        var secondMax = new Vector3(secondPosition.X + secondHalfWidth, secondPosition.Y + secondHeight, secondPosition.Z + secondHalfWidth);

        return (firstMin.X <= secondMax.X) & (firstMax.X >= secondMin.X)
            & (firstMin.Y <= secondMax.Y) & (firstMax.Y >= secondMin.Y)
            & (firstMin.Z <= secondMax.Z) & (firstMax.Z >= secondMin.Z);
    }

    internal static string GetBlockName(BlockType block)
    {
        return block switch
        {
            BlockType.Grass => "Трава",
            BlockType.Dirt => "Земля",
            BlockType.Stone => "Камень",
            BlockType.Wood => "Дерево",
            BlockType.Leaves => "Листва",
            _ => "Блок"
        };
    }

    private enum MenuAction
    {
        None,
        Start,
        ToggleFullscreen,
        CycleGraphicsQuality,
        ToggleFog,
        ToggleReliefContours,
        Exit
    }

    internal static string GetQualityName(GraphicsQuality quality)
    {
        return quality switch
        {
            GraphicsQuality.Low => "Низкая",
            GraphicsQuality.Medium => "Средняя",
            _ => "Высокая"
        };
    }

    internal static string GetCameraModeName(CameraMode mode)
    {
        return mode == CameraMode.FirstPerson ? "1-е лицо" : "3-е лицо";
    }

    internal static string ResolveUiFontPath(IEnumerable<string>? candidates = null)
    {
        candidates ??= new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "NotoSans-Regular.ttf"),
            Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "DejaVuSans.ttf"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "AIG.Game", "assets", "fonts", "NotoSans-Regular.ttf"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "fonts", "NotoSans-Regular.ttf"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "fonts", "DejaVuSans.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
            "/usr/share/fonts/noto/NotoSans-Regular.ttf"
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private void InitializePlatform(bool enableFullscreen)
    {
        _platform.SetConfigFlags(ConfigFlags.ResizableWindow);
        _platform.InitWindow(_config.WindowWidth, _config.WindowHeight, _config.Title);
        _platform.SetExitKey(KeyboardKey.Null);
        _platform.SetTargetFps(_config.TargetFps);

        if (enableFullscreen && _config.FullscreenByDefault && !_platform.IsWindowFullscreen())
        {
            _platform.ToggleFullscreen();
        }

        _platform.LoadUiFont(ResolveUiFontPath(), 42);
        _platform.EnableCursor();
    }

    private void ShutdownPlatform()
    {
        _platform.UnloadUiFont();
        _platform.EnableCursor();
        _platform.CloseWindow();
    }

    private static AutoCaptureShot[] GetAutoCaptureShots()
    {
        return
        [
            new AutoCaptureShot(
                "autocap-1.png",
                new Vector3(1152.50f, 40.00f, 1152.50f),
                new Vector3(1156.40f, 34.80f, 1148.60f)),
            new AutoCaptureShot(
                "autocap-2.png",
                new Vector3(1152.50f, 40.00f, 1152.50f),
                new Vector3(1148.40f, 34.90f, 1148.90f))
        ];
    }

    private BlockRaycastHit? PrepareAutoCaptureShot(AutoCaptureShot shot)
    {
        var pose = LiftPoseAboveTerrain(shot.Position);
        if (_world.Seed != 0)
        {
            ClearSpawnCanopy(pose, horizontalRadius: 12, verticalSpan: 14);
        }

        _player = new PlayerController(_config, pose);
        _playerVisual.Reset(pose);
        var captureLook = BuildGroundLookDirection(pose, shot.LookTarget - pose);
        _player.SetPose(pose, captureLook);
        ResetAdaptiveTracking(_player.Position);
        UpdateWorldStreaming(force: true);
        return VoxelRaycaster.Raycast(_world, _player.EyePosition, _player.LookDirection, _config.InteractionDistance);
    }

    private static GameConfig CreateDefaultConfig()
    {
        return new GameConfig
        {
            BotDiagnosticsEnabled = true
        };
    }

    private static WorldMap CreateDefaultWorld(GameConfig config)
    {
        return new WorldMap(width: 2304, height: 72, depth: 2304, chunkSize: config.ChunkSize, seed: config.WorldSeed);
    }

    private Vector3 CreateSpawnPosition()
    {
        var centerX = Math.Clamp(_world.Width / 2, 0, _world.Width - 1);
        var centerZ = Math.Clamp(_world.Depth / 2, 0, _world.Depth - 1);
        return FindClearSpawnPose(centerX, centerZ, searchRadius: 48);
    }

    private Vector3 CreateCompanionSpawnPosition(Vector3 playerPosition)
    {
        var centerX = Math.Clamp((int)MathF.Floor(playerPosition.X + 3f), 0, Math.Max(0, _world.Width - 1));
        var centerZ = Math.Clamp((int)MathF.Floor(playerPosition.Z + 3f), 0, Math.Max(0, _world.Depth - 1));
        return FindClearSpawnPose(centerX, centerZ, searchRadius: 12);
    }

    private Vector3 LiftPoseAboveTerrain(Vector3 position)
    {
        var baseX = Math.Clamp((int)MathF.Floor(position.X), 0, _world.Width - 1);
        var baseZ = Math.Clamp((int)MathF.Floor(position.Z), 0, _world.Depth - 1);
        if (!TryFindTreeFreeColumn(baseX, baseZ, radius: 24, out var x, out var z))
        {
            x = baseX;
            z = baseZ;
        }

        var topY = Math.Max(_world.GetTerrainTopY(x, z), _world.GetTopSolidY(x, z));
        var minY = topY + 1.2f;
        var y = minY;
        return new Vector3(x + 0.5f, y, z + 0.5f);
    }

    private Vector3 FindClearSpawnPose(int centerX, int centerZ, int searchRadius)
    {
        var minY = 1.2f;
        var maxY = Math.Max(minY, _world.Height - 1.2f);

        for (var radius = 0; radius <= Math.Max(0, searchRadius); radius++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                {
                    if (radius > 0 && Math.Abs(dx) != radius && Math.Abs(dz) != radius)
                    {
                        continue;
                    }

                    var x = centerX + dx;
                    var z = centerZ + dz;
                    if (x < 0 || z < 0 || x >= _world.Width || z >= _world.Depth)
                    {
                        continue;
                    }

                    var terrainTop = _world.GetTerrainTopY(x, z);
                    var topSolid = _world.GetTopSolidY(x, z);
                    if (topSolid > terrainTop + 1)
                    {
                        continue;
                    }

                    var groundTop = Math.Max(terrainTop, topSolid);
                    var preferredY = groundTop + 1.2f;
                    var minClearY = groundTop + 1.2f;
                    var feetY = Math.Clamp(MathF.Max(preferredY, minClearY), minY, maxY);
                    var pose = new Vector3(x + 0.5f, feetY, z + 0.5f);
                    if (IsPlayerPoseClear(pose))
                    {
                        return pose;
                    }
                }
            }
        }

        var fallbackX = Math.Clamp(centerX, 0, _world.Width - 1);
        var fallbackZ = Math.Clamp(centerZ, 0, _world.Depth - 1);
        var fallbackTerrainTop = _world.GetTerrainTopY(fallbackX, fallbackZ);
        var fallbackSolidTop = _world.GetTopSolidY(fallbackX, fallbackZ);
        var fallbackGroundTop = Math.Max(fallbackTerrainTop, fallbackSolidTop);
        var fallbackY = Math.Clamp(fallbackGroundTop + 1.2f, minY, maxY);
        return new Vector3(fallbackX + 0.5f, fallbackY, fallbackZ + 0.5f);
    }

    private bool TryFindTreeFreeColumn(int centerX, int centerZ, int radius, out int foundX, out int foundZ)
    {
        for (var ring = 0; ring <= Math.Max(0, radius); ring++)
        {
            for (var dx = -ring; dx <= ring; dx++)
            {
                for (var dz = -ring; dz <= ring; dz++)
                {
                    if (ring > 0 && Math.Abs(dx) != ring && Math.Abs(dz) != ring)
                    {
                        continue;
                    }

                    var x = centerX + dx;
                    var z = centerZ + dz;
                    if (x < 0 || z < 0 || x >= _world.Width || z >= _world.Depth)
                    {
                        continue;
                    }

                    var terrainTop = _world.GetTerrainTopY(x, z);
                    var topSolid = _world.GetTopSolidY(x, z);
                    if (topSolid > terrainTop + 1)
                    {
                        continue;
                    }

                    foundX = x;
                    foundZ = z;
                    return true;
                }
            }
        }

        foundX = centerX;
        foundZ = centerZ;
        return false;
    }

    private void ClearSpawnCanopy(Vector3 pose, int horizontalRadius, int verticalSpan)
    {
        var centerX = Math.Clamp((int)MathF.Floor(pose.X), 0, _world.Width - 1);
        var centerZ = Math.Clamp((int)MathF.Floor(pose.Z), 0, _world.Depth - 1);
        var baseY = Math.Clamp((int)MathF.Floor(pose.Y), 0, _world.Height - 1);

        var minX = Math.Max(0, centerX - Math.Max(0, horizontalRadius));
        var maxX = Math.Min(_world.Width - 1, centerX + Math.Max(0, horizontalRadius));
        var minZ = Math.Max(0, centerZ - Math.Max(0, horizontalRadius));
        var maxZ = Math.Min(_world.Depth - 1, centerZ + Math.Max(0, horizontalRadius));
        var minY = Math.Max(0, baseY - 1);
        var maxY = Math.Min(_world.Height - 1, baseY + Math.Max(0, verticalSpan));

        for (var x = minX; x <= maxX; x++)
        {
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var y = minY; y <= maxY; y++)
                {
                    var block = _world.GetBlock(x, y, z);
                    if (block is BlockType.Leaves or BlockType.Wood)
                    {
                        _world.SetBlock(x, y, z, BlockType.Air);
                    }
                }
            }
        }
    }

    private bool IsPlayerPoseClear(Vector3 pose)
    {
        const float halfWidth = 0.3f;
        const float height = 1.8f;

        var minX = (int)MathF.Floor(pose.X - halfWidth);
        var maxX = (int)MathF.Floor(pose.X + halfWidth);
        var minY = (int)MathF.Floor(pose.Y);
        var maxY = (int)MathF.Floor(pose.Y + height);
        var minZ = (int)MathF.Floor(pose.Z - halfWidth);
        var maxZ = (int)MathF.Floor(pose.Z + halfWidth);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    if (_world.IsSolid(x, y, z))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private Vector3 BuildGroundLookDirection(Vector3 pose, Vector3 preferredForward)
    {
        const float eyeOffsetY = 1.656f;
        var forward = ToHorizontalForward(preferredForward);
        var eye = pose + new Vector3(0f, eyeOffsetY, 0f);

        for (var distance = 5; distance <= 18; distance += 2)
        {
            var sampleX = Math.Clamp((int)MathF.Floor(pose.X + forward.X * distance), 0, _world.Width - 1);
            var sampleZ = Math.Clamp((int)MathF.Floor(pose.Z + forward.Z * distance), 0, _world.Depth - 1);
            var terrainY = _world.GetTerrainTopY(sampleX, sampleZ);
            var target = new Vector3(sampleX + 0.5f, terrainY + 0.6f, sampleZ + 0.5f);
            var toTarget = target - eye;
            if (toTarget.LengthSquared() < 0.0001f || toTarget.Y > -0.2f)
            {
                continue;
            }

            return Vector3.Normalize(toTarget);
        }

        return Vector3.Normalize(new Vector3(forward.X, -0.45f, forward.Z));
    }

    private void AdvanceRuntime(float deltaSeconds)
    {
        var delta = deltaSeconds > 0f ? deltaSeconds : 1f / 60f;
        _runtimeSeconds += Math.Clamp(delta, 1f / 240f, 0.1f);
    }

    private void ResetAdaptiveTracking(Vector3 position)
    {
        _lastAdaptiveProbePosition = new Vector2(position.X, position.Z);
        _hasAdaptiveProbe = true;
        _adaptiveMovementFreezeTimer = 0f;
    }

    private void UpdateWorldStreaming(bool force)
    {
        var center = _player.Position;
        var chunkX = Math.Clamp((int)MathF.Floor(center.X), 0, _world.Width - 1) / _world.ChunkSize;
        var chunkZ = Math.Clamp((int)MathF.Floor(center.Z), 0, _world.Depth - 1) / _world.ChunkSize;

        var measuredFps = _platform.GetFps();
        var targetRenderDistance = ResolveStreamingRenderDistance(measuredFps, center, force);
        var visibleChunkRadius = (targetRenderDistance + _world.ChunkSize - 1) / _world.ChunkSize;
        var chunkRadius = Math.Max(2, visibleChunkRadius + GetStreamingWarmupChunks());
        var underPressure = !force && measuredFps > 0 && measuredFps < 63;
        var chunkLoadBudget = underPressure
            ? 1
            : (force ? GetChunkLoadBurstBudget(chunkRadius) : GetChunkLoadBudget());
        var useBackgroundStreaming = !force && _state == AppState.Playing;
        if (useBackgroundStreaming)
        {
            _ = _world.ApplyBackgroundStreamingResults(chunkLoadBudget + 1, GetSurfaceRebuildBudget() + 1);
            _world.EnsureChunksAroundBudgetedAsync(center, chunkRadius, chunkLoadBudget);
        }
        else
        {
            _world.EnsureChunksAroundBudgeted(center, chunkRadius, chunkLoadBudget);
        }

        var prefetchBudget = force
            ? Math.Max(1, GetForwardPrefetchBudget(underPressure: false))
            : GetForwardPrefetchBudget(underPressure);
        EnsureForwardChunksBudgeted(center, chunkRadius, prefetchBudget, useBackgroundStreaming);
        var holdExtraRadius = _adaptiveMovementFreezeTimer > 0f ? 1 : 0;
        var unloadRadius = chunkRadius + GetUnloadHysteresisChunks() + holdExtraRadius;
        _world.UnloadFarChunks(center, unloadRadius);
        CleanupChunkRevealCache(chunkX, chunkZ, unloadRadius + 2);
        StreamCompanionWorkArea(useBackgroundStreaming, underPressure);

        if (!force
            && chunkX == _lastStreamChunkX
            && chunkZ == _lastStreamChunkZ
            && chunkRadius == _lastStreamRadius)
        {
            var frameBudget = underPressure ? 1 : GetSurfaceRebuildBudget();
            if (useBackgroundStreaming)
            {
                _world.QueueDirtyChunkSurfacesAsync(chunkX, chunkZ, frameBudget);
                _ = _world.ApplyBackgroundStreamingResults(1, frameBudget);
            }
            else
            {
                _world.RebuildDirtyChunkSurfaces(chunkX, chunkZ, frameBudget);
            }

            return;
        }

        var rebuildBudget = underPressure
            ? 1
            : (force ? GetSurfaceBurstBudget(chunkRadius) : GetSurfaceRebuildBudget());
        if (useBackgroundStreaming)
        {
            _world.QueueDirtyChunkSurfacesAsync(chunkX, chunkZ, rebuildBudget);
            _ = _world.ApplyBackgroundStreamingResults(chunkLoadBudget, rebuildBudget);
        }
        else
        {
            _world.RebuildDirtyChunkSurfaces(chunkX, chunkZ, rebuildBudget);
        }

        _lastStreamChunkX = chunkX;
        _lastStreamChunkZ = chunkZ;
        _lastStreamRadius = chunkRadius;
    }

    private void StreamCompanionWorkArea(bool useBackgroundStreaming, bool underPressure)
    {
        if (_companion is null || _companion.ActiveCommand is null)
        {
            return;
        }

        var chunkBudget = underPressure ? 1 : 2;
        var surfaceBudget = underPressure ? 1 : 2;
        var chunkRadius = 2;
        var center = _companion.Position;
        if (useBackgroundStreaming)
        {
            _world.EnsureChunksAroundBudgetedAsync(center, chunkRadius, chunkBudget);
            _world.QueueDirtyChunkSurfacesAsync(center, surfaceBudget);
            _ = _world.ApplyBackgroundStreamingResults(chunkBudget, surfaceBudget);
            return;
        }

        _world.EnsureChunksAroundBudgeted(center, chunkRadius, chunkBudget);
        _world.RebuildDirtyChunkSurfaces(center, surfaceBudget);
    }

    private int ResolveStreamingRenderDistance(int measuredFps, Vector3 center, bool force)
    {
        if (force)
        {
            _adaptiveMovementFreezeTimer = 0f;
            return GetAdaptiveRenderDistance(measuredFps, advanceSmoothing: true);
        }

        var delta = Math.Clamp(_platform.GetFrameTime(), 1f / 240f, 0.1f);
        var speed = MeasureHorizontalSpeed(center, delta);
        if (speed > GetAdaptiveFreezeSpeedThreshold())
        {
            _adaptiveMovementFreezeTimer = 0.85f;
        }
        else if (_adaptiveMovementFreezeTimer > 0f)
        {
            _adaptiveMovementFreezeTimer = MathF.Max(0f, _adaptiveMovementFreezeTimer - delta);
        }

        var allowIncrease = _adaptiveMovementFreezeTimer <= 0f;
        return GetAdaptiveRenderDistance(measuredFps, advanceSmoothing: true, allowIncrease: allowIncrease);
    }

    private float MeasureHorizontalSpeed(Vector3 center, float deltaTime)
    {
        var current = new Vector2(center.X, center.Z);
        if (!_hasAdaptiveProbe)
        {
            _lastAdaptiveProbePosition = current;
            _hasAdaptiveProbe = true;
            return 0f;
        }

        var moved = Vector2.Distance(current, _lastAdaptiveProbePosition);
        _lastAdaptiveProbePosition = current;
        return moved / Math.Max(deltaTime, 1f / 240f);
    }

    private float GetAdaptiveFreezeSpeedThreshold()
    {
        return _graphics.Quality switch
        {
            GraphicsQuality.Low => 0.7f,
            GraphicsQuality.Medium => 0.65f,
            _ => 0.6f
        };
    }

    private int GetAdaptiveRenderDistance(int measuredFps, bool advanceSmoothing, bool allowIncrease = true)
    {
        var target = _graphics.ResolveRenderDistance(measuredFps);
        if (_adaptiveRenderDistance < 0f)
        {
            _adaptiveRenderDistance = target;
        }

        if (advanceSmoothing)
        {
            if (target < _adaptiveRenderDistance)
            {
                // Снижаем быстро, чтобы вовремя сбрасывать нагрузку.
                var drop = Math.Clamp((_adaptiveRenderDistance - target) * 0.45f, 2.5f, 4.5f);
                _adaptiveRenderDistance = MathF.Max(target, _adaptiveRenderDistance - drop);
            }
            else if (target > _adaptiveRenderDistance && allowIncrease)
            {
                // Повышаем плавно и ограниченно, чтобы дальняя полоса не появлялась рывком.
                var rise = Math.Clamp((target - _adaptiveRenderDistance) * 0.25f, 0.6f, 1.2f);
                _adaptiveRenderDistance = MathF.Min(target, _adaptiveRenderDistance + rise);
            }
        }

        var minDistance = _graphics.Quality switch
        {
            GraphicsQuality.Low => 12,
            GraphicsQuality.Medium => 13,
            _ => 24
        };
        return Math.Clamp((int)MathF.Round(_adaptiveRenderDistance), minDistance, _graphics.RenderDistance);
    }

    private int GetStreamingWarmupChunks()
    {
        return _graphics.Quality switch
        {
            GraphicsQuality.Low => 1,
            GraphicsQuality.Medium => 2,
            _ => 2
        };
    }

    private int GetUnloadHysteresisChunks()
    {
        return _graphics.Quality switch
        {
            GraphicsQuality.Low => 2,
            GraphicsQuality.Medium => 2,
            _ => 2
        };
    }

    private int GetForwardPrefetchBudget(bool underPressure)
    {
        if (underPressure)
        {
            return _graphics.Quality switch
            {
                GraphicsQuality.Low => 0,
                GraphicsQuality.Medium => 1,
                _ => 1
            };
        }

        return _graphics.Quality switch
        {
            GraphicsQuality.Low => 1,
            GraphicsQuality.Medium => 2,
            _ => 3
        };
    }

    private int GetForwardPrefetchRadius(int chunkRadius)
    {
        var baseRadius = _graphics.Quality switch
        {
            GraphicsQuality.Low => 1,
            GraphicsQuality.Medium => 2,
            _ => 3
        };

        return Math.Clamp(baseRadius + chunkRadius / 4, 1, Math.Max(1, chunkRadius));
    }

    private float GetForwardPrefetchDistanceBlocks()
    {
        return _graphics.Quality switch
        {
            GraphicsQuality.Low => _world.ChunkSize * 1.25f,
            GraphicsQuality.Medium => _world.ChunkSize * 1.6f,
            _ => _world.ChunkSize * 2.0f
        };
    }

    private void EnsureForwardChunksBudgeted(Vector3 center, int chunkRadius, int maxNewChunks, bool useBackgroundStreaming)
    {
        if (maxNewChunks <= 0)
        {
            return;
        }

        var forward = ToHorizontalForward(_player.LookDirection);
        var nearBudget = Math.Max(1, maxNewChunks - maxNewChunks / 3);
        var nearDistance = GetForwardPrefetchDistanceBlocks();
        var nearAhead = new Vector3(
            center.X + forward.X * nearDistance,
            center.Y,
            center.Z + forward.Z * nearDistance);
        var nearRadius = GetForwardPrefetchRadius(chunkRadius);
        if (useBackgroundStreaming)
        {
            _world.EnsureChunksAroundBudgetedAsync(nearAhead, nearRadius, nearBudget);
        }
        else
        {
            _world.EnsureChunksAroundBudgeted(nearAhead, nearRadius, nearBudget);
        }

        var farBudget = maxNewChunks - nearBudget;
        if (farBudget <= 0)
        {
            return;
        }

        var farAhead = new Vector3(
            center.X + forward.X * nearDistance * 2f,
            center.Y,
            center.Z + forward.Z * nearDistance * 2f);
        var farRadius = Math.Max(1, nearRadius - 1);
        if (useBackgroundStreaming)
        {
            _world.EnsureChunksAroundBudgetedAsync(farAhead, farRadius, farBudget);
        }
        else
        {
            _world.EnsureChunksAroundBudgeted(farAhead, farRadius, farBudget);
        }
    }

    private void CleanupChunkRevealCache(int centerChunkX, int centerChunkZ, int keepRadius)
    {
        if (_chunkRevealStartedAt.Count == 0)
        {
            return;
        }

        List<(int ChunkX, int ChunkZ)>? stale = null;
        foreach (var key in _chunkRevealStartedAt.Keys)
        {
            if (Math.Abs(key.ChunkX - centerChunkX) <= keepRadius
                && Math.Abs(key.ChunkZ - centerChunkZ) <= keepRadius)
            {
                continue;
            }

            stale ??= [];
            stale.Add(key);
        }

        if (stale is null)
        {
            return;
        }

        for (var i = 0; i < stale.Count; i++)
        {
            _chunkRevealStartedAt.Remove(stale[i]);
        }
    }

    private int GetSurfaceRebuildBudget()
    {
        return _graphics.Quality switch
        {
            GraphicsQuality.Low => 1,
            GraphicsQuality.Medium => 2,
            _ => 3
        };
    }

    private int GetSurfaceBurstBudget(int chunkRadius)
    {
        var baseBudget = _graphics.Quality switch
        {
            GraphicsQuality.Low => 4,
            GraphicsQuality.Medium => 6,
            _ => 6
        };

        // Мягкая прогревка кэша: снижаем пиковую нагрузку в кадре при стриминге.
        return Math.Clamp(baseBudget + chunkRadius / 2, 3, 12);
    }

    private int GetChunkLoadBudget()
    {
        return _graphics.Quality switch
        {
            GraphicsQuality.Low => 1,
            GraphicsQuality.Medium => 2,
            _ => 3
        };
    }

    private int GetChunkLoadBurstBudget(int chunkRadius)
    {
        var baseBudget = _graphics.Quality switch
        {
            GraphicsQuality.Low => 4,
            GraphicsQuality.Medium => 5,
            _ => 6
        };

        return Math.Clamp(baseBudget + chunkRadius / 2, 3, 12);
    }

    private int GetSurfaceDrawFallbackBudget()
    {
        return _graphics.Quality switch
        {
            GraphicsQuality.Low => 2,
            GraphicsQuality.Medium => 3,
            _ => 4
        };
    }
}
