// Combat smoke and dust (CombatView): shell smoke, thrown dirt, bullet
// strikes, smoke screens. Alpha-blended billboards, shaped by noise with no
// texture, lit by the scene's own sun and sky (a puff is brighter on top,
// where the sun reaches it), and faded where they meet the ground so a puff
// never shows a hard line through the grass.
// uv: xy the corner, z a seed, w the puff's own density (0..1).
Shader "LOV/FX Smoke"
{
    Properties
    {
        _SoftDistance ("Fade into geometry over (m)", Float) = 1.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
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

            CBUFFER_START(UnityPerMaterial)
                float _SoftDistance;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; half fog : TEXCOORD2; };

            V Vert(A i)
            {
                V o;
                o.positionWS = i.positionOS.xyz;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = i.color;
                o.uv = i.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
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
                float s = i.uv.z * 17.31;
                // Billows: the edge is broken by the noise, and the body is lumpy.
                float n = Noise(p * 2.2 + s) * 0.5 + Noise(p * 5.1 - s) * 0.3 + Noise(p * 11 + s * 2) * 0.2;
                float r = length(p) + (n - 0.5) * 0.95;
                float body = saturate(1 - r);
                float a = body * sqrt(body) * (0.35 + 1.3 * n * n) * i.uv.w;

                // Soft where it meets the ground and the plants.
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float own = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                a *= saturate((scene - own) / _SoftDistance);

                // Lit: the sky from above, the sun more on the upper side.
                Light sun = GetMainLight();
                half3 sky = SampleSH(half3(0, 1, 0));
                // Self-shadowed: the lumps catch the light, the hollows and the underside do not.
                half top = saturate(0.35 + p.y * 0.45 + (n - 0.5) * 0.9);
                half3 c = i.color.rgb * (sky * (0.7 + 0.3 * top) + sun.color * (0.25 + 0.55 * top));
                c = MixFog(c, i.fog);
                return half4(c, saturate(a * i.color.a));
            }
            ENDHLSL
        }
    }
}
