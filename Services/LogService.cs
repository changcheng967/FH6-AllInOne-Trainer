using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FH6Mod.Services;

public sealed class LogService : IDisposable
{
    private readonly List<string> _entries = [];
    private const int MaxEntries = 500;
    private readonly StreamWriter? _file;
    private readonly object _lock = new();

    public event Action? Changed;

    public LogService()
    {
        var dir = AppContext.BaseDirectory;
        var path = Path.Combine(dir, "trainer.log");
        try
        {
            // Shared access: a second trainer instance or an external viewer holding
            // the log must not crash this one at startup (#197). If the file still
            // cannot be opened, fall back to in-memory logging — never kill the app.
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _file = new StreamWriter(stream) { AutoFlush = true };
            _file.WriteLine($"\n========== Trainer started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========");
        }
        catch (IOException)
        {
            _file = null;
        }
    }

    public void Info(string msg) => Add(msg);
    public void Error(string msg) => Add($"[FAIL] {msg}");

    private void Add(string msg)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
        var entry = $"[{ts}] {msg}";
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            try { _file?.WriteLine(entry); } catch { /* logging must never throw */ }
        }
        Changed?.Invoke();
    }

    public string GetTail(int lines = 60)
    {
        lock (_lock)
        {
            var start = Math.Max(0, _entries.Count - lines);
            return string.Join("\n", _entries.Skip(start));
        }
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        try { _file?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Trainer shutting down."); _file?.Dispose(); }
        catch { }
    }
}
