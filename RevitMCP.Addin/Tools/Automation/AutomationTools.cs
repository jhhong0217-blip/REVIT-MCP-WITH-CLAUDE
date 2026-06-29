using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace RevitMCP.Addin.Tools.Automation
{
    // ── 카테고리 전체 자동 태그 ───────────────────────────────────
    public class AutoTagAllTool : ToolBase
    {
        public override string Name => "auto_tag_all";
        public override string Description => "뷰에서 지정 카테고리의 모든 요소에 자동으로 태그를 답니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "category", "viewId" },
                ["properties"] = new JObject
                {
                    ["category"]   = new JObject { ["type"] = "string" },
                    ["viewId"]     = new JObject { ["type"] = "integer" },
                    ["addLeader"]  = new JObject { ["type"] = "boolean", ["description"] = "지시선 추가 여부" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            if (!System.Enum.TryParse<BuiltInCategory>(args["category"]!.ToString(), out var bic))
                return ErrorContent("잘못된 카테고리");

            var viewId    = new ElementId(args["viewId"]!.ToObject<int>());
            var view      = doc.GetElement(viewId) as View ?? throw new System.Exception("뷰 없음");
            var addLeader = args["addLeader"]?.ToObject<bool>() ?? false;

            var elements = new FilteredElementCollector(doc, viewId)
                .OfCategory(bic).WhereElementIsNotElementType().ToList();

            using var tx = new Transaction(doc, "MCP: 자동 태그 전체");
            tx.Start();
            int count = 0;
            foreach (var elem in elements)
            {
                try
                {
                    var bb = elem.get_BoundingBox(view);
                    if (bb == null) continue;
                    var center = (bb.Min + bb.Max) / 2.0;
                    IndependentTag.Create(doc, viewId, new Reference(elem), addLeader,
                        TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, center);
                    count++;
                }
                catch { }
            }
            tx.Commit();

            return TextContent($"{count}개 요소에 태그 완료");
        }
    }

    // ── 요소 번호 자동 부여 ───────────────────────────────────────
    public class RenumberElementsTool : ToolBase
    {
        public override string Name => "renumber_elements";
        public override string Description => "카테고리 요소에 순번 기반 번호를 자동 부여합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "category", "paramName" },
                ["properties"] = new JObject
                {
                    ["category"]  = new JObject { ["type"] = "string" },
                    ["paramName"] = new JObject { ["type"] = "string", ["description"] = "번호를 기록할 파라미터 이름" },
                    ["prefix"]    = new JObject { ["type"] = "string", ["description"] = "번호 앞 접두사 (예: D-)" },
                    ["startNum"]  = new JObject { ["type"] = "integer", ["description"] = "시작 번호 (기본 1)" },
                    ["levelName"] = new JObject { ["type"] = "string", ["description"] = "레벨 필터 (선택)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            if (!System.Enum.TryParse<BuiltInCategory>(args["category"]!.ToString(), out var bic))
                return ErrorContent("잘못된 카테고리");

            var paramName = args["paramName"]!.ToString();
            var prefix    = args["prefix"]?.ToString() ?? "";
            var startNum  = args["startNum"]?.ToObject<int>() ?? 1;

            IEnumerable<Element> elements = new FilteredElementCollector(doc)
                .OfCategory(bic).WhereElementIsNotElementType();

            if (args["levelName"] is JToken ln)
            {
                var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(l => l.Name == ln.ToString());
                if (level != null)
                    elements = elements.Where(e =>
                    {
                        var p = e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                             ?? e.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                        return p?.AsElementId() == level.Id;
                    });
            }

            var list = elements.ToList();
            using var tx = new Transaction(doc, "MCP: 요소 번호 부여");
            tx.Start();
            int n = startNum;
            foreach (var elem in list)
            {
                var p = elem.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set($"{prefix}{n++}");
            }
            tx.Commit();

            return TextContent($"{list.Count}개 요소에 번호 부여 완료 ({prefix}{startNum} ~ {prefix}{n - 1})");
        }
    }

    // ── 시트 일괄 생성 ────────────────────────────────────────────
    public class BatchCreateSheetsTool : ToolBase
    {
        public override string Name => "batch_create_sheets";
        public override string Description => "번호/이름 목록으로 시트를 일괄 생성합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "sheets" },
                ["properties"] = new JObject
                {
                    ["sheets"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "[{number, name}] 배열",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["number"] = new JObject { ["type"] = "string" },
                                ["name"]   = new JObject { ["type"] = "string" }
                            }
                        }
                    },
                    ["titleBlockName"] = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            FamilySymbol? tb = null;
            if (args["titleBlockName"] is JToken tbn)
                tb = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Family.Name == tbn.ToString());
            tb ??= new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault();

            var sheets = args["sheets"]!.ToObject<JArray>()!;
            var created = new List<string>();

            using var tx = new Transaction(doc, "MCP: 시트 일괄 생성");
            tx.Start();
            foreach (var s in sheets)
            {
                var sheet = ViewSheet.Create(doc, tb?.Id ?? ElementId.InvalidElementId);
                sheet.SheetNumber = s["number"]?.ToString() ?? "";
                sheet.Name        = s["name"]?.ToString() ?? "";
                created.Add($"{sheet.SheetNumber} - {sheet.Name}");
            }
            tx.Commit();

            return TextContent($"{created.Count}개 시트 생성 완료\n{string.Join("\n", created)}");
        }
    }

    // ── Excel → 파라미터 가져오기 ─────────────────────────────────
    public class ImportParametersFromCsvTool : ToolBase
    {
        public override string Name => "import_parameters_from_csv";
        public override string Description => "CSV 파일에서 ElementId 기준으로 파라미터 값을 일괄 가져옵니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "csvPath" },
                ["properties"] = new JObject
                {
                    ["csvPath"] = new JObject { ["type"] = "string", ["description"] = "CSV 파일 경로 (첫 열: ElementId)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var path = args["csvPath"]!.ToString();
            if (!File.Exists(path)) return ErrorContent($"파일 없음: {path}");

            var lines  = File.ReadAllLines(path, System.Text.Encoding.UTF8);
            if (lines.Length < 2) return ErrorContent("데이터가 없습니다.");

            var headers = lines[0].Split(',');
            int success = 0;

            using var tx = new Transaction(doc, "MCP: CSV 파라미터 가져오기");
            tx.Start();
            foreach (var line in lines.Skip(1))
            {
                var cols = line.Split(',');
                if (!int.TryParse(cols[0].Trim('"'), out int id)) continue;
                var elem = doc.GetElement(new ElementId(id));
                if (elem == null) continue;

                for (int i = 1; i < headers.Length && i < cols.Length; i++)
                {
                    var p = elem.LookupParameter(headers[i].Trim());
                    var v = cols[i].Trim().Trim('"');
                    if (p == null || p.IsReadOnly) continue;
                    try
                    {
                        switch (p.StorageType)
                        {
                            case StorageType.String:  p.Set(v); break;
                            case StorageType.Double:  if (double.TryParse(v, out double d)) p.Set(d); break;
                            case StorageType.Integer: if (int.TryParse(v, out int n)) p.Set(n); break;
                        }
                        success++;
                    }
                    catch { }
                }
            }
            tx.Commit();

            return TextContent($"{success}개 파라미터 값 가져오기 완료");
        }
    }

    // ── 룸 면적/둘레 자동 계산 ───────────────────────────────────
    public class RoomDataSummaryTool : ToolBase
    {
        public override string Name => "room_data_summary";
        public override string Description => "모든 룸의 이름, 번호, 면적, 둘레, 레벨을 요약합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["levelName"] = new JObject { ["type"] = "string", ["description"] = "레벨 필터 (선택)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            IEnumerable<Room> rooms = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement)).OfType<Room>()
                .Where(r => r.Area > 0);

            if (args["levelName"] is JToken ln)
            {
                var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(l => l.Name == ln.ToString());
                if (level != null)
                    rooms = rooms.Where(r => r.LevelId == level.Id);
            }

            double totalArea = 0;
            var result = rooms.Select(r =>
            {
                totalArea += r.Area * 0.0929; // ft² → m²
                return new JObject
                {
                    ["number"]     = r.Number,
                    ["name"]       = r.Name,
                    ["area_m2"]    = System.Math.Round(r.Area * 0.0929, 2),
                    ["perimeter_m"] = System.Math.Round(r.Perimeter * 0.3048, 2),
                    ["level"]      = doc.GetElement(r.LevelId)?.Name ?? ""
                };
            }).ToList();

            var summary = new JObject
            {
                ["roomCount"]    = result.Count,
                ["totalArea_m2"] = System.Math.Round(totalArea, 2),
                ["rooms"]        = new JArray(result)
            };

            return TextContent(summary.ToString());
        }
    }

    // ── 경고 일괄 해소 시도 ───────────────────────────────────────
    public class ResolveWarningsTool : ToolBase
    {
        public override string Name => "get_warnings_detail";
        public override string Description => "모델 경고를 유형별로 분류하고 해소 방법을 제안합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var warnings = doc.GetWarnings();
            var grouped  = warnings
                .GroupBy(w => w.GetDescriptionText())
                .Select(g => new JObject
                {
                    ["description"] = g.Key,
                    ["count"]       = g.Count(),
                    ["severity"]    = g.First().GetSeverity().ToString(),
                    ["elementIds"]  = new JArray(g.SelectMany(w =>
                        w.GetFailingElements().Select(id => id.IntegerValue)).Distinct().Take(10)),
                    ["suggestion"]  = GetSuggestion(g.Key)
                }).ToList();

            return TextContent($"경고 {warnings.Count}건 / 유형 {grouped.Count}종\n{new JArray(grouped)}");
        }

        private static string GetSuggestion(string desc)
        {
            if (desc.Contains("같은 위치")) return "동일 위치 요소 삭제 또는 이동 필요";
            if (desc.Contains("겹침") || desc.Contains("overlap")) return "겹치는 요소를 분리하거나 삭제";
            if (desc.Contains("연결") || desc.Contains("join"))    return "요소 연결 상태 확인";
            if (desc.Contains("룸") || desc.Contains("room"))      return "룸 경계 확인 및 룸 재배치";
            return "Revit 경고 창에서 직접 확인 권장";
        }
    }
}
