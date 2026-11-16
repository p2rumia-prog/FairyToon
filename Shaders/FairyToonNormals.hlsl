// Normal blending follows lilToon (MIT, lilxyzw).
#ifndef FAIRYTOON_NORMALS_INCLUDED
#define FAIRYTOON_NORMALS_INCLUDED

// Flip the mapped normal only; origN remains geometric, as in lilToon.
float3 FairyToonFacingNormal(float3 normal, half facing)
{
    return facing < (_FlipNormal - 1.0) ? -normal : normal;
}

float3 FairyToonBlendNormal(float3 dstNormal, float3 srcNormal)
{
    return float3(dstNormal.xy + srcNormal.xy, dstNormal.z * srcNormal.z);
}

float FairyToonNormalZ(float2 xy)
{
    return sqrt(1.0 - saturate(dot(xy, xy)));
}

// lilToon reconstructs Z after scaling XY for BC5 / DXT5nm; URP does the reverse.
half3 FairyToonUnpackLayerNormal(half4 normalTex, half scale)
{
    half3 normal;
    #if defined(UNITY_NO_DXT5nm)
        normal = normalTex.rgb * 2.0 - 1.0;
        normal.xy *= scale;
    #else
        #if !defined(UNITY_ASTC_NORMALMAP_ENCODING)
            normalTex.a *= normalTex.r;
        #endif
        normal.xy = normalTex.ag * 2.0 - 1.0;
        normal.xy *= scale;
        normal.z = FairyToonNormalZ(normal.xy);
    #endif
    return normal;
}

half3 FairyToonNormalTS(FairyToonLayerContext layers, float2 uvMain)
{
    half3 normalTS = half3(0, 0, 1);
    #if defined(_NORMALMAP)
        half4 packed = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvMain * _BumpMap_ST.xy + _BumpMap_ST.zw);
        // Endfield's authored primary-only strength stays unchanged in Forward and DepthNormals.
        // DepthNormals has no _FT_ENDFIELD variant, so use the shared material selector here.
        #if defined(_FT_NORMAL2ND)
            bool legacyPrimary = _ShadingModel > 0.5 && _UseBump2ndMap < 0.5;
        #else
            bool legacyPrimary = _ShadingModel > 0.5;
        #endif
        normalTS = legacyPrimary ? UnpackNormalScale(packed, _BumpScale) : FairyToonUnpackLayerNormal(packed, _BumpScale);
    #endif
    #if defined(_FT_NORMAL2ND)
        if (_UseBump2ndMap > 0.5)
        {
            float2 uv = FairyToonLayerUV(layers, _Bump2ndMap_UVMode, false);
            // Opaque DepthNormals may not bind sampler_MainTex because MainTex is unused there.
            #if defined(FAIRYTOON_PASS_DEPTHNORMALS)
                #define FT_NORMAL_MASK_SAMPLER sampler_ft_linear_repeat
            #else
                #define FT_NORMAL_MASK_SAMPLER sampler_MainTex
            #endif
            half scale = _Bump2ndScale * SAMPLE_TEXTURE2D(_Bump2ndScaleMask, FT_NORMAL_MASK_SAMPLER,
                uvMain * _Bump2ndScaleMask_ST.xy + _Bump2ndScaleMask_ST.zw).r;
            half3 normal2 = FairyToonUnpackLayerNormal(SAMPLE_TEXTURE2D(_Bump2ndMap, sampler_ft_linear_repeat,
                uv * _Bump2ndMap_ST.xy + _Bump2ndMap_ST.zw), scale);
            #undef FT_NORMAL_MASK_SAMPLER
            normalTS = FairyToonBlendNormal(normalTS, normal2);
        }
    #endif
    return normalTS;
}

#endif
