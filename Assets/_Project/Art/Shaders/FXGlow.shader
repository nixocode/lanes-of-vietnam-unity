// Combat light (CombatView): muzzle flashes, tracers, fireballs. Additive and
// HDR, so the bloom (Post.asset, threshold 1.15) carries them; shaped here,
// with no texture, by what each one is (uv.w):
//   0 flash     a hot core and four to six ragged spikes
//   1 tracer    a streak: bright along its axis, soft at the ends
//   2 fireball  a turbulent ball, hottest at the heart
Shader "LOV/FX Glow"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; half fog : TEXCOORD1; };

            V Vert(A i)
            {
                V o;
                o.positionCS = TransformWorldToHClip(i.positionOS.xyz);
                o.color = i.color;
                o.uv = i.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float n) { return frac(sin(n) * 43758.5453); }

            half4 Frag(V i) : SV_Target
            {
                float2 p = i.uv.xy * 2 - 1;
                float seed = i.uv.z;
                float kind = i.uv.w;
                float shape;
                if (kind < 0.5)
                {
                    float r = length(p);
                    float a = atan2(p.y, p.x);
                    float spikes = pow(saturate(cos(a * (4 + floor(Hash(seed) * 3)) + seed * 6.28)), 6);
                    shape = exp(-r * r * 18) + spikes * saturate(1 - r) * (0.5 + 0.5 * Hash(seed + 1));
                }
                else if (kind < 1.5)
                {
                    // p.x along the streak, p.y across it.
                    shape = exp(-p.y * p.y * 9) * smoothstep(1, 0.6, abs(p.x));
                }
                else
                {
                    float r = length(p);
                    float t = sin(p.x * 7 + seed * 13) * sin(p.y * 6 - seed * 7) * 0.15;
                    shape = saturate(1 - (r + t)) ;
                    shape *= shape;
                }
                half3 c = i.color.rgb * shape * i.color.a;
                c = MixFogColor(c, half3(0, 0, 0), i.fog);      // light fades into the haze, never lifts it
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
