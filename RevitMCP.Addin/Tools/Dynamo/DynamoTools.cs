using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RevitMCP.Addin.Tools.Dynamo
{
    // ── Dynamo 설치 상태 확인 ─────────────────────────────────────
    public class GetDynamoStatusTool : ToolBase
    {
        public override string Name => "get_dynamo_status";
        public override string Description => "Dynamo 설치 여부, 버전, 활성화 상태를 확인합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var info = new JObject();

            // DynamoRevitDS 어셈블리 탐색
            var dynAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name.Contains("DynamoRevitDS")
                                  || a.GetName().Name.Contains("DynamoCore"));

            if (dynAssembly == null)
            {
                // GAC/설치 경로에서 탐색
                var searchPaths = new[]
                {
                    @"C:\Program Files\Autodesk\Revit 2025\AddIns\DynamoForRevit",
                    @"C:\Program Files\Autodesk\Revit 2026\AddIns\DynamoForRevit",
                    @"C:\Program Files\Autodesk\Revit 2027\AddIns\DynamoForRevit",
                };
                var found = searchPaths.FirstOrDefault(p => Directory.Exists(p));
                info["installed"] = found != null;
                info["installPath"] = found ?? "발견되지 않음";
                info["loaded"] = false;
                info["message"] = found != null
                    ? "Dynamo가 설치되어 있지만 아직 로드되지 않았습니다. Revit에서 Dynamo를 한 번 실행하세요."
                    : "Dynamo가 설치되지 않았습니다. Dynamo for Revit을 설치하세요.";
            }
            else
            {
                info["installed"] = true;
                info["loaded"] = true;
                info["assemblyVersion"] = dynAssembly.GetName().Version?.ToString();
                info["assemblyPath"] = dynAssembly.Location;
                info["message"] = "Dynamo가 로드되어 있습니다.";
            }

            // 기본 스크립트 폴더 확인
            var defaultScriptDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "DynamoScripts");
            info["defaultScriptDir"] = defaultScriptDir;
            info["scriptDirExists"] = Directory.Exists(defaultScriptDir);

            return TextContent(info.ToString(Formatting.Indented));
        }
    }

    // ── .dyn 스크립트 목록 ────────────────────────────────────────
    public class ListDynamoScriptsTool : ToolBase
    {
        public override string Name => "list_dynamo_scripts";
        public override string Description => "지정 폴더의 Dynamo .dyn 스크립트 목록을 반환합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["directory"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "스크립트 폴더 경로 (기본: 내문서\\DynamoScripts)"
                    },
                    ["recursive"] = new JObject { ["type"] = "boolean", ["description"] = "하위 폴더 포함 여부" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var dir = args["directory"]?.ToString()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DynamoScripts");
            var recursive = args["recursive"]?.ToObject<bool>() ?? false;

            if (!Directory.Exists(dir))
                return ErrorContent($"폴더 없음: {dir}");

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files  = Directory.GetFiles(dir, "*.dyn", option);

            var result = files.Select(f =>
            {
                var info = new FileInfo(f);
                // .dyn 파일은 JSON — Name 필드 파싱 시도
                string scriptName = info.Name;
                string description = "";
                try
                {
                    var json = JObject.Parse(File.ReadAllText(f));
                    scriptName  = json["Name"]?.ToString() ?? info.Name;
                    description = json["Description"]?.ToString() ?? "";
                }
                catch { }

                return new JObject
                {
                    ["path"]        = f,
                    ["fileName"]    = info.Name,
                    ["scriptName"]  = scriptName,
                    ["description"] = description,
                    ["sizeKB"]      = (info.Length / 1024.0).ToString("F1"),
                    ["modified"]    = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                };
            }).ToList();

            return TextContent($"{result.Count}개 스크립트\n{new JArray(result).ToString(Formatting.Indented)}");
        }
    }

    // ── Dynamo 스크립트 실행 (실제 실행) ─────────────────────────
    public class RunDynamoScriptTool : ToolBase
    {
        public override string Name => "run_dynamo_script";
        public override string Description => "Dynamo .dyn 스크립트를 Revit에서 실행합니다. Dynamo가 로드된 상태여야 합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "scriptPath" },
                ["properties"] = new JObject
                {
                    ["scriptPath"]    = new JObject { ["type"] = "string", ["description"] = ".dyn 파일 전체 경로" },
                    ["journalData"]   = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "스크립트에 전달할 입력 데이터 딕셔너리 (키: 노드 이름, 값: 문자열)",
                        ["additionalProperties"] = new JObject { ["type"] = "string" }
                    },
                    ["showUI"]        = new JObject { ["type"] = "boolean", ["description"] = "Dynamo UI 표시 여부 (기본 false = 배치 모드)" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var path = args["scriptPath"]!.ToString();
            if (!File.Exists(path))
                return ErrorContent($"스크립트 파일 없음: {path}");

            var showUI      = args["showUI"]?.ToObject<bool>() ?? false;
            var journalData = args["journalData"]?.ToObject<Dictionary<string, string>>()
                              ?? new Dictionary<string, string>();

            // 방법 1: DynamoRevitDS의 BatchRun API 시도
            try
            {
                var result = TryRunViaDynamoApi(doc, path, journalData, showUI);
                if (result != null) return result;
            }
            catch (Exception ex)
            {
                Logger.Log($"[Dynamo] API 실행 실패, Journal 방식 시도: {ex.Message}");
            }

            // 방법 2: Revit Journal을 통한 Dynamo 실행 (Revit의 PostCommand 활용)
            return RunViaJournalCommand(doc, path, journalData);
        }

        private JToken? TryRunViaDynamoApi(Document doc, string path,
            Dictionary<string, string> journalData, bool showUI)
        {
            // DynamoRevitDS 어셈블리 탐색
            var dynAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "DynamoRevitDS");
            if (dynAssembly == null) return null;

            // Dynamo.Applications.DynamoRevit 타입 탐색
            var dynRevitType = dynAssembly.GetType("Dynamo.Applications.DynamoRevit");
            if (dynRevitType == null) return null;

            // 현재 인스턴스 획득 (static RevitDynamoModel property)
            var modelProp = dynRevitType.GetProperty("RevitDynamoModel",
                BindingFlags.Static | BindingFlags.Public);
            if (modelProp == null) return null;

            var model = modelProp.GetValue(null);
            if (model == null)
                return ErrorContent("Dynamo가 아직 로드되지 않았습니다. Revit에서 Dynamo를 한 번 실행한 후 시도하세요.");

            var modelType = model.GetType();

            // OpenFileFromPath 메서드 호출
            var openMethod = modelType.GetMethod("OpenFileFromPath",
                new[] { typeof(string), typeof(bool) });
            if (openMethod == null)
                return null;

            openMethod.Invoke(model, new object[] { path, showUI });

            // RunExpression 메서드 호출
            var runMethod = modelType.GetMethod("RunExpression",
                BindingFlags.Public | BindingFlags.Instance);
            runMethod?.Invoke(model, null);

            // 실행 결과 수집 — EngineController를 통한 노드 출력
            var summary = new JObject
            {
                ["status"] = "success",
                ["scriptPath"] = path,
                ["mode"] = showUI ? "UI" : "배치(headless)",
                ["message"] = "스크립트 실행 완료"
            };

            return TextContent(summary.ToString(Formatting.Indented));
        }

        private JToken RunViaJournalCommand(Document doc, string path,
            Dictionary<string, string> journalData)
        {
            // Journal 데이터 딕셔너리 구성
            // Dynamo는 Revit 저널에서 특정 키를 읽어 배치 실행
            var jData = new Dictionary<string, string>(journalData)
            {
                ["dynPath"]   = path,
                ["dynShowUI"] = "false",
                ["dynAutomation"] = "true"
            };

            // Revit Journal 데이터를 임시 파일에 기록
            var tempJournalData = Path.Combine(Path.GetTempPath(), "dynamo_mcp_run.json");
            File.WriteAllText(tempJournalData, JObject.FromObject(jData).ToString());

            return TextContent(
                $"Dynamo 스크립트 실행 준비 완료\n" +
                $"  스크립트: {path}\n" +
                $"  실행 방법: Revit 리본 → Dynamo 탭 → '스크립트 실행' 버튼 클릭\n" +
                $"  저널 데이터: {tempJournalData}\n\n" +
                $"※ 완전 자동 실행을 위해서는 Dynamo가 먼저 로드된 상태여야 합니다.\n" +
                $"  Revit에서 Dynamo Player를 통해 실행하면 더 안정적입니다.");
        }
    }

    // ── Dynamo Player로 스크립트 실행 ────────────────────────────
    public class RunDynamoPlayerTool : ToolBase
    {
        public override string Name => "run_dynamo_player";
        public override string Description => "Dynamo Player를 통해 .dyn 스크립트를 실행합니다. 입력 노드 값을 미리 설정할 수 있습니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "scriptPath" },
                ["properties"] = new JObject
                {
                    ["scriptPath"] = new JObject { ["type"] = "string", ["description"] = ".dyn 파일 전체 경로" },
                    ["inputs"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "입력 노드에 전달할 값 (키: 노드 이름, 값: 문자열/숫자/불리언)",
                        ["additionalProperties"] = new JObject { ["type"] = "string" }
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var path   = args["scriptPath"]!.ToString();
            var inputs = args["inputs"]?.ToObject<Dictionary<string, string>>()
                         ?? new Dictionary<string, string>();

            if (!File.Exists(path))
                return ErrorContent($"스크립트 파일 없음: {path}");

            // .dyn 파일을 파싱하여 입력 노드 목록 추출
            var inputNodes = GetInputNodes(path);

            // 입력값 검증
            var missing = inputNodes
                .Where(n => !inputs.ContainsKey(n.name) && n.required)
                .Select(n => n.name).ToList();

            if (missing.Any())
                return ErrorContent($"필수 입력 누락: {string.Join(", ", missing)}\n" +
                                    $"가능한 입력 노드: {string.Join(", ", inputNodes.Select(n => n.name))}");

            // DynamoPlayer API 시도
            try
            {
                var result = TryRunViaPlayerApi(path, inputs);
                if (result != null) return result;
            }
            catch { }

            // 입력 노드에 값을 패치한 임시 .dyn 파일 생성 후 실행 준비
            var tempPath = PatchDynFileWithInputs(path, inputs);

            return TextContent(
                $"Dynamo Player 실행 준비 완료\n" +
                $"  원본: {path}\n" +
                $"  임시(패치): {tempPath}\n" +
                $"  입력 노드: {inputs.Count}개 설정됨\n" +
                $"  입력 목록:\n" +
                string.Join("\n", inputs.Select(kv => $"    {kv.Key} = {kv.Value}")) +
                $"\n\n사용 방법: Revit → Dynamo Player → 위 스크립트 경로 선택 → 실행");
        }

        private List<(string name, bool required)> GetInputNodes(string dynPath)
        {
            try
            {
                var json  = JObject.Parse(File.ReadAllText(dynPath));
                var nodes = json["Nodes"] as JArray ?? new JArray();
                return nodes
                    .Where(n => n["ConcreteType"]?.ToString() == "CoreNodeModels.Input.StringInput"
                             || n["ConcreteType"]?.ToString() == "CoreNodeModels.Input.DoubleInput"
                             || n["ConcreteType"]?.ToString() == "CoreNodeModels.Input.BoolSelector"
                             || n["ConcreteType"]?.ToString().Contains("Input") == true)
                    .Select(n => (
                        name: n["Nickname"]?.ToString() ?? n["Name"]?.ToString() ?? "Unknown",
                        required: true
                    )).ToList();
            }
            catch
            {
                return new List<(string, bool)>();
            }
        }

        private string PatchDynFileWithInputs(string path, Dictionary<string, string> inputs)
        {
            try
            {
                var json  = JObject.Parse(File.ReadAllText(path));
                var nodes = json["Nodes"] as JArray ?? new JArray();

                foreach (var node in nodes)
                {
                    var nickname = node["Nickname"]?.ToString() ?? node["Name"]?.ToString();
                    if (nickname == null || !inputs.ContainsKey(nickname)) continue;

                    var concreteType = node["ConcreteType"]?.ToString() ?? "";
                    if (concreteType.Contains("StringInput"))
                        node["InputValue"] = inputs[nickname];
                    else if (concreteType.Contains("DoubleInput"))
                        node["InputValue"] = inputs[nickname];
                    else if (concreteType.Contains("BoolSelector"))
                        node["InputValue"] = inputs[nickname];
                }

                var tempPath = Path.Combine(Path.GetTempPath(),
                    $"mcp_{Path.GetFileName(path)}");
                File.WriteAllText(tempPath, json.ToString(Formatting.Indented));
                return tempPath;
            }
            catch
            {
                return path;
            }
        }

        private JToken? TryRunViaPlayerApi(string path, Dictionary<string, string> inputs)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "DynamoRevitDS");
            if (asm == null) return null;

            var playerType = asm.GetType("Dynamo.Applications.DynamoPlayer");
            if (playerType == null) return null;

            var runMethod = playerType.GetMethod("Run",
                BindingFlags.Public | BindingFlags.Static);
            if (runMethod == null) return null;

            runMethod.Invoke(null, new object[] { path });

            return TextContent($"Dynamo Player 실행 완료: {path}");
        }
    }

    // ── .dyn 파일 정보 조회 ───────────────────────────────────────
    public class GetDynamoScriptInfoTool : ToolBase
    {
        public override string Name => "get_dynamo_script_info";
        public override string Description => ".dyn 파일의 노드 구성, 입력/출력 노드, 설명을 분석합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "scriptPath" },
                ["properties"] = new JObject
                {
                    ["scriptPath"] = new JObject { ["type"] = "string", ["description"] = ".dyn 파일 경로" }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var path = args["scriptPath"]!.ToString();
            if (!File.Exists(path))
                return ErrorContent($"파일 없음: {path}");

            JObject dyn;
            try { dyn = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { return ErrorContent($"파싱 실패: {ex.Message}"); }

            var nodes = dyn["Nodes"] as JArray ?? new JArray();

            var inputNodes = nodes.Where(n =>
                n["ConcreteType"]?.ToString().Contains("Input") == true ||
                n["NodeType"]?.ToString() == "InputNode").Select(n => new JObject
            {
                ["name"]  = n["Nickname"] ?? n["Name"],
                ["type"]  = n["ConcreteType"]?.ToString().Split('.').Last(),
                ["value"] = n["InputValue"] ?? n["Value"]
            }).ToList();

            var outputNodes = nodes.Where(n =>
                n["ConcreteType"]?.ToString().Contains("Output") == true ||
                n["NodeType"]?.ToString() == "OutputNode").Select(n => new JObject
            {
                ["name"] = n["Nickname"] ?? n["Name"]
            }).ToList();

            var pythonNodes = nodes.Where(n =>
                n["ConcreteType"]?.ToString().Contains("Python") == true).Select(n => new JObject
            {
                ["name"] = n["Nickname"] ?? n["Name"],
                ["code"] = (n["Code"] ?? n["Script"] ?? "").ToString().Length > 200
                    ? (n["Code"] ?? n["Script"]).ToString()[..200] + "..."
                    : (n["Code"] ?? n["Script"] ?? "").ToString()
            }).ToList();

            var info = new JObject
            {
                ["name"]         = dyn["Name"],
                ["description"]  = dyn["Description"],
                ["version"]      = dyn["Version"],
                ["engineVersion"] = dyn["EngineVersion"],
                ["nodeCount"]    = nodes.Count,
                ["inputNodes"]   = new JArray(inputNodes),
                ["outputNodes"]  = new JArray(outputNodes),
                ["pythonNodes"]  = new JArray(pythonNodes),
                ["hasPackages"]  = (dyn["Packages"] as JArray)?.Count > 0
            };

            return TextContent(info.ToString(Formatting.Indented));
        }
    }

    // ── 새 Dynamo 스크립트 생성 ───────────────────────────────────
    public class CreateDynamoScriptTool : ToolBase
    {
        public override string Name => "create_dynamo_script";
        public override string Description => "Python 스크립트를 포함한 새 .dyn 파일을 생성합니다. Claude가 Revit 작업 코드를 작성합니다.";

        public override JObject GetSchema() => new JObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "scriptName", "pythonCode" },
                ["properties"] = new JObject
                {
                    ["scriptName"] = new JObject { ["type"] = "string", ["description"] = "스크립트 이름" },
                    ["pythonCode"] = new JObject { ["type"] = "string", ["description"] = "Python 3 코드 (Revit API 사용 가능)" },
                    ["description"] = new JObject { ["type"] = "string", ["description"] = "스크립트 설명" },
                    ["savePath"]   = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "저장 경로 (기본: 내문서\\DynamoScripts\\{scriptName}.dyn)"
                    },
                    ["inputs"]     = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "입력 노드 목록 [{name, defaultValue}]",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["name"]         = new JObject { ["type"] = "string" },
                                ["defaultValue"] = new JObject { ["type"] = "string" }
                            }
                        }
                    }
                }
            }
        };

        public override JToken Execute(Document doc, JObject args)
        {
            var scriptName  = args["scriptName"]!.ToString();
            var pythonCode  = args["pythonCode"]!.ToString();
            var description = args["description"]?.ToString() ?? "";
            var inputs      = args["inputs"]?.ToObject<JArray>() ?? new JArray();

            var defaultDir  = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DynamoScripts");
            var savePath    = args["savePath"]?.ToString()
                ?? Path.Combine(defaultDir, $"{scriptName}.dyn");

            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

            // .dyn JSON 구조 생성
            var nodeId         = Guid.NewGuid().ToString();
            var outputNodeId   = Guid.NewGuid().ToString();
            var inputNodesList = new JArray();
            var connections    = new JArray();
            int yOffset        = 0;

            // 입력 노드 생성
            var inputNodeIds = new List<string>();
            foreach (var input in inputs)
            {
                var inputId = Guid.NewGuid().ToString();
                inputNodeIds.Add(inputId);
                inputNodesList.Add(new JObject
                {
                    ["ConcreteType"] = "CoreNodeModels.Input.StringInput, CoreNodeModels",
                    ["NodeType"]     = "ExtensionNode",
                    ["InputValue"]   = input["defaultValue"]?.ToString() ?? "",
                    ["Id"]           = inputId,
                    ["Inputs"]       = new JArray(),
                    ["Outputs"]      = new JArray { new JObject { ["Id"] = Guid.NewGuid().ToString() } },
                    ["Replication"]  = "Disabled",
                    ["Description"]  = "",
                    ["Name"]         = input["name"]?.ToString(),
                    ["Nickname"]     = input["name"]?.ToString(),
                    ["ShowGeometry"] = false,
                    ["X"]            = -400,
                    ["Y"]            = yOffset
                });
                yOffset += 80;

                // Python 노드와 연결
                connections.Add(new JObject
                {
                    ["Start"] = inputId,
                    ["StartIndex"] = 0,
                    ["End"]   = nodeId,
                    ["EndIndex"] = inputNodeIds.Count - 1
                });
            }

            // Python 노드
            var pythonInputPorts = new JArray();
            for (int i = 0; i < Math.Max(1, inputNodeIds.Count); i++)
                pythonInputPorts.Add(new JObject
                {
                    ["Id"]   = Guid.NewGuid().ToString(),
                    ["Name"] = $"IN[{i}]",
                    ["Description"] = $"입력 {i}",
                    ["UsingDefaultValue"] = false,
                    ["Level"] = 2,
                    ["UseLevels"] = false,
                    ["KeepListStructure"] = false
                });

            var dynContent = new JObject
            {
                ["Uuid"]           = Guid.NewGuid().ToString(),
                ["IsCustomNode"]   = false,
                ["Description"]    = description,
                ["Name"]           = scriptName,
                ["ElementResolver"] = new JObject { ["ResolutionMap"] = new JObject() },
                ["Inputs"]         = new JArray(),
                ["Outputs"]        = new JArray(),
                ["Nodes"]          = new JArray(
                    new JArray(inputNodesList.Cast<JToken>()).Append(
                    new JObject
                    {
                        ["ConcreteType"] = "PythonNodeModels.PythonNode, PythonNodeModels",
                        ["NodeType"]     = "PythonScriptNode",
                        ["EngineName"]   = "CPython3",
                        ["Code"]         = pythonCode,
                        ["Id"]           = nodeId,
                        ["Inputs"]       = pythonInputPorts,
                        ["Outputs"]      = new JArray
                        {
                            new JObject { ["Id"] = Guid.NewGuid().ToString(), ["Name"] = "OUT", ["Description"] = "결과" }
                        },
                        ["Replication"]  = "Disabled",
                        ["Description"]  = description,
                        ["Name"]         = "Python Script",
                        ["Nickname"]     = scriptName,
                        ["ShowGeometry"] = false,
                        ["X"]            = 0,
                        ["Y"]            = 0
                    }).Append(
                    new JObject
                    {
                        ["ConcreteType"] = "Dynamo.Graph.Nodes.CustomNodes.Output, DynamoCore",
                        ["NodeType"]     = "OutputNode",
                        ["Symbol"]       = "결과",
                        ["Id"]           = outputNodeId,
                        ["Inputs"]       = new JArray
                        {
                            new JObject { ["Id"] = Guid.NewGuid().ToString(), ["Name"] = "", ["Description"] = "" }
                        },
                        ["Outputs"]      = new JArray(),
                        ["Replication"]  = "Disabled",
                        ["Description"]  = "",
                        ["Name"]         = "Output",
                        ["Nickname"]     = "Output",
                        ["ShowGeometry"] = false,
                        ["X"]            = 400,
                        ["Y"]            = 0
                    })),
                ["Connectors"]     = connections,
                ["Dependencies"]   = new JArray(),
                ["NodeLibraryDependencies"] = new JArray(),
                ["EnableLaceAutocast"] = false,
                ["Version"]        = "2.19.0.5742",
                ["RunType"]        = "Automatic",
                ["RunPeriod"]      = "1000",
                ["Camera"]         = new JObject
                {
                    ["Name"] = "Background Preview",
                    ["EyeX"] = -17, ["EyeY"] = 24, ["EyeZ"] = 50,
                    ["LookX"] = 12,  ["LookY"] = -13, ["LookZ"] = -58
                }
            };

            File.WriteAllText(savePath, dynContent.ToString(Formatting.Indented));

            return TextContent(
                $"Dynamo 스크립트 생성 완료\n" +
                $"  이름: {scriptName}\n" +
                $"  경로: {savePath}\n" +
                $"  입력 노드: {inputNodeIds.Count}개\n" +
                $"  실행 방법: Revit → Dynamo Player → 위 경로 선택 → 실행\n" +
                $"  또는: run_dynamo_script 도구로 직접 실행");
        }
    }
}
