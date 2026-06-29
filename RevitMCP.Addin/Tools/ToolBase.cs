using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Addin.Tools
{
    /// <summary>모든 MCP 도구의 기반 클래스</summary>
    public abstract class ToolBase
    {
        public abstract string Name { get; }
        public abstract string Description { get; }

        /// <summary>JSON Schema (MCP tools/list 응답용)</summary>
        public abstract JObject GetSchema();

        /// <summary>실제 실행 — Revit 메인 스레드에서 호출됨</summary>
        public abstract JToken Execute(Document doc, JObject args);

        protected static JObject TextContent(string text) => new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } }
        };

        protected static JObject ErrorContent(string msg) => new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"오류: {msg}" } },
            ["isError"] = true
        };
    }
}
