// Shared lighting for this project's own shaders.
//
// Everything lit goes through URP's UniversalFragmentPBR, the same function
// URP/Lit uses, so a custom shader and a stock one standing side by side are
// lit by the same model — sun, soft shadows, sky ambient, screen-space AO and
// fog — and differ only in what they say the surface is. The three.js build
// hand-derived its lighting from the tonemap curve up, and spent sessions
// chasing materials that disagreed with each other for reasons that were
// really two lighting models.
#ifndef LOV_LIT_INCLUDED
#define LOV_LIT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// The view's own clock, set by the game (Shader.SetGlobalFloat), not _Time.
// A frozen capture fixes it, so wind and anything else animated are the same
// on every render of the same frame.
float _LovTime;

// Build URP's InputData for a surface point.
InputData LOV_InputData(float3 positionWS, float3 normalWS, float4 positionCS, half fogFactor)
{
    InputData d = (InputData)0;
    d.positionWS = positionWS;
    d.positionCS = positionCS;
    d.normalWS = NormalizeNormalPerPixel(normalWS);
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
    d.shadowCoord = TransformWorldToShadowCoord(positionWS);
    d.fogCoord = fogFactor;
    d.vertexLighting = half3(0, 0, 0);
    d.bakedGI = SampleSH(d.normalWS);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    d.shadowMask = half4(1, 1, 1, 1);
    return d;
}

// Shade a dielectric surface. Metallic surfaces call UniversalFragmentPBR directly.
half4 LOV_Shade(float3 positionWS, float3 normalWS, float4 positionCS, half fogFactor,
                half3 albedo, half smoothness, half occlusion, half3 emission)
{
    InputData d = LOV_InputData(positionWS, normalWS, positionCS, fogFactor);
    SurfaceData s = (SurfaceData)0;
    s.albedo = albedo;
    s.metallic = 0;
    s.specular = half3(0, 0, 0);
    s.smoothness = smoothness;
    s.normalTS = half3(0, 0, 1);
    s.occlusion = occlusion;
    s.emission = emission;
    s.alpha = 1;
    half4 c = UniversalFragmentPBR(d, s);
    c.rgb = MixFog(c.rgb, fogFactor);
    return c;
}

// What SSAO and anything else reading the normals buffer expects.
half4 LOV_DepthNormalsOutput(float3 normalWS)
{
#if defined(_GBUFFER_NORMALS_OCT)
    float3 n = normalize(normalWS);
    float2 oct = PackNormalOctQuadEncode(n);
    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
#else
    return half4(NormalizeNormalPerPixel(normalWS), 0.0);
#endif
}

// Shadow-caster clip position, with URP's normal and depth bias applied.
float3 _LightDirection;
float3 _LightPosition;

float4 LOV_ShadowPositionCS(float3 positionWS, float3 normalWS)
{
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    return ApplyShadowClamping(positionCS);
}

#endif
