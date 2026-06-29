using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Addin.Tools.Element
{
    public class GetElementsTool : ToolBase
    {
        public override string Name => "get_elements";
        public override string Description => "카테고리, 레벨, 파라미터 필터로 Revit 요소 목록을 조회합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "BuiltInCategory 이름 (예: OST_Walls, OST_Doors)" },
                    ["levelName"] = new JObject { ["type"] = "string", ["description"] = "레벨 이름으로 필터" },
                    ["limit"] = new JObject { ["type"] = "integer", ["description"] = "최대 반환 개수 (기본 100)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var categoryName = args["category"]?.ToString();
            var levelName = args["levelName"]?.ToString();
            var limit = args["limit"]?.ToObject<int>() ?? 100;

            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();

            if (!string.IsNullOrEmpty(categoryName))
            {
                if (System.Enum.TryParse<BuiltInCategory>(categoryName, out var bic))
                    collector = collector.OfCategory(bic);
            }

            IEnumerable<Autodesk.Revit.DB.Element> elements = collector;

            if (!string.IsNullOrEmpty(levelName))
            {
                var level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault(l => l.Name == levelName);
                if (level != null)
                    elements = elements.Where(e =>
                    {
                        var lvlParam = e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                                    ?? e.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                        return lvlParam?.AsElementId() == level.Id;
                    });
            }

            var result = elements.Take(limit).Select(e => new JObject
            {
                ["id"] = e.Id.IntegerValue,
                ["name"] = e.Name,
                ["category"] = e.Category?.Name ?? "",
                ["typeName"] = doc.GetElement(e.GetTypeId())?.Name ?? ""
            });

            return TextContent(new JArray(result).ToString());
        }
    }

    public class GetElementParametersTool : ToolBase
    {
        public override string Name => "get_element_parameters";
        public override string Description => "특정 요소의 모든 파라미터 값을 반환합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId" },
                ["properties"] = new JObject
                {
                    ["elementId"] = new JObject { ["type"] = "integer", ["description"] = "요소 ID" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var id = new ElementId(args["elementId"]!.ToObject<int>());
            var elem = doc.GetElement(id);
            if (elem == null) return ErrorContent("요소를 찾을 수 없습니다.");

            var arr = new JArray();
            foreach (Parameter p in elem.Parameters)
            {
                if (p.Definition == null) continue;
                arr.Add(new JObject
                {
                    ["name"] = p.Definition.Name,
                    ["value"] = p.AsValueString() ?? p.AsString() ?? p.AsDouble().ToString(),
                    ["type"] = p.StorageType.ToString(),
                    ["isReadOnly"] = p.IsReadOnly
                });
            }
            return TextContent(arr.ToString());
        }
    }

    public class DeleteElementTool : ToolBase
    {
        public override string Name => "delete_element";
        public override string Description => "Revit 요소를 삭제합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementIds" },
                ["properties"] = new JObject
                {
                    ["elementIds"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "integer" },
                        ["description"] = "삭제할 요소 ID 목록"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var ids = args["elementIds"]!.ToObject<int[]>()!
                .Select(i => new ElementId(i)).ToList();
            using var tx = new Transaction(doc, "MCP: 요소 삭제");
            tx.Start();
            doc.Delete(ids);
            tx.Commit();
            return TextContent($"{ids.Count}개 요소 삭제 완료");
        }
    }

    public class SelectElementsTool : ToolBase
    {
        public override string Name => "select_elements";
        public override string Description => "Revit UI에서 지정한 요소들을 선택 상태로 만듭니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementIds" },
                ["properties"] = new JObject
                {
                    ["elementIds"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "integer" }
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var uiDoc = App.UiApp?.ActiveUIDocument;
            if (uiDoc == null) return ErrorContent("활성 문서 없음");

            var ids = args["elementIds"]!.ToObject<int[]>()!
                .Select(i => new ElementId(i)).ToList();
            uiDoc.Selection.SetElementIds(ids);
            return TextContent($"{ids.Count}개 요소 선택됨");
        }
    }
}
