using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;
using RevitMCP.Addin.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RevitMCP.Addin.Tools.Dynamic
{
    // ═══════════════════════════════════════════════════════════════
    //  공유 컴파일러
    // ═══════════════════════════════════════════════════════════════
    internal static class CSharpCompiler
    {
        internal static readonly string[] DefaultUsings = new[]
        {
            "System", "System.Collections.Generic", "System.Linq", "System.Text",
            "System.IO", "System.Math",
            "Autodesk.Revit.DB", "Autodesk.Revit.DB.Architecture",
            "Autodesk.Revit.DB.Structure", "Autodesk.Revit.DB.Mechanical",
            "Autodesk.Revit.DB.Electrical", "Autodesk.Revit.DB.Plumbing",
            "Autodesk.Revit.UI",
        };

        internal static string BuildSource(string userCode, IEnumerable<string> usings, bool useTransaction)
        {
            var sb = new StringBuilder();
            foreach (var u in usings.Distinct()) sb.AppendLine($"using {u};");
            sb.AppendLine();
            sb.AppendLine("namespace RevitMCPDynamic {");
            sb.AppendLine("public static class UserScript {");
            sb.AppendLine("public static void Run(Autodesk.Revit.DB.Document doc, Autodesk.Revit.UI.UIApplication uiApp, System.Text.StringBuilder result) {");
            if (useTransaction)
            {
                sb.AppendLine("using(var __tx=new Autodesk.Revit.DB.Transaction(doc,\"MCP Dynamic\")){");
                sb.AppendLine("__tx.Start();");
                sb.AppendLine(userCode);
                sb.AppendLine("__tx.Commit(); }");
            }
            else sb.AppendLine(userCode);
            sb.AppendLine("}}}" );
            return sb.ToString();
        }

        internal static (Assembly? asm, string errors) Compile(string source)
        {
            var revitDir = Path.GetDirectoryName(
                System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "")
                ?? @"C:\Program Files\Autodesk\Revit 2025";

            // MetadataReference 수집
            var refs = new HashSet<string>();
            void TryAdd(string p) { if (File.Exists(p)) refs.Add(p); }

            TryAdd(typeof(object).Assembly.Location);                  // mscorlib
            TryAdd(typeof(Enumerable).Assembly.Location);              // System.Core
            TryAdd(Assembly.GetExecutingAssembly().Location);          // RevitMCP.Addin
            TryAdd(Path.Combine(revitDir, "RevitAPI.dll"));
            TryAdd(Path.Combine(revitDir, "RevitAPIUI.dll"));

            // .NET Framework 기본 어셈블리
            var frameworkDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            foreach (var dll in new[] { "mscorlib.dll", "System.dll", "System.Core.dll",
                                        "System.Xml.dll", "System.IO.dll" })
                TryAdd(Path.Combine(frameworkDir, dll));

            // 현재 프로세스에 로드된 어셈블리 중 주요 것 포함
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (string.IsNullOrEmpty(a.Location) || a.IsDynamic) continue;
                    var n = a.GetName().Name ?? "";
                    if (n.StartsWith("RevitAPI") || n.StartsWith("System") ||
                        n == "mscorlib" || n == "Newtonsoft.Json" ||
                        n.StartsWith("Microsoft.CodeAnalysis"))
                        refs.Add(a.Location);
                }
                catch { }
            }

            var metaRefs = refs
                .Select(r => MetadataReference.CreateFromFile(r) as MetadataReference)
                .ToList();

            // Roslyn 컴파일
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                assemblyName: "RevitMCPDynamic_" + Guid.NewGuid().ToString("N"),
                syntaxTrees: new[] { syntaxTree },
                references: metaRefs,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: false));

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            if (!emitResult.Success)
            {
                var headerLines = DefaultUsings.Length + 4; // using 수 + namespace/class/method 선언
                var errs = string.Join("\n", emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d =>
                    {
                        var line = d.Location.GetLineSpan().StartLinePosition.Line + 1 - headerLines;
                        return $"  Line {line}: {d.GetMessage()}";
                    }));
                return (null, errs);
            }

            ms.Seek(0, SeekOrigin.Begin);
            var asm = Assembly.Load(ms.ToArray());
            return (asm, "");
        }

        internal static string RunAssembly(Assembly asm, Document doc, UIApplication uiApp)
        {
            var sb = new StringBuilder();
            var type = asm.GetType("RevitMCPDynamic.UserScript")!;
            var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            method.Invoke(null, new object[] { doc, uiApp, sb });
            return sb.Length > 0 ? sb.ToString() : "(실행 완료 — 출력 없음)";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  1. execute_csharp  — C# 코드 런타임 실행
    // ═══════════════════════════════════════════════════════════════
    public class ExecuteCSharpTool : ToolBase
    {
        public override string Name => "execute_csharp";
        public override string Description =>
            "C# 코드를 런타임에 컴파일하여 Revit API로 즉시 실행합니다. " +
            "MCP에 없는 기능, 커스텀 로직, 우회 구현에 사용합니다. " +
            "코드에서 doc(Document), uiApp(UIApplication), result(StringBuilder) 사용 가능.";

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
                            "실행할 C# 코드 본문. doc/uiApp/result 변수 사용 가능.\n" +
                            "예) var walls = new FilteredElementCollector(doc)\n" +
                            "      .OfCategory(BuiltInCategory.OST_Walls)\n" +
                            "      .WhereElementIsNotElementType().ToList();\n" +
                            "    result.AppendLine($\"벽 수: {walls.Count}\");"
                    },
                    ["useTransaction"] = new JObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "모델 수정 시 true (자동 Transaction 래핑, 기본 false)"
                    },
                    ["extraUsings"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "추가 using 네임스페이스 (예: [\"System.Xml\"])"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "코드 설명 (로그용)"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var code = args["code"]!.ToString();
            var desc = args["description"]?.ToString() ?? "execute_csharp";
            var useTx = args["useTransaction"]?.ToObject<bool>() ?? false;
            var extra = args["extraUsings"]?.ToObject<List<string>>() ?? new List<string>();
            var uiApp = RevitEventDispatcher.CurrentApp
                ?? throw new Exception("UIApplication 없음");

            Logger.Info($"[execute_csharp] {desc}");

            var usings = CSharpCompiler.DefaultUsings.Concat(extra);
            var source = CSharpCompiler.BuildSource(code, usings, useTx);
            var (asm, errors) = CSharpCompiler.Compile(source);

            if (asm == null)
                return TextContent(
                    $"[컴파일 오류]\n{errors}\n\n" +
                    $"[힌트] get_revit_api_hints 도구로 올바른 API 사용법을 확인하세요.\n" +
                    $"[코드]\n{code}");

            try
            {
                var output = CSharpCompiler.RunAssembly(asm, doc, uiApp);
                Logger.Info($"[execute_csharp] 완료");
                return TextContent($"[실행 완료]\n{output}");
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException?.Message ?? tie.Message;
                var stack = tie.InnerException?.StackTrace ?? "";
                return TextContent($"[런타임 오류]\n{inner}\n\n[스택]\n{stack}");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  2. get_revit_api_hints  — API 힌트 조회
    // ═══════════════════════════════════════════════════════════════
    public class GetRevitApiHintsTool : ToolBase
    {
        public override string Name => "get_revit_api_hints";
        public override string Description =>
            "Revit API 주요 클래스·패턴 힌트를 반환합니다. " +
            "execute_csharp 코드 작성 전 필요한 API를 확인할 때 사용합니다.";

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
                        ["description"] = "찾고 싶은 주제 (예: MEP, 구조, 필터, 수정, 뷰, 패밀리, 선택, 내보내기)"
                    }
                }
            }
        };

        private static readonly Dictionary<string, string> Hints = new()
        {
            ["필터/수집"] =
                "new FilteredElementCollector(doc)\n" +
                "  .WhereElementIsNotElementType()\n" +
                "  .OfCategory(BuiltInCategory.OST_Walls)   // 카테고리\n" +
                "  .OfClass(typeof(Wall))                   // 클래스\n" +
                "  .WherePasses(new ElementLevelFilter(levelId)) // 레벨\n" +
                "  .WherePasses(new BoundingBoxIntersectsFilter(outline)) // 영역\n" +
                "  .ToList();",

            ["파라미터 읽기/쓰기"] =
                "// 읽기\n" +
                "Parameter p = elem.LookupParameter(\"파라미터이름\");\n" +
                "// 또는: elem.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)\n" +
                "string val = p.AsString();\n" +
                "double mm  = p.AsDouble() * 304.8;\n" +
                "\n// 쓰기 (Transaction 필요)\n" +
                "p.Set(\"새값\");          // String\n" +
                "p.Set(1000.0 / 304.8); // Double (피트 단위)\n" +
                "p.Set(1);              // Integer",

            ["Transaction/수정"] =
                "using (var tx = new Transaction(doc, \"작업명\")) {\n" +
                "  tx.Start();\n" +
                "  // ... 수정 코드 ...\n" +
                "  tx.Commit();\n" +
                "}",

            ["지오메트리"] =
                "BoundingBoxXYZ bb = elem.get_BoundingBox(null);\n" +
                "GeometryElement ge = elem.get_Geometry(new Options());\n" +
                "foreach (GeometryObject obj in ge) {\n" +
                "  if (obj is Solid solid && solid.Volume > 0) {\n" +
                "    result.AppendLine($\"Volume: {solid.Volume * 3.28084*3.28084*3.28084:F3} m³\");\n" +
                "  }\n" +
                "}",

            ["MEP"] =
                "// 덕트/파이프/케이블트레이\n" +
                "var ducts = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Mechanical.Duct));\n" +
                "var pipes = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Plumbing.Pipe));\n" +
                "// 커넥터\n" +
                "var cm = (elem as MEPCurve)?.ConnectorManager;\n" +
                "foreach (Connector c in cm.Connectors) { /* ... */ }",

            ["구조"] =
                "// 구조 기둥/보/기초\n" +
                "var cols = new FilteredElementCollector(doc)\n" +
                "  .OfCategory(BuiltInCategory.OST_StructuralColumns)\n" +
                "  .WhereElementIsNotElementType().Cast<FamilyInstance>();\n" +
                "// 철근\n" +
                "// Autodesk.Revit.DB.Structure.Rebar.Create(doc, barType, ...)",

            ["뷰 생성"] =
                "// 평면도\n" +
                "ViewFamilyType vft = new FilteredElementCollector(doc)\n" +
                "  .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()\n" +
                "  .First(v => v.ViewFamily == ViewFamily.FloorPlan);\n" +
                "ViewPlan vp = ViewPlan.Create(doc, vft.Id, level.Id);\n" +
                "// 3D\n" +
                "View3D v3d = View3D.CreateIsometric(doc, vft3d.Id);",

            ["패밀리/인스턴스"] =
                "// 배치\n" +
                "FamilySymbol sym = doc.GetElement(symbolId) as FamilySymbol;\n" +
                "if (!sym.IsActive) sym.Activate();\n" +
                "FamilyInstance fi = doc.Create.NewFamilyInstance(\n" +
                "  new XYZ(x/304.8, y/304.8, z/304.8), sym, level,\n" +
                "  Autodesk.Revit.DB.Structure.StructuralType.NonStructural);\n" +
                "// 로드\n" +
                "doc.LoadFamilySymbol(@\"C:\\path\\to.rfa\", \"TypeName\", out FamilySymbol loaded);",

            ["선택"] =
                "Selection sel = uiApp.ActiveUIDocument.Selection;\n" +
                "var ids = sel.GetElementIds();          // 현재 선택\n" +
                "sel.SetElementIds(new List<ElementId>{id1, id2}); // 선택 설정",

            ["파일 내보내기/저장"] =
                "// CSV 저장\n" +
                "File.WriteAllLines(@\"C:\\output.csv\", rows, System.Text.Encoding.UTF8);\n" +
                "// JSON 저장\n" +
                "File.WriteAllText(@\"C:\\output.json\", json);\n" +
                "// DWG 내보내기\n" +
                "doc.Export(@\"C:\\folder\", \"filename\", new DWGExportOptions());",

            ["링크 모델"] =
                "var links = new FilteredElementCollector(doc)\n" +
                "  .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>();\n" +
                "foreach (var link in links) {\n" +
                "  Document linked = link.GetLinkDocument();\n" +
                "  Transform tf = link.GetTotalTransform();\n" +
                "  // linked 문서에서 요소 수집 가능\n" +
                "}",

            ["Join Geometry"] =
                "// 결합\n" +
                "JoinGeometryUtils.JoinGeometry(doc, elem1, elem2);\n" +
                "// 결합 확인\n" +
                "bool isJoined = JoinGeometryUtils.AreElementsJoined(doc, elem1, elem2);\n" +
                "// 해제\n" +
                "JoinGeometryUtils.UnjoinGeometry(doc, elem1, elem2);\n" +
                "// 우선순위 반전\n" +
                "JoinGeometryUtils.SwitchJoinOrder(doc, elem1, elem2);",

            ["단위 변환"] =
                "// Revit 내부 단위 = 피트\n" +
                "double mm_to_ft = 1.0 / 304.8;\n" +
                "double ft_to_mm = 304.8;\n" +
                "// XYZ 생성 (mm 입력)\n" +
                "var pt = new XYZ(x_mm / 304.8, y_mm / 304.8, z_mm / 304.8);",
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var topic = args["topic"]?.ToString()?.ToLower();
            var result = new JObject();

            if (topic != null)
            {
                var matched = Hints.Where(kv =>
                    kv.Key.ToLower().Contains(topic) || topic.Contains(kv.Key.ToLower()))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                result["매칭힌트"] = matched.Count > 0
                    ? JObject.FromObject(matched)
                    : new JObject { ["안내"] = $"'{topic}' 힌트 없음. 전체 목록 참고." };
            }

            result["execute_csharp_변수"] = new JObject
            {
                ["doc"] = "Document — 현재 열린 Revit 문서",
                ["uiApp"] = "UIApplication — ActiveUIDocument, ActiveView 접근",
                ["result"] = "StringBuilder — result.AppendLine(\"출력\")으로 결과 반환",
                ["useTransaction"] = "true → 모델 수정 가능 (Transaction 자동 적용)"
            };
            result["단위"] = "Revit 내부 단위 = 피트. mm 입력 시 /304.8, 출력 시 *304.8";
            result["전체힌트목록"] = new JArray(Hints.Keys);
            result["힌트"] = JObject.FromObject(Hints);

            return TextContent(result.ToString(Newtonsoft.Json.Formatting.Indented));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  3. solve_missing_feature  — 미구현 기능 자동 분석 + 코드 생성
    // ═══════════════════════════════════════════════════════════════
    public class SolveMissingFeatureTool : ToolBase
    {
        public override string Name => "solve_missing_feature";
        public override string Description =>
            "MCP에 없는 기능을 분석하고, execute_csharp에 바로 붙여넣을 수 있는 C# 코드 초안을 생성합니다. " +
            "task에 원하는 작업을 자연어로 설명하세요.";

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
                        ["description"] = "구현하려는 작업 (예: '모든 보의 단면 크기를 CSV로 저장')"
                    }
                }
            }
        };

        // 키워드별 코드 템플릿
        private static readonly List<(string[] keywords, string category, string template, bool needsTx)> Templates =
            new()
            {
                (new[]{"csv","엑셀","excel","내보내기","export"},
                 "파일 내보내기",
                 "var rows = new List<string> { \"ID,이름,카테고리,값\" };\n" +
                 "var elems = new FilteredElementCollector(doc)\n" +
                 "  .WhereElementIsNotElementType()\n" +
                 "  .OfCategory(BuiltInCategory.OST_Walls) // ← 카테고리 변경\n" +
                 "  .ToList();\n" +
                 "foreach (var e in elems) {\n" +
                 "  var p = e.LookupParameter(\"파라미터이름\"); // ← 파라미터 변경\n" +
                 "  rows.Add($\"{e.Id.Value},{e.Name},{e.Category?.Name},{p?.AsString()}\");\n" +
                 "}\n" +
                 "string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), \"output.csv\");\n" +
                 "System.IO.File.WriteAllLines(path, rows, System.Text.Encoding.UTF8);\n" +
                 "result.AppendLine($\"저장 완료: {path} ({rows.Count-1}행)\");",
                 false),

                (new[]{"면적","area","체적","volume","물량"},
                 "면적/체적 계산",
                 "double totalArea = 0, totalVol = 0;\n" +
                 "var elems = new FilteredElementCollector(doc)\n" +
                 "  .WhereElementIsNotElementType()\n" +
                 "  .OfCategory(BuiltInCategory.OST_Walls) // ← 카테고리 변경\n" +
                 "  .ToList();\n" +
                 "foreach (var e in elems) {\n" +
                 "  var areaP = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);\n" +
                 "  var volP  = e.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);\n" +
                 "  if (areaP != null) totalArea += areaP.AsDouble();\n" +
                 "  if (volP  != null) totalVol  += volP.AsDouble();\n" +
                 "}\n" +
                 "result.AppendLine($\"요소 수: {elems.Count}\");\n" +
                 "result.AppendLine($\"총 면적: {totalArea * 0.0929:F2} m²\");\n" +
                 "result.AppendLine($\"총 체적: {totalVol  * 0.0283:F3} m³\");",
                 false),

                (new[]{"이름","name","rename","바꾸","변경","수정","파라미터","parameter"},
                 "파라미터 일괄 수정",
                 "var elems = new FilteredElementCollector(doc)\n" +
                 "  .WhereElementIsNotElementType()\n" +
                 "  .OfCategory(BuiltInCategory.OST_Walls) // ← 카테고리 변경\n" +
                 "  .ToList();\n" +
                 "int count = 0;\n" +
                 "foreach (var e in elems) {\n" +
                 "  var p = e.LookupParameter(\"파라미터이름\"); // ← 파라미터명 변경\n" +
                 "  if (p == null || p.IsReadOnly) continue;\n" +
                 "  p.Set(\"새로운값\"); // ← 값 변경\n" +
                 "  count++;\n" +
                 "}\n" +
                 "result.AppendLine($\"{count}개 요소 파라미터 설정 완료\");",
                 true),

                (new[]{"join","결합","조인","unjoin","해제"},
                 "Join Geometry",
                 "var cat1 = BuiltInCategory.OST_Walls;             // ← 첫번째 카테고리\n" +
                 "var cat2 = BuiltInCategory.OST_StructuralColumns;  // ← 두번째 카테고리\n" +
                 "var list1 = new FilteredElementCollector(doc).WhereElementIsNotElementType().OfCategory(cat1).ToList();\n" +
                 "var list2 = new FilteredElementCollector(doc).WhereElementIsNotElementType().OfCategory(cat2).ToList();\n" +
                 "int joined = 0;\n" +
                 "foreach (var e1 in list1) {\n" +
                 "  var bb1 = e1.get_BoundingBox(null);\n" +
                 "  foreach (var e2 in list2) {\n" +
                 "    try {\n" +
                 "      if (JoinGeometryUtils.AreElementsJoined(doc, e1, e2)) continue;\n" +
                 "      JoinGeometryUtils.JoinGeometry(doc, e1, e2);\n" +
                 "      joined++;\n" +
                 "    } catch { }\n" +
                 "  }\n" +
                 "}\n" +
                 "result.AppendLine($\"Join 완료: {joined}쌍\");",
                 true),

                (new[]{"mep","덕트","duct","파이프","pipe","배관"},
                 "MEP 요소 조회",
                 "var ducts = new FilteredElementCollector(doc)\n" +
                 "  .OfClass(typeof(Autodesk.Revit.DB.Mechanical.Duct))\n" +
                 "  .Cast<Autodesk.Revit.DB.Mechanical.Duct>().ToList();\n" +
                 "result.AppendLine($\"덕트 수: {ducts.Count}\");\n" +
                 "foreach (var d in ducts.Take(10)) {\n" +
                 "  double len = d.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() * 304.8 ?? 0;\n" +
                 "  result.AppendLine($\"  ID:{d.Id.Value} 길이:{len:F0}mm\");\n" +
                 "}",
                 false),

                (new[]{"선택","select","선택된","selected"},
                 "선택 요소 처리",
                 "var selIds = uiApp.ActiveUIDocument.Selection.GetElementIds();\n" +
                 "result.AppendLine($\"선택된 요소: {selIds.Count}개\");\n" +
                 "foreach (var id in selIds) {\n" +
                 "  var e = doc.GetElement(id);\n" +
                 "  result.AppendLine($\"  ID:{id.Value} | {e.Category?.Name} | {e.Name}\");\n" +
                 "}",
                 false),

                (new[]{"링크","link","rvt","연결"},
                 "링크 모델 요소 수집",
                 "var links = new FilteredElementCollector(doc)\n" +
                 "  .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList();\n" +
                 "result.AppendLine($\"링크 파일: {links.Count}개\");\n" +
                 "foreach (var link in links) {\n" +
                 "  var ld = link.GetLinkDocument();\n" +
                 "  if (ld == null) { result.AppendLine($\"  {link.Name}: 언로드됨\"); continue; }\n" +
                 "  var cnt = new FilteredElementCollector(ld)\n" +
                 "    .WhereElementIsNotElementType()\n" +
                 "    .OfCategory(BuiltInCategory.OST_StructuralColumns).GetElementCount();\n" +
                 "  result.AppendLine($\"  {link.Name}: 구조기둥 {cnt}개\");\n" +
                 "}",
                 false),
            };

        public override JToken Execute(Document doc, JObject args)
        {
            var task = args["task"]!.ToString().ToLower();

            // 환경 정보
            var revitVer = doc.Application.VersionNumber;
            var hasDynamo = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name?.Contains("Dynamo") == true);

            // 키워드 매칭
            var matched = Templates.Where(t =>
                t.keywords.Any(kw => task.Contains(kw))).ToList();

            var suggestions = new JArray();
            if (matched.Count > 0)
            {
                foreach (var (_, category, template, needsTx) in matched)
                {
                    suggestions.Add(new JObject
                    {
                        ["카테고리"] = category,
                        ["execute_csharp_코드"] = template,
                        ["useTransaction"] = needsTx,
                        ["사용방법"] = $"위 코드를 execute_csharp(code: \"...\", useTransaction: {needsTx.ToString().ToLower()})에 바로 붙여 실행하세요."
                    });
                }
            }
            else
            {
                // 범용 템플릿
                suggestions.Add(new JObject
                {
                    ["카테고리"] = "범용 조회",
                    ["execute_csharp_코드"] =
                        "// 원하는 카테고리로 요소 수집\n" +
                        "var elems = new FilteredElementCollector(doc)\n" +
                        "  .WhereElementIsNotElementType()\n" +
                        "  .OfCategory(BuiltInCategory.OST_Walls) // ← 변경\n" +
                        "  .ToList();\n" +
                        "result.AppendLine($\"요소 수: {elems.Count}\");\n" +
                        "foreach (var e in elems.Take(20))\n" +
                        "  result.AppendLine($\"  {e.Id.Value}: {e.Name}\");",
                    ["useTransaction"] = false,
                    ["사용방법"] = "카테고리와 로직을 수정하여 execute_csharp으로 실행하세요."
                });
            }

            return TextContent(new JObject
            {
                ["요청"] = args["task"]!.ToString(),
                ["Revit버전"] = revitVer,
                ["Dynamo사용가능"] = hasDynamo,
                ["권장방법"] = "아래 코드를 execute_csharp 도구에 code 파라미터로 전달하여 즉시 실행",
                ["코드제안"] = suggestions,
                ["추가힌트"] = "get_revit_api_hints(topic: \"관련주제\")로 더 자세한 API 사용법 확인 가능"
            }.ToString(Newtonsoft.Json.Formatting.Indented));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  4. list_mcp_tools  — 현재 등록된 MCP 도구 전체 목록
    // ═══════════════════════════════════════════════════════════════
    public class ListMcpToolsTool : ToolBase
    {
        public override string Name => "list_mcp_tools";
        public override string Description =>
            "현재 RevitMCP에 등록된 모든 도구의 이름과 설명을 반환합니다. " +
            "원하는 기능이 이미 있는지 확인할 때 사용합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name, ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["filter"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "도구 이름/설명 필터 키워드 (선택)"
                    }
                }
            }
        };

        // ToolRegistry를 직접 참조하지 않고 reflection으로 조회
        private static List<(string name, string desc)>? _cached;

        public override JToken Execute(Document doc, JObject args)
        {
            var filter = args["filter"]?.ToString()?.ToLower();

            // 현재 로드된 ToolBase 구현체 전부 탐색
            if (_cached == null)
            {
                _cached = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .Where(t => !t.IsAbstract && typeof(ToolBase).IsAssignableFrom(t))
                    .Select(t => { try { var inst = (ToolBase)Activator.CreateInstance(t)!; return (inst.Name, inst.Description); } catch { return ("", ""); } })
                    .Where(x => !string.IsNullOrEmpty(x.Name))
                    .OrderBy(x => x.Name)
                    .ToList();
            }

            var tools = filter == null
                ? _cached
                : _cached.Where(x => x.name.Contains(filter) || x.desc.ToLower().Contains(filter)).ToList();

            var arr = new JArray(tools.Select(t => new JObject
            {
                ["name"] = t.name,
                ["description"] = t.desc
            }));

            return TextContent(new JObject
            {
                ["총도구수"] = _cached.Count,
                ["조회수"] = tools.Count,
                ["필터"] = filter ?? "없음",
                ["도구목록"] = arr
            }.ToString(Newtonsoft.Json.Formatting.Indented));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  5. run_dynamo_script  — Dynamo 스크립트 실행
    // ═══════════════════════════════════════════════════════════════
    public class RunDynamoScriptTool : ToolBase
    {
        public override string Name => "run_dynamo_script";
        public override string Description =>
            "Dynamo .dyn 스크립트 파일을 실행합니다. " +
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
                        ["description"] = "Dynamo .dyn 파일 전체 경로"
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var scriptPath = args["scriptPath"]!.ToString();
            if (!File.Exists(scriptPath))
                throw new Exception($"Dynamo 스크립트 파일 없음: {scriptPath}");

            var hasDynamo = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name?.Contains("DynamoRevitDS") == true
                       || a.GetName().Name?.Contains("Dynamo.Applications") == true);

            if (!hasDynamo)
                throw new Exception(
                    "DynamoRevit가 로드되지 않았습니다. " +
                    "Revit → Manage 탭 → Dynamo를 한 번 실행 후 다시 시도하세요.");

            return TextContent(new JObject
            {
                ["상태"] = "Dynamo 감지됨",
                ["스크립트"] = scriptPath,
                ["안내"] = "Dynamo headless API는 버전마다 달라 직접 호출이 제한됩니다. " +
                           "동일 로직을 execute_csharp으로 구현하는 방법을 권장합니다.",
                ["대안"] = "solve_missing_feature → execute_csharp"
            }.ToString());
        }
    }
}
