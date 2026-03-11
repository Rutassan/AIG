using System.Numerics;
using System.Reflection;
using AIG.Game.Config;
using AIG.Game.Core;
using AIG.Game.Tests.Fakes;
using AIG.Game.World;
using Raylib_cs;

namespace AIG.Game.Tests;

public sealed class GameCaptureTests
{
    [Fact(DisplayName = "FfmpegVideoEncoder собирает ожидаемую команду ffmpeg")]
    public void FfmpegVideoEncoder_CreateStartInfo_BuildsExpectedArguments()
    {
        var request = new VideoEncodingRequest(
            "/tmp/aig-frames/frame-%06d.bmp",
            "/tmp/aig-video.mp4",
            30);

        var startInfo = FfmpegVideoEncoder.CreateStartInfo(request);

        Assert.Equal("ffmpeg", startInfo.FileName);
        Assert.Equal("-y", startInfo.ArgumentList[0]);
        Assert.Contains("/tmp/aig-frames/frame-%06d.bmp", startInfo.ArgumentList);
        Assert.Contains("libx264", startInfo.ArgumentList);
        Assert.Equal("/tmp/aig-video.mp4", startInfo.ArgumentList[^1]);
    }

    [Fact(DisplayName = "FfmpegVideoEncoder возвращает успех только если ffmpeg завершился и файл создан")]
    public void FfmpegVideoEncoder_Encode_ReturnsSuccessWhenFileExists()
    {
        var request = new VideoEncodingRequest("in-%06d.png", "out.mp4", 60);

        var result = FfmpegVideoEncoder.Encode(
            request,
            _ => new ProcessExecutionResult(0, string.Empty),
            path => path == "out.mp4");

        Assert.True(result.Success);
        Assert.Equal("out.mp4", result.OutputFilePath);
        Assert.Equal(string.Empty, result.ErrorMessage);
    }

    [Fact(DisplayName = "FfmpegVideoEncoder возвращает ошибку при ненулевом коде ffmpeg")]
    public void FfmpegVideoEncoder_Encode_ReturnsFailureWhenFfmpegFails()
    {
        var request = new VideoEncodingRequest("in-%06d.png", "out.mp4", 30);

        var result = FfmpegVideoEncoder.Encode(
            request,
            _ => new ProcessExecutionResult(1, "encoder failed"),
            _ => false);

        Assert.False(result.Success);
        Assert.Equal("encoder failed", result.ErrorMessage);
    }

    [Fact(DisplayName = "FfmpegVideoEncoder возвращает ошибку если ffmpeg не создал файл или бросил исключение")]
    public void FfmpegVideoEncoder_Encode_CoversMissingFileAndException()
    {
        var request = new VideoEncodingRequest("in-%06d.png", "out.mp4", 30);
        var missingFile = FfmpegVideoEncoder.Encode(
            request,
            _ => new ProcessExecutionResult(0, string.Empty),
            _ => false);
        var exception = FfmpegVideoEncoder.Encode(
            request,
            _ => throw new InvalidOperationException("boom"),
            _ => false);

        Assert.False(missingFile.Success);
        Assert.Contains("without creating", missingFile.ErrorMessage, StringComparison.Ordinal);
        Assert.False(exception.Success);
        Assert.Equal("boom", exception.ErrorMessage);
    }

    [Fact(DisplayName = "FfmpegVideoEncoder покрывает default runner/fileExists и реальный локальный ffmpeg")]
    public void FfmpegVideoEncoder_Encode_UsesRealFfmpeg()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var framesDir = Path.Combine(tempRoot, "frames");
            Directory.CreateDirectory(framesDir);
            File.WriteAllBytes(
                Path.Combine(framesDir, "frame-000000.bmp"),
                Convert.FromBase64String("Qk02AAAAAAAAADYAAAAoAAAAAQAAAAEAAAABABgAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAA////AA=="));

            var request = new VideoEncodingRequest(
                Path.Combine(framesDir, "frame-%06d.bmp"),
                Path.Combine(tempRoot, "out.mp4"),
                1);

