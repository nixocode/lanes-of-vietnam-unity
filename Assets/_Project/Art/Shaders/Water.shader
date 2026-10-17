// Water (RiverView, Rice Fields R3): the river and the paddies' still water over the ground the simulation has under
// them, lit as the ground is (LOVLit) and holding the sky.
//
//   ripples   worked out here, no texture: two layers of small waves, carried down the river by its current (_Flow, in
//             metres a second along the world's x and z) and barely moving on a paddy
//   depth     against the ground under it (the camera's depth texture): clear at its edge, muddy as it deepens, so a
//             bank runs down into it and a paddy's mud shows through a hand of water
//   the dead  up to sixteen stains (_LOV_Stains: x, z, radius, how red; set by RiverView from the men killed in the
//             water), each reddening the water round it, ragged at its edge, drifting down with the current and fading
//             over minutes (the owner: "tint of water changes to red if many bodies pile up")
Shader "LOV/Water"
{
    Properties
    {
        _Shallow ("Shallow albedo (sRGB)", Color) = (0.36, 0.31, 0.21, 1)
        _Deep ("Deep albedo (sRGB)", Color) = (0.20, 0.17, 0.11, 1)
        _Blood ("Blood in the water (sRGB)", Color) = (0.34, 0.05, 0.03, 1)
        _FadeDepth ("Depth to its full colour (m)", Range(0.05, 3)) = 0.9
        _Edge ("Depth to its full opacity (m)", Range(0.01, 1)) = 0.12
        _Opacity ("Opacity at depth", Range(0, 1)) = 0.93
        _Smooth ("Smoothness", Range(0, 1)) = 0.88
        _Murk ("Murk: how much of its own colour shows through the sky in it", Range(0, 1)) = 0.5
        _Ripple ("Ripple strength", Range(0, 2)) = 0.55
        _RippleScale ("Ripple size (1/m)", Range(0.1, 8)) = 1.6
        _Flow ("Current (m/s along world x, z)", Vector) = (0, -0.45, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "LOVLit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow, _Deep, _Blood;
                half _FadeDepth, _Edge, _Opacity, _Smooth, _Murk, _Ripple, _RippleScale;
                float4 _Flow;
            CBUFFER_END

            // The dead in the water (RiverView): x, z (the world's), radius, how red.
            float4 _LOV_Stains[16];
            float _LOV_StainCount;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screen : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screen = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p), u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            // The height of the ripples at w: two layers crossing, the larger carried by the current, the smaller across it.
            float Waves(float2 w, float t)
            {
                // (Three layers at odd angles to each other, so no grid shows.)
                float2 a = mul(float2x2(0.8, -0.6, 0.6, 0.8), w - _Flow.xz * t) * _RippleScale;
                float2 b = mul(float2x2(0.28, 0.96, -0.96, 0.28), w - _Flow.xz * t * 0.6 + float2(t * 0.11, -t * 0.07)) * _RippleScale * 2.3;
                float2 c = mul(float2x2(-0.5, 0.87, -0.87, -0.5), w - _Flow.xz * t * 1.3) * _RippleScale * 4.1;
                return Noise(a) * 0.55 + Noise(b + 17.3) * 0.3 + Noise(c + 41.9) * 0.15;
            }

            // How red the water is at w: each stain's share, ragged at its edge.
            float Red(float2 w, float t)
            {
                float red = 0;
                int n = (int)_LOV_StainCount;
                for (int k = 0; k < 16; k++)
                {
                    if (k >= n) break;
                    float4 s = _LOV_Stains[k];
                    float d = length(w - s.xy) / max(0.2, s.z);
                    float ragged = 0.75 + 0.5 * Noise((w - _Flow.xz * t * 0.8) * 0.9 + k * 7.1);
                    red += s.w * saturate(1.0 - d * d * ragged);
                }
                return saturate(red);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 w = i.positionWS.xz;
                // The ripples' slope, felt through the light: its normal tipped by the gradient.
                // (Fading with distance: far off the small waves are finer than a pixel and would only shimmer.)
                const float e = 0.08;
                float h0 = Waves(w, t), hx = Waves(w + float2(e, 0), t), hz = Waves(w + float2(0, e), t);
                float far = saturate(1 - length(i.positionWS - GetCameraPositionWS()) / 110);
                float k = _Ripple * 0.12 * far;
                float3 n = normalize(float3((h0 - hx) / e * k, 1, (h0 - hz) / e * k));

                // How much water is in front of what is under it, along the ray.
                float2 uv = i.screen.xy / i.screen.w;
                float bed = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float thick = max(0, bed - i.screen.w);

                half3 albedo = lerp(SRGBToLinear(_Shallow.rgb), SRGBToLinear(_Deep.rgb), saturate(thick / _FadeDepth));
                float red = Red(w, t);
                albedo = lerp(albedo, SRGBToLinear(_Blood.rgb), red);
                // Its surface holding the sky, and its own colour, the mud carried in it, showing through that (a lowland river
                // seen from low down is brown, not the sky's blue); blood in it is a film on it, dull, seen and not the sky.
                half4 gloss = LOV_Shade(i.positionWS, n, i.positionCS, i.fog, albedo, _Smooth * (1 - 0.7 * red), 1, 0);
                half4 dull = LOV_Shade(i.positionWS, n, i.positionCS, i.fog, albedo, 0.1, 1, 0);
                half4 c = lerp(gloss, dull, saturate(_Murk + 0.6 * red));
                // Clear at its very edge, its colour as it deepens; blood is opaque in it sooner.
                c.a = saturate(thick / _Edge) * lerp(_Opacity, 1, red * 0.6);
                return c;
            }
            ENDHLSL
        }
    }
}
