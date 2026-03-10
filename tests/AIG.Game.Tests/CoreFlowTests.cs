using System.Numerics;
using AIG.Game;
using AIG.Game.Bot;
using AIG.Game.Config;
using AIG.Game.Core;
using AIG.Game.Gameplay;
using AIG.Game.Player;
using AIG.Game.Tests.Fakes;
using AIG.Game.World;
using Raylib_cs;
using System.Reflection;
using System.Linq;
using System.Globalization;
using System.Threading;

namespace AIG.Game.Tests;

public sealed class CoreFlowTests
{
    [Fact(DisplayName = "Program.Main запускает раннер из фабрики")]
    public void Program_Main_UsesFactoryRunner()
    {
        var called = false;
        var previousFactory = Program.GameFactory;

        Program.GameFactory = () => new DelegatingRunner(() => called = true);
        try
        {
            Program.Main([]);
        }
        finally
        {
            Program.GameFactory = previousFactory;
        }

        Assert.True(called);
    }

    [Fact(DisplayName = "Program.Main в режиме autocap использует AutoCaptureFactory")]
    public void Program_Main_Autocap_UsesAutoCaptureFactory()
    {
        var autocapCalled = false;
        var gameCalled = false;
        string? capturedOutputDir = null;

        var previousAutoFactory = Program.AutoCaptureFactory;
        var previousGameFactory = Program.GameFactory;

        Program.AutoCaptureFactory = outputDir => new DelegatingRunner(() =>
        {
            autocapCalled = true;
            capturedOutputDir = outputDir;
        });
        Program.GameFactory = () => new DelegatingRunner(() => gameCalled = true);
        try
        {
            Program.Main(["autocap", "/tmp/aig-autocap-test"]);
        }
        finally
        {
            Program.AutoCaptureFactory = previousAutoFactory;
            Program.GameFactory = previousGameFactory;
        }

        Assert.True(autocapCalled);
        Assert.False(gameCalled);
        Assert.Equal("/tmp/aig-autocap-test", capturedOutputDir);
    }

    [Fact(DisplayName = "Program.Main в режиме autoperf использует AutoPerfFactory")]
    public void Program_Main_Autoperf_UsesAutoPerfFactory()
    {
        var autoperfCalled = false;
        var gameCalled = false;
        string? capturedOutputDir = null;
        float capturedDuration = 0f;
        int capturedMinFps = 0;

        var previousAutoPerfFactory = Program.AutoPerfFactory;
        var previousGameFactory = Program.GameFactory;

        Program.AutoPerfFactory = (outputDir, duration, minFps) => new DelegatingRunner(() =>
        {
            autoperfCalled = true;
            capturedOutputDir = outputDir;
            capturedDuration = duration;
            capturedMinFps = minFps;
        });
        Program.GameFactory = () => new DelegatingRunner(() => gameCalled = true);
        try
        {
            Program.Main(["autoperf", "/tmp/aig-autoperf-test", "13.5", "75"]);
        }
        finally
        {
            Program.AutoPerfFactory = previousAutoPerfFactory;
            Program.GameFactory = previousGameFactory;
        }

        Assert.True(autoperfCalled);
        Assert.False(gameCalled);
        Assert.Equal("/tmp/aig-autoperf-test", capturedOutputDir);
        Assert.Equal(13.5f, capturedDuration);
        Assert.Equal(75, capturedMinFps);
    }

    [Fact(DisplayName = "TryRunAutoCapture возвращает false для обычного запуска")]
    public void Program_TryRunAutoCapture_ReturnsFalse_ForRegularArgs()
    {
        var previousAutoFactory = Program.AutoCaptureFactory;
        var autoCalled = false;
        Program.AutoCaptureFactory = _ =>
        {
            autoCalled = true;
            return new DelegatingRunner(() => { });
        };

        try
        {
            var result = Program.TryRunAutoCapture([]);
            Assert.False(result);
            Assert.False(autoCalled);
        }
        finally
        {
            Program.AutoCaptureFactory = previousAutoFactory;
        }
    }

    [Fact(DisplayName = "TryRunAutoCapture использует директорию по умолчанию")]
    public void Program_TryRunAutoCapture_UsesDefaultOutputDir()
    {
        var previousAutoFactory = Program.AutoCaptureFactory;
        string? outputDir = null;
        Program.AutoCaptureFactory = dir => new DelegatingRunner(() => outputDir = dir);

        try
        {
            var result = Program.TryRunAutoCapture(["autocap"]);
            Assert.True(result);
        }
        finally
        {
            Program.AutoCaptureFactory = previousAutoFactory;
        }

        Assert.NotNull(outputDir);
        Assert.EndsWith("autocap", outputDir, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "TryRunAutoPerf возвращает false для обычного запуска")]
    public void Program_TryRunAutoPerf_ReturnsFalse_ForRegularArgs()
    {
        var previousAutoPerfFactory = Program.AutoPerfFactory;
        var autoPerfCalled = false;
        Program.AutoPerfFactory = (_, _, _) =>
        {
            autoPerfCalled = true;
            return new DelegatingRunner(() => { });
        };

        try
        {
            var result = Program.TryRunAutoPerf([]);
            Assert.False(result);
            Assert.False(autoPerfCalled);
        }
        finally
        {
            Program.AutoPerfFactory = previousAutoPerfFactory;
        }
    }

    [Fact(DisplayName = "TryRunAutoPerf использует директорию и параметры по умолчанию")]
    public void Program_TryRunAutoPerf_UsesDefaultParams()
    {
        var previousAutoPerfFactory = Program.AutoPerfFactory;
        string? outputDir = null;
        float duration = 0f;
        int minFps = 0;
        Program.AutoPerfFactory = (dir, seconds, threshold) => new DelegatingRunner(() =>
        {
            outputDir = dir;
            duration = seconds;
            minFps = threshold;
        });

        try
        {
            var result = Program.TryRunAutoPerf(["autoperf"]);
            Assert.True(result);
        }
        finally
        {
            Program.AutoPerfFactory = previousAutoPerfFactory;
        }

        Assert.NotNull(outputDir);
        Assert.EndsWith("autologs", outputDir, StringComparison.Ordinal);
        Assert.Equal(12f, duration);
        Assert.Equal(60, minFps);
    }

    [Fact(DisplayName = "TryRunAutoPerf нормализует некорректные duration и min fps")]
    public void Program_TryRunAutoPerf_ClampsParams()
    {
        var previousAutoPerfFactory = Program.AutoPerfFactory;
        float duration = 0f;
        int minFps = 0;
        Program.AutoPerfFactory = (_, seconds, threshold) => new DelegatingRunner(() =>
        {
            duration = seconds;
            minFps = threshold;
        });

        try
        {
            var result = Program.TryRunAutoPerf(["autoperf", "/tmp/autoperf", "999", "2"]);
            Assert.True(result);
        }
        finally
        {
            Program.AutoPerfFactory = previousAutoPerfFactory;
        }

        Assert.Equal(15f, duration);
        Assert.Equal(30, minFps);
    }

