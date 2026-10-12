// Combat light (CombatView): muzzle flashes, tracers, fireballs. Additive and
// HDR, so the bloom (Post.asset, threshold 1.15) carries them. By what each one
// is (uv.w):
//   0 flash     a hot core and four to six ragged spikes, computed
//   1 streak    a tracer or a round's trace: bright along its axis, soft at the ends
//   2 ball      a soft ball of light, hottest at the heart
//   3 sprite    a frame of the sheet of fire (Art/FX/fx_glow.png, tools/art/fx_atlas.py)
//               fading into the next: a muzzle's tongue of gas, the star out of a
//               flash hider, a shell's fire
// uv:  for 0 to 2, xy across the quad and z a seed; for 3, xy the frame's corner
//      in the sheet and z how far into the next frame
// uv2: for 3, xy the same corner in the next frame
Shader "LOV/FX Glow"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Sheets (colour on black)", 2D) = "black" {}
    }
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

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            struct A { float4 positionOS : POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; float4 uv2 : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; float4 uv2 : TEXCOORD1; half fog : TEXCOORD2; };

            V Vert(A i)
            {
                V o;
                o.positionCS = TransformWorldToHClip(i.positionOS.xyz);
                o.color = i.color;
                o.uv = i.uv;
                o.uv2 = i.uv2;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float n) { return frac(sin(n) * 43758.5453); }

            half4 Frag(V i) : SV_Target
            {
                float kind = i.uv.w;
                half3 c;
                if (kind > 2.5)
                {
                    c = lerp(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy).rgb,
                             SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv2.xy).rgb, i.uv.z) * i.color.rgb * i.color.a;
                }
                else
                {
                    float2 p = i.uv.xy * 2 - 1;
                    float seed = i.uv.z;
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
                        shape = saturate(1 - (r + t));
                        shape *= shape;
                    }
                    c = i.color.rgb * shape * i.color.a;
                }
                c = MixFogColor(c, half3(0, 0, 0), i.fog);      // light fades into the haze, never lifts it
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
