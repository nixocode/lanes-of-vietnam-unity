// Marks on the ground (CombatView): the soft contact shadow under each man —
// PLAN §12.7 item 4, "a blob ambient-occlusion shadow under each man so his
// feet sit on the ground" — and the scorch a shell or grenade leaves, which
// stays (§12.7 item 5, scars that stay).
//
// Flat quads a few centimetres over the ground, multiplied into what is
// already drawn, so a mark darkens the grass and soil under it and never
// adds light. Shaped with no texture: uv.xy the corner, uv.z a seed, uv.w the
// kind (0 contact shadow: a soft ellipse; 1 scorch: a ragged burn with a
// darker heart).
Shader "LOV/Ground Mark"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Blend DstColor Zero
        ZWrite Off
        Cull Off
        Offset -1, -1

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; };

            V Vert(A i)
            {
                V o;
                o.positionCS = TransformWorldToHClip(i.positionOS.xyz);
                o.color = i.color;
                o.uv = i.uv;
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p), u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 Frag(V i) : SV_Target
            {
                float2 p = i.uv.xy * 2 - 1;
                float r = length(p);
                float a;
                if (i.uv.w < 0.5)
                {
                    a = saturate(1 - r);
                    a = a * a * (3 - 2 * a);
                }
                else
                {
                    float s = i.uv.z * 31.7;
                    float n = Noise(p * 3 + s) * 0.6 + Noise(p * 8 - s) * 0.4;
                    a = saturate((1 - r + (n - 0.5) * 0.8) * 1.6);
                    a *= 0.75 + 0.25 * saturate(1 - r * 1.8);
                }
                // Multiply: 1 leaves the ground as it is, the colour darkens it.
                half3 m = lerp(half3(1, 1, 1), i.color.rgb, a * i.color.a);
                return half4(m, 1);
            }
            ENDHLSL
        }
    }
}
