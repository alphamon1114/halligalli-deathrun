using System;
using System.IO;
using UnityEngine;

namespace HalliGalli.Movement
{
    // Local completion records are never part of a portable map or network payload.
    public static class UserMapClearance
    {
        const string ProofVersion = "solo-clear-1\n";
        static string DirectoryPath => Path.Combine(Application.persistentDataPath, "UserMapClearance");

        public static bool IsCleared(UserMapData data)
        {
            try {
                string json = UserMapStorage.Serialize(data);
                return HasProof(data.id, UserMapStorage.Hash(json));
            } catch (InvalidDataException) { return false; }
        }

        public static void RequireCleared(UserMapData data)
            => RequireCleared(data, UserMapStorage.Serialize(data));

        internal static void RequireCleared(UserMapData data, string canonicalJson)
        {
            if (!HasProof(data.id, UserMapStorage.Hash(canonicalJson)))
                throw new InvalidOperationException("이 맵은 아직 완주 인증되지 않았어요. 맵 만들기에서 현재 맵을 혼자 종까지 완주한 뒤 저장·내보내기·방 선택을 할 수 있어요.");
        }

        // Called only after the studio observes the player's real solo bell completion.
        internal static void RecordClear(UserMapData data)
        {
            string json = UserMapStorage.Serialize(data);
            string hash = UserMapStorage.Hash(json);
            Directory.CreateDirectory(DirectoryPath);
            UserMapStorage.AtomicWrite(ProofPath(data.id, hash), ProofText(data.id, hash));
        }

        // IDs have been validated by Serialize; hashes are generated locally, never read from a map field.
        // Keep each completed snapshot so clearing an unsaved revision does not revoke the saved one.
        static string ProofPath(string id, string hash)
            => Path.Combine(DirectoryPath, id.ToLowerInvariant() + "-" + hash + ".clearance");
        static string ProofText(string id, string hash) => ProofVersion + id + "\n" + hash;

        static bool HasProof(string id, string hash)
        {
            string path = ProofPath(id, hash);
            if (!File.Exists(path)) return false;
            string expected = ProofText(id, hash);
            try {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    if (file.Length != expected.Length) return false;
                    // The fixed ASCII record is bounded; truncated or extended files never certify a map.
                    for (int i = 0; i < expected.Length; i++)
                        if (file.ReadByte() != expected[i]) return false;
                    return file.ReadByte() == -1;
                }
            } catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
