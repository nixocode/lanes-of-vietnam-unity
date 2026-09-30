// The sky: a real photographed sky (Poly Haven, CC0), baked by
// tools/blender/sky_bake.py.
//
// The camera never rotates, only pans, so only one window of sky is ever in
// view. That window is a sharp 40 px/degree cut from a 16K HDRI; everywhere
// else — reflections, the edges of the glasses — falls back to the whole
// sphere at 1K. Both hold radiance / k as 8-bit sRGB; k and the scene's own
// exposure scale are applied here, so the sky is in the same light units as
// the sun and ambient built from it (SkyLighting).
Shader "LOV/Sky"
{
    Properties
    {
        _Window ("Window (sRGB, radiance / k)", 2D) = "grey" {}
        _Full ("Full sphere (sRGB, radiance / k)", 2D) = "grey" {}
        _K ("Bake exposure k", Float) = 1
        _Scale ("Scene light scale", Float) = 1
        _WindowAz ("Window azimuth min, max (deg)", Vector) = (-32, 32, 0, 0)
        _WindowEl ("Window elevation min, max (deg)", Vector) = (-2, 24, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Window); SAMPLER(sampler_Window);
            TEXTURE2D(_Full); SAMPLER(sampler_Full);
            CBUFFER_START(UnityPerMaterial)
                float _K;
                float _Scale;
                float4 _WindowAz;
                float4 _WindowEl;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float az = degrees(atan2(d.x, d.z));
                float el = degrees(asin(clamp(d.y, -1, 1)));

                float2 uvFull = float2((az + 180) / 360, (el + 90) / 180);
                half3 full = SAMPLE_TEXTURE2D(_Full, sampler_Full, uvFull).rgb;

                float2 uvWin = float2((az - _WindowAz.x) / (_WindowAz.y - _WindowAz.x),
                                      (el - _WindowEl.x) / (_WindowEl.y - _WindowEl.x));
                // Fade to the 1K sphere over the last few percent of the window.
                float2 edge = min(uvWin, 1 - uvWin);
                float inside = saturate(min(edge.x, edge.y) * 25);
                half3 win = SAMPLE_TEXTURE2D(_Window, sampler_Window, saturate(uvWin)).rgb;

                // Textures are sRGB, so the sampler has already decoded them to linear.
                half3 c = lerp(full, win, inside);
                return half4(c * _K * _Scale, 1);
            }
            ENDHLSL
        }
    }
}