    [Fact(DisplayName = "TryRunAutoCapture c default AutoCaptureRunner выполняет автокапчер через платформу")]
    public void Program_TryRunAutoCapture_DefaultRunner_CapturesScreenshots()
    {
        var previousPlatformFactory = Program.PlatformFactory;
        var previousConfigFactory = Program.AutoCaptureConfigFactory;
        var previousWorldFactory = Program.WorldFactory;

        var platform = new FakeGamePlatform();
        Program.PlatformFactory = () => platform;
        Program.AutoCaptureConfigFactory = () => new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        Program.WorldFactory = config => new WorldMap(width: 96, height: 32, depth: 96, chunkSize: config.ChunkSize, seed: config.WorldSeed);

        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autocap-{Guid.NewGuid():N}");
        try
        {
            var result = Program.TryRunAutoCapture(["autocap", outputDir]);
            Assert.True(result);
        }
        finally
        {
            Program.PlatformFactory = previousPlatformFactory;
            Program.AutoCaptureConfigFactory = previousConfigFactory;
            Program.WorldFactory = previousWorldFactory;
        }

        Assert.Equal(3, platform.SavedScreenshots.Count);
        Assert.All(platform.SavedScreenshots, screenshot => Assert.StartsWith(outputDir, screenshot, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "CreateDefaultConfig включает диагностику бота по умолчанию")]
    public void GameApp_CreateDefaultConfig_EnablesBotDiagnostics()
    {
        var method = typeof(GameApp).GetMethod("CreateDefaultConfig", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var config = Assert.IsType<GameConfig>(method.Invoke(null, null));
        Assert.True(config.BotDiagnosticsEnabled);
        Assert.EndsWith("botlogs", config.BotDiagnosticsDirectory, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BotDiagnosticsLog не создается когда диагностика отключена")]
    public void BotDiagnosticsLog_Create_ReturnsNull_WhenDisabled()
    {
        var log = BotDiagnosticsLog.Create(new GameConfig());
        Assert.Null(log);
    }

    [Fact(DisplayName = "Обычный запуск GameApp пишет bot log файл")]
    public void GameApp_Run_WritesBotDiagnosticsFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"aig-botlog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var platform = new FakeGamePlatform();
            platform.EnqueueWindowShouldClose(false, true);

            var config = new GameConfig
            {
                FullscreenByDefault = false,
                BotDiagnosticsEnabled = true,
                BotDiagnosticsDirectory = tempDir
            };

            var world = new WorldMap(width: 48, height: 24, depth: 48, chunkSize: config.ChunkSize, seed: 0);
            var app = new GameApp(config, platform, world);
            app.Run();

            var files = Directory.GetFiles(tempDir, "bot-*.log");
            Assert.Single(files);

            var text = File.ReadAllText(files[0]);
            Assert.Contains("[diag] session-start", text, StringComparison.Ordinal);
            Assert.Contains("[app] run-start", text, StringComparison.Ordinal);
            Assert.Contains("[bot] spawn", text, StringComparison.Ordinal);
            Assert.Contains("[app] run-stop", text, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact(DisplayName = "Обычный запуск GameApp не пишет bot log файл при отключенной диагностике")]
    public void GameApp_Run_DoesNotWriteBotDiagnosticsFile_WhenDisabled()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"aig-botlog-off-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var platform = new FakeGamePlatform();
            platform.EnqueueWindowShouldClose(false, true);

            var config = new GameConfig
            {
                FullscreenByDefault = false,
                BotDiagnosticsEnabled = false,
                BotDiagnosticsDirectory = tempDir
            };

            var world = new WorldMap(width: 48, height: 24, depth: 48, chunkSize: config.ChunkSize, seed: 0);
            var app = new GameApp(config, platform, world);
            app.Run();

            Assert.Empty(Directory.GetFiles(tempDir, "bot-*.log"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact(DisplayName = "BotDiagnosticsLog гасит IOException и помечает лог как failed")]
    public void BotDiagnosticsLog_Write_SwallowsIoException()
    {
        var writer = new StreamWriter(new ThrowingWriteStream())
        {
            AutoFlush = true
        };
        using var log = CreateBotDiagnosticsLog(writer, "io.log");

        log.Write("bot", "broken");
        log.Write("bot", "ignored");

        Assert.True(GetPrivateField<bool>(log, "_failed"));
    }

    [Fact(DisplayName = "BotDiagnosticsLog гасит ObjectDisposedException и повторный Dispose безопасен")]
    public void BotDiagnosticsLog_Write_SwallowsObjectDisposedException_AndDisposeIsIdempotent()
    {
        using var writer = new StreamWriter(new MemoryStream());
        var log = CreateBotDiagnosticsLog(writer, "disposed.log");
        writer.Dispose();

        log.Write("bot", "disposed");
        Assert.True(GetPrivateField<bool>(log, "_failed"));

        log.Dispose();
        log.Dispose();
        log.Write("bot", "after-dispose");

        Assert.True(GetPrivateField<bool>(log, "_disposed"));
    }

    [Fact(DisplayName = "BotDiagnosticsLog гасит IOException при Dispose")]
    public void BotDiagnosticsLog_Dispose_SwallowsIoException()
    {
        var writer = new StreamWriter(new ThrowingDisposeStream(objectDisposed: false));
        var log = CreateBotDiagnosticsLog(writer, "dispose-io.log");

        log.Dispose();

        Assert.True(GetPrivateField<bool>(log, "_failed"));
        Assert.True(GetPrivateField<bool>(log, "_disposed"));
    }

    [Fact(DisplayName = "BotDiagnosticsLog гасит ObjectDisposedException при Dispose")]
    public void BotDiagnosticsLog_Dispose_SwallowsObjectDisposedException()
    {
        var writer = new StreamWriter(new ThrowingDisposeStream(objectDisposed: true));
        var log = CreateBotDiagnosticsLog(writer, "dispose-od.log");

        log.Dispose();

        Assert.True(GetPrivateField<bool>(log, "_failed"));
        Assert.True(GetPrivateField<bool>(log, "_disposed"));
    }

    [Fact(DisplayName = "FormatCommand возвращает fallback для неизвестного вида команды")]
    public void CompanionBot_FormatCommand_ReturnsFallback_ForUnknownKind()
    {
        var method = typeof(CompanionBot).GetMethod("FormatCommand", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var command = (BotCommand?)new BotCommand((BotCommandKind)999, BotResourceType.Wood, 3, null);
        var result = Assert.IsType<string>(method.Invoke(null, new object?[] { command }));
        Assert.Equal("999", result);
    }

    [Fact(DisplayName = "TryRunAutoPerf c default AutoPerfRunner пишет лог FPS")]
    public void Program_TryRunAutoPerf_DefaultRunner_WritesPerfLog()
    {
        var previousPlatformFactory = Program.PlatformFactory;
        var previousConfigFactory = Program.AutoPerfConfigFactory;
        var previousWorldFactory = Program.WorldFactory;

        var platform = new FakeGamePlatform
        {
            FrameTime = 0.5f,
            Fps = 120
        };
        platform.EnqueueWindowShouldClose(false, false, false, true);

        Program.PlatformFactory = () => platform;
        Program.AutoPerfConfigFactory = () => new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        Program.WorldFactory = config => new WorldMap(width: 64, height: 24, depth: 64, chunkSize: config.ChunkSize, seed: config.WorldSeed);

        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autoperf-{Guid.NewGuid():N}");
        try
        {
            var result = Program.TryRunAutoPerf(["autoperf", outputDir, "10", "60"]);
            Assert.True(result);
        }
        finally
        {
            Program.PlatformFactory = previousPlatformFactory;
            Program.AutoPerfConfigFactory = previousConfigFactory;
            Program.WorldFactory = previousWorldFactory;
        }

        var logs = Directory.GetFiles(outputDir, "autoperf-*.log");
        Assert.Single(logs);
        var logText = File.ReadAllText(logs[0]);
        Assert.Contains("fps_min=", logText, StringComparison.Ordinal);
        Assert.Contains("fps_avg=", logText, StringComparison.Ordinal);
        Assert.Contains("result=PASS", logText, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "ReadInput корректно собирает направление и прыжок")]
    public void ReadInput_CollectsKeysAndMouse()
    {
        var platform = new FakeGamePlatform
        {
            MouseDelta = new Vector2(3.5f, -2.25f)
        };
        platform.SetDownKeys(KeyboardKey.W, KeyboardKey.D);
        platform.SetPressedKeys(KeyboardKey.Space);

        var input = GameApp.ReadInput(platform);

        Assert.Equal(1f, input.MoveForward);
        Assert.Equal(1f, input.MoveRight);
        Assert.True(input.Jump);
        Assert.Equal(3.5f, input.LookDeltaX);
        Assert.Equal(-2.25f, input.LookDeltaY);
    }

    [Fact(DisplayName = "ReadInput учитывает стрелки и противоположные клавиши")]
    public void ReadInput_CancelsOppositeDirections()
    {
        var platform = new FakeGamePlatform();
        platform.SetDownKeys(KeyboardKey.Up, KeyboardKey.Down, KeyboardKey.Left, KeyboardKey.Right);

        var input = GameApp.ReadInput(platform);

        Assert.Equal(0f, input.MoveForward);
        Assert.Equal(0f, input.MoveRight);
        Assert.False(input.Jump);
    }

    [Fact(DisplayName = "ReadInput покрывает ветки для S/Down и A/Left")]
    public void ReadInput_CoversAlternativeDirectionBranches()
    {
        var platformSAndA = new FakeGamePlatform();
        platformSAndA.SetDownKeys(KeyboardKey.S, KeyboardKey.A);
        var inputSAndA = GameApp.ReadInput(platformSAndA);
        Assert.Equal(-1f, inputSAndA.MoveForward);
        Assert.Equal(-1f, inputSAndA.MoveRight);

        var platformDownAndLeft = new FakeGamePlatform();
        platformDownAndLeft.SetDownKeys(KeyboardKey.Down, KeyboardKey.Left);
        var inputDownAndLeft = GameApp.ReadInput(platformDownAndLeft);
        Assert.Equal(-1f, inputDownAndLeft.MoveForward);
        Assert.Equal(-1f, inputDownAndLeft.MoveRight);
    }

    [Fact(DisplayName = "SelectHotbarIndex выбирает слот по цифре")]
    public void SelectHotbarIndex_ChangesByNumberKey()
    {
        var platform = new FakeGamePlatform();
        platform.SetPressedKeys(KeyboardKey.Two);

        var index = GameApp.SelectHotbarIndex(0, platform, hotbarLength: 2);

        Assert.Equal(1, index);
    }

    [Fact(DisplayName = "На старте включается fullscreen по умолчанию")]
    public void Run_EnablesFullscreenByDefault()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, true);

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.True(platform.ToggleFullscreenCalled);
        Assert.True(platform.IsFullscreen);
        Assert.True(platform.SetExitKeyCalled);
        Assert.True(platform.LoadUiFontCalled);
        Assert.True(platform.UnloadUiFontCalled);
    }

    [Fact(DisplayName = "RunAutoCapture делает два скриншота без меню и закрывает окно")]
    public void RunAutoCapture_SavesTwoScreenshots()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autocap-{Guid.NewGuid():N}");
        var platform = new FakeGamePlatform();
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 96, height: 32, depth: 96, chunkSize: config.ChunkSize, seed: config.WorldSeed);
        var app = new GameApp(config, platform, world);

        app.RunAutoCapture(outputDir);

        Assert.Equal(3, platform.SavedScreenshots.Count);
        Assert.All(platform.SavedScreenshots, screenshot => Assert.StartsWith(outputDir, screenshot, StringComparison.Ordinal));
        Assert.Contains(platform.SavedScreenshots, s => s.EndsWith("autocap-warmup.png", StringComparison.Ordinal));
        Assert.Contains(platform.SavedScreenshots, s => s.EndsWith("autocap-1.png", StringComparison.Ordinal));
        Assert.Contains(platform.SavedScreenshots, s => s.EndsWith("autocap-2.png", StringComparison.Ordinal));
        Assert.True(platform.CloseWindowCalled);
        Assert.True(platform.DisableCursorCalled);
    }

    [Fact(DisplayName = "RunAutoPerf пишет FAIL лог, если FPS ниже порога")]
    public void RunAutoPerf_WritesFailLog_WhenFpsBelowThreshold()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autoperf-{Guid.NewGuid():N}");
        var platform = new FakeGamePlatform
        {
            FrameTime = 0.5f,
            Fps = 55
        };
        platform.EnqueueWindowShouldClose(false, false, true);

        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 64, height: 24, depth: 64, chunkSize: 16, seed: 777);
        var app = new GameApp(config, platform, world);

        app.RunAutoPerf(outputDir, durationSeconds: 10f, minAllowedFps: 60);

        var logs = Directory.GetFiles(outputDir, "autoperf-*.log");
        Assert.Single(logs);
        var logText = File.ReadAllText(logs[0]);
        Assert.Contains("result=FAIL", logText, StringComparison.Ordinal);
        Assert.Contains("below_threshold_frames=", logText, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "RunAutoPerf пишет FAIL лог с нулевыми кадрами при мгновенном закрытии окна")]
    public void RunAutoPerf_WritesFailLog_WhenNoFrames()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autoperf-{Guid.NewGuid():N}");
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(true);

        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(width: 64, height: 24, depth: 64, chunkSize: 16, seed: 777));

        app.RunAutoPerf(outputDir, durationSeconds: 10f, minAllowedFps: 60);

        var logs = Directory.GetFiles(outputDir, "autoperf-*.log");
        Assert.Single(logs);
        var logText = File.ReadAllText(logs[0]);
        Assert.Contains("frames=0", logText, StringComparison.Ordinal);
        Assert.Contains("fps_min=0", logText, StringComparison.Ordinal);
        Assert.Contains("result=FAIL", logText, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "RunAutoPerf использует fallback delta при нулевом FrameTime")]
    public void RunAutoPerf_UsesFallbackDelta_WhenFrameTimeIsZero()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autoperf-{Guid.NewGuid():N}");
        var platform = new FakeGamePlatform
        {
            FrameTime = 0f,
            Fps = 120
        };
        platform.EnqueueWindowShouldClose(false, true);

        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(width: 64, height: 24, depth: 64, chunkSize: 16, seed: 777));

        app.RunAutoPerf(outputDir, durationSeconds: 10f, minAllowedFps: 60);

        var logs = Directory.GetFiles(outputDir, "autoperf-*.log");
        Assert.Single(logs);
        var logText = File.ReadAllText(logs[0]);
        Assert.Contains("frames=1", logText, StringComparison.Ordinal);
        Assert.Contains("result=PASS", logText, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "RunAutoPerf может завершиться по лимиту длительности без сигнала закрытия окна")]
    public void RunAutoPerf_CanStopByDurationLimit()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autoperf-{Guid.NewGuid():N}");
        var platform = new FakeGamePlatform
        {
            FrameTime = 1f / 20f,
            Fps = 120
        };
        platform.EnqueueWindowShouldClose(Enumerable.Repeat(false, 64).ToArray());

        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(width: 64, height: 24, depth: 64, chunkSize: 16, seed: 777));

        app.RunAutoPerf(outputDir, durationSeconds: 1f, minAllowedFps: 60);

        var logs = Directory.GetFiles(outputDir, "autoperf-*.log");
        Assert.Single(logs);
        var logText = File.ReadAllText(logs[0]);
        Assert.Contains("result=PASS", logText, StringComparison.Ordinal);
        Assert.Contains("frames=20", logText, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "RunAutoPerf учитывает warmup и пишет PASS на длинном стабильном прогоне")]
    public void RunAutoPerf_LongStableRun_UsesWarmupTrimmedMetrics()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autoperf-{Guid.NewGuid():N}");
        var platform = new FakeGamePlatform
        {
            FrameTime = 1f / 120f,
            Fps = 120
        };
        platform.EnqueueWindowShouldClose(Enumerable.Repeat(false, 180).ToArray());

        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(width: 64, height: 24, depth: 64, chunkSize: 16, seed: 777));

        app.RunAutoPerf(outputDir, durationSeconds: 2f, minAllowedFps: 60);

        var logs = Directory.GetFiles(outputDir, "autoperf-*.log");
        Assert.Single(logs);
        var logText = File.ReadAllText(logs[0]);
        Assert.Contains("warmup_frames_ignored=60", logText, StringComparison.Ordinal);
        Assert.Contains("result=PASS", logText, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "RunAutoPerf учитывает warmup и пишет FAIL на длинном нестабильном прогоне")]
    public void RunAutoPerf_LongLowFpsRun_UsesWarmupTrimmedMetrics()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autoperf-{Guid.NewGuid():N}");
        var platform = new FakeGamePlatform
        {
            FrameTime = 1f / 120f,
            Fps = 55
        };
        platform.EnqueueWindowShouldClose(Enumerable.Repeat(false, 180).ToArray());

        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(width: 64, height: 24, depth: 64, chunkSize: 16, seed: 777));

        app.RunAutoPerf(outputDir, durationSeconds: 1f, minAllowedFps: 60);

        var logs = Directory.GetFiles(outputDir, "autoperf-*.log");
        Assert.Single(logs);
        var logText = File.ReadAllText(logs[0]);
        Assert.Contains("warmup_frames_ignored=60", logText, StringComparison.Ordinal);
        Assert.Contains("result=FAIL", logText, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "ReadAutoBotInput поворачивает и прыгает при препятствии, даже при нулевой чувствительности")]
    public void ReadAutoBotInput_ObstaclePath_UsesTurnAndSensitivityFallback()
    {
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 16, seed: 0);
        world.SetBlock(16, 2, 15, BlockType.Stone);

        var player = new PlayerController(new GameConfig { MouseSensitivity = 0f }, new Vector3(16.5f, 2f, 16.5f));
        player.Update(world, new PlayerInput(0f, 0f, false, 0f, 0f), 1f / 60f);

        var app = new GameApp(new GameConfig { FullscreenByDefault = false, MouseSensitivity = 0f }, new FakeGamePlatform(), world);
        SetPrivateField(app, "_player", player);

        var method = typeof(GameApp).GetMethod("ReadAutoBotInput", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var state = CreateAutoBotState(lastPosition: player.Position, stuckTime: 0f, turnSign: 1, turnLockTime: 0f, wanderPhase: 0f);
        object[] args = [0.016f, state];
        var input = (PlayerInput)method!.Invoke(app, args)!;
        var updatedState = args[1];

        Assert.True(float.IsFinite(input.LookDeltaX));
        Assert.True(input.MoveRight != 0f);
        Assert.True(input.Jump);
        Assert.Equal(-1, (int)GetAutoBotField(updatedState, "TurnSign"));
        Assert.True((float)GetAutoBotField(updatedState, "TurnLockTime") > 0.35f);
    }

    [Fact(DisplayName = "ReadAutoBotInput в свободном проходе не прыгает и не форсирует разворот")]
    public void ReadAutoBotInput_FreePath_DoesNotForceTurn()
    {
        var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 16, seed: 0);
        var player = new PlayerController(new GameConfig(), new Vector3(16.5f, 2f, 16.5f));
        player.Update(world, new PlayerInput(0f, 0f, false, 0f, 0f), 1f / 60f);

        var app = new GameApp(new GameConfig { FullscreenByDefault = false }, new FakeGamePlatform(), world);
        SetPrivateField(app, "_player", player);

        var method = typeof(GameApp).GetMethod("ReadAutoBotInput", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var state = CreateAutoBotState(lastPosition: player.Position + new Vector3(1f, 0f, 0f), stuckTime: 0.2f, turnSign: 1, turnLockTime: 0.2f, wanderPhase: 0f);
        object[] args = [0.1f, state];
        var input = (PlayerInput)method!.Invoke(app, args)!;
        var updatedState = args[1];

        Assert.False(input.Jump);
        Assert.InRange(MathF.Abs(input.MoveRight), 0f, 0.35f);
        Assert.InRange((float)GetAutoBotField(updatedState, "StuckTime"), 0f, 0.0001f);
        Assert.InRange((float)GetAutoBotField(updatedState, "TurnLockTime"), 0.09f, 0.11f);
    }

    [Fact(DisplayName = "ToHorizontalForward имеет fallback для вертикального направления")]
    public void ToHorizontalForward_UsesFallbackForVerticalDirection()
    {
        var method = typeof(GameApp).GetMethod("ToHorizontalForward", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var vertical = (Vector3)method!.Invoke(null, [new Vector3(0f, 1f, 0f)])!;
        Assert.Equal(Vector3.UnitZ, vertical);

        var regular = (Vector3)method.Invoke(null, [new Vector3(3f, 0f, 4f)])!;
        Assert.True(MathF.Abs(regular.Length() - 1f) < 0.001f);
        Assert.True(regular.X > 0f && regular.Z > 0f);
    }

    [Fact(DisplayName = "DrawWorld в низком качестве покрывает ветку low и цвет дерева")]
    public void DrawWorld_LowQuality_CoversLowBoundsAndWoodColor()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.Low
        };
        var world = new WorldMap(width: 8, height: 8, depth: 8, chunkSize: 8, seed: 0);

        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        world.SetBlock(4, 2, 4, BlockType.Wood);

        var platform = new FakeGamePlatform();
        var app = new GameApp(config, platform, world);
        SetPrivateField(app, "_player", new PlayerController(config, new Vector3(4.5f, 2.2f, 4.5f)));

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.True(platform.DrawCubeCalls > 0);
    }

    [Fact(DisplayName = "DrawWorld на высоте продолжает рисовать поверхность и деревья внизу")]
    public void DrawWorld_HighAltitude_StillRendersGroundAndTrees()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 96, height: 72, depth: 96, chunkSize: 16, seed: 777);
        var centerX = 48;
        var centerZ = 48;
        var topY = world.GetTerrainTopY(centerX, centerZ);

        var platform = new FakeGamePlatform();
        var app = new GameApp(config, platform, world);
        SetPrivateField(app, "_player", new PlayerController(config, new Vector3(centerX + 0.5f, topY + 28f, centerZ + 0.5f)));

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.True(platform.DrawCubeCalls > 0);
    }

    [Fact(DisplayName = "DrawWorld в дефолтном большом мире рисует геометрию возле спавна")]
    public void DrawWorld_DefaultLargeWorld_HasVisibleGeometryNearSpawn()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 2304, height: 72, depth: 2304, chunkSize: config.ChunkSize, seed: config.WorldSeed);
        var platform = new FakeGamePlatform { Fps = 120, FrameTime = 1f / 60f };
        var app = new GameApp(config, platform, world);

        var createSpawn = typeof(GameApp).GetMethod("CreateSpawnPosition", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(createSpawn);
        var spawn = (Vector3)createSpawn!.Invoke(app, null)!;
        var player = new PlayerController(config, spawn);
        player.SetPose(player.Position, new Vector3(0f, -0.28f, -1f));
        SetPrivateField(app, "_player", player);

        var appStateType = typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic);
        Assert.NotNull(appStateType);
        var playing = Enum.Parse(appStateType!, "Playing");
        SetPrivateField(app, "_state", playing);

        var updateStreaming = typeof(GameApp).GetMethod("UpdateWorldStreaming", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(updateStreaming);
        updateStreaming!.Invoke(app, [true]);

        var drawWorld = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(drawWorld);
        drawWorld!.Invoke(app, null);

        Assert.True(platform.DrawCubeInstancedInstances > 0, "Ожидали видимую геометрию вокруг спавна в большом мире.");
    }

    [Fact(DisplayName = "DrawWorld ранним выходом обрабатывает мир с нулевой шириной или глубиной")]
    public void DrawWorld_EarlyReturn_ForZeroWorldDimensions()
    {
        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var appZeroWidth = new GameApp(
            new GameConfig { FullscreenByDefault = false },
            new FakeGamePlatform(),
            new WorldMap(width: 0, height: 8, depth: 16, chunkSize: 8, seed: 0));
        method!.Invoke(appZeroWidth, null);

        var appZeroDepth = new GameApp(
            new GameConfig { FullscreenByDefault = false },
            new FakeGamePlatform(),
            new WorldMap(width: 16, height: 8, depth: 0, chunkSize: 8, seed: 0));
        method.Invoke(appZeroDepth, null);
    }

    [Fact(DisplayName = "DrawWorld в low качестве отсекает дальнюю листву")]
    public void DrawWorld_LowQuality_CullsFarLeaves()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.Low
        };
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        world.SetBlock(13, 2, 1, BlockType.Leaves);

        var platform = new FakeGamePlatform();
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(1.5f, 2.2f, 1.5f));
        SetPrivateField(app, "_player", player);
        world.EnsureChunksAround(player.Position, radiusInChunks: 2);
        _ = world.RebuildDirtyChunkSurfaces(player.Position, maxChunks: 16);

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.Equal(0, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "DrawWorld отрисовывает неизвестный тип блока через fallback-цвет")]
    public void DrawWorld_UnknownBlockType_UsesFallbackColorBranch()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.Medium
        };
        var world = new WorldMap(width: 8, height: 8, depth: 8, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        world.SetBlock(4, 2, 4, (BlockType)999);

        var platform = new FakeGamePlatform();
        var app = new GameApp(config, platform, world);
        SetPrivateField(app, "_player", new PlayerController(config, new Vector3(4.5f, 2.2f, 4.5f)));

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.True(platform.DrawCubeCalls > 0);
    }

    [Fact(DisplayName = "DrawWorld отсекает блоки за пределом render distance")]
    public void DrawWorld_SkipsBlocksBeyondRenderDistance()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.Low
        };
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        world.SetBlock(13, 2, 13, BlockType.Stone);

        var platform = new FakeGamePlatform();
        var app = new GameApp(config, platform, world);
        SetPrivateField(app, "_player", new PlayerController(config, new Vector3(0.5f, 2.2f, 0.5f)));

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.Equal(0, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "DrawWorld отсекает очень дальние стволы на very-far LOD")]
    public void DrawWorld_CullsVeryFarWood()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 160, height: 12, depth: 32, chunkSize: 8, seed: 0);

        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        world.SetBlock(70, 2, 9, BlockType.Wood);

        var platform = new FakeGamePlatform();
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.2f, 9.5f));
        player.SetPose(player.Position, new Vector3(1f, 0f, 0f));
        SetPrivateField(app, "_player", player);
        var appStateType = typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic);
        Assert.NotNull(appStateType);
        SetPrivateField(app, "_state", Enum.Parse(appStateType!, "Playing"));
        world.EnsureChunksAround(player.Position, radiusInChunks: 20);
        _ = world.RebuildDirtyChunkSurfaces(player.Position, maxChunks: 300);

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.Equal(0, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "DrawWorld отсекает нетеррейн-блок на ultra-far LOD")]
    public void DrawWorld_CullsUltraFarNonTerrainBlock()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 192, height: 12, depth: 32, chunkSize: 8, seed: 0);

        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        world.SetBlock(90, 2, 10, (BlockType)999);

        var platform = new FakeGamePlatform();
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.2f, 9.5f));
        player.SetPose(player.Position, new Vector3(1f, 0f, 0f));
        SetPrivateField(app, "_player", player);
        var appStateType = typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic);
        Assert.NotNull(appStateType);
        SetPrivateField(app, "_state", Enum.Parse(appStateType!, "Playing"));
        world.EnsureChunksAround(player.Position, radiusInChunks: 24);
        _ = world.RebuildDirtyChunkSurfaces(player.Position, maxChunks: 300);

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.Equal(0, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "DrawWorld отсекает ultra-far неизвестный блок при keepChance=0 после sparse-множителя")]
    public void DrawWorld_CullsUltraFarUnknownBlock_WhenSparseKeepBecomesZero()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 176, height: 12, depth: 32, chunkSize: 8, seed: 0);

        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        world.SetBlock(108, 2, 9, (BlockType)999); // дистанция ~100 блоков: keepChance становится 0 в sparse ветке

        var platform = new FakeGamePlatform
        {
            Fps = 120
        };
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.2f, 9.5f));
        player.SetPose(player.Position, new Vector3(1f, 0f, 0f));
        SetPrivateField(app, "_player", player);
        var appStateType = typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic);
        Assert.NotNull(appStateType);
        SetPrivateField(app, "_state", Enum.Parse(appStateType!, "Playing"));
        world.EnsureChunksAround(player.Position, radiusInChunks: 24);
        _ = world.RebuildDirtyChunkSurfaces(player.Position, maxChunks: 500);

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.Equal(0, platform.DrawCubeCalls);
    }

    [Fact(DisplayName = "DrawWorld применяет far-style для дальнего блока Dirt")]
    public void DrawWorld_FarStyle_CoversDirtBranch()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 128, height: 12, depth: 32, chunkSize: 8, seed: 0);

        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        // На FPS=84 adaptive High дает renderDistance=56, берём дистанцию чуть выше порога far-style.
        world.SetBlock(54, 2, 15, BlockType.Dirt);

        var platform = new FakeGamePlatform
        {
            Fps = 84
        };
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.2f, 15.5f));
        player.SetPose(player.Position, new Vector3(1f, 0f, 0f));
        SetPrivateField(app, "_player", player);

        var appStateType = typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic);
        Assert.NotNull(appStateType);
        SetPrivateField(app, "_state", Enum.Parse(appStateType!, "Playing"));

        world.EnsureChunksAround(player.Position, radiusInChunks: 10);
        _ = world.RebuildDirtyChunkSurfaces(player.Position, maxChunks: 400);
        Assert.True(world.TryGetChunkSurfaceBlocks(6, 1, out var targetChunkSurface));
        Assert.Contains(targetChunkSurface, s => s.X == 54 && s.Y == 2 && s.Z == 15 && s.Block == BlockType.Dirt);

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.True(platform.DrawCubeCalls > 0);
    }

    [Fact(DisplayName = "DrawWorld покрывает false-ветку far-style для дальнего блока Wood при адаптивной дистанции")]
    public void DrawWorld_FarStyle_CoversNonTerrainBranchAtAdaptiveDistance()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 64, height: 12, depth: 32, chunkSize: 8, seed: 0);

        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        world.SetBlock(36, 2, 15, BlockType.Wood);

        var platform = new FakeGamePlatform
        {
            Fps = 66 // High-профиль адаптируется до RenderDistance=34
        };
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.2f, 15.5f));
        player.SetPose(player.Position, new Vector3(1f, 0f, 0f));
        SetPrivateField(app, "_player", player);

        var appStateType = typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic);
        Assert.NotNull(appStateType);
        SetPrivateField(app, "_state", Enum.Parse(appStateType!, "Playing"));

        world.EnsureChunksAround(player.Position, radiusInChunks: 6);
        _ = world.RebuildDirtyChunkSurfaces(player.Position, maxChunks: 200);

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.True(platform.DrawCubeCalls > 0);
    }

    [Fact(DisplayName = "DrawWorld на FPS~84 не включает sparse-culling very-far (без рваной полосы)")]
    public void DrawWorld_AdaptiveHigh84_DoesNotCullVeryFarWood()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 128, height: 12, depth: 32, chunkSize: 8, seed: 0);

        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        // Дистанция около 52 блоков: при renderDistance=56 это зона very-far.
        // На FPS=84 sparse-culling должен быть выключен, иначе тут будет резкий pop-in сеткой.
        world.SetBlock(60, 2, 9, BlockType.Wood);

        var platform = new FakeGamePlatform
        {
            Fps = 84
        };
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.2f, 9.5f));
        player.SetPose(player.Position, new Vector3(1f, 0f, 0f));
        SetPrivateField(app, "_player", player);

        var appStateType = typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic);
        Assert.NotNull(appStateType);
        SetPrivateField(app, "_state", Enum.Parse(appStateType!, "Playing"));

        world.EnsureChunksAround(player.Position, radiusInChunks: 12);
        _ = world.RebuildDirtyChunkSurfaces(player.Position, maxChunks: 400);

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.True(platform.DrawCubeCalls > 0);
    }

    [Fact(DisplayName = "DrawWorld использует инстансинг кубов для world-геометрии")]
    public void DrawWorld_UsesCubeInstancing_ForWorldGeometry()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 32, height: 12, depth: 32, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        world.SetBlock(12, 2, 12, BlockType.Stone);
        world.SetBlock(13, 2, 12, BlockType.Dirt);
        world.SetBlock(14, 2, 12, BlockType.Grass);

        var platform = new FakeGamePlatform { Fps = 84 };
        var app = new GameApp(config, platform, world);
        SetPrivateField(app, "_player", new PlayerController(config, new Vector3(12.5f, 2.2f, 12.5f)));
        world.EnsureChunksAround(new Vector3(12.5f, 2.2f, 12.5f), radiusInChunks: 2);
        _ = world.RebuildDirtyChunkSurfaces(new Vector3(12.5f, 2.2f, 12.5f), maxChunks: 32);

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.True(platform.DrawCubeInstancedCalls > 0);
        Assert.True(platform.DrawCubeInstancedInstances > 0);
        Assert.True(platform.DrawCubeCalls >= platform.DrawCubeInstancedInstances);
    }

    [Fact(DisplayName = "LOD кроссфейд near/mid/far возвращает нормализованные веса")]
    public void LodBlendWeights_AreNormalizedAcrossTransitions()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(width: 64, height: 8, depth: 64, chunkSize: 8, seed: 0));
        var type = typeof(GameApp);
        var profileMethod = type.GetMethod("GetLodTransitionProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        var weightsMethod = type.GetMethod("GetLodBlendWeights", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(profileMethod);
        Assert.NotNull(weightsMethod);

        var profile = ((float NearDistance, float MidDistance, float BlendBand))profileMethod!.Invoke(app, [56f])!;
        var nearDistance = profile.NearDistance;
        var midDistance = profile.MidDistance;
        var blendBand = profile.BlendBand;

        static (float Near, float Mid, float Far) ReadWeights(object weights)
        {
            var t = weights.GetType();
            return (
                (float)t.GetProperty("Near")!.GetValue(weights)!,
                (float)t.GetProperty("Mid")!.GetValue(weights)!,
                (float)t.GetProperty("Far")!.GetValue(weights)!);
        }

        var nearWeights = ReadWeights(weightsMethod!.Invoke(null, [Math.Max(0f, nearDistance - blendBand * 2f), nearDistance, midDistance, blendBand])!);
        var midWeights = ReadWeights(weightsMethod.Invoke(null, [midDistance, nearDistance, midDistance, blendBand])!);
        var farWeights = ReadWeights(weightsMethod.Invoke(null, [midDistance + blendBand * 2f, nearDistance, midDistance, blendBand])!);

        Assert.True(nearWeights.Near > nearWeights.Mid);
        Assert.True(midWeights.Mid >= midWeights.Near);
        Assert.True(farWeights.Far > farWeights.Mid);

        var nearSum = nearWeights.Near + nearWeights.Mid + nearWeights.Far;
        var midSum = midWeights.Near + midWeights.Mid + midWeights.Far;
        var farSum = farWeights.Near + farWeights.Mid + farWeights.Far;
        Assert.InRange(nearSum, 0.999f, 1.001f);
        Assert.InRange(midSum, 0.999f, 1.001f);
        Assert.InRange(farSum, 0.999f, 1.001f);
    }

    [Fact(DisplayName = "Бюджеты стриминга и пересборки меняются по quality-профилю")]
    public void StreamingBudgets_FollowGraphicsQuality()
    {
        static (int Surface, int SurfaceBurst, int Chunk, int ChunkBurst, int WarmupChunks, int UnloadHysteresis, int PrefetchBudget, int PrefetchBudgetPressure, int PrefetchRadius, float PrefetchDistance) ReadBudgets(GameApp app)
        {
            var type = typeof(GameApp);
            var surface = (int)type.GetMethod("GetSurfaceRebuildBudget", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null)!;
            var surfaceBurst = (int)type.GetMethod("GetSurfaceBurstBudget", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [2])!;
            var chunk = (int)type.GetMethod("GetChunkLoadBudget", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null)!;
            var chunkBurst = (int)type.GetMethod("GetChunkLoadBurstBudget", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [2])!;
            var warmupChunks = (int)type.GetMethod("GetStreamingWarmupChunks", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null)!;
            var unloadHysteresis = (int)type.GetMethod("GetUnloadHysteresisChunks", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null)!;
            var prefetchBudget = (int)type.GetMethod("GetForwardPrefetchBudget", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [false])!;
            var prefetchBudgetPressure = (int)type.GetMethod("GetForwardPrefetchBudget", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [true])!;
            var prefetchRadius = (int)type.GetMethod("GetForwardPrefetchRadius", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [8])!;
            var prefetchDistance = (float)type.GetMethod("GetForwardPrefetchDistanceBlocks", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null)!;
            return (surface, surfaceBurst, chunk, chunkBurst, warmupChunks, unloadHysteresis, prefetchBudget, prefetchBudgetPressure, prefetchRadius, prefetchDistance);
        }

        var low = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low },
            new FakeGamePlatform(),
            new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0));
        var medium = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Medium },
            new FakeGamePlatform(),
            new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0));
        var high = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0));

        var lowBudgets = ReadBudgets(low);
        Assert.Equal((1, 5, 1, 5, 1, 2, 1, 0, 3), (lowBudgets.Surface, lowBudgets.SurfaceBurst, lowBudgets.Chunk, lowBudgets.ChunkBurst, lowBudgets.WarmupChunks, lowBudgets.UnloadHysteresis, lowBudgets.PrefetchBudget, lowBudgets.PrefetchBudgetPressure, lowBudgets.PrefetchRadius));
        Assert.InRange(lowBudgets.PrefetchDistance, 9.99f, 10.01f);

        var mediumBudgets = ReadBudgets(medium);
        Assert.Equal((2, 7, 2, 6, 2, 2, 2, 1, 4), (mediumBudgets.Surface, mediumBudgets.SurfaceBurst, mediumBudgets.Chunk, mediumBudgets.ChunkBurst, mediumBudgets.WarmupChunks, mediumBudgets.UnloadHysteresis, mediumBudgets.PrefetchBudget, mediumBudgets.PrefetchBudgetPressure, mediumBudgets.PrefetchRadius));
        Assert.InRange(mediumBudgets.PrefetchDistance, 12.79f, 12.81f);

        var highBudgets = ReadBudgets(high);
        Assert.Equal((3, 7, 3, 7, 2, 2, 3, 1, 5), (highBudgets.Surface, highBudgets.SurfaceBurst, highBudgets.Chunk, highBudgets.ChunkBurst, highBudgets.WarmupChunks, highBudgets.UnloadHysteresis, highBudgets.PrefetchBudget, highBudgets.PrefetchBudgetPressure, highBudgets.PrefetchRadius));
        Assert.InRange(highBudgets.PrefetchDistance, 15.99f, 16.01f);
    }

    [Fact(DisplayName = "Adaptive render distance повышается плавно при росте FPS")]
    public void AdaptiveRenderDistance_GrowsSmoothly_WhenFpsRises()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(width: 32, height: 8, depth: 32, chunkSize: 8, seed: 0));

        var method = typeof(GameApp).GetMethod("GetAdaptiveRenderDistance", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var start = (int)method!.Invoke(app, [66, true, true])!;
        var next = (int)method.Invoke(app, [84, true, true])!;

        Assert.True(next - start <= 2, $"Ожидали мягкий рост дальности, но получили {start} -> {next}");

        var later = next;
        for (var i = 0; i < 20; i++)
        {
            later = (int)method.Invoke(app, [84, true, true])!;
        }

        Assert.True(later > next);
        Assert.True(later <= 56);
    }

    [Fact(DisplayName = "Forward prefetch подгружает чанки по направлению взгляда")]
    public void EnsureForwardChunksBudgeted_LoadsAhead()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 128, height: 8, depth: 128, chunkSize: 8, seed: 0);
        var app = new GameApp(config, new FakeGamePlatform(), world);
        var player = new PlayerController(config, new Vector3(32.5f, 2.2f, 32.5f));
        player.SetPose(player.Position, new Vector3(1f, 0f, 0f));
        SetPrivateField(app, "_player", player);

        var method = typeof(GameApp).GetMethod("EnsureForwardChunksBudgeted", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var before = world.LoadedChunkCount;
        method!.Invoke(app, [player.Position, 8, 0, false]);
        Assert.Equal(before, world.LoadedChunkCount);

        method!.Invoke(app, [player.Position, 8, 200, false]);
        var afterSync = world.LoadedChunkCount;

        Assert.True(afterSync > before);
        Assert.True(world.IsChunkLoaded(8, 4));

        method.Invoke(app, [player.Position, 8, 24, true]);
        for (var i = 0; i < 80 && world.LoadedChunkCount == afterSync; i++)
        {
            _ = world.ApplyBackgroundStreamingResults(maxChunkApplies: 8, maxSurfaceApplies: 8);
            Thread.Sleep(5);
        }

        var afterAsync = world.LoadedChunkCount;
        Assert.True(afterAsync >= afterSync);
        Assert.True(world.IsChunkLoaded(8, 4));
    }

    [Fact(DisplayName = "Хелперы fade-LOD возвращают ожидаемые значения на границах")]
    public void FadeHelpers_ReturnExpectedBounds()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(width: 32, height: 8, depth: 32, chunkSize: 8, seed: 0));
        var type = typeof(GameApp);

        var getEdgeKeep = type.GetMethod("GetDistanceEdgeKeep", BindingFlags.Instance | BindingFlags.NonPublic);
        var getFoliageKeep = type.GetMethod("GetFoliageKeepChance", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(getEdgeKeep);
        Assert.NotNull(getFoliageKeep);

        var edgeNear = (float)getEdgeKeep!.Invoke(app, [10f, 56f, 5.5f])!;
        var edgeMid = (float)getEdgeKeep.Invoke(app, [56f, 56f, 5.5f])!;
        var edgeFar = (float)getEdgeKeep.Invoke(app, [65f, 56f, 5.5f])!;
        Assert.Equal(1f, edgeNear);
        Assert.InRange(edgeMid, 0.35f, 0.65f);
        Assert.Equal(0f, edgeFar);

        var foliageNear = (float)getFoliageKeep!.Invoke(app, [12f, 24f, 4.5f])!;
        var foliageMid = (float)getFoliageKeep.Invoke(app, [26f, 24f, 4.5f])!;
        var foliageFar = (float)getFoliageKeep.Invoke(app, [30f, 24f, 4.5f])!;
        Assert.Equal(1f, foliageNear);
        Assert.InRange(foliageMid, 0.25f, 0.75f);
        Assert.Equal(0f, foliageFar);
    }

    [Fact(DisplayName = "Sparse keep chance для неизвестных блоков падает до нуля на ultra-far")]
    public void SparseFarKeepChance_DropsToZero_ForUnknownBlock()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(width: 64, height: 8, depth: 64, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("GetSparseFarKeepChance", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var chance = (float)method!.Invoke(app, [(BlockType)999, 96f, 48f, 72f, 100f])!;
        Assert.Equal(0f, chance);
    }

    [Fact(DisplayName = "Sparse keep chance покрывает ветки для Leaves и terrain-блоков")]
    public void SparseFarKeepChance_CoversLeavesAndTerrainBranches()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(width: 64, height: 8, depth: 64, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("GetSparseFarKeepChance", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var leavesChance = (float)method!.Invoke(app, [BlockType.Leaves, 92f, 48f, 72f, 100f])!;
        var terrainChance = (float)method.Invoke(app, [BlockType.Stone, 92f, 48f, 72f, 100f])!;

        Assert.InRange(leavesChance, 0f, 1f);
        Assert.InRange(terrainChance, 0f, 1f);
    }

    [Fact(DisplayName = "PassSpatialDither покрывает границы keepChance и средний диапазон")]
    public void PassSpatialDither_CoversBoundsAndMidRange()
    {
        var passMethod = typeof(GameApp).GetMethod("PassSpatialDither", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(passMethod);

        var alwaysDrop = (bool)passMethod!.Invoke(null, [1, 2, 3, 17, 0f])!;
        var alwaysKeep = (bool)passMethod.Invoke(null, [1, 2, 3, 17, 1f])!;
        Assert.False(alwaysDrop);
        Assert.True(alwaysKeep);

        var sawKeep = false;
        var sawDrop = false;
        for (var i = 0; i < 64; i++)
        {
            var keep = (bool)passMethod.Invoke(null, [i, i + 1, i + 2, 73, 0.5f])!;
            sawKeep |= keep;
            sawDrop |= !keep;
            if (sawKeep && sawDrop)
            {
                break;
            }
        }

        Assert.True(sawKeep);
        Assert.True(sawDrop);
    }

    [Fact(DisplayName = "Chunk reveal и очистка кэша покрывают временные ветки")]
    public void ChunkReveal_AndCleanup_CoverBranches()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(width: 64, height: 8, depth: 64, chunkSize: 8, seed: 0));
        var type = typeof(GameApp);
        var getReveal = type.GetMethod("GetChunkRevealFactor", BindingFlags.Instance | BindingFlags.NonPublic);
        var cleanup = type.GetMethod("CleanupChunkRevealCache", BindingFlags.Instance | BindingFlags.NonPublic);
        var advance = type.GetMethod("AdvanceRuntime", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(getReveal);
        Assert.NotNull(cleanup);
        Assert.NotNull(advance);

        var t0 = (float)getReveal!.Invoke(app, [5, 6])!;
        Assert.InRange(t0, 0f, 0.01f);

        advance!.Invoke(app, [0.2f]);
        var t1 = (float)getReveal.Invoke(app, [5, 6])!;
        Assert.True(t1 > t0);

        cleanup!.Invoke(app, [5, 6, 0]);
        var dict = (IReadOnlyDictionary<(int ChunkX, int ChunkZ), float>)type.GetField("_chunkRevealStartedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        Assert.Contains((5, 6), dict.Keys);

        cleanup.Invoke(app, [0, 0, 0]);
        dict = (IReadOnlyDictionary<(int ChunkX, int ChunkZ), float>)type.GetField("_chunkRevealStartedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        Assert.DoesNotContain((5, 6), dict.Keys);
    }

    [Fact(DisplayName = "CreateSpawnPosition выбирает безопасную колонку, если центр занят блоками")]
    public void CreateSpawnPosition_AvoidsBlockedCenter()
    {
        var config = new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High };
        var world = new WorldMap(width: 48, height: 24, depth: 48, chunkSize: 16, seed: 0);
        var centerX = world.Width / 2;
        var centerZ = world.Depth / 2;

        world.SetBlock(centerX, 4, centerZ, BlockType.Wood);
        world.SetBlock(centerX + 1, 4, centerZ, BlockType.Leaves);
        world.SetBlock(centerX, 4, centerZ + 1, BlockType.Leaves);

        var app = new GameApp(config, new FakeGamePlatform(), world);
        var createSpawn = typeof(GameApp).GetMethod("CreateSpawnPosition", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(createSpawn);
        var spawn = (Vector3)createSpawn!.Invoke(app, null)!;

        var isPoseClear = typeof(GameApp).GetMethod("IsPlayerPoseClear", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(isPoseClear);
        var clear = (bool)isPoseClear!.Invoke(app, [spawn])!;
        Assert.True(clear);
        Assert.True(MathF.Abs(spawn.X - (centerX + 0.5f)) > 0.001f || MathF.Abs(spawn.Z - (centerZ + 0.5f)) > 0.001f);
    }

    [Fact(DisplayName = "LiftPoseAboveTerrain поднимает камеру выше верхнего твёрдого блока")]
    public void LiftPoseAboveTerrain_UsesTopSolidHeight()
    {
        var config = new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High };
        var world = new WorldMap(width: 32, height: 24, depth: 32, chunkSize: 16, seed: 0);
        world.SetBlock(10, 9, 10, BlockType.Wood);

        var app = new GameApp(config, new FakeGamePlatform(), world);
        var lift = typeof(GameApp).GetMethod("LiftPoseAboveTerrain", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(lift);

        var lifted = (Vector3)lift!.Invoke(app, [new Vector3(10.5f, 2f, 10.5f)])!;
        var liftedX = (int)MathF.Floor(lifted.X);
        var liftedZ = (int)MathF.Floor(lifted.Z);
        Assert.True(world.GetTopSolidY(liftedX, liftedZ) <= world.GetTerrainTopY(liftedX, liftedZ) + 1);
        Assert.True(lifted.Y >= world.GetTerrainTopY(liftedX, liftedZ) + 1.19f);
    }

    [Fact(DisplayName = "LiftPoseAboveTerrain использует fallback-колонку, если свободная не найдена")]
    public void LiftPoseAboveTerrain_UsesFallbackColumn_WhenNoTreeFreeColumn()
    {
        var config = new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High };
        var world = new WorldMap(width: 8, height: 16, depth: 8, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 6, z, BlockType.Wood);
            }
        }

        var app = new GameApp(config, new FakeGamePlatform(), world);
        var lift = typeof(GameApp).GetMethod("LiftPoseAboveTerrain", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(lift);

        var input = new Vector3(3.4f, 0.1f, 3.9f);
        var lifted = (Vector3)lift!.Invoke(app, [input])!;

        Assert.Equal(3.5f, lifted.X);
        Assert.Equal(3.5f, lifted.Z);
        Assert.InRange(lifted.Y, 7.19f, 7.21f);
    }

    [Fact(DisplayName = "TryFindTreeFreeColumn покрывает границы и false-ветку при полностью занятых колонках")]
    public void TryFindTreeFreeColumn_CoversBoundsAndFalseBranch()
    {
        var world = new WorldMap(width: 2, height: 12, depth: 2, chunkSize: 2, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 5, z, BlockType.Wood);
            }
        }

        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            world);

        var method = typeof(GameApp).GetMethod("TryFindTreeFreeColumn", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        object[] args = [0, 0, 2, 0, 0];
        var ok = (bool)method!.Invoke(app, args)!;

        Assert.False(ok);
        Assert.Equal(0, (int)args[3]);
        Assert.Equal(0, (int)args[4]);
    }

    [Fact(DisplayName = "BuildGroundLookDirection использует fallback при невалидных сэмплах")]
    public void BuildGroundLookDirection_UsesFallback_WhenAllSamplesAreRejected()
    {
        var world = new WorldMap(width: 1, height: 8, depth: 1, chunkSize: 1, seed: 0);
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            world);

        var method = typeof(GameApp).GetMethod("BuildGroundLookDirection", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var fallbackByZeroVector = (Vector3)method!.Invoke(app, [new Vector3(0.5f, -0.056f, 0.5f), Vector3.UnitZ])!;
        Assert.True(fallbackByZeroVector.Y < -0.1f);
        Assert.True(fallbackByZeroVector.Z > 0.5f);

        var fallbackByHighTarget = (Vector3)method.Invoke(app, [new Vector3(0.5f, 0f, 0.5f), Vector3.UnitZ])!;
        Assert.True(fallbackByHighTarget.Y < -0.1f);
        Assert.True(fallbackByHighTarget.Z > 0.5f);
    }

    [Fact(DisplayName = "AdvanceRuntime использует fallback delta при нулевом и отрицательном шаге")]
    public void AdvanceRuntime_UsesFallbackDelta_ForNonPositiveInput()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(width: 32, height: 8, depth: 32, chunkSize: 8, seed: 0));
        var type = typeof(GameApp);
        var advance = type.GetMethod("AdvanceRuntime", BindingFlags.Instance | BindingFlags.NonPublic);
        var runtimeField = type.GetField("_runtimeSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(advance);
        Assert.NotNull(runtimeField);

        runtimeField!.SetValue(app, 0f);
        advance!.Invoke(app, [0f]);
        var afterZero = (float)runtimeField.GetValue(app)!;

        advance.Invoke(app, [-1f]);
        var afterNegative = (float)runtimeField.GetValue(app)!;

        Assert.InRange(afterZero, 0.016f, 0.017f);
        Assert.InRange(afterNegative - afterZero, 0.016f, 0.017f);
    }

    [Fact(DisplayName = "RunAutoPerf пишет scene-jump метрики и сохраняет периодические кадры")]
    public void RunAutoPerf_WritesSceneMetrics_AndPeriodicCaptures()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autoperf-{Guid.NewGuid():N}");
        var platform = new FakeGamePlatform
        {
            FrameTime = 1f / 120f,
            Fps = 120
        };
        platform.EnqueueWindowShouldClose(Enumerable.Repeat(false, 260).ToArray());

        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(width: 64, height: 24, depth: 64, chunkSize: 16, seed: 777));

        app.RunAutoPerf(outputDir, durationSeconds: 1f, minAllowedFps: 60);

        var logs = Directory.GetFiles(outputDir, "autoperf-*.log");
        Assert.Single(logs);
        var logText = File.ReadAllText(logs[0]);
        Assert.Contains("scene_jump_avg=", logText, StringComparison.Ordinal);
        Assert.Contains("scene_jump_max=", logText, StringComparison.Ordinal);
        Assert.Contains("autocap_frames_saved=", logText, StringComparison.Ordinal);

        var capturesLine = File.ReadAllLines(logs[0]).Single(line => line.StartsWith("autocap_frames_saved=", StringComparison.Ordinal));
        var captures = int.Parse(capturesLine.Split('=')[1], NumberStyles.Integer, CultureInfo.InvariantCulture);
        Assert.True(captures >= 0);
        Assert.True(platform.SavedScreenshots.Count >= captures + 1);
    }

    [Fact(DisplayName = "RunAutoPerf для medium-профиля пишет interval=120")]
    public void RunAutoPerf_MediumQuality_UsesMediumCaptureInterval()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autoperf-{Guid.NewGuid():N}");
        var platform = new FakeGamePlatform
        {
            FrameTime = 1f / 120f,
            Fps = 120
        };
        platform.EnqueueWindowShouldClose(Enumerable.Repeat(false, 120).ToArray());

        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Medium },
            platform,
            new WorldMap(width: 64, height: 24, depth: 64, chunkSize: 16, seed: 777));

        app.RunAutoPerf(outputDir, durationSeconds: 1f, minAllowedFps: 60);

        var logs = Directory.GetFiles(outputDir, "autoperf-*.log");
        Assert.Single(logs);
        var logText = File.ReadAllText(logs[0]);
        Assert.Contains("autocap_interval_frames=120", logText, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "RunAutoPerf для low-профиля пишет interval=96")]
    public void RunAutoPerf_LowQuality_UsesLowCaptureInterval()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"aig-autoperf-{Guid.NewGuid():N}");
        var platform = new FakeGamePlatform
        {
            FrameTime = 1f / 120f,
            Fps = 120
        };
        platform.EnqueueWindowShouldClose(Enumerable.Repeat(false, 120).ToArray());

        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low },
            platform,
            new WorldMap(width: 64, height: 24, depth: 64, chunkSize: 16, seed: 777));

        app.RunAutoPerf(outputDir, durationSeconds: 1f, minAllowedFps: 60);

        var logs = Directory.GetFiles(outputDir, "autoperf-*.log");
        Assert.Single(logs);
        var logText = File.ReadAllText(logs[0]);
        Assert.Contains("autocap_interval_frames=96", logText, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Кнопка Начать игру запускает игровой режим")]
    public void Run_StartButton_TransitionsToPlaying()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f));

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.True(platform.DisableCursorCalled);
        Assert.True(platform.DrawCubeCalls > 0);
    }

    [Fact(DisplayName = "В режиме Playing обрабатываются хотбар, ЛКМ/ПКМ и подсветка блока")]
    public void Run_Playing_HandlesHotbarAndBlockInteractions()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            RenderDistance = 2,
            DrawBlockWires = true,
            InteractionDistance = 10f
        };

        var world = new WorldMap(width: 20, height: 12, depth: 20, chunkSize: 8, seed: 0);
        world.SetBlock(10, 5, 8, BlockType.Stone); // hit + highlight
        world.SetBlock(11, 5, 9, (BlockType)999); // default-color ветка
        world.SetBlock(9, 5, 9, BlockType.Stone);
        world.SetBlock(10, 6, 9, BlockType.Stone);
        world.SetBlock(10, 4, 9, BlockType.Stone);
        world.SetBlock(10, 5, 10, BlockType.Stone);
        // (10,5,8) остается открытым с одной стороны для проверки поздних OR-веток видимости.

        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(
            mousePosition: new Vector2(0f, 0f),
            rightMousePressed: true,
            pressedKeys: [KeyboardKey.Two]);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f));

        var app = new GameApp(config, platform, world);
        app.Run();

        Assert.False(platform.ToggleFullscreenCalled);
        Assert.True(platform.DrawCubeWiresCalls > 0);
        Assert.True(platform.DrawLineCalls > 0);
        Assert.NotEqual(BlockType.Air, world.GetBlock(11, 5, 9));
    }

    [Fact(DisplayName = "BlockCenterIntersectsPlayer корректно определяет пересечение с коллайдером игрока")]
    public void BlockCenterIntersectsPlayer_ReturnsExpectedForNearAndFarBlocks()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            RenderDistance = 4,
            InteractionDistance = 8f
        };

        var app = new GameApp(
            config,
            new FakeGamePlatform(),
            new WorldMap(width: 21, height: 12, depth: 21, chunkSize: 8, seed: 0));

        var playerField = typeof(GameApp).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(playerField);
        playerField!.SetValue(app, new PlayerController(config, new Vector3(10.5f, 5f, 10.5f)));

        var method = typeof(GameApp).GetMethod("BlockCenterIntersectsPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var intersects = (bool)method!.Invoke(app, [new Vector3(10.5f, 5.5f, 10.5f)])!;
        var misses = (bool)method.Invoke(app, [new Vector3(13.5f, 5.5f, 10.5f)])!;

        Assert.True(intersects);
        Assert.False(misses);
    }

    [Fact(DisplayName = "GameApp учитывает спутника как блокирующую сущность для установки блока и движения")]
    public void GameApp_EntityBlockingHelpers_IncludeCompanion()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            RenderDistance = 4,
            InteractionDistance = 8f
        };

        var app = new GameApp(
            config,
            new FakeGamePlatform(),
            new WorldMap(width: 21, height: 12, depth: 21, chunkSize: 8, seed: 0));

        var player = new PlayerController(config, new Vector3(10.5f, 5f, 10.5f));
        var companion = new CompanionBot(config, new Vector3(12.5f, 5f, 10.5f));

        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_companion", companion);

        var blockMethod = typeof(GameApp).GetMethod("BlockCenterIntersectsBlockingActor", BindingFlags.Instance | BindingFlags.NonPublic);
        var playerMoveMethod = typeof(GameApp).GetMethod("PlayerPoseIntersectsCompanion", BindingFlags.Instance | BindingFlags.NonPublic);
        var botMoveMethod = typeof(GameApp).GetMethod("CompanionPoseIntersectsPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(blockMethod);
        Assert.NotNull(playerMoveMethod);
        Assert.NotNull(botMoveMethod);

        Assert.True((bool)blockMethod!.Invoke(app, [new Vector3(10.5f, 5.5f, 10.5f)])!);
        Assert.True((bool)blockMethod!.Invoke(app, [new Vector3(12.5f, 5.5f, 10.5f)])!);
        Assert.False((bool)blockMethod.Invoke(app, [new Vector3(15.5f, 5.5f, 10.5f)])!);
        Assert.True((bool)playerMoveMethod!.Invoke(app, [new Vector3(12.5f, 5f, 10.5f)])!);
        Assert.True((bool)botMoveMethod!.Invoke(app, [new Vector3(10.5f, 5f, 10.5f)])!);
    }

    [Fact(DisplayName = "GameApp helper-методы блокировки корректно возвращают false без спутника")]
    public void GameApp_EntityBlockingHelpers_ReturnFalseWithoutCompanion()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            RenderDistance = 4,
            InteractionDistance = 8f
        };

        var app = new GameApp(
            config,
            new FakeGamePlatform(),
            new WorldMap(width: 21, height: 12, depth: 21, chunkSize: 8, seed: 0));

        SetPrivateField(app, "_player", new PlayerController(config, new Vector3(10.5f, 5f, 10.5f)));
        SetPrivateField(app, "_companion", (CompanionBot?)null);

        var companionBlockMethod = typeof(GameApp).GetMethod("BlockCenterIntersectsCompanion", BindingFlags.Instance | BindingFlags.NonPublic);
        var blockMethod = typeof(GameApp).GetMethod("BlockCenterIntersectsBlockingActor", BindingFlags.Instance | BindingFlags.NonPublic);
        var playerMoveMethod = typeof(GameApp).GetMethod("PlayerPoseIntersectsCompanion", BindingFlags.Instance | BindingFlags.NonPublic);
        var botMoveMethod = typeof(GameApp).GetMethod("CompanionPoseIntersectsPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(companionBlockMethod);
        Assert.NotNull(blockMethod);
        Assert.NotNull(playerMoveMethod);
        Assert.NotNull(botMoveMethod);

        Assert.False((bool)companionBlockMethod!.Invoke(app, [new Vector3(12.5f, 5.5f, 10.5f)])!);
        Assert.False((bool)blockMethod!.Invoke(app, [new Vector3(15.5f, 5.5f, 10.5f)])!);
        Assert.False((bool)playerMoveMethod!.Invoke(app, [new Vector3(12.5f, 5f, 10.5f)])!);
        Assert.False((bool)botMoveMethod!.Invoke(app, [new Vector3(12.5f, 5f, 10.5f)])!);
    }

    [Fact(DisplayName = "Подсветка блока рисуется на грани попадания, а не по центру всего блока")]
    public void Run_BlockHighlight_UsesRaycastHitCoordinates()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false },
            platform,
            new WorldMap(width: 21, height: 12, depth: 21, chunkSize: 8, seed: 0));

        var stateField = typeof(GameApp).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(stateField);
        var playingState = Enum.Parse(stateField!.FieldType, "Playing");
        stateField.SetValue(app, playingState);

        var drawHighlight = typeof(GameApp).GetMethod("DrawBlockHighlight", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(drawHighlight);
        var hit = new BlockRaycastHit(10, 5, 8, 10, 5, 9);
        var rayOrigin = new Vector3(10.5f, 5.65f, 10.5f);
        var rayDirection = Vector3.Normalize(new Vector3(0f, -0.05f, -1f));
        drawHighlight!.Invoke(app, [hit, rayOrigin, rayDirection]);

        var highlight = platform.DrawnCubeWires.FirstOrDefault(c =>
            (Math.Abs(c.Width - 0.04f) < 0.0001f)
            || (Math.Abs(c.Height - 0.04f) < 0.0001f)
            || (Math.Abs(c.Length - 0.04f) < 0.0001f));
        Assert.NotNull(highlight);
        Assert.True(MathF.Abs(highlight.Position.X - 10.5f) < 0.001f);
        Assert.True(MathF.Abs(highlight.Position.Y - 5.5f) < 0.001f);
        Assert.True(MathF.Abs(highlight.Position.Z - 9.01f) < 0.001f);
        Assert.Equal(Color.Yellow.R, highlight.Color.R);
        Assert.Equal(Color.Yellow.G, highlight.Color.G);
        Assert.Equal(Color.Yellow.B, highlight.Color.B);
    }

    [Fact(DisplayName = "Подсветка боковой грани по X использует тонкую толщину по ширине")]
    public void DrawHitFaceHighlight_XFace_UsesThinWidth()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false },
            platform,
            new WorldMap(width: 4, height: 4, depth: 4, chunkSize: 4, seed: 0));

        var method = typeof(GameApp).GetMethod("DrawHitFaceHighlight", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var hit = new BlockRaycastHit(1, 1, 1, 0, 1, 1);
        method!.Invoke(app, [hit, new Vector3(1f, 0f, 0f)]);

        var highlight = platform.DrawnCubeWires.Single();
        Assert.True(MathF.Abs(highlight.Width - 0.04f) < 0.0001f);
        Assert.True(MathF.Abs(highlight.Height - 1.02f) < 0.0001f);
        Assert.True(MathF.Abs(highlight.Length - 1.02f) < 0.0001f);
    }

    [Fact(DisplayName = "TryGetHitFaceNormal отклоняет скачок previous более чем на 1 блок по любой оси")]
    public void TryGetHitFaceNormal_RejectsOutOfRangePreviousDelta()
    {
        var method = typeof(GameApp).GetMethod("TryGetHitFaceNormal", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var cases = new[]
        {
            new BlockRaycastHit(10, 10, 10, 12, 10, 10),
            new BlockRaycastHit(10, 10, 10, 10, 12, 10),
            new BlockRaycastHit(10, 10, 10, 10, 10, 12)
        };

        foreach (var testHit in cases)
        {
            object[] args = [testHit, null!];
            var ok = (bool)method!.Invoke(null, args)!;
            Assert.False(ok);
            var outNormal = (Vector3)args[1];
            Assert.Equal(Vector3.Zero, outNormal);
        }
    }

    [Fact(DisplayName = "TryGetHitFaceNormal отклоняет случаи без единственной оси смещения")]
    public void TryGetHitFaceNormal_RejectsInvalidAxisCount()
    {
        var method = typeof(GameApp).GetMethod("TryGetHitFaceNormal", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var cases = new[]
        {
            new BlockRaycastHit(10, 10, 10, 10, 10, 10), // axisCount = 0
            new BlockRaycastHit(10, 10, 10, 11, 11, 10)  // axisCount = 2
        };

        foreach (var testHit in cases)
        {
            object[] args = [testHit, null!];
            var ok = (bool)method!.Invoke(null, args)!;
            Assert.False(ok);
            Assert.Equal(Vector3.Zero, (Vector3)args[1]);
        }
    }

    [Fact(DisplayName = "TryGetHitFaceNormalFromRay определяет боковую грань по направлению луча")]
    public void TryGetHitFaceNormalFromRay_ResolvesSideFace()
    {
        var method = typeof(GameApp).GetMethod("TryGetHitFaceNormalFromRay", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        object[] args =
        [
            new Vector3(10.5f, 5.6f, 9.6f),
            new Vector3(0f, 0f, -1f),
            new BlockRaycastHit(10, 5, 8, 10, 6, 8),
            null!
        ];

        var ok = (bool)method!.Invoke(null, args)!;
        Assert.True(ok);
        Assert.Equal(new Vector3(0f, 0f, 1f), (Vector3)args[3]);
    }

    [Fact(DisplayName = "TryGetHitFaceNormalFromRay отклоняет нулевое направление луча")]
    public void TryGetHitFaceNormalFromRay_RejectsZeroDirection()
    {
        var method = typeof(GameApp).GetMethod("TryGetHitFaceNormalFromRay", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        object[] args =
        [
            new Vector3(10.5f, 5.6f, 9.6f),
            Vector3.Zero,
            new BlockRaycastHit(10, 5, 8, 10, 5, 9),
            null!
        ];

        var ok = (bool)method!.Invoke(null, args)!;
        Assert.False(ok);
        Assert.Equal(Vector3.Zero, (Vector3)args[3]);
    }

    [Fact(DisplayName = "TryGetHitFaceNormalFromRay выбирает грани и знак нормали по оси луча")]
    public void TryGetHitFaceNormalFromRay_ResolvesAxisFacesAndSigns()
    {
        var method = typeof(GameApp).GetMethod("TryGetHitFaceNormalFromRay", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        object[] xArgs =
        [
            new Vector3(12f, 5.5f, 8.5f),
            new Vector3(-1f, 0f, 0f),
            new BlockRaycastHit(10, 5, 8, 11, 5, 8),
            null!
        ];
        var xOk = (bool)method!.Invoke(null, xArgs)!;
        Assert.True(xOk);
        Assert.Equal(new Vector3(1f, 0f, 0f), (Vector3)xArgs[3]);

        object[] xPosArgs =
        [
            new Vector3(9f, 5.5f, 8.5f),
            new Vector3(1f, 0f, 0f),
            new BlockRaycastHit(10, 5, 8, 9, 5, 8),
            null!
        ];
        var xPosOk = (bool)method.Invoke(null, xPosArgs)!;
        Assert.True(xPosOk);
        Assert.Equal(new Vector3(-1f, 0f, 0f), (Vector3)xPosArgs[3]);

        object[] yArgs =
        [
            new Vector3(10.5f, 8f, 8.5f),
            new Vector3(0f, -1f, 0f),
            new BlockRaycastHit(10, 5, 8, 10, 6, 8),
            null!
        ];
        var yOk = (bool)method.Invoke(null, yArgs)!;
        Assert.True(yOk);
        Assert.Equal(new Vector3(0f, 1f, 0f), (Vector3)yArgs[3]);

        object[] yPosArgs =
        [
            new Vector3(10.5f, 4f, 8.5f),
            new Vector3(0f, 1f, 0f),
            new BlockRaycastHit(10, 5, 8, 10, 4, 8),
            null!
        ];
        var yPosOk = (bool)method.Invoke(null, yPosArgs)!;
        Assert.True(yPosOk);
        Assert.Equal(new Vector3(0f, -1f, 0f), (Vector3)yPosArgs[3]);

        object[] zPosArgs =
        [
            new Vector3(10.5f, 5.5f, 7f),
            new Vector3(0f, 0f, 1f),
            new BlockRaycastHit(10, 5, 8, 10, 5, 7),
            null!
        ];
        var zPosOk = (bool)method.Invoke(null, zPosArgs)!;
        Assert.True(zPosOk);
        Assert.Equal(new Vector3(0f, 0f, -1f), (Vector3)zPosArgs[3]);
    }

    [Fact(DisplayName = "TryGetHitFaceNormalFromRay отклоняет луч внутри блока и промах по оси")]
    public void TryGetHitFaceNormalFromRay_RejectsInsideAndAxisMiss()
    {
        var method = typeof(GameApp).GetMethod("TryGetHitFaceNormalFromRay", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        object[] insideArgs =
        [
            new Vector3(10.5f, 5.5f, 8.5f),
            new Vector3(1f, 0f, 0f),
            new BlockRaycastHit(10, 5, 8, 9, 5, 8),
            null!
        ];
        var insideOk = (bool)method!.Invoke(null, insideArgs)!;
        Assert.False(insideOk);
        Assert.Equal(Vector3.Zero, (Vector3)insideArgs[3]);

        object[] missArgs =
        [
            new Vector3(12f, 9f, 8.5f),
            new Vector3(-1f, 0f, 0f),
            new BlockRaycastHit(10, 5, 8, 11, 5, 8),
            null!
        ];
        var missOk = (bool)method.Invoke(null, missArgs)!;
        Assert.False(missOk);
        Assert.Equal(Vector3.Zero, (Vector3)missArgs[3]);

        object[] disjointIntervalsArgs =
        [
            new Vector3(12f, 9f, 8.5f),
            new Vector3(-1f, -1f, 0f),
            new BlockRaycastHit(10, 5, 8, 11, 6, 8),
            null!
        ];
        var disjointOk = (bool)method.Invoke(null, disjointIntervalsArgs)!;
        Assert.False(disjointOk);
        Assert.Equal(Vector3.Zero, (Vector3)disjointIntervalsArgs[3]);
    }

    [Fact(DisplayName = "TryAxis покрывает ветки без направления и инверсию near/far")]
    public void TryAxis_CoversZeroDirectionAndSwapBranches()
    {
        var method = typeof(GameApp).GetMethod("TryAxis", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        object[] insideArgs = [0.5f, 0f, 0f, 1f, 0f, 1f, 0f, false];
        var insideOk = (bool)method!.Invoke(null, insideArgs)!;
        Assert.True(insideOk);
        Assert.False((bool)insideArgs[7]);

        object[] outsideArgs = [2f, 0f, 0f, 1f, 0f, 1f, 0f, false];
        var outsideOk = (bool)method.Invoke(null, outsideArgs)!;
        Assert.False(outsideOk);
        Assert.False((bool)outsideArgs[7]);

        object[] belowMinArgs = [-1f, 0f, 0f, 1f, 0f, 1f, 0f, false];
        var belowMinOk = (bool)method.Invoke(null, belowMinArgs)!;
        Assert.False(belowMinOk);
        Assert.False((bool)belowMinArgs[7]);

        object[] swapArgs = [2f, -1f, 0f, 1f, 0f, 10f, 0f, false];
        var swapOk = (bool)method.Invoke(null, swapArgs)!;
        Assert.True(swapOk);
        Assert.True((bool)swapArgs[7]);
        Assert.InRange((float)swapArgs[6], 0.9f, 1.1f);
    }

    [Fact(DisplayName = "DrawBlockHighlight берёт грань из луча, если previous указывает неверно")]
    public void DrawBlockHighlight_PrefersRayFaceOverPreviousFace()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false },
            platform,
            new WorldMap(width: 16, height: 16, depth: 16, chunkSize: 8, seed: 0));

        var stateType = typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic);
        Assert.NotNull(stateType);
        var playingState = Enum.Parse(stateType!, "Playing");
        SetPrivateField(app, "_state", playingState);

        var method = typeof(GameApp).GetMethod("DrawBlockHighlight", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var hit = new BlockRaycastHit(10, 5, 8, 10, 6, 8); // previous указывает на верхнюю грань
        method!.Invoke(app, [(BlockRaycastHit?)hit, new Vector3(10.5f, 5.6f, 9.6f), new Vector3(0f, 0f, -1f)]);

        var highlight = platform.DrawnCubeWires.Single();
        Assert.True(MathF.Abs(highlight.Width - 1.02f) < 0.0001f);
        Assert.True(MathF.Abs(highlight.Height - 1.02f) < 0.0001f);
        Assert.True(MathF.Abs(highlight.Length - 0.04f) < 0.0001f);
        Assert.True(MathF.Abs(highlight.Position.Z - 9.01f) < 0.001f);
    }

    [Fact(DisplayName = "DrawBlockHighlight использует previous как fallback, если луч не даёт грань")]
    public void DrawBlockHighlight_UsesPreviousFallback_WhenRayCannotResolve()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false },
            platform,
            new WorldMap(width: 16, height: 16, depth: 16, chunkSize: 8, seed: 0));

        var stateType = typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic);
        Assert.NotNull(stateType);
        var playingState = Enum.Parse(stateType!, "Playing");
        SetPrivateField(app, "_state", playingState);

        var method = typeof(GameApp).GetMethod("DrawBlockHighlight", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var hit = new BlockRaycastHit(10, 5, 8, 10, 6, 8);
        method!.Invoke(app, [(BlockRaycastHit?)hit, new Vector3(10.5f, 5.6f, 9.6f), Vector3.Zero]);

        var highlight = platform.DrawnCubeWires.Single();
        Assert.True(MathF.Abs(highlight.Width - 1.02f) < 0.0001f);
        Assert.True(MathF.Abs(highlight.Height - 0.04f) < 0.0001f);
        Assert.True(MathF.Abs(highlight.Length - 1.02f) < 0.0001f);
    }

    [Fact(DisplayName = "DrawBlockHighlight рисует рамку блока, если не удалось получить грань")]
    public void DrawBlockHighlight_DrawsFullBlockFallback_WhenFaceUnknown()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false },
            platform,
            new WorldMap(width: 16, height: 16, depth: 16, chunkSize: 8, seed: 0));

        var stateType = typeof(GameApp).GetNestedType("AppState", BindingFlags.NonPublic);
        Assert.NotNull(stateType);
        var playingState = Enum.Parse(stateType!, "Playing");
        SetPrivateField(app, "_state", playingState);

        var method = typeof(GameApp).GetMethod("DrawBlockHighlight", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var hit = new BlockRaycastHit(10, 5, 8, 12, 5, 8); // previous заведомо некорректен
        method!.Invoke(app, [(BlockRaycastHit?)hit, new Vector3(10.5f, 5.5f, 8.5f), new Vector3(0f, 0f, -1f)]);

        var highlight = platform.DrawnCubeWires.Single();
        Assert.True(MathF.Abs(highlight.Width - 1.02f) < 0.0001f);
        Assert.True(MathF.Abs(highlight.Height - 1.02f) < 0.0001f);
        Assert.True(MathF.Abs(highlight.Length - 1.02f) < 0.0001f);
    }

    [Fact(DisplayName = "Кнопка fullscreen в меню переключает режим")]
    public void Run_MenuFullscreenButton_TogglesMode()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 388f), leftMousePressed: true);

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.False(platform.IsFullscreen);
    }

    [Fact(DisplayName = "Меню применяет графические переключатели и их видно в тексте UI")]
    public void Run_MenuGraphicsButtons_AffectRuntimeSettings()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false
        };

        var world = new WorldMap(width: 96, height: 32, depth: 96, chunkSize: 16, seed: 777);
        var platform = new FakeGamePlatform();

        platform.EnqueueWindowShouldClose(false, false, false, false, false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 500f), leftMousePressed: true); // Графика
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 570f), leftMousePressed: true); // Туман
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 640f), leftMousePressed: true); // Контуры рельефа
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true); // Старт
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f)); // Playing кадр
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f), pressedKeys: [KeyboardKey.Escape]); // Пауза

        var app = new GameApp(config, platform, world);
        app.Run();

        Assert.Contains(platform.DrawnUiTexts, t => t.Contains("Графика: Высокая", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, t => t.Contains("Туман: Выкл", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, t => t.Contains("Контуры рельефа: Выкл", StringComparison.Ordinal));
        Assert.Contains(platform.DrawnUiTexts, t => t.Contains("FPS", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Клики вне кнопок меню не выполняют действий")]
    public void Run_MenuOutsideClicks_DoNotTriggerActions()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, false, true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(200f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(1080f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 200f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 580f), leftMousePressed: true);

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.False(platform.DisableCursorCalled);
        Assert.True(platform.CloseWindowCalled);
    }

    [Fact(DisplayName = "Кнопка Выход закрывает игру из меню")]
    public void Run_ExitButton_ClosesFromMenu()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false);
        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 450f), leftMousePressed: true);

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 2, height: 3, depth: 2));
        app.Run();

        Assert.True(platform.CloseWindowCalled);
        Assert.True(platform.DrawRectangleCalls > 0);
    }

    [Fact(DisplayName = "ESC в игре открывает паузу")]
    public void Run_EscapeInGame_OpensPauseMenu()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, true);

        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f), pressedKeys: [KeyboardKey.Escape]);
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f));

        var app = new GameApp(new GameConfig(), platform, new WorldMap(width: 6, height: 4, depth: 6));
        app.Run();

        Assert.True(platform.DisableCursorCalled);
        Assert.True(platform.EnableCursorCalled);
        Assert.True(platform.DrawRectangleCalls >= 4);
    }

    [Fact(DisplayName = "В игре клавиша V переключает режим камеры, и HUD показывает 3-е лицо")]
    public void Run_ToggleCameraMode_KeyV_UpdatesHudLabel()
    {
        var platform = new FakeGamePlatform();
        platform.EnqueueWindowShouldClose(false, false, false, false, true);

        platform.EnqueueFrameInput(mousePosition: new Vector2(640f, 320f), leftMousePressed: true); // Start
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f), pressedKeys: [KeyboardKey.V]); // Toggle camera
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f)); // Render frame with new mode
        platform.EnqueueFrameInput(mousePosition: new Vector2(0f, 0f));

        var app = new GameApp(new GameConfig { FullscreenByDefault = false }, platform, new WorldMap(width: 24, height: 12, depth: 24, chunkSize: 8, seed: 777));
        app.Run();

        Assert.Contains(platform.DrawnUiTexts, t => t.Contains("3-е лицо", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "DrawWorld в medium покрывает fade-ветку листвы")]
    public void DrawWorld_MediumQuality_CoversFoliageFadeBranch()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.Medium
        };
        var world = new WorldMap(width: 64, height: 12, depth: 32, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var z = 0; z < world.Depth; z++)
            {
                world.SetBlock(x, 0, z, BlockType.Air);
                world.SetBlock(x, 1, z, BlockType.Air);
            }
        }

        world.SetBlock(15, 2, 8, BlockType.Leaves); // ~14 блоков от игрока: между foliage и far LOD границами

        var platform = new FakeGamePlatform();
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(1.5f, 2.2f, 8.5f));
        player.SetPose(player.Position, new Vector3(1f, 0f, 0f));
        SetPrivateField(app, "_player", player);
        world.EnsureChunksAround(player.Position, radiusInChunks: 4);
        _ = world.RebuildDirtyChunkSurfaces(player.Position, maxChunks: 128);

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        Assert.True(platform.DrawCubeInstancedCalls >= 0);
    }

    [Fact(DisplayName = "DrawWorld с включенными scene-metrics пишет нулевой hash для пустой поверхности")]
    public void DrawWorld_SceneMetrics_EmptySurface_WritesZeroHash()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0);
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var z = 0; z < world.Depth; z++)
                {
                    world.SetBlock(x, y, z, BlockType.Air);
                }
            }
        }

        var platform = new FakeGamePlatform();
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(8.5f, 2.2f, 8.5f));
        player.SetPose(player.Position, new Vector3(1f, 0f, 0f));
        SetPrivateField(app, "_player", player);
        SetPrivateField(app, "_sceneMetricsEnabled", true);
        world.EnsureChunksAround(player.Position, radiusInChunks: 4);
        _ = world.RebuildDirtyChunkSurfaces(player.Position, maxChunks: 128);

        var method = typeof(GameApp).GetMethod("DrawWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(app, null);

        var countField = typeof(GameApp).GetField("_lastDrawnSurfaceCount", BindingFlags.Instance | BindingFlags.NonPublic);
        var hashField = typeof(GameApp).GetField("_lastDrawSceneHash", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(countField);
        Assert.NotNull(hashField);
        var count = (int)countField!.GetValue(app)!;
        var hash = (ulong)hashField!.GetValue(app)!;
        Assert.Equal(0, count);
        Assert.Equal(0UL, hash);
    }

    [Fact(DisplayName = "BuildLodBlendedColor покрывает terrain shortcut и fallback-blend ветки")]
    public void BuildLodBlendedColor_CoversTerrainShortcutAndFallbackBlend()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 8, seed: 0));

        var lodType = typeof(GameApp).GetNestedType("LodBlendWeights", BindingFlags.NonPublic);
        Assert.NotNull(lodType);
        var method = typeof(GameApp).GetMethod("BuildLodBlendedColor", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var terrainSurface = new WorldMap.SurfaceBlock(8, 2, 8, BlockType.Stone, VisibleFaces: 4, TopVisible: true, SkyExposure: 3);
        var customTerrainBlend = Activator.CreateInstance(lodType!, [0.3f, 0.2f, 0.5f])!;
        var terrainColor = (Color)method!.Invoke(
            app,
            [
                new Color(124, 120, 113, 255),
                terrainSurface,
                30f,
                0.8f,
                customTerrainBlend
            ])!;

        var expectedFar = (Color)typeof(GameApp).GetMethod("ApplyFarVisualStyle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(
            app,
            [new Color(124, 120, 113, 255), BlockType.Stone, 8, 2, 8, true, 30f])!;
        Assert.Equal(expectedFar.R, terrainColor.R);
        Assert.Equal(expectedFar.G, terrainColor.G);
        Assert.Equal(expectedFar.B, terrainColor.B);

        var fallbackSurface = new WorldMap.SurfaceBlock(9, 2, 9, BlockType.Wood, VisibleFaces: 4, TopVisible: true, SkyExposure: 3);
        var fallbackBlend = Activator.CreateInstance(lodType, [0.4f, 0.1f, 0.5f])!;
        var fallbackColor = (Color)method.Invoke(
            app,
            [
                new Color(120, 88, 54, 255),
                fallbackSurface,
                24f,
                0.5f,
                fallbackBlend
            ])!;

        Assert.InRange(fallbackColor.R, 0, 255);
    }

    [Fact(DisplayName = "BuildLodBlendedColor покрывает false-ветку guard-а Far при Far=0")]
    public void BuildLodBlendedColor_CoversFarGuardFalse_WhenFarIsZero()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 8, seed: 0));

        var lodType = typeof(GameApp).GetNestedType("LodBlendWeights", BindingFlags.NonPublic);
        var method = typeof(GameApp).GetMethod("BuildLodBlendedColor", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(lodType);
        Assert.NotNull(method);

        var surface = new WorldMap.SurfaceBlock(9, 2, 9, BlockType.Wood, VisibleFaces: 4, TopVisible: true, SkyExposure: 3);
        var blend = Activator.CreateInstance(lodType!, [0f, 0.5f, 0f])!;
        var color = (Color)method!.Invoke(
            app,
            [
                new Color(120, 88, 54, 255),
                surface,
                24f,
                0.5f,
                blend
            ])!;

        Assert.InRange(color.R, 0, 255);
        Assert.InRange(color.G, 0, 255);
        Assert.InRange(color.B, 0, 255);
    }

    [Fact(DisplayName = "FlushWorldCubeInstances пропускает пустой батч")]
    public void FlushWorldCubeInstances_SkipsEmptyBatch()
    {
        var platform = new FakeGamePlatform();
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            platform,
            new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0));

        var batchesField = typeof(GameApp).GetField("_worldInstanceBatches", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(batchesField);
        var batches = batchesField!.GetValue(app)!;
        var dictType = batches.GetType();
        var addMethod = dictType.GetMethod("Add");
        Assert.NotNull(addMethod);

        var keyType = typeof(GameApp).GetNestedType("InstancedBatchKey", BindingFlags.NonPublic);
        var lodType = typeof(GameApp).GetNestedType("WorldLodTier", BindingFlags.NonPublic);
        Assert.NotNull(keyType);
        Assert.NotNull(lodType);
        var lodNear = Enum.Parse(lodType!, "Near");
        var key = Activator.CreateInstance(keyType!, [byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue, lodNear])!;
        addMethod!.Invoke(batches, [key, new List<Matrix4x4>()]);

        var flush = typeof(GameApp).GetMethod("FlushWorldCubeInstances", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(flush);
        flush!.Invoke(app, null);

        Assert.Equal(0, platform.DrawCubeInstancedCalls);
    }

    [Fact(DisplayName = "UpdateWorldStreaming в sync-ветке пересобирает dirty-чанк при неизменной позиции")]
    public void UpdateWorldStreaming_SyncEarlyReturn_RebuildsDirtyChunk()
    {
        var config = new GameConfig
        {
            FullscreenByDefault = false,
            GraphicsQuality = GraphicsQuality.High
        };
        var world = new WorldMap(width: 96, height: 24, depth: 96, chunkSize: 16, seed: 777);
        var platform = new FakeGamePlatform { Fps = 120, FrameTime = 1f / 60f };
        var app = new GameApp(config, platform, world);
        var player = new PlayerController(config, new Vector3(24.5f, 3f, 24.5f));
        SetPrivateField(app, "_player", player);

        var update = typeof(GameApp).GetMethod("UpdateWorldStreaming", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(update);
        update!.Invoke(app, [false]); // первичный прогрев и фиксация last stream state

        world.SetBlock(24, 3, 24, BlockType.Wood);
        Assert.True(world.TryGetChunkSurfaceBlocks(1, 1, out var dirtySurfaceBefore));
        Assert.NotEmpty(dirtySurfaceBefore);

        update.Invoke(app, [false]); // та же позиция -> early return + sync rebuild path

        Assert.True(world.TryGetChunkSurfaceBlocks(1, 1, out var rebuiltSurface));
        Assert.NotEmpty(rebuiltSurface);
    }

    [Fact(DisplayName = "Адаптивный порог заморозки покрывает low-профиль")]
    public void GetAdaptiveFreezeSpeedThreshold_CoversLowBranch()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.Low },
            new FakeGamePlatform(),
            new WorldMap(width: 32, height: 8, depth: 32, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("GetAdaptiveFreezeSpeedThreshold", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var threshold = (float)method!.Invoke(app, null)!;
        Assert.Equal(0.7f, threshold);
    }

    [Fact(DisplayName = "GetAdaptiveRenderDistance покрывает ветку быстрого снижения")]
    public void AdaptiveRenderDistance_DropsQuickly_WhenTargetFalls()
    {
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false, GraphicsQuality = GraphicsQuality.High },
            new FakeGamePlatform(),
            new WorldMap(width: 64, height: 8, depth: 64, chunkSize: 8, seed: 0));
        var method = typeof(GameApp).GetMethod("GetAdaptiveRenderDistance", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var high = (int)method!.Invoke(app, [120, true, true])!;
        var dropped = (int)method.Invoke(app, [66, true, true])!;

        Assert.True(dropped < high);
        Assert.True(dropped >= 24);
    }

    [Fact(DisplayName = "ResolveUiFontPath возвращает пустую строку, если пути не существуют")]
    public void ResolveUiFontPath_ReturnsEmpty_WhenNoFilesExist()
    {
        var result = GameApp.ResolveUiFontPath(
        [
            "/tmp/aig-missing-font-1.ttf",
            "/tmp/aig-missing-font-2.ttf"
        ]);

        Assert.Equal(string.Empty, result);
    }

    [Fact(DisplayName = "GetBlockName возвращает fallback для неизвестного типа")]
    public void GetBlockName_ReturnsDefault_ForUnknown()
    {
        Assert.Equal("Трава", GameApp.GetBlockName(BlockType.Grass));
        Assert.Equal("Дерево", GameApp.GetBlockName(BlockType.Wood));
        Assert.Equal("Листва", GameApp.GetBlockName(BlockType.Leaves));
        Assert.Equal("Блок", GameApp.GetBlockName((BlockType)999));
    }

    [Fact(DisplayName = "GetQualityName корректно возвращает все подписи пресетов")]
    public void GetQualityName_ReturnsAllLabels()
    {
        Assert.Equal("Низкая", GameApp.GetQualityName(GraphicsQuality.Low));
        Assert.Equal("Средняя", GameApp.GetQualityName(GraphicsQuality.Medium));
        Assert.Equal("Высокая", GameApp.GetQualityName(GraphicsQuality.High));
    }

    [Fact(DisplayName = "Пустой конструктор GameApp создаёт экземпляр")]
    public void GameApp_DefaultConstructor_CreatesInstance()
    {
        var app = new GameApp();
        Assert.NotNull(app);
    }

    private sealed class DelegatingRunner(Action action) : IGameRunner
    {
        public void Run() => action();
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(target));
    }

    private static BotDiagnosticsLog CreateBotDiagnosticsLog(StreamWriter writer, string fileName)
    {
        var constructor = typeof(BotDiagnosticsLog).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(string), typeof(StreamWriter)],
            modifiers: null);
        Assert.NotNull(constructor);
        return Assert.IsType<BotDiagnosticsLog>(constructor.Invoke([fileName, writer]));
    }

    private static object CreateAutoBotState(Vector3 lastPosition, float stuckTime, int turnSign, float turnLockTime, float wanderPhase)
    {
        var stateType = typeof(GameApp).GetNestedType("AutoBotState", BindingFlags.NonPublic);
        Assert.NotNull(stateType);

        var state = Activator.CreateInstance(stateType!)!;
        SetAutoBotField(state, "LastPosition", lastPosition);
        SetAutoBotField(state, "StuckTime", stuckTime);
        SetAutoBotField(state, "TurnSign", turnSign);
        SetAutoBotField(state, "TurnLockTime", turnLockTime);
        SetAutoBotField(state, "WanderPhase", wanderPhase);
        return state;
    }

    private static void SetAutoBotField(object state, string fieldName, object value)
    {
        var field = state.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(state, value);
    }

    private static object GetAutoBotField(object state, string fieldName)
    {
        var field = state.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(state)!;
    }

    private sealed class ThrowingWriteStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new IOException("broken stream");
        }
    }

    private sealed class ThrowingDisposeStream(bool objectDisposed) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            if (objectDisposed)
            {
                throw new ObjectDisposedException(nameof(ThrowingDisposeStream));
            }

            throw new IOException("dispose flush failed");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }
}
