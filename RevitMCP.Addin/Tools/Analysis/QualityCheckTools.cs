using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Addin.Tools.Analysis
{
    // ── 미태그 요소 탐지 ──────────────────────────────────────────
    public class FindUntaggedElementsTool : ToolBase
    {
        public override string Name => "find_untagged_elements";
        public override string Description => "태그가 달리지 않은 요소를 찾아 목록으로 반환합니다.";

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
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "BuiltInCategory 이름 (예: OST_Doors)" },
                    ["viewId"]   = new JObject { ["type"] = "integer", ["description"] = "검사할 뷰 ID" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            if (!System.Enum.TryParse<BuiltInCategory>(args["category"]!.ToString(), out var bic))
                return ErrorContent("잘못된 카테고리");

            var viewId = new ElementId(args["viewId"]!.ToObject<int>());

            var elements = new FilteredElementCollector(doc, viewId)
                .OfCategory(bic).WhereElementIsNotElementType().ToList();

            var taggedIds = new FilteredElementCollector(doc, viewId)
                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
                .Where(t => t.GetTaggedElementIds().Count > 0)
                .SelectMany(t => t.GetTaggedElementIds().Select(r => r.HostElementId))
                .ToHashSet();

            var untagged = elements
                .Where(e => !taggedIds.Contains(e.Id))
                .Select(e => new JObject
                {
                    ["id"]       = e.Id.IntegerValue,
                    ["name"]     = e.Name,
                    ["category"] = e.Category?.Name ?? ""
                }).ToList();

            return TextContent($"미태그 요소 {untagged.Count}개\n{new JArray(untagged)}");
        }
    }

    // ── 미치수 요소 탐지 ──────────────────────────────────────────
    public class FindUndimensionedElementsTool : ToolBase
    {
        public override string Name => "find_undimensioned_elements";
        public override string Description => "치수선이 없는 요소를 찾아 반환합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "viewId" },
                ["properties"] = new JObject
                {
                    ["viewId"]   = new JObject { ["type"] = "integer" },
                    ["category"] = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var viewId = new ElementId(args["viewId"]!.ToObject<int>());

            var dims = new FilteredElementCollector(doc, viewId)
                .OfClass(typeof(Dimension)).Cast<Dimension>()
                .SelectMany(d =>
                {
                    var ids = new List<ElementId>();
                    foreach (Reference r in d.References)
                        if (r.ElementId != ElementId.InvalidElementId)
                            ids.Add(r.ElementId);
                    return ids;
                })
                .ToHashSet();

            var collector = new FilteredElementCollector(doc, viewId).WhereElementIsNotElementType();
            if (args["category"] is JToken cat && System.Enum.TryParse<BuiltInCategory>(cat.ToString(), out var bic))
                collector = collector.OfCategory(bic);

            var undim = collector
                .Where(e => !dims.Contains(e.Id))
                .Select(e => new JObject
                {
                    ["id"]       = e.Id.IntegerValue,
                    ["name"]     = e.Name,
                    ["category"] = e.Category?.Name ?? ""
                }).ToList();

            return TextContent($"미치수 요소 {undim.Count}개\n{new JArray(undim)}");
        }
    }

    // ── 간섭 요소 색상 표시 ───────────────────────────────────────
    public class ColorClashElementsTool : ToolBase
    {
        public override string Name => "color_clash_elements";
        public override string Description => "간섭 요소들을 지정한 색상으로 뷰에 표시합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementIds", "viewId" },
                ["properties"] = new JObject
                {
                    ["elementIds"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } },
                    ["viewId"]     = new JObject { ["type"] = "integer" },
                    ["r"]          = new JObject { ["type"] = "integer", ["description"] = "Red 0-255 (기본 255)" },
                    ["g"]          = new JObject { ["type"] = "integer", ["description"] = "Green 0-255 (기본 0)" },
                    ["b"]          = new JObject { ["type"] = "integer", ["description"] = "Blue 0-255 (기본 0)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var viewId = new ElementId(args["viewId"]!.ToObject<int>());
            var view   = doc.GetElement(viewId) as View ?? throw new System.Exception("뷰 없음");
            var ids    = args["elementIds"]!.ToObject<int[]>()!.Select(i => new ElementId(i)).ToList();
            int r = args["r"]?.ToObject<int>() ?? 255;
            int g = args["g"]?.ToObject<int>() ?? 0;
            int b = args["b"]?.ToObject<int>() ?? 0;

            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Color((byte)r, (byte)g, (byte)b));
            ogs.SetSurfaceForegroundPatternColor(new Color((byte)r, (byte)g, (byte)b));
            ogs.SetSurfaceTransparency(50);

            using var tx = new Transaction(doc, "MCP: 간섭 색상 표시");
            tx.Start();
            foreach (var id in ids)
                view.SetElementOverrides(id, ogs);
            tx.Commit();

            return TextContent($"{ids.Count}개 요소에 간섭 색상 적용 완료");
        }
    }

    // ── 미사용 요소 정리 ──────────────────────────────────────────
    public class PurgeUnusedTool : ToolBase
    {
        public override string Name => "purge_unused";
        public override string Description => "모델에서 사용되지 않는 패밀리/타입/재료 등을 정리합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var unusedIds = new HashSet<ElementId>();

            // 미사용 패밀리 심볼 수집
            var usedSymbolIds = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.GetTypeId() != ElementId.InvalidElementId)
                .Select(e => e.GetTypeId())
                .ToHashSet();

            var unusedSymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(s => !usedSymbolIds.Contains(s.Id))
                .Select(s => s.Id)
                .ToList();

            unusedIds.UnionWith(unusedSymbols);

            using var tx = new Transaction(doc, "MCP: 미사용 요소 정리");
            tx.Start();
            var deleted = doc.Delete(unusedIds.ToList());
            tx.Commit();

            return TextContent($"미사용 요소 {deleted.Count}개 정리 완료");
        }
    }

    // ── 뷰 그래픽 재지정 ──────────────────────────────────────────
    public class OverrideGraphicsTool : ToolBase
    {
        public override string Name => "override_element_graphics";
        public override string Description => "요소의 뷰 그래픽(선 색상, 투명도 등)을 재지정합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "elementIds", "viewId" },
                ["properties"] = new JObject
                {
                    ["elementIds"]   = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } },
                    ["viewId"]       = new JObject { ["type"] = "integer" },
                    ["r"]            = new JObject { ["type"] = "integer" },
                    ["g"]            = new JObject { ["type"] = "integer" },
                    ["b"]            = new JObject { ["type"] = "integer" },
                    ["transparency"] = new JObject { ["type"] = "integer", ["description"] = "투명도 0-100" },
                    ["reset"]        = new JObject { ["type"] = "boolean", ["description"] = "true이면 재지정 초기화" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var viewId = new ElementId(args["viewId"]!.ToObject<int>());
            var view   = doc.GetElement(viewId) as View ?? throw new System.Exception("뷰 없음");
            var ids    = args["elementIds"]!.ToObject<int[]>()!.Select(i => new ElementId(i)).ToList();
            var reset  = args["reset"]?.ToObject<bool>() ?? false;

            using var tx = new Transaction(doc, "MCP: 그래픽 재지정");
            tx.Start();
            foreach (var id in ids)
            {
                if (reset)
                {
                    view.SetElementOverrides(id, new OverrideGraphicSettings());
                }
                else
                {
                    var ogs = new OverrideGraphicSettings();
                    if (args["r"] != null)
                    {
                        var color = new Color(
                            (byte)(args["r"]?.ToObject<int>() ?? 0),
                            (byte)(args["g"]?.ToObject<int>() ?? 0),
                            (byte)(args["b"]?.ToObject<int>() ?? 0));
                        ogs.SetProjectionLineColor(color);
                        ogs.SetSurfaceForegroundPatternColor(color);
                    }
                    if (args["transparency"] != null)
                        ogs.SetSurfaceTransparency(args["transparency"]!.ToObject<int>());
                    view.SetElementOverrides(id, ogs);
                }
            }
            tx.Commit();
            return TextContent($"{ids.Count}개 요소 그래픽 재지정 완료");
        }
    }

    // ── 뷰 필터 생성 ──────────────────────────────────────────────
    public class CreateViewFilterTool : ToolBase
    {
        public override string Name => "create_view_filter";
        public override string Description => "파라미터 조건 기반의 뷰 필터를 생성하고 뷰에 적용합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "filterName", "category", "viewId", "paramName", "paramValue" },
                ["properties"] = new JObject
                {
                    ["filterName"]  = new JObject { ["type"] = "string" },
                    ["category"]    = new JObject { ["type"] = "string" },
                    ["viewId"]      = new JObject { ["type"] = "integer" },
                    ["paramName"]   = new JObject { ["type"] = "string" },
                    ["paramValue"]  = new JObject { ["type"] = "string" },
                    ["r"]           = new JObject { ["type"] = "integer" },
                    ["g"]           = new JObject { ["type"] = "integer" },
                    ["b"]           = new JObject { ["type"] = "integer" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            if (!System.Enum.TryParse<BuiltInCategory>(args["category"]!.ToString(), out var bic))
                return ErrorContent("잘못된 카테고리");

            var viewId    = new ElementId(args["viewId"]!.ToObject<int>());
            var view      = doc.GetElement(viewId) as View ?? throw new System.Exception("뷰 없음");
            var catIds    = new List<ElementId> { new ElementId(bic) };
            var paramName = args["paramName"]!.ToString();
            var paramVal  = args["paramValue"]!.ToString();

            // 파라미터 찾기
            var elem = new FilteredElementCollector(doc).OfCategory(bic)
                .WhereElementIsNotElementType().FirstOrDefault();
            if (elem == null) return ErrorContent("해당 카테고리 요소 없음");

            var param = elem.LookupParameter(paramName)
                ?? throw new System.Exception($"파라미터 '{paramName}' 없음");

            using var tx = new Transaction(doc, "MCP: 뷰 필터 생성");
            tx.Start();

            var rule = ParameterFilterRuleFactory.CreateEqualsRule(param.Id, paramVal, false);
            var filter = ParameterFilterElement.Create(doc, args["filterName"]!.ToString(), catIds,
                new ElementParameterFilter(rule));

            view.AddFilter(filter.Id);

            if (args["r"] != null)
            {
                var ogs = new OverrideGraphicSettings();
                var color = new Color(
                    (byte)(args["r"]!.ToObject<int>()),
                    (byte)(args["g"]?.ToObject<int>() ?? 0),
                    (byte)(args["b"]?.ToObject<int>() ?? 0));
                ogs.SetSurfaceForegroundPatternColor(color);
                view.SetFilterOverrides(filter.Id, ogs);
            }

            tx.Commit();
            return TextContent($"뷰 필터 '{args["filterName"]}' 생성 및 적용 완료 (ID: {filter.Id.IntegerValue})");
        }
    }

    // ── 뷰 템플릿 적용 ────────────────────────────────────────────
    public class ApplyViewTemplateTool : ToolBase
    {
        public override string Name => "apply_view_template";
        public override string Description => "뷰에 뷰 템플릿을 적용합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "viewId", "templateName" },
                ["properties"] = new JObject
                {
                    ["viewId"]       = new JObject { ["type"] = "integer" },
                    ["templateName"] = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var viewId = new ElementId(args["viewId"]!.ToObject<int>());
            var view   = doc.GetElement(viewId) as View ?? throw new System.Exception("뷰 없음");
            var tmplName = args["templateName"]!.ToString();

            var template = new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(v => v.IsTemplate && v.Name == tmplName)
                ?? throw new System.Exception($"뷰 템플릿 '{tmplName}' 없음");

            using var tx = new Transaction(doc, "MCP: 뷰 템플릿 적용");
            tx.Start();
            view.ViewTemplateId = template.Id;
            tx.Commit();

            return TextContent($"뷰 템플릿 '{tmplName}' 적용 완료");
        }
    }

    // ── 뷰 복제 ───────────────────────────────────────────────────
    public class DuplicateViewTool : ToolBase
    {
        public override string Name => "duplicate_view";
        public override string Description => "뷰를 복제합니다 (독립 복사 또는 종속 복사).";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "viewId" },
                ["properties"] = new JObject
                {
                    ["viewId"]   = new JObject { ["type"] = "integer" },
                    ["newName"]  = new JObject { ["type"] = "string" },
                    ["withDetailing"] = new JObject { ["type"] = "boolean", ["description"] = "상세 포함 여부 (기본 false)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var viewId = new ElementId(args["viewId"]!.ToObject<int>());
            var view   = doc.GetElement(viewId) as View ?? throw new System.Exception("뷰 없음");
            var withDetail = args["withDetailing"]?.ToObject<bool>() ?? false;
            var mode = withDetail ? ViewDuplicateOption.WithDetailing : ViewDuplicateOption.Duplicate;

            using var tx = new Transaction(doc, "MCP: 뷰 복제");
            tx.Start();
            var newId  = view.Duplicate(mode);
            var newView = doc.GetElement(newId) as View;
            if (args["newName"] is JToken n && newView != null)
                newView.Name = n.ToString();
            tx.Commit();

            return TextContent($"뷰 복제 완료 (ID: {newId.IntegerValue}, 이름: {newView?.Name})");
        }
    }
}
