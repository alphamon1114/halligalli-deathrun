using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class UserMapStudio
    {
        public bool EasyBuild { get; private set; } = true;
        public bool EraseMode { get; private set; }
        public bool AirBuild { get; private set; }
        public float AirBuildDistance { get; private set; } = 8;
        public int BuildSizeIndex { get; private set; } = 1;
        public bool BuildPreviewAvailable => placementMode && placementAvailable;
        public bool BuildPreviewValid => placementMode && placementAvailable && placementValid;
        public Vector3 BuildPreviewPosition => placementPoint;
        public Vector3 BuildPreviewRotation => placementTemplate != null ? placementTemplate.rotation : Vector3.zero;
        public Vector3 BuildPreviewScale => placementTemplate != null ? placementTemplate.scale : Vector3.zero;
        public string BuildPreviewError => buildPreviewError;

        bool placementAvailable, hasBuildRay, placementSurfaceValid;
        float buildYaw;
        Ray buildRay;
        string buildPreviewError;
        public bool AirBuildGuideActive => AirBuild && BuildPreviewAvailable && CanNavigate && !EraseMode
            && !BuildPointerBusy && (FirstPersonBuild && BuildCursorCaptured || ViewportRect.Contains(BellUI.MousePoint));
        public bool AirBuildGuideHasGround { get; private set; }
        public Vector3 AirBuildGuideBottom { get; private set; }
        public Vector3 AirBuildGuideReferencePoint { get; private set; }
        public float AirBuildGuideHeight => AirBuildGuideBottom.y - AirBuildGuideReferencePoint.y;
        public string AirBuildGuideLabel { get; private set; } = "";
        int airGuideLabelHeight, airGuideLabelDistance;
        bool airGuideLabelGround;

        bool BuildPointerBusy => looking && !(FirstPersonBuild && BuildCursorCaptured)
            || orbiting || middlePanning || panning;

        public void SetAirBuild(bool enabled)
        {
            if (!CanNavigate || AirBuild == enabled || !ApplyPendingFields()) return;
            if (dragging) CancelDrag();
            AirBuild = enabled;
            RefreshBuildPreview();
            Status = enabled ? "공중 배치 · 조준한 거리의 0.5m 격자에 놓아요. 휠로 거리 조절 · Q로 표면 배치"
                : "표면 배치 · 윗면이나 옆면에 붙이세요. Q로 공중 배치";
            GUIUtility.keyboardControl = 0;
        }

        public void AdjustAirBuildDistance(float delta)
        {
            if (!CanNavigate || !AirBuild || float.IsNaN(delta) || float.IsInfinity(delta)) return;
            float distance = Mathf.Clamp(AirBuildDistance + delta, 2, 60);
            if (distance == AirBuildDistance) return;
            AirBuildDistance = distance;
            RefreshBuildPreview();
        }

        public void SetEasyBuild(bool enabled)
        {
            if (!CanNavigate || EasyBuild == enabled || !ApplyPendingFields()) return;
            StopPointerInteraction();
            EasyBuild = enabled;
            EraseMode = false;
            placementAvailable = hasBuildRay = false;
            if (placementMode) CreatePlacementTemplate();
            GUIUtility.keyboardControl = 0;
        }

        public void SelectBuildPart(UserMapPart part)
        {
            if (!CanNavigate || (int)part < 0 || (int)part > (int)UserMapPart.ItemBox || !ApplyInspectorFields()) return;
            StopPointerInteraction();
            SetPartsMenuOpen(false);
            palettePart = part;
            placementMode = true;
            EraseMode = false;
            placementAvailable = hasBuildRay = false;
            CreatePlacementTemplate();
            Status = PartName(part) + (AirBuild ? " · 공중에 클릭해 놓으세요. 휠로 거리 조절 · Q로 표면 배치"
                : EasyBuild ? " · 윗면이나 옆면을 클릭해 붙이세요. R로 돌리기 · Esc로 선택"
                : " · 바닥/발판 위를 클릭해 배치하세요. 빈 공간은 배치 높이 평면을 사용해요.");
            GUIUtility.keyboardControl = 0;
        }

        void CreatePlacementTemplate()
        {
            if (!EasyBuild && palettePart == UserMapPart.Bell)
                foreach (var existing in draft.objects)
                    if (existing.kind == UserMapPart.Bell) { placementTemplate = CopyObject(existing); return; }
            placementTemplate = DefaultObject(palettePart, Vector3.zero);
            if (!EasyBuild) return;
            placementTemplate.scale = BuildScale(palettePart, BuildSizeIndex);
            placementTemplate.rotation = new Vector3(0, buildYaw, 0);
        }

        static Vector3 BuildScale(UserMapPart part, int index)
        {
            if (part == UserMapPart.ItemBox) return Vector3.one;
            float factor = index == 0 ? .5f : index == 2 ? 2 : 1;
            Vector3 medium = part == UserMapPart.Block ? Vector3.one * 2
                : part == UserMapPart.Ramp ? new Vector3(2, 2, 4) : DefaultScale(part);
            return medium * factor;
        }

        static bool FloorAnchored(UserMapPart part) => part == UserMapPart.Bell || part == UserMapPart.Press
            || part == UserMapPart.Waypoint || part == UserMapPart.ItemBox;

        public void SetBuildSize(int index)
        {
            if (!CanNavigate || !EasyBuild || index < 0 || index > 2 || !ApplyInspectorFields()) return;
            if (!placementMode)
            {
                var selected = FindObject(selectedId);
                if (selected != null)
                {
                    if (selected.kind == UserMapPart.ItemBox)
                    { Status = "아이템 박스 크기는 (1, 1, 1)로 고정되어 있어요."; return; }
                    var resized = CopyObject(selected);
                    resized.scale = BuildScale(selected.kind, index);
                    if (!FloorAnchored(selected.kind))
                    {
                        Vector3 localUp = Quaternion.Inverse(Quaternion.Euler(selected.rotation)) * Vector3.up;
                        resized.position.y += ProjectedExtent(resized.scale * .5f, localUp) - ProjectedExtent(selected.scale * .5f, localUp);
                    }
                    if (!UpdateObject(resized)) return;
                }
            }
            BuildSizeIndex = index;
            if (placementTemplate != null) placementTemplate.scale = BuildScale(palettePart, index);
            RefreshBuildPreview();
            GUIUtility.keyboardControl = 0;
        }

        public void RotateBuild90()
        {
            if (!CanNavigate || !(EasyBuild || FirstPersonBuild) || !ApplyInspectorFields()) return;
            if (placementMode)
            {
                buildYaw = Mathf.Repeat(placementTemplate.rotation.y + 90, 360);
                Vector3 rotation = placementTemplate.rotation;
                rotation.y = buildYaw;
                placementTemplate.rotation = rotation;
                RefreshBuildPreview();
            }
            else
            {
                var selected = FindObject(selectedId);
                if (selected == null) return;
                var rotated = CopyObject(selected);
                rotated.rotation.y = Mathf.Repeat(rotated.rotation.y + 90, 360);
                if (!UpdateObject(rotated)) return;
            }
            GUIUtility.keyboardControl = 0;
        }

        public void SetEraseMode(bool enabled)
        {
            if (!CanNavigate || !ApplyInspectorFields()) return;
            StopPointerInteraction();
            EraseMode = enabled;
            if (enabled) placementMode = false;
            placementAvailable = hasBuildRay = false;
            if (enabled) Status = "지우기 · 지울 부품을 클릭하세요. 출발 구역과 마지막 종은 보호돼요. Ctrl+Z로 되돌리기";
            GUIUtility.keyboardControl = 0;
        }

        // The viewport and diagnostics both use this pick path; a miss never deletes the old selection.
        public bool EraseAtRay(Ray ray)
        {
            if (!CanNavigate || !EraseMode || BuildPointerBusy) return false;
            string id = ObjectAt(ray);
            if (id == null) { Status = "직접 만든 부품만 지울 수 있어요. 출발 구역은 보호돼요."; return false; }
            if (!SelectObject(id) || !DeleteSelected()) return false;
            Status = "부품을 지웠어요. Ctrl+Z로 되돌릴 수 있어요.";
            return true;
        }

        void RefreshBuildPreview()
        {
            if (placementMode && hasBuildRay) PreviewBuild(buildRay);
            else placementValid = false;
        }

        // No draft clones, temporary arrays, or exceptions are needed while the pointer moves.
        public bool PreviewBuild(Ray ray)
        {
            placementValid = placementAvailable = false;
            buildPreviewError = null;
            if (!CanNavigate || !placementMode || EraseMode || placementTemplate == null || BuildPointerBusy) return false;
            buildRay = ray;
            hasBuildRay = true;
            var item = placementTemplate;
            Vector3 point;
            if (AirBuild)
            {
                // This is the authored origin, not a contact point: floor-anchored parts keep their
                // bottom origin, while centered parts keep their center. Surfaces never change it.
                point = ray.GetPoint(AirBuildDistance);
                point = new Vector3(Snap(point.x, .5f), Snap(point.y, .5f), Snap(point.z, .5f));
                placementSurfaceValid = true;
            }
            else
            {
                Vector3 normal = Vector3.up;
                if (Physics.Raycast(ray, out var hit, studioCamera.farClipPlane, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    point = hit.point;
                    normal = hit.normal;
                }
                else
                {
                    var plane = new Plane(Vector3.up, new Vector3(0, placementHeight, 0));
                    if (!plane.Raycast(ray, out float distance)) return false;
                    point = ray.GetPoint(distance);
                }
                bool anchored = FloorAnchored(item.kind);
                placementSurfaceValid = !EasyBuild || !anchored || normal.y >= .7f;
                if (EasyBuild && !anchored)
                {
                    // Snap only tangents, then restore the face plane and offset by the rotated extent.
                    Vector3 snapped = new Vector3(Snap(point.x, snapSize), Snap(point.y, snapSize), Snap(point.z, snapSize));
                    snapped -= normal * Vector3.Dot(snapped - point, normal);
                    Vector3 localNormal = Quaternion.Inverse(Quaternion.Euler(item.rotation)) * normal;
                    point = snapped + normal * ProjectedExtent(item.scale * .5f, localNormal);
                }
                else
                {
                    if (EasyBuild || snap)
                    {
                        point.x = Snap(point.x, snapSize);
                        point.z = Snap(point.z, snapSize);
                    }
                    if (!anchored) point.y += item.scale.y * .5f;
                    else if (item.kind == UserMapPart.Waypoint) point.y += .025f;
                }
            }
            placementPoint = point;
            item.position = point;
            placementAvailable = true;
            placementValid = ValidateBuildPreview();
            if (AirBuild) UpdateAirBuildGuide();
            return placementValid;
        }

        bool ValidateBuildPreview()
        {
            if (!placementSurfaceValid)
            { buildPreviewError = "이 부품은 바닥이나 발판의 윗면에 놓아 주세요."; return false; }
            if (!UserMapStorage.TryValidateObject(placementTemplate, draft.fallHeight, out buildPreviewError)) return false;
            bool existingBell = false;
            if (palettePart == UserMapPart.Bell)
                foreach (var item in draft.objects)
                    if (item.kind == UserMapPart.Bell) { existingBell = true; break; }
            if (EasyBuild && existingBell)
            { buildPreviewError = "종은 맵마다 하나예요. 기존 종을 선택해서 옮겨 주세요."; return false; }
            if (draft.objects.Length >= UserMapStorage.MaxObjects && !existingBell)
            { buildPreviewError = "맵은 1 ~ 512개의 부품을 포함해야 합니다."; return false; }
            return true;
        }

        public string PlaceBuildPreview()
        {
            if (!CanNavigate || !placementMode || EraseMode || !placementAvailable || placementTemplate == null
                || BuildPointerBusy || !ApplyInspectorFields()) return null;
            placementValid = ValidateBuildPreview();
            if (!placementValid) { Status = buildPreviewError; return null; }
            var next = UserMapStorage.Clone(draft);
            var part = CopyObject(placementTemplate);
            int existingBell = -1;
            if (!EasyBuild && part.kind == UserMapPart.Bell)
                for (int i = 0; i < next.objects.Length; i++)
                    if (next.objects[i].kind == UserMapPart.Bell) { existingBell = i; break; }
            if (existingBell >= 0)
            {
                part.id = next.objects[existingBell].id;
                next.objects[existingBell] = part;
            }
            else
            {
                part.id = Guid.NewGuid().ToString("N");
                var objects = new UserMapObject[next.objects.Length + 1];
                Array.Copy(next.objects, objects, next.objects.Length);
                objects[objects.Length - 1] = part;
                next.objects = objects;
            }
            if (!Commit(next))
            {
                buildPreviewError = Status;
                placementValid = false;
                return null;
            }
            selectedId = part.id;
            RefreshSelectionRoot();
            SyncInspectorFields();
            Status = PartName(palettePart) + (AirBuild
                ? " 공중 배치 완료 · 계속 클릭해서 놓기 · 휠로 거리 조절 · Q로 표면 배치"
                : " 배치 완료 · 계속 클릭해서 붙이기 · R로 돌리기 · Esc로 선택");
            return part.id;
        }

        void UpdateAirBuildGuide()
        {
            var item = placementTemplate;
            Vector3 bottom = placementPoint;
            // Bell, press and item-box bounds start at their authored floor origin. Waypoint
            // origins denote feet, but their editor marker (and its wire preview) is centered.
            if (!FloorAnchored(item.kind) || item.kind == UserMapPart.Waypoint)
            {
                Vector3 localUp = Quaternion.Inverse(Quaternion.Euler(item.rotation)) * Vector3.up;
                bottom.y -= ProjectedExtent(item.scale * .5f, localUp);
            }
            AirBuildGuideBottom = bottom;
            // The preview is wire-only. The editor pawn uses Ignore Raycast, excluded by this mask.
            AirBuildGuideHasGround = Physics.Raycast(bottom, Vector3.down, out var hit, Mathf.Infinity,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            AirBuildGuideReferencePoint = AirBuildGuideHasGround ? hit.point : new Vector3(bottom.x, 0, bottom.z);
            int height = Mathf.RoundToInt(AirBuildGuideHeight * 10);
            int distance = Mathf.RoundToInt(AirBuildDistance * 10);
            if (AirBuildGuideLabel.Length != 0 && height == airGuideLabelHeight
                && distance == airGuideLabelDistance && AirBuildGuideHasGround == airGuideLabelGround) return;
            airGuideLabelHeight = height;
            airGuideLabelDistance = distance;
            airGuideLabelGround = AirBuildGuideHasGround;
            string heightText = Number(height * .1f), distanceText = Number(distance * .1f);
            AirBuildGuideLabel = AirBuildGuideHasGround ? "바닥까지 " + heightText + "m · 거리 " + distanceText + "m"
                : "바닥 없음 · Y0 기준 " + heightText + "m\n거리 " + distanceText + "m";
        }

        // Called by the studio HUD inside BellUI.Begin, so projection and clipping use logical pixels.
        public void DrawAirBuildGuide()
        {
            if (!AirBuildGuideActive || Event.current == null || Event.current.type != EventType.Repaint) return;
            Rect viewport = ViewportRect;
            if (viewport.width < 32 || viewport.height < 60) return;
            bool lineVisible = ProjectAirBuildGuideLine(viewport, out Vector2 start, out Vector2 end);
            bool labelVisible = Project(placementPoint, out Vector2 labelAnchor) && viewport.Contains(labelAnchor)
                && Vector3.Dot(placementPoint - studioCamera.transform.position, studioCamera.transform.forward) < studioCamera.farClipPlane;
            if (!lineVisible && !labelVisible) return;
            Color color = !placementValid ? BellUI.Danger : AirBuildGuideHasGround ? BellUI.Cool : BellUI.Gold;
            if (lineVisible)
            {
                Vector2 direction = end - start;
                float length = direction.magnitude;
                if (length > .001f) direction /= length;
                for (float distance = 0; distance <= length; distance += 9)
                {
                    Vector2 dot = start + direction * distance;
                    BellUI.Round(new Rect(dot.x - 1.5f, dot.y - 1.5f, 3, 3), color, 1.5f);
                }
            }
            if (!labelVisible) labelAnchor = start;
            float width = Mathf.Min(328, viewport.width - 16);
            var label = new Rect(
                Mathf.Clamp(labelAnchor.x + 18, viewport.xMin + 8, viewport.xMax - width - 8),
                Mathf.Clamp(labelAnchor.y + 38, viewport.yMin + 8, viewport.yMax - 52), width, 44);
            BellUI.Round(label, BellUI.Ink, 6);
            BellUI.Text(new Rect(label.x + 8, label.y, label.width - 16, label.height), AirBuildGuideLabel, 13, color);
        }

        bool ProjectAirBuildGuideLine(Rect viewport, out Vector2 start, out Vector2 end)
        {
            start = end = Vector2.zero;
            Vector3 origin = AirBuildGuideBottom;
            Vector3 delta = AirBuildGuideReferencePoint - origin;
            var cameraTransform = studioCamera.transform;
            float depth = Vector3.Dot(origin - cameraTransform.position, cameraTransform.forward);
            float depthDelta = Vector3.Dot(delta, cameraTransform.forward);
            float first = 0, last = 1;
            // Clip in camera depth before perspective projection, including lines crossing behind us.
            if (!ClipAirGuideBoundary(-depthDelta, depth - studioCamera.nearClipPlane - .001f, ref first, ref last)
                || !ClipAirGuideBoundary(depthDelta, studioCamera.farClipPlane - .001f - depth, ref first, ref last)
                || !Project(origin + delta * first, out start) || !Project(origin + delta * last, out end)) return false;
            Vector2 screenDelta = end - start;
            first = 0; last = 1;
            if (!ClipAirGuideBoundary(-screenDelta.x, start.x - viewport.xMin - 3, ref first, ref last)
                || !ClipAirGuideBoundary(screenDelta.x, viewport.xMax - 3 - start.x, ref first, ref last)
                || !ClipAirGuideBoundary(-screenDelta.y, start.y - viewport.yMin - 3, ref first, ref last)
                || !ClipAirGuideBoundary(screenDelta.y, viewport.yMax - 3 - start.y, ref first, ref last)) return false;
            end = start + screenDelta * last;
            start += screenDelta * first;
            return true;
        }

        static bool ClipAirGuideBoundary(float direction, float distance, ref float first, ref float last)
        {
            if (Mathf.Abs(direction) < .000001f) return distance >= 0;
            float crossing = distance / direction;
            if (direction < 0) first = Mathf.Max(first, crossing);
            else last = Mathf.Min(last, crossing);
            return first <= last;
        }
    }
}
