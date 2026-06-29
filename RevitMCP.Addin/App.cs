using Autodesk.Revit.UI;
using RevitMCP.Addin.Server;
using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace RevitMCP.Addin
{
    /// <summary>
    /// Revit MCP Addin 진입점 — 리본 탭/버튼 생성 + MCP 서버 생명주기 관리
    /// </summary>
    public class App : IExternalApplication
    {
        internal static App? Instance { get; private set; }
        internal static UIControlledApplication? UiApp { get; private set; }
        internal static MCPServer? Server { get; private set; }

        private RibbonItem? _toggleButton;

        public Result OnStartup(UIControlledApplication application)
        {
            Instance = this;
            UiApp = application;

            try
            {
                CreateRibbon(application);
                Server = new MCPServer();
                Logger.Info("RevitMCP Addin 로드 완료");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error($"OnStartup 실패: {ex}");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            Server?.Stop();
            return Result.Succeeded;
        }

        // ── 리본 UI ──────────────────────────────────────────────

        private void CreateRibbon(UIControlledApplication app)
        {
            const string tabName = "RevitMCP";
            app.CreateRibbonTab(tabName);

            var panel = app.CreateRibbonPanel(tabName, "MCP 서버");

            var buttonData = new PushButtonData(
                "ToggleMCP",
                "MCP\n시작",
                Assembly.GetExecutingAssembly().Location,
                "RevitMCP.Addin.Commands.ToggleMCPCommand")
            {
                ToolTip = "MCP 서버를 시작/중지합니다",
                LongDescription = "Claude 등 AI 에이전트가 Revit 모델에 접근할 수 있도록 로컬 MCP 서버를 켜거나 끕니다.",
                Image = LoadImage("off_16.png"),
                LargeImage = LoadImage("off_32.png"),
            };

            _toggleButton = panel.AddItem(buttonData);
        }

        private static BitmapImage LoadImage(string name)
        {
            // 리소스가 없으면 기본 아이콘 반환
            var uri = new Uri($"pack://application:,,,/RevitMCP.Addin;component/Resources/{name}", UriKind.Absolute);
            try { return new BitmapImage(uri); }
            catch { return new BitmapImage(); }
        }

        // ── 버튼 상태 갱신 ────────────────────────────────────────

        internal void RefreshButtonState(bool isRunning)
        {
            if (_toggleButton is not PushButton btn) return;
            btn.ItemText = isRunning ? "MCP\n중지" : "MCP\n시작";
            btn.ToolTip = isRunning
                ? $"MCP 서버 실행 중 (포트 {Config.Port}) — 클릭하여 중지"
                : "MCP 서버가 중지됨 — 클릭하여 시작";
            btn.LargeImage = LoadImage(isRunning ? "on_32.png" : "off_32.png");
            btn.Image = LoadImage(isRunning ? "on_16.png" : "off_16.png");
        }
    }
}
