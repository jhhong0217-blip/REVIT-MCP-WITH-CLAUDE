# RevitMCP — AI 기반 Revit 자동화 플랫폼

**Claude Desktop 앱 채팅창**과 Revit을 연결하여 모델링 자동화, 도서 자동화, 충돌 검사, 패밀리 편집 등을 AI로 처리할 수 있는 Revit Addin입니다.

## 지원 버전

| Revit | 상태 |
|-------|------|
| 2025  | ✅ 지원 |
| 2026  | ✅ 지원 |
| 2027  | ✅ 지원 |

---

## 설치 방법

### 사전 준비

- [Revit 2025 / 2026 / 2027](https://www.autodesk.com/products/revit) 설치
- [Claude Desktop 앱](https://claude.ai/download) 설치
- .NET SDK 8 — 없으면 Install.bat이 자동 설치

### 설치 (중요: Claude Desktop을 완전히 종료한 상태에서 실행)

1. **Claude Desktop 완전 종료** (작업표시줄 우클릭 → Quit)
2. `Install.bat` 더블클릭
3. 아무 키 누르기
4. 설치할 Revit 버전 선택 (`[1]` ~ `[4] All`)
5. 설치 완료 후 **Revit 실행 → RevitMCP 탭 → [Start MCP] 클릭**
6. **Claude Desktop 재시작**

> ⚠️ Claude Desktop이 실행 중일 때 설치하면 config가 초기화됩니다. 반드시 종료 후 실행하세요.

---

## 사용 방법

1. Revit 실행
2. **RevitMCP 탭 → [Start MCP]** 클릭 (버튼이 초록색으로 변함)
3. Claude Desktop 앱 채팅창에서 Revit 작업 요청

```
"현재 모델의 레벨과 요소 수를 알려줘"
"1층 평면도의 모든 벽을 조회해줘"
"A-101 시트 만들고 1층 평면도 배치해줘"
"구조 기둥과 덕트 충돌 검사해줘"
```

---

## 아키텍처

```
Claude Desktop 채팅창
       │ stdio (JSON-RPC)
       ▼
RevitMCP.Bridge.exe          ← Claude Desktop이 자동 실행
       │ HTTP POST localhost:9876
       ▼
RevitMCP Addin (Revit 내부)
┌─────────────────────────────┐
│  MCPServer (HttpListener)   │
│  ToolRegistry (85개 도구)   │
│  RevitEventDispatcher       │ ← Revit 메인 스레드 위임
└─────────────────────────────┘
       │ ExternalEvent
       ▼
Revit API (메인 스레드)
```

---

## 제공 도구 (MCP Tools) — 총 85개

### 요소 조회 / 조작

| 도구 | 설명 |
|------|------|
| `get_elements` | 카테고리·레벨 필터로 요소 목록 조회 |
| `get_element_parameters` | 요소의 모든 파라미터 조회 |
| `set_parameter` | 파라미터 값 설정 |
| `delete_element` | 요소 삭제 |
| `select_elements` | UI에서 요소 선택 |
| `get_element_by_id` | ID로 요소 정보 및 파라미터 조회 |
| `list_element_types` | 카테고리의 모든 패밀리 타입 목록 |
| `get_element_location` | 요소 위치 좌표(mm) 및 방향 |
| `duplicate_element_type` | 기존 타입 복제하여 새 이름으로 생성 |
| `set_type_parameter` | 요소 타입의 파라미터 값 설정 |
| `get_elements_by_level` | 레벨별 요소 카테고리 그룹 조회 |
| `get_element_bounding_box` | 요소 바운딩 박스 좌표(mm) 조회 |
| `join_geometry_by_category` | 두 카테고리 간 교차 요소 자동 Join Geometry |
| `unjoin_geometry_by_category` | 두 카테고리 간 Join Geometry 전체 해제 |
| `join_geometry_by_ids` | ID 지정 요소 그룹 간 Join/Unjoin |
| `switch_join_order` | 두 요소 간 Join 우선순위(절단 방향) 반전 |

### 모델링 자동화

| 도구 | 설명 |
|------|------|
| `create_wall` | 두 점 사이 벽 생성 |
| `create_floor` | 폴리라인 경계로 바닥 슬래브 생성 |
| `create_room` | 룸 생성 및 이름/번호 지정 |
| `create_grid` | 그리드 선 생성 |
| `create_level` | 레벨 생성 |
| `place_family_instance` | 패밀리 인스턴스 배치 |
| `move_element` | 요소 이동 |
| `copy_element` | 요소 복사 |
| `rotate_element` | 요소 회전 |
| `mirror_element` | 요소 미러 |
| `create_dimension` | 치수선 생성 |
| `create_group` | 선택한 요소들을 그룹으로 묶기 |

### 도서 자동화

| 도구 | 설명 |
|------|------|
| `create_sheet` | 도면 시트 생성 |
| `batch_create_sheets` | 번호/이름 목록으로 시트 일괄 생성 |
| `create_view` | 평면도·입면도·단면도 생성 |
| `duplicate_view` | 뷰 복제 (상세 포함 선택) |
| `apply_view_template` | 뷰 템플릿 적용 |
| `list_views` | 현재 모델의 모든 뷰 목록 조회 |
| `navigate_view` | 지정한 이름의 뷰로 활성 뷰 전환 |
| `place_viewport` | 시트에 뷰 배치 |
| `create_schedule` | 일람표 생성 |
| `export_sheets_to_pdf` | 시트 PDF 내보내기 |
| `add_revision` | 개정 추가 |
| `create_text_note` | 텍스트 주석 추가 |
| `tag_element` | 요소 개별 태그 |
| `auto_tag_all` | 카테고리 전체 자동 태그 |
| `renumber_elements` | 요소 번호 자동 부여 |

### 뷰 고급 관리

| 도구 | 설명 |
|------|------|
| `get_sheets` | 모든 도면 시트 목록 + 배치된 뷰 정보 |
| `get_active_view_info` | 현재 활성 뷰 상세 정보 (타입·축척·레벨) |
| `set_view_scale` | 뷰 축척 변경 (예: 100 → 1:100) |
| `set_view_detail_level` | 뷰 상세 수준 변경 (Coarse/Medium/Fine) |
| `set_crop_region` | 자르기 영역 활성화/비활성화 및 경계 설정 |
| `hide_elements_in_view` | 뷰에서 요소 숨기기 |
| `unhide_elements_in_view` | 뷰에서 숨긴 요소 다시 표시 |
| `isolate_category_in_view` | 특정 카테고리만 격리 표시 |
| `create_view_filter` | 파라미터 조건 기반 뷰 필터 생성 |
| `override_element_graphics` | 요소 그래픽 재지정 (색상·투명도) |
| `color_clash_elements` | 간섭 요소 색상 강조 표시 |
| `purge_unused` | 미사용 패밀리/타입 정리 |

### 파라미터 관리

| 도구 | 설명 |
|------|------|
| `bulk_set_parameters` | 다중 요소 파라미터 일괄 설정 |
| `filter_elements_by_parameter` | 파라미터 값으로 요소 필터 |
| `export_parameters_to_csv` | 파라미터 CSV 내보내기 |
| `import_parameters_from_csv` | CSV → Revit 파라미터 일괄 가져오기 |

### 오류 / 품질 체크

| 도구 | 설명 |
|------|------|
| `get_warnings` | 모델 경고 목록 조회 |
| `get_warnings_detail` | 경고 유형 분류 + 해소 방법 제안 |
| `find_untagged_elements` | 미태그 요소 탐지 |
| `find_undimensioned_elements` | 미치수 요소 탐지 |
| `clash_detection` | 두 카테고리 간 충돌 감지 |

### 패밀리 관리 / 파라미터 편집

| 도구 | 설명 |
|------|------|
| `list_families` | 로드된 패밀리 전체 목록 조회 |
| `load_family` | .rfa 파일 패밀리 로드 |
| `replace_family_type` | 패밀리 타입 일괄 교체 |
| `export_family` | 패밀리 .rfa 파일로 내보내기 |
| `get_family_parameters` | 패밀리 파라미터 목록 조회 |
| `add_family_parameter` | 패밀리에 파라미터 추가 |
| `remove_family_parameter` | 패밀리 파라미터 삭제 |
| `set_family_parameter_formula` | 파라미터 수식 설정 |
| `add_family_type` | 패밀리에 새 타입 추가 |

### 작업세트 관리

| 도구 | 설명 |
|------|------|
| `get_worksets` | 작업세트 목록 조회 |
| `create_workset` | 작업세트 생성 |
| `set_element_workset` | 요소 작업세트 변경 |
| `assign_workset_by_category` | 카테고리별 작업세트 일괄 배정 |

### 프로젝트 / 문서 관리

| 도구 | 설명 |
|------|------|
| `list_phases` | 프로젝트 공사 단계(Phase) 목록 |
| `set_element_phase` | 요소의 신설/철거 단계 설정 |
| `get_linked_models` | 연결된 Revit 링크 파일 목록 |
| `get_project_parameters` | 프로젝트/공유 파라미터 정의 목록 |
| `get_revision_history` | 개정 이력 조회 |
| `get_schedule_data` | 일람표 데이터 표 형식 조회 |
| `get_rooms_info` | 룸 이름·번호·면적·레벨·위치 상세 조회 |

### 분석 / 물량

| 도구 | 설명 |
|------|------|
| `get_model_info` | 프로젝트 전체 정보 조회 |
| `material_takeoff` | 재료 물량 산출 |
| `room_data_summary` | 룸 면적·둘레·레벨 전체 요약 |
| `get_element_count` | 카테고리별 요소 수 집계 (레벨 필터 가능) |

---

## 사용 예시

```
"현재 모델 정보 알려줘"
→ get_model_info

"1층 평면도의 모든 벽을 조회해줘"
→ get_elements(category: "OST_Walls", levelName: "Z1")

"A-101 시트 만들고 1층 평면도 배치해줘"
→ create_sheet → create_view → place_viewport

"문 일람표 만들어서 PDF로 내보내줘"
→ create_schedule → export_sheets_to_pdf

"구조 기둥과 덕트 충돌 검사해줘"
→ clash_detection(category1: "OST_StructuralColumns", category2: "OST_DuctCurves")

"Z2 레벨 평면도로 이동해줘"
→ navigate_view(viewName: "Z2")

"모든 평면도 목록 보여줘"
→ list_views(viewType: "FloorPlan")
```

---

## 프로젝트 구조

```
revit-mcp/
├── Install.bat                        # 단일 설치 스크립트
├── RevitMCP.Addin/                    # Revit 애드인 (C# .NET 4.8)
│   ├── App.cs                         # IExternalApplication (리본 버튼)
│   ├── Config.cs                      # 포트(9876) / 버전 설정
│   ├── Commands/
│   │   └── ToggleMCPCommand.cs        # MCP 시작/중지
│   ├── Server/
│   │   ├── MCPServer.cs               # HttpListener JSON-RPC 2.0
│   │   └── RevitEventDispatcher.cs    # 메인 스레드 안전 실행
│   └── Tools/                         # 81개 도구
│       ├── Element/    ElementTools.cs, ElementAdvancedTools.cs
│       ├── Modeling/   ModelingTools.cs
│       ├── Document/   DocumentTools.cs
│       ├── View/       ViewTools.cs
│       ├── Project/    ProjectTools.cs
│       ├── Parameter/  ParameterTools.cs
│       ├── Family/     FamilyTools.cs, FamilyEditTools.cs
│       ├── Analysis/   AnalysisTools.cs, QualityCheckTools.cs
│       ├── Workset/    WorksetTools.cs
│       └── Automation/ AutomationTools.cs
└── RevitMCP.Bridge/                   # stdio↔HTTP 브릿지 (C# .NET 8)
    └── Program.cs                     # Claude Desktop ↔ Revit 연결
```

## 로그 위치

`%AppData%\RevitMCP\revit-mcp.log`
