// The ground. Vertex colour carries the terrain's own masks, computed from the
// same features as its heights (GroundView.Masks): r track, g disturbed earth,
// b wet ruts. The dirt is where the track is because both come from one
// function — the three.js build kept a second copy of the track's curve in a
// shader and the two drifted apart.
//
// Four scanned layers (tools/art/terrain_pack.py; sources in ASSETS.md), an
// albedo (+ height) and a normal each: 0 grass floor, 1 verge, 2 track,
// 3 disturbed earth. Separate textures rather than arrays, because only 2D
// textures can be crunched for the download. The masks
// say how much of each; each layer's height says which wins where they meet, so
// dirt shows first in the low gaps of the grass rather than in a smooth fade.
//
// Tiling is the enemy at a lens this long: a camera looking along 100 m of
// ground sees hundreds of repeats. Three things break it up:
//   - the scans were flattened at low frequency when packed, so no single
//     tile has a patch that repeats;
//   - large-scale variation comes back as noise in world space (it never
//     repeats), in brightness and in where the verge breaks into the grass;
//   - each albedo has a second read, rotated 37 degrees and at 0.8 of the
//     frequency. Near the camera, world-space noise picks one or the other in
//     ~5 m patches, so neighbouring stretches never repeat; far off the two
//     are averaged, which dissolves the grid a single period would draw.
Shader "LOV/Ground"
{
    Properties
    {
        _Albedo0 ("Grass floor: albedo (RGB) + height (A)", 2D) = "grey" {}
        _Normal0 ("Grass floor: normal", 2D) = "bump" {}
        _Albedo1 ("Verge: albedo + height", 2D) = "grey" {}
        _Normal1 ("Verge: normal", 2D) = "bump" {}
        _Albedo2 ("Track: albedo + height", 2D) = "grey" {}
        _Normal2 ("Track: normal", 2D) = "bump" {}
        _Albedo3 ("Disturbed earth: albedo + height", 2D) = "grey" {}
        _Normal3 ("Disturbed earth: normal", 2D) = "bump" {}
        _Tile ("Metres per repeat, per layer", Vector) = (1.6, 3.0, 2.2, 1.5)
        _Smooth ("Smoothness, per layer", Vector) = (0.08, 0.1, 0.14, 0.1)
        _NormalScale ("Normal strength", Float) = 1
        _BlendDepth ("Height-blend depth", Range(0.02, 1)) = 0.2
        _Macro ("Macro variation: scale (1/m), brightness, verge patches, far averaging", Vector) = (0.035, 0.22, 0.55, 0.45)
        _Cavity ("Cavity occlusion at height 0", Range(0, 1)) = 0.3
        _GradScale ("Texture gradient scale (1 = no mip bias)", Range(1, 3)) = 2
        [Enum(Off,0,Albedo,1,Normal,2,Layers,3,Lit flat normal,4,Grey card,5)] _DebugView ("Debug view", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "LOVLit.hlsl"

        TEXTURE2D(_Albedo0); SAMPLER(sampler_Albedo0);
        TEXTURE2D(_Albedo1); TEXTURE2D(_Albedo2); TEXTURE2D(_Albedo3);
        TEXTURE2D(_Normal0); SAMPLER(sampler_Normal0);
        TEXTURE2D(_Normal1); TEXTURE2D(_Normal2); TEXTURE2D(_Normal3);

        CBUFFER_START(UnityPerMaterial)
            float4 _Tile;
            half4 _Smooth;
            half _NormalScale;
            half _BlendDepth;
            float4 _Macro;
            half _Cavity;
            half _DebugView;
            float _GradScale;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            half4 color : TEXCOORD2;
            half fog : TEXCOORD3;
        };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.color = i.color;
            o.fog = ComputeFogFactor(o.positionCS.z);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            // Value noise on a hashed lattice, smooth-stepped: cheap, no texture,
            // and it never repeats over the map.
            float Hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }
            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 w = i.positionWS.xz;
                float3 N = normalize(i.normalWS);
                float dist = length(i.positionWS - GetCameraPositionWS());

                // --- where each layer is ---------------------------------------
                float macro = ValueNoise(w * _Macro.x) * 0.6 + ValueNoise(w * _Macro.x * 3.7 + 17.3) * 0.4;
                float fine = ValueNoise(w * 0.9 + 5.1);
                half track = i.color.r, earth = i.color.g, wet = i.color.b;
                // The track's edge wanders: noise moves where the verge starts.
                float t = track + (macro - 0.5) * 0.3 + (fine - 0.5) * 0.12;
                // Thresholds keep the grey-box's visible dirt (mask > ~0.36, about
                // +-3.7 m): the track's centre is behind the berm from this
                // camera, so what reads as "the track" is mostly its edge.
                float wTrack = smoothstep(0.28, 0.52, t);
                float wVerge = smoothstep(0.06, 0.3, t) * (1 - wTrack);
                // Worn patches out in the grass, where the noise is high.
                wVerge = max(wVerge, smoothstep(0.58, 0.8, macro) * _Macro.z * (1 - wTrack));
                float4 wt = float4(1, wVerge, wTrack, 0);
                wt.x = saturate(1 - wVerge - wTrack);
                wt = lerp(wt, float4(0, 0, 0, 1), earth);

                // --- the layers ------------------------------------------------
                float4 invTile = 1 / _Tile;
                float2 dx = ddx(w) * _GradScale, dy = ddy(w) * _GradScale;
                // The second read: rotated about 37 degrees, at 0.8 of the
                // frequency, so its repeats land nowhere near the first's.
                const float2x2 rot = float2x2(0.7986, -0.6018, 0.6018, 0.7986);
                float2 wf = mul(rot, w) * 0.8 + 0.37;
                float2 dxf = mul(rot, dx) * 0.8, dyf = mul(rot, dy) * 0.8;
                half pick = smoothstep(0.3, 0.7, ValueNoise(w * 0.19 + 3.7));
                half far = lerp(pick, 0.5, saturate((dist - 25) / 45) * _Macro.w * 2);

                // One layer: the near read, the far read blended in with
                // distance, and the normal. Every map shares its sampler state.
                #define LOV_LAYER(k, A, NM) \
                    a[k] = lerp(SAMPLE_TEXTURE2D_GRAD(A, sampler_Albedo0, w * invTile[k], dx * invTile[k], dy * invTile[k]), \
                                SAMPLE_TEXTURE2D_GRAD(A, sampler_Albedo0, wf * invTile[k], dxf * invTile[k], dyf * invTile[k]), far); \
                    nt[k] = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(NM, sampler_Normal0, w * invTile[k], dx * invTile[k], dy * invTile[k]), _NormalScale);
                half4 a[4];
                half3 nt[4];
                LOV_LAYER(0, _Albedo0, _Normal0)
                LOV_LAYER(1, _Albedo1, _Normal1)
                LOV_LAYER(2, _Albedo2, _Normal2)
                LOV_LAYER(3, _Albedo3, _Normal3)
                #undef LOV_LAYER

                // Height blend: each layer's height plus its mask weight; whatever
                // stands within _BlendDepth of the highest shares the pixel.
                float4 hw = float4(a[0].a, a[1].a, a[2].a, a[3].a) * 0.5 + wt;
                hw *= step(0.001, wt);
                float top = max(max(hw.x, hw.y), max(hw.z, hw.w));
                float4 b = max(hw - (top - _BlendDepth), 0);
                b /= max(dot(b, 1), 1e-4);

                half3 albedo = a[0].rgb * b.x + a[1].rgb * b.y + a[2].rgb * b.z + a[3].rgb * b.w;
                half height = a[0].a * b.x + a[1].a * b.y + a[2].a * b.z + a[3].a * b.w;
                half3 n = normalize(nt[0] * b.x + nt[1] * b.y + nt[2] * b.z + nt[3] * b.w);
                half smooth = dot(_Smooth, b);

                // Large-scale brightness, so a field is not one flat tone.
                albedo *= 1 + (macro - 0.5) * 2 * _Macro.y;

                // Wet ruts: water settles in the low texels first; wet earth is
                // darker and glossier.
                half puddle = saturate(wet * 1.6 - height * 0.8);
                albedo *= lerp(1, 0.55, puddle);
                smooth = lerp(smooth, 0.62, puddle);

                // Normal map on the heightfield: uv is world xz, so the tangent
                // frame follows from the geometric normal alone.
                float3 T = normalize(float3(N.y, -N.x, 0));
                float3 B = normalize(float3(0, -N.z, N.y));
                float3 nWS = normalize(T * n.x + B * n.y + N * n.z);

                half occlusion = lerp(1 - _Cavity, 1, height);
                // Debug views, for tuning by capture (nopost=1): what the
                // surface is, without the light.
                if (_DebugView > 0.5)
                {
                    if (_DebugView < 1.5) return half4(albedo, 1);
                    if (_DebugView < 2.5) return half4(nWS * 0.5 + 0.5, 1);
                    if (_DebugView < 3.5) return half4(b.y + b.w, b.x + b.w, b.z, 1);
                    if (_DebugView < 4.5) return LOV_Shade(i.positionWS, N, i.positionCS, i.fog, albedo, smooth, 1, 0);
                    // An 18% grey card lying on the ground: its colour is the light.
                    return LOV_Shade(i.positionWS, N, i.positionCS, 0, 0.18, 0, 1, 0);
                }
                return LOV_Shade(i.positionWS, nWS, i.positionCS, i.fog, albedo, smooth, occlusion, 0);
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
            #pragma fragment Frag
            half Frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 Frag(Varyings i) : SV_Target { return LOV_DepthNormalsOutput(i.normalWS); }
            ENDHLSL
        }
    }
}
