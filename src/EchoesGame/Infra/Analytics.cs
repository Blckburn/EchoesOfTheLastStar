using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EchoesGame.Infra
{
    public static class Analytics
    {
        private static string? _runLogPath;
        private static string? _latestPath;
        private static readonly object _lock = new object();
        private const int MaxRunsToKeep = 50;

        public static void Init()
        {
            string cwd = Directory.GetCurrentDirectory();
            string logDir = Path.Combine(cwd, "builds", "devlogs");
            Directory.CreateDirectory(Path.GetFullPath(logDir));
            string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            _runLogPath = Path.GetFullPath(Path.Combine(logDir, $"run_{ts}.log"));
            _latestPath = Path.GetFullPath(Path.Combine(logDir, "events_latest.log"));
            // Retention: keep only last N run_*.log files
            try
            {
                var files = new DirectoryInfo(logDir).GetFiles("run_*.log").OrderByDescending(f => f.CreationTimeUtc).ToList();
                for (int i = MaxRunsToKeep; i < files.Count; i++) files[i].Delete();
            }
            catch {}
        }

        public static void Log(string type, Dictionary<string, object> data)
        {
            if (_runLogPath == null) return;
            var record = new Dictionary<string, object>(data)
            {
                ["type"] = type,
                ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            string line = JsonSerializer.Serialize(record);
            lock (_lock)
            {
                File.AppendAllText(_runLogPath, line + Environment.NewLine);
                if (_latestPath != null)
                {
                    // overwrite latest snapshot each write (keeps last run)
                    File.AppendAllText(_latestPath, line + Environment.NewLine);
                }
            }
        }
    }
}
