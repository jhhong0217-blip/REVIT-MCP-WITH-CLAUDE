using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Addin.Server;

namespace RevitMCP.Addin.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleMCPCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var server = App.Server;
            if (server == null) return Result.Failed;

            if (server.IsRunning)
            {
                server.Stop();
                App.Instance?.RefreshButtonState(false);
                TaskDialog.Show("RevitMCP", "MCP 서버가 중지되었습니다.");
            }
            else
            {
                server.Start(commandData.Application);
                App.Instance?.RefreshButtonState(true);
                TaskDialog.Show("RevitMCP",
                    $"MCP 서버가 시작되었습니다.\n포트: {Config.Port}\nRevit 버전: {Config.RevitVersion}\n\n" +
                    "Claude Code 또는 다른 MCP 클라이언트에서 연결하세요.");
            }

            return Result.Succeeded;
        }
    }
}
