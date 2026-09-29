// Selection ring: a camera-facing ring at a man's chest (PLAN §5.1). Not on
// the ground — the camera sits at eye level, and a ground marker is seen
// edge-on and collapses to nothing (it cost an hour in the three.js build).
//
// Two passes. Where the man is visible the ring is drawn solid; where grass or
// a berm hides him it is drawn faint, so a selected squad is never lost in the
// foliage but a ring never pretends to be in front of the plant it is behind.
Shader "LOV/Ring"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.78, 0.30, 1)
        _Width ("Ring width", Range(0.03, 0.5)) = 0.16
        _Occluded ("Alpha when hidden", Range(0, 1)) = 0.28
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Width;
            half _Occluded;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
            o.uv = i.uv;
            return o;
        }

        half Ring(float2 uv)
        {
            float r = length(uv * 2 - 1);
            float aa = fwidth(r) * 1.5;
            float inner = 1 - _Width;
            return smoothstep(inner - aa, inner + aa, r) * (1 - smoothstep(1 - aa * 2, 1, r));
        }
        ENDHLSL

        Pass
        {
            Name "RingVisible"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(_Color.rgb, _Color.a * Ring(i.uv)); }
            ENDHLSL
        }

        Pass
        {
            Name "RingHidden"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Greater Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(_Color.rgb, _Color.a * _Occluded * Ring(i.uv)); }
            ENDHLSL
        }
    }
}
