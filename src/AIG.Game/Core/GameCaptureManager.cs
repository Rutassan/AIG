using System.Diagnostics;
using System.Globalization;

namespace AIG.Game.Core;

internal readonly record struct VideoEncodingRequest(
    string InputPattern,
    string OutputFilePath,
    int FramesPerSecond);

internal readonly record struct VideoEncodingResult(
    bool Success,
    string OutputFilePath,
    string ErrorMessage);

internal readonly record struct ProcessExecutionResult(
    int ExitCode,
    string StandardError);

internal static class FfmpegVideoEncoder
{
    internal static VideoEncodingResult Encode(
        VideoEncodingRequest request,
        Func<ProcessStartInfo, ProcessExecutionResult>? runner = null,
        Func<string, bool>? fileExists = null)
    {
        runner ??= RunProcess;
        fileExists ??= File.Exists;

        try
        {
            var result = runner(CreateStartInfo(request));
            if (result.ExitCode != 0)
            {
                return new VideoEncodingResult(false, request.OutputFilePath, string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"ffmpeg exited with code {result.ExitCode}."
                    : result.StandardError.Trim());
            }

            return fileExists(request.OutputFilePath)
                ? new VideoEncodingResult(true, request.OutputFilePath, string.Empty)
                : new VideoEncodingResult(false, request.OutputFilePath, "ffmpeg finished without creating the output file.");
        }
        catch (Exception ex)
        {
            return new VideoEncodingResult(false, request.OutputFilePath, ex.Message);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(VideoEncodingRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-framerate");
        startInfo.ArgumentList.Add(request.FramesPerSecond.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(request.InputPattern);
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("libx264");
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add("pad=ceil(iw/2)*2:ceil(ih/2)*2");
        startInfo.ArgumentList.Add("-preset");
        startInfo.ArgumentList.Add("veryfast");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("yuv420p");
        startInfo.ArgumentList.Add(request.OutputFilePath);
        return startInfo;
    }

    private static ProcessExecutionResult RunProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)!;
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessExecutionResult(process.ExitCode, standardError);
    }
}

internal sealed class GameCaptureManager
{
    private const string RecordingFrameExtension = ".bmp";
    private const string LegacyScreenshotPattern = "screenshot*.png";
    private sealed record RecordingSession(string FramesDirectory, string OutputFilePath);

    private readonly string _screenshotsDirectory;
    private readonly string _videosDirectory;
    private readonly int _videoFramesPerSecond;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<VideoEncodingRequest, VideoEncodingResult> _encoder;
    private readonly string _workingDirectory;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, string, SearchOption, string[]> _getFiles;
    private readonly Action<string> _deleteFile;

    private RecordingSession? _recordingSession;
    private float _recordingAccumulator;
    private int _recordingFrameIndex;

    internal GameCaptureManager(
        string screenshotsDirectory,
        string videosDirectory,
        int videoFramesPerSecond,
        Func<VideoEncodingRequest, VideoEncodingResult>? encoder = null,
        Func<DateTimeOffset>? clock = null,
        string? workingDirectory = null,
        Func<string, bool>? directoryExists = null,
        Func<string, string, SearchOption, string[]>? getFiles = null,
        Action<string>? deleteFile = null)
    {
        _screenshotsDirectory = screenshotsDirectory;
        _videosDirectory = videosDirectory;
        _videoFramesPerSecond = Math.Max(1, videoFramesPerSecond);
        _encoder = encoder ?? (request => FfmpegVideoEncoder.Encode(request));
        _clock = clock ?? (() => DateTimeOffset.Now);
        _workingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? Directory.GetCurrentDirectory()
            : workingDirectory;
        _directoryExists = directoryExists ?? Directory.Exists;
        _getFiles = getFiles ?? Directory.GetFiles;
        _deleteFile = deleteFile ?? File.Delete;
    }

    internal bool IsRecording => _recordingSession is not null;
    internal string RecordingIndicatorText => IsRecording ? "Идёт запись" : string.Empty;

    internal string CreateScreenshotPath()
    {
        Directory.CreateDirectory(_screenshotsDirectory);
        return Path.Combine(_screenshotsDirectory, $"shot-{BuildTimestamp()}.png");
    }

    internal string StartRecording()
    {
        Directory.CreateDirectory(_videosDirectory);
        var stamp = BuildTimestamp();
        var outputFilePath = Path.Combine(_videosDirectory, $"video-{stamp}.mp4");
        var framesDirectory = Path.Combine(_videosDirectory, $".frames-{stamp}");

        Directory.CreateDirectory(framesDirectory);
        _recordingSession = new RecordingSession(framesDirectory, outputFilePath);
        _recordingAccumulator = 1f / _videoFramesPerSecond;
        _recordingFrameIndex = 0;
        return outputFilePath;
    }

    internal VideoEncodingResult StopRecording()
    {
        if (_recordingSession is null)
        {
            return new VideoEncodingResult(false, string.Empty, "recording is not active.");
        }

        var session = _recordingSession;
        _recordingSession = null;
        _recordingAccumulator = 0f;

        if (_recordingFrameIndex == 0)
        {
            DeleteDirectoryIfExists(session.FramesDirectory);
            return new VideoEncodingResult(false, session.OutputFilePath, "recording has no captured frames.");
        }

        var result = _encoder(new VideoEncodingRequest(
            Path.Combine(session.FramesDirectory, $"frame-%06d{RecordingFrameExtension}"),
            session.OutputFilePath,
            _videoFramesPerSecond));

        if (result.Success)
        {
            DeleteDirectoryIfExists(session.FramesDirectory);
        }

        return result;
    }

    internal bool TryGetNextRecordingFramePath(float deltaTime, out string framePath)
    {
        framePath = string.Empty;
        if (_recordingSession is null)
        {
            return false;
        }

        var frameDuration = 1f / _videoFramesPerSecond;
        _recordingAccumulator += Math.Max(0f, deltaTime);

        if (_recordingFrameIndex > 0 && _recordingAccumulator + 0.0001f < frameDuration)
        {
            return false;
        }

        if (_recordingFrameIndex > 0)
        {
            _recordingAccumulator = MathF.Max(0f, _recordingAccumulator - frameDuration);
        }
        else
        {
            _recordingAccumulator = 0f;
        }

        framePath = Path.Combine(_recordingSession.FramesDirectory, $"frame-{_recordingFrameIndex:D06}{RecordingFrameExtension}");
        _recordingFrameIndex++;
        return true;
    }

    internal int CleanupLegacyRootScreenshots()
    {
        if (!_directoryExists(_workingDirectory))
        {
            return 0;
        }

        var removed = 0;
        foreach (var path in _getFiles(_workingDirectory, LegacyScreenshotPattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                _deleteFile(path);
                removed++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    private string BuildTimestamp()
    {
        return _clock().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
