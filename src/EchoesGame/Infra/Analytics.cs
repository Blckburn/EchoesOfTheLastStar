using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EchoesGame.Infra
{
    public static class Analytics
    {
        private static string? _logPath;
        private static readonly object _lock = new object();

        public static void Init()
        {
            string root = AppContext.BaseDirectory;
            string logDir = Path.Combine(root, "..", "..", "..", "..", "builds", "devlogs");
            Directory.CreateDirectory(Path.GetFullPath(logDir));
            _logPath = Path.GetFullPath(Path.Combine(logDir, "events.log"));
        }

        public static void Log(string type, Dictionary<string, object> data)
        {
            if (_logPath == null) return;
            var record = new Dictionary<string, object>(data)
            {
                ["type"] = type,
                ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            string line = JsonSerializer.Serialize(record);
            lock (_lock)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
    }
}
