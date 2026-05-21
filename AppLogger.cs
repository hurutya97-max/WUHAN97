namespace HostComputerApp;

public sealed class AppLogger
{
    private readonly object _lock = new();
    private readonly string _logDirectory;

    public AppLogger()
    {
        _logDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{level}\t{message}";
        lock (_lock)
        {
            File.AppendAllText(GetLogPath(DateTime.Now), line + Environment.NewLine);
        }
    }

    public List<LogRecord> ReadLatest(int take)
    {
        lock (_lock)
        {
            var files = Directory.GetFiles(_logDirectory, "app-*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .Take(5)
                .ToArray();

            return files
                .SelectMany(File.ReadLines)
                .Reverse()
                .Take(take)
                .Select(ParseLine)
                .ToList();
        }
    }

    private string GetLogPath(DateTime dateTime) => Path.Combine(_logDirectory, $"app-{dateTime:yyyyMMdd}.log");

    private static LogRecord ParseLine(string line)
    {
        var parts = line.Split('\t', 3);
        if (parts.Length != 3)
        {
            return new LogRecord("", "", line);
        }

        return new LogRecord(parts[0], parts[1], parts[2]);
    }
}

public sealed record LogRecord(string 时间, string 等级, string 内容);
