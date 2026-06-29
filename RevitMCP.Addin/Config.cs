namespace RevitMCP.Addin
{
    internal static class Config
    {
        /// <summary>MCP HTTP 서버 포트 (기본 9876)</summary>
        public static int Port { get; set; } = 9876;

        /// <summary>현재 Revit 버전 문자열</summary>
        public static string RevitVersion =>
#if REVIT2025
            "2025";
#elif REVIT2026
            "2026";
#elif REVIT2027
            "2027";
#else
            "unknown";
#endif
    }
}
