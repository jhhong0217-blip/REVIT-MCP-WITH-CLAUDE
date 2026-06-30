using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Newtonsoft.Json.Linq;
using RevitMCP.Addin.Server;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Addin.Tools.Elements
{
    /// <summary>
    /// Revit UI에서 현재 선택된 요소들의 ID와 기본 정보를 반환합니다.
    /// </summary>
    public class GetSelectedElementsTool : ToolBase
    {
        public override string Name => "get_selected_elements";
        public override string Description =>
            "Revit에서 현재 선택된 요소들의 ID, 카테고리, 이름, 레벨을 반환합니다. " +
            "선택된 요소 ID를 join_selected_elements 등에 바로 활용할 수 있습니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["includeParameters"] = new JObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "true면 각 요소의 주요 파라미터도 함께 반환 (기본 false)"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var uiApp = RevitEventDispatcher.CurrentApp
                ?? throw new Exception("UIApplication 없음. Revit MCP가 실행 중인지 확인하세요.");

            var includeParams = args["includeParameters"]?.ToObject<bool>() ?? false;
            var selection = uiApp.ActiveUIDocument.Selection;
            var selectedIds = selection.GetElementIds();

            if (selectedIds.Count == 0)
                return TextContent(new JObject
                {
                    ["선택된요소수"] = 0,
                    ["안내"] = "Revit에서 요소를 선택한 뒤 다시 호출하세요.",
                    ["요소목록"] = new JArray()
                }.ToString());

            var result = new JArray();
            foreach (var id in selectedIds)
            {
                var elem = doc.GetElement(id);
                if (elem == null) continue;

                var bb = elem.get_BoundingBox(null);
                var item = new JObject
                {
                    ["id"] = id.Value,
                    ["name"] = elem.Name,
                    ["category"] = elem.Category?.Name ?? "Unknown",
                    ["level"] = (doc.GetElement(elem.LevelId) as Level)?.Name,
                    ["familyName"] = (elem as FamilyInstance)?.Symbol?.FamilyName,
                };

                if (bb != null)
                {
                    item["center_mm"] = new JObject
                    {
                        ["x"] = Math.Round((bb.Min.X + bb.Max.X) / 2 * 304.8, 0),
                        ["y"] = Math.Round((bb.Min.Y + bb.Max.Y) / 2 * 304.8, 0),
                        ["z"] = Math.Round((bb.Min.Z + bb.Max.Z) / 2 * 304.8, 0)
                    };
                }

                if (includeParams)
                {
                    var ps = new JObject();
                    foreach (Parameter p in elem.Parameters)
                    {
                        if (!p.HasValue || p.Definition == null) continue;
                        var val = p.StorageType switch
                        {
                            StorageType.String => p.AsString(),
                            StorageType.Integer => p.AsInteger().ToString(),
                            StorageType.Double => Math.Round(p.AsDouble() * 304.8, 2).ToString(),
                            StorageType.ElementId => p.AsElementId().Value.ToString(),
                            _ => null
                        };
                        if (val != null) ps[p.Definition.Name] = val;
                    }
                    item["parameters"] = ps;
                }

                result.Add(item);
            }

            return TextContent(new JObject
            {
                ["선택된요소수"] = selectedIds.Count,
                ["ids"] = new JArray(selectedIds.Select(id => id.Value)),
                ["요소목록"] = result
            }.ToString());
        }
    }

    /// <summary>
    /// 현재 Revit 선택 또는 지정한 두 ID 그룹 간 Join Geometry를 수행합니다.
    /// </summary>
    public class JoinSelectedElementsTool : ToolBase
    {
        public override string Name => "join_selected_elements";
        public override string Description =>
            "선택된 요소들을 서로 Join Geometry합니다. " +
            "ids1·ids2를 모두 지정하면 두 그룹 간 교차 Join, " +
            "ids1만 지정하면 해당 목록 내 모든 요소 쌍을 Join합니다. " +
            "둘 다 생략하면 현재 Revit 선택 요소를 사용합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["ids1"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "integer" },
                        ["description"] = "첫 번째 요소 ID 목록 (생략 시 현재 선택 사용)"
                    },
                    ["ids2"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "integer" },
                        ["description"] = "두 번째 요소 ID 목록 (생략 시 ids1과 동일 — 내부 전체 조합)"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var (list1, list2) = ResolveElementLists(doc, args);
            if (list1.Count == 0)
                return TextContent("결합할 요소가 없습니다. Revit에서 요소를 선택하거나 ids1을 지정하세요.");

            int joined = 0, skipped = 0, failed = 0;
            using var tx = new Transaction(doc, "MCP: 선택 요소 Join");
            tx.Start();
            for (int i = 0; i < list1.Count; i++)
            {
                var e1 = list1[i];
                var bb1 = e1.get_BoundingBox(null);
                int startJ = list1 == list2 ? i + 1 : 0;
                for (int j = startJ; j < list2.Count; j++)
                {
                    var e2 = list2[j];
                    if (e1.Id == e2.Id) continue;
                    try
                    {
                        if (JoinGeometryUtils.AreElementsJoined(doc, e1, e2)) { skipped++; continue; }
                        if (bb1 != null)
                        {
                            var bb2 = e2.get_BoundingBox(null);
                            if (bb2 != null && !BBoxIntersects(bb1, bb2)) continue;
                        }
                        JoinGeometryUtils.JoinGeometry(doc, e1, e2);
                        joined++;
                    }
                    catch { failed++; }
                }
            }
            tx.Commit();

            return TextContent(new JObject
            {
                ["결과"] = "Join Geometry 완료",
                ["새로결합"] = joined,
                ["이미결합"] = skipped,
                ["실패(타입불일치등)"] = failed,
                ["대상"] = $"{list1.Count}×{list2.Count} 요소"
            }.ToString());
        }

        private static (List<Element> list1, List<Element> list2) ResolveElementLists(Document doc, JObject args)
        {
            List<Element> GetById(JArray arr) =>
                arr.Values<long>()
                   .Select(id => doc.GetElement(new ElementId(id)))
                   .Where(e => e != null)
                   .ToList()!;

            List<Element> GetSelection()
            {
                var uiApp = RevitEventDispatcher.CurrentApp;
                if (uiApp == null) return new List<Element>();
                return uiApp.ActiveUIDocument.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null)
                    .ToList()!;
            }

            var ids1 = args["ids1"] as JArray;
            var ids2 = args["ids2"] as JArray;

            var list1 = ids1 != null ? GetById(ids1) : GetSelection();
            var list2 = ids2 != null ? GetById(ids2) : list1;
            return (list1, list2);
        }

        private static bool BBoxIntersects(BoundingBoxXYZ a, BoundingBoxXYZ b)
            => !(a.Max.X < b.Min.X || b.Max.X < a.Min.X ||
                 a.Max.Y < b.Min.Y || b.Max.Y < a.Min.Y ||
                 a.Max.Z < b.Min.Z || b.Max.Z < a.Min.Z);
    }

    /// <summary>
    /// 현재 Revit 선택 또는 지정한 ID 목록의 요소 간 Join Geometry를 해제합니다.
    /// </summary>
    public class UnjoinSelectedElementsTool : ToolBase
    {
        public override string Name => "unjoin_selected_elements";
        public override string Description =>
            "선택된 요소들의 Join Geometry를 해제합니다. " +
            "ids1·ids2를 지정하면 두 그룹 간 결합 해제, " +
            "생략하면 현재 Revit 선택 요소 간 결합을 모두 해제합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["ids1"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "integer" },
                        ["description"] = "첫 번째 요소 ID 목록 (생략 시 현재 선택 사용)"
                    },
                    ["ids2"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "integer" },
                        ["description"] = "두 번째 요소 ID 목록 (생략 시 ids1과 동일)"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var (list1, list2) = ResolveElementLists(doc, args);
            if (list1.Count == 0)
                return TextContent("해제할 요소가 없습니다. Revit에서 요소를 선택하거나 ids1을 지정하세요.");

            int unjoined = 0, notJoined = 0;
            using var tx = new Transaction(doc, "MCP: 선택 요소 Unjoin");
            tx.Start();
            for (int i = 0; i < list1.Count; i++)
            {
                var e1 = list1[i];
                int startJ = list1 == list2 ? i + 1 : 0;
                for (int j = startJ; j < list2.Count; j++)
                {
                    var e2 = list2[j];
                    if (e1.Id == e2.Id) continue;
                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(doc, e1, e2)) { notJoined++; continue; }
                        JoinGeometryUtils.UnjoinGeometry(doc, e1, e2);
                        unjoined++;
                    }
                    catch { }
                }
            }
            tx.Commit();

            return TextContent(new JObject
            {
                ["결과"] = "Unjoin Geometry 완료",
                ["해제된쌍"] = unjoined,
                ["결합안된쌍"] = notJoined
            }.ToString());
        }

        private static (List<Element> list1, List<Element> list2) ResolveElementLists(Document doc, JObject args)
        {
            List<Element> GetById(JArray arr) =>
                arr.Values<long>()
                   .Select(id => doc.GetElement(new ElementId(id)))
                   .Where(e => e != null)
                   .ToList()!;

            List<Element> GetSelection()
            {
                var uiApp = RevitEventDispatcher.CurrentApp;
                if (uiApp == null) return new List<Element>();
                return uiApp.ActiveUIDocument.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null)
                    .ToList()!;
            }

            var ids1 = args["ids1"] as JArray;
            var ids2 = args["ids2"] as JArray;
            var list1 = ids1 != null ? GetById(ids1) : GetSelection();
            var list2 = ids2 != null ? GetById(ids2) : list1;
            return (list1, list2);
        }
    }

    /// <summary>
    /// 요소 ID로 현재 결합 상태를 조회합니다.
    /// </summary>
    public class GetElementJoinStatusTool : ToolBase
    {
        public override string Name => "get_element_join_status";
        public override string Description =>
            "지정한 요소들의 Join Geometry 결합 상태를 조회합니다. " +
            "ids를 생략하면 현재 Revit 선택 요소를 사용합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["ids"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "integer" },
                        ["description"] = "조회할 요소 ID 목록 (생략 시 현재 선택 사용)"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            List<Element> elems;
            var idsArg = args["ids"] as JArray;
            if (idsArg != null)
            {
                elems = idsArg.Values<long>()
                    .Select(id => doc.GetElement(new ElementId(id)))
                    .Where(e => e != null).ToList()!;
            }
            else
            {
                var uiApp = RevitEventDispatcher.CurrentApp
                    ?? throw new Exception("UIApplication 없음");
                elems = uiApp.ActiveUIDocument.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null).ToList()!;
            }

            if (elems.Count == 0)
                return TextContent("요소가 없습니다. Revit에서 선택하거나 ids를 지정하세요.");

            var joinedPairs = new JArray();
            var unjoinedPairs = new JArray();

            for (int i = 0; i < elems.Count; i++)
            {
                for (int j = i + 1; j < elems.Count; j++)
                {
                    var e1 = elems[i];
                    var e2 = elems[j];
                    try
                    {
                        var pair = new JObject
                        {
                            ["id1"] = e1.Id.Value,
                            ["name1"] = e1.Name,
                            ["category1"] = e1.Category?.Name,
                            ["id2"] = e2.Id.Value,
                            ["name2"] = e2.Name,
                            ["category2"] = e2.Category?.Name
                        };
                        if (JoinGeometryUtils.AreElementsJoined(doc, e1, e2))
                            joinedPairs.Add(pair);
                        else
                            unjoinedPairs.Add(pair);
                    }
                    catch { }
                }
            }

            return TextContent(new JObject
            {
                ["조회요소수"] = elems.Count,
                ["결합된쌍"] = joinedPairs.Count,
                ["미결합쌍"] = unjoinedPairs.Count,
                ["결합목록"] = joinedPairs,
                ["미결합목록"] = unjoinedPairs
            }.ToString());
        }
    }
}
