using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class UserMapStudio
    {
        bool libraryOpen, helpOpen, objectList, mapInspector;
        bool partsMenuOpen, fileMenuOpen, easyContextOpen;
        public bool IsPartsMenuOpen => partsMenuOpen;
        public bool IsContextPanelOpen => ShowInspector;
        public Rect BuildHotbarScreenRect => new Rect(0, 0, Screen.width, BottomHeight * BellUI.Scale);

        public void SetPartsMenuOpen(bool open)
        {
            if (!CanEdit || pendingAction != null || libraryOpen || helpOpen || UserMapStudioFileDialog.IsOpen) return;
            StopPointerInteraction();
            GUIUtility.keyboardControl = 0;
            partsMenuOpen = open;
            fileMenuOpen = false;
            placementValid = false;
            if (studioCamera) UpdateCameraViewport();
        }
        Vector2 paletteScroll, inspectorScroll, libraryScroll;
        UserMapData[] library = Array.Empty<UserMapData>();
        bool[] libraryCleared = Array.Empty<bool>();
        bool showingDownloads;
        UserMapData[] downloadedLibrary = Array.Empty<UserMapData>();
        string[] downloadedHashes = Array.Empty<string>();
        string mapName = "", mapDescription = "", fallText = "-20", baselineFall = "-20", planeText = "0";
        readonly string[] transformFields = new string[9];
        readonly string[] travelFields = new string[3];
        readonly string[] inspectorBaseline = new string[14];
        string periodText = "4", speedText = "2";
        string cameraHint;
        int colorIndex;
        GUIStyle compactField;
        static readonly string[] PartNames = { "기본 발판", "경사로", "삼각 서프", "체크포인트", "이동 발판", "컨베이어", "프레스", "위험 구역", "도착 종", "AI 경로점", "아이템 박스" };
        static readonly string[] ToolNames = { "이동 W", "회전 E", "크기 R" };
        static readonly string[] PartHints = { "자유롭게 쌓는 바닥과 벽", "회전해서 올라가는 방향 바꾸기", "양쪽을 탈 수 있는 삼각 기둥", "추락 시 돌아오는 발판", "두 지점을 왕복하는 발판", "진행 방향으로 밀어주는 바닥", "위에서 내려오는 압착 장애물", "닿으면 체크포인트로 복귀", "코스마다 하나 · 선택해서 옮기기", "자세히에서 목록 순서대로 AI가 이동", "닿으면 3종 중 하나 획득 · C 사용" };
        static readonly Color[] Swatches = UserMapRuntime.PaletteColors;
        static readonly UserMapPart[] UIQuickParts = { UserMapPart.Block, UserMapPart.Ramp, UserMapPart.Checkpoint, UserMapPart.MovingPlatform, UserMapPart.ItemBox, UserMapPart.Bell };
        static readonly string[] UIQuickKeys = { "1", "2", "3", "4", "5", "6" };
        static readonly UserMapPart[] MenuParts = { UserMapPart.Block, UserMapPart.Ramp, UserMapPart.Surf, UserMapPart.Checkpoint, UserMapPart.MovingPlatform, UserMapPart.ItemBox, UserMapPart.Bell, UserMapPart.Conveyor, UserMapPart.Press, UserMapPart.Hazard, UserMapPart.Waypoint };
        static readonly int[] PartGroupStarts = { 0, 3, 7, 10, 11 };
        static readonly string[] PartGroupNames = { "길과 발판", "도움과 도착", "움직이는 장애물", "자세히 · AI 길 안내" };
        static readonly string[] BuildSizeNames = { "작게", "보통", "크게" };
        static readonly string[] MoveAxisNames = { "좌우", "위아래", "앞뒤" };
        static readonly string[] MoveDistanceNames = { "가까이", "보통", "멀리" };
        static readonly float[] MoveDistances = { 2, 4, 8 };
        static readonly string[] MoveSpeedNames = { "천천히", "보통", "빠르게" };
        static readonly float[] MovePeriods = { 8, 4, 2 };
        static readonly Color HintColor = new Color(.78f, .81f, .77f);
        const float LeftWidth = 344, RightWidth = 320, TopHeight = 116, BottomHeight = 190;
        bool ShowInspector => mapInspector || !EasyBuild || easyContextOpen && !placementMode && !EraseMode && FindObject(selectedId) != null;
        float InspectorWidth => EasyBuild && !mapInspector ? 264 : RightWidth;
        Rect ViewportRect
        {
            get
            {
                float left = partsMenuOpen ? LeftWidth + 20 : 10;
                float right = ShowInspector ? InspectorWidth + 20 : 10;
                return new Rect(left, TopHeight, BellUI.W - left - right, BellUI.H - TopHeight - BottomHeight);
            }
        }
        Rect TestHudRect => new Rect(BellUI.W * .5f - 560, 12, 1120, 148);
        Rect TestReturnRect => new Rect(BellUI.W * .5f + 280, 21, 260, 38);
        bool MapFieldsChanged => draft != null && (mapName != draft.name || mapDescription != (draft.description ?? "") || fallText != baselineFall);
        bool CanSaveFromUI => CanSaveVerified && !MapFieldsChanged && !InspectorFieldsChanged;
        string ClearanceLabel => MapFieldsChanged || InspectorFieldsChanged
            ? "미검증 · 미적용 입력이 있어요. 입력을 적용하고 클리어 검증을 진행하세요."
            : ClearVerificationStatus;

        static string PartName(UserMapPart part)
        {
            int index = (int)part;
            return index >= 0 && index < PartNames.Length ? PartNames[index] : part.ToString();
        }

        void SyncMapFields()
        {
            if (draft == null) return;
            mapName = draft.name; mapDescription = draft.description ?? ""; fallText = baselineFall = Number(draft.fallHeight);
        }

        void SyncInspectorFields()
        {
            var item = FindObject(selectedId);
            if (item == null) return;
            if (item.kind == UserMapPart.ItemBox && transformTool == TransformTool.Scale) transformTool = TransformTool.Move;
            for (int axis = 0; axis < 3; axis++)
            {
                transformFields[axis] = Number(item.position[axis]);
                transformFields[axis + 3] = Number(item.rotation[axis]);
                transformFields[axis + 6] = Number(item.scale[axis]);
                travelFields[axis] = Number(item.travel[axis]);
            }
            periodText = Number(item.period); speedText = Number(item.speed); colorIndex = item.color;
            Array.Copy(transformFields, inspectorBaseline, 9);
            Array.Copy(travelFields, 0, inspectorBaseline, 9, 3);
            inspectorBaseline[12] = periodText; inspectorBaseline[13] = speedText;
        }

        bool InspectorFieldsChanged
        {
            get
            {
                var item = FindObject(selectedId);
                if (item == null) return false;
                for (int i = 0; i < 9; i++) if (transformFields[i] != inspectorBaseline[i]) return true;
                for (int i = 0; i < 3; i++) if (travelFields[i] != inspectorBaseline[i + 9]) return true;
                return periodText != inspectorBaseline[12] || speedText != inspectorBaseline[13] || colorIndex != item.color;
            }
        }

        bool ApplyMapFields()
        {
            if (!MapFieldsChanged) return true;
            if (!TryNumber(fallText, out float fall)) { Status = "추락 높이는 올바른 숫자로 입력해 주세요."; return false; }
            return SetMapInfo(mapName.Trim(), mapDescription, fall);
        }

        bool ApplyPendingFields() => ApplyInspectorFields() && ApplyMapFields();

        bool ApplyInspectorFields()
        {
            var current = FindObject(selectedId);
            if (current == null || !InspectorFieldsChanged) return true;
            var item = CopyObject(current);
            for (int axis = 0; axis < 3; axis++)
            {
                if (!TryNumber(transformFields[axis], out float position) || !TryNumber(transformFields[axis + 3], out float rotation)
                    || !TryNumber(transformFields[axis + 6], out float scale) || !TryNumber(travelFields[axis], out float travel))
                { Status = "위치·회전·크기·이동 거리는 올바른 숫자로 입력해 주세요. 소수점은 . 을 사용해요."; return false; }
                item.position[axis] = position; item.rotation[axis] = rotation; item.scale[axis] = scale; item.travel[axis] = travel;
            }
            if (!TryNumber(periodText, out item.period) || !TryNumber(speedText, out item.speed))
            { Status = "주기와 속도는 올바른 숫자로 입력해 주세요."; return false; }
            item.color = colorIndex;
            if (!UpdateObject(item)) return false;
            Status = "개체 속성을 적용했어요.";
            return true;
        }

        void OnGUI()
        {
            if (closing || draft == null) return;
            Matrix4x4 matrix = GUI.matrix;
            Color color = GUI.color;
            Color contentColor = GUI.contentColor;
            bool enabled = GUI.enabled;
            int depth = GUI.depth;
            GUI.depth = -60;
            BellUI.Begin();
            GUI.contentColor = Color.white;
            try
            {
                if (testing)
                {
                    var panel = TestHudRect;
                    BellUI.Round(panel, BellUI.Surface, 18);
                    BellUI.Text(new Rect(panel.x + 20, panel.y + 8, 790, 36),
                        IsClearVerified ? "클리어 검증 완료 · 편집기로 돌아가 저장하세요" : "클리어 검증 진행 중 · 혼자서 출발부터 도착 종까지",
                        20, IsClearVerified ? BellUI.Cool : BellUI.Gold, true);
                    BellUI.Text(new Rect(panel.x + 20, panel.y + 46, panel.width - 40, 27), ClearVerificationStatus, 15, BellUI.Paper);
                    BellUI.Text(new Rect(panel.x + 20, panel.y + 78, panel.width - 40, 26),
                        "도착만으로는 완료되지 않아요. 종 가까이에서 E를 눌러 직접 울려야 검증돼요.", 15, BellUI.Gold);
                    BellUI.Text(new Rect(panel.x + 20, panel.y + 110, panel.width - 40, 26),
                        "실패 / R: 출발점부터 재도전 (체크포인트 없음) · Esc: 마우스 해제 · 화면 클릭: 계속 조작", 14, HintColor);
                    GUI.enabled = !busy;
                    if (BellUI.Button(TestReturnRect, "편집기로 돌아가기  [F8]", true, size: 16)) EndTest();
                    return;
                }
                var viewport = ViewportRect;
                BellUI.Round(new Rect(0, 0, BellUI.W, viewport.yMin), BellUI.Ink, 0);
                BellUI.Round(new Rect(0, viewport.yMax, BellUI.W, BellUI.H - viewport.yMax), BellUI.Ink, 0);
                BellUI.Round(new Rect(0, viewport.yMin, viewport.xMin, viewport.height), BellUI.Ink, 0);
                BellUI.Round(new Rect(viewport.xMax, viewport.yMin, BellUI.W - viewport.xMax, viewport.height), BellUI.Ink, 0);
                DrawWorldGuides();
                DrawAirBuildGuide();
                DrawFirstPersonBuildHud();
                GUI.enabled = !busy && pendingAction == null && !libraryOpen && !helpOpen && !BuildCursorCaptured;
                DrawToolbar();
                if (partsMenuOpen) { if (fileMenuOpen) DrawFileMenu(); else DrawPalette(); }
                if (ShowInspector) DrawInspector();
                DrawBuildHotbar();
                DrawStatus();
                GUI.enabled = !busy && pendingAction == null;
                if (libraryOpen) DrawLibrary();
                if (helpOpen) DrawHelp();
                if (pendingAction != null) DrawUnsaved();
                if (busy)
                {
                    BellUI.Round(new Rect(0, 0, BellUI.W, BellUI.H), new Color(0, 0, 0, .55f), 0);
                    BellUI.Text(new Rect(BellUI.W * .5f - 320, BellUI.H * .5f - 36, 640, 72), Status, 22, BellUI.Gold, true, TextAnchor.MiddleCenter);
                }
            }
            finally { GUI.enabled = enabled; GUI.matrix = matrix; GUI.color = color; GUI.contentColor = contentColor; GUI.depth = depth; }
        }

        void DrawToolbar()
        {
            float width = BellUI.W;
            cameraHint = null;
            BellUI.Round(new Rect(0, 0, width, TopHeight - 6), BellUI.Surface, 0);
            if (BellUI.Button(new Rect(18, 12, 92, 40), "파일", selected: fileMenuOpen, size: 17))
            {
                bool open = !fileMenuOpen;
                SetPartsMenuOpen(open);
                fileMenuOpen = open && partsMenuOpen;
            }
            BellUI.Text(new Rect(124, 12, 290, 24), draft.name, 18, BellUI.Paper, true);
            BellUI.Text(new Rect(124, 36, 290, 18), HasUnsavedChanges ? "저장하지 않은 변경 *" : "모든 변경 저장됨", 12, HasUnsavedChanges ? BellUI.Gold : HintColor);
            bool interactive = GUI.enabled;
            GUI.enabled = interactive && CanUndo;
            if (BellUI.Button(new Rect(428, 12, 94, 40), "실행 취소", size: 14)) { if (ApplyPendingFields()) Undo(); }
            GUI.enabled = interactive && CanRedo;
            if (BellUI.Button(new Rect(530, 12, 94, 40), "다시 실행", size: 14)) { if (ApplyPendingFields()) Redo(); }
            GUI.enabled = interactive;
            if (BellUI.Button(new Rect(width - 546, 12, 188, 40), "플레이 · 클리어 F8", true, size: 16)) BeginTest();
            GUI.enabled = interactive && CanSaveFromUI;
            if (BellUI.Button(new Rect(width - 350, 12, 174, 40), CanSaveFromUI ? "저장 Ctrl+S" : "클리어 후 저장", true, size: 15)) SaveMap();
            GUI.enabled = interactive;
            if (BellUI.Button(new Rect(width - 168, 12, 70, 40), "도움말", size: 14)) { StopPointerInteraction(); helpOpen = true; }
            if (BellUI.Button(new Rect(width - 90, 12, 72, 40), "홈으로", size: 14)) Close();
            DrawCameraToolbar();
        }

        void DrawCameraToolbar()
        {
            const float y = 64, height = 36;
            if (CameraButton(new Rect(18, y, 110, height), "1인칭 제작", "직접 돌아다니며 만들어요. Tab으로 마우스를 풀어요.", FirstPersonBuild))
                SetFirstPersonBuild(true);
            if (CameraButton(new Rect(136, y, 110, height), "전체 보기", "위에서 전체 구조를 확인하고 자유롭게 돌려 보세요.", !FirstPersonBuild))
            { SetFirstPersonBuild(false); FocusAll(); }
            if (FirstPersonBuild)
            {
                if (CameraButton(new Rect(258, y, 144, height), BuildNoclip ? "V 비행 · 노클립" : "V 걷기 · 점프", "V로 전환. 걷기는 실제 이동 물리로 간격을 확인하며 클리어 인증은 하지 않아요.", !BuildNoclip))
                    SetBuildNoclip(!BuildNoclip);
                if (CameraButton(new Rect(410, y, 146, height), AirBuild ? "Q 공중에 놓기" : "Q 붙여 놓기", "공중 배치는 휠로 거리를 조절해요. 자동으로 모드가 바뀌지 않아요.", AirBuild))
                    SetAirBuild(!AirBuild);
                BellUI.Text(new Rect(570, y, 64, height), "비행 속도", 13, HintColor);
                for (int i = 0; i < MoveSpeedNames.Length; i++)
                    if (CameraButton(new Rect(638 + i * 74, y, 68, height), MoveSpeedNames[i], "제작 비행 속도만 바뀌어요. 실제 걷기·점프 물리는 그대로예요.", BuildFlySpeedIndex == i))
                        SetBuildFlySpeed(i);
                if (CameraButton(new Rect(872, y, 86, height), "출발점", "출발 구역으로 돌아가요.")) FocusStart();
                if (CameraButton(new Rect(966, y, 138, height), "조작 시작 Tab", "Tab 또는 맵 클릭으로 조작. 메뉴를 쓰려면 다시 Tab."))
                    SetBuildCursorCaptured(true);
                BellUI.Text(new Rect(1116, y, BellUI.W - 1134, height), "F8 클리어 검증", 12, BellUI.Cool);
                return;
            }
            if (CameraButton(new Rect(258, y, 54, height), "−", "화면 축소")) ZoomCamera(-1);
            if (CameraButton(new Rect(320, y, 54, height), "+", "화면 확대")) ZoomCamera(1);
            if (CameraButton(new Rect(386, y, 64, height), "위", "위에서 보기", !AdvancedCamera && !IsOrbitView && CurrentStudioView == StudioView.Top)) SetStudioView(StudioView.Top);
            if (CameraButton(new Rect(458, y, 64, height), "사선", "사선에서 보기", !AdvancedCamera && !IsOrbitView && CurrentStudioView == StudioView.Angled)) SetStudioView(StudioView.Angled);
            if (CameraButton(new Rect(530, y, 64, height), "옆", "옆에서 높이 확인", !AdvancedCamera && !IsOrbitView && CurrentStudioView == StudioView.Side)) SetStudioView(StudioView.Side);
            if (CameraButton(new Rect(610, y, 78, height), "출발점", "출발 구역을 보여줘요.")) FocusStart();
            if (CameraButton(new Rect(696, y, 78, height), "도착 종", "도착 종을 보여줘요.")) FocusBell();
            if (CameraButton(new Rect(782, y, 78, height), "선택 F", "선택 부품을 가까이 보여줘요.")) FocusSelected();
            if (CameraButton(new Rect(868, y, 108, height), "중심 되찾기", "맵 전체로 돌아와요.")) FocusAll();
            if (CameraButton(new Rect(984, y, 146, height), AirBuild ? "Q 공중에 놓기" : "Q 붙여 놓기", "배치 방식을 선택해요.", AirBuild)) SetAirBuild(!AirBuild);
            BellUI.Text(new Rect(1142, y, BellUI.W - 1160, height), "우클릭 회전", 12, BellUI.Cool);
        }

        void DrawFirstPersonBuildHud()
        {
            if (!FirstPersonBuild || !CanNavigate) return;
            var viewport = ViewportRect;
            if (BuildCursorCaptured)
            {
                Vector2 center = viewport.center;
                BellUI.Round(new Rect(center.x - 7, center.y - 1, 14, 2), BellUI.Paper, 0);
                BellUI.Round(new Rect(center.x - 1, center.y - 7, 2, 14), BellUI.Paper, 0);
            }
            string hint = BuildCursorCaptured
                ? BuildNoclip ? "WASD 이동 · 마우스 둘러보기 · Space / Ctrl 위아래 · V 걷기 · Tab 메뉴"
                    : "WASD 이동 · Space 점프 · V 비행 · Tab 메뉴 · 점프 연습은 클리어 인증과 별개"
                : "Tab 또는 맵 클릭: 조작 시작 · 우클릭: 부품 선택 · F8: 저장을 위한 클리어 검증";
            BellUI.Text(new Rect(viewport.x + 12, viewport.y + 8, viewport.width - 24, 28), hint, 13, BellUI.Paper, shadow: true);
            BellUI.Text(new Rect(viewport.x + 12, viewport.yMax - 34, viewport.width - 24, 26),
                AirBuild ? "공중에 놓기 · 휠: 거리 조절 · 좌클릭: 설치 · Q: 붙여 놓기로 전환"
                    : "붙여 놓기 · 좌클릭: 설치 · R: 90도 회전 · Q: 공중에 놓기", 13, BellUI.Gold, shadow: true);
        }

        bool CameraButton(Rect rect, string label, string hint, bool selected = false)
        {
            if (GUI.enabled && rect.Contains(Event.current.mousePosition)) cameraHint = hint;
            return BellUI.Button(rect, label, selected: selected, size: 14);
        }

        void DrawFileMenu()
        {
            var panel = new Rect(10, TopHeight, LeftWidth, ViewportRect.height);
            BellUI.Round(panel, BellUI.Surface, 12);
            BellUI.Text(new Rect(panel.x + 16, panel.y + 10, 200, 28), "파일과 맵 설정", 20, BellUI.Gold, true);
            if (BellUI.Button(new Rect(panel.xMax - 72, panel.y + 10, 58, 28), "닫기", size: 13)) SetPartsMenuOpen(false);
            float x = panel.x + 16, y = panel.y + 50, w = panel.width - 32;
            if (BellUI.Button(new Rect(x, y, w, 44), "새 맵 만들기", size: 17)) { SetPartsMenuOpen(false); NewMap(); }
            if (BellUI.Button(new Rect(x, y + 52, w, 44), "열기 · 내 맵 / 받은 맵", size: 17)) { SetPartsMenuOpen(false); OpenLibrary(); }
            if (BellUI.Button(new Rect(x, y + 104, w, 44), "파일 가져오기…", size: 17)) { SetPartsMenuOpen(false); ImportWithPicker(); }
            bool interactive = GUI.enabled;
            GUI.enabled = interactive && CanSaveFromUI;
            if (BellUI.Button(new Rect(x, y + 156, w, 44), "파일 내보내기…", size: 17)) { SetPartsMenuOpen(false); ExportWithPicker(); }
            GUI.enabled = interactive;
            if (BellUI.Button(new Rect(x, y + 208, w, 44), "맵 이름과 설정", size: 17))
            {
                if (ApplyPendingFields())
                {
                    SetPartsMenuOpen(false); mapInspector = true; inspectorScroll = Vector2.zero;
                    if (studioCamera) UpdateCameraViewport();
                }
            }
            BellUI.Text(new Rect(x, y + 268, w, 76), "먼저 F8로 직접 클리어해 주세요.\n도착 종을 E로 울리고 돌아오면\n저장과 파일 내보내기가 열려요.", 14, HintColor);
        }

        void DrawPalette()
        {
            var panel = new Rect(10, TopHeight, LeftWidth, ViewportRect.height);
            BellUI.Round(panel, BellUI.Surface, 12);
            if (BellUI.Button(new Rect(22, TopHeight + 10, 132, 34), "모든 부품 11", selected: !objectList, size: 16)) { objectList = false; paletteScroll = Vector2.zero; }
            if (BellUI.Button(new Rect(162, TopHeight + 10, 112, 34), "개체 " + draft.objects.Length, selected: objectList, size: 15)) { objectList = true; paletteScroll = Vector2.zero; }
            if (BellUI.Button(new Rect(282, TopHeight + 10, 58, 34), "닫기", size: 13)) SetPartsMenuOpen(false);
            var view = new Rect(panel.x + 8, panel.y + 54, panel.width - 16, panel.height - 64);
            float contentHeight = objectList ? draft.objects.Length * 46 + 10 : 806;
            paletteScroll = GUI.BeginScrollView(view, paletteScroll, new Rect(0, 0, view.width - 18, contentHeight));
            float width = view.width - 22;
            if (objectList)
            {
                for (int i = 0; i < draft.objects.Length; i++)
                {
                    var item = draft.objects[i];
                    if (BellUI.Button(new Rect(2, 4 + i * 46, width, 40), (i + 1) + ". " + PartName(item.kind), selected: selectedId == item.id, size: 15))
                    {
                        if (SelectObject(item.id))
                        { SetEraseMode(false); mapInspector = false; SetPartsMenuOpen(false); }
                    }
                }
            }
            else
            {
                float y = 0, tileWidth = (width - 10) * .5f;
                for (int group = 0; group < PartGroupNames.Length; group++)
                {
                    BellUI.Text(new Rect(4, y, width - 8, 28), PartGroupNames[group], 15, BellUI.Cool, true);
                    y += 32;
                    int count = PartGroupStarts[group + 1] - PartGroupStarts[group];
                    for (int index = 0; index < count; index++)
                    {
                        var part = MenuParts[PartGroupStarts[group] + index];
                        var tile = new Rect(2 + index % 2 * (tileWidth + 10), y + index / 2 * 94, tileWidth, 86);
                        if (BellUI.Button(tile, "", selected: placementMode && palettePart == part))
                        {
                            if (part == UserMapPart.Waypoint && EasyBuild) SwitchBuildDetail();
                            if (part != UserMapPart.Waypoint || !EasyBuild) SelectBuildPart(part);
                        }
                        DrawPartIcon(new Rect(tile.center.x - 22, tile.y + 8, 44, 40), part, BellUI.Gold);
                        BellUI.Text(new Rect(tile.x + 4, tile.y + 52, tile.width - 8, 28), PartName(part), 16, BellUI.Paper, true, TextAnchor.MiddleCenter);
                        if (GUI.enabled && tile.Contains(Event.current.mousePosition)) cameraHint = PartHints[(int)part];
                    }
                    y += ((count + 1) / 2) * 94 + 4;
                }
            }
            GUI.EndScrollView();
        }

        void SwitchBuildDetail()
        {
            if (!ApplyPendingFields()) return;
            SetEasyBuild(!EasyBuild);
            mapInspector = easyContextOpen = false;
            inspectorScroll = Vector2.zero;
            if (EasyBuild && AdvancedCamera) SetAdvancedCamera(false);
            if (studioCamera) UpdateCameraViewport();
        }

        void DrawInspector()
        {
            float width = InspectorWidth, x = BellUI.W - width - 10;
            var panel = new Rect(x, TopHeight, width, ViewportRect.height);
            BellUI.Round(panel, BellUI.Surface, 12);
            if (EasyBuild)
            {
                BellUI.Text(new Rect(x + 12, panel.y + 10, width - 90, 32), mapInspector ? "맵 설정" : "선택한 부품", 18, BellUI.Gold, true);
                if (BellUI.Button(new Rect(panel.xMax - 70, panel.y + 10, 58, 32), "닫기", size: 13) && ApplyPendingFields())
                { mapInspector = easyContextOpen = false; if (studioCamera) UpdateCameraViewport(); }
            }
            else
            {
                if (BellUI.Button(new Rect(x + 12, panel.y + 10, 143, 32), "개체 속성", selected: !mapInspector, size: 15) && ApplyPendingFields())
                { mapInspector = false; inspectorScroll = Vector2.zero; }
                if (BellUI.Button(new Rect(x + 163, panel.y + 10, 145, 32), "맵 설정", selected: mapInspector, size: 15) && ApplyPendingFields())
                { mapInspector = true; inspectorScroll = Vector2.zero; }
            }
            var view = new Rect(x + 10, panel.y + 52, panel.width - 20, panel.height - 62);
            var item = FindObject(selectedId);
            float height = mapInspector ? 560 : EasyBuild ? item?.kind == UserMapPart.MovingPlatform ? 600 : 460 : item == null ? 390 : 820;
            inspectorScroll = GUI.BeginScrollView(view, inspectorScroll, new Rect(0, 0, view.width - 18, height));
            if (mapInspector) DrawMapFields(view.width - 24);
            else if (EasyBuild && item != null) DrawEasyObjectFields(item, view.width - 24);
            else if (item != null) DrawObjectFields(item, view.width - 24);
            else
            {
                BellUI.Text(new Rect(4, 6, 264, 100), "개체를 선택하면 정확한 위치·회전·크기와 장치 설정이 나와요.\n변경한 값은 ‘적용’을 눌러 주세요.", 15, BellUI.Paper);
                BellUI.Text(new Rect(4, 120, 264, 180), "W 이동 · E 회전 · R 크기\n축 손잡이를 드래그해 편집해요.\nAI 경로는 개체 목록 순서예요.\n\n우클릭 드래그: 자유롭게 둘러보기\n가운데 드래그: 화면 이동\n휠: 확대 / 축소", 14, HintColor);
                if (BellUI.Button(new Rect(4, 324, 264, 38), "맵 이름과 추락 높이 설정", size: 14)) mapInspector = true;
            }
            GUI.EndScrollView();
        }

        void DrawEasyObjectFields(UserMapObject item, float width)
        {
            BellUI.Text(new Rect(4, 0, width, 28), PartName(item.kind), 18, BellUI.Gold, true);
            BellUI.Text(new Rect(4, 32, width, 48), PartHints[(int)item.kind], 13, HintColor);
            BellUI.Text(new Rect(4, 90, width, 24), "화면 방향으로 한 칸씩", 14, BellUI.Paper, true);
            float cell = (width - 12) / 3;
            if (BellUI.Button(new Rect(4, 120, cell, 34), "화면 왼쪽", size: 12)) NudgeSelectedInView(true, -1);
            if (BellUI.Button(new Rect(10 + cell, 120, cell, 34), "화면 앞", size: 12)) NudgeSelectedInView(false, 1);
            if (BellUI.Button(new Rect(16 + cell * 2, 120, cell, 34), "높이 올리기", size: 12)) NudgeSelectedBuild(1, 1);
            if (BellUI.Button(new Rect(4, 160, cell, 34), "화면 오른쪽", size: 12)) NudgeSelectedInView(true, 1);
            if (BellUI.Button(new Rect(10 + cell, 160, cell, 34), "화면 뒤", size: 12)) NudgeSelectedInView(false, -1);
            if (BellUI.Button(new Rect(16 + cell * 2, 160, cell, 34), "높이 내리기", size: 12)) NudgeSelectedBuild(1, -1);
            BellUI.Text(new Rect(4, 212, width, 24), "색 고르기", 15, BellUI.Paper, true);
            for (int i = 0; i < Swatches.Length; i++)
            {
                var rect = new Rect(4 + i % 6 * 34, 244 + i / 6 * 36, 28, 28);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    var changed = CopyObject(item); changed.color = i; UpdateObject(changed);
                }
                BellUI.Round(rect, Swatches[i], 7);
                if (item.color == i) BellUI.Round(new Rect(rect.x - 2, rect.y - 2, 32, 32), BellUI.Paper, 8, 2);
            }
            float y = 244 + ((Swatches.Length + 5) / 6) * 36 + 14;
            if (item.kind == UserMapPart.MovingPlatform)
            {
                DrawMovePresetRow(item, width, y, "움직이는 방향", MoveAxisNames, 0); y += 68;
                DrawMovePresetRow(item, width, y, "움직이는 거리", MoveDistanceNames, 1); y += 68;
                DrawMovePresetRow(item, width, y, "움직이는 빠르기", MoveSpeedNames, 2); y += 68;
            }
            else if (item.kind == UserMapPart.Bell)
            {
                BellUI.Text(new Rect(4, y, width, 58), "종도 위 버튼으로 옮길 수 있어요.\n하나뿐인 종은 지우거나 복제하지 않아요.", 14, HintColor); y += 70;
            }
            if (BellUI.Button(new Rect(4, y, width, 36), "자세히 · 수치와 장치 설정", size: 14)) SwitchBuildDetail();
        }

        void NudgeSelectedInView(bool horizontal, int direction)
        {
            if (!studioCamera) return;
            Vector3 basis = horizontal ? studioCamera.transform.right : studioCamera.transform.forward;
            basis.y = 0;
            if (basis.sqrMagnitude < .0001f)
            {
                basis = studioCamera.transform.up;
                basis.y = 0;
            }
            float x = Mathf.Abs(basis.x), z = Mathf.Abs(basis.z);
            int axis = Mathf.Approximately(x, z) ? horizontal ? 0 : 2 : x > z ? 0 : 2;
            NudgeSelectedBuild(axis, direction * (basis[axis] >= 0 ? 1 : -1));
        }

        public bool NudgeSelectedBuild(int axis, int direction)
        {
            if (!CanNavigate || !EasyBuild || placementMode || EraseMode || axis < 0 || axis > 2
                || direction != -1 && direction != 1 || !ApplyPendingFields()) return false;
            var item = FindObject(selectedId);
            if (item == null) return false;
            StopPointerInteraction();
            var changed = CopyObject(item);
            changed.position[axis] += direction * (snap ? snapSize : .5f);
            if (!UpdateObject(changed)) return false;
            Status = PartName(item.kind) + "을 한 칸 옮겼어요. 실행 취소로 되돌릴 수 있어요.";
            return true;
        }

        void DrawMovePresetRow(UserMapObject item, float width, float y, string label, string[] names, int setting)
        {
            BellUI.Text(new Rect(4, y, width, 24), label, 14, BellUI.Paper, true);
            int axis = Mathf.Abs(item.travel.x) >= Mathf.Abs(item.travel.y) && Mathf.Abs(item.travel.x) >= Mathf.Abs(item.travel.z) ? 0
                : Mathf.Abs(item.travel.y) >= Mathf.Abs(item.travel.z) ? 1 : 2;
            float distance = item.travel.magnitude, cell = (width - 12) / 3;
            for (int i = 0; i < 3; i++)
            {
                bool selected = setting == 0 ? axis == i && distance > 0 : setting == 1 ? Mathf.Approximately(distance, MoveDistances[i]) : Mathf.Approximately(item.period, MovePeriods[i]);
                if (!BellUI.Button(new Rect(4 + i * (cell + 6), y + 28, cell, 32), names[i], selected: selected, size: 12)) continue;
                var changed = CopyObject(item);
                if (setting == 2) changed.period = MovePeriods[i];
                else
                {
                    changed.travel = Vector3.zero;
                    changed.travel[setting == 0 ? i : axis] = setting == 0 ? Mathf.Max(2, distance) : MoveDistances[i];
                }
                UpdateObject(changed);
            }
        }

        void DrawMapFields(float width)
        {
            BellUI.Text(new Rect(4, 0, width, 26), "맵 이름", 14, BellUI.Paper, true);
            mapName = Field(new Rect(4, 30, width, 38), mapName, "Name", 48);
            BellUI.Text(new Rect(4, 82, width, 26), "설명", 14, BellUI.Paper, true);
            mapDescription = Field(new Rect(4, 112, width, 72), mapDescription, "Description", 256, true);
            BellUI.Text(new Rect(4, 198, width, 26), "추락 판정 높이 (Y)", 14, BellUI.Paper, true);
            fallText = Field(new Rect(4, 228, width, 34), fallText, "Fall", 12);
            if (BellUI.Button(new Rect(4, 278, width, 38), "맵 설정 적용", true, size: 15)) ApplyPendingFields();
            BellUI.Text(new Rect(4, 332, width, 100), "F8 클리어 검증을 마쳐야 저장·내보내기가 가능해요. 수정하면 다시 검증해야 해요.\n저장한 맵은 방 만들기의 ‘내 맵’에서 고를 수 있고 참가자에게 자동 전달돼요.", 14, HintColor);
            BellUI.Text(new Rect(4, 442, width, 108), "검증은 혼자 출발부터 종까지 이동해 E로 종을 울리는 모드예요. 실패하거나 R을 누르면 체크포인트 없이 처음부터 시작해요.\n실제 방의 할리갈리 규칙은 그대로예요.", 13, BellUI.Cool);
        }

        void DrawObjectFields(UserMapObject item, float width)
        {
            BellUI.Text(new Rect(4, 0, width, 28), PartName(item.kind), 19, BellUI.Gold, true);
            bool fixedScale = item.kind == UserMapPart.ItemBox;
            BellUI.Text(new Rect(4, 31, width, 42), fixedScale ? "위치는 바닥 기준 · 닿으면 획득 · C 사용\n미사일·바나나·바리게이트가 나와요."
                : item.kind == UserMapPart.Bell || item.kind == UserMapPart.Press ? "위치는 바닥 높이 기준이에요."
                : item.kind == UserMapPart.Waypoint ? "발이 닿을 위치예요. 목록 순서가 AI 경로예요." : "위치는 중심, 크기는 실제 가로·높이·깊이예요.", 13, HintColor);
            DrawVectorFields("위치", transformFields, 0, 84, width, "Position");
            DrawVectorFields("회전 (도)", transformFields, 3, 160, width, "Rotation", fixedScale);
            bool scaleEditable = GUI.enabled;
            GUI.enabled = scaleEditable && !fixedScale;
            DrawVectorFields(fixedScale ? "크기 배율 (1 × 1 × 1 고정)" : "크기 (m)", transformFields, 6, 236, width, "Scale");
            GUI.enabled = scaleEditable;
            if (YawOnly(item.kind)) BellUI.Text(new Rect(4, 306, width, 26), "이 부품은 Y 회전만 지원해요 (X/Z=0).", 12, HintColor);
            BellUI.Text(new Rect(4, 338, width, 24), "색상", 14, BellUI.Paper, true);
            for (int i = 0; i < Swatches.Length; i++)
            {
                var rect = new Rect(4 + i * 34, 368, 28, 28);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) colorIndex = i;
                BellUI.Round(rect, Swatches[i], 7);
                if (colorIndex == i) BellUI.Round(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), BellUI.Paper, 8, 3);
            }
            float y = 414;
            if (item.kind == UserMapPart.MovingPlatform)
            {
                DrawVectorFields("왕복 이동 거리 (XYZ)", travelFields, 0, y, width, "Travel"); y += 80;
            }
            if (item.kind == UserMapPart.MovingPlatform || item.kind == UserMapPart.Press)
            {
                BellUI.Text(new Rect(4, y, 130, 32), "왕복 주기 (초)", 14, BellUI.Paper, true);
                periodText = Field(new Rect(142, y, width - 138, 32), periodText, "Period", 12); y += 46;
            }
            if (item.kind == UserMapPart.Conveyor)
            {
                BellUI.Text(new Rect(4, y, 130, 32), "밀기 속도 (m/s)", 14, BellUI.Paper, true);
                speedText = Field(new Rect(142, y, width - 138, 32), speedText, "Speed", 12); y += 46;
                BellUI.Text(new Rect(4, y, width, 38), "+Z 방향으로 밀어요. 음수는 반대 방향이에요.", 13, HintColor); y += 46;
            }
            if (BellUI.Button(new Rect(4, y, width, 38), "수치 / 색상 적용", true, size: 15)) ApplyInspectorFields(); y += 48;
            if (BellUI.Button(new Rect(4, y, width * .5f - 4, 34), "선택 보기 F", size: 13)) FocusSelected();
            if (BellUI.Button(new Rect(width * .5f + 6, y, width * .5f - 2, 34), "입력 되돌리기", size: 12)) SyncInspectorFields(); y += 44;
            bool interactive = GUI.enabled;
            GUI.enabled = interactive && item.kind != UserMapPart.Bell;
            if (BellUI.Button(new Rect(4, y, width * .5f - 4, 34), "복제 Ctrl+D", size: 13)) DuplicateSelected();
            if (BellUI.Button(new Rect(width * .5f + 6, y, width * .5f - 2, 34), "삭제 Del", size: 13)) DeleteSelected();
            GUI.enabled = interactive; y += 44;
            if (BellUI.Button(new Rect(4, y, width * .5f - 4, 34), "목록 위로", size: 13)) MoveSelectedInList(-1);
            if (BellUI.Button(new Rect(width * .5f + 6, y, width * .5f - 2, 34), "목록 아래로", size: 13)) MoveSelectedInList(1); y += 43;
            BellUI.Text(new Rect(4, y, width, 80), fixedScale
                ? "클릭은 선택만, 두 번 클릭은 가까이 보기.\n이동·Y 회전은 축 손잡이를 드래그해요.\n크기는 고정이며 바닥 기준으로 배치해요.\n빈 바닥 드래그: 화면 이동 · 격자 G · 취소 Esc"
                : "클릭은 선택만, 두 번 클릭은 가까이 보기.\n이동·회전·크기는 축 손잡이를 드래그해요.\n개체 자체를 드래그해도 바뀌지 않아요.\n빈 바닥 드래그: 화면 이동 · 격자 G · 취소 Esc", 13, HintColor);
        }

        void DrawVectorFields(string label, string[] fields, int offset, float y, float width, string prefix, bool yawOnly = false)
        {
            BellUI.Text(new Rect(4, y, width, 24), label, 14, BellUI.Paper, true);
            float cell = (width - 12) / 3;
            bool interactive = GUI.enabled;
            for (int i = 0; i < 3; i++)
            {
                float x = 4 + i * (cell + 6);
                BellUI.Text(new Rect(x, y + 30, 15, 32), i == 0 ? "X" : i == 1 ? "Y" : "Z", 13, AxisColors[i], true);
                GUI.enabled = interactive && (!yawOnly || i == 1);
                fields[offset + i] = Field(new Rect(x + 17, y + 30, cell - 17, 32), fields[offset + i] ?? "", prefix + i, 12);
            }
            GUI.enabled = interactive;
        }

        string Field(Rect rect, string value, string name, int max, bool multiline = false)
        {
            if (compactField == null)
            {
                compactField = new GUIStyle(BellUI.Style(16)) { padding = new RectOffset(7, 7, 5, 4), alignment = TextAnchor.UpperLeft, wordWrap = false };
                compactField.normal.textColor = compactField.hover.textColor = compactField.active.textColor = compactField.focused.textColor = BellUI.Paper;
            }
            BellUI.Round(rect, new Color(.05f, .055f, .047f), 7);
            bool focused = GUI.GetNameOfFocusedControl() == "Studio" + name;
            BellUI.Round(rect, focused ? BellUI.Gold : new Color(.46f, .49f, .45f), 7, focused ? 2 : 1);
            GUI.SetNextControlName("Studio" + name);
            compactField.wordWrap = multiline;
            return multiline ? GUI.TextArea(rect, value ?? "", max, compactField) : GUI.TextField(rect, value ?? "", max, compactField);
        }

        void DrawBuildHotbar()
        {
            float y = BellUI.H - BottomHeight;
            BellUI.Round(new Rect(0, y, BellUI.W, BottomHeight), BellUI.Surface, 0);
            if (EasyBuild) DrawEasyTools(y + 8); else DrawAdvancedTools(y + 8);
            var all = new Rect(18, y + 54, 158, 72);
            if (BellUI.Button(all, "", true, partsMenuOpen && !fileMenuOpen))
            {
                bool open = !partsMenuOpen || fileMenuOpen;
                SetPartsMenuOpen(open);
                objectList = false;
            }
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 3; col++)
                    BellUI.Round(new Rect(all.center.x - 23 + col * 18, all.y + 10 + row * 14, 12, 10), BellUI.Ink, 2);
            BellUI.Text(new Rect(all.x + 4, all.y + 42, all.width - 8, 26), "모든 부품 11", 18, BellUI.Ink, true, TextAnchor.MiddleCenter);
            float width = (BellUI.W - 248) / 6;
            for (int i = 0; i < UIQuickParts.Length; i++)
            {
                var part = UIQuickParts[i];
                var rect = new Rect(190 + i * (width + 8), y + 54, width, 72);
                bool selected = placementMode && !EraseMode && palettePart == part;
                if (BellUI.Button(rect, "", selected: selected)) SelectBuildPart(part);
                BellUI.Text(new Rect(rect.x + 10, rect.y + 6, 24, 24), UIQuickKeys[i], 18, selected ? BellUI.Gold : HintColor, true);
                DrawPartIcon(new Rect(rect.center.x - 20, rect.y + 5, 40, 34), part, BellUI.Gold);
                BellUI.Text(new Rect(rect.x + 4, rect.y + 42, rect.width - 8, 26), PartName(part), 17, BellUI.Paper, true, TextAnchor.MiddleCenter);
                if (GUI.enabled && rect.Contains(Event.current.mousePosition)) cameraHint = PartHints[(int)part];
            }
        }

        void DrawEasyTools(float y)
        {
            bool interactive = GUI.enabled;
            var item = FindObject(selectedId);
            if (BellUI.Button(new Rect(18, y, 78, 36), "선택", selected: !placementMode && !EraseMode, size: 16))
            { SetEraseMode(false); StopPointerInteraction(); placementMode = false; GUIUtility.keyboardControl = 0; }
            if (BellUI.Button(new Rect(104, y, 78, 36), "짓기", selected: placementMode && !EraseMode, size: 16))
                SelectBuildPart(item != null && !placementMode ? item.kind : palettePart);
            if (BellUI.Button(new Rect(190, y, 78, 36), "지우개", selected: EraseMode, size: 16)) SetEraseMode(!EraseMode);
            BellUI.Text(new Rect(286, y, 36, 36), "크기", 14, HintColor);
            bool fixedSize = (placementMode ? palettePart : item?.kind) == UserMapPart.ItemBox;
            GUI.enabled = interactive && !EraseMode && (placementMode || item != null) && !fixedSize;
            for (int i = 0; i < 3; i++)
                if (BellUI.Button(new Rect(328 + i * 68, y, 60, 36), BuildSizeNames[i], selected: !fixedSize && (placementMode ? BuildSizeIndex == i : item != null && item.scale == BuildScale(item.kind, i)), size: 14)) SetBuildSize(i);
            GUI.enabled = interactive && !EraseMode && (placementMode || item != null);
            if (BellUI.Button(new Rect(540, y, 106, 36), "90도 회전", size: 15)) RotateBuild90();
            GUI.enabled = interactive && item != null && !placementMode && !EraseMode && item.kind != UserMapPart.Bell;
            if (BellUI.Button(new Rect(658, y, 96, 36), "복제", size: 15)) DuplicateSelected();
            if (BellUI.Button(new Rect(762, y, 84, 36), "삭제", size: 15)) DeleteSelected();
            GUI.enabled = interactive && item != null && !placementMode && !EraseMode;
            if (BellUI.Button(new Rect(858, y, 116, 36), item?.kind == UserMapPart.MovingPlatform ? "옮기기 / 장치" : "옮기기 / 색", selected: easyContextOpen && !mapInspector, size: 14))
            {
                if (ApplyPendingFields())
                {
                    easyContextOpen = !easyContextOpen; mapInspector = false; inspectorScroll = Vector2.zero;
                    if (studioCamera) UpdateCameraViewport();
                }
            }
            GUI.enabled = interactive;
            BellUI.Text(new Rect(986, y, BellUI.W - 1132, 36), fixedSize ? "크기 고정" : EraseMode ? "클릭해서 지우기" : placementMode ? AirBuild ? "공중에 놓아요" : "면에 붙여요" : "클릭해서 선택", 12, HintColor);
            if (BellUI.Button(new Rect(BellUI.W - 132, y, 114, 36), "자세히", size: 16)) SwitchBuildDetail();
        }

        void DrawAdvancedTools(float y)
        {
            bool interactive = GUI.enabled;
            var item = FindObject(selectedId);
            bool fixedScale = item?.kind == UserMapPart.ItemBox;
            for (int i = 0; i < 3; i++)
            {
                GUI.enabled = interactive && !(fixedScale && i == (int)TransformTool.Scale);
                if (BellUI.Button(new Rect(18 + i * 88, y, 80, 36), fixedScale && i == (int)TransformTool.Scale ? "크기 고정" : ToolNames[i],
                    selected: !placementMode && transformTool == (TransformTool)i, size: 14))
                { StopPointerInteraction(); placementMode = false; transformTool = (TransformTool)i; }
            }
            GUI.enabled = interactive;
            if (BellUI.Button(new Rect(292, y, 118, 36), snap ? "격자 맞춤 ON" : "격자 맞춤 OFF", selected: snap, size: 13)) snap = !snap;
            if (BellUI.Button(new Rect(418, y, 64, 36), Number(snapSize) + " m", size: 14)) snapSize = snapSize < .5f ? .5f : snapSize < 1 ? 1 : snapSize < 2 ? 2 : .25f;
            BellUI.Text(new Rect(494, y, 90, 36), "빈 공간 높이", 13, BellUI.Paper);
            planeText = Field(new Rect(590, y, 64, 36), planeText, "Plane", 8);
            if (TryNumber(planeText, out float height)) placementHeight = Mathf.Clamp(height, -99, 299);
            if (BellUI.Button(new Rect(670, y, 150, 36), AdvancedCamera ? "고급 비행 끄기" : "고급 비행 켜기", selected: AdvancedCamera, size: 14))
                SetAdvancedCamera(!AdvancedCamera);
            GUI.enabled = interactive && item != null && item.kind != UserMapPart.Bell;
            if (BellUI.Button(new Rect(836, y, 96, 36), "복제 Ctrl+D", size: 13)) DuplicateSelected();
            if (BellUI.Button(new Rect(940, y, 88, 36), "삭제 Del", size: 13)) { if (ApplyInspectorFields()) DeleteSelected(); }
            GUI.enabled = interactive;
            if (BellUI.Button(new Rect(BellUI.W - 132, y, 114, 36), "쉬운 만들기", true, size: 15)) SwitchBuildDetail();
        }

        static void DrawPartIcon(Rect rect, UserMapPart part, Color color)
        {
            switch (part)
            {
                case UserMapPart.Block:
                case UserMapPart.ItemBox:
                    IconLine(rect, .12f, .28f, .58f, .08f, color); IconLine(rect, .58f, .08f, .9f, .3f, color);
                    IconLine(rect, .9f, .3f, .44f, .5f, color); IconLine(rect, .44f, .5f, .12f, .28f, color);
                    IconLine(rect, .12f, .28f, .12f, .72f, color); IconLine(rect, .12f, .72f, .44f, .94f, color);
                    IconLine(rect, .44f, .5f, .44f, .94f, color); IconLine(rect, .44f, .94f, .9f, .74f, color);
                    IconLine(rect, .9f, .3f, .9f, .74f, color);
                    if (part == UserMapPart.ItemBox) BellUI.Text(new Rect(rect.x + rect.width * .55f, rect.y + rect.height * .38f, rect.width * .32f, rect.height * .48f), "?", 16, color, true, TextAnchor.MiddleCenter);
                    break;
                case UserMapPart.Ramp:
                    IconLine(rect, .1f, .82f, .88f, .82f, color); IconLine(rect, .88f, .82f, .88f, .14f, color);
                    IconLine(rect, .88f, .14f, .1f, .82f, color); IconLine(rect, .16f, .88f, .94f, .88f, color);
                    break;
                case UserMapPart.Surf:
                    IconLine(rect, .06f, .84f, .5f, .12f, color); IconLine(rect, .5f, .12f, .94f, .84f, color);
                    IconLine(rect, .94f, .84f, .06f, .84f, color); IconLine(rect, .5f, .12f, .5f, .68f, color);
                    break;
                case UserMapPart.Checkpoint:
                    IconLine(rect, .1f, .9f, .9f, .9f, color); IconLine(rect, .3f, .86f, .3f, .08f, color);
                    IconLine(rect, .3f, .1f, .84f, .22f, color); IconLine(rect, .84f, .22f, .3f, .45f, color);
                    break;
                case UserMapPart.MovingPlatform:
                    BellUI.Round(new Rect(rect.x + rect.width * .23f, rect.y + rect.height * .58f, rect.width * .54f, rect.height * .22f), color, 3);
                    IconLine(rect, .08f, .27f, .92f, .27f, color); IconLine(rect, .08f, .27f, .23f, .1f, color);
                    IconLine(rect, .08f, .27f, .23f, .44f, color); IconLine(rect, .92f, .27f, .77f, .1f, color);
                    IconLine(rect, .92f, .27f, .77f, .44f, color);
                    break;
                case UserMapPart.Conveyor:
                    BellUI.Round(new Rect(rect.x + rect.width * .06f, rect.y + rect.height * .63f, rect.width * .88f, rect.height * .25f), color, 4, 2);
                    IconLine(rect, .16f, .34f, .84f, .34f, color); IconLine(rect, .84f, .34f, .62f, .12f, color);
                    IconLine(rect, .84f, .34f, .62f, .55f, color);
                    break;
                case UserMapPart.Press:
                    IconLine(rect, .12f, .08f, .12f, .9f, color); IconLine(rect, .88f, .08f, .88f, .9f, color);
                    IconLine(rect, .12f, .08f, .88f, .08f, color); IconLine(rect, .5f, .08f, .5f, .4f, color);
                    BellUI.Round(new Rect(rect.x + rect.width * .28f, rect.y + rect.height * .4f, rect.width * .44f, rect.height * .2f), color, 2);
                    IconLine(rect, .28f, .9f, .72f, .9f, color);
                    break;
                case UserMapPart.Hazard:
                    IconLine(rect, .08f, .86f, .5f, .08f, color); IconLine(rect, .5f, .08f, .92f, .86f, color);
                    IconLine(rect, .92f, .86f, .08f, .86f, color); IconLine(rect, .5f, .34f, .5f, .57f, color);
                    BellUI.Round(new Rect(rect.center.x - 2, rect.y + rect.height * .69f, 4, 4), color, 2);
                    break;
                case UserMapPart.Bell:
                    BellUI.Round(new Rect(rect.x + rect.width * .2f, rect.y + rect.height * .22f, rect.width * .6f, rect.height * .58f), color, 12, 3);
                    IconLine(rect, .1f, .8f, .9f, .8f, color); IconLine(rect, .5f, .08f, .5f, .22f, color);
                    BellUI.Round(new Rect(rect.center.x - 3, rect.y + rect.height * .84f, 6, 5), color, 3);
                    break;
                case UserMapPart.Waypoint:
                    IconLine(rect, .18f, .78f, .48f, .28f, color); IconLine(rect, .48f, .28f, .82f, .64f, color);
                    BellUI.Round(new Rect(rect.x + rect.width * .08f, rect.y + rect.height * .68f, 9, 9), color, 5);
                    BellUI.Round(new Rect(rect.x + rect.width * .38f, rect.y + rect.height * .18f, 9, 9), color, 5);
                    BellUI.Round(new Rect(rect.x + rect.width * .72f, rect.y + rect.height * .54f, 9, 9), color, 5);
                    break;
            }
        }

        static readonly System.Collections.Generic.Dictionary<(int, int, bool), Texture2D> iconLines
            = new System.Collections.Generic.Dictionary<(int, int, bool), Texture2D>();

        static void IconLine(Rect rect, float x1, float y1, float x2, float y2, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            int width = Mathf.RoundToInt(Mathf.Abs(x2 - x1) * rect.width) + 4;
            int height = Mathf.RoundToInt(Mathf.Abs(y2 - y1) * rect.height) + 4;
            bool rising = (x2 - x1) * (y2 - y1) < 0;
            var key = (width, height, rising);
            if (!iconLines.TryGetValue(key, out var texture))
            {
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color[width * height];
                Vector2 start = new Vector2(2, rising ? height - 2 : 2), end = new Vector2(width - 2, rising ? 2 : height - 2);
                Vector2 direction = end - start;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        Vector2 point = new Vector2(x + .5f, y + .5f);
                        float t = Mathf.Clamp01(Vector2.Dot(point - start, direction) / Mathf.Max(1, direction.sqrMagnitude));
                        float alpha = Mathf.Clamp01(2 - Vector2.Distance(point, start + direction * t));
                        pixels[(height - 1 - y) * width + x] = new Color(1, 1, 1, alpha);
                    }
                texture.SetPixels(pixels); texture.Apply(false, true);
                iconLines.Add(key, texture);
            }
            // Axis-aligned textured strokes respect scroll-view clipping without rotating GUI.matrix.
            Color previous = GUI.color; GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x + Mathf.Min(x1, x2) * rect.width - 2,
                rect.y + Mathf.Min(y1, y2) * rect.height - 2, width, height), texture);
            GUI.color = previous;
        }

        void DrawStatus()
        {
            float y = BellUI.H - BottomHeight + 132;
            BellUI.Text(new Rect(18, y, BellUI.W - 36, 24), ClearanceLabel, 13, CanSaveFromUI ? BellUI.Cool : BellUI.Gold, true);
            BellUI.Text(new Rect(18, y + 26, BellUI.W - 36, 24), cameraHint ?? Status, 13, cameraHint == null ? BellUI.Paper : BellUI.Cool);
        }

        public void OpenLibrary()
        {
            if (!CanEdit) return;
            StopCameraLook(); RefreshLibrary(); libraryOpen = true;
        }

        void RefreshLibrary()
        {
            try
            {
                library = UserMapStorage.List();
                libraryCleared = new bool[library.Length];
                for (int i = 0; i < library.Length; i++) libraryCleared[i] = UserMapClearance.IsCleared(library[i]);
            }
            catch (Exception e) { library = Array.Empty<UserMapData>(); libraryCleared = Array.Empty<bool>(); Status = e.Message; }
            try
            {
                downloadedLibrary = UserMapDownloads.List();
                downloadedHashes = new string[downloadedLibrary.Length];
                for (int i = 0; i < downloadedLibrary.Length; i++)
                    downloadedHashes[i] = UserMapStorage.Hash(UserMapStorage.Serialize(downloadedLibrary[i]));
            }
            catch (Exception e) { downloadedLibrary = Array.Empty<UserMapData>(); downloadedHashes = Array.Empty<string>(); Status = e.Message; }
        }

        void DrawLibrary()
        {
            BellUI.Round(new Rect(0, 0, BellUI.W, BellUI.H), new Color(0, 0, 0, .7f), 0);
            var panel = new Rect((BellUI.W - 880) * .5f, (BellUI.H - 568) * .5f, 880, 568);
            BellUI.Round(panel, BellUI.Surface, 20);
            BellUI.Text(new Rect(panel.x + 24, panel.y + 18, 520, 38), "내 맵 · .bellmap 파일 보관함", 25, BellUI.Gold, true);
            if (BellUI.Button(new Rect(panel.xMax - 104, panel.y + 22, 80, 34), "닫기", size: 14)) libraryOpen = false;
            if (BellUI.Button(new Rect(panel.x + 24, panel.y + 64, 160, 32), "내가 만든 맵", !showingDownloads, size: 14))
            { showingDownloads = false; libraryScroll = Vector2.zero; }
            if (BellUI.Button(new Rect(panel.x + 194, panel.y + 64, 160, 32), "받은 맵 · 원본", showingDownloads, size: 14))
            { showingDownloads = true; libraryScroll = Vector2.zero; }
            BellUI.Text(new Rect(panel.x + 24, panel.y + 104, 832, 44), showingDownloads
                ? "파일이나 방에서 받은 원본은 방에서 바로 선택하고 다시 공유할 수 있어요. 직접 클리어 인증은 아니에요.\n‘사본 편집’은 새 미검증 초안을 열어요. 편집 사본을 저장·내보내려면 직접 클리어해야 해요."
                : "직접 만든 맵은 혼자 클리어한 뒤 저장·내보내기할 수 있어요. 수정하면 다시 검증해야 해요.\n파일이나 방에서 받은 원본은 ‘받은 맵’에 따로 보관되고 방에서 바로 선택할 수 있어요.", 14, HintColor);
            var view = new Rect(panel.x + 20, panel.y + 154, 840, 276);
            int count = showingDownloads ? downloadedLibrary.Length : library.Length;
            libraryScroll = GUI.BeginScrollView(view, libraryScroll, new Rect(0, 0, 816, Mathf.Max(256, count * 70)));
            if (showingDownloads)
            {
                if (downloadedLibrary.Length == 0) BellUI.Text(new Rect(24, 45, 768, 100), "받은 맵이 아직 없어요.\n친구의 .bellmap 파일을 가져오거나 사용자 맵 방에 참가하세요.\n같은 원본은 다시 받지 않고, 다른 버전은 따로 보관해요.", 17, HintColor, align: TextAnchor.MiddleCenter);
                for (int i = 0; i < downloadedLibrary.Length; i++)
                {
                    var map = downloadedLibrary[i]; string hash = downloadedHashes[i]; float y = i * 70;
                    BellUI.Round(new Rect(4, y, 802, 61), new Color(.16f, .17f, .15f), 12);
                    BellUI.Text(new Rect(18, y + 5, 555, 27), map.name, 18, BellUI.Paper, true);
                    BellUI.Text(new Rect(18, y + 32, 555, 23), "받은 원본 · 방 선택 가능 · " + map.objects.Length + "개 부품 · 버전 " + hash.Substring(0, 12), 12, BellUI.Cool);
                    if (BellUI.Button(new Rect(594, y + 13, 88, 34), "사본 편집", size: 13)) OpenDownloadedMap(hash);
                    if (BellUI.Button(new Rect(692, y + 13, 102, 34), "원본 저장", true, size: 13)) ExportDownloadedWithPicker(hash, map.name);
                }
            }
            else
            {
                if (library.Length == 0) BellUI.Text(new Rect(24, 45, 768, 100), "저장한 맵이 아직 없어요.\n편집 중인 맵을 클리어 검증한 뒤 저장하세요.\n친구에게 받은 원본은 ‘받은 맵’에서 확인할 수 있어요.", 17, HintColor, align: TextAnchor.MiddleCenter);
                for (int i = 0; i < library.Length; i++)
                {
                    var map = library[i]; float y = i * 70;
                    BellUI.Round(new Rect(4, y, 802, 61), new Color(.16f, .17f, .15f), 12);
                    BellUI.Text(new Rect(18, y + 5, 555, 27), map.name, 18, BellUI.Paper, true);
                    BellUI.Text(new Rect(18, y + 32, 555, 23), (libraryCleared[i] ? "검증 완료" : "미검증") + " · " + map.objects.Length + "개 부품 · " + (map.description ?? ""), 12, libraryCleared[i] ? BellUI.Cool : HintColor);
                    if (BellUI.Button(new Rect(594, y + 13, 88, 34), libraryCleared[i] ? "열기" : "열고 검증", true, size: 13)) OpenMap(map);
                    bool interactive = GUI.enabled;
                    GUI.enabled = interactive && libraryCleared[i];
                    if (BellUI.Button(new Rect(692, y + 13, 102, 34), "파일로 저장", size: 13)) ExportLibraryMap(map);
                    GUI.enabled = interactive;
                }
            }
            GUI.EndScrollView();
            if (BellUI.Button(new Rect(panel.x + 24, panel.y + 444, 170, 38), "파일 가져오기…", true, size: 15)) ImportWithPicker();
            if (BellUI.Button(new Rect(panel.x + 204, panel.y + 444, 110, 38), "새로고침", size: 14)) RefreshLibrary();
            BellUI.Text(new Rect(panel.x + 24, panel.y + 489, 832, 34), Status, 12, BellUI.Gold);
            BellUI.Text(new Rect(panel.x + 24, panel.y + 527, 832, 25), showingDownloads ? UserMapDownloads.DirectoryPath : UserMapStorage.DirectoryPath, 12, HintColor);
        }

        void DrawUnsaved()
        {
            GUI.enabled = !busy;
            BellUI.Round(new Rect(0, 0, BellUI.W, BellUI.H), new Color(0, 0, 0, .78f), 0);
            float x = (BellUI.W - 660) * .5f, y = (BellUI.H - 300) * .5f;
            BellUI.Round(new Rect(x, y, 660, 300), BellUI.Surface, 20);
            BellUI.Text(new Rect(x + 26, y + 21, 608, 37), "저장하지 않은 변경이 있어요", 24, BellUI.Gold, true);
            BellUI.Text(new Rect(x + 26, y + 70, 608, 100), CanSaveFromUI
                ? pendingLabel + " 전에 검증한 현재 맵을 저장할까요?\n‘변경 버리기’를 선택하면 저장하지 않은 편집은 사라져요."
                : pendingLabel + " 전에 저장하려면 먼저 클리어 검증이 필요해요.\n‘계속 편집’으로 돌아가 입력을 적용하고 F8로 검증하세요.\n또는 ‘변경 버리기’로 저장하지 않고 계속할 수 있어요.", 16, BellUI.Paper);
            bool interactive = GUI.enabled;
            GUI.enabled = interactive && CanSaveFromUI;
            if (BellUI.Button(new Rect(x + 26, y + 194, 192, 43), "저장 후 계속", true, size: 16)) ConfirmPending(true);
            GUI.enabled = interactive;
            if (BellUI.Button(new Rect(x + 234, y + 194, 192, 43), "변경 버리기", size: 16)) ConfirmPending(false);
            if (BellUI.Button(new Rect(x + 442, y + 194, 192, 43), "계속 편집", size: 16)) CancelPending();
            BellUI.Text(new Rect(x + 26, y + 249, 608, 37), Status, 12, BellUI.Gold);
        }

        void DrawHelp()
        {
            BellUI.Round(new Rect(0, 0, BellUI.W, BellUI.H), new Color(0, 0, 0, .75f), 0);
            float x = (BellUI.W - 900) * .5f, y = (BellUI.H - 640) * .5f;
            BellUI.Round(new Rect(x, y, 900, 640), BellUI.Surface, 20);
            BellUI.Text(new Rect(x + 28, y + 20, 844, 38), "맵 만들기 빠른 안내", 26, BellUI.Gold, true);
            BellUI.Text(new Rect(x + 28, y + 74, 844, 480),
                "1. 아래 큰 부품 버튼이나 숫자 1~6을 눌러 고르고, 붙일 면을 클릭해요.\n" +
                "   노란 미리보기는 놓을 수 있는 곳, 빨강 미리보기는 놓을 수 없는 곳이에요.\n" +
                "2. 작게 / 보통 / 크게로 크기를 고르고 ‘90도 회전’으로 방향을 바꿔요.\n" +
                "   ‘모든 부품 11’에는 경사로·삼각 서프·움직이는 장애물도 있어요.\n" +
                "3. ‘선택’으로 부품을 고르면 크기 변경, 회전, 복제, 삭제를 할 수 있어요.\n" +
                "   ‘옮기기 / 색’의 여섯 방향 버튼으로 종과 부품을 옮겨요. ‘옮기기 / 장치’에서 왕복 설정도 해요.\n" +
                "4. ‘지우개’로 불필요한 부품을 클릭해 지워요. 실수했다면 실행 취소 Ctrl+Z!\n" +
                "5. 기본은 1인칭 제작! Tab / 맵 클릭으로 조작하고, Tab / Esc로 마우스를 풀어요.\n" +
                "   WASD 이동 · 마우스 둘러보기 · 비행 중 Space / Ctrl로 위아래 이동해요.\n" +
                "   V로 비행 / 걷기 전환. 걷기에서는 Space로 실제 점프 간격을 확인해요.\n" +
                "6. Q로 붙여 놓기 / 공중에 놓기 전환. 공중에서는 휠로 배치 거리를 조절해요.\n" +
                "   전체 보기는 우클릭 회전 / 가운데 드래그 이동. 자세히에서 수치를 입력해요.\n" +
                "7. F8 플레이: 혼자 출발부터 도착 종까지 가서 E로 직접 종을 울리세요.\n" +
                "   실패 / R은 체크포인트 없이 처음부터. F8로 돌아온 뒤 저장하세요.\n" +
                "8. 파일 메뉴에서 새 맵, 열기, 가져오기 / 내보내기, 맵 설정을 찾아요.\n" +
                "   수정하면 다시 클리어해야 저장돼요. 받은 원본은 방에서 바로 공유할 수 있어요.\n\n" +
                "출발 책상은 보호돼요. 도착 종은 하나만 있어요. 편집한 사본은 직접 클리어해야 해요.\n" +
                "실제 방의 할리갈리 규칙은 그대로이며, 사용자 맵은 참가자에게 자동 공유돼요.",
                16, BellUI.Paper);
            if (BellUI.Button(new Rect(x + 330, y + 578, 240, 42), "만들러 가기", true, size: 17)) helpOpen = false;
        }

        void ImportWithPicker()
        {
            if (!CanEdit) return;
            try
            {
                string path = UserMapStudioFileDialog.OpenMapFile(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                if (!string.IsNullOrEmpty(path)) ImportMap(path);
            }
            catch (Exception e) { Status = e.Message; }
        }

        void ExportWithPicker()
        {
            if (!CanEdit) return;
            if (!CanSaveFromUI) { Status = "내보내려면 입력을 적용하고 F8 클리어 검증부터 완료해 주세요."; return; }
            try
            {
                string path = UserMapStudioFileDialog.SaveMapFile(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), draft.name);
                if (!string.IsNullOrEmpty(path)) ExportMap(path);
            }
            catch (Exception e) { Status = e.Message; }
        }

        void ExportDownloadedWithPicker(string hash, string name)
        {
            if (!CanEdit) return;
            try
            {
                string path = UserMapStudioFileDialog.SaveMapFile(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), name);
                if (!string.IsNullOrEmpty(path)) ExportDownloadedMap(hash, path);
            }
            catch (Exception e) { Status = e.Message; }
        }

        void ExportLibraryMap(UserMapData map)
        {
            if (!CanEdit) return;
            try
            {
                if (!UserMapClearance.IsCleared(map))
                {
                    Status = "이 맵은 미검증 상태예요. ‘열고 검증’으로 직접 클리어한 뒤 내보내세요.";
                    return;
                }
                string path = UserMapStudioFileDialog.SaveMapFile(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), map.name);
                if (string.IsNullOrEmpty(path)) return;
                UserMapStorage.Export(map, path);
                Status = "내보냈어요: " + path;
            }
            catch (Exception e) { Status = e.Message; }
        }
    }
}
