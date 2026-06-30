using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCP.Addin.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RevitMCP.Addin.Server
{
    /// <summary>
    /// JSON-RPC 2.0 / MCP HTTP 서버.
    /// Revit API 스레드 위임은 ExternalEventHandler 패턴 사용.
    /// </summary>
    public class MCPServer
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private UIApplication? _uiApp;
        private readonly ToolRegistry _registry;

        public bool IsRunning { get; private set; }

        public MCPServer()
        {
            _registry = new ToolRegistry();
        }

        public void Start(UIApplication uiApp)
        {
            if (IsRunning) return;
            _uiApp = uiApp;
            _registry.Initialize(uiApp);

            RevitEventDispatcher.Initialize(uiApp);

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Config.Port}/");
            _listener.Start();

            _cts = new CancellationTokenSource();
            Task.Run(() => ListenLoop(_cts.Token));

            IsRunning = true;
            Logger.Info($"MCP 서버 시작: 포트 {Config.Port}");
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            IsRunning = false;
            Logger.Info("MCP 서버 중지");
        }

        // ── HTTP 루프 ──────────────────────────────────────────────

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _listener!.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(ctx), ct);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Logger.Error($"ListenLoop: {ex.Message}"); }
            }
        }

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (ctx.Request.HttpMethod == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }

            if (ctx.Request.HttpMethod == "GET" && ctx.Request.Url?.AbsolutePath == "/health")
            {
                await WriteJson(ctx.Response, new { status = "ok", revit = Config.RevitVersion, port = Config.Port });
                return;
            }

            if (ctx.Request.HttpMethod != "POST")
            {
                ctx.Response.StatusCode = 405;
                ctx.Response.Close();
                return;
            }

            string body;
            using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = await sr.ReadToEndAsync();

            JObject? response = null;
            try
            {
                var request = JObject.Parse(body);
                response = await DispatchRpc(request);
            }
            catch (Exception ex)
            {
                response = RpcError(null, -32700, $"Parse error: {ex.Message}");
            }

            await WriteJson(ctx.Response, response!);
        }

        // ── JSON-RPC 2.0 디스패처 ──────────────────────────────────

        private async Task<JObject> DispatchRpc(JObject req)
        {
            var id = req["id"];
            var method = req["method"]?.ToString();
            var params_ = req["params"] as JObject ?? new JObject();

            try
            {
                JToken result = method switch
                {
                    "initialize" => HandleInitialize(params_),
                    "tools/list" => HandleToolsList(),
                    "tools/call" => await HandleToolCall(params_),
                    "ping" => new JObject { ["pong"] = true },
                    _ => throw new RpcException(-32601, $"Method not found: {method}")
                };

                return new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = result
                };
            }
            catch (RpcException rex)
            {
                return RpcError(id, rex.Code, rex.Message);
            }
            catch (Exception ex)
            {
                Logger.Error($"DispatchRpc [{method}]: {ex}");
                return RpcError(id, -32603, ex.Message);
            }
        }

        private JToken HandleInitialize(JObject _) => new JObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JObject { ["tools"] = new JObject() },
            ["serverInfo"] = new JObject
            {
                ["name"] = "revit-mcp",
                ["version"] = "1.0.0",
                ["revitVersion"] = Config.RevitVersion
            }
        };

        private JToken HandleToolsList()
        {
            var arr = new JArray();
            foreach (var tool in _registry.GetAll())
                arr.Add(tool.GetSchema());
            return new JObject { ["tools"] = arr };
        }

        private async Task<JToken> HandleToolCall(JObject params_)
        {
            var name = params_["name"]?.ToString()
                ?? throw new RpcException(-32602, "name 파라미터 필수");
            var args = params_["arguments"] as JObject ?? new JObject();

            var tool = _registry.Get(name)
                ?? throw new RpcException(-32602, $"알 수 없는 도구: {name}");

            // Revit API 작업은 External Event를 통해 메인 스레드에서 실행
            var tcs = new TaskCompletionSource<JToken>();
            RevitEventDispatcher.Dispatch(_uiApp!, doc =>
            {
                try { tcs.SetResult(tool.Execute(doc, args)); }
                catch (Exception ex) { tcs.SetException(ex); }
            });

            return await tcs.Task;
        }

        // ── 유틸 ──────────────────────────────────────────────────

        private static JObject RpcError(JToken? id, int code, string msg) => new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JObject { ["code"] = code, ["message"] = msg }
        };

        private static async Task WriteJson(HttpListenerResponse resp, object data)
        {
            var json = JsonConvert.SerializeObject(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentType = "application/json; charset=utf-8";
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            resp.Close();
        }
    }

    internal class RpcException : Exception
    {
        public int Code { get; }
        public RpcException(int code, string message) : base(message) => Code = code;
    }
}
