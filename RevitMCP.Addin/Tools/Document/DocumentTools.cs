using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Addin.Tools.Docs
{
    public class CreateSheetTool : ToolBase
    {
        public override string Name => "create_sheet";
        public override string Description => "타이틀블록 패밀리를 사용해 도면 시트를 생성합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "sheetNumber", "sheetName" },
                ["properties"] = new JObject
                {
                    ["sheetNumber"] = new JObject { ["type"] = "string", ["description"] = "도면 번호 (예: A-101)" },
                    ["sheetName"] = new JObject { ["type"] = "string", ["description"] = "도면 이름" },
                    ["titleBlockName"] = new JObject { ["type"] = "string", ["description"] = "타이틀블록 패밀리 이름 (미지정 시 첫 번째 사용)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var sheetNum = args["sheetNumber"]!.ToString();
            var sheetName = args["sheetName"]!.ToString();

            FamilySymbol? titleBlock = null;
            if (args["titleBlockName"] is JToken tb)
                titleBlock = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Family.Name == tb.ToString());

            titleBlock ??= new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            using var tx = new Transaction(doc, "MCP: 시트 생성");
            tx.Start();
            var sheet = ViewSheet.Create(doc, titleBlock?.Id ?? ElementId.InvalidElementId);
            sheet.SheetNumber = sheetNum;
            sheet.Name = sheetName;
            tx.Commit();

            return TextContent($"시트 생성 완료 (ID: {sheet.Id.Value}, 번호: {sheetNum})");
        }
    }

    public class CreateViewTool : ToolBase
    {
        public override string Name => "create_view";
        public override string Description => "평면도, 입면도, 단면도 등 뷰를 생성합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "viewType", "levelNameOrId" },
                ["properties"] = new JObject
                {
                    ["viewType"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray { "FloorPlan", "CeilingPlan", "Elevation", "Section" },
                        ["description"] = "생성할 뷰 타입"
                    },
                    ["levelNameOrId"] = new JObject { ["type"] = "string", ["description"] = "레벨 이름 또는 ID" },
                    ["viewName"] = new JObject { ["type"] = "string", ["description"] = "뷰 이름 (미지정 시 자동)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var viewTypeStr = args["viewType"]!.ToString();
            var levelRef = args["levelNameOrId"]!.ToString();

            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => l.Name == levelRef || l.Id.Value.ToString() == levelRef)
                ?? throw new System.Exception($"레벨 '{levelRef}' 없음");

            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily.ToString().Contains(viewTypeStr))
                ?? throw new System.Exception($"뷰 패밀리 타입 '{viewTypeStr}' 없음");

            using var tx = new Transaction(doc, "MCP: 뷰 생성");
            tx.Start();
            View view = viewTypeStr switch
            {
                "FloorPlan" => ViewPlan.Create(doc, vft.Id, level.Id),
                "CeilingPlan" => ViewPlan.Create(doc, vft.Id, level.Id),
                _ => ViewPlan.Create(doc, vft.Id, level.Id)
            };

            if (args["viewName"] is JToken vn)
                view.Name = vn.ToString();
            tx.Commit();

            return TextContent($"뷰 생성 완료 (ID: {view.Id.Value}, 이름: {view.Name})");
        }
    }

    public class PlaceViewportTool : ToolBase
    {
        public override string Name => "place_viewport";
        public override string Description => "시트에 뷰 뷰포트를 배치합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "sheetId", "viewId", "x", "y" },
                ["properties"] = new JObject
                {
                    ["sheetId"] = new JObject { ["type"] = "integer" },
                    ["viewId"] = new JObject { ["type"] = "integer" },
                    ["x"] = new JObject { ["type"] = "number", ["description"] = "배치 X (mm)" },
                    ["y"] = new JObject { ["type"] = "number", ["description"] = "배치 Y (mm)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            double MmToFt(double mm) => mm / 304.8;
            var sheetId = new ElementId(args["sheetId"]!.ToObject<int>());
            var viewId = new ElementId(args["viewId"]!.ToObject<int>());
            var pt = new XYZ(MmToFt(args["x"]!.ToObject<double>()), MmToFt(args["y"]!.ToObject<double>()), 0);

            using var tx = new Transaction(doc, "MCP: 뷰포트 배치");
            tx.Start();
            var vp = Viewport.Create(doc, sheetId, viewId, pt);
            tx.Commit();
            return TextContent($"뷰포트 배치 완료 (ID: {vp.Id.Value})");
        }
    }

    public class CreateScheduleTool : ToolBase
    {
        public override string Name => "create_schedule";
        public override string Description => "지정 카테고리의 일람표를 생성합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "category", "fields" },
                ["properties"] = new JObject
                {
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "BuiltInCategory 이름 (예: OST_Doors)" },
                    ["fields"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "포함할 파라미터 이름 목록"
                    },
                    ["scheduleName"] = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            if (!System.Enum.TryParse<BuiltInCategory>(args["category"]!.ToString(), out var bic))
                return ErrorContent("잘못된 카테고리");

            var fields = args["fields"]!.ToObject<string[]>()!;
            var name = args["scheduleName"]?.ToString() ?? $"{args["category"]} 일람표";

            using var tx = new Transaction(doc, "MCP: 일람표 생성");
            tx.Start();
            var schedule = ViewSchedule.CreateSchedule(doc, new ElementId(bic));
            schedule.Name = name;

            var def = schedule.Definition;
            foreach (var field in fields)
            {
                var sf = def.GetSchedulableFields().FirstOrDefault(f => f.GetName(doc) == field);
                if (sf != null) def.AddField(sf);
            }
            tx.Commit();

            return TextContent($"일람표 생성 완료 (ID: {schedule.Id.Value})");
        }
    }

    public class ExportSheetsTool : ToolBase
    {
        public override string Name => "export_sheets_to_pdf";
        public override string Description => "시트들을 PDF로 내보냅니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "outputFolder" },
                ["properties"] = new JObject
                {
                    ["outputFolder"] = new JObject { ["type"] = "string", ["description"] = "출력 폴더 경로" },
                    ["sheetIds"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "integer" },
                        ["description"] = "내보낼 시트 ID (미지정 시 전체)"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var folder = args["outputFolder"]!.ToString();
            System.IO.Directory.CreateDirectory(folder);

            IEnumerable<ViewSheet> sheets;
            if (args["sheetIds"] is JArray ids && ids.Count > 0)
            {
                sheets = ids.Select(i => doc.GetElement(new ElementId(i.ToObject<int>())) as ViewSheet)
                            .Where(s => s != null)!;
            }
            else
            {
                sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>();
            }

            var pdfOptions = new PDFExportOptions
            {
                Combine = false,
                FileName = "sheet"
            };

            var viewIds = sheets.Select(s => s.Id).ToList();
            doc.Export(folder, viewIds, pdfOptions);

            return TextContent($"{viewIds.Count}개 시트를 '{folder}'에 PDF로 내보냄");
        }
    }

    public class AddRevisionTool : ToolBase
    {
        public override string Name => "add_revision";
        public override string Description => "문서에 개정(Revision)을 추가합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "description" },
                ["properties"] = new JObject
                {
                    ["description"] = new JObject { ["type"] = "string", ["description"] = "개정 설명" },
                    ["revisionDate"] = new JObject { ["type"] = "string", ["description"] = "개정 날짜 (yyyy-MM-dd)" },
                    ["issuedBy"] = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            using var tx = new Transaction(doc, "MCP: 개정 추가");
            tx.Start();
            var rev = Revision.Create(doc);
            rev.Description = args["description"]!.ToString();
            if (args["revisionDate"] is JToken rd) rev.RevisionDate = rd.ToString();
            if (args["issuedBy"] is JToken ib) rev.IssuedBy = ib.ToString();
            tx.Commit();
            return TextContent($"개정 추가 완료 (ID: {rev.Id.Value})");
        }
    }

    public class CreateTextNoteTool : ToolBase
    {
        public override string Name => "create_text_note";
        public override string Description => "뷰에 텍스트 주석을 추가합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "viewId", "x", "y", "text" },
                ["properties"] = new JObject
                {
                    ["viewId"] = new JObject { ["type"] = "integer" },
                    ["x"] = new JObject { ["type"] = "number" },
                    ["y"] = new JObject { ["type"] = "number" },
                    ["text"] = new JObject { ["type"] = "string" },
                    ["width"] = new JObject { ["type"] = "number", ["description"] = "텍스트 박스 폭 (mm, 기본 500)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            double MmToFt(double mm) => mm / 304.8;
            var viewId = new ElementId(args["viewId"]!.ToObject<int>());
            var view = doc.GetElement(viewId) as View ?? throw new System.Exception("뷰 없음");
            var pt = new XYZ(MmToFt(args["x"]!.ToObject<double>()), MmToFt(args["y"]!.ToObject<double>()), 0);
            var width = MmToFt(args["width"]?.ToObject<double>() ?? 500);

            var tnt = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().First();

            using var tx = new Transaction(doc, "MCP: 텍스트 추가");
            tx.Start();
            var tn = TextNote.Create(doc, viewId, pt, width, args["text"]!.ToString(), tnt.Id);
            tx.Commit();
            return TextContent($"텍스트 주석 생성 완료 (ID: {tn.Id.Value})");
        }
    }

    public class TagElementTool : ToolBase
    {
        public override string Name => "tag_element";
        public override string Description => "요소에 태그를 자동으로 달아줍니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementId", "viewId" },
                ["properties"] = new JObject
                {
                    ["elementId"] = new JObject { ["type"] = "integer" },
                    ["viewId"] = new JObject { ["type"] = "integer" },
                    ["addLeader"] = new JObject { ["type"] = "boolean", ["description"] = "지시선 추가 여부" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var elemId = new ElementId(args["elementId"]!.ToObject<int>());
            var viewId = new ElementId(args["viewId"]!.ToObject<int>());
            var view = doc.GetElement(viewId) as View ?? throw new System.Exception("뷰 없음");
            var elem = doc.GetElement(elemId);
            var bb = elem.get_BoundingBox(view);
            var center = (bb.Min + bb.Max) / 2.0;
            var addLeader = args["addLeader"]?.ToObject<bool>() ?? false;

            using var tx = new Transaction(doc, "MCP: 태그 추가");
            tx.Start();
            var tag = IndependentTag.Create(doc, viewId, new Reference(elem), addLeader, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, center);
            tx.Commit();
            return TextContent($"태그 추가 완료 (ID: {tag.Id.Value})");
        }
    }
}
