using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCP.Addin.Server;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Addin.Tools.Views
{
    public class GetSheetsTool : ToolBase
    {
        public override string Name => "get_sheets";
        public override string Description => "모든 도면 시트 목록과 배치된 뷰 정보를 반환합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber)
                .Select(s => new JObject
                {
                    ["sheetNumber"] = s.SheetNumber,
                    ["sheetName"] = s.Name,
                    ["id"] = s.Id.Value,
                    ["views"] = new JArray(s.GetAllPlacedViews()
                        .Select(vid => doc.GetElement(vid))
                        .Where(v => v != null)
                        .Select(v => v.Name))
                });
            return TextContent(new JArray(sheets).ToString());
        }
    }

    public class SetViewScaleTool : ToolBase
    {
        public override string Name => "set_view_scale";
        public override string Description => "뷰의 축척을 변경합니다. scale은 분모 값 (예: 100 = 1:100).";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "viewName", "scale" },
                ["properties"] = new JObject
                {
                    ["viewName"] = new JObject { ["type"] = "string" },
                    ["scale"] = new JObject { ["type"] = "integer", ["description"] = "분모 (예: 100 → 1:100)" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var name = args["viewName"]!.ToString();
            var scale = args["scale"]!.ToObject<int>();
            var view = new FilteredElementCollector(doc).OfClass(typeof(View))
                .Cast<View>().FirstOrDefault(v => !v.IsTemplate && v.Name == name)
                ?? throw new Exception($"뷰 없음: {name}");
            using var tx = new Transaction(doc, "MCP: 축척 변경");
            tx.Start();
            view.Scale = scale;
            tx.Commit();
            return TextContent($"축척 변경 완료: {name} → 1:{scale}");
        }
    }

    public class SetCropRegionTool : ToolBase
    {
        public override string Name => "set_crop_region";
        public override string Description => "뷰의 자르기 영역을 활성화/비활성화하거나 경계를 설정합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "viewName", "enabled" },
                ["properties"] = new JObject
                {
                    ["viewName"] = new JObject { ["type"] = "string" },
                    ["enabled"] = new JObject { ["type"] = "boolean" },
                    ["minX"] = new JObject { ["type"] = "number", ["description"] = "mm" },
                    ["minY"] = new JObject { ["type"] = "number", ["description"] = "mm" },
                    ["maxX"] = new JObject { ["type"] = "number", ["description"] = "mm" },
                    ["maxY"] = new JObject { ["type"] = "number", ["description"] = "mm" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var name = args["viewName"]!.ToString();
            var enabled = args["enabled"]!.ToObject<bool>();
            var view = new FilteredElementCollector(doc).OfClass(typeof(View))
                .Cast<View>().FirstOrDefault(v => !v.IsTemplate && v.Name == name)
                ?? throw new Exception($"뷰 없음: {name}");
            using var tx = new Transaction(doc, "MCP: 자르기 영역");
            tx.Start();
            view.CropBoxActive = enabled;
            view.CropBoxVisible = enabled;
            if (enabled && args["minX"] != null)
            {
                double f = 1.0 / 304.8;
                var bb = view.CropBox;
                bb.Min = new XYZ(args["minX"]!.ToObject<double>() * f, args["minY"]!.ToObject<double>() * f, bb.Min.Z);
                bb.Max = new XYZ(args["maxX"]!.ToObject<double>() * f, args["maxY"]!.ToObject<double>() * f, bb.Max.Z);
                view.CropBox = bb;
            }
            tx.Commit();
            return TextContent($"자르기 영역 {(enabled ? "활성화" : "비활성화")}: {name}");
        }
    }

    public class GetActiveViewInfoTool : ToolBase
    {
        public override string Name => "get_active_view_info";
        public override string Description => "현재 활성 뷰의 상세 정보(이름, 타입, 축척, 레벨 등)를 반환합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var uiApp = RevitEventDispatcher.CurrentApp ?? throw new Exception("UIApplication 없음");
            var view = uiApp.ActiveUIDocument.ActiveView;
            var result = new JObject
            {
                ["name"] = view.Name,
                ["viewType"] = view.ViewType.ToString(),
                ["scale"] = view.Scale,
                ["id"] = view.Id.Value,
                ["cropBoxActive"] = view.CropBoxActive,
                ["detailLevel"] = view.DetailLevel.ToString()
            };
            if (view is ViewPlan vp)
                result["associatedLevel"] = vp.GenLevel?.Name;
            return TextContent(result.ToString());
        }
    }

    public class SetViewDetailLevelTool : ToolBase
    {
        public override string Name => "set_view_detail_level";
        public override string Description => "뷰의 상세 수준을 변경합니다. level: Coarse, Medium, Fine";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "viewName", "level" },
                ["properties"] = new JObject
                {
                    ["viewName"] = new JObject { ["type"] = "string" },
                    ["level"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Coarse", "Medium", "Fine" } }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var name = args["viewName"]!.ToString();
            var levelStr = args["level"]!.ToString();
            var view = new FilteredElementCollector(doc).OfClass(typeof(View))
                .Cast<View>().FirstOrDefault(v => !v.IsTemplate && v.Name == name)
                ?? throw new Exception($"뷰 없음: {name}");
            var detailLevel = Enum.Parse<ViewDetailLevel>(levelStr);
            using var tx = new Transaction(doc, "MCP: 상세 수준");
            tx.Start();
            view.DetailLevel = detailLevel;
            tx.Commit();
            return TextContent($"상세 수준 변경: {name} → {levelStr}");
        }
    }

    public class HideElementsInViewTool : ToolBase
    {
        public override string Name => "hide_elements_in_view";
        public override string Description => "현재 뷰에서 지정한 요소들을 숨깁니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementIds" },
                ["properties"] = new JObject
                {
                    ["elementIds"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } },
                    ["viewName"] = new JObject { ["type"] = "string", ["description"] = "미지정 시 활성 뷰" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var ids = args["elementIds"]!.ToObject<List<long>>()!
                .Select(id => new ElementId(id)).ToList();
            View view;
            var viewName = args["viewName"]?.ToString();
            if (viewName != null)
                view = new FilteredElementCollector(doc).OfClass(typeof(View))
                    .Cast<View>().First(v => !v.IsTemplate && v.Name == viewName);
            else
                view = RevitEventDispatcher.CurrentApp!.ActiveUIDocument.ActiveView;
            using var tx = new Transaction(doc, "MCP: 요소 숨기기");
            tx.Start();
            view.HideElements(ids);
            tx.Commit();
            return TextContent($"{ids.Count}개 요소 숨김 완료");
        }
    }

    public class UnhideElementsInViewTool : ToolBase
    {
        public override string Name => "unhide_elements_in_view";
        public override string Description => "뷰에서 숨겨진 요소를 다시 표시합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementIds" },
                ["properties"] = new JObject
                {
                    ["elementIds"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } },
                    ["viewName"] = new JObject { ["type"] = "string" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var ids = args["elementIds"]!.ToObject<List<long>>()!
                .Select(id => new ElementId(id)).ToList();
            View view;
            var viewName = args["viewName"]?.ToString();
            if (viewName != null)
                view = new FilteredElementCollector(doc).OfClass(typeof(View))
                    .Cast<View>().First(v => !v.IsTemplate && v.Name == viewName);
            else
                view = RevitEventDispatcher.CurrentApp!.ActiveUIDocument.ActiveView;
            using var tx = new Transaction(doc, "MCP: 요소 표시");
            tx.Start();
            view.UnhideElements(ids);
            tx.Commit();
            return TextContent($"{ids.Count}개 요소 표시 완료");
        }
    }

    public class IsolateCategoryInViewTool : ToolBase
    {
        public override string Name => "isolate_category_in_view";
        public override string Description => "뷰에서 특정 카테고리만 격리 표시하거나 격리를 해제합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "category" },
                ["properties"] = new JObject
                {
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "예: OST_Walls" },
                    ["reset"] = new JObject { ["type"] = "boolean", ["description"] = "true면 격리 해제" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var uiApp = RevitEventDispatcher.CurrentApp ?? throw new Exception("UIApplication 없음");
            var view = uiApp.ActiveUIDocument.ActiveView;
            var reset = args["reset"]?.ToObject<bool>() ?? false;
            using var tx = new Transaction(doc, "MCP: 카테고리 격리");
            tx.Start();
            if (reset)
                view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            else
            {
                var catStr = args["category"]!.ToString();
                BuiltInCategory bic = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), catStr);
                var ids = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(bic).ToElementIds();
                if (ids.Count == 0) throw new Exception($"카테고리 '{catStr}' 요소 없음");
                view.IsolateElementsTemporary(ids);
            }
            tx.Commit();
            return TextContent(reset ? "격리 해제 완료" : $"카테고리 격리 완료");
        }
    }
}
