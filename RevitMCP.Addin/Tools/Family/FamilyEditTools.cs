using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Addin.Tools.Family
{
    // ── 패밀리 파라미터 목록 조회 ─────────────────────────────────
    public class GetFamilyParametersTool : ToolBase
    {
        public override string Name => "get_family_parameters";
        public override string Description => "패밀리의 모든 파라미터(인스턴스/타입) 목록을 반환합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "familyName" },
                ["properties"] = new JObject
                {
                    ["familyName"] = new JObject { ["type"] = "string", ["description"] = "조회할 패밀리 이름" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var familyName = args["familyName"]!.ToString();
            var family = GetFamily(doc, familyName);

            var famDoc = doc.EditFamily(family);
            var mgr    = famDoc.FamilyManager;

            var result = new JArray();
            foreach (FamilyParameter p in mgr.Parameters)
            {
                result.Add(new JObject
                {
                    ["name"]         = p.Definition.Name,
                    ["paramType"]    = p.Definition.ParameterType.ToString(),
                    ["group"]        = p.Definition.ParameterGroup.ToString(),
                    ["isInstance"]   = p.IsInstance,
                    ["isShared"]     = p.IsShared,
                    ["isReadOnly"]   = p.IsReadOnly,
                    ["formula"]      = p.IsDeterminedByFormula ? mgr.GetFormula(p) ?? "" : ""
                });
            }

            famDoc.Close(false);
            return TextContent($"파라미터 {result.Count}개\n{result}");
        }

        protected static Family GetFamily(Document doc, string name) =>
            new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .FirstOrDefault(f => f.Name == name)
            ?? throw new System.Exception($"패밀리 '{name}'을 찾을 수 없습니다.");
    }

    // ── 패밀리 파라미터 추가 ──────────────────────────────────────
    public class AddFamilyParameterTool : ToolBase
    {
        public override string Name => "add_family_parameter";
        public override string Description => "패밀리에 새 파라미터를 추가합니다. 인스턴스/타입 구분, 파라미터 그룹, 단위 유형을 지정할 수 있습니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "familyName", "paramName", "paramType" },
                ["properties"] = new JObject
                {
                    ["familyName"]  = new JObject { ["type"] = "string", ["description"] = "대상 패밀리 이름" },
                    ["paramName"]   = new JObject { ["type"] = "string", ["description"] = "추가할 파라미터 이름" },
                    ["paramType"]   = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "파라미터 유형",
                        ["enum"] = new JArray
                        {
                            "Length", "Area", "Volume", "Angle", "Number",
                            "Text", "Integer", "YesNo", "Material"
                        }
                    },
                    ["isInstance"]  = new JObject { ["type"] = "boolean", ["description"] = "true=인스턴스, false=타입 (기본 true)" },
                    ["group"]       = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "파라미터 그룹 (기본 PG_DATA)",
                        ["enum"] = new JArray
                        {
                            "PG_DATA", "PG_GEOMETRY", "PG_CONSTRAINTS",
                            "PG_CONSTRUCTION", "PG_MATERIALS", "PG_IDENTITY_DATA",
                            "PG_MECHANICAL", "PG_ELECTRICAL", "PG_STRUCTURAL"
                        }
                    },
                    ["defaultValue"] = new JObject { ["type"] = "string", ["description"] = "기본값 (선택)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var familyName = args["familyName"]!.ToString();
            var paramName  = args["paramName"]!.ToString();
            var paramTypeStr = args["paramType"]!.ToString();
            var isInstance = args["isInstance"]?.ToObject<bool>() ?? true;
            var groupStr   = args["group"]?.ToString() ?? "PG_DATA";

            // ParameterType 파싱
            if (!System.Enum.TryParse<ParameterType>(paramTypeStr, out var paramType))
                return ErrorContent($"지원하지 않는 파라미터 유형: {paramTypeStr}");

            // BuiltInParameterGroup 파싱
            if (!System.Enum.TryParse<BuiltInParameterGroup>(groupStr, out var group))
                group = BuiltInParameterGroup.PG_DATA;

            var family = GetFamily(doc, familyName);
            var famDoc = doc.EditFamily(family);
            var mgr    = famDoc.FamilyManager;

            // 이미 존재하는지 확인
            var existing = mgr.Parameters.Cast<FamilyParameter>()
                .FirstOrDefault(p => p.Definition.Name == paramName);
            if (existing != null)
            {
                famDoc.Close(false);
                return ErrorContent($"파라미터 '{paramName}'이 이미 존재합니다.");
            }

            FamilyParameter newParam;
            using (var tx = new Transaction(famDoc, "MCP: 패밀리 파라미터 추가"))
            {
                tx.Start();
                newParam = mgr.AddParameter(paramName, group, paramType, isInstance);

                // 기본값 설정
                if (args["defaultValue"] is JToken dv && newParam != null)
                {
                    try { SetDefaultValue(mgr, newParam, dv.ToString(), paramType); }
                    catch { /* 기본값 설정 실패는 무시 */ }
                }
                tx.Commit();
            }

            // 저장 후 프로젝트에 재로드
            var savePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"{familyName}.rfa");
            famDoc.SaveAs(savePath, new SaveAsOptions { OverwriteExistingFile = true });
            famDoc.Close(false);

            // 프로젝트에 다시 로드
            using (var tx = new Transaction(doc, "MCP: 패밀리 재로드"))
            {
                tx.Start();
                doc.LoadFamily(savePath, new FamilyLoadOptions(), out _);
                tx.Commit();
            }

            return TextContent(
                $"파라미터 추가 완료\n" +
                $"  패밀리: {familyName}\n" +
                $"  파라미터: {paramName}\n" +
                $"  유형: {paramTypeStr}\n" +
                $"  종류: {(isInstance ? "인스턴스" : "타입")}\n" +
                $"  그룹: {groupStr}");
        }

        private static void SetDefaultValue(FamilyManager mgr, FamilyParameter param, string value, ParameterType pType)
        {
            switch (pType)
            {
                case ParameterType.Length:
                case ParameterType.Area:
                case ParameterType.Volume:
                case ParameterType.Number:
                case ParameterType.Angle:
                    if (double.TryParse(value, out double d)) mgr.Set(param, d / 304.8);
                    break;
                case ParameterType.Integer:
                    if (int.TryParse(value, out int i)) mgr.Set(param, i);
                    break;
                case ParameterType.YesNo:
                    mgr.Set(param, value.ToLower() == "true" || value == "1" ? 1 : 0);
                    break;
                case ParameterType.Text:
                    mgr.Set(param, value);
                    break;
            }
        }

        private static Family GetFamily(Document doc, string name) =>
            new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .FirstOrDefault(f => f.Name == name)
            ?? throw new System.Exception($"패밀리 '{name}'을 찾을 수 없습니다.");
    }

    // ── 패밀리 파라미터 삭제 ──────────────────────────────────────
    public class RemoveFamilyParameterTool : ToolBase
    {
        public override string Name => "remove_family_parameter";
        public override string Description => "패밀리에서 파라미터를 삭제합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "familyName", "paramName" },
                ["properties"] = new JObject
                {
                    ["familyName"] = new JObject { ["type"] = "string" },
                    ["paramName"]  = new JObject { ["type"] = "string" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var familyName = args["familyName"]!.ToString();
            var paramName  = args["paramName"]!.ToString();

            var family = GetFamily(doc, familyName);
            var famDoc = doc.EditFamily(family);
            var mgr    = famDoc.FamilyManager;

            var param = mgr.Parameters.Cast<FamilyParameter>()
                .FirstOrDefault(p => p.Definition.Name == paramName)
                ?? throw new System.Exception($"파라미터 '{paramName}' 없음");

            using (var tx = new Transaction(famDoc, "MCP: 패밀리 파라미터 삭제"))
            {
                tx.Start();
                mgr.RemoveParameter(param);
                tx.Commit();
            }

            var savePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{familyName}.rfa");
            famDoc.SaveAs(savePath, new SaveAsOptions { OverwriteExistingFile = true });
            famDoc.Close(false);

            using (var tx = new Transaction(doc, "MCP: 패밀리 재로드"))
            {
                tx.Start();
                doc.LoadFamily(savePath, new FamilyLoadOptions(), out _);
                tx.Commit();
            }

            return TextContent($"파라미터 '{paramName}' 삭제 완료");
        }

        private static Family GetFamily(Document doc, string name) =>
            new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .FirstOrDefault(f => f.Name == name)
            ?? throw new System.Exception($"패밀리 '{name}'을 찾을 수 없습니다.");
    }

    // ── 패밀리 파라미터 수식 설정 ─────────────────────────────────
    public class SetFamilyParameterFormulaTool : ToolBase
    {
        public override string Name => "set_family_parameter_formula";
        public override string Description => "패밀리 파라미터에 수식을 설정합니다. (예: Width * 2)";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "familyName", "paramName", "formula" },
                ["properties"] = new JObject
                {
                    ["familyName"] = new JObject { ["type"] = "string" },
                    ["paramName"]  = new JObject { ["type"] = "string" },
                    ["formula"]    = new JObject { ["type"] = "string", ["description"] = "Revit 수식 (예: Width * 2, if(Height > 3000mm, 200mm, 100mm))" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var familyName = args["familyName"]!.ToString();
            var paramName  = args["paramName"]!.ToString();
            var formula    = args["formula"]!.ToString();

            var family = GetFamily(doc, familyName);
            var famDoc = doc.EditFamily(family);
            var mgr    = famDoc.FamilyManager;

            var param = mgr.Parameters.Cast<FamilyParameter>()
                .FirstOrDefault(p => p.Definition.Name == paramName)
                ?? throw new System.Exception($"파라미터 '{paramName}' 없음");

            using (var tx = new Transaction(famDoc, "MCP: 수식 설정"))
            {
                tx.Start();
                mgr.SetFormula(param, formula);
                tx.Commit();
            }

            var savePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{familyName}.rfa");
            famDoc.SaveAs(savePath, new SaveAsOptions { OverwriteExistingFile = true });
            famDoc.Close(false);

            using (var tx = new Transaction(doc, "MCP: 패밀리 재로드"))
            {
                tx.Start();
                doc.LoadFamily(savePath, new FamilyLoadOptions(), out _);
                tx.Commit();
            }

            return TextContent($"수식 설정 완료\n  파라미터: {paramName}\n  수식: {formula}");
        }

        private static Family GetFamily(Document doc, string name) =>
            new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .FirstOrDefault(f => f.Name == name)
            ?? throw new System.Exception($"패밀리 '{name}'을 찾을 수 없습니다.");
    }

    // ── 패밀리 타입 추가 ──────────────────────────────────────────
    public class AddFamilyTypeTool : ToolBase
    {
        public override string Name => "add_family_type";
        public override string Description => "패밀리에 새 타입을 추가하고 파라미터 값을 설정합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "familyName", "typeName" },
                ["properties"] = new JObject
                {
                    ["familyName"]  = new JObject { ["type"] = "string" },
                    ["typeName"]    = new JObject { ["type"] = "string", ["description"] = "새 타입 이름" },
                    ["parameters"]  = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "파라미터명: 값 딕셔너리",
                        ["additionalProperties"] = new JObject { ["type"] = "string" }
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var familyName = args["familyName"]!.ToString();
            var typeName   = args["typeName"]!.ToString();
            var parameters = args["parameters"]?.ToObject<Dictionary<string, string>>()
                             ?? new Dictionary<string, string>();

            var family = GetFamily(doc, familyName);
            var famDoc = doc.EditFamily(family);
            var mgr    = famDoc.FamilyManager;

            using (var tx = new Transaction(famDoc, "MCP: 패밀리 타입 추가"))
            {
                tx.Start();
                mgr.NewType(typeName);

                foreach (var (pName, pVal) in parameters)
                {
                    var param = mgr.Parameters.Cast<FamilyParameter>()
                        .FirstOrDefault(p => p.Definition.Name == pName);
                    if (param == null || param.IsDeterminedByFormula) continue;
                    try
                    {
                        switch (param.StorageType)
                        {
                            case StorageType.Double:
                                if (double.TryParse(pVal, out double d))
                                    mgr.Set(param, d / 304.8);
                                break;
                            case StorageType.Integer:
                                if (int.TryParse(pVal, out int i)) mgr.Set(param, i);
                                break;
                            case StorageType.String:
                                mgr.Set(param, pVal);
                                break;
                        }
                    }
                    catch { }
                }
                tx.Commit();
            }

            var savePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{familyName}.rfa");
            famDoc.SaveAs(savePath, new SaveAsOptions { OverwriteExistingFile = true });
            famDoc.Close(false);

            using (var tx = new Transaction(doc, "MCP: 패밀리 재로드"))
            {
                tx.Start();
                doc.LoadFamily(savePath, new FamilyLoadOptions(), out _);
                tx.Commit();
            }

            return TextContent(
                $"타입 '{typeName}' 추가 완료\n" +
                $"  설정된 파라미터: {parameters.Count}개");
        }

        private static Family GetFamily(Document doc, string name) =>
            new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .FirstOrDefault(f => f.Name == name)
            ?? throw new System.Exception($"패밀리 '{name}'을 찾을 수 없습니다.");
    }

    // FamilyLoadOptions 구현
    internal class FamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }
        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
            out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
}
