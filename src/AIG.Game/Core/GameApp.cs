using System.Numerics;
using System.IO;
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
    private readonly record struct AutoCaptureShot(string FileName, Vector3 Position, Vector3 LookTarget);
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

    private PlayerController _player = null!;
    private AppState _state = AppState.MainMenu;
    private CameraMode _cameraMode = CameraMode.FirstPerson;
    private int _selectedHotbarIndex;
    private float _lastFrameMs;
    private float _cameraBobPhase;
    private int _lastStreamChunkX = int.MinValue;
    private int _lastStreamChunkZ = int.MinValue;
    private int _lastStreamRadius = -1;

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

    public void Run()
    {
        InitializePlatform(enableFullscreen: true);

        _player = new PlayerController(_config, CreateSpawnPosition());
        _playerVisual.Reset(_player.Position);
        UpdateWorldStreaming(force: true);

        var shouldExit = false;
        var currentHit = (BlockRaycastHit?)null;
        var currentView = CameraViewBuilder.Build(_player, _world, _cameraMode, 0f);

        while (!shouldExit && !_platform.WindowShouldClose())
        {
            if (_state == AppState.Playing && _platform.IsKeyPressed(KeyboardKey.Escape))
            {
                _state = AppState.PauseMenu;
                _platform.EnableCursor();
            }

            var delta = _platform.GetFrameTime();
            _lastFrameMs = delta * 1000f;
            var cameraBob = 0f;

            if (_state == AppState.Playing)
            {
                if (_platform.IsKeyPressed(KeyboardKey.V) || _platform.IsKeyPressed(KeyboardKey.F5))
                {
                    _cameraMode = CameraViewBuilder.Toggle(_cameraMode);
                }

                HandleHotbarInput();

                var input = ReadInput(_platform);
                _player.Update(_world, input, delta);
                _playerVisual.Update(_player.Position, delta, _config.MoveSpeed);
                UpdateWorldStreaming(force: false);

                var walkBob = _playerVisual.WalkBlend * _graphics.ViewBobScale;
                _cameraBobPhase += delta * (7f + walkBob * 6f);
                cameraBob = MathF.Sin(_cameraBobPhase) * 0.06f * walkBob;

                currentView = CameraViewBuilder.Build(_player, _world, _cameraMode, cameraBob);

                currentHit = VoxelRaycaster.Raycast(_world, currentView.RayOrigin, currentView.RayDirection, _config.InteractionDistance);
                if (_platform.IsMouseButtonPressed(MouseButton.Left))
                {
                    BlockInteraction.TryBreak(_world, currentHit);
                }

                if (_platform.IsMouseButtonPressed(MouseButton.Right))
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

        ShutdownPlatform();
    }

    internal void RunAutoCapture(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        InitializePlatform(enableFullscreen: false);
        _state = AppState.Playing;
        _cameraMode = CameraMode.FirstPerson;
        _platform.DisableCursor();

        var shots = GetAutoCaptureShots();
        if (shots.Length > 0)
        {
            var warmupHit = PrepareAutoCaptureShot(shots[0]);
            var warmupView = CameraViewBuilder.Build(_player, _world, CameraMode.FirstPerson, 0f);
            for (var i = 0; i < 8; i++)
            {
                DrawFrame(warmupHit, warmupView);
            }
            _platform.TakeScreenshot(Path.Combine(outputDirectory, "autocap-warmup.png"));
        }

        foreach (var shot in shots)
        {
            var hit = PrepareAutoCaptureShot(shot);
            var view = CameraViewBuilder.Build(_player, _world, CameraMode.FirstPerson, 0f);
            DrawFrame(hit, view);
            DrawFrame(hit, view);

            _platform.TakeScreenshot(Path.Combine(outputDirectory, shot.FileName));
        }

        ShutdownPlatform();
    }

    internal void RunAutoPerf(string outputDirectory, float durationSeconds = 12f, int minAllowedFps = 60)
    {
        Directory.CreateDirectory(outputDirectory);

        InitializePlatform(enableFullscreen: false);
        _state = AppState.Playing;
        _cameraMode = CameraMode.FirstPerson;
        _platform.DisableCursor();

        _player = new PlayerController(_config, CreateSpawnPosition());
        _playerVisual.Reset(_player.Position);
        UpdateWorldStreaming(force: true);

        var duration = Math.Clamp(durationSeconds, 1f, 300f);
        var fpsThreshold = Math.Clamp(minAllowedFps, 1, 240);
        var bot = new AutoBotState
        {
            LastPosition = _player.Position,
            TurnSign = 1
        };

        var elapsed = 0f;
        var frameCount = 0;
        var minFps = int.MaxValue;
        var maxFps = 0;
        var sumFps = 0.0;
        var belowThresholdFrames = 0;

        while (elapsed < duration && !_platform.WindowShouldClose())
        {
            var rawDelta = _platform.GetFrameTime();
            var delta = rawDelta > 0f ? rawDelta : 1f / 60f;
            delta = Math.Clamp(delta, 1f / 240f, 0.05f);
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

            var fps = Math.Max(1, _platform.GetFps());
            minFps = Math.Min(minFps, fps);
            maxFps = Math.Max(maxFps, fps);
            sumFps += fps;
            frameCount++;
            if (fps < fpsThreshold)
            {
                belowThresholdFrames++;
            }
        }

        if (frameCount == 0)
        {
            minFps = 0;
        }

        var avgFps = frameCount == 0 ? 0f : (float)(sumFps / frameCount);
        var result = frameCount > 0 && belowThresholdFrames == 0 ? "PASS" : "FAIL";
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
        _platform.ClearBackground(GetSkyColor());

        if (_state != AppState.MainMenu)
        {
            _platform.BeginMode3D(view.Camera);
            DrawWorld();
            DrawPlayerAvatar();
            DrawBlockHighlight(hit, view.RayOrigin, view.RayDirection);
            DrawFirstPersonHand(view.Camera);
            _platform.EndMode3D();

            DrawHud(_state == AppState.Playing);
            DrawHotbar();
        }

        if (_state != AppState.Playing)
        {
            DrawMenu();
        }

        _platform.EndDrawing();
    }

    private void DrawWorld()
    {
        var renderDistance = _graphics.RenderDistance;
        var centerX = (int)MathF.Floor(_player.Position.X);
        var centerY = (int)MathF.Floor(_player.Position.Y);
        var centerZ = (int)MathF.Floor(_player.Position.Z);

        var minX = Math.Max(0, centerX - renderDistance);
        var maxX = Math.Min(_world.Width - 1, centerX + renderDistance);
        var minZ = Math.Max(0, centerZ - renderDistance);
        var maxZ = Math.Min(_world.Depth - 1, centerZ + renderDistance);
        var belowY = _graphics.Quality switch
        {
            GraphicsQuality.Low => 8,
            GraphicsQuality.Medium => 10,
            _ => 12
        };
        var aboveY = _graphics.Quality switch
        {
            GraphicsQuality.Low => 12,
            GraphicsQuality.Medium => 14,
            _ => 16
        };
        var centerSurfaceY = _world.GetTerrainTopY(centerX, centerZ);
        var minY = Math.Max(0, Math.Min(centerY - belowY, centerSurfaceY - 6));
        var maxY = Math.Min(_world.Height - 1, Math.Max(centerY + aboveY, centerSurfaceY + 12));

        var maxDistSq = renderDistance * renderDistance;
        var foliageDistance = _graphics.Quality switch
        {
            GraphicsQuality.Low => 8,
            GraphicsQuality.Medium => Math.Min(renderDistance, 12),
            _ => renderDistance
        };
        var foliageDistSq = foliageDistance * foliageDistance;
        var canopyBand = _graphics.Quality switch
        {
            GraphicsQuality.Low => 10,
            GraphicsQuality.Medium => 12,
            _ => 12
        };

        for (var x = minX; x <= maxX; x++)
        {
            for (var z = minZ; z <= maxZ; z++)
            {
                var dx = x - _player.Position.X;
                var dz = z - _player.Position.Z;
                var distSq = dx * dx + dz * dz;
                if (distSq > maxDistSq)
                {
                    continue;
                }

                var columnSurfaceY = _world.GetTerrainTopY(x, z);
                var columnMinY = Math.Max(minY, columnSurfaceY - 6);
                var columnMaxY = Math.Min(maxY, columnSurfaceY + canopyBand);

                var hiddenStreak = 0;
                for (var y = columnMaxY; y >= columnMinY; y--)
                {
                    var block = _world.GetBlock(x, y, z);
                    if (block == BlockType.Air)
                    {
                        continue;
                    }

                    if (block == BlockType.Leaves && distSq > foliageDistSq)
                    {
                        continue;
                    }

                    if (!IsBlockVisible(x, y, z))
                    {
                        var isTerrainCore = block is BlockType.Grass or BlockType.Dirt or BlockType.Stone;
                        if (isTerrainCore && y <= columnSurfaceY)
                        {
                            hiddenStreak++;
                            if (hiddenStreak >= 3)
                            {
                                break;
                            }
                        }

                        continue;
                    }

                    hiddenStreak = 0;

                    var center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                    var color = block switch
                    {
                        BlockType.Grass => new Color(100, 152, 78, 255),
                        BlockType.Dirt => new Color(136, 98, 63, 255),
                        BlockType.Stone => new Color(124, 120, 113, 255),
                        BlockType.Wood => new Color(120, 88, 54, 255),
                        BlockType.Leaves => new Color(86, 134, 66, 255),
                        _ => Color.White
                    };
                    var topVisible = IsTopFaceVisible(x, y, z);
                    color = ApplyVisualStyle(color, block, x, y, z, centerX, centerZ, topVisible);

                    _platform.DrawCube(center, 1f, 1f, 1f, color);
                    if (_graphics.DrawBlockWires)
                    {
                        _platform.DrawCubeWires(center, 1f, 1f, 1f, new Color(0, 0, 0, 35));
                    }
                }
            }
        }
    }

    private bool IsBlockVisible(int x, int y, int z)
    {
        return !_world.IsSolid(x + 1, y, z)
            || !_world.IsSolid(x - 1, y, z)
            || !_world.IsSolid(x, y + 1, z)
            || !_world.IsSolid(x, y - 1, z)
            || !_world.IsSolid(x, y, z + 1)
            || !_world.IsSolid(x, y, z - 1);
    }

    private bool IsTopFaceVisible(int x, int y, int z)
    {
        return !_world.IsSolid(x, y + 1, z);
    }

    private bool HasSteepDropNear(int x, int y, int z)
    {
        return !_world.IsSolid(x + 1, y - 1, z)
            || !_world.IsSolid(x - 1, y - 1, z)
            || !_world.IsSolid(x, y - 1, z + 1)
            || !_world.IsSolid(x, y - 1, z - 1);
    }

    private Color ApplyVisualStyle(Color baseColor, BlockType block, int x, int y, int z, int centerX, int centerZ, bool topVisible)
    {
        var noise = (Math.Abs((x * 73856093) ^ (y * 19349663) ^ (z * 83492791)) % 15) - 7;
        var noiseScale = _graphics.TextureNoiseStrength;

        if (block == BlockType.Leaves)
        {
            return ApplyLeafStyle(baseColor, x, z, centerX, centerZ, noise, noiseScale);
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

        var visibleFaces = CountVisibleFaces(x, y, z);
        var brightness = topVisible ? 1.06f : 0.9f;
        brightness += (visibleFaces - 2) * 0.028f;

        if (topVisible)
        {
            var skyExposure = CountSkyExposure(x, y, z);
            brightness *= 0.82f + skyExposure * 0.05f;

            if (_graphics.ReliefContoursEnabled && HasSteepDropNear(x, y, z))
            {
                brightness *= 0.88f;
            }
        }

        var sunExposure = Math.Clamp(CountSkyExposure(x, y, z) / 5f, 0f, 1f);
        brightness *= (0.94f + sunExposure * 0.16f) * _graphics.LightStrength;

        var lit = MultiplyRgb(textured, brightness);
        var contrasted = ApplyContrast(lit, _graphics.Contrast);

        if (!_graphics.FogEnabled)
        {
            return contrasted;
        }

        var dx = x - centerX;
        var dz = z - centerZ;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        var fogT = Math.Clamp((distance - _graphics.FogNear) / (_graphics.FogFar - _graphics.FogNear), 0f, 1f);
        return LerpColor(contrasted, _graphics.FogColor, fogT);
    }

    private Color ApplyLeafStyle(Color baseColor, int x, int z, int centerX, int centerZ, int noise, float noiseScale)
    {
        var variation = (int)(noise * 0.6f * noiseScale);
        var textured = ChangeRgb(baseColor, variation / 4, variation, variation / 5);
        var lit = MultiplyRgb(textured, 0.96f * _graphics.LightStrength);
        var contrasted = ApplyContrast(lit, 1f + (_graphics.Contrast - 1f) * 0.45f);

        if (!_graphics.FogEnabled)
        {
            return contrasted;
        }

        var dx = x - centerX;
        var dz = z - centerZ;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        var fogT = Math.Clamp((distance - _graphics.FogNear) / (_graphics.FogFar - _graphics.FogNear), 0f, 1f);
        return LerpColor(contrasted, _graphics.FogColor, fogT);
    }

    private int CountVisibleFaces(int x, int y, int z)
    {
        var count = 0;
        if (!_world.IsSolid(x + 1, y, z)) count++;
        if (!_world.IsSolid(x - 1, y, z)) count++;
        if (!_world.IsSolid(x, y + 1, z)) count++;
        if (!_world.IsSolid(x, y - 1, z)) count++;
        if (!_world.IsSolid(x, y, z + 1)) count++;
        if (!_world.IsSolid(x, y, z - 1)) count++;
        return count;
    }

    private int CountSkyExposure(int x, int y, int z)
    {
        var count = 0;
        if (!_world.IsSolid(x, y + 1, z)) count++;
        if (!_world.IsSolid(x + 1, y + 1, z)) count++;
        if (!_world.IsSolid(x - 1, y + 1, z)) count++;
        if (!_world.IsSolid(x, y + 1, z + 1)) count++;
        if (!_world.IsSolid(x, y + 1, z - 1)) count++;
        return count;
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

    private static Color GetSkyColor()
    {
        return new Color(132, 188, 243, 255);
    }

    private void DrawPlayerAvatar()
    {
        if (_cameraMode != CameraMode.ThirdPerson)
        {
            return;
        }

        var yaw = _player.Yaw;
        var forward = new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
        forward = Vector3.Normalize(forward);

        var right = new Vector3(-forward.Z, 0f, forward.X);
        var walkSwing = MathF.Sin(_playerVisual.WalkPhase) * 0.22f * _playerVisual.WalkBlend;
        var armLift = _playerVisual.IsJumping ? 0.08f : _playerVisual.IsFalling ? -0.05f : 0f;
        var root = _player.Position + new Vector3(0f, 0.04f, 0f);

        var torso = root + new Vector3(0f, 1.08f, 0f);
        var head = root + new Vector3(0f, 1.82f, 0f) + forward * 0.04f;
        var leftArm = root + Vector3.UnitY * (1.12f + armLift) - right * 0.38f + forward * walkSwing;
        var rightArm = root + Vector3.UnitY * (1.12f + armLift) + right * 0.38f - forward * walkSwing;
        var leftLeg = root + Vector3.UnitY * 0.44f - right * 0.16f - forward * walkSwing;
        var rightLeg = root + Vector3.UnitY * 0.44f + right * 0.16f + forward * walkSwing;

        _platform.DrawCube(torso, 0.6f, 0.9f, 0.36f, new Color(88, 145, 205, 255));
        _platform.DrawCube(head, 0.46f, 0.46f, 0.46f, new Color(232, 200, 170, 255));
        _platform.DrawCube(leftArm, 0.2f, 0.72f, 0.2f, new Color(64, 112, 176, 255));
        _platform.DrawCube(rightArm, 0.2f, 0.72f, 0.2f, new Color(64, 112, 176, 255));
        _platform.DrawCube(leftLeg, 0.24f, 0.74f, 0.24f, new Color(74, 87, 122, 255));
        _platform.DrawCube(rightLeg, 0.24f, 0.74f, 0.24f, new Color(74, 87, 122, 255));
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

        if (tMax < tMin || tMax < 0f || tMin <= tieEpsilon)
        {
            faceNormal = Vector3.Zero;
            return false;
        }

        var xHit = hasX && MathF.Abs(nearX - tMin) <= tieEpsilon;
        var yHit = hasY && MathF.Abs(nearY - tMin) <= tieEpsilon;
        var zHit = hasZ && MathF.Abs(nearZ - tMin) <= tieEpsilon;
        if (!xHit && !yHit && !zHit)
        {
            faceNormal = Vector3.Zero;
            return false;
        }

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

        _platform.DrawRectangle(8, 8, 460, 112, new Color(255, 255, 255, 190));
        _platform.DrawUiText($"FPS: {_platform.GetFps()}", new Vector2(16, 14), 20, 1f, Color.Black);
        _platform.DrawUiText($"Pos: {_player.Position.X:0.00}, {_player.Position.Y:0.00}, {_player.Position.Z:0.00}", new Vector2(16, 38), 20, 1f, Color.DarkGray);
        _platform.DrawUiText($"Render: {_lastFrameMs:0.0} ms  |  Графика: {GetQualityName(_graphics.Quality)}", new Vector2(16, 62), 18, 1f, Color.DarkGray);
        _platform.DrawUiText($"Камера: {GetCameraModeName(_cameraMode)}", new Vector2(16, 84), 18, 1f, Color.DarkGray);
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
                new Vector3(320.72f, 35.0f, 305.52f),
                new Vector3(324.00f, 34.80f, 302.30f)),
            new AutoCaptureShot(
                "autocap-2.png",
                new Vector3(320.72f, 35.0f, 305.52f),
                new Vector3(318.10f, 34.90f, 303.60f))
        ];
    }

    private BlockRaycastHit? PrepareAutoCaptureShot(AutoCaptureShot shot)
    {
        var pose = LiftPoseAboveTerrain(shot.Position);
        _player = new PlayerController(_config, pose);
        _playerVisual.Reset(pose);
        _player.SetPose(pose, shot.LookTarget - shot.Position);
        UpdateWorldStreaming(force: true);
        return VoxelRaycaster.Raycast(_world, _player.EyePosition, _player.LookDirection, _config.InteractionDistance);
    }

    private static GameConfig CreateDefaultConfig()
    {
        return new GameConfig();
    }

    private static WorldMap CreateDefaultWorld(GameConfig config)
    {
        return new WorldMap(width: 600, height: 72, depth: 600, chunkSize: config.ChunkSize, seed: config.WorldSeed);
    }

    private Vector3 CreateSpawnPosition()
    {
        var centerX = Math.Clamp(_world.Width / 2, 0, _world.Width - 1);
        var centerZ = Math.Clamp(_world.Depth / 2, 0, _world.Depth - 1);
        var topY = _world.GetTerrainTopY(centerX, centerZ);
        var preferredY = topY + 3f;
        var minY = 1.2f;
        var maxY = Math.Max(minY, _world.Height - 1.2f);
        var spawnY = Math.Clamp(preferredY, minY, maxY);
        return new Vector3(centerX + 0.5f, spawnY, centerZ + 0.5f);
    }

    private Vector3 LiftPoseAboveTerrain(Vector3 position)
    {
        var x = Math.Clamp((int)MathF.Floor(position.X), 0, _world.Width - 1);
        var z = Math.Clamp((int)MathF.Floor(position.Z), 0, _world.Depth - 1);
        var topY = _world.GetTerrainTopY(x, z);
        var minY = topY + 3f;
        var maxY = Math.Max(minY, _world.Height - 1.2f);
        var y = Math.Clamp(position.Y, minY, maxY);
        return new Vector3(position.X, y, position.Z);
    }

    private void UpdateWorldStreaming(bool force)
    {
        var center = _player.Position;
        var chunkX = Math.Clamp((int)MathF.Floor(center.X), 0, _world.Width - 1) / _world.ChunkSize;
        var chunkZ = Math.Clamp((int)MathF.Floor(center.Z), 0, _world.Depth - 1) / _world.ChunkSize;

        var visibleChunkRadius = (_graphics.RenderDistance + _world.ChunkSize - 1) / _world.ChunkSize;
        var chunkRadius = Math.Max(2, visibleChunkRadius + 1);

        if (!force
            && chunkX == _lastStreamChunkX
            && chunkZ == _lastStreamChunkZ
            && chunkRadius == _lastStreamRadius)
        {
            return;
        }

        _world.EnsureChunksAround(center, chunkRadius);
        _world.UnloadFarChunks(center, chunkRadius + 1);
        _lastStreamChunkX = chunkX;
        _lastStreamChunkZ = chunkZ;
        _lastStreamRadius = chunkRadius;
    }
}
