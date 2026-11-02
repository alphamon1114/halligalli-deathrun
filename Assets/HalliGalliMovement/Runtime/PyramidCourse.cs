using UnityEngine;

namespace HalliGalli.Movement
{
    // The geometry owns the race. This component only lights the torches and describes the rooms.
    public sealed class PyramidCourse:MonoBehaviour
    {
        [System.Serializable] public sealed class Chamber
        {
            public string name;
            public Vector3 minimum,maximum,landing;
            public Vector3 origin,localMinimum,localMaximum;
            public float heading;
            public int firstSection,lastSection;
            public Quaternion Rotation=>Quaternion.Euler(0,heading,0);
            public Vector3 ToWorld(Vector3 local)=>origin+Rotation*local;
            public Vector3 ToLocal(Vector3 world)=>Quaternion.Inverse(Rotation)*(world-origin);
        }
        public Chamber[] chambers;
        public Light[] flames;
        void Update()
        {
            if(flames==null)return;
            for(int i=0;i<flames.Length;i++)if(flames[i])
                flames[i].intensity=2.5f+.23f*Mathf.Sin(Time.time*7+i*1.8f)+.15f*Mathf.Sin(Time.time*13+i*2.3f);
        }
    }
}
