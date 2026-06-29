using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.IO;

namespace RevitMCP.Addin.Tools.Family
{
    // ── 패밀리 목록 조회 ──────────────────────────────────────────
    public class ListFamiliesTool : ToolBase
    {
        public override string Name => "list_families";
        public override string Description => "프로젝트에 로드된 모든 패밀리와 타입 목록을 반환합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "BuiltInCategory 필터 (선택)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var collector = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>();

            if (args["category"] is JToken cat && System.Enum.TryParse<BuiltInCategory>(cat.ToString(), out var bic))
                collector = collector.Where(f => f.FamilyCategory?.Id == new ElementId(bic));

            var result = collector.Select(f => new JObject
            {
                ["familyId"]   = f.Id.IntegerValue,
                ["familyName"] = f.Name,
                ["category"]   = f.FamilyCategory?.Name ?? "",
                ["types"]      = new JArray(f.GetFamilySymbolIds()
                    .Select(id => doc.GetElement(id) as FamilySymbol)
                    .Where(s => s != null)
                    .Select(s => new JObject
                    {
                        ["typeId"]   = s!.Id.IntegerValue,
                        ["typeName"] = s.Name
                    }))
            }).ToList();

            return TextContent($"패밀리 {result.Count}개\n{new JArray(result)}");
        }
    }

    // ── 패밀리 로드 ───────────────────────────────────────────────
    public class LoadFamilyTool : ToolBase
    {
        public override string Name => "load_family";
        public override string Description => ".rfa 파일에서 패밀리를 프로젝트에 로드합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "filePath" },
                ["properties"] = new JObject
                {
                    ["filePath"] = new JObject { ["type"] = "string", ["description"] = ".rfa 파일 전체 경로" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var path = args["filePath"]!.ToString();
            if (!File.Exists(path)) return ErrorContent($"파일 없음: {path}");

            using var tx = new Transaction(doc, "MCP: 패밀리 로드");
            tx.Start();
            doc.LoadFamily(path, out var family);
            tx.Commit();

            if (family == null) return ErrorContent("패밀리 로드 실패 (이미 로드됐거나 호환되지 않음)");
            return TextContent($"패밀리 '{family.Name}' 로드 완료 (ID: {family.Id.IntegerValue})");
        }
    }

    // ── 패밀리 타입 일괄 교체 ─────────────────────────────────────
    public class ReplaceFamilyTypeTool : ToolBase
    {
        public override string Name => "replace_family_type";
        public override string Description => "특정 패밀리 타입을 사용하는 모든 인스턴스를 다른 타입으로 교체합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "fromFamilyName", "fromTypeName", "toFamilyName", "toTypeName" },
                ["properties"] = new JObject
                {
                    ["fromFamilyName"] = new JObject { ["type"] = "string" },
                    ["fromTypeName"]   = new JObject { ["type"] = "string" },
                    ["toFamilyName"]   = new JObject { ["type"] = "string" },
                    ["toTypeName"]     = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var fromFamily = args["fromFamilyName"]!.ToString();
            var fromType   = args["fromTypeName"]!.ToString();
            var toFamily   = args["toFamilyName"]!.ToString();
            var toType     = args["toTypeName"]!.ToString();

            var fromSymbol = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Family.Name == fromFamily && s.Name == fromType)
                ?? throw new System.Exception($"원본 타입 '{fromFamily}:{fromType}' 없음");

            var toSymbol = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Family.Name == toFamily && s.Name == toType)
                ?? throw new System.Exception($"대상 타입 '{toFamily}:{toType}' 없음");

            var instances = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(i => i.Symbol.Id == fromSymbol.Id).ToList();

            using var tx = new Transaction(doc, "MCP: 패밀리 타입 교체");
            tx.Start();
            if (!toSymbol.IsActive) toSymbol.Activate();
            foreach (var inst in instances)
                inst.Symbol = toSymbol;
            tx.Commit();

            return TextContent($"{instances.Count}개 인스턴스를 '{toFamily}:{toType}'으로 교체 완료");
        }
    }

    // ── 패밀리 내보내기 ───────────────────────────────────────────
    public class ExportFamilyTool : ToolBase
    {
        public override string Name => "export_family";
        public override string Description => "프로젝트에서 패밀리를 .rfa 파일로 저장합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "familyName", "outputFolder" },
                ["properties"] = new JObject
                {
                    ["familyName"]   = new JObject { ["type"] = "string" },
                    ["outputFolder"] = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var name   = args["familyName"]!.ToString();
            var folder = args["outputFolder"]!.ToString();
            Directory.CreateDirectory(folder);

            var family = new FilteredElementCollector(doc).OfClass(typeof(Family))
                .Cast<Family>().FirstOrDefault(f => f.Name == name)
                ?? throw new System.Exception($"패밀리 '{name}' 없음");

            var famDoc = doc.EditFamily(family);
            var path   = Path.Combine(folder, $"{name}.rfa");
            famDoc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
            famDoc.Close(false);

            return TextContent($"패밀리 저장 완료 → {path}");
        }
    }
}
