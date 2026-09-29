// The ground. Vertex colour carries the terrain's own masks, computed from the
// same features as its heights (GroundView.Masks): r track, g disturbed earth,
// b wet ruts. The dirt is where the track is because both come from one
// function — the three.js build kept a second copy of the track's curve in a
// shader and the two drifted apart.
//
// Grey-box for now: the masks pick between flat colours. The art pass replaces
// the colours with scanned PBR layers and keeps the masks.
Shader "LOV/Ground"
{
    Properties
    {
        _GrassColor ("Grass", Color) = (0.20, 0.25, 0.11, 1)
        _DirtColor ("Track dirt", Color) = (0.36, 0.27, 0.18, 1)
        _EarthColor ("Disturbed earth", Color) = (0.29, 0.22, 0.15, 1)
        _MudColor ("Wet ruts", Color) = (0.18, 0.14, 0.10, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "LOVLit.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _GrassColor;
            half4 _DirtColor;
            half4 _EarthColor;
            half4 _MudColor;
            half _Smoothness;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            half4 Frag(Varyings i) : SV_Target
            {
                half track = i.color.r, earth = i.color.g, wet = i.color.b;
                half3 albedo = lerp(_GrassColor.rgb, _DirtColor.rgb, saturate(track * 1.4));
                albedo = lerp(albedo, _EarthColor.rgb, earth);
                albedo = lerp(albedo, _MudColor.rgb, wet);
                half smooth = _Smoothness + wet * 0.35;
                return LOV_Shade(i.positionWS, i.normalWS, i.positionCS, i.fog, albedo, smooth, 1, 0);
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
