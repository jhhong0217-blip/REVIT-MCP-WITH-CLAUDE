using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace RevitMCP.Addin.Tools.Modeling
{
    public class CreateWallTool : ToolBase
    {
        public override string Name => "create_wall";
        public override string Description => "두 점 사이에 벽을 생성합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "x1", "y1", "x2", "y2", "levelName" },
                ["properties"] = new JObject
                {
                    ["x1"] = new JObject { ["type"] = "number", ["description"] = "시작점 X (mm)" },
                    ["y1"] = new JObject { ["type"] = "number", ["description"] = "시작점 Y (mm)" },
                    ["x2"] = new JObject { ["type"] = "number", ["description"] = "끝점 X (mm)" },
                    ["y2"] = new JObject { ["type"] = "number", ["description"] = "끝점 Y (mm)" },
                    ["levelName"] = new JObject { ["type"] = "string", ["description"] = "배치할 레벨 이름" },
                    ["height"] = new JObject { ["type"] = "number", ["description"] = "벽 높이 (mm, 기본 3000)" },
                    ["wallTypeName"] = new JObject { ["type"] = "string", ["description"] = "벽 타입 이름 (미지정 시 첫 번째 타입 사용)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            double MmToFt(double mm) => mm / 304.8;

            var p1 = new XYZ(MmToFt(args["x1"]!.ToObject<double>()), MmToFt(args["y1"]!.ToObject<double>()), 0);
            var p2 = new XYZ(MmToFt(args["x2"]!.ToObject<double>()), MmToFt(args["y2"]!.ToObject<double>()), 0);
            var height = MmToFt(args["height"]?.ToObject<double>() ?? 3000);
            var levelName = args["levelName"]!.ToString();

            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => l.Name == levelName)
                ?? throw new System.Exception($"레벨 '{levelName}'을 찾을 수 없습니다.");

            WallType? wallType = null;
            if (args["wallTypeName"] is JToken wtn)
            {
                wallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType)).Cast<WallType>()
                    .FirstOrDefault(t => t.Name == wtn.ToString());
            }
            wallType ??= new FilteredElementCollector(doc)
                .OfClass(typeof(WallType)).Cast<WallType>().First();

            using var tx = new Transaction(doc, "MCP: 벽 생성");
            tx.Start();
            var wall = Wall.Create(doc, Line.CreateBound(p1, p2), wallType.Id, level.Id, height, 0, false, false);
            tx.Commit();

            return TextContent($"벽 생성 완료 (ID: {wall.Id.IntegerValue})");
        }
    }

    public class CreateFloorTool : ToolBase
    {
        public override string Name => "create_floor";
        public override string Description => "폴리라인 경계로 바닥 슬래브를 생성합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "points", "levelName" },
                ["properties"] = new JObject
                {
                    ["points"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "꼭짓점 배열 [{x, y}] (mm 단위)",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["x"] = new JObject { ["type"] = "number" },
                                ["y"] = new JObject { ["type"] = "number" }
                            }
                        }
                    },
                    ["levelName"] = new JObject { ["type"] = "string" },
                    ["floorTypeName"] = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            double MmToFt(double mm) => mm / 304.8;

            var pts = args["points"]!.ToObject<JArray>()!
                .Select(p => new XYZ(MmToFt(p["x"]!.ToObject<double>()), MmToFt(p["y"]!.ToObject<double>()), 0))
                .ToList();

            var levelName = args["levelName"]!.ToString();
            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => l.Name == levelName)
                ?? throw new System.Exception($"레벨 '{levelName}' 없음");

            var loop = new CurveLoop();
            for (int i = 0; i < pts.Count; i++)
                loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));

            FloorType floorType;
            if (args["floorTypeName"] is JToken ftn)
                floorType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                    .FirstOrDefault(t => t.Name == ftn.ToString())
                    ?? new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First();
            else
                floorType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First();

            using var tx = new Transaction(doc, "MCP: 바닥 생성");
            tx.Start();
            var floor = Floor.Create(doc, new System.Collections.Generic.List<CurveLoop> { loop }, floorType.Id, level.Id);
            tx.Commit();

            return TextContent($"바닥 생성 완료 (ID: {floor.Id.IntegerValue})");
        }
    }

    public class CreateRoomTool : ToolBase
    {
        public override string Name => "create_room";
        public override string Description => "지정한 위치에 룸을 생성합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "x", "y", "levelName" },
                ["properties"] = new JObject
                {
                    ["x"] = new JObject { ["type"] = "number", ["description"] = "X 좌표 (mm)" },
                    ["y"] = new JObject { ["type"] = "number", ["description"] = "Y 좌표 (mm)" },
                    ["levelName"] = new JObject { ["type"] = "string" },
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "룸 이름" },
                    ["number"] = new JObject { ["type"] = "string", ["description"] = "룸 번호" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            double MmToFt(double mm) => mm / 304.8;
            var x = MmToFt(args["x"]!.ToObject<double>());
            var y = MmToFt(args["y"]!.ToObject<double>());
            var levelName = args["levelName"]!.ToString();

            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => l.Name == levelName)
                ?? throw new System.Exception($"레벨 '{levelName}' 없음");

            using var tx = new Transaction(doc, "MCP: 룸 생성");
            tx.Start();
            var uv = new UV(x, y);
            var room = doc.Create.NewRoom(level, uv);
            if (args["name"] is JToken rn) room.Name = rn.ToString();
            if (args["number"] is JToken rnum) room.Number = rnum.ToString();
            tx.Commit();

            return TextContent($"룸 생성 완료 (ID: {room.Id.IntegerValue}, 이름: {room.Name})");
        }
    }

    public class CreateGridTool : ToolBase
    {
        public override string Name => "create_grid";
        public override string Description => "수평 또는 수직 그리드 선을 생성합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "x1", "y1", "x2", "y2" },
                ["properties"] = new JObject
                {
                    ["x1"] = new JObject { ["type"] = "number" },
                    ["y1"] = new JObject { ["type"] = "number" },
                    ["x2"] = new JObject { ["type"] = "number" },
                    ["y2"] = new JObject { ["type"] = "number" },
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "그리드 이름 (예: A, 1)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            double MmToFt(double mm) => mm / 304.8;
            var p1 = new XYZ(MmToFt(args["x1"]!.ToObject<double>()), MmToFt(args["y1"]!.ToObject<double>()), 0);
            var p2 = new XYZ(MmToFt(args["x2"]!.ToObject<double>()), MmToFt(args["y2"]!.ToObject<double>()), 0);

            using var tx = new Transaction(doc, "MCP: 그리드 생성");
            tx.Start();
            var grid = Grid.Create(doc, Line.CreateBound(p1, p2));
            if (args["name"] is JToken n) grid.Name = n.ToString();
            tx.Commit();

            return TextContent($"그리드 생성 완료 (ID: {grid.Id.IntegerValue}, 이름: {grid.Name})");
        }
    }

    public class CreateLevelTool : ToolBase
    {
        public override string Name => "create_level";
        public override string Description => "지정한 높이에 레벨을 생성합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elevation", "name" },
                ["properties"] = new JObject
                {
                    ["elevation"] = new JObject { ["type"] = "number", ["description"] = "레벨 높이 (mm)" },
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "레벨 이름" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            double elev = args["elevation"]!.ToObject<double>() / 304.8;
            string name = args["name"]!.ToString();

            using var tx = new Transaction(doc, "MCP: 레벨 생성");
            tx.Start();
            var level = Level.Create(doc, elev);
            level.Name = name;
            tx.Commit();

            return TextContent($"레벨 생성 완료 (ID: {level.Id.IntegerValue}, 높이: {args["elevation"]}mm)");
        }
    }

    public class PlaceFamilyInstanceTool : ToolBase
    {
        public override string Name => "place_family_instance";
        public override string Description => "패밀리 인스턴스를 지정 위치에 배치합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "familyName", "typeName", "x", "y", "levelName" },
                ["properties"] = new JObject
                {
                    ["familyName"] = new JObject { ["type"] = "string" },
                    ["typeName"] = new JObject { ["type"] = "string" },
                    ["x"] = new JObject { ["type"] = "number", ["description"] = "X (mm)" },
                    ["y"] = new JObject { ["type"] = "number", ["description"] = "Y (mm)" },
                    ["z"] = new JObject { ["type"] = "number", ["description"] = "Z (mm, 기본 0)" },
                    ["levelName"] = new JObject { ["type"] = "string" },
                    ["rotation"] = new JObject { ["type"] = "number", ["description"] = "회전각 (도)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            double MmToFt(double mm) => mm / 304.8;
            var familyName = args["familyName"]!.ToString();
            var typeName = args["typeName"]!.ToString();
            var pt = new XYZ(MmToFt(args["x"]!.ToObject<double>()),
                             MmToFt(args["y"]!.ToObject<double>()),
                             MmToFt(args["z"]?.ToObject<double>() ?? 0));

            var symbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Family.Name == familyName && s.Name == typeName)
                ?? throw new System.Exception($"패밀리 타입 '{familyName}:{typeName}' 없음");

            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => l.Name == args["levelName"]!.ToString())
                ?? throw new System.Exception("레벨 없음");

            using var tx = new Transaction(doc, "MCP: 패밀리 배치");
            tx.Start();
            if (!symbol.IsActive) symbol.Activate();
            var inst = doc.Create.NewFamilyInstance(pt, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

            if (args["rotation"] is JToken rot)
            {
                var axis = Line.CreateBound(pt, pt + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, inst.Id, axis, rot.ToObject<double>() * System.Math.PI / 180.0);
            }
            tx.Commit();

            return TextContent($"패밀리 배치 완료 (ID: {inst.Id.IntegerValue})");
        }
    }

    public class MoveElementTool : ToolBase
    {
        public override string Name => "move_element";
        public override string Description => "요소를 지정한 벡터만큼 이동합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId", "dx", "dy" },
                ["properties"] = new JObject
                {
                    ["elementId"] = new JObject { ["type"] = "integer" },
                    ["dx"] = new JObject { ["type"] = "number", ["description"] = "X 이동량 (mm)" },
                    ["dy"] = new JObject { ["type"] = "number", ["description"] = "Y 이동량 (mm)" },
                    ["dz"] = new JObject { ["type"] = "number", ["description"] = "Z 이동량 (mm)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            double MmToFt(double mm) => mm / 304.8;
            var id = new ElementId(args["elementId"]!.ToObject<int>());
            var v = new XYZ(MmToFt(args["dx"]!.ToObject<double>()),
                            MmToFt(args["dy"]!.ToObject<double>()),
                            MmToFt(args["dz"]?.ToObject<double>() ?? 0));
            using var tx = new Transaction(doc, "MCP: 요소 이동");
            tx.Start();
            ElementTransformUtils.MoveElement(doc, id, v);
            tx.Commit();
            return TextContent($"요소 {id.IntegerValue} 이동 완료");
        }
    }

    public class CopyElementTool : ToolBase
    {
        public override string Name => "copy_element";
        public override string Description => "요소를 지정 벡터 방향으로 복사합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId", "dx", "dy" },
                ["properties"] = new JObject
                {
                    ["elementId"] = new JObject { ["type"] = "integer" },
                    ["dx"] = new JObject { ["type"] = "number" },
                    ["dy"] = new JObject { ["type"] = "number" },
                    ["dz"] = new JObject { ["type"] = "number" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            double MmToFt(double mm) => mm / 304.8;
            var id = new ElementId(args["elementId"]!.ToObject<int>());
            var v = new XYZ(MmToFt(args["dx"]!.ToObject<double>()),
                            MmToFt(args["dy"]!.ToObject<double>()),
                            MmToFt(args["dz"]?.ToObject<double>() ?? 0));
            using var tx = new Transaction(doc, "MCP: 요소 복사");
            tx.Start();
            var copied = ElementTransformUtils.CopyElement(doc, id, v);
            tx.Commit();
            return TextContent($"복사 완료 → ID: {string.Join(", ", System.Linq.Enumerable.Select(copied, i => i.IntegerValue))}");
        }
    }

    public class RotateElementTool : ToolBase
    {
        public override string Name => "rotate_element";
        public override string Description => "요소를 Z축 기준으로 회전합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId", "angle" },
                ["properties"] = new JObject
                {
                    ["elementId"] = new JObject { ["type"] = "integer" },
                    ["angle"] = new JObject { ["type"] = "number", ["description"] = "회전각 (도)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var id = new ElementId(args["elementId"]!.ToObject<int>());
            var elem = doc.GetElement(id);
            var bb = elem.get_BoundingBox(null);
            var center = (bb.Min + bb.Max) / 2.0;
            var axis = Line.CreateBound(center, center + XYZ.BasisZ);
            using var tx = new Transaction(doc, "MCP: 요소 회전");
            tx.Start();
            ElementTransformUtils.RotateElement(doc, id, axis, args["angle"]!.ToObject<double>() * System.Math.PI / 180.0);
            tx.Commit();
            return TextContent("회전 완료");
        }
    }

    public class MirrorElementTool : ToolBase
    {
        public override string Name => "mirror_element";
        public override string Description => "요소를 지정한 평면에 대해 미러합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId", "axis" },
                ["properties"] = new JObject
                {
                    ["elementId"] = new JObject { ["type"] = "integer" },
                    ["axis"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "X", "Y" }, ["description"] = "미러 축" },
                    ["offset"] = new JObject { ["type"] = "number", ["description"] = "미러 평면 오프셋 (mm)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            double MmToFt(double mm) => mm / 304.8;
            var id = new ElementId(args["elementId"]!.ToObject<int>());
            var offset = MmToFt(args["offset"]?.ToObject<double>() ?? 0);
            var axisStr = args["axis"]!.ToString();

            Plane plane = axisStr == "X"
                ? Plane.CreateByNormalAndOrigin(XYZ.BasisX, new XYZ(offset, 0, 0))
                : Plane.CreateByNormalAndOrigin(XYZ.BasisY, new XYZ(0, offset, 0));

            using var tx = new Transaction(doc, "MCP: 요소 미러");
            tx.Start();
            var mirrored = ElementTransformUtils.MirrorElement(doc, id, plane);
            tx.Commit();
            return TextContent($"미러 완료 → ID: {string.Join(", ", System.Linq.Enumerable.Select(mirrored, i => i.IntegerValue))}");
        }
    }

    public class CreateDimensionTool : ToolBase
    {
        public override string Name => "create_dimension";
        public override string Description => "두 참조 요소 사이에 치수선을 작성합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId1", "elementId2", "viewId" },
                ["properties"] = new JObject
                {
                    ["elementId1"] = new JObject { ["type"] = "integer" },
                    ["elementId2"] = new JObject { ["type"] = "integer" },
                    ["viewId"] = new JObject { ["type"] = "integer" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var id1 = new ElementId(args["elementId1"]!.ToObject<int>());
            var id2 = new ElementId(args["elementId2"]!.ToObject<int>());
            var viewId = new ElementId(args["viewId"]!.ToObject<int>());
            var view = doc.GetElement(viewId) as View
                ?? throw new System.Exception("유효하지 않은 뷰 ID");

            var e1 = doc.GetElement(id1);
            var e2 = doc.GetElement(id2);
            var bb1 = e1.get_BoundingBox(view);
            var bb2 = e2.get_BoundingBox(view);
            var c1 = (bb1.Min + bb1.Max) / 2;
            var c2 = (bb2.Min + bb2.Max) / 2;

            var refs = new ReferenceArray();
            refs.Append(e1.GetGeneratingElementIds(doc) != null ? new Reference(e1) : new Reference(e1));
            refs.Append(new Reference(e2));

            var line = Line.CreateBound(c1, c2);

            using var tx = new Transaction(doc, "MCP: 치수 생성");
            tx.Start();
            doc.Create.NewDimension(view, line, refs);
            tx.Commit();
            return TextContent("치수 생성 완료");
        }
    }
}
