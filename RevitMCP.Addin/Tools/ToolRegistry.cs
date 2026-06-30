using Autodesk.Revit.UI;
using RevitMCP.Addin.Tools.Elements;
using RevitMCP.Addin.Tools.Modeling;
using RevitMCP.Addin.Tools.Docs;
using RevitMCP.Addin.Tools.Analysis;
using RevitMCP.Addin.Tools.Params;
using RevitMCP.Addin.Tools.Families;
using RevitMCP.Addin.Tools.Workset;
using RevitMCP.Addin.Tools.Automation;
using RevitMCP.Addin.Tools.Views;
using RevitMCP.Addin.Tools.Project;
using System.Collections.Generic;

namespace RevitMCP.Addin.Tools
{
    public class ToolRegistry
    {
        private readonly Dictionary<string, ToolBase> _tools = new();

        public void Initialize(UIApplication app)
        {
            Register(
                // ── 요소 조회/조작 ──────────────────────────────
                new GetElementsTool(),
                new GetElementParametersTool(),
                new SetParameterTool(),
                new DeleteElementTool(),
                new SelectElementsTool(),

                // ── 모델링 자동화 ────────────────────────────────
                new CreateWallTool(),
                new CreateFloorTool(),
                new CreateRoomTool(),
                new CreateGridTool(),
                new CreateLevelTool(),
                new PlaceFamilyInstanceTool(),
                new MoveElementTool(),
                new CopyElementTool(),
                new RotateElementTool(),
                new MirrorElementTool(),
                new CreateDimensionTool(),

                // ── 도서 자동화 ──────────────────────────────────
                new CreateSheetTool(),
                new CreateViewTool(),
                new PlaceViewportTool(),
                new CreateScheduleTool(),
                new ExportSheetsTool(),
                new AddRevisionTool(),
                new CreateTextNoteTool(),
                new TagElementTool(),
                new NavigateViewTool(),
                new ListViewsTool(),

                // ── 파라미터 관리 ────────────────────────────────
                new BulkSetParametersTool(),
                new FilterElementsByParameterTool(),
                new ExportParametersTool(),
                new ImportParametersFromCsvTool(),

                // ── 분석/검토 ────────────────────────────────────
                new GetModelInfoTool(),
                new ClashDetectionTool(),
                new GetWarningsTool(),
                new GetWarningsDetailTool(),       // 경고 유형 분류 + 해소 제안
                new MaterialTakeoffTool(),

                // ── 오류/품질 체크 ───────────────────────────────
                new FindUntaggedElementsTool(),    // 미태그 요소 탐지
                new FindUndimensionedElementsTool(), // 미치수 요소 탐지

                // ── 뷰 관리 ──────────────────────────────────────
                new ColorClashElementsTool(),      // 간섭 요소 색상 표시
                new OverrideGraphicsTool(),        // 그래픽 재지정
                new CreateViewFilterTool(),        // 뷰 필터 생성
                new ApplyViewTemplateTool(),       // 뷰 템플릿 적용
                new DuplicateViewTool(),           // 뷰 복제
                new PurgeUnusedTool(),             // 미사용 요소 정리

                // ── 패밀리 관리 ──────────────────────────────────
                new ListFamiliesTool(),            // 패밀리 목록 조회
                new LoadFamilyTool(),              // 패밀리 로드
                new ReplaceFamilyTypeTool(),       // 패밀리 타입 일괄 교체
                new ExportFamilyTool(),            // 패밀리 내보내기

                // ── 패밀리 파라미터 편집 ─────────────────────────────
                new GetFamilyParametersTool(),     // 파라미터 목록 조회
                new AddFamilyParameterTool(),      // 파라미터 추가
                new RemoveFamilyParameterTool(),   // 파라미터 삭제
                new SetFamilyParameterFormulaTool(), // 수식 설정
                new AddFamilyTypeTool(),           // 패밀리 타입 추가

                // ── 작업세트 관리 ────────────────────────────────
                new GetWorksetsTool(),             // 작업세트 목록
                new CreateWorksetTool(),           // 작업세트 생성
                new SetElementWorksetTool(),       // 요소 작업세트 변경
                new AssignWorksetByCategoryTool(), // 카테고리별 일괄 배정

                // ── 자동화 워크플로우 ────────────────────────────
                new AutoTagAllTool(),              // 카테고리 전체 자동 태그
                new RenumberElementsTool(),        // 요소 번호 자동 부여
                new BatchCreateSheetsTool(),       // 시트 일괄 생성
                new RoomDataSummaryTool(),         // 룸 면적/둘레 요약

                // ── 뷰 고급 관리 ─────────────────────────────────
                new GetSheetsTool(),               // 시트 목록 + 배치된 뷰
                new SetViewScaleTool(),            // 뷰 축척 변경
                new SetCropRegionTool(),           // 자르기 영역 설정
                new GetActiveViewInfoTool(),       // 활성 뷰 상세 정보
                new SetViewDetailLevelTool(),      // 상세 수준 변경
                new HideElementsInViewTool(),      // 뷰에서 요소 숨기기
                new UnhideElementsInViewTool(),    // 숨긴 요소 표시
                new IsolateCategoryInViewTool(),   // 카테고리 격리

                // ── 요소 고급 조작 ───────────────────────────────
                new GetElementByIdTool(),          // ID로 요소 조회
                new ListElementTypesTool(),        // 카테고리 타입 목록
                new GetElementLocationTool(),      // 요소 위치 좌표
                new DuplicateElementTypeTool(),    // 타입 복제
                new SetTypeParameterTool(),        // 타입 파라미터 설정
                new GetElementsByLevelTool(),      // 레벨별 요소 목록
                new GetElementBoundingBoxTool(),   // 바운딩 박스 조회

                // ── 프로젝트 관리 ────────────────────────────────
                new ListPhasesTool(),              // 공사 단계 목록
                new GetLinkedModelsTool(),         // 링크 파일 목록
                new GetProjectParametersTool(),    // 프로젝트 파라미터 목록
                new GetRevisionHistoryTool(),      // 개정 이력 조회
                new GetElementCountTool(),         // 카테고리별 요소 수 집계
                new GetScheduleDataTool(),         // 일람표 데이터 조회
                new SetElementPhaseTool(),         // 요소 단계 설정
                new CreateGroupTool(),             // 그룹 생성
                new GetRoomsInfoTool()             // 룸 상세 정보
            );
        }

        private void Register(params ToolBase[] tools)
        {
            foreach (var t in tools) _tools[t.Name] = t;
        }

        public ToolBase? Get(string name) => _tools.TryGetValue(name, out var t) ? t : null;
        public IEnumerable<ToolBase> GetAll() => _tools.Values;
    }
}
