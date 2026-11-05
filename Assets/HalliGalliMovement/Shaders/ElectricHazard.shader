Shader "HalliGalli/ElectricHazard"
{
    Properties { _Size ("Surface size", Vector) = (25.5,42.25,0,0) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            float4 _Size;
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert(appdata_base v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord.xy; UNITY_TRANSFER_FOG(o,o.pos); return o; }
            float cross2(float2 a,float2 b) { return a.x*b.y-a.y*b.x; }
            float insideTriangle(float2 p,float2 a,float2 b,float2 c)
            {
                float3 sides=float3(cross2(b-a,p-a),cross2(c-b,p-b),cross2(a-c,p-c));
                return step(0,min(sides.x,min(sides.y,sides.z)));
            }
            fixed4 frag(v2f i):SV_Target
            {
                float2 p=i.uv*_Size.xy;
                // One uninterrupted dark electrode surface: no floor grid competing with the bank.
                float3 color=float3(.045,.075,.085);
                // Constant hazard tape, independent of animation or frame rate.
                float edge=min(min(p.x,_Size.x-p.x),min(p.y,_Size.y-p.y));
                float border=1-step(.48,edge);
                float hatch=step(.5,frac((p.x+p.y)/1.2));
                color=lerp(color,lerp(float3(.018,.022,.023),float3(.87,.56,.085),hatch),border);
                // Sparse warning signs retain the hazard identity without carpeting the room.
                float2 q=(frac(p/12)-.5)*12;
                float sign=insideTriangle(q,float2(-.72,-.51),float2(.72,-.51),float2(0,.81));
                float inner=insideTriangle(q,float2(-.58,-.43),float2(.58,-.43),float2(0,.64));
                color=lerp(color,float3(.012,.018,.02),sign);
                color=lerp(color,float3(.91,.62,.13),inner);
                float bolt=max(insideTriangle(q,float2(.13,.51),float2(-.20,-.04),float2(.06,.04)),
                               insideTriangle(q,float2(-.06,-.02),float2(-.14,-.35),float2(.23,.13)));
                color=lerp(color,float3(.012,.018,.02),bolt*inner);
                fixed4 result=fixed4(color,1); UNITY_APPLY_FOG(i.fogCoord,result); return result;
            }
            ENDCG
        }
    }
}
