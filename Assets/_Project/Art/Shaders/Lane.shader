// The lane selector (Deployer / LaneMarks): light laid on the ground, not paint.
//
// With a card in hand each lane is a ribbon of light on its own ground, the
// one the card would go to brighter, with chevrons running up it the way the
// squad will go; a call-in has a disc the size of what it does and a beam
// standing on its middle, because the camera sits at eye level and anything
// flat on the ground is seen edge-on. It is drawn where it can be seen, and
// again, fainter, where grass, a wall or a man stands in front of it: the
// selection ring's two passes (Ring.shader), for the same reason.
//
//   _Shape 0   ribbon   uv.x metres along the lane, uv.y 0..1 across it
//   _Shape 1   disc     uv -1..1 from its middle
//   _Shape 2   beam     uv.x 0..1 across, uv.y 0..1 up
Shader "LOV/Lane"
{
    Properties
    {
        _Color ("Colour (rgb) and strength (a)", Color) = (1, 0.74, 0.26, 1)
        _Shape ("Shape: 0 ribbon, 1 disc, 2 beam", Float) = 0
        _Flow ("Chevrons run this way along x (+1, -1; 0 none)", Float) = 0
        _Focus ("World x the pointer is at", Float) = 0
        _Reach ("Metres either side of the pointer that stay bright", Float) = 16
        _Occluded ("Strength where something is in front of it", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Shape;
            float _Flow;
            float _Focus;
            float _Reach;
            half _Occluded;
        CBUFFER_END
        float _LovTime;

        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float worldX : TEXCOORD1; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            float3 p = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(p);
            o.uv = i.uv;
            o.worldX = p.x;
            return o;
        }

        half Ribbon(Varyings i)
        {
            float across = abs(i.uv.y * 2 - 1);                         // 0 down the middle, 1 at the edge
            float w = fwidth(across) * 1.5;
            half edge = 1 - smoothstep(0.94, 1.0, across);              // soft outer edge
            // A rail down each side: a thin bright line, and a glow inside it.
            half rail = (1 - smoothstep(0.0, 0.035 + w, abs(across - 0.88))) * 0.9
                      + (1 - smoothstep(0.0, 0.22, abs(across - 0.88))) * 0.18;
            half fill = 0.13;
            // Chevrons: lines swept back from the middle, moving the way the squad will go.
            float s = (i.uv.x * _Flow - across * 1.6 - _LovTime * 2.4) / 4.5;
            float f = frac(s);
            float cw = fwidth(s) * 1.5;
            half chev = smoothstep(0.40 - cw, 0.45, f) * (1 - smoothstep(0.55, 0.60 + cw, f));
            chev *= abs(_Flow) * (1 - smoothstep(0.55, 0.80, across));
            // Bright where the pointer is, fading up and down the lane from it.
            float d = (i.worldX - _Focus) / max(1, _Reach);
            half near = 0.30 + 0.70 * exp(-d * d);
            return (fill + rail + chev * 0.55) * edge * near;
        }

        half Disc(Varyings i)
        {
            float r = length(i.uv);
            float w = fwidth(r) * 1.5;
            half inside = 1 - smoothstep(1 - w, 1, r);
            half rim = smoothstep(0.92 - w, 0.96, r) * inside;
            // A ring going out from the middle, over and over: it is live, it has not landed.
            float p = frac(_LovTime * 0.6);
            half pulse = (1 - smoothstep(0, 0.05 + w, abs(r - p))) * (1 - p) * inside;
            // Ticks at the four quarters.
            float2 a = abs(i.uv);
            half tick = step(min(a.x, a.y), 0.02 + w * 0.5) * smoothstep(0.72, 0.78, r) * inside;
            return 0.06 * inside + rim * 0.85 + pulse * 0.5 + tick * 0.6;
        }

        half Beam(Varyings i)
        {
            float x = abs(i.uv.x * 2 - 1);
            half core = pow(saturate(1 - x), 2.5);
            half up = pow(saturate(1 - i.uv.y), 1.6);
            return core * up * (0.75 + 0.25 * sin(_LovTime * 5 + i.uv.y * 14));
        }

        half Shape(Varyings i) { return saturate(_Shape < 0.5 ? Ribbon(i) : _Shape < 1.5 ? Disc(i) : Beam(i)); }
        ENDHLSL

        // Where it can be seen: as it is.
        Pass
        {
            Name "LaneVisible"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(_Color.rgb, Shape(i) * _Color.a); }
            ENDHLSL
        }

        // Where grass, a wall or a man is in front of it: faintly, through them. The far lane's
        // ground is mostly behind the grass between it and the lens, and a lane nobody can see is
        // not a choice.
        Pass
        {
            Name "LaneHidden"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Greater Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(_Color.rgb, Shape(i) * _Color.a * _Occluded); }
            ENDHLSL
        }
    }
}
