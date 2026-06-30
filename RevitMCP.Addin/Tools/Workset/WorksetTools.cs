using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace RevitMCP.Addin.Tools.Workset
{
    public class GetWorksetsTool : ToolBase
    {
        public override string Name => "get_worksets";
        public override string Description => "프로젝트의 작업세트 목록을 반환합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            if (!doc.IsWorkshared) return TextContent("이 프로젝트는 작업분담(Worksharing)이 활성화되지 않았습니다.");

            var worksets = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .Select(w => new JObject
                {
                    ["id"]         = (long)w.Id.IntegerValue,
                    ["name"]       = w.Name,
                    ["isOpen"]     = w.IsOpen,
                    ["isEditable"] = w.IsEditable,
                    ["owner"]      = w.Owner
                }).ToList();

            return TextContent($"작업세트 {worksets.Count}개\n{new JArray(worksets)}");
        }
    }

    public class CreateWorksetTool : ToolBase
    {
        public override string Name => "create_workset";
        public override string Description => "새 작업세트를 생성합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "worksetName" },
                ["properties"] = new JObject
                {
                    ["worksetName"] = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            if (!doc.IsWorkshared) return ErrorContent("작업분담이 활성화되지 않은 프로젝트입니다.");

            using var tx = new Transaction(doc, "MCP: 작업세트 생성");
            tx.Start();
            var ws = Autodesk.Revit.DB.Workset.Create(doc, args["worksetName"]!.ToString());
            tx.Commit();

            return TextContent($"작업세트 '{ws.Name}' 생성 완료 (ID: {ws.Id.IntegerValue})");
        }
    }

    public class SetElementWorksetTool : ToolBase
    {
        public override string Name => "set_element_workset";
        public override string Description => "요소들을 지정한 작업세트로 이동합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementIds", "worksetName" },
                ["properties"] = new JObject
                {
                    ["elementIds"]  = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } },
                    ["worksetName"] = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            if (!doc.IsWorkshared) return ErrorContent("작업분담이 활성화되지 않은 프로젝트입니다.");

            var wsName = args["worksetName"]!.ToString();
            var ws = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
                .FirstOrDefault(w => w.Name == wsName)
                ?? throw new System.Exception($"작업세트 '{wsName}' 없음");

            var ids = args["elementIds"]!.ToObject<long[]>()!;
            int success = 0;

            using var tx = new Transaction(doc, "MCP: 작업세트 변경");
            tx.Start();
            foreach (var id in ids)
            {
                var elem = doc.GetElement(new ElementId(id));
                var wsParam = elem?.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (wsParam != null && !wsParam.IsReadOnly)
                {
                    wsParam.Set(ws.Id.IntegerValue);
                    success++;
                }
            }
            tx.Commit();

            return TextContent($"{success}개 요소를 작업세트 '{wsName}'으로 이동 완료");
        }
    }

    public class AssignWorksetByCategoryTool : ToolBase
    {
        public override string Name => "assign_workset_by_category";
        public override string Description => "카테고리 전체 요소를 지정 작업세트로 일괄 배정합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "category", "worksetName" },
                ["properties"] = new JObject
                {
                    ["category"]    = new JObject { ["type"] = "string" },
                    ["worksetName"] = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            if (!doc.IsWorkshared) return ErrorContent("작업분담이 활성화되지 않은 프로젝트입니다.");
            if (!System.Enum.TryParse<BuiltInCategory>(args["category"]!.ToString(), out var bic))
                return ErrorContent("잘못된 카테고리");

            var wsName = args["worksetName"]!.ToString();
            var ws = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
                .FirstOrDefault(w => w.Name == wsName)
                ?? throw new System.Exception($"작업세트 '{wsName}' 없음");

            var elements = new FilteredElementCollector(doc)
                .OfCategory(bic).WhereElementIsNotElementType().ToList();

            using var tx = new Transaction(doc, "MCP: 카테고리 작업세트 배정");
            tx.Start();
            int success = 0;
            foreach (var elem in elements)
            {
                var p = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (p != null && !p.IsReadOnly) { p.Set(ws.Id.IntegerValue); success++; }
            }
            tx.Commit();

            return TextContent($"{success}개 요소를 작업세트 '{wsName}'으로 배정 완료");
        }
    }
}
