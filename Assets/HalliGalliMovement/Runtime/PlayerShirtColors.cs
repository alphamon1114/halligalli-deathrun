using UnityEngine;

namespace HalliGalli.Movement
{
    public static class PlayerShirtColors
    {
        public const int Count = 8;
        public const string PreferenceKey = "HalliGalli.PlayerShirtColor";
        static readonly Color[] Colors = {
            new Color(.18f, .42f, .85f), new Color(.88f, .16f, .18f),
            new Color(1f, .45f, .08f), new Color(.95f, .82f, .10f),
            new Color(.18f, .65f, .25f), new Color(.05f, .78f, .92f),
            new Color(.57f, .26f, .85f), new Color(1f, .32f, .63f)
        };
        static readonly string[] Names = { "파랑", "빨강", "주황", "노랑", "초록", "하늘", "보라", "분홍" };

        public static bool IsValid(int index) => (uint)index < Count;
        public static Color ColorAt(int index) => Colors[IsValid(index) ? index : 0];
        public static string NameAt(int index) => Names[IsValid(index) ? index : 0];
        public static int SavedIndex {
            get { int index = PlayerPrefs.GetInt(PreferenceKey, 0); return IsValid(index) ? index : 0; }
        }
        public static void Save(int index)
        {
            if (!IsValid(index)) return;
            PlayerPrefs.SetInt(PreferenceKey, index);
            PlayerPrefs.Save();
        }
    }
}
