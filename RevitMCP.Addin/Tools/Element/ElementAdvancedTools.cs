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
}
