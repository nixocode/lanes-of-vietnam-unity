// The mountains (MountainView): a forested massif 1-22 km away.
//
// At that range what reads is the light on the slopes and the air in front
// of them, so the surface is simple — forest with lighter grassy patches and
// the mottling of crowns, lit by the scene's own sun and sky — and the air
// is its own: the scene's fog (tuned for a battlefield 150 m deep) would wipe
// out everything past a kilometre, so the mountains fade toward the horizon's
// colour over their own distance instead.
Shader "LOV/Mountain"
{
    Properties
    {
        _Forest ("Forest albedo", Color) = (0.24, 0.30, 0.19, 1)
        _Grass ("Grassy patches albedo", Color) = (0.38, 0.40, 0.25, 1)
        _Haze ("Air: distance for 63% (m), start (m)", Vector) = (6500, 600, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        HLSLINCLUDE
        #include "LOVLit.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Forest;
            half4 _Grass;
            float4 _Haze;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
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

            float Hash(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p), u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 w = i.positionWS.xz;
                float3 n = normalize(i.normalWS);
                // Crowns: a bump at about 25 m, felt only through the light.
                float e = 6;
                float c0 = Noise(w / 25), cx = Noise((w + float2(e, 0)) / 25), cz = Noise((w + float2(0, e)) / 25);
                n = normalize(n + float3(c0 - cx, 0, c0 - cz) * 0.9);
                // Grassy clearings on the gentler, higher ground; forest elsewhere.
                float patches = Noise(w / 420) * 0.7 + Noise(w / 140) * 0.3;
                float grass = smoothstep(0.62, 0.8, patches) * smoothstep(0.75, 0.95, n.y);
                // Colours are given as sRGB; the lighting wants linear.
                half3 albedo = lerp(SRGBToLinear(_Forest.rgb), SRGBToLinear(_Grass.rgb), grass) * (0.75 + 0.5 * c0);
                half3 lit = LOV_Shade(i.positionWS, n, i.positionCS, 0, albedo, 0.05, 1, 0).rgb;

                float d = length(i.positionWS - GetCameraPositionWS());
                float air = 1 - exp(-max(0, d - _Haze.y) / _Haze.x);
                half3 c = lerp(lit, unity_FogColor.rgb, air);
                return half4(c, 1);
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
