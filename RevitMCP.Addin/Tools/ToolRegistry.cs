using Autodesk.Revit.UI;
using RevitMCP.Addin.Tools.Element;
using RevitMCP.Addin.Tools.Modeling;
using RevitMCP.Addin.Tools.Document;
using RevitMCP.Addin.Tools.View;
using RevitMCP.Addin.Tools.Parameter;
using System.Collections.Generic;

namespace RevitMCP.Addin.Tools
{
    public class ToolRegistry
    {
        private readonly Dictionary<string, ToolBase> _tools = new();

        public void Initialize(UIApplication app)
        {
            Register(
                // ── 요소 조회/조작 ──
                new GetElementsTool(),
                new GetElementParametersTool(),
                new SetParameterTool(),
                new DeleteElementTool(),
                new SelectElementsTool(),

                // ── 모델링 자동화 ──
                new CreateWallTool(),
                new CreateFloorTool(),
                new CreateRoomTool(),
                new CreateGridTool(),
                new CreateLevelTool(),
                new PlaceFamilyInstanceTool(),
                new MoveElementTool(),
                new CopyElementTool(),
                new RotateElementTool(),
                new MirrorElementTool(),
                new CreateDimensionTool(),

                // ── 도서 자동화 ──
                new CreateSheetTool(),
                new CreateViewTool(),
                new PlaceViewportTool(),
                new CreateScheduleTool(),
                new ExportSheetsTool(),
                new AddRevisionTool(),
                new CreateTextNoteTool(),
                new TagElementTool(),

                // ── 파라미터 관리 ──
                new BulkSetParametersTool(),
                new FilterElementsByParameterTool(),
                new ExportParametersTool(),

                // ── 분석 / 검토 ──
                new GetModelInfoTool(),
                new ClashDetectionTool(),
                new GetWarningsTool(),
                new MaterialTakeoffTool(),
                new RunDynamo()
            );
        }

        private void Register(params ToolBase[] tools)
        {
            foreach (var t in tools)
                _tools[t.Name] = t;
        }

        public ToolBase? Get(string name) => _tools.TryGetValue(name, out var t) ? t : null;
        public IEnumerable<ToolBase> GetAll() => _tools.Values;
    }
}