            var result = FfmpegVideoEncoder.Encode(request);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(File.Exists(request.OutputFilePath));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact(DisplayName = "FfmpegVideoEncoder возвращает дефолтное сообщение если stderr пустой")]
    public void FfmpegVideoEncoder_Encode_UsesFallbackMessageWhenStdErrEmpty()
    {
        var request = new VideoEncodingRequest("in-%06d.png", "out.mp4", 30);

        var result = FfmpegVideoEncoder.Encode(
            request,
            _ => new ProcessExecutionResult(3, string.Empty),
            _ => false);

        Assert.False(result.Success);
        Assert.Contains("code 3", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "GameCaptureManager создает путь для скриншота в папке проекта")]
    public void GameCaptureManager_CreateScreenshotPath_UsesConfiguredDirectory()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var screenshots = Path.Combine(tempRoot, "captures", "screens");
            var manager = new GameCaptureManager(
                screenshots,
                Path.Combine(tempRoot, "captures", "videos"),
                30,
                clock: () => new DateTimeOffset(2026, 3, 10, 12, 34, 56, 789, TimeSpan.Zero));

            var path = manager.CreateScreenshotPath();

            Assert.Equal(Path.Combine(screenshots, "shot-20260310-123456-789.png"), path);
            Assert.True(Directory.Exists(screenshots));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact(DisplayName = "GameCaptureManager покрывает дефолтные encoder/clock")]
    public void GameCaptureManager_DefaultConstructorBranches_Work()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var manager = new GameCaptureManager(
                Path.Combine(tempRoot, "captures", "screens"),
                Path.Combine(tempRoot, "captures", "videos"),
                1);

            var screenshot = manager.CreateScreenshotPath();

            Assert.StartsWith(Path.Combine(tempRoot, "captures", "screens"), screenshot, StringComparison.Ordinal);
            Assert.Contains("shot-", Path.GetFileName(screenshot), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact(DisplayName = "GameCaptureManager удаляет лишние screenshotXXX из корня проекта")]
    public void GameCaptureManager_CleanupLegacyRootScreenshots_RemovesRootDuplicates()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var screenshots = Path.Combine(tempRoot, "captures", "screens");
            Directory.CreateDirectory(screenshots);
            var legacyA = Path.Combine(tempRoot, "screenshot000.png");
            var legacyB = Path.Combine(tempRoot, "screenshot001.png");
            var keep = Path.Combine(tempRoot, "not-a-screenshot.png");
            File.WriteAllText(legacyA, "a");
            File.WriteAllText(legacyB, "b");
            File.WriteAllText(keep, "keep");

            var manager = new GameCaptureManager(
                screenshots,
                Path.Combine(tempRoot, "captures", "videos"),
                30,
                workingDirectory: tempRoot);

            var removed = manager.CleanupLegacyRootScreenshots();

            Assert.Equal(2, removed);
            Assert.False(File.Exists(legacyA));
            Assert.False(File.Exists(legacyB));
            Assert.True(File.Exists(keep));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact(DisplayName = "GameCaptureManager записывает BMP-кадры и очищает временные кадры после успешной сборки видео")]
    public void GameCaptureManager_StartAndStopRecording_CleansFramesDirectoryOnSuccess()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var videos = Path.Combine(tempRoot, "captures", "videos");
            var manager = new GameCaptureManager(
                Path.Combine(tempRoot, "captures", "screens"),
                videos,
                30,
                request =>
                {
                    File.WriteAllText(request.OutputFilePath, "video");
                    return new VideoEncodingResult(true, request.OutputFilePath, string.Empty);
                },
                () => new DateTimeOffset(2026, 3, 10, 12, 0, 0, 111, TimeSpan.Zero));

            var outputPath = manager.StartRecording();
            Assert.True(manager.IsRecording);
            Assert.True(manager.TryGetNextRecordingFramePath(0f, out var firstFrame));
            Assert.EndsWith(".bmp", firstFrame, StringComparison.Ordinal);
            File.WriteAllText(firstFrame, "frame");
            Assert.False(manager.TryGetNextRecordingFramePath(0.01f, out _));
            Assert.True(manager.TryGetNextRecordingFramePath(0.05f, out var secondFrame));
            Assert.EndsWith(".bmp", secondFrame, StringComparison.Ordinal);
            File.WriteAllText(secondFrame, "frame");

            var result = manager.StopRecording();

            Assert.True(result.Success);
            Assert.Equal(outputPath, result.OutputFilePath);
            Assert.True(File.Exists(outputPath));
            Assert.Empty(Directory.GetDirectories(videos, ".frames-*"));
            Assert.False(manager.IsRecording);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact(DisplayName = "GameCaptureManager покрывает stop без активной записи и stop без кадров")]
    public void GameCaptureManager_StopRecording_CoversInactiveAndNoFramesBranches()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var videos = Path.Combine(tempRoot, "captures", "videos");
            var manager = new GameCaptureManager(
                Path.Combine(tempRoot, "captures", "screens"),
                videos,
                30,
                request => new VideoEncodingResult(true, request.OutputFilePath, string.Empty),
                () => new DateTimeOffset(2026, 3, 10, 12, 0, 0, 222, TimeSpan.Zero));

            var inactive = manager.StopRecording();
            var outputPath = manager.StartRecording();
            var noFrames = manager.StopRecording();

            Assert.False(inactive.Success);
            Assert.Equal("recording is not active.", inactive.ErrorMessage);
            Assert.False(noFrames.Success);
            Assert.Equal(outputPath, noFrames.OutputFilePath);
            Assert.Contains("no captured frames", noFrames.ErrorMessage, StringComparison.Ordinal);
            Assert.Empty(Directory.GetDirectories(videos, ".frames-*"));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact(DisplayName = "GameCaptureManager сохраняет PNG кадры если сборка видео завершилась с ошибкой")]
    public void GameCaptureManager_StopRecording_KeepsFramesOnEncoderFailure()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var videos = Path.Combine(tempRoot, "captures", "videos");
            var manager = new GameCaptureManager(
                Path.Combine(tempRoot, "captures", "screens"),
                videos,
                30,
                request => new VideoEncodingResult(false, request.OutputFilePath, "ffmpeg fail"),
                () => new DateTimeOffset(2026, 3, 10, 12, 0, 0, 333, TimeSpan.Zero));

            _ = manager.StartRecording();
            Assert.True(manager.TryGetNextRecordingFramePath(0f, out var frame));
            File.WriteAllText(frame, "frame");

            var result = manager.StopRecording();

            Assert.False(result.Success);
            Assert.Equal("ffmpeg fail", result.ErrorMessage);
            Assert.Single(Directory.GetDirectories(videos, ".frames-*"));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact(DisplayName = "GameApp обрабатывает F12 и F10: снимок и запись попадают в папку проекта")]
    public void GameApp_HandleCaptureHotkeys_SavesScreenshotAndVideoFrame()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var (app, platform, captureManager, videoPath) = CreateCaptureApp(tempRoot);

            platform.SetPressedKeys(KeyboardKey.F12, KeyboardKey.F10);
            Assert.True(app.HandleCaptureHotkeys());
            Assert.True(captureManager.IsRecording);

            app.FlushCaptureOutputs(0f);
            platform.SetPressedKeys(KeyboardKey.F10);
            Assert.True(app.HandleCaptureHotkeys());

            Assert.False(captureManager.IsRecording);
            Assert.Equal(2, platform.SavedScreenshots.Count);
            Assert.Contains(platform.SavedScreenshots, path => path.StartsWith(Path.Combine(tempRoot, "captures", "screenshots"), StringComparison.Ordinal));
            Assert.Contains(platform.SavedScreenshots, path => path.StartsWith(Path.Combine(tempRoot, "captures", "videos"), StringComparison.Ordinal));
            Assert.True(File.Exists(videoPath));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact(DisplayName = "GameApp после снимка очищает лишние screenshotXXX из корня проекта")]
    public void GameApp_FlushCaptureOutputs_CleansLegacyRootScreenshots()
    {
        var tempRoot = CreateTempDirectory();
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempRoot);
            var platform = new FakeGamePlatform();
            var captureManager = new GameCaptureManager(
                Path.Combine(tempRoot, "captures", "screenshots"),
                Path.Combine(tempRoot, "captures", "videos"),
                30,
                request =>
                {
                    File.WriteAllText(request.OutputFilePath, "video");
                    return new VideoEncodingResult(true, request.OutputFilePath, string.Empty);
                },
                () => new DateTimeOffset(2026, 3, 10, 12, 0, 0, 555, TimeSpan.Zero),
                tempRoot);
            var config = new GameConfig { FullscreenByDefault = false };
            var world = new WorldMap(width: 32, height: 16, depth: 32, chunkSize: config.ChunkSize, seed: 0);
            var app = new GameApp(config, platform, world, captureManager);

            File.WriteAllText(Path.Combine(tempRoot, "screenshot000.png"), "legacy");
            platform.SetPressedKeys(KeyboardKey.F12);
            Assert.True(app.HandleCaptureHotkeys());

            app.FlushCaptureOutputs(0f);

            Assert.Single(platform.SavedScreenshots);
            Assert.StartsWith(Path.Combine(tempRoot, "captures", "screenshots"), platform.SavedScreenshots[0], StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(tempRoot, "screenshot000.png")));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact(DisplayName = "GameApp рисует индикатор записи только во время активной записи")]
    public void GameApp_DrawCaptureIndicator_ShowsOnlyWhenRecording()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var (app, platform, captureManager, _) = CreateCaptureApp(tempRoot);

            InvokePrivate(app, "DrawCaptureIndicator");
            Assert.DoesNotContain(platform.DrawnUiTexts, text => text.Contains("Идёт запись", StringComparison.Ordinal));

            captureManager.StartRecording();
            InvokePrivate(app, "DrawCaptureIndicator");

            Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Идёт запись", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact(DisplayName = "Run завершает активную запись и собирает mp4 при выходе из игры")]
    public void GameApp_Run_StopsRecordingDuringShutdown()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var platform = new FakeGamePlatform();
            platform.EnqueueWindowShouldClose(false, false, true);
            platform.EnqueueFrameInput(mousePosition: Vector2.Zero, pressedKeys: [KeyboardKey.F10]);
            platform.EnqueueFrameInput(mousePosition: Vector2.Zero);

            var outputVideo = Path.Combine(tempRoot, "captures", "videos", "video-20260310-120000-444.mp4");
            var captureManager = new GameCaptureManager(
                Path.Combine(tempRoot, "captures", "screenshots"),
                Path.Combine(tempRoot, "captures", "videos"),
                30,
                request =>
                {
                    File.WriteAllText(request.OutputFilePath, "video");
                    return new VideoEncodingResult(true, request.OutputFilePath, string.Empty);
                },
                () => new DateTimeOffset(2026, 3, 10, 12, 0, 0, 444, TimeSpan.Zero));

            var app = new GameApp(
                new GameConfig
                {
                    FullscreenByDefault = false,
                    ScreenshotDirectory = Path.Combine(tempRoot, "captures", "screenshots"),
                    VideoDirectory = Path.Combine(tempRoot, "captures", "videos")
                },
                platform,
                new WorldMap(width: 48, height: 24, depth: 48, chunkSize: 8, seed: 0),
                captureManager);

            app.Run();

            Assert.True(File.Exists(outputVideo));
            Assert.Contains(platform.DrawnUiTexts, text => text.Contains("Идёт запись", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private static (GameApp App, FakeGamePlatform Platform, GameCaptureManager CaptureManager, string OutputVideoPath) CreateCaptureApp(string tempRoot)
    {
        var platform = new FakeGamePlatform();
        var outputVideoPath = Path.Combine(tempRoot, "captures", "videos", "video-20260310-120000-555.mp4");
        var captureManager = new GameCaptureManager(
            Path.Combine(tempRoot, "captures", "screenshots"),
            Path.Combine(tempRoot, "captures", "videos"),
            30,
            request =>
            {
                File.WriteAllText(request.OutputFilePath, "video");
                return new VideoEncodingResult(true, request.OutputFilePath, string.Empty);
            },
            () => new DateTimeOffset(2026, 3, 10, 12, 0, 0, 555, TimeSpan.Zero));

        var app = new GameApp(
            new GameConfig
            {
                FullscreenByDefault = false,
                ScreenshotDirectory = Path.Combine(tempRoot, "captures", "screenshots"),
                VideoDirectory = Path.Combine(tempRoot, "captures", "videos")
            },
            platform,
            new WorldMap(width: 48, height: 24, depth: 48, chunkSize: 8, seed: 0),
            captureManager);

        return (app, platform, captureManager, outputVideoPath);
    }

    private static object? InvokePrivate(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(target, null);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aig-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
