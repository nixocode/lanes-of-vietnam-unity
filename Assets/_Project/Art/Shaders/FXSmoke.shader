// Combat smoke and dust (CombatView): gun smoke, shell smoke, thrown dirt,
// bullet strikes, blood, brass, smoke screens. Billboards over the scene, each a
// frame of a sheet of real smoke (Art/FX/fx_smoke.png, tools/art/fx_atlas.py)
// fading into the next, tinted by the vertex, lit by the scene's own sun and
// sky, and faded where they meet the ground so a puff never shows a hard line
// through the grass.
//
// They were shaped by noise, with no texture: three octaves of it a pixel, for
// every pixel of every puff, and what it drew was a soft blob whatever it was
// meant to be.
//
// The sheet holds colour times alpha (a puff's clear edge then has no colour of
// its own to leak into the filtering), so the blend is One, OneMinusSrcAlpha.
// uv:  xy the frame's corner in the sheet, z how far into the next frame, w the puff's own density
// uv2: xy the same corner in the next frame, z the metres it fades into what is behind it over
Shader "LOV/FX Smoke"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Sheets (colour times alpha, alpha)", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One OneMinusSrcAlpha
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            struct A { float4 positionOS : POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; float4 uv2 : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; float4 uv2 : TEXCOORD1; half fog : TEXCOORD2; };

            V Vert(A i)
            {
                V o;
                o.positionCS = TransformWorldToHClip(i.positionOS.xyz);
                // Lit once a corner, not once a pixel: the sky from above and the sun. (The light and
                // shade across a puff is in the sheet.)
                Light sun = GetMainLight();
                half3 light = SampleSH(half3(0, 1, 0)) + sun.color * 0.62;
                o.color = half4(i.color.rgb * light, i.color.a * i.uv.w);
                o.uv = i.uv;
                o.uv2 = i.uv2;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                half4 t = lerp(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy),
                               SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv2.xy), i.uv.z);
                // Half of a puff's quad is the clear margin round the smoke. Nothing is drawn there, and
                // nothing is read for it either: the scene's depth is the dear part of this, and a barrage's
                // smoke was reading it for a screenful of pixels that came to nothing.
                clip(t.a - 0.004);

                // Soft where it meets the ground and the plants.
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float own = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                half k = saturate(i.color.a * saturate((scene - own) / i.uv2.z));

                half a = t.a * k;
                half3 c = t.rgb * i.color.rgb * k;
                c = MixFogColor(c, unity_FogColor.rgb * a, i.fog);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
