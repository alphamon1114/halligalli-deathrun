using UnityEngine;

namespace HalliGalli.Movement
{
    // One worn head accessory per player. Index 0 is always "nothing"; append new items at the end
    // so saved preferences and room state keep their meaning.
    public static class PlayerAccessories
    {
        public const int None = 0, Sunglasses = 1, CatEars = 2, BirthdayHat = 3, MaidHeaddress = 4, PirateHat = 5, RabbitEars = 6, Crown = 7, Count = 8;
        public const string PreferenceKey = "HalliGalli.PlayerAccessory";
        static readonly string[] Names = { "없음", "선글라스", "고양이 귀 머리띠", "생일 꼬깔", "메이드 헤드드레스", "해적 선장 모자", "토끼 머리띠", "왕관" };
        // Prefabs are authored for direct parenting under the body model's Head bone.
        static readonly string[] PrefabResources = { null, "Accessories/Sunglasses", "Accessories/CatEars", "Accessories/BirthdayHat", "Accessories/MaidHeaddress", "Accessories/PirateHat", "Accessories/RabbitEars", "Accessories/Crown" };
        static readonly GameObject[] prefabs = new GameObject[Count];
        static readonly bool[] missingReported = new bool[Count];

        public static bool IsValid(int index) => (uint)index < Count;
        public static string NameAt(int index) => Names[IsValid(index) ? index : None];
        public static int SavedIndex {
            get { int index = PlayerPrefs.GetInt(PreferenceKey, None); return IsValid(index) ? index : None; }
        }
        public static void Save(int index)
        {
            if (!IsValid(index)) return;
            PlayerPrefs.SetInt(PreferenceKey, index);
            PlayerPrefs.Save();
        }
        public static GameObject Prefab(int index)
        {
            if (!IsValid(index) || PrefabResources[index] == null) return null;
            if (!prefabs[index]) prefabs[index] = Resources.Load<GameObject>(PrefabResources[index]);
            if (!prefabs[index] && !missingReported[index]) {
                Debug.LogError("Missing accessory prefab " + PrefabResources[index] + ". Install the authored accessory assets before building.");
                missingReported[index] = true;
            }
            return prefabs[index];
        }
    }
}
