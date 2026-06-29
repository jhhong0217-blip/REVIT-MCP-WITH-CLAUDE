using System;
using System.IO;

namespace RevitMCP.Addin
{
    internal static class Logger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RevitMCP", "revit-mcp.log");

        static Logger()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        }

        public static void Info(string msg) => Write("INFO", msg);
        public static void Error(string msg) => Write("ERROR", msg);

        private static void Write(string level, string msg)
        {
            try
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {msg}{Environment.NewLine}");
            }
            catch { /* 로그 실패는 무시 */ }
        }
    }
}
