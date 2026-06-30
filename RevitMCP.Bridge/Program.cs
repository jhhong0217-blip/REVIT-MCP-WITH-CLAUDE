using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

// RevitMCP stdio bridge: Claude Desktop(stdio) <-> Revit HTTP server(localhost:9876)
class Program
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    const string RevitUrl = "http://localhost:9876/";

    static async Task Main()
    {
        Console.InputEncoding  = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        while (true)
        {
            var line = Console.ReadLine();
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var id     = ExtractId(line);
            var method = ExtractMethod(line);

            // initialize: Revit 상태 무관하게 항상 성공 응답
            if (method == "initialize")
            {
                var initResp = $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{" +
                    $"\"protocolVersion\":\"2024-11-05\"," +
                    $"\"capabilities\":{{\"tools\":{{}}}}," +
                    $"\"serverInfo\":{{\"name\":\"revit-mcp-addin\",\"version\":\"1.0.0\"}}}}}}";
                Console.WriteLine(initResp);
                Console.Out.Flush();
                continue;
            }

            // initialized notification: 무시
            if (method == "notifications/initialized")
            {
                continue;
            }

            // 나머지는 Revit HTTP 서버로 전달
            try
            {
                using var content  = new StringContent(line, Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(RevitUrl, content);
                var result = await response.Content.ReadAsStringAsync();
                Console.WriteLine(result);
                Console.Out.Flush();
            }
            catch (HttpRequestException)
            {
                Console.WriteLine($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":-32603,\"message\":\"Revit MCP not running. Open Revit and click [Start MCP] in RevitMCP tab.\"}}}}");
                Console.Out.Flush();
            }
            catch (Exception ex)
            {
                var msg = ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\"");
                Console.WriteLine($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":-32603,\"message\":\"{msg}\"}}}}");
                Console.Out.Flush();
            }
        }
    }

    static string ExtractId(string json)
    {
        var idx = json.IndexOf("\"id\"", StringComparison.Ordinal);
        if (idx < 0) return "null";
        idx += 4;
        while (idx < json.Length && (json[idx] == ':' || json[idx] == ' ')) idx++;
        if (idx >= json.Length) return "null";
        if (json[idx] == '"')
        {
            var end = json.IndexOf('"', idx + 1);
            return end < 0 ? "null" : $"\"{json.Substring(idx + 1, end - idx - 1)}\"";
        }
        var numEnd = idx;
        while (numEnd < json.Length && (char.IsDigit(json[numEnd]) || json[numEnd] == '-')) numEnd++;
        return numEnd > idx ? json.Substring(idx, numEnd - idx) : "null";
    }

    static string ExtractMethod(string json)
    {
        var idx = json.IndexOf("\"method\"", StringComparison.Ordinal);
        if (idx < 0) return "";
        idx += 8;
        while (idx < json.Length && (json[idx] == ':' || json[idx] == ' ')) idx++;
        if (idx >= json.Length || json[idx] != '"') return "";
        idx++;
        var end = json.IndexOf('"', idx);
        return end < 0 ? "" : json.Substring(idx, end - idx);
    }
}
