using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace HalliGalli.Movement
{
    // Only built-in geometry is serialized. Names never become file paths.
    public static class UserMapStorage
    {
        public const string Extension = ".bellmap";
        public const int MaxBytes = 524288, MaxObjects = 512;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static string DirectoryPath => Path.Combine(Application.persistentDataPath, "UserMaps");

        public static UserMapData CreateDefault()
        {
            var parts = new List<UserMapObject>();
            parts.Add(Part(UserMapPart.Block, new Vector3(0, -.25f, 2.5f), new Vector3(10, .5f, 5), 0));
            parts.Add(Part(UserMapPart.Block, new Vector3(0, -.05f, 6.9f), new Vector3(5, .5f, 2.6f), 1));
            parts.Add(Part(UserMapPart.Checkpoint, new Vector3(0, .15f, 10.2f), new Vector3(5, .5f, 2.6f), 2));
            parts.Add(Part(UserMapPart.Block, new Vector3(1, .15f, 13.5f), new Vector3(4, .5f, 2.6f), 3));
            parts.Add(Part(UserMapPart.Block, new Vector3(0, .15f, 17.5f), new Vector3(6, .5f, 4), 0));
            parts.Add(Part(UserMapPart.Bell, new Vector3(0, .4f, 18.5f), Vector3.one, 4));
            foreach (var at in new[] { new Vector3(0, .025f, 2.5f), new Vector3(0, .225f, 6.9f), new Vector3(0, .425f, 10.2f), new Vector3(1, .425f, 13.5f), new Vector3(0, .425f, 17.5f) })
                parts.Add(Part(UserMapPart.Waypoint, at, Vector3.one * .35f, 5));
            return new UserMapData { id = Guid.NewGuid().ToString("N"), name = "나의 첫 코스", description = "발판을 건너 체크포인트를 밟고 종을 울리세요.", objects = parts.ToArray() };
        }

        static UserMapObject Part(UserMapPart kind, Vector3 at, Vector3 size, int color)
            => new UserMapObject { id = Guid.NewGuid().ToString("N"), kind = kind, position = at, scale = size, color = color };

        public static string Serialize(UserMapData data)
        {
            Validate(data);
            string json = JsonUtility.ToJson(data);
            CheckSize(json);
            return json;
        }

        public static UserMapData Parse(string json)
        {
            CheckSize(json);
            var data = new MapJsonReader(json).Read();
            Validate(data);
            return data;
        }

        public static void Validate(UserMapData data)
        {
            if (data == null || data.version != 1) Fail("지원하지 않는 맵 버전입니다.");
            CheckId(data.id);
            CheckText(data.name, 60, false, "맵 이름");
            CheckText(data.description, 1000, true, "설명");
            if (!InRange(data.fallHeight, -200, -2)) Fail("낙하 복귀 높이는 -200 ~ -2 범위여야 합니다.");
            if (data.objects == null || data.objects.Length == 0 || data.objects.Length > MaxObjects)
                Fail("맵은 1 ~ 512개의 부품을 포함해야 합니다.");
            int bells = 0;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in data.objects) {
                if (item == null) Fail("비어 있는 부품이 있습니다.");
                CheckId(item.id);
                if (!ids.Add(item.id)) Fail("부품 ID가 중복됩니다.");
                if ((int)item.kind < 0 || (int)item.kind > (int)UserMapPart.Waypoint) Fail("알 수 없는 부품 종류입니다.");
                if (!VectorRange(item.position, new Vector3(-500, -100, 0), new Vector3(500, 300, 1000))) Fail("부품 위치가 제작 영역 밖입니다.");
                if (!VectorRange(item.rotation, Vector3.one * -360, Vector3.one * 360)) Fail("회전은 -360 ~ 360도 범위여야 합니다.");
                if (!VectorRange(item.scale, Vector3.one * .1f, new Vector3(100, 60, 100))) Fail("부품 크기는 0.1 ~ 100m (높이 최대 60m)여야 합니다.");
                if (!VectorRange(item.travel, Vector3.one * -80, Vector3.one * 80)) Fail("이동 거리는 축마다 -80 ~ 80m 범위여야 합니다.");
                if (!InRange(item.period, .5f, 120) || !InRange(item.speed, -20, 20)) Fail("주기는 0.5 ~ 120초, 속도는 -20 ~ 20m/s 범위여야 합니다.");
                if (item.color < 0 || item.color > 7) Fail("부품 색상 번호가 올바르지 않습니다.");
                bool yawOnly = item.kind == UserMapPart.Checkpoint || item.kind == UserMapPart.MovingPlatform || item.kind == UserMapPart.Conveyor
                    || item.kind == UserMapPart.Press || item.kind == UserMapPart.Bell || item.kind == UserMapPart.Waypoint;
                if (yawOnly && (Mathf.Abs(Mathf.DeltaAngle(0, item.rotation.x)) > .001f || Mathf.Abs(Mathf.DeltaAngle(0, item.rotation.z)) > .001f))
                    Fail("이 부품은 수평 회전(Y축)만 지원합니다.");
                if (item.kind == UserMapPart.Bell) {
                    bells++;
                    if (!VectorRange(item.scale, Vector3.one * .5f, Vector3.one * 2)) Fail("종 크기는 0.5 ~ 2 범위여야 합니다.");
                }
                CheckFootprint(item, data.fallHeight);
            }
            if (bells != 1) Fail("맵에는 종이 정확히 하나 있어야 합니다.");
        }

        static void CheckFootprint(UserMapObject item, float fallHeight)
        {
            if (item.kind == UserMapPart.Waypoint) return;
            Vector3 extent = item.scale * .5f, centre = item.position;
            if (item.kind == UserMapPart.Bell) { extent = Vector3.Scale(item.scale, new Vector3(.38f, .55f, .38f)); centre.y += extent.y; }
            if (item.kind == UserMapPart.Press) { extent = new Vector3(item.scale.x * .5f + .4f, item.scale.y * .5f + .5f, item.scale.z * .5f); centre.y += extent.y; }
            var rotation = Quaternion.Euler(item.rotation);
            var right = rotation * new Vector3(extent.x, 0, 0);
            var up = rotation * new Vector3(0, extent.y, 0);
            var forward = rotation * new Vector3(0, 0, extent.z);
            var bound = Abs(right) + Abs(up) + Abs(forward);
            if (item.kind == UserMapPart.MovingPlatform) bound += Abs(item.travel);
            var min = centre - bound; var max = centre + bound;
            if (min.z < -.001f || max.z > 1000 || min.x < -500 || max.x > 500 || max.y > 300 || min.y < -100)
                Fail("부품과 이동 경로는 시작 책상 앞(z ≥ 0)의 제작 영역 안에 있어야 합니다.");
            if (max.y <= fallHeight + .25f) Fail("부품은 낙하 복귀 높이보다 위에 있어야 합니다.");
        }

        static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        static bool InRange(float value, float min, float max) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        static bool VectorRange(Vector3 value, Vector3 min, Vector3 max) => InRange(value.x, min.x, max.x) && InRange(value.y, min.y, max.y) && InRange(value.z, min.z, max.z);
        static void Fail(string message) { throw new InvalidDataException(message); }
        static void CheckId(string id)
        {
            if (id == null || id.Length != 32 || !Guid.TryParseExact(id, "N", out _)) Fail("맵 또는 부품 ID가 올바르지 않습니다.");
        }
        static void CheckText(string text, int max, bool multiline, string label)
        {
            if (text == null || text.Length > max || !multiline && string.IsNullOrWhiteSpace(text)) Fail(label + "은(는) " + max + "자 이내로 입력하세요.");
            foreach (char c in text) if (char.IsControl(c) && !(multiline && (c == '\n' || c == '\r' || c == '\t'))) Fail(label + "에 사용할 수 없는 문자가 있습니다.");
            try { Utf8.GetByteCount(text); } catch (EncoderFallbackException) { Fail(label + "의 문자 인코딩이 올바르지 않습니다."); }
        }
        static void CheckSize(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes) Fail("맵 파일이 비어 있거나 512KB보다 큽니다.");
            try { if (Utf8.GetByteCount(json) > MaxBytes) Fail("맵 파일은 512KB 이하여야 합니다."); }
            catch (EncoderFallbackException) { Fail("맵 파일의 UTF-8 인코딩이 올바르지 않습니다."); }
        }
        // A fixed schema reader rejects duplicate/unknown fields and wrong JSON types before Unity sees data.
        // Its nesting is structural (map -> objects -> vectors), so hostile recursive JSON cannot exhaust the stack.
        sealed class MapJsonReader
        {
            readonly string json;
            int offset;
            public MapJsonReader(string value) { json = value; }
            void White() { while (offset < json.Length && (json[offset] == ' ' || json[offset] == '\t' || json[offset] == '\r' || json[offset] == '\n')) offset++; }
            bool Take(char value) { White(); if (offset >= json.Length || json[offset] != value) return false; offset++; return true; }
            void Need(char value) { if (!Take(value)) Fail("맵 JSON 구문이 올바르지 않습니다: " + offset); }
            static void Mark(ref int seen, int bit) { if ((seen & bit) != 0) Fail("맵 JSON에 중복된 필드가 있습니다."); seen |= bit; }
            string Key() { string key = Text(); Need(':'); return key; }
            public UserMapData Read()
            {
                var map = new UserMapData(); int seen = 0;
                Need('{');
                do {
                    switch (Key()) {
                        case "version": Mark(ref seen, 1); map.version = Integer(); break;
                        case "id": Mark(ref seen, 2); map.id = Text(); break;
                        case "name": Mark(ref seen, 4); map.name = Text(); break;
                        case "description": Mark(ref seen, 8); map.description = Text(); break;
                        case "fallHeight": Mark(ref seen, 16); map.fallHeight = Number(); break;
                        case "objects": Mark(ref seen, 32); map.objects = Objects(); break;
                        default: Fail("지원하지 않는 맵 필드입니다."); break;
                    }
                } while (Take(','));
                Need('}'); White();
                if (seen != 63 || offset != json.Length) Fail("맵 필드가 누락되었거나 JSON 뒤에 잘못된 데이터가 있습니다.");
                return map;
            }
            UserMapObject[] Objects()
            {
                Need('['); var objects = new List<UserMapObject>();
                if (Take(']')) return objects.ToArray();
                do {
                    if (objects.Count >= MaxObjects) Fail("부품은 512개까지 저장할 수 있습니다.");
                    var item = new UserMapObject(); int seen = 0; Need('{');
                    do {
                        switch (Key()) {
                            case "id": Mark(ref seen, 1); item.id = Text(); break;
                            case "kind": Mark(ref seen, 2); item.kind = (UserMapPart)Integer(); break;
                            case "position": Mark(ref seen, 4); item.position = Vector(); break;
                            case "rotation": Mark(ref seen, 8); item.rotation = Vector(); break;
                            case "scale": Mark(ref seen, 16); item.scale = Vector(); break;
                            case "color": Mark(ref seen, 32); item.color = Integer(); break;
                            case "travel": Mark(ref seen, 64); item.travel = Vector(); break;
                            case "period": Mark(ref seen, 128); item.period = Number(); break;
                            case "speed": Mark(ref seen, 256); item.speed = Number(); break;
                            default: Fail("지원하지 않는 부품 필드입니다."); break;
                        }
                    } while (Take(','));
                    Need('}'); if (seen != 511) Fail("부품 필드가 누락되었습니다.");
                    objects.Add(item);
                } while (Take(','));
                Need(']'); return objects.ToArray();
            }
            Vector3 Vector()
            {
                var value = Vector3.zero; int seen = 0; Need('{');
                do {
                    switch (Key()) {
                        case "x": Mark(ref seen, 1); value.x = Number(); break;
                        case "y": Mark(ref seen, 2); value.y = Number(); break;
                        case "z": Mark(ref seen, 4); value.z = Number(); break;
                        default: Fail("벡터에는 x, y, z만 사용할 수 있습니다."); break;
                    }
                } while (Take(','));
                Need('}'); if (seen != 7) Fail("벡터 좌표가 누락되었습니다."); return value;
            }
            string Text()
            {
                Need('"'); var result = new StringBuilder();
                while (offset < json.Length) {
                    char c = json[offset++];
                    if (c == '"') return result.ToString();
                    if (c < 32) Fail("문자열에 잘못된 제어 문자가 있습니다.");
                    if (c == '\\') {
                        if (offset >= json.Length) Fail("문자열 이스케이프가 잘못되었습니다.");
                        c = json[offset++];
                        switch (c) {
                            case '"': case '\\': case '/': break;
                            case 'b': c = '\b'; break;
                            case 'f': c = '\f'; break;
                            case 'n': c = '\n'; break;
                            case 'r': c = '\r'; break;
                            case 't': c = '\t'; break;
                            case 'u':
                                if (offset + 4 > json.Length || !ushort.TryParse(json.AsSpan(offset, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                                    throw new InvalidDataException("유니코드 이스케이프가 잘못되었습니다.");
                                c = (char)code; offset += 4; break;
                            default: Fail("문자열 이스케이프가 잘못되었습니다."); break;
                        }
                    }
                    result.Append(c);
                }
                throw new InvalidDataException("문자열이 닫히지 않았습니다.");
            }
            ReadOnlySpan<char> NumericToken()
            {
                White(); int start = offset;
                if (offset < json.Length && json[offset] == '-') offset++;
                if (offset < json.Length && json[offset] == '0') offset++;
                else Digits();
                if (offset < json.Length && json[offset] == '.') { offset++; Digits(); }
                if (offset < json.Length && (json[offset] == 'e' || json[offset] == 'E')) {
                    offset++; if (offset < json.Length && (json[offset] == '+' || json[offset] == '-')) offset++; Digits();
                }
                return json.AsSpan(start, offset - start);
            }
            void Digits()
            {
                int start = offset;
                while (offset < json.Length && json[offset] >= '0' && json[offset] <= '9') offset++;
                if (start == offset) Fail("맵 숫자 형식이 올바르지 않습니다.");
            }
            float Number()
            {
                if (!float.TryParse(NumericToken(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value))
                    throw new InvalidDataException("유한한 숫자만 사용할 수 있습니다.");
                return value;
            }
            int Integer()
            {
                if (!int.TryParse(NumericToken(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
                    throw new InvalidDataException("정수 필드 형식이 올바르지 않습니다.");
                return value;
            }
        }

        // Snapshot cloning preserves identity; importing a separate authoring copy assigns a new identity.
        public static UserMapData Clone(UserMapData data)
        {
            Validate(data);
            var copy = new UserMapData { version = data.version, id = data.id, name = data.name, description = data.description, fallHeight = data.fallHeight, objects = new UserMapObject[data.objects.Length] };
            for (int i = 0; i < copy.objects.Length; i++) {
                var item = data.objects[i];
                copy.objects[i] = new UserMapObject { id = item.id, kind = item.kind, position = item.position, rotation = item.rotation, scale = item.scale, color = item.color, travel = item.travel, period = item.period, speed = item.speed };
            }
            return copy;
        }
        public static string Hash(string json)
        {
            CheckSize(json);
            using (var hash = SHA256.Create()) {
                var digest = hash.ComputeHash(Utf8.GetBytes(json));
                var result = new StringBuilder(64);
                foreach (byte b in digest) result.Append(b.ToString("x2"));
                return result.ToString();
            }
        }
        static string LocalPath(string id) { CheckId(id); return Path.Combine(DirectoryPath, id.ToLowerInvariant() + Extension); }
        public static string Save(UserMapData data)
        {
            string json = Serialize(data), path = LocalPath(data.id);
            UserMapClearance.RequireCleared(data, json);
            Directory.CreateDirectory(DirectoryPath);
            AtomicWrite(path, json);
            return path;
        }
        public static UserMapData Load(string id)
        {
            var data = LoadFile(LocalPath(id));
            if (!string.Equals(id, data.id, StringComparison.OrdinalIgnoreCase)) Fail("파일 이름과 맵 ID가 일치하지 않습니다.");
            return data;
        }
        public static UserMapData[] List()
        {
            if (!Directory.Exists(DirectoryPath)) return Array.Empty<UserMapData>();
            var maps = new List<UserMapData>();
            foreach (string path in Directory.EnumerateFiles(DirectoryPath, "*" + Extension)) {
                try { maps.Add(Load(Path.GetFileNameWithoutExtension(path))); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException) {
                    Debug.LogWarning("맵을 읽을 수 없습니다: " + Path.GetFileName(path) + " — " + error.Message);
                }
            }
            maps.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.CurrentCulture));
            return maps.ToArray();
        }
        // Reads a portable original without assigning authoring identity or recording a solo clear.
        public static UserMapData LoadFile(string path) => Parse(ReadBounded(path));

        // Imported maps remain unsaved drafts until the player completes their own local solo run.
        public static UserMapData Import(string path)
        {
            var data = LoadFile(path);
            AssignDraftIdentity(data);
            return data;
        }
        internal static void AssignDraftIdentity(UserMapData data)
        {
            do { data.id = Guid.NewGuid().ToString("N"); } while (File.Exists(LocalPath(data.id)));
        }

        public static void Export(UserMapData data, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase))
                Fail("내보내기 파일의 확장자는 .bellmap이어야 합니다.");
            string json = Serialize(data);
            UserMapClearance.RequireCleared(data, json);
            AtomicWrite(Path.GetFullPath(path), json);
        }
        static string ReadBounded(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (file.Length <= 0 || file.Length > MaxBytes) Fail("맵 파일이 비어 있거나 512KB보다 큽니다.");
                var bytes = new byte[(int)file.Length]; int read = 0;
                while (read < bytes.Length) { int count = file.Read(bytes, read, bytes.Length - read); if (count == 0) throw new EndOfStreamException("맵 파일 읽기가 중단되었습니다."); read += count; }
                if (file.ReadByte() != -1) Fail("맵 파일 크기가 읽는 중 변경되었습니다.");
                try {
                    int offset = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
                    return Utf8.GetString(bytes, offset, bytes.Length - offset);
                } catch (DecoderFallbackException error) { throw new InvalidDataException("UTF-8 맵 파일만 가져올 수 있습니다.", error); }
            }
        }
        internal static void AtomicWrite(string path, string content)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    byte[] bytes = Utf8.GetBytes(content); file.Write(bytes, 0, bytes.Length); file.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
