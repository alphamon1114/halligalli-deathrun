using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalliGalli.Movement
{
    public sealed class MapMusicPlayer : MonoBehaviour
    {
        const string VolumeKey = "HalliGalli.MusicVolume";
        const float DefaultVolume = .30f;
        const float FadeSeconds = .75f;
        static bool volumeLoaded, volumeDirty;
        static float volume;
        static float saveAt;
        readonly AudioSource[] sources = new AudioSource[2];
        readonly string[] clipMaps = new string[2];
        readonly float[] gains = new float[2];
        readonly float[] fadeFrom = new float[2];
        CourseSession session;
        CourseMapDefinition boundMap;
        string sceneMapId = "", requestedMapId = "", failedMapId;
        int target = -1;
        float fadeTime;
        bool fading;

        public static MapMusicPlayer Instance { get; private set; }
        public string CurrentMapId => target >= 0 ? clipMaps[target] : "";
        public AudioSource CurrentSource => target >= 0 ? sources[target] : null;
        public static float Volume {
            get {
                if (!volumeLoaded) {
                    volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, DefaultVolume));
                    volumeLoaded = true;
                }
                return volume;
            }
            set {
                float next = Mathf.Clamp01(value);
                if (float.IsNaN(next) || Mathf.Approximately(Volume, next)) return;
                volume = next;
                PlayerPrefs.SetFloat(VolumeKey, volume);
                volumeDirty = true;
                saveAt = Time.realtimeSinceStartup + .5f;
                if (Instance) Instance.ApplyVolume();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic()
        {
            if (Instance) SceneManager.sceneLoaded -= Instance.SceneLoaded;
            Instance = null;
            volumeLoaded = volumeDirty = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { GameReleaseGate.RunWhenAllowed(BootAfterReleaseCheck); }

        static void BootAfterReleaseCheck()
        {
            if (!Instance) new GameObject("Map music").AddComponent<MapMusicPlayer>();
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < sources.Length; i++) {
                var source = gameObject.AddComponent<AudioSource>();
                sources[i] = source;
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0;
                source.dopplerLevel = 0;
                source.priority = 220;
                source.volume = 0;
                source.ignoreListenerPause = false;
            }
            SceneManager.sceneLoaded += SceneLoaded;
            BindSession();
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode) { BindSession(); }

        void BindSession()
        {
            session = FindAnyObjectByType<CourseSession>();
            BindMap();
        }

        void BindMap()
        {
            boundMap = session && session.maps ? session.maps.current : null;
            sceneMapId = "";
            if (!session || session.practiceRoom || session.userMap || !boundMap || !boundMap.playable ||
                !session.maps.catalog || !session.maps.catalog.Contains(boundMap) ||
                session.gameObject.scene.path != boundMap.scenePath || ResourcePath(boundMap.id) == null) return;
            sceneMapId = boundMap.id;
        }

        string DesiredMapId()
        {
            if (!GameReleaseGate.IsAllowed || UserMapStudio.IsOpen || !session || !session.isActiveAndEnabled) return "";
            if (session.maps && session.maps.current != boundMap) BindMap();
            var network = CourseNetwork.Instance;
            if (network) {
                if (CourseNetwork.Active) {
                    if (!network.Playing) return "";
                } else if (network.OverlayVisible) return "";
            }
            return sceneMapId;
        }

        void LateUpdate()
        {
            string desired = DesiredMapId();
            if (desired != requestedMapId) {
                requestedMapId = desired;
                failedMapId = null;
            }
            TryTransition();
            if (fading) {
                fadeTime += Time.unscaledDeltaTime;
                float amount = Mathf.Clamp01(fadeTime / FadeSeconds);
                for (int i = 0; i < sources.Length; i++)
                    gains[i] = Mathf.Lerp(fadeFrom[i], i == target ? 1 : 0, amount);
                ApplyVolume();
                if (amount >= 1) {
                    fading = false;
                    for (int i = 0; i < sources.Length; i++) if (i != target) ReleaseClip(i);
                }
            }
            if (volumeDirty && Time.realtimeSinceStartup >= saveAt) SaveVolume();
        }

        void TryTransition()
        {
            if (requestedMapId == CurrentMapId || requestedMapId == failedMapId) return;
            if (requestedMapId.Length == 0) { FadeTo(-1); return; }
            for (int i = 0; i < sources.Length; i++) {
                if (sources[i].clip && clipMaps[i] == requestedMapId) { FadeTo(i); return; }
            }
            for (int i = 0; i < sources.Length; i++) {
                if (sources[i].clip) continue;
                var clip = Resources.Load<AudioClip>(ResourcePath(requestedMapId));
                if (!clip) {
                    failedMapId = requestedMapId;
                    Debug.LogWarning("Missing map music: " + requestedMapId, this);
                    FadeTo(-1);
                    return;
                }
                clipMaps[i] = requestedMapId;
                sources[i].clip = clip;
                sources[i].mute = Volume <= 0;
                sources[i].Play();
                FadeTo(i);
                return;
            }
            // A rapid third selection waits for a silent slot rather than cutting a playing waveform.
        }

        void FadeTo(int index)
        {
            if (target == index) return;
            target = index;
            fadeTime = 0;
            fading = true;
            for (int i = 0; i < sources.Length; i++) fadeFrom[i] = gains[i];
        }

        void ApplyVolume()
        {
            float level = Volume;
            for (int i = 0; i < sources.Length; i++) {
                sources[i].volume = gains[i] * level;
                sources[i].mute = level <= 0;
            }
        }

        void ReleaseClip(int index)
        {
            var source = sources[index];
            if (!source) return;
            var clip = source.clip;
            source.Stop();
            source.clip = null;
            source.volume = 0;
            clipMaps[index] = null;
            gains[index] = 0;
            if (clip) Resources.UnloadAsset(clip);
        }

        static string ResourcePath(string id)
        {
            switch (id) {
                case "first-steps": return "MapMusic/first-steps";
                case "summit": return "MapMusic/summit";
                case "surf": return "MapMusic/surf";
                case "factory": return "MapMusic/factory";
                case "pyramid": return "MapMusic/pyramid";
                case "item-race": return "MapMusic/item-race";
                case "clockwork": return "MapMusic/clockwork";
                default: return null;
            }
        }

        static void SaveVolume()
        {
            if (!volumeDirty) return;
            PlayerPrefs.Save();
            volumeDirty = false;
        }

        void OnApplicationFocus(bool focused) { if (!focused) SaveVolume(); }
        void OnApplicationPause(bool paused) { if (paused) SaveVolume(); }
        void OnApplicationQuit() { SaveVolume(); }
        void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            if (Instance != this) return;
            SaveVolume();
            for (int i = 0; i < sources.Length; i++) ReleaseClip(i);
            Instance = null;
        }
    }
}
