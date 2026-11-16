// Anisotropy follows lilToon (MIT, lilxyzw); only used by the colour passes.
#ifndef FAIRYTOON_ANISOTROPY_INCLUDED
#define FAIRYTOON_ANISOTROPY_INCLUDED
#if defined(_FT_ANISOTROPY)

// lilToon's sharp (three-argument) toon threshold has a bounded AA denominator.
float FairyToonAnisoToon(float value, float derivative)
{
    return saturate((value - 0.5) / max(0.0001, saturate(derivative)));
}

// Neutral tangent maps and missing mesh tangents must not produce normalize(0).
float3 FairyToonAnisoOrtho(float3 tangent, float3 normal, float3 fallback)
{
    float3 t = tangent - normal * dot(normal, tangent);
    return dot(t, t) > 1e-10 ? normalize(t) : fallback;
}

void FairyToonSetupAnisotropy(inout FairyToonSurface s, float3 tangentWS, float handedness)
{
    float3 meshT = FairyToonAnisoOrtho(tangentWS, s.origN,
        SafeNormalize(cross(s.origN, abs(s.origN.y) < 0.999 ? float3(0,1,0) : float3(1,0,0))));
    float3 meshB = cross(s.origN, meshT) * handedness;
    half3 directionTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_AnisotropyTangentMap, sampler_MainTex,
        s.uvMain * _AnisotropyTangentMap_ST.xy + _AnisotropyTangentMap_ST.zw), 1.0);
    float3 directionWS = normalize(mul(directionTS, float3x3(meshT, meshB, s.origN)));
    s.anisoT = FairyToonAnisoOrtho(directionWS, s.N, meshT);
    s.anisoB = cross(s.N, s.anisoT);
    s.anisotropy = _UseAnisotropy > 0.5 ? _AnisotropyScale * SAMPLE_TEXTURE2D(_AnisotropyScaleMask, sampler_MainTex,
        s.uvMain * _AnisotropyScaleMask_ST.xy + _AnisotropyScaleMask_ST.zw).r : 0.0;
    float3 direction = s.anisotropy > 0.0 ? s.anisoB : s.anisoT;
    direction = FairyToonAnisoOrtho(s.V, direction, s.N);
    s.anisoN = SafeNormalize(lerp(s.N, direction, abs(s.anisotropy)));
}

bool FairyToonAnisotropyReflection()
{
    return _UseAnisotropy > 0.5 && _Anisotropy2Reflection > 0.5;
}

float FairyToonAnisoLobe(float3 T, float3 B, float3 N, float3 H, float rt, float rb, float shift)
{
    float3 shiftedT = normalize(T - N * shift);
    float3 shiftedB = normalize(B - N * shift);
    float r = rt * rb;
    float3 v = float3(dot(shiftedT, H) * rb, dot(shiftedB, H) * rt, saturate(dot(N, H)) * r);
    float w = r / max(dot(v, v), 1e-30);
    return r * w * w;
}

void FairyToonAnisotropicGGX(FairyToonSurface s, float3 N, float3 L, float3 H, float roughness,
    out float ggx, out float visibility)
{
    float rt = max(roughness * (1.0 + s.anisotropy), 0.002);
    float rb = max(roughness * (1.0 - s.anisotropy), 0.002);
    float nv = saturate(dot(N, s.V));
    float nl = saturate(dot(N, L));
    float lambdaV = nl * length(float3(rt * dot(s.anisoT, s.V), rb * dot(s.anisoB, s.V), nv));
    float lambdaL = nv * length(float3(rt * dot(s.anisoT, L), rb * dot(s.anisoB, L), nl));
    visibility = 0.5 / (lambdaV + lambdaL + 1e-5);
    float noise = SAMPLE_TEXTURE2D(_AnisotropyShiftNoiseMask, sampler_MainTex,
        s.uvMain * _AnisotropyShiftNoiseMask_ST.xy + _AnisotropyShiftNoiseMask_ST.zw).r - 0.5;
    ggx = FairyToonAnisoLobe(s.anisoT, s.anisoB, N, H,
        rt * _AnisotropyTangentWidth, rb * _AnisotropyBitangentWidth,
        noise * _AnisotropyShiftNoiseScale + _AnisotropyShift) * _AnisotropySpecularStrength;
    ggx += FairyToonAnisoLobe(s.anisoT, s.anisoB, N, H,
        rt * _Anisotropy2ndTangentWidth, rb * _Anisotropy2ndBitangentWidth,
        noise * _Anisotropy2ndShiftNoiseScale + _Anisotropy2ndShift) * _Anisotropy2ndSpecularStrength;
}
#endif
#endif
