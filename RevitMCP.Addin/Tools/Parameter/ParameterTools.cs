using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace RevitMCP.Addin.Tools.Params
{
    public class SetParameterTool : ToolBase
    {
        public override string Name => "set_parameter";
        public override string Description => "요소의 특정 파라미터 값을 설정합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId", "paramName", "value" },
                ["properties"] = new JObject
                {
                    ["elementId"] = new JObject { ["type"] = "integer" },
                    ["paramName"] = new JObject { ["type"] = "string" },
                    ["value"] = new JObject { ["type"] = "string", ["description"] = "설정할 값 (문자열로 전달)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var id = new ElementId(args["elementId"]!.ToObject<long>());
            var elem = doc.GetElement(id) ?? throw new System.Exception("요소 없음");
            var paramName = args["paramName"]!.ToString();
            var value = args["value"]!.ToString();

            var param = elem.LookupParameter(paramName)
                ?? throw new System.Exception($"파라미터 '{paramName}' 없음");

            if (param.IsReadOnly) return ErrorContent($"파라미터 '{paramName}'은 읽기 전용입니다.");

            using var tx = new Transaction(doc, "MCP: 파라미터 설정");
            tx.Start();
            switch (param.StorageType)
            {
                case StorageType.String:
                    param.Set(value);
                    break;
                case StorageType.Double:
                    param.Set(double.Parse(value));
                    break;
                case StorageType.Integer:
                    param.Set(int.Parse(value));
                    break;
                case StorageType.ElementId:
                    param.Set(new ElementId(int.Parse(value)));
                    break;
            }
            tx.Commit();
            return TextContent($"파라미터 '{paramName}' = '{value}' 설정 완료");
        }
    }

    public class BulkSetParametersTool : ToolBase
    {
        public override string Name => "bulk_set_parameters";
        public override string Description => "여러 요소에 동일한 파라미터 값을 일괄 설정합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementIds", "parameters" },
                ["properties"] = new JObject
                {
                    ["elementIds"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "integer" }
                    },
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "파라미터명: 값 딕셔너리",
                        ["additionalProperties"] = new JObject { ["type"] = "string" }
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var ids = args["elementIds"]!.ToObject<long[]>()!;
            var parameters = args["parameters"]!.ToObject<Dictionary<string, string>>()!;
            int success = 0;

            using var tx = new Transaction(doc, "MCP: 파라미터 일괄 설정");
            tx.Start();
            foreach (var id in ids)
            {
                var elem = doc.GetElement(new ElementId(id));
                if (elem == null) continue;
                foreach (var kvp in parameters)
                {
                    var param = elem.LookupParameter(kvp.Key);
                    if (param == null || param.IsReadOnly) continue;
                    try
                    {
                        switch (param.StorageType)
                        {
                            case StorageType.String: param.Set(kvp.Value); break;
                            case StorageType.Double: param.Set(double.Parse(kvp.Value)); break;
                            case StorageType.Integer: param.Set(int.Parse(kvp.Value)); break;
                        }
                        success++;
                    }
                    catch { }
                }
            }
            tx.Commit();
            return TextContent($"{success}개 파라미터 값 설정 완료");
        }
    }

    public class FilterElementsByParameterTool : ToolBase
    {
        public override string Name => "filter_elements_by_parameter";
        public override string Description => "파라미터 값 조건으로 요소를 필터링합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "paramName", "value" },
                ["properties"] = new JObject
                {
                    ["paramName"] = new JObject { ["type"] = "string" },
                    ["value"] = new JObject { ["type"] = "string" },
                    ["category"] = new JObject { ["type"] = "string" },
                    ["operator"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray { "equals", "contains", "startsWith" },
                        ["description"] = "비교 연산자 (기본: equals)"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var paramName = args["paramName"]!.ToString();
            var value = args["value"]!.ToString();
            var op = args["operator"]?.ToString() ?? "equals";

            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            if (args["category"] is JToken cat && System.Enum.TryParse<BuiltInCategory>(cat.ToString(), out var bic))
                collector = collector.OfCategory(bic);

            var result = collector
                .Where(e =>
                {
                    var p = e.LookupParameter(paramName);
                    if (p == null) return false;
                    var v = p.AsValueString() ?? p.AsString() ?? "";
                    return op switch
                    {
                        "contains" => v.Contains(value),
                        "startsWith" => v.StartsWith(value),
                        _ => v == value
                    };
                })
                .Select(e => new JObject
                {
                    ["id"] = e.Id.Value,
                    ["name"] = e.Name,
                    ["category"] = e.Category?.Name ?? ""
                })
                .ToList();

            return TextContent(new JArray(result).ToString());
        }
    }

    public class ExportParametersTool : ToolBase
    {
        public override string Name => "export_parameters_to_csv";
        public override string Description => "요소들의 파라미터 값을 CSV 파일로 내보냅니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "category", "outputPath" },
                ["properties"] = new JObject
                {
                    ["category"] = new JObject { ["type"] = "string" },
                    ["outputPath"] = new JObject { ["type"] = "string", ["description"] = "CSV 파일 경로" },
                    ["paramNames"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "내보낼 파라미터 이름 목록"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            if (!System.Enum.TryParse<BuiltInCategory>(args["category"]!.ToString(), out var bic))
                return ErrorContent("잘못된 카테고리");

            var outputPath = args["outputPath"]!.ToString();
            var paramNames = args["paramNames"]?.ToObject<string[]>() ?? System.Array.Empty<string>();

            var elements = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToList();

            // 파라미터 이름이 없으면 첫 요소에서 수집
            if (paramNames.Length == 0 && elements.Count > 0)
                paramNames = elements[0].Parameters.Cast<Parameter>()
                    .Where(p => p.Definition != null)
                    .Select(p => p.Definition.Name)
                    .ToArray();

            using var sw = new StreamWriter(outputPath, false, System.Text.Encoding.UTF8);
            sw.WriteLine("ElementId,Name," + string.Join(",", paramNames));

            foreach (var elem in elements)
            {
                var values = paramNames.Select(pn =>
                {
                    var p = elem.LookupParameter(pn);
                    var v = p?.AsValueString() ?? p?.AsString() ?? "";
                    return $"\"{v.Replace("\"", "\"\"")}\"";
                });
                sw.WriteLine($"{elem.Id.Value},{elem.Name},{string.Join(",", values)}");
            }

            return TextContent($"{elements.Count}개 요소를 '{outputPath}'에 내보냄");
        }
    }
}
