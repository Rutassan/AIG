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
    private const int BotDevicePanelWidth = 430;
    private const int BotDeviceButtonHeight = 48;
    private const int BotDeviceButtonsGap = 12;
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
    private readonly Dictionary<(int ChunkX, int ChunkZ), float> _chunkRevealStartedAt = new();
    private readonly Dictionary<InstancedBatchKey, List<Matrix4x4>> _worldInstanceBatches = new();

    public GameApp()
        : this(CreateDefaultConfig(), new RaylibGamePlatform(), CreateDefaultWorld(CreateDefaultConfig()))
    {
    }

    internal GameApp(GameConfig config, IGamePlatform platform, WorldMap world)
    {
        _config = config;
        _platform = platform;
        _world = world;
        _graphics = new GraphicsSettings(config);
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
                        HandleBotDeviceInput();
                    }
                    else
                    {
                        HandleHotbarInput();
                    }

                    var input = _botDevice.IsOpen
                        ? default
                        : ReadInput(_platform);
                    _player.Update(_world, input, delta);
                    _playerVisual.Update(_player.Position, delta, _config.MoveSpeed);
                    UpdateWorldStreaming(force: false);
                    if (_companion is not null)
                    {
                        _companion.Update(_world, _player.Position, _player.LookDirection, delta);
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
                        BlockInteraction.TryPlace(_world, currentHit, _hotbar[_selectedHotbarIndex], BlockCenterIntersectsPlayer);
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
            }
        }
        finally
        {
            diagnostics?.Write("app", "run-stop");
            diagnostics?.Dispose();
            if (platformInitialized)
            {
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

        _botDevice.OpenMain();
        _platform.EnableCursor();
    }

    private void CloseBotDevice()
    {
        if (!_botDevice.IsOpen)
        {
            return;
        }

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

        HandleBotDeviceDigitInput();

        if (_botDevice.Screen == BotWristDeviceScreen.GatherResource && _platform.IsKeyPressed(KeyboardKey.Enter))
        {
            TriggerBotDeviceTap();
            QueueGatherFromDevice();
            return;
        }

        if (_botDevice.Screen == BotWristDeviceScreen.BuildHouse && _platform.IsKeyPressed(KeyboardKey.Enter))
        {
            TriggerBotDeviceTap();
            QueueBuildFromDevice();
            return;
        }

        switch (ReadBotDeviceAction())
        {
            case BotDeviceAction.OpenGatherResource:
                _botDevice.OpenGatherResource();
                _botDevice.SetMessage("Выберите ресурс и введите количество.");
                TriggerBotDeviceTap();
                break;
            case BotDeviceAction.OpenBuildHouse:
                _botDevice.OpenBuildHouse();
                _botDevice.SetMessage("Подтвердите строительство Дом S.");
                TriggerBotDeviceTap();
                break;
            case BotDeviceAction.BackToMain:
                _botDevice.BackToMain();
                _botDevice.SetMessage(string.Empty);
                TriggerBotDeviceTap();
                break;
            case BotDeviceAction.QueueGather:
                TriggerBotDeviceTap();
                QueueGatherFromDevice();
                break;
            case BotDeviceAction.QueueBuildHouse:
                TriggerBotDeviceTap();
                QueueBuildFromDevice();
                break;
            case BotDeviceAction.CancelCommands:
                TriggerBotDeviceTap();
                _companion.CancelAll();
                _botDevice.SetMessage("Команды бота сброшены.");
                break;
            case BotDeviceAction.CloseDevice:
                TriggerBotDeviceTap();
                CloseBotDevice();
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
                TriggerBotDeviceTap();
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
                TriggerBotDeviceTap();
            }
        }
    }

    private void SelectDeviceResource(BotResourceType resource)
    {
        _botDevice.SelectResource(resource);
        _botDevice.SetMessage($"Ресурс: {resource.GetLabel()}");
        TriggerBotDeviceTap();
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

    private void TriggerBotDeviceTap()
    {
        _botDeviceVisual.TriggerTap();
    }

    private BotDeviceAction ReadBotDeviceAction()
    {
        if (!_botDevice.IsOpen)
        {
            return BotDeviceAction.None;
        }

        if (_botDevice.Screen == BotWristDeviceScreen.Main)
        {
            if (IsButtonClicked(GetBotDeviceGatherButtonRect()))
            {
                return BotDeviceAction.OpenGatherResource;
            }

            if (IsButtonClicked(GetBotDeviceBuildButtonRect()))
            {
                return BotDeviceAction.OpenBuildHouse;
            }

            if (IsButtonClicked(GetBotDeviceCancelButtonRect()))
            {
                return BotDeviceAction.CancelCommands;
            }

            if (IsButtonClicked(GetBotDeviceCloseButtonRect()))
            {
                return BotDeviceAction.CloseDevice;
            }

            return BotDeviceAction.None;
        }

        if (_botDevice.Screen == BotWristDeviceScreen.GatherResource)
        {
            if (IsButtonClicked(GetBotDeviceBackButtonRect()))
            {
                return BotDeviceAction.BackToMain;
            }

            if (IsButtonClicked(GetBotDeviceWoodButtonRect()))
            {
                return BotDeviceAction.SelectWood;
            }

            if (IsButtonClicked(GetBotDeviceStoneButtonRect()))
            {
                return BotDeviceAction.SelectStone;
            }

            if (IsButtonClicked(GetBotDeviceDirtButtonRect()))
            {
                return BotDeviceAction.SelectDirt;
            }

            if (IsButtonClicked(GetBotDeviceLeavesButtonRect()))
            {
                return BotDeviceAction.SelectLeaves;
            }

            if (IsButtonClicked(GetBotDeviceConfirmButtonRect()))
            {
                return BotDeviceAction.QueueGather;
            }

            return BotDeviceAction.None;
        }

        if (IsButtonClicked(GetBotDeviceBackButtonRect()))
        {
            return BotDeviceAction.BackToMain;
        }

        if (IsButtonClicked(GetBotDeviceConfirmButtonRect()))
        {
            return BotDeviceAction.QueueBuildHouse;
        }

        return BotDeviceAction.None;
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

            DrawHud(_state == AppState.Playing && !_botDevice.IsOpen);
            DrawHotbar();
            if (_state == AppState.Playing && _botDevice.IsOpen)
            {
                DrawBotDeviceOverlay();
            }
        }

        if (_state != AppState.Playing)
        {
            DrawMenu();
        }

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
        var horizon = GetSkyHorizonColor();
        const int bands = 20;
        for (var i = 0; i < bands; i++)
        {
            var y0 = i * height / bands;
            var y1 = (i + 1) * height / bands;
            var bandHeight = Math.Max(1, y1 - y0);
            var t = i / (float)(bands - 1);
            t = MathF.Pow(t, 1.35f);
            var color = LerpColor(top, horizon, t);
            _platform.DrawRectangle(0, y0, width, bandHeight, color);
        }

        var viewY = Math.Clamp(view.RayDirection.Y, -1f, 1f);
        var horizonY = (int)MathF.Round(height * (0.56f + viewY * 0.2f));
        horizonY = Math.Clamp(horizonY, 0, height - 1);
        var fog = _graphics.FogColor;
        DrawHorizonBand(width, height, horizonY - 34, 26, new Color(fog.R, fog.G, fog.B, (byte)42));
        DrawHorizonBand(width, height, horizonY - 8, 16, new Color(fog.R, fog.G, fog.B, (byte)62));
        DrawHorizonBand(width, height, horizonY + 10, 18, new Color(fog.R, fog.G, fog.B, (byte)35));
    }

    private void DrawScreenFogOverlay(CameraViewBuilder.CameraView view)
    {
        var width = _platform.GetScreenWidth();
        var height = _platform.GetScreenHeight();
        var viewY = Math.Clamp(view.RayDirection.Y, -1f, 1f);
        var horizonY = (int)MathF.Round(height * (0.60f + viewY * 0.16f));
        var fog = _graphics.FogColor;

        DrawHorizonBand(width, height, horizonY, 48, new Color(fog.R, fog.G, fog.B, (byte)34));
        DrawHorizonBand(width, height, horizonY + 26, 52, new Color(fog.R, fog.G, fog.B, (byte)26));
        DrawHorizonBand(width, height, horizonY + 58, 62, new Color(fog.R, fog.G, fog.B, (byte)18));
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
            _ => Math.Min(renderDistance, 24)
        };
        var veryFarLodDistance = _graphics.Quality switch
        {
            GraphicsQuality.Low => Math.Min(renderDistance, 10),
            GraphicsQuality.Medium => Math.Min(renderDistance, 18),
            _ => Math.Min(renderDistance, 48)
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
            GraphicsQuality.Low => 8,
            GraphicsQuality.Medium => Math.Min(renderDistance, 12),
            _ => Math.Min(renderDistance, 24)
        };
        var foliageDistSq = foliageDistance * foliageDistance;
        var foliageFadeBand = GetFoliageFadeBand();
        var canopyBand = _graphics.Quality switch
        {
            GraphicsQuality.Low => 10,
            GraphicsQuality.Medium => 12,
            _ => 12
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

                        if (surface.Block == BlockType.Leaves)
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
                        BlockType.Grass => new Color(100, 152, 78, 255),
                        BlockType.Dirt => new Color(136, 98, 63, 255),
                        BlockType.Stone => new Color(124, 120, 113, 255),
                        BlockType.Wood => new Color(120, 88, 54, 255),
                        BlockType.Leaves => new Color(86, 134, 66, 255),
                        _ => Color.White
                    };
                    var color = BuildLodBlendedColor(baseColor, surface, distance, distanceFactor, lodBlend);
                    if (chunkReveal < 0.999f)
                    {
                        color = LerpColor(_graphics.FogColor, color, chunkReveal);
                    }

                    QueueWorldCubeInstance(center, color, GetDominantLodTier(lodBlend));

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
            return ApplyVisualStyle(baseColor, surface.Block, surface.X, surface.Y, surface.Z, surface.TopVisible, surface.VisibleFaces, surface.SkyExposure, distance);
        }

        if (lodBlend.Mid >= 0.999f)
        {
            return ApplyMidVisualStyle(baseColor, surface.Block, surface.X, surface.Y, surface.Z, surface.TopVisible, surface.VisibleFaces, surface.SkyExposure, distance);
        }

        if (lodBlend.Far >= 0.999f)
        {
            return ApplyFarVisualStyle(baseColor, surface.Block, surface.X, surface.Y, surface.Z, surface.TopVisible, distance);
        }

        if (lodBlend.Near > 0.0001f && lodBlend.Far <= 0.0001f)
        {
            var nearColor = ApplyVisualStyle(baseColor, surface.Block, surface.X, surface.Y, surface.Z, surface.TopVisible, surface.VisibleFaces, surface.SkyExposure, distance);
            var midColor = ApplyMidVisualStyle(baseColor, surface.Block, surface.X, surface.Y, surface.Z, surface.TopVisible, surface.VisibleFaces, surface.SkyExposure, distance);
            var t = lodBlend.Mid / Math.Max(0.0001f, lodBlend.Near + lodBlend.Mid);
            return LerpColor(nearColor, midColor, t);
        }

        if (lodBlend.Far > 0.0001f && lodBlend.Near <= 0.0001f)
        {
            var midColor = ApplyMidVisualStyle(baseColor, surface.Block, surface.X, surface.Y, surface.Z, surface.TopVisible, surface.VisibleFaces, surface.SkyExposure, distance);
            var farColor = ApplyFarVisualStyle(baseColor, surface.Block, surface.X, surface.Y, surface.Z, surface.TopVisible, distance);
            var t = lodBlend.Far / Math.Max(0.0001f, lodBlend.Mid + lodBlend.Far);
            return LerpColor(midColor, farColor, t);
        }

        var isTerrainBlock = surface.Block is BlockType.Grass or BlockType.Dirt or BlockType.Stone;
        if (isTerrainBlock && distanceFactor > 0.62f)
        {
            return ApplyFarVisualStyle(baseColor, surface.Block, surface.X, surface.Y, surface.Z, surface.TopVisible, distance);
        }

        var blendedNear = ApplyVisualStyle(baseColor, surface.Block, surface.X, surface.Y, surface.Z, surface.TopVisible, surface.VisibleFaces, surface.SkyExposure, distance);
        var blendedFar = ApplyFarVisualStyle(baseColor, surface.Block, surface.X, surface.Y, surface.Z, surface.TopVisible, distance);
        return LerpColor(blendedNear, blendedFar, 0.5f);
    }

    private Color ApplyMidVisualStyle(Color baseColor, BlockType block, int x, int y, int z, bool topVisible, int visibleFaces, int skyExposure, float distance)
    {
        var noise = (Math.Abs((x * 73856093) ^ (y * 19349663) ^ (z * 83492791)) % 13) - 6;
        var noiseScale = _graphics.TextureNoiseStrength * 0.82f;
        var dr = (int)(noise * noiseScale * 0.9f);
        var dg = block == BlockType.Stone ? (int)(noise * noiseScale * 0.84f) : (int)(noise * noiseScale * 0.8f);
        var db = block == BlockType.Stone ? (int)(noise * noiseScale * 0.54f) : (int)(noise * noiseScale * 0.66f);
        var textured = ChangeRgb(baseColor, dr, dg, db);

        var brightness = topVisible ? 1f : 0.88f;
        brightness += (visibleFaces - 2) * 0.02f;
        brightness *= (0.92f + Math.Clamp(skyExposure / 5f, 0f, 1f) * 0.11f) * _graphics.LightStrength;
        var lit = MultiplyRgb(textured, brightness);
        var contrasted = ApplyContrast(lit, 1f + (_graphics.Contrast - 1f) * 0.72f);
        if (!_graphics.FogEnabled)
        {
            return contrasted;
        }

        var fogT = Math.Clamp((distance - _graphics.FogNear) / (_graphics.FogFar - _graphics.FogNear), 0f, 1f);
        return LerpColor(contrasted, _graphics.FogColor, fogT);
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

    private float GetSparseFarKeepChance(BlockType block, float distance, float veryFarDistance, float ultraFarDistance, float renderDistance)
    {
        var veryBand = Math.Max(4f, Math.Min(renderDistance * 0.16f, 16f));
        var ultraBand = Math.Max(4f, Math.Min(renderDistance * 0.18f, 18f));
        var veryT = SmoothStep01((distance - veryFarDistance) / veryBand);
        var ultraT = SmoothStep01((distance - ultraFarDistance) / ultraBand);

        var veryMinKeep = block switch
        {
            BlockType.Wood => 0.48f,
            BlockType.Leaves => 0.2f,
            BlockType.Grass or BlockType.Dirt or BlockType.Stone => 0.33f,
            _ => 0.15f
        };

        var ultraMinKeep = block switch
        {
            BlockType.Wood => 0.22f,
            BlockType.Leaves => 0.06f,
            BlockType.Grass or BlockType.Dirt or BlockType.Stone => 0.18f,
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

    private bool HasSteepDropNear(int x, int y, int z)
    {
        return !_world.IsSolid(x + 1, y - 1, z)
            || !_world.IsSolid(x - 1, y - 1, z)
            || !_world.IsSolid(x, y - 1, z + 1)
            || !_world.IsSolid(x, y - 1, z - 1);
    }

    private Color ApplyVisualStyle(Color baseColor, BlockType block, int x, int y, int z, bool topVisible, int visibleFaces, int skyExposure, float distance)
    {
        var noise = (Math.Abs((x * 73856093) ^ (y * 19349663) ^ (z * 83492791)) % 15) - 7;
        var noiseScale = _graphics.TextureNoiseStrength;

        if (block == BlockType.Leaves)
        {
            return ApplyLeafStyle(baseColor, noise, noiseScale, distance);
        }

        var dr = (int)(noise * noiseScale);
        var dg = block switch
        {
            BlockType.Stone => (int)(noise * 0.92f * noiseScale),
            _ => (int)(noise * 0.88f * noiseScale)
        };
        var db = block switch
        {
            BlockType.Stone => (int)(noise * 0.56f * noiseScale),
            _ => (int)(noise * 0.76f * noiseScale)
        };
        var textured = ChangeRgb(baseColor, dr, dg, db);

        var brightness = topVisible ? 1.06f : 0.9f;
        brightness += (visibleFaces - 2) * 0.028f;
        var nearRelief = GetNearReliefContrast(distance);

        if (topVisible)
        {
            brightness *= 0.82f + skyExposure * 0.05f;
            var cavity = Math.Clamp((3f - skyExposure) / 3f, 0f, 1f);
            var ridge = Math.Clamp((skyExposure - 3f) / 2f, 0f, 1f);
            brightness *= 1f - cavity * nearRelief * 0.2f;
            brightness *= 1f + ridge * nearRelief * 0.08f;

            if (_graphics.ReliefContoursEnabled && HasSteepDropNear(x, y, z))
            {
                brightness *= 0.86f - nearRelief * 0.03f;
            }
        }
        else
        {
            brightness *= 1f - nearRelief * 0.08f;
        }

        var sunExposure = Math.Clamp(skyExposure / 5f, 0f, 1f);
        brightness *= (0.94f + sunExposure * 0.16f) * _graphics.LightStrength;

        var lit = MultiplyRgb(textured, brightness);
        var contrasted = ApplyContrast(lit, _graphics.Contrast);

        if (!_graphics.FogEnabled)
        {
            return contrasted;
        }

        var fogT = Math.Clamp((distance - _graphics.FogNear) / (_graphics.FogFar - _graphics.FogNear), 0f, 1f);
        return LerpColor(contrasted, _graphics.FogColor, fogT);
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

    private Color ApplyFarVisualStyle(Color baseColor, BlockType block, int x, int y, int z, bool topVisible, float distance)
    {
        var noise = (Math.Abs((x * 73856093) ^ (y * 19349663) ^ (z * 83492791)) % 11) - 5;
        var noiseScale = _graphics.TextureNoiseStrength * 0.7f;
        var dr = (int)(noise * noiseScale);
        var dg = block == BlockType.Stone ? (int)(noise * 0.85f * noiseScale) : (int)(noise * 0.8f * noiseScale);
        var db = block == BlockType.Stone ? (int)(noise * 0.52f * noiseScale) : (int)(noise * 0.68f * noiseScale);
        var textured = ChangeRgb(baseColor, dr, dg, db);

        var brightness = (topVisible ? 0.98f : 0.86f) * _graphics.LightStrength;
        var lit = MultiplyRgb(textured, brightness);
        var contrasted = ApplyContrast(lit, 1f + (_graphics.Contrast - 1f) * 0.55f);
        if (!_graphics.FogEnabled)
        {
            return contrasted;
        }

        var fogT = Math.Clamp((distance - _graphics.FogNear) / (_graphics.FogFar - _graphics.FogNear), 0f, 1f);
        return LerpColor(contrasted, _graphics.FogColor, fogT);
    }

    private Color ApplyLeafStyle(Color baseColor, int noise, float noiseScale, float distance)
    {
        var variation = (int)(noise * 0.6f * noiseScale);
        var textured = ChangeRgb(baseColor, variation / 4, variation, variation / 5);
        var lit = MultiplyRgb(textured, 0.96f * _graphics.LightStrength);
        var contrasted = ApplyContrast(lit, 1f + (_graphics.Contrast - 1f) * 0.45f);

        if (!_graphics.FogEnabled)
        {
            return contrasted;
        }

        var fogT = Math.Clamp((distance - _graphics.FogNear) / (_graphics.FogFar - _graphics.FogNear), 0f, 1f);
        return LerpColor(contrasted, _graphics.FogColor, fogT);
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
        return new Color(119, 171, 222, 255);
    }

    private static Color GetSkyHorizonColor()
    {
        return new Color(166, 201, 228, 255);
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
        var walkSwing = MathF.Sin(visual.WalkPhase) * 0.22f * visual.WalkBlend;
        var armLift = visual.IsJumping ? 0.08f : visual.IsFalling ? -0.05f : 0f;

        var torso = root + new Vector3(0f, 1.08f, 0f);
        var head = root + new Vector3(0f, 1.82f, 0f) + forward * 0.04f;
        var leftArm = root + Vector3.UnitY * (1.12f + armLift) - right * 0.38f + forward * walkSwing;
        var rightArm = root + Vector3.UnitY * (1.12f + armLift) + right * 0.38f - forward * walkSwing;
        var leftLeg = root + Vector3.UnitY * 0.44f - right * 0.16f - forward * walkSwing;
        var rightLeg = root + Vector3.UnitY * 0.44f + right * 0.16f + forward * walkSwing;
        Vector3? wristDevice = null;

        if (devicePose is WristDevicePose pose)
        {
            leftArm = root + Vector3.UnitY * (1.16f + 0.18f * pose.RaiseBlend) - right * (0.34f - 0.16f * pose.RaiseBlend) + forward * (0.04f + 0.32f * pose.RaiseBlend);
            rightArm = root + Vector3.UnitY * (1.08f + 0.08f * pose.RaiseBlend) + right * (0.34f - 0.10f * pose.TapBlend) + forward * (0.02f + 0.10f * pose.RaiseBlend + 0.34f * pose.TapBlend);
            wristDevice = leftArm + forward * 0.18f + right * 0.10f + Vector3.UnitY * 0.02f;
        }

        _platform.DrawCube(torso, 0.6f, 0.9f, 0.36f, torsoColor);
        _platform.DrawCube(head, 0.46f, 0.46f, 0.46f, skinColor);
        _platform.DrawCube(leftArm, 0.2f, 0.72f, 0.2f, armColor);
        _platform.DrawCube(rightArm, 0.2f, 0.72f, 0.2f, armColor);
        _platform.DrawCube(leftLeg, 0.24f, 0.74f, 0.24f, legColor);
        _platform.DrawCube(rightLeg, 0.24f, 0.74f, 0.24f, legColor);
        if (wristDevice is Vector3 devicePosition)
        {
            _platform.DrawCube(devicePosition, 0.26f, 0.18f, 0.18f, new Color(46, 62, 70, 255));
            _platform.DrawCube(devicePosition + forward * 0.08f, 0.02f, 0.20f, 0.18f, new Color(118, 255, 228, 155));
        }
        _platform.DrawCube(root + new Vector3(0f, -0.02f, 0f), 0.74f, 0.02f, 0.74f, new Color(0, 0, 0, 35));
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
        if (_botDevice.IsOpen)
        {
            var raise = _botDeviceVisual.RaiseBlend;
            var tap = _botDeviceVisual.TapBlend;
            var deviceHand = camera.Position + forward * (0.46f + 0.08f * raise) - right * (0.12f - 0.22f * raise) - up * (0.42f - 0.20f * raise) + up * bob;
            var deviceBody = deviceHand + forward * 0.18f + right * 0.09f;
            var hologram = deviceBody + forward * 0.10f + up * 0.06f;
            var tapHand = camera.Position + forward * (0.56f + 0.10f * raise + 0.08f * tap) + right * (0.22f - 0.18f * tap) - up * (0.36f - 0.08f * raise) + up * bob;

            _platform.DrawCube(deviceHand, 0.16f, 0.28f, 0.18f, new Color(232, 202, 172, 255));
            _platform.DrawCube(deviceBody, 0.24f, 0.17f, 0.16f, new Color(50, 68, 76, 255));
            _platform.DrawCube(hologram, 0.02f, 0.22f, 0.18f, new Color(118, 255, 228, 150));
            _platform.DrawCube(tapHand, 0.15f, 0.24f, 0.17f, new Color(232, 202, 172, 255));
            return;
        }

        var hand = camera.Position + forward * 0.72f + right * 0.34f - up * 0.28f + up * bob;
        var held = hand + forward * 0.16f + right * 0.08f;

        _platform.DrawCube(hand, 0.16f, 0.26f, 0.18f, new Color(232, 202, 172, 255));
        _platform.DrawCube(held, 0.18f, 0.18f, 0.18f, GetHeldBlockColor(_hotbar[_selectedHotbarIndex]));
        if (_graphics.DrawBlockWires)
        {
            _platform.DrawCubeWires(held, 0.18f, 0.18f, 0.18f, new Color(0, 0, 0, 35));
        }
    }

    private static Color GetHeldBlockColor(BlockType block)
    {
        return block switch
        {
            BlockType.Grass => new Color(105, 162, 82, 255),
            BlockType.Dirt => new Color(146, 106, 72, 255),
            BlockType.Stone => new Color(128, 123, 116, 255),
            BlockType.Wood => new Color(132, 96, 58, 255),
            BlockType.Leaves => new Color(86, 138, 70, 255),
            _ => new Color(220, 220, 220, 255)
        };
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

        _platform.DrawCube(faceCenter, width, height, length, new Color(255, 232, 87, 110));
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

        var hudHeight = _companion is null ? 112 : 186;
        _platform.DrawRectangle(8, 8, 500, hudHeight, new Color(255, 255, 255, 190));
        _platform.DrawUiText($"FPS: {_platform.GetFps()}", new Vector2(16, 14), 20, 1f, Color.Black);
        _platform.DrawUiText($"Pos: {_player.Position.X:0.00}, {_player.Position.Y:0.00}, {_player.Position.Z:0.00}", new Vector2(16, 38), 20, 1f, Color.DarkGray);
        _platform.DrawUiText($"Render: {_lastFrameMs:0.0} ms  |  Графика: {GetQualityName(_graphics.Quality)}", new Vector2(16, 62), 18, 1f, Color.DarkGray);
        _platform.DrawUiText($"Камера: {GetCameraModeName(_cameraMode)}", new Vector2(16, 84), 18, 1f, Color.DarkGray);
        if (_companion is not null)
        {
            _platform.DrawUiText($"Бот: {_companion.Status.GetLabel()}", new Vector2(16, 108), 18, 1f, Color.Black);
            _platform.DrawUiText($"Активно: {_companion.GetActiveSummary()}", new Vector2(16, 130), 18, 1f, Color.DarkGray);
            _platform.DrawUiText($"Далее: {_companion.GetQueuedSummary()}", new Vector2(16, 152), 18, 1f, Color.DarkGray);
            _platform.DrawUiText($"Запасы: {_companion.GetStockpileSummary()}", new Vector2(16, 174), 18, 1f, Color.DarkGray);
        }
    }

    private void DrawHotbar()
    {
        var slotWidth = 120;
        var slotHeight = 42;
        var spacing = 8;
        var totalWidth = _hotbar.Length * slotWidth + (_hotbar.Length - 1) * spacing;

        var startX = _platform.GetScreenWidth() / 2 - totalWidth / 2;
        var y = _platform.GetScreenHeight() - slotHeight - 18;

        for (var i = 0; i < _hotbar.Length; i++)
        {
            var x = startX + i * (slotWidth + spacing);
            var isSelected = i == _selectedHotbarIndex;

            _platform.DrawRectangle(x, y, slotWidth, slotHeight, isSelected ? new Color(255, 245, 163, 235) : new Color(245, 245, 245, 210));
            var label = $"{i + 1}: {GetBlockName(_hotbar[i])}";
            _platform.DrawUiText(label, new Vector2(x + 8, y + 12), 18, 1f, Color.Black);
        }
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
        if (_companion is null || !_botDevice.IsOpen)
        {
            return;
        }

        var panel = GetBotDevicePanelRect();
        var accent = new Color(118, 255, 228, 190);
        var glow = new Color(66, 126, 140, 210);
        _platform.DrawRectangle(panel.X, panel.Y, panel.W, panel.H, new Color(8, 28, 36, 208));
        _platform.DrawRectangle(panel.X + 4, panel.Y + 4, panel.W - 8, 56, new Color(17, 49, 58, 220));
        _platform.DrawUiText("Наручный модуль бота", new Vector2(panel.X + 20, panel.Y + 18), 28, 1f, accent);
        _platform.DrawUiText("Проекция активна", new Vector2(panel.X + 22, panel.Y + 58), 18, 1f, new Color(170, 224, 235, 255));
        _platform.DrawUiText($"Статус: {_companion.Status.GetLabel()}", new Vector2(panel.X + 22, panel.Y + 88), 18, 1f, Color.White);
        _platform.DrawUiText($"Активно: {_companion.GetActiveSummary()}", new Vector2(panel.X + 22, panel.Y + 112), 18, 1f, new Color(210, 240, 246, 255));
        _platform.DrawUiText($"Очередь: {_companion.GetQueuedSummary()}", new Vector2(panel.X + 22, panel.Y + 134), 18, 1f, new Color(210, 240, 246, 255));
        _platform.DrawUiText($"Запасы: {_companion.GetStockpileSummary()}", new Vector2(panel.X + 22, panel.Y + 156), 18, 1f, new Color(210, 240, 246, 255));

        switch (_botDevice.Screen)
        {
            case BotWristDeviceScreen.Main:
                DrawBotDeviceButton(GetBotDeviceGatherButtonRect(), "Сбор ресурсов", glow, Color.White);
                DrawBotDeviceButton(GetBotDeviceBuildButtonRect(), "Построить Дом S", new Color(76, 121, 173, 220), Color.White);
                DrawBotDeviceButton(GetBotDeviceCancelButtonRect(), "Сбросить команды", new Color(168, 80, 80, 220), Color.White);
                DrawBotDeviceButton(GetBotDeviceCloseButtonRect(), "Убрать устройство", new Color(48, 73, 79, 220), Color.White);
                _platform.DrawUiText("Клавиша B: открыть/убрать модуль", new Vector2(panel.X + 22, panel.Y + 352), 18, 1f, accent);
                break;
            case BotWristDeviceScreen.GatherResource:
                _platform.DrawUiText("Сбор ресурсов", new Vector2(panel.X + 22, panel.Y + 188), 26, 1f, accent);
                DrawBotDeviceResourceButton(GetBotDeviceWoodButtonRect(), "Дерево", BotResourceType.Wood);
                DrawBotDeviceResourceButton(GetBotDeviceStoneButtonRect(), "Камень", BotResourceType.Stone);
                DrawBotDeviceResourceButton(GetBotDeviceDirtButtonRect(), "Земля", BotResourceType.Dirt);
                DrawBotDeviceResourceButton(GetBotDeviceLeavesButtonRect(), "Листва", BotResourceType.Leaves);
                var amount = GetBotDeviceAmountRect();
                _platform.DrawRectangle(amount.X, amount.Y, amount.W, amount.H, new Color(14, 42, 50, 230));
                _platform.DrawUiText($"Количество: {_botDevice.AmountText}", new Vector2(amount.X + 18, amount.Y + 12), 24, 1f, Color.White);
                _platform.DrawUiText("Цифры: 0-9, Backspace, Enter", new Vector2(amount.X + 18, amount.Y + 44), 16, 1f, accent);
                DrawBotDeviceButton(GetBotDeviceConfirmButtonRect(), "Подтвердить сбор", glow, Color.White);
                DrawBotDeviceButton(GetBotDeviceBackButtonRect(), "Назад", new Color(48, 73, 79, 220), Color.White);
                break;
            case BotWristDeviceScreen.BuildHouse:
                _platform.DrawUiText("Строительство", new Vector2(panel.X + 22, panel.Y + 188), 26, 1f, accent);
                _platform.DrawUiText("Шаблон: Дом S", new Vector2(panel.X + 22, panel.Y + 224), 22, 1f, Color.White);
                _platform.DrawUiText("Бот сам подготовит площадку, доберет ресурсы и начнет стройку.", new Vector2(panel.X + 22, panel.Y + 258), 18, 1f, new Color(210, 240, 246, 255));
                DrawBotDeviceButton(GetBotDeviceConfirmButtonRect(), "Запустить стройку", new Color(76, 121, 173, 220), Color.White);
                DrawBotDeviceButton(GetBotDeviceBackButtonRect(), "Назад", new Color(48, 73, 79, 220), Color.White);
                break;
        }

        if (!string.IsNullOrWhiteSpace(_botDevice.Message))
        {
            _platform.DrawUiText(_botDevice.Message, new Vector2(panel.X + 22, panel.Y + panel.H - 30), 18, 1f, accent);
        }
    }

    private void DrawBotDeviceButton((int X, int Y, int W, int H) rect, string label, Color background, Color text)
    {
        _platform.DrawRectangle(rect.X, rect.Y, rect.W, rect.H, background);
        _platform.DrawUiText(label, new Vector2(rect.X + 18, rect.Y + 13), 24, 1f, text);
    }

    private void DrawBotDeviceResourceButton((int X, int Y, int W, int H) rect, string label, BotResourceType resource)
    {
        var isSelected = _botDevice.SelectedResource == resource;
        var fill = isSelected
            ? new Color(118, 255, 228, 225)
            : new Color(15, 55, 65, 225);
        var text = isSelected ? Color.Black : Color.White;
        DrawBotDeviceButton(rect, label, fill, text);
    }

    private (int X, int Y, int W, int H) GetBotDevicePanelRect()
    {
        var x = _platform.GetScreenWidth() - BotDevicePanelWidth - 52;
        var y = 84;
        return (x, y, BotDevicePanelWidth, 418);
    }

    private (int X, int Y, int W, int H) GetBotDeviceGatherButtonRect()
    {
        var panel = GetBotDevicePanelRect();
        return (panel.X + 20, panel.Y + 188, panel.W - 40, BotDeviceButtonHeight);
    }

    private (int X, int Y, int W, int H) GetBotDeviceBuildButtonRect()
    {
        var gather = GetBotDeviceGatherButtonRect();
        return (gather.X, gather.Y + BotDeviceButtonHeight + BotDeviceButtonsGap, gather.W, BotDeviceButtonHeight);
    }

    private (int X, int Y, int W, int H) GetBotDeviceCancelButtonRect()
    {
        var build = GetBotDeviceBuildButtonRect();
        return (build.X, build.Y + BotDeviceButtonHeight + BotDeviceButtonsGap, build.W, BotDeviceButtonHeight);
    }

    private (int X, int Y, int W, int H) GetBotDeviceCloseButtonRect()
    {
        var cancel = GetBotDeviceCancelButtonRect();
        return (cancel.X, cancel.Y + BotDeviceButtonHeight + BotDeviceButtonsGap, cancel.W, BotDeviceButtonHeight);
    }

    private (int X, int Y, int W, int H) GetBotDeviceWoodButtonRect()
    {
        var panel = GetBotDevicePanelRect();
        return (panel.X + 20, panel.Y + 226, 184, BotDeviceButtonHeight);
    }

    private (int X, int Y, int W, int H) GetBotDeviceStoneButtonRect()
    {
        var wood = GetBotDeviceWoodButtonRect();
        return (wood.X + wood.W + 12, wood.Y, wood.W, wood.H);
    }

    private (int X, int Y, int W, int H) GetBotDeviceDirtButtonRect()
    {
        var wood = GetBotDeviceWoodButtonRect();
        return (wood.X, wood.Y + BotDeviceButtonHeight + BotDeviceButtonsGap, wood.W, wood.H);
    }

    private (int X, int Y, int W, int H) GetBotDeviceLeavesButtonRect()
    {
        var dirt = GetBotDeviceDirtButtonRect();
        return (dirt.X + dirt.W + 12, dirt.Y, dirt.W, dirt.H);
    }

    private (int X, int Y, int W, int H) GetBotDeviceAmountRect()
    {
        var dirt = GetBotDeviceDirtButtonRect();
        return (dirt.X, dirt.Y + BotDeviceButtonHeight + BotDeviceButtonsGap, BotDevicePanelWidth - 40, 74);
    }

    private (int X, int Y, int W, int H) GetBotDeviceConfirmButtonRect()
    {
        var amount = GetBotDeviceAmountRect();
        return (amount.X, amount.Y + amount.H + BotDeviceButtonsGap, amount.W, BotDeviceButtonHeight);
    }

    private (int X, int Y, int W, int H) GetBotDeviceBackButtonRect()
    {
        var confirm = GetBotDeviceConfirmButtonRect();
        return (confirm.X, confirm.Y + BotDeviceButtonHeight + BotDeviceButtonsGap, confirm.W, BotDeviceButtonHeight);
    }

    private bool BlockCenterIntersectsPlayer(Vector3 blockCenter)
    {
        var half = _player.ColliderHalfWidth;
        var height = _player.ColliderHeight;

        var playerMin = new Vector3(_player.Position.X - half, _player.Position.Y, _player.Position.Z - half);
        var playerMax = new Vector3(_player.Position.X + half, _player.Position.Y + height, _player.Position.Z + half);

        var blockMin = blockCenter - new Vector3(0.5f, 0.5f, 0.5f);
        var blockMax = blockCenter + new Vector3(0.5f, 0.5f, 0.5f);

        return (playerMin.X <= blockMax.X) & (playerMax.X >= blockMin.X)
            & (playerMin.Y <= blockMax.Y) & (playerMax.Y >= blockMin.Y)
            & (playerMin.Z <= blockMax.Z) & (playerMax.Z >= blockMin.Z);
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
