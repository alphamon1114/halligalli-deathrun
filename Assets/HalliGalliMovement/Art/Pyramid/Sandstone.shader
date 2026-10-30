Shader "HalliGalli/Sandstone"
{
    Properties
    {
        _Color ("Stone tint", Color) = (1,1,1,1)
        _MainTex ("Scanned stone / colour", 2D) = "white" {}
        [Normal] _BumpMap ("Scanned stone / normal", 2D) = "bump" {}
        _MaskMap ("AO / roughness / metal", 2D) = "white" {}
        _HeightMap ("Scanned stone / height", 2D) = "gray" {}
        _TileMetres ("World tile size in metres", Float) = 8
        _BumpScale ("Surface relief", Range(0,2)) = 1
        _Parallax ("Depth in texture units", Range(0,.04)) = .012
        _Polish ("Polish", Range(0,1)) = .25
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #include "UnityStandardUtils.cginc"
        struct Input { float3 worldPos; float3 worldNormal; INTERNAL_DATA };
        sampler2D _MainTex, _BumpMap, _MaskMap, _HeightMap;
        fixed4 _Color;
        float _TileMetres, _BumpScale, _Parallax, _Polish;
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            // World projection preserves masonry scale across differently sized walls.
            float3 n = normalize(WorldNormalVector(IN, float3(0,0,1)));
            float3 a = abs(n), u, v;
            if(a.y > a.x && a.y > a.z) { u=float3(1,0,0); v=float3(0,0,-sign(n.y)); }
            else if(a.x > a.z) { u=float3(0,0,-sign(n.x)); v=float3(0,1,0); }
            else { u=float3(sign(n.z),0,0); v=float3(0,1,0); }
            float2 uv=float2(dot(IN.worldPos,u),dot(IN.worldPos,v))/max(.1,_TileMetres);
            float3 t=normalize(u-n*dot(u,n)), b=normalize(cross(n,t));
            float3 eye=normalize(_WorldSpaceCameraPos-IN.worldPos);
            float2 shift=float2(dot(eye,t),dot(eye,b))/max(.4,abs(dot(eye,n)));
            float h=tex2D(_HeightMap,uv).r-.5;
            uv += shift*h*_Parallax;
            float3 normal=UnpackScaleNormal(tex2D(_BumpMap,uv),_BumpScale);
            float3 worldN=normalize(t*normal.x+b*normal.y+n*normal.z);
            o.Normal=float3(dot(worldN,normalize(WorldNormalVector(IN,float3(1,0,0)))),
                            dot(worldN,normalize(WorldNormalVector(IN,float3(0,1,0)))),dot(worldN,n));
            float3 mask=tex2D(_MaskMap,uv).rgb;
            // Broad weathering reduces uniformity without hiding the scanned detail.
            float weather=1+.055*sin(IN.worldPos.y*.13+sin(IN.worldPos.x*.05+IN.worldPos.z*.07));
            o.Albedo=tex2D(_MainTex,uv).rgb*_Color.rgb*weather;
            o.Occlusion=lerp(1,mask.r,.75);
            o.Smoothness=saturate(1-mask.g)*_Polish;
            o.Metallic=0;
        }
        ENDCG
    }
    FallBack "Standard"
}
