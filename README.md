# RevitMCP — AI 기반 Revit 자동화 플랫폼

**Claude Desktop 앱**과 Revit을 연결하여 모델링 자동화, 도서 자동화, 충돌 검사 등을 AI로 처리할 수 있는 Revit Addin입니다.

## 지원 버전
| Revit | .NET | 상태 |
|-------|------|------|
| 2025 | 4.8 | ✅ 지원 |
| 2026 | 4.8 | ✅ 지원 |
| 2027 | 4.8 | ✅ 지원 |

> ⚠️ **Claude Desktop 전용입니다.** Claude Code(터미널)에서는 동작하지 않습니다.

---

## 설치 방법

### 사전 준비
- [Revit 2025 / 2026 / 2027](https://www.autodesk.com/products/revit) 설치
- [.NET SDK 4.8 이상](https://dotnet.microsoft.com/download) 설치
- [Claude Desktop 앱](https://claude.ai/download) 설치

### 설치 (Setup.ps1 한 번 실행으로 완료)

```powershell
.\Setup.ps1
```

실행하면 아래가 자동으로 처리됩니다:
1. 설치된 Revit 버전 자동 감지
2. 버전 선택 메뉴 표시
3. Revit Addin 빌드 및 설치
4. Claude Desktop MCP 자동 등록 (`claude_desktop_config.json`)

### 사용 방법

```
1. Revit 재시작
2. 'RevitMCP' 탭 → [MCP 시작] 버튼 클릭
3. Claude Desktop 앱 재시작
4. Claude에게 Revit 작업을 요청하세요!
```

---

## 제공 도구 (MCP Tools) — 총 55개

### 요소 조회 / 조작
| 도구 | 설명 |
|------|------|
| `get_elements` | 카테고리·레벨 필터로 요소 목록 조회 |
| `get_element_parameters` | 요소의 모든 파라미터 조회 |
| `set_parameter` | 파라미터 값 설정 |
| `delete_element` | 요소 삭제 |
| `select_elements` | UI에서 요소 선택 |

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

### 도서 자동화
| 도구 | 설명 |
|------|------|
| `create_sheet` | 도면 시트 생성 |
| `batch_create_sheets` | 번호/이름 목록으로 시트 일괄 생성 |
| `create_view` | 평면도·입면도·단면도 생성 |
| `duplicate_view` | 뷰 복제 (상세 포함 선택) |
| `apply_view_template` | 뷰 템플릿 적용 |
| `place_viewport` | 시트에 뷰 배치 |
| `create_schedule` | 일람표 생성 |
| `export_sheets_to_pdf` | 시트 PDF 내보내기 |
| `add_revision` | 개정 추가 |
| `create_text_note` | 텍스트 주석 추가 |
| `tag_element` | 요소 개별 태그 |
| `auto_tag_all` | 카테고리 전체 자동 태그 |
| `renumber_elements` | 요소 번호 자동 부여 |

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

### 뷰 / 그래픽 관리
| 도구 | 설명 |
|------|------|
| `create_view_filter` | 파라미터 조건 기반 뷰 필터 생성 |
| `override_element_graphics` | 요소 그래픽 재지정 (색상·투명도) |
| `color_clash_elements` | 간섭 요소 색상 강조 표시 |
| `purge_unused` | 미사용 패밀리/타입 정리 |

### 패밀리 관리 / 파라미터 편집
| 도구 | 설명 |
|------|------|
| `list_families` | 로드된 패밀리 전체 목록 조회 |
| `load_family` | .rfa 파일 패밀리 로드 |
| `replace_family_type` | 패밀리 타입 일괄 교체 |
| `export_family` | 패밀리 .rfa 파일로 내보내기 |
| `get_family_parameters` | 패밀리 파라미터 목록 조회 |
| `add_family_parameter` | 패밀리에 파라미터 추가 (인스턴스/타입, 유형, 그룹 지정) |
| `remove_family_parameter` | 패밀리 파라미터 삭제 |
| `set_family_parameter_formula` | 파라미터 수식 설정 (예: Width * 2) |
| `add_family_type` | 패밀리에 새 타입 추가 및 파라미터 값 설정 |

### 작업세트 관리
| 도구 | 설명 |
|------|------|
| `get_worksets` | 작업세트 목록 조회 |
| `create_workset` | 작업세트 생성 |
| `set_element_workset` | 요소 작업세트 변경 |
| `assign_workset_by_category` | 카테고리별 작업세트 일괄 배정 |

### 분석 / 물량
| 도구 | 설명 |
|------|------|
| `get_model_info` | 프로젝트 전체 정보 조회 |
| `material_takeoff` | 재료 물량 산출 |
| `room_data_summary` | 룸 면적·둘레·레벨 전체 요약 |


---

## 사용 예시 (Claude Desktop과 대화)

```
"1층 평면도의 모든 벽을 조회해줘"
→ get_elements(category: "OST_Walls", levelName: "1F")

"A-101 시트 만들고 1층 평면도 배치해줘"
→ create_sheet → create_view → place_viewport

"문 일람표 만들어서 PDF로 내보내줘"
→ create_schedule(category: "OST_Doors") → export_sheets_to_pdf

"구조 기둥과 덕트 충돌 검사해줘"
→ clash_detection(category1: "OST_StructuralColumns", category2: "OST_DuctCurves")
```

---

## 아키텍처

```
Claude Desktop 앱
       │ HTTP JSON-RPC 2.0
       ▼
RevitMCP Server (localhost:9876)
┌─────────────────────────────┐
│  MCPServer (HttpListener)   │
│  ToolRegistry (30개 도구)   │
│  RevitEventDispatcher       │ ←─ Revit 메인 스레드 위임
└─────────────────────────────┘
       │ ExternalEvent
       ▼
Revit API (메인 스레드)
```

## 로그 위치
`%AppData%\RevitMCP\revit-mcp.log`
