using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HalliGalli.Movement
{
    // Received originals are immutable content-addressed revisions, not creator-clear records.
    public static class UserMapDownloads
    {
        static readonly object WriteLock = new object();
        public static string DirectoryPath => Path.Combine(Application.persistentDataPath, "DownloadedMaps");

        public static string Store(UserMapData data)
        {
            string json = UserMapStorage.Serialize(data);
            string hash = UserMapStorage.Hash(json);
            lock (WriteLock) {
                if (TryLoad(hash, out _)) return hash;
                Directory.CreateDirectory(DirectoryPath);
                UserMapStorage.AtomicWrite(LocalPath(hash), json);
            }
            return hash;
        }

        public static bool TryLoad(string hash, out UserMapData data)
        {
            data = null;
            if (!IsHash(hash)) return false;
            try {
                var candidate = UserMapStorage.LoadFile(LocalPath(hash));
                if (UserMapStorage.Hash(UserMapStorage.Serialize(candidate)) != hash) return false;
                data = candidate;
                return true;
            } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException) {
                return false;
            }
        }

        public static bool Contains(UserMapData data)
        {
            try {
                string hash = UserMapStorage.Hash(UserMapStorage.Serialize(data));
                return TryLoad(hash, out _);
            } catch (InvalidDataException) { return false; }
        }

        public static UserMapData[] List()
        {
            if (!Directory.Exists(DirectoryPath)) return Array.Empty<UserMapData>();
            var maps = new List<UserMapData>();
            foreach (string path in Directory.EnumerateFiles(DirectoryPath, "*" + UserMapStorage.Extension)) {
                string hash = Path.GetFileNameWithoutExtension(path);
                if (TryLoad(hash, out var data)) maps.Add(data);
                else Debug.LogWarning("받은 맵을 읽을 수 없습니다: " + Path.GetFileName(path));
            }
            maps.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.CurrentCulture));
            return maps.ToArray();
        }

        // Re-read the stored revision instead of exporting a potentially edited in-memory copy.
        public static void Export(string hash, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), UserMapStorage.Extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("내보내기 파일의 확장자는 .bellmap이어야 합니다.");
            if (!TryLoad(hash, out var data)) throw new InvalidDataException("받은 맵 원본이 없거나 손상되었어요. 파일을 다시 가져오세요.");
            UserMapStorage.AtomicWrite(Path.GetFullPath(path), UserMapStorage.Serialize(data));
        }

        static string LocalPath(string hash) => Path.Combine(DirectoryPath, hash + UserMapStorage.Extension);

        static bool IsHash(string hash)
        {
            if (hash == null || hash.Length != 64) return false;
            foreach (char c in hash) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }
    }
}
