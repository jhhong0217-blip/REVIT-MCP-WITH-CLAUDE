using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Addin.Tools.Elements
{
    public class GetElementByIdTool : ToolBase
    {
        public override string Name => "get_element_by_id";
        public override string Description => "요소 ID로 요소 정보와 파라미터를 조회합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId" },
                ["properties"] = new JObject
                {
                    ["elementId"] = new JObject { ["type"] = "integer" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var id = new ElementId(args["elementId"]!.ToObject<long>());
            var elem = doc.GetElement(id) ?? throw new Exception($"요소 없음: {id}");
            var bb = elem.get_BoundingBox(null);
            var result = new JObject
            {
                ["id"] = elem.Id.Value,
                ["name"] = elem.Name,
                ["category"] = elem.Category?.Name,
                ["familyType"] = (elem as FamilyInstance)?.Symbol?.FamilyName,
                ["level"] = (doc.GetElement(elem.LevelId) as Level)?.Name,
                ["location"] = bb != null ? new JObject
                {
                    ["centerX_mm"] = ((bb.Min.X + bb.Max.X) / 2) * 304.8,
                    ["centerY_mm"] = ((bb.Min.Y + bb.Max.Y) / 2) * 304.8,
                    ["centerZ_mm"] = ((bb.Min.Z + bb.Max.Z) / 2) * 304.8
                } : null
            };
            var ps = new JObject();
            foreach (Parameter p in elem.Parameters)
                if (p.HasValue && p.Definition != null)
                    ps[p.Definition.Name] = p.StorageType switch
                    {
                        StorageType.String => p.AsString(),
                        StorageType.Integer => p.AsInteger().ToString(),
                        StorageType.Double => Math.Round(p.AsDouble() * 304.8, 2).ToString(),
                        StorageType.ElementId => p.AsElementId().Value.ToString(),
                        _ => null
                    };
            result["parameters"] = ps;
            return TextContent(result.ToString());
        }
    }

    public class ListElementTypesTool : ToolBase
    {
        public override string Name => "list_element_types";
        public override string Description => "카테고리의 모든 타입(패밀리 심볼) 목록을 반환합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "category" },
                ["properties"] = new JObject
                {
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "예: OST_Walls, OST_Doors, OST_Windows" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var catStr = args["category"]!.ToString();
            BuiltInCategory bic = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), catStr);
            var types = new FilteredElementCollector(doc)
                .OfClass(typeof(ElementType))
                .OfCategory(bic)
                .Cast<ElementType>()
                .OrderBy(t => t.FamilyName)
                .ThenBy(t => t.Name)
                .Select(t => new JObject
                {
                    ["id"] = t.Id.Value,
                    ["family"] = t.FamilyName,
                    ["typeName"] = t.Name
                });
            return TextContent(new JArray(types).ToString());
        }
    }

    public class GetElementLocationTool : ToolBase
    {
        public override string Name => "get_element_location";
        public override string Description => "요소의 위치 좌표(mm)와 방향을 반환합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId" },
                ["properties"] = new JObject
                {
                    ["elementId"] = new JObject { ["type"] = "integer" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var id = new ElementId(args["elementId"]!.ToObject<long>());
            var elem = doc.GetElement(id) ?? throw new Exception("요소 없음");
            var result = new JObject { ["id"] = id.Value };
            if (elem.Location is LocationPoint lp)
            {
                result["type"] = "point";
                result["x_mm"] = Math.Round(lp.Point.X * 304.8, 1);
                result["y_mm"] = Math.Round(lp.Point.Y * 304.8, 1);
                result["z_mm"] = Math.Round(lp.Point.Z * 304.8, 1);
                result["rotation_deg"] = Math.Round(lp.Rotation * 180 / Math.PI, 2);
            }
            else if (elem.Location is LocationCurve lc)
            {
                var s = lc.Curve.GetEndPoint(0);
                var e = lc.Curve.GetEndPoint(1);
                result["type"] = "curve";
                result["start"] = new JObject { ["x_mm"] = Math.Round(s.X * 304.8, 1), ["y_mm"] = Math.Round(s.Y * 304.8, 1), ["z_mm"] = Math.Round(s.Z * 304.8, 1) };
                result["end"] = new JObject { ["x_mm"] = Math.Round(e.X * 304.8, 1), ["y_mm"] = Math.Round(e.Y * 304.8, 1), ["z_mm"] = Math.Round(e.Z * 304.8, 1) };
                result["length_mm"] = Math.Round(lc.Curve.Length * 304.8, 1);
            }
            return TextContent(result.ToString());
        }
    }

    public class DuplicateElementTypeTool : ToolBase
    {
        public override string Name => "duplicate_element_type";
        public override string Description => "기존 타입을 복제하여 새 이름으로 생성합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId", "newTypeName" },
                ["properties"] = new JObject
                {
                    ["elementId"] = new JObject { ["type"] = "integer", ["description"] = "복제할 타입의 ID" },
                    ["newTypeName"] = new JObject { ["type"] = "string" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var id = new ElementId(args["elementId"]!.ToObject<long>());
            var newName = args["newTypeName"]!.ToString();
            var elemType = doc.GetElement(id) as ElementType ?? throw new Exception("타입 요소 없음");
            using var tx = new Transaction(doc, "MCP: 타입 복제");
            tx.Start();
            var newType = elemType.Duplicate(newName);
            tx.Commit();
            return TextContent($"타입 복제 완료: {newName} (ID: {newType.Id.Value})");
        }
    }

    public class SetTypeParameterTool : ToolBase
    {
        public override string Name => "set_type_parameter";
        public override string Description => "요소 타입의 파라미터 값을 설정합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "typeElementId", "parameterName", "value" },
                ["properties"] = new JObject
                {
                    ["typeElementId"] = new JObject { ["type"] = "integer" },
                    ["parameterName"] = new JObject { ["type"] = "string" },
                    ["value"] = new JObject { ["type"] = "string" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var id = new ElementId(args["typeElementId"]!.ToObject<long>());
            var paramName = args["parameterName"]!.ToString();
            var value = args["value"]!.ToString();
            var elemType = doc.GetElement(id) as ElementType ?? throw new Exception("타입 없음");
            var param = elemType.LookupParameter(paramName) ?? throw new Exception($"파라미터 없음: {paramName}");
            using var tx = new Transaction(doc, "MCP: 타입 파라미터 설정");
            tx.Start();
            switch (param.StorageType)
            {
                case StorageType.String: param.Set(value); break;
                case StorageType.Integer: param.Set(int.Parse(value)); break;
                case StorageType.Double: param.Set(double.Parse(value) / 304.8); break;
            }
            tx.Commit();
            return TextContent($"타입 파라미터 설정: {paramName} = {value}");
        }
    }

    public class GetElementsByLevelTool : ToolBase
    {
        public override string Name => "get_elements_by_level";
        public override string Description => "특정 레벨의 모든 요소를 카테고리별로 그룹화하여 반환합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "levelName" },
                ["properties"] = new JObject
                {
                    ["levelName"] = new JObject { ["type"] = "string" },
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "특정 카테고리 필터 (선택)" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var levelName = args["levelName"]!.ToString();
            var level = new FilteredElementCollector(doc).OfClass(typeof(Level))
                .Cast<Level>().FirstOrDefault(l => l.Name == levelName)
                ?? throw new Exception($"레벨 없음: {levelName}");

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementLevelFilter(level.Id));

            var catFilter = args["category"]?.ToString();
            IEnumerable<Element> elems = collector;
            if (catFilter != null)
            {
                BuiltInCategory bic = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), catFilter);
                elems = collector.OfCategory(bic);
            }

            var grouped = elems
                .Where(e => e.Category != null)
                .GroupBy(e => e.Category!.Name)
                .OrderByDescending(g => g.Count())
                .Select(g => new JObject
                {
                    ["category"] = g.Key,
                    ["count"] = g.Count(),
                    ["ids"] = new JArray(g.Take(50).Select(e => e.Id.Value))
                });
            return TextContent(new JArray(grouped).ToString());
        }
    }

    public class GetElementBoundingBoxTool : ToolBase
    {
        public override string Name => "get_element_bounding_box";
        public override string Description => "요소의 바운딩 박스(최소/최대 좌표, mm)를 반환합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId" },
                ["properties"] = new JObject
                {
                    ["elementId"] = new JObject { ["type"] = "integer" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var id = new ElementId(args["elementId"]!.ToObject<long>());
            var elem = doc.GetElement(id) ?? throw new Exception("요소 없음");
            var bb = elem.get_BoundingBox(null) ?? throw new Exception("바운딩 박스 없음");
            return TextContent(new JObject
            {
                ["min"] = new JObject { ["x"] = Math.Round(bb.Min.X * 304.8, 1), ["y"] = Math.Round(bb.Min.Y * 304.8, 1), ["z"] = Math.Round(bb.Min.Z * 304.8, 1) },
                ["max"] = new JObject { ["x"] = Math.Round(bb.Max.X * 304.8, 1), ["y"] = Math.Round(bb.Max.Y * 304.8, 1), ["z"] = Math.Round(bb.Max.Z * 304.8, 1) },
                ["width_mm"] = Math.Round((bb.Max.X - bb.Min.X) * 304.8, 1),
                ["depth_mm"] = Math.Round((bb.Max.Y - bb.Min.Y) * 304.8, 1),
                ["height_mm"] = Math.Round((bb.Max.Z - bb.Min.Z) * 304.8, 1)
            }.ToString());
        }
    }

    public class JoinGeometryByCategoryTool : ToolBase
    {
        public override string Name => "join_geometry_by_category";
        public override string Description => "두 카테고리 간 교차하는 요소들을 자동으로 Join Geometry(지오메트리 결합)합니다. 예: 벽+기둥, 바닥+보.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "category1", "category2" },
                ["properties"] = new JObject
                {
                    ["category1"] = new JObject { ["type"] = "string", ["description"] = "첫 번째 카테고리 (예: OST_Walls)" },
                    ["category2"] = new JObject { ["type"] = "string", ["description"] = "두 번째 카테고리 (예: OST_StructuralColumns)" },
                    ["levelName"] = new JObject { ["type"] = "string", ["description"] = "레벨 필터 (선택, 미지정 시 전체)" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var cat1 = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), args["category1"]!.ToString());
            var cat2 = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), args["category2"]!.ToString());
            var levelName = args["levelName"]?.ToString();

            ElementLevelFilter? levelFilter = null;
            if (levelName != null)
            {
                var level = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>().FirstOrDefault(l => l.Name == levelName)
                    ?? throw new Exception($"레벨 없음: {levelName}");
                levelFilter = new ElementLevelFilter(level.Id);
            }

            IEnumerable<Element> elems1 = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType().OfCategory(cat1);
            IEnumerable<Element> elems2 = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType().OfCategory(cat2);
            if (levelFilter != null)
            {
                elems1 = new FilteredElementCollector(doc).WhereElementIsNotElementType().OfCategory(cat1).WherePasses(levelFilter);
                elems2 = new FilteredElementCollector(doc).WhereElementIsNotElementType().OfCategory(cat2).WherePasses(levelFilter);
            }

            var list1 = elems1.ToList();
            var list2 = elems2.ToList();

            int joined = 0, skipped = 0;
            using var tx = new Transaction(doc, "MCP: Join Geometry");
            tx.Start();
            foreach (var e1 in list1)
            {
                foreach (var e2 in list2)
                {
                    try
                    {
                        if (JoinGeometryUtils.AreElementsJoined(doc, e1, e2)) { skipped++; continue; }
                        var bb1 = e1.get_BoundingBox(null);
                        var bb2 = e2.get_BoundingBox(null);
                        if (bb1 == null || bb2 == null) continue;
                        // 바운딩 박스 교차 여부 빠른 검사
                        if (bb1.Max.X < bb2.Min.X || bb2.Max.X < bb1.Min.X) continue;
                        if (bb1.Max.Y < bb2.Min.Y || bb2.Max.Y < bb1.Min.Y) continue;
                        if (bb1.Max.Z < bb2.Min.Z || bb2.Max.Z < bb1.Min.Z) continue;
                        JoinGeometryUtils.JoinGeometry(doc, e1, e2);
                        joined++;
                    }
                    catch { /* 결합 불가 조합 무시 */ }
                }
            }
            tx.Commit();
            return TextContent($"Join Geometry 완료: {joined}쌍 결합, {skipped}쌍 이미 결합됨 (대상: {list1.Count}×{list2.Count} 요소)");
        }
    }

    public class UnjoinGeometryByCategoryTool : ToolBase
    {
        public override string Name => "unjoin_geometry_by_category";
        public override string Description => "두 카테고리 간 Join Geometry(지오메트리 결합)를 모두 해제합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "category1", "category2" },
                ["properties"] = new JObject
                {
                    ["category1"] = new JObject { ["type"] = "string", ["description"] = "첫 번째 카테고리 (예: OST_Walls)" },
                    ["category2"] = new JObject { ["type"] = "string", ["description"] = "두 번째 카테고리 (예: OST_StructuralColumns)" },
                    ["levelName"] = new JObject { ["type"] = "string", ["description"] = "레벨 필터 (선택)" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var cat1 = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), args["category1"]!.ToString());
            var cat2 = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), args["category2"]!.ToString());
            var levelName = args["levelName"]?.ToString();

            ElementLevelFilter? levelFilter = null;
            if (levelName != null)
            {
                var level = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>().FirstOrDefault(l => l.Name == levelName)
                    ?? throw new Exception($"레벨 없음: {levelName}");
                levelFilter = new ElementLevelFilter(level.Id);
            }

            IEnumerable<Element> elems1 = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType().OfCategory(cat1);
            IEnumerable<Element> elems2 = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType().OfCategory(cat2);
            if (levelFilter != null)
            {
                elems1 = new FilteredElementCollector(doc).WhereElementIsNotElementType().OfCategory(cat1).WherePasses(levelFilter);
                elems2 = new FilteredElementCollector(doc).WhereElementIsNotElementType().OfCategory(cat2).WherePasses(levelFilter);
            }

            var list1 = elems1.ToList();
            var list2 = elems2.ToList();

            int unjoined = 0;
            using var tx = new Transaction(doc, "MCP: Unjoin Geometry");
            tx.Start();
            foreach (var e1 in list1)
            {
                foreach (var e2 in list2)
                {
                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(doc, e1, e2)) continue;
                        JoinGeometryUtils.UnjoinGeometry(doc, e1, e2);
                        unjoined++;
                    }
                    catch { }
                }
            }
            tx.Commit();
            return TextContent($"Unjoin Geometry 완료: {unjoined}쌍 결합 해제 (대상: {list1.Count}×{list2.Count} 요소)");
        }
    }

    public class JoinGeometryByIdsTool : ToolBase
    {
        public override string Name => "join_geometry_by_ids";
        public override string Description => "지정한 요소 ID 목록 간 Join Geometry를 수행합니다. 두 그룹(ids1, ids2) 사이 모든 조합을 결합합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "ids1", "ids2" },
                ["properties"] = new JObject
                {
                    ["ids1"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" }, ["description"] = "첫 번째 요소 ID 목록" },
                    ["ids2"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" }, ["description"] = "두 번째 요소 ID 목록" },
                    ["unjoin"] = new JObject { ["type"] = "boolean", ["description"] = "true면 결합 해제, false(기본)면 결합" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var ids1 = args["ids1"]!.ToObject<List<long>>()!.Select(id => doc.GetElement(new ElementId(id))).Where(e => e != null).ToList();
            var ids2 = args["ids2"]!.ToObject<List<long>>()!.Select(id => doc.GetElement(new ElementId(id))).Where(e => e != null).ToList();
            var unjoin = args["unjoin"]?.ToObject<bool>() ?? false;

            int count = 0;
            using var tx = new Transaction(doc, unjoin ? "MCP: Unjoin Geometry" : "MCP: Join Geometry");
            tx.Start();
            foreach (var e1 in ids1)
            {
                foreach (var e2 in ids2)
                {
                    try
                    {
                        if (unjoin)
                        {
                            if (!JoinGeometryUtils.AreElementsJoined(doc, e1!, e2!)) continue;
                            JoinGeometryUtils.UnjoinGeometry(doc, e1!, e2!);
                        }
                        else
                        {
                            if (JoinGeometryUtils.AreElementsJoined(doc, e1!, e2!)) continue;
                            JoinGeometryUtils.JoinGeometry(doc, e1!, e2!);
                        }
                        count++;
                    }
                    catch { }
                }
            }
            tx.Commit();
            return TextContent($"{(unjoin ? "결합 해제" : "결합")} 완료: {count}쌍");
        }
    }

    public class SwitchJoinOrderTool : ToolBase
    {
        public override string Name => "switch_join_order";
        public override string Description => "두 요소 간 Join 우선순위(절단 방향)를 반전합니다. 어떤 요소가 다른 요소를 절단할지 변경합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId1", "elementId2" },
                ["properties"] = new JObject
                {
                    ["elementId1"] = new JObject { ["type"] = "integer" },
                    ["elementId2"] = new JObject { ["type"] = "integer" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var e1 = doc.GetElement(new ElementId(args["elementId1"]!.ToObject<long>())) ?? throw new Exception("요소1 없음");
            var e2 = doc.GetElement(new ElementId(args["elementId2"]!.ToObject<long>())) ?? throw new Exception("요소2 없음");
            if (!JoinGeometryUtils.AreElementsJoined(doc, e1, e2))
                throw new Exception("두 요소가 결합되어 있지 않습니다. 먼저 join_geometry_by_ids로 결합하세요.");
            using var tx = new Transaction(doc, "MCP: Join 우선순위 반전");
            tx.Start();
            JoinGeometryUtils.SwitchJoinOrder(doc, e1, e2);
            tx.Commit();
            return TextContent($"Join 우선순위 반전 완료: {e1.Id.Value} ↔ {e2.Id.Value}");
        }
    }
}
