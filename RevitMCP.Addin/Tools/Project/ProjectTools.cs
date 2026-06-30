using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Addin.Tools.Project
{
    public class ListPhasesTool : ToolBase
    {
        public override string Name => "list_phases";
        public override string Description => "프로젝트의 모든 공사 단계(Phase)를 반환합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var phases = doc.Phases.Cast<Phase>()
                .Select((p, i) => new JObject { ["index"] = i, ["name"] = p.Name, ["id"] = p.Id.Value });
            return TextContent(new JArray(phases).ToString());
        }
    }

    public class GetLinkedModelsTool : ToolBase
    {
        public override string Name => "get_linked_models";
        public override string Description => "현재 모델에 연결된 Revit 링크 파일 목록을 반환합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Select(l => new JObject
                {
                    ["name"] = l.Name,
                    ["id"] = l.Id.Value,
                    ["isLoaded"] = l.GetLinkDocument() != null
                });
            return TextContent(new JArray(links).ToString());
        }
    }

    public class GetProjectParametersTool : ToolBase
    {
        public override string Name => "get_project_parameters";
        public override string Description => "프로젝트에 정의된 모든 공유/프로젝트 파라미터 목록을 반환합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var bindings = doc.ParameterBindings;
            var iter = bindings.ForwardIterator();
            var result = new JArray();
            while (iter.MoveNext())
            {
                var def = iter.Key as InternalDefinition;
                if (def == null) continue;
                var binding = iter.Current as ElementBinding;
                result.Add(new JObject
                {
                    ["name"] = def.Name,
                    ["parameterType"] = def.GetDataType().TypeId,
                    ["bindingType"] = binding is InstanceBinding ? "Instance" : "Type",
                    ["group"] = TryGetGroupLabel(def)
                });
            }
            return TextContent(result.ToString());
        }

        private static string TryGetGroupLabel(InternalDefinition def)
        {
            try { return LabelUtils.GetLabelForGroup(def.GetGroupTypeId()); }
            catch { return def.GetGroupTypeId()?.TypeId ?? "Unknown"; }
        }
    }

    public class GetRevisionHistoryTool : ToolBase
    {
        public override string Name => "get_revision_history";
        public override string Description => "프로젝트의 개정 이력 목록을 반환합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var revisions = new FilteredElementCollector(doc)
                .OfClass(typeof(Revision))
                .Cast<Revision>()
                .Select(r => new JObject
                {
                    ["sequence"] = r.SequenceNumber,
                    ["description"] = r.Description,
                    ["date"] = r.RevisionDate,
                    ["issuedBy"] = r.IssuedBy,
                    ["issuedTo"] = r.IssuedTo,
                    ["id"] = r.Id.Value
                });
            return TextContent(new JArray(revisions).ToString());
        }
    }

    public class GetElementCountTool : ToolBase
    {
        public override string Name => "get_element_count";
        public override string Description => "카테고리별 요소 수를 집계합니다. levelName 지정 시 해당 레벨만 집계.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["levelName"] = new JObject { ["type"] = "string" },
                    ["category"] = new JObject { ["type"] = "string" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var levelName = args["levelName"]?.ToString();
            var catStr = args["category"]?.ToString();
            IEnumerable<Element> elems = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            if (levelName != null)
            {
                var level = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>().FirstOrDefault(l => l.Name == levelName)
                    ?? throw new Exception($"레벨 없음: {levelName}");
                elems = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                    .WherePasses(new ElementLevelFilter(level.Id));
            }
            if (catStr != null)
            {
                BuiltInCategory bic = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), catStr);
                elems = elems.Where(e => e.Category?.Id == new ElementId(bic));
            }
            var grouped = elems.Where(e => e.Category != null)
                .GroupBy(e => e.Category!.Name)
                .OrderByDescending(g => g.Count())
                .Select(g => new JObject { ["category"] = g.Key, ["count"] = g.Count() });
            return TextContent(new JArray(grouped).ToString());
        }
    }

    public class GetScheduleDataTool : ToolBase
    {
        public override string Name => "get_schedule_data";
        public override string Description => "일람표의 데이터를 표 형식으로 반환합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "scheduleName" },
                ["properties"] = new JObject
                {
                    ["scheduleName"] = new JObject { ["type"] = "string" },
                    ["maxRows"] = new JObject { ["type"] = "integer", ["description"] = "최대 행 수 (기본 100)" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var name = args["scheduleName"]!.ToString();
            var maxRows = args["maxRows"]?.ToObject<int>() ?? 100;
            var schedule = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => s.Name == name)
                ?? throw new Exception($"일람표 없음: {name}");
            var tableData = schedule.GetTableData();
            var section = tableData.GetSectionData(SectionType.Body);
            var rows = new JArray();
            for (int r = 0; r < Math.Min(section.NumberOfRows, maxRows); r++)
            {
                var row = new JArray();
                for (int c = 0; c < section.NumberOfColumns; c++)
                    row.Add(schedule.GetCellText(SectionType.Body, r, c));
                rows.Add(row);
            }
            return TextContent(new JObject
            {
                ["scheduleName"] = name,
                ["rows"] = rows,
                ["totalRows"] = section.NumberOfRows
            }.ToString());
        }
    }

    public class SetElementPhaseTool : ToolBase
    {
        public override string Name => "set_element_phase";
        public override string Description => "요소의 신설/철거 단계를 설정합니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementIds", "phaseCreatedName" },
                ["properties"] = new JObject
                {
                    ["elementIds"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } },
                    ["phaseCreatedName"] = new JObject { ["type"] = "string", ["description"] = "신설 단계 이름" },
                    ["phaseDemolishedName"] = new JObject { ["type"] = "string", ["description"] = "철거 단계 이름 (선택)" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var ids = args["elementIds"]!.ToObject<List<long>>()!.Select(id => new ElementId(id)).ToList();
            var phaseCreatedName = args["phaseCreatedName"]!.ToString();
            var phaseDemolishedName = args["phaseDemolishedName"]?.ToString();
            var phaseCreated = doc.Phases.Cast<Phase>().FirstOrDefault(p => p.Name == phaseCreatedName)
                ?? throw new Exception($"단계 없음: {phaseCreatedName}");
            Phase? phaseDemolished = phaseDemolishedName != null
                ? doc.Phases.Cast<Phase>().FirstOrDefault(p => p.Name == phaseDemolishedName) : null;
            using var tx = new Transaction(doc, "MCP: 단계 설정");
            tx.Start();
            foreach (var id in ids)
            {
                var elem = doc.GetElement(id);
                if (elem == null) continue;
                elem.get_Parameter(BuiltInParameter.PHASE_CREATED)?.Set(phaseCreated.Id);
                if (phaseDemolished != null)
                    elem.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED)?.Set(phaseDemolished.Id);
            }
            tx.Commit();
            return TextContent($"{ids.Count}개 요소 단계 설정 완료 → {phaseCreatedName}");
        }
    }

    public class CreateGroupTool : ToolBase
    {
        public override string Name => "create_group";
        public override string Description => "선택한 요소들을 그룹으로 묶습니다.";
        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementIds", "groupName" },
                ["properties"] = new JObject
                {
                    ["elementIds"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } },
                    ["groupName"] = new JObject { ["type"] = "string" }
                }
            }
        };
        public override JToken Execute(Document doc, JObject args)
        {
            var ids = args["elementIds"]!.ToObject<List<long>>()!.Select(id => new ElementId(id)).ToList();
            var groupName = args["groupName"]!.ToString();
            using var tx = new Transaction(doc, "MCP: 그룹 생성");
            tx.Start();
            var group = doc.Create.NewGroup(ids);
            group.GroupType.Name = groupName;
            tx.Commit();
            return TextContent($"그룹 생성 완료: {groupName} (ID: {group.Id.Value}, 요소 {ids.Count}개)");
        }
    }

    public class GetRoomsInfoTool : ToolBase
    {
        public override string Name => "get_rooms_info";
        public override string Description => "모든 룸의 이름, 번호, 면적, 레벨, 위치 정보를 반환합니다.";
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
            var levelFilter = args["levelName"]?.ToString();
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .Where(r => levelFilter == null || r.Level?.Name == levelFilter)
                .OrderBy(r => r.Level?.Name)
                .ThenBy(r => r.Number)
                .Select(r =>
                {
                    var loc = r.Location as LocationPoint;
                    return new JObject
                    {
                        ["number"] = r.Number,
                        ["name"] = r.Name,
                        ["level"] = r.Level?.Name,
                        ["area_m2"] = Math.Round(r.Area * 0.0929, 2),
                        ["perimeter_m"] = Math.Round(r.Perimeter * 0.3048, 2),
                        ["x_mm"] = loc != null ? Math.Round(loc.Point.X * 304.8, 0) : (double?)null,
                        ["y_mm"] = loc != null ? Math.Round(loc.Point.Y * 304.8, 0) : (double?)null
                    };
                });
            return TextContent(new JArray(rooms).ToString());
        }
    }
}
