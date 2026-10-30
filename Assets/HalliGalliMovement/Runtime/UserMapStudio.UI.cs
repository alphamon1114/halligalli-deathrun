using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class UserMapStudio
    {
        bool libraryOpen, helpOpen, objectList, mapInspector;
        bool partsMenuOpen;
        public bool IsPartsMenuOpen => partsMenuOpen;

        public void SetPartsMenuOpen(bool open)
        {
            if (!CanEdit || pendingAction != null || libraryOpen || helpOpen || UserMapStudioFileDialog.IsOpen) return;
            StopPointerInteraction();
            GUIUtility.keyboardControl = 0;
            partsMenuOpen = open;
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
        static readonly string[] PartHints = { "자유롭게 쌓는 바닥과 벽", "+Z 방향으로 올라가는 경사", "양쪽을 탈 수 있는 삼각 기둥", "추락 시 돌아오는 발판", "두 지점을 왕복하는 발판", "진행 방향으로 밀어주는 바닥", "위에서 내려오는 압착 장애물", "닿으면 체크포인트로 복귀", "코스마다 하나 · 손잡이로 이동", "목록 순서대로 AI가 이동", "닿으면 5종 중 하나 획득 · C 사용" };
        static readonly Color[] Swatches = UserMapRuntime.PaletteColors;
        static readonly Color HintColor = new Color(.78f, .81f, .77f);
        const float LeftWidth = 228, RightWidth = 320, TopHeight = 178, BottomHeight = 98;
        Rect ViewportRect => new Rect(partsMenuOpen ? LeftWidth + 10 : 10, TopHeight,
            BellUI.W - (partsMenuOpen ? LeftWidth : 0) - RightWidth - 30, BellUI.H - TopHeight - BottomHeight);
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
                GUI.enabled = !busy && pendingAction == null && !libraryOpen && !helpOpen;
                DrawToolbar();
                if (partsMenuOpen) DrawPalette();
                DrawInspector();
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
            BellUI.Round(new Rect(0, 0, width, TopHeight - 10), BellUI.Surface, 0);
            if (BellUI.Button(new Rect(18, 76, 218, 34), partsMenuOpen ? "부품 메뉴 닫기" : "부품 메뉴", true, selected: partsMenuOpen, size: 18))
                SetPartsMenuOpen(!partsMenuOpen);
            for (int line = 0; line < 3; line++)
                BellUI.Round(new Rect(32, 85 + line * 6, 18, 3), BellUI.Ink, 0);
            BellUI.Text(new Rect(18, 48, 218, 22), HasUnsavedChanges ? "저장하지 않은 변경 있음 *" : "모든 변경 저장됨", 13, HasUnsavedChanges ? BellUI.Gold : HintColor);
            float x = 245;
            bool interactive = GUI.enabled;
            if (BellUI.Button(new Rect(x, 18, 68, 40), "새 맵", size: 15)) NewMap(); x += 76;
            if (BellUI.Button(new Rect(x, 18, 76, 40), "내 맵", size: 15)) OpenLibrary(); x += 84;
            GUI.enabled = interactive && CanSaveFromUI;
            if (BellUI.Button(new Rect(x, 18, 68, 40), "저장", true, size: 15)) SaveMap(); x += 76;
            GUI.enabled = interactive;
            if (BellUI.Button(new Rect(x, 18, 92, 40), "가져오기", size: 15)) ImportWithPicker(); x += 100;
            GUI.enabled = interactive && CanSaveFromUI;
            if (BellUI.Button(new Rect(x, 18, 92, 40), "내보내기", size: 15)) ExportWithPicker(); x += 102;
            GUI.enabled = interactive && CanUndo;
            if (BellUI.Button(new Rect(x, 18, 66, 40), "실행 취소", size: 13)) Undo(); x += 73;
            GUI.enabled = interactive && CanRedo;
            if (BellUI.Button(new Rect(x, 18, 66, 40), "다시 실행", size: 13)) Redo();
            GUI.enabled = interactive;
            if (BellUI.Button(new Rect(width - 294, 18, 142, 40), "클리어 검증 F8", true, size: 15)) BeginTest();
            if (BellUI.Button(new Rect(width - 144, 18, 54, 40), "도움말", size: 13)) helpOpen = true;
            if (BellUI.Button(new Rect(width - 82, 18, 64, 40), "홈으로", size: 14)) Close();
            if (BellUI.Button(new Rect(18, 12, 218, 30), "선택 / 배치 취소", selected: !placementMode, size: 14)) placementMode = false;
            bool fixedScale = FindObject(selectedId)?.kind == UserMapPart.ItemBox;
            for (int i = 0; i < 3; i++)
            {
                GUI.enabled = interactive && !(fixedScale && i == (int)TransformTool.Scale);
                if (BellUI.Button(new Rect(244 + i * 87, 76, 80, 32), fixedScale && i == (int)TransformTool.Scale ? "크기 고정" : ToolNames[i],
                    selected: !placementMode && transformTool == (TransformTool)i, size: 14))
                { placementMode = false; transformTool = (TransformTool)i; }
            }
            GUI.enabled = interactive;
            if (BellUI.Button(new Rect(517, 76, 110, 32), snap ? "격자 맞춤 ON" : "격자 맞춤 OFF", selected: snap, size: 13)) snap = !snap;
            if (BellUI.Button(new Rect(635, 76, 65, 32), Number(snapSize) + " m", size: 14)) snapSize = snapSize < .5f ? .5f : snapSize < 1 ? 1 : snapSize < 2 ? 2 : .25f;
            BellUI.Text(new Rect(714, 76, 84, 32), "빈 공간 높이", 13, BellUI.Paper);
            planeText = Field(new Rect(798, 76, 62, 32), planeText, "Plane", 8);
            if (TryNumber(planeText, out float height)) placementHeight = Mathf.Clamp(height, -99, 299);
            BellUI.Text(new Rect(width - 390, 76, 372, 32), "개체 클릭은 선택만 · 편집은 손잡이로", 13, HintColor, align: TextAnchor.MiddleRight);
            DrawCameraToolbar();
        }

        void DrawCameraToolbar()
        {
            const float y = 120, height = 40;
            BellUI.Text(new Rect(18, y, 60, height), "카메라", 14, BellUI.Cool, true);
            if (CameraButton(new Rect(84, y, 88, height), "위에서", "선택 개체를 중심에 두고 위에서 봐요. 선택이 없으면 현재 중심을 유지해요.", !AdvancedCamera && CurrentStudioView == StudioView.Top))
                SetStudioView(StudioView.Top);
            if (CameraButton(new Rect(180, y, 88, height), "사선", "선택 중심을 유지하며 입체적으로 봐요. 빈 바닥 드래그로 화면을 이동해요.", !AdvancedCamera && CurrentStudioView == StudioView.Angled))
                SetStudioView(StudioView.Angled);
            if (CameraButton(new Rect(276, y, 88, height), "옆에서", "선택 중심을 유지하며 옆에서 높이를 확인해요. 개체 이동은 손잡이로 해요.", !AdvancedCamera && CurrentStudioView == StudioView.Side))
                SetStudioView(StudioView.Side);
            if (CameraButton(new Rect(390, y, 80, height), "− 축소", "카메라만 멀어져요. 휠 아래로도 축소할 수 있고, 비행 속도는 바뀌지 않아요."))
                ZoomCamera(-1);
            if (CameraButton(new Rect(478, y, 80, height), "+ 확대", "카메라만 가까워져요. 휠 위로도 확대할 수 있고, 비행 속도는 바뀌지 않아요."))
                ZoomCamera(1);
            BellUI.Text(new Rect(582, y, 40, height), "초점", 14, BellUI.Paper);
            if (CameraButton(new Rect(630, y, 92, height), "전체 맵", "출발점과 도착 종을 포함해 맵 전체가 보이도록 맞춰요. 맵 내용은 바뀌지 않아요."))
                FocusAll();
            bool interactive = GUI.enabled;
            GUI.enabled = interactive && FindObject(selectedId) != null;
            if (CameraButton(new Rect(730, y, 90, height), "선택 F", "선택 개체를 화면 중심에 맞춰요. F 키나 개체 두 번 클릭으로도 볼 수 있어요."))
                FocusSelected();
            GUI.enabled = interactive;
            if (CameraButton(new Rect(828, y, 88, height), "출발점", "출발 구역을 화면 중심에 맞춰요. 개체 선택과 맵 내용은 유지돼요."))
                FocusStart();
            if (CameraButton(new Rect(924, y, 88, height), "도착 종", "도착 종을 화면 중심에 맞춰요. 개체 선택과 맵 내용은 유지돼요."))
                FocusBell();
            if (CameraButton(new Rect(BellUI.W - 232, y, 214, height), AdvancedCamera ? "고급 비행 ON · 끄기" : "고급 비행 OFF · 켜기",
                AdvancedCamera ? "끄면 쉬운 카메라로 돌아가요. 비행 중에도 휠과 − / +는 항상 확대·축소예요."
                    : "고급 사용자 전용: 켜야 우클릭 시점 회전과 WASD / Q·E 비행을 사용할 수 있어요.", AdvancedCamera))
                SetAdvancedCamera(!AdvancedCamera);
        }

        bool CameraButton(Rect rect, string label, string hint, bool selected = false)
        {
            if (GUI.enabled && rect.Contains(Event.current.mousePosition)) cameraHint = hint;
            return BellUI.Button(rect, label, selected: selected, size: 14);
        }

        void DrawPalette()
        {
            var panel = new Rect(10, TopHeight, LeftWidth, BellUI.H - TopHeight - BottomHeight);
            BellUI.Round(panel, BellUI.Surface, 12);
            if (BellUI.Button(new Rect(20, TopHeight + 10, 98, 32), "부품", selected: !objectList, size: 15)) objectList = false;
            if (BellUI.Button(new Rect(124, TopHeight + 10, 104, 32), "개체 " + draft.objects.Length, selected: objectList, size: 15)) objectList = true;
            float contentHeight = objectList ? draft.objects.Length * 42 + 14 : PartNames.Length * 51 + 12;
            var view = new Rect(panel.x + 6, panel.y + 52, panel.width - 12, panel.height - 62);
            paletteScroll = GUI.BeginScrollView(view, paletteScroll, new Rect(0, 0, view.width - 18, contentHeight));
            if (objectList)
            {
                for (int i = 0; i < draft.objects.Length; i++)
                {
                    var item = draft.objects[i];
                    if (BellUI.Button(new Rect(4, 4 + i * 42, view.width - 26, 36), (i + 1) + ". " + PartName(item.kind), selected: selectedId == item.id, size: 14))
                    { SelectObject(item.id); mapInspector = false; SetPartsMenuOpen(false); }
                }
            }
            else
                for (int i = 0; i < PartNames.Length; i++)
                {
                    float y = i * 51 + 4;
                    if (BellUI.Button(new Rect(4, y, view.width - 26, 45), "", selected: placementMode && (int)palettePart == i)) ChoosePalette((UserMapPart)i);
                    BellUI.Text(new Rect(14, y + 3, view.width - 46, 21), PartNames[i], 15, BellUI.Paper, true);
                    BellUI.Text(new Rect(14, y + 24, view.width - 46, 17), PartHints[i], 11, HintColor);
                }
            GUI.EndScrollView();
        }

        void DrawInspector()
        {
            float x = BellUI.W - RightWidth - 10;
            var panel = new Rect(x, TopHeight, RightWidth, BellUI.H - TopHeight - BottomHeight);
            BellUI.Round(panel, BellUI.Surface, 12);
            if (BellUI.Button(new Rect(x + 12, panel.y + 10, 143, 32), "개체 속성", selected: !mapInspector, size: 15)) mapInspector = false;
            if (BellUI.Button(new Rect(x + 163, panel.y + 10, 145, 32), "맵 설정", selected: mapInspector, size: 15)) mapInspector = true;
            var view = new Rect(x + 10, panel.y + 52, panel.width - 20, panel.height - 62);
            var item = FindObject(selectedId);
            float height = mapInspector ? 560 : item == null ? 390 : 820;
            inspectorScroll = GUI.BeginScrollView(view, inspectorScroll, new Rect(0, 0, view.width - 18, height));
            if (mapInspector) DrawMapFields(view.width - 24);
            else if (item != null) DrawObjectFields(item, view.width - 24);
            else
            {
                BellUI.Text(new Rect(4, 6, 264, 82), "한 번 클릭: 개체 선택만\n두 번 클릭: 개체 가까이 보기\n부품 메뉴에서 목록을 열 수 있어요.", 15, BellUI.Paper);
                BellUI.Text(new Rect(4, 100, 264, 230), "빈 바닥을 드래그하면 화면 이동,\n휠과 − / +는 항상 확대·축소예요.\n개체 이동은 선택 후 손잡이로 해요.\n\n부품을 고르고 바닥을 클릭해 배치해요.\n시작 책상과 출발 구역은 보호돼요.\n코스는 Z=0 앞쪽에 만들어 주세요.\n종은 코스마다 하나만 있어요.", 14, HintColor);
                if (BellUI.Button(new Rect(4, 344, 264, 34), "맵 이름과 추락 높이 설정", size: 14)) mapInspector = true;
            }
            GUI.EndScrollView();
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
            BellUI.Text(new Rect(4, 31, width, 42), fixedScale ? "위치는 바닥 기준 · 닿으면 획득 · C 사용\n공식 레이스와 같은 5종 아이템이에요."
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

        void DrawStatus()
        {
            float y = BellUI.H - BottomHeight + 8;
            BellUI.Round(new Rect(0, y - 5, BellUI.W, BottomHeight), BellUI.Surface, 0);
            BellUI.Text(new Rect(18, y, BellUI.W - 36, 26), ClearanceLabel, 14, CanSaveFromUI ? BellUI.Cool : BellUI.Gold, true);
            BellUI.Text(new Rect(18, y + 28, BellUI.W - 36, 26), cameraHint ?? Status, 13, cameraHint == null ? BellUI.Paper : BellUI.Cool);
            BellUI.Text(new Rect(18, y + 57, BellUI.W - 36, 22), AdvancedCamera
                ? "고급 비행 ON · 우클릭+마우스: 시점 · WASD/QE: 이동 · Shift: 빠르게 · 휠/−/+: 항상 줌 · 개체 편집: 손잡이 · F8 클리어 검증"
                : "빈 바닥 드래그: 화면 이동 · 클릭: 선택만 · 두 번 클릭/F: 선택 보기 · 손잡이: 개체 편집 · 휠/−/+: 항상 줌 · 부품 선택 후 클릭: 배치 · F8 클리어 검증",
                13, HintColor);
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
                "1. 기본은 위에서 보기예요. 빈 바닥을 드래그하면 지도처럼 화면이 움직여요.\n" +
                "   휠과 상단 − / +는 언제나 축소 / 확대예요. 손을 떼면 바로 멈춰요.\n" +
                "2. 한 번 클릭은 선택만, 두 번 클릭이나 F는 선택 개체를 가까이 보여줘요.\n" +
                "   위 / 사선 / 옆은 선택 중심을 유지해요. 전체 맵 / 선택 / 출발점 / 도착 종으로 초점을 맞춰요.\n" +
                "3. 왼쪽 위 부품 메뉴를 열어 부품을 고르면 메뉴가 닫혀요. 코스를 클릭해 배치해요.\n" +
                "4. 개체 선택 후 W 이동 / E 회전 / R 크기. 축 손잡이만 드래그해요.\n" +
                "   개체 자체를 드래그해도 바뀌지 않아요. G는 격자 맞춤, Esc는 편집 취소예요.\n" +
                "5. 오른쪽에서 정확한 XYZ, 색, 장치 값을 입력하고 ‘적용’을 눌러요.\n" +
                "6. ‘고급 비행’을 켠 경우에만 우클릭+마우스로 둘러보고 WASD 이동, Q/E 상하 이동.\n" +
                "   Shift는 빠르게, 휠은 항상 줌이에요. 고급 비행을 끄면 쉬운 카메라로 돌아와요.\n" +
                "7. Ctrl+D 복제, Delete 삭제, Ctrl+Z 취소, Ctrl+Y 다시 실행.\n" +
                "8. F8 클리어 검증: 혼자 출발부터 이동해 도착 종을 E로 울리세요.\n" +
                "   실패 / R은 체크포인트 없이 처음부터. Esc 마우스 해제, F8 편집 복귀.\n" +
                "9. 검증 완료 후 Ctrl+S 저장 / ‘내보내기’. 수정하면 다시 검증해야 해요.\n" +
                "10. 가져온 원본은 ‘받은 맵’에 보관돼 방에서 바로 선택·공유할 수 있어요.\n" +
                "   편집용 사본은 새 미검증 초안이에요. 저장·내보내려면 직접 클리어하세요.\n\n" +
                "출발 책상은 보호돼요. 종은 정확히 하나, AI 경로점은 개체 목록 순서예요.\n" +
                "실제 방에서는 원래 할리갈리 규칙을 그대로 사용하고 맵이 자동 공유돼요.",
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
