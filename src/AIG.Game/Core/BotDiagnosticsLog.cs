using System.Globalization;
using System.IO;
using AIG.Game.Config;

namespace AIG.Game.Core;

internal sealed class BotDiagnosticsLog : IDisposable
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer;
    private bool _disposed;
    private bool _failed;

    private BotDiagnosticsLog(string filePath, StreamWriter writer)
    {
        FilePath = filePath;
        _writer = writer;
    }

    internal string FilePath { get; }

    internal static BotDiagnosticsLog? Create(GameConfig config)
    {
        if (!config.BotDiagnosticsEnabled)
        {
            return null;
        }

        var directory = config.BotDiagnosticsDirectory;
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(
            directory,
            $"bot-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.log");
        var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var writer = new StreamWriter(stream)
        {
            AutoFlush = true
        };

        var log = new BotDiagnosticsLog(filePath, writer);
        log.Write("diag", "session-start");
        return log;
    }

    internal void Write(string category, string message)
    {
        lock (_sync)
        {
            if (_disposed || _failed)
            {
                return;
            }

            try
            {
                _writer.WriteLine($"{DateTime.UtcNow:O} [{category}] {message}");
            }
            catch (IOException)
            {
                _failed = true;
            }
            catch (ObjectDisposedException)
            {
                _failed = true;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _writer.Dispose();
            }
            catch (IOException)
            {
                _failed = true;
            }
            catch (ObjectDisposedException)
            {
                _failed = true;
            }
        }
    }
}
