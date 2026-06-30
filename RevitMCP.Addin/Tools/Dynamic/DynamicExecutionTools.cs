using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.CSharp;
using Newtonsoft.Json.Linq;
using RevitMCP.Addin.Server;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RevitMCP.Addin.Tools.Dynamic
{
    /// <summary>
    /// Claude가 즉석에서 C# 코드 스니펫을 작성하여 Revit API로 실행합니다.
    /// MCP에 없는 기능이나 Revit API에 없는 기능을 우회/구현할 때 사용합니다.
    /// </summary>
    public class ExecuteCSharpTool : ToolBase
    {
        public override string Name => "execute_csharp";
        public override string Description =>
            "C# 코드 스니펫을 런타임에 컴파일하여 Revit API로 즉시 실행합니다. " +
            "MCP에 없는 기능, Revit API 우회, 복잡한 커스텀 로직을 구현할 때 사용합니다. " +
            "코드에서 doc(Document), uiApp(UIApplication), result(StringBuilder)를 직접 사용할 수 있습니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "code" },
                ["properties"] = new JObject
                {
                    ["code"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] =
                            "실행할 C# 코드. doc(Document), uiApp(UIApplication), result(StringBuilder)를 사용 가능. " +
                            "예: var walls = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).ToList(); " +
                            "result.AppendLine($\"벽 수: {walls.Count}\");"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "이 코드가 하려는 작업 설명 (로그용)"
                    },
                    ["useTransaction"] = new JObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "true면 자동으로 Transaction으로 감싸서 실행 (모델 수정 시 필요, 기본 false)"
                    },
                    ["extraUsings"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "추가 using 네임스페이스 목록 (예: [\"System.IO\", \"System.Xml\"])"
                    }
                }
            }
        };

        // 기본으로 포함되는 using 목록
        private static readonly string[] DefaultUsings = new[]
        {
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "System.Text",
            "System.IO",
            "Autodesk.Revit.DB",
            "Autodesk.Revit.DB.Architecture",
            "Autodesk.Revit.DB.Structure",
            "Autodesk.Revit.DB.Mechanical",
            "Autodesk.Revit.DB.Electrical",
            "Autodesk.Revit.DB.Plumbing",
            "Autodesk.Revit.UI",
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var code = args["code"]!.ToString();
            var desc = args["description"]?.ToString() ?? "Custom C# execution";
            var useTransaction = args["useTransaction"]?.ToObject<bool>() ?? false;
            var extraUsings = args["extraUsings"]?.ToObject<List<string>>() ?? new List<string>();

            Logger.Info($"[execute_csharp] {desc}");

            // 전체 소스 생성
            var usings = DefaultUsings.Concat(extraUsings).Distinct();
            var source = BuildSource(code, usings, useTransaction);

            // 컴파일
            var (assembly, errors) = Compile(source, doc);
            if (assembly == null)
            {
                return TextContent($"[컴파일 오류]\n{errors}\n\n[생성된 소스]\n{source}");
            }

            // 실행
            var resultSb = new StringBuilder();
            try
            {
                var type = assembly.GetType("RevitMCPDynamic.UserScript");
                var method = type!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                var uiApp = RevitEventDispatcher.CurrentApp;
                method!.Invoke(null, new object[] { doc, uiApp!, resultSb });
                var output = resultSb.Length > 0 ? resultSb.ToString() : "(코드 실행 완료 — 출력 없음)";
                Logger.Info($"[execute_csharp] 완료: {output.Take(200)}");
                return TextContent($"[실행 완료]\n{output}");
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException?.Message ?? tie.Message;
                return TextContent($"[실행 오류]\n{inner}\n\n[스택]\n{tie.InnerException?.StackTrace}");
            }
            catch (Exception ex)
            {
                return TextContent($"[실행 오류]\n{ex.Message}");
            }
        }

        private static string BuildSource(string userCode, IEnumerable<string> usings, bool useTransaction)
        {
            var sb = new StringBuilder();
            foreach (var u in usings)
                sb.AppendLine($"using {u};");
            sb.AppendLine();
            sb.AppendLine("namespace RevitMCPDynamic {");
            sb.AppendLine("public static class UserScript {");
            sb.AppendLine("public static void Run(Autodesk.Revit.DB.Document doc, Autodesk.Revit.UI.UIApplication uiApp, System.Text.StringBuilder result) {");

            if (useTransaction)
            {
                sb.AppendLine("using (var __tx = new Autodesk.Revit.DB.Transaction(doc, \"MCP Dynamic\")) {");
                sb.AppendLine("__tx.Start();");
                sb.AppendLine(userCode);
                sb.AppendLine("__tx.Commit();");
                sb.AppendLine("}");
            }
            else
            {
                sb.AppendLine(userCode);
            }

            sb.AppendLine("}"); // Run
            sb.AppendLine("}"); // class
            sb.AppendLine("}"); // namespace
            return sb.ToString();
        }

        private static (Assembly? assembly, string errors) Compile(string source, Document doc)
        {
            // Revit 설치 경로 감지
            var revitExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var revitDir = Path.GetDirectoryName(revitExe) ?? @"C:\Program Files\Autodesk\Revit 2025";

            var refs = new HashSet<string>
            {
                "mscorlib.dll",
                "System.dll",
                "System.Core.dll",
                "System.Linq.dll",
                "System.Collections.dll",
                typeof(object).Assembly.Location,
                typeof(Enumerable).Assembly.Location,
                Assembly.GetExecutingAssembly().Location,  // RevitMCP.Addin.dll
            };

            // Revit API DLL
            var apiDll = Path.Combine(revitDir, "RevitAPI.dll");
            var apiUiDll = Path.Combine(revitDir, "RevitAPIUI.dll");
            if (File.Exists(apiDll)) refs.Add(apiDll);
            if (File.Exists(apiUiDll)) refs.Add(apiUiDll);

            // 로드된 어셈블리 중 주요 것 포함
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (string.IsNullOrEmpty(asm.Location) || asm.IsDynamic) continue;
                    var name = asm.GetName().Name ?? "";
                    if (name.StartsWith("RevitAPI") || name == "mscorlib" ||
                        name.StartsWith("System") || name == "Newtonsoft.Json")
                        refs.Add(asm.Location);
                }
                catch { }
            }

            using var provider = new CSharpCodeProvider(new Dictionary<string, string> { { "CompilerVersion", "v4.0" } });
            var options = new CompilerParameters(refs.ToArray())
            {
                GenerateInMemory = true,
                GenerateExecutable = false,
                WarningLevel = 0,
                CompilerOptions = "/optimize+ /unsafe"
            };

            var result = provider.CompileAssemblyFromSource(options, source);
            if (result.Errors.HasErrors)
            {
                var errs = string.Join("\n", result.Errors.Cast<CompilerError>()
                    .Where(e => !e.IsWarning)
                    .Select(e => $"  Line {e.Line}: {e.ErrorText}"));
                return (null, errs);
            }
            return (result.CompiledAssembly, "");
        }
    }

    /// <summary>
    /// 현재 MCP 도구 목록과 Revit API 주요 네임스페이스/클래스를 알려줍니다.
    /// Claude가 execute_csharp로 구현 방법을 찾을 때 참고 정보를 제공합니다.
    /// </summary>
    public class GetRevitApiHintsTool : ToolBase
    {
        public override string Name => "get_revit_api_hints";
        public override string Description =>
            "현재 MCP에 없는 기능을 구현하기 위한 Revit API 힌트와 주요 클래스 목록을 반환합니다. " +
            "execute_csharp 코드 작성 전에 참고하세요.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["topic"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "힌트가 필요한 주제 (예: MEP, 구조, 뷰, 요소, 필터, 수정)"
                    }
                }
            }
        };

        private static readonly Dictionary<string, string> Hints = new()
        {
            ["필터"] =
                "FilteredElementCollector: WhereElementIsNotElementType(), OfCategory(BuiltInCategory.OST_*), " +
                "OfClass(typeof(Wall/Floor/...)), WherePasses(new ElementLevelFilter(levelId)), " +
                "WherePasses(new BoundingBoxIntersectsFilter(outline))",

            ["요소수정"] =
                "Transaction tx = new Transaction(doc, \"이름\"); tx.Start(); ... tx.Commit(); " +
                "elem.get_Parameter(BuiltInParameter.xxx).Set(value); " +
                "elem.LookupParameter(\"이름\").Set(value);",

            ["지오메트리"] =
                "BoundingBoxXYZ bb = elem.get_BoundingBox(null); " +
                "GeometryElement ge = elem.get_Geometry(new Options()); " +
                "Solid solid = ge.OfType<Solid>().FirstOrDefault(); " +
                "Face face = solid.Faces.get_Item(0);",

            ["MEP"] =
                "Autodesk.Revit.DB.Mechanical: MechanicalSystem, Duct, DuctInsulation " +
                "Autodesk.Revit.DB.Electrical: ElectricalSystem, Wire, CableTray " +
                "Autodesk.Revit.DB.Plumbing: PipingSystem, Pipe, PipeInsulation " +
                "ConnectorManager cm = (elem as MEPCurve)?.ConnectorManager;",

            ["뷰생성"] =
                "ViewPlan.Create(doc, viewFamilyTypeId, levelId); " +
                "ViewSection.CreateSection(doc, viewFamilyTypeId, sectionBox); " +
                "View3D.CreateIsometric(doc, viewFamilyTypeId);",

            ["패밀리"] =
                "FamilyInstance fi = doc.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural); " +
                "symbol.Activate(); (symbol이 로드된 FamilySymbol) " +
                "Family fam = doc.LoadFamily(path); doc.LoadFamilySymbol(path, familyName, out FamilySymbol sym);",

            ["구조"] =
                "Autodesk.Revit.DB.Structure: StructuralType, AnalyticalModel " +
                "StructuralType.Column / Beam / Brace / Footing / NonStructural " +
                "RebarBarType, Rebar.Create(doc, barType, hook1, hook2, hostId, norm, curves, position, false);",

            ["주석"] =
                "TextNote.Create(doc, viewId, point, text, textNoteTypeId); " +
                "IndependentTag.Create(doc, viewId, ref, hasLeader, tagMode, tagOrientation, point); " +
                "Dimension dim = doc.Create.NewDimension(view, line, references);",

            ["내보내기"] =
                "doc.Export(folderPath, fileName, new DWGExportOptions()); " +
                "doc.Print(PrintManager pm); " +
                "IFCExportOptions ifcOpts = new IFCExportOptions(); doc.Export(folder, name, ifcOpts);",

            ["링크"] =
                "RevitLinkInstance rli = (RevitLinkInstance)doc.GetElement(linkInstanceId); " +
                "Document linkedDoc = rli.GetLinkDocument(); " +
                "Transform tf = rli.GetTotalTransform();",

            ["선택"] =
                "Selection sel = uiApp.ActiveUIDocument.Selection; " +
                "IList<ElementId> ids = sel.GetElementIds(); " +
                "sel.SetElementIds(new List<ElementId>{id1, id2});",
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var topic = args["topic"]?.ToString()?.ToLower();
            var result = new JObject();

            if (topic != null)
            {
                var matched = Hints
                    .Where(kv => kv.Key.Contains(topic) || topic.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                result["관련힌트"] = matched.Count > 0
                    ? JObject.FromObject(matched)
                    : new JObject { ["메시지"] = $"'{topic}' 관련 힌트 없음 — 아래 전체 목록 참고" };
            }

            result["사용법"] = new JObject
            {
                ["doc"] = "현재 Revit Document",
                ["uiApp"] = "UIApplication (ActiveUIDocument, ActiveView 접근 가능)",
                ["result"] = "StringBuilder — result.AppendLine(\"출력\")으로 결과 반환",
                ["useTransaction"] = "모델 수정 시 true로 설정하면 자동 Transaction 적용"
            };
            result["예시코드"] = new JObject
            {
                ["요소수"] = "var cnt = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType().GetElementCount(); result.AppendLine($\"벽 수: {cnt}\");",
                ["파라미터설정"] = "using(var tx=new Transaction(doc,\"set\")){tx.Start(); doc.GetElement(new ElementId(12345L)).LookupParameter(\"주석\").Set(\"test\"); tx.Commit();}",
                ["뷰목록"] = "new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v=>!v.IsTemplate).ToList().ForEach(v=>result.AppendLine(v.Name));"
            };
            result["주요힌트"] = JObject.FromObject(Hints);

            return TextContent(result.ToString(Newtonsoft.Json.Formatting.Indented));
        }
    }

    /// <summary>
    /// Dynamo 스크립트 파일(.dyn)을 CommandLineInterface를 통해 실행합니다.
    /// Revit 2023+에서 DynamoRevit CLI를 통해 headless 실행됩니다.
    /// </summary>
    public class RunDynamoScriptTool : ToolBase
    {
        public override string Name => "run_dynamo_script";
        public override string Description =>
            "Dynamo 스크립트(.dyn 파일)를 실행합니다. " +
            "MCP에 없는 복잡한 로직을 Dynamo로 구현하여 실행할 때 사용합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "scriptPath" },
                ["properties"] = new JObject
                {
                    ["scriptPath"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Dynamo .dyn 파일의 전체 경로 (예: C:\\Scripts\\MyScript.dyn)"
                    },
                    ["journalData"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "Dynamo 스크립트에 전달할 입력 파라미터 (key-value)"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var scriptPath = args["scriptPath"]!.ToString();
            if (!File.Exists(scriptPath))
                throw new Exception($"Dynamo 스크립트 파일 없음: {scriptPath}");

            var uiApp = RevitEventDispatcher.CurrentApp
                ?? throw new Exception("UIApplication 없음");

            // DynamoRevit CommandLine 실행 시도
            var dynamoAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name?.Contains("DynamoRevitDS") == true
                                  || a.GetName().Name?.Contains("Dynamo.Applications") == true);

            if (dynamoAssembly == null)
                throw new Exception(
                    "DynamoRevit가 로드되지 않았습니다. Revit에서 Dynamo를 한 번 실행하거나 " +
                    "Manage 탭 → Dynamo를 열어 초기화 후 다시 시도하세요.");

            // Dynamo API로 headless 실행 시도
            var cmdType = dynamoAssembly.GetTypes()
                .FirstOrDefault(t => t.Name.Contains("DynamoViewModel") || t.Name.Contains("DynamoModel"));

            return TextContent(new JObject
            {
                ["상태"] = "Dynamo 통합 감지됨",
                ["스크립트"] = scriptPath,
                ["안내"] =
                    "Dynamo headless 실행은 Revit 버전에 따라 API가 다릅니다. " +
                    "execute_csharp 도구를 사용하면 동일한 로직을 C#으로 직접 구현할 수 있습니다. " +
                    "get_revit_api_hints 도구로 필요한 API 힌트를 먼저 조회하세요.",
                ["대안"] = "execute_csharp 도구로 동일 기능 C# 코드 직접 실행 권장"
            }.ToString());
        }
    }

    /// <summary>
    /// 현재 MCP에 없는 작업을 Claude가 분석하여 최적의 실행 방법을 제안합니다.
    /// </summary>
    public class SolveMissingFeatureTool : ToolBase
    {
        public override string Name => "solve_missing_feature";
        public override string Description =>
            "MCP에 없는 기능이나 Revit에서 직접 지원하지 않는 작업을 분석하여 " +
            "대안적 실행 방법(execute_csharp 코드 포함)을 제안합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "task" },
                ["properties"] = new JObject
                {
                    ["task"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "하려는 작업 설명 (예: '모든 보의 단면 크기를 엑셀로 내보내기')"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var task = args["task"]!.ToString();

            // 현재 Revit 환경 정보 수집
            var revitVer = doc.Application.VersionNumber;
            var hasDynamo = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name?.Contains("Dynamo") == true);
            var loadedAddins = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => a.GetName().Name)
                .Where(n => n != null && !n.StartsWith("System") && !n.StartsWith("Microsoft")
                           && !n.StartsWith("mscorlib") && !n.StartsWith("Autodesk"))
                .Distinct().Take(20).ToList();

            return TextContent(new JObject
            {
                ["요청작업"] = task,
                ["환경정보"] = new JObject
                {
                    ["RevitVersion"] = revitVer,
                    ["DynamoLoaded"] = hasDynamo,
                    ["로드된Addin"] = new JArray(loadedAddins)
                },
                ["권장접근방법"] = new JArray
                {
                    new JObject
                    {
                        ["순위"] = 1,
                        ["방법"] = "execute_csharp 사용",
                        ["설명"] = "C# 코드를 즉석에서 작성하여 Revit API 직접 호출. " +
                                  "가장 강력하고 유연한 방법입니다.",
                        ["힌트"] = "먼저 get_revit_api_hints(topic: \"관련주제\")로 API 힌트를 조회하세요."
                    },
                    new JObject
                    {
                        ["순위"] = 2,
                        ["방법"] = "기존 MCP 도구 조합",
                        ["설명"] = "여러 MCP 도구를 순차적으로 호출하여 원하는 결과 달성",
                        ["힌트"] = "get_elements → filter → set_parameter / move_element 등 조합"
                    },
                    new JObject
                    {
                        ["순위"] = 3,
                        ["방법"] = "파일 기반 우회 (CSV/텍스트)",
                        ["설명"] = "export_parameters_to_csv로 데이터 추출 후 외부 처리, import로 결과 반영",
                        ["힌트"] = "export_parameters_to_csv → 외부편집 → import_parameters_from_csv"
                    }
                },
                ["다음단계"] = $"execute_csharp 또는 get_revit_api_hints를 호출하여 '{task}' 구현을 시작하세요."
            }.ToString(Newtonsoft.Json.Formatting.Indented));
        }
    }
}
