// Plants: baked billboards (tools/blender/plant_bake.py), lit here by the
// game's own sun and sky.
//
// Each quad stands upright facing the camera, which never rotates, and shows
// a plant rendered from that same direction. The atlas holds what the surface
// is — albedo and coverage, the normal in the bake camera's frame, the plant's
// own occlusion of the sky — and one thing about the light: how much of the
// sun each leaf gets (cos x visibility, baked under the game's own sun, which
// never moves). A card cannot know that a crown shades its own inside; without
// it every leaf was lit like the crown's surface and the treeline came out
// flat and bright.
//
// So the light is put together here rather than by URP's PBR function:
//   sun      baked sunlit x the shadow map (what other things cast on it)
//   sky      the same spherical harmonics as the rest of the scene, at the
//            baked normal, x baked occlusion x screen-space AO
//   through  a leaf turned from the sun still passes some of it
//
// Vertex data (Plants.cs): uv0 the atlas texel; uv1.x height above the root
// as a fraction of the plant (for sway), uv1.y +1 or -1 (mirrored: the image
// is flipped, so the normal's x is too); colour rgb a per-plant tint, a its
// sway phase.
Shader "LOV/Foliage"
{
    Properties
    {
        _Albedo ("Albedo (RGB) + coverage (A)", 2D) = "white" {}
        _Normal ("Normal xy (RG, bake frame), occlusion (B), sunlit (A)", 2D) = "gray" {}
        _Cutoff ("Coverage cutoff", Range(0.05, 0.95)) = 0.5
        _Pitch ("Bake pitch (degrees below horizontal)", Float) = 6
        _Translucency ("Light through thin leaves", Range(0, 1)) = 0.35
        _Wind ("Sway at the top (m)", Float) = 0.06
        _MipBias ("Mip bias near, far (m: from, to)", Vector) = (0.5, 1.75, 40, 90)
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull Off

        HLSLINCLUDE
        #include "LOVLit.hlsl"

        TEXTURE2D(_Albedo); SAMPLER(sampler_Albedo);
        TEXTURE2D(_Normal); SAMPLER(sampler_Normal);
        CBUFFER_START(UnityPerMaterial)
            float4 _Albedo_ST;
            half _Cutoff;
            float _Pitch;
            half _Translucency;
            float _Wind;
            float4 _MipBias;
        CBUFFER_END

        // Every map is read a little blurrier than the screen asks. A crown
        // baked at leaf scale has light and dark within a pixel or two, and
        // TAA's jitter resamples that differently every frame: the canopy
        // shimmered at 2.1 mean |dL*|, most of the frame's flicker.
        // The bias grows with distance: a plant at 30 m has leaves many pixels
        // across and keeps them sharp; the treeline at 90 m, where a leaf is a
        // pixel, becomes the soft mass a canopy is at that distance.
        float MipBias(float3 positionWS)
        {
            float d = length(positionWS - GetCameraPositionWS());
            return lerp(_MipBias.x, _MipBias.y, saturate((d - _MipBias.z) / (_MipBias.w - _MipBias.z)));
        }
        half4 AlbedoAt(float2 uv, float bias) { return SAMPLE_TEXTURE2D_BIAS(_Albedo, sampler_Albedo, uv, bias); }
        half4 SurfaceAt(float2 uv, float bias) { return SAMPLE_TEXTURE2D_BIAS(_Normal, sampler_Normal, uv, bias); }

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            float2 plant : TEXCOORD1;
            half4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            half4 tint : TEXCOORD2;          // rgb tint, a mirror sign
            half fog : TEXCOORD3;
        };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            float3 p = TransformObjectToWorld(i.positionOS.xyz);
            // Sway: the top moves, the root does not; two slow waves out of phase.
            float h = saturate(i.plant.x);
            float phase = i.color.a * 6.2832;
            float t = _LovTime;
            float sway = (sin(t * 1.3 + phase + p.x * 0.21) * 0.7 + sin(t * 2.9 + phase * 1.7) * 0.3) * _Wind * h * h;
            p.x += sway;
            o.positionWS = p;
            o.positionCS = TransformWorldToHClip(p);
            o.uv = i.uv;
            o.tint = half4(i.color.rgb, i.plant.y);
            o.fog = ComputeFogFactor(o.positionCS.z);
            return o;
        }

        // The bake camera's frame in the world: right, up, and toward the camera.
        void BakeFrame(out float3 R, out float3 U, out float3 B)
        {
            float s, c;
            sincos(radians(_Pitch), s, c);
            R = float3(1, 0, 0);
            U = float3(0, c, s);
            B = float3(0, s, -c);
        }

        float3 SurfaceNormal(Varyings i, half4 nt)
        {
            float3 R, U, B;
            BakeFrame(R, U, B);
            float2 xy = nt.rg * 2 - 1;
            xy.x *= i.tint.a;
            float z = sqrt(saturate(1 - dot(xy, xy)));
            return normalize(R * xy.x + U * xy.y + B * z);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            half4 Frag(Varyings i) : SV_Target
            {
                float bias = MipBias(i.positionWS);
                half4 a = AlbedoAt(i.uv, bias);
                clip(a.a - _Cutoff);
                half4 nt = SurfaceAt(i.uv, bias);
                float3 n = SurfaceNormal(i, nt);
                half3 albedo = a.rgb * i.tint.rgb;

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                AmbientOcclusionFactor ssao = GetScreenSpaceAmbientOcclusion(screenUV);

                half3 direct = sun.color * (nt.a * sun.shadowAttenuation) * ssao.directAmbientOcclusion;
                half3 sky = SampleSH(n) * (nt.b * ssao.indirectAmbientOcclusion);
                // Through the leaf: from the side away from the sun, as much as
                // its neighbours let the sun reach it (the occlusion stands in).
                half through = saturate(-dot(n, sun.direction)) * _Translucency * nt.b * sun.shadowAttenuation;
                half3 c = albedo * (direct + sky + sun.color * through);
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            Varyings ShadowVert(Attributes i)
            {
                Varyings o = Vert(i);
                // The card faces the camera, not the sun; pushing it along the
                // light keeps its own face from shadowing itself.
                o.positionCS = LOV_ShadowPositionCS(o.positionWS, float3(0, 0, -1));
                return o;
            }
            half4 ShadowFrag(Varyings i) : SV_Target
            {
                clip(AlbedoAt(i.uv, MipBias(i.positionWS)).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            half DepthFrag(Varyings i) : SV_Target
            {
                clip(AlbedoAt(i.uv, MipBias(i.positionWS)).a - _Cutoff);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalsFrag(Varyings i) : SV_Target
            {
                float bias = MipBias(i.positionWS);
                clip(AlbedoAt(i.uv, bias).a - _Cutoff);
                return LOV_DepthNormalsOutput(SurfaceNormal(i, SurfaceAt(i.uv, bias)));
            }
            ENDHLSL
        }
    }
}
