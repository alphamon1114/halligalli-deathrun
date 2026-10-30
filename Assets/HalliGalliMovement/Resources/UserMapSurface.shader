Shader "HalliGalli/UserMapSurface"
{
    Properties
    {
        _Color ("Body", Color) = (.62,.65,.63,1)
        _AccentColor ("Paint accent", Color) = (.12,.57,.67,1)
        _GrainTex ("Original aggregate / grain / pores", 2D) = "gray" {}
        _Metallic ("Metallic", Range(0,1)) = .12
        _Glossiness ("Smoothness", Range(0,1)) = .25
        _EmissionColor ("Status lamp emission", Color) = (0,0,0,1)
        [HideInInspector] _Finish ("Finish", Float) = 0
        [HideInInspector] _PartSize ("Metric dimensions", Vector) = (1,1,1,0)
        [HideInInspector] _TravelDirection ("Travel axis", Vector) = (0,1,0,0)
        [HideInInspector] _BeltSpeed ("Signed belt speed", Float) = 0
        [HideInInspector] _BeltTime ("Shared course clock", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 250
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "UnityCG.cginc"
        sampler2D _GrainTex;
        UNITY_INSTANCING_BUFFER_START(Part)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_DEFINE_INSTANCED_PROP(float4, _AccentColor)
            UNITY_DEFINE_INSTANCED_PROP(float4, _EmissionColor)
            UNITY_DEFINE_INSTANCED_PROP(float4, _PartSize)
            UNITY_DEFINE_INSTANCED_PROP(float4, _TravelDirection)
            UNITY_DEFINE_INSTANCED_PROP(float, _BeltSpeed)
        UNITY_INSTANCING_BUFFER_END(Part)
        half _Metallic, _Glossiness;
        float _Finish, _BeltTime;
        struct Input
        {
            float3 metricPosition;
            float3 localNormal;
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };
        void vert(inout appdata_full vertex, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            UNITY_SETUP_INSTANCE_ID(vertex);
            float3 size = UNITY_ACCESS_INSTANCED_PROP(Part, _PartSize).xyz;
            // Object-anchored meters retain density through resizing, rotation and platform motion.
            data.metricPosition = vertex.vertex.xyz * size;
            data.localNormal = normalize(vertex.normal / max(size, .0001));
        }
        float Fill(float distance)
        {
            float aa = max(fwidth(distance), .0001);
            return 1 - smoothstep(-aa, aa, distance);
        }
        float Stroke(float distance, float width) { return Fill(abs(distance) - width); }
        float Rectangle(float2 position, float2 halfSize) { return max(abs(position.x) - halfSize.x, abs(position.y) - halfSize.y); }
        float Chevron(float2 position)
        {
            return Stroke(position.y + abs(position.x) * .65, .065) * Fill(abs(position.x) - .7);
        }
        float3 SurfaceNormal(Input data, float height)
        {
            float3 normal = normalize(WorldNormalVector(data, float3(0,0,1)));
            float3 dx = ddx(data.worldPos), dy = ddy(data.worldPos);
            float3 rx = cross(dy, normal), ry = cross(normal, dx);
            float determinant = dot(dx, rx);
            float3 gradient = (ddx(height) * rx + ddy(height) * ry) * sign(determinant) / max(abs(determinant), 1e-9);
            // Sub-millimeter relief, not displaced geometry or a different standing surface.
            float3 perturbed = normalize(normal - gradient / max(1, length(gradient) * 2));
            return normalize(float3(dot(perturbed, WorldNormalVector(data, float3(1,0,0))),
                dot(perturbed, WorldNormalVector(data, float3(0,1,0))), dot(perturbed, normal)));
        }
        void surf(Input data, inout SurfaceOutputStandard output)
        {
            float3 p = data.metricPosition;
            float3 n = normalize(data.localNormal);
            float3 size = max(UNITY_ACCESS_INSTANCED_PROP(Part, _PartSize).xyz, .0001);
            float3 accent = UNITY_ACCESS_INSTANCED_PROP(Part, _AccentColor).rgb;
            float3 bodyColor = UNITY_ACCESS_INSTANCED_PROP(Part, _Color).rgb;
            float3 blend = pow(abs(n), 4); blend /= max(dot(blend, 1), .0001);
            float3 grain = tex2D(_GrainTex, p.yz * 2).rgb * blend.x
                + tex2D(_GrainTex, p.xz * 2).rgb * blend.y + tex2D(_GrainTex, p.xy * 2).rgb * blend.z;
            float top = step(.0001, n.y);
            float side = 1 - step(.0001, abs(n.y));
            float3 color = bodyColor * (.91 + grain.r * .18);
            float height = (grain.r - .5) * .0013;
            float smoothness = _Glossiness * (.84 + grain.g * .3);
            float metallic = _Metallic;
            float occlusion = .94 + grain.b * .06;
            float3 emission = 0;
            float2 topUnit = p.xz / size.xz;
            float2 face = abs(n.x) > abs(n.z) ? p.zy : p.xy;
            float2 faceSize = abs(n.x) > abs(n.z) ? size.zy : size.xy;
            float2 cells = max(1, floor(faceSize / float2(2.4, 1.2)));
            float2 cellSize = faceSize / cells;
            float2 cell = (frac((face + faceSize * .5) / cellSize) - .5) * cellSize;
            float rim = min(.045, min(cellSize.x, cellSize.y) * .14);
            float2 panelHalf = cellSize * .5 - rim;
            float recessDistance = max(Rectangle(cell, panelHalf), (abs(cell.x) + abs(cell.y) - panelHalf.x - panelHalf.y + rim * .65) * .7071);
            float panel = Fill(recessDistance) * side * step(.075, faceSize.y);
            color *= 1 - panel * .21;
            height -= panel * .0015;
            occlusion *= 1 - Stroke(recessDistance, rim * .18) * side * .18;
            float paintTab = Fill(Rectangle(cell - float2(-panelHalf.x * .58, 0), float2(panelHalf.x * .12, panelHalf.y * .5))) * panel;
            color = lerp(color, accent * .77, paintTab * .72);
            float boltRadius = min(.027, rim * .43);
            float2 boltPoint = abs(cell) - max(0, panelHalf - rim * .8);
            float bolt = Fill(length(boltPoint) - boltRadius) * panel;
            float boltSlot = Stroke(boltPoint.x + boltPoint.y * .35, boltRadius * .17) * bolt;
            color = lerp(color, float3(.65,.7,.71), bolt * .8);
            color *= 1 - boltSlot * .75;
            height += bolt * .001;

            if (_Finish < .5 || _Finish > 11.5)
            {
                // Cast concrete with exposed aggregate, fine pores and restrained inset color tabs.
                float2 aggregateUV = abs(n.y) > max(abs(n.x), abs(n.z)) ? p.xz : abs(n.x) > abs(n.z) ? p.yz : p.xy;
                float aggregate = tex2D(_GrainTex, aggregateUV / 64).g;
                color *= .78 + grain.b * .14 + aggregate * .22;
                height += (grain.b - .8) * .002;
                float mark = Stroke(length(p.xz) - min(min(size.x, size.z) * .16, .18), min(.012, min(size.x, size.z) * .012)) * top;
                color = lerp(color, accent, mark * .65);
                if (_Finish > 11.5)
                {
                    // Checkpoints alone retain a perimeter marking; paint is flush with collision.
                    float edgeDistance = min(size.x * .5 - abs(p.x), size.z * .5 - abs(p.z));
                    float frame = Stroke(edgeDistance - min(min(size.x, size.z) * .055, .12), clamp(min(size.x, size.z) * .025, .003, .055)) * top;
                    float check = Stroke(topUnit.y - (topUnit.x < -.025 ? -topUnit.x - .08 : topUnit.x * .8 - .035), .028)
                        * Fill(abs(topUnit.x) - .18) * top;
                    color = lerp(color, float3(.18,.78,.43), max(frame, check));
                    emission += float3(.025,.11,.055) * max(frame, check);
                }
            }
            else if (_Finish < 1.5)
            {
                float brushing = Stroke(frac(p.y * 85) - .5, .09);
                color *= 1 - brushing * .035;
                height *= .4;
            }
            else if (_Finish < 2.5)
            {
                float2 direction = UNITY_ACCESS_INSTANCED_PROP(Part, _TravelDirection).xy;
                float2 arrowUV = float2(dot(topUnit, float2(direction.y, -direction.x)), dot(topUnit, direction)) * 3;
                float arrows = max(Chevron(arrowUV - float2(0,.58)), Chevron(-arrowUV - float2(0,.58))) * top;
                float centre = Fill(length(topUnit) - .09) * top;
                color = lerp(color, accent * .7 + .2, max(arrows, centre));
                float grip = Stroke(frac((p.x + p.z) * 7) - .5, .08) * top;
                color *= 1 - grip * .14; height += grip * .0008;
            }
            else if (_Finish < 3.5)
            {
                float pitch = clamp(size.z / 3, .08, 1.2);
                float beltSpeed = UNITY_ACCESS_INSTANCED_PROP(Part, _BeltSpeed);
                float beltZ = p.z - _BeltTime * beltSpeed;
                float rib = Stroke(frac(beltZ / .14) - .5, .065) * top;
                color *= 1 - rib * .42; height -= rib * .0016;
                float signZ = beltSpeed < 0 ? -1 : 1;
                float2 arrowUV = float2(p.x / max(.025, min(.6, size.x * .32)), (frac(beltZ * signZ / pitch + .5) - .5) * 2);
                float arrow = Chevron(arrowUV) * top;
                color = lerp(color, float3(.91,.75,.36), arrow * .92);
                float radius = min(faceSize.y * .3, faceSize.x * .12);
                float2 rollerPoint = float2(abs(face.x) - (faceSize.x * .5 - radius * 1.4), face.y);
                float roller = Fill(length(rollerPoint) - radius) * side;
                color = lerp(color, float3(.5,.57,.59), roller);
                color *= 1 - Stroke(length(rollerPoint) - radius * .57, radius * .12) * roller * .6;
                metallic = lerp(.05, .65, roller);
            }
            else if (_Finish < 4.5)
            {
                // Hazard has a red vented face and repeated amber Xs, never a footprint frame.
                color = lerp(color, float3(.31,.055,.042), top * .9);
                float vent = Fill(Rectangle(float2((frac(p.x / .3) - .5) * .3, (frac(p.z / .8) - .5) * .8), float2(.07,.28))) * top;
                color *= 1 - vent * .65; height -= vent * .002;
                float2 signUV = (frac(topUnit * 2 + .5) - .5) * 2;
                float danger = Stroke(abs(signUV.x) - abs(signUV.y), .08) * Fill(max(abs(signUV.x),abs(signUV.y)) - .29) * top;
                color = lerp(color, float3(1,.43,.12), danger);
                emission = float3(.14,.016,.005) * top;
            }
            else if (_Finish < 5.5)
            {
                float along = dot(p.yz, normalize(size.yz));
                float rib = Stroke(frac(along / .18) - .5, .08) * top;
                color *= 1 - rib * .18; height += rib * .001;
                float2 chevronUV = float2(topUnit.x * 3, frac(along / max(.1, min(1.6, length(size.yz) / 3))) * 2 - 1);
                color = lerp(color, accent, Chevron(chevronUV) * top * .7);
            }
            else if (_Finish < 6.5)
            {
                float across = abs(topUnit.x) * length(float2(size.x, size.y * 2));
                float brushing = Stroke(frac(p.z * 90) - .5, .12);
                float inlay = Stroke(frac(across / 1.25) - .5, .025) * top;
                color *= 1 - brushing * .07;
                color = lerp(color, accent, inlay * .7);
                metallic = .65; height *= .3;
            }
            else if (_Finish < 7.5)
            {
                float2 emblemUV = face / max(faceSize, .001);
                float medallion = Fill(length(emblemUV) - .25) * side;
                float dome = Fill(length(emblemUV - float2(0,-.02)) - .13) * step(-.04, emblemUV.y);
                float bellFoot = Fill(Rectangle(emblemUV - float2(0,-.055), float2(.15,.025)));
                float bell = max(dome, bellFoot) * medallion;
                color = lerp(color, float3(.76,.56,.23), medallion * .9);
                color *= 1 - bell * .7;
                metallic = .65; height += medallion * .0012;
            }
            else if (_Finish < 8.5)
            {
                float2 glyph = top > .5 ? topUnit : face / faceSize;
                float target = max(Stroke(length(glyph) - .25, .035), Fill(Rectangle(glyph, float2(.035,.17))));
                target = max(target, Fill(Rectangle(glyph, float2(.17,.035))));
                color = lerp(float3(.11,.2,.26), accent * .55 + .45, target);
                emission = accent * target * .18;
            }
            else if (_Finish < 9.5)
            {
                float band = Fill(abs(face.y / faceSize.y + .15) - .22) * side;
                float stripe = Fill(frac((face.x + face.y) / .26) - .5);
                color = lerp(color, lerp(float3(.09,.1,.11), float3(.98,.66,.15), stripe), band);
                metallic = .5;
            }
            else if (_Finish < 10.5)
            {
                float ribs = Stroke(frac(face.x * 30) - .5, .14) * side;
                color *= 1 - ribs * .18; height -= ribs * .0006;
                metallic = 0; smoothness = .15;
            }
            else
            {
                color = bodyColor;
                emission = UNITY_ACCESS_INSTANCED_PROP(Part, _EmissionColor).rgb;
                smoothness = .7; metallic = .05;
                height = 0;
            }
            output.Albedo = saturate(color);
            output.Normal = SurfaceNormal(data, height);
            output.Metallic = metallic;
            output.Smoothness = saturate(smoothness);
            output.Occlusion = saturate(occlusion);
            output.Emission = emission;
            output.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
