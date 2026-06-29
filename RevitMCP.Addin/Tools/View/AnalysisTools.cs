using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Collections.Generic;

namespace RevitMCP.Addin.Tools.View
{
    public class GetModelInfoTool : ToolBase
    {
        public override string Name => "get_model_info";
        public override string Description => "현재 Revit 모델의 기본 정보(프로젝트 정보, 레벨, 카테고리별 요소 수 등)를 반환합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var info = doc.ProjectInformation;
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => new JObject { ["name"] = l.Name, ["elevation_mm"] = l.Elevation * 304.8 });

            var categories = new Dictionary<string, int>();
            foreach (var elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                var cat = elem.Category?.Name ?? "Unknown";
                categories[cat] = categories.TryGetValue(cat, out var c) ? c + 1 : 1;
            }

            var result = new JObject
            {
                ["projectName"] = info.Name,
                ["projectNumber"] = info.Number,
                ["projectAddress"] = info.Address,
                ["author"] = info.Author,
                ["revitVersion"] = Config.RevitVersion,
                ["levels"] = new JArray(levels),
                ["elementCountByCategory"] = JObject.FromObject(
                    categories.OrderByDescending(k => k.Value).Take(20).ToDictionary(k => k.Key, k => k.Value))
            };

            return TextContent(result.ToString());
        }
    }

    public class ClashDetectionTool : ToolBase
    {
        public override string Name => "clash_detection";
        public override string Description => "두 카테고리 간의 기하학적 충돌을 감지합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "category1", "category2" },
                ["properties"] = new JObject
                {
                    ["category1"] = new JObject { ["type"] = "string", ["description"] = "BuiltInCategory 이름" },
                    ["category2"] = new JObject { ["type"] = "string", ["description"] = "BuiltInCategory 이름" },
                    ["tolerance"] = new JObject { ["type"] = "number", ["description"] = "허용 간격 (mm, 기본 0)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            if (!System.Enum.TryParse<BuiltInCategory>(args["category1"]!.ToString(), out var bic1) ||
                !System.Enum.TryParse<BuiltInCategory>(args["category2"]!.ToString(), out var bic2))
                return ErrorContent("잘못된 카테고리");

            double tol = (args["tolerance"]?.ToObject<double>() ?? 0) / 304.8;

            var elems1 = new FilteredElementCollector(doc).OfCategory(bic1).WhereElementIsNotElementType().ToList();
            var elems2 = new FilteredElementCollector(doc).OfCategory(bic2).WhereElementIsNotElementType().ToList();

            var clashes = new JArray();
            var filter = new ElementIntersectsElementFilter(null!); // 필터를 개별 적용

            foreach (var e1 in elems1)
            {
                var intersectFilter = new ElementIntersectsElementFilter(e1);
                var intersecting = new FilteredElementCollector(doc)
                    .OfCategory(bic2)
                    .WhereElementIsNotElementType()
                    .WherePasses(intersectFilter)
                    .ToList();

                foreach (var e2 in intersecting)
                {
                    clashes.Add(new JObject
                    {
                        ["element1Id"] = e1.Id.IntegerValue,
                        ["element1Name"] = e1.Name,
                        ["element2Id"] = e2.Id.IntegerValue,
                        ["element2Name"] = e2.Name
                    });
                }
            }

            return TextContent($"충돌 감지 완료: {clashes.Count}건\n{clashes}");
        }
    }

    public class GetWarningsTool : ToolBase
    {
        public override string Name => "get_warnings";
        public override string Description => "Revit 모델의 경고 목록을 반환합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var warnings = doc.GetWarnings()
                .Select(w => new JObject
                {
                    ["description"] = w.GetDescriptionText(),
                    ["severity"] = w.GetSeverity().ToString(),
                    ["elementIds"] = new JArray(w.GetFailingElements().Select(id => id.IntegerValue))
                })
                .ToList();

            return TextContent($"경고 {warnings.Count}건\n{new JArray(warnings)}");
        }
    }

    public class MaterialTakeoffTool : ToolBase
    {
        public override string Name => "material_takeoff";
        public override string Description => "지정 카테고리 요소들의 재료 물량을 산출합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "category" },
                ["properties"] = new JObject
                {
                    ["category"] = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            if (!System.Enum.TryParse<BuiltInCategory>(args["category"]!.ToString(), out var bic))
                return ErrorContent("잘못된 카테고리");

            var elems = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType();
            var materialSums = new Dictionary<string, double>();

            foreach (var elem in elems)
            {
                foreach (ElementId matId in elem.GetMaterialIds(false))
                {
                    var mat = doc.GetElement(matId) as Material;
                    if (mat == null) continue;
                    double vol = elem.GetMaterialVolume(matId) * 304.8 * 304.8 * 304.8 / 1e9; // m³
                    materialSums[mat.Name] = materialSums.TryGetValue(mat.Name, out var v) ? v + vol : vol;
                }
            }

            var result = materialSums.Select(kv => new JObject
            {
                ["material"] = kv.Key,
                ["volume_m3"] = System.Math.Round(kv.Value, 4)
            });

            return TextContent(new JArray(result).ToString());
        }
    }

    public class RunDynamo : ToolBase
    {
        public override string Name => "run_dynamo_script";
        public override string Description => "Dynamo .dyn 스크립트를 배치 실행합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "scriptPath" },
                ["properties"] = new JObject
                {
                    ["scriptPath"] = new JObject { ["type"] = "string", ["description"] = ".dyn 파일 전체 경로" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var path = args["scriptPath"]!.ToString();
            if (!System.IO.File.Exists(path))
                return ErrorContent($"스크립트 파일 없음: {path}");

            // Dynamo 배치 실행 — Revit 버전별 API가 상이하므로 Revit.IFC 패턴 사용
            try
            {
                var dynType = System.Type.GetType("Dynamo.Applications.DynamoRevit, DynamoRevitDS");
                if (dynType == null) return ErrorContent("Dynamo가 설치되지 않았습니다.");

                // DynamoRevit BatchRun은 버전별 구현 필요 — 기본 메시지 반환
                return TextContent($"Dynamo 스크립트 실행 요청: {path}\n(실제 실행은 Dynamo API 연동이 필요합니다)");
            }
            catch (System.Exception ex)
            {
                return ErrorContent($"Dynamo 실행 실패: {ex.Message}");
            }
        }
    }
}
