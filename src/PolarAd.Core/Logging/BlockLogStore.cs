using System.Text.Json;
using PolarAd.Core.Models;

namespace PolarAd.Core.Logging;

/// <summary>
/// Keeps the N most recent block/allow events in memory and mirrors them to a local
/// JSON-lines file so the history survives an app restart. Never leaves the machine.
/// </summary>
public sealed class BlockLogStore
{
    private readonly object _lock = new();
    private readonly LinkedList<BlockLogEntry> _entries = new();
    private readonly string _logFilePath;
    private readonly int _maxEntries;

    public BlockLogStore(string logFilePath, int maxEntries = 2000)
    {
        _logFilePath = logFilePath;
        _maxEntries = maxEntries;
        LoadFromDisk();
    }

    public void Add(BlockLogEntry entry)
    {
        lock (_lock)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > _maxEntries)
            {
                _entries.RemoveLast();
            }
        }

        AppendToDisk(entry);
    }

    public IReadOnlyList<BlockLogEntry> GetRecent(int count = 200)
    {
        lock (_lock)
        {
            return _entries.Take(count).ToList();
        }
    }

    public int TotalBlockedCount
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count(e => e.Decision is BlockDecision.BlockedByList or BlockDecision.BlockedByUserRule);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
        try
        {
            if (File.Exists(_logFilePath))
            {
                File.Delete(_logFilePath);
            }
        }
        catch { }
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_logFilePath))
        {
            return;
        }

        try
        {
            foreach (var line in File.ReadLines(_logFilePath).Reverse().Take(_maxEntries))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = JsonSerializer.Deserialize<BlockLogEntry>(line);
                if (entry != null)
                {
                    _entries.AddLast(entry);
                }
            }
        }
        catch
        {
            // Corrupt log file; start fresh rather than fail to launch.
        }
    }

    private void AppendToDisk(BlockLogEntry entry)
    {
        try
        {
            var dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.AppendAllText(_logFilePath, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch
        {
            // Logging to disk is best-effort; never let it crash the proxy loop.
        }
    }
}
