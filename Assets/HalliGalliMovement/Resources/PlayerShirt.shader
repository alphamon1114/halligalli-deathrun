Shader "HalliGalli/PlayerShirt"
{
    Properties
    {
        _Color ("Original body tint", Color) = (1,1,1,1)
        _MainTex ("Original body albedo", 2D) = "white" {}
        _MetallicGlossMap ("Original metallic / smoothness", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = .5
        _GlossMapScale ("Smoothness map scale", Range(0,1)) = .75
        _OcclusionMap ("Original occlusion", 2D) = "white" {}
        _OcclusionStrength ("Occlusion strength", Range(0,1)) = .7
        _BumpMap ("Original normal", 2D) = "bump" {}
        _BumpScale ("Normal scale", Float) = 1
        _EmissionMap ("Original emission", 2D) = "white" {}
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        _ShirtMask ("Authored Body atlas garment mask", 2D) = "black" {}
        _ShirtColor ("Player shirt", Color) = (.18,.42,.85,1)
        [HideInInspector] _ShirtAmount ("Shirt amount", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #pragma shader_feature_local _METALLICGLOSSMAP
        #pragma shader_feature_local _NORMALMAP
        #pragma shader_feature_local _EMISSION
        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        sampler2D _MainTex, _MetallicGlossMap, _OcclusionMap, _BumpMap, _EmissionMap, _ShirtMask;
        fixed4 _Color, _ShirtColor, _EmissionColor;
        half _Metallic, _Glossiness, _GlossMapScale, _OcclusionStrength, _BumpScale, _ShirtAmount;
        struct Input { float2 uv_MainTex; };
        void surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 original = tex2D(_MainTex, input.uv_MainTex);
            half mask = tex2D(_ShirtMask, input.uv_MainTex).r * _ShirtAmount;
            // The blue channel carries the authored fabric folds, stitching and shading.
            // Normalize its original midtone, not the skin/pants or the whole material.
            #ifdef UNITY_COLORSPACE_GAMMA
                half fabric = original.b / .44h;
            #else
                half fabric = original.b / .162647h;
            #endif
            output.Albedo = lerp(original.rgb, _ShirtColor.rgb * fabric, mask) * _Color.rgb;
            output.Alpha = original.a * _Color.a;
            #ifdef _METALLICGLOSSMAP
                half4 metal = tex2D(_MetallicGlossMap, input.uv_MainTex);
                output.Metallic = metal.r;
                output.Smoothness = metal.a * _GlossMapScale;
            #else
                output.Metallic = _Metallic;
                output.Smoothness = _Glossiness;
            #endif
            output.Occlusion = lerp(1, tex2D(_OcclusionMap, input.uv_MainTex).g, _OcclusionStrength);
            #ifdef _NORMALMAP
                output.Normal = UnpackScaleNormal(tex2D(_BumpMap, input.uv_MainTex), _BumpScale);
            #endif
            #ifdef _EMISSION
                output.Emission = tex2D(_EmissionMap, input.uv_MainTex).rgb * _EmissionColor.rgb;
            #endif
        }
        ENDCG
    }
    FallBack "Standard"
}
