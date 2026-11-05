using UnityEngine;

namespace HalliGalli.Movement
{
    internal static class MusicVolumeControl
    {
        static int shownPercent = -1;
        static string caption;

        public static void Draw(Rect rect)
        {
            float volume = MapMusicPlayer.Volume;
            var slider = new Rect(rect.x + 42, rect.y + 25, rect.width - 84, 34);
            int id = GUIUtility.GetControlID(0x4D555349, FocusType.Keyboard, slider);
            var current = Event.current;
            if (GUI.enabled) {
                switch (current.GetTypeForControl(id)) {
                    case EventType.MouseDown:
                        if (current.button == 0 && slider.Contains(current.mousePosition)) {
                            GUIUtility.hotControl = GUIUtility.keyboardControl = id;
                            volume = SliderValue(slider, current.mousePosition.x);
                            current.Use();
                        }
                        break;
                    case EventType.MouseDrag:
                        if (GUIUtility.hotControl == id) {
                            volume = SliderValue(slider, current.mousePosition.x);
                            current.Use();
                        }
                        break;
                    case EventType.MouseUp:
                        if (GUIUtility.hotControl == id && current.button == 0) {
                            GUIUtility.hotControl = 0;
                            current.Use();
                        }
                        break;
                    case EventType.KeyDown:
                        if (GUIUtility.keyboardControl != id) break;
                        bool handled = true;
                        switch (current.keyCode) {
                            case KeyCode.LeftArrow: case KeyCode.DownArrow: volume -= .05f; break;
                            case KeyCode.RightArrow: case KeyCode.UpArrow: volume += .05f; break;
                            case KeyCode.Home: volume = 0; break;
                            case KeyCode.End: volume = 1; break;
                            default: handled = false; break;
                        }
                        if (handled) current.Use();
                        break;
                }
            }
            if (BellUI.Button(new Rect(rect.x, slider.y, 34, 34), "−", size: 20)) volume -= .05f;
            if (BellUI.Button(new Rect(rect.xMax - 34, slider.y, 34, 34), "+", size: 20)) volume += .05f;
            MapMusicPlayer.Volume = volume;
            volume = MapMusicPlayer.Volume;
            int percent = Mathf.RoundToInt(volume * 100);
            if (shownPercent != percent) {
                shownPercent = percent;
                caption = percent == 0 ? "배경 음악 · 음소거" : "배경 음악 · " + percent + "%";
            }
            bool focused = GUIUtility.keyboardControl == id;
            BellUI.Text(new Rect(rect.x, rect.y, rect.width - 90, 24), caption, 14, BellUI.Paper);
            BellUI.Text(new Rect(rect.xMax - 90, rect.y, 90, 24), focused ? "←/→ 조절" : "0 = 음소거", 11, BellUI.Muted, align: TextAnchor.MiddleRight);
            if (focused) BellUI.Round(slider, BellUI.Gold, 8, 1);
            float start = slider.x + 9, width = slider.width - 18;
            BellUI.Round(new Rect(start, slider.center.y - 3, width, 6), BellUI.Muted, 3);
            BellUI.Round(new Rect(start, slider.center.y - 3, width * volume, 6), BellUI.Gold, 3);
            BellUI.Round(new Rect(start + width * volume - 9, slider.center.y - 12, 18, 24), BellUI.Paper, 9);
        }

        static float SliderValue(Rect slider, float x) => Mathf.Clamp01((x - slider.x - 9) / (slider.width - 18));
    }
}
