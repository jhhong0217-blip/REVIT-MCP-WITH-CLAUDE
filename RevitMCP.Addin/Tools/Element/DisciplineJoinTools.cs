using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Addin.Tools.Elements
{
    /// <summary>
    /// 건축(Architectural) / 구조(Structural) 분류 기반 Join Geometry 도구
    /// </summary>
    internal static class DisciplineCategories
    {
        public static readonly BuiltInCategory[] Architectural = new[]
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_Ramps,
        };

        public static readonly BuiltInCategory[] Structural = new[]
        {
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_StructuralWall,
        };

        public static BuiltInCategory[] Get(string discipline) => discipline.ToLower() switch
        {
            "arch" or "architectural" or "건축" => Architectural,
            "struct" or "structural" or "구조" => Structural,
            _ => throw new Exception($"discipline 값은 'arch' 또는 'struct' 중 하나여야 합니다: {discipline}")
        };

        public static string Label(string d) => d.ToLower() is "arch" or "architectural" or "건축" ? "건축" : "구조";
    }

    public class JoinDisciplinesTool : ToolBase
    {
        public override string Name => "join_disciplines";
        public override string Description =>
            "건축(arch) 요소와 구조(struct) 요소 간, 또는 같은 분야 내부의 교차 요소를 자동으로 Join Geometry합니다. " +
            "discipline1/discipline2에 'arch' 또는 'struct'를 지정하세요.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "discipline1", "discipline2" },
                ["properties"] = new JObject
                {
                    ["discipline1"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray { "arch", "struct" },
                        ["description"] = "첫 번째 분야: 'arch'(건축) 또는 'struct'(구조)"
                    },
                    ["discipline2"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray { "arch", "struct" },
                        ["description"] = "두 번째 분야: 'arch'(건축) 또는 'struct'(구조)"
                    },
                    ["levelName"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "레벨 필터 (선택, 미지정 시 전체 레벨)"
                    },
                    ["extraCategories1"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "discipline1에 추가할 카테고리 (예: [\"OST_GenericModel\"])"
                    },
                    ["extraCategories2"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "discipline2에 추가할 카테고리"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var cats1 = BuildCategoryList(args, "discipline1", "extraCategories1");
            var cats2 = BuildCategoryList(args, "discipline2", "extraCategories2");
            var label1 = DisciplineCategories.Label(args["discipline1"]!.ToString());
            var label2 = DisciplineCategories.Label(args["discipline2"]!.ToString());

            var (list1, list2) = CollectElements(doc, cats1, cats2, args["levelName"]?.ToString());

            int joined = 0, alreadyJoined = 0;
            using var tx = new Transaction(doc, $"MCP: Join {label1}↔{label2}");
            tx.Start();
            foreach (var e1 in list1)
            {
                var bb1 = e1.get_BoundingBox(null);
                if (bb1 == null) continue;
                foreach (var e2 in list2)
                {
                    if (e1.Id == e2.Id) continue;
                    try
                    {
                        if (JoinGeometryUtils.AreElementsJoined(doc, e1, e2)) { alreadyJoined++; continue; }
                        var bb2 = e2.get_BoundingBox(null);
                        if (bb2 == null || !BBoxIntersects(bb1, bb2)) continue;
                        JoinGeometryUtils.JoinGeometry(doc, e1, e2);
                        joined++;
                    }
                    catch { }
                }
            }
            tx.Commit();

            return TextContent(new JObject
            {
                ["결과"] = $"{label1}↔{label2} Join Geometry 완료",
                ["새로결합"] = joined,
                ["이미결합"] = alreadyJoined,
                ["대상요소"] = $"{list1.Count}×{list2.Count}",
                ["카테고리1"] = new JArray(cats1.Select(c => c.ToString())),
                ["카테고리2"] = new JArray(cats2.Select(c => c.ToString()))
            }.ToString());
        }

        internal static (List<Element> list1, List<Element> list2) CollectPair(
            Document doc, List<BuiltInCategory> cats1, List<BuiltInCategory> cats2, string? levelName)
            => CollectElements(doc, cats1, cats2, levelName);

        private static (List<Element> list1, List<Element> list2) CollectElements(
            Document doc, List<BuiltInCategory> cats1, List<BuiltInCategory> cats2, string? levelName)
        {
            ElementLevelFilter? lf = null;
            if (levelName != null)
            {
                var level = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>().FirstOrDefault(l => l.Name == levelName)
                    ?? throw new Exception($"레벨 없음: {levelName}");
                lf = new ElementLevelFilter(level.Id);
            }

            List<Element> Collect(List<BuiltInCategory> cats)
            {
                var result = new List<Element>();
                foreach (var cat in cats)
                {
                    var col = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .OfCategory(cat);
                    if (lf != null) col = col.WherePasses(lf);
                    result.AddRange(col.ToList());
                }
                return result;
            }

            return (Collect(cats1), Collect(cats2));
        }

        private static List<BuiltInCategory> BuildCategoryList(JObject args, string disciplineKey, string extraKey)
        {
            var cats = DisciplineCategories.Get(args[disciplineKey]!.ToString()).ToList();
            if (args[extraKey] is JArray extra)
                foreach (var s in extra.Values<string>()!)
                    cats.Add((BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), s!));
            return cats;
        }

        internal static bool BBoxIntersects(BoundingBoxXYZ a, BoundingBoxXYZ b)
            => !(a.Max.X < b.Min.X || b.Max.X < a.Min.X ||
                 a.Max.Y < b.Min.Y || b.Max.Y < a.Min.Y ||
                 a.Max.Z < b.Min.Z || b.Max.Z < a.Min.Z);
    }

    public class UnjoinDisciplinesTool : ToolBase
    {
        public override string Name => "unjoin_disciplines";
        public override string Description =>
            "건축(arch)과 구조(struct) 요소 간, 또는 같은 분야 내 Join Geometry를 전체 해제합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "discipline1", "discipline2" },
                ["properties"] = new JObject
                {
                    ["discipline1"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray { "arch", "struct" },
                        ["description"] = "'arch'(건축) 또는 'struct'(구조)"
                    },
                    ["discipline2"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray { "arch", "struct" },
                        ["description"] = "'arch'(건축) 또는 'struct'(구조)"
                    },
                    ["levelName"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "레벨 필터 (선택)"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var cats1 = DisciplineCategories.Get(args["discipline1"]!.ToString()).ToList();
            var cats2 = DisciplineCategories.Get(args["discipline2"]!.ToString()).ToList();
            var label1 = DisciplineCategories.Label(args["discipline1"]!.ToString());
            var label2 = DisciplineCategories.Label(args["discipline2"]!.ToString());
            var (list1, list2) = JoinDisciplinesTool.CollectPair(doc, cats1, cats2, args["levelName"]?.ToString());

            int unjoined = 0;
            using var tx = new Transaction(doc, $"MCP: Unjoin {label1}↔{label2}");
            tx.Start();
            foreach (var e1 in list1)
                foreach (var e2 in list2)
                {
                    if (e1.Id == e2.Id) continue;
                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(doc, e1, e2)) continue;
                        JoinGeometryUtils.UnjoinGeometry(doc, e1, e2);
                        unjoined++;
                    }
                    catch { }
                }
            tx.Commit();

            return TextContent($"{label1}↔{label2} Join 해제 완료: {unjoined}쌍 (대상: {list1.Count}×{list2.Count})");
        }
    }

    public class GetJoinStatusTool : ToolBase
    {
        public override string Name => "get_join_status";
        public override string Description =>
            "건축/구조 분야별 Join Geometry 현황을 분석합니다. 결합된 쌍 수, 미결합 교차 요소 수를 보고합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
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
            var levelName = args["levelName"]?.ToString();
            var archCats = DisciplineCategories.Architectural.ToList();
            var structCats = DisciplineCategories.Structural.ToList();
            var (archElems, structElems) = JoinDisciplinesTool.CollectPair(doc, archCats, structCats, levelName);

            int joinedCount = 0, intersectingNotJoined = 0;
            var notJoinedPairs = new JArray();

            foreach (var e1 in archElems)
            {
                var bb1 = e1.get_BoundingBox(null);
                if (bb1 == null) continue;
                foreach (var e2 in structElems)
                {
                    var bb2 = e2.get_BoundingBox(null);
                    if (bb2 == null || !JoinDisciplinesTool.BBoxIntersects(bb1, bb2)) continue;
                    try
                    {
                        if (JoinGeometryUtils.AreElementsJoined(doc, e1, e2))
                            joinedCount++;
                        else
                        {
                            intersectingNotJoined++;
                            if (notJoinedPairs.Count < 20)
                                notJoinedPairs.Add(new JObject
                                {
                                    ["archId"] = e1.Id.Value,
                                    ["archName"] = e1.Name,
                                    ["structId"] = e2.Id.Value,
                                    ["structCategory"] = e2.Category?.Name
                                });
                        }
                    }
                    catch { }
                }
            }

            return TextContent(new JObject
            {
                ["레벨"] = levelName ?? "전체",
                ["건축요소수"] = archElems.Count,
                ["구조요소수"] = structElems.Count,
                ["결합된쌍"] = joinedCount,
                ["교차하지만미결합"] = intersectingNotJoined,
                ["미결합샘플"] = notJoinedPairs
            }.ToString());
        }
    }

    public class AutoJoinAllDisciplinesTool : ToolBase
    {
        public override string Name => "auto_join_all_disciplines";
        public override string Description =>
            "건축↔구조, 구조↔구조 모든 조합을 한 번에 자동으로 Join Geometry합니다. 대규모 모델에서 전체 Join을 일괄 처리합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["levelName"] = new JObject { ["type"] = "string", ["description"] = "레벨 필터 (선택, 미지정 시 전체)" },
                    ["includeArchArch"] = new JObject { ["type"] = "boolean", ["description"] = "건축↔건축 결합 포함 (기본 false)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var levelName = args["levelName"]?.ToString();
            var includeArchArch = args["includeArchArch"]?.ToObject<bool>() ?? false;
            var archCats = DisciplineCategories.Architectural.ToList();
            var structCats = DisciplineCategories.Structural.ToList();

            var (archElems, structElems) = JoinDisciplinesTool.CollectPair(doc, archCats, structCats, levelName);

            var report = new JObject();
            using var tx = new Transaction(doc, "MCP: Auto Join All Disciplines");
            tx.Start();

            report["건축↔구조"] = RunJoin(doc, archElems, structElems);
            report["구조↔구조"] = RunJoin(doc, structElems, structElems);
            if (includeArchArch)
                report["건축↔건축"] = RunJoin(doc, archElems, archElems);

            tx.Commit();
            return TextContent(new JObject
            {
                ["결과"] = "Auto Join All Disciplines 완료",
                ["레벨"] = levelName ?? "전체",
                ["상세"] = report
            }.ToString());
        }

        private static JObject RunJoin(Document doc, List<Element> list1, List<Element> list2)
        {
            int joined = 0, already = 0;
            for (int i = 0; i < list1.Count; i++)
            {
                var e1 = list1[i];
                var bb1 = e1.get_BoundingBox(null);
                if (bb1 == null) continue;
                int start = (list1 == list2) ? i + 1 : 0;
                for (int j = start; j < list2.Count; j++)
                {
                    var e2 = list2[j];
                    if (e1.Id == e2.Id) continue;
                    try
                    {
                        if (JoinGeometryUtils.AreElementsJoined(doc, e1, e2)) { already++; continue; }
                        var bb2 = e2.get_BoundingBox(null);
                        if (bb2 == null || !JoinDisciplinesTool.BBoxIntersects(bb1, bb2)) continue;
                        JoinGeometryUtils.JoinGeometry(doc, e1, e2);
                        joined++;
                    }
                    catch { }
                }
            }
            return new JObject { ["새결합"] = joined, ["기결합"] = already };
        }
    }
}
