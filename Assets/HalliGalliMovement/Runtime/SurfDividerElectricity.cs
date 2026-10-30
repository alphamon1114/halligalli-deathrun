using UnityEngine;

namespace HalliGalli.Movement
{
    // Cosmetic only: the continuous SurfDivider collider remains the sole contact authority.
    public sealed class SurfDividerElectricity : MonoBehaviour
    {
        public Vector3 surfaceMin;
        public Vector2 surfaceSize;
        public LineRenderer[] arcs;
        const int Points = 13;
        readonly Vector3[] points = new Vector3[Points];
        float nextFrame;

        void OnEnable() { Apply(Time.time); }
        void Update()
        {
            if (Time.unscaledTime < nextFrame) return;
            nextFrame = Time.unscaledTime + 1f / 18f;
            Apply(Time.time);
        }

        public void Apply(float time)
        {
            if (arcs == null) return;
            for (int i = 0; i < arcs.Length; i++) {
                var arc = arcs[i]; if (!arc) continue;
                float clock = time * .75f + i * .618034f;
                int cycle = Mathf.FloorToInt(clock);
                float phase = clock - cycle;
                // Staggered local discharges, with no full-surface flashing or point lights.
                arc.enabled = phase < .74f;
                if (!arc.enabled) continue;
                float seed = i * 27.19f + cycle * 17.7f;
                float x = 2.4f + Hash(seed + 1) * (surfaceSize.x - 4.8f);
                float z = 2.4f + Hash(seed + 2) * (surfaceSize.y - 4.8f);
                float angle = Hash(seed + 3) * Mathf.PI * 2;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                Vector3 side = new Vector3(direction.z, 0, -direction.x);
                Vector3 center = surfaceMin + new Vector3(x, .065f, z);
                float length = Mathf.Lerp(2.2f, 4.2f, Hash(seed + 4));
                int frame = Mathf.FloorToInt(time * 18);
                for (int n = 0; n < Points; n++) {
                    float t = n / (float)(Points - 1), envelope = Mathf.Sin(t * Mathf.PI);
                    float noise = seed + n * 5.13f + frame * 1.71f;
                    points[n] = center + direction * ((t - .5f) * length)
                        + side * ((Hash(noise) - .5f) * .55f * envelope)
                        + Vector3.up * ((.07f + Hash(noise + 8) * .32f) * envelope);
                }
                arc.SetPositions(points);
                float strength = Mathf.Sin(phase / .74f * Mathf.PI);
                arc.widthMultiplier = .024f + strength * .018f;
                Color color = Color.Lerp(new Color(.1f, .48f, .65f), new Color(.63f, .94f, 1), strength);
                arc.startColor = arc.endColor = color;
            }
        }

        static float Hash(float seed) { return Mathf.Repeat(Mathf.Sin(seed * 12.9898f) * 43758.5453f, 1); }
    }
}
